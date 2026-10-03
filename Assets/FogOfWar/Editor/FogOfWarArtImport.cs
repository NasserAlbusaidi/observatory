using UnityEditor;
using UnityEngine;

namespace FogOfWar.EditorTools
{
    // Import settings for the art pack (space-sim-art). Sky maps need full 4096 resolution and no mips, or the RA 12h
    // seam shows. Data maps (clouds, ocean mask, normals, height) must stay linear, and the Mars height map must stay
    // 16-bit and CPU-readable because MarsScene displaces the globe from it.
    class FogOfWarArtImport : AssetPostprocessor
    {
        public override uint GetVersion() => 2;

        void OnPreprocessTexture()
        {
            if (!assetPath.Contains("FogOfWar/Resources/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.maxTextureSize = 4096;
            ti.wrapModeU = TextureWrapMode.Repeat;
            ti.wrapModeV = TextureWrapMode.Clamp;
            ti.anisoLevel = 4;
            if (assetPath.Contains("/Sky/")) ti.mipmapEnabled = false;
            bool data = assetPath.Contains("_Height") || assetPath.Contains("_Clouds") || assetPath.Contains("_Ocean") || assetPath.Contains("_Normal");
            if (data) ti.sRGBTexture = false;
            if (assetPath.Contains("_Height") || assetPath.Contains("_Clouds") || assetPath.Contains("_Ocean"))
            {
                ti.textureType = TextureImporterType.SingleChannel;
                ti.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
            }
            if (assetPath.Contains("_Height"))
            {
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.isReadable = true;
                ti.mipmapEnabled = false;
            }
        }

        // Textures copied in before this script compiled were imported with Unity's defaults; this re-imports them.
        public static void Reimport()
        {
            foreach (var dir in new[] { "Assets/FogOfWar/Resources/Sky", "Assets/FogOfWar/Resources/Textures" })
                if (AssetDatabase.IsValidFolder(dir))
                    AssetDatabase.ImportAsset(dir, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        }
    }
}
