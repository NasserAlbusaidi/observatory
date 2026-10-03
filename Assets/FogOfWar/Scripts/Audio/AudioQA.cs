using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FogOfWar.Audio;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // Unattended audio check: `FogOfWar.exe -fowaudioqa <dir>` starts a career, drives the ship through the sounds
    // (views, drive, RCS, turns, undocking, low fuel) and checks the synth's parameters follow the game. It records
    // what the synth renders to audioqa.wav, with the speakers muted, and writes audioqa.txt.
    public class AudioQA : MonoBehaviour
    {
        string dir;
        StreamWriter log;
        FogOfWarApp app;
        OpenSystemScene s;
        int fails;
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        readonly List<float> rec = new List<float>(48000 * 2 * 60);
        readonly object recLock = new object();
        int recChannels = 2;

        public static void StartIfRequested(FogOfWarApp app)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-fowaudioqa");
            if (i < 0 || i + 1 >= args.Length) return;
            app.gameObject.AddComponent<AudioQA>().dir = args[i + 1];
        }

        object Get(object o, string field) => o.GetType().GetField(field, F).GetValue(o);
        void Put(string field, object v)
        {
            var f = s.GetType().GetField(field, F);
            f.SetValue(s, f.FieldType.IsEnum ? Enum.ToObject(f.FieldType, v) : v);
        }
        Ship ship => (Ship)Get(s, "ship");
        ShipSynth synth => (ShipSynth)Get(Get(s, "sound"), "synth");

        void Check(bool ok, string what) { if (!ok) fails++; log.WriteLine((ok ? "  PASS " : "  FAIL ") + what); }
        void Note(string what) => log.WriteLine($"[{Time.realtimeSinceStartup:F1}s] {what}");

        void Update() => AudioListener.volume = 0; // record, but play nothing out loud

        IEnumerator Start()
        {
            Directory.CreateDirectory(dir);
            log = new StreamWriter(Path.Combine(dir, "audioqa.txt")) { AutoFlush = true };
            app = GetComponent<FogOfWarApp>();
            app.InputLocked = true; Controls.Locked = true;
            bool cine = GameSettings.CinematicSound, on = GameSettings.ShipSound, bg = GameSettings.MuteInBackground;
            GameSettings.ShipSound = true; GameSettings.MuteInBackground = false; GameSettings.CinematicSound = false;
            ShipSynthFilter.Tap = (d, ch) => { lock (recLock) { recChannels = ch; rec.AddRange(d); } };
            Note($"output {AudioSettings.outputSampleRate} Hz, {AudioSettings.driverCapabilities}, batchmode {Application.isBatchMode}");

            yield return new WaitForSecondsRealtime(1f);
            app.Restart(app.GameIndex);
            yield return new WaitForSecondsRealtime(1.5f);
            s = (OpenSystemScene)app.Current;
            var sy = synth;
            Check(sy != null, "career has a ship synth");
            long b0 = ShipSynthFilter.Blocks;

            Note("chase view, docked at Earth orbit");
            yield return new WaitForSecondsRealtime(2f);
            Check(ShipSynthFilter.Blocks > b0, $"audio thread renders the synth ({ShipSynthFilter.Blocks - b0} blocks in 2 s)");
            Check(sy.Interior == 0, "chase view: listener outside the hull");

            Put("camMode", 2); // cockpit
            yield return new WaitForSecondsRealtime(3f);
            Note("cockpit view, idle: life support and wheels");
            Check(sy.Interior == 1, "cockpit view: listener inside the hull");

            Note("full burn: drive light, rumble, undock as the ship leaves the port");
            Controls.Tap(Controls.Action.ThrottleFull);
            yield return new WaitForSecondsRealtime(1f);
            Check(sy.Throttle > 0.9f && sy.ThrustG > 0.5f, $"full throttle reaches the synth (throttle {sy.Throttle:F2}, {sy.ThrustG:F2} g)");
            Put("warpIdx", 3);
            float t0 = Time.realtimeSinceStartup;
            while (s.Career.Docked != null && Time.realtimeSinceStartup - t0 < 10) yield return null;
            Put("warpIdx", 0);
            Check(s.Career.Docked == null, "burn leaves the port (undock sound queued)");
            yield return new WaitForSecondsRealtime(3f);
            Controls.Tap(Controls.Action.ThrottleCut);
            yield return new WaitForSecondsRealtime(2.5f);
            Check(sy.Throttle == 0, "throttle cut reaches the synth");

            Note("RCS: forward translation, then short taps");
            Controls.Hold(Controls.Action.RcsForward, true);
            yield return new WaitForSecondsRealtime(1.5f);
            Check(sy.Rcs > 0.9f, $"RCS firing reaches the synth ({sy.Rcs:F2})");
            Controls.Hold(Controls.Action.RcsForward, false);
            for (int i = 0; i < 3; i++)
            {
                yield return new WaitForSecondsRealtime(0.6f);
                Controls.Hold(Controls.Action.RcsUp, true);
                yield return new WaitForSecondsRealtime(0.12f);
                Controls.Hold(Controls.Action.RcsUp, false);
            }
            yield return new WaitForSecondsRealtime(1f);

            Note("turn: attitude jets and wheels");
            Controls.Hold(Controls.Action.YawRight, true);
            yield return new WaitForSecondsRealtime(1.5f);
            Controls.Hold(Controls.Action.YawRight, false);
            Check(sy.AngRate > 0.05f, $"turn rate reaches the synth ({sy.AngRate:F2} rad/s)");
            yield return new WaitForSecondsRealtime(3f);

            Note("low reaction mass: master caution");
            ship.Fuel = s.Career.TankCapacity * 0.1;
            yield return new WaitForSecondsRealtime(3f);

            Note("high warp in Earth's shadow cycle: thermal ticks stay small");
            Put("warpIdx", 3);
            float tMax = 0;
            for (float tw = 0; tw < 6; tw += Time.unscaledDeltaTime) { tMax = Mathf.Max(tMax, sy.Thermal); yield return null; }
            Put("warpIdx", 0);
            Check(tMax <= 0.1f, $"thermal kick at 1000x warp stays at or below 0.1 ({tMax:F2})");

            Note("chase view with the drive lit: silent hull, then cinematic sound");
            Controls.Tap(Controls.Action.ThrottleFull);
            Put("camMode", 0);
            yield return new WaitForSecondsRealtime(3f);
            Check(sy.Interior == 0 && sy.CinematicOn == 0, "outside view, cinematic off: hull buses muted");
            GameSettings.CinematicSound = true;
            yield return new WaitForSecondsRealtime(3f);
            Check(sy.CinematicOn == 1, "cinematic switch reaches the synth");
            GameSettings.CinematicSound = false;
            Controls.Tap(Controls.Action.ThrottleCut);
            Put("camMode", 2);
            yield return new WaitForSecondsRealtime(3f);

            ShipSynthFilter.Tap = null;
            GameSettings.CinematicSound = cine; GameSettings.ShipSound = on; GameSettings.MuteInBackground = bg;
            float[] data;
            int ch;
            lock (recLock) { data = rec.ToArray(); ch = recChannels; }
            if (data.Length > 0) WriteWav(Path.Combine(dir, "audioqa.wav"), data, ch, AudioSettings.outputSampleRate);
            double sum = 0, peak = 0;
            foreach (var x in data) { sum += x * x; peak = Math.Max(peak, Math.Abs(x)); }
            Note($"recorded {data.Length / Math.Max(ch, 1) / (float)AudioSettings.outputSampleRate:F1} s, {ch} ch, RMS {10 * Math.Log10(sum / Math.Max(data.Length, 1) + 1e-12):F1} dBFS, peak {20 * Math.Log10(peak + 1e-9):F1} dBFS");
            Check(data.Length > 0 && peak > 1e-3, "recording has sound in it");
            log.WriteLine(fails == 0 ? "AUDIO QA: all checks pass" : $"AUDIO QA: {fails} checks FAIL");
            log.Close();
            Application.Quit();
        }

        static void WriteWav(string path, float[] d, int ch, int rate)
        {
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int bytes = d.Length * 2;
                w.Write("RIFF".ToCharArray()); w.Write(36 + bytes); w.Write("WAVE".ToCharArray());
                w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)ch); w.Write(rate); w.Write(rate * ch * 2); w.Write((short)(ch * 2)); w.Write((short)16);
                w.Write("data".ToCharArray()); w.Write(bytes);
                foreach (var x in d) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(x * 32767), -32768, 32767));
            }
        }
    }
}
