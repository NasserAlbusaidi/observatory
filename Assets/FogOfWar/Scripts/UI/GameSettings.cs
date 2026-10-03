using System;
using System.IO;
using UnityEngine;

namespace FogOfWar
{
    // Player settings outside the key bindings (Controls.cs) and the ray-trace quality (FogOfWarApp.Quality).
    // Stored in PlayerPrefs under fow-set-*.
    public static class GameSettings
    {
        static float F(string k, float d) => PlayerPrefs.GetFloat("fow-set-" + k, d);
        static int I(string k, int d) => PlayerPrefs.GetInt("fow-set-" + k, d);
        static void SetF(string k, float v) { PlayerPrefs.SetFloat("fow-set-" + k, v); PlayerPrefs.Save(); Changed?.Invoke(); }
        static void SetI(string k, int v) { PlayerPrefs.SetInt("fow-set-" + k, v); PlayerPrefs.Save(); Changed?.Invoke(); }

        public static event Action Changed;
        public static bool SilentOutput; // the audio check records the synth with the speakers off

        // interface
        public static float UiScale { get => F("ui-scale", 1f); set => SetF("ui-scale", Mathf.Clamp(value, 0.7f, 1.6f)); }
        public static float HudOpacity { get => F("hud-opacity", 1f); set => SetF("hud-opacity", Mathf.Clamp(value, 0.3f, 1f)); }
        public static bool ShowHints { get => I("hints", 1) == 1; set => SetI("hints", value ? 1 : 0); }
        public static bool ShowFps { get => I("fps", 0) == 1; set => SetI("fps", value ? 1 : 0); }
        public static bool ShowLabels { get => I("labels", 1) == 1; set => SetI("labels", value ? 1 : 0); }
        // the original IMGUI instrument panel instead of the flight console (keeps every scene control reachable)
        public static bool ClassicPanel { get => I("classic", 0) == 1; set => SetI("classic", value ? 1 : 0); }

        // graphics
        public static int DisplayMode { get => I("display", 0); set => SetI("display", value); } // 0 borderless, 1 exclusive, 2 windowed
        public static bool VSync { get => I("vsync", 1) == 1; set => SetI("vsync", value ? 1 : 0); }
        public static int FrameCap { get => I("fpscap", 120); set => SetI("fpscap", value); } // 0 = unlimited

        // audio: levels for the ship's sound (ShipAudio applies them every frame, menus included)
        public static float Master { get => F("vol-master", 0.8f); set => SetF("vol-master", Mathf.Clamp01(value)); }
        public static bool ShipSound { get => I("ship-sound", 1) == 1; set => SetI("ship-sound", value ? 1 : 0); }
        public static float Effects { get => F("vol-fx", 0.8f); set => SetF("vol-fx", Mathf.Clamp01(value)); }      // drive, thrusters, machinery
        public static float Music { get => F("vol-music", 0.6f); set => SetF("vol-music", Mathf.Clamp01(value)); }   // life support and hull
        public static float Chimes { get => F("vol-chimes", 0.8f); set => SetF("vol-chimes", Mathf.Clamp01(value)); } // console and alarms
        // the film version: drive roar and thruster hiss in outside views, where vacuum would carry nothing
        public static bool CinematicSound { get => I("cine-sound", 0) == 1; set => SetI("cine-sound", value ? 1 : 0); }
        public static bool MuteInBackground { get => I("mute-bg", 1) == 1; set => SetI("mute-bg", value ? 1 : 0); }

        // capture
        public static int ShotScale { get => I("shot-scale", 1); set => SetI("shot-scale", Mathf.Clamp(value, 1, 4)); } // 1, 2 or 4
        public static int ClipSeconds { get => I("clip-sec", 15); set => SetI("clip-sec", Mathf.Clamp(value, 5, 60)); }
        public static int ClipFps { get => I("clip-fps", 30); set => SetI("clip-fps", value == 60 ? 60 : 30); }
        public static int ClipQuality { get => I("clip-q", 1); set => SetI("clip-q", Mathf.Clamp(value, 0, 2)); } // 0 small, 1 high, 2 near-lossless

        public static string CaptureDir
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.IndexOf(args, "-fowcapturedir"); // tests write somewhere other than Pictures
                if (i >= 0 && i + 1 < args.Length) return args[i + 1];
                var pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                if (string.IsNullOrEmpty(pics)) pics = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures");
                return Path.Combine(pics, "FogOfWar");
            }
        }

        public static void ApplyDisplay()
        {
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = FrameCap > 0 ? FrameCap : -1;
            AudioListener.volume = SilentOutput ? 0 : 1; // Master is applied in the ship synth, so the audio check can meter it
            AudioListener.pause = false;
        }

        public static void SetDisplayMode(int mode)
        {
            DisplayMode = mode;
            var m = mode == 1 ? FullScreenMode.ExclusiveFullScreen : mode == 2 ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
            if (mode == 2) Screen.SetResolution(Mathf.Min(Screen.width, Display.main.systemWidth * 3 / 4), Mathf.Min(Screen.height, Display.main.systemHeight * 3 / 4), m);
            else Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, m);
        }
    }
}
