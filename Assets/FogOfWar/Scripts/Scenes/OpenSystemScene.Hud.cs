using System;
using System.Collections.Generic;
using FogOfWar.OpenWorld;
using FogOfWar.UI;
using UnityEngine;
using UnityEngine.UIElements;
using A = FogOfWar.Controls.Action;

namespace FogOfWar
{
    // Open System flight HUD for the UI Toolkit shell: status (top left), target card and ship console (right),
    // flight cluster and status pills (bottom), and flight markers drawn over the view (prograde, retrograde,
    // heading, target brackets with an edge arrow when the target is off screen). Labels stay in IMGUI (DrawLabels).
    public partial class OpenSystemScene
    {
        public override bool HasHud => !GameSettings.ClassicPanel;
        // in flight the key bar shows for the first seconds and during a lesson; F1 has the full sheet after that
        public override bool WantsKeyHints => TutorialText != null || Time.unscaledTime - hudBuiltAt < 20;
        float hudBuiltAt;

        // ---- free camera: offsets are km from the pivot (the ship, the target or the Sun)
        public override bool RigInScene => true;
        // km: clear of the 52 m hull around the ship, or just above the surface of the target or the Sun
        public override float RigMinDistance => pivot == Pivot.Ship ? 0.045f
            : pivot == Pivot.Target && ship.Target != null ? (float)(ship.Target.Radius * 1.02) : (float)(sys.Sun.Radius * 1.05);
        public override void RigBegin(CamRig rig)
        {
            rig.Offset = Frame.U(camW - PivotW());
            rig.Rotation = Cam.transform.rotation;
            rig.Scale = (float)Math.Max(camDist, 0.05);
            // QA-050: the cockpit eye is inside the hull, closer than the free camera may go; start from the chase pose
            if (pivot == Pivot.Ship && camMode == CamMode.Cockpit)
            {
                float d = (float)Math.Max(camDist, 0.08);
                var off = ship.Att * OffsetDir(chaseYaw, chasePitch);
                rig.Offset = off * d;
                rig.Rotation = Quaternion.LookRotation(-off, ship.Att * Vector3.up);
                rig.Scale = d;
            }
        }

        UiShell hudShell;
        VisualElement markers, mkPro, mkRetro, mkHead, mkTgt, mkEdge, mapHead, rightCol, consoleBody;
        Label stDate, stWarp, stNear, stLimited;
        VisualElement mkIcon;
        Label dvHead;
        VisualElement statusBox;
        Label spdVal, spdRef, drvVal, drvSub, dvVal, rotVal, mkTgtName, mkTgtDist, mkEdgeName, consoleToggle;
        VisualElement thrFill, thrCmd, pills;
        Label pAssist, pHold, pNav, pLimited, pLanded, pRaider, pModel, pArrival;
        FowSlider thrSlider;
        Segmented segG, segPlan, segWarp, segCam, segWarpTop;
        Switch swAssist, swBelt, swHist, swRings;
        readonly List<Label> holdBtns = new List<Label>();
        Label planEngage, navTarget, navEst, navHohmann, navPhase;
        VisualElement moonsBox, histBox, dataBox;
        Body moonsFor;
        bool consoleOpen; // a drawer: closed at the start of a flight
        Tabs consoleTabs;
        float dataT;
        readonly List<Readout> hudReadouts = new List<Readout>();
        bool hudTargetsDirty = true;

        public override void BuildHud(UiShell shell, VisualElement layer)
        {
            hudShell = shell;
            hudBuiltAt = Time.unscaledTime;
            holdBtns.Clear();
            markers = Ui.E(layer, "layer"); markers.pickingMode = PickingMode.Ignore;
            BuildMarkers();

            // ---- status, top left
            var st = Ui.E(layer, "status-box");
            stDate = Ui.L(st, "", "t-h3", "f-mono", "glow-ink");
            var wr = Ui.E(st, "row");
            wr.style.marginTop = 4;
            Ui.L(wr, "TIME", "t-micro", "f-condm").style.marginRight = 8;
            segWarpTop = new Segmented(WarpNames, warpIdx, i => warpIdx = i);
            segWarpTop.style.marginBottom = 0;
            foreach (var c in segWarpTop.Children()) { c.AddToClassList("small"); c.style.paddingLeft = 6; c.style.paddingRight = 6; }
            wr.Add(segWarpTop);
            stLimited = Ui.L(st, "", "t-note", "warn");
            stNear = Ui.L(st, "", "t-body", "f-semi");
            stNear.style.marginTop = 6;
            var stStatus = st;
            statusBox = st;

            // ---- right column: target card + console
            rightCol = Ui.E(layer, "col");
            rightCol.style.position = Position.Absolute;
            rightCol.style.top = 18; rightCol.style.right = 20; rightCol.style.width = 380;
            rightCol.style.bottom = shell.Portrait ? 350 : 56; // portrait: clear the lesson card and flight cluster
            BuildTargetCard(rightCol);
            BuildFirstJob(rightCol);
            BuildConsole(rightCol);

            // ---- flight cluster, bottom centre
            var fl = Ui.E(layer, "flight");
            flightBox = fl;
            var sp = Ui.E(fl, "gauge", "speed");
            Ui.L(sp, "VELOCITY", "t-micro", "f-condm");
            spdVal = Ui.L(sp, "", "t-big", "f-mono", "glow-ink");
            spdRef = Ui.L(sp, "", "t-label", "f-condm");
            var dr = Ui.E(fl, "gauge", "drive");
            var drHead = Ui.E(dr, "row");
            Ui.L(drHead, "DRIVE", "t-micro", "f-condm").style.flexGrow = 1;
            drvVal = Ui.L(drHead, "", "t-h3", "f-mono");
            var thr = Ui.E(dr, "thr");
            for (int i = 1; i < 4; i++) { var t = Ui.E(thr, "tick"); t.style.left = Length.Percent(25 * i); }
            thrFill = Ui.E(thr, "fill");
            thrCmd = Ui.E(thr, "cmd");
            drvSub = Ui.L(dr, "", "t-label", "f-condm");
            BuildCareerStrip(dr, stStatus);
            var rg = Ui.E(fl, "gauge", "right-g");
            dvHead = Ui.L(rg, "SPEED CHANGE SPENT", "t-micro", "f-condm");
            dvVal = Ui.L(rg, "", "t-big", "f-mono");
            rotVal = Ui.L(rg, "", "t-label", "f-mono");

            BuildLessonCard(layer);
            BuildCallCard(layer);
            pills = Ui.E(layer, "pills");
            pills.pickingMode = PickingMode.Ignore;
            pRaider = Pill("RAIDER"); // first: a chase is the one that needs an answer
            pAssist = Pill("ASSIST"); pHold = Pill("HOLD"); pNav = Pill("NAV");
            pLanded = Pill("LANDED"); pLimited = Pill("WARP LIMITED");
            pModel = Pill("TWO-BODY COAST"); pArrival = Pill("ASSISTED ARRIVAL");

            // ---- system map header
            mapHead = Ui.E(layer, "col");
            mapHead.style.position = Position.Absolute; mapHead.style.top = 18; mapHead.style.left = Length.Percent(50);
            mapHead.style.width = 640; mapHead.style.marginLeft = -320; mapHead.style.alignItems = Align.Center;
            Ui.L(mapHead, "SYSTEM MAP", "t-h2", "f-cond", "glow");
            Ui.L(mapHead, "Distances beyond 100,000 km are drawn on a log scale. Click a label to target it.", "t-note").style.unityTextAlign = TextAnchor.MiddleCenter;
            var mrow = Ui.E(mapHead, "row");
            mrow.style.marginTop = 6;
            Ui.B(mrow, "Back to ship  " + UiShell.Keys(A.Map), ToggleMap, "small");
            Ui.B(mrow, "Frame target", () => { if (ship.Target != null) SetPivot(Pivot.Target, ship.Target.Radius * 4); }, "small");
            BuildPage(layer);
            hudTargetsDirty = true;
        }

        Label Pill(string text)
        {
            var l = Ui.L(pills, text, "chip", "f-condm");
            return l;
        }

        void SetPill(Label p, string text, string tone)
        {
            Ui.Show(p, text != null);
            if (text == null) return;
            Ui.Text(p, text);
            Ui.SetClass(p, "hot", tone == "hot"); Ui.SetClass(p, "cold", tone == "cold");
            Ui.SetClass(p, "bad", tone == "bad"); Ui.SetClass(p, "ok", tone == "ok");
        }

        // ------------------------------------------------------------------------------------------ markers

        VisualElement Ring(VisualElement parent, float size, Color c, float width = 2)
        {
            var r = Ui.E(parent);
            r.pickingMode = PickingMode.Ignore;
            r.style.position = Position.Absolute;
            r.style.width = size; r.style.height = size; r.style.left = -size / 2; r.style.top = -size / 2;
            r.style.borderTopLeftRadius = r.style.borderTopRightRadius = r.style.borderBottomLeftRadius = r.style.borderBottomRightRadius = size / 2;
            r.style.borderTopWidth = r.style.borderBottomWidth = r.style.borderLeftWidth = r.style.borderRightWidth = width;
            r.style.borderTopColor = r.style.borderBottomColor = r.style.borderLeftColor = r.style.borderRightColor = c;
            return r;
        }

        VisualElement Bar(VisualElement parent, float x, float y, float w, float h, Color c, float rot = 0)
        {
            var b = Ui.E(parent);
            b.pickingMode = PickingMode.Ignore;
            b.style.position = Position.Absolute;
            b.style.left = x - w / 2; b.style.top = y - h / 2; b.style.width = w; b.style.height = h;
            b.style.backgroundColor = c;
            if (rot != 0) b.style.rotate = new Rotate(rot);
            return b;
        }

        VisualElement Mk()
        {
            var m = Ui.E(markers, "mk");
            m.pickingMode = PickingMode.Ignore;
            return m;
        }

        void BuildMarkers()
        {
            var green = new Color(0.55f, 1f, 0.62f, 0.95f);
            var red = new Color(1f, 0.55f, 0.48f, 0.95f);
            var white = new Color(0.92f, 0.95f, 1f, 0.95f);
            var amber = new Color(1f, 0.71f, 0.28f, 1f);

            mkPro = Mk();
            Ring(mkPro, 24, green);
            Bar(mkPro, 0, -17, 2, 9, green); Bar(mkPro, -17, 0, 9, 2, green); Bar(mkPro, 17, 0, 9, 2, green);
            Bar(mkPro, 0, 0, 4, 4, green);
            mkRetro = Mk();
            Ring(mkRetro, 24, red);
            Bar(mkRetro, 0, 0, 22, 2, red, 45); Bar(mkRetro, 0, 0, 22, 2, red, -45);
            Bar(mkRetro, 0, 17, 2, 9, red); Bar(mkRetro, -17, 0, 9, 2, red); Bar(mkRetro, 17, 0, 9, 2, red);
            // ship's nose: a broken horizon line
            mkHead = Mk();
            Bar(mkHead, -15, 0, 14, 2, white); Bar(mkHead, 15, 0, 14, 2, white); Bar(mkHead, -8, 4, 2, 9, white); Bar(mkHead, 8, 4, 2, 9, white);
            Bar(mkHead, 0, 0, 3, 3, white);

            mkTgt = Mk();
            foreach (var c in new[] { "tl", "tr", "bl", "br2" }) { var b = Ui.E(mkTgt, "br", c); b.pickingMode = PickingMode.Ignore; b.name = c; }
            var lab = Ui.E(mkTgt, "col");
            lab.name = "label";
            lab.pickingMode = PickingMode.Ignore;
            lab.style.position = Position.Absolute;
            var nameRow = Ui.E(lab, "row");
            mkIcon = KindIcon(nameRow);
            mkTgtName = Ui.L(nameRow, "", "t-h3", "f-cond", "accent", "glow");
            mkTgtDist = Ui.L(lab, "", "t-label", "f-mono");
            mkTgtName.style.whiteSpace = WhiteSpace.NoWrap; mkTgtDist.style.whiteSpace = WhiteSpace.NoWrap;
            // QA-046: long names end in an ellipsis instead of running under the panels
            mkTgtName.style.maxWidth = 300; mkTgtName.style.overflow = Overflow.Hidden; mkTgtName.style.textOverflow = TextOverflow.Ellipsis;

            mkEdge = Mk();
            var chev = Ui.E(mkEdge);
            chev.name = "chev";
            chev.pickingMode = PickingMode.Ignore;
            chev.style.position = Position.Absolute;
            chev.style.width = 16; chev.style.height = 16; chev.style.left = -8; chev.style.top = -8;
            chev.style.borderTopWidth = 3; chev.style.borderRightWidth = 3;
            chev.style.borderTopColor = amber; chev.style.borderRightColor = amber;
            mkEdgeName = Ui.L(mkEdge, "", "t-label", "f-condm", "accent");
            mkEdgeName.style.position = Position.Absolute;
            mkEdgeName.style.whiteSpace = WhiteSpace.NoWrap;
            BuildTargetMarkers();
            BuildMapMarkers();
        }

        // place a marker at a render-space direction; false if it's behind the camera or off screen
        bool PlaceDir(VisualElement m, Vector3 dirRender, out Vector2 at)
        {
            var sp = Cam.WorldToScreenPoint(dirRender.normalized * 10f);
            at = hudShell.ScreenToHud(sp);
            bool on = sp.z > 0 && sp.x >= 0 && sp.y >= 0 && sp.x <= Cam.pixelWidth && sp.y <= Cam.pixelHeight && CockpitShows(sp);
            Ui.Show(m, on);
            if (on) m.style.translate = new Translate(at.x, at.y);
            return on;
        }

        void UpdateMarkers()
        {
            bool flying = pivot == Pivot.Ship;
            var v = ship.Vel - sys.Vel(reference, jd);
            bool moving = v.Sqr > 1e-10;
            if (flying && moving) { PlaceDir(mkPro, Frame.U(v), out _); PlaceDir(mkRetro, Frame.U(-v), out _); }
            else { Ui.Show(mkPro, false); Ui.Show(mkRetro, false); }
            if (flying && camMode != CamMode.Cockpit) PlaceDir(mkHead, Frame.U(ship.Heading), out _);
            else if (flying) PlaceDir(mkHead, Frame.U(ship.Heading), out _);
            else Ui.Show(mkHead, false);
            UpdateTargetMarkers(flying);
            UpdateMapMarkers();

            if (ship.Target == null) { Ui.Show(mkTgt, false); Ui.Show(mkEdge, false); return; }
            var tw = sys.Pos(ship.Target, jd);
            var rel = tw - camW;
            var dir = Frame.U(rel);
            var sp = Cam.WorldToScreenPoint(dir.normalized * 10f);
            bool inView = sp.z > 0 && sp.x >= 0 && sp.y >= 0 && sp.x <= Cam.pixelWidth && sp.y <= Cam.pixelHeight && CockpitShows(sp);
            string dist = Km((tw - ship.Pos).Mag - ship.Target.Radius);
            if (inView)
            {
                Ui.Show(mkTgt, true); Ui.Show(mkEdge, false);
                var at = hudShell.ScreenToHud(sp);
                mkTgt.style.translate = new Translate(at.x, at.y);
                // bracket the body's disc, never smaller than a fixed box
                float pxPerRad = Cam.pixelHeight / 2f / Mathf.Tan(Cam.fieldOfView * Mathf.Deg2Rad / 2);
                float ang = (float)Math.Atan2(ship.Target.Radius, Math.Max(rel.Mag, 1e-9));
                float r = Mathf.Clamp(ang * pxPerRad / hudShell.Scale + 6, 16, hudShell.H * 0.45f);
                SetBracket("tl", -r, -r); SetBracket("tr", r - 12, -r); SetBracket("bl", -r, r - 12); SetBracket("br2", r - 12, r - 12);
                var lab = mkTgt.Q("label");
                PlaceTargetLabel(lab, at, r);
                Ui.Text(mkTgtName, ship.Target.Name.ToUpperInvariant());
                Ui.Text(mkTgtDist, dist);
            }
            else
            {
                Ui.Show(mkTgt, false); Ui.Show(mkEdge, true);
                // direction on screen from the centre; flipped if the target is behind
                var c = new Vector2(Cam.pixelWidth / 2f, Cam.pixelHeight / 2f);
                var d = new Vector2(sp.x, sp.y) - c;
                if (sp.z < 0) d = -d;
                if (d.sqrMagnitude < 1e-6f) d = Vector2.down;
                d.Normalize();
                var free = FreeView(); // QA-057: clear of the console and the flight cluster
                float hw = free.width / 2, hh = free.height / 2;
                float k = Mathf.Min(hw / Mathf.Max(Mathf.Abs(d.x), 1e-4f), hh / Mathf.Max(Mathf.Abs(d.y), 1e-4f));
                var at = OntoDisplay(new Vector2(free.center.x + d.x * k, free.center.y - d.y * k));
                // nor on the status box top left (the warp buttons): drop the arrow below it
                if (statusBox != null && !float.IsNaN(statusBox.layout.yMax))
                {
                    var sb = statusBox.layout;
                    if (at.x < sb.xMax + 20 && at.y < sb.yMax + 20) at.y = sb.yMax + 28;
                }
                mkEdge.style.translate = new Translate(at.x, at.y);
                float deg = Mathf.Atan2(-d.y, d.x) * Mathf.Rad2Deg; // screen y grows downward
                mkEdge.Q("chev").style.rotate = new Rotate(deg + 45);
                Ui.Text(mkEdgeName, ship.Target.Name.ToUpperInvariant() + "  " + dist);
                mkEdgeName.style.left = d.x > 0.3f ? -150 : 14;
                mkEdgeName.style.top = d.y < -0.3f ? -30 : 12;
                mkEdgeName.style.width = 136;
                mkEdgeName.style.unityTextAlign = d.x > 0.3f ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            }
        }

        // QA-046: the label sits right of the bracket unless that would run under the right-hand panels (or off
        // screen); then it goes left of the bracket, and below it if neither side has room.
        VisualElement flightBox;

        // QA-056: under 1 km/s a speed reads in m/s, as the lessons and the drift row do
        static string SpeedNum(double kms, out string unit)
        {
            double a = Math.Abs(kms);
            if (a >= 1) { unit = "km/s"; return Fmt.Num(kms, a < 10 ? 3 : 1); }
            unit = "m/s";
            double ms = kms * 1000;
            if (Math.Abs(ms) < 0.005) ms = 0; // no "×10^-5 m/s" at rest
            return Fmt.Num(ms, Math.Abs(ms) < 10 ? 2 : Math.Abs(ms) < 100 ? 1 : 0);
        }

        static string BrachSpeed(double kms) => kms < 1 ? SpeedText(kms) : Fmt.Num(kms, kms < 10 ? 1 : 0) + " km/s";

        static string SpeedText(double kms) => SpeedNum(kms, out string u) + " " + u;

        // QA-057: the part of the HUD that has no panels on it (right column and bottom flight cluster excluded)
        Rect FreeView()
        {
            float xMax = hudShell.HudWidth - 60, yMax = hudShell.H - 90;
            if (rightCol != null && rightCol.resolvedStyle.display == DisplayStyle.Flex && !float.IsNaN(rightCol.layout.x))
                xMax = Mathf.Min(xMax, rightCol.layout.x - 24);
            if (flightBox != null && flightBox.resolvedStyle.display == DisplayStyle.Flex && !float.IsNaN(flightBox.layout.y))
                yMax = Mathf.Min(yMax, flightBox.layout.y - 24);
            float xMin = 60, yMin = 70;
            if (xMax < xMin + 200) xMax = xMin + 200;
            if (yMax < yMin + 200) yMax = yMin + 200;
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        void PlaceTargetLabel(VisualElement lab, Vector2 at, float r)
        {
            float w = float.IsNaN(lab.resolvedStyle.width) ? 200 : lab.resolvedStyle.width;
            float h = float.IsNaN(lab.resolvedStyle.height) ? 40 : lab.resolvedStyle.height;
            float rightLimit = hudShell.HudWidth - 8;
            if (rightCol != null && rightCol.resolvedStyle.display == DisplayStyle.Flex && !float.IsNaN(rightCol.layout.x))
            {
                var col = rightCol.layout;
                // only where the label's rows overlap the column vertically
                if (at.y - r < col.yMax && at.y - r + h > col.y) rightLimit = Mathf.Min(rightLimit, col.x - 8);
            }
            float left, top = -r;
            if (at.x + r + 8 + w <= rightLimit) { left = r + 8; lab.style.alignItems = Align.FlexStart; }
            else if (at.x - r - 8 - w >= 8) { left = -r - 8 - w; lab.style.alignItems = Align.FlexEnd; }
            else { left = -w / 2; top = r + 6; lab.style.alignItems = Align.Center; }
            // a bracket that is itself under the panels still gets its label in the open: pull it left of them
            left = Mathf.Max(Mathf.Min(left, rightLimit - w - at.x), 8 - at.x);
            // QA-057: and never down over the flight cluster: lift it above the bracket instead
            if (flightBox != null && flightBox.resolvedStyle.display == DisplayStyle.Flex && !float.IsNaN(flightBox.layout.y))
            {
                var fb = flightBox.layout;
                bool overlapX = at.x + left < fb.xMax && at.x + left + w > fb.x;
                if (overlapX && at.y + top + h > fb.y - 6) top = Mathf.Min(-r - h - 4, fb.y - 6 - h - at.y);
            }
            lab.style.left = left; lab.style.top = top;
        }

        void SetBracket(string n, float x, float y)
        {
            var b = mkTgt.Q(n);
            b.style.left = x; b.style.top = y;
        }

        string Kind(Body b)
        {
            var craft = CraftKind(b);
            if (craft != null) return craft;
            if (b.Asteroid) return "Asteroid";
            if (b.Parent == sys.Sun) return "Planet";
            return b.Parent != null ? "Moon of " + b.Parent.Name : "Star";
        }

        // ------------------------------------------------------------------------------------------ console

        void BuildConsole(VisualElement parent)
        {
            var p = Ui.Panel(parent, null, null, true, "console-body");
            p.style.flexShrink = 1;
            ConsoleHead(p);
            consoleBody = Ui.E(p, "col");
            consoleBody.style.flexShrink = 1;
            var tabs = new Tabs(true);
            tabs.style.flexShrink = 1;
            consoleBody.Add(tabs);
            consoleTabs = tabs;
            tabs.Add("Flight", Scroll(FlightTab));
            tabs.Add("Nav", Scroll(NavTab));
            tabs.Add("Targets", Scroll(TargetsTab));
            tabs.Add("View", Scroll(ViewTab));
            tabs.Add("Data", Scroll(DataTab));
            SetConsole(consoleOpen);
        }

        static VisualElement Scroll(Action<VisualElement> fill) => Ui.Scroll(fill);

        Label thrLabel;

        void FlightTab(VisualElement c)
        {
            var head = Ui.E(c, "row");
            Ui.L(head, "THROTTLE", "t-label", "f-condm").style.flexGrow = 1;
            thrLabel = Ui.L(head, "", "t-val", "f-mono");
            thrSlider = new FowSlider(0, 1, (float)ship.ThrottleCmd, v => ManualThrottle(v));
            c.Add(thrSlider);
            Ui.L(c, $"{UiShell.Keys(A.ThrottleUp)} / {UiShell.Keys(A.ThrottleDown)} throttle · {UiShell.Keys(A.ThrottleFull)} full · {UiShell.Keys(A.ThrottleCut)} cut", "t-note");
            Ui.Section(c, "Drive limit");
            var gl = new string[Gs.Length];
            for (int i = 0; i < Gs.Length; i++) gl[i] = Fmt.Num(Gs[i], 1) + " g" + (Gs[i] >= 10 ? "*" : "");
            segG = new Segmented(gl, Array.FindIndex(Gs, g => Math.Abs(g - ship.MaxG) < 1e-6), i => ship.MaxG = Gs[i]);
            c.Add(segG);
            segGNote = Ui.L(c, "", "t-note");
            var r = Ui.E(c, "check-row");
            r.style.marginTop = 6;
            swAssist = new Switch(ship.FlightAssist, v => ship.FlightAssist = v);
            r.Add(swAssist);
            Ui.L(r, $"Flight assist ({UiShell.Keys(A.FlightAssist)}): the stick sets the turn rate; release it to stop", "t-body");
            var row = Ui.E(c, "row", "wrap");
            row.style.marginTop = 6;
            Ui.B(row, "Flip 180°  " + UiShell.Keys(A.Flip), Flip);
            Ui.B(row, "Kill rotation", () => { ship.AngVel = Vector3.zero; ship.Hold = Hold.Off; });
            Ui.Section(c, "Attitude hold  (keys 1–9)");
            Ui.B(c, "Velocity reference: orbit / target  " + UiShell.Keys(A.VelocityRef), ToggleVelRef, "small");
            var grid = Ui.E(c, "row", "wrap");
            for (int i = 0; i < 9; i++)
            {
                int k = i;
                var b = Ui.B(grid, $"{i + 1}  {HoldNames[i]}", () => SetHold((Hold)k), "small");
                b.style.width = 108;
                b.style.justifyContent = Justify.FlexStart;
                b.style.unityTextAlign = TextAnchor.MiddleLeft;
                holdBtns.Add(b);
            }
            Ui.Section(c, "Thrusters");
            Ui.L(c, $"RCS: {UiShell.Keys(A.RcsForward)}/{UiShell.Keys(A.RcsBack)} fore-aft, {UiShell.Keys(A.RcsLeft)}/{UiShell.Keys(A.RcsRight)} left-right, {UiShell.Keys(A.RcsUp)}/{UiShell.Keys(A.RcsDown)} up-down. Attitude: {UiShell.Keys(A.PitchDown)}{UiShell.Keys(A.PitchUp)} pitch, {UiShell.Keys(A.YawLeft)}{UiShell.Keys(A.YawRight)} yaw, {UiShell.Keys(A.RollLeft)}{UiShell.Keys(A.RollRight)} roll.", "t-note");
        }

        void NavTab(VisualElement c)
        {
            navTarget = Ui.L(c, "", "t-h3", "f-cond", "accent");
            navEst = Ui.L(c, "", "t-note", "f-mono");
            BuildTrip(c);
            navHohmann = Ui.L(c, "", "t-note", "f-mono");
            Ui.Section(c, "Plan");
            var pl = new[] { Plan.Brachistochrone, Plan.MatchVelocity, Plan.HoldStation };
            segPlan = new Segmented(new[] { "Fast", "Match velocity", "Hold station" }, Array.IndexOf(pl, selectedPlan), i => selectedPlan = pl[i]);
            c.Add(segPlan);
            planEngage = Ui.B(c, "ENGAGE  " + UiShell.Keys(A.Engage), TogglePlan, "primary", "f-cond");
            planEngage.style.marginTop = 4;
            navPhase = Ui.L(c, "", "t-label", "f-condm");
            Ui.Section(c, "Time compression");
            segWarp = new Segmented(WarpNames, warpIdx, i => warpIdx = i);
            c.Add(segWarp);
            Ui.L(c, $"{UiShell.Keys(A.WarpUp)} faster · {UiShell.Keys(A.WarpDown)} slower. Flying by hand drops to 1×.", "t-note");
        }

        void TargetsTab(VisualElement c)
        {
            Ui.Section(c, "Planets");
            var row = Ui.E(c, "row", "wrap");
            foreach (var p in PlanetButtons())
            {
                var b = p;
                var l = Ui.B(row, p.Name, () => SetTarget(b), "small");
                l.userData = b;
            }
            moonsBox = Ui.E(c, "col");
            Ui.Section(c, "Asteroids");
            var ar = Ui.E(c, "row", "wrap");
            foreach (var a in sys.Bodies.FindAll(x => x.Asteroid))
            {
                var b = a;
                var l = Ui.B(ar, a.Name, () => SetTarget(b), "small");
                l.userData = b;
            }
            moonsFor = null;
        }

        void RebuildMoons()
        {
            var sel = ship.Target == null ? null : ship.Target.Parent == sys.Sun ? ship.Target : ship.Target.Parent;
            if (sel == moonsFor) return;
            moonsFor = sel;
            moonsBox.Clear();
            if (sel == null || sel.Asteroid) return;
            var moons = sys.Bodies.FindAll(x => x.Parent == sel && x.Radius >= 10);
            moons.Sort((x, y) => y.Radius.CompareTo(x.Radius));
            int small = sys.Bodies.FindAll(x => x.Parent == sel).Count - moons.Count;
            if (moons.Count == 0 && small == 0) return;
            Ui.Section(moonsBox, "Moons of " + sel.Name);
            var row = Ui.E(moonsBox, "row", "wrap");
            foreach (var m in moons)
            {
                var b = m;
                var l = Ui.B(row, m.Name, () => SetTarget(b), "small");
                l.userData = b;
            }
            if (small > 0) Ui.L(moonsBox, small + (small == 1 ? " smaller moon: click its label in view" : " smaller moons: click their labels in view"), "t-note");
            Ui.Fontify(moonsBox);
        }

        void ViewTab(VisualElement c)
        {
            Ui.Section(c, "Camera  (" + UiShell.Keys(A.CameraMode) + " cycles)");
            segCam = new Segmented(new[] { "Chase", "Orbit", "Cockpit", "Target", "Map" }, 0, SetCam, wrap: true);
            c.Add(segCam);
            Ui.Section(c, "Show");
            var r = Ui.E(c, "check-row"); r.Add(new Switch(GameSettings.ShowLabels, v => GameSettings.ShowLabels = v)); Ui.L(r, "Body labels", "t-body");
            r = Ui.E(c, "check-row"); swBelt = new Switch(beltView.Visible, v => beltView.Visible = v); r.Add(swBelt); Ui.L(r, "Asteroid belt (a 1 % sample: real rocks are about a million km apart)", "t-body");
            r = Ui.E(c, "check-row"); swRings = new Switch(faintRings, v => faintRings = v); r.Add(swRings); Ui.L(r, "Brighten faint rings ×1000 (Jupiter, Uranus, Neptune)", "t-body");
            r = Ui.E(c, "check-row"); swHist = new Switch(showHist, v => { showHist = v; Ui.Show(histBox, v); }); r.Add(swHist); Ui.L(r, "Belt semi-major axis histogram", "t-body");
            histBox = Ui.E(c, "col");
            var img = new Image { image = beltHist, scaleMode = ScaleMode.StretchToFill };
            img.style.height = 90; img.style.marginTop = 4;
            histBox.Add(img);
            Ui.L(histBox, "1.6 → 5.6 AU. Kirkwood gaps (Jupiter resonances 4:1, 3:1, 5:2, 7:3, 2:1) at 2.06, 2.50, 2.82, 2.96, 3.28 AU; Hildas at 3.97 (3:2); Trojans at 5.2 (1:1). The gaps are in orbit size, so they don't show as empty rings in space.", "t-note");
            Ui.Show(histBox, showHist);
        }

        void SetCam(int ci)
        {
            if (ci <= 2) { SetPivot(Pivot.Ship, 0.16); camMode = (CamMode)ci; ResetLook(); Cam.fieldOfView = 55; }
            if (ci == 3 && ship.Target != null) SetPivot(Pivot.Target, ship.Target.Radius * 4);
            if (ci == 4) { SetPivot(Pivot.Sun, 6 * Frame.AU); pitch = 65; }
        }

        int CamIndex() => pivot == Pivot.Target ? 3 : pivot == Pivot.Sun ? 4 : (int)camMode;

        void DataTab(VisualElement c)
        {
            dataBox = Ui.E(c, "col");
            Ui.L(c, Note, "t-note").style.marginTop = 10;
            Ui.Section(c, "Game");
            var row = Ui.E(c, "row", "wrap");
            Ui.B(row, "Save  " + UiShell.Keys(A.QuickSave), () => hudShell.Toast(SaveGame() ? "Game saved" : ChaseNoSave));
            Ui.B(row, "Load", () => { if (HasSave) { LoadGame(); hudShell.Toast("Game loaded"); } });
            Ui.B(row, "Main menu", App.OpenMenu);
        }

        void UpdateData()
        {
            hudReadouts.Clear();
            Readouts(hudReadouts);
            while (dataBox.childCount > hudReadouts.Count) dataBox.RemoveAt(dataBox.childCount - 1);
            for (int i = 0; i < hudReadouts.Count; i++)
            {
                var r = hudReadouts[i];
                if (i >= dataBox.childCount)
                {
                    var (row, _) = Ui.KV(dataBox, "");
                    Ui.Fontify(row);
                }
                var rowE = dataBox[i];
                var k = (Label)rowE[0]; var v = (Label)rowE[1];
                Ui.Text(k, r.label.ToUpperInvariant());
                Ui.Text(v, r.value);
                Ui.SetClass(v, "accent", r.tone == Tone.Hot); Ui.SetClass(v, "cold", r.tone == Tone.Cold); Ui.SetClass(v, "warn", r.tone == Tone.Bad);
                v.style.whiteSpace = WhiteSpace.Normal;
                v.style.flexShrink = 1;
            }
        }

        // ------------------------------------------------------------------------------------------ per frame

        public override void UpdateHud(UiShell shell)
        {
            if (markers == null) return;
            hudShell = shell;
            bool map = pivot == Pivot.Sun;
            UpdateMarkers();
            Ui.Show(mapHead, map);

            // status
            var date = Frame.UtcFromTdb(jd);
            Ui.Text(stDate, date.ToString("ddd d MMM yyyy  ·  HH:mm", System.Globalization.CultureInfo.InvariantCulture));
            segWarpTop.Value = warpIdx;
            bool limited = Time.realtimeSinceStartup - limitedAt < 2f;
            Ui.Text(stLimited, limited ? $"Limited by the frame budget: running at {Fmt.Num(effectiveWarp, 0)}×" : "");
            Ui.Show(stLimited, limited);
            var refPos = sys.Pos(reference, jd);
            double alt = (ship.Pos - refPos).Mag - reference.Radius;
            Ui.Text(stNear, (ship.Landed != null ? "ON " : "NEAR ") + reference.Name.ToUpperInvariant() + "  ·  " + Km(alt) + (reference == sys.Sun ? " out" : " up"));

            UpdateTargetCard();

            // flight cluster
            var vRef = (ship.Vel - RefVel(VelRefBody)).Mag;
            Ui.Text(spdVal, SpeedNum(vRef, out string spdUnit));
            Ui.Text(spdRef, spdUnit.ToUpperInvariant() + " VS " + RefName(VelRefBody).ToUpperInvariant());
            Ui.Text(drvVal, Fmt.Num(ship.Throttle * ship.AccelG, 2) + " g");
            Ui.SetClass(drvVal, "accent", ship.Throttle > 0);
            Ui.SetClass(drvVal, "glow", ship.Throttle > 0);
            thrFill.style.width = Length.Percent((float)ship.Throttle * 100);
            thrCmd.style.left = Length.Percent((float)ship.ThrottleCmd * 100);
            Ui.Text(drvSub, $"THROTTLE {Mathf.RoundToInt((float)ship.ThrottleCmd * 100)} %  ·  LIMIT {Fmt.Num(ship.MaxG, 1)} g");
            // what is left decides the next trip; with an unmetered tank only what was spent means anything
            bool metered = !ship.UnlimitedFuel;
            double dvShown = metered ? Career.DvLeft(ship) : ship.DvUsed;
            Ui.Text(dvHead, metered ? "SPEED CHANGE LEFT" : "SPEED CHANGE SPENT");
            Ui.Text(dvVal, Fmt.Num(dvShown, dvShown < 100 ? 2 : 0));
            Ui.Text(rotVal, "KM/S  ·  ROT " + Fmt.Num(ship.AngVel.magnitude * Mathf.Rad2Deg, 1) + " °/s");

            // pills
            UpdateCareerStrip();
            UpdateLessonCard();
            // a pill only for a state that is not the normal one: a chase, assist off, a hold, a plan flying
            SetPill(pRaider, RaiderLine()?.ToUpperInvariant(), "bad");
            SetPill(pAssist, ship.FlightAssist ? null : "ASSIST OFF", "bad");
            SetPill(pHold, ship.Hold == Hold.Off ? null : "HOLD " + HoldNames[Math.Min((int)ship.Hold, HoldNames.Length - 1)].ToUpperInvariant() + (ship.VelRef != null ? " VS " + ship.VelRef.Name.ToUpperInvariant() : ""), "cold");
            SetPill(pNav, ship.PlanActive ? "NAV " + ship.Phase.ToUpperInvariant() : null, "hot");
            SetPill(pLanded, ship.Landed != null ? (ship.AssistedArrival ? "ASSISTED ARRIVAL " : "LANDED ") + ship.Landed.Name.ToUpperInvariant() : null, "ok");
            SetPill(pModel, Coasting() ? "TWO-BODY COAST · APPROXIMATE" : null, "cold");
            SetPill(pArrival, ship.Landed == null && ArrivalNotice != null ? (Ship.Giant(reference) ? "CLOUD-TOP ASSIST · NO SOLID SURFACE" : "ARRIVAL ASSIST · NO ATMOSPHERIC FLIGHT") : null, "bad");
            SetPill(pLimited, limited ? "WARP LIMITED " + Fmt.Num(effectiveWarp, 0) + "×" : null, "bad");

            UpdatePages();
            if (!consoleOpen) return;
            // console: reflect state changed by keys
            if (!thrSlider.Dragging) thrSlider.SetValueWithoutNotify((float)ship.ThrottleCmd);
            Ui.Text(thrLabel, Mathf.RoundToInt((float)ship.ThrottleCmd * 100) + " %");
            segG.Value = Array.FindIndex(Gs, g => Math.Abs(g - ship.MaxG) < 1e-6);
            UpdateDriveLimit();
            UpdateTrip();
            swAssist.Value = ship.FlightAssist;
            for (int i = 0; i < holdBtns.Count; i++) Ui.SetClass(holdBtns[i], "on", (int)ship.Hold == i);
            segWarp.Value = warpIdx;
            segCam.Value = CamIndex();
            segPlan.Value = Array.IndexOf(new[] { Plan.Brachistochrone, Plan.MatchVelocity, Plan.HoldStation }, selectedPlan);
            swBelt.Value = beltView.Visible; swRings.Value = faintRings;
            Ui.Text(planEngage, ship.PlanActive ? "ABORT PLAN  " + UiShell.Keys(A.Engage) : "ENGAGE  " + UiShell.Keys(A.Engage));
            Ui.SetClass(planEngage, "danger", ship.PlanActive);
            Ui.Text(navPhase, ship.PlanActive ? "ACTIVE: " + ship.Phase.ToUpperInvariant() : "IDLE (" + ship.Phase + ")");
            var t = ship.Target;
            if (t != null)
            {
                var tp = sys.Pos(t, jd);
                double d = Math.Max((tp - ship.Pos).Mag - Ship.Standoff(t), 0);
                Ui.Text(navTarget, "TARGET  " + t.Name.ToUpperInvariant());
                Ui.Text(navEst, BrachLine(d, out bool navShort));
                Ui.SetClass(navEst, "warn", navShort);
                if (t.Parent == sys.Sun)
                {
                    var (dv, tt) = Ship.Hohmann(ship.Pos.Mag, tp.Mag, sys.Sun.GM);
                    Ui.Text(navHohmann, $"Slow route (orbit transfer, not on autopilot): {Dur(tt)}, about {Fmt.Num(ship.Mass * (1 - Math.Exp(-dv / Ship.Ve)), 1)} t of fuel");
                }
                else Ui.Text(navHohmann, "");
            }
            else { Ui.Text(navTarget, "NO TARGET"); Ui.Text(navEst, "Pick one in Targets or click a label."); Ui.Text(navHohmann, ""); }
            RebuildMoons();
            foreach (var b in consoleBody.Query<Label>(className: "btn").ToList())
                if (b.userData is Body body) Ui.SetClass(b, "on", body == ship.Target);
            if ((dataT -= Time.unscaledDeltaTime) <= 0) { dataT = 0.25f; UpdateData(); }
        }
    }
}
