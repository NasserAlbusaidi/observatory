using UnityEngine;

namespace FogOfWar
{
    // Loads the Blender-generated models from Resources/Models (see Blender/generate_models.py) and gives them
    // pipeline-independent materials. Falls back to primitives if the FBX files haven't been generated yet.
    public static class Models
    {
        public static GameObject Spawn(string name, Transform parent, Color tint, Color emission = default)
        {
            var prefab = Resources.Load<GameObject>("Models/" + name);
            GameObject go;
            if (prefab != null)
            {
                go = Object.Instantiate(prefab, parent, false);
                go.name = name;
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        // keep the Blender material's base colour where it has one
                        Color baseCol = tint;
                        var src = mats[i];
                        if (src != null && src.HasProperty("_Color")) baseCol = src.color;
                        else if (src != null && src.HasProperty("_BaseColor")) baseCol = src.GetColor("_BaseColor");
                        bool glow = src != null && src.name.ToLowerInvariant().Contains("glow");
                        mats[i] = Gfx.Lit(baseCol, glow ? baseCol * 1.5f : emission);
                    }
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                foreach (var c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c);
                return go;
            }
            go = Fallback(name, tint, emission);
            go.transform.SetParent(parent, false);
            return go;
        }

        public static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var r = FindDeep(c, name); if (r != null) return r; }
            return null;
        }

        // Rotate a spawned model so its child marker (e.g. "Nose") points along `dir`, whatever the FBX axis conversion did.
        public static void AlignMarker(Transform model, string marker, Vector3 dir)
        {
            var m = FindDeep(model, marker);
            if (m == null) return;
            var d = Vector3.ProjectOnPlane(m.position - model.position, Vector3.up);
            if (d.sqrMagnitude < 1e-6f) return;
            model.rotation = Quaternion.FromToRotation(d.normalized, dir) * model.rotation;
        }

        static GameObject Prim(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Quaternion rot, Material m)
        {
            var g = GameObject.CreatePrimitive(t);
            Object.Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos; g.transform.localScale = scale; g.transform.localRotation = rot;
            g.GetComponent<Renderer>().sharedMaterial = m;
            return g;
        }

        static GameObject Fallback(string name, Color tint, Color emission)
        {
            var root = new GameObject(name + " (primitive)");
            var hull = Gfx.Lit(tint, emission, 0.8f);
            var glow = Gfx.Lit(new Color(0.41f, 0.82f, 1f), new Color(0.41f, 0.82f, 1f));
            var z90 = Quaternion.Euler(0, 0, 90);
            switch (name)
            {
                case "WarpShip":
                    Prim(PrimitiveType.Capsule, root.transform, Vector3.zero, new Vector3(0.44f, 0.8f, 0.44f), z90, hull);
                    Prim(PrimitiveType.Cube, root.transform, new Vector3(-0.4f, 0, 0), new Vector3(0.5f, 0.05f, 1.1f), Quaternion.identity, hull);
                    Prim(PrimitiveType.Sphere, root.transform, new Vector3(-0.85f, 0, 0), Vector3.one * 0.3f, Quaternion.identity, glow);
                    break;
                case "RadioTower":
                    Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0, 0.45f, 0), new Vector3(0.08f, 0.45f, 0.08f), Quaternion.identity, hull);
                    Prim(PrimitiveType.Sphere, root.transform, new Vector3(0, 0.95f, 0), Vector3.one * 0.12f, Quaternion.identity, Gfx.Lit(tint, tint));
                    break;
                case "Antenna":
                    Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0, 0.2f, 0), new Vector3(0.03f, 0.2f, 0.03f), Quaternion.identity, hull);
                    Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0, 0.4f, 0), new Vector3(0.02f, 0.3f, 0.02f), z90, hull);
                    break;
                default:
                    Prim(PrimitiveType.Sphere, root.transform, Vector3.zero, Vector3.one, Quaternion.identity, hull);
                    break;
            }
            return root;
        }
    }
}
