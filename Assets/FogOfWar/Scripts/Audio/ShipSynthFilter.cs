using System;
using FogOfWar.Audio;
using UnityEngine;

namespace FogOfWar
{
    // Runs the synth on Unity's audio thread.
    public class ShipSynthFilter : MonoBehaviour
    {
        public ShipSynth Synth;
        public static long Blocks;                // rendered blocks: proves the filter runs (AudioQA)
        public static Action<float[], int> Tap;   // audio-thread copy of what the synth rendered (AudioQA records it)

        void OnAudioFilterRead(float[] data, int channels)
        {
            var s = Synth;
            if (s == null) return;
            s.Render(data, channels);
            Blocks++;
            Tap?.Invoke(data, channels);
        }
    }
}
