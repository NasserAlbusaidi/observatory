using System;
using FogOfWar.Audio;
using UnityEngine;

namespace FogOfWar
{
    // The ship's sound in the game. Everything is synthesised live (Audio/Dsp/ShipSynth), so there are no audio files
    // and no licences to track. Vacuum carries no sound: inside the hull (the cockpit view) you hear what reaches you
    // through the structure and the cabin air; outside views hear only the console, unless Cinematic sound is on.
    // AUDIO-DESIGN.md explains each sound.
    public class ShipAudio
    {
        readonly ShipSynth synth;
        public bool Muted;
        public float Hull = 1, Ambient = 1, Console = 1; // group levels from Settings → Audio
        public bool Interior = true, Cinematic;

        public ShipAudio(Transform parent, Func<UnityEngine.Object, UnityEngine.Object> own)
        {
            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            synth = new ShipSynth(rate, (uint)Environment.TickCount);
            var go = new GameObject("Ship audio");
            go.transform.SetParent(parent, false);
            // the synth adds itself to this source's output; a looping silent clip keeps the source (and the filter) running
            var src = go.AddComponent<AudioSource>();
            src.clip = (AudioClip)own(AudioClip.Create("Silence", rate, 1, rate, false));
            src.loop = true; src.spatialBlend = 0; src.playOnAwake = false; src.volume = 1;
            go.AddComponent<ShipSynthFilter>().Synth = synth;
            src.Play();
        }

        // throttle 0..1 of the rating; g the applied acceleration; angRate the rotation rate in rad/s;
        // thermal 0..1 hull temperature change; pingRate dust hits per second
        public void Update(double throttle, double g, bool rcsOn, float angRate, float thermal, float pingRate)
        {
            synth.Throttle = (float)throttle;
            synth.ThrustG = (float)g;
            synth.RcsOn = rcsOn;
            synth.AngRate = angRate;
            synth.Thermal = thermal;
            synth.PingRate = pingRate;
            synth.Interior = Interior ? 1 : 0;
            synth.CinematicOn = Cinematic ? 1 : 0;
            synth.Master = Muted ? 0 : 1;
            synth.BusLevel[ShipSynth.Machinery] = Hull;
            synth.BusLevel[ShipSynth.Cinematic] = Hull;
            synth.BusLevel[ShipSynth.Cabin] = Ambient;
            synth.BusLevel[ShipSynth.Console] = Console;
        }

        public void Notice(bool goodNews) => synth.Play(goodNews ? ShipSound.ChimeGood : ShipSound.ChimeBad);
        public void Caution() => synth.Play(ShipSound.CautionTone);
        public void Warning() => synth.Play(ShipSound.WarningBeep);
        public void Touchdown(float strength) => synth.Play(ShipSound.Touchdown, Mathf.Clamp01(strength));

        // contact, the clamp motors drive the latches home, then the hatch equalises
        public void Dock()
        {
            synth.Play(ShipSound.DockContact);
            synth.Play(ShipSound.ClampMotor, 1, 0.6f);
            synth.Play(ShipSound.Latch, 1, 1.9f); synth.Play(ShipSound.Latch, 1, 2.15f); synth.Play(ShipSound.Latch, 1, 2.35f);
            synth.Play(ShipSound.Equalise, 1, 2.8f);
        }

        public void Undock()
        {
            synth.Play(ShipSound.Latch); synth.Play(ShipSound.Latch, 1, 0.2f);
            synth.Play(ShipSound.ClampMotorReverse, 1, 0.4f);
            synth.Play(ShipSound.PushOff, 1, 1.6f);
        }
    }
}
