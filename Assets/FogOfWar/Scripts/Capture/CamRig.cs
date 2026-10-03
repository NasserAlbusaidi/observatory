using UnityEngine;

namespace FogOfWar
{
    // The free camera used by photo mode and the cinematic camera. Offset is from the scene's anchor (the orbit
    // target, or in the open world the camera pivot) in the scene's own units: Unity units, or km in the open world.
    // Scale is a typical size of what's in view, so move speeds feel the same at any scale.
    public class CamRig
    {
        public Vector3 Offset;
        public Quaternion Rotation = Quaternion.identity;
        public float Scale = 1;
        public float Yaw, Pitch, Roll;
        public float Fov = 55;
        public float Speed = 1; // multiplier on Scale per second
        public bool Driven;     // a cinematic path or orbit owns the pose; mouse look is ignored

        public void FromRotation()
        {
            var e = Rotation.eulerAngles;
            Pitch = Mathf.DeltaAngle(0, e.x); Yaw = e.y; Roll = Mathf.DeltaAngle(0, e.z);
        }

        public void ToRotation() => Rotation = Quaternion.Euler(Pitch, Yaw, 0) * Quaternion.Euler(0, 0, Roll);

        public void Look(Vector2 delta, int button)
        {
            if (Driven) return;
            Yaw += delta.x * 0.15f;
            Pitch = Mathf.Clamp(Pitch + delta.y * 0.15f, -89.5f, 89.5f);
            ToRotation();
        }

        public void Wheel(float wheel) => Fov = Mathf.Clamp(Fov * Mathf.Exp(wheel * 0.04f), 5, 120);

        // fly in the camera's own axes
        public void Move(Vector3 local, float dt)
        {
            var r = Quaternion.Euler(Pitch, Yaw, 0);
            Offset += r * local * (Scale * Speed * dt);
        }
    }
}
