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
        public override string Eyebrow => "Torch-ship flight, true scale";
        public override string Short => "Fly anywhere from Mercury to Neptune";
        public override string Blurb =>
            "Explore today's solar system using fitted and mean-element orbits based on JPL data. Your ship has a fictional torch drive: pick a <color=#FFB547>target</color> and hit <b>Plot & burn</b> for a feedback-guided transfer. It accelerates, turns to brake, then aims to park in orbit; the ideal flip-and-burn estimate is a planning guide.\n\n" +
            "Crank time compression to cross the system. At 1 g, Mars is a few days away; Neptune is a few weeks.";
        public override string Note => "Positions mix JPL fits and mean elements; fitted coverage and fallback vary by body. The drive regulates acceleration with assumed thrust reserve; cargo raises fuel cost. Translational RCS shares its fictional exhaust efficiency. Electric reaction wheels use no reaction mass; saturation, power and waste-heat limits are omitted. High-g endurance is fictionalized. Through 100×, burns and coasts share multi-body gravity and J2; at 1k× and above, eligible coasts use a labelled two-body approximation. Atmospheric arrivals are assisted; drag, pressure and entry heating are omitted. F1 lists the controls; Esc → Controls rebinds them.";

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

        class Vis { public Body b; public Transform t; public Material[] mats; public bool model; public TerrainBody terrain; public Renderer r; public GameObject[] lods; public int lod; }
        readonly List<Vis> vis = new List<Vis>();
        Transform shipT, shipModel, engine, plume, sunGlow, stars;
        Vector3 engineScale;
        Material plumeMat, orbitMat;
        ShipLighting lighting;
        Mesh orbitMesh;
        readonly List<Vector3> pts = new List<Vector3>();
        readonly List<Color> cols = new List<Color>();
        float shipLen = 1;

        // camera: orbit around a pivot, distance in km; or chase / cockpit views that follow the ship's attitude
        enum Pivot { Ship, Target, Sun }
        enum CamMode { Chase, Orbit, Cockpit }
        CamMode camMode = CamMode.Chase;
        const float ChasePitch0 = 14;
        float chaseYaw, chasePitch = ChasePitch0, lookYaw, lookPitch;
        Pivot pivot;
        double camDist = 0.15;
        float yaw = 30, pitch = 12;
        D3 camW;
        int targetIdx;
        List<Body> targets;

        protected override void Build()
        {
            sys = SolarSystem.Build();
            traffic = null; // its ships were bodies of the old system
            targets = sys.Bodies.FindAll(b => b != sys.Sun && (b.Parent == sys.Sun || b.Radius >= 100 || b == sys.Moon)); // T cycles these
            jd = CareerTesting ? CareerQA.Epoch : Frame.JulianDateTdb(DateTime.UtcNow);
            warpIdx = 0;
            // the scene object lives across New game: a new flight starts with the drawer closed, no page open and a
            // compact target card
            consoleOpen = false; pageView = -1; lessonSeen = null; cardExpanded = false;

            Orbit.enabled = false;
            Cam.nearClipPlane = 0.003f;
            Cam.farClipPlane = 4e6f;
            Cam.fieldOfView = 55;
            Bloom.Enable(Cam, true);

            LoadArtBodies();
            BuildShip();
            BuildCockpit();
            PlaceShipNearEarth();
            var sphere = TerrainBody.UnitSphere(); // shared smooth cube-sphere; bodies with a DEM get their own
            foreach (var b in sys.Bodies)
            {
                var v = new Vis { b = b };
                if (b.Look == Look.Sun)
                {
                    // a 5772 K blackbody against the sRGB (D65) white point: (1, 0.95, 0.91). From space the Sun is
                    // white; the old warm tints (blue at sRGB 0.6 in the halo) read as orange (QA-048)
                    var sunWhite = new Color(1f, 0.95f, 0.91f);
                    var m = OwnMat(Gfx.Unlit(new Color(sunWhite.r * 1.6f, sunWhite.g * 1.6f, sunWhite.b * 1.6f, 1)));
                    v.t = Gfx.Obj(b.Name, Root, sphere, m).transform;
                    v.mats = new[] { m };
                    var glowMat = OwnMat(Gfx.Unlit(new Color(sunWhite.r, sunWhite.g, sunWhite.b, 0.9f), true));
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
                    // With LODs (Blender/lods.py: <Name>_LOD0/1/2, same frame and scale) all three are spawned and the
                    // one that suits the body's size on screen is shown.
                    var holder = new GameObject(b.Name).transform;
                    holder.SetParent(Root, false);
                    if (art.TryGetValue(b.Name, out var info) && info.meanRadiusKm > 0)
                    { b.Radius = info.meanRadiusKm; b.RadiusSource = "shape asset volume-equivalent radius"; b.SizeEstimated = false; }
                    b.ShapeSource = modelPath.StartsWith("Asteroids/", StringComparison.Ordinal) ?
                        "borrowed procedural asteroid mesh (unmeasured shape)" : "body shape asset: Models/" + modelPath + " (source in Models/Bodies/LODS.json)";
                    v.t = holder; v.model = true;
                    bool lods = Resources.Load<GameObject>($"Models/Bodies/{b.Name}_LOD0") != null;
                    var paths = lods ? new[] { $"Bodies/{b.Name}_LOD0", $"Bodies/{b.Name}_LOD1", $"Bodies/{b.Name}_LOD2" } : new[] { modelPath };
                    bool mappedBody = b.Texture != null && Resources.Load<Texture2D>(b.Texture) != null;
                    var ms = new List<Material>();
                    var gos = new GameObject[paths.Length];
                    for (int n = 0; n < paths.Length; n++)
                    {
                        var go = gos[n] = Models.Spawn(paths[n], holder, b.ColA);
                        AlignToMarkers(go.transform);
                        // each LOD has its own normal map (baked from LOD0), so its own material
                        var nrm = Resources.Load<Texture2D>(NormalMapFor(paths[n]));
                        var mapped = mappedBody ? PlanetMaterial(b) : null;
                        foreach (var r in go.GetComponentsInChildren<Renderer>())
                        {
                            if (mapped != null) { var arr = new Material[r.sharedMaterials.Length]; for (int i = 0; i < arr.Length; i++) arr[i] = mapped; r.sharedMaterials = arr; continue; }
                            // QA-045: SimpleLit's blue rim and gloss are for hulls; on bare rock the rim drew a blue shell
                            // around the silhouette, night side included
                            foreach (var m in r.sharedMaterials) { Own(m); m.SetColor("_Rim", Color.black); m.SetFloat("_Spec", 0); SetLodNormal(m, nrm); ms.Add(m); }
                        }
                        if (mapped != null) { SetLodNormal(mapped, nrm); ms.Add(mapped); }
                        go.SetActive(n == paths.Length - 1);
                    }
                    if (lods) { v.lods = gos; v.lod = paths.Length - 1; }
                    v.mats = ms.ToArray();
                }
                else
                {
                    var m = PlanetMaterial(b);
                    // the streaming quadtree takes over near the body; real relief where the terrain pack has its DEM
                    var data = b.Look == Look.Rocky ? OpenWorld.TerrainData.Open(b.Name) : null;
                    v.terrain = new TerrainBody(b, data);
                    v.t = Gfx.Obj(b.Name, Root, data != null ? Own(v.terrain.WholeMesh()) : sphere, m).transform;
                    v.r = v.t.GetComponent<Renderer>();
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
                r.m.SetFloat("_SunIrr", ShipLighting.Irradiance((sunW - sys.Pos(pv.b, jd)).Mag));
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

        // The baked normal map of a model path (Blender/lods.py, Blender/rocks.py), if the art pack has one.
        static string NormalMapFor(string modelPath)
        {
            var name = modelPath.Substring(modelPath.LastIndexOf('/') + 1);
            if (modelPath.StartsWith("Asteroids/")) return $"Models/Asteroids/Normals/{name}_Normal";
            int lod = name.IndexOf("_LOD", StringComparison.Ordinal);
            return lod > 0 ? $"Models/Bodies/{name.Substring(0, lod)}/{name}_Normal" : null;
        }

        static void SetLodNormal(Material m, Texture2D nrm)
        {
            m.SetFloat("_HasLodNormal", nrm != null ? 1 : 0);
            if (nrm != null) m.SetTexture("_LodNormal", nrm);
        }

        // Shape-model LOD by the body's radius on screen (px): LOD2 (10k triangles) below 200, LOD1 (50k) below 800,
        // LOD0 (200k) above, with 10 % either way so a body on a threshold doesn't flicker between two.
        static int LodFor(float px, int cur)
        {
            int want = px > 800 ? 0 : px > 200 ? 1 : 2;
            if (want < cur && px < (want == 0 ? 880 : 220)) return cur;
            if (want > cur && px > (cur == 0 ? 720 : 180)) return cur;
            return want;
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
            // textured hull (PBR) with physical lighting; without the texture set the flat Models.Spawn look stays
            var hull = ShipLighting.ApplyHullMaterial(model);
            if (hull != null && hull.Length > 0) OwnMat(hull[0].sharedMaterial);
            lighting = new ShipLighting(hull);
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
            foreach (var v in vis) v.terrain?.Dispose();
            vis.Clear();
            lighting?.Dispose();
            lighting = null;
            Bloom.Enable(Cam, false);
            RecorderClose();
        }

        // ---------------------------------------------------------------- simulation

        // -fowprof: log where frame time goes, every 2 s
        static readonly bool Prof = Array.IndexOf(Environment.GetCommandLineArgs(), "-fowprof") >= 0;
        readonly System.Diagnostics.Stopwatch swTick = new System.Diagnostics.Stopwatch(), swLate = new System.Diagnostics.Stopwatch(), swGui = new System.Diagnostics.Stopwatch(),
            swTraffic = new System.Diagnostics.Stopwatch(); // inside tick and gui
        int profFrames, profSteps; float profT;
        static readonly bool CareerTesting = Array.IndexOf(Environment.GetCommandLineArgs(), "-fowcareer") >= 0;
        // QA stops on an exact simulation epoch, regardless of how many real frames the machine needs.
        public double QaStopJd = double.PositiveInfinity;

        public override void Tick(float dt)
        {
            if (CareerTesting) dt = 1f / 60;
            if (Prof) swTick.Start();
            try { TickInner(dt); } finally { if (Prof) swTick.Stop(); }
            if (Prof && (profT += Time.unscaledDeltaTime) > 2f)
            {
                Debug.Log($"PROF frames {profFrames} tick {swTick.Elapsed.TotalMilliseconds / profFrames:F2} ms late {swLate.Elapsed.TotalMilliseconds / profFrames:F2} ms gui {swGui.Elapsed.TotalMilliseconds / profFrames:F2} ms traffic {swTraffic.Elapsed.TotalMilliseconds / profFrames:F2} ms ({traffic?.Flying.Count ?? 0} ships) substeps/frame {profSteps / (float)profFrames:F0} warp {WarpNames[warpIdx]}");
                swTick.Reset(); swLate.Reset(); swGui.Reset(); swTraffic.Reset(); profFrames = profSteps = 0; profT = 0;
            }
            profFrames++;
        }

        void TickInner(float dt)
        {
            if (ship.Crashed != null) { Crashed(); return; }
            SpawnRunnerTick();
            ReadInput(dt);
            if (ship.VelRef != null && (ship.VelRef != ship.Target || !sys.Bodies.Contains(ship.VelRef))) ship.VelRef = null;
            double simDt = Math.Min(dt * Warps[warpIdx], Math.Max(0, (QaStopJd - jd) * 86400));
            double requested = simDt;
            warpLimited = false;
            // only bodies the ship could reach this frame get per-substep collision checks; craft (a derelict, the practice
            // beacon) have no surface to land on or crash into
            ship.Near.Clear();
            double reach0 = simDt * (ship.Vel.Mag + 60) + 100;
            foreach (var b in sys.Bodies)
                if (b.Parent != null && !Career.IsCraft(b) && (sys.Pos(b, jd) - ship.Pos).Mag < reach0 + b.Radius * 2) ship.Near.Add(b);
            int steps = 0;
            // QA-003: a frame budget, so heavy integration lowers the warp instead of the frame rate
            var budget = System.Diagnostics.Stopwatch.StartNew();
            while (simDt > 1e-9)
            {
                if (++steps > MaxSubsteps || (!CareerTesting && budget.Elapsed.TotalMilliseconds > 10)) { warpLimited = true; break; }
                if (ship.Landed != null && ship.ThrottleCmd == 0 && !ship.PlanActive && !ship.ManualInput)
                {
                    ship.Step(sys, jd, simDt, reference); // riding the surface: no dynamics to resolve
                    jd += simDt / 86400; simDt = 0;
                    break;
                }
                if (Coasting())
                {
                    double used = Coast(simDt);
                    if (used > 0)
                    {
                        jd += used / 86400; simDt -= used;
                        profSteps++;
                        continue;
                    }
                    // the conic would not converge: integrate this substep instead
                }
                double h = SubstepSize(simDt);
                // jd (~2.46e6 days) resolves only ~40 µs, so step by exactly the time jd advances: otherwise each 1/60 s
                // step rounds the planets' clock against the ship's (0.06 % slow, an 18 m/s drift against Earth)
                h = (jd + h / 86400 - jd) * 86400;
                if (h <= 0) break;
                ship.Step(sys, jd, h, reference);
                profSteps++;
                jd += h / 86400;
                simDt -= h;
                if (simDt < 1e-4) simDt = 0; // finer than jd can resolve
            }
            // the player's warp stays selected; when the frame budget runs out, report the rate actually achieved
            double achieved = requested - Math.Max(simDt, 0);
            effectiveWarp = effectiveWarp * 0.9 + 0.1 * (dt > 0 ? achieved / dt : 0);
            if (warpLimited) limitedAt = Time.realtimeSinceStartup;
            // QA-022: a new warp choice starts fresh, so a stale "limited" note never outlives the setting it was about
            if (warpIdx != lastWarpIdx) { lastWarpIdx = warpIdx; limitedAt = -10; effectiveWarp = Warps[warpIdx]; }
            reference = Ship.Dominant(sys, ship.Pos, jd);
            TickTraffic();
            TickCareer(achieved);
            RecorderTick(dt, steps);
        }

        // #25: an impact above Ship.SafeTouchdown ends the flight. The last save comes back (under the active
        // -fowsavekey); with none, a new game starts. The note says what happened either way.
        static string crashNote;
        void Crashed()
        {
            string what = $"Destroyed: hit {ship.Crashed.Name} at {Speed(ship.ImpactSpeed)}. ";
            ship.Crashed = null;
            if (HasSave)
            {
                LoadGame();
                career.Notes.Add(new Career.Note { Text = what + "Reloaded your last save", Good = false, Time = Time.realtimeSinceStartup });
            }
            else { crashNote = what + "No save to reload, so a new game started"; App.NewGame(); }
        }

        static string Speed(double kms) => kms < 1 ? $"{kms * 1000:F0} m/s" : $"{kms:F2} km/s";

        // Through 100×, drive-off flight uses the same multi-body/J2 integrator as powered flight. At 1k× and above,
        // eligible coasts use a labelled dominant-body approximation; switching preserves world position/velocity.
        bool Coasting() => warpIdx >= 3 && ship.Landed == null && ship.RcsInput.sqrMagnitude == 0 &&
            (ship.PlanActive ? ship.Throttle == 0 && (ship.Phase == "in orbit" || ship.Phase == "velocity matched" || ship.Phase == "cruising") : ship.ThrottleCmd == 0);

        string FlightModel => ship.Landed != null ? (ship.AssistedArrival ? "assisted arrival" : "body-fixed contact") :
            Coasting() ? "two-body coast (approximation, 1k×+)" : "integrated multi-body gravity + J2";

        string ArrivalNotice => reference != null && Ship.HasAtmosphere(reference) &&
            (ship.Pos - sys.Pos(reference, jd)).Mag - reference.Radius < Math.Max(300, reference.Radius * 0.1) ?
            Ship.ArrivalPolicy(reference) : null;

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
            // #5: never adopt an unconverged conic; shorter arcs are easier, and failing that the integrator takes over
            D3 r1, v1;
            for (int tries = 0; ; tries++)
            {
                chunk = (jd + chunk / 86400 - jd) * 86400;
                if (chunk <= 0) return 0;
                if (Rails.Propagate(r0, v0, dom.GM, chunk, out r1, out v1)) break;
                if (tries == 20) return 0;
                chunk *= 0.5;
            }
            // #6: stop at the first surface the arc passes through, even if it is outside again by the arc's end
            double hit = FirstSurfaceHit(dom, r0, v0, chunk);
            if (hit >= 0)
            {
                chunk = Math.Max(hit, 1e-3);
                chunk = (jd + chunk / 86400 - jd) * 86400;
                if (!Rails.Propagate(r0, v0, dom.GM, chunk, out r1, out v1)) return 0;
            }
            double jd1 = jd + chunk / 86400;
            ship.Pos = sys.Pos(dom, jd1) + r1;
            ship.Vel = sys.Vel(dom, jd1) + v1;
            ship.Throttle = 0; // nothing fires on a coast arc
            if (!ship.PlanActive) ship.Phase = "coasting";
            ship.Turn(sys, jd1, chunk, dom);
            ship.CheckTouchdown(sys, jd1);
            if (ship.PlanActive && ship.Phase == "cruising") ship.CruiseSeconds += chunk;
            // the autopilot re-checks its plan after each arc and burns again if it has drifted
            if (ship.PlanActive) ship.RecheckPlan(sys, jd1, dom);
            return chunk;
        }

        // The first time within `span` at which the coast arc (r0, v0 about `dom`) meets a surface, or −1. The arc is a
        // conic about `dom`, so that body is found exactly (Rails.FirstCrossing); other nearby bodies move on their
        // own orbits, so the arc is sampled and any chord that enters one is refined by bisection on the distance.
        double FirstSurfaceHit(Body dom, D3 r0, D3 v0, double span)
        {
            // only bodies CheckTouchdown will test (ship.Near), or a found crossing would stop the arc without landing
            double best = dom == sys.Sun || !ship.Near.Contains(dom) ? -1 : Rails.FirstCrossing(r0, v0, dom.GM, InnerRadius(dom), span);
            double limit = best >= 0 ? best : span;
            var vShip = ship.Vel;
            double gDom = dom.GM / r0.Sqr;
            foreach (var b in ship.Near)
            {
                if (b == dom || b == sys.Sun) continue;
                double Rb = InnerRadius(b);
                var rel0 = ship.Pos - sys.Pos(b, jd);
                // can't get there within the arc even with the dominant body's pull added to today's closing speed
                double reach = ((vShip - sys.Vel(b, jd)).Mag + gDom * limit) * limit * 1.5;
                if (rel0.Mag - Rb > reach) continue;
                const int K = 24;
                D3 Rel(double t)
                {
                    Rails.Propagate(r0, v0, dom.GM, t, out var p, out _);
                    double j = jd + t / 86400;
                    return sys.Pos(dom, j) + p - sys.Pos(b, j);
                }
                var a = Rel(0); double ta = 0;
                for (int i = 1; i <= K; i++)
                {
                    double tb = limit * i / K; var c = Rel(tb);
                    // closest point of the chord a→c to the body
                    var d = c - a; double s = d.Sqr > 0 ? Math.Max(0, Math.Min(1, -D3.Dot(a, d) / d.Sqr)) : 0;
                    double tc = ta + s * (tb - ta);
                    if ((a + d * s).Mag < Rb && Rel(tc).Mag < Rb)
                    {
                        double lo = ta, hi = tc;
                        for (int k = 0; k < 60 && hi - lo > 1e-3; k++) { double m = 0.5 * (lo + hi); if (Rel(m).Mag < Rb) hi = m; else lo = m; }
                        limit = hi; best = hi;
                        break;
                    }
                    a = c; ta = tb;
                }
            }
            return best;
        }

        // Strictly inside every point of the touchdown surface, so CheckTouchdown always fires at a found crossing.
        static double InnerRadius(Body b) => b.Radius * Math.Max(b.Shape.x, Math.Max(b.Shape.y, b.Shape.z)) * (1 - b.Flattening) * 0.998;

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
            if (QaCamera()) mode = CamMode.Orbit;
            else if (App.Rig != null)
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
            TerrainBody.Collect();
            float pxPerRad = Cam.pixelHeight / 2f / Mathf.Tan(Cam.fieldOfView * Mathf.Deg2Rad / 2);
            GeometryUtility.CalculateFrustumPlanes(Cam, frustum);
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
                    if (v.lods != null)
                    {
                        float px = v.t.lossyScale.x / Mathf.Max((v.t.position - Cam.transform.position).magnitude, 1e-9f) * pxPerRad;
                        int lod = LodFor(px, v.lod);
                        if (lod != v.lod) { v.lods[v.lod].SetActive(false); v.lods[lod].SetActive(true); v.lod = lod; }
                    }
                }
                else
                {
                    v.t.localScale = Vector3.Scale(b.Shape, new Vector3(1, (float)(1 - b.Flattening), 1)) * s;
                    b.Axes(jd, out var prime, out var east, out var pole);
                    v.t.localRotation = Quaternion.LookRotation(Frame.U(east), Frame.U(pole));
                    if (v.terrain != null && v.mats.Length > 0)
                    {
                        bool chunks = v.t.gameObject.activeInHierarchy && v.terrain.Draw(v.mats[0], camW, bw, prime, pole, east, k, v.t.localPosition,
                            v.t.localRotation, Root.localToWorldMatrix, v.t.worldToLocalMatrix, v.t.gameObject.layer, pxPerRad, frustum);
                        v.r.enabled = !chunks;
                    }
                }
                float irr = ShipLighting.Irradiance((sunW - bw).Mag);
                foreach (var m in v.mats) { m.SetVector("_SunDir", sunDir); m.SetVector("_FOWSunDir", sunDir); m.SetFloat("_SunIrr", irr); }
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
            lighting?.Update(sys, jd, ship.Pos, sunW, dt, shipT, shipLen * shipT.lossyScale.x,
                             shipT.TransformPoint(new Vector3(0, 0, -shipLen * 0.55f)), thr);

            PlaceRings(sunW);
            DrawOrbits();
            beltView.Draw(jd, camW, ship.Pos, sunW, Cam, Map);
            LateCareer();
        }


        void DrawOrbits()
        {
            pts.Clear(); cols.Clear(); targetLine.Clear();
            var own = OwnBody();
            ownSegs = 0;
            // the map with a target set: the rest of the orbits step back
            float rest = MapView && ship.Target != null ? 0.5f : 1f;
            float fade = Mathf.Clamp01((float)(Math.Log10(Math.Max(camDist, 1)) - 3) / 4f);
            foreach (var b in sys.Bodies)
            {
                if (b.Parent == null) continue;
                bool isMoon = b.Parent != sys.Sun;
                // moons' orbits only when the camera is near enough to see them as more than a dot
                var parentW = isMoon ? sys.Pos(b.Parent, jd) : D3.Zero;
                bool tgt = ship.Target == b;
                if (isMoon && (parentW - camW).Mag > b.A * 400) continue;
                if (isMoon && !tgt && b.Radius < 50) continue; // hundreds of small moons: their orbits would be a hairball
                bool mine = b == own && !tgt;
                var c = tgt ? new Color(1f, 0.71f, 0.28f, 0.75f) : mine ? OwnOrbitCol : isMoon ? new Color(0.5f, 0.6f, 0.85f, 0.35f) : new Color(0.45f, 0.55f, 0.85f, 0.4f);
                // planet orbits fade out when the camera is in close: from a ship they'd be streaks across the screen
                if (!isMoon) c.a *= Mathf.Lerp(tgt || mine ? 0.3f : 0f, 1f, fade);
                if (b.Asteroid && !tgt && !mine) c.a *= 0.25f; // two dozen named asteroids: keep their orbits in the background
                if (!tgt && !mine) c.a *= rest;
                if (b.Path != null)
                {
                    // a craft on its own course has no orbit: its line is the course itself (QA-065)
                    if (!CoursePoints(b, line)) continue;
                }
                else
                {
                    line.Clear();
                    const int N = 180;
                    double period = 360 / Math.Max(Math.Abs(b.dL), 1e-9); // days
                    for (int i = 0; i <= N; i++)
                    {
                        double t = jd + period * i / N;
                        // the real (perturbed) path relative to the parent, drawn around where the parent is now
                        line.Add(parentW + (b.Cheb == null && isMoon ? b.Kepler(t) : sys.PosAt(b, t) - (isMoon ? sys.PosAt(b.Parent, t) : D3.Zero)));
                    }
                }
                Vector3 prev = default;
                for (int i = 0; i < line.Count; i++)
                {
                    var q = Map(line[i], out _);
                    if (i > 0) { pts.Add(prev); pts.Add(q); cols.Add(c); cols.Add(c); if (mine) ownSegs++; }
                    prev = q;
                }
                if (tgt) { targetLine.Clear(); targetLine.AddRange(line); }
            }
            DrawMapLines(own, Mathf.Lerp(0.3f, 1f, fade));
            AddThreatRings(fade);
            // photo mode and F2 (HUD hidden) take the orbit and course lines out of the picture too
            if (App.HideGui || !App.ShowUI) { pts.Clear(); cols.Clear(); }
            OrbitSegments = pts.Count / 2;
            Gfx.SetLines(orbitMesh, pts, cols);
            orbitMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e7f);
        }

        readonly List<D3> line = new List<D3>(), targetLine = new List<D3>();
        public int OrbitSegments { get; private set; }   // line segments drawn last frame (checks)

        // An intercept runner's course from its departure to its arrival, in world km. Other path bodies (the
        // practice beacon) get no line. Treated as an orbit, a runner sampled its path ~1e9 days out, where it sits
        // at its destination port at scattered dates: dozens of chords across that port's orbit.
        bool CoursePoints(Body b, List<D3> outW)
        {
            outW.Clear();
            var job = career?.Active.Find(x => x.Kind == JobKind.Intercept && x.To == b.Name);
            if (job == null || !(job.T1 > job.T0)) return false;
            const int N = 120;
            for (int i = 0; i <= N; i++) outW.Add(sys.PosAt(b, job.T0 + (job.T1 - job.T0) * i / N));
            return true;
        }

        // review checks: the line last drawn for the target, in world km: its segments, length and longest segment's share
        public string TestTargetLineReport(out double longestShare)
        {
            longestShare = -1;
            if (targetLine.Count < 2) return "no line";
            double len = 0, longest = 0;
            for (int i = 1; i < targetLine.Count; i++) { double s = (targetLine[i] - targetLine[i - 1]).Mag; len += s; longest = Math.Max(longest, s); }
            longestShare = longest / Math.Max(len, 1e-9);
            return $"{targetLine.Count - 1} segments, {len / Frame.AU:F2} AU long, longest {longestShare * 100:F1} %";
        }

    }
}
