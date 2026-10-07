using System;
using System.Reflection;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // Synchronous checks before the career's first flight; Start keeps their established order.
    public partial class CareerQA
    {
        void TouchdownSoundChecks()
        {
            var audio = Get<ShipAudio>("sound");
            var synth = typeof(ShipAudio).GetField("synth", F).GetValue(audio);
            var kind = synth.GetType();
            var sync = kind.GetField("inboxLock", F).GetValue(synth);
            var landed = ship.Landed; var crashed = ship.Crashed; string phase = ship.Phase;
            bool wasLanded = Get<bool>("wasLanded");
            // Hold the queue lock while observing the actual one-shot events; the audio thread cannot consume them.
            lock (sync)
            {
                int Touchdowns()
                {
                    var inbox = (Array)kind.GetField("inbox", F).GetValue(synth);
                    int count = (int)kind.GetField("inboxCount", F).GetValue(synth), hits = 0;
                    for (int i = 0; i < count; i++)
                    {
                        object pending = inbox.GetValue(i);
                        if ((Audio.ShipSound)pending.GetType().GetField("s", F).GetValue(pending) == Audio.ShipSound.Touchdown) hits++;
                    }
                    return hits;
                }
                try
                {
                    foreach (string name in new[] { "Earth", "Venus", "Mars", "Titan", "Jupiter", "Moon" })
                    {
                        ship.Landed = sys[name]; ship.Crashed = null; ship.Phase = "contact label without a landed prefix";
                        Put("wasLanded", false); int before = Touchdowns();
                        Call("TickSound", 0.0); int after = Touchdowns();
                        ship.Phase = "game saved"; Call("TickSound", 0.0);
                        Check(after == before + 1 && Touchdowns() == after,
                            name + " contact queues one touchdown sound regardless of phase text");
                    }
                    ship.Crashed = sys.Moon; Put("wasLanded", false); int clean = Touchdowns();
                    Call("TickSound", 0.0);
                    Check(Touchdowns() == clean, "a crash does not queue a successful touchdown sound");
                    // Exercise LoadGame itself with a temporary career and isolated QA save, without abandoning the live jobs.
                    // Keep this in preflight, before the tutorial/camera are used: tutorial state, camDist and jd are not restored.
                    var player = ship; var currentCareer = career; var reference = Get<Body>("reference");
                    var state = new System.Collections.Generic.Dictionary<string, object>();
                    foreach (string field in new[] { "autosaveArmed", "autosaveDue", "autosaveNote", "homePort", "careerTicks", "warpIdx", "pivot", "camMode", "targetIdx", "wasDocked" })
                        state[field] = s.GetType().GetField(field, F).GetValue(s);
                    string careerKey = SaveKey + "-career";
                    bool hadSave = PlayerPrefs.HasKey(SaveKey), hadCareer = PlayerPrefs.HasKey(careerKey);
                    string saveText = PlayerPrefs.GetString(SaveKey), careerText = PlayerPrefs.GetString(careerKey);
                    try
                    {
                        Put("ship", new Ship()); Put("career", new Career { Sys = sys });
                        s.TestSurfaceReadout(0.001, true);
                        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(ship.ToSave(jd, 0)));
                        PlayerPrefs.SetString(careerKey, JsonUtility.ToJson(currentCareer.ToSave(player)));
                        ship.Landed = null; Put("wasLanded", false);
                        int beforeLoad = Touchdowns();
                        s.LoadGame(); Call("TickSound", 0.0);
                        Check(ship.Landed == sys.Earth && Touchdowns() == beforeLoad,
                            "loading a landed save from flight queues no touchdown sound");
                    }
                    finally
                    {
                        Put("ship", player); Put("career", currentCareer); Put("reference", reference);
                        foreach (var saved in state) Put(saved.Key, saved.Value);
                        if (hadSave) PlayerPrefs.SetString(SaveKey, saveText); else PlayerPrefs.DeleteKey(SaveKey);
                        if (hadCareer) PlayerPrefs.SetString(careerKey, careerText); else PlayerPrefs.DeleteKey(careerKey);
                    }
                }
                finally { ship.Landed = landed; ship.Crashed = crashed; ship.Phase = phase; Put("wasLanded", wasLanded); }
            }
        }

        void SurfaceReadoutChecks()
        {
            var player = ship; var reference = Get<Body>("reference");
            try
            {
                Put("ship", new Ship());
                s.TestSurfaceReadout(0.001, true);
                double centre = (ship.Vel - sys.Vel(sys.Earth, jd)).Mag;
                double shown = (ship.Vel - s.RefVel(sys.Earth)).Mag;
                Check(shown == 0 && centre > 0.1 && s.RefName(sys.Earth).EndsWith(" surface"),
                    $"landed equatorial Earth speed {shown:F6} km/s (centre {centre:F6}), label {s.RefName(sys.Earth)}");
                var row = s.TestSpeedReadout();
                Check(s.ClassicSpeedLine.Contains("0 m/s vs Earth surface") && !s.ClassicSpeedLine.Contains("10^")
                    && row.label == "Speed vs Earth surface" && row.value == "0 m/s" && !row.value.Contains("10^"),
                    $"landed text: {s.ClassicSpeedLine}; {row.label} {row.value}");
                Check((s.RefVel(sys.Moon) - sys.Vel(sys.Moon, jd)).Mag < 1e-12 && !s.RefName(sys.Moon).EndsWith(" surface"),
                    "a different reference keeps its centre velocity while landed on Earth");
                s.TestSurfaceReadout(50, false);
                Check((ship.Vel - s.RefVel(sys.Earth)).Mag < 0.001 && s.RefName(sys.Earth).EndsWith(" surface"),
                    "near-surface arrival uses the surface frame before touchdown (50 km above Earth)");
                ship.Vel = s.RefVel(sys.Earth) + new D3(0.0001, 0, 0);
                row = s.TestSpeedReadout();
                Check(row.value == "0.1 m/s" && s.ClassicSpeedLine.Contains("0.1 m/s vs Earth surface")
                    && !s.ClassicSpeedLine.Contains("10^"), "slow descent reads 0.1 m/s in the classic line and data row");
                double altitude = Math.Max(300, sys.Earth.Radius * 0.1) + 100;
                s.TestSurfaceReadout(altitude, false);
                ship.Vel = sys.Vel(sys.Earth, jd) + sys.Earth.FromFixed(new D3(0, Math.Sqrt(sys.Earth.GM / (sys.Earth.Radius + altitude)), 0), jd);
                shown = (ship.Vel - s.RefVel(sys.Earth)).Mag;
                centre = (ship.Vel - sys.Vel(sys.Earth, jd)).Mag;
                Check(Math.Abs(shown - centre) < 1e-12 && !s.RefName(sys.Earth).EndsWith(" surface"),
                    $"parking orbit at {altitude:F1} km reads centre speed {shown:F6} km/s, label {s.RefName(sys.Earth)}");
                foreach (string name in new[] { "Moon", "Ceres", "Vesta", "Pallas", "Mercury" })
                {
                    var body = sys[name]; double boundary = Math.Max(300, body.Radius * 0.1);
                    s.TestSurfaceReadout(boundary - 0.001, false, name);
                    Check(s.RefName(body) == name + " surface" && (ship.Vel - s.RefVel(body)).Mag < 1e-8
                        && !(bool)s.GetType().GetMethod("InArrivalBoundary", F).Invoke(s, new object[] { body }), name + " uses the near-surface frame without an atmospheric arrival notice");
                    s.TestSurfaceReadout(boundary + 0.001, false, name);
                    Check(s.RefName(body) == name && (s.RefVel(body) - sys.Vel(body, jd)).Mag == 0,
                        name + " switches back to the centre frame above the altitude boundary");
                    double parking = Ship.Standoff(body) - body.Radius;
                    s.TestSurfaceReadout(parking, false, name);
                    Check(parking > boundary && s.RefName(body) == name,
                        $"{name} switch altitude {boundary:F3} km, Ship.Standoff radius {Ship.Standoff(body):F3} km, parking altitude {parking:F3} km");
                }
                s.TestSurfaceReadout(50, true, "Jupiter");
                row = s.TestSpeedReadout();
                Check(s.RefName(sys["Jupiter"]) == "Jupiter cloud tops" && row.label == "Speed vs Jupiter cloud tops"
                    && row.value == "0 m/s" && s.ClassicSpeedLine.Contains("0 m/s vs Jupiter cloud tops"),
                    "giant cloud-top hold reads 0 m/s and names cloud tops in both text readouts");
                s.TestSurfaceReadout(50, false, "Sun");
                Check(s.RefName(sys.Sun) == "Sun", "the Sun never uses a surface readout frame");
                var proxy = new Body { Name = "readout craft probe", Parent = sys.Sun, Radius = 0.05, Path = _ => ship.Pos };
                Check(s.RefName(proxy) == proxy.Name && (s.RefVel(proxy) - sys.Vel(proxy, jd)).Mag == 0,
                    "a craft path proxy never uses a surface readout frame");
            }
            finally { Put("ship", player); Put("reference", reference); }
        }

        void CoastStateChecks()
        {
            var sun = new Body { Name = "Sun", Look = Look.Sun };
            var testSys = new SolarSystem { Sun = sun }; testSys.Bodies.Add(sun);
            var target = new Body { Name = "Derelict aim probe", Parent = sun, Radius = 0.05,
                Path = _ => new D3(10000, 0, 0), PathAccel = _ => D3.Zero };
            var probe = new Ship { Pos = new D3(11000, 0, 0), Vel = new D3(1, 0, 0), Target = target,
                Plan = Plan.MatchVelocity, UnlimitedFuel = true, Att = Quaternion.LookRotation(Frame.U(new D3(1, 0, 0))) };
            probe.RecheckPlan(testSys, jd, sun);
            Check(probe.Throttle == 0 && probe.Phase == "turning", "coast wake-up waits for the 3 degree drive aim gate");
            probe.Att = Quaternion.LookRotation(Frame.U(new D3(-1, 0, 0)));
            probe.RecheckPlan(testSys, jd, sun);
            Check(probe.Throttle > 0, "coast wake-up permits an aligned drive command");
            probe.BurnSeconds = 12; probe.CruiseSeconds = 34;
            var saved = JsonUtility.FromJson<Ship.SaveData>(JsonUtility.ToJson(probe.ToSave(jd, 0)));
            probe.BurnSeconds = 100; probe.CruiseSeconds = 200;
            probe.FromSave(saved, testSys);
            Check(probe.BurnSeconds == 12 && probe.CruiseSeconds == 34, "save round-trip restores burn and cruise time");
            saved.burnSeconds = saved.cruiseSeconds = 0;
            probe.FromSave(saved, testSys); probe.Phase = "game loaded";
            var codex = new Codex(); codex.CheckFeats(new Career(), probe);
            Check(probe.BurnSeconds == 0 && probe.CruiseSeconds == 0 && !codex.Has("coast"),
                "loading a pre-cruise save cannot grant the coast feat from a later timeline");
        }

        void SaveKeyChecks()
        {
            var keyPolicy = typeof(OpenSystemScene).GetMethod("SaveKeyFor", BindingFlags.Static | BindingFlags.NonPublic);
            string Key(string[] args, string path) => (string)keyPolicy.Invoke(null, new object[] { args, path });
            Check(SaveKey != "fow-save-v1" && SaveKey != "fow-career-v1", "unattended career never uses the player's save key");
            var suiteKeys = new System.Collections.Generic.HashSet<string>();
            foreach (string flag in OpenSystemScene.UnattendedSuites())
            {
                string key = Key(new[] { flag }, Application.dataPath);
                Check(key.StartsWith("fow-save-qa-", StringComparison.Ordinal) && suiteKeys.Add(key)
                    && key == Key(new[] { flag }, Application.dataPath)
                    && key != Key(new[] { flag }, Application.dataPath + "-other"),
                    flag + " reuses a bounded isolated save key per build and suite");
            }
            Check(Key(Array.Empty<string>(), Application.dataPath) == "fow-save-v1"
                && Key(new[] { "-fowcareer", "-fowsavekey", "fow-save-job3" }, Application.dataPath) == "fow-save-job3",
                "normal play keeps its key and an explicit save key wins");
        }
    }
}
