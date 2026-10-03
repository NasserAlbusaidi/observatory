using UnityEngine;

namespace FogOfWar
{
    // Orbit (around a target) or look-around (fixed position) camera, driven by IMGUI mouse events so it works
    // with either the legacy Input Manager or the new Input System.
    public class OrbitCamera
    {
        public enum Mode { Orbit, Look }
        public Mode mode = Mode.Orbit;
        public bool enabled = true;
        public Vector3 target;
        public float distance = 10, minDistance = 1, maxDistance = 100;
        public float yaw, pitch = 15, minPitch = -85, maxPitch = 85;
        public float rotateSpeed = 0.25f, zoomSpeed = 0.08f;
        public bool allowPan;

        float yawS, pitchS, distS;
        Vector3 targetS;
        readonly Camera cam;

        public OrbitCamera(Camera cam) { this.cam = cam; }

        public void Set(Vector3 position, Vector3 target, float minD, float maxD)
        {
            mode = Mode.Orbit;
            this.target = target; minDistance = minD; maxDistance = maxD;
            minPitch = -85; maxPitch = 85; allowPan = false; rotateSpeed = 0.25f; enabled = true;
            FromPosition(position);
            Snap();
        }

        public void SetLook(Vector3 position, float yawDeg, float pitchDeg)
        {
            mode = Mode.Look; enabled = true;
            target = position; yaw = yawDeg; pitch = pitchDeg;
            minPitch = -89; maxPitch = 89; rotateSpeed = 0.18f;
            Snap();
        }

        public void FromPosition(Vector3 position)
        {
            var off = position - target;
            distance = Mathf.Clamp(off.magnitude, minDistance, maxDistance);
            yaw = Mathf.Atan2(off.x, -off.z) * Mathf.Rad2Deg;
            pitch = Mathf.Asin(Mathf.Clamp(off.y / Mathf.Max(off.magnitude, 1e-5f), -1, 1)) * Mathf.Rad2Deg;
        }

        public void Snap() { yawS = yaw; pitchS = pitch; distS = distance; targetS = target; }

        public void Drag(Vector2 delta, bool pan)
        {
            if (!enabled) return;
            if (pan && allowPan && mode == Mode.Orbit)
            {
                float k = distance * 0.0015f;
                target += -cam.transform.right * delta.x * k + cam.transform.up * delta.y * k;
                return;
            }
            // GUI y grows downward: dragging down raises the orbit (or tilts the view up in look mode).
            yaw -= delta.x * rotateSpeed;
            pitch = Mathf.Clamp(pitch + delta.y * rotateSpeed, minPitch, maxPitch);
        }

        public void Zoom(float wheel)
        {
            if (!enabled || mode != Mode.Orbit) return;
            distance = Mathf.Clamp(distance * Mathf.Exp(wheel * zoomSpeed), minDistance, maxDistance);
        }

        public void Apply(float dt)
        {
            if (!enabled) return;
            float k = 1 - Mathf.Exp(-dt * 12f);
            yawS = Mathf.LerpAngle(yawS, yaw, k);
            pitchS = Mathf.Lerp(pitchS, pitch, k);
            distS = Mathf.Lerp(distS, distance, k);
            targetS = Vector3.Lerp(targetS, target, k);
            var rot = Quaternion.Euler(-pitchS, yawS, 0);
            if (mode == Mode.Orbit)
            {
                // yaw 0 places the camera on −Z looking toward +Z
                float horiz = Mathf.Cos(pitchS * Mathf.Deg2Rad) * distS;
                var off = new Vector3(Mathf.Sin(yawS * Mathf.Deg2Rad) * horiz, Mathf.Sin(pitchS * Mathf.Deg2Rad) * distS, -Mathf.Cos(yawS * Mathf.Deg2Rad) * horiz);
                cam.transform.position = targetS + off;
                cam.transform.LookAt(targetS, Vector3.up);
            }
            else
            {
                cam.transform.position = targetS;
                cam.transform.rotation = rot;
            }
        }
    }
}
