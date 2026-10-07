using System;
using System.Collections;
using System.IO;
using System.Reflection;
using FogOfWar.UI;
using UnityEngine;

namespace FogOfWar
{
    // Offscreen UI review: `FogOfWarUI.exe -batchmode -fowuishots <dir> [-fowuires 1920x1080,1440x2560]` renders the
    // camera and the UI Toolkit layer into textures at each resolution, composites them and saves a PNG per state.
    // No window opens, so it can run while someone else is playing. (IMGUI isn't drawn in batch mode: body labels
    // and the Observatory scenes' IMGUI panels are missing from these shots.)
    public class UiShots : MonoBehaviour
    {
        string dir;
        Vector2Int[] sizes = { new Vector2Int(1920, 1080), new Vector2Int(1440, 2560), new Vector2Int(2560, 1440), new Vector2Int(3840, 2160), new Vector2Int(5120, 1440) };
        string only;
        StreamWriter log;
        int errors;
        bool classicWas; // the player's "classic panel" setting, off for the run (no HUD to shoot) and put back at the end

        public static void StartIfRequested(FogOfWarApp app)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-fowuishots");
            if (i < 0 || i + 1 >= args.Length) return;
            var s = app.gameObject.AddComponent<UiShots>();
            s.dir = args[i + 1];
            int j = Array.IndexOf(args, "-fowuires");
            if (j >= 0 && j + 1 < args.Length)
            {
                var parts = args[j + 1].Split(',');
                s.sizes = new Vector2Int[parts.Length];
                for (int k = 0; k < parts.Length; k++) { var wh = parts[k].Split('x'); s.sizes[k] = new Vector2Int(int.Parse(wh[0]), int.Parse(wh[1])); }
            }
            int o = Array.IndexOf(args, "-fowuionly");
            if (o >= 0 && o + 1 < args.Length) s.only = args[o + 1];
        }

        void OnEnable() => Application.logMessageReceived += OnLog;
        void OnDisable() => Application.logMessageReceived -= OnLog;
        void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Log) return;
            if (type != LogType.Warning) errors++;
            log?.WriteLine($"[{type}] {msg}\n{stack}");
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(dir);
            log = new StreamWriter(Path.Combine(dir, "uishots.txt")) { AutoFlush = true };
            var app = GetComponent<FogOfWarApp>();
            Controls.Locked = true;
            app.InputLocked = true;
            yield return null;
            var shell = UiShell.Instance;
            if (shell == null) { log.WriteLine("no UI shell"); Quit(); yield break; }
            classicWas = GameSettings.ClassicPanel;
            if (classicWas) { GameSettings.ClassicPanel = false; log.WriteLine("classic panel setting on: off for this run"); }

            foreach (var size in sizes)
            {
                var camRT = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var uiRT = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                app.Cam.targetTexture = camRT;
                shell.Surface = uiRT;
                string tag = $"{size.x}x{size.y}";
                log.WriteLine("size " + tag);

                // fresh boot: main menu over the paused game
                app.Restart(app.GameIndex); app.ShowMenu(UiShell.MenuMain); SetStarted(app, false);
                yield return Shot(app, shell, camRT, uiRT, tag, "01-main-menu");
                shell.OpenSettings(0);
                yield return Shot(app, shell, camRT, uiRT, tag, "02-settings");
                shell.OpenSettings(4);
                yield return Shot(app, shell, camRT, uiRT, tag, "02b-audio");
                app.ShowMenu(UiShell.MenuControls);
                yield return Shot(app, shell, camRT, uiRT, tag, "03-controls");
                app.ShowMenu(UiShell.MenuObservatory);
                yield return Shot(app, shell, camRT, uiRT, tag, "04-observatory-menu");

                // flight
                app.NewGame();
                yield return Wait(1.5f);
                yield return Shot(app, shell, camRT, uiRT, tag, "05-flight");
                if (app.Current is OpenSystemScene first)
                {
                    log.WriteLine("    05-flight: console " + (first.TestConsoleOpen ? "open" : "closed"));
                    if (first.TestConsoleOpen) { errors++; log.WriteLine("    ERROR 05-flight: the console drawer should start closed"); }
                    // the target card: compact in cruise, everything with the details open
                    string rows = first.TestCardRows();
                    log.WriteLine("    05-flight: card rows " + rows);
                    if (rows != "Distance Closing Trip Engage") { errors++; log.WriteLine("    ERROR 05-flight: expected the compact card 'Distance Closing Trip Engage'"); }
                    first.SetCardExpanded(true);
                    yield return Wait(0.3f);
                    yield return Shot(app, shell, camRT, uiRT, tag, "05b-card-expanded");
                    rows = first.TestCardRows();
                    log.WriteLine("    05b-card-expanded: card rows " + rows);
                    foreach (var want in new[] { "Kind", "Rel", "Drift", "Stop", "Brach", "Note" })
                        if (Array.IndexOf(rows.Split(' '), want) < 0) { errors++; log.WriteLine($"    ERROR 05b-card-expanded: no {want} row"); }
                    first.SetCardExpanded(false);
                }
                Controls.Tap(Controls.Action.ThrottleFull);
                yield return Wait(1.2f);
                yield return Shot(app, shell, camRT, uiRT, tag, "06-flight-burn");
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fowuidump") >= 0) Dump(shell.HudLayer, 0);
                if (size == sizes[0] && Array.IndexOf(Environment.GetCommandLineArgs(), "-fowcapturetest") >= 0) yield return CaptureTest();
                Controls.Tap(Controls.Action.ThrottleCut);
                var os = app.Current as OpenSystemScene;
                if (os != null)
                {
                    // cockpit view: straight ahead, then looking down at the console (HUD must stay off the MFDs)
                    var setCam = typeof(OpenSystemScene).GetMethod("SetCam", BindingFlags.Instance | BindingFlags.NonPublic);
                    var lookPitch = typeof(OpenSystemScene).GetField("lookPitch", BindingFlags.Instance | BindingFlags.NonPublic);
                    setCam.Invoke(os, new object[] { 2 });
                    yield return Wait(0.5f);
                    yield return Shot(app, shell, camRT, uiRT, tag, "06f-cockpit");
                    log.WriteLine("    06f-cockpit: target card " + (os.TestCardShown ? "shown" : "hidden"));
                    if (os.TestCardShown) { errors++; log.WriteLine("    ERROR 06f-cockpit: the MFDs carry the target in cruise; the card should hide"); }
                    lookPitch.SetValue(os, 22f);
                    yield return Wait(0.3f);
                    yield return Shot(app, shell, camRT, uiRT, tag, "06g-cockpit-look-down");
                    // the nearest surfaces: couch wing, armrest and side console (issue #4)
                    var lookYaw = typeof(OpenSystemScene).GetField("lookYaw", BindingFlags.Instance | BindingFlags.NonPublic);
                    lookPitch.SetValue(os, 30f); lookYaw.SetValue(os, -55f);
                    yield return Wait(0.3f);
                    yield return Shot(app, shell, camRT, uiRT, tag, "06i-cockpit-look-left");
                    lookPitch.SetValue(os, 10f); lookYaw.SetValue(os, 0f);
                    // the darker condition: red lighting under a 3 g burn
                    var shipF = (OpenWorld.Ship)typeof(OpenSystemScene).GetField("ship", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(os);
                    double g0 = shipF.MaxG;
                    shipF.MaxG = 3;
                    Controls.Tap(Controls.Action.ThrottleFull);
                    yield return Wait(1.5f);
                    yield return Shot(app, shell, camRT, uiRT, tag, "06j-cockpit-burn-3g");
                    Controls.Tap(Controls.Action.ThrottleCut);
                    shipF.MaxG = g0;
                    yield return Wait(1.0f);
                    // QA-050: photo mode from the cockpit starts outside the hull and the free camera moves
                    lookPitch.SetValue(os, 0f);
                    Capture.TogglePhoto();
                    yield return Wait(0.5f);
                    log.WriteLine("    cockpit photo start: " + Capture.RigState);
                    Capture.TestFly(new Vector3(0, 0.6f, -6f), 1f, 30f);
                    yield return Shot(app, shell, camRT, uiRT, tag, "06h-cockpit-photo-moved");
                    log.WriteLine("    cockpit photo after move: " + Capture.RigState);
                    Capture.TogglePhoto();
                    yield return null;
                    setCam.Invoke(os, new object[] { 0 });
                    yield return null;
                }
                if (os != null)
                {
                    // QA-056/058: the tutorial's practice beacon as the target, speeds measured against it (m/s);
                    // QA-057: the same target off screen, so its edge marker shows
                    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
                    var spawn = typeof(OpenSystemScene).GetMethod("SpawnBeacon", F);
                    if (spawn != null)
                    {
                        spawn.Invoke(os, null);
                        var beacon = (OpenWorld.Body)typeof(OpenSystemScene).GetField("beacon", F).GetValue(os);
                        var ship = (OpenWorld.Ship)typeof(OpenSystemScene).GetField("ship", F).GetValue(os);
                        if (beacon != null && ship != null)
                        {
                            ship.VelRef = beacon;
                            yield return Wait(0.5f);
                            yield return Shot(app, shell, camRT, uiRT, tag, "06i-beacon");
                            os.TestAimTargetAt(1.6f);
                            yield return Wait(0.3f);
                            yield return Shot(app, shell, camRT, uiRT, tag, "06j-beacon-offscreen");
                            log.WriteLine("    beacon: " + os.TestLabelReport());
                            // QA-062: the travel line within parking distance (the beacon starts 5 km off), then 5 km past it
                            log.WriteLine("    travel line, parked: " + os.TestTravelLine());
                            os.TestOffsetFromTarget(5);
                            os.TestAimTargetAt(0.5f);
                            yield return Wait(0.3f);
                            yield return Shot(app, shell, camRT, uiRT, tag, "06k-travel-5km");
                            log.WriteLine("    travel line, 5 km out: " + os.TestTravelLine());
                            // the approach HUD: brake gauge early, at the burn point and too late, then docking with the RCS split
                            foreach (var (aShot, aKm, aKms, aDrift, aWant) in new[] {
                                ("06n-approach", 200.0, 1.5, 0.05, "Approach early"), ("06o-approach-burn", 130.0, 1.5, 0.0, "Approach now"),
                                ("06p-approach-late", 80.0, 1.5, 0.0, "Approach late"), ("06q-docking", 3.0, 0.03, 0.005, "Docking") })
                            {
                                os.TestApproachTarget(aKm, aKms, aDrift);
                                os.TestAimTargetAt(0.35f);
                                yield return Wait(0.3f);
                                yield return Shot(app, shell, camRT, uiRT, tag, aShot);
                                string aRep = os.TestApproachReport();
                                log.WriteLine($"    {aShot}: {aRep}");
                                if (!aRep.StartsWith(aWant)) { errors++; log.WriteLine($"    ERROR {aShot}: expected '{aWant}'"); }
                            }
                            // the stop marker (target-relative retrograde) with its burn cue, looking away from the target
                            os.TestApproachTarget(130, 1.5);
                            os.TestLookAwayFromTarget();
                            yield return Wait(0.3f);
                            yield return Shot(app, shell, camRT, uiRT, tag, "06s-stop-marker");
                            string stopRep = os.TestStopMarkerReport();
                            log.WriteLine("    06s-stop-marker: " + stopRep);
                            if (!stopRep.Contains("BURN NOW")) { errors++; log.WriteLine("    ERROR 06s-stop-marker: expected the BURN NOW cue"); }
                            // a lesson that names a card row keeps it on the compact card: Brake (the Stop row, closing
                            // fast) and Beacon (Closing, under the gauge; that lesson holds only under 20 m/s)
                            var lessonF = typeof(OpenSystemScene).GetField("lesson", F);
                            var lessonWas = lessonF.GetValue(os);
                            foreach (var (lName, lKm, lKms, lRow) in new[] { ("Brake", 130.0, 1.5, "Stop"), ("Beacon", 3.0, 0.01, "Closing") })
                            {
                                os.TestApproachTarget(lKm, lKms);
                                lessonF.SetValue(os, Enum.Parse(lessonF.FieldType, lName));
                                yield return Wait(0.3f);
                                string lRows = os.TestCardRows();
                                log.WriteLine($"    lesson {lName}: card rows {lRows}");
                                var lSet = lRows.Split(' ');
                                if (Array.IndexOf(lSet, lRow) < 0 || Array.IndexOf(lSet, "Gauge") < 0) { errors++; log.WriteLine($"    ERROR lesson {lName}: expected the gauge and the {lRow} row"); }
                            }
                            lessonF.SetValue(os, lessonWas);
                            // cockpit: the card shows while the gauge is up (the MFDs repeat the rest of it)
                            os.TestApproachTarget(130, 1.5);
                            var setCamA = typeof(OpenSystemScene).GetMethod("SetCam", F);
                            setCamA.Invoke(os, new object[] { 2 });
                            yield return Wait(0.5f);
                            yield return Shot(app, shell, camRT, uiRT, tag, "06r-cockpit-approach");
                            log.WriteLine("    06r-cockpit-approach: " + os.TestApproachReport());
                            setCamA.Invoke(os, new object[] { 0 });
                            os.TestOffsetFromTarget(5);
                            yield return null;
                            // QA-060: toasts stay clear of the target card
                            shell.Toast("Test toast: job posted");
                            shell.Toast("Test toast with a note", "and a second line");
                            shell.Toast("Test toast: something went wrong", null, 3.5f, "bad");
                            // QA-063: the fuel-short warning, a long title over a long note
                            shell.Toast("Not enough fuel to reach Neptune", "Needs about 412.6 t with margin, 196.4 t aboard. Engage again to go anyway.", 6, "bad");
                            yield return Wait(0.4f);
                            yield return Shot(app, shell, camRT, uiRT, tag, "06l-toasts");
                            log.WriteLine("    toasts: " + shell.TestToastReport());
                            // a payday: a green toast with the bank line, the credits counting up, then settled on the new total
                            bool wasSandbox = os.TestPayday(12345);
                            yield return Wait(0.4f);
                            yield return Shot(app, shell, camRT, uiRT, tag, "06p-payday");
                            string payMid = os.TestPaydayReport();
                            yield return Wait(1.2f);
                            string payEnd = os.TestPaydayReport();
                            log.WriteLine($"    06p-payday: {payMid}  ->  {payEnd}");
                            string bank = payEnd.Substring(payEnd.LastIndexOf("bank ") + 5);
                            if (!payMid.Contains("counting True") || payMid.StartsWith(bank + " CR")) { errors++; log.WriteLine("    ERROR 06p-payday: expected the credits to be counting up"); }
                            if (!payEnd.Contains("counting False") || !payEnd.StartsWith(bank + " CR")) { errors++; log.WriteLine("    ERROR 06p-payday: expected the credits to settle on the new total"); }
                            if (!shell.TestToastReport().Contains("pay")) { errors++; log.WriteLine("    ERROR 06p-payday: expected a pay toast"); }
                            // a route call: the distress card with its two answers
                            bool called = os.TestRouteCall(OpenWorld.Career.RouteKind.Distress);
                            yield return Wait(0.4f);
                            yield return Shot(app, shell, camRT, uiRT, tag, "06t-route-call");
                            if (!called) { errors++; log.WriteLine("    ERROR 06t-route-call: expected a distress call"); }
                            string callRep = os.TestRouteCallReport();
                            log.WriteLine($"    06t-route-call: {callRep}");
                            if (!callRep.Contains("card Flex opacity 1.00") || !callRep.Contains("on screen True") || callRep.Contains("above the lesson False")) { errors++; log.WriteLine("    ERROR 06t-route-call: expected the call card on screen"); }
                            os.TestRouteCallClear();
                            os.TestPaydayUndo(12345, wasSandbox);
                            ship.VelRef = null;
                            typeof(OpenSystemScene).GetMethod("EndPractice", F)?.Invoke(os, null);
                        }
                    }
                }
                // the full-screen Jobs and Codex pages
                os?.TestHandJob(true);
                os?.TestShipPaid(350000);
                os?.OpenPage(0);
                yield return Shot(app, shell, camRT, uiRT, tag, "06a-jobs-page");
                os?.TestShipPaid(double.NaN);
                os?.TestHandJob(false);
                if (os != null)
                {
                    // the long goal reached: the buyout card says the career so far
                    os.ClosePage();
                    os.TestBuyout();
                    yield return Wait(0.6f);
                    yield return Shot(app, shell, camRT, uiRT, tag, "06o-ship-owned");
                    string own = os.TestBuyoutReport();
                    log.WriteLine($"    06o-ship-owned: {own}");
                    if (!own.Contains("owned True") || !own.Contains("cr earned")) { errors++; log.WriteLine("    ERROR 06o-ship-owned: expected the ship owned and the record"); }
                    os.TestBuyoutUndo();
                    os.OpenPage(0);
                }
                os?.OpenPage(1);
                yield return Shot(app, shell, camRT, uiRT, tag, "06b-codex");
                if (os != null)
                {
                    const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic;
                    var system = (OpenWorld.SolarSystem)typeof(OpenSystemScene).GetField("sys", f).GetValue(os);
                    var craft = (OpenWorld.Ship)typeof(OpenSystemScene).GetField("ship", f).GetValue(os);
                    var career = (OpenWorld.Career)typeof(OpenSystemScene).GetField("career", f).GetValue(os);
                    var epoch = typeof(OpenSystemScene).GetField("jd", f);
                    double oldJd = (double)epoch.GetValue(os);
                    var oldTarget = craft.Target; var oldPlan = craft.Plan; string oldPhase = craft.Phase;
                    var targetIndex = typeof(OpenSystemScene).GetField("targetIdx", f);
                    var cameraDistance = typeof(OpenSystemScene).GetField("camDist", f);
                    object oldIndex = targetIndex.GetValue(os), oldDistance = cameraDistance.GetValue(os);
                    var setTarget = typeof(OpenSystemScene).GetMethod("SetTarget", f);
                    bool oldFreeze = os.QaFreeze;
                    string oldCareer = JsonUtility.ToJson(career.ToSave(craft));
                    var visited = new System.Collections.Generic.HashSet<string>(career.Codex.Visited);
                    var toastLayer = (UnityEngine.UIElements.VisualElement)typeof(UiShell).GetField("toastLayer", f).GetValue(shell);
                    bool toastsVisible = toastLayer.visible;
                    toastLayer.visible = false;
                    os.QaFreeze = true;
                    var mean = system.Bodies.Find(b => b.ElementsCalibrated && b.Cheb == null) ?? system.Bodies.Find(b => b.Synchronous && b.Cheb == null);
                    var estimated = system.Bodies.Find(b => b.SizeEstimated);
                    var targets = new[] { system.Earth, mean, estimated, system.Earth, system["Jupiter"], system["Psyche"], system["Phobos"], system["Titan"] };
                    var names = new[] { "fitted", "mean-moon", "estimated", "fallback", "giant", "procedural-mesh", "shape-asset", "parent-fallback" };
                    try
                    {
                        for (int q = 0; q < targets.Length; q++)
                        {
                            var body = targets[q]; if (body == null) continue;
                            bool added = career.Codex.Visited.Add(body.Name);
                            setTarget.Invoke(os, new object[] { body });
                            epoch.SetValue(os, names[q] == "fallback" ? system.Earth.Cheb.JdEnd + 1 :
                                names[q] == "parent-fallback" ? body.Parent.Cheb.JdEnd + 1 : CareerQA.Epoch);
                            os.ShowCodex(0);
                            yield return Wait(0.5f);
                            yield return Shot(app, shell, camRT, uiRT, tag, "06b-provenance-" + names[q]);
                            log.WriteLine($"    provenance {names[q]}: {body.Name}; {body.PositionModel((double)epoch.GetValue(os))}; radius {body.RadiusSource}; shape {body.ShapeSource}; rotation {body.RotationSource}");
                            bool honest = names[q] != "procedural-mesh" || (body.ShapeSource.StartsWith("procedural") && body.RadiusSource == "catalogue triaxial-diameter radius");
                            honest &= names[q] != "shape-asset" || body.ShapeSource.StartsWith("shape model");
                            honest &= names[q] != "parent-fallback" || body.PositionModel((double)epoch.GetValue(os)).Contains("Saturn's mean-element fallback");
                            if (!honest) { errors++; log.WriteLine("    ERROR provenance " + names[q] + ": wrong model label"); }
                            if (added) career.Codex.Visited.Remove(body.Name);
                        }
                    }
                    finally
                    {
                        if (oldTarget != null) setTarget.Invoke(os, new object[] { oldTarget });
                        else craft.Target = null;
                        craft.Plan = oldPlan; craft.Phase = oldPhase;
                        targetIndex.SetValue(os, oldIndex); cameraDistance.SetValue(os, oldDistance);
                        epoch.SetValue(os, oldJd);
                        career.Codex.Visited.Clear(); career.Codex.Visited.UnionWith(visited);
                        os.QaFreeze = oldFreeze; toastLayer.visible = toastsVisible;
                    }
                    bool restored = (double)epoch.GetValue(os) == oldJd && JsonUtility.ToJson(career.ToSave(craft)) == oldCareer;
                    log.WriteLine("    " + (restored ? "PASS" : "ERROR") + " provenance restores the scene date and career state");
                    if (!restored) errors++;
                }
                os?.ShowCodex(1);
                yield return Shot(app, shell, camRT, uiRT, tag, "06c-achievements");
                os?.ShowCodex(0);
                os?.ClosePage();
                os?.ShowConsoleTab(0);
                if (os != null)
                {
                    // QA-059: the lesson card above the console, which must clear it
                    yield return Wait(0.3f);
                    yield return Shot(app, shell, camRT, uiRT, tag, "06m-lesson-console");
                    log.WriteLine("    flight tab: " + os.TestLessonReport());
                    // the first flight's payday card, with its two answers, and a step of the hand-flying practice
                    var lessonFld = typeof(OpenSystemScene).GetField("lesson", BindingFlags.Instance | BindingFlags.NonPublic);
                    var lessonBefore = lessonFld.GetValue(os);
                    foreach (var (pName, pShot, pWant) in new[] { ("Payday", "06u-payday-card", "FIRST FLIGHT  ·  PAID | pips 3 | skip False | answers True"),
                                                                 ("Turn", "06v-practice-card", "HAND-FLYING PRACTICE  ·  STEP 1 OF 8 | pips 8 | skip True | answers False") })
                    {
                        lessonFld.SetValue(os, Enum.Parse(lessonFld.FieldType, pName));
                        yield return Wait(0.4f);
                        yield return Shot(app, shell, camRT, uiRT, tag, pShot);
                        string pRep = os.TestLessonCardReport();
                        log.WriteLine($"    {pShot}: {pRep}");
                        if (!pRep.StartsWith(pWant)) { errors++; log.WriteLine($"    ERROR {pShot}: expected '{pWant}'"); }
                    }
                    lessonFld.SetValue(os, lessonBefore);
                }
                if (os != null)
                    foreach (var k in new[] { OpenWorld.JobKind.Intercept, OpenWorld.JobKind.Rescue })
                    {
                        bool ok = os.TestTargetJob(k);
                        log.WriteLine($"    target a {k} job: {(ok ? "yes" : "none posted")}");
                        if (ok) yield return Shot(app, shell, camRT, uiRT, tag, "06d-target-" + k.ToString().ToLowerInvariant());
                        if (ok)
                            foreach (float frac in new[] { 0.62f, 0.72f, 0.95f })
                            {
                                // QA-046: target near the right-hand panels
                                os.TestAimTargetAt(frac);
                                yield return Shot(app, shell, camRT, uiRT, tag, $"06e-{k.ToString().ToLowerInvariant()}-at-{frac * 100:0}");
                                log.WriteLine("    " + os.TestLabelReport());
                            }
                    }
                Controls.Tap(Controls.Action.Help);
                yield return Wait(0.3f);
                yield return Shot(app, shell, camRT, uiRT, tag, "07-help");
                Controls.Tap(Controls.Action.Help);
                Controls.Tap(Controls.Action.Map);
                yield return Wait(1.0f);
                yield return Shot(app, shell, camRT, uiRT, tag, "08-system-map");
                if (app.Current is OpenSystemScene mapOs)
                {
                    // the map as a trip planner: your orbit and the target's stand out, the planned path with its marks
                    const BindingFlags MF = BindingFlags.Instance | BindingFlags.NonPublic;
                    var mapSys = (OpenWorld.SolarSystem)typeof(OpenSystemScene).GetField("sys", MF).GetValue(mapOs);
                    var setTarget = typeof(OpenSystemScene).GetMethod("SetTarget", MF);
                    foreach (var (mName, mShot) in new[] { ((string)null, "08-system-map"), ("Mars", "08b-map-mars"), ("Jupiter", "08c-map-jupiter") })
                    {
                        if (mName != null)
                        {
                            setTarget.Invoke(mapOs, new object[] { mapSys[mName] });
                            yield return Wait(0.5f);
                            yield return Shot(app, shell, camRT, uiRT, tag, mShot);
                        }
                        string mRep = mapOs.TestMapReport();
                        log.WriteLine($"    {mShot}: {mRep}");
                        // Jupiter's meet point is off screen at the map's opening zoom; Mars barely moves, so its ring gives way
                        if (mRep.Contains(" 0 segs") || mRep.Contains("path off") || !mRep.Contains("flip mark on") || mRep.Contains("meet mark none")) { errors++; log.WriteLine($"    ERROR {mShot}: expected your orbit, the planned path and its marks"); }
                    }
                }
                Controls.Tap(Controls.Action.Map);
                yield return Wait(0.5f);
                app.ShowMenu(UiShell.MenuMain);
                yield return Shot(app, shell, camRT, uiRT, tag, "09-pause-menu");
                app.ShowMenu(UiShell.MenuNone);
                Capture.TogglePhoto();
                yield return Wait(0.5f);
                yield return Shot(app, shell, camRT, uiRT, tag, "10-photo-mode");
                // QA-028: the free camera must actually move the view in the open world
                Capture.TestFly(new Vector3(0, 0.6f, -6f), 1f, 30f);
                yield return Shot(app, shell, camRT, uiRT, tag, "10b-photo-moved");
                log.WriteLine("    after move: " + Capture.RigState);
                Capture.TestApproach();
                yield return Shot(app, shell, camRT, uiRT, tag, "10c-photo-hull");
                log.WriteLine("    after flying at the hull: " + Capture.RigState);
                Capture.TogglePhoto();
                app.Activate(1);
                yield return Wait(1.0f);
                yield return Shot(app, shell, camRT, uiRT, tag, "11-observatory-scene");

                app.Cam.targetTexture = null;
                shell.Surface = null;
                camRT.Release(); uiRT.Release();
                Destroy(camRT); Destroy(uiRT);
            }
            log.WriteLine($"done, {errors} errors");
            Quit();
        }

        IEnumerator CaptureTest()
        {
            foreach (int scale in new[] { 1, 2 })
            {
                GameSettings.ShotScale = scale;
                Capture.TakeScreenshot();
                float t0 = Time.realtimeSinceStartup;
                string before = Capture.LastShot;
                while (Capture.LastShot == null && Time.realtimeSinceStartup - t0 < 30) yield return null;
                log.WriteLine($"  screenshot {scale}x: {Capture.LastShot} {(Capture.LastShot != null ? new FileInfo(Capture.LastShot).Length : 0)} bytes");
                yield return Wait(1.1f); // distinct file names
            }
            // QA-066: a 4x shot counts as saving (so quit waits for it) until its file is written
            GameSettings.ShotScale = 4;
            Capture.TakeScreenshot();
            float t2 = Time.realtimeSinceStartup;
            bool sawSaving = false;
            while (Time.realtimeSinceStartup - t2 < 30)
            {
                sawSaving |= CaptureRunner.Saving;
                if (sawSaving && !CaptureRunner.Saving) break;
                yield return null;
            }
            while (Capture.LastShot == null && Time.realtimeSinceStartup - t2 < 35) yield return null; // posted to the main thread
            bool written = Capture.LastShot != null && File.Exists(Capture.LastShot);
            log.WriteLine($"  QA-066 quit guard: saving seen {sawSaving}, cleared {!CaptureRunner.Saving}, file written {written}");
            if (!sawSaving || CaptureRunner.Saving || !written) errors++;
            yield return Wait(1.1f);
            GameSettings.ShotScale = 1;
            Capture.StartClip(3);
            float t1 = Time.realtimeSinceStartup;
            while (Capture.Recording && Time.realtimeSinceStartup - t1 < 120) yield return null;
            while (Capture.LastClip == null && Time.realtimeSinceStartup - t1 < 180) yield return null;
            log.WriteLine($"  clip: {Capture.LastClip} {(Capture.LastClip != null && File.Exists(Capture.LastClip) ? new FileInfo(Capture.LastClip).Length : 0)} bytes, {Time.realtimeSinceStartup - t1:0.0} s");
        }

        static void SetStarted(FogOfWarApp app, bool v)
        {
            var f = typeof(FogOfWarApp).GetField("gameStarted", BindingFlags.Instance | BindingFlags.NonPublic);
            f?.SetValue(app, v);
        }

        static IEnumerator Wait(float s)
        {
            float t = Time.realtimeSinceStartup + s;
            while (Time.realtimeSinceStartup < t) yield return null;
        }

        IEnumerator Shot(FogOfWarApp app, UiShell shell, RenderTexture camRT, RenderTexture uiRT, string tag, string name)
        {
            if (only != null && !name.Contains(only)) yield break;
            // a few frames for layout, transitions and the HUD to settle
            yield return Wait(0.6f);
            yield return new WaitForEndOfFrame();
            app.Cam.Render(); // batch mode runs no render loop; the UI panel draws itself into its texture
            // overlay cameras on top of the main one (the cockpit interior) render into the same target
            foreach (var oc in Camera.allCameras)
                if (oc != app.Cam && oc.depth > app.Cam.depth && oc.clearFlags == CameraClearFlags.Depth && oc.targetTexture == camRT) oc.Render();
            var cam = Read(camRT);
            var ui = Read(uiRT);
            var a = cam.GetPixels32(); var b = ui.GetPixels32();
            for (int i = 0; i < a.Length; i++)
            {
                // UI Toolkit writes premultiplied colour into its target
                int k = 255 - b[i].a;
                a[i].r = (byte)Mathf.Min(255, b[i].r + a[i].r * k / 255);
                a[i].g = (byte)Mathf.Min(255, b[i].g + a[i].g * k / 255);
                a[i].b = (byte)Mathf.Min(255, b[i].b + a[i].b * k / 255);
                a[i].a = 255;
            }
            cam.SetPixels32(a);
            File.WriteAllBytes(Path.Combine(dir, $"{tag}-{name}.png"), cam.EncodeToPNG());
            // the target card reads in plain units: no "×10^-4 m/s" at rest, no "4.17 ×10^6 km" stop distance
            if (app.Current is OpenSystemScene card && card.TestCardReport() is string cr && cr.Contains("×10"))
            {
                errors++;
                log.WriteLine("    target card has a power of ten: " + cr);
            }
            Destroy(cam); Destroy(ui);
            log.WriteLine($"  {name}");
        }

        void Dump(UnityEngine.UIElements.VisualElement e, int depth)
        {
            if (depth > 9) return;
            var l = e.layout;
            string txt = e is UnityEngine.UIElements.Label lab ? " \"" + lab.text.Replace('\n', ' ') + "\"" : "";
            log.WriteLine($"{new string(' ', depth * 2)}{e.GetType().Name} [{string.Join(",", e.GetClasses())}] {l.x:0},{l.y:0} {l.width:0}x{l.height:0}{txt}");
            foreach (var c in e.hierarchy.Children()) Dump(c, depth + 1);
        }

        static Texture2D Read(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            return t;
        }

        void Quit()
        {
            if (classicWas) GameSettings.ClassicPanel = true;
            log?.Close(); log = null;
            Application.Quit();
        }
    }
}
