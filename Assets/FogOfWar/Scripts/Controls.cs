using System;
using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar
{
    // Rebindable keyboard actions for the open world. Each action has a primary and a secondary key; bindings persist in
    // PlayerPrefs. Unattended QA can hold or press actions virtually (Hold/Tap), which also works while real input is
    // locked.
    public static class Controls
    {
        public enum Action
        {
            PitchDown, PitchUp, YawLeft, YawRight, RollLeft, RollRight,
            ThrottleUp, ThrottleDown, ThrottleFull, ThrottleCut,
            RcsForward, RcsBack, RcsUp, RcsDown, RcsLeft, RcsRight,
            FlightAssist, Flip, Engage, NextTarget, WarpUp, WarpDown, Map, CameraMode, Help, QuickSave,
            Screenshot, PhotoMode, RecordClip, Floodlights, VelocityRef, MarkLog,
        }

        public static readonly Dictionary<Action, string> Label = new Dictionary<Action, string>
        {
            [Action.PitchDown] = "Pitch nose down", [Action.PitchUp] = "Pitch nose up", [Action.YawLeft] = "Yaw left", [Action.YawRight] = "Yaw right",
            [Action.RollLeft] = "Roll left", [Action.RollRight] = "Roll right",
            [Action.ThrottleUp] = "Throttle up", [Action.ThrottleDown] = "Throttle down", [Action.ThrottleFull] = "Throttle full", [Action.ThrottleCut] = "Throttle cut",
            [Action.RcsForward] = "RCS forward", [Action.RcsBack] = "RCS back", [Action.RcsUp] = "RCS up", [Action.RcsDown] = "RCS down",
            [Action.RcsLeft] = "RCS left", [Action.RcsRight] = "RCS right",
            [Action.FlightAssist] = "Flight assist on/off", [Action.Flip] = "Flip (turn 180°)", [Action.Engage] = "Engage / abort nav plan",
            [Action.NextTarget] = "Next target", [Action.WarpUp] = "Time compression up", [Action.WarpDown] = "Time compression down",
            [Action.Map] = "System map", [Action.CameraMode] = "Camera: chase / orbit / cockpit", [Action.Help] = "Show controls", [Action.QuickSave] = "Quick save",
            [Action.Screenshot] = "Screenshot", [Action.PhotoMode] = "Photo mode", [Action.RecordClip] = "Record a clip",
            [Action.Floodlights] = "Hull floodlights on/off",
            [Action.VelocityRef] = "Velocity reference: orbit / target",
            [Action.MarkLog] = "Mark the flight log (something went wrong)",
        };

        static readonly Dictionary<Action, (KeyCode, KeyCode)> Defaults = new Dictionary<Action, (KeyCode, KeyCode)>
        {
            [Action.PitchDown] = (KeyCode.W, KeyCode.None), [Action.PitchUp] = (KeyCode.S, KeyCode.None),
            [Action.YawLeft] = (KeyCode.A, KeyCode.None), [Action.YawRight] = (KeyCode.D, KeyCode.None),
            [Action.RollLeft] = (KeyCode.Q, KeyCode.None), [Action.RollRight] = (KeyCode.E, KeyCode.None),
            [Action.ThrottleUp] = (KeyCode.LeftShift, KeyCode.KeypadPlus), [Action.ThrottleDown] = (KeyCode.LeftControl, KeyCode.KeypadMinus),
            [Action.ThrottleFull] = (KeyCode.Z, KeyCode.None), [Action.ThrottleCut] = (KeyCode.X, KeyCode.None),
            [Action.RcsForward] = (KeyCode.H, KeyCode.None), [Action.RcsBack] = (KeyCode.N, KeyCode.None),
            [Action.RcsUp] = (KeyCode.I, KeyCode.None), [Action.RcsDown] = (KeyCode.K, KeyCode.None),
            [Action.RcsLeft] = (KeyCode.J, KeyCode.None), [Action.RcsRight] = (KeyCode.L, KeyCode.None),
            [Action.FlightAssist] = (KeyCode.V, KeyCode.None), [Action.Flip] = (KeyCode.F, KeyCode.None),
            [Action.Engage] = (KeyCode.P, KeyCode.None), [Action.NextTarget] = (KeyCode.Tab, KeyCode.None),
            [Action.WarpUp] = (KeyCode.Period, KeyCode.RightBracket), [Action.WarpDown] = (KeyCode.Comma, KeyCode.LeftBracket),
            [Action.Map] = (KeyCode.M, KeyCode.None), [Action.CameraMode] = (KeyCode.C, KeyCode.None),
            [Action.Help] = (KeyCode.F1, KeyCode.None), [Action.QuickSave] = (KeyCode.F5, KeyCode.None),
            [Action.Screenshot] = (KeyCode.F12, KeyCode.None), [Action.PhotoMode] = (KeyCode.F10, KeyCode.None), [Action.RecordClip] = (KeyCode.F9, KeyCode.None),
            [Action.Floodlights] = (KeyCode.G, KeyCode.None),
            [Action.VelocityRef] = (KeyCode.B, KeyCode.None),
            [Action.MarkLog] = (KeyCode.F8, KeyCode.None),
        };

        static readonly Dictionary<Action, KeyCode[]> bind = new Dictionary<Action, KeyCode[]>();
        static readonly HashSet<Action> virtualHeld = new HashSet<Action>(), virtualTap = new HashSet<Action>();
        public static bool Locked; // real input ignored (unattended QA/tours)

        static Controls() => Load();

        public static IEnumerable<Action> All => (Action[])Enum.GetValues(typeof(Action));
        public static KeyCode Get(Action a, int slot) => bind[a][slot];

        // Keys with a fixed job that the rebindable actions can't take (#21): 1–9 attitude holds in flight (1–8 pick a
        // scene in the Observatory), Esc menu, F2 hide panels, F11 fullscreen.
        public static string FixedUse(KeyCode k)
        {
            if (k >= KeyCode.Alpha1 && k <= KeyCode.Alpha9) return "attitude holds";
            switch (k)
            {
                case KeyCode.Escape: return "the menu";
                case KeyCode.F2: return "hiding the panels";
                case KeyCode.F11: return "fullscreen";
            }
            return null;
        }

        // Assign a key. A key drives one action only: if another action had it, that action gets this slot's old key
        // (a swap). A fixed key is refused and nothing changes. Returns a note for the player, or null.
        public static string Set(Action a, int slot, KeyCode k)
        {
            var use = FixedUse(k);
            if (use != null) return $"{KeyLabel(k)} is fixed for {use} and can't be assigned. \"{Label[a]}\" is unchanged.";
            string note = null;
            var old = bind[a][slot];
            if (k != KeyCode.None)
                foreach (var kv in bind)
                    for (int i = 0; i < 2; i++)
                        if (kv.Value[i] == k && !(kv.Key == a && i == slot))
                        {
                            kv.Value[i] = old;
                            note = old == KeyCode.None ? $"{k} moved from \"{Label[kv.Key]}\" (now unbound there)" : $"Swapped: \"{Label[kv.Key]}\" now uses {old}";
                        }
            bind[a][slot] = k;
            Save();
            return note;
        }

        public static void ResetDefaults() { foreach (var kv in Defaults) bind[kv.Key] = new[] { kv.Value.Item1, kv.Value.Item2 }; Save(); }

        static void Load()
        {
            var repaired = new List<(Action, int)>();
            foreach (var kv in Defaults)
            {
                var keys = new[] { kv.Value.Item1, kv.Value.Item2 };
                for (int i = 0; i < 2; i++)
                {
                    string s = PlayerPrefs.GetString($"fow-key-{kv.Key}-{i}", null);
                    if (!string.IsNullOrEmpty(s) && Enum.TryParse(s, out KeyCode k))
                    {
                        // a save from before fixed keys were refused: that slot goes back to its default (#21)
                        if (FixedUse(k) != null) repaired.Add((kv.Key, i));
                        else keys[i] = k;
                    }
                }
                bind[kv.Key] = keys;
            }
            // a restored default still drives one action only: if something else now has that key, leave the slot empty
            foreach (var (a, i) in repaired)
            {
                var k = bind[a][i];
                if (k == KeyCode.None) continue;
                foreach (var kv in bind)
                    for (int j = 0; j < 2; j++)
                        if (kv.Value[j] == k && !(kv.Key == a && j == i)) bind[a][i] = KeyCode.None;
            }
        }

        static void Save()
        {
            foreach (var kv in bind) for (int i = 0; i < 2; i++) PlayerPrefs.SetString($"fow-key-{kv.Key}-{i}", kv.Value[i].ToString());
            PlayerPrefs.Save();
        }

        public static bool Held(Action a)
        {
            if (virtualHeld.Contains(a)) return true;
            if (Locked) return false;
            var k = bind[a];
            return (k[0] != KeyCode.None && Input.GetKey(k[0])) || (k[1] != KeyCode.None && Input.GetKey(k[1]));
        }

        // Actions that fired since the flight recorder last looked, so its log can say what the pilot pressed.
        public static readonly List<Action> Fired = new List<Action>();

        public static bool Pressed(Action a)
        {
            bool on = virtualTap.Remove(a);
            if (!on && !Locked)
            {
                var k = bind[a];
                on = (k[0] != KeyCode.None && Input.GetKeyDown(k[0])) || (k[1] != KeyCode.None && Input.GetKeyDown(k[1]));
            }
            if (on && Fired.Count < 32) Fired.Add(a);
            return on;
        }

        public static float Axis(Action neg, Action pos) => (Held(pos) ? 1 : 0) - (Held(neg) ? 1 : 0);

        // QA: hold an action until released, or tap it once
        public static void Hold(Action a, bool on) { if (on) virtualHeld.Add(a); else virtualHeld.Remove(a); }
        public static void Tap(Action a) => virtualTap.Add(a);
        public static void ReleaseAll() { virtualHeld.Clear(); virtualTap.Clear(); }

        public static string Keys(Action a)
        {
            var k = bind[a];
            string a0 = KeyLabel(k[0]), a1 = KeyLabel(k[1]);
            return a1 == "" ? a0 : a0 == "" ? a1 : a0 + " / " + a1;
        }

        static string KeyLabel(KeyCode c) => c switch
        {
            KeyCode.None => "", KeyCode.LeftShift => "L-Shift", KeyCode.LeftControl => "L-Ctrl", KeyCode.Period => ".", KeyCode.Comma => ",",
            KeyCode.LeftBracket => "[", KeyCode.RightBracket => "]", KeyCode.KeypadPlus => "Num +", KeyCode.KeypadMinus => "Num -",
            KeyCode.Alpha1 => "1", KeyCode.Alpha2 => "2", KeyCode.Alpha3 => "3", KeyCode.Alpha4 => "4", KeyCode.Alpha5 => "5",
            KeyCode.Alpha6 => "6", KeyCode.Alpha7 => "7", KeyCode.Alpha8 => "8", KeyCode.Alpha9 => "9", KeyCode.Escape => "Esc",
            _ => c.ToString(),
        };
    }
}
