using System;
using UnityEngine;

namespace FogOfWar.OpenWorld
{
    // A pirate's chase (docs/design-world.md W4): a Ship on Plan.Pursue whose Target is a stand-in Body for the player.
    // Lockstep: the scene steps the player over [jd, jd + h] first, then calls Advance with that step, which sets the
    // stand-in from it and steps the pirate over the same span. Scene-free, so the pursuit check flies it headless.
    public sealed class Pursuit
    {
        // working values [design]: the stand-in's radius (the pirate holds Radius + 1 km off it, Ship.Standoff); the
        // pirate's longest and shortest step inside a span; how often it looks up the gravity well it is in
        public const double Radius = 0.05, MaxStepS = 30, MinStepS = 0.5, RefreshS = 60;

        public readonly Ship Pirate;
        // The player as the pirate sees it, kept out of sys.Bodies so nothing else reads it (Dominant, Gravity and the HUD
        // walk that list). Path is the player's last step as a quadratic from its start state and the step's mean
        // acceleration; PathAccel is that acceleration, gravity included, since Guide takes it in place of the target's
        // gravity. Not a cubic through both ends: sys.Vel differences the path 30 s either side, which magnifies the
        // rounding of p1 − p0 over a 1/60 s step to km/s, while the central difference of a quadratic is exact.
        public readonly Body StandIn;
        double jd0, end;
        D3 p0, v0, a;
        Body reference;
        double refJd = double.NegativeInfinity;

        // A pirate at (pos, vel) with a g-rated drive, aimed at the player at (playerPos, playerVel) on date jd.
        public Pursuit(SolarSystem sys, double jd, D3 pos, D3 vel, double g, D3 playerPos, D3 playerVel, double radius = Radius)
        {
            StandIn = new Body { Name = "Pursued ship", Parent = sys.Sun, GM = 0, Radius = radius, Path = PosAt, PathAccel = AccelAt };
            jd0 = end = jd; p0 = playerPos; v0 = playerVel; a = D3.Zero;
            // reaction mass is not modelled for pirates [design]. Near stays empty: a touchdown would clear the plan
            // (Ship.CheckTouchdown) and strand the pirate; Climb and Avoid keep it off the surfaces instead.
            Pirate = new Ship
            {
                Pos = pos, Vel = vel, MaxG = g, UnlimitedFuel = true, CruiseCap = 0, Att = Facing(playerPos - pos),
                Plan = Plan.Pursue, Target = StandIn,
            };
            foreach (var b in sys.Bodies) if (b.Parent != null && b.Radius >= 100) Pirate.Obstacles.Add(b); // the scene's rule
        }

        D3 PosAt(double jd) => PlayerPos((jd - jd0) * 86400);
        D3 AccelAt(double jd) => a;

        // The player t seconds into the span, read directly (no cache, no date rounding).
        internal D3 PlayerPos(double t) => p0 + v0 * t + a * (0.5 * t * t);
        internal D3 PlayerVel(double t) => v0 + a * t;
        internal double SpanStart => jd0;
        internal double SpanEnd => end;

        // The player's step from (p0, v0) at jd to velocity v1 at jd + h/86400. h > 0: Advance checks it for the scene.
        internal void Span(SolarSystem sys, double jd, double h, D3 p0, D3 v0, D3 v1)
        {
            jd0 = jd; end = jd + h / 86400; this.p0 = p0; this.v0 = v0; a = (v1 - v0) / h;
            // SolarSystem.Pos keeps one date's positions by Body, and the stand-in keeps its Body from span to span: move
            // the cache off every date, so a read of the old path at a date inside this span cannot survive
            sys.Pos(sys.Sun, double.NaN);
        }

        // Sets the stand-in from the player's step and flies the pirate over the same span.
        public void Advance(SolarSystem sys, double jd, double h, D3 p0, D3 v0, D3 v1)
        {
            if (!(h > 0)) return;
            Span(sys, jd, h, p0, v0, v1);
            for (double j = jd; j < end;) j = Step(sys, j);
        }

        // One pirate step from j toward the span's end, returning the date reached. The step is the player's plan rule
        // (OpenSystemScene.SubstepSize), min(span left, 30 s, max(0.05·d/v, 0.5 s)): fine steps closing in, long ones holding.
        internal double Step(SolarSystem sys, double j)
        {
            double t = (j - jd0) * 86400, left = (end - j) * 86400;
            double d = (PlayerPos(t) - Pirate.Pos).Mag, v = (PlayerVel(t) - Pirate.Vel).Mag;
            double h = Math.Min(left, Math.Min(MaxStepS, Math.Max(0.05 * d / Math.Max(v, 1e-9), MinStepS)));
            double j1 = h >= left ? end : j + h / 86400;
            h = (j1 - j) * 86400; // by exactly what jd advances, as the scene steps the player
            if (reference == null || (j - refJd) * 86400 >= RefreshS) { reference = Ship.Dominant(sys, Pirate.Pos, j); refJd = j; }
            Pirate.Step(sys, j, h, reference);
            return j1;
        }

        static Quaternion Facing(D3 dir)
        {
            var up = Math.Abs(D3.Dot(dir.Norm, Frame.EclNorth)) > 0.99 ? new D3(1, 0, 0) : Frame.EclNorth;
            return Quaternion.LookRotation(Frame.U(dir.Norm), Frame.U(up));
        }
    }
}
