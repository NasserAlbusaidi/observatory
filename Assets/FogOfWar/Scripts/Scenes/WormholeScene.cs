using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar
{
    // Ray-traced Morris–Thorne (Ellis) wormhole with a fly-through and an illustrated Einstein–Rosen pinch-off (the Ellis
    // throat is shrunk on a set curve with a visual cutoff; no dynamical Einstein–Rosen solution is computed).
    public class WormholeScene : SimScene
    {
        public override string Title => "Wormhole";
        public override string Eyebrow => "Morris–Thorne, 1988";
        public override string Short => "Ray-traced tunnel to another sky";
        public override string Blurb =>
            "Every pixel is a light ray bent through the wormhole metric. The sphere in front of you is a lens: you're seeing <color=#FFB547>another universe's sky</color> through the throat, plus a squashed copy of your own sky wrapped round its edge.\n\n" +
            "Hit Fly through to cross. Hit Einstein–Rosen pinch for a sketch of the 1935 version, which slams shut faster than light can get across.";
        public override string Note => "The catch: to stay open the throat needs negative energy density. The readout compares it with the best negative energy we can make in a lab (the Casimir effect). It compiles. It doesn't run. The pinch is an illustration: it squeezes this same tunnel shut on a set timetable rather than solving how an Einstein–Rosen bridge really evolves.";

        float throat = 1.2f, unitPow = 3f;
        Material mat;
        float lc = 8, pinch, pinchTarget;
        int side = 1;              // +1 our universe, −1 the far side
        Vector3 er = Vector3.back;
        // fly-through state
        bool flying; float flyT, turnT, flyFrom, flyTo;
        Quaternion q0, q1;

        protected override void Build()
        {
            mat = OwnMat(Gfx.Mat("Wormhole"));
            Gfx.Fullscreen("Wormhole", Root, Cam, mat);
            Orbit.Set(new Vector3(2.5f, 1.2f, -8f), Vector3.zero, 0.6f, 30f);
            side = 1; pinch = pinchTarget = 0; flying = false;
        }

        public override void Tick(float dt)
        {
            pinch += (pinchTarget - pinch) * Mathf.Min(1, dt * (pinchTarget > 0 ? 1.4f : 3f));
            if (flying)
            {
                if (flyT < 1)
                {
                    flyT = Mathf.Min(1, flyT + dt / 5f);
                    float e = flyT < 0.5f ? 2 * flyT * flyT : 1 - Mathf.Pow(-2 * flyT + 2, 2) / 2;
                    lc = Mathf.Lerp(flyFrom, flyTo, e);
                }
                else
                {
                    // Arrived: turn round to look back at the throat, then mirror the camera so orbiting works on this side.
                    turnT = Mathf.Min(1, turnT + dt / 1.6f);
                    float e = turnT * turnT * (3 - 2 * turnT);
                    Cam.transform.rotation = Quaternion.Slerp(q0, q1, e);
                    if (turnT >= 1)
                    {
                        var p = -Cam.transform.position;
                        Cam.transform.position = p;
                        Orbit.target = Vector3.zero;
                        Orbit.FromPosition(p);
                        Orbit.Snap();
                        Orbit.enabled = true;
                        side = -side;
                        flying = false;
                    }
                }
            }
        }

        public override void LateTick(float dt)
        {
            if (!flying)
            {
                float d = Cam.transform.position.magnitude;
                er = Cam.transform.position.normalized * side;
                lc = side * Mathf.Sqrt(Mathf.Max(d * d - throat * throat, 0.0001f));
            }
            Gfx.SetRayCamera(mat, Cam);
            mat.SetFloat("_B", throat);
            mat.SetFloat("_Lc", lc);
            mat.SetFloat("_Pinch", pinch);
            mat.SetVector("_Er", er);
            mat.SetInt("_Steps", Mathf.RoundToInt(400 * App.StepsScale));
        }

        void StartFly()
        {
            if (flying) return;
            pinchTarget = 0;
            if (Cam.transform.position.magnitude < 2) Cam.transform.position = Cam.transform.position.normalized * 4;
            Orbit.enabled = false;
            Cam.transform.LookAt(Vector3.zero, Vector3.up);
            q0 = Cam.transform.rotation;
            q1 = Quaternion.LookRotation(Cam.transform.position.normalized, Vector3.up);
            flyFrom = lc; flyTo = -lc; flyT = 0; turnT = 0;
            flying = true;
        }

        public override void Readouts(List<Readout> list)
        {
            double unit = System.Math.Pow(10, unitPow);
            double b0 = throat * unit;
            double k = System.Math.Pow(Phys.c, 4) / (8 * System.Math.PI * Phys.G);
            double rhoE = k / (b0 * b0);
            double casimir = System.Math.PI * System.Math.PI * Phys.hbar * Phys.c / (720 * System.Math.Pow(1e-6, 4));
            list.Add(new Readout("You are in", lc > 0 ? "our universe" : "the other universe", lc > 0 ? Tone.Normal : Tone.Cold));
            list.Add(new Readout("Distance from throat", Fmt.Num(System.Math.Abs(lc) * unit, 2) + " m"));
            list.Add(new Readout("Throat radius", Fmt.Num(b0, 2) + " m"));
            list.Add(new Readout("Throat state", pinch > 0.5f ? "pinched shut" : pinch > 0.02f ? "collapsing" : "held open", pinch > 0.02f ? Tone.Bad : Tone.Normal));
            list.Add(new Readout("Energy density needed", "−" + Fmt.Num(rhoE) + " J/m³", Tone.Cold));
            list.Add(new Readout("Same, as mass density", "−" + Fmt.Num(rhoE / (Phys.c * Phys.c)) + " kg/m³", Tone.Cold));
            list.Add(new Readout("Casimir effect, 1 µm gap", "−" + Fmt.Num(casimir) + " J/m³"));
            list.Add(new Readout("Shortfall", Fmt.Num(rhoE / casimir) + "×", Tone.Bad));
        }

        public override void Controls(PanelUI ui)
        {
            int b = ui.Buttons(side > 0 ? "Fly through" : "Fly back", "Einstein–Rosen pinch", "Reopen (exotic matter)");
            if (b == 0) StartFly();
            if (b == 1) pinchTarget = 1;
            if (b == 2) pinchTarget = 0;
            throat = ui.Slider("Throat radius", throat, 0.4f, 3f, v => v.ToString("0.00") + " units");
            unitPow = ui.Slider("One unit is", unitPow, 0f, 6f, v => Fmt.Num(System.Math.Pow(10, v)) + " m", 0.1f);
        }
    }
}
