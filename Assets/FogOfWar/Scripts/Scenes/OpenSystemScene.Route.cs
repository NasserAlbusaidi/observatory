using System;
using FogOfWar.OpenWorld;
using FogOfWar.UI;
using UnityEngine.UIElements;

namespace FogOfWar
{
    // The route strip under the status box while a flip-and-burn flies: the trip's legs (burn, coast, brake) drawn to
    // scale in time with what is flown filled in, the flip as a mark between them, and a countdown to the next event.
    // What is flown comes from the ship's own burn and coast counters since the plan was engaged; what is left from
    // Ship.Legs at the ship's state now, so a new coast choice mid-flight redraws the rest.
    // The flip watch (TrackRoute, every sim frame, HUD shown or not): a toast FlipWarnSeconds before the flip, and time
    // compression stepped down on the way in, so the flip plays out where the pilot can see it.
    public partial class OpenSystemScene
    {
        VisualElement routeBox, routeAccel, routeCoast, routeBrake, routeFlip;
        Label routeHead, routeNext, routeWhen, routeLegs;

        // the plan being drawn and the counters when it started; routeBrakeAt is BurnSeconds when braking began (-1: not yet)
        Body routeFor;
        double routeBurn0, routeCoast0, routeBrakeAt = -1;
        // flown and left of each leg, as of the last TrackRoute
        double routeAccelDone, routeCoastDone, routeBrakeDone, routeAccelLeft, routeCoastLeft, routeBrakeLeft;
        bool routeWarned;

        const double FlipWarnSeconds = 300; // [design] sim seconds of notice before the flip
        // [design] time compression steps down while the flip is closer than this many real seconds at the current
        // rate (1k× to 100× under 2,000 s, 100× to 10× under 200 s), down to FlipWarpFloor and no further
        const double FlipWarpRealSeconds = 2;
        const int FlipWarpFloor = 1; // 10×: the pilot drops to 1× by hand to watch the turn

        void BuildRouteStrip(VisualElement status)
        {
            routeBox = Ui.E(status, "route");
            routeHead = Ui.L(routeBox, "", "t-micro", "f-condm");
            var row = Ui.E(routeBox, "row");
            routeNext = Ui.L(row, "", "t-label", "f-condm");
            routeNext.style.flexGrow = 1;
            routeWhen = Ui.L(row, "", "t-h3", "f-mono", "accent", "glow");
            var bar = Ui.E(routeBox, "route-bar");
            routeAccel = Leg(bar, "accel");
            routeCoast = Leg(bar, "coast");
            routeFlip = Ui.E(bar, "flip");
            routeBrake = Leg(bar, "brake");
            routeLegs = Ui.L(routeBox, "", "t-label", "f-mono");
            routeFor = null;
            Ui.Show(routeBox, false);

            static VisualElement Leg(VisualElement bar, string kind)
            {
                var leg = Ui.E(bar, "leg", kind);
                Ui.E(leg, "done");
                return leg;
            }
        }

        // Under CapWarp in the sim tick: keep the record of the plan flown, then watch for the flip.
        void TrackRoute()
        {
            if (!FlyingPlan) { routeFor = null; return; }
            if (routeFor != ship.Target)
            {
                routeFor = ship.Target;
                routeBurn0 = ship.BurnSeconds; routeCoast0 = ship.CruiseSeconds; routeBrakeAt = -1;
                routeWarned = false;
            }
            if (ship.Phase == "flipped, braking" && routeBrakeAt < 0) routeBrakeAt = ship.BurnSeconds;

            // flown so far: every burn before the brake counts as the first leg (a clearance climb included)
            routeBrakeDone = routeBrakeAt < 0 ? 0 : ship.BurnSeconds - routeBrakeAt;
            routeAccelDone = Math.Max(ship.BurnSeconds - routeBurn0 - routeBrakeDone, 0);
            routeCoastDone = Math.Max(ship.CruiseSeconds - routeCoast0, 0);

            // still to fly; once the brake has begun the rest is brake, whatever the guidance does near the curve
            double d = Math.Max((sys.Pos(ship.Target, jd) - ship.Pos).Mag - Ship.Standoff(ship.Target), 0);
            var (u, _) = ship.RelToTarget(sys, jd);
            (routeAccelLeft, routeCoastLeft, routeBrakeLeft) = routeBrakeAt >= 0
                ? (0.0, 0.0, Math.Max(u, 0) / (ship.AMax * Ship.BrakeShare))
                : Ship.Legs(d, ship.AMax, u, ship.CruiseCap);

            // only on the way to a flip the drive will fly: not parked, braking, out of fuel or in a clearance climb (a ship
            // over the braking curve reads "flip in 0 s" and would hold 1× for good). The career suite flies its trips at
            // its own warp, and checks them.
            bool toFlip = ship.Phase == "accelerating" || ship.Phase == "cruising" || ship.Phase == "turning";
            if (routeBrakeAt >= 0 || !toFlip || CareerTesting) return;
            double flipIn = routeAccelLeft + routeCoastLeft;
            if (!routeWarned && flipIn <= FlipWarnSeconds)
            {
                routeWarned = true;
                hudShell?.Toast("Flip in " + Dur(flipIn), "Time compression steps down for it. The drive turns the ship end over end and starts braking.", 8f);
            }
            while (warpIdx > FlipWarpFloor && flipIn < Warps[warpIdx] * FlipWarpRealSeconds) warpIdx--;
        }

        void UpdateRouteStrip()
        {
            if (routeBox == null) return;
            if (!FlyingPlan || routeFor != ship.Target) { Ui.Show(routeBox, false); return; }
            double accelled = routeAccelDone, coasted = routeCoastDone, braked = routeBrakeDone;
            double accelLeft = routeAccelLeft, coastLeft = routeCoastLeft, brakeLeft = routeBrakeLeft;

            SetLeg(routeAccel, accelled, accelLeft);
            SetLeg(routeCoast, coasted, coastLeft);
            SetLeg(routeBrake, braked, brakeLeft);
            Ui.SetClass(routeFlip, "on", routeBrakeAt >= 0);

            bool arrived = ship.Phase == "in orbit" || ship.Phase == "circularising" || ship.Phase.Contains("station");
            string next, when = "";
            if (arrived) next = ship.Phase.ToUpperInvariant();
            // a turn before anything is flown aims the drive; one after, before the brake, is the flip
            else if (ship.Phase == "turning") next = routeBrakeAt < 0 && accelled + coasted > 0 ? "FLIPPING" : "TURNING";
            else if (routeBrakeAt >= 0) { next = "BRAKE ENDS IN"; when = Dur(brakeLeft); }
            else if (ship.Phase == "cruising") { next = "FLIP IN"; when = Dur(coastLeft); }
            else if (ship.Phase == "accelerating") { next = coastLeft > 0 ? "COAST IN" : "FLIP IN"; when = Dur(accelLeft); }
            else next = ship.Phase.ToUpperInvariant();
            Ui.Text(routeHead, "NAV  ·  " + ship.Target.Name.ToUpperInvariant());
            Ui.Text(routeNext, next);
            Ui.Text(routeWhen, when);
            Ui.Show(routeWhen, when.Length > 0);

            double burn = accelled + accelLeft, coast = coasted + coastLeft, brake = braked + brakeLeft;
            Ui.Text(routeLegs, "BURN " + Dur(burn) + (coast > 0 ? "  ·  COAST " + Dur(coast) : "") + "  ·  BRAKE " + Dur(brake));
            Ui.Show(routeBox, true);

            static void SetLeg(VisualElement leg, double done, double left)
            {
                double total = done + left;
                Ui.Show(leg, total > 0);
                leg.style.flexGrow = (float)total;
                leg[0].style.width = Length.Percent(total > 0 ? (float)(100 * done / total) : 0);
            }
        }
    }
}
