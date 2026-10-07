using System;
using System.Collections.Generic;
using FogOfWar.OpenWorld;
using UnityEngine;
using A = FogOfWar.Controls.Action;
using Ctl = FogOfWar.Controls;

namespace FogOfWar
{
    // Open System: pilot input, the ship and nav-computer panel, the flight HUD, labels, readouts and save games.
    public partial class OpenSystemScene
    {
        static readonly string[] HoldNames = { "Off", "Prograde", "Retrograde", "Normal", "Anti-normal", "Radial out", "Radial in", "Target", "Anti-target", "Flip 180°" };
        // indexed by (int)Plan; the pickers list their plans by hand, so Pursue (a pirate's) is never offered
        static readonly string[] PlanShort = { "", "Fast", "Match velocity", "Hold station", "Pursue" };
        static readonly string[] PlanNames = { "", "Fast (full burn)", "Match velocity", "Hold station", "Pursue" };

        // ---------------------------------------------------------------- pilot input (polled every frame)

        void ReadInput(float dt)
        {
            bool live = !App.MenuOpen && !App.HelpOpen && App.Rig == null; // photo mode flies its own camera
            ship.RotInput = live ? new Vector3(Ctl.Axis(A.PitchUp, A.PitchDown), Ctl.Axis(A.YawLeft, A.YawRight), Ctl.Axis(A.RollRight, A.RollLeft)) : Vector3.zero;
            ship.RcsInput = live ? new Vector3(Ctl.Axis(A.RcsLeft, A.RcsRight), Ctl.Axis(A.RcsDown, A.RcsUp), Ctl.Axis(A.RcsBack, A.RcsForward)) : Vector3.zero;
            if (!live) return;

            double th = ship.ThrottleCmd;
            if (Ctl.Held(A.ThrottleUp)) th += dt * 0.5;
            if (Ctl.Held(A.ThrottleDown)) th -= dt * 0.5;
            if (Ctl.Pressed(A.ThrottleFull)) th = 1;
            if (Ctl.Pressed(A.ThrottleCut)) CutDrive();
            else ManualThrottle(th);

            if (Ctl.Pressed(A.FlightAssist)) ship.FlightAssist = !ship.FlightAssist;
            if (Ctl.Pressed(A.Floodlights) && lighting != null) lighting.Floods = !lighting.Floods;
            if (Ctl.Pressed(A.Flip)) Flip();
            if (Ctl.Pressed(A.VelocityRef)) ToggleVelRef();
            if (Ctl.Pressed(A.Engage)) TogglePlan();
            if (Ctl.Pressed(A.NextTarget)) NextTarget();
            if (Ctl.Pressed(A.WarpUp)) warpIdx = Math.Min(Warps.Length - 1, warpIdx + 1);
            if (Ctl.Pressed(A.WarpDown)) warpIdx = Math.Max(0, warpIdx - 1);
            if (Ctl.Pressed(A.Map)) ToggleMap();
            if (Ctl.Pressed(A.CameraMode)) CycleCamera();
            if (Ctl.Pressed(A.QuickSave)) SaveGame();
            if (!Ctl.Locked)
                for (int i = 0; i < 9; i++)
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i)) SetHold((Hold)i);
            // steering in the chase view brings the camera back behind the ship: seen from the bow, yaw and pitch look mirrored
            if (ship.RotInput.sqrMagnitude > 0 && pivot == Pivot.Ship && camMode == CamMode.Chase)
            {
                chaseYaw = Mathf.MoveTowardsAngle(chaseYaw, 0, 240 * dt);
                chasePitch = Mathf.MoveTowards(chasePitch, ChasePitch0, 120 * dt);
            }
            // warping with the stick or RCS held makes no sense: drop to 1× while flying by hand
            if (ship.ManualInput && warpIdx > 0) warpIdx = 0;
        }

        // The pilot sets the drive (keys or slider): a change takes over from the nav computer and drops time
        // compression to 1×, so a manual burn never starts at high warp (#15).
        void ManualThrottle(double th)
        {
            th = Math.Max(0, Math.Min(1, th));
            if (th == ship.ThrottleCmd) return;
            ship.ThrottleCmd = th;
            if (ship.PlanActive) { ship.Plan = Plan.None; ship.Phase = "pilot override"; }
            warpIdx = 0;
        }

        // Cut: the drive stops now, whoever was flying. Cancels the nav plan too (its burns don't go through ThrottleCmd)
        // and zeroes the applied throttle, so no powered step runs on the old value.
        void CutDrive()
        {
            bool takeover = ship.PlanActive || ship.ThrottleCmd > 0 || ship.Throttle > 0;
            ship.ThrottleCmd = 0; ship.Throttle = 0;
            if (ship.PlanActive) { ship.Plan = Plan.None; ship.Phase = "drive cut"; }
            if (takeover) warpIdx = 0;
        }

        void Flip() { ship.Plan = Plan.None; ship.CustomDir = -ship.Heading; ship.Hold = Hold.Custom; }

        void SetHold(Hold h) { ship.Hold = h; if (h != Hold.Off) ship.Plan = Plan.None; }

        // #27: velocity holds, the speed readout and the approach aid measure against the orbit (the dominant body) or
        // the target. Switching needs a target; losing the target falls back to the orbit.
        void ToggleVelRef() => ship.VelRef = ship.VelRef == null && ship.Target != null ? ship.Target : null;
        Body VelRefBody => ship.VelRef ?? reference;

        // Stopping against the target at full drive (or RCS, if that is all there is) after a flip: no gravity, the mass
        // now, the drive limit now. Distance and time scale with 1/a, so a lower g limit shows up at once.
        void StopEstimate(D3 vr, out double a, out double t, out double d, out string by)
        {
            bool drive = !ship.Dry && ship.AMax > 0;
            a = drive ? ship.AMax : ship.Dry ? 0 : Ship.RcsAccel;
            by = drive ? Fmt.Num(ship.MaxG, 1) + " g drive" : ship.Dry ? "no fuel" : "RCS";
            double v = vr.Mag;
            t = a > 0 ? v / a : double.PositiveInfinity;
            d = a > 0 ? v * v / (2 * a) : double.PositiveInfinity;
        }

        static string Spd(double kms) => Math.Abs(kms) < 1 ? (Math.Abs(kms) < 5e-5 ? "0" : Fmt.Num(kms * 1000, Math.Abs(kms) < 0.01 ? 1 : 0)) + " m/s" : Fmt.Num(kms, 2) + " km/s";

        void TogglePlan()
        {
            if (ship.PlanActive) { ship.Plan = Plan.None; ship.Throttle = 0; ship.Phase = "plan aborted"; return; }
            if (ship.Target == null) return;
            // QA-061: a brachistochrone the tank can't finish asks once; engaging again within a few seconds goes anyway
            if (selectedPlan == Plan.Brachistochrone && !ship.UnlimitedFuel)
            {
                PlanTrip(out _, out double dv, out _);
                if (DvShort(dv) && !(shortArmed == ship.Target && Time.unscaledTime < shortUntil))
                {
                    shortArmed = ship.Target; shortUntil = Time.unscaledTime + 8;
                    ship.Phase = "fuel short: engage again to go anyway";
                    hudShell?.Toast($"Not enough fuel to reach {ship.Target.Name}",
                        $"Needs about {ship.Mass * (1 - Math.Exp(-dv * DvMargin / Ship.Ve)):N1} t with margin, {ship.Fuel:N1} t aboard. Engage again to go anyway.", 6f, "bad");
                    return;
                }
            }
            shortArmed = null;
            ship.Plan = selectedPlan;
            ship.Landed = null; ship.ThrottleCmd = 0; ship.Hold = Hold.Off;
            if (selectedPlan == Plan.HoldStation) ship.StationOffset = ship.Pos - sys.Pos(ship.Target, jd);
        }

        Plan selectedPlan = Plan.Brachistochrone;
        Body shortArmed; float shortUntil;

        // kept for QA scripts
        void TogglePlot() { selectedPlan = Plan.Brachistochrone; TogglePlan(); }

        void CycleCamera()
        {
            if (pivot != Pivot.Ship) { SetPivot(Pivot.Ship, 0.16); camMode = CamMode.Chase; return; }
            camMode = camMode == CamMode.Chase ? CamMode.Orbit : camMode == CamMode.Orbit ? CamMode.Cockpit : CamMode.Chase;
            ResetLook();
        }

        void ResetLook() { lookYaw = lookPitch = 0; chaseYaw = 0; chasePitch = ChasePitch0; }

        public override bool HandleDrag(Vector2 delta, int button)
        {
            var mode = pivot == Pivot.Ship ? camMode : CamMode.Orbit;
            if (mode == CamMode.Chase) { chaseYaw -= delta.x * 0.25f; chasePitch = Mathf.Clamp(chasePitch + delta.y * 0.25f, -89, 89); }
            else if (mode == CamMode.Cockpit) { lookYaw += delta.x * 0.2f; lookPitch = Mathf.Clamp(lookPitch + delta.y * 0.2f, -80, 80); }
            else { yaw -= delta.x * 0.25f; pitch = Mathf.Clamp(pitch + delta.y * 0.25f, -89, 89); }
            return true;
        }

        public override bool HandleZoom(float wheel)
        {
            if (pivot == Pivot.Ship && camMode == CamMode.Cockpit) { Cam.fieldOfView = Mathf.Clamp(Cam.fieldOfView * Mathf.Exp(wheel * 0.05f), 10, 90); return true; }
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

        void NextTarget() => SetTarget(targets[(targetIdx + 1) % targets.Count]);

        void SetTarget(Body b)
        {
            if (ship.PlanActive && ship.Target != b) { ship.Plan = Plan.None; ship.Phase = "plan aborted (new target)"; }
            ship.Target = b;
            targetIdx = targets.IndexOf(b);
            if (pivot == Pivot.Target) camDist = b.Radius * 4;
        }

        void ToggleMap()
        {
            if (pivot == Pivot.Sun) SetPivot(Pivot.Ship, 0.16);
            else { SetPivot(Pivot.Sun, 6 * Frame.AU); pitch = 65; }
        }

        // ---------------------------------------------------------------- HUD and labels

        public override void OnGUIOverlay()
        {
            if (Prof) swGui.Start();
            try { if (GameSettings.ShowLabels) { DrawLabels(); DrawTraffic(); } if (!HasHud || !App.ShellActive) { DrawHud(); DrawCareerNotes(); } } finally { if (Prof) swGui.Stop(); }
        }

        GUIStyle labelStyle, labelOn, hudStyle, hudBig;
        readonly List<(Vector2 c, float r, float d)> occluders = new List<(Vector2, float, float)>();

        void Styles()
        {
            if (labelStyle != null) return;
            labelStyle = new GUIStyle { font = Resources.Load<Font>("UI/Fonts/Barlow-Medium"), fontSize = 13, normal = { textColor = new Color(0.75f, 0.8f, 0.95f, 0.85f) } };
            labelOn = new GUIStyle(labelStyle) { fontStyle = FontStyle.Bold, normal = { textColor = PanelUI.Accent } };
            hudStyle = new GUIStyle { fontSize = 13, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.85f, 0.9f, 1f, 0.95f) } };
            hudBig = new GUIStyle(hudStyle) { fontSize = 18, fontStyle = FontStyle.Bold };
        }

        // Screen point (GUI units) of a direction from the camera, or null if behind it.
        Vector2? ScreenDir(Vector3 dirRender, float ui)
        {
            var sp = Cam.WorldToScreenPoint(dirRender.normalized * 10f);
            if (sp.z <= 0) return null;
            return new Vector2(sp.x / ui, (UnityEngine.Screen.height - sp.y) / ui);
        }

        void Marker(Vector2? p, string glyph, Color c, string tag)
        {
            if (!p.HasValue) return;
            var g = p.Value;
            GUI.color = c;
            GUI.Label(new Rect(g.x - 20, g.y - 12, 40, 24), glyph, hudBig);
            if (tag != null) GUI.Label(new Rect(g.x - 60, g.y + 10, 120, 16), tag, hudStyle);
            GUI.color = Color.white;
        }

        void DrawHud()
        {
            Styles();
            float ui = App.UiScale;
            float W = UnityEngine.Screen.width / ui, H = UnityEngine.Screen.height / ui;
            if (pivot == Pivot.Ship)
            {
                // flight markers: nose, velocity vs the dominant body, target direction
                var v = ship.Vel - sys.Vel(reference, jd);
                if (v.Sqr > 1e-10)
                {
                    Marker(ScreenDir(Frame.U(v), ui), "O", new Color(0.6f, 1f, 0.6f), "prograde");
                    Marker(ScreenDir(-Frame.U(v), ui), "X", new Color(1f, 0.6f, 0.5f), "retrograde");
                }
                Marker(ScreenDir(Frame.U(ship.Heading), ui), "+", Color.white, null);
                if (ship.Target != null) Marker(ScreenDir(Frame.U(sys.Pos(ship.Target, jd) - camW), ui), "[ ]", PanelUI.Accent, null);
            }

            // bottom-centre flight strip
            float free = W - App.PanelWidth - 32;
            float w = Mathf.Min(520, free - 24), x = (free - w) / 2 + 8, y = H - 98;
            GUI.color = new Color(0, 0, 0, 0.45f);
            GUI.DrawTexture(new Rect(x, y, w, 62), Texture2D.whiteTexture);
            GUI.color = Color.white;
            // throttle bar: commanded (outline) and applied (fill)
            var bar = new Rect(x + 10, y + 8, w - 20, 10);
            GUI.color = new Color(1, 1, 1, 0.15f); GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.71f, 0.28f, 0.9f); GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * (float)ship.Throttle, bar.height), Texture2D.whiteTexture);
            GUI.color = Color.white; GUI.DrawTexture(new Rect(bar.x + bar.width * (float)ship.ThrottleCmd - 1, bar.y - 3, 2, bar.height + 6), Texture2D.whiteTexture);
            var vRef = (ship.Vel - sys.Vel(reference, jd)).Mag;
            string l1 = $"DRIVE {Fmt.Num(ship.Throttle * ship.MaxG, 2)} g of {Fmt.Num(ship.MaxG, 1)} g   ·   {Fmt.Num(vRef, 3)} km/s vs {reference.Name}   ·   {WarpNames[warpIdx]}";
            string l2 = $"ASSIST {(ship.FlightAssist ? "ON" : "OFF")}   ·   HOLD {HoldNames[Math.Min((int)ship.Hold, HoldNames.Length - 1)]}   ·   " +
                        (ship.PlanActive ? $"NAV {PlanNames[(int)ship.Plan]}: {ship.Phase}" : $"NAV idle ({ship.Phase})") + $"   ·   CAM {(pivot == Pivot.Ship ? camMode.ToString() : pivot.ToString())}";
            GUI.Label(new Rect(x, y + 22, w, 18), l1, hudStyle);
            GUI.Label(new Rect(x, y + 40, w, 18), l2, hudStyle);
        }

        void DrawLabels()
        {
            Styles();
            float ui = App.UiScale;
            float pxPerRad = UnityEngine.Screen.height / 2f / Mathf.Tan(Cam.fieldOfView * Mathf.Deg2Rad / 2);
            var placed = new List<Vector2>();
            // discs of bodies big enough on screen to hide labels of things behind them
            occluders.Clear();
            foreach (var v in vis)
            {
                var p = v.t.localPosition; float d = p.magnitude;
                var sp = Cam.WorldToScreenPoint(p);
                if (sp.z <= 0 || d < 1e-6f) continue;
                float rpx = v.t.localScale.y / d * pxPerRad / ui;
                if (rpx > 4) occluders.Add((new Vector2(sp.x / ui, (UnityEngine.Screen.height - sp.y) / ui), rpx, d));
            }

            // ship first so it wins overlaps
            if (!(pivot == Pivot.Ship && camMode == CamMode.Cockpit)) Label("You", shipT.localPosition, 0.05, null, ui, pxPerRad, placed, true);
            foreach (var v in vis) Label(v.b.Name, v.t.localPosition, v.t.localScale.y, v.b, ui, pxPerRad, placed, false);
        }

        void Label(string name, Vector3 renderPos, double renderRadius, Body b, float ui, float pxPerRad, List<Vector2> placed, bool force)
        {
            float dist = renderPos.magnitude;
            if (dist < 1e-6f) return;
            var sp = Cam.WorldToScreenPoint(renderPos);
            if (sp.z <= 0 || !CockpitShows(sp)) return;
            float rpx = (float)renderRadius / dist * pxPerRad;
            bool tgt = b != null && b == ship.Target;
            if (rpx > 40 && !tgt) return; // big enough to recognise
            // hundreds of small moons: name them only when the camera is close to them
            if (b != null && !tgt && b.Parent != null && b.Parent != sys.Sun && b.Radius < 100 && dist > 2e4 * renderRadius) return;
            var g = new Vector2(sp.x / ui, (UnityEngine.Screen.height - sp.y) / ui);
            if (App.UnderPanel(g)) return; // labels stay off the panels
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

        // ---------------------------------------------------------------- readouts

        public override void Readouts(List<Readout> list)
        {
            CareerReadouts(list);
            var date = Frame.UtcFromTdb(jd);
            list.Add(new Readout("Date (UTC)", date.ToString("yyyy-MM-dd HH:mm")));
            bool limited = Time.realtimeSinceStartup - limitedAt < 2f;
            list.Add(new Readout("Time compression", WarpNames[warpIdx] + (limited ? $" (limited: running at {Fmt.Num(effectiveWarp, 0)}×)" : ""), limited ? Tone.Bad : Tone.Normal));
            list.Add(new Readout("Flight model", FlightModel, Coasting() ? Tone.Bad : Tone.Normal));
            if (ArrivalNotice != null) list.Add(new Readout("Arrival boundary", ArrivalNotice, Tone.Bad));
            var refPos = sys.Pos(reference, jd);
            double alt = (ship.Pos - refPos).Mag - reference.Radius;
            list.Add(new Readout("Near", reference.Name + ", " + Km(alt) + (reference == sys.Sun ? " out" : " up")));
            list.Add(new Readout("Speed vs " + reference.Name, Fmt.Num((ship.Vel - sys.Vel(reference, jd)).Mag, 3) + " km/s"));
            list.Add(new Readout("From the Sun", Fmt.Num(ship.Pos.Mag / Frame.AU, 3) + " AU"));
            list.Add(new Readout("Drive", ship.Throttle > 0 ? Fmt.Num(ship.Throttle * ship.MaxG, 2) + " g" : "off", ship.Throttle > 0 ? Tone.Hot : Tone.Normal));
            list.Add(new Readout("Rotation", Fmt.Num(ship.AngVel.magnitude * Mathf.Rad2Deg, 1) + " °/s"));
            list.Add(new Readout("Δv spent", Fmt.Num(ship.DvUsed, 3) + " km/s"));
            // jet power P = ½·m·a·vₑ with the live mass and the D–He³-class exhaust; no known physics sheds that heat
            if (ship.Throttle > 0) list.Add(new Readout("Jet power (fictional efficiency)", Fmt.Num(0.5 * ship.Mass * 1e3 * ship.Throttle * ship.AMax * 1e3 * Ship.Ve * 1e3 / 1e12, 3) + " TW", Tone.Bad));
            if (sys.Approximate(jd)) list.Add(new Readout("Ephemeris", "approximate (JPL fits: Jul 2000 – Jun 2099)", Tone.Bad));
            if (ship.Target != null)
            {
                TargetRel(out var r, out var vr, out double closing);
                list.Add(new Readout("Target", ship.Target.Name + (ship.Target.SizeEstimated ? " (size unknown, shown 2 km)" : ""), Tone.Hot));
                if (ship.Target.Name == "Titan") list.Add(new Readout("Titan surface", "shown faintly through the haze (938 nm map)", Tone.Bad));
                list.Add(new Readout("Distance", Km(r.Mag - ship.Target.Radius)));
                list.Add(new Readout("Relative speed", Fmt.Num(vr.Mag, 3) + " km/s"));
                list.Add(new Readout("Closing speed", Fmt.Num(closing, 3) + " km/s", closing > 0 ? Tone.Cold : Tone.Normal));
                list.Add(new Readout("Sideways drift", Spd((vr - r.Norm * closing).Mag)));
                StopEstimate(vr, out var aStop, out var tStop, out var dStop, out var by);
                bool late = closing > 0 && dStop > r.Mag - ship.Target.Radius;
                list.Add(new Readout("Stop (flip now, " + by + ", no gravity)", aStop > 0 ? Dur(tStop) + ", " + Km(dStop) + (late ? ": too fast" : "") : "can't", late ? Tone.Bad : Tone.Normal));
                list.Add(new Readout("Velocity reference (" + Ctl.Keys(A.VelocityRef) + ")", VelRefBody.Name));
            }
            double rAu = ship.Pos.Mag / Frame.AU;
            if (rAu > 1.7 && rAu < 5.6 && beltView.NearestIndex >= 0)
                list.Add(new Readout("Nearest belt rock (1 % sample)", Km(beltView.NearestKm) + ", " + Fmt.Num(belt.RadiusKm[beltView.NearestIndex] * 2, 1) + " km wide"));
            if (faintRings) list.Add(new Readout("Faint rings", "brightened ×1000 (not real)", Tone.Bad));
            list.Add(new Readout("Nav computer", ship.PlanActive ? ship.Phase : ship.Landed != null ? ship.Phase : "idle", ship.PlanActive ? Tone.Hot : Tone.Normal));
            if (ship.PlanActive) list.Add(new Readout("Plan", PlanShort[(int)ship.Plan]));
        }

        // Fmt.Num writes powers of ten from 10^6 up and under 10^-3; a flight readout wants "4,170,345 km" and "0 m"
        static string Whole(double x)
        {
            double r = Math.Round(x);
            return (r == 0 ? 0 : r).ToString("#,0", System.Globalization.CultureInfo.InvariantCulture);
        }

        static string Km(double km)
        {
            double a = Math.Abs(km);
            return a > 0.05 * Frame.AU ? Fmt.Num(km / Frame.AU, 3) + " AU" : a >= 1 ? Whole(km) + " km" : Whole(km * 1000) + " m";
        }

        static string Dur(double s)
        {
            if (s < 120) return Whole(s) + " s";
            if (s < 7200) return Fmt.Num(s / 60, 1) + " min";
            if (s < 172800) return Fmt.Num(s / 3600, 1) + " h";
            return Fmt.Num(s / 86400, 1) + " days";
        }

        // ---------------------------------------------------------------- panel

        public override void Controls(PanelUI ui)
        {
            CareerPanel(ui);
            // --- ship
            GUILayout.Label("Throttle (" + Ctl.Keys(A.ThrottleUp) + " / " + Ctl.Keys(A.ThrottleDown) + ", " + Ctl.Keys(A.ThrottleFull) + " full, " + Ctl.Keys(A.ThrottleCut) + " cut)", ui.label);
            float th = GUILayout.HorizontalSlider((float)ship.ThrottleCmd, 0, 1);
            if (Mathf.Abs(th - (float)ship.ThrottleCmd) > 1e-4f) ManualThrottle(th);
            GUILayout.Space(6);
            GUILayout.Label("Drive limit", ui.label);
            var gl = new string[Gs.Length];
            for (int i = 0; i < Gs.Length; i++) gl[i] = (Math.Abs(ship.MaxG - Gs[i]) < 1e-6 ? "● " : "") + Fmt.Num(Gs[i], 1) + " g" + (Gs[i] >= 10 ? " (juice: fictional)" : "");
            int gi = ui.Buttons(gl);
            if (gi >= 0) ship.MaxG = Gs[gi];
            ship.FlightAssist = ui.Toggle("Flight assist (" + Ctl.Keys(A.FlightAssist) + "): the stick sets the turn rate; release it to stop", ship.FlightAssist);
            int fb = ui.Buttons("Flip 180° (" + Ctl.Keys(A.Flip) + ")", "Kill rotation");
            if (fb == 0) Flip();
            if (fb == 1) { ship.AngVel = Vector3.zero; ship.Hold = Hold.Off; }

            GUILayout.Label("Attitude hold (keys 1–9)", ui.label);
            var hl = new string[9];
            for (int i = 0; i < 9; i++) hl[i] = ((int)ship.Hold == i ? "● " : "") + HoldNames[i];
            int hi = ui.Buttons(hl);
            if (hi >= 0) SetHold((Hold)hi);

            // --- nav computer
            ui.Section("Nav computer");
            if (ship.Target != null)
            {
                var tp = sys.Pos(ship.Target, jd);
                double d = Math.Max((tp - ship.Pos).Mag - Ship.Standoff(ship.Target), 0);
                GUILayout.Label($"Target: {ship.Target.Name}", ui.label);
                string brach = BrachLine(d, out bool dvShort).Replace("\n", ", ");
                GUILayout.Label(brach, dvShort ? ui.label : ui.note);
                if (ship.Target.Parent == sys.Sun && reference == sys.Sun || ship.Target.Parent == sys.Sun)
                {
                    var (dv, t) = Ship.Hohmann(ship.Pos.Mag, tp.Mag, sys.Sun.GM);
                    GUILayout.Label($"Economy reference (Hohmann, circular orbits): {Dur(t)}, Δv ≈ {Fmt.Num(dv, 1)} km/s", ui.note);
                }
            }
            else GUILayout.Label("No target: pick one below or click a label.", ui.label);
            var pl = new[] { Plan.Brachistochrone, Plan.MatchVelocity, Plan.HoldStation };
            int pi = ui.Buttons(Array.ConvertAll(pl, p => (selectedPlan == p ? "● " : "") + PlanNames[(int)p]));
            if (pi >= 0) selectedPlan = pl[pi];
            int eb = ui.Buttons(ship.PlanActive ? "Abort plan (" + Ctl.Keys(A.Engage) + ")" : "Engage plan (" + Ctl.Keys(A.Engage) + ")");
            if (eb == 0) TogglePlan();

            // --- time and view
            ui.Section("Time and view");
            var wl = new string[Warps.Length];
            for (int i = 0; i < Warps.Length; i++) wl[i] = (i == warpIdx ? "● " : "") + WarpNames[i];
            int wi = ui.Buttons(wl);
            if (wi >= 0) warpIdx = wi;
            int ci = ui.Buttons((pivot == Pivot.Ship && camMode == CamMode.Chase ? "● " : "") + "Chase", (pivot == Pivot.Ship && camMode == CamMode.Orbit ? "● " : "") + "Orbit ship",
                (pivot == Pivot.Ship && camMode == CamMode.Cockpit ? "● " : "") + "Cockpit", (pivot == Pivot.Target ? "● " : "") + "Target", (pivot == Pivot.Sun ? "● " : "") + "System map");
            if (ci >= 0 && ci <= 2) { SetPivot(Pivot.Ship, 0.16); camMode = (CamMode)(ci == 0 ? 0 : ci == 1 ? 1 : 2); ResetLook(); Cam.fieldOfView = 55; }
            if (ci == 3 && ship.Target != null) SetPivot(Pivot.Target, ship.Target.Radius * 4);
            if (ci == 4) { SetPivot(Pivot.Sun, 6 * Frame.AU); pitch = 65; }
            beltView.Visible = ui.Toggle("Show the asteroid belt (a 1 % sample: real rocks are ~1 million km apart)", beltView.Visible);
            showHist = ui.Toggle("Belt semi-major axis histogram", showHist);
            if (showHist)
            {
                ui.Image(beltHist, 90);
                GUILayout.Label("1.6 → 5.6 AU. Kirkwood gaps (Jupiter resonances 4:1, 3:1, 5:2, 7:3, 2:1) at 2.06, 2.50, 2.82, 2.96, 3.28 AU; Hildas at 3.97 (3:2); Trojans at 5.2 (1:1). The gaps are in orbit size, so they don't show as empty rings in space.", ui.note);
            }
            faintRings = ui.Toggle("Brighten faint rings ×1000 (Jupiter, Uranus, Neptune)", faintRings);

            // --- targets
            ui.Section("Targets");
            var planets = sys.Bodies.FindAll(x => x.Parent == sys.Sun && !x.Asteroid && !Traffic.Is(x));
            var sel = ship.Target == null ? null : ship.Target.Parent == sys.Sun ? ship.Target : ship.Target.Parent;
            int si = ui.Buttons(planets.ConvertAll(p => (p == sel ? "● " : "") + p.Name).ToArray());
            if (si >= 0) SetTarget(planets[si]);
            if (sel != null && !sel.Asteroid)
            {
                var moons = sys.Bodies.FindAll(x => x.Parent == sel && x.Radius >= 10);
                moons.Sort((x, y) => y.Radius.CompareTo(x.Radius));
                int small = sys.Bodies.FindAll(x => x.Parent == sel).Count - moons.Count;
                if (moons.Count > 0)
                {
                    int mi = ui.Buttons(moons.ConvertAll(m => (m == ship.Target ? "● " : "") + m.Name).ToArray());
                    if (mi >= 0) SetTarget(moons[mi]);
                }
                if (small > 0) GUILayout.Label(small + (small == 1 ? " smaller moon" : " smaller moons") + ": click the labels in view", ui.label);
            }
            var rocks = sys.Bodies.FindAll(x => x.Asteroid);
            GUILayout.Label("Asteroids", ui.label);
            int ri = ui.Buttons(rocks.ConvertAll(x => (x == ship.Target ? "● " : "") + x.Name).ToArray());
            if (ri >= 0) SetTarget(rocks[ri]);

            ui.Section("Game");
            int sb = ui.Buttons("Save (" + Ctl.Keys(A.QuickSave) + ")", "Load", "Main menu");
            if (sb == 0) SaveGame();
            if (sb == 1 && HasSave) LoadGame();
            if (sb == 2) App.OpenMenu();
        }

        // ---------------------------------------------------------------- save games

        // `-fowsavekey <name>` points Save, Continue and F5 at another PlayerPrefs key, so live QA never touches the
        // player's own save (the career goes under <name>-career)
        static readonly string SaveKey = SaveKeyFromArgs();
        static string SaveKeyFromArgs()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-fowsavekey");
            if (i >= 0 && i + 1 < args.Length && !string.IsNullOrWhiteSpace(args[i + 1])) return args[i + 1];
            // the unattended suites save, load and autosave too (the -fowqa "save" step, a dock): keep them off the player's save
            // A process nonce also isolates two copies of the same suite/build. Keep it for this process's lifetime
            // so scene restarts and Continue still share their own save. Explicit keys remain reusable across runs.
            return Unattended() ? "fow-save-qa-" + Guid.NewGuid().ToString("N") : "fow-save-v1";
        }
        // an unattended suite is running: no player save, and no route calls to drop the warp mid-trip (a method, not a
        // static field, so SaveKey's initialiser never sees it unset)
        static bool Unattended() => Array.Exists(new[] { "-fowqa", "-fowcareer", "-fowtour", "-fowuishots", "-fowaudioqa", "-fowshipshots", "-fowfidelity", "-fowbhshots" },
            a => Array.IndexOf(Environment.GetCommandLineArgs(), a) >= 0);
        public static bool HasSave => PlayerPrefs.HasKey(SaveKey);
        public static string SaveSummary
        {
            get
            {
                try
                {
                    var s = JsonUtility.FromJson<Ship.SaveData>(PlayerPrefs.GetString(SaveKey));
                    return Frame.UtcFromTdb(s.jd).ToString("yyyy-MM-dd") + (string.IsNullOrEmpty(s.target) ? "" : ", target " + s.target);
                }
                catch (Exception) { return "unreadable"; }
            }
        }

        public void SaveGame()
        {
            WriteSave();
            ship.Phase = "game saved";
        }

        void WriteSave()
        {
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(ship.ToSave(jd, warpIdx)));
            SaveCareer();
            PlayerPrefs.Save();
        }

        public void LoadGame()
        {
            if (!HasSave) return;
            Ship.SaveData s;
            try { s = JsonUtility.FromJson<Ship.SaveData>(PlayerPrefs.GetString(SaveKey)); }
            catch (Exception e) { Debug.LogWarning("Fog of War: could not read the save: " + e.Message); return; }
            jd = s.jd;
            ship.FromSave(s, sys);
            // #18: the save keeps no plan to fly on, so Continue starts at 1× with the drive off and says what it dropped
            warpIdx = 0;
            reference = Ship.Dominant(sys, ship.Pos, jd);
            SetPivot(Pivot.Ship, 0.16);
            camMode = CamMode.Chase;
            LoadCareer();
            WatchCareer(fresh: false);
            // rescue and intercept ships exist again only now: resolve the saved target after the career (a finished
            // job's ship is gone, so its target stays empty)
            ship.Target = string.IsNullOrEmpty(s.target) ? null : sys[s.target];
            if (ship.Target != null) targetIdx = targets.IndexOf(ship.Target);
            var dropped = new List<string>();
            if (s.plan != (int)Plan.None) dropped.Add("autopilot plan");
            if (s.hold != (int)Hold.Off) dropped.Add("attitude hold");
            if (s.throttle > 0) dropped.Add("burn");
            if (dropped.Count > 0 || s.warp > 0)
                career.Notes.Add(new Career.Note
                {
                    Text = "Loaded at 1× with the drive off" + (dropped.Count > 0 ? ". Not resumed: " + string.Join(", ", dropped) : ""),
                    Good = dropped.Count == 0, Time = Time.realtimeSinceStartup,
                });
            ship.Phase = "game loaded";
        }
    }
}
