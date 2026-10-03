using System;

namespace FogOfWar.Audio
{
    // One-shot voices. The synth keeps fixed pools of each and reuses them, so the audio thread never allocates.
    public abstract class Voice
    {
        public bool Active;
        public int Bus;
        public float Gain, PanL, PanR;
        protected float fs;
        protected int t, life;
        protected Rng rng;

        protected Voice(float fs, uint seed) { this.fs = fs; rng = new Rng(seed); }

        protected void Begin(int bus, float gain, float pan)
        {
            Active = true; Bus = bus; Gain = gain; t = 0; life = 0;
            float p = (Math.Clamp(pan, -1f, 1f) + 1f) * 0.25f * MathF.PI; // constant-power pan
            PanL = MathF.Cos(p) * 1.4142f; PanR = MathF.Sin(p) * 1.4142f;
        }

        // Start `seconds` late (sequences such as a docking or an alarm pattern queue their parts this way).
        public void Wait(float seconds) => t = -(int)(seconds * fs);

        // Render `n` samples into the bus buffers.
        public void Render(float[] l, float[] r, int n)
        {
            for (int i = 0; i < n; i++)
            {
                if (t < 0) { t++; continue; }
                float y = Tick() * Gain;
                l[i] += y * PanL; r[i] += y * PanR;
                if (++t >= life) { Active = false; return; }
            }
        }

        protected abstract float Tick();
    }

    // Struck or rubbed metal: up to six vibration modes, excited by one impulse, a train of stick-slip impulses
    // (creaks), or a short noise burst (thuds). Modes can glide in pitch (a thump that drops as a panel unloads).
    public sealed class ModalVoice : Voice
    {
        readonly Mode[] modes = new Mode[6];
        int n;
        enum Ex { Impulse, Train, Noise }
        Ex ex;
        int exLen, nextImpulse, trainLeft;
        float trainGap, trainJitter, glide;
        OnePole exLp;

        public ModalVoice(float fs, uint seed) : base(fs, seed) { }

        public ModalVoice Start(int bus, float gain, float pan)
        {
            Begin(bus, gain, pan); n = 0; exLen = 0; glide = 1f; ex = Ex.Impulse; nextImpulse = 0; trainLeft = 1;
            exLp = default; exLp.Set(fs * 0.2f, fs);
            return this;
        }

        public ModalVoice Add(float freq, float decay, float amp)
        {
            if (n >= modes.Length) return this;
            modes[n].Set(freq, decay, amp, fs); n++;
            life = Math.Max(life, exLen + (int)(decay * 7f * fs));
            return this;
        }

        // pitch multiplier per second (0.5 = falls an octave each second)
        public ModalVoice Glide(float perSecond) { glide = MathF.Pow(perSecond, 32f / fs); return this; }

        public ModalVoice Train(int count, float gap, float jitter)
        {
            ex = Ex.Train; trainLeft = count; trainGap = gap; trainJitter = jitter;
            int extra = (int)(count * gap * (1f + jitter) * fs);
            exLen = extra; life += extra;
            return this;
        }

        // noise burst of `len` seconds through a low-pass at `bright` Hz: a softer, heavier strike than an impulse
        public ModalVoice Noise(float len, float bright)
        {
            ex = Ex.Noise; exLen = (int)(len * fs); life += exLen; exLp.Set(bright, fs);
            return this;
        }

        protected override float Tick()
        {
            float x = 0;
            switch (ex)
            {
                case Ex.Impulse: x = t == 0 ? 1f : 0f; break;
                case Ex.Train:
                    if (trainLeft > 0 && t >= nextImpulse)
                    {
                        x = rng.Range(0.35f, 1f) * (rng.Unit() < 0.5f ? -1f : 1f);
                        trainLeft--;
                        nextImpulse = t + Math.Max(1, (int)(trainGap * (1f + trainJitter * rng.Next()) * fs));
                    }
                    break;
                case Ex.Noise:
                    if (t < exLen) x = exLp.Tick(rng.Next()) * (1f - t / (float)exLen) * 4f;
                    break;
            }
            if (glide != 1f && (t & 31) == 0)
                for (int k = 0; k < n; k++) modes[k].Retune(modes[k].Freq * glide);
            float y = 0;
            if (ex == Ex.Noise) for (int k = 0; k < n; k++) y += modes[k].Tick(x * modes[k].NoiseNorm);
            else for (int k = 0; k < n; k++) y += modes[k].Tick(x);
            return y;
        }
    }

    // A pitched tone with up to four harmonics, a pitch glide and an attack/decay envelope: console chimes, alarm
    // beeps, the whine of a clamp motor.
    public sealed class ToneVoice : Voice
    {
        Osc osc;
        float f0, f1, h2, h3, h4, attack, hold, decay;
        int dur;
        OnePole lp;
        bool useLp;

        public ToneVoice(float fs, uint seed) : base(fs, seed) { }

        // f0→f1 over `hold` seconds; full level from `attack` to `hold`, then an exponential tail of `decay` seconds
        public ToneVoice Start(int bus, float gain, float pan, float f0, float f1, float attack, float hold, float decay)
        {
            Begin(bus, gain, pan);
            this.f0 = f0; this.f1 = f1; this.attack = MathF.Max(attack, 0.001f) * fs; this.hold = hold * fs; this.decay = MathF.Max(decay, 0.001f) * fs;
            dur = (int)this.hold; h2 = h3 = h4 = 0; useLp = false; osc.Phase = 0;
            life = (int)(this.hold + this.decay * 7f);
            return this;
        }

        public ToneVoice Harmonics(float a2, float a3, float a4) { h2 = a2; h3 = a3; h4 = a4; return this; }
        public ToneVoice LowPass(float fc) { lp = default; lp.Set(fc, fs); useLp = true; return this; }

        protected override float Tick()
        {
            float f = dur > 0 ? f0 + (f1 - f0) * MathF.Min(1f, t / (float)dur) : f0;
            float p0 = osc.Phase;
            float y = osc.Sine(f, fs);
            if (h2 != 0 || h3 != 0 || h4 != 0)
            {
                float a = 2f * MathF.PI * p0;
                y += h2 * MathF.Sin(2f * a) + h3 * MathF.Sin(3f * a) + h4 * MathF.Sin(4f * a);
            }
            float env = MathF.Min(1f, t / attack) * (t < hold ? 1f : MathF.Exp(-(t - hold) / decay));
            y *= env;
            return useLp ? lp.Tick(y) : y;
        }
    }

    // Filtered noise with a moving centre frequency and an attack/sustain/release envelope: gas venting through a
    // valve, air rushing in when a hatch equalises.
    public sealed class NoiseVoice : Voice
    {
        Svf f;
        float c0, c1, q, attack, sustain, release;

        public NoiseVoice(float fs, uint seed) : base(fs, seed) { }

        public NoiseVoice Start(int bus, float gain, float pan, float c0, float c1, float q, float attack, float sustain, float release)
        {
            Begin(bus, gain, pan);
            this.c0 = c0; this.c1 = c1; this.q = q;
            this.attack = MathF.Max(attack, 0.001f) * fs; this.sustain = sustain * fs; this.release = MathF.Max(release, 0.001f) * fs;
            life = (int)(this.attack + this.sustain + this.release);
            f = default; f.Set(c0, q, fs);
            return this;
        }

        protected override float Tick()
        {
            if ((t & 31) == 0) f.Set(c0 + (c1 - c0) * MathF.Min(1f, t / (float)life), q, fs);
            f.Tick(rng.Next());
            float env = t < attack ? t / attack : t < attack + sustain ? 1f : 1f - (t - attack - sustain) / release;
            return f.Band * env * env;
        }
    }
}
