// What a headset can do without. It draws about twice the pixels of a monitor, in stereo, and a frame that misses the
// headset's deadline halves the frame rate, so the costly effects that are hardest to see up close in VR come off for as
// long as the headset is in use and go back on afterwards.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Rotorcraft.Presentation
{
    public static class VrGraphics
    {
        public const float NearTrees = 300, FarTrees = 2500, ShadowDistance = 150;
        const int ShadowMap = 2048, Cascades = 2;

        sealed class Saved
        {
            public float shadowDistance;
            public int shadowMap, cascades;
            public bool depthTexture, opaqueTexture;
        }

        static readonly Dictionary<UniversalRenderPipelineAsset, Saved> saved = new Dictionary<UniversalRenderPipelineAsset, Saved>();
        static readonly Dictionary<ScriptableRendererFeature, bool> occlusion = new Dictionary<ScriptableRendererFeature, bool>();

        public static bool Active { get; private set; }

        /// <summary>Turn the headset's settings on or off.</summary>
        public static void Apply(bool on)
        {
            Active = on;
            Refresh();
        }

        /// <summary>Put them back on the current quality level's settings (changing quality swaps the settings in use).</summary>
        public static void Refresh()
        {
            foreach (var a in new List<UniversalRenderPipelineAsset>(saved.Keys)) if (!Active || a != UniversalRenderPipeline.asset) Restore(a);
            var urp = UniversalRenderPipeline.asset;
            if (Active && urp != null && !saved.ContainsKey(urp))
            {
                saved[urp] = new Saved { shadowDistance = urp.shadowDistance, shadowMap = urp.mainLightShadowmapResolution, cascades = urp.shadowCascadeCount, depthTexture = urp.supportsCameraDepthTexture, opaqueTexture = urp.supportsCameraOpaqueTexture };
                urp.shadowDistance = Mathf.Min(urp.shadowDistance, ShadowDistance);
                urp.mainLightShadowmapResolution = Mathf.Min(urp.mainLightShadowmapResolution, ShadowMap);
                urp.shadowCascadeCount = Mathf.Min(urp.shadowCascadeCount, Cascades);
                urp.supportsCameraDepthTexture = false;     // nothing in the game reads these two: only ambient occlusion, which is off here
                urp.supportsCameraOpaqueTexture = false;
            }
            if (Active)
            {
                foreach (var f in AmbientOcclusion())
                    if (!occlusion.ContainsKey(f)) { occlusion[f] = f.isActive; f.SetActive(false); }
            }
            else
            {
                foreach (var kv in occlusion) kv.Key.SetActive(kv.Value);
                occlusion.Clear();
            }
        }

        static void Restore(UniversalRenderPipelineAsset a)
        {
            var s = saved[a];
            a.shadowDistance = s.shadowDistance;
            a.mainLightShadowmapResolution = s.shadowMap;
            a.shadowCascadeCount = s.cascades;
            a.supportsCameraDepthTexture = s.depthTexture;
            a.supportsCameraOpaqueTexture = s.opaqueTexture;
            saved.Remove(a);
        }

        /// <summary>Screen-space ambient occlusion draws the whole scene once more to get its normals, then fills the screen.</summary>
        public static IEnumerable<ScriptableRendererFeature> AmbientOcclusion()
        {
            foreach (var f in Resources.FindObjectsOfTypeAll<ScriptableRendererFeature>())
                if (f.name == "ScreenSpaceAmbientOcclusion") yield return f;
        }
    }
}
