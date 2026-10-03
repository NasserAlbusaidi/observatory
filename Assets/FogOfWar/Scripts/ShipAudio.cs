using System;
using UnityEngine;

namespace FogOfWar
{
    // Ship sound, synthesised at start-up, so there are no third-party audio files and no licences to track.
    // Space carries no sound: this is what the crew hears through the hull (drive rumble, RCS thumps), the
    // console (notice chimes) and a quiet ambient drone.
    public class ShipAudio
    {
        const int Rate = 44100;
        readonly AudioSource drive, rcs, ambient, ui;
        readonly AudioClip good, bad;
        public bool Muted;

        public ShipAudio(Transform parent, Func<UnityEngine.Object, UnityEngine.Object> own)
        {
            var go = new GameObject("Ship audio");
            go.transform.SetParent(parent, false);
            AudioSource Src(AudioClip clip, bool loop)
            {
                var s = go.AddComponent<AudioSource>();
                s.clip = clip; s.loop = loop; s.spatialBlend = 0; s.playOnAwake = false; s.volume = 0;
                if (loop) s.Play();
                return s;
            }
            drive = Src((AudioClip)own(DriveClip()), true);
            rcs = Src((AudioClip)own(RcsClip()), true);
            ambient = Src((AudioClip)own(AmbientClip()), true);
            ui = Src(null, false);
            good = (AudioClip)own(Chime(new[] { 660.0, 990.0 }, 0.09));
            bad = (AudioClip)own(Chime(new[] { 220.0, 165.0 }, 0.14));
        }

        // throttle 0..1 of the rating; g the applied acceleration in g
        public void Update(double throttle, double g, bool rcsOn, float dt)
        {
            float m = Muted ? 0 : 1;
            float target = throttle > 0 ? (float)(0.25 + 0.35 * Math.Min(1, Math.Sqrt(g / 3))) : 0;
            drive.volume = Mathf.MoveTowards(drive.volume, target * m, dt * 1.5f);
            drive.pitch = 0.75f + 0.35f * Mathf.Clamp01((float)g / 3);
            rcs.volume = Mathf.MoveTowards(rcs.volume, (rcsOn ? 0.22f : 0) * m, dt * 8f);
            ambient.volume = Mathf.MoveTowards(ambient.volume, 0.12f * m, dt * 0.3f);
        }

        public void Notice(bool goodNews) { if (!Muted) ui.PlayOneShot(goodNews ? good : bad, 0.35f); }

        // ------------------------------------------------------------ synthesis

        static AudioClip Make(string name, float[] data)
        {
            var c = AudioClip.Create(name, data.Length, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }

        // Low rumble: brown noise through two one-pole low-passes, with a 41 Hz pump hum.
        static AudioClip DriveClip()
        {
            var rng = new System.Random(1);
            int n = Rate * 4, fade = Rate / 5;
            var d = new float[n + fade];
            double b = 0, l1 = 0, l2 = 0;
            for (int i = 0; i < d.Length; i++)
            {
                b = (b + (rng.NextDouble() * 2 - 1) * 0.02) * 0.998;
                l1 += (b - l1) * 0.05; l2 += (l1 - l2) * 0.05;
                double t = i / (double)Rate;
                d[i] = (float)(l2 * 6 + 0.18 * Math.Sin(2 * Math.PI * 41 * t) + 0.06 * Math.Sin(2 * Math.PI * 82 * t));
            }
            return Make("Drive", Looped(d, fade));
        }

        // Short gas thumps at about 9 per second.
        static AudioClip RcsClip()
        {
            var rng = new System.Random(2);
            int n = Rate; // 1 s, 9 pulses
            var d = new float[n];
            double hp = 0, prev = 0;
            for (int i = 0; i < n; i++)
            {
                double ph = (i * 9.0 / n) % 1.0;
                double env = Math.Exp(-ph * 14);
                double x = rng.NextDouble() * 2 - 1;
                hp = 0.9 * (hp + x - prev); prev = x; // high-pass: a hiss, not a rumble
                d[i] = (float)(hp * env * 0.5);
            }
            return Make("RCS", d);
        }

        // A slow drone: tones with whole cycles in 20 s so the loop is seamless, swelling on slow LFOs.
        static AudioClip AmbientClip()
        {
            int n = Rate * 20;
            var d = new float[n];
            double[] f = { 55, 82.5, 110.25, 164.85 };
            double[] lfo = { 0.05, 0.1, 0.15, 0.05 };
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate, s = 0;
                for (int k = 0; k < f.Length; k++)
                    s += Math.Sin(2 * Math.PI * f[k] * t) * (0.5 + 0.5 * Math.Sin(2 * Math.PI * lfo[k] * t + k)) / (k + 1);
                d[i] = (float)(s * 0.35);
            }
            return Make("Ambient", d);
        }

        static AudioClip Chime(double[] notes, double each)
        {
            int per = (int)(Rate * each), n = per * notes.Length + Rate / 10;
            var d = new float[n];
            for (int k = 0; k < notes.Length; k++)
                for (int i = 0; i < per + Rate / 10 && k * per + i < n; i++)
                {
                    double t = i / (double)Rate;
                    double env = Math.Min(1, t * 200) * Math.Exp(-t * 18);
                    d[k * per + i] += (float)(Math.Sin(2 * Math.PI * notes[k] * t) * env * 0.6);
                }
            return Make("Chime", d);
        }

        // Crossfade the extra tail into the head and drop it, so the clip loops cleanly.
        static float[] Looped(float[] d, int fade)
        {
            int n = d.Length - fade;
            var o = new float[n];
            Array.Copy(d, o, n);
            for (int i = 0; i < fade; i++)
            {
                float w = i / (float)fade;
                o[i] = d[i] * w + d[n + i] * (1 - w);
            }
            return o;
        }
    }
}
