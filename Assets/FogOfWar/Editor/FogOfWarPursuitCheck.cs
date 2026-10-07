using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FogOfWar.EditorTools
{
    // A pirate on Plan.Pursue against the player (OpenWorld/PursuitCheck.cs). Writes Logs/pursuit-check-table.txt and
    // Logs/pursuit-check-raw.txt; in batch mode the exit code is the verdict (0 pass, 1 fail).
    public static class FogOfWarPursuitCheck
    {
        [MenuItem("Fog of War/Pursuit check")]
        public static void Run() => Exec(true);

        // the stand-in's numerics alone: -executeMethod FogOfWar.EditorTools.FogOfWarPursuitCheck.Probe
        public static void Probe() => Exec(false);

        static void Exec(bool full)
        {
            int code;
            try
            {
                var dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs");
                Directory.CreateDirectory(dir);
                using var table = new StreamWriter(Path.Combine(dir, full ? "pursuit-check-table.txt" : "pursuit-probe.txt"));
                if (full)
                {
                    using var raw = new StreamWriter(Path.Combine(dir, "pursuit-check-raw.txt"));
                    code = OpenWorld.PursuitCheck.Run(table, raw);
                }
                else code = OpenWorld.PursuitCheck.Probe(table) < 1e-4 ? 0 : 1;
            }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            Debug.Log("PURSUIT exit " + code);
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
