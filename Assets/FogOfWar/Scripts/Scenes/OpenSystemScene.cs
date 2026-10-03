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
    public partial class OpenSystemScene : SimScene
    {
        public override string Title => "Open System";
        public override string Eyebrow => "The Expanse, true scale";
        public override string Short => "Fly anywhere from Mercury to Neptune";
        public override string Blurb =>
            "Every planet and moon is where it really is today, on its real orbit. Your ship has a fusion drive: pick a <color=#FFB547>target</color> and hit <b>Plot & burn</b>, and it flies a brachistochrone, burning toward the target, flipping at the midpoint and braking the rest of the way, then parks in orbit.\n\n" +
            "Crank time compression to cross the system. At 1 g, Mars is a few days away; Neptune is a few weeks.";
        public override string Note => "Positions: JPL Horizons fits, 2000–2100 (TDB). Drive efficiency and high-g endurance are fictional; trajectories and Δv are real. Coasting follows two-body conics around the dominant body (patched conics). F1 lists the controls; Esc → Controls rebinds them.";

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
        float limitedAt = -10;
        int lastWarpIdx = -1;
        double effectiveWarp = 1;
        Body reference;

        class Vis { public Body b; public Transform t; public Material[] mats; public bool model; }
        readonly List<Vis> vis = new List<Vis>();
        Transform shipT, shipModel, engine, plume, sunGlow, stars;
        Vector3 engineScale;
        Material plumeMat, orbitMat;
        Mesh orbitMesh;
        readonly List<Vector3> pts = new List<Vector3>();
        readonly List<Color> cols = new List<Color>();
        float shipLen = 1;

        // camera: orbit around a pivot, distance in km; or chase / cockpit views that follow the ship's attitude
        enum Pivot { Ship, Target, Sun }
        enum CamMode { Chase, Orbit, Cockpit }
        CamMode camMode = CamMode.Chase;
        float chaseYaw, chasePitch = 14, lookYaw, lookPitch;
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
            BuildCockpit();
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
                    Art.Bind(m, ("_DayTex", "Planets/Earth/Color"), ("_NightTex", "Planets/Earth/Night"), ("_CloudTex", "Planets/Earth/Clouds"),
                        ("_OceanTex", "Planets/Earth/Ocean"), ("_NormalTex", "Planets/Earth/Normal"));
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
            InitCareer();

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
                // level (perceptual curve, 0.3 → ×1). Maps marked mapIsAbsolute (Mars) are used as they are.
                var json = Resources.Load<TextAsset>("Planets/" + b.Name + "/Body");
                var info = json != null ? JsonUtility.FromJson<BodyJson>(json.text) : null;
                float ga = info != null && !info.mapIsAbsolute ? info.geometricAlbedo : 0;
                m.SetFloat("_Gain", ga > 0 ? Mathf.Pow(ga / 0.3f, 0.6f) : 1f);
                // relief from the body's DEM, where the pipeline made one (Tools/fidelity/normals.py)
                var nrm = Art.Tex("Planets/" + b.Name + "/Normal");
                if (nrm != null) { m.SetTexture("_NormalTex", nrm); m.SetFloat("_HasNormal", 1); }
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

        [Serializable] class BodyJson { public float geometricAlbedo; public bool mapIsAbsolute; }
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

            plumeMat = OwnMat(Gfx.Mat("Plume"));
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
                Att = Quaternion.LookRotation(Frame.U(tangent), Frame.U(e.Pole)),
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
            if (Cam != null) Cam.nearClipPlane = 0.05f; // on quit the camera may already be gone
            Orbit.enabled = true;
            DisposeCockpit(); // gives the camera back its full culling mask
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
            ReadInput(dt);
            double simDt = dt * Warps[warpIdx];
            warpLimited = false;
            // only bodies the ship could reach this frame get per-substep collision checks
            ship.Near.Clear();
            double reach0 = simDt * (ship.Vel.Mag + 60) + 100;
            foreach (var b in sys.Bodies)
                if (b.Parent != null && (sys.Pos(b, jd) - ship.Pos).Mag < reach0 + b.Radius * 2) ship.Near.Add(b);
            int steps = 0;
            // QA-003: a frame budget, so heavy integration lowers the warp instead of the frame rate
            var budget = System.Diagnostics.Stopwatch.StartNew();
            while (simDt > 1e-9)
            {
                if (++steps > MaxSubsteps || budget.Elapsed.TotalMilliseconds > 10) { warpLimited = true; break; }
                if (ship.Landed != null && ship.ThrottleCmd == 0 && !ship.PlanActive && !ship.ManualInput)
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
            // the player's warp stays selected; when the frame budget runs out, report the rate actually achieved
            double achieved = dt * Warps[warpIdx] - Math.Max(simDt, 0);
            effectiveWarp = effectiveWarp * 0.9 + 0.1 * (dt > 0 ? achieved / dt : 0);
            if (warpLimited) limitedAt = Time.realtimeSinceStartup;
            // QA-022: a new warp choice starts fresh, so a stale "limited" note never outlives the setting it was about
            if (warpIdx != lastWarpIdx) { lastWarpIdx = warpIdx; limitedAt = -10; effectiveWarp = Warps[warpIdx]; }
            reference = Ship.Dominant(sys, ship.Pos, jd);
            TickCareer(achieved);
        }

        // Drive off (or the autopilot parked in orbit): ride a two-body conic around the dominant body. Exact at any
        // warp; other bodies' pulls are ignored until the next burn (patched conics).
        bool Coasting() => ship.Landed == null && ship.RcsInput.sqrMagnitude == 0 &&
            (ship.PlanActive ? ship.Throttle == 0 && (ship.Phase == "in orbit" || ship.Phase == "velocity matched" || ship.Phase == "cruising") : ship.ThrottleCmd == 0);

        double Coast(double maxDt)
        {
            var dom = Ship.Dominant(sys, ship.Pos, jd);
            var dp = sys.Pos(dom, jd);
            var r0 = ship.Pos - dp; var v0 = ship.Vel - sys.Vel(dom, jd);
            double T = Rails.Period(r0, v0, dom.GM);
            // chunks short enough to notice a sphere-of-influence change or a surface in the way
            double chunk = Math.Min(maxDt, double.IsInfinity(T) ? Math.Max(60, 0.25 * r0.Mag / Math.Max(v0.Mag, 1e-6)) : T / 16);
            // a cruising autopilot must wake up in time for its braking point
            if (ship.PlanActive && ship.Phase == "cruising")
                chunk = Math.Min(chunk, 0.01 * (sys.Pos(ship.Target, jd) - ship.Pos).Mag / Math.Max((ship.Vel - sys.Vel(ship.Target, jd)).Mag, 1e-3));
            chunk = Math.Max(chunk, Math.Min(maxDt, 1));
            Rails.Propagate(r0, v0, dom.GM, chunk, out var r1, out var v1);
            double jd1 = jd + chunk / 86400;
            ship.Pos = sys.Pos(dom, jd1) + r1;
            ship.Vel = sys.Vel(dom, jd1) + v1;
            ship.Throttle = 0; // nothing fires on a coast arc
            if (!ship.PlanActive) ship.Phase = "coasting";
            ship.Turn(sys, jd1, chunk, dom);
            ship.CheckTouchdown(sys, jd1);
            // the autopilot re-checks its plan after each arc and burns again if it has drifted
            if (ship.PlanActive) ship.Step(sys, jd1, 0.001, dom);
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
            if (ship.ThrottleCmd > 0 || ship.PlanActive || ship.RcsInput.sqrMagnitude > 0) h = Math.Min(h, 30);
            // rotating under pilot control at low warp: small steps so the rigid-body dynamics stay smooth
            if (ship.RotInput.sqrMagnitude > 0 || ship.AngVel.sqrMagnitude > 0) h = Math.Min(h, 0.05);
            foreach (var b in ship.Near)
            {
                if (b == sys.Sun) continue;
                var rel = ship.Pos - sys.Pos(b, jd);
                double alt = rel.Mag - b.Radius, closing = -D3.Dot(ship.Vel - sys.Vel(b, jd), rel.Norm);
                if (closing > 0) h = Math.Min(h, Math.Max(0.2 * alt / closing, 0.05));
            }
            if (ship.PlanActive)
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

        static Vector3 OffsetDir(float yawDeg, float pitchDeg)
        {
            float y = yawDeg * Mathf.Deg2Rad, p = pitchDeg * Mathf.Deg2Rad;
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
            MixSound();
            if (Prof) swLate.Start();
            try { LateInner(dt); } finally { if (Prof) swLate.Stop(); }
        }

        void LateInner(float dt)
        {
            camDist = Math.Max(camDist, MinCamDist()); // targets change size under a fixed camera distance
            var mode = pivot == Pivot.Ship ? camMode : CamMode.Orbit;
            Cam.transform.position = Vector3.zero;
            if (App.Rig != null)
            {
                // photo mode / cinematics: a free camera at an offset (km) from the pivot
                camW = PivotW() + Frame.FromU(App.Rig.Offset);
                Cam.transform.rotation = App.Rig.Rotation;
                mode = CamMode.Orbit; // keep the ship model visible
            }
            else if (mode == CamMode.Chase)
            {
                // behind and above the ship, turning with it
                var off = ship.Att * OffsetDir(chaseYaw, chasePitch);
                camW = ship.Pos + Frame.FromU(off) * camDist;
                Cam.transform.rotation = Quaternion.LookRotation(-off, ship.Att * Vector3.up);
            }
            else if (mode == CamMode.Cockpit)
            {
                camW = ship.Pos + ship.Heading * 0.02;
                Cam.transform.rotation = ship.Att * Quaternion.Euler(lookPitch, lookYaw, 0);
            }
            else
            {
                var off = OffsetDir(yaw, pitch);
                camW = PivotW() + Frame.FromU(off) * camDist;
                Cam.transform.rotation = Quaternion.LookRotation(-off, Vector3.up);
            }
            shipModel.gameObject.SetActive(mode != CamMode.Cockpit);
            LateCockpit(mode == CamMode.Cockpit);

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
            shipT.localRotation = ship.Att;
            float realLen = 0.052f; // km
            shipT.localScale = Vector3.one * (float)(realLen * ks / shipLen);
            Shader.SetGlobalVector("_FOWSunDir", Frame.U((sunW - ship.Pos).Norm));
            float thr = (float)ship.Throttle;
            if (engine != null) engine.localScale = thr > 0 ? engineScale * (1 + 0.15f * Mathf.Sin(Time.time * 37f)) : engineScale * 0.0001f;
            plume.gameObject.SetActive(thr > 0.01f);
            if (thr > 0.01f)
            {
                float L = shipLen * Mathf.Min(0.5f + 0.9f * Mathf.Sqrt(thr * (float)ship.MaxG / 3f), 1.6f);
                plume.localScale = new Vector3(shipLen * 0.06f, shipLen * 0.06f, L);
                plume.localPosition = new Vector3(0, 0, -shipLen * 0.5f - L * 0.9f);
                float behind = Mathf.Clamp01(Vector3.Dot((shipT.position - Cam.transform.position).normalized, shipT.forward));
                plumeMat.SetFloat("_Intensity", (0.5f + 0.9f * thr) * Mathf.Lerp(1f, 0.3f, behind));
            }

            PlaceRings(sunW);
            DrawOrbits();
            beltView.Draw(jd, camW, ship.Pos, sunW, Cam, Map);
            LateCareer();
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

    }
}
