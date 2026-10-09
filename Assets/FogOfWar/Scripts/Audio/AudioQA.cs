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


        IEnumerator Start()
        {
            Directory.CreateDirectory(dir);
            log = new StreamWriter(Path.Combine(dir, "audioqa.txt")) { AutoFlush = true };
            OpenSystemScene.CheckUnattendedSaveKey(log);
            app = GetComponent<FogOfWarApp>();
            app.InputLocked = true; Controls.Locked = true;
            GameSettings.SilentOutput = true; AudioListener.volume = 0; // record, but play nothing out loud
            bool cine = GameSettings.CinematicSound, on = GameSettings.ShipSound, bg = GameSettings.MuteInBackground;
            float vMaster = GameSettings.Master, vFx = GameSettings.Effects, vAmb = GameSettings.Music, vCh = GameSettings.Chimes;
            float savedMaster = PlayerPrefs.GetFloat("fow-set-vol-master", -1);
            GameSettings.Master = 0.8f; GameSettings.Effects = 0.8f; GameSettings.Music = 0.6f; GameSettings.Chimes = 0.8f;
            GameSettings.ShipSound = true; GameSettings.MuteInBackground = false; GameSettings.CinematicSound = false;
            Check(GameSettings.InMemory && PlayerPrefs.GetFloat("fow-set-vol-master", -1) == savedMaster, "QA levels stay in memory: the player's saved Master is untouched");
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

            int q0 = Mark();
            yield return new WaitForSecondsRealtime(1.5f);
            var quiet = Level(q0, Mark());
            Note("RCS: forward translation, then short taps");
            Controls.Hold(Controls.Action.RcsForward, true);
            yield return new WaitForSecondsRealtime(0.2f);
            int r0 = Mark();
            yield return new WaitForSecondsRealtime(1.5f);
            var jets = Level(r0, Mark());
            Check(sy.Rcs > 0.9f, $"RCS firing reaches the synth ({sy.Rcs:F2})");
            Controls.Hold(Controls.Action.RcsForward, false);
            Note($"meter, cockpit: idle RMS {quiet.rms:F1} peak {quiet.peak:F1} dBFS; RCS firing RMS {jets.rms:F1} peak {jets.peak:F1} dBFS");
            Check(jets.rms > quiet.rms + 6 && jets.peak > quiet.peak + 10, "RCS is audible: firing is clearly louder than the idle cockpit (QA-033)");
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

            yield return Sliders();

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
            yield return new WaitForSecondsRealtime(2f);

            Note("payday in the chase view: the console sting is heard outside the hull");
            int p0 = Mark();
            yield return new WaitForSecondsRealtime(1f);
            var still = Level(p0, Mark());
            bool wasSandbox = s.TestPayday(5000);
            int p1 = Mark();
            yield return new WaitForSecondsRealtime(1.5f);
            var pay = Level(p1, Mark());
            s.TestPaydayUndo(5000, wasSandbox);
            Note($"meter, chase view: quiet RMS {still.rms:F1} peak {still.peak:F1} dBFS; payday RMS {pay.rms:F1} peak {pay.peak:F1} dBFS");
            Check(pay.rms > still.rms + 6 && pay.peak > still.peak + 10, "a payday is clearly audible in the chase view");
            Put("camMode", 2);
            yield return new WaitForSecondsRealtime(3f);

            ShipSynthFilter.Tap = null;
            GameSettings.CinematicSound = cine; GameSettings.ShipSound = on; GameSettings.MuteInBackground = bg;
            GameSettings.Master = vMaster; GameSettings.Effects = vFx; GameSettings.Music = vAmb; GameSettings.Chimes = vCh;
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
            Application.Quit(fails == 0 ? 0 : 1);
        }

        int Mark() { lock (recLock) return rec.Count; }

        IEnumerator Meter(float seconds, Action<(float rms, float peak)> got)
        {
            yield return new WaitForSecondsRealtime(0.4f); // bus levels glide for about 150 ms
            int a = Mark();
            yield return new WaitForSecondsRealtime(seconds);
            got(Level(a, Mark()));
        }

        // Each Settings → Audio slider changes what plays (QA round 6), in the cockpit and with the menu open.
        IEnumerator Sliders()
        {
            (float rms, float peak) on = default, off = default;
            Note("sliders: drive at full burn in the cockpit, cabin bed muted");
            Put("camMode", 2);
            // the cabin bed and its random creaks would set the floor of the drive-off window, not the drive
            GameSettings.Music = 0;
            Controls.Tap(Controls.Action.ThrottleFull);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Meter(1f, l => on = l);
            GameSettings.Effects = 0;
            yield return Meter(1f, l => off = l);
            GameSettings.Effects = 0.8f;
            Note($"meter: drive {on.rms:F1} dBFS RMS; Drive and thrusters at 0 % {off.rms:F1}");
            Check(off.rms < on.rms - 15, "Drive, thrusters and machinery slider at 0 % silences the drive");
            GameSettings.Master = 0;
            yield return Meter(1f, l => off = l);
            GameSettings.Master = 0.8f;
            Note($"meter: Master at 0 % {off.rms:F1} dBFS RMS");
            Check(off.rms < -70, "Master slider at 0 % silences everything");
            // the drive bed wanders by a dB or two between one-second windows: alternate 80 % and 40 % and pool each level's power
            double pFull = 0, pHalf = 0;
            float peakFull = -200;
            for (int k = 0; k < 4; k++)
            {
                GameSettings.Master = 0.8f;
                yield return Meter(1f, l => { pFull += Math.Pow(10, l.rms / 10); peakFull = Mathf.Max(peakFull, l.peak); });
                GameSettings.Master = 0.4f;
                yield return Meter(1f, l => pHalf += Math.Pow(10, l.rms / 10));
            }
            GameSettings.Master = 0.8f;
            float drop = (float)(10 * Math.Log10(pFull / Math.Max(pHalf, 1e-30)));
            Note($"meter: Master 40 % against 80 %, four pairs pooled: {drop:F1} dB lower (80 % peak {peakFull:F1} dBFS)");
            Check(Mathf.Abs(drop - 6.02f) < 2, "Master slider at half level lowers the mix by about 6 dB");
            GameSettings.Music = 0.6f;

            Controls.Tap(Controls.Action.ThrottleCut);
            yield return new WaitForSecondsRealtime(3f);
            yield return Meter(1.5f, l => on = l);
            GameSettings.Music = 0;
            yield return Meter(1.5f, l => off = l);
            GameSettings.Music = 0.6f;
            Note($"meter: idle cockpit {on.rms:F1} dBFS RMS; Life support and hull at 0 % {off.rms:F1}");
            Check(off.rms < on.rms - 6, "Life support and hull slider at 0 % removes the cabin bed");

            var snd = (ShipAudio)Get(s, "sound");
            int c0 = Mark(); snd.Notice(true);
            yield return new WaitForSecondsRealtime(0.4f);
            on = Level(c0, Mark());
            GameSettings.Chimes = 0;
            yield return new WaitForSecondsRealtime(0.5f);
            c0 = Mark(); snd.Notice(true);
            yield return new WaitForSecondsRealtime(0.4f);
            off = Level(c0, Mark());
            GameSettings.Chimes = 0.8f;
            Note($"meter: chime peak {on.peak:F1} dBFS; Console and alarms at 0 % {off.peak:F1}");
            Check(off.peak < on.peak - 10, "Console and alarms slider at 0 % silences the chime");

            Note("sliders: Settings menu open (game paused)");
            app.ShowMenu(4);
            yield return new WaitForSecondsRealtime(3f); // past the preview from the last slider change
            yield return Meter(1f, l => off = l);
            GameSettings.Music = 0.9f;
            yield return Meter(1.2f, l => on = l);
            Note($"meter: menu open {off.rms:F1} dBFS RMS; after moving Life support {on.rms:F1}");
            Check(off.rms < -70, "the ship is silent under the menu");
            Check(on.rms > off.rms + 30, "moving a slider in the menu plays a preview at the new level");
            GameSettings.Music = 0.6f;
            app.ShowMenu(0);
            yield return new WaitForSecondsRealtime(3f);
        }

        (float rms, float peak) Level(int a, int b)
        {
            double sum = 0, peak = 0;
            lock (recLock)
                for (int i = a; i < b && i < rec.Count; i++) { double x = rec[i]; sum += x * x; peak = Math.Max(peak, Math.Abs(x)); }
            return ((float)(10 * Math.Log10(sum / Math.Max(b - a, 1) + 1e-12)), (float)(20 * Math.Log10(peak + 1e-9)));
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
