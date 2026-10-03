using System;
using System.Collections.Generic;
using FogOfWar.Cockpit;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // The cockpit view's interior: the cabin, its lamps and the three MFDs (Scripts/Cockpit).
    public partial class OpenSystemScene
    {
        CockpitView cockpit;
        float mfdT, plotT;
        readonly List<Readout> mfdLines = new List<Readout>();

        void BuildCockpit() => cockpit = new CockpitView(Root, Cam);

        void DisposeCockpit() { cockpit?.Dispose(); cockpit = null; }

        // after the world camera is placed; `inside` is the cockpit view with no photo-mode rig
        void LateCockpit(bool inside)
        {
            if (cockpit == null) return;
            float dt = Time.unscaledDeltaTime;
            cockpit.Frame(inside, Quaternion.Euler(lookPitch, lookYaw, 0), (float)(ship.Throttle * ship.MaxG), (float)ship.Throttle, ship.RcsLevel, dt);
            if (!inside) return;
            if ((mfdT += dt) >= 0.1f) { mfdT = 0; FillMfds(); }
            if ((plotT += dt) >= 0.25f) { plotT = 0; PlotOrbit(); }
        }

        void FillMfds()
        {
            var m = cockpit.Mfd;

            // 1: drive
            mfdLines.Clear();
            double g = ship.Throttle * ship.MaxG;
            mfdLines.Add(new Readout("Drive", ship.Throttle > 0 ? Fmt.Num(g, 2) + " g" : "safe", ship.Throttle > 0 ? Tone.Hot : Tone.Normal));
            mfdLines.Add(new Readout("Throttle", Fmt.Num(ship.ThrottleCmd * 100, 0) + " %  of " + Fmt.Num(ship.MaxG, 1) + " g"));
            mfdLines.Add(new Readout("Δv spent", Fmt.Num(ship.DvUsed, 3) + " km/s"));
            mfdLines.Add(new Readout("Rotation", Fmt.Num(ship.AngVel.magnitude * Mathf.Rad2Deg, 1) + " °/s"));
            mfdLines.Add(new Readout("Assist", ship.FlightAssist ? "on" : "off", ship.FlightAssist ? Tone.Normal : Tone.Bad));
            mfdLines.Add(new Readout("RCS", ship.RcsActive ? "firing" : "idle", ship.RcsActive ? Tone.Hot : Tone.Normal));
            mfdLines.Add(new Readout("Reaction mass", ship.UnlimitedFuel ? "unmetered" : Fmt.Num(ship.Fuel, 1) + " t", ship.Dry ? Tone.Bad : Tone.Normal));
            if (g >= 2.5) mfdLines.Add(new Readout("High g", "stay in your couch", Tone.Bad));
            m.Page(0, "DRIVE", mfdLines);

            // 2: navigation, under the orbit plot
            mfdLines.Clear();
            var refPos = sys.Pos(reference, jd);
            double alt = (ship.Pos - refPos).Mag - reference.Radius;
            mfdLines.Add(new Readout(reference.Name, Km(alt) + (reference == sys.Sun ? " out" : " up")));
            mfdLines.Add(new Readout("Speed", Fmt.Num((ship.Vel - sys.Vel(reference, jd)).Mag, 3) + " km/s"));
            mfdLines.Add(new Readout("Nav", ship.PlanActive || ship.Landed != null ? ship.Phase : "idle", ship.PlanActive ? Tone.Hot : Tone.Normal));
            m.Page(1, "NAV", mfdLines);

            // 3: target
            mfdLines.Clear();
            if (ship.Target != null)
            {
                var r = sys.Pos(ship.Target, jd) - ship.Pos;
                var vr = ship.Vel - sys.Vel(ship.Target, jd);
                double closing = D3.Dot(vr, r.Norm);
                mfdLines.Add(new Readout("Target", ship.Target.Name, Tone.Hot));
                mfdLines.Add(new Readout("Range", Km(r.Mag - ship.Target.Radius)));
                mfdLines.Add(new Readout("Rel. speed", Fmt.Num(vr.Mag, 3) + " km/s"));
                mfdLines.Add(new Readout("Closing", Fmt.Num(closing, 3) + " km/s", closing > 0 ? Tone.Cold : Tone.Normal));
                mfdLines.Add(new Readout("Flip & burn", Dur(ship.EtaSeconds(sys, jd)) + " at " + Fmt.Num(ship.MaxG, 1) + " g"));
                mfdLines.Add(new Readout("Plan", ship.PlanActive ? PlanShort[(int)ship.Plan] : "none", ship.PlanActive ? Tone.Hot : Tone.Normal));
            }
            else mfdLines.Add(new Readout("Target", "none"));
            m.Page(2, "TARGET", mfdLines);
        }

        void PlotOrbit()
        {
            var dom = reference;
            if (dom == null) return;
            cockpit.Mfd.Orbit(ship.Pos - sys.Pos(dom, jd), ship.Vel - sys.Vel(dom, jd), dom.GM, dom.Radius);
        }
    }
}
