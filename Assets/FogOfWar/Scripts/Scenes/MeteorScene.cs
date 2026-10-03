using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar
{
    // Forward-scatter meteor detection. A distant FM transmitter is below Muscat's horizon; meteors ionise trails at
    // 80–115 km that briefly reflect its signal down to the receiver. Scale: 1 unit = 100 km.
    // Forward scatter is specular: a trail echoes only if it is (nearly) tangent to an ellipsoid with the transmitter and
    // receiver as foci, i.e. perpendicular to the bisector of the two ray directions at the trail (McKinley 1961). Shower
    // meteors arrive parallel to the radiant, which rises and sets with sidereal time, so echo rates follow the clock.
    public class MeteorScene : SimScene
    {
        public override string Title => "Radio Meteors";
        public override string Eyebrow => "Forward scatter";
        public override string Short => "Hear meteors with an SDR from Muscat";
        public override string Blurb =>
            "The plan from the RTL-SDR chat. Tune to an FM frequency that's dead in Muscat but used by a transmitter far beyond the horizon. When a meteor burns up between you, its <color=#FFB547>ionised trail</color> reflects that station's signal down to you for a fraction of a second.\n\n" +
            "Each ping lands on the waterfall below, the same thing your SDR software would show. Long ones are overdense trails from bigger meteors.";
        public override string Note => "Geometry limit: both stations must see the trail above their horizon. At ~100 km altitude that caps the transmitter at about 2,250 km away. Push the slider past that and the pings stop. In summer, tropospheric ducting over the Gulf of Oman can carry FM 1,000 km directly: a steady signal is ducting, not meteors.";

        const float R = 63.71f;
        static readonly Vector3 Center = new Vector3(0, -R, 0);
        static readonly string[] ShowerNames = { "Sporadic background (ZHR ≈ 10)", "Orionids, peak 21 Oct (ZHR ≈ 20)", "Geminids, peak 14 Dec (ZHR ≈ 150)", "Leonid-style storm (ZHR ≈ 2,000)" };
        static readonly float[] Zhr = { 10, 20, 150, 2000 };
        static readonly float[] Vel = { 35, 66, 35, 71 };   // km/s
        static readonly float[] RadRa = { 0, 95, 112, 152 }, RadDec = { 0, 16, 33, 22 }; // radiant, degrees (J2000)
        const float SporadicZhr = 10;
        // Trail must be within this angle of the specular plane: ≈3° for underdense trails; overdense trails are
        // broader targets, so the tolerance grows with size.
        static float SpecularTol(float big) => 0.05f + 0.25f * big * big;
        const float VIS = 1.5f;  // trail length exaggeration so trails read at this scale
        int shower = 1;
        float txKm = 1100, timeWarp = 180;
        bool paths = true;

        class Meteor { public Vector3 start, dir; public float t, speed, burn, ion, big, freq; public bool detect; }
        readonly List<Meteor> meteors = new List<Meteor>();
        Mesh lineMesh, headMesh;
        Transform txT, rxT;
        Vector3 rx, tx;
        Texture2D waterfall;
        Color32[] wfPix;
        const int WFW = 320, WFH = 150;
        float simT, spawnAcc = 2.5f, rowAcc;
        int detected, total;
        Vector3 radiant = Vector3.up; float radiantAlt;
        int specMiss;
        readonly List<Vector3> pts = new List<Vector3>();
        readonly List<Color> cols = new List<Color>();
        readonly List<(float f, float a, float big)> active = new List<(float, float, float)>();

        static Vector3 Surface(float km, float alt = 0)
        {
            float a = km / 100f / R;
            return new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0) * (R + alt) + Center;
        }

        // Turn the textured globe so Muscat (23.6°N 58.4°E) sits under the receiver at Surface(0) and the path to the
        // transmitter (+X here) heads out at TxAzimuth, toward Karachi and the Makran coast.
        const float MuscatLat = 23.6f, MuscatLon = 58.4f, TxAzimuth = 80f;
        static Quaternion GlobeRotation()
        {
            var p = Art.FromLatLon(MuscatLat, MuscatLon);
            float lo = MuscatLon * Mathf.Deg2Rad, la = MuscatLat * Mathf.Deg2Rad;
            var east = new Vector3(-Mathf.Sin(lo), 0, Mathf.Cos(lo));
            var north = new Vector3(-Mathf.Sin(la) * Mathf.Cos(lo), Mathf.Cos(la), -Mathf.Sin(la) * Mathf.Sin(lo));
            float az = TxAzimuth * Mathf.Deg2Rad;
            var toTx = north * Mathf.Cos(az) + east * Mathf.Sin(az);
            // object → world: Muscat → +Y (up), toTx → +X
            return Quaternion.Inverse(Quaternion.LookRotation(Vector3.Cross(toTx, p), p));
        }

        static bool AboveHorizon(Vector3 p, Vector3 s)
        {
            var up = (s - Center).normalized;
            return Vector3.Dot((p - s).normalized, up) > 0.01f;
        }

        protected override void Build()
        {
            Shader.SetGlobalVector("_FOWSunDir", new Vector3(0.3f, 1f, -0.2f).normalized);
            var groundMat = OwnMat(Gfx.Mat("Ground"));
            Art.Bind(groundMat, ("_DayTex", "Textures/Earth_Day"), ("_NightTex", "Textures/Earth_Night"));
            var ground = Gfx.Obj("Earth", Root, Own(Gfx.UVSphere(256, 128, R)), groundMat);
            ground.transform.localPosition = Center;
            ground.transform.localRotation = GlobeRotation();
            var air = Gfx.Obj("Atmosphere", Root, Own(Gfx.UVSphere(128, 64, R + 1.1f)), OwnMat(Gfx.Unlit(new Color(0.23f, 0.42f, 1f, 0.05f))));
            air.transform.localPosition = Center;

            rx = Surface(0);
            rxT = Models.Spawn("Antenna", Root, PanelUI.Accent2, PanelUI.Accent2 * 0.4f).transform;
            rxT.localPosition = rx; rxT.localScale = Vector3.one * 0.8f;
            txT = Models.Spawn("RadioTower", Root, PanelUI.Accent, PanelUI.Accent * 0.3f).transform;

            lineMesh = Own(Gfx.LineMesh("Meteor lines"));
            Gfx.Obj("Meteor lines", Root, lineMesh, OwnMat(Gfx.Unlit(Color.white, true)));
            headMesh = Own(new Mesh { name = "Meteor heads" });
            headMesh.MarkDynamic();
            var headMat = OwnMat(Gfx.Unlit(Color.white, true));
            headMat.mainTexture = Own(Gfx.SoftDot());
            Gfx.Obj("Meteor heads", Root, headMesh, headMat);
            Gfx.Starfield(Root, 5000);

            waterfall = Own(new Texture2D(WFW, WFH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp });
            wfPix = new Color32[WFW * WFH];
            for (int i = 0; i < wfPix.Length; i++) wfPix[i] = Noise(Random.value * 0.18f + 0.04f);
            waterfall.SetPixels32(wfPix); waterfall.Apply();

            Cam.fieldOfView = 50;
            Orbit.Set(new Vector3(-6, 9, -22), new Vector3(5, 0, 0), 4, 120);
            Orbit.minPitch = 2;
            meteors.Clear(); simT = 0; detected = total = specMiss = 0; spawnAcc = 2.5f;
        }

        static Color32 Noise(float v)
        {
            v = Mathf.Min(1, v);
            float r = Mathf.Max(0, v * 2.2f - 0.6f);
            return new Color(Mathf.Min(1, r), Mathf.Min(1, v * 1.6f), Mathf.Min(1, 0.25f + v * 1.2f), 1);
        }

        // Radiant direction (toward the radiant) in scene space for the current sidereal time. The sim starts with the
        // radiant 6 h east of the meridian (rising), so a few minutes at full warp run through a night.
        void UpdateRadiant()
        {
            if (shower == 0) { radiantAlt = 0; return; }
            float lat = MuscatLat * Mathf.Deg2Rad, dec = RadDec[shower] * Mathf.Deg2Rad;
            float H = (-90f + simT * 360f / 86164f) * Mathf.Deg2Rad;  // hour angle
            float e = -Mathf.Cos(dec) * Mathf.Sin(H);
            float n = Mathf.Cos(lat) * Mathf.Sin(dec) - Mathf.Sin(lat) * Mathf.Cos(dec) * Mathf.Cos(H);
            float u = Mathf.Sin(lat) * Mathf.Sin(dec) + Mathf.Cos(lat) * Mathf.Cos(dec) * Mathf.Cos(H);
            Basis(out var east, out var north, out var up);
            radiant = (GlobeRotation() * (east * e + north * n + up * u)).normalized;
            radiantAlt = Mathf.Asin(Mathf.Clamp(u, -1, 1)) * Mathf.Rad2Deg;
        }

        static void Basis(out Vector3 east, out Vector3 north, out Vector3 up)
        {
            float lo = MuscatLon * Mathf.Deg2Rad, la = MuscatLat * Mathf.Deg2Rad;
            up = Art.FromLatLon(MuscatLat, MuscatLon);
            east = new Vector3(-Mathf.Sin(lo), 0, Mathf.Cos(lo));
            north = new Vector3(-Mathf.Sin(la) * Mathf.Cos(lo), Mathf.Cos(la), -Mathf.Sin(la) * Mathf.Sin(lo));
        }

        void Spawn(bool fromShower)
        {
            if (meteors.Count > 120) return;
            float mid = txKm / 2;
            float along = mid + (Random.value - 0.5f) * 3200;
            float across = (Random.value - 0.5f) * 2600;
            var b = Surface(along, 1.15f);
            b.z += across / 100f;
            b = (b - Center).normalized * (R + 1.15f) + Center;
            // shower meteors travel away from the radiant (≈1° spread); sporadics come from anywhere above the horizon
            Vector3 dir = fromShower
                ? -(radiant + Random.insideUnitSphere * 0.02f).normalized
                : new Vector3(Random.value - 0.5f, -0.4f - Random.value * 0.6f, Random.value - 0.5f).normalized;
            float big = Mathf.Pow(Random.value, 3);   // most meteors are faint
            var m = new Meteor
            {
                start = b, dir = dir, speed = Vel[shower] / 100f,
                burn = 0.4f + big * 1.2f,                 // seconds of visible flight
                ion = 0.25f + big * 4f,                   // trail lifetime: underdense → overdense
                big = big,
                freq = (Random.value - 0.5f) * 140,       // Hz offset on the waterfall
            };
            var midPt = m.start + m.dir * (m.speed * m.burn * VIS * 0.5f);
            var bis = ((tx - midPt).normalized + (rx - midPt).normalized).normalized;
            bool specular = Mathf.Abs(Vector3.Dot(m.dir, bis)) < SpecularTol(big);
            bool seen = AboveHorizon(midPt, rx) && AboveHorizon(midPt, tx);
            m.detect = seen && specular;
            if (seen && !specular) specMiss++;
            if (m.detect) detected++;
            total++;
            meteors.Add(m);
        }

        public override void Tick(float dt)
        {
            tx = Surface(txKm);
            var up = (tx - Center).normalized;
            txT.localPosition = tx;
            txT.localRotation = Quaternion.FromToRotation(Vector3.up, up);

            float simDt = dt * timeWarp;
            simT += simDt;
            // radio picks up far fainter meteors than the eye: a few times ZHR over this patch of sky
            UpdateRadiant();
            // shower rate scales with the radiant's altitude (zero once it sets); the sporadic background never stops
            float showerZhr = shower == 0 ? 0 : Zhr[shower] * Mathf.Max(0, Mathf.Sin(radiantAlt * Mathf.Deg2Rad));
            float rate = showerZhr + SporadicZhr;
            spawnAcc += simDt * rate * 4 / 3600f;
            while (spawnAcc > 1) { Spawn(Random.value * rate < showerZhr); spawnAcc -= 1; }

            pts.Clear(); cols.Clear(); active.Clear();
            var hv = new List<Vector3>(); var hc = new List<Color>(); var huv = new List<Vector2>(); var ht = new List<int>();
            var right = Cam.transform.right; var cup = Cam.transform.up;
            for (int i = meteors.Count - 1; i >= 0; i--)
            {
                var m = meteors[i];
                m.t += dt; // the meteors themselves play in real time so they stay watchable
                if (m.t > m.burn + m.ion) { meteors.RemoveAt(i); continue; }
                float flying = Mathf.Min(m.t, m.burn);
                var end = m.start + m.dir * (m.speed * flying * VIS);
                float ionLeft = 1 - (m.t - m.burn) / m.ion;
                bool burning = m.t < m.burn;
                var c = burning ? new Color(0.85f, 1f, 0.9f, 0.9f) : new Color(0.41f, 0.82f, 1f, Mathf.Max(0, ionLeft) * 0.5f);
                pts.Add(m.start); pts.Add(end); cols.Add(new Color(c.r, c.g, c.b, c.a * 0.3f)); cols.Add(c);
                if (m.detect)
                {
                    float a = burning ? 1 : Mathf.Max(0, ionLeft);
                    active.Add((m.freq, a, m.big));
                    if (paths)
                    {
                        var midNow = m.start + m.dir * (m.speed * flying * VIS * 0.5f);
                        var pc = new Color(1f, 0.71f, 0.28f, 0.55f * a);
                        pts.Add(tx); pts.Add(midNow); pts.Add(midNow); pts.Add(rx);
                        cols.Add(pc); cols.Add(pc); cols.Add(pc); cols.Add(pc);
                    }
                }
                if (burning)
                {
                    float s = 0.12f + m.big * 0.35f;
                    int bi = hv.Count;
                    hv.Add(end - right * s - cup * s); hv.Add(end + right * s - cup * s); hv.Add(end + right * s + cup * s); hv.Add(end - right * s + cup * s);
                    huv.Add(new Vector2(0, 0)); huv.Add(new Vector2(1, 0)); huv.Add(new Vector2(1, 1)); huv.Add(new Vector2(0, 1));
                    var hcol = new Color(0.8f, 1f, 0.85f, 1);
                    hc.Add(hcol); hc.Add(hcol); hc.Add(hcol); hc.Add(hcol);
                    ht.Add(bi); ht.Add(bi + 1); ht.Add(bi + 2); ht.Add(bi); ht.Add(bi + 2); ht.Add(bi + 3);
                }
            }
            Gfx.SetLines(lineMesh, pts, cols);
            headMesh.Clear();
            headMesh.SetVertices(hv); headMesh.SetColors(hc); headMesh.SetUVs(0, huv); headMesh.SetTriangles(ht, 0);
            headMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);

            // waterfall: newest row on top, one row per ~16 ms
            rowAcc += dt;
            bool dirty = false;
            while (rowAcc > 0.016f)
            {
                rowAcc -= 0.016f;
                System.Array.Copy(wfPix, WFW, wfPix, 0, WFW * (WFH - 1)); // texture row 0 is the bottom
                int top = WFW * (WFH - 1);
                for (int x = 0; x < WFW; x++)
                {
                    float hz = ((float)x / WFW - 0.5f) * 1000;
                    float v = Random.value * 0.18f + 0.04f;
                    foreach (var a in active)
                    {
                        float w = 6 + a.big * 30;
                        v += a.a * (0.6f + a.big) * Mathf.Exp(-(hz - a.f) * (hz - a.f) / (2 * w * w));
                    }
                    wfPix[top + x] = Noise(v);
                }
                dirty = true;
            }
            if (dirty) { waterfall.SetPixels32(wfPix); waterfall.Apply(); }
        }

        public override void OnGUIOverlay()
        {
            Label(rx + Vector3.up * 0.9f, "Muscat (receiver)", PanelUI.Accent2);
            Label(tx + (tx - Center).normalized * 1.3f, "FM transmitter", PanelUI.Accent);
        }

        void Label(Vector3 world, string text, Color color)
        {
            var sp = Cam.WorldToScreenPoint(world);
            if (sp.z < 0) return;
            float s = GUI.matrix.m00;
            var style = new GUIStyle { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = color }, alignment = TextAnchor.LowerCenter };
            GUI.Label(new Rect(sp.x / s - 100, (Screen.height - sp.y) / s - 24, 200, 22), text, style);
        }

        public override void Readouts(List<Readout> list)
        {
            float hrs = simT / 3600f;
            float reach = 2 * Mathf.Sqrt(2 * 6371f * 100f);
            list.Add(new Readout("Sim time elapsed", hrs.ToString("0.00") + " h"));
            list.Add(new Readout("Meteors over the region", Fmt.Num(total, 0)));
            list.Add(new Readout("Pings received", Fmt.Num(detected, 0), Tone.Hot));
            list.Add(new Readout("Missed: wrong trail angle", Fmt.Num(specMiss, 0), Tone.Cold));
            if (shower != 0) list.Add(new Readout("Radiant altitude", radiantAlt.ToString("0") + "°" + (radiantAlt < 0 ? " (set: sporadics only)" : ""), radiantAlt < 0 ? Tone.Cold : Tone.Normal));
            list.Add(new Readout("Ping rate", (hrs > 0.05f ? Fmt.Num(detected / hrs, 0) : "…") + " per hour"));
            list.Add(new Readout("Transmitter distance", Fmt.Num(txKm, 0) + " km"));
            list.Add(new Readout("Direct reception?", "no, far below the horizon", Tone.Cold));
            list.Add(new Readout("Max forward-scatter reach", "≈ " + Fmt.Num(reach, 0) + " km", txKm > reach ? Tone.Bad : Tone.Normal));
        }

        public override void Controls(PanelUI ui)
        {
            ui.Section("SDR waterfall · ±500 Hz around the dead channel");
            ui.Image(waterfall, 150);
            GUILayout.Space(8);
            shower = ui.Choice("Shower", shower, ShowerNames);
            txKm = ui.Slider("Transmitter distance", txKm, 200, 2600, v => Fmt.Num(v, 0) + " km", 50);
            timeWarp = ui.Slider("Time warp", timeWarp, 1, 600, v => v.ToString("0") + "×", 1);
            paths = ui.Toggle("Show radio paths", paths);
        }
    }
}
