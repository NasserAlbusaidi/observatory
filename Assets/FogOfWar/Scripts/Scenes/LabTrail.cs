using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar
{
    // Bounded trails sampled in simulation time, so pausing doesn't erase them and frame rate doesn't change their length.
    public sealed class LabTrail
    {
        readonly Vector3[] points = new Vector3[960];
        int head, count;
        public void Clear() { head = count = 0; }
        public void Add(Vector3 point) { points[head] = point; head = (head + 1) % points.Length; count = Mathf.Min(count + 1, points.Length); }
        public void Append(List<Vector3> vertices, List<Color> colors, Color color)
        {
            for (int i = 1; i < count; i++)
            {
                vertices.Add(points[(head - i + points.Length) % points.Length]);
                vertices.Add(points[(head - i - 1 + points.Length) % points.Length]);
                color.a = 0.75f * (1 - (float)i / count);
                colors.Add(color); colors.Add(color);
            }
        }
    }
}
