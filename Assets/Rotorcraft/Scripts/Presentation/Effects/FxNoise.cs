// Procedural, tileable textures for the effects (port of src/render/textures.ts): the four-channel fbm noise
// the cloud layer is drawn from (with a CPU copy so "in cloud" matches what is drawn), and the sea's wave normals.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rotorcraft.Presentation
{
    public static class FxNoise
    {
        public const int N = 256;
        static byte[] noise;
        static Texture2D noiseTex;

        static float[] Lattice(int period, int seed)
        {
            var g = new float[period * period];
            uint s = (uint)seed; if (s == 0) s = 1;
            for (int i = 0; i < g.Length; i++) { s ^= s << 13; s ^= s >> 17; s ^= s << 5; g[i] = s / 4294967296f; }
            return g;
        }

        static float TileNoise(float[] lat, int period, float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            float ux = fx * fx * fx * (fx * (fx * 6 - 15) + 10), uy = fy * fy * fy * (fy * (fy * 6 - 15) + 10);
            int M(int v) => ((v % period) + period) % period;
            float a = lat[M(yi) * period + M(xi)], b = lat[M(yi) * period + M(xi + 1)];
            float c = lat[M(yi + 1) * period + M(xi)], d = lat[M(yi + 1) * period + M(xi + 1)];
            return a + (b - a) * ux + (c - a) * uy + (a - b - c + d) * ux * uy;
        }

        static float TileFbm(float u, float v, int baseFreq, int octaves, int seed, Dictionary<long, float[]> lats)
        {
            float sum = 0, amp = 0.5f, norm = 0; int f = baseFreq;
            for (int o = 0; o < octaves; o++)
            {
                long key = ((long)f << 32) | (uint)(seed + o);
                if (!lats.TryGetValue(key, out var lat)) lats[key] = lat = Lattice(f, seed * 977 + o * 131 + f);
                sum += amp * TileNoise(lat, f, u * f, v * f);
                norm += amp; amp *= 0.5f; f *= 2;
            }
            return sum / norm;
        }

        /// <summary>256×256 RGBA bytes: four independent tileable fbm fields at increasing frequency.</summary>
        public static byte[] NoiseBytes()
        {
            if (noise != null) return noise;
            var data = new byte[N * N * 4];
            var lats = new Dictionary<long, float[]>();
            int[,] ch = { { 4, 5, 11 }, { 8, 4, 23 }, { 16, 3, 37 }, { 32, 2, 53 } };
            var tmp = new float[N * N];
            for (int c = 0; c < 4; c++)
            {
                float lo = 1, hi = 0;
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        var v = TileFbm(x / (float)N, y / (float)N, ch[c, 0], ch[c, 1], ch[c, 2], lats);
                        tmp[y * N + x] = v; if (v < lo) lo = v; if (v > hi) hi = v;
                    }
                // stretch to the full range so the thresholds in the shaders behave predictably
                for (int i = 0; i < N * N; i++) data[i * 4 + c] = (byte)Mathf.RoundToInt((tmp[i] - lo) / (hi - lo) * 255);
            }
            return noise = data;
        }

        /// <summary>The noise as a wrapping, filtered texture (linear data).</summary>
        public static Texture2D NoiseTexture
        {
            get
            {
                if (noiseTex != null) return noiseTex;
                noiseTex = new Texture2D(N, N, TextureFormat.RGBA32, true, true) { name = "Fx noise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
                noiseTex.SetPixelData(NoiseBytes(), 0);
                noiseTex.Apply(true, true);
                return noiseTex;
            }
        }

        /// <summary>The CPU copy of the noise (bilinear, wrapping), as the GPU samples it.</summary>
        public static double Sample(double u, double v, int channel)
        {
            var d = NoiseBytes();
            double x = (u - Math.Floor(u)) * N - 0.5, y = (v - Math.Floor(v)) * N - 0.5;
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y); double fx = x - xi, fy = y - yi;
            int M(int i) => ((i % N) + N) % N;
            double G(int ix, int iy) => d[(M(iy) * N + M(ix)) * 4 + channel] / 255.0;
            return (G(xi, yi) * (1 - fx) + G(xi + 1, yi) * fx) * (1 - fy) + (G(xi, yi + 1) * (1 - fx) + G(xi + 1, yi + 1) * fx) * fy;
        }

        /// <summary>
        /// Tileable wave heights: a few directional swells with integer wave numbers (so they tile), sharper at
        /// the crests than a plain sine, plus choppy noise. Normalised to 0..1.
        /// </summary>
        public static float[] WaveHeights(int seed)
        {
            var h = new float[N * N];
            var lats = new Dictionary<long, float[]>();
            var waves = seed == 0
                ? new[] { (3, 1, 0.5f, 0f), (2, -3, 0.35f, 1.3f), (5, 2, 0.22f, 2.1f), (-4, 6, 0.15f, 0.7f), (9, -5, 0.08f, 4.2f) }
                : new[] { (-2, 3, 0.4f, 0.4f), (4, 3, 0.3f, 2.6f), (-6, -1, 0.2f, 1.1f), (7, 5, 0.12f, 3.3f), (-3, -9, 0.08f, 5.0f) };
            float lo = float.MaxValue, hi = float.MinValue;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N, s = 0;
                    foreach (var (kx, ky, a, ph) in waves) s += a * (1 - Mathf.Abs(Mathf.Sin(2 * Mathf.PI * (kx * u + ky * v) + ph)));
                    s += (TileFbm(u, v, 16, 3, 71 + seed * 13, lats) - 0.5f) * 0.6f;
                    h[y * N + x] = s; lo = Mathf.Min(lo, s); hi = Mathf.Max(hi, s);
                }
            for (int i = 0; i < h.Length; i++) h[i] = (h[i] - lo) / (hi - lo);
            return h;
        }

        /// <summary>Wave slopes and height for the sea shader: r, g = the normal's x and y (0.5 biased), b = height.</summary>
        public static Texture2D WaveMap(float[] h, float strength, string name)
        {
            var px = new Color32[N * N];
            float At(int x, int y) => h[(((y % N) + N) % N) * N + (((x % N) + N) % N)];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (At(x + 1, y) - At(x - 1, y)) * strength, dy = (At(x, y + 1) - At(x, y - 1)) * strength;
                    var n = new Vector3(-dx, -dy, 1).normalized;
                    px[y * N + x] = new Color32((byte)Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255), (byte)Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255), (byte)Mathf.RoundToInt(At(x, y) * 255), 255);
                }
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            t.SetPixels32(px); t.Apply(true, true);
            return t;
        }
    }
}
