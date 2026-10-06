using System;
using System.Collections.Generic;
using FogOfWar.OpenWorld;
using FogOfWar.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace FogOfWar
{
    // The map reads as a trip planner: your own orbit (green) and the target's (amber) stand out, the rest dims while a
    // target is set, and the brachistochrone the autopilot would fly is a dashed line from the ship to where the target
    // will be at arrival, with the flip and the meeting point marked.
    public partial class OpenSystemScene
    {
        static readonly Color OwnOrbitCol = new Color(0.55f, 1f, 0.62f, 0.7f); // the colour of your velocity marker
        static readonly Color PathCol = new Color(1f, 0.71f, 0.28f, 0.9f);
        VisualElement mkFlip, mkMeet;
        Label mkMeetLabel;
        D3 flipW, meetW;
        bool pathOn, flipOn;
        int ownSegs, pathSegs;
        string flipShow = "none", meetShow = "none";
        string ownName = "";

        // The body whose orbit around the Sun is the ship's own, near enough: the one you are near (a moon's planet).
        // Null when the Sun itself is the reference: then the ship's own conic is drawn.
        Body OwnBody()
        {
            var b = reference;
            if (b == null || b == sys.Sun) return null;
            while (b.Parent != null && b.Parent != sys.Sun) b = b.Parent;
            return b;
        }

        bool MapView => pivot == Pivot.Sun;

        // Called from DrawOrbits after the bodies' orbits: the ship's conic around the Sun and the planned path.
        void DrawMapLines(Body own, float fade)
        {
            pathSegs = 0; pathOn = false; flipOn = false;
            ownName = own != null ? own.Name : "ship";
            if (own == null) AddShipOrbit(fade);
            if (MapView) AddTripPath();
        }

        void AddShipOrbit(float fade)
        {
            var sp = sys.Pos(sys.Sun, jd);
            var r0 = ship.Pos - sp; var v0 = ship.Vel - sys.Vel(sys.Sun, jd);
            double T = Rails.Period(r0, v0, sys.Sun.GM);
            if (double.IsInfinity(T)) T = 2 * 365.25 * 86400; // on an escape course: two years of it
            var c = OwnOrbitCol; c.a *= fade;
            const int N = 180;
            Vector3 prev = Map(ship.Pos, out _);
            for (int i = 1; i <= N; i++)
            {
                if (!Rails.Propagate(r0, v0, sys.Sun.GM, T * i / N, out var r, out _)) break;
                var q = Map(sp + r, out _);
                pts.Add(prev); pts.Add(q); cols.Add(c); cols.Add(c);
                prev = q;
                ownSegs++;
            }
        }

        // The straight line is the burn direction, not the curved path the ship flies under gravity; at the map's
        // scale the two are close for a brachistochrone. The flip sits where the braking distance at the peak speed
        // begins (no flip mark once the ship is braking).
        void AddTripPath()
        {
            var t = ship.Target;
            bool brach = ship.PlanActive ? FlyingPlan : selectedPlan == Plan.Brachistochrone;
            if (t == null || !brach) return;
            double d = Math.Max((sys.Pos(t, jd) - ship.Pos).Mag - Ship.Standoff(t), 0);
            if (d < 1) return;
            PlanTrip(out double secs, out _, out _);
            meetW = sys.PosAt(t, jd + secs / 86400);
            var (u, _) = ship.RelToTarget(sys, jd);
            double peak = Math.Max(Math.Sqrt(ship.AMax * d + u * u / 2), u);
            double f = PlannerFrac(d);
            if (f < 1) peak = Math.Min(peak, Math.Max(f * Math.Sqrt(ship.AMax * d), u));
            double brake = peak * peak / (2 * ship.AMax);
            var chord = meetW - ship.Pos;
            if (brake < d * 0.999) { flipOn = true; flipW = ship.Pos + chord * (1 - brake / d); }
            pathOn = true;
            // dashed: every other piece of the line, mapped piece by piece (the map is logarithmic past 100,000 km)
            const int N = 80;
            for (int i = 0; i < N; i += 2)
            {
                pts.Add(Map(ship.Pos + chord * ((double)i / N), out _));
                pts.Add(Map(ship.Pos + chord * ((double)(i + 1) / N), out _));
                cols.Add(PathCol); cols.Add(PathCol);
                pathSegs++;
            }
        }

        void BuildMapMarkers()
        {
            mkFlip = Mk();
            Bar(mkFlip, 0, 0, 10, 10, PathCol, 45);
            MapTag(mkFlip, "FLIP");
            mkMeet = Mk();
            Ring(mkMeet, 18, PathCol, 2);
            mkMeetLabel = MapTag(mkMeet, "MEET");
        }

        Label MapTag(VisualElement m, string text)
        {
            var l = Ui.L(m, text, "t-micro", "f-condm");
            l.pickingMode = PickingMode.Ignore;
            l.style.position = Position.Absolute;
            l.style.left = -80; l.style.width = 160; l.style.top = 12;
            l.style.unityTextAlign = TextAnchor.UpperCenter;
            l.style.whiteSpace = WhiteSpace.NoWrap;
            l.style.color = PathCol;
            return l;
        }

        // The meet ring gives way to the target bracket when the target barely moves before you arrive: the bracket and
        // the path's end already mark the spot.
        void UpdateMapMarkers()
        {
            bool on = MapView && pathOn;
            flipShow = !on || !flipOn ? "none" : PlaceDir(mkFlip, Frame.U(flipW - camW), out _) ? "on" : "off screen";
            if (!on || !flipOn) Ui.Show(mkFlip, false);
            if (!on) { Ui.Show(mkMeet, false); meetShow = "none"; return; }
            if (!PlaceDir(mkMeet, Frame.U(meetW - camW), out var at)) { meetShow = "off screen"; return; }
            var tgt = mkTgt.style.translate.value;
            bool onBracket = mkTgt.resolvedStyle.display == DisplayStyle.Flex &&
                             (at - new Vector2(tgt.x.value, tgt.y.value)).magnitude < 40;
            Ui.Show(mkMeet, !onBracket);
            meetShow = onBracket ? "at target" : "on";
            Ui.Text(mkMeetLabel, "MEET " + ship.Target.Name.ToUpperInvariant());
        }

        // review shots: what the map draws for the ship and the trip
        public string TestMapReport() =>
            $"own {ownName} {ownSegs} segs | path {(pathOn ? pathSegs + " segs" : "off")} | flip {(flipOn ? "on" : "off")}" +
            $" | meet {(pathOn ? Fmt.Num(meetW.Mag / Frame.AU, 3) + " AU from the Sun" : "-")}" +
            $" | flip mark {flipShow} | meet mark {meetShow} | rings {patrolRings} patrol, {havenRings} haven, {beltRings} belt";
    }
}
