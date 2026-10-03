using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Random = UnityEngine.Random;

namespace FogOfWar
{
    public static class Phys
    {
        public const double c = 299792458, G = 6.674e-11, Msun = 1.989e30, g = 9.80665, sigma = 5.670374e-8, hbar = 1.054571817e-34;
        public const double ly = 9.4607e15, yr = 3.15576e7;
    }

    public static class Fmt
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // 1234.5 → "1,234.5"; 3.3e36 → "3.34 ×10^36"
        public static string Num(double x, int digits = 3)
        {
            if (double.IsInfinity(x) || double.IsNaN(x)) return "∞";
            double a = Math.Abs(x);
            if (a != 0 && (a >= 1e6 || a < 1e-3))
            {
                int e = (int)Math.Floor(Math.Log10(a));
                double m = x / Math.Pow(10, e);
                return m.ToString("0.00", Inv) + " ×10^" + e.ToString(Inv);
            }
            string fmt = digits <= 0 ? "#,0" : "#,0." + new string('#', digits);
            return x.ToString(fmt, Inv);
        }

        public static string Years(double y)
        {
            if (y < 1.0 / 365) return Num(y * 365 * 24, 1) + " h";
            if (y < 1) return Num(y * 365, 1) + " days";
            if (y < 1e4) return Num(y, 2) + " yr";
            return Num(y, 0) + " yr";
        }

        // 0.999999123 → "0.999999123 c"
        public static string Beta(double b, double oneMinus)
        {
            if (b < 0.99) return b.ToString("0.0000", Inv) + " c";
            if (oneMinus < 1e-12) return "1 − " + Num(oneMinus) + " c";
            int nines = (int)Math.Floor(-Math.Log10(oneMinus));
            string s = b.ToString("0." + new string('0', Math.Min(nines + 3, 15)), Inv);
            return s + " c";
        }
    }

    // Smooth value noise for terrain generation (matches the feel of the shader fbm).
    public class ValueNoise
    {
        readonly int seed;
        public ValueNoise(int seed) { this.seed = seed; }
        float H(int x, int y, int z)
        {
            unchecked
            {
                int n = x * 374761393 + y * 668265263 + z * 2147483647 + seed * 144269504;
                n = (n ^ (int)((uint)n >> 13)) * 1274126177;
                return (uint)(n ^ (int)((uint)n >> 16)) / 4294967295f;
            }
        }
        static float S(float t) => t * t * (3 - 2 * t);
        float Noise(float x, float y, float z)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y), zi = Mathf.FloorToInt(z);
            float xf = S(x - xi), yf = S(y - yi), zf = S(z - zi);
            float a = Mathf.Lerp(Mathf.Lerp(H(xi, yi, zi), H(xi + 1, yi, zi), xf), Mathf.Lerp(H(xi, yi + 1, zi), H(xi + 1, yi + 1, zi), xf), yf);
            float b = Mathf.Lerp(Mathf.Lerp(H(xi, yi, zi + 1), H(xi + 1, yi, zi + 1), xf), Mathf.Lerp(H(xi, yi + 1, zi + 1), H(xi + 1, yi + 1, zi + 1), xf), yf);
            return Mathf.Lerp(a, b, zf);
        }
        public float Fbm(Vector3 p, int oct = 5)
        {
            float a = 0.5f, f = 1, sum = 0;
            for (int i = 0; i < oct; i++) { sum += a * Noise(p.x * f, p.y * f, p.z * f); f *= 2.03f; a *= 0.5f; }
            return sum;
        }
    }

    public static class Gfx
    {
        static readonly Dictionary<string, Shader> cache = new Dictionary<string, Shader>();

        // Shaders live in Resources/Shaders so they ship in builds without being referenced by a scene.
        public static Material Mat(string shader)
        {
            if (!cache.TryGetValue(shader, out var s) || s == null)
            {
                s = Resources.Load<Shader>("Shaders/" + shader);
                if (s == null) { Debug.LogError("Fog of War: missing shader Resources/Shaders/" + shader); s = Shader.Find("Hidden/InternalErrorShader"); }
                cache[shader] = s;
            }
            return new Material(s);
        }

        public static Material Unlit(Color c, bool additive = false, bool zwrite = false)
        {
            var m = Mat("Unlit");
            m.SetColor("_Color", c);
            m.SetFloat("_SrcBlend", additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", zwrite ? 1 : 0);
            return m;
        }

        public static Material Lit(Color c, Color emission = default, float spec = 0.4f)
        {
            var m = Mat("SimpleLit");
            m.SetColor("_Color", c);
            m.SetColor("_Emission", emission);
            m.SetFloat("_Spec", spec);
            return m;
        }

        public static GameObject Obj(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        // A quad whose shader writes clip-space directly; parented to the camera so it is never culled.
        public static GameObject Fullscreen(string name, Transform parent, Camera cam, Material mat)
        {
            var mesh = new Mesh { name = "FullscreenQuad" };
            mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            var go = Obj(name, parent, mesh, mat);
            go.AddComponent<FollowCamera>().cam = cam;
            return go;
        }

        public static void SetRayCamera(Material m, Camera cam)
        {
            var t = cam.transform;
            m.SetVector("_CamPos", t.position);
            m.SetVector("_CamRight", t.right);
            m.SetVector("_CamUp", t.up);
            m.SetVector("_CamFwd", t.forward);
            m.SetFloat("_TanFov", Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad / 2));
            m.SetFloat("_Aspect", cam.aspect);
            m.SetFloat("_ScreenH", cam.pixelHeight);
        }

        public static Mesh UVSphere(int seg = 96, int rings = 48, float r = 1)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            for (int y = 0; y <= rings; y++)
            {
                float v = (float)y / rings, th = v * Mathf.PI;
                for (int x = 0; x <= seg; x++)
                {
                    float u = (float)x / seg, ph = u * Mathf.PI * 2;
                    var n = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    verts.Add(n * r); norms.Add(n); uvs.Add(new Vector2(u, 1 - v));
                }
            }
            var tris = new List<int>();
            for (int y = 0; y < rings; y++)
                for (int x = 0; x < seg; x++)
                {
                    int a = y * (seg + 1) + x, b = a + seg + 1;
                    tris.Add(a); tris.Add(a + 1); tris.Add(b);
                    tris.Add(a + 1); tris.Add(b + 1); tris.Add(b);
                }
            var m = new Mesh { name = "UVSphere" };
            if (verts.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        // Subdivided icosahedron with shared vertices (good for displacement + smooth normals).
        public static Mesh IcoSphere(int subdivisions)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            var f = new List<int>
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
            };
            for (int s = 0; s < subdivisions; s++)
            {
                var mid = new Dictionary<long, int>();
                var nf = new List<int>(f.Count * 4);
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (mid.TryGetValue(key, out int idx)) return idx;
                    v.Add(((v[a] + v[b]) * 0.5f).normalized);
                    mid[key] = v.Count - 1;
                    return v.Count - 1;
                }
                for (int i = 0; i < f.Count; i += 3)
                {
                    int a = f[i], b = f[i + 1], c = f[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    nf.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                f = nf;
            }
            // This face list already winds front faces outward under Unity's convention (cross(b−a, c−a) faces the viewer).
            var m = new Mesh { name = "IcoSphere", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(v);
            m.SetTriangles(f, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // Billboard-star mesh for FogOfWar/Stars. dirs on the unit sphere; temps in K; mags are brightness.
        public static Mesh StarMesh(IList<Vector3> dirs, IList<float> temps, IList<float> mags)
        {
            int n = dirs.Count;
            var verts = new Vector3[n * 4];
            var norms = new Vector3[n * 4];
            var uv = new Vector2[n * 4];
            var uv2 = new Vector2[n * 4];
            var idx = new int[n * 6];
            var corners = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < 4; k++)
                {
                    verts[i * 4 + k] = dirs[i];
                    norms[i * 4 + k] = dirs[i];
                    uv[i * 4 + k] = corners[k];
                    uv2[i * 4 + k] = new Vector2(temps[i], mags[i]);
                }
                int b = i * 4;
                idx[i * 6] = b; idx[i * 6 + 1] = b + 2; idx[i * 6 + 2] = b + 1;
                idx[i * 6 + 3] = b; idx[i * 6 + 4] = b + 3; idx[i * 6 + 5] = b + 2;
            }
            var m = new Mesh { name = "Stars" };
            if (verts.Length > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = verts; m.normals = norms; m.uv = uv; m.uv2 = uv2; m.triangles = idx;
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 1e7f);
            return m;
        }

        // Background stars for the mesh-based scenes: the real sky from the Bright Star Catalogue when the art pack is
        // present (one shared mesh and material for every scene), `count` random stars otherwise.
        static Mesh catalogueMesh;
        static Material catalogueMat;

        public static GameObject Starfield(Transform parent, int count = 5000, float gain = 1f)
        {
            var cat = Art.Catalogue();
            if (cat != null)
            {
                if (catalogueMesh == null) catalogueMesh = StarMesh(cat.dirs, cat.temps, cat.mags);
                if (catalogueMat == null) catalogueMat = StarMat(1f);
                return Obj("Starfield", parent, catalogueMesh, catalogueMat);
            }
            var dirs = new List<Vector3>(); var temps = new List<float>(); var mags = new List<float>();
            for (int i = 0; i < count; i++)
            {
                dirs.Add(Random.onUnitSphere);
                float r = Random.value;
                temps.Add(r < 0.7f ? 3200 + Random.value * 2500 : r < 0.93f ? 5500 + Random.value * 3000 : 9000 + Random.value * 14000);
                mags.Add(0.12f + Mathf.Pow(Random.value, 6) * 2f);
            }
            return Obj("Starfield", parent, StarMesh(dirs, temps, mags), StarMat(gain));
        }

        static Material StarMat(float gain)
        {
            var mat = Mat("Stars");
            mat.SetFloat("_Beta", 0); mat.SetFloat("_Gamma", 1); mat.SetFloat("_SizePx", 1); mat.SetFloat("_Gain", gain);
            mat.SetVector("_Fwd", Vector3.forward);
            return mat;
        }

        // Dynamic line list mesh with vertex colours.
        public static Mesh LineMesh(string name)
        {
            var m = new Mesh { name = name };
            m.MarkDynamic();
            return m;
        }

        public static void SetLines(Mesh m, List<Vector3> pts, List<Color> cols, MeshTopology topo = MeshTopology.Lines)
        {
            m.Clear();
            if (pts.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(pts);
            m.SetColors(cols);
            var idx = new int[pts.Count];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            m.SetIndices(idx, topo, 0, false);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
        }

        public static Texture2D SoftDot(int size = 64)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2 - 1, dy = (y + 0.5f) / size * 2 - 1;
                    float a = Mathf.Exp(-(dx * dx + dy * dy) * 5f);
                    px[y * size + x] = new Color(1, 1, 1, a);
                }
            tex.SetPixels32(px); tex.Apply();
            return tex;
        }

        public static void Destroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }
    }

    // Keeps a fullscreen quad in front of the camera so frustum culling never drops it.
    public class FollowCamera : MonoBehaviour
    {
        public Camera cam;
        void LateUpdate()
        {
            if (cam == null) return;
            transform.position = cam.transform.position + cam.transform.forward * (cam.nearClipPlane + 0.5f);
            transform.rotation = cam.transform.rotation;
        }
    }
}
