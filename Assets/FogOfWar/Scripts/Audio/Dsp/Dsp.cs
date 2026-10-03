using System;

namespace FogOfWar.Audio
{
    // Small DSP parts for the ship synth. Nothing here uses UnityEngine, so the same files also build in Tools/audio
    // for offline renders and level checks.

    // xorshift32: cheap, allocation-free noise for the audio thread
    public struct Rng
    {
        uint s;
        public Rng(uint seed) { s = seed == 0 ? 1u : seed; }
        public float Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFFFF) / 8388608f - 1f; } // −1..1
        public float Unit() => (Next() + 1f) * 0.5f;                                                          // 0..1
        public float Range(float a, float b) => a + (b - a) * Unit();
    }

    public struct OnePole
    {
        float a, y;
        public void Set(float fc, float fs) => a = 1f - MathF.Exp(-2f * MathF.PI * fc / fs);
        public float Tick(float x) => y += a * (x - y);
        public float Value => y;
    }

    // Zero-delay state-variable filter (Simper). Low, band and high outputs from one update.
    public struct Svf
    {
        float a1, a2, a3, k, ic1, ic2;
        public float Low, Band, High;
        public void Set(float fc, float q, float fs)
        {
            float g = MathF.Tan(MathF.PI * MathF.Min(fc, fs * 0.45f) / fs);
            k = 1f / q;
            a1 = 1f / (1f + g * (g + k)); a2 = g * a1; a3 = g * a2;
        }
        public void Tick(float x)
        {
            float v3 = x - ic2, v1 = a1 * ic1 + a2 * v3, v2 = ic2 + a2 * ic1 + a3 * v3;
            ic1 = 2f * v1 - ic1; ic2 = 2f * v2 - ic2;
            Low = v2; Band = v1; High = x - k * v1 - v2;
        }
    }

    // Two-pole resonator: one vibration mode of a panel, strut or valve body. An impulse in gives a decaying sine
    // of about `amp` peak at `freq`; `decay` is the time to fall to 1/e.
    public struct Mode
    {
        float b, c1, c2, y1, y2, r, amp, fs;
        public float Freq;
        // scale for a noise input, so a sustained noise drive settles near `amp` RMS whatever the decay
        public float NoiseNorm => 2f * MathF.Sqrt(1f - r);
        public void Set(float freq, float decay, float amp, float fs)
        {
            this.fs = fs; this.amp = amp; r = MathF.Exp(-1f / (MathF.Max(decay, 1e-4f) * fs));
            Retune(freq);
            y1 = y2 = 0;
        }
        public void Retune(float freq)
        {
            Freq = MathF.Min(freq, fs * 0.45f);
            float w = 2f * MathF.PI * Freq / fs;
            c1 = 2f * r * MathF.Cos(w); c2 = -r * r; b = amp * MathF.Sin(w);
        }
        public float Tick(float x) { float y = b * x + c1 * y1 + c2 * y2; y2 = y1; y1 = y; return y; }
    }

    // A sine oscillator with a running phase (0..1), so frequency can change without clicks.
    public struct Osc
    {
        public float Phase;
        public float Sine(float freq, float fs)
        {
            Phase += freq / fs; if (Phase >= 1f) Phase -= 1f;
            return MathF.Sin(2f * MathF.PI * Phase);
        }
    }

    public static class Db
    {
        public static float ToGain(float db) => MathF.Pow(10f, db / 20f);
        public static float FromGain(float g) => 20f * MathF.Log10(MathF.Max(g, 1e-9f));
    }
}
