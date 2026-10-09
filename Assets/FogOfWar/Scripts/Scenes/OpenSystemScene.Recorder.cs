using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FogOfWar.OpenWorld;
using UnityEngine;
using A = FogOfWar.Controls.Action;
using Ctl = FogOfWar.Controls;

namespace FogOfWar
{
    // Flight recorder: one text file per run of the game in the capture folder, always on. "Unintended" can't be detected in
    // general, so the file keeps enough to reconstruct what the ship did and why: a state line every second (S), a
    // line for every change of a discrete state with the keys that fired in that frame (EVT), a few sanity rules (WARN),
    // Unity errors (ERR), and a bookmark key (MARK) that also writes the last 10 s at 10 Hz and takes a screenshot.
    // Lines are "<real seconds> <kind> key=value ..." so they grep.
    public partial class OpenSystemScene
    {
        const int RecKeep = 20;            // newest session files kept
        const float RecRingSeconds = 10, RecRingHz = 10;

        // static: the app enters and leaves this scene many times in one run (menus, new games), and all of it is one file
        static StreamWriter rec;
        static string recPath;
        static bool recFailed;
        static float recT;
        float recSampleAt, recRingAt, recCustomSince = -1;
        double recClosing = double.NaN;
        D3 recPos;
        Body recClosingTarget;
        bool recAimed;
        readonly Queue<string> recRing = new Queue<string>();
        readonly StringBuilder recSb = new StringBuilder(512);
        readonly Dictionary<string, float> recWarned = new Dictionary<string, float>();
        // discrete states: the value last written and when, so a flickering state can't flood the file
        static readonly string[] RecFields = { "hold", "plan", "phase", "warp", "limited", "cam", "target", "velref", "assist", "landed", "ref", "thr", "drive", "rot", "rcs", "dry", "menu", "raider", "missiles", "damage" };
        readonly string[] recNow = new string[RecFields.Length], recLogged = new string[RecFields.Length];
        readonly float[] recLoggedAt = new float[RecFields.Length];

        static string F(double v, string fmt = "0.###") => v.ToString(fmt, CultureInfo.InvariantCulture);
        static string V(Vector3 v) => F(v.x, "0.##") + "," + F(v.y, "0.##") + "," + F(v.z, "0.##");

        static void RecorderOpen()
        {
            try
            {
                string dir = GameSettings.CaptureDir;
                Directory.CreateDirectory(dir);
                var old = Directory.GetFiles(dir, "flight-*.log");
                Array.Sort(old, StringComparer.Ordinal); // the stamp sorts by date
                for (int i = 0; i < old.Length - (RecKeep - 1); i++) File.Delete(old[i]);
                recPath = Path.Combine(dir, "flight-" + Capture.Stamp() + ".log");
                rec = new StreamWriter(new FileStream(recPath, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
                rec.WriteLine($"# Fog of War flight log, {DateTime.Now:yyyy-MM-dd HH:mm:ss}, game {Application.version}, Unity {Application.unityVersion}, {Screen.width}x{Screen.height}");
                rec.WriteLine("# args: " + string.Join(" ", Environment.GetCommandLineArgs()));
                rec.WriteLine("# S: state each second. EVT: a state changed (keys = actions fired that frame). WARN: a sanity rule. ERR: Unity error. MARK: bookmark, then the last 10 s at 10 Hz (R).");
                rec.WriteLine("# units: km, km/s, deg/s. closing > 0: approaching the target. raider_closing > 0: a raider approaching; boarding: its clock's part of the boarding time; magazine, missiles: its missiles left and in flight, missile_km: the first one's range; killed: its missiles the PDCs killed; bear_raider, bear_missile: working PDC mounts that bear on the raider and on the first missile; dmg_*: missile hits not repaired (dmg_mounts: PDC mounts out). nose_tgt / nose_vel: cosine of the angle from the nose to the target / to the velocity vs ref. g_drive: local gravity over the drive's limit.");
                Application.logMessageReceived += RecOnLog;
                Application.quitting += () => { RecLine("EVT", "game quit"); rec?.Dispose(); rec = null; recFailed = true; };
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                recFailed = true; rec = null;
                Debug.LogWarning("Fog of War: flight log off, can't write to the capture folder: " + e.Message);
            }
        }

        // Leaving the game scene: the file stays open for the next entry, which starts from an unknown state.
        void RecorderClose()
        {
            RecLine("EVT", "left the game scene");
            Array.Clear(recLogged, 0, recLogged.Length);
            recRing.Clear();
        }

        static void RecLine(string kind, string text) => RecRaw(F(recT, "0.00") + " " + kind + " " + text);

        static void RecRaw(string line)
        {
            if (rec == null) return;
            try { rec.WriteLine(line); }
            catch (IOException e)
            {
                Application.logMessageReceived -= RecOnLog;
                rec = null; recFailed = true;
                Debug.LogWarning("Fog of War: flight log stopped: " + e.Message);
            }
        }

        static void RecOnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Log || type == LogType.Warning) return;
            RecLine("ERR", type + ": " + msg.Replace('\n', ' ') + " | " + stack.Replace('\n', ' '));
        }

        void RecWarn(string rule, string detail)
        {
            if (recWarned.TryGetValue(rule, out float at) && recT - at < 5) return;
            recWarned[rule] = recT;
            RecLine("WARN", rule + (detail != null ? " " + detail : "") + " | " + RecState());
        }

        // Line to the target, velocity relative to it, and the closing speed along that line (positive: approaching).
        void TargetRel(out D3 r, out D3 vr, out double closing)
        {
            r = sys.Pos(ship.Target, jd) - ship.Pos;
            vr = ship.Vel - sys.Vel(ship.Target, jd);
            closing = D3.Dot(vr, r.Norm);
        }

        string RecCam => pivot == Pivot.Ship ? camMode.ToString() : pivot.ToString();

        string RecState()
        {
            var rf = reference ?? sys.Sun;
            var sb = recSb; sb.Clear();
            var rr = ship.Pos - sys.Pos(rf, jd);
            var vref = ship.Vel - sys.Vel(ship.VelRef ?? rf, jd);
            sb.Append("jd=").Append(F(jd, "0.0000000")).Append(" warp=").Append(WarpNames[warpIdx]).Append(" eff=").Append(F(effectiveWarp, "0.#"))
              .Append(" coast=").Append(Coasting() ? 1 : 0)
              .Append(" ref=").Append(rf.Name).Append(" alt=").Append(F(rr.Mag - rf.Radius, "0.#")).Append(" v_ref=").Append(F(vref.Mag, "0.####"))
              .Append(" g_drive=").Append(F(sys.Gravity(ship.Pos, jd).Mag / Math.Max(ship.AMax, 1e-12), "0.####"))
              .Append(" nose_vel=").Append(vref.Sqr > 1e-12 ? F(D3.Dot(ship.Heading, vref.Norm)) : "-");
            if (ship.Target != null)
            {
                TargetRel(out var r, out var vr, out double closing);
                sb.Append(" tgt=").Append(ship.Target.Name).Append(" dist=").Append(F(r.Mag - ship.Target.Radius, "0.#")).Append(" rel=").Append(F(vr.Mag, "0.####"))
                  .Append(" closing=").Append(F(closing, "0.####")).Append(" drift=").Append(F((vr - r.Norm * closing).Mag, "0.####"))
                  .Append(" nose_tgt=").Append(F(D3.Dot(ship.Heading, r.Norm)));
            }
            else sb.Append(" tgt=-");
            sb.Append(" thr_cmd=").Append(F(ship.ThrottleCmd, "0.##")).Append(" thr=").Append(F(ship.Throttle, "0.##")).Append(" max_g=").Append(F(ship.MaxG, "0.#"))
              .Append(" hold=").Append(ship.Hold).Append(" plan=").Append(ship.PlanActive ? ship.Plan : Plan.None).Append(" phase=\"").Append(ship.Phase).Append('"')
              .Append(" rot=").Append(V(ship.RotInput)).Append(" rcs=").Append(V(ship.RcsInput)).Append(" angvel=").Append(V(ship.AngVel * Mathf.Rad2Deg))
              .Append(" assist=").Append(ship.FlightAssist ? 1 : 0).Append(" cam=").Append(RecCam)
              .Append(" fuel=").Append(ship.UnlimitedFuel ? "unmetered" : F(ship.Fuel, "0.#"));
            if (RaiderStatus(out double raiderKm, out double raiderClosing, out double boarding))
            {
                sb.Append(" raider_km=").Append(F(raiderKm, "0.#")).Append(" raider_closing=").Append(F(raiderClosing, "0.####")).Append(" boarding=").Append(F(boarding, "0.###"));
                var e = career.Encounter;
                sb.Append(" magazine=").Append(e.Magazine).Append(" missiles=").Append(e.Missiles.Count);
                if (e.Missiles.Count > 0) sb.Append(" missile_km=").Append(F(e.Missiles[0].P.Mag, "0.#"));
                if (career.Build.MountCount > 0)
                {
                    sb.Append(" killed=").Append(e.Killed).Append(" bear_raider=").Append(career.MountsBearing(ship, e.Pirate.Pos - ship.Pos));
                    if (e.Missiles.Count > 0) sb.Append(" bear_missile=").Append(career.MountsBearing(ship, e.Missiles[0].P));
                }
            }
            if (career != null && career.Damage.Any)
                sb.Append(" dmg_drive=").Append(career.Damage.drive).Append(" dmg_sensors=").Append(career.Damage.sensors).Append(" dmg_radiators=").Append(career.Damage.radiators ? 1 : 0)
                  .Append(" dmg_mounts=").Append(PointDefense.Count(career.Damage.mounts));
            return sb.ToString();
        }

        // Called at the end of every simulated frame.
        void RecorderTick(float dt, int steps)
        {
            if (rec == null) { if (recFailed) return; RecorderOpen(); if (rec == null) return; }
            recT = Time.realtimeSinceStartup;

            // discrete states, compared with what was last written
            int n = 0;
            recNow[n++] = ship.Hold.ToString();
            recNow[n++] = (ship.PlanActive ? ship.Plan : Plan.None).ToString();
            recNow[n++] = ship.Phase;
            recNow[n++] = WarpNames[warpIdx];
            recNow[n++] = warpLimited ? "1" : "0";
            recNow[n++] = RecCam;
            recNow[n++] = ship.Target?.Name ?? "-";
            recNow[n++] = ship.VelRef?.Name ?? "orbit";
            recNow[n++] = ship.FlightAssist ? "1" : "0";
            recNow[n++] = ship.Landed?.Name ?? "-";
            recNow[n++] = (reference ?? sys.Sun).Name;
            recNow[n++] = F(Math.Round(ship.ThrottleCmd * 20) / 20, "0.##");
            recNow[n++] = ship.Throttle > 0 ? "1" : "0";
            recNow[n++] = ship.RotInput.sqrMagnitude > 0 ? V(ship.RotInput) : "0";
            recNow[n++] = ship.RcsInput.sqrMagnitude > 0 ? V(ship.RcsInput) : "0";
            recNow[n++] = ship.Dry ? "1" : "0";
            recNow[n++] = App.MenuOpen || App.HelpOpen ? "1" : "0";
            recNow[n++] = RecRaider();
            recNow[n++] = RecMissiles();
            recNow[n++] = career?.DamageText() ?? "-";
            string changes = null;
            for (int i = 0; i < RecFields.Length; i++)
            {
                if (recNow[i] == recLogged[i] || (recLogged[i] != null && recT - recLoggedAt[i] < 0.25f)) continue;
                changes += (changes == null ? "" : ", ") + RecFields[i] + ": " + (recLogged[i] ?? "?") + " -> " + recNow[i];
                recLogged[i] = recNow[i]; recLoggedAt[i] = recT;
            }
            string keys = Ctl.Fired.Count > 0 ? string.Join(",", Ctl.Fired) : "-";
            Ctl.Fired.Clear();
            if (changes != null) RecLine("EVT", changes + " | keys=" + keys + " | " + RecState());
            else if (keys != "-") RecLine("EVT", "keys=" + keys);

            RecorderRules(dt, steps);

            if (recT >= recRingAt)
            {
                recRingAt = recT + 1 / RecRingHz;
                recRing.Enqueue(F(recT, "0.00") + " R " + RecState());
                while (recRing.Count > RecRingSeconds * RecRingHz) recRing.Dequeue();
            }
            if (recT >= recSampleAt) { recSampleAt = recT + 1; RecLine("S", RecState() + " steps=" + steps); }

            if (Ctl.Pressed(A.MarkLog) && !App.KeysQuiet) RecorderMark();
        }

        // Things that should not happen, or that a pilot reads as the ship misbehaving. Each rule writes at most once in 5 s.
        void RecorderRules(float dt, int steps)
        {
            var p = ship.Pos; var v = ship.Vel; var q = ship.Att;
            double sum = p.x + p.y + p.z + v.x + v.y + v.z + q.x + q.y + q.z + q.w;
            if (double.IsNaN(sum) || double.IsInfinity(sum)) RecWarn("nan-state", null);
            if (ship.AngVel.magnitude > Ship.MaxRate * 1.01f) RecWarn("spin-over-limit", null);
            if (ship.Hold != Hold.Off && ship.RotInput.sqrMagnitude > 0 && steps > 0) RecWarn("hold-under-stick", null);
            if (ship.Hold != Hold.Custom) recCustomSince = -1;
            else if (recCustomSince < 0) recCustomSince = recT;
            else if (recT - recCustomSince > 20 && warpIdx == 0) RecWarn("flip-not-finished", null);
            if (steps > 0 && ship.ThrottleCmd > 0 && ship.Throttle == 0 && !ship.PlanActive && !ship.Dry && ship.Landed == null) RecWarn("drive-off-under-command", null);
            if (Time.unscaledDeltaTime > 0.25f) RecWarn("hitch", "frame=" + F(Time.unscaledDeltaTime, "0.00") + "s");

            if (ship.Target == null) { recClosing = double.NaN; recClosingTarget = null; recAimed = false; return; }
            TargetRel(out var r, out var vr, out double closing);
            // a pilot's burn straight at the target, far from anything that bends the path (gravity, or the swing of
            // the line to the target as the ship drifts across it)
            double drift2 = (vr - r.Norm * closing).Sqr;
            bool aimed = !ship.PlanActive && ship.Throttle > 0 && D3.Dot(ship.Heading, r.Norm) > 0.99
                && sys.Gravity(ship.Pos, jd).Mag < 0.1 * ship.AMax && drift2 / r.Mag < 0.1 * ship.AMax;
            // a move larger than speed and drive allow in one frame is a load or a QA move, not flight
            double span = dt * Warps[warpIdx];
            bool flown = (ship.Pos - recPos).Mag < 3 * (ship.Vel.Mag * span + ship.AMax * span * span);
            if (ship.Target == recClosingTarget && !double.IsNaN(recClosing) && steps > 0 && flown)
            {
                // moving apart after approaching, too fast to be in orbit there or to keep station (50 m/s): the
                // ship flew past the target
                if (recClosing > 0 && closing <= 0 && vr.Sqr > 2 * ship.Target.GM / r.Mag && vr.Sqr > 0.05 * 0.05) RecWarn("passed-target", "dist=" + F(r.Mag - ship.Target.Radius, "0.#"));
                // ...and the closing speed still falls, by no more than the drive could do (a larger jump is a load or a
                // QA move, not flight): nothing explains that
                double drop = recClosing - closing;
                if (aimed && recAimed && drop > 1e-6 && drop < 3 * ship.AMax * dt * Warps[warpIdx]) RecWarn("closing-falls-under-thrust", "was=" + F(recClosing, "0.######"));
            }
            recClosing = closing; recPos = ship.Pos; recClosingTarget = ship.Target; recAimed = aimed;
        }

        void RecorderMark()
        {
            RecLine("MARK", RecState());
            foreach (var l in recRing) RecRaw(l);
            RecLine("MARK", "end of the last " + F(RecRingSeconds, "0") + " s");
            Ctl.Tap(A.Screenshot);
            hudShell?.Toast("Flight log marked", Path.GetFileName(recPath));
        }
    }
}
