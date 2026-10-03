using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar.Cockpit
{
    // The pilot's cabin, built in code (no third-party art; see CREDITS.md). Units are metres in the ship's body frame:
    // +Z toward the nose, +Y ship-up, the pilot's eye at the origin. The pilot lies back in the crash couch, so under
    // thrust "down" is the aft wall (−Z), as on an Expanse torch ship.
    //
    // There are no windows. The forward wall carries a hull-camera display; the cabin leaves that rectangle empty and the
    // world camera's frame shows through it, so every HUD marker projected through the world camera stays correct.
    // Vertex colour: rgb albedo; alpha 0 for painted surfaces, 1 for self-lit indicator glass.
    public static class CockpitMesh
    {
        // Display aperture on the forward wall (z = WallZ): x ±HalfW, y Bottom…Top. Chosen so the default 55° view in
        // portrait (±27.5° vertical, ±16° horizontal) shows the display above and the MFD row below.
        public const float WallZ = 1.30f, HalfW = 0.95f, Bottom = -0.27f, Top = 1.02f;

        // MFD row: three screens set in the console under the display, each MfdW × MfdH, centred at MfdCentres.
        public const float MfdW = 0.32f, MfdH = 0.24f;
        public static readonly Vector3[] MfdCentres = { new Vector3(-0.37f, -0.447f, 1.138f), new Vector3(0, -0.447f, 1.138f), new Vector3(0.37f, -0.447f, 1.138f) };
        // the console face leans back toward the pilot by this much (degrees from vertical)
        public const float ConsoleTilt = 28f;

        static readonly Color Hull = new Color(0.30f, 0.31f, 0.33f, 0), HullDark = new Color(0.16f, 0.17f, 0.18f, 0),
            Panel = new Color(0.40f, 0.41f, 0.42f, 0), Bezel = new Color(0.05f, 0.05f, 0.055f, 0), Pad = new Color(0.12f, 0.13f, 0.16f, 0),
            Frame = new Color(0.22f, 0.22f, 0.21f, 0), Stripe = new Color(0.62f, 0.48f, 0.12f, 0), Grip = new Color(0.07f, 0.07f, 0.075f, 0),
            LedAmber = new Color(1.0f, 0.62f, 0.18f, 1), LedGreen = new Color(0.25f, 1.0f, 0.45f, 1), LedRed = new Color(1.0f, 0.15f, 0.1f, 1),
            LampGlass = new Color(0.95f, 0.92f, 0.85f, 1);

        class B
        {
            public readonly List<Vector3> v = new List<Vector3>(); public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Color> c = new List<Color>(); public readonly List<int> t = new List<int>();

            // one flat quad, corners counter-clockwise as seen from the side its normal faces
            public void Quad(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color col)
            {
                var nn = Vector3.Cross(d - a, b - a).normalized;
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(cc); v.Add(d);
                for (int k = 0; k < 4; k++) { n.Add(nn); c.Add(col); }
                t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2);
            }

            // an oriented box: centre, half extents, rotation
            public void Box(Vector3 ctr, Vector3 half, Quaternion rot, Color col, bool skipBack = false)
            {
                Vector3 P(float x, float y, float z) => ctr + rot * Vector3.Scale(half, new Vector3(x, y, z));
                Out(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), ctr, col);     // +z
                if (!skipBack) Out(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), ctr, col); // −z
                Out(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), ctr, col);     // +x
                Out(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), ctr, col); // −x
                Out(P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1), ctr, col);     // +y
                Out(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), ctr, col); // −y
            }

            // a box face, wound so it faces away from the box centre
            void Out(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Vector3 ctr, Color col)
            {
                if (Vector3.Dot(Vector3.Cross(d - a, b - a), (a + cc) / 2 - ctr) >= 0) Quad(a, b, cc, d, col);
                else Quad(a, d, cc, b, col);
            }

            public void Box(Vector3 ctr, Vector3 half, Color col) => Box(ctr, half, Quaternion.identity, col);

            // an inward-facing wall rectangle on plane axis = value, spanning the other two axes
            public void Wall(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color col) => Quad(a, b, cc, d, col);
        }

        public static Mesh Build()
        {
            var b = new B();
            const float X = 1.05f, Yd = -1.25f, Yu = 1.15f, Zb = -1.45f, Zf = WallZ;

            // ---- shell (normals face in)
            // floor (the couch rests on it; under thrust the aft wall is "down")
            b.Wall(new Vector3(-X, Yd, Zb), new Vector3(X, Yd, Zb), new Vector3(X, Yd, Zf), new Vector3(-X, Yd, Zf), HullDark);
            // ceiling
            b.Wall(new Vector3(-X, Yu, Zf), new Vector3(X, Yu, Zf), new Vector3(X, Yu, Zb), new Vector3(-X, Yu, Zb), Hull);
            // side walls
            b.Wall(new Vector3(-X, Yd, Zf), new Vector3(-X, Yu, Zf), new Vector3(-X, Yu, Zb), new Vector3(-X, Yd, Zb), Hull);
            b.Wall(new Vector3(X, Yd, Zb), new Vector3(X, Yu, Zb), new Vector3(X, Yu, Zf), new Vector3(X, Yd, Zf), Hull);
            // aft wall
            b.Wall(new Vector3(-X, Yd, Zb), new Vector3(-X, Yu, Zb), new Vector3(X, Yu, Zb), new Vector3(X, Yd, Zb), Hull);
            // forward wall, with the display aperture left open
            b.Wall(new Vector3(X, Yd, Zf), new Vector3(X, Bottom, Zf), new Vector3(-X, Bottom, Zf), new Vector3(-X, Yd, Zf), HullDark);
            b.Wall(new Vector3(X, Top, Zf), new Vector3(X, Yu, Zf), new Vector3(-X, Yu, Zf), new Vector3(-X, Top, Zf), HullDark);
            b.Wall(new Vector3(-HalfW, Bottom, Zf), new Vector3(-HalfW, Top, Zf), new Vector3(-X, Top, Zf), new Vector3(-X, Bottom, Zf), HullDark);
            b.Wall(new Vector3(X, Bottom, Zf), new Vector3(X, Top, Zf), new Vector3(HalfW, Top, Zf), new Vector3(HalfW, Bottom, Zf), HullDark);

            // ---- display bezel: a 4 cm lip around the aperture, standing 3 cm proud of the wall
            const float lip = 0.04f, proud = 0.03f;
            float zb = Zf - proud / 2;
            b.Box(new Vector3(0, Top + lip / 2, zb), new Vector3(HalfW + lip, lip / 2, proud / 2), Bezel);
            b.Box(new Vector3(0, Bottom - lip / 2, zb), new Vector3(HalfW + lip, lip / 2, proud / 2), Bezel);
            b.Box(new Vector3(-HalfW - lip / 2, (Top + Bottom) / 2, zb), new Vector3(lip / 2, (Top - Bottom) / 2, proud / 2), Bezel);
            b.Box(new Vector3(HalfW + lip / 2, (Top + Bottom) / 2, zb), new Vector3(lip / 2, (Top - Bottom) / 2, proud / 2), Bezel);
            // status LEDs on the lower bezel
            for (int i = 0; i < 5; i++)
                b.Box(new Vector3(-HalfW + 0.05f + i * 0.03f, Bottom - lip / 2, Zf - proud - 0.001f), new Vector3(0.006f, 0.004f, 0.001f), i == 0 ? LedGreen : LedAmber);

            // ---- console under the display: a slab leaning back toward the pilot, holding the MFD row
            var tilt = Quaternion.Euler(ConsoleTilt, 0, 0);
            var consoleCtr = new Vector3(0, -0.47f, 1.20f);
            b.Box(consoleCtr, new Vector3(0.62f, 0.20f, 0.06f), tilt, Panel);
            // MFD bezels, each sunk around its screen (the screens themselves are a separate mesh)
            foreach (var m in MfdCentres)
            {
                var rot = MfdRotation(m);
                var back = rot * Vector3.forward * 0.012f;
                b.Box(m + back, new Vector3(MfdW / 2 + 0.025f, MfdH / 2 + 0.03f, 0.01f), rot, Bezel, true);
                // soft keys down both sides
                for (int k = 0; k < 4; k++)
                {
                    float y = -MfdH / 2 + 0.03f + k * (MfdH - 0.06f) / 3;
                    b.Box(m + rot * new Vector3(-MfdW / 2 - 0.013f, y, -0.006f), new Vector3(0.007f, 0.009f, 0.004f), rot, Grip);
                    b.Box(m + rot * new Vector3(MfdW / 2 + 0.013f, y, -0.006f), new Vector3(0.007f, 0.009f, 0.004f), rot, Grip);
                }
            }
            // lower console, knee panel and a row of guarded switches
            b.Box(new Vector3(0, -0.86f, 1.05f), new Vector3(0.62f, 0.22f, 0.20f), HullDark);
            for (int i = 0; i < 8; i++)
            {
                float x = -0.42f + i * 0.12f;
                b.Box(new Vector3(x, -0.70f, 0.838f), new Vector3(0.022f, 0.012f, 0.012f), Grip);
                b.Box(new Vector3(x, -0.67f, 0.846f), new Vector3(0.004f, 0.004f, 0.002f), i == 3 ? LedRed : i % 2 == 0 ? LedGreen : LedAmber);
            }
            // yellow-black hazard stripe along the console's lower edge (the drive interlock panel)
            for (int i = 0; i < 12; i++)
                b.Box(new Vector3(-0.55f + i * 0.1f, -0.75f, 0.848f), new Vector3(0.025f, 0.006f, 0.002f), Stripe);

            // ---- crash couch: the pilot lies back in it; the eye sits just off the headrest
            var couchTilt = Quaternion.Euler(-14, 0, 0);
            b.Box(new Vector3(0, -0.05f, -0.22f), new Vector3(0.27f, 0.62f, 0.07f), couchTilt, Pad);          // backrest
            b.Box(new Vector3(0, 0.04f, -0.14f), new Vector3(0.15f, 0.10f, 0.05f), couchTilt, Pad);           // headrest
            b.Box(new Vector3(-0.19f, 0.05f, -0.10f), new Vector3(0.035f, 0.13f, 0.10f), couchTilt, Pad);     // head wings
            b.Box(new Vector3(0.19f, 0.05f, -0.10f), new Vector3(0.035f, 0.13f, 0.10f), couchTilt, Pad);
            b.Box(new Vector3(0, -0.72f, 0.18f), new Vector3(0.27f, 0.07f, 0.34f), Quaternion.Euler(12, 0, 0), Pad); // seat pan
            b.Box(new Vector3(0, -0.95f, 0.58f), new Vector3(0.24f, 0.06f, 0.20f), Quaternion.Euler(40, 0, 0), Pad); // leg rest
            b.Box(new Vector3(0, -1.08f, -0.05f), new Vector3(0.10f, 0.17f, 0.10f), Frame);                    // gimbal post
            b.Box(new Vector3(0, -0.84f, -0.05f), new Vector3(0.32f, 0.03f, 0.40f), Frame);                    // couch frame
            // armrests with the two flight sticks (left: translation, right: rotation)
            for (int s = -1; s <= 1; s += 2)
            {
                b.Box(new Vector3(s * 0.34f, -0.50f, 0.12f), new Vector3(0.05f, 0.035f, 0.28f), Pad);
                b.Box(new Vector3(s * 0.34f, -0.57f, 0.12f), new Vector3(0.03f, 0.04f, 0.24f), Frame);
                b.Box(new Vector3(s * 0.34f, -0.44f, 0.34f), new Vector3(0.018f, 0.045f, 0.018f), Grip);      // stick
                b.Box(new Vector3(s * 0.34f, -0.395f, 0.345f), new Vector3(0.022f, 0.012f, 0.026f), Grip);    // stick head
                b.Box(new Vector3(s * 0.34f, -0.383f, 0.36f), new Vector3(0.005f, 0.002f, 0.005f), s < 0 ? LedAmber : LedRed);
            }
            // five-point harness straps across the backrest
            b.Box(new Vector3(-0.12f, -0.25f, -0.135f), new Vector3(0.025f, 0.30f, 0.004f), Quaternion.Euler(-14, 0, 8), Stripe);
            b.Box(new Vector3(0.12f, -0.25f, -0.135f), new Vector3(0.025f, 0.30f, 0.004f), Quaternion.Euler(-14, 0, -8), Stripe);

            // ---- side walls: conduit runs, handholds, equipment lockers
            for (int s = -1; s <= 1; s += 2)
            {
                float wx = s * X;
                b.Box(new Vector3(wx - s * 0.03f, 0.55f, -0.1f), new Vector3(0.03f, 0.03f, 1.30f), Frame);              // conduit
                b.Box(new Vector3(wx - s * 0.025f, 0.45f, -0.1f), new Vector3(0.02f, 0.02f, 1.30f), HullDark);
                b.Box(new Vector3(wx - s * 0.12f, -0.15f, 0.55f), new Vector3(0.12f, 0.35f, 0.40f), Panel);             // side console
                for (int k = 0; k < 6; k++)
                {
                    var at = new Vector3(wx - s * 0.241f, -0.05f - (k / 2) * 0.12f, 0.40f + (k % 2) * 0.22f);
                    b.Box(at, new Vector3(0.002f, 0.035f, 0.07f), Bezel);                                                   // small status panel
                    b.Box(at + new Vector3(-s * 0.002f, 0.025f, -0.055f), new Vector3(0.001f, 0.004f, 0.004f), k == 1 ? LedRed : LedAmber); // its LED
                }
                b.Box(new Vector3(wx - s * 0.09f, 0.15f, -0.85f), new Vector3(0.09f, 0.55f, 0.32f), HullDark);          // locker
                b.Box(new Vector3(wx - s * 0.181f, 0.15f, -0.85f), new Vector3(0.002f, 0.50f, 0.005f), Bezel);           // locker seam
                // grab bar on stand-offs
                b.Box(new Vector3(wx - s * 0.09f, 0.85f, 0.2f), new Vector3(0.012f, 0.012f, 0.45f), Stripe);
                b.Box(new Vector3(wx - s * 0.045f, 0.85f, -0.2f), new Vector3(0.045f, 0.01f, 0.01f), Frame);
                b.Box(new Vector3(wx - s * 0.045f, 0.85f, 0.6f), new Vector3(0.045f, 0.01f, 0.01f), Frame);
            }

            // ---- ceiling: overhead panel, lamp housing, a pipe run
            b.Box(new Vector3(0, Yu - 0.05f, 0.35f), new Vector3(0.45f, 0.05f, 0.40f), Panel);
            for (int k = 0; k < 10; k++)
                b.Box(new Vector3(-0.36f + k * 0.08f, Yu - 0.101f, 0.2f + (k % 3) * 0.12f), new Vector3(0.015f, 0.002f, 0.01f), k == 6 ? LedRed : k % 3 == 0 ? LedGreen : Grip);
            b.Box(new Vector3(0, Yu - 0.03f, -0.35f), new Vector3(0.35f, 0.03f, 0.06f), Frame);              // lamp housing
            b.Box(new Vector3(0, Yu - 0.061f, -0.35f), new Vector3(0.32f, 0.001f, 0.035f), LampGlass);     // lamp strip
            b.Box(new Vector3(0.6f, Yu - 0.06f, -0.2f), new Vector3(0.04f, 0.04f, 1.25f), Frame);
            b.Box(new Vector3(-0.6f, Yu - 0.06f, -0.2f), new Vector3(0.04f, 0.04f, 1.25f), Frame);

            // ---- aft wall: the hatch to the rest of the ship, with its ladder rungs (the ladder is "down" under thrust)
            b.Box(new Vector3(0, 0, Zb + 0.04f), new Vector3(0.40f, 0.55f, 0.04f), Frame);
            b.Box(new Vector3(0, 0, Zb + 0.081f), new Vector3(0.34f, 0.49f, 0.001f), HullDark);
            b.Box(new Vector3(0.28f, 0, Zb + 0.09f), new Vector3(0.02f, 0.06f, 0.01f), Stripe);                 // hatch handle
            for (int k = 0; k < 4; k++) b.Box(new Vector3(-0.6f, -0.9f + k * 0.4f, Zb + 0.1f), new Vector3(0.18f, 0.012f, 0.012f), Stripe);
            // footwell lamps either side of the couch
            b.Box(new Vector3(-0.55f, Yd + 0.02f, 0.8f), new Vector3(0.08f, 0.01f, 0.03f), LampGlass);
            b.Box(new Vector3(0.55f, Yd + 0.02f, 0.8f), new Vector3(0.08f, 0.01f, 0.03f), LampGlass);

            var mesh = new Mesh { name = "Cockpit" };
            mesh.SetVertices(b.v); mesh.SetNormals(b.n); mesh.SetColors(b.c); mesh.SetTriangles(b.t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // The MFDs lie in the console face, which leans back toward the pilot (local −Z faces the pilot).
        public static Quaternion MfdRotation(Vector3 centre) => Quaternion.Euler(ConsoleTilt, 0, 0);

        // The three screens as one mesh; uv.x picks the screen's third of the shared MFD texture.
        public static Mesh Screens()
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var n = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i < MfdCentres.Length; i++)
            {
                var m = MfdCentres[i]; var rot = MfdRotation(m);
                int o = v.Count;
                // the screen's front faces −Z of its rotation (toward the pilot)
                v.Add(m + rot * new Vector3(-MfdW / 2, -MfdH / 2, 0)); v.Add(m + rot * new Vector3(MfdW / 2, -MfdH / 2, 0));
                v.Add(m + rot * new Vector3(MfdW / 2, MfdH / 2, 0)); v.Add(m + rot * new Vector3(-MfdW / 2, MfdH / 2, 0));
                float u0 = i / 3f, u1 = (i + 1) / 3f;
                uv.Add(new Vector2(u0, 0)); uv.Add(new Vector2(u1, 0)); uv.Add(new Vector2(u1, 1)); uv.Add(new Vector2(u0, 1));
                for (int k = 0; k < 4; k++) n.Add(rot * Vector3.back);
                t.Add(o); t.Add(o + 2); t.Add(o + 1); t.Add(o); t.Add(o + 3); t.Add(o + 2);
            }
            var mesh = new Mesh { name = "CockpitScreens" };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetNormals(n); mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // The glass over the display aperture (the hull-camera feed shows through it).
        public static Mesh Glass()
        {
            const float z = WallZ - 0.004f;
            var mesh = new Mesh { name = "CockpitGlass" };
            mesh.vertices = new[] { new Vector3(-HalfW, Bottom, z), new Vector3(HalfW, Bottom, z), new Vector3(HalfW, Top, z), new Vector3(-HalfW, Top, z) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
