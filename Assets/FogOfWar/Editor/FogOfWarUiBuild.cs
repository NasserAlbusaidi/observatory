using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FogOfWar.EditorTools
{
    // Builds the UI branch to Builds/UI/FogOfWarUI.exe, apart from the main Builds/Windows game, so a review build
    // never replaces the build that QA plays.
    public static class FogOfWarUiBuild
    {
        [MenuItem("Fog of War/3. Build UI review copy")]
        public static void Build()
        {
            if (!File.Exists("Assets/FogOfWar/FogOfWar.unity")) FogOfWarBuild.CreateScene();
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "UI"));
            Directory.CreateDirectory(dir);
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.colorSpace = ColorSpace.Linear;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/FogOfWar/FogOfWar.unity" },
                locationPathName = Path.Combine(dir, "FogOfWarUI.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            if (report.summary.result == BuildResult.Succeeded) Debug.Log($"Fog of War: UI build ok ({report.summary.totalSize / (1024 * 1024)} MB)");
            else { Debug.LogError("Fog of War: UI build failed"); if (Application.isBatchMode) EditorApplication.Exit(1); }
        }
    }
}
