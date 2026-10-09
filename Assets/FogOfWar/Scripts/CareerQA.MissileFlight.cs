using System;
using System.Collections.Generic;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // 17m-17o: the missile's flight (Career.Damage.cs Missile; docs/design-combat.md D2, decided 2026-10-09). A missile
    // starts at the raider's closing speed, burns 30 g until it has added 10 km/s, then coasts, straight at the ship in the
    // ship's frame. 17m: a raider closing at 5 km/s fires at 1,999 km; the salvo hits at the closed form (144.6 s, 15 km/s)
    // and the raider never passes it. 17n: a raider at rest 2,000 km off, at updates the length of a 1×, a 100× and a 1,000×
    // frame: the hit on the update that holds 216.9 s, the burn's end on the one that holds 34.0 s, 10 km/s at the coast, and
    // the kill roll at 10 km/s. 17o: the kill roll takes the impact speed mid-burn and from a closing raider too. The kill
    // roll is read through set draws: the first missile's draw is just under KillChance(m, V), the second's just over, so
    // one kill means the chance used was V's within 1e-7. Flight is worked here, apart from Missile's own closed form.
    public partial class CareerQA
    {
        // the seconds to impact and the speed then, for a missile fired at s0 km/s from km out
        static (double s, double kms) Flight(double s0, double km)
        {
            double a = Career.MissileG * Ship.G0, tb = Career.MissileDvKms / a, onBurn = s0 * tb + 0.5 * a * tb * tb;
            if (km <= onBurn) { double t = (Math.Sqrt(s0 * s0 + 2 * a * km) - s0) / a; return (t, s0 + a * t); }
            return (tb + (km - onBurn) / (s0 + Career.MissileDvKms), s0 + Career.MissileDvKms);
        }

        static double FlightS(double s0, double km) => Flight(s0, km).s;

        // a combat rng with set draws, then 0.5: a strike's first draw is its kill draw
        sealed class SetRng : System.Random
        {
            readonly double[] u;
            int i;
            public SetRng(params double[] u) { this.u = u; }
            public override double NextDouble() => i < u.Length ? u[i++] : 0.5;
        }

        const double KillEps = 1e-7; // about 1e-5 km/s of speed at 10 km/s

        // 17m
        void MissileChaseChecks(SolarSystem qs)
        {
            var (_, at, vel) = Belt(qs);
            var b = MakeBench(qs, at, vel);
            try
            {
                // a bench flies neither ship: the raider closes at 5 km/s by hand, 1 s an update, and fires at 1,999 km
                var n = b.Ship.Pos.Norm;
                var e = b.C.StartContact(b.Job, b.Sys, b.Ship, b.Jd, b.Ship.Pos + n * 2004, b.Ship.Vel - n * 5);
                double t = 0, fired = double.NaN, hit = double.NaN, s0 = double.NaN, from = double.NaN, kms = double.NaN, ahead = double.NaN, gap = double.PositiveInfinity;
                string note = null;
                int Hits() => b.Said.FindAll(x => x.What == Career.Happening.MissileHit).Count;
                for (int i = 0; i < 400 && b.C.Encounter == e && double.IsNaN(hit); i++)
                {
                    double before = e.Missiles.Count > 0 ? e.Missiles[0].P.Mag : double.NaN;
                    int hits0 = Hits();
                    e.Pirate.Pos += (e.Pirate.Vel - b.Ship.Vel) * 1.0;
                    b.Tick(1);
                    t += 1;
                    if (double.IsNaN(fired) && e.Missiles.Count > 0)
                    {
                        fired = t; s0 = e.Missiles[0].S0; from = e.Missiles[0].P.Mag;
                        note = b.Said.FindLast(x => x.What == Career.Happening.MissileFired).Text;
                    }
                    else if (!double.IsNaN(fired) && Hits() > hits0)
                    {
                        hit = t;
                        ahead = D3.Dot(e.Pirate.Pos - b.Ship.Pos, n);
                    }
                    else if (!double.IsNaN(fired) && e.Missiles.Count > 0) kms = before - e.Missiles[0].P.Mag;
                    // the raider out along the line past its missile, every update the salvo is in flight after the one it fired in
                    if (t > fired && double.IsNaN(hit) && e.Missiles.Count > 0)
                        gap = Math.Min(gap, D3.Dot(e.Pirate.Pos - b.Ship.Pos, n) - D3.Dot(e.Missiles[0].P, n));
                }
                var (want, wantKms) = Flight(5, 1999);
                double flight = hit - fired;
                Check(Near(s0, 5, 1e-9) && Near(from, 1999, 1e-6) && Near(want, 144.6, 0.1) && flight >= want - 1e-6 && flight < want + 1 && Near(kms, wantKms, 1e-6) && Near(wantKms, 15, 1e-9)
                      && ahead > 0 && gap > 0 && note == Career.FiredNote(2, want),
                    $"defense 17m: a raider closing at 5 km/s fires at {from:N1} km (1,999), its missiles starting at {s0:0.000000} km/s (5): they hit {flight:0} s later (the 1 s update that holds {want:0.00} s, 144.6), " +
                    $"closing at {kms:0.000000} km/s over the last whole update ({wantKms:0}); the raider is then {ahead:N0} km out on the side it fired from (> 0) and was never past its salvo (the least lead {gap:N0} km, > 0); " +
                    $"at the old 2 km/s it would have hit {from / 2:0} s after firing, with the raider by then past the ship; note \"{note}\"");
            }
            finally { b.C.DropContact(); }
        }

        // a raider at rest `km` off along body direction dir on a 2-mount bench, or closing at `closing` km/s; a salvo by
        // DevSalvo, flown at updates of h s with set kill draws either side of the chance at the closed form's impact speed.
        // What it measured, as text, and whether every part held
        bool SetFlight(SolarSystem qs, double km, Vector3 dir, double closing, int bearWant, double h, out string got)
        {
            var b = PdcBench(qs, 1);
            try
            {
                var d = Frame.FromU(dir);
                var e = b.C.StartContact(b.Job, b.Sys, b.Ship, b.Jd, b.Ship.Pos + d * km, b.Ship.Vel - d * closing);
                var (T, V) = Flight(closing, km);
                int bear = b.C.MountsBearing(b.Ship, d);
                double p = PointDefense.KillChance(bear, V);
                SetCombatRng(b.C, new SetRng(p - KillEps, p + KillEps));
                int fired = b.C.DevSalvo(b.Ship);
                string note = b.Said.FindLast(x => x.What == Career.Happening.MissileFired).Text;
                double tb = Career.MissileDvKms / (Career.MissileG * Ship.G0), hit = double.NaN, burnEnd = double.NaN, coast = double.NaN, s0 = e.Missiles.Count > 0 ? e.Missiles[0].S0 : double.NaN;
                for (long k = 1; k * h < T + 2 * h && e.Missiles.Count > 0; k++)
                {
                    double before = e.Missiles[0].P.Mag;
                    bool coasting = !e.Missiles[0].Burning;
                    b.Tick(h);
                    if (e.Missiles.Count == 0) { hit = k * h; break; }
                    if (coasting) coast = (before - e.Missiles[0].P.Mag) / h;
                    if (double.IsNaN(burnEnd) && !e.Missiles[0].Burning) burnEnd = k * h;
                }
                int killNotes = b.Said.FindAll(x => x.What == Career.Happening.MissileKilled).Count;
                bool coastOk = T <= tb || Near(coast, closing + Career.MissileDvKms, 1e-6);
                bool burnOk = T <= tb ? double.IsNaN(burnEnd) : burnEnd >= tb - 1e-6 && burnEnd < tb + h + 1e-6;
                bool ok = fired == 2 && bear == bearWant && Near(s0, closing, 1e-9) && hit >= T - 1e-6 && hit < T + h + 1e-6 && burnOk && coastOk
                          && e.Killed == 1 && killNotes == 1 && note == Career.FiredNote(2, T);
                got = $"{km:N0} km at {closing:0} km/s, {h:0.###} s updates: hit at {hit:0.###} s (the update that holds {T:0.000}), burn over at {burnEnd:0.###} s ({(T <= tb ? "still burning at impact" : $"the update that holds {tb:0.000}")}), " +
                      $"{(T <= tb ? "" : $"coast {coast:0.000000} km/s, ")}{V:0.000} km/s at impact: {bear} bearing ({bearWant}), draws {p:0.000000} ∓ 1e-7 killed {e.Killed} (1), {killNotes} kill notes; \"{note}\"";
                return ok;
            }
            finally { b.C.DropContact(); }
        }

        // 17n
        void MissileWarpChecks(SolarSystem qs)
        {
            var got = new List<string>();
            bool ok = true;
            foreach (double warp in new[] { 1.0, 100, 1000 })
            {
                ok &= SetFlight(qs, Career.FireKm, Vector3.up, 0, 1, warp / 60, out string one);
                got.Add($"{warp:N0}×: {one}");
            }
            var (T, V) = Flight(0, Career.FireKm);
            Check(ok && Near(T, 216.9, 0.1) && Near(V, 10, 1e-9),
                $"defense 17n: a raider at rest {Career.FireKm:N0} km off, from the top (1 mount bears): {T:0.0} s (216.9) at {V:0} km/s (10), at updates of a 1×, a 100× and a 1,000× frame: {string.Join("; ", got)}");
        }

        // 17o
        void MissileKillChecks(SolarSystem qs)
        {
            bool mid = SetFlight(qs, 100, Vector3.up, 0, 1, 1, out string a);
            bool fast = SetFlight(qs, Career.FireKm, Vector3.forward, 5, 2, 1, out string c);
            var (_, vMid) = Flight(0, 100);
            var (_, vFast) = Flight(5, Career.FireKm);
            Check(mid && fast && Near(vMid, 7.67, 0.01) && Near(vFast, 15, 1e-9),
                $"defense 17o: the kill roll takes the impact speed: mid-burn from 100 km at rest, {vMid:0.00} km/s (7.67; at launch 0): {a}; " +
                $"from the nose (2 bear), the raider closing at 5 km/s, {vFast:0} km/s (15; P(kill) {PointDefense.KillChance(2, vFast):0.000}, at the launch speed it would be {PointDefense.KillChance(2, 5):0.000}): {c}");
        }
    }
}
