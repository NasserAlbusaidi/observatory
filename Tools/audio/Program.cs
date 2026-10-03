using System;
using System.Collections.Generic;
using System.IO;
using FogOfWar.Audio;

// Renders scripted scenarios of the ship synth to WAV and prints their levels.
//   dotnet run -- [outDir]
// Levels are RMS and peak in dBFS over the whole file, plus the loudest 400 ms window (short-term RMS).
static class Program
{
    const int Rate = 48000, Block = 1024;

    static void Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : "out";
        Directory.CreateDirectory(dir);
        Console.WriteLine($"{"file",-28} {"RMS",7} {"short",7} {"peak",7}   dBFS");

        // ---- one layer at a time (the bus soloed, view gating ignored)
        Run(dir, "01-cabin-bed", 40, ShipSynth.Cabin, (s, t) => { });
        Run(dir, "02-drive-ramp", 26, ShipSynth.Machinery, (s, t) =>
        {
            float thr = t < 3 ? 0 : t < 9 ? 0.333f : t < 18 ? 1f : 0;
            s.Throttle = thr; s.ThrustG = thr * 3f;
        });
        Run(dir, "03-drive-1g", 10, ShipSynth.Machinery, (s, t) => { s.Throttle = 1; s.ThrustG = 1; });
        Run(dir, "04-drive-3g", 10, ShipSynth.Machinery, (s, t) => { s.Throttle = 1; s.ThrustG = 3; });
        Run(dir, "05-rcs", 12, ShipSynth.Machinery, (s, t) =>
        {
            s.Rcs = ((t > 1 && t < 1.4) || (t > 3 && t < 5.5) || (t > 7 && t < 7.1) || (t > 8 && t < 8.1) || (t > 9 && t < 9.15)) ? 1 : 0;
        });
        Run(dir, "05b-rcs-autopilot-trim", 12, ShipSynth.Machinery, (s, t) => { s.Rcs = 0.06f; });
        Run(dir, "06-wheels-slew", 16, ShipSynth.Machinery, (s, t) => { s.AngRate = t > 3 && t < 9 ? 0.6f : 0; });
        Run(dir, "07-creaks-under-g", 30, ShipSynth.Cabin, (s, t) =>
        {
            float thr = t < 4 ? 0 : t < 12 ? 0.333f : t < 22 ? 1f : 0;
            s.Throttle = thr; s.ThrustG = thr * 3f;
        });
        Run(dir, "08-thermal", 25, ShipSynth.Cabin, (s, t) => { s.Thermal = t > 3 ? MathF.Exp(-(t - 3) / 12f) : 0; });
        Run(dir, "09-dock-undock", 14, -1, (s, t) => { }, new List<(float, ShipSound)>
        {
            (1.0f, ShipSound.DockContact), (1.6f, ShipSound.ClampMotor), (2.9f, ShipSound.Latch), (3.15f, ShipSound.Latch), (3.35f, ShipSound.Latch), (3.8f, ShipSound.Equalise),
            (10f, ShipSound.Latch), (10.2f, ShipSound.Latch), (10.4f, ShipSound.ClampMotorReverse), (11.6f, ShipSound.PushOff),
        }, interior: true);
        Run(dir, "10-touchdown", 6, -1, (s, t) => { }, new List<(float, ShipSound)> { (1f, ShipSound.Touchdown) }, interior: true);
        Run(dir, "11-pings", 10, ShipSynth.Cabin, (s, t) => { }, new List<(float, ShipSound)>
            { (1f, ShipSound.Ping), (2.5f, ShipSound.Ping), (4f, ShipSound.Ping), (6.2f, ShipSound.Ping), (8f, ShipSound.Ping) });
        Run(dir, "12-console", 12, ShipSynth.Console, (s, t) => { }, new List<(float, ShipSound)>
            { (0.5f, ShipSound.ChimeGood), (2f, ShipSound.ChimeBad), (3.5f, ShipSound.ScanDone), (5f, ShipSound.CautionTone), (8.5f, ShipSound.WarningBeep) });
        Run(dir, "13-cinematic-exterior", 14, -1, (s, t) =>
        {
            s.Interior = 0; s.CinematicOn = 1;
            float thr = t > 2 && t < 9 ? 1 : 0; s.Throttle = thr; s.ThrustG = thr * 3;
            s.Rcs = (t > 10 && t < 11.5f) ? 1 : 0;
        }, null, interior: false);
        Run(dir, "14-exterior-default-silent", 8, -1, (s, t) =>
        {
            s.Interior = 0; s.CinematicOn = 0; s.Throttle = 1; s.ThrustG = 3; s.Rcs = (t > 4 && t < 5) ? 1 : 0;
        }, null, interior: false);

        // ---- every one-shot alone, for peak levels
        foreach (ShipSound e in Enum.GetValues(typeof(ShipSound)))
            Run(dir, "ev-" + e, 4, -1, (s, t) => { }, new List<(float, ShipSound)> { (0.2f, e) }, interior: true, quiet: true);

        // ---- the full cockpit mix: a short flight
        Run(dir, "20-cockpit-flight", 60, -1, (s, t) =>
        {
            s.AngRate = t > 4 && t < 9 ? 0.5f : 0.05f;
            s.Rcs = ((t > 4 && t < 4.6) || (t > 8.4 && t < 9) || (t > 47 && t < 48.5)) ? 1 : 0;
            float thr = t < 11 ? 0 : t < 20 ? 0.333f : t < 34 ? 1f : 0;
            s.Throttle = thr; s.ThrustG = thr * 3;
            s.Thermal = t > 36 ? MathF.Exp(-(t - 36) / 10f) : 0;
            s.PingRate = 0.05f;
        }, new List<(float, ShipSound)>
        {
            (2f, ShipSound.ChimeGood), (40f, ShipSound.CautionTone), (50f, ShipSound.DockContact), (50.6f, ShipSound.ClampMotor),
            (51.9f, ShipSound.Latch), (52.15f, ShipSound.Latch), (52.35f, ShipSound.Latch), (52.8f, ShipSound.Equalise), (56f, ShipSound.ChimeGood),
        }, interior: true);
        // mute fade and view crossfade: no clicks expected
        Run(dir, "21-view-switch-and-mute", 16, -1, (s, t) =>
        {
            s.Throttle = 1; s.ThrustG = 2; s.CinematicOn = 0;
            s.Interior = t < 4 || (t > 8 && t < 10) ? 1 : 0;
            s.Master = t > 12 && t < 14 ? 0 : 1;
        }, null, interior: true);
    }

    static void Run(string dir, string name, float seconds, int solo, Action<ShipSynth, float> drive, List<(float at, ShipSound s)> events = null, bool interior = true, bool quiet = false)
    {
        var s = new ShipSynth(Rate, 7) { Solo = solo, Interior = interior ? 1 : 0, BedsOff = quiet };
        int total = (int)(seconds * Rate);
        var outL = new float[total]; var outR = new float[total];
        var buf = new float[Block * 2];
        int done = 0, ev = 0;
        events?.Sort((a, b) => a.at.CompareTo(b.at));
        while (done < total)
        {
            int n = Math.Min(Block, total - done);
            if (buf.Length != n * 2) buf = new float[n * 2];
            Array.Clear(buf, 0, buf.Length);
            float t = done / (float)Rate;
            drive(s, t);
            while (events != null && ev < events.Count && events[ev].at <= t + n / (float)Rate)
            {
                s.Play(events[ev].s, 1f, Math.Max(0, events[ev].at - t));
                ev++;
            }
            s.Render(buf, 2);
            for (int i = 0; i < n; i++) { outL[done + i] = buf[2 * i]; outR[done + i] = buf[2 * i + 1]; }
            done += n;
        }
        if (!quiet) WriteWav(Path.Combine(dir, name + ".wav"), outL, outR);
        Report(name, outL, outR);
    }

    static void Report(string name, float[] l, float[] r)
    {
        double sum = 0, peak = 0, best = 0;
        int win = Rate * 4 / 10;
        double wsum = 0, step = 0;
        for (int i = 0; i < l.Length; i++)
        {
            double e = 0.5 * (l[i] * l[i] + r[i] * r[i]);
            sum += e; wsum += e;
            if (i >= win) { double o = 0.5 * (l[i - win] * l[i - win] + r[i - win] * r[i - win]); wsum -= o; }
            if (i >= win - 1) best = Math.Max(best, wsum / win);
            peak = Math.Max(peak, Math.Max(Math.Abs(l[i]), Math.Abs(r[i])));
            if (i > 0) step = Math.Max(step, Math.Abs(l[i] - l[i - 1]));
        }
        string D(double p) => p <= 1e-12 ? "  -inf" : (10 * Math.Log10(p)).ToString("F1").PadLeft(6);
        Console.WriteLine($"{name,-28} {D(sum / l.Length),7} {D(best),7} {D(peak * peak),7}  step {step:F3}");
    }

    static void WriteWav(string path, float[] l, float[] r)
    {
        using var w = new BinaryWriter(File.Create(path));
        int n = l.Length, bytes = n * 4;
        w.Write("RIFF".ToCharArray()); w.Write(36 + bytes); w.Write("WAVE".ToCharArray());
        w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(Rate); w.Write(Rate * 4); w.Write((short)4); w.Write((short)16);
        w.Write("data".ToCharArray()); w.Write(bytes);
        for (int i = 0; i < n; i++)
        {
            w.Write((short)Math.Clamp((int)Math.Round(l[i] * 32767), -32768, 32767));
            w.Write((short)Math.Clamp((int)Math.Round(r[i] * 32767), -32768, 32767));
        }
    }
}
