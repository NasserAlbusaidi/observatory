using System;
using System.Collections.Generic;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // The solar system at true scale and today's date, with a torch ship you can fly anywhere.
    //
    // Rendering: physics runs in double-precision heliocentric km. Every frame the camera sits at the render origin
    // (floating origin) and each object is placed at its offset from the camera. Past 100,000 km that offset is
    // compressed logarithmically and the object scaled by the same factor, so angular sizes stay exact and depth order
    // is kept while everything fits inside a float depth range.
    public class OpenSystemScene : SimScene
    {
        public override string Title => "Open System";
        public override string Eyebrow => "The Expanse, true scale";
        public override string Short => "Fly anywhere from Mercury to Neptune";
        public override string Blurb =>
            "Every planet and moon is where it really is today, on its real orbit. Your ship has a fusion drive: pick a <color=#FFB547>target</color> and hit <b>Plot & burn</b>, and it flies a brachistochrone, burning toward the target, flipping at the midpoint and braking the rest of the way, then parks in orbit.\n\n" +
            "Crank time compression to cross the system. At 1 g, Mars is a few days away; Neptune is a few weeks.";
        public override string Note => "Positions: JPL Horizons fits, 2000–2100 (TDB). Drive efficiency and high-g endurance are fictional; trajectories and Δv are real. Coasting follows two-body conics around the dominant body (patched conics). Keys: Space drive on/off · F flip · P plot & burn · T next target · [ ] time compression · M system map. Drag to orbit the camera, scroll to zoom from 20 m to the whole system.";

        const double LinearLimit = 1e5;     // km rendered 1:1 around the camera
        static readonly double[] Warps = { 1, 10, 100, 1e3, 1e4, 1e5, 1e6 };
        static readonly string[] WarpNames = { "1×", "10×", "100×", "1k×", "10k×", "100k×", "1M×" };
        static readonly double[] Gs = { 0.3, 1, 3, 5, 10 };
        const int MaxSubsteps = 4000;

        SolarSystem sys;
        Ship ship;
        double jd;
        int warpIdx;
        bool warpLimited;
        Body reference;

        class Vis { public Body b; public Transform t; public Material[] mats; public bool model; }
        readonly List<Vis> vis = new List<Vis>();
        Transform shipT, shipModel, engine, plume, sunGlow, stars;
        Vector3 engineScale;
        Material plumeMat, orbitMat;
        Mesh orbitMesh;
        readonly List<Vector3> pts = new List<Vector3>();
        readonly List<Color> cols = new List<Color>();
        Quaternion shipRot = Quaternion.identity;
        float shipLen = 1;

        // camera: orbit around a pivot, distance in km
        enum Pivot { Ship, Target, Sun }
        Pivot pivot;
        double camDist = 0.15;
        float yaw = 30, pitch = 12;
        D3 camW;
        int targetIdx;
        List<Body> targets;

        protected override void Build()
        {
            sys = SolarSystem.Build();
            targets = sys.Bodies.FindAll(b => b != sys.Sun && (b.Parent == sys.Sun || b.Radius >= 100 || b == sys.Moon)); // T cycles these
            jd = Frame.JulianDateTdb(DateTime.UtcNow);
            warpIdx = 0;

            Orbit.enabled = false;
            Cam.nearClipPlane = 0.003f;
            Cam.farClipPlane = 4e6f;
            Cam.fieldOfView = 55;

            LoadArtBodies();
            BuildShip();
            PlaceShipNearEarth();
            var sphere = Own(Gfx.UVSphere(128, 64));
            foreach (var b in sys.Bodies)
            {
                var v = new Vis { b = b };
                if (b.Look == Look.Sun)
                {
                    var m = OwnMat(Gfx.Unlit(new Color(1.6f, 1.45f, 1.2f, 1)));
                    v.t = Gfx.Obj(b.Name, Root, sphere, m).transform;
                    v.mats = new[] { m };
                    var glowMat = OwnMat(Gfx.Unlit(new Color(1f, 0.85f, 0.6f, 0.9f), true));
                    glowMat.SetTexture("_MainTex", Own(Gfx.SoftDot(128)));
                    sunGlow = Gfx.Obj("Sun glow", Root, Own(Quad()), glowMat).transform;
                }
                else if (b.Look == Look.Earth)
                {
                    var m = OwnMat(Gfx.Mat("Earth"));
                    Art.Bind(m, ("_DayTex", "Textures/Earth_Day"), ("_NightTex", "Textures/Earth_Night"), ("_CloudTex", "Textures/Earth_Clouds"),
                        ("_OceanTex", "Textures/Earth_Ocean"), ("_NormalTex", "Textures/Earth_Normal"));
                    v.t = Gfx.Obj(b.Name, Root, sphere, m).transform;
                    v.mats = new[] { m };
                }
                else if (ModelFor(b) is string modelPath)
                {
                    // irregular body from the art pack: mean radius 1 (volume-equivalent), body frame given by its
                    // Prime / North markers (the FBX axis conversion can't be trusted). Scaled by meanRadiusKm.
                    // Small moons with no shape model borrow one of the instancing rocks.
                    var holder = new GameObject(b.Name).transform;
                    holder.SetParent(Root, false);
                    var go = Models.Spawn(modelPath, holder, b.ColA);
                    AlignToMarkers(go.transform);
                    if (art.TryGetValue(b.Name, out var info) && info.meanRadiusKm > 0) b.Radius = info.meanRadiusKm;
                    v.t = holder; v.model = true;
                    var rs = go.GetComponentsInChildren<Renderer>();
                    var ms = new List<Material>();
                    var mapped = b.Texture != null && Resources.Load<Texture2D>(b.Texture) != null ? PlanetMaterial(b) : null;
                    foreach (var r in rs)
                    {
                        if (mapped != null) { var arr = new Material[r.sharedMaterials.Length]; for (int i = 0; i < arr.Length; i++) arr[i] = mapped; r.sharedMaterials = arr; ms.Add(mapped); continue; }
                        foreach (var m in r.sharedMaterials) { Own(m); ms.Add(m); }
                    }
                    v.mats = ms.ToArray();
                }
                else
                {
                    var m = PlanetMaterial(b);
                    v.t = Gfx.Obj(b.Name, Root, sphere, m).transform;
                    v.mats = new[] { m };
                }
                vis.Add(v);
            }
            BuildRings();
            belt = new Belt(sys);
            beltView = new BeltView(belt, o => Own(o));
            beltHist = Own(belt.HistogramTexture());
            // biggest first, so labels of large bodies win overlaps
            vis.Sort((x, y) => y.b.Radius.CompareTo(x.b.Radius));
            foreach (var b in sys.Bodies) if (b.Parent != null && b.Radius >= 100) ship.Obstacles.Add(b);

            orbitMesh = Own(Gfx.LineMesh("Orbits"));
            orbitMat = OwnMat(Gfx.Unlit(Color.white, true));
            Gfx.Obj("Orbits", Root, orbitMesh, orbitMat).GetComponent<MeshFilter>().sharedMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e7f);

            stars = Gfx.Starfield(Root, 6000).transform;
            stars.localRotation = Frame.StarsToRender;

        }

        Belt belt;
        BeltView beltView;
        Texture2D beltHist;
        bool showHist;

        class RingVis { public Vis planet; public Transform t; public Material m; public double outerKm; }
        [Serializable] class RingJson { public float innerKm, outerKm; }
        readonly List<RingVis> rings = new List<RingVis>();
        bool faintRings;

        void BuildRings()
        {
            rings.Clear();
            foreach (var name in new[] { "Jupiter", "Saturn", "Uranus", "Neptune" })
            {
                var tex = Resources.Load<Texture2D>("Rings/" + name + "_Rings");
                var json = Resources.Load<TextAsset>("Rings/" + name + "_Rings");
                if (tex == null || json == null) continue;
                var rj = JsonUtility.FromJson<RingJson>(json.text);
                var m = OwnMat(Gfx.Mat("Ring"));
                m.SetTexture("_MainTex", tex);
                m.SetFloat("_Inner", rj.innerKm / rj.outerKm);
                var planet = vis.Find(v => v.b.Name == name);
                var t = Gfx.Obj(name + " rings", Root, Own(Annulus(rj.innerKm / rj.outerKm, 720)), m).transform;
                rings.Add(new RingVis { planet = planet, t = t, m = m, outerKm = rj.outerKm });
            }
        }

        static Mesh Annulus(float inner, int seg)
        {
            var v = new Vector3[seg * 2]; var tri = new int[seg * 6];
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2 / seg, c = Mathf.Cos(a), s = Mathf.Sin(a);
                // the outer edge sits on the polygon's corners, so push it out to cover the true circle
                float outer = 1f / Mathf.Cos(Mathf.PI / seg);
                v[i * 2] = new Vector3(c * inner, 0, s * inner); v[i * 2 + 1] = new Vector3(c * outer, 0, s * outer);
                int j = (i + 1) % seg;
                tri[i * 6] = i * 2; tri[i * 6 + 1] = j * 2; tri[i * 6 + 2] = i * 2 + 1;
                tri[i * 6 + 3] = i * 2 + 1; tri[i * 6 + 4] = j * 2; tri[i * 6 + 5] = j * 2 + 1;
            }
            var m = new Mesh { name = "Annulus", vertices = v, triangles = tri };
            m.RecalculateBounds();
            return m;
        }

        void PlaceRings(D3 sunW)
        {
            foreach (var r in rings)
            {
                var pv = r.planet;
                float k = pv.t.localScale.x / (float)pv.b.Radius; // render units per km at the planet
                r.t.localPosition = pv.t.localPosition;
                r.t.localRotation = pv.t.localRotation;
                r.t.localScale = Vector3.one * (float)(r.outerKm * k);
                r.m.SetVector("_PlanetPos", pv.t.localPosition);
                r.m.SetFloat("_PlanetR", pv.t.localScale.x);
                r.m.SetVector("_SunDir", Frame.U((sunW - sys.Pos(pv.b, jd)).Norm));
                // Saturn's rings are bright for real; the others are τ ≈ 10⁻⁶–10⁻¹ and nearly invisible unless boosted
                r.m.SetFloat("_TauGain", faintRings && pv.b.Name != "Saturn" ? 1000f : 1f);
            }
        }

        Material PlanetMaterial(Body b)
        {
            var m = OwnMat(Gfx.Mat("Planet"));
            m.SetColor("_ColA", b.ColA); m.SetColor("_ColB", b.ColB); m.SetColor("_Atmo", b.Atmo);
            m.SetFloat("_Kind", b.Look == Look.Rocky ? 0 : b.Look == Look.Cloudy ? 1 : b.Look == Look.Gas ? 2 : 3);
            m.SetFloat("_Seed", b.Name.GetHashCode() % 97);
            if (b.Name == "Titan")
            {
                // the art map is the 938 nm surface; in visible light Titan is an orange haze ball
                m.SetColor("_Atmo", new Color(0.9f, 0.62f, 0.3f, 0.9f));
                m.SetColor("_HazeCol", new Color(0.78f, 0.55f, 0.28f));
                m.SetFloat("_Haze", 0.85f);
            }
            if (b.Texture != null && Art.Bind(m, ("_MainTex", b.Texture)))
            {
                // Planets/<Body> maps are contrast-stretched per body; geometric albedo sets the absolute
                // level (perceptual curve, 0.3 → ×1). Older Textures/ maps are already absolute.
                var json = Resources.Load<TextAsset>("Planets/" + b.Name + "/Body");
                float ga = json != null ? JsonUtility.FromJson<BodyJson>(json.text).geometricAlbedo : 0;
                m.SetFloat("_Gain", ga > 0 && b.Texture.StartsWith("Planets/") ? Mathf.Pow(ga / 0.3f, 0.6f) : 1f);
            }
            return m;
        }

        string ModelFor(Body b)
        {
            if (Resources.Load<GameObject>("Models/Bodies/" + b.Name) != null) return "Bodies/" + b.Name;
            bool moon = b.Parent != null && b.Parent != sys.Sun;
            if (moon && b.Radius < 150 && (b.Texture == null || Resources.Load<Texture2D>(b.Texture) == null))
                return $"Asteroids/Asteroid{(b.Name.GetHashCode() & 0x7fffffff) % 32:00}";
            return null;
        }

        [Serializable] class BodyJson { public float geometricAlbedo; }
        [Serializable] class ArtBody { public string name; public float meanRadiusKm; public float geometricAlbedo; }
        [Serializable] class ArtBodies { public ArtBody[] items; }
        readonly Dictionary<string, ArtBody> art = new Dictionary<string, ArtBody>();

        void LoadArtBodies()
        {
            art.Clear();
            var ta = Resources.Load<TextAsset>("Models/Bodies/Bodies");
            if (ta == null) return;
            foreach (var a in JsonUtility.FromJson<ArtBodies>("{\"items\":" + ta.text + "}").items) art[a.name] = a;
        }

        // Rotate a body model inside its holder so its Prime marker lies on +X and North on +Y (the body frame).
        static void AlignToMarkers(Transform model)
        {
            var prime = Models.FindDeep(model, "Prime");
            var north = Models.FindDeep(model, "North");
            if (prime == null || north == null) return;
            var px = (prime.position - model.position).normalized;
            var ny = north.position - model.position;
            ny = (ny - Vector3.Dot(ny, px) * px).normalized;
            var frame = Quaternion.LookRotation(Vector3.Cross(px, ny), ny); // maps +X→prime, +Y→north
            model.localRotation = Quaternion.Inverse(frame) * model.localRotation;
        }

        static Mesh Quad()
        {
            var m = new Mesh { name = "Quad" };
            m.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            return m;
        }

        void BuildShip()
        {
            shipT = new GameObject("Ship").transform;
            shipT.SetParent(Root, false);
            bool own = Resources.Load<GameObject>("Models/PlayerShip") != null;
            var model = Models.Spawn(own ? "PlayerShip" : "TorchShip", shipT, new Color(0.78f, 0.8f, 0.84f));
            shipModel = model.transform;
            Models.AlignMarker(shipModel, "Nose", Vector3.forward);
            var rends = model.GetComponentsInChildren<Renderer>();
            var bounds = rends[0].bounds;
            foreach (var r in rends) bounds.Encapsulate(r.bounds);
            shipLen = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            shipModel.localPosition -= bounds.center - shipT.position;
            engine = Models.FindDeep(shipModel, "Engine");
            if (engine != null) engineScale = engine.localScale;

            plumeMat = OwnMat(Gfx.Unlit(new Color(0.55f, 0.75f, 1f, 0.8f), true));
            plume = Gfx.Obj("Plume", shipT, Own(Gfx.UVSphere(24, 12)), plumeMat).transform;
        }

        void PlaceShipNearEarth()
        {
            var e = sys.Earth;
            var ep = sys.Pos(e, jd);
            var ev = sys.Vel(e, jd);
            var toSun = (-ep).Norm;
            var side = D3.Cross(Frame.EclNorth, toSun).Norm;
            var radial = (toSun * 0.55 + side * 0.83).Norm;
            double r = 15000;
            var tangent = D3.Cross(e.Pole, radial).Norm;
            ship = new Ship
            {
                Pos = ep + radial * r,
                Vel = ev + tangent * Math.Sqrt(e.GM / r),
                Heading = tangent,
                MaxG = 1,
                Target = sys["Mars"],
            };
            targetIdx = targets.IndexOf(ship.Target);
            pivot = Pivot.Ship;
            camDist = 0.16;
            // look past the ship at Earth
            var dir = Frame.U(radial);
            yaw = Mathf.Atan2(dir.x, -dir.z) * Mathf.Rad2Deg;
            pitch = Mathf.Asin(Mathf.Clamp(dir.y, -1, 1)) * Mathf.Rad2Deg + 8;
            reference = e;
        }

        protected override void Teardown()
        {
            Cam.nearClipPlane = 0.05f;
            Orbit.enabled = true;
            vis.Clear();
        }

        // ---------------------------------------------------------------- simulation

        // -fowprof: log where frame time goes, every 2 s
        static readonly bool Prof = Array.IndexOf(Environment.GetCommandLineArgs(), "-fowprof") >= 0;
        readonly System.Diagnostics.Stopwatch swTick = new System.Diagnostics.Stopwatch(), swLate = new System.Diagnostics.Stopwatch(), swGui = new System.Diagnostics.Stopwatch();
        int profFrames, profSteps; float profT;

        public override void Tick(float dt)
        {
            if (Prof) swTick.Start();
            try { TickInner(dt); } finally { if (Prof) swTick.Stop(); }
            if (Prof && (profT += Time.unscaledDeltaTime) > 2f)
            {
                Debug.Log($"PROF frames {profFrames} tick {swTick.Elapsed.TotalMilliseconds / profFrames:F2} ms late {swLate.Elapsed.TotalMilliseconds / profFrames:F2} ms gui {swGui.Elapsed.TotalMilliseconds / profFrames:F2} ms substeps/frame {profSteps / (float)profFrames:F0} warp {WarpNames[warpIdx]}");
                swTick.Reset(); swLate.Reset(); swGui.Reset(); profFrames = profSteps = 0; profT = 0;
            }
            profFrames++;
        }

        void TickInner(float dt)
        {
            double simDt = dt * Warps[warpIdx];
            warpLimited = false;
            // only bodies the ship could reach this frame get per-substep collision checks
            ship.Near.Clear();
            double reach0 = simDt * (ship.Vel.Mag + 60) + 100;
            foreach (var b in sys.Bodies)
                if (b.Parent != null && (sys.Pos(b, jd) - ship.Pos).Mag < reach0 + b.Radius * 2) ship.Near.Add(b);
            int steps = 0;
            while (simDt > 1e-9)
            {
                if (++steps > MaxSubsteps) { warpLimited = true; break; }
                if (ship.Landed != null && !ship.DriveLit && !ship.Autopilot)
                {
                    ship.Step(sys, jd, simDt, reference); // riding the surface: no dynamics to resolve
                    jd += simDt / 86400; simDt = 0;
                    break;
                }
                if (Coasting())
                {
                    double used = Coast(simDt);
                    jd += used / 86400; simDt -= used;
                    profSteps++;
                    continue;
                }
                double h = SubstepSize(simDt);
                ship.Step(sys, jd, h, reference);
                profSteps++;
                jd += h / 86400;
                simDt -= h;
            }
            if (warpLimited && warpIdx > 0) warpIdx--;
            reference = Ship.Dominant(sys, ship.Pos, jd);
        }

        // Drive off (or the autopilot parked in orbit): ride a two-body conic around the dominant body. Exact at any
        // warp; other bodies' pulls are ignored until the next burn (patched conics).
        bool Coasting() => ship.Landed == null && (ship.Autopilot ? ship.Phase == "in orbit" && ship.Throttle == 0 : !ship.DriveLit);

        double Coast(double maxDt)
        {
            var dom = Ship.Dominant(sys, ship.Pos, jd);
            var dp = sys.Pos(dom, jd);
            var r0 = ship.Pos - dp; var v0 = ship.Vel - sys.Vel(dom, jd);
            double T = Rails.Period(r0, v0, dom.GM);
            // chunks short enough to notice a sphere-of-influence change or a surface in the way
            double chunk = Math.Min(maxDt, double.IsInfinity(T) ? Math.Max(60, 0.25 * r0.Mag / Math.Max(v0.Mag, 1e-6)) : T / 16);
            chunk = Math.Max(chunk, Math.Min(maxDt, 1));
            Rails.Propagate(r0, v0, dom.GM, chunk, out var r1, out var v1);
            double jd1 = jd + chunk / 86400;
            ship.Pos = sys.Pos(dom, jd1) + r1;
            ship.Vel = sys.Vel(dom, jd1) + v1;
            ship.Phase = ship.Autopilot ? "in orbit" : "coasting";
            ship.CheckTouchdown(sys, jd1);
            // the autopilot re-checks its orbit after each arc and burns again if it has drifted
            if (ship.Autopilot) ship.Step(sys, jd1, 0.001, dom);
            return chunk;
        }

        double SubstepSize(double remaining)
        {
            // a small fraction of the local orbital period around whatever is closest relative to its pull
            double best = double.MaxValue;
            foreach (var b in sys.Bodies)
            {
                if (b.GM < 1e-3) continue;
                double r = Math.Max((sys.Pos(b, jd) - ship.Pos).Mag, b.Radius);
                best = Math.Min(best, 0.01 * Math.Sqrt(r * r * r / b.GM));
            }
            double h = best;
            if (ship.Throttle > 0 || ship.Autopilot) h = Math.Min(h, 30);
            if (ship.Autopilot && ship.Target != null)
            {
                double d = (sys.Pos(ship.Target, jd) - ship.Pos).Mag;
                double v = (ship.Vel - sys.Vel(ship.Target, jd)).Mag + 1e-6;
                h = Math.Max(Math.Min(h, 0.05 * d / v), 0.5);
            }
            return Math.Max(Math.Min(h, remaining), Math.Min(0.02, remaining));
        }

        // ---------------------------------------------------------------- rendering

        D3 PivotW()
        {
            switch (pivot)
            {
                case Pivot.Target when ship.Target != null: return sys.Pos(ship.Target, jd);
                case Pivot.Sun: return D3.Zero;
                default: return ship.Pos;
            }
        }

        Vector3 OffsetDir()
        {
            float y = yaw * Mathf.Deg2Rad, p = pitch * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(y) * Mathf.Cos(p), Mathf.Sin(p), -Mathf.Cos(y) * Mathf.Cos(p));
        }

        // Render-space position of a world point and the scale factor applied at that distance.
        Vector3 Map(D3 world, out double k)
        {
            var rel = world - camW;
            double d = rel.Mag;
            if (d <= LinearLimit) { k = 1; return Frame.U(rel); }
            double D = LinearLimit * (1 + Math.Log(d / LinearLimit));
            k = D / d;
            return Frame.U(rel * k);
        }

        public override void LateTick(float dt)
        {
            if (Prof) swLate.Start();
            try { LateInner(dt); } finally { if (Prof) swLate.Stop(); }
        }

        void LateInner(float dt)
        {
            camDist = Math.Max(camDist, MinCamDist()); // targets change size under a fixed camera distance
            var off = OffsetDir();
            var pw = PivotW();
            camW = pw + Frame.FromU(off) * camDist;
            Cam.transform.position = Vector3.zero;
            Cam.transform.rotation = Quaternion.LookRotation(-off, Vector3.up);

            var sunW = sys.Pos(sys.Sun, jd);
            foreach (var v in vis)
            {
                var b = v.b;
                var bw = sys.Pos(b, jd);
                v.t.localPosition = Map(bw, out double k);
                float s = (float)(b.Radius * k);
                var sunDir = Frame.U((sunW - bw).Norm);
                if (v.model)
                {
                    v.t.localScale = Vector3.one * s;
                    // the holder is the body frame, so the IAU pole and prime meridian orient it like any planet
                    b.Axes(jd, out _, out var east, out var pole);
                    v.t.localRotation = Quaternion.LookRotation(Frame.U(east), Frame.U(pole));
                    foreach (var m in v.mats) { m.SetMatrix("_WorldToBody", v.t.worldToLocalMatrix); m.SetFloat("_UseBodyFrame", 1); }
                }
                else
                {
                    v.t.localScale = Vector3.Scale(b.Shape, new Vector3(1, (float)(1 - b.Flattening), 1)) * s;
                    b.Axes(jd, out var prime, out var east, out var pole);
                    v.t.localRotation = Quaternion.LookRotation(Frame.U(east), Frame.U(pole));
                }
                foreach (var m in v.mats) { m.SetVector("_SunDir", sunDir); m.SetVector("_FOWSunDir", sunDir); }
                if (b == sys.Sun && sunGlow != null)
                {
                    sunGlow.localPosition = v.t.localPosition;
                    sunGlow.localRotation = Quaternion.LookRotation(v.t.localPosition.normalized);
                    // a corona a few solar radii across, never smaller than a few pixels
                    float minPx = 18f * v.t.localPosition.magnitude * Mathf.Tan(Cam.fieldOfView * Mathf.Deg2Rad / 2) / Mathf.Max(Screen.height, 1);
                    sunGlow.localScale = Vector3.one * Mathf.Max(s * 6f, minPx);
                }
            }

            // ship
            var shipPos = Map(ship.Pos, out double ks);
            shipT.localPosition = shipPos;
            var want = Quaternion.LookRotation(Frame.U(ship.Heading), Mathf.Abs(Vector3.Dot(Frame.U(ship.Heading), Vector3.up)) > 0.98f ? Vector3.right : Vector3.up);
            shipRot = Quaternion.RotateTowards(shipRot, want, dt * 120f * Mathf.Max(1, (float)Warps[warpIdx] / 10f));
            shipT.localRotation = shipRot;
            float realLen = 0.052f; // km
            shipT.localScale = Vector3.one * (float)(realLen * ks / shipLen);
            Shader.SetGlobalVector("_FOWSunDir", Frame.U((sunW - ship.Pos).Norm));
            float thr = (float)ship.Throttle;
            if (engine != null) engine.localScale = thr > 0 ? engineScale * (1 + 0.15f * Mathf.Sin(Time.time * 37f)) : engineScale * 0.0001f;
            plume.gameObject.SetActive(thr > 0.01f);
            if (thr > 0.01f)
            {
                float L = shipLen * (0.6f + 1.4f * Mathf.Sqrt(thr * (float)ship.MaxG / 3f));
                plume.localScale = new Vector3(shipLen * 0.06f, shipLen * 0.06f, L);
                plume.localPosition = new Vector3(0, 0, -shipLen * 0.5f - L * 0.9f);
                plumeMat.SetColor("_Color", new Color(0.55f, 0.75f, 1f, 0.35f + 0.5f * thr));
            }

            PlaceRings(sunW);
            DrawOrbits();
            beltView.Draw(jd, camW, ship.Pos, sunW, Cam, Map);
        }


        void DrawOrbits()
        {
            pts.Clear(); cols.Clear();
            foreach (var b in sys.Bodies)
            {
                if (b.Parent == null) continue;
                bool isMoon = b.Parent != sys.Sun;
                // moons' orbits only when the camera is near enough to see them as more than a dot
                var parentW = isMoon ? sys.Pos(b.Parent, jd) : D3.Zero;
                bool tgt = ship.Target == b;
                if (isMoon && (parentW - camW).Mag > b.A * 400) continue;
                if (isMoon && !tgt && b.Radius < 50) continue; // hundreds of small moons: their orbits would be a hairball
                var c = tgt ? new Color(1f, 0.71f, 0.28f, 0.75f) : isMoon ? new Color(0.5f, 0.6f, 0.85f, 0.35f) : new Color(0.45f, 0.55f, 0.85f, 0.4f);
                // planet orbits fade out when the camera is in close: from a ship they'd be streaks across the screen
                if (!isMoon) c.a *= Mathf.Lerp(tgt ? 0.3f : 0f, 1f, Mathf.Clamp01((float)(Math.Log10(Math.Max(camDist, 1)) - 3) / 4f));
                if (b.Asteroid && !tgt) c.a *= 0.25f; // two dozen named asteroids: keep their orbits in the background
                const int N = 180;
                double period = 360 / Math.Max(Math.Abs(b.dL), 1e-9); // days
                Vector3 prev = default;
                for (int i = 0; i <= N; i++)
                {
                    double t = jd + period * i / N;
                    // the real (perturbed) path relative to the parent, drawn around where the parent is now
                    var p = parentW + (b.Cheb == null && isMoon ? b.Kepler(t) : sys.PosAt(b, t) - (isMoon ? sys.PosAt(b.Parent, t) : D3.Zero));
                    var q = Map(p, out _);
                    if (i > 0) { pts.Add(prev); pts.Add(q); cols.Add(c); cols.Add(c); }
                    prev = q;
                }
            }
            Gfx.SetLines(orbitMesh, pts, cols);
            orbitMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e7f);
        }

        // ---------------------------------------------------------------- input and UI

        public override bool HandleDrag(Vector2 delta, int button)
        {
            yaw -= delta.x * 0.25f;
            pitch = Mathf.Clamp(pitch + delta.y * 0.25f, -89, 89);
            return true;
        }

        public override bool HandleZoom(float wheel)
        {
            camDist = Math.Min(Math.Max(camDist * Math.Exp(wheel * 0.12), MinCamDist()), 2e10);
            return true;
        }

        double MinCamDist()
        {
            if (pivot == Pivot.Ship) return 0.03;
            if (pivot == Pivot.Target && ship.Target != null) return ship.Target.Radius * 1.2;
            return sys.Sun.Radius * 1.5;
        }

        void SetPivot(Pivot p, double dist)
        {
            pivot = p;
            camDist = Math.Max(dist, MinCamDist());
        }

        public override void OnGUIOverlay()
        {
            if (Prof) swGui.Start();
            try { OverlayInner(); } finally { if (Prof) swGui.Stop(); }
        }

        void OverlayInner()
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown && !App.InputLocked)
            {
                switch (e.keyCode)
                {
                    case KeyCode.Space: ship.Autopilot = false; ship.DriveLit = !ship.DriveLit; e.Use(); break;
                    case KeyCode.F: ship.Autopilot = false; ship.Attitude = Attitude.Hold; ship.Heading = -ship.Heading; e.Use(); break;
                    case KeyCode.P: TogglePlot(); e.Use(); break;
                    case KeyCode.T: NextTarget(); e.Use(); break;
                    case KeyCode.LeftBracket: warpIdx = Math.Max(0, warpIdx - 1); e.Use(); break;
                    case KeyCode.RightBracket: warpIdx = Math.Min(Warps.Length - 1, warpIdx + 1); e.Use(); break;
                    case KeyCode.M: ToggleMap(); e.Use(); break;
                }
            }
            DrawLabels();
        }

        void TogglePlot()
        {
            if (ship.Autopilot) { ship.Autopilot = false; ship.Throttle = 0; return; }
            if (ship.Target == null) return;
            ship.Autopilot = true; ship.DriveLit = false; ship.Landed = null;
        }

        void NextTarget()
        {
            SetTarget(targets[(targetIdx + 1) % targets.Count]);
        }

        void SetTarget(Body b)
        {
            ship.Target = b;
            targetIdx = targets.IndexOf(b);
            if (pivot == Pivot.Target) camDist = b.Radius * 4;
        }

        void ToggleMap()
        {
            if (pivot == Pivot.Sun) SetPivot(Pivot.Ship, 0.16);
            else { SetPivot(Pivot.Sun, 6 * Frame.AU); pitch = 65; }
        }

        GUIStyle labelStyle, labelOn;
        readonly List<(Vector2 c, float r, float d)> occluders = new List<(Vector2, float, float)>();

        void DrawLabels()
        {
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle { fontSize = 12, normal = { textColor = new Color(0.75f, 0.8f, 0.95f, 0.85f) } };
                labelOn = new GUIStyle(labelStyle) { fontStyle = FontStyle.Bold, normal = { textColor = PanelUI.Accent } };
            }
            float ui = Mathf.Clamp(Screen.height / 1000f, 0.85f, 2.2f);
            float pxPerRad = Screen.height / 2f / Mathf.Tan(Cam.fieldOfView * Mathf.Deg2Rad / 2);
            var placed = new List<Vector2>();
            // discs of bodies big enough on screen to hide labels of things behind them
            occluders.Clear();
            foreach (var v in vis)
            {
                var p = v.t.localPosition; float d = p.magnitude;
                var sp = Cam.WorldToScreenPoint(p);
                if (sp.z <= 0 || d < 1e-6f) continue;
                float rpx = v.t.localScale.y / d * pxPerRad / ui;
                if (rpx > 4) occluders.Add((new Vector2(sp.x / ui, (Screen.height - sp.y) / ui), rpx, d));
            }

            // ship first so it wins overlaps
            Label("You", shipT.localPosition, 0.05, null, ui, pxPerRad, placed, true);
            foreach (var v in vis) Label(v.b.Name, v.t.localPosition, v.t.localScale.y, v.b, ui, pxPerRad, placed, false);
        }

        void Label(string name, Vector3 renderPos, double renderRadius, Body b, float ui, float pxPerRad, List<Vector2> placed, bool force)
        {
            float dist = renderPos.magnitude;
            if (dist < 1e-6f) return;
            var sp = Cam.WorldToScreenPoint(renderPos);
            if (sp.z <= 0) return;
            float rpx = (float)renderRadius / dist * pxPerRad;
            bool tgt = b != null && b == ship.Target;
            if (rpx > 40 && !tgt) return; // big enough to recognise
            // hundreds of small moons: name them only when the camera is close to them
            if (b != null && !tgt && b.Parent != null && b.Parent != sys.Sun && b.Radius < 100 && dist > 2e4 * renderRadius) return;
            var g = new Vector2(sp.x / ui, (Screen.height - sp.y) / ui);
            foreach (var (c, cr, cd) in occluders)
                if (cd < dist * 0.999f && (c - g).magnitude < cr) return;
            if (!force && !tgt)
                foreach (var q in placed) if ((q - g).sqrMagnitude < 14 * 14) return;
            placed.Add(g);
            var style = tgt ? labelOn : labelStyle;
            GUI.color = tgt ? PanelUI.Accent : new Color(0.75f, 0.8f, 0.95f, 0.9f);
            GUI.DrawTexture(new Rect(g.x - 2, g.y - 2, 4, 4), Texture2D.whiteTexture);
            GUI.color = Color.white;
            var r = new Rect(g.x + 6, g.y - 8, 140, 16);
            if (b != null && GUI.Button(r, name, style)) SetTarget(b);
            else if (b == null) GUI.Label(r, name, style);
        }

        public override void Readouts(List<Readout> list)
        {
            var date = Frame.UtcFromTdb(jd);
            list.Add(new Readout("Date (UTC)", date.ToString("yyyy-MM-dd HH:mm")));
            list.Add(new Readout("Time compression", WarpNames[warpIdx] + (warpLimited ? " (limited)" : ""), warpLimited ? Tone.Bad : Tone.Normal));
            var refPos = sys.Pos(reference, jd);
            double alt = (ship.Pos - refPos).Mag - reference.Radius;
            list.Add(new Readout("Near", reference.Name + ", " + Km(alt) + (reference == sys.Sun ? " out" : " up")));
            list.Add(new Readout("Speed vs " + reference.Name, Fmt.Num((ship.Vel - sys.Vel(reference, jd)).Mag, 3) + " km/s"));
            list.Add(new Readout("From the Sun", Fmt.Num(ship.Pos.Mag / Frame.AU, 3) + " AU"));
            list.Add(new Readout("Drive", ship.Throttle > 0 ? Fmt.Num(ship.Throttle * ship.MaxG, 2) + " g" : "off", ship.Throttle > 0 ? Tone.Hot : Tone.Normal));
            list.Add(new Readout("Δv spent", Fmt.Num(ship.DvUsed, 3) + " km/s"));
            // jet power P = ½·m·a·vₑ for a 500 t ship and a D–He³-class exhaust; no known physics sheds that heat
            if (ship.Throttle > 0) list.Add(new Readout("Jet power (fictional efficiency)", Fmt.Num(0.5 * 5e5 * ship.Throttle * ship.AMax * 1e3 * 1e7 / 1e12, 3) + " TW", Tone.Bad));
            if (sys.Approximate(jd)) list.Add(new Readout("Ephemeris", "outside 2000–2100: approximate", Tone.Bad));
            if (ship.Target != null)
            {
                var tp = sys.Pos(ship.Target, jd);
                var r = tp - ship.Pos;
                var vr = ship.Vel - sys.Vel(ship.Target, jd);
                double closing = D3.Dot(vr, r.Norm);
                list.Add(new Readout("Target", ship.Target.Name + (ship.Target.SizeEstimated ? " (size unknown, shown 2 km)" : ""), Tone.Hot));
                if (ship.Target.Name == "Titan") list.Add(new Readout("Titan surface", "shown faintly through the haze (938 nm map)", Tone.Bad));
                list.Add(new Readout("Distance", Km(r.Mag - ship.Target.Radius)));
                list.Add(new Readout("Closing speed", Fmt.Num(closing, 3) + " km/s", closing > 0 ? Tone.Cold : Tone.Normal));
                list.Add(new Readout("Flip-and-burn at " + Fmt.Num(ship.MaxG, 2) + " g", Dur(ship.EtaSeconds(sys, jd))));
            }
            double rAu = ship.Pos.Mag / Frame.AU;
            if (rAu > 1.7 && rAu < 5.6 && beltView.NearestIndex >= 0)
                list.Add(new Readout("Nearest belt rock (1 % sample)", Km(beltView.NearestKm) + ", " + Fmt.Num(belt.RadiusKm[beltView.NearestIndex] * 2, 1) + " km wide"));
            if (faintRings) list.Add(new Readout("Faint rings", "brightened ×1000 (not real)", Tone.Bad));
            list.Add(new Readout("Autopilot", ship.Autopilot ? ship.Phase : ship.Landed != null ? ship.Phase : "off", ship.Autopilot ? Tone.Hot : Tone.Normal));
        }

        static string Km(double km) => km > 0.05 * Frame.AU ? Fmt.Num(km / Frame.AU, 3) + " AU" : km >= 1 ? Fmt.Num(km, 0) + " km" : Fmt.Num(km * 1000, 0) + " m";

        static string Dur(double s)
        {
            if (s < 120) return Fmt.Num(s, 0) + " s";
            if (s < 7200) return Fmt.Num(s / 60, 1) + " min";
            if (s < 172800) return Fmt.Num(s / 3600, 1) + " h";
            return Fmt.Num(s / 86400, 1) + " days";
        }

        public override void Controls(PanelUI ui)
        {
            int b = ui.Buttons(ship.Autopilot ? "Cut drive" : "Plot & burn", ship.DriveLit ? "Drive off" : "Drive on", "Flip");
            if (b == 0) TogglePlot();
            if (b == 1) { ship.Autopilot = false; ship.DriveLit = !ship.DriveLit; }
            if (b == 2) { ship.Autopilot = false; ship.Attitude = Attitude.Hold; ship.Heading = -ship.Heading; }

            GUILayout.Label("Drive", ui.label);
            var gl = new string[Gs.Length];
            for (int i = 0; i < Gs.Length; i++) gl[i] = (Math.Abs(ship.MaxG - Gs[i]) < 1e-6 ? "● " : "") + Fmt.Num(Gs[i], 1) + " g" + (Gs[i] >= 10 ? " (juice: fictional)" : "");
            int gi = ui.Buttons(gl);
            if (gi >= 0) ship.MaxG = Gs[gi];

            GUILayout.Label("Point the ship", ui.label);
            int ai = ui.Buttons("Prograde", "Retrograde", "At target", "Away");
            if (ai >= 0) { ship.Autopilot = false; ship.Attitude = ai == 0 ? Attitude.Prograde : ai == 1 ? Attitude.Retrograde : ai == 2 ? Attitude.Target : Attitude.AntiTarget; }

            GUILayout.Label("Time compression", ui.label);
            var wl = new string[Warps.Length];
            for (int i = 0; i < Warps.Length; i++) wl[i] = (i == warpIdx ? "● " : "") + WarpNames[i];
            int wi = ui.Buttons(wl);
            if (wi >= 0) warpIdx = wi;

            beltView.Visible = ui.Toggle("Show the asteroid belt (a 1 % sample: real rocks are ~1 million km apart)", beltView.Visible);
            showHist = ui.Toggle("Belt semi-major axis histogram", showHist);
            if (showHist)
            {
                ui.Image(beltHist, 90);
                GUILayout.Label("1.6 → 5.6 AU. Kirkwood gaps (Jupiter resonances 4:1, 3:1, 5:2, 7:3, 2:1) at 2.06, 2.50, 2.82, 2.96, 3.28 AU; Hildas at 3.97 (3:2); Trojans at 5.2 (1:1). The gaps are in orbit size, so they don't show as empty rings in space.", ui.note);
            }
            faintRings = ui.Toggle("Brighten faint rings ×1000 (Jupiter, Uranus, Neptune)", faintRings);
            GUILayout.Label("Camera", ui.label);
            int ci = ui.Buttons("Ship", "Target", "System map");
            if (ci == 0) SetPivot(Pivot.Ship, 0.16);
            if (ci == 1 && ship.Target != null) SetPivot(Pivot.Target, ship.Target.Radius * 4);
            if (ci == 2) { SetPivot(Pivot.Sun, 6 * Frame.AU); pitch = 65; }

            // target picker: choose a planet, then its moons (largest first; tiny ones are clickable by label)
            var planets = sys.Bodies.FindAll(x => x.Parent == sys.Sun && !x.Asteroid);
            var sel = ship.Target == null ? null : ship.Target.Parent == sys.Sun ? ship.Target : ship.Target.Parent;
            int si = ui.Buttons(planets.ConvertAll(p => (p == sel ? "● " : "") + p.Name).ToArray());
            if (si >= 0) SetTarget(planets[si]);
            if (sel != null)
            {
                var moons = sys.Bodies.FindAll(x => x.Parent == sel && x.Radius >= 10);
                moons.Sort((x, y) => y.Radius.CompareTo(x.Radius));
                int small = sys.Bodies.FindAll(x => x.Parent == sel).Count - moons.Count;
                if (moons.Count > 0)
                {
                    var mnames = moons.ConvertAll(m => (m == ship.Target ? "● " : "") + m.Name).ToArray();
                    int mi = ui.Buttons(mnames);
                    if (mi >= 0) SetTarget(moons[mi]);
                }
                if (small > 0) GUILayout.Label(small + " smaller moons: click their labels in view", ui.label);
            }
            var rocks = sys.Bodies.FindAll(x => x.Asteroid);
            GUILayout.Label("Asteroids", ui.label);
            int ri = ui.Buttons(rocks.ConvertAll(x => (x == ship.Target ? "● " : "") + x.Name).ToArray());
            if (ri >= 0) SetTarget(rocks[ri]);
        }
    }
}
