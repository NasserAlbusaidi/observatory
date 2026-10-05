using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace FogOfWar.OpenWorld
{
    // Cube-sphere geometry for every round body, and the streaming quadtree that refines it near the camera.
    //
    // Body frame (Unity local axes of the body holder): +X prime meridian, +Y north pole, +Z east (90° E). A cube face
    // point (s, t) ∈ [-1, 1]² maps to the direction normalize(N + U tan(s π/4) + V tan(t π/4)), which spaces the
    // vertices within ~1.4× of uniform. Heights come from the body's TerrainData (a real DEM) where it has one.
    //
    // Far away, the body is one mesh on its holder (WholeMesh). Once it covers enough of the screen the quadtree takes
    // over: chunks of 64×64 quads split where a quad would cover too many pixels (or, on a smooth sphere, where the
    // facets would show) and merge back when the camera leaves. Chunk vertices are relative to the chunk centre and
    // the centre is placed relative to the camera in double precision, so the surface holds at metre scale.
    // Chunk meshes are built on worker threads; skirts hide the cracks between chunks of different levels.
    public class TerrainBody
    {
        public const int N = 64;                 // quads per chunk edge: 64 rather than 32 draws a quarter as many chunks,
                                                 // at 1 km over Valles that took 1,170 chunks to 366 and 76 to 83 fps
        const int WholeN = 32;                   // quads per face edge of the whole-body mesh
        const float SplitPx = 10f;               // split a DEM chunk while a quad covers more than this many pixels
        const float SagittaPx = 0.6f;            // ... or while a smooth facet bulges more than this
        const int MaxLevel = 22;
        const float EnterPx = 260f, LeavePx = 200f; // quadtree on when the body's radius covers this many pixels

        public readonly Body B;
        public readonly TerrainData Data;
        public readonly bool Flatten;            // apply the body's flattening (ellipsoid-like datum, or no DEM)
        readonly double R;                       // reference radius, km
        readonly Vector3 shape;                  // tri-axial factors incl. flattening, body axes
        readonly Vector3 holderShape;            // what the scene scales the holder by (always flattened)
        public bool Active { get; private set; }
        public int ChunksDrawn { get; private set; }
        public int ChunksLive => live;
        public int MaxLevelDrawn { get; private set; }
        // QA: how far round from the point under the camera the nearest drawn chunk starts (0 when one covers it, km),
        // its level and the top of its height range (km from the centre)
        public double NadirGapKm { get; private set; }
        public int NadirLevel { get; private set; }
        public double NadirTopKm { get; private set; }

        static readonly Vector3[] FaceN = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        static readonly Vector3[] FaceU = { Vector3.forward, Vector3.back, Vector3.right, Vector3.right, Vector3.left, Vector3.right };
        static readonly Vector3[] FaceV = { Vector3.up, Vector3.up, Vector3.forward, Vector3.back, Vector3.up, Vector3.up };

        class Node
        {
            public int face, level;
            public double s0, t0, size;            // face square [s0, s0+size] × [t0, t0+size]
            public double cx, cy, cz;              // centre on the surface, body frame km
            public double dirX, dirY, dirZ;        // unit direction of the centre
            public double angRad;                  // angular radius (rad) of the square seen from the body centre
            public double quadKm;                  // ground length of one quad edge
            public double hLo, hHi;                // height range, km
            public Node[] kids;
            public Node parent;
            public Mesh mesh;
            public bool queued, freed;
            public int lastWanted;                 // frame this node was last split
        }

        readonly Node[] roots = new Node[6];
        int live;
        int frame;
        Material mat;
        MaterialPropertyBlock mpb;
        readonly double mapTexelKm;
        Texture[] streamTex;

        public TerrainBody(Body b, TerrainData data)
        {
            B = b; Data = data;
            Flatten = data == null || data.Datum != "sphere";
            R = data != null ? data.RefRadiusKm : b.Radius;
            shape = Vector3.Scale(b.Shape, new Vector3(1, Flatten ? (float)(1 - b.Flattening) : 1, 1));
            holderShape = Vector3.Scale(b.Shape, new Vector3(1, (float)(1 - b.Flattening), 1));
            mapTexelKm = 2 * Math.PI * b.Radius / 8192;
        }

        // ---------------------------------------------------------------- whole-body mesh (holder, far away)

        static Mesh unitSphere;

        // The shared smooth cube-sphere (radius 1) for bodies without a DEM: 32 quads per face edge.
        public static Mesh UnitSphere() => unitSphere != null ? unitSphere : (unitSphere = BuildWhole(null));

        // This body's whole mesh in holder units (radius ~1; the holder scales by Radius × shape): with relief if it
        // has a DEM, otherwise the shared smooth one.
        public Mesh WholeMesh() => Data == null ? UnitSphere() : BuildWhole(this);

        static Mesh BuildWhole(TerrainBody tb)
        {
            int n = WholeN, vpf = (n + 1) * (n + 1);
            var verts = new Vector3[vpf * 6];
            var norms = new Vector3[vpf * 6];
            var uvs = new Vector2[vpf * 6];
            var tris = new int[6 * n * n * 6];
            int ti = 0;
            int lvl = tb?.Data?.LevelFor(Math.PI / 2 * tb.R / n) ?? 0;
            for (int f = 0; f < 6; f++)
            {
                var g = new double[(n + 3) * (n + 3) * 3];
                FillGrid(tb, f, -1, -1, 2, n, lvl, g, out _);
                for (int j = 0; j <= n; j++)
                    for (int i = 0; i <= n; i++)
                    {
                        int v = f * vpf + j * (n + 1) + i, gi = ((j + 1) * (n + 3) + i + 1) * 3;
                        var p = new Vector3((float)g[gi], (float)g[gi + 1], (float)g[gi + 2]);
                        if (tb != null) p = new Vector3(p.x / (float)(tb.B.Radius * tb.holderShape.x), p.y / (float)(tb.B.Radius * tb.holderShape.y), p.z / (float)(tb.B.Radius * tb.holderShape.z));
                        verts[v] = p;
                        norms[v] = GridNormal(g, n + 3, i + 1, j + 1);
                        var d = p.normalized;
                        uvs[v] = new Vector2(0.5f + Mathf.Atan2(d.z, d.x) / (2 * Mathf.PI), 0.5f + Mathf.Asin(Mathf.Clamp(d.y, -1, 1)) / Mathf.PI);
                    }
                bool flip = Winding(f);
                for (int j = 0; j < n; j++)
                    for (int i = 0; i < n; i++)
                    {
                        int a = f * vpf + j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                        Tri(tris, ref ti, a, c, b, flip); Tri(tris, ref ti, b, c, d, flip);
                    }
            }
            var m = new Mesh { name = tb == null ? "CubeSphere" : "CubeSphere " + tb.B.Name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.vertices = verts; m.normals = norms; m.uv = uvs; m.triangles = tris;
            m.RecalculateBounds();
            m.RecalculateUVDistributionMetrics(); // mip streaming sizes the planet maps from this
            return m;
        }

        // ---------------------------------------------------------------- grid maths (any thread)

        static void FaceDir(int f, double s, double t, out double x, out double y, out double z)
        {
            double a = Math.Tan(s * Math.PI / 4), b = Math.Tan(t * Math.PI / 4);
            Vector3 N0 = FaceN[f], U = FaceU[f], V = FaceV[f];
            x = N0.x + U.x * a + V.x * b; y = N0.y + U.y * a + V.y * b; z = N0.z + U.z * a + V.z * b;
            double m = Math.Sqrt(x * x + y * y + z * z);
            x /= m; y /= m; z /= m;
        }

        // Surface positions (body frame km) on an (n+3)² grid: the chunk's (n+1)² vertices plus a one-sample border for
        // normals. tb == null: the unit sphere.
        static void FillGrid(TerrainBody tb, int f, double s0, double t0, double size, int n, int lvl, double[] g, out double hMin)
        {
            short[] tile = null; long key = -1;
            hMin = double.MaxValue;
            for (int j = -1; j <= n + 1; j++)
                for (int i = -1; i <= n + 1; i++)
                {
                    FaceDir(f, s0 + size * i / n, t0 + size * j / n, out double x, out double y, out double z);
                    double r = 1;
                    if (tb != null)
                    {
                        double h = tb.Data != null ? tb.Data.HeightKm(lvl, x, y, z, ref tile, ref key) : 0;
                        if (h < hMin) hMin = h;
                        r = tb.R + h;
                        x *= tb.shape.x; y *= tb.shape.y; z *= tb.shape.z;
                    }
                    int gi = ((j + 1) * (n + 3) + i + 1) * 3;
                    g[gi] = x * r; g[gi + 1] = y * r; g[gi + 2] = z * r;
                }
        }

        static Vector3 GridNormal(double[] g, int w, int i, int j)
        {
            int l = (j * w + i - 1) * 3, r = (j * w + i + 1) * 3, d = ((j - 1) * w + i) * 3, u = ((j + 1) * w + i) * 3, c = (j * w + i) * 3;
            double ax = g[r] - g[l], ay = g[r + 1] - g[l + 1], az = g[r + 2] - g[l + 2];
            double bx = g[u] - g[d], by = g[u + 1] - g[d + 1], bz = g[u + 2] - g[d + 2];
            double nx = ay * bz - az * by, ny = az * bx - ax * bz, nz = ax * by - ay * bx;
            if (nx * g[c] + ny * g[c + 1] + nz * g[c + 2] < 0) { nx = -nx; ny = -ny; nz = -nz; }
            double m = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            return m > 0 ? new Vector3((float)(nx / m), (float)(ny / m), (float)(nz / m)) : new Vector3((float)g[c], (float)g[c + 1], (float)g[c + 2]).normalized;
        }

        // Unity's front faces: cross(b − a, c − a) towards the viewer. True when (a, c, b) on this face needs flipping.
        static bool Winding(int f)
        {
            Vector3 a = FaceN[f], b = a + FaceU[f], c = a + FaceV[f];
            return Vector3.Dot(Vector3.Cross(c - a, b - a), FaceN[f]) < 0;
        }

        static void Tri(int[] t, ref int i, int a, int b, int c, bool flip)
        {
            t[i++] = a; t[i++] = flip ? c : b; t[i++] = flip ? b : c;
        }

        // ---------------------------------------------------------------- chunk meshes (worker threads)

        class Built { public Node node; public Vector3[] v, n; public int[] t; public TerrainBody tb; }
        static readonly ConcurrentQueue<Built> done = new ConcurrentQueue<Built>();
        static readonly ConcurrentQueue<(TerrainBody, Node)> todo = new ConcurrentQueue<(TerrainBody, Node)>();
        static readonly SemaphoreSlim todoCount = new SemaphoreSlim(0);
        static Thread[] workers;
        public static int Pending => todo.Count;

        static void StartWorkers()
        {
            if (workers != null) return;
            workers = new Thread[Mathf.Clamp(SystemInfo.processorCount - 2, 2, 6)];
            for (int i = 0; i < workers.Length; i++)
            {
                workers[i] = new Thread(Work) { IsBackground = true, Name = "Terrain " + i, Priority = System.Threading.ThreadPriority.BelowNormal };
                workers[i].Start();
            }
        }

        static void Work()
        {
            while (true)
            {
                todoCount.Wait();
                if (!todo.TryDequeue(out var job)) continue;
                var (tb, node) = job;
                if (node.freed) continue;
                try { done.Enqueue(tb.BuildChunk(node)); }
                catch (Exception e) { Debug.LogError($"Fog of War: terrain chunk {tb.B.Name} L{node.level}: {e}"); }
            }
        }

        Built BuildChunk(Node nd)
        {
            int n = N, w = n + 3;
            var g = new double[w * w * 3];
            int lvl = Data?.LevelFor(nd.quadKm) ?? 0;
            FillGrid(this, nd.face, nd.s0, nd.t0, nd.size, n, lvl, g, out _);
            int vg = (n + 1) * (n + 1), skirtN = 4 * (n + 1);
            var v = new Vector3[vg + skirtN];
            var nn = new Vector3[vg + skirtN];
            for (int j = 0; j <= n; j++)
                for (int i = 0; i <= n; i++)
                {
                    int gi = ((j + 1) * w + i + 1) * 3, k = j * (n + 1) + i;
                    v[k] = new Vector3((float)(g[gi] - nd.cx), (float)(g[gi + 1] - nd.cy), (float)(g[gi + 2] - nd.cz));
                    nn[k] = GridNormal(g, w, i + 1, j + 1);
                }
            // skirts: each edge vertex again, dropped toward the centre by more than the neighbour's error can open
            // deep enough for a much coarser neighbour: at 1 km in Valles Marineris, skirts of 0.6 quad + 5 % of the
            // relief left a seam band and a hole under the camera (sky showing through); they hang under the ground
            float drop = (float)(nd.quadKm * 2 + (nd.hHi - nd.hLo) * 0.3 + 0.001);
            var dir = new Vector3((float)nd.dirX, (float)nd.dirY, (float)nd.dirZ);
            int[] edge = new int[skirtN];
            int e = 0;
            for (int i = 0; i <= n; i++) edge[e++] = i;                          // t = 0
            for (int i = 0; i <= n; i++) edge[e++] = n * (n + 1) + i;            // t = 1
            for (int j = 0; j <= n; j++) edge[e++] = j * (n + 1);                // s = 0
            for (int j = 0; j <= n; j++) edge[e++] = j * (n + 1) + n;            // s = 1
            for (int q = 0; q < skirtN; q++)
            {
                var c = v[edge[q]] + new Vector3((float)nd.cx, (float)nd.cy, (float)nd.cz);
                v[vg + q] = v[edge[q]] - c.normalized * drop;
                nn[vg + q] = nn[edge[q]];
            }
            bool flip = Winding(nd.face);
            var t = new int[n * n * 6 + 4 * n * 12];
            int ti = 0;
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    Tri(t, ref ti, a, c, b, flip); Tri(t, ref ti, b, c, d, flip);
                }
            // skirt quads both ways round (cheap, and no winding to get wrong per edge)
            for (int s = 0; s < 4; s++)
                for (int q = 0; q < n; q++)
                {
                    int a = edge[s * (n + 1) + q], b = edge[s * (n + 1) + q + 1], c = vg + s * (n + 1) + q, d = c + 1;
                    Tri(t, ref ti, a, b, c, false); Tri(t, ref ti, b, d, c, false);
                    Tri(t, ref ti, a, c, b, false); Tri(t, ref ti, b, c, d, false);
                }
            return new Built { node = nd, v = v, n = nn, t = t, tb = this };
        }

        // Main thread, once per frame: turn finished worker output into meshes.
        public static void Collect(int budget = 48)
        {
            while (budget-- > 0 && done.TryDequeue(out var r))
            {
                var nd = r.node;
                nd.queued = false;
                if (nd.freed) continue;
                var m = new Mesh { name = "Terrain" };
                m.vertices = r.v; m.normals = r.n; m.triangles = r.t;
                m.RecalculateBounds();
                nd.mesh = m;
                r.tb.live++;
            }
        }

        void Request(Node nd)
        {
            if (nd.mesh != null || nd.queued) return;
            nd.queued = true;
            StartWorkers();
            todo.Enqueue((this, nd));
            todoCount.Release();
        }

        // ---------------------------------------------------------------- quadtree

        Node MakeNode(int face, int level, double s0, double t0, double size, Node parent)
        {
            var nd = new Node { face = face, level = level, s0 = s0, t0 = t0, size = size, parent = parent };
            FaceDir(face, s0 + size / 2, t0 + size / 2, out nd.dirX, out nd.dirY, out nd.dirZ);
            FaceDir(face, s0, t0, out double ax, out double ay, out double az);
            FaceDir(face, s0 + size, t0 + size, out double bx, out double by, out double bz);
            double cosA = ax * nd.dirX + ay * nd.dirY + az * nd.dirZ, cosB = bx * nd.dirX + by * nd.dirY + bz * nd.dirZ;
            nd.angRad = Math.Acos(Math.Min(1, Math.Min(cosA, cosB)));
            nd.quadKm = 2 * nd.angRad * R / Math.Sqrt(2) / N;
            double h = 0;
            nd.hLo = nd.hHi = 0;
            if (Data != null)
            {
                int lvl = Data.LevelFor(nd.quadKm);
                h = Data.HeightKm(lvl, nd.dirX, nd.dirY, nd.dirZ);
                double lat = Math.Asin(nd.dirY), lon = Math.Atan2(nd.dirZ, nd.dirX);
                double cl = Math.Max(Math.Cos(Math.Min(Math.PI / 2, Math.Abs(lat) + nd.angRad)), 1e-3);
                if (Math.Abs(lat) + nd.angRad >= Math.PI / 2) Data.RangeKm(lvl, lat - nd.angRad, lat + nd.angRad, -Math.PI, Math.PI, out nd.hLo, out nd.hHi);
                else Data.RangeKm(lvl, lat - nd.angRad, lat + nd.angRad, lon - nd.angRad / cl, lon + nd.angRad / cl, out nd.hLo, out nd.hHi);
            }
            double r = R + h;
            nd.cx = nd.dirX * shape.x * r; nd.cy = nd.dirY * shape.y * r; nd.cz = nd.dirZ * shape.z * r;
            return nd;
        }

        void EnsureRoots()
        {
            if (roots[0] != null) return;
            for (int f = 0; f < 6; f++) roots[f] = MakeNode(f, 0, -1, -1, 2, null);
        }

        // Per frame. bodyW / camW heliocentric km; prime, pole, east the body axes; k the scene's distance compression
        // at the body (1 inside the linear zone); bodyRender / rot the holder's render position and rotation.
        // Returns true while the quadtree draws the body (the caller hides the holder's own mesh).
        public bool Draw(Material m, D3 camW, D3 bodyW, D3 prime, D3 pole, D3 east, double k, Vector3 bodyRender, Quaternion rot,
                         Matrix4x4 rootToWorld, Matrix4x4 worldToBody, int layer, float pxPerRad, Plane[] frustum)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try { return DrawInner(m, camW, bodyW, prime, pole, east, k, bodyRender, rot, rootToWorld, worldToBody, layer, pxPerRad, frustum); }
            finally
            {
                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                if (Active) DrawMs = DrawMs * 0.95 + ms * 0.05;
            }
        }

        // Main-thread cost of Draw while the quadtree is on, ms per frame (smoothed).
        public double DrawMs { get; private set; }

        // QA: draw every body as its whole mesh, to measure the frame rate without the quadtree.
        public static bool Off;

        bool DrawInner(Material m, D3 camW, D3 bodyW, D3 prime, D3 pole, D3 east, double k, Vector3 bodyRender, Quaternion rot,
                       Matrix4x4 rootToWorld, Matrix4x4 worldToBody, int layer, float pxPerRad, Plane[] frustum)
        {
            if (Off) { ChunksDrawn = 0; return false; }
            frame++;
            var rel = camW - bodyW;
            double dist = rel.Mag;
            double px = B.Radius / Math.Max(dist - B.Radius, 1e-3) * pxPerRad;
            bool want = Active ? px > LeavePx : px > EnterPx;
            if (want != Active) { Active = want; SetStreaming(m, want); if (!want) FreeAll(); }
            ChunksDrawn = 0; MaxLevelDrawn = 0; NadirGapKm = double.MaxValue;
            if (!Active) return false;
            EnsureRoots();
            mat = m;
            mpb ??= new MaterialPropertyBlock();
            mpb.SetMatrix("_WorldToBody", worldToBody);
            mpb.SetFloat("_Terrain", 1);
            // camera in the body frame (Unity local axes: x prime, y pole, z east)
            double lx = D3.Dot(rel, prime), ly = D3.Dot(rel, pole), lz = D3.Dot(rel, east);
            var ctx = new Ctx
            {
                lx = lx, ly = ly, lz = lz, d = dist, k = k, camW = camW, bodyW = bodyW, prime = prime, pole = pole, east = east,
                bodyRender = bodyRender, rot = rot, root = rootToWorld, layer = layer, pxPerRad = pxPerRad, frustum = frustum,
                rootScale = Mathf.Max(rootToWorld.GetColumn(0).magnitude, rootToWorld.GetColumn(1).magnitude, rootToWorld.GetColumn(2).magnitude),
            };
            // horizon: anything further round than the horizon of the lowest ground, seen over the highest, is hidden
            double rLo = R - 12, hz = dist > rLo ? Math.Acos(Math.Min(1, rLo / dist)) : Math.PI;
            ctx.horizon = hz;
            // the holder keeps drawing until all six faces have their first mesh
            bool rootsReady = true;
            foreach (var r in roots) if (r.mesh == null) { rootsReady = false; Request(r); }
            if (!rootsReady) return false;
            for (int f = 0; f < 6; f++) Visit(roots[f], ref ctx);
            foreach (var r in roots) Prune(r);
            return true;
        }

        struct Ctx
        {
            public double lx, ly, lz, d, k, horizon;
            public D3 camW, bodyW, prime, pole, east;
            public Vector3 bodyRender; public Quaternion rot; public Matrix4x4 root; public int layer; public float pxPerRad;
            public Plane[] frustum; public float rootScale;
        }

        // Node centre in render space (before the scene root): camera-relative in double inside the linear zone.
        static Vector3 RenderPos(Node nd, ref Ctx c)
        {
            if (c.k >= 1) return Frame.U(c.bodyW + c.prime * nd.cx + c.pole * nd.cy + c.east * nd.cz - c.camW);
            return c.bodyRender + c.rot * new Vector3((float)(nd.cx * c.k), (float)(nd.cy * c.k), (float)(nd.cz * c.k));
        }

        // False when the node's bounding sphere is wholly outside the view frustum: it is neither drawn nor split.
        bool InView(Node nd, ref Ctx c)
        {
            if (c.frustum == null) return true;
            var centre = c.root.MultiplyPoint3x4(RenderPos(nd, ref c));
            float rad = (float)((nd.angRad * R * 1.05 + (nd.hHi - nd.hLo) + 0.01) * c.k) * c.rootScale;
            foreach (var p in c.frustum) if (p.GetDistanceToPoint(centre) < -rad) return false;
            return true;
        }

        void Visit(Node nd, ref Ctx c)
        {
            // horizon cull (the camera's direction against the node's, widened by the node and the hills' horizon)
            double cd = (c.lx * nd.dirX + c.ly * nd.dirY + c.lz * nd.dirZ) / Math.Max(c.d, 1e-9);
            double over = R + Math.Max(nd.hHi, 0) > R - 12 ? Math.Acos(Math.Min(1, (R - 12) / (R + Math.Max(nd.hHi, 0) + 0.001))) : 0;
            if (Math.Acos(Math.Max(-1, Math.Min(1, cd))) - nd.angRad > c.horizon + over + 1e-4) return;
            if (!InView(nd, ref c)) return;

            double dx = c.lx - nd.cx, dy = c.ly - nd.cy, dz = c.lz - nd.cz;
            double near = Math.Max(Math.Sqrt(dx * dx + dy * dy + dz * dz) - nd.angRad * R * 1.05 - (nd.hHi - nd.hLo), 1e-4);
            double quadPx = nd.quadKm / near * c.pxPerRad;
            double sagPx = R * (nd.quadKm / R) * (nd.quadKm / R) / 8 / near * c.pxPerRad;
            bool split = nd.level < MaxLevel && (sagPx > SagittaPx || (Data != null && quadPx > SplitPx && nd.quadKm > Data.NativeKm * 0.75));
            if (split)
            {
                nd.lastWanted = frame;
                if (nd.kids == null)
                {
                    double h = nd.size / 2;
                    nd.kids = new[]
                    {
                        MakeNode(nd.face, nd.level + 1, nd.s0, nd.t0, h, nd), MakeNode(nd.face, nd.level + 1, nd.s0 + h, nd.t0, h, nd),
                        MakeNode(nd.face, nd.level + 1, nd.s0, nd.t0 + h, h, nd), MakeNode(nd.face, nd.level + 1, nd.s0 + h, nd.t0 + h, h, nd),
                    };
                }
                bool ready = true;
                foreach (var kd in nd.kids) { if (kd.mesh == null) { ready = false; Request(kd); } }
                if (ready) { foreach (var kd in nd.kids) Visit(kd, ref c); return; }
            }
            if (nd.mesh == null) { Request(nd); return; }
            DrawNode(nd, ref c);
        }

        void DrawNode(Node nd, ref Ctx c)
        {
            var pos = RenderPos(nd, ref c);
            var M = c.root * Matrix4x4.TRS(pos, c.rot, Vector3.one * (float)c.k);
            // the 8K relief map carries the slopes until the mesh is finer than its texels, then the mesh does
            mpb.SetFloat("_MapNormalW", Mathf.Clamp01((float)((nd.quadKm - mapTexelKm) / mapTexelKm)));
            Graphics.DrawMesh(nd.mesh, M, mat, c.layer, null, 0, mpb, UnityEngine.Rendering.ShadowCastingMode.Off, false);
            ChunksDrawn++;
            if (nd.level > MaxLevelDrawn) MaxLevelDrawn = nd.level;
            // QA: the drawn chunk nearest the point under the camera, to tell a hole from a chunk that is drawn
            double nadir = Math.Acos(Math.Max(-1, Math.Min(1, (c.lx * nd.dirX + c.ly * nd.dirY + c.lz * nd.dirZ) / Math.Max(c.d, 1e-9)))) - nd.angRad;
            if (nadir < NadirGapKm / R) { NadirGapKm = Math.Max(nadir, 0) * R; NadirLevel = nd.level; NadirTopKm = R + nd.hHi; }
        }

        // Drop subtrees that haven't been split for a while.
        void Prune(Node nd)
        {
            if (nd.kids == null) return;
            if (frame - nd.lastWanted > 90) { foreach (var kd in nd.kids) Free(kd); nd.kids = null; return; }
            foreach (var kd in nd.kids) Prune(kd);
        }

        void Free(Node nd)
        {
            nd.freed = true;
            if (nd.mesh != null) { UnityEngine.Object.Destroy(nd.mesh); nd.mesh = null; live--; }
            if (nd.kids != null) foreach (var kd in nd.kids) Free(kd);
            nd.kids = null;
        }

        void FreeAll()
        {
            for (int f = 0; f < 6; f++) if (roots[f] != null) { Free(roots[f]); roots[f] = null; }
        }

        public void Dispose()
        {
            FreeAll();
            Data?.Close();
        }

        // Full-resolution maps while the camera is close (chunks drawn with Graphics.DrawMesh don't feed mip streaming).
        void SetStreaming(Material m, bool on)
        {
            streamTex ??= new[] { m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null, m.HasProperty("_NormalTex") ? m.GetTexture("_NormalTex") : null };
            foreach (var t in streamTex)
                if (t is Texture2D t2 && t2.streamingMipmaps)
                {
                    if (on) t2.requestedMipmapLevel = 0; else t2.ClearRequestedMipmapLevel();
                }
        }

        // Height of the ground (km above RefRadius... i.e. radius km of the surface) under a body-frame direction, from
        // the finest level whose tiles load quickly. Main thread or any.
        public double SurfaceRadiusKm(double x, double y, double z, double spacingKm = 0)
        {
            double m = Math.Sqrt(x * x + y * y + z * z);
            x /= m; y /= m; z /= m;
            double h = Data != null ? Data.HeightKm(Data.LevelFor(Math.Max(spacingKm, Data.NativeKm)), x, y, z) : 0;
            double sx = x * shape.x, sy = y * shape.y, sz = z * shape.z;
            return (R + h) * Math.Sqrt(sx * sx + sy * sy + sz * sz);
        }
    }
}
