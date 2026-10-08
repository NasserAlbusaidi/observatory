using System;
using System.Collections.Generic;
using FogOfWar.OpenWorld;
using UnityEngine;
using UnityEngine.UIElements;

namespace FogOfWar
{
    // What the ship shows a sensor (combat Phase 2, docs/design-combat.md): the signature and raider readouts for both
    // shells, and the UI Toolkit pill. Career.RaiderWatch allocates (Threat.Presence), so the state is cached at 4 Hz.
    public partial class OpenSystemScene
    {
        Label pSeen;
        Career.Watch watch;
        double watchLeft;
        float watchAt = -1;

        void UpdateWatch()
        {
            if (Time.unscaledTime - watchAt < 0.25f) return;
            watchAt = Time.unscaledTime;
            watch = career == null ? Career.Watch.None : career.RaiderWatch(sys, ship, jd, out watchLeft);
        }

        string SeenPill()
        {
            UpdateWatch();
            if (watch == Career.Watch.Loud) return "LOUD · SEEN AT " + Km(Signature.RangeKm(Signature.JetW(ship))).ToUpperInvariant();
            if (watch == Career.Watch.Tracked) return $"TRACKED {Math.Floor(watchLeft / 3600):0}:{Math.Floor(watchLeft % 3600 / 60):00}";
            return null;
        }

        void SignatureReadouts(List<Readout> list)
        {
            double p = Signature.JetW(ship), r = Signature.RangeKm(p);
            // P = ½ m a vₑ of the main drive and RCS: ideal exhaust power, not onboard waste heat
            list.Add(p > 0 ? new Readout("Signature", $"{Fmt.Num(p / 1e12, 1)} TW exhaust (ideal, not heat), seen at {Km(r)}", Tone.Hot)
                           : new Readout("Signature", $"cold ({Signature.IdleW / 1e6:0} MW idle), seen at {Km(r)}"));
            UpdateWatch();
            if (watch == Career.Watch.Loud) list.Add(new Readout("Raiders", "can see your burn", Tone.Bad));
            else if (watch == Career.Watch.Tracked) list.Add(new Readout("Raiders", $"tracking you, {Dur(watchLeft)} left", Tone.Bad));
            else if (watch == Career.Watch.Cold) list.Add(new Readout("Raiders", "no track: cold for over 2 h", Tone.Cold));
            if (traffic != null) list.Add(new Readout("Contacts", $"sensed: {traffic.SeenCount} of {traffic.Flying.Count} in flight"));
        }

        public int SeenRings { get; private set; } // checks: 1 while the own-burn ring is on the map

        // How far a raider's stock sensors see our burn (no sensor factor), in a warm tone like the patrols' reach.
        void AddSeenRing(float fade)
        {
            SeenRings = 0;
            double p = Signature.JetW(ship);
            if (p <= 0 || fade <= 0) return;
            var c = new Color(1f, 0.55f, 0.2f, 0.5f * fade);
            MapRing(ship.Pos, Signature.RangeKm(p), c);
            SeenRings = 1;
        }

        // UpdateHud redraws the pill only on its next run, so a test redraws it from a fresh watch before it reads it
        public string TestSeenPill()
        {
            if (pSeen == null) return "no pill";
            watchAt = -1;
            SetPill(pSeen, SeenPill(), "bad");
            return pSeen.style.display.value == DisplayStyle.Flex ? pSeen.text : "-";
        }
        public void TestWatchNow() => watchAt = -1;
    }
}
