using System.IO;
using UnityEditor;
using UnityEngine;

namespace FogOfWar.EditorTools
{
    // Checks the ship's object-space normal map against the imported mesh: for each vertex, the map's normal mapped by
    // ShipLighting.BlenderToMesh should agree with the mesh normal (the bake has no detail at most texels). Also
    // searches all 48 signed axis permutations and prints the best, so a changed FBX axis setting shows up at once.
    public static class FogOfWarShipCheck
    {
        [MenuItem("Fog of War/Check ship normal map")]
        public static void Check()
        {
            const string fbx = "Assets/FogOfWar/Resources/Models/PlayerShip.fbx";
            const string png = "Assets/FogOfWar/Resources/Models/PlayerShip/PlayerShip_Normal.png";
            Mesh mesh = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx))
                if (o is Mesh m && m.name != "Engine" && (mesh == null || m.vertexCount > mesh.vertexCount)) mesh = m;
            if (mesh == null || !File.Exists(png)) { Debug.LogError("ShipCheck: missing mesh or normal map"); Exit(1); return; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            tex.LoadImage(File.ReadAllBytes(png));
            var n = mesh.normals; var uv = mesh.uv;
            var bs = new Vector3[n.Length];
            for (int i = 0; i < n.Length; i++)
            {
                var c = tex.GetPixelBilinear(uv[i].x, uv[i].y);
                bs[i] = new Vector3(c.r, c.g, c.b) * 2f - Vector3.one;
            }
            float Score(Matrix4x4 m)
            {
                double s = 0;
                for (int i = 0; i < n.Length; i++) s += Vector3.Dot(m.MultiplyVector(bs[i]).normalized, n[i]);
                return (float)(s / n.Length);
            }
            float cur = Score(OpenWorld.ShipLighting.BlenderToMesh);
            int[][] perms = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
            float best = -2; Matrix4x4 bestM = Matrix4x4.identity;
            foreach (var p in perms)
                for (int sgn = 0; sgn < 8; sgn++)
                {
                    var m = Matrix4x4.zero; m.m33 = 1;
                    for (int r = 0; r < 3; r++) m[r, p[r]] = ((sgn >> r) & 1) == 1 ? -1 : 1;
                    float sc = Score(m);
                    if (sc > best) { best = sc; bestM = m; }
                }
            Debug.Log($"ShipCheck: {mesh.name} {mesh.vertexCount} verts, {tex.width}x{tex.height} map. BlenderToMesh mean dot {cur:F3}; best {best:F3} rows ({bestM.GetRow(0)}) ({bestM.GetRow(1)}) ({bestM.GetRow(2)})");
            Exit(cur > 0.9f ? 0 : 1);
        }

        static void Exit(int code) { if (Application.isBatchMode) EditorApplication.Exit(code); }
    }
}
