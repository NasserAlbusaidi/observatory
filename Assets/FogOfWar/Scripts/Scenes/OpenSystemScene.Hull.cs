using System.Collections.Generic;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    public partial class OpenSystemScene
    {
        // the fitted hull in km: the ship model scales to it, and the free camera clears it. 52 m outside a career (no build).
        float HullKm() => (float)((career?.Build.Hull.LengthM ?? 52) / 1000);

        static readonly Color HullTint = new Color(0.78f, 0.8f, 0.84f);
        const float FoldSeconds = 1.5f, FoldScale = 0.05f;
        string hullModel;     // the model name asked for (Hull.Model), not the file a fallback spawned: the guard compares this
        readonly List<Material> hullMats = new List<Material>();
        readonly List<(Transform t, Vector3 scale)> radiators = new List<(Transform, Vector3)>();
        float fold;           // 0 out, 1 stowed

        // The fitted hull's own model and everything read off it: the nose alignment, shipLen and the centring, the hull
        // material and the lighting's casters, the Engine glow and the radiators. HullKm() (the 60 m or 52 m scale) and the
        // plume read shipLen every frame. LateInner asks every frame, so a refit (Career.RefitTo swaps the build at once),
        // a load or a new career all land here; it rebuilds only when the hull's model name changes.
        void RefreshHullModel()
        {
            string want = career?.Build.Hull.Model ?? "PlayerShip";
            if (shipModel != null && want == hullModel) return;
            hullModel = want;
            if (shipModel != null) { shipModel.gameObject.SetActive(false); Gfx.Destroy(shipModel.gameObject); }
            foreach (var m in hullMats) Gfx.Destroy(m);
            hullMats.Clear();

            // AlignMarker and Renderer.bounds are world space: measure with the holder at the origin, unrotated, unscaled
            // (mid-flight it has the ship's attitude and its render scale); LateInner places it again this frame
            Vector3 p0 = shipT.localPosition, s0 = shipT.localScale;
            var r0 = shipT.localRotation;
            shipT.localPosition = Vector3.zero; shipT.localRotation = Quaternion.identity; shipT.localScale = Vector3.one;
            var model = SpawnHullModel(want, shipT);
            shipModel = model.transform;
            Models.AlignMarker(shipModel, "Nose", Vector3.forward);
            var rends = model.GetComponentsInChildren<Renderer>();
            var bounds = rends[0].bounds;
            foreach (var r in rends) bounds.Encapsulate(r.bounds);
            shipLen = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z); // radiators out, as imported
            shipModel.localPosition -= bounds.center - shipT.position;
            shipT.localPosition = p0; shipT.localRotation = r0; shipT.localScale = s0;

            // Models.Spawn's flat materials, most of them replaced below: all destroyed with this model
            foreach (var r in rends) foreach (var m in r.sharedMaterials) if (m != null && !hullMats.Contains(m)) hullMats.Add(m);
            // textured hull (PBR) with physical lighting; the texture set is the model's name, and without it the flat look stays
            var hull = ShipLighting.ApplyHullMaterial(model, model.name);
            if (hull != null && hull.Length > 0) hullMats.Add(hull[0].sharedMaterial);
            foreach (var m in hullMats) OwnMat(m); // a scene exit frees what a swap has not
            lighting.SetHull(hull, model.name);
            engine = Models.FindDeep(shipModel, "Engine");
            if (engine != null) engineScale = engine.localScale;
            radiators.Clear();
            for (int i = 0; i < 4; i++)
            {
                var t = Models.FindDeep(shipModel, "Radiator_" + i);
                if (t != null) radiators.Add((t, t.localScale));
            }
            fold = career != null && career.Stowed(ship, jd) ? 1 : 0; // a new model shows the state it is in, with no fold to watch
        }

        // The hull's model by its Resources/Models name. A name with no file falls back to the frigate's, then the old
        // torch ship (Models.Spawn draws a primitive if even that is missing). The spawned object is named for the file used.
        static GameObject SpawnHullModel(string name, Transform parent)
        {
            foreach (var n in new[] { name, "PlayerShip" })
                if (Resources.Load<GameObject>("Models/" + n) != null) return Models.Spawn(n, parent, HullTint);
            return Models.Spawn("TorchShip", parent, HullTint);
        }

        // Stowed radiators fold flat on their hinges: local X (hinge to tip) eases to FoldScale of its import value over
        // FoldSeconds of real time, and back. Only a stowable build is ever Stowed, so fixed radiators stay out; so does a
        // ship outside a career. A model without Radiator_n children has nothing to fold.
        void FoldRadiators()
        {
            bool stowed = career != null && career.Stowed(ship, jd);
            fold = Mathf.MoveTowards(fold, stowed ? 1 : 0, Time.unscaledDeltaTime / FoldSeconds);
            float k = Mathf.Lerp(1, FoldScale, Mathf.SmoothStep(0, 1, fold));
            foreach (var (t, s) in radiators) t.localScale = new Vector3(s.x * k, s.y, s.z);
        }
    }
}
