using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace FogOfWar.OpenWorld
{
    // The matter a missile hit or kill throws out (docs/design-combat.md "The sparks (fx-2)"): hot droplets that glow and cool
    // as blackbodies, and cold chunks lit by the Sun. A burst is a seed, an age and a few numbers; SparksCompute.compute works
    // out every piece from them each frame and appends the ones bright enough to see, and Sparks.shader draws them with one
    // indirect draw. Positions are in metres in the ship's frame (Unity axes, not turning with it). Off without compute
    // shaders: the flash alone is drawn then.
    public sealed class Sparks : IDisposable
    {
        const int Slots = 8, PerBurst = 32768;
        const int HitHot = 16384, HitCold = 24, KillHot = 2048, KillCold = 40;
        const int Capacity = Slots * (HitHot + HitCold); // every piece of a full set of hits drawn at once
        const float HitLife = 20, KillLife = 3; // s: past these every piece is gone or far away
        const double SpecificJ = 50e6;          // [design] E / 50 MJ is the hot mass
        const double RoundKg = 0.05;            // [design] a PDC round
        const float MinPixel = 0.003f;          // a piece whose pixels sum under this (white in full sunlight = 1) is not drawn

        [StructLayout(LayoutKind.Sequential)]
        struct Burst
        {
            public Vector3 origin; public float age;
            public Vector3 axis; public float hotCos;
            public Vector3 vBase; public float vMin;
            public Vector3 disp; public float vMax;
            public float hotKg, coldKg, coldVMin, coldVMax;
            public uint nHot, nCold, seed; public float coldCos;
            public uint absorb, pad0, pad1, pad2;
        }

        readonly Burst[] bursts = new Burst[Slots], live = new Burst[Slots];
        readonly float[] at = new float[Slots], life = new float[Slots];
        readonly D3[] vel0 = new D3[Slots];
        readonly bool[] used = new bool[Slots];
        readonly int[] kinds = new int[Slots];
        D3 velNow;
        int next, last = -1, held = -1; // held: the burst PinAge froze, at heldAge
        float heldAge;
        uint serial;

        readonly ComputeShader cs;
        readonly int kernel;
        readonly GraphicsBuffer burstBuf, outBuf, argsBuf;
        readonly Material mat;
        readonly Texture2D lut;
        readonly uint[] args = { 6, 0, 0, 0 };

        public bool Ok { get; }
        public int Count { get; private set; }   // bursts alive
        public int Fired { get; private set; }   // review checks: bursts started
        public int LastKind { get; private set; } // review checks: 0 hit, 1 kill

        static readonly int idBursts = Shader.PropertyToID("_Bursts"), idBurstCount = Shader.PropertyToID("_BurstCount"),
            idOut = Shader.PropertyToID("_Out"), idLut = Shader.PropertyToID("_BlackbodyLUT"), idToWorld = Shader.PropertyToID("_ToWorld"),
            idToHull = Shader.PropertyToID("_ToHull"), idCamM = Shader.PropertyToID("_CamM"), idSunDir = Shader.PropertyToID("_SunDir"),
            idSunIrr = Shader.PropertyToID("_SunIrr"), idShutter = Shader.PropertyToID("_Shutter"), idMinE = Shader.PropertyToID("_MinE"),
            idSparks = Shader.PropertyToID("_Sparks");

        public Sparks()
        {
            if (!SystemInfo.supportsComputeShaders) { Debug.LogWarning("Fog of War: no compute shaders, impact sparks off"); return; }
            cs = Resources.Load<ComputeShader>("Shaders/SparksCompute");
            if (cs == null) { Debug.LogError("Fog of War: missing compute shader Resources/Shaders/SparksCompute"); return; }
            kernel = cs.FindKernel("Sparks");
            mat = Gfx.Mat("Sparks");
            burstBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Slots, Marshal.SizeOf<Burst>());
            outBuf = new GraphicsBuffer(GraphicsBuffer.Target.Append | GraphicsBuffer.Target.Structured, Capacity, sizeof(float) * 9);
            argsBuf = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawArgs.size);
            lut = BlackbodyLut();
            mat.SetBuffer(idSparks, outBuf);
            Ok = true;
        }

        // log10 T from 3 to 4: rgb the colour at unit luminance, a the visible share (Blackbody)
        static Texture2D BlackbodyLut()
        {
            var t = new Texture2D(256, 1, TextureFormat.RGBAFloat, false, true)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Blackbody LUT", hideFlags = HideFlags.DontSave };
            var px = new Color[256];
            for (int i = 0; i < 256; i++)
            {
                float k = Mathf.Pow(10, 3 + i / 255f);
                var c = Blackbody.Colour(k);
                c.a = Blackbody.VisibleShare(k);
                px[i] = c;
            }
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }

        // A missile hit at `offKm` from the ship's middle (sim frame) on a hull face with normal `normal`; `from` the unit
        // direction it came in from, `kms` its closing speed
        public void Hit(D3 offKm, D3 normal, D3 from, double kms, double missileKg, D3 shipVel)
        {
            double v = kms * 1e3;
            var b = new Burst
            {
                origin = Frame.U(offKm * 1e3), axis = Frame.U((normal + from).Norm), hotCos = Mathf.Cos(50 * Mathf.Deg2Rad),
                vMin = 30, vMax = (float)(v / 2), hotKg = (float)(0.5 * missileKg * v * v / SpecificJ),
                coldKg = 20, coldVMin = 5, coldVMax = 30, coldCos = Mathf.Cos(50 * Mathf.Deg2Rad), nHot = HitHot, nCold = HitCold,
            };
            Start(b, HitLife, shipVel, 0);
        }

        // A PDC kill at `offKm` (sim frame), the missile closing at `kms` along -`from`: its debris keeps that velocity
        public void Kill(D3 offKm, D3 from, double kms, double missileKg, D3 shipVel)
        {
            double closing = (kms + PointDefense.RoundKms) * 1e3, e = 0.5 * RoundKg * closing * closing;
            var b = new Burst
            {
                origin = Frame.U(offKm * 1e3), axis = Frame.U(from), hotCos = 0.5f, vBase = Frame.U(from * (-kms * 1e3)),
                vMin = 200, vMax = (float)(closing / 2), hotKg = (float)(e / SpecificJ),
                coldKg = (float)missileKg, coldVMin = 5, coldVMax = (float)Math.Sqrt(2 * 0.1 * e / missileKg), coldCos = -1,
                nHot = KillHot, nCold = KillCold, absorb = 1,
            };
            Start(b, KillLife, shipVel, 1);
        }

        void Start(Burst b, float lifeS, D3 shipVel, int kind)
        {
            if (!Ok) return;
            serial++;
            b.seed = serial * 2654435761u;
            int i = next;
            next = (next + 1) % Slots;
            bursts[i] = b;
            at[i] = Time.unscaledTime;
            life[i] = lifeS;
            vel0[i] = shipVel;
            used[i] = true;
            kinds[i] = kind;
            last = i;
            held = -1;
            Fired++;
            LastKind = kind;
        }

        // review checks: the newest burst of `kind` (0 hit, 1 kill) held at `age` s, with no burn since, until the next burst
        // or Clear; the others dropped. Held, not started `age` s ago: kill debris crosses 167 m to the hull in 17 ms, so one
        // slow QA frame would age it past the hull. False without one.
        public bool PinAge(int kind, float age)
        {
            int k = -1;
            for (int n = 0; n < Slots && k < 0; n++)
            {
                int i = ((last - n) % Slots + Slots) % Slots;
                if (used[i] && kinds[i] == kind) k = i;
            }
            if (k < 0) return false;
            for (int i = 0; i < Slots; i++) used[i] = i == k;
            at[k] = Time.unscaledTime - age;
            bursts[k].disp = Vector3.zero;
            vel0[k] = velNow;
            last = k;
            held = k;
            heldAge = age;
            return true;
        }

        // Each frame, after the camera is placed. `toWorld`: ship frame (m) -> world; `hullFromShip`: ship frame (m) -> hull
        // frame (m, nose on +Z); `hullM` its length; `camM` the camera in the ship frame (m); `sunDir` unit, `sunIrr` with the
        // eclipse; `shipVel` sim frame, km/s.
        public void Update(float dt, D3 shipVel, Matrix4x4 toWorld, Matrix4x4 hullFromShip, float hullM, Vector3 camM,
                           Vector3 sunDir, float sunIrr, float exposure, Camera cam)
        {
            Count = 0;
            velNow = shipVel;
            if (!Ok) return;
            float now = Time.unscaledTime;
            for (int i = 0; i < Slots; i++)
            {
                if (!used[i]) continue;
                float age = i == held ? heldAge : now - at[i];
                if (age > life[i]) { used[i] = false; continue; }
                // the pieces fly on as the ship burns: take away what the ship's change of velocity since the burst moved it
                bursts[i].disp += Frame.U((shipVel - vel0[i]) * 1e3) * dt;
                var b = bursts[i];
                b.age = Mathf.Max(age, 0);
                live[Count++] = b;
            }
            if (Count == 0) return;
            burstBuf.SetData(live, 0, 0, Count);
            outBuf.SetCounterValue(0);
            float px = 2f * Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad / 2) / Mathf.Max(cam.pixelHeight, 1);
            var hullScale = Matrix4x4.Scale(new Vector3(1 / (0.15f * hullM), 1 / (0.12f * hullM), 1 / (0.5f * hullM)));
            cs.SetBuffer(kernel, idBursts, burstBuf);
            cs.SetInt(idBurstCount, Count);
            cs.SetBuffer(kernel, idOut, outBuf);
            cs.SetTexture(kernel, idLut, lut);
            cs.SetMatrix(idToWorld, toWorld);
            cs.SetMatrix(idToHull, hullScale * hullFromShip);
            cs.SetVector(idCamM, camM);
            cs.SetVector(idSunDir, sunDir);
            cs.SetFloat(idSunIrr, sunIrr);
            cs.SetFloat(idShutter, Mathf.Clamp(dt, 1f / 240, 1f / 30));
            cs.SetFloat(idMinE, MinPixel * px * px / (Mathf.PI * Mathf.Max(exposure, 1e-6f)));
            cs.Dispatch(kernel, Count * PerBurst / 64, 1, 1);
            argsBuf.SetData(args);
            GraphicsBuffer.CopyCount(outBuf, argsBuf, sizeof(uint));
            var rp = new RenderParams(mat)
            {
                camera = cam, layer = 0, worldBounds = new Bounds(Vector3.zero, Vector3.one * 1e7f),
                shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false,
            };
            Graphics.RenderPrimitivesIndirect(rp, MeshTopology.Triangles, argsBuf);
        }

        // review checks: how many pieces the last frame drew, and their heads (world)
        public int Drawn()
        {
            if (!Ok || Count == 0) return 0;
            var a = new uint[4];
            argsBuf.GetData(a);
            return (int)a[1];
        }

        public Vector3[] Heads(int max)
        {
            int n = Math.Min(Drawn(), max);
            var raw = new float[n * 9];
            if (n > 0) outBuf.GetData(raw, 0, 0, n * 9);
            var h = new Vector3[n];
            for (int i = 0; i < n; i++) h[i] = new Vector3(raw[i * 9], raw[i * 9 + 1], raw[i * 9 + 2]);
            return h;
        }

        public void Clear()
        {
            for (int i = 0; i < Slots; i++) used[i] = false;
            Count = 0;
            last = -1;
            held = -1;
        }

        public void Dispose()
        {
            burstBuf?.Release();
            outBuf?.Release();
            argsBuf?.Release();
            Gfx.Destroy(mat);
            Gfx.Destroy(lut);
        }
    }
}
