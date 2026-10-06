using System;
using System.Collections.Generic;
using FogOfWar.Cockpit;
using FogOfWar.OpenWorld;
using FogOfWar.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace FogOfWar
{
    // The cockpit view's interior: the cabin, its lamps and the three MFDs (Scripts/Cockpit).
    public partial class OpenSystemScene
    {
        CockpitView cockpit;
        VisualElement flightCluster;
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
            // the MFDs carry the bottom flight cluster's readouts, and in portrait the cluster would sit on top of them
            if ((flightCluster == null || flightCluster.panel == null) && hudShell != null) flightCluster = hudShell.HudLayer?.Q(className: "flight");
            if (flightCluster != null) flightCluster.style.visibility = inside ? Visibility.Hidden : Visibility.Visible;
            KeepHudAboveConsole(inside);
            if (!inside) return;
            if ((mfdT += dt) >= 0.1f) { mfdT = 0; FillMfds(); }
            if ((plotT += dt) >= 0.25f) { plotT = 0; PlotOrbit(); }
        }

        // The status chips and the lesson card normally sit at fixed heights over the flight cluster. In the cockpit they
        // ride just above the MFD row instead, wherever the aspect ratio and head turn put it.
        void KeepHudAboveConsole(bool inside)
        {
            if (hudShell == null || pills == null) return;
            float top = inside ? cockpit.ConsoleTop() : 0;
            float colBottom = hudShell.Portrait ? 350 : 56; // BuildHud's own value
            if (top <= 0)
            {
                pills.style.bottom = StyleKeyword.Null;
                if (lessonCard != null) lessonCard.style.bottom = StyleKeyword.Null;
                if (rightCol != null) rightCol.style.bottom = colBottom;
                return;
            }
            float b = top / hudShell.Scale + 8;
            pills.style.bottom = b;
            if (lessonCard != null) lessonCard.style.bottom = b + 44;
            // QA-049: the target card and ship console column ends above the MFDs (its body scrolls)
            // in portrait the lesson card spans under the column too, so the column also ends above the card
            float lesson = hudShell.Portrait && lessonCard != null && lessonCard.resolvedStyle.display == DisplayStyle.Flex &&!float.IsNaN(lessonCard.resolvedStyle.height)
                ? 44 + lessonCard.resolvedStyle.height + 8 : 0;
            if (rightCol != null) rightCol.style.bottom = Mathf.Max(colBottom, b + lesson);
        }

        // HUD world markers and labels: false for a screen point that lands on the cabin instead of the display.
        bool CockpitShows(Vector3 screen) => cockpit == null || cockpit.OnDisplay(screen);

        // Pull an edge marker (HUD coordinates) in toward the screen centre until it sits on the display.
        Vector2 OntoDisplay(Vector2 at)
        {
            if (cockpit == null || !cockpit.Visible) return at;
            var c = new Vector2(hudShell.HudWidth / 2, hudShell.H / 2);
            // looking at the cabin wall, not even the screen centre is on the display: no chevron at all
            if (!CockpitShows(HudToScreen(c))) { Ui.Show(mkEdge, false); return at; }
            bool moved = false;
            for (int i = 0; i < 24 && !CockpitShows(HudToScreen(at)); i++) { at = c + (at - c) * 0.9f; moved = true; }
            // a little margin, so the chevron and its name stay clear of the bezel
            return moved ? c + (at - c) * 0.9f : at;
        }

        Vector3 HudToScreen(Vector2 at)
        {
            float left = float.IsNaN(hudShell.HudLayer.resolvedStyle.left) ? 0 : hudShell.HudLayer.resolvedStyle.left;
            return new Vector3((at.x + left) * hudShell.Scale, Cam.pixelHeight - at.y * hudShell.Scale, 0);
        }

        // QA-056, the HUD's rule: under 1 km/s a speed reads in m/s, and a speed at rest reads 0
        static string MfdSpeed(double kms)
        {
            double a = Math.Abs(kms);
            if (a >= 1) return Fmt.Num(kms, a < 10 ? 3 : 1) + " km/s";
            double ms = kms * 1000;
            if (Math.Abs(ms) < 0.005) ms = 0;
            return Fmt.Num(ms, Math.Abs(ms) < 10 ? 2 : Math.Abs(ms) < 100 ? 1 : 0) + " m/s";
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
            if (career != null && !career.Sandbox && career.TankCapacity > 0)
                mfdLines.Add(new Readout("Fuel", $"{ship.Fuel:N0} / {career.TankCapacity:N0} t", ship.Fuel < 0.15 * career.TankCapacity ? Tone.Bad : Tone.Normal));
            else mfdLines.Add(new Readout("Fuel", ship.UnlimitedFuel ? "unmetered" : Fmt.Num(ship.Fuel, 1) + " t", ship.Dry ? Tone.Bad : Tone.Normal));
            mfdLines.Add(new Readout("Speed", MfdSpeed((ship.Vel - sys.Vel(reference, jd)).Mag) + " vs " + reference.Name));
            if (g >= 2.5) mfdLines.Add(new Readout("High g", "stay in your couch", Tone.Bad));
            m.Page(0, "DRIVE", mfdLines);

            // 2: navigation, under the orbit plot
            mfdLines.Clear();
            var refPos = sys.Pos(reference, jd);
            double alt = (ship.Pos - refPos).Mag - reference.Radius;
            mfdLines.Add(new Readout(reference.Name, Km(alt) + (reference == sys.Sun ? " out" : " up")));
            mfdLines.Add(new Readout("Speed", MfdSpeed((ship.Vel - sys.Vel(reference, jd)).Mag)));
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
                mfdLines.Add(new Readout("Rel. speed", MfdSpeed(vr.Mag)));
                mfdLines.Add(new Readout("Closing", MfdSpeed(closing), closing > 0 ? Tone.Cold : Tone.Normal));
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
