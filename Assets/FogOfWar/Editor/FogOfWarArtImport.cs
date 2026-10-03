using System.IO;
using UnityEditor;
using UnityEngine;

namespace FogOfWar.EditorTools
{
    // Import settings for the art pack (space-sim-art). Sky maps need full 4096 resolution and no mips, or the RA 12h
    // seam shows. Data maps (clouds, ocean mask, normals, height) must stay linear, and height maps must stay 16-bit
    // and CPU-readable because scenes displace globes from them. Ring profiles are data too: uncompressed half floats,
    // clamped, no mips, or the faint rings vanish and the F ring bleeds onto the D ring.
    class FogOfWarArtImport : AssetPostprocessor
    {
        public override uint GetVersion() => 3;

        void OnPreprocessTexture()
        {
            if (!assetPath.Contains("FogOfWar/Resources/")) return;
            var ti = (TextureImporter)assetImporter;
            string name = Path.GetFileNameWithoutExtension(assetPath);
            bool Is(string kind) => name == kind || name.EndsWith("_" + kind);
            ti.maxTextureSize = 4096;
            ti.wrapModeU = TextureWrapMode.Repeat;
            ti.wrapModeV = TextureWrapMode.Clamp;
            ti.anisoLevel = 4;
            if (assetPath.Contains("/Sky/")) ti.mipmapEnabled = false;
            if (assetPath.Contains("/Rings/"))
            {
                ti.sRGBTexture = false;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.wrapModeU = TextureWrapMode.Clamp;
                ti.mipmapEnabled = false;
                ti.npotScale = TextureImporterNPOTScale.None;
                var ps = ti.GetPlatformTextureSettings("Standalone");
                ps.overridden = true;
                ps.format = TextureImporterFormat.RGBAHalf;
                ps.maxTextureSize = 4096;
                ti.SetPlatformTextureSettings(ps);
                return;
            }
            if (Is("Height") || Is("Clouds") || Is("Ocean") || Is("Normal")) ti.sRGBTexture = false;
            if (Is("Height") || Is("Clouds") || Is("Ocean"))
            {
                ti.textureType = TextureImporterType.SingleChannel;
                var s = new TextureImporterSettings();
                ti.ReadTextureSettings(s);
                s.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
                ti.SetTextureSettings(s);
            }
            if (Is("Height"))
            {
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.isReadable = true;
                ti.mipmapEnabled = false;
            }
        }

        // Textures copied in before this script compiled were imported with Unity's defaults; this re-imports them.
        public static void Reimport()
        {
            foreach (var dir in new[] { "Assets/FogOfWar/Resources/Sky", "Assets/FogOfWar/Resources/Textures", "Assets/FogOfWar/Resources/Planets", "Assets/FogOfWar/Resources/Rings" })
                if (AssetDatabase.IsValidFolder(dir))
                    AssetDatabase.ImportAsset(dir, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        }
    }
}
