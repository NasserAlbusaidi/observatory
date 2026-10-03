using UnityEngine;

namespace FogOfWar
{
    // Starts the simulator in any scene, including an empty one, so the project needs no hand-built scene files.
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start()
        {
            if (Object.FindAnyObjectByType<FogOfWarApp>() != null) return;
            var go = new GameObject("Fog of War");
            go.AddComponent<FogOfWarApp>();
        }
    }
}
