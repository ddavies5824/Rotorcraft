// Unity terrain for the Kinloch region, generated from the same height function the flight model uses
// (WorldMap.TerrainHeight), so the ground drawn is the ground landed on. A 12 km core round Kinloch has 2 m
// detail; tiles beyond it, out to the browser game's horizon, have 16 m. Tiles wholly out to sea are left out:
// the sea surface covers them. Surface cover follows the browser game's rules (render/terrainLod.ts): beach and
// dunes by the shore, moorland with height, rock on steep ground and the quarry, crop fields on the farmland.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Rotorcraft.Sim;
using Rotorcraft.World;
using UnityEngine;
using static Rotorcraft.Sim.SimMath;
using static Rotorcraft.World.Terrain;
using Terrain = UnityEngine.Terrain;

namespace Rotorcraft.Presentation
{
    public static class KinlochTerrain
    {
        public const double HMIN = -45, HRANGE = 900;          // heightmap range (m): 1.4 cm per step
        /// <summary>Core tiles: 2 km with 2 m cells; outer tiles: 4 km with 16 m cells.</summary>
        public const int CORE = 2048, OUTER = 4096;
        public static readonly (int x0, int z0, int x1, int z1) CoreArea = (-8192, -8192, 4096, 4096);
        public static readonly (int x0, int z0, int x1, int z1) Domain = (-28672, -24576, 8192, 16384);

        enum Layer { Grass, Moor, Rock, Sand, Field }

        sealed class Tile
        {
            public int x0, z0, size, res, alphaRes;
            public float[,] heights;
            public float[,,] alpha;
        }

        /// <summary>The farmland mask (port of farmMask in render/terrainLod.ts).</summary>
        public static double FarmMask(double x, double z, double h, double slope)
        {
            var d = CoastX(z) - x;
            var m = Smoothstep(120, 260, d) * (1 - Smoothstep(4200, 5200, d)) * (1 - Smoothstep(150, 200, h)) * (1 - Smoothstep(0.12, 0.2, slope));
            if (m <= 0) return 0;
            foreach (var s in WorldMap.SITES) if (s.flatten.HasValue && s.flatten > 0) m *= Smoothstep(s.flatten.Value * 0.6, s.flatten.Value * 1.0, Js.Hypot(x - s.x, z - s.z));
            foreach (var t in Places.SETTLEMENTS) m *= Smoothstep(t.r * 0.55, t.r * 0.9, Js.Hypot(x - t.x, z - t.z));
            m *= Smoothstep(30, 70, Math.Abs(z - RiverZ(x)));
            // no crops on the quarry, the golf links, the caravan park or the levelled building plots
            var q = Places.QUARRY;
            m *= Smoothstep(q.r * 1.05, q.r * 1.4, Js.Hypot(x - q.x, z - q.z));
            m *= Smoothstep(380, 470, Js.Hypot(x - 80, z + 1650));
            m *= Smoothstep(160, 230, Js.Hypot(x - 690, z - 2450));
            foreach (var f in Places.FLAT_AREAS) m *= Smoothstep(f.r * 0.9, f.r * 1.4, Js.Hypot(x - f.x, z - f.z));
            return m;
        }

        /// <summary>Make sure everything TerrainHeight caches lazily is filled, so tiles can be computed on several threads.</summary>
        static void WarmUp()
        {
            foreach (var f in Places.FLAT_AREAS) FlatElevation(f);
            foreach (var s in WorldMap.SITES) WorldMap.SiteElevation(s);
            WorldMap.TerrainHeight(0, 0);
            RoadNet.BridgeDeck();
        }

        static List<Tile> Plan()
        {
            var tiles = new List<Tile>();
            bool AllSea(int x0, int z0, int size)
            {
                double maxCoast = double.NegativeInfinity;
                for (int k = 0; k <= 32; k++) maxCoast = Math.Max(maxCoast, CoastX(z0 + size * k / 32.0));
                return x0 > maxCoast + 150;
            }
            var c = CoreArea;
            for (int x = c.x0; x < c.x1; x += CORE)
                for (int z = c.z0; z < c.z1; z += CORE)
                    if (!AllSea(x, z, CORE)) tiles.Add(new Tile { x0 = x, z0 = z, size = CORE, res = 1025, alphaRes = 512 });
            var d = Domain;
            for (int x = d.x0; x < d.x1; x += OUTER)
                for (int z = d.z0; z < d.z1; z += OUTER)
                {
                    var inCore = x >= c.x0 && x + OUTER <= c.x1 && z >= c.z0 && z + OUTER <= c.z1;
                    if (!inCore && !AllSea(x, z, OUTER)) tiles.Add(new Tile { x0 = x, z0 = z, size = OUTER, res = 257, alphaRes = 128 });
                }
            return tiles;
        }

        /// <summary>Heights and surface cover for one tile (pure computation, safe on a worker thread).</summary>
        static void Compute(Tile t)
        {
            int n = t.res; double cell = (double)t.size / (n - 1);
            var raw = new double[n, n];
            // Unity's heightmap rows run along Unity z (= north, sim -z): row j is at sim z = z0 + size - j*cell
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                    raw[j, i] = WorldMap.TerrainHeight(t.x0 + i * cell, t.z0 + t.size - j * cell);
            t.heights = new float[n, n];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                    t.heights[j, i] = (float)Clamp((raw[j, i] - HMIN) / HRANGE, 0, 1);
            int a = t.alphaRes; double acell = (double)t.size / a;
            t.alpha = new float[a, a, 5];
            for (int j = 0; j < a; j++)
                for (int i = 0; i < a; i++)
                {
                    // texel centre, its height and slope from the height grid
                    double fx = (i + 0.5) * acell, fz = (j + 0.5) * acell;
                    double gx = t.x0 + fx, gz = t.z0 + t.size - fz;
                    int hi = Math.Min(n - 2, (int)(fx / cell)), hj = Math.Min(n - 2, (int)(fz / cell));
                    var h = raw[hj, hi];
                    double sx = (raw[hj, hi + 1] - raw[hj, hi]) / cell, sz = (raw[hj + 1, hi] - raw[hj, hi]) / cell;
                    var slope = Math.Atan(Math.Sqrt(sx * sx + sz * sz));
                    var dCoast = CoastX(gz) - gx;
                    var sand = dCoast < 60 && h < 6 ? 1 : (1 - Smoothstep(60, 160, dCoast)) * 0.6;
                    if (h < 0.6) sand = 1;                              // seabed
                    var q = Places.QUARRY;
                    var quarry = 1 - Smoothstep(q.r * 0.85, q.r * 1.1, Js.Hypot(gx - q.x, gz - q.z));
                    var rock = Math.Max(Smoothstep(0.42, 0.7, slope), quarry);
                    var moor = Clamp((h - 160) / 240, 0, 1);
                    var field = h > 0.6 ? FarmMask(gx, gz, h, slope) : 0;
                    // stack them: rock over everything, then sand, then fields, then moor over grass
                    var wRock = rock;
                    var wSand = sand * (1 - wRock);
                    var wField = field * (1 - wRock - wSand);
                    var rest = Math.Max(0, 1 - wRock - wSand - wField);
                    var wMoor = moor * rest;
                    var wGrass = rest - wMoor;
                    // alphamap rows also run along Unity z
                    t.alpha[j, i, (int)Layer.Grass] = (float)wGrass;
                    t.alpha[j, i, (int)Layer.Moor] = (float)wMoor;
                    t.alpha[j, i, (int)Layer.Rock] = (float)wRock;
                    t.alpha[j, i, (int)Layer.Sand] = (float)wSand;
                    t.alpha[j, i, (int)Layer.Field] = (float)wField;
                }
        }

        // ---- procedural surface textures (no image files to carry about)
        /// <summary>
        /// A tileable noise texture between two colours. URP's terrain shader reads the alpha channel as
        /// glossiness, so alpha carries the surface's (low) smoothness, not opacity.
        /// </summary>
        static Texture2D Noise(string name, Color a, Color b, float scale, float gloss, Func<int, int, float> pattern = null, int size = 512)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // tileable value noise: sample on a torus by wrapping the lattice; broad features, fine grain on top
                    float n = 0, amp = 0.55f, tot = 0;
                    for (int o = 0; o < 5; o++)
                    {
                        var p = (int)(4 * Mathf.Pow(2, o));
                        n += amp * TileNoise(x * p / (float)size, y * p / (float)size, p, o * 31); tot += amp; amp *= 0.5f;
                    }
                    n /= tot;
                    var k = Mathf.Clamp01((n - 0.5f) * scale + 0.5f);
                    if (pattern != null) k = Mathf.Clamp01(k * 0.6f + pattern(x, y) * 0.4f);
                    var c = Color.Lerp(a, b, k);
                    c.a = gloss;
                    px[y * size + x] = c;
                }
            tex.SetPixels(px); tex.Apply(true);
            return tex;
        }

        static float Hash(int x, int y, int s) { unchecked { uint h = (uint)(x * 374761393 + y * 668265263 + s * 2147483647); h = (h ^ (h >> 13)) * 1274126177u; return (h ^ (h >> 16)) / 4294967296f; } }
        static float TileNoise(float x, float y, int period, int seed)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy, ux = fx * fx * (3 - 2 * fx), uy = fy * fy * (3 - 2 * fy);
            int x0 = ((ix % period) + period) % period, y0 = ((iy % period) + period) % period, x1 = (x0 + 1) % period, y1 = (y0 + 1) % period;
            return Mathf.Lerp(Mathf.Lerp(Hash(x0, y0, seed), Hash(x1, y0, seed), ux), Mathf.Lerp(Hash(x0, y1, seed), Hash(x1, y1, seed), ux), uy);
        }

        static TerrainLayer MakeLayer(string name, Texture2D tex, float tile)
        {
            return new TerrainLayer { name = name, diffuseTexture = tex, tileSize = new Vector2(tile, tile), smoothness = 0, metallic = 0 };
        }

        static TerrainLayer[] Layers()
        {
            var grass = Noise("Grass", new Color(0.24f, 0.31f, 0.15f), new Color(0.33f, 0.40f, 0.20f), 1.6f, 0.06f);
            var moor = Noise("Moor", new Color(0.33f, 0.28f, 0.20f), new Color(0.46f, 0.40f, 0.28f), 1.8f, 0.05f);
            var rock = Noise("Rock", new Color(0.38f, 0.37f, 0.35f), new Color(0.54f, 0.52f, 0.49f), 2.2f, 0.12f);
            var sand = Noise("Sand", new Color(0.72f, 0.66f, 0.50f), new Color(0.82f, 0.77f, 0.61f), 1.2f, 0.10f);
            // farmland: a paler, yellower green (separate hedged fields with their own crops come with the field layout)
            var field = Noise("Field", new Color(0.38f, 0.41f, 0.20f), new Color(0.50f, 0.49f, 0.26f), 1.0f, 0.05f);
            return new[] { MakeLayer("Grass", grass, 40), MakeLayer("Moor", moor, 50), MakeLayer("Rock", rock, 24), MakeLayer("Sand", sand, 20), MakeLayer("Field", field, 60) };
        }

        // ---- tile cache: computing the region takes about a minute, so each tile is kept on disk after the first
        // time, under a key that changes whenever the world data or this generator changes
        const string GeneratorVersion = "terrain-2";

        static string CacheDir()
        {
            unchecked
            {
                long h = 17;
                foreach (var s in WorldMap.SITES) h = h * 31 + (long)(s.x * 7 + s.z * 13 + (s.flatten ?? 0) * 3 + (s.deckHeight ?? 0));
                foreach (var f in Places.FLAT_AREAS) h = h * 31 + (long)(f.x + f.z * 3 + f.r * 5 + (f.drop ?? 0));
                foreach (var r in Places.ROADS) h = h * 31 + r.pts.Length * 7 + (long)(r.width * 10);
                return System.IO.Path.Combine(Application.persistentDataPath, "TerrainCache", $"{GeneratorVersion}-{(ulong)h:x}");
            }
        }

        static string CachePath(string dir, Tile t) => System.IO.Path.Combine(dir, $"{t.x0}_{t.z0}_{t.res}_{t.alphaRes}.bin");

        static bool Load(string path, Tile t)
        {
            try
            {
                if (!System.IO.File.Exists(path)) return false;
                using (var r = new System.IO.BinaryReader(System.IO.File.OpenRead(path)))
                {
                    if (r.ReadInt32() != 0x4B4C5452 || r.ReadInt32() != t.res || r.ReadInt32() != t.alphaRes) return false;
                    int n = t.res, a = t.alphaRes;
                    var hb = r.ReadBytes(n * n * 2); var ab = r.ReadBytes(a * a * 5);
                    if (hb.Length != n * n * 2 || ab.Length != a * a * 5) return false;
                    t.heights = new float[n, n];
                    for (int j = 0, k = 0; j < n; j++) for (int i = 0; i < n; i++, k += 2) t.heights[j, i] = (hb[k] | hb[k + 1] << 8) / 65535f;
                    t.alpha = new float[a, a, 5];
                    for (int j = 0, k = 0; j < a; j++) for (int i = 0; i < a; i++) for (int l = 0; l < 5; l++, k++) t.alpha[j, i, l] = ab[k] / 255f;
                    return true;
                }
            }
            catch (Exception) { return false; }
        }

        static void Save(string path, Tile t)
        {
            try
            {
                int n = t.res, a = t.alphaRes;
                var hb = new byte[n * n * 2]; var ab = new byte[a * a * 5];
                for (int j = 0, k = 0; j < n; j++) for (int i = 0; i < n; i++, k += 2) { var v = (ushort)Mathf.RoundToInt(t.heights[j, i] * 65535f); hb[k] = (byte)v; hb[k + 1] = (byte)(v >> 8); }
                for (int j = 0, k = 0; j < a; j++) for (int i = 0; i < a; i++) for (int l = 0; l < 5; l++, k++) ab[k] = (byte)Mathf.RoundToInt(t.alpha[j, i, l] * 255f);
                var tmp = path + ".tmp";
                using (var w = new System.IO.BinaryWriter(System.IO.File.Create(tmp))) { w.Write(0x4B4C5452); w.Write(n); w.Write(a); w.Write(hb); w.Write(ab); }
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                System.IO.File.Move(tmp, path);
            }
            catch (Exception e) { Debug.LogWarning($"Terrain cache not written ({e.Message})"); }
        }

        /// <summary>Build every tile under parent. Tiles come from the disk cache, or are computed on all cores and then cached.</summary>
        public static List<Terrain> Build(Transform parent, Material material)
        {
            WarmUp();
            var tiles = Plan();
            var t0 = Time.realtimeSinceStartup;
            var dir = CacheDir();
            System.IO.Directory.CreateDirectory(dir);
            int computed = 0;
            Parallel.ForEach(tiles, t =>
            {
                var path = CachePath(dir, t);
                if (Load(path, t)) return;
                Compute(t);
                Save(path, t);
                System.Threading.Interlocked.Increment(ref computed);
            });
            var layers = Layers();
            var made = new List<Terrain>();
            var byCore = new Dictionary<(int, int), Terrain>();
            foreach (var t in tiles)
            {
                var td = new TerrainData { heightmapResolution = t.res, alphamapResolution = t.alphaRes, baseMapResolution = Math.Min(512, t.alphaRes) };
                td.size = new Vector3(t.size, (float)HRANGE, t.size);
                td.SetHeights(0, 0, t.heights);
                td.terrainLayers = layers;
                td.SetAlphamaps(0, 0, t.alpha);
                var go = new GameObject($"Terrain {t.x0},{t.z0}");
                go.transform.SetParent(parent, false);
                go.transform.position = new Vector3(t.x0, (float)HMIN, -(t.z0 + t.size));
                var terrain = go.AddComponent<Terrain>();
                terrain.terrainData = td;
                if (material != null) terrain.materialTemplate = material;
                terrain.heightmapPixelError = t.size == CORE ? 2 : 8;
                terrain.basemapDistance = 1200;
                terrain.drawInstanced = true;
                terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
                go.isStatic = true;
                made.Add(terrain);
                if (t.size == CORE) byCore[(t.x0, t.z0)] = terrain;
            }
            // neighbours among the core tiles, so their levels of detail meet without cracks
            foreach (var kv in byCore)
            {
                var (x, z) = kv.Key;
                byCore.TryGetValue((x - CORE, z), out var left); byCore.TryGetValue((x + CORE, z), out var right);
                byCore.TryGetValue((x, z - CORE), out var top); byCore.TryGetValue((x, z + CORE), out var bottom);   // sim -z is Unity +z
                kv.Value.SetNeighbors(left, top, right, bottom);
            }
            Debug.Log($"Kinloch terrain: {tiles.Count} tiles ({computed} computed, {tiles.Count - computed} from the cache) in {Time.realtimeSinceStartup - t0:F1} s");
            return made;
        }
    }
}
