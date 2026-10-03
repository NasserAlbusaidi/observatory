using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FogOfWar.EditorTools
{
    // One-click setup and Windows build. Menu: Fog of War → …
    public static class FogOfWarBuild
    {
        const string ScenePath = "Assets/FogOfWar/FogOfWar.unity";

        [MenuItem("Fog of War/1. Create and open the sim scene")]
        public static void CreateScene()
        {
            FogOfWarArtImport.Reimport();
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var cam = new GameObject("Main Camera") { tag = "MainCamera" };
                cam.AddComponent<Camera>();
                new GameObject("Fog of War").AddComponent<FogOfWarApp>();
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorSceneManager.OpenScene(ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            ApplyPlayerSettings();
            Debug.Log("Fog of War: scene ready. Press Play, or use Fog of War → 2. Build Windows game.");
        }

        static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "Nasser";
            PlayerSettings.productName = "Fog of War Observatory";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;
            // Mono builds with only "Windows Build Support (Mono)" installed; IL2CPP would need the extra module.
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        }

        [MenuItem("Fog of War/2. Build Windows game")]
        public static void BuildWindows()
        {
            if (!File.Exists(ScenePath)) CreateScene();
            ApplyPlayerSettings();
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "Windows"));
            Directory.CreateDirectory(dir);
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(dir, "FogOfWar.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"Fog of War: built {opts.locationPathName} ({report.summary.totalSize / (1024 * 1024)} MB)");
                EditorUtility.RevealInFinder(opts.locationPathName);
            }
            else Debug.LogError("Fog of War: build failed, see the Console for the first error.");
        }
    }
}
