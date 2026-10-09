using System;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    public partial class OpenSystemScene
    {
        // Test money, for trying refits and hulls without the career's progression. F7 toggles it in the editor, or in a
        // build started with -fowdev; other builds ignore the key. While on, the credits stay topped up and the ship is
        // bought out. Both are career state, so a save made with it on keeps them: test on a new game.
        static readonly bool DevKeys = Application.isEditor || Array.IndexOf(Environment.GetCommandLineArgs(), "-fowdev") >= 0;
        const double DevCredits = 100_000_000;
        static bool devMoney; // for the session: a new game or a load keeps it on

        void DevInput()
        {
            if (!DevKeys || FogOfWar.Controls.Locked || !Input.GetKeyDown(KeyCode.F7)) return;
            devMoney = !devMoney;
            career?.Notes.Add(new Career.Note { Text = devMoney ? "Test money on (F7): credits topped up, ship bought out" : "Test money off (F7)", Good = true, Time = Time.realtimeSinceStartup });
        }

        void DevMoney()
        {
            if (!devMoney || career == null || career.Sandbox) return;
            career.Credits = Math.Max(career.Credits, DevCredits);
            career.ShipPaid = Career.ShipPrice;
        }
    }
}
