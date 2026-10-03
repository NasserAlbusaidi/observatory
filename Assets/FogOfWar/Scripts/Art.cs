using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace FogOfWar
{
    // Optional assets from the art pack (space-sim-art): sky maps, planet textures and the Yale Bright Star Catalogue.
    // Everything degrades to the procedural look when a file is missing, so the code never depends on the art.
    public static class Art
    {
        static readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();

        public static Texture2D Tex(string path)
        {
            if (!texCache.TryGetValue(path, out var t) || t == null)
            {
                t = Resources.Load<Texture2D>(path);
                texCache[path] = t;
            }
            return t;
        }

        // Binds the sky maps globally for the wormhole and black-hole ray tracers (skyA/skyB in FOWCommon.cginc).
        public static void BindSky()
        {
            var a = Tex("Sky/SkyOurs");
            var b = Tex("Sky/SkyOther");
            bool ok = a != null && b != null;
            Shader.SetGlobalTexture("_SkyA", ok ? a : Texture2D.blackTexture);
            Shader.SetGlobalTexture("_SkyB", ok ? b : Texture2D.blackTexture);
            Shader.SetGlobalFloat("_FOWHasSky", ok ? 1 : 0);
            SkyFacing(Quaternion.identity);
        }

        // Which way the real sky is turned behind the ray-traced scenes: sky direction = rotation × world direction.
        public static void SkyFacing(Quaternion worldToSky) => Shader.SetGlobalMatrix("_SkyRot", Matrix4x4.Rotate(worldToSky));

        // Galactic centre (RA 17h45.6m, Dec −28.94°) straight ahead along +Z and the galactic plane horizontal.
        public static Quaternion GalacticCentreAhead => Quaternion.LookRotation(FromRaDec(266.405f, -28.936f), FromRaDec(192.859f, 27.128f));

        public static Vector3 FromRaDec(float raDeg, float decDeg) => FromLatLon(decDeg, raDeg);

        // Sets textures on a material and flips its _HasTex switch only if every one of them loaded.
        public static bool Bind(Material m, params (string prop, string path)[] maps)
        {
            bool ok = true;
            foreach (var (prop, path) in maps)
            {
                var t = Tex(path);
                if (t == null) { ok = false; continue; }
                m.SetTexture(prop, t);
            }
            m.SetFloat("_HasTex", ok ? 1 : 0);
            return ok;
        }

        public class StarList
        {
            public readonly List<Vector3> dirs = new List<Vector3>();
            public readonly List<float> temps = new List<float>();
            public readonly List<float> mags = new List<float>();
        }

        static StarList catalogue;
        static bool catalogueTried;

        // Stars/bsc5.csv: x,y,z,tempK,vmag,brightness, already in Unity's frame (+Y north celestial pole, RA 0h at +X,
        // RA 6h at +Z) with brightness on Gfx.Starfield's 0.12–2.1 scale. Null if the file isn't there.
        public static StarList Catalogue()
        {
            if (catalogueTried) return catalogue;
            catalogueTried = true;
            var ta = Resources.Load<TextAsset>("Stars/bsc5");
            if (ta == null) return null;
            var list = new StarList();
            var inv = CultureInfo.InvariantCulture;
            foreach (var raw in ta.text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || char.IsLetter(line[0])) continue;
                var f = line.Split(',');
                if (f.Length < 6) continue;
                if (!float.TryParse(f[0], NumberStyles.Float, inv, out float x) ||
                    !float.TryParse(f[1], NumberStyles.Float, inv, out float y) ||
                    !float.TryParse(f[2], NumberStyles.Float, inv, out float z) ||
                    !float.TryParse(f[3], NumberStyles.Float, inv, out float t) ||
                    !float.TryParse(f[5], NumberStyles.Float, inv, out float b)) continue;
                list.dirs.Add(new Vector3(x, y, z).normalized);
                list.temps.Add(t);
                list.mags.Add(b);
            }
            Resources.UnloadAsset(ta);
            catalogue = list.dirs.Count > 0 ? list : null;
            return catalogue;
        }

        // Equirect lookup shared with the shaders: longitude/RA 0 at +X, 90° east at +Z, +Y north.
        public static Vector2 SphereUV(Vector3 d)
        {
            d.Normalize();
            return new Vector2(0.5f + Mathf.Atan2(d.z, d.x) / (2 * Mathf.PI), 0.5f + Mathf.Asin(Mathf.Clamp(d.y, -1, 1)) / Mathf.PI);
        }

        public static Vector3 FromLatLon(float latDeg, float lonDeg)
        {
            float la = latDeg * Mathf.Deg2Rad, lo = lonDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(la) * Mathf.Cos(lo), Mathf.Sin(la), Mathf.Cos(la) * Mathf.Sin(lo));
        }
    }
}
