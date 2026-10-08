namespace FogOfWar.OpenWorld
{
    // Combat Phase 2 (docs/design-combat.md): what a sensor sees is jet power plus a cold hull's idle, and its range grows as
    // √P. A raider sees a ship while it is loud (seen across the raider's whole start window) and keeps the track TrackHoldS.
    // A cold hull radiates IdleW unless its radiators are stowed (Phase 3).
    public static class Signature
    {
        public const double IdleW = 2e6;                          // [design] a cold hull
        public const double StockKm = 10000;                      // [design] stock sensors see IdleW this far
        public const double TrackHoldS = Threat.BoardingSeconds;  // [design] a raider keeps a cold ship's track this long
        // the far edge of a raider's start window from relative rest (Career.Spawn): a·CatchMaxS²/4 = 1,143,848 km
        public const double WindowKm = Career.PirateG * Ship.G0 * Career.CatchMaxS * Career.CatchMaxS / 4;
        // loud: seen by stock sensors across the whole window. 26.17 GW, 0.00107 g at 500 t
        public const double LoudW = IdleW * (WindowKm / StockKm * (WindowKm / StockKm) - 1);

        public static double JetW(double massT, double aKms2) => 0.5 * massT * 1e3 * aKms2 * 1e3 * Ship.Ve * 1e3; // W
        public static double JetW(Ship s) => JetW(s.Mass, s.Throttle * s.AMax + s.RcsLevel * Ship.RcsAccel);   // display
        public static double RangeKm(double jetW, double baseKm = StockKm, double coldW = IdleW) => baseKm * System.Math.Sqrt((jetW + coldW) / IdleW);
        public static double ColdW(double rho) => rho * rho * IdleW;                                  // radiators stowed to rho: 0.3 gives 180 kW
        public static string HoldText(double s) => s >= 3600 ? $"{s / 3600:0.#} h" : $"{System.Math.Round(s / 60):0} min";
        public static bool Loud(double massT, double aKms2) => JetW(massT, aKms2) >= LoudW;

        // Combat Phase 2b: traffic ships. Wet mass = 500 t × (L/52 m)³, the player hull's 300 t dry × 5/3 (Phase 6 rule).
        public const double RefWetT = 500, RefLengthM = 52;                                    // [design]
        public static double HullMassT(double lengthM) => RefWetT * System.Math.Pow(lengthM / RefLengthM, 3);
        public static double SensorKm(int level) => level > 0 ? 2 * StockKm : StockKm;         // as Codex.SensorRange
        public static bool RevealAll;                                                          // shots and QA only: draw every traffic ship
        static double stockBurnW;
        public static double StockBurnW => stockBurnW > 0 ? stockBurnW : stockBurnW = ShipBuild.Stock().JetPowerTW * 1e12; // 24.52 TW
    }
}
