using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FogOfWar.EditorTools
{
    // Builds the fidelity branch to Builds/Fidelity/FogOfWarFidelity.exe, apart from the main Builds/Windows game, so a review build
    // never replaces the build that QA plays.
    public static class FogOfWarFidelityBuild
    {
        [MenuItem("Fog of War/Build fidelity review copy")]
        public static void Build()
        {
            if (!File.Exists("Assets/FogOfWar/FogOfWar.unity")) FogOfWarBuild.CreateScene();
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "Fidelity"));
            Directory.CreateDirectory(dir);
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.colorSpace = ColorSpace.Linear;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/FogOfWar/FogOfWar.unity" },
                locationPathName = Path.Combine(dir, "FogOfWarFidelity.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            if (report.summary.result == BuildResult.Succeeded) Debug.Log($"Fog of War: fidelity build ok ({report.summary.totalSize / (1024 * 1024)} MB)");
            else { Debug.LogError("Fog of War: fidelity build failed"); if (Application.isBatchMode) EditorApplication.Exit(1); }
        }
    }
}
