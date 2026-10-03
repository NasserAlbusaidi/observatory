using UnityEditor;
using UnityEngine;

namespace FogOfWar.EditorTools
{
    // Shader compile errors only show in the Console when a material first renders. This checks every shader up front.
    public static class FogOfWarDiagnostics
    {
        [MenuItem("Fog of War/3. Check shaders")]
        public static void CheckShaders()
        {
            int bad = 0, total = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Shader", new[] { "Assets/FogOfWar" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                total++;
                foreach (var m in ShaderUtil.GetShaderMessages(shader))
                {
                    string line = $"Fog of War: {path}:{m.line} [{m.platform}] {m.message} {m.messageDetails}";
                    if (m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) { Debug.LogError(line); bad++; }
                    else Debug.LogWarning(line);
                }
                if (ShaderUtil.ShaderHasError(shader) && bad == 0) { Debug.LogError($"Fog of War: {path} has errors"); bad++; }
            }
            Debug.Log($"Fog of War: checked {total} shaders, {bad} with errors.");
        }
    }
}
