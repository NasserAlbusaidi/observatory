using System;
using System.Collections.Generic;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // Open System: how the bodies look. Ring systems, planet materials, shape-model choice and level of detail, and the
    // art pack's body table; BuildShip and the rest of Build stay in OpenSystemScene.cs.
    public partial class OpenSystemScene
    {
        class RingVis { public Vis planet; public Transform t; public Material m; public double outerKm; }
        [Serializable] class RingJson { public float innerKm, outerKm; }
        readonly List<RingVis> rings = new List<RingVis>();
        bool faintRings;

        void BuildRings()
        {
            rings.Clear();
            foreach (var name in new[] { "Jupiter", "Saturn", "Uranus", "Neptune" })
            {
                var tex = Resources.Load<Texture2D>("Rings/" + name + "_Rings");
                var json = Resources.Load<TextAsset>("Rings/" + name + "_Rings");
                if (tex == null || json == null) continue;
                var rj = JsonUtility.FromJson<RingJson>(json.text);
                var m = OwnMat(Gfx.Mat("Ring"));
                m.SetTexture("_MainTex", tex);
                m.SetFloat("_Inner", rj.innerKm / rj.outerKm);
                var planet = vis.Find(v => v.b.Name == name);
                var t = Gfx.Obj(name + " rings", Root, Own(Annulus(rj.innerKm / rj.outerKm, 720)), m).transform;
                rings.Add(new RingVis { planet = planet, t = t, m = m, outerKm = rj.outerKm });
            }
        }

        static Mesh Annulus(float inner, int seg)
        {
            var v = new Vector3[seg * 2]; var tri = new int[seg * 6];
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2 / seg, c = Mathf.Cos(a), s = Mathf.Sin(a);
                // the outer edge sits on the polygon's corners, so push it out to cover the true circle
                float outer = 1f / Mathf.Cos(Mathf.PI / seg);
                v[i * 2] = new Vector3(c * inner, 0, s * inner); v[i * 2 + 1] = new Vector3(c * outer, 0, s * outer);
                int j = (i + 1) % seg;
                tri[i * 6] = i * 2; tri[i * 6 + 1] = j * 2; tri[i * 6 + 2] = i * 2 + 1;
                tri[i * 6 + 3] = i * 2 + 1; tri[i * 6 + 4] = j * 2; tri[i * 6 + 5] = j * 2 + 1;
            }
            var m = new Mesh { name = "Annulus", vertices = v, triangles = tri };
            m.RecalculateBounds();
            return m;
        }

        void PlaceRings(D3 sunW)
        {
            foreach (var r in rings)
            {
                var pv = r.planet;
                float k = pv.t.localScale.x / (float)pv.b.Radius; // render units per km at the planet
                r.t.localPosition = pv.t.localPosition;
                r.t.localRotation = pv.t.localRotation;
                r.t.localScale = Vector3.one * (float)(r.outerKm * k);
                r.m.SetVector("_PlanetPos", pv.t.localPosition);
                r.m.SetFloat("_PlanetR", pv.t.localScale.x);
                r.m.SetVector("_SunDir", Frame.U((sunW - sys.Pos(pv.b, jd)).Norm));
                r.m.SetFloat("_SunIrr", ShipLighting.Irradiance((sunW - sys.Pos(pv.b, jd)).Mag));
                // Saturn's rings are bright for real; the others are τ ≈ 10⁻⁶–10⁻¹ and nearly invisible unless boosted
                r.m.SetFloat("_TauGain", faintRings && pv.b.Name != "Saturn" ? 1000f : 1f);
            }
        }

        Material PlanetMaterial(Body b)
        {
            var m = OwnMat(Gfx.Mat("Planet"));
            m.SetColor("_ColA", b.ColA); m.SetColor("_ColB", b.ColB); m.SetColor("_Atmo", b.Atmo);
            m.SetFloat("_Kind", b.Look == Look.Rocky ? 0 : b.Look == Look.Cloudy ? 1 : b.Look == Look.Gas ? 2 : 3);
            m.SetFloat("_Seed", b.Name.GetHashCode() % 97);
            if (b.Name == "Titan")
            {
                // the art map is the 938 nm surface; in visible light Titan is an orange haze ball
                m.SetColor("_Atmo", new Color(0.9f, 0.62f, 0.3f, 0.9f));
                m.SetColor("_HazeCol", new Color(0.78f, 0.55f, 0.28f));
                m.SetFloat("_Haze", 0.85f);
            }
            if (b.Texture != null && Art.Bind(m, ("_MainTex", b.Texture)))
            {
                // Planets/<Body> maps are contrast-stretched per body; geometric albedo sets the absolute
                // level (perceptual curve, 0.3 → ×1). Maps marked mapIsAbsolute (Mars) are used as they are.
                var json = Resources.Load<TextAsset>("Planets/" + b.Name + "/Body");
                var info = json != null ? JsonUtility.FromJson<BodyJson>(json.text) : null;
                float ga = info != null && !info.mapIsAbsolute ? info.geometricAlbedo : 0;
                m.SetFloat("_Gain", ga > 0 ? Mathf.Pow(ga / 0.3f, 0.6f) : 1f);
                // relief from the body's DEM, where the pipeline made one (Tools/fidelity/normals.py)
                var nrm = Art.Tex("Planets/" + b.Name + "/Normal");
                if (nrm != null) { m.SetTexture("_NormalTex", nrm); m.SetFloat("_HasNormal", 1); }
            }
            return m;
        }

        string ModelFor(Body b)
        {
            if (Resources.Load<GameObject>("Models/Bodies/" + b.Name) != null) return "Bodies/" + b.Name;
            bool moon = b.Parent != null && b.Parent != sys.Sun;
            if (moon && b.Radius < 150 && (b.Texture == null || Resources.Load<Texture2D>(b.Texture) == null))
                return $"Asteroids/Asteroid{(b.Name.GetHashCode() & 0x7fffffff) % 32:00}";
            return null;
        }

        // The baked normal map of a model path (Blender/lods.py, Blender/rocks.py), if the art pack has one.
        static string NormalMapFor(string modelPath)
        {
            var name = modelPath.Substring(modelPath.LastIndexOf('/') + 1);
            if (modelPath.StartsWith("Asteroids/")) return $"Models/Asteroids/Normals/{name}_Normal";
            int lod = name.IndexOf("_LOD", StringComparison.Ordinal);
            return lod > 0 ? $"Models/Bodies/{name.Substring(0, lod)}/{name}_Normal" : null;
        }

        static void SetLodNormal(Material m, Texture2D nrm)
        {
            m.SetFloat("_HasLodNormal", nrm != null ? 1 : 0);
            if (nrm != null) m.SetTexture("_LodNormal", nrm);
        }

        // Shape-model LOD by the body's radius on screen (px): LOD2 (10k triangles) below 200, LOD1 (50k) below 800,
        // LOD0 (200k) above, with 10 % either way so a body on a threshold doesn't flicker between two.
        static int LodFor(float px, int cur)
        {
            int want = px > 800 ? 0 : px > 200 ? 1 : 2;
            if (want < cur && px < (want == 0 ? 880 : 220)) return cur;
            if (want > cur && px > (cur == 0 ? 720 : 180)) return cur;
            return want;
        }

        [Serializable] class BodyJson { public float geometricAlbedo; public bool mapIsAbsolute; }
        [Serializable] class ArtBody { public string name, source; public float meanRadiusKm; public float geometricAlbedo; }
        [Serializable] class ArtBodies { public ArtBody[] items; }
        readonly Dictionary<string, ArtBody> art = new Dictionary<string, ArtBody>();

        void LoadArtBodies()
        {
            art.Clear();
            var ta = Resources.Load<TextAsset>("Models/Bodies/Bodies");
            if (ta == null) return;
            foreach (var a in JsonUtility.FromJson<ArtBodies>("{\"items\":" + ta.text + "}").items) art[a.name] = a;
        }

        // Rotate a body model inside its holder so its Prime marker lies on +X and North on +Y (the body frame).
        static void AlignToMarkers(Transform model)
        {
            var prime = Models.FindDeep(model, "Prime");
            var north = Models.FindDeep(model, "North");
            if (prime == null || north == null) return;
            var px = (prime.position - model.position).normalized;
            var ny = north.position - model.position;
            ny = (ny - Vector3.Dot(ny, px) * px).normalized;
            var frame = Quaternion.LookRotation(Vector3.Cross(px, ny), ny); // maps +X→prime, +Y→north
            model.localRotation = Quaternion.Inverse(frame) * model.localRotation;
        }

        static Mesh Quad()
        {
            var m = new Mesh { name = "Quad" };
            m.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            return m;
        }
    }
}
