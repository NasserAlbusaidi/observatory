using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // NPC traffic in the scene (Traffic.cs): the ships in flight follow the clock, and each shows as a dot in its owner's
    // colour. A dot shows only while the player's sensors see the ship's burn (Signature, combat Phase 2b). It is still not
    // a button: targeting a traffic ship is the optional PR 2c.
    public partial class OpenSystemScene
    {
        Traffic traffic;
        public Traffic TrafficNow => traffic; // checks
        // per Repaint (checks): ships past the Seen gate, dots drawn, and dots drawn for an unseen ship (RevealAll only)
        public int TrafficConsidered { get; private set; }
        public int TrafficDrawn { get; private set; }
        public int TrafficDrawnUnseen { get; private set; }

        // working values [design]: a colour for each faction (Faction order), and the range inside which a dot names its
        // hull class and owner
        static readonly Color[] FactionColours =
        {
            new Color(0.75f, 0.75f, 0.75f), new Color(0.5f, 0.72f, 1f), new Color(1f, 0.6f, 0.35f), new Color(0.55f, 1f, 0.85f),
            new Color(1f, 0.35f, 0.3f),
        };
        const double TrafficTagKm = 5e4;

        void TickTraffic()
        {
            if (Prof) swTraffic.Start();
            if (traffic == null) traffic = new Traffic(sys);
            traffic.Update(jd);
            traffic.Sense(ship.Pos, jd, Signature.SensorKm(career?.Codex.SensorLevel ?? 0, career?.Codex.SensorFactor ?? 1));
            if (Prof) swTraffic.Stop();
        }

        // after DrawLabels, which fills the occluders
        void DrawTraffic()
        {
            if (traffic == null || Event.current.type != EventType.Repaint) return;
            TrafficConsidered = TrafficDrawn = TrafficDrawnUnseen = 0;
            if (traffic.Flying.Count == 0) return;
            if (Prof) swTraffic.Start();
            float ui = App.UiScale;
            foreach (var t in traffic.Flying)
            {
                if (!t.Seen && !Signature.RevealAll) continue;
                TrafficConsidered++;
                var w = sys.Pos(t.Body, jd);
                var p = Map(w, out _);
                float dist = p.magnitude;
                var sp = Cam.WorldToScreenPoint(p);
                if (dist < 1e-6f || sp.z <= 0 || !CockpitShows(sp)) continue;
                var g = new Vector2(sp.x / ui, (UnityEngine.Screen.height - sp.y) / ui);
                if (App.UnderPanel(g)) continue;
                bool hidden = false;
                foreach (var (c, cr, cd) in occluders) if (cd < dist * 0.999f && (c - g).magnitude < cr) { hidden = true; break; }
                if (hidden) continue;
                GUI.color = FactionColours[(int)t.Owner];
                GUI.DrawTexture(new Rect(g.x - 1.5f, g.y - 1.5f, 3, 3), Texture2D.whiteTexture);
                TrafficDrawn++;
                if (!t.Seen) TrafficDrawnUnseen++;
                if ((w - camW).Mag < TrafficTagKm) GUI.Label(new Rect(g.x + 5, g.y - 8, 220, 16), $"{t.Hull} · {Career.Factions[(int)t.Owner].Name}", labelStyle);
            }
            GUI.color = Color.white;
            if (Prof) swTraffic.Stop();
        }
    }
}
