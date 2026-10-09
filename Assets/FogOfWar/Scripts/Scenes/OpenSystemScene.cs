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
        readonly StarGlare starGlare = new StarGlare();
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
                    art.TryGetValue(b.Name, out var info);
                    bool measuredShape = info?.source?.StartsWith("shape model", StringComparison.Ordinal) == true;
                    if (info != null && info.meanRadiusKm > 0)
                    { b.Radius = info.meanRadiusKm; b.RadiusSource = measuredShape ? "shape asset volume-equivalent radius" : "catalogue triaxial-diameter radius"; }
                    b.ShapeSource = modelPath.StartsWith("Asteroids/", StringComparison.Ordinal) ?
                        "borrowed procedural asteroid mesh (unmeasured shape)" : measuredShape ? info.source :
                        info?.source?.StartsWith("procedural", StringComparison.Ordinal) == true ?
                        "procedural mesh seeded by name (unmeasured shape)" : "body mesh (source unavailable)";
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
            RefreshHullModel(); // the new career's hull: the scene object lives across New game, so career was the old one until now

        }

        Belt belt;
        BeltView beltView;
        Texture2D beltHist;
        bool showHist;

        // the ship's holder, its lighting and plume; the model itself is the fitted hull's (RefreshHullModel, after InitCareer)
        void BuildShip()
        {
            shipT = new GameObject("Ship").transform;
            shipT.SetParent(Root, false);
            shipModel = null; hullModel = null; engine = null; radiators.Clear(); hullMats.Clear(); fold = 0;
            lighting = new ShipLighting(null);

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
            DropRaiderModel(); // its materials are the chase's, not the scene's
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
        public bool QaFreeze; // provenance captures may change the displayed epoch without ticking ship or career state

        public override void Tick(float dt)
        {
            if (QaFreeze) return;
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
            CapWarp();
            TrackRoute();
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
                    ship.Step(sys, jd, simDt, reference); // riding the surface: no dynamics to resolve (a raider's chase ends at the career tick)
                    jd += simDt / 86400; simDt = 0;
                    break;
                }
                D3 p0 = ship.Pos, v0 = ship.Vel; double jd0 = jd; // the player's step, for a raider to fly the same span
                if (Coasting())
                {
                    double used = Coast(simDt);
                    if (used > 0)
                    {
                        jd += used / 86400; simDt -= used;
                        profSteps++;
                        Chase(jd0, p0, v0);
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
                Chase(jd0, p0, v0);
                simDt -= h;
                if (simDt < 1e-4) simDt = 0; // finer than jd can resolve
            }
            // the player's warp stays selected; when the frame budget runs out, report the rate actually achieved
            double achieved = requested - Math.Max(simDt, 0);
            frameWarp = dt > 0 ? achieved / dt : 0;
            effectiveWarp = effectiveWarp * 0.9 + 0.1 * frameWarp;
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
            RefreshHullModel(); // a no-op unless a refit, a load or a new career changed the hull's model
            shipModel.gameObject.SetActive(mode != CamMode.Cockpit);
            LateCockpit(mode == CamMode.Cockpit);

            var sunW = sys.Pos(sys.Sun, jd);
            TerrainBody.Collect();
            float pxPerRad = Cam.pixelHeight / 2f / Mathf.Tan(Cam.fieldOfView * Mathf.Deg2Rad / 2);
            GeometryUtility.CalculateFrustumPlanes(Cam, frustum);
            starGlare.Begin(Cam);
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
                // the map is a diagram, and its Sun-centred camera would hold the glare on for good
                if (!MapView) starGlare.AddBody(v.t.position, b.Radius, bw, camW, sunW, b == sys.Sun);
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
            float realLen = HullKm(); // km
            shipT.localScale = Vector3.one * (float)(realLen * ks / shipLen);
            Shader.SetGlobalVector("_FOWSunDir", Frame.U((sunW - ship.Pos).Norm));
            float thr = (float)ship.Throttle;
            if (engine != null) engine.localScale = thr > 0 ? engineScale * (1 + 0.15f * Mathf.Sin(Time.time * 37f)) : engineScale * 0.0001f;
            FoldRadiators();
            plume.gameObject.SetActive(thr > 0.01f);
            if (thr > 0.01f)
            {
                float L = shipLen * Mathf.Min(0.5f + 0.9f * Mathf.Sqrt(thr * (float)ship.AccelG / 3f), 1.6f);
                plume.localScale = new Vector3(shipLen * 0.06f, shipLen * 0.06f, L);
                plume.localPosition = new Vector3(0, 0, -shipLen * 0.5f - L * 0.9f);
                float behind = Mathf.Clamp01(Vector3.Dot((shipT.position - Cam.transform.position).normalized, shipT.forward));
                plumeMat.SetFloat("_Intensity", (0.5f + 0.9f * thr) * Mathf.Lerp(1f, 0.3f, behind));
            }
            if (lighting != null) lighting.LengthM = realLen * 1000;
            lighting?.Update(sys, jd, ship.Pos, sunW, dt, shipT, shipLen * shipT.lossyScale.x,
                             shipT.TransformPoint(new Vector3(0, 0, -shipLen * 0.55f)), thr);
            starGlare.End(dt, lighting != null ? lighting.Exposure : 1f);

            PlaceRings(sunW);
            DrawOrbits();
            beltView.Draw(jd, camW, ship.Pos, sunW, Cam, Map, MapView);
            LateCareer();
        }

    }
}
