using System;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    public partial class OpenSystemScene
    {
        // The dev menu, for testing by hand: F7 opens it in the editor, in a build started with -fowdev, and under the
        // -fowcareer QA run (section 19, CareerQA.DevMenu.cs); other builds ignore the key. Each entry posts a note (a hit
        // posts its own). Test money keeps the credits topped up and the ship bought out; both are career state, so a save
        // made with it on keeps them: test on a new game. The panels are in OpenSystemScene.DevMenu.cs.
        static readonly bool DevKeys = Application.isEditor || Array.IndexOf(Environment.GetCommandLineArgs(), "-fowdev") >= 0
                                       || Array.IndexOf(Environment.GetCommandLineArgs(), "-fowcareer") >= 0;
        const double DevCredits = 100_000_000;
        const int DevSeed = 1;
        static bool devMoney, devSeeded; // for the session: a new game or a load keeps them on
        bool devMenu;
        double devSkipTo = double.NaN;   // the date a time skip stops at

        internal bool DevMenuOpen => devMenu;

        void DevInput()
        {
            if (FogOfWar.Controls.Locked || !Input.GetKeyDown(KeyCode.F7)) return;
            ToggleDevMenu();
        }

        internal void ToggleDevMenu()
        {
            if (!DevKeys) return;
            devMenu = !devMenu;
            if (devMenu) App.PanelToTop(); // the classic panel draws the menu at its top
        }

        // TickCareer, before the career's update
        void DevTick()
        {
            if (devMoney && career != null && !career.Sandbox)
            {
                career.Credits = Math.Max(career.Credits, DevCredits);
                career.ShipPaid = Career.ShipPrice;
            }
            if (double.IsNaN(devSkipTo)) return;
            // the step stops on QaStopJd; jd resolves ~40 µs, so within a second is there
            if (jd >= devSkipTo - 1.0 / 86400) { warpIdx = 0; DevNote("skip done: " + Frame.UtcFromTdb(jd).ToString("d MMM HH:mm", System.Globalization.CultureInfo.InvariantCulture)); }
            else if (warpIdx == 0) DevNote($"skip stopped at 1×, {(devSkipTo - jd) * 24:0.0} h short");
            else return;
            devSkipTo = double.NaN;
            QaStopJd = double.PositiveInfinity;
        }

        void DevNote(string text) => career.Notes.Add(new Career.Note { Text = "Dev: " + text, Good = true, Time = Time.realtimeSinceStartup });

        // each refuses with a note when its reason is not null, the same reason the menu shows
        bool DevRefused(string why, string what)
        {
            if (why == null) return false;
            DevNote($"{what}: {why}");
            return true;
        }

        string SandboxWhy => career.Sandbox ? "sandbox" : null;

        internal bool DevSetMoney(bool on)
        {
            if (DevRefused(SandboxWhy, "test money")) return false;
            devMoney = on;
            DevNote(on ? "test money on, ship bought out" : "test money off");
            return true;
        }

        internal void DevReveal(bool on)
        {
            Signature.RevealAll = on;
            DevNote(on ? "all traffic shown" : "traffic as the sensors see it");
        }

        // While on, each test chase starts the combat draws from the same seed, so its hits repeat
        internal void DevSeedCombat(bool on)
        {
            devSeeded = on;
            career.SeedCombat(on ? DevSeed : Environment.TickCount);
            DevNote(on ? $"combat draws seeded ({DevSeed}) at each test chase" : "combat draws random");
        }

        // ------------------------------------------------------------ the raider

        // Contacts ends a chase at once for a docked or landed ship, or one in a patrol's reach
        string ChaseWhy => SandboxWhy ?? (career.Docked != null ? "undock first" : ship.Landed != null ? "landed"
            : Threat.PatrolAt(sys, ship.Pos, jd) is Port p ? "in " + p.Name + "'s patrol reach" : null);

        internal Encounter DevChase(double km)
        {
            if (DevRefused(ChaseWhy, "no chase")) return null;
            if (devSeeded) career.SeedCombat(DevSeed);
            var e = career.DevChase(sys, ship, jd, km);
            DevNote($"{e.Hull} {km:N0} km off, at rest with the ship");
            return e;
        }

        string SalvoWhy => SandboxWhy ?? (career.Encounter == null ? "no chase" : career.Encounter.Magazine <= 0 ? "magazine empty" : null);

        internal int DevSalvo()
        {
            if (DevRefused(SalvoWhy, "no salvo")) return 0;
            return career.DevSalvo(ship); // the fired note is the real one
        }

        string RefillWhy => SandboxWhy ?? (career.Encounter == null ? "no chase" : career.Encounter.Magazine >= Career.MagazineSize ? "full" : null);

        internal bool DevRefill()
        {
            if (DevRefused(RefillWhy, "no refill")) return false;
            career.Encounter.Magazine = Career.MagazineSize;
            DevNote($"the raider's magazine refilled ({Career.MagazineSize})");
            return true;
        }

        // ------------------------------------------------------------ damage, repair, water and heat

        // A system with nothing to lose takes the hit on the hull (Career.Hit): those entries wait for something to hit
        string HitWhy(Career.HitSystem s)
        {
            if (career.Sandbox) return "sandbox";
            switch (s)
            {
                case Career.HitSystem.Tank: return ship.Fuel > 0 ? null : "tank dry";
                case Career.HitSystem.Radiators: return career.Build.Stowable ? null : "fixed radiators";
                case Career.HitSystem.Pdc: return career.WorkingMounts != 0 ? null : career.Build.MountCount == 0 ? "no PDCs" : "all mounts out";
            }
            return null;
        }

        // the real hit, its note and its effect; from the raider's side in a chase, else from ahead
        internal bool DevHit(Career.HitSystem s)
        {
            if (DevRefused(HitWhy(s), "no hit")) return false;
            var e = career.Encounter;
            career.Hit(ship, s, e != null ? Career.ToBody(ship, e.Pirate.Pos - ship.Pos) : Vector3.forward);
            return true;
        }

        string RepairWhy => SandboxWhy ?? (career.Damage.Any ? null : "no damage");

        internal bool DevRepair()
        {
            if (DevRefused(RepairWhy, "no repair")) return false;
            career.Damage = new ShipDamage();
            career.Apply(ship);
            DevNote("repaired, no charge");
            return true;
        }

        string WaterWhy => SandboxWhy ?? (ship.Fuel >= career.TankCapacity ? "full" : null);

        internal bool DevWater()
        {
            if (DevRefused(WaterWhy, "no water")) return false;
            career.DevFillTank(ship);
            DevNote($"tank filled, {ship.Fuel:N0} t");
            return true;
        }

        // Soak keeps a docked sink cold and a dry tank's full
        string SinkWhy => SandboxWhy ?? (career.Docked != null ? "docked" : ship.Fuel <= 0 ? "tank dry" : null);

        internal bool DevSink(bool full)
        {
            if (DevRefused(SinkWhy, "no sink change")) return false;
            career.SinkK = full ? Career.SinkMaxK : 0;
            DevNote(full ? "heat sink full" : "heat sink empty");
            return true;
        }

        // ------------------------------------------------------------ time

        // Time compression with a stop, not a jump: a jump would leave the ship where it was while every body moved on.
        // The scene's own caps (a chase, a crossing, a job's end) slow it or stop it.
        string SkipWhy => !double.IsNaN(devSkipTo) ? "skipping" : !double.IsPositiveInfinity(QaStopJd) ? "QA stop set" : null;

        internal bool DevSkip(double hours)
        {
            if (DevRefused(SkipWhy, "no skip")) return false;
            devSkipTo = jd + hours / 24;
            QaStopJd = devSkipTo;
            warpIdx = hours >= 24 ? 5 : 4; // 100k×: a day in about a second; 10k×: an hour in a third of one
            DevNote($"skipping {(hours >= 24 ? hours / 24 + " d" : hours + " h")} at {WarpNames[warpIdx]}");
            return true;
        }

        internal bool DevSkipping => !double.IsNaN(devSkipTo);

        // one line each: money and the tank, the chase, the damage, a skip
        string DevStatus()
        {
            var e = career.Encounter;
            string s = $"{career.Credits:N0} cr  ·  water {ship.Fuel:N1} / {career.TankCapacity:N0} t  ·  sink {career.SinkK:0} / {Career.SinkMaxK:0} K\n" +
                       (e != null ? $"{e.Hull}: {(e.Pirate.Pos - ship.Pos).Mag:N0} km, {e.Magazine} aboard, {e.Missiles.Count} inbound" : "No chase") +
                       "\nDamage: " + (career.DamageText() ?? "none");
            return DevSkipping ? s + $"\nSkipping: {(devSkipTo - jd) * 24:0.0} h to go" : s;
        }
    }
}
