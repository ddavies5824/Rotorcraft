// The sea's material ("Rotorcraft/Sea") and its motion: long swells and short chop scrolled downwind at
// different speeds and angles, stronger waves and whitecaps as the wind gets up.
using UnityEngine;

namespace Rotorcraft.Presentation
{
    public sealed class SeaSurface : MonoBehaviour
    {
        const float SWELL_M = 150f, CHOP_M = 23f;   // how far each wave map stretches before it repeats (m)
        static Material material;
        Vector2 swell, chop;

        /// <summary>The sea material (one, shared).</summary>
        public static Material Material
        {
            get
            {
                if (material != null) return material;
                var shader = Shader.Find("Rotorcraft/Sea");
                if (shader == null)
                {
                    Debug.LogWarning("Rotorcraft/Sea shader missing");
                    material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Sea" };
                    material.SetColor("_BaseColor", new Color(0.04f, 0.12f, 0.16f)); material.SetFloat("_Smoothness", 0.92f);
                    return material;
                }
                material = new Material(shader) { name = "Sea" };
                material.SetTexture("_Swell", FxNoise.WaveMap(FxNoise.WaveHeights(0), 2.2f, "Sea swell"));
                material.SetTexture("_Chop", FxNoise.WaveMap(FxNoise.WaveHeights(1), 2.6f, "Sea chop"));
                material.SetColor("_SeaColor", new Color(0.03f, 0.13f, 0.15f));
                material.SetColor("_DeepColor", new Color(0.012f, 0.045f, 0.07f));
                return material;
            }
        }

        void Update()
        {
            if (material == null || !material.HasProperty("_Swell")) return;
            var w = SkyEnvironment.Current != null ? SkyEnvironment.Current.Weather : null;
            var kt = w != null ? (float)w.windKt : 5;
            material.SetFloat("_Waves", Mathf.Clamp01(kt / 25));
            material.SetFloat("_Foam", Mathf.Clamp01((kt - 12) / 14));
            // at night the reflection probe (captured by day) is turned right down, so the water reflects the
            // night sky's horizon colour (the fog colour, brightened to match the sky) and meets the sky rather
            // than ending in black. A vector, so it goes in as the linear value it already is.
            var night = SkyEnvironment.Current != null ? SkyEnvironment.Current.Night : 0;
            material.SetVector("_SkyGlow", (Vector4)(RenderSettings.fogColor.linear * (Mathf.SmoothStep(0, 1, (night - 0.75f) * 4) * 15f)));
            // the waves run downwind (x east, z south); the chop at an angle to the swell
            var (dx, dz) = w != null ? Rotorcraft.Sim.Weather.WindDirUnit(w) : (0.7, 0.7);
            var dir = new Vector2((float)dx, (float)dz);
            var dt = Time.deltaTime;
            swell += dir * (2.5f + kt * 0.12f) / SWELL_M * dt;
            var cdir = new Vector2(dir.x * 0.8f - dir.y * 0.6f, dir.x * 0.6f + dir.y * 0.8f);
            chop += cdir * (1.2f + kt * 0.05f) / CHOP_M * dt;
            swell.x %= 1; swell.y %= 1; chop.x %= 1; chop.y %= 1;
            material.SetVector("_SwellST", new Vector4(1 / SWELL_M, 1 / SWELL_M, -swell.x, -swell.y));
            material.SetVector("_ChopST", new Vector4(1 / CHOP_M, 1 / CHOP_M, -chop.x, -chop.y));
        }
    }
}
