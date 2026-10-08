using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FogOfWar.EditorTools
{
    // Report only: -executeMethod FogOfWar.EditorTools.FogOfWarCareerReport.Express writes Logs/express-rate.txt
    // (OpenWorld/Career.ExpressReport.cs)
    public static class FogOfWarCareerReport
    {
        public static void Express()
        {
            int code = 0;
            try
            {
                var dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs");
                Directory.CreateDirectory(dir);
                using var t = new StreamWriter(Path.Combine(dir, "express-rate.txt"));
                OpenWorld.Career.ExpressReport(t, OpenWorld.PursuitCheck.Jd0);
            }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
