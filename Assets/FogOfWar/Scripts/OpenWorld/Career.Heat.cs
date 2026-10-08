using System;

namespace FogOfWar.OpenWorld
{
    // Combat Phase 3 (docs/design-combat.md): stowable radiators put the hull's waste heat into its own reaction mass
    // (water) while a raider could hold its track, the 2 h after a loud moment or a chase. Stowed, the hull is cold to
    // StowRho of the normal range and a raider loses it in StowRho × TrackHoldS. SinkK is the water's rise in K above
    // port water: intensive, so water that is burned, vented or sold takes its heat with it. Gameplay values.
    public partial class Career
    {
        public const double WaterC = 4186;         // J/kg·K [calc]
        public const double SinkLoadW = 2e6;       // [design] the hull's waste heat; the same number as Signature.IdleW, not the same role
        public const double SinkMaxK = 100;        // [design]
        public static double SinkSecondsPerT => WaterC * 1000 * SinkMaxK / SinkLoadW;   // 209.3 s of 2 MW per tonne of water
        public double SinkK;
        double fillJd = double.NaN;                // the date the sink filled inside this update, else NaN: SeenSeconds reads it

        // stowable radiators, water aboard and room in the sink: a raider's hold is StowRho of TrackHoldS
        public bool DarkReady(Ship ship) => !Sandbox && Build.Stowable && ship.Fuel > 0 && SinkK < SinkMaxK;
        public double HoldS(Ship ship) => DarkReady(ship) ? Signature.TrackHoldS * Build.StowRho : Signature.TrackHoldS;
        // the radiators are stowed now: dark-ready, not docked, quiet, and inside the 2 h window (PR 3b adds: or a chase is on)
        public bool Stowed(Ship ship, double jd) => DarkReady(ship) && Docked == null && !LoudNow(ship) && (jd - ship.LastLoudJd) * 86400 < Signature.TrackHoldS;
        public double ColdW(Ship ship, double jd) => Stowed(ship, jd) ? Signature.ColdW(Build.StowRho) : Signature.IdleW;
        public double SinkLeftS(Ship ship) => ship.Fuel > 0 ? Math.Max(0, SinkMaxK - SinkK) * WaterC * ship.Fuel * 1000 / SinkLoadW : 0;

        // Once per Tick, after the dock test and before the roll. The stowed seconds are this update's overlap with the 2 h
        // after the last loud moment: the tail SeenSeconds counts, at TrackHoldS. It holds no loud seconds by definition.
        // (PR 3b adds the chase: with Encounter on, s = simDt − this update's loud seconds.)
        void Soak(Ship ship, double simDt)
        {
            fillJd = double.NaN;
            if (Docked != null) { SinkK = 0; return; }                     // the port's loop takes the heat
            if (!Build.Stowable || simDt <= 0 || !spanOk) return;
            if (ship.Fuel <= 0) { SinkK = SinkMaxK; return; }               // no water, no sink: radiators out
            double s = (Math.Min(jd, ship.LastLoudJd + Signature.TrackHoldS / 86400) - Math.Max(spanJd, ship.LastLoudJd)) * 86400;
            s = Math.Max(0, Math.Min(s, simDt));
            double toFullS = (SinkMaxK - SinkK) * WaterC * ship.Fuel * 1000 / SinkLoadW;
            if (SinkK < SinkMaxK && toFullS <= s) fillJd = Math.Max(spanJd, ship.LastLoudJd) + toFullS / 86400;
            SinkK = Math.Min(SinkMaxK, SinkK + SinkLoadW * s / (WaterC * ship.Fuel * 1000));
        }

        // cold port water into a warm tank (the tender): the temperature is the mass-weighted mean
        void MixWater(double before, double after) => SinkK = after > 0 ? SinkK * Math.Max(0, before) / after : 0;
    }
}
