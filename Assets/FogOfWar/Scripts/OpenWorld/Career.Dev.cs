using System;

namespace FogOfWar.OpenWorld
{
    // The dev menu's career side (OpenSystemScene.Dev.cs): a raider placed by hand, a salvo out of turn and a full tank.
    // Editor and -fowdev builds only; CareerQA.DevMenu.cs (section 19) runs each one.
    public partial class Career
    {
        public const string DevJobKey = "dev/chase";

        // A raider km off along the ship's line from the Sun, at rest with the ship: it closes on its own at PirateG. The
        // chase is about the job one is on now, the first job a raider works, or a test cargo run added for it.
        internal Encounter DevChase(SolarSystem sys, Ship ship, double jd, double km)
        {
            var job = Encounter != null && Active.Contains(Encounter.Job) ? Encounter.Job
                : Active.Find(c => c.Kind == JobKind.Cargo || c.Kind == JobKind.Passengers || c.Kind == JobKind.Survey);
            if (job == null)
            {
                job = new Contract { Id = nextId++, Kind = JobKind.Cargo, Title = "Test cargo", From = "Earth orbit", To = "Ceres", Tonnes = 10, Pay = 1000, Days = 30, Key = DevJobKey };
                Add(job, jd);
            }
            return StartContact(job, sys, ship, jd, ship.Pos + ship.Pos.Norm * km, ship.Vel);
        }

        // a salvo now, at any range and with missiles still in flight; 0 with no chase or an empty magazine
        internal int DevSalvo(Ship ship) => Encounter != null ? Fire(Encounter, ship) : 0;

        // the tank filled as a purchase fills it: the new water cools the sink
        internal void DevFillTank(Ship ship)
        {
            double before = ship.Fuel;
            ship.Fuel = TankCapacity;
            MixWater(before, ship.Fuel);
        }
    }
}
