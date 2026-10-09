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
        Label pSeen, pDark;
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
            double p = Signature.JetW(ship), coldW = career?.ColdW(ship, jd) ?? Signature.IdleW, r = Signature.RangeKm(p, Signature.StockKm, coldW);
            // P = ½ m a vₑ of the main drive and RCS: ideal exhaust power, not onboard waste heat
            list.Add(p > 0 ? new Readout("Signature", $"{Fmt.Num(p / 1e12, 1)} TW exhaust (ideal, not heat), seen at {Km(r)}", Tone.Hot)
                           : new Readout("Signature", career != null && career.Stowed(ship, jd) ? $"cold, radiators stowed ({coldW / 1e6:0.00} MW), seen at {Km(r)}"
                                                                                                    : $"cold ({coldW / 1e6:0.##} MW idle), seen at {Km(r)}"));
            if (career != null && !career.Sandbox && career.Build.Stowable)
            {
                double k = career.SinkK, left = career.SinkLeftS(ship);
                if (career.Docked != null) list.Add(new Readout("Heat sink", "0 of 100 K: the port takes the heat"));
                else if (career.Damage.radiators) list.Add(new Readout("Heat sink", $"radiators jammed out: tracked {Signature.HoldText(career.OutHoldS)} after a burn", Tone.Bad));
                else if (!career.DarkReady(ship)) list.Add(new Readout("Heat sink", $"full: radiators out, tracked {Signature.HoldText(career.OutHoldS)} after a burn", Tone.Bad));
                else if (career.Stowed(ship, jd)) list.Add(new Readout("Heat sink", $"{k:0} of 100 K, radiators stowed, {Dur(left)} dark left", Tone.Cold));
                else list.Add(new Readout("Heat sink", $"{k:0} of 100 K, radiators out, {Dur(left)} dark left"));
            }
            UpdateWatch();
            if (watch == Career.Watch.Loud) list.Add(new Readout("Raiders", "can see your burn", Tone.Bad));
            else if (watch == Career.Watch.Tracked) list.Add(new Readout("Raiders", $"tracking you, {Dur(watchLeft)} left", Tone.Bad));
            else if (watch == Career.Watch.Cold) list.Add(new Readout("Raiders", "no track: cold for over " + Signature.HoldText(career.HoldS(ship)), Tone.Cold));
            if (career != null && career.Encounter != null)
            {
                // the raider's clock (D7). It holds the track on NaN (no dark-ready hull, or inside the stowed reach), while the
                // drive is loud (LoseInS counts from the burn's end then) and when the sink fills before the clock runs out
                double lose = career.LoseInS(ship, jd);
                bool holds = double.IsNaN(lose) || p > 0 || career.SinkLeftS(ship) < lose;
                list.Add(holds ? new Readout("Raider track", "it has your track", Tone.Bad)
                                       : new Readout("Raider track", $"it loses you in {Math.Floor(Math.Max(0, lose) / 60):0}:{Math.Floor(Math.Max(0, lose) % 60):00} if you stay dark", Tone.Cold));
            }
            // combat 4a: the salvo in flight (it closes at MissileKms, so the nearest hits first) and what the hits have taken;
            // 4b: the working mounts that bear on the nearest, with any fitted
            if (career?.Encounter is Encounter en && en.Missiles.Count > 0)
            {
                var near = en.Missiles[0].P;
                foreach (var m in en.Missiles) if (m.P.Mag < near.Mag) near = m.P;
                double t = near.Mag / Career.MissileKms;
                list.Add(new Readout("Missiles", $"{en.Missiles.Count} inbound, impact in {Math.Floor(t / 60):0}:{Math.Floor(t % 60):00}; {en.Magazine} left aboard the raider" +
                                                 (career.Build.MountCount > 0 ? "; " + Career.BearText(career.MountsBearing(ship, near)) : ""), Tone.Bad));
            }
            if (career != null && career.DamageText() is string dmg) list.Add(new Readout("Damage", dmg + ": any port repairs it", Tone.Bad));
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

        string DarkPill() => career != null && career.Stowed(ship, jd)
            ? $"DARK {Math.Floor(career.SinkLeftS(ship) / 3600):0}:{Math.Floor(career.SinkLeftS(ship) % 3600 / 60):00}" : null;

        public string TestDarkPill()
        {
            if (pDark == null) return "no pill";
            SetPill(pDark, DarkPill(), "cold");
            return pDark.style.display.value == DisplayStyle.Flex ? pDark.text : "-";
        }
    }
}
