using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FogOfWar.EditorTools
{
    // Report only: -executeMethod FogOfWar.EditorTools.FogOfWarCareerReport.Express writes Logs/express-rate.txt
    // (OpenWorld/Career.ExpressReport.cs); .Gear writes Logs/gear-report.txt (OpenWorld/Career.GearReport.cs)
    public static class FogOfWarCareerReport
    {
        public static void Express() => Write("express-rate.txt", OpenWorld.Career.ExpressReport);

        public static void Gear() => Write("gear-report.txt", OpenWorld.Career.GearReport);

        static void Write(string file, Action<TextWriter, double> report)
        {
            int code = 0;
            try
            {
                var dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs");
                Directory.CreateDirectory(dir);
                using var t = new StreamWriter(Path.Combine(dir, file));
                report(t, OpenWorld.PursuitCheck.Jd0);
            }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
