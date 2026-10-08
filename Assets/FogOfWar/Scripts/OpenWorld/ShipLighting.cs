using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FogOfWar.OpenWorld
{
    // Physical light around the player ship, published as shader globals each frame (read by ShipPBR and, through
    // fowSunlit() in FOWCommon.cginc, by the planet, ring and rock shaders):
    //   _FOWSunIrr     sunlight at the ship, 1 at 1 AU, inverse square
    //   _FOWEclipse    fraction of the Sun's disc hidden by a body, seen from the ship (0..1)
    //   _FOWShineDir   direction to the brightest sunlit body (xyz) and its angular radius in rad (w)
    //   _FOWShineCol   planetshine irradiance at the ship, linear RGB, same units as _FOWSunIrr
    //   _FOWExposure   camera exposure, adapting toward 1 / (light on the ship), within ExposureMin..ExposureMax
    //   _FOWPlumePos/_FOWPlumeCol  the drive plume as a point light, zero while the drive is off
    // and renders a self-shadow map of the hull along the sunlight (_ShipShadowTex, _ShipShadowMatrix).
    // Every other scene sees the neutral values from ResetGlobals().
    public class ShipLighting : IDisposable
    {
        public const double AuKm = 149597870.7;
        public const double SunRadiusKm = 695700;
        // Exposure range in stops relative to full sunlight at 1 AU: -3 covers Mercury's 6.7x sunlight, +6 brings
        // Saturn (1/91) and Uranus (1/370) most of the way back, so the outer system reads darker but not black.
        public const float ExposureMin = 1f / 8f, ExposureMax = 64f;
        // In shadow the meter may open up by at most two stops over what the same sunlight would need, so an eclipse
        // reads as dark, with planetshine and the ship's own lights doing the work.
        const float EclipseHeadroom = 4f;
        const int ShadowRes = 2048;

        static readonly int idSunIrr = Shader.PropertyToID("_FOWSunIrr"), idEclipse = Shader.PropertyToID("_FOWEclipse"),
            idShineDir = Shader.PropertyToID("_FOWShineDir"), idShineCol = Shader.PropertyToID("_FOWShineCol"),
            idExposure = Shader.PropertyToID("_FOWExposure"), idPlumePos = Shader.PropertyToID("_FOWPlumePos"),
            idPlumeCol = Shader.PropertyToID("_FOWPlumeCol"), idShadowTex = Shader.PropertyToID("_ShipShadowTex"),
            idShadowMatrix = Shader.PropertyToID("_ShipShadowMatrix"), idShadowParams = Shader.PropertyToID("_ShipShadowParams");

        readonly Renderer[] hull;
        readonly Material depthMat;
        readonly RenderTexture shadowRt;
        readonly CommandBuffer cb = new CommandBuffer { name = "Ship shadow" };
        readonly Dictionary<Body, float> albedo = new Dictionary<Body, float>();
        float ev = float.NaN;   // log2 exposure, adapted

        public float SunIrr { get; private set; }
        public float Eclipse { get; private set; }
        public float Exposure => Mathf.Pow(2f, ev);
        public Body ShineBody { get; private set; }
        public bool Floods;     // hull floodlights, toggled by the pilot

        // Hull floodlights: (position, aim) in the Blender model frame of ship.py (x nose, y port, z up; hull length
        // 1.1 from -0.60 to +0.50), on booms and masts pointing back at the hull like the ISS's external lights.
        static readonly (Vector3 pos, Vector3 aim)[] FloodMounts =
        {
            (new Vector3(-0.13f, 0.17f, 0.04f), new Vector3(1f, -0.35f, -0.05f)),    // radiator roots, along each flank
            (new Vector3(-0.13f, -0.17f, 0.04f), new Vector3(1f, 0.35f, -0.05f)),
            (new Vector3(0.12f, 0f, 0.13f), new Vector3(-1f, 0f, -0.35f)),           // dorsal mast, aft along the spine
            (new Vector3(0.36f, 0f, -0.13f), new Vector3(-1f, 0f, 0.22f)),           // under the chin, aft along the keel
        };
        // Irradiance 10 m from a lamp, in sunlight-at-1-AU units: 0.05 is about 5,000 lux, a strong LED work light.
        // A 5000 K white.
        static readonly Color FloodColour = new Color(1f, 0.96f, 0.90f) * 0.05f;
        const float FloodRefM = 10f, FloodCosOuter = 0.80f, FloodCosInner = 0.92f;
        static readonly int idFloodPos = Shader.PropertyToID("_FOWFloodPos"), idFloodDir = Shader.PropertyToID("_FOWFloodDir"),
            idFloodCol = Shader.PropertyToID("_FOWFloodCol");
        readonly Vector4[] floodPos = new Vector4[4], floodDir = new Vector4[4];
        public Vector3 Shine { get; private set; }

        public ShipLighting(Renderer[] hullRenderers)
        {
            hull = hullRenderers;
            var s = Resources.Load<Shader>("Shaders/ShipDepth");
            if (s == null) { Debug.LogError("Fog of War: missing shader Resources/Shaders/ShipDepth"); return; }
            depthMat = new Material(s);
            var fmt = SystemInfo.SupportsBlendingOnRenderTextureFormat(RenderTextureFormat.RFloat) ? RenderTextureFormat.RFloat : RenderTextureFormat.RHalf;
            shadowRt = new RenderTexture(ShadowRes, ShadowRes, 0, fmt, RenderTextureReadWrite.Linear)
            {
                name = "Ship shadow", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, useMipMap = false,
            };
            shadowRt.Create();
        }

        // Blender object axes -> the imported mesh's object axes. The FBX export (forward -Z, up Y, baked space
        // transform) maps Blender (x, y, z) to FBX (x, z, -y), and Unity's import negates x. Checked against the mesh
        // normals by FogOfWar/Check ship normal map (FogOfWarShipCheck.cs).
        public static readonly Matrix4x4 BlenderToMesh = new Matrix4x4(
            new Vector4(-1, 0, 0, 0), new Vector4(0, 0, -1, 0), new Vector4(0, 1, 0, 0), new Vector4(0, 0, 0, 1));

        // Puts the textured hull material on every part of the ship model except the drive glow. Returns the hull
        // renderers, or null (model left as Models.Spawn made it) when the texture set isn't in the build. `set` names
        // the atlas in Models/<set>/ (the NPC fleet uses the same encoding as the player ship).
        public static Renderer[] ApplyHullMaterial(GameObject model, string set = "PlayerShip")
        {
            string dir = "Models/" + set + "/" + set + "_";
            var albedoTex = Resources.Load<Texture2D>(dir + "Albedo");
            var normalTex = Resources.Load<Texture2D>(dir + "Normal");
            var maskTex = Resources.Load<Texture2D>(dir + "Mask");
            var emissionTex = Resources.Load<Texture2D>(dir + "Emission");
            if (albedoTex == null || normalTex == null || maskTex == null) return null;
            var m = Gfx.Mat("ShipPBR");
            m.SetTexture("_AlbedoTex", albedoTex);
            m.SetTexture("_NormalTex", normalTex);
            m.SetTexture("_MaskTex", maskTex);
            if (emissionTex != null) m.SetTexture("_EmissionTex", emissionTex);
            m.SetMatrix("_BlenderToMesh", BlenderToMesh);
            var list = new List<Renderer>();
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                if (r.name == "Engine") continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = m;
                r.sharedMaterials = mats;
                list.Add(r);
            }
            return list.ToArray();
        }

        public static void ResetGlobals()
        {
            Shader.SetGlobalFloat(idSunIrr, 1);
            Shader.SetGlobalFloat(idEclipse, 0);
            Shader.SetGlobalVector(idShineDir, new Vector4(0, 1, 0, 0));
            Shader.SetGlobalVector(idShineCol, Vector4.zero);
            Shader.SetGlobalFloat(idExposure, 1);
            Shader.SetGlobalVector(idPlumePos, Vector4.zero);
            Shader.SetGlobalVector(idPlumeCol, Vector4.zero);
            Shader.SetGlobalVector(idShadowParams, Vector4.zero);
            Shader.SetGlobalVector(idFloodCol, Vector4.zero);
            StarGlare.Reset();
        }

        // Sunlight at a heliocentric distance, 1 at 1 AU.
        public static float Irradiance(double sunDistKm) => (float)(AuKm * AuKm / Math.Max(sunDistKm * sunDistKm, 1));

        // sunW: the Sun's position (km, heliocentric; normally the origin). shipRender: the ship's render-space
        // position and its render-space length, for the shadow map and the plume. plumeRender: nozzle exit, render space.
        public void Update(SolarSystem sys, double jd, D3 shipPos, D3 sunW, float dt, Transform shipT, float shipRenderLen,
                           Vector3 plumeRender, float throttle)
        {
            var toSun = sunW - shipPos;
            double sunDist = toSun.Mag;
            SunIrr = Irradiance(sunDist);
            Eclipse = EclipseFraction(sys, jd, shipPos, toSun);
            PlanetShine(sys, jd, shipPos, sunW, out var shineDir, out float shineRad);

            float shineLum = Vector3.Dot(Shine, new Vector3(0.2126f, 0.7152f, 0.0722f));
            // the floodlights meter like any other light on the hull (about their irradiance at 10 m)
            float floodLum = Floods ? FloodColour.grayscale : 0f;
            float key = Mathf.Max(SunIrr * (1 - Eclipse) + floodLum, shineLum, SunIrr / EclipseHeadroom);
            float target = Mathf.Log(Mathf.Clamp(1f / Mathf.Max(key, 1e-6f), ExposureMin, ExposureMax), 2f);
            if (float.IsNaN(ev)) ev = target;
            // the eye opens up slowly in the dark and closes quickly in the light
            float tau = target > ev ? 1.2f : 0.35f;
            ev += (target - ev) * (1f - Mathf.Exp(-dt / tau));

            Shader.SetGlobalFloat(idSunIrr, SunIrr);
            Shader.SetGlobalFloat(idEclipse, Eclipse);
            Shader.SetGlobalVector(idShineDir, new Vector4(shineDir.x, shineDir.y, shineDir.z, shineRad));
            Shader.SetGlobalVector(idShineCol, new Vector4(Shine.x, Shine.y, Shine.z, 0));
            Shader.SetGlobalFloat(idExposure, Exposure);

            if (throttle > 0.01f)
            {
                // a fusion torch: a few times full sunlight at the nozzle, falling off over a quarter of the hull
                Shader.SetGlobalVector(idPlumePos, new Vector4(plumeRender.x, plumeRender.y, plumeRender.z, shipRenderLen * 0.22f));
                var c = new Color(0.62f, 0.74f, 1f) * (2.5f * Mathf.Sqrt(throttle));
                Shader.SetGlobalVector(idPlumeCol, new Vector4(c.r, c.g, c.b, 1));
            }
            else
            {
                Shader.SetGlobalVector(idPlumeCol, Vector4.zero);
            }

            SetFloods(shipT, shipRenderLen);
            RenderShadow(Frame.U(toSun.Norm), shipT.position, shipRenderLen);
        }

        // Mount points are in the model frame; shipT has the nose on +Z, up on +Y and its origin at the hull's middle
        // (model x = -0.05), with the ship shipRenderLen long.
        void SetFloods(Transform shipT, float shipRenderLen)
        {
            if (!Floods) { Shader.SetGlobalVector(idFloodCol, Vector4.zero); return; }
            float k = shipRenderLen / 1.1f;                    // render units per model unit
            float metre = shipRenderLen / 52f;
            for (int i = 0; i < FloodMounts.Length; i++)
            {
                var (p, a) = FloodMounts[i];
                var local = new Vector3(p.y, p.z, p.x + 0.05f) * (k / shipT.lossyScale.x);
                var w = shipT.TransformPoint(local);
                var d = shipT.TransformDirection(new Vector3(a.y, a.z, a.x)).normalized;
                floodPos[i] = new Vector4(w.x, w.y, w.z, FloodRefM * metre);
                floodDir[i] = new Vector4(d.x, d.y, d.z, FloodCosOuter);
            }
            Shader.SetGlobalVectorArray(idFloodPos, floodPos);
            Shader.SetGlobalVectorArray(idFloodDir, floodDir);
            Shader.SetGlobalVector(idFloodCol, new Vector4(FloodColour.r, FloodColour.g, FloodColour.b, FloodCosInner));
        }

        // Fraction of the solar disc covered by any body, from the overlap of two discs on the sky.
        static float EclipseFraction(SolarSystem sys, double jd, D3 shipPos, D3 toSun)
        {
            double ds = toSun.Mag;
            double a = Math.Asin(Math.Min(SunRadiusKm / ds, 1));
            var sunDir = toSun / ds;
            double covered = 0;
            foreach (var b in sys.Bodies)
            {
                if (b == sys.Sun || b.Radius < 1) continue;
                var rel = sys.Pos(b, jd) - shipPos;
                double db = rel.Mag;
                if (db >= ds || db <= b.Radius) continue;
                double r = Math.Asin(b.Radius / db);
                double d = Math.Acos(Math.Max(-1, Math.Min(1, D3.Dot(rel / db, sunDir))));
                if (d >= a + r) continue;
                covered = Math.Max(covered, DiscOverlap(a, r, d));
            }
            return (float)Math.Min(covered, 1);
        }

        // Area of the sun disc (radius a) hidden by an occulting disc (radius r) at separation d, over the sun's area.
        static double DiscOverlap(double a, double r, double d)
        {
            if (d <= Math.Abs(r - a)) return r >= a ? 1 : (r * r) / (a * a);
            double a2 = a * a, r2 = r * r;
            double p = a2 * Math.Acos(Math.Max(-1, Math.Min(1, (d * d + a2 - r2) / (2 * d * a))));
            double q = r2 * Math.Acos(Math.Max(-1, Math.Min(1, (d * d + r2 - a2) / (2 * d * r))));
            double k = 0.5 * Math.Sqrt(Math.Max(0, (-d + a + r) * (d + a - r) * (d - a + r) * (d + a + r)));
            return (p + q - k) / (Math.PI * a2);
        }

        // Light from the brightest sunlit body: a Lambert sphere of geometric albedo p seen at phase angle alpha gives
        // E = E_sun(body) * p * (R / d)^2 * phi(alpha), phi(alpha) = (sin alpha + (pi - alpha) cos alpha) / pi.
        void PlanetShine(SolarSystem sys, double jd, D3 shipPos, D3 sunW, out Vector3 dir, out float angRadius)
        {
            float best = 0;
            Body bestBody = null;
            Vector3 bestDir = Vector3.up, bestCol = Vector3.zero;
            float bestRad = 0;
            foreach (var b in sys.Bodies)
            {
                if (b == sys.Sun || b.Radius < 50) continue;
                var bp = sys.Pos(b, jd);
                var rel = bp - shipPos;
                double d = rel.Mag;
                if (d <= b.Radius || d > b.Radius * 400) continue;
                var bodyToSun = (sunW - bp).Norm;
                var bodyToShip = (shipPos - bp) / d;
                double alpha = Math.Acos(Math.Max(-1, Math.Min(1, D3.Dot(bodyToSun, bodyToShip))));
                double phi = (Math.Sin(alpha) + (Math.PI - alpha) * Math.Cos(alpha)) / Math.PI;
                double s = b.Radius / d;
                float e = (float)(Irradiance((sunW - bp).Mag) * Albedo(b) * s * s * phi);
                if (e <= best) continue;
                best = e;
                bestBody = b;
                // the light comes from the lit part of the disc: lean the direction toward the subsolar side
                var litCentre = bp + bodyToSun * (b.Radius * 0.5 * (1 - phi));
                bestDir = Frame.U((litCentre - shipPos).Norm);
                bestRad = (float)Math.Asin(Math.Min(s, 1));
                var tint = Tint(b);
                bestCol = tint * e;
            }
            ShineBody = bestBody;
            Shine = bestCol;
            dir = bestDir;
            angRadius = bestRad;
        }

        // Geometric albedo from the body's Body.json, else a rocky default.
        float Albedo(Body b)
        {
            if (albedo.TryGetValue(b, out float a)) return a;
            a = 0.15f;
            var ta = Resources.Load<TextAsset>("Planets/" + b.Name + "/Body");
            if (ta != null)
            {
                var j = JsonUtility.FromJson<BodyJson>(ta.text);
                if (j.geometricAlbedo > 0) a = j.geometricAlbedo;
            }
            albedo[b] = a;
            return a;
        }

        [Serializable] class BodyJson { public float geometricAlbedo; }

        // Colour of the reflected light: the body's two display colours, linearised, averaged and normalised to unit
        // luminance, then half desaturated (a whole disc averages out its colour contrasts).
        static Vector3 Tint(Body b)
        {
            var c = (b.ColA.linear + b.ColB.linear) * 0.5f;
            float lum = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            if (lum <= 1e-4f) return Vector3.one;
            var t = new Vector3(c.r, c.g, c.b) / lum;
            return Vector3.Lerp(Vector3.one, t, 0.5f);
        }

        void RenderShadow(Vector3 sunDir, Vector3 centre, float len)
        {
            if (depthMat == null || hull == null) return;
            float r = len * 0.6f;
            var l = sunDir.normalized;
            var right = Vector3.Cross(l, Mathf.Abs(l.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var up = Vector3.Cross(right, l);
            float k = 1f / (2f * r);
            // rows: u, v, depth (0 at the Sun side), all 0..1 across a cube of side 2r around the ship
            var m = new Matrix4x4();
            m.SetRow(0, new Vector4(right.x * k, right.y * k, right.z * k, 0.5f - Vector3.Dot(centre, right) * k));
            m.SetRow(1, new Vector4(up.x * k, up.y * k, up.z * k, 0.5f - Vector3.Dot(centre, up) * k));
            m.SetRow(2, new Vector4(-l.x * k, -l.y * k, -l.z * k, 0.5f + Vector3.Dot(centre, l) * k));
            m.SetRow(3, new Vector4(0, 0, 0, 1));
            Shader.SetGlobalMatrix(idShadowMatrix, m);
            cb.Clear();
            cb.SetRenderTarget(shadowRt);
            cb.ClearRenderTarget(false, true, Color.white);
            foreach (var h in hull)
                if (h != null && h.enabled && h.gameObject.activeInHierarchy) cb.DrawRenderer(h, depthMat, 0, 0);
            Graphics.ExecuteCommandBuffer(cb);
            Shader.SetGlobalTexture(idShadowTex, shadowRt);
            // bias: about two texels of depth; offset along the normal by one texel of the map
            float texel = 2f * r / ShadowRes;
            Shader.SetGlobalVector(idShadowParams, new Vector4(1f / ShadowRes, 2.5f / ShadowRes, texel * 1.5f, 1));
        }

        public void Dispose()
        {
            cb.Release();
            if (shadowRt != null) { shadowRt.Release(); UnityEngine.Object.Destroy(shadowRt); }
            if (depthMat != null) UnityEngine.Object.Destroy(depthMat);
            ResetGlobals();
        }
    }
}
