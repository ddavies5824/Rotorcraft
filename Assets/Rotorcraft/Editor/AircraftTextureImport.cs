// Import settings for the aircraft's baked textures (Models/Aircraft/*_textures): crunched block compression
// (a fraction of the download, which matters for the web build), base colour in sRGB, the metal/roughness maps
// linear and smaller (they hold only slowly varying values).
using UnityEditor;

namespace Rotorcraft.EditorTools
{
    public sealed class AircraftTextureImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Contains("/Models/Aircraft/") || !assetPath.EndsWith(".png")) return;
            var ti = (TextureImporter)assetImporter;
            var metalRough = assetPath.EndsWith("_metalrough.png");
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = !metalRough;
            ti.mipmapEnabled = true;
            ti.anisoLevel = 8;
            ti.maxTextureSize = metalRough ? 2048 : 4096;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.crunchedCompression = true;
            ti.compressionQuality = 60;
        }
    }
}
