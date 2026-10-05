using System;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // `-fowspawnrunner <class>` (courier, packet, liner, freighter, tanker, hauler): for filming. Once the career is
    // docked, accept an intercept for that hull and hold the ship 2 km beside the runner with the camera on it, so a
    // runner can be shot without flying an intercept. It accepts a real job, so pair it with -fowsavekey.
    public partial class OpenSystemScene
    {
        static readonly string SpawnRunnerClass = SpawnRunnerFromArgs();
        bool runnerSpawned;
        int runnerFrame = -1;

        static string SpawnRunnerFromArgs()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-fowspawnrunner");
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        void SpawnRunnerTick()
        {
            if (SpawnRunnerClass == null || career == null) return;
            if (!runnerSpawned && career.Docked != null)
            {
                runnerSpawned = true;
                string name = Career.RunnerName(SpawnRunnerClass);
                if (name == null) { Debug.LogWarning($"-fowspawnrunner: unknown class {SpawnRunnerClass}"); return; }
                career.Sandbox = false;
                Contract job = null;
                for (int seed = 1; seed < 400 && job == null; seed++)
                {
                    var c = career.Intercept(new System.Random(seed), sys, jd);
                    if (c != null && c.To.Contains(" " + name + " ")) job = c;
                }
                if (job == null) { Debug.LogWarning($"-fowspawnrunner: no {SpawnRunnerClass} intercept from {career.Docked.Name}"); return; }
                career.Accept(job, jd);
                var runner = sys[job.To];
                if (runner == null) { Debug.LogWarning($"-fowspawnrunner: accepted {job.To} but it has no body"); return; }
                ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Landed = null;
                ship.Pos = sys.Pos(runner, jd) + new D3(0, 0, 2);
                ship.Vel = sys.Vel(runner, jd);
                SetTarget(runner);
                runnerFrame = Time.frameCount;
                Debug.Log($"-fowspawnrunner: {job.To} ({SpawnRunnerClass}), ship 2 km beside it");
            }
            // the runner's model and length are known a few frames later: frame it then
            if (runnerFrame >= 0 && Time.frameCount > runnerFrame + 10)
            {
                runnerFrame = -1;
                SetPivot(Pivot.Target, runnerKm * 2.5);
            }
        }
    }
}
