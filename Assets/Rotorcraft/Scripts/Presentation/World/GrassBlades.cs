// The grass the terrain grows near the camera: a texture of blades drawn here (no image file), and the two
// terrain detail kinds that use it, lush and rough. The terrain tints the light blades with the healthy and dry
// colours, so the texture itself is near white.
using UnityEngine;

namespace Rotorcraft.Presentation
{
    public static class GrassBlades
    {
        public static Texture2D Texture(int seed = 3, int size = 256)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Grass blades", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(0.75f, 0.85f, 0.6f, 0);
            var rnd = new System.Random(seed);
            for (int b = 0; b < 46; b++)
            {
                // a tapered, gently curved blade from the bottom edge
                float x0 = (float)rnd.NextDouble() * (size - 20) + 10, h = size * (0.45f + 0.55f * (float)rnd.NextDouble());
                float lean = ((float)rnd.NextDouble() - 0.5f) * size * 0.35f, w0 = 2.2f + (float)rnd.NextDouble() * 2.5f;
                float shade = 0.7f + 0.3f * (float)rnd.NextDouble();
                for (int y = 0; y < (int)h; y++)
                {
                    var t = y / h;
                    var cx = x0 + lean * t * t;
                    var w = w0 * (1 - t * 0.92f);
                    for (int x = Mathf.FloorToInt(cx - w); x <= Mathf.CeilToInt(cx + w); x++)
                    {
                        if (x < 0 || x >= size) continue;
                        var a = Mathf.Clamp01(w - Mathf.Abs(x - cx) + 0.5f);
                        var k = shade * (0.65f + 0.35f * t);                // darker at the base
                        var i = y * size + x;
                        if (a > px[i].a) px[i] = new Color(0.8f * k, 0.92f * k, 0.62f * k, a);
                    }
                }
            }
            tex.SetPixels(px); tex.Apply(true);
            return tex;
        }

        /// <summary>Lush grass (fields, verges, lawns) and rough grass (moorland, rough grazing, hedge bottoms).</summary>
        public static DetailPrototype[] Prototypes(Texture2D tex) => new[]
        {
            new DetailPrototype
            {
                prototypeTexture = tex, renderMode = DetailRenderMode.GrassBillboard, usePrototypeMesh = false, useInstancing = false,
                minWidth = 0.45f, maxWidth = 0.9f, minHeight = 0.18f, maxHeight = 0.42f, noiseSpread = 0.4f, density = 1.6f,
                healthyColor = new Color(0.42f, 0.55f, 0.24f), dryColor = new Color(0.55f, 0.56f, 0.30f),
            },
            new DetailPrototype
            {
                prototypeTexture = tex, renderMode = DetailRenderMode.GrassBillboard, usePrototypeMesh = false, useInstancing = false,
                minWidth = 0.5f, maxWidth = 1.1f, minHeight = 0.3f, maxHeight = 0.75f, noiseSpread = 0.6f, density = 1.0f,
                healthyColor = new Color(0.46f, 0.47f, 0.26f), dryColor = new Color(0.58f, 0.50f, 0.32f),
            },
        };
    }
}
