// Import settings for the aircraft's baked textures (Models/Aircraft/*_textures): the base colour crunched (a
// fraction of the download, which matters for the web build) and in sRGB; the metal/roughness maps linear and
// smaller; the normal maps linear RGB. Data maps keep high-quality compression.
using UnityEditor;

namespace Rotorcraft.EditorTools
{
    public sealed class AircraftTextureImport : AssetPostprocessor
    {
        public override uint GetVersion() => 2;   // bump to re-import every aircraft texture with new settings

        void OnPreprocessTexture()
        {
            if (!assetPath.Contains("/Models/Aircraft/") || !assetPath.EndsWith(".png")) return;
            var ti = (TextureImporter)assetImporter;
            var metalRough = assetPath.EndsWith("_metalrough.png");
            var normal = assetPath.EndsWith("_normal.png");
            // normals stay a plain linear RGB texture: glTFast's shader reads all three channels, which Unity's own
            // normal-map format (two channels) would leave empty, turning the surfaces inside out
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = !metalRough && !normal;
            ti.mipmapEnabled = true;
            ti.anisoLevel = 8;
            ti.maxTextureSize = metalRough ? 2048 : 4096;
            // crunch only the colour: on metal/roughness and normals its blotches read as patchy metal and dents
            ti.textureCompression = metalRough || normal ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Compressed;
            ti.crunchedCompression = !metalRough && !normal;
            ti.compressionQuality = 70;
            // the web build: data maps at 256 px (at browser resolution the joints barely show), all crunched; the
            // small parts (role kit, rotor blades, tail rotor) at half that, as they cover few pixels on screen
            var small = assetPath.Contains("_eq_") || assetPath.Contains("_blades_") || assetPath.Contains("_tail_rotor_");
            var web = ti.GetPlatformTextureSettings("WebGL");
            web.overridden = true;
            web.maxTextureSize = (metalRough || normal ? 256 : 512) / (small ? 2 : 1);
            web.format = TextureImporterFormat.Automatic;
            web.textureCompression = TextureImporterCompression.Compressed;
            web.crunchedCompression = true;
            web.compressionQuality = 60;
            ti.SetPlatformTextureSettings(web);
        }
    }
}
