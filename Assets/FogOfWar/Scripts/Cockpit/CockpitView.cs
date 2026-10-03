using System;
using UnityEngine;

namespace FogOfWar.Cockpit
{
    // The cockpit interior, drawn by its own camera on top of the world camera.
    //
    // The world renders in km with a 3 m near plane, so a metre-scale cabin can't live in it. The cabin sits on its own
    // layer under the scene root, in metres, and an overlay camera (depth-only clear, same look and field of view as the
    // world camera) draws it. The forward display is a hole in the cabin mesh: the world camera's frame shows through
    // it, so the HUD markers projected through the world camera still sit on what they mark.
    public sealed class CockpitView : IDisposable
    {
        public const int Layer = 29; // unused by the project (TagManager slots 6–31 are empty); by index, so no settings change
        const int LampCount = 8;

        readonly Camera main;
        readonly Transform root, eye;
        readonly Camera cam;
        readonly Mesh cabinMesh, screenMesh, glassMesh;
        readonly Material cabinMat, screenMat, glassMat;
        public readonly CockpitMfd Mfd;
        readonly Vector4[] lampPos = new Vector4[LampCount], lampCol = new Vector4[LampCount];
        bool on;
        float red, shakeT;

        // Cabin lamps: position (cabin metres) and colour in normal and in high-g (red) lighting, display units at 1 m.
        static readonly Vector3[] LampAt =
        {
            new Vector3(0, 1.08f, -0.35f),                                     // 0 overhead strip (the glass reflects this one)
            new Vector3(-0.55f, -1.21f, 0.8f), new Vector3(0.55f, -1.21f, 0.8f), // 1–2 footwell
            new Vector3(0, 0.35f, 1.22f),                                      // 3 spill from the hull-camera display
            CockpitMesh.MfdCentres[0] + new Vector3(0, 0.03f, -0.06f),         // 4–6 the MFDs
            CockpitMesh.MfdCentres[1] + new Vector3(0, 0.03f, -0.06f),
            CockpitMesh.MfdCentres[2] + new Vector3(0, 0.03f, -0.06f),
            new Vector3(-0.78f, -0.05f, 0.55f),                                // 7 side console indicators
        };
        // The overhead strip lights the cabin; the screens only tint what is right in front of them.
        static readonly Color[] LampNormal =
        {
            new Color(1.40f, 1.30f, 1.10f), new Color(0.22f, 0.21f, 0.19f), new Color(0.22f, 0.21f, 0.19f), new Color(0.05f, 0.06f, 0.075f),
            new Color(0.005f, 0.007f, 0.007f), new Color(0.005f, 0.007f, 0.007f), new Color(0.005f, 0.007f, 0.007f), new Color(0.03f, 0.02f, 0.006f),
        };
        static readonly Color[] LampRed =
        {
            new Color(1.10f, 0.05f, 0.03f), new Color(0.30f, 0.012f, 0.008f), new Color(0.30f, 0.012f, 0.008f), new Color(0.05f, 0.06f, 0.075f),
            new Color(0.005f, 0.007f, 0.007f), new Color(0.005f, 0.007f, 0.007f), new Color(0.005f, 0.007f, 0.007f), new Color(0.03f, 0.02f, 0.006f),
        };

        public CockpitView(Transform parent, Camera main)
        {
            this.main = main;
            root = new GameObject("Cockpit").transform;
            root.SetParent(parent, false);
            root.gameObject.layer = Layer;

            cabinMesh = CockpitMesh.Build();
            screenMesh = CockpitMesh.Screens();
            glassMesh = CockpitMesh.Glass();
            cabinMat = Gfx.Mat("Cockpit");
            screenMat = Gfx.Mat("CockpitScreen");
            glassMat = Gfx.Mat("CockpitGlass");
            // the MFD panel stays active while the cabin is hidden (a disabled UIDocument drops its tree)
            Mfd = new CockpitMfd(parent);
            screenMat.SetTexture("_MainTex", Mfd.Texture);

            Part("Cabin", cabinMesh, cabinMat);
            Part("MFD screens", screenMesh, screenMat);
            Part("Display glass", glassMesh, glassMat);

            eye = new GameObject("Cockpit eye").transform;
            eye.SetParent(root, false);
            cam = eye.gameObject.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Depth;
            cam.cullingMask = 1 << Layer;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 10f;
            cam.depth = main.depth + 1;
            cam.enabled = false;

            main.cullingMask &= ~(1 << Layer);
            root.gameObject.SetActive(false);
        }

        void Part(string name, Mesh mesh, Material mat) => Gfx.Obj(name, root, mesh, mat).layer = Layer;

        // Once per frame, after the world camera is placed. look: the pilot's head turn relative to the ship;
        // g: drive acceleration in g; rcs: thruster level 0–1.
        public void Frame(bool show, Quaternion look, float g, float throttle, float rcs, float dt)
        {
            if (show != on)
            {
                on = show;
                root.gameObject.SetActive(on);
                cam.enabled = on;
                Mfd.Show(on);
            }
            if (!on) return;

            cam.fieldOfView = main.fieldOfView;
            cam.rect = main.rect;
            cam.allowHDR = main.allowHDR;
            cam.allowMSAA = main.allowMSAA;
            cam.targetTexture = main.targetTexture;

            // under thrust the couch takes the load: the head sinks back a few centimetres, and the drive and the
            // thrusters shake the hull. The world view is a stabilised camera feed, so only the cabin moves.
            shakeT += dt;
            float sink = -0.012f * Mathf.Min(g, 6f);
            float amp = 0.0012f * throttle + 0.0025f * rcs;
            var shake = new Vector3(Mathf.PerlinNoise(shakeT * 23f, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, shakeT * 19f) - 0.5f, Mathf.PerlinNoise(shakeT * 17f, 5.1f) - 0.5f) * 2f * amp;
            // the eye sits 5 cm low in the couch, which lifts the MFD row clear of the bottom edge on wide screens
            eye.localPosition = new Vector3(0, -0.05f, sink) + shake;
            eye.localRotation = look;

            // high-g burns go to red lighting, as on a warship: it keeps night vision and marks "stay in your couch"
            red = Mathf.MoveTowards(red, g >= 2.5f ? 1f : 0f, dt * 1.5f);
            for (int i = 0; i < LampCount; i++)
            {
                lampPos[i] = root.TransformPoint(LampAt[i]);
                lampCol[i] = Color.Lerp(LampNormal[i], LampRed[i], red);
            }
            Shader.SetGlobalVectorArray("_CkLampPos", lampPos);
            Shader.SetGlobalVectorArray("_CkLampCol", lampCol);
            Shader.SetGlobalVector("_CkLedCol", Color.Lerp(new Color(1.1f, 1.1f, 1.1f), new Color(0.9f, 0.4f, 0.4f), red));
            Shader.SetGlobalFloat("_CkExposure", 1f);
            Shader.SetGlobalFloat("_CkScreenGain", Mathf.Lerp(0.55f, 0.4f, red));
        }

        public bool Visible => on;

        // True when a screen point (pixels, origin bottom-left) shows the hull-camera display, not the cabin around it.
        // The HUD keeps its world markers to the display: a marker drawn over a wall or an MFD would mark nothing.
        public bool OnDisplay(Vector3 screen)
        {
            if (!on) return true;
            var ray = cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0));
            var o = root.InverseTransformPoint(ray.origin);
            var d = root.InverseTransformDirection(ray.direction);
            if (d.z <= 1e-4f) return false;
            float t = (CockpitMesh.WallZ - o.z) / d.z;
            var p = o + d * t;
            return Mathf.Abs(p.x) <= CockpitMesh.HalfW && p.y >= CockpitMesh.Bottom && p.y <= CockpitMesh.Top;
        }

        // Screen height (pixels from the bottom) of the top of the MFD row, for HUD panels that must sit above it;
        // 0 when the row is out of view.
        public float ConsoleTop()
        {
            if (!on) return 0;
            float top = 0;
            var rot = CockpitMesh.MfdRotation(Vector3.zero);
            foreach (var m in CockpitMesh.MfdCentres)
                for (int s = -1; s <= 1; s += 2)
                {
                    var p = m + rot * new Vector3(s * (CockpitMesh.MfdW / 2 + 0.03f), CockpitMesh.MfdH / 2 + 0.035f, 0);
                    var sp = cam.WorldToScreenPoint(root.TransformPoint(p));
                    if (sp.z > 0 && sp.x >= 0 && sp.x <= cam.pixelWidth) top = Mathf.Max(top, sp.y);
                }
            return Mathf.Clamp(top, 0, cam.pixelHeight);
        }

        public void Dispose()
        {
            if (main != null) main.cullingMask |= 1 << Layer;
            Mfd.Dispose();
            Gfx.Destroy(cabinMesh); Gfx.Destroy(screenMesh); Gfx.Destroy(glassMesh);
            Gfx.Destroy(cabinMat); Gfx.Destroy(screenMat); Gfx.Destroy(glassMat);
            if (root != null) Gfx.Destroy(root.gameObject);
        }
    }
}
