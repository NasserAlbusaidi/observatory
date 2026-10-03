using System.IO;
using UnityEditor;
using UnityEngine;

namespace FogOfWar.EditorTools
{
    // Import settings for the art pack (space-sim-art) and the fidelity maps (Tools/fidelity). Maps import at up to
    // 8192 (their source size; GameSettings.TextureDetail caps it at run time), planet maps with mip streaming. On
    // Standalone, colour maps are BC7, normal maps BC5 and single-channel data BC4: the default DXT1 bands smooth
    // gradients and blocks stars. Data maps (clouds, ocean mask, normals, height) must stay linear, and height maps
    // must stay 16-bit (R16, not R8) and CPU-readable because scenes displace globes from them. Ring profiles are
    // data too: uncompressed half floats, clamped, no mips, or the faint rings vanish and the F ring bleeds onto the
    // D ring.
    class FogOfWarArtImport : AssetPostprocessor
    {
        public override uint GetVersion() => 5;

        void OnPreprocessTexture()
        {
            if (!assetPath.Contains("FogOfWar/Resources/")) return;
            var ti = (TextureImporter)assetImporter;
            string name = Path.GetFileNameWithoutExtension(assetPath);
            bool Is(string kind) => name == kind || name.EndsWith("_" + kind);
            ti.maxTextureSize = 8192;
            ti.wrapModeU = TextureWrapMode.Repeat;
            ti.wrapModeV = TextureWrapMode.Clamp;
            ti.anisoLevel = 4;
            bool sky = assetPath.Contains("/Sky/");
            // skies are bound globally (no renderer for mip streaming to measure); the ray tracers pick the mip
            // themselves from the pixel footprint (skyTex in FOWCommon.cginc)
            if (sky) { ti.mipmapEnabled = true; ti.streamingMipmaps = false; }
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
            bool data = Is("Height") || Is("Clouds") || Is("Ocean") || Is("Normal");
            if (data) ti.sRGBTexture = false;
            if (ti.mipmapEnabled && !sky) ti.streamingMipmaps = true;
            if (!Is("Height"))
                Standalone(ti, Is("Normal") ? TextureImporterFormat.BC5 : data ? TextureImporterFormat.BC4 : TextureImporterFormat.BC7);
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
                ti.streamingMipmaps = false;
                Standalone(ti, TextureImporterFormat.R16);
            }
        }

        static void Standalone(TextureImporter ti, TextureImporterFormat format)
        {
            var ps = ti.GetPlatformTextureSettings("Standalone");
            ps.overridden = true;
            ps.format = format;
            ps.maxTextureSize = ti.maxTextureSize;
            ps.compressionQuality = 50;
            ti.SetPlatformTextureSettings(ps);
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
