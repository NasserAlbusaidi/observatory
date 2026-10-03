using System;

namespace FogOfWar.Audio
{
    public enum ShipSound
    {
        RcsPulse, RcsClose, DriveLight, DriveCut,
        Creak, Groan, ThermalTick, Ping,
        DockContact, ClampMotor, ClampMotorReverse, Latch, Equalise, PushOff, Touchdown, ScrubberValve,
        ChimeGood, ChimeBad, CautionTone, WarningBeep, ScanDone,
    }

    // The ship's sound, made sample by sample. The game writes the parameters once a frame and queues one-shot sounds;
    // the audio thread calls Render. Physical reasoning for each layer is in AUDIO-DESIGN.md.
    //
    // Buses: Machinery and Cabin are heard only from inside the hull (the cockpit view). Console (chimes, alarms) is
    // the pilot's interface and plays in every view. Cinematic is the not-physical exterior version, off by default.
    public sealed class ShipSynth
    {
        public const int Machinery = 0, Cabin = 1, Console = 2, Cinematic = 3, Buses = 4;

        // ---- parameters, written by the game thread (single float writes are atomic)
        public float Throttle;        // applied fraction of the drive rating, 0..1
        public float ThrustG;         // applied acceleration, g
        public bool RcsOn;            // any RCS jet firing this frame
        public float AngRate;         // body rotation rate, rad/s
        public float Thermal;         // 0..1 thermal stress (just crossed into or out of sunlight)
        public float PingRate;        // micrometeoroid hits per second
        public float Interior = 1;    // 1 listener inside the hull, 0 outside
        public float CinematicOn;     // 1 = play the not-physical exterior sound in outside views
        public float Master = 1;      // mute, menu and background fade
        public readonly float[] BusLevel = { 1, 1, 1, 1 };

        readonly float fs;
        Rng rng;
        readonly ModalVoice[] modal = new ModalVoice[40];
        readonly ToneVoice[] tone = new ToneVoice[16];
        readonly NoiseVoice[] noise = new NoiseVoice[16];
        float[][] busL = new float[Buses][], busR = new float[Buses][];
        readonly float[] busGain = new float[Buses];
        float master;

        // event queue: game thread → audio thread
        struct Pending { public ShipSound s; public float strength; public int at; }
        readonly Pending[] inbox = new Pending[64];
        int inboxCount;
        readonly object inboxLock = new object();
        readonly Pending[] due = new Pending[128];
        int dueCount;
        long clock; // samples rendered

        public ShipSynth(int sampleRate, uint seed = 1)
        {
            fs = sampleRate;
            rng = new Rng(seed);
            for (int i = 0; i < modal.Length; i++) modal[i] = new ModalVoice(fs, seed * 97 + (uint)i * 13 + 1);
            for (int i = 0; i < tone.Length; i++) tone[i] = new ToneVoice(fs, seed * 89 + (uint)i * 7 + 3);
            for (int i = 0; i < noise.Length; i++) noise[i] = new NoiseVoice(fs, seed * 83 + (uint)i * 11 + 5);
            Ensure(1024);
            InitBeds();
        }

        public int SampleRate => (int)fs;

        // Queue a one-shot sound `delay` seconds from now. Safe from any thread.
        public void Play(ShipSound s, float strength = 1f, float delay = 0f)
        {
            lock (inboxLock)
            {
                if (inboxCount >= inbox.Length) return;
                inbox[inboxCount++] = new Pending { s = s, strength = strength, at = (int)(delay * fs) };
            }
        }

        // Interleaved buffer, `channels` wide. Adds to what is already there.
        public void Render(float[] data, int channels)
        {
            int n = data.Length / channels;
            Ensure(n);
            for (int b = 0; b < Buses; b++) { Array.Clear(busL[b], 0, n); Array.Clear(busR[b], 0, n); }

            TakeInbox(n);
            if (!BedsOff) { Schedule(n / fs); RenderBeds(n); }
            foreach (var v in modal) if (v.Active) v.Render(busL[v.Bus], busR[v.Bus], n);
            foreach (var v in tone) if (v.Active) v.Render(busL[v.Bus], busR[v.Bus], n);
            foreach (var v in noise) if (v.Active) v.Render(busL[v.Bus], busR[v.Bus], n);

            // bus gains glide (about 150 ms), so view changes and mute never click
            float inside = Math.Clamp(Interior, 0, 1);
            float a = 1f - MathF.Exp(-1f / (0.15f * fs)), am = 1f - MathF.Exp(-1f / (0.03f * fs));
            float tMach = BusLevel[Machinery] * inside, tCab = BusLevel[Cabin] * inside, tCon = BusLevel[Console];
            float tCin = BusLevel[Cinematic] * (1f - inside) * Math.Clamp(CinematicOn, 0, 1);
            if (Solo >= 0) { tMach = Solo == Machinery ? 1 : 0; tCab = Solo == Cabin ? 1 : 0; tCon = Solo == Console ? 1 : 0; tCin = Solo == Cinematic ? 1 : 0; }
            float tMaster = Math.Clamp(Master, 0, 1);
            var l0 = busL[0]; var l1 = busL[1]; var l2 = busL[2]; var l3 = busL[3];
            var r0 = busR[0]; var r1 = busR[1]; var r2 = busR[2]; var r3 = busR[3];
            for (int i = 0; i < n; i++)
            {
                busGain[0] += a * (tMach - busGain[0]); busGain[1] += a * (tCab - busGain[1]);
                busGain[2] += a * (tCon - busGain[2]); busGain[3] += a * (tCin - busGain[3]);
                master += am * (tMaster - master);
                float L = (l0[i] * busGain[0] + l1[i] * busGain[1] + l2[i] * busGain[2] + l3[i] * busGain[3]) * master;
                float R = (r0[i] * busGain[0] + r1[i] * busGain[1] + r2[i] * busGain[2] + r3[i] * busGain[3]) * master;
                L = SoftClip(L); R = SoftClip(R);
                int o = i * channels;
                if (channels == 1) data[o] += 0.5f * (L + R);
                else { data[o] += L; data[o + 1] += R; }
            }
            clock += n;
        }

        // Renders only one bus at full gain, ignoring the view gating. For level checks in the offline harness.
        public int Solo = -1;
        public bool BedsOff; // one-shots only (harness)

        static float SoftClip(float x)
        {
            float ax = MathF.Abs(x);
            if (ax < 0.8f) return x;
            return MathF.Sign(x) * (0.8f + 0.2f * MathF.Tanh((ax - 0.8f) / 0.2f));
        }

        void Ensure(int n)
        {
            if (busL[0] != null && busL[0].Length >= n) return;
            for (int b = 0; b < Buses; b++) { busL[b] = new float[n]; busR[b] = new float[n]; }
        }

        void TakeInbox(int n)
        {
            lock (inboxLock)
            {
                for (int i = 0; i < inboxCount && dueCount < due.Length; i++) due[dueCount++] = inbox[i];
                inboxCount = 0;
            }
            // start everything due within this block (block-level timing: ~20 ms at 1024 samples)
            for (int i = 0; i < dueCount;)
            {
                if (due[i].at <= n) { Spawn(due[i].s, due[i].strength); due[i] = due[--dueCount]; }
                else { due[i].at -= n; i++; }
            }
        }

        // ================================================================ continuous layers ("beds")

        // drive
        float thr, g, gPrev, jerk, roughness;
        Osc pumpOsc, blade;
        Svf rumbleF, roarF;
        float brown;
        OnePole roughLp;
        bool driveWasOn;
        // attitude wheels
        float wheel;
        Osc wheelOsc;
        Svf bearingF;
        // life support
        Osc fan1, fan2, coolant;
        Svf airF;
        OnePole airLp;
        float pumpOn, pumpTarget, pumpTimer, scrubTimer;
        // schedulers
        float rcsTimer;
        bool rcsWas;

        void InitBeds()
        {
            roughLp.Set(7f, fs);
            airLp.Set(1400f, fs);
            airF.Set(320f, 0.6f, fs);
            bearingF.Set(400f, 6f, fs);
            rumbleF.Set(60f, 0.7f, fs);
            roarF.Set(500f, 0.5f, fs);
            pumpTarget = 1; pumpTimer = rng.Range(40, 80); scrubTimer = rng.Range(20, 50);
        }

        void RenderBeds(int n)
        {
            float dt = n / fs;
            // the turbopumps spool up in about half a second and run down more slowly
            float tTarget = Math.Clamp(Throttle, 0, 1);
            thr += (tTarget - thr) * (1f - MathF.Exp(-dt / (tTarget > thr ? 0.45f : 1.1f)));
            float gT = MathF.Max(ThrustG, 0);
            g += (gT - g) * (1f - MathF.Exp(-dt / 0.25f));
            jerk = jerk * MathF.Exp(-dt / 0.5f) + MathF.Abs(g - gPrev); // g changed recently (g, not g/s: one step of throttle counts once)
            gPrev = g;

            float lvl = MathF.Min(1.3f, MathF.Sqrt(g / 3f));   // structure-borne level grows with thrust
            float pumpF = 38f + 52f * thr;                      // pump shaft rate tracks propellant flow
            rumbleF.Set(35f + 45f * lvl, 0.8f, fs);
            roarF.Set(250f + 600f * thr, 0.5f, fs);
            float driveAmp = 0.16f * lvl;
            float cinAmp = 0.25f * MathF.Min(1f, thr * 1.5f);

            // attitude wheels: a momentum bias, spun harder as the ship turns
            float wT = 0.25f + 0.75f * MathF.Min(1f, AngRate / 0.6f);
            wheel += (wT - wheel) * (1f - MathF.Exp(-dt / 2.5f));
            float wheelF = 60f + 340f * wheel;
            bearingF.Set(wheelF * 2.03f, 6f, fs);

            // coolant pump cycles on and off
            pumpTimer -= dt;
            if (pumpTimer <= 0) { pumpTarget = pumpTarget > 0 ? 0 : 1; pumpTimer = pumpTarget > 0 ? rng.Range(50, 90) : rng.Range(15, 30); }
            pumpOn += (pumpTarget - pumpOn) * (1f - MathF.Exp(-dt / 1.2f));
            scrubTimer -= dt;
            if (scrubTimer <= 0) { Spawn(ShipSound.ScrubberValve, 1); scrubTimer = rng.Range(90, 150); }

            var mL = busL[Machinery]; var mR = busR[Machinery];
            var cL = busL[Cabin]; var cR = busR[Cabin];
            var xL = busL[Cinematic]; var xR = busR[Cinematic];
            bool drive = driveAmp > 1e-4f || thr > 1e-3f;
            for (int i = 0; i < n; i++)
            {
                float w = rng.Next();
                // ---- drive (machinery bus)
                if (drive)
                {
                    brown = (brown + w * 0.03f) * 0.997f;
                    rumbleF.Tick(brown);
                    roughness = roughLp.Tick(rng.Next());
                    float am = 1f + 1.2f * roughness;
                    float p = pumpOsc.Sine(pumpF, fs);
                    float a = 2f * MathF.PI * pumpOsc.Phase;
                    float tones = 0.5f * p + 0.28f * MathF.Sin(2 * a) + 0.12f * MathF.Sin(3 * a)
                                + 0.03f * blade.Sine(pumpF * 11f, fs); // blade pass, mostly lost in the structure
                    float d = (rumbleF.Low * 5f * am + tones * thr * 0.6f) * driveAmp;
                    mL[i] += d; mR[i] += d;
                    // cinematic exterior roar: not physical
                    roarF.Tick(w);
                    float c = (roarF.Band * 1.4f + rumbleF.Low * 3f) * cinAmp;
                    xL[i] += c; xR[i] += c;
                }
                // ---- attitude wheels (machinery)
                float wh = 0.010f * wheelOsc.Sine(wheelF, fs);
                bearingF.Tick(w);
                wh += 0.012f * bearingF.Band;
                mL[i] += wh; mR[i] += wh;
                // ---- life support (cabin): two fans slightly apart in speed beat against each other
                airF.Tick(w);
                float air = airLp.Tick(airF.Band) * 0.18f;
                float fans = 0.011f * fan1.Sine(118f, fs) + 0.009f * fan2.Sine(123.5f, fs);
                float cool = pumpOn * (0.020f * coolant.Sine(29.5f, fs) + 0.008f * MathF.Sin(4f * MathF.PI * coolant.Phase));
                cL[i] += air + fans * 1.2f + cool; cR[i] += air + fans * 0.8f + cool;
            }

            // drive light and cut: a jolt as the drive takes or drops the load
            bool on = Throttle > 0.01f;
            if (on && !driveWasOn) Spawn(ShipSound.DriveLight, 1);
            if (!on && driveWasOn && thr > 0.05f) Spawn(ShipSound.DriveCut, thr);
            driveWasOn = on;
        }

        // ================================================================ random and rate-driven events

        void Schedule(float dt)
        {
            // RCS: pulse-width-modulated jets, each pulse a valve opening and a thump
            if (RcsOn)
            {
                if (!rcsWas) rcsTimer = 0;
                rcsTimer -= dt;
                if (rcsTimer <= 0) { Spawn(ShipSound.RcsPulse, rng.Range(0.75f, 1f)); rcsTimer = rng.Range(0.05f, 0.13f); }
            }
            else if (rcsWas) Spawn(ShipSound.RcsClose, 1);
            rcsWas = RcsOn;

            // hull creaks: a slow background rate, more under g, many more just after the load changes, and ticks
            // while the hull is changing temperature
            float rate = 1f / 45f + 0.08f * MathF.Max(0, g - 0.3f) + 1.2f * MathF.Min(jerk, 2f) + 0.6f * Thermal;
            if (rng.Unit() < rate * dt)
            {
                float u = rng.Unit();
                if (Thermal > 0.1f && u < 0.6f * Thermal) Spawn(ShipSound.ThermalTick, rng.Range(0.4f, 1f));
                else if (u < (g > 0.5f ? 0.65f : 0.85f)) Spawn(ShipSound.Creak, MathF.Min(1f, 0.35f + 0.25f * g + jerk));
                else Spawn(ShipSound.Groan, MathF.Min(1f, 0.3f + 0.25f * g));
            }

            if (PingRate > 0 && rng.Unit() < PingRate * dt) Spawn(ShipSound.Ping, rng.Range(0.3f, 1f));
        }

        // ================================================================ one-shots

        ModalVoice M(int bus, float gain, float pan) { foreach (var v in modal) if (!v.Active) return v.Start(bus, gain, pan); return null; }
        ToneVoice T(int bus, float gain, float pan, float f0, float f1, float a, float h, float d)
        { foreach (var v in tone) if (!v.Active) return v.Start(bus, gain, pan, f0, f1, a, h, d); return null; }
        NoiseVoice N(int bus, float gain, float pan, float c0, float c1, float q, float a, float s, float r)
        { foreach (var v in noise) if (!v.Active) return v.Start(bus, gain, pan, c0, c1, q, a, s, r); return null; }

        void Spawn(ShipSound s, float k)
        {
            float pan = rng.Range(-0.5f, 0.5f), j = rng.Range(0.93f, 1.07f); // where and which panel: small variations
            switch (s)
            {
                case ShipSound.RcsPulse:
                    // solenoid valve snaps open (bright, very short), then the jet's reaction thumps the frame
                    M(Machinery, 0.10f * k, pan)?.Add(2900 * j, 0.006f, 1).Add(4300 * j, 0.004f, 0.6f).Add(6100 * j, 0.003f, 0.4f);
                    M(Machinery, 0.28f * k, pan)?.Add(80 * j, 0.06f, 1).Add(165 * j, 0.03f, 0.4f).Noise(0.008f, 600).Glide(0.6f);
                    N(Machinery, 0.10f * k, pan, 1100, 800, 1.5f, 0.002f, 0.025f, 0.04f);
                    if (CinematicOn > 0) N(Cinematic, 0.45f * k, pan * 1.6f, 2600, 1800, 0.8f, 0.004f, 0.06f, 0.12f);
                    break;
                case ShipSound.RcsClose:
                    M(Machinery, 0.07f, pan)?.Add(2600 * j, 0.005f, 1).Add(3900 * j, 0.004f, 0.5f);
                    break;
                case ShipSound.DriveLight:
                    M(Machinery, 0.22f, 0)?.Add(42, 0.35f, 1).Add(97, 0.2f, 0.5f).Add(230, 0.08f, 0.25f).Noise(0.03f, 300);
                    if (CinematicOn > 0) M(Cinematic, 0.6f, 0)?.Add(55, 0.5f, 1).Add(130, 0.3f, 0.5f).Noise(0.05f, 900);
                    break;
                case ShipSound.DriveCut:
                    M(Machinery, 0.35f * k, 0)?.Add(48, 0.25f, 1).Add(110, 0.12f, 0.4f).Noise(0.02f, 250);
                    break;
                case ShipSound.Creak:
                    // stick-slip: a joint slips in a burst of tiny steps, each ringing the same few panel modes
                    M(Cabin, 0.07f * k, pan * 1.6f)?.Add(rng.Range(160, 260), 0.03f, 1).Add(rng.Range(380, 520), 0.02f, 0.7f)
                        .Add(rng.Range(700, 1100), 0.012f, 0.4f).Train((int)rng.Range(8, 30), rng.Range(0.008f, 0.025f), 0.4f);
                    break;
                case ShipSound.Groan:
                    // a large panel or longeron flexing under load: low, slow, falling a little in pitch
                    M(Cabin, 0.028f * k, pan)?.Add(rng.Range(48, 75), 0.6f, 1).Add(rng.Range(110, 150), 0.35f, 0.5f)
                        .Noise(rng.Range(0.4f, 0.9f), 120).Glide(rng.Range(0.8f, 0.95f));
                    break;
                case ShipSound.ThermalTick:
                    // a fastener lets go as the hull expands or contracts: one sharp tick
                    M(Cabin, 0.035f * k, pan * 1.6f)?.Add(rng.Range(1400, 2400), 0.02f, 1).Add(rng.Range(3000, 4200), 0.01f, 0.5f);
                    break;
                case ShipSound.Ping:
                    // a dust grain hits the outer shield: inharmonic high modes of a thin plate, faint and short
                    M(Cabin, 0.05f * k, rng.Range(-1f, 1f))?.Add(rng.Range(1700, 2100), 0.09f, 1).Add(rng.Range(2900, 3300), 0.06f, 0.7f)
                        .Add(rng.Range(5000, 5600), 0.04f, 0.5f).Add(rng.Range(7000, 7600), 0.025f, 0.3f);
                    break;
                case ShipSound.DockContact:
                    M(Machinery, 0.32f, 0.2f)?.Add(46, 0.3f, 1).Add(95, 0.18f, 0.6f).Add(210, 0.08f, 0.3f).Noise(0.02f, 400);
                    if (CinematicOn > 0) M(Cinematic, 0.7f, 0)?.Add(70, 0.4f, 1).Add(180, 0.25f, 0.7f).Add(420, 0.1f, 0.4f).Noise(0.03f, 1500);
                    break;
                case ShipSound.ClampMotor:
                    T(Machinery, 0.05f, 0.2f, 140, 210, 0.08f, 1.1f, 0.1f)?.Harmonics(0.5f, 0.33f, 0.25f).LowPass(900);
                    break;
                case ShipSound.ClampMotorReverse:
                    T(Machinery, 0.05f, 0.2f, 210, 140, 0.08f, 0.9f, 0.1f)?.Harmonics(0.5f, 0.33f, 0.25f).LowPass(900);
                    break;
                case ShipSound.Latch:
                    M(Machinery, 0.16f, 0.2f + pan * 0.3f)?.Add(130 * j, 0.15f, 1).Add(310 * j, 0.08f, 0.7f).Add(620 * j, 0.05f, 0.5f).Add(1250 * j, 0.025f, 0.3f);
                    break;
                case ShipSound.Equalise:
                    // air through the equalisation valve as the hatch pressures match
                    N(Cabin, 0.22f, 0.2f, 1500, 600, 1.2f, 0.3f, 2.2f, 1.6f);
                    break;
                case ShipSound.PushOff:
                    M(Machinery, 0.23f, 0.2f)?.Add(60, 0.3f, 1).Add(140, 0.15f, 0.5f).Noise(0.04f, 300);
                    break;
                case ShipSound.Touchdown:
                    M(Machinery, 0.45f * k, 0)?.Add(32, 0.5f, 1).Add(55, 0.35f, 0.7f).Add(120, 0.15f, 0.4f).Add(260, 0.07f, 0.2f).Noise(0.05f, 250);
                    Spawn(ShipSound.Groan, 0.8f);
                    if (CinematicOn > 0) M(Cinematic, 0.8f * k, 0)?.Add(45, 0.6f, 1).Add(110, 0.3f, 0.6f).Noise(0.08f, 1200);
                    break;
                case ShipSound.ScrubberValve:
                    // CO₂ scrubber beds swap: a valve actuator clunk and a short rush of air
                    M(Cabin, 0.06f, -0.6f)?.Add(210, 0.08f, 1).Add(470, 0.05f, 0.5f).Noise(0.01f, 800);
                    N(Cabin, 0.05f, -0.6f, 900, 700, 1f, 0.05f, 0.6f, 0.8f);
                    break;
                case ShipSound.ChimeGood:
                    T(Console, 0.21f, 0, 660, 660, 0.005f, 0.005f, 0.055f)?.Harmonics(0.15f, 0, 0);
                    T(Console, 0.21f, 0, 990, 990, 0.005f, 0.005f, 0.055f)?.Harmonics(0.15f, 0, 0).Wait(0.09f);
                    break;
                case ShipSound.ScanDone:
                    T(Console, 0.16f, 0, 1320, 1320, 0.004f, 0.004f, 0.08f)?.Harmonics(0.1f, 0, 0);
                    break;
                case ShipSound.ChimeBad:
                    T(Console, 0.21f, 0, 220, 220, 0.005f, 0.005f, 0.055f)?.Harmonics(0.3f, 0.1f, 0);
                    T(Console, 0.21f, 0, 165, 165, 0.005f, 0.005f, 0.055f)?.Harmonics(0.3f, 0.1f, 0).Wait(0.14f);
                    break;
                case ShipSound.CautionTone:
                    // master caution: three two-tone cycles
                    for (int c = 0; c < 3; c++)
                    {
                        float t0 = c * 0.6f;
                        T(Console, 0.13f, 0, 880, 880, 0.01f, 0.17f, 0.03f)?.Harmonics(0.2f, 0.1f, 0).Wait(t0);
                        T(Console, 0.13f, 0, 660, 660, 0.01f, 0.17f, 0.03f)?.Harmonics(0.2f, 0.1f, 0).Wait(t0 + 0.2f);
                    }
                    break;
                case ShipSound.WarningBeep:
                    for (int c = 0; c < 8; c++) { float t0 = c * 0.16f; T(Console, 0.12f, 0, 1250, 1250, 0.004f, 0.08f, 0.01f)?.Harmonics(0.3f, 0.15f, 0.08f).Wait(t0); }
                    break;
            }
        }
    }
}
