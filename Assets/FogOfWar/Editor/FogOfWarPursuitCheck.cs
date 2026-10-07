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

        public static void Focused()
        {
            var report=new BenchmarkReport("float64 km/s/TDB; production pursuit; deterministic epoch 2461316.5");
            try
            {
                var sys=OpenWorld.SolarSystem.Build();
                foreach(string id in new[]{"S4r","S4","S4v","S1ahead"}) foreach(double h in new[]{1.0,5.0})
                {
                    var result=OpenWorld.PursuitCheck.Fly(sys,id,h);
                    report.Check(id+"/h="+h+"/hold",result.Holds ? 0:1,0,"failures","existing 7200 s alongside criterion");
                    report.Check(id+"/h="+h+"/clearance",Math.Max(0,result.Rs+1-result.MinRange),0,"km inside exclusion","craft radius +1 km exclusion; minimum sampled within every step");
                    if(id!="S1ahead")
                    {
                        report.Check(id+"/h="+h+"/catch",result.Capture < 0 ? double.PositiveInfinity : result.Capture/result.ClosedForm,id=="S4r" ? 1.2 : 1.05,"ratio","rest-to-rest relative acceleration closed form");
                        report.Check(id+"/h="+h+"/climb",result.ClimbIn,10,"entries","bounded mode switching");
                    }
                }
            }
            catch(Exception e){report.Check("exception",1,0,"exceptions","suite completes",e.ToString());}
            report.Save("pursuit-focused");
            if(Application.isBatchMode) EditorApplication.Exit(report.failed==0?0:1);
        }

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
