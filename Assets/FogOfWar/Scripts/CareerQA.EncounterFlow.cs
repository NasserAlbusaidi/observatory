using System;
using System.Collections;
using System.Collections.Generic;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // 20: Phase 5's encounter flow (docs/design-combat.md, PR 5a). 20a-20c, point 1, the cap follows time: 20a the raider's
    // FireKm and alongside times (Career.ReachInS) against a raider flown by its own Pursue: from every update the bound plus
    // the time gone is never past the real event; 20b the cap at a launch from rest at FireKm is the flight's 217 s / EventS,
    // and D11's 1× floor holds for a coasting missile 15 s out; 20c the Fast plan's brake (Career.BrakeInS) against the autopilot flown to it.
    public partial class CareerQA
    {
        IEnumerator EncounterFlowChecks()
        {
            SolarSystem qs = null;
            if (Guard("encounter flow system", () => qs = SolarSystem.Build()))
            {
                Guard("encounter flow 20a", () => EventBoundChecks(qs));
                Guard("encounter flow 20b", () => LaunchCapChecks(qs));
                Guard("encounter flow 20c", () => BrakeEventChecks(qs));
            }
            yield break;
        }

        static double EventIn(Career c, Career.ChaseEvent what)
        {
            var list = new List<(Career.ChaseEvent what, double s)>();
            c.ChaseEvents(list);
            var i = list.FindIndex(x => x.what == what);
            return i < 0 ? double.NaN : list[i].s;
        }

        // 20a: a raider at rest with a coasting ship, 50,000 km off, flown by Pursue in 10 s updates as the scene flies it
        void EventBoundChecks(SolarSystem qs)
        {
            var (_, at, vel) = Belt(qs);
            var b = MakeBench(qs, at, vel);
            var e = RaiderAt(b, 5e4);
            b.Tick(0);
            double fire0 = EventIn(b.C, Career.ChaseEvent.Fire), along0 = EventIn(b.C, Career.ChaseEvent.Alongside);
            double t = 0, fired = double.NaN, along = double.NaN;
            var fireAt = new List<double>();
            var alongAt = new List<double>();
            Body dom = Ship.Dominant(qs, b.Ship.Pos, b.Jd);
            while (t < 4 * 3600 && b.C.Encounter == e && double.IsNaN(along))
            {
                double f = EventIn(b.C, Career.ChaseEvent.Fire), a = EventIn(b.C, Career.ChaseEvent.Alongside);
                if (double.IsNaN(fired) && !double.IsNaN(f)) fireAt.Add(t + f);
                if (!double.IsNaN(a)) alongAt.Add(t + a);
                D3 p0 = b.Ship.Pos, v0 = b.Ship.Vel;
                double j0 = b.Jd, h = (b.Jd + 10.0 / 86400 - b.Jd) * 86400;
                b.Ship.Step(qs, j0, h, dom);
                e.Advance(qs, j0, h, p0, v0, b.Ship.Vel);
                b.Tick(h);
                t += h;
                if (double.IsNaN(fired) && e.Magazine < Career.MagazineSize) fired = t;
                if (double.IsNaN(along) && e.Range < Career.InterceptKm && e.Vrel < Career.InterceptKms) along = t;
            }
            double fireLate = double.NegativeInfinity, alongLate = double.NegativeInfinity;
            foreach (var x in fireAt) fireLate = Math.Max(fireLate, x - fired);
            foreach (var x in alongAt) alongLate = Math.Max(alongLate, x - along);
            Check(!double.IsNaN(fired) && !double.IsNaN(along) && fireLate <= 1e-6 && alongLate <= 1e-6 && fire0 >= 0.5 * fired && along0 >= 0.5 * along,
                $"20a cap events: a 1 g raider at rest 50,000 km off, flown by Pursue in 10 s updates: it fired at {fired:N0} s (bound at the start {fire0:N0} s, ≥ half), alongside at {along:N0} s (bound {along0:N0} s, ≥ half); " +
                $"over {fireAt.Count} and {alongAt.Count} updates the bound plus the time gone came at most {fireLate:N1} s and {alongLate:N1} s past the real time (≤ 0: never late)");
            b.C.DropContact();
        }

        // 20b: a salvo put in flight from rest at FireKm, the raider 12,000 km astern with an empty magazine
        void LaunchCapChecks(SolarSystem qs)
        {
            var (_, at, vel) = Belt(qs);
            var b = MakeBench(qs, at, vel);
            var dir = b.Ship.Pos.Norm;
            var e = RaiderAt(b, 12000, dir);
            e.Magazine = 0;
            b.Tick(0);
            e.Missiles.Add(new Missile { P = dir * Career.FireKm });
            e.Missiles.Add(new Missile { P = dir * Career.FireKm });
            double flight = FlightS(0, Career.FireKm), cap = b.C.WarpCap;
            e.Missiles.Clear();
            e.Missiles.Add(new Missile { P = dir * 150, Age = Career.BurnS });
            double flight150 = 150 / Career.MissileDvKms, in150 = b.C.ImpactInS, cap150 = b.C.WarpCap;
            Check(Math.Abs(flight - 217.0) < 0.1 && Math.Abs(cap - flight / Career.EventS) < 1e-6 && Math.Abs(in150 - flight150) < 1e-6 && flight150 < Career.ImpactHoldS && cap150 == 1,
                $"20b cap at launch: a salvo from rest {Career.FireKm:N0} km off hits in {flight:N1} s (217.0), so the cap is {cap:N2}× ({flight / Career.EventS:N2}×: the flight / {Career.EventS:0} s); " +
                $"one coasting 150 km off, {in150:N1} s out ({flight150:N1}), is held at {cap150:N0}× (D11: 1× inside {Career.ImpactHoldS:0} s)");
            b.C.DropContact();
        }

        // 20c: a Fast plan to Vesta from rest with it, 1,000,000 km out, on a constant mass (unlimited fuel: the formula takes
        // the drive's a now, and a ship that burns fuel speeds up and flips sooner, so the scene's cap reads it again each frame)
        void BrakeEventChecks(SolarSystem qs)
        {
            var (vesta, at, vel) = Belt(qs);
            var b = MakeBench(qs, at, vel);
            b.Ship.UnlimitedFuel = true;
            var dir = (qs.Pos(vesta, b.Jd) - at).Norm;
            b.Ship.Att = Quaternion.LookRotation(Frame.U(dir), Frame.U(Frame.EclNorth));
            b.Ship.Target = vesta; b.Ship.Plan = Plan.Brachistochrone;
            var e = RaiderAt(b, 1e5, -dir);
            e.Magazine = 0;
            b.Tick(0);
            double flip0 = EventIn(b.C, Career.ChaseEvent.Flip), arrive0 = EventIn(b.C, Career.ChaseEvent.Arrival);
            b.C.DropContact();
            double t = 0, flipped = double.NaN;
            Body dom = Ship.Dominant(qs, b.Ship.Pos, b.Jd);
            while (t < 8 * 3600 && double.IsNaN(flipped))
            {
                double h = (b.Jd + 10.0 / 86400 - b.Jd) * 86400;
                b.Ship.Step(qs, b.Jd, h, dom);
                b.Jd += h / 86400;
                t += h;
                if (b.Ship.Phase.StartsWith("flipped", StringComparison.Ordinal)) flipped = t;
            }
            Check(!double.IsNaN(flipped) && flip0 <= flipped + 10 && flip0 >= 0.95 * flipped && arrive0 > flip0,
                $"20c cap events: a Fast plan to Vesta from 1,000,000 km at rest with it, {b.Ship.AccelG:F2} g: the brake event at the start {flip0:N0} s, the autopilot flipped at {flipped:N0} s " +
                $"(the event not past it by more than a 10 s update, and ≥ 95 % of it); the arrival event {arrive0:N0} s, after the flip");
        }
    }
}
