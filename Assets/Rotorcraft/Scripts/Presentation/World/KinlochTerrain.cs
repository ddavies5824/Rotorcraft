// Unity terrain for the Kinloch region, generated from the same height function the flight model uses
// (WorldMap.TerrainHeight), so the ground drawn is the ground landed on. A 12 km core round Kinloch has 2 m
// detail; tiles beyond it, out to the browser game's horizon, have 16 m. Tiles wholly out to sea are left out:
// the sea surface covers them. Surface cover follows the browser game's rules (render/terrainLod.ts and
// render/fields.ts): beach and dunes by the shore, moorland with height, rock on steep ground and the quarry,
// and on the farmland a patchwork of fields (pasture, stubble, ploughed, silage, rough grazing) with hedgerows.
// Grass grows as real blades near the camera (terrain details); trees are placed by KinlochTrees.
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
    /// <summary>Surface layers, in the order the terrain's layer list must follow.</summary>
    public enum SurfaceLayer { Grass, Moor, Rock, Sand, Pasture, Stubble, Ploughed, Hedge }

    public static class KinlochTerrain
    {
        public const double HMIN = -45, HRANGE = 900;          // heightmap range (m): 1.4 cm per step
        /// <summary>Core tiles: 2 km with 2 m cells; outer tiles: 4 km with 16 m cells.</summary>
        public const int CORE = 2048, OUTER = 4096;
        public static readonly (int x0, int z0, int x1, int z1) CoreArea = (-8192, -8192, 4096, 4096);
        public static readonly (int x0, int z0, int x1, int z1) Domain = (-28672, -24576, 8192, 16384);
        public const int LAYERS = 8;
        /// <summary>Grass detail kinds: lush (fields, verges, lawns) and rough (moor, rough grazing).</summary>
        public const int GRASS_KINDS = 2;

        sealed class Tile
        {
            public int x0, z0, size, res, alphaRes;
            public float[,] heights;
            public float[,,] alpha;
            /// <summary>Grass coverage per detail cell and kind (core tiles only; the same grid as the alphamap).</summary>
            public int[][,] grass;
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

#if UNITY_WEBGL && !UNITY_EDITOR
        // the browser computes every tile on one thread: 8 m cells close in, 32 m further out
        const int CORE_RES = 257, CORE_ALPHA = 128, OUTER_RES = 129, OUTER_ALPHA = 64;
#else
        const int CORE_RES = 1025, CORE_ALPHA = 512, OUTER_RES = 257, OUTER_ALPHA = 128;
#endif

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
                    if (!AllSea(x, z, CORE)) tiles.Add(new Tile { x0 = x, z0 = z, size = CORE, res = CORE_RES, alphaRes = CORE_ALPHA });
            var d = Domain;
            for (int x = d.x0; x < d.x1; x += OUTER)
                for (int z = d.z0; z < d.z1; z += OUTER)
                {
                    var inCore = x >= c.x0 && x + OUTER <= c.x1 && z >= c.z0 && z + OUTER <= c.z1;
                    if (!inCore && !AllSea(x, z, OUTER)) tiles.Add(new Tile { x0 = x, z0 = z, size = OUTER, res = OUTER_RES, alphaRes = OUTER_ALPHA });
                }
            return tiles;
        }

        /// <summary>Surface weights at a point (sum 1) and the grass that grows there (lush, rough; 0..1 each).</summary>
        static void Surface(double gx, double gz, double h, double slope, float[] w, out double lush, out double rough)
        {
            Array.Clear(w, 0, LAYERS);
            var dCoast = CoastX(gz) - gx;
            var sand = dCoast < 60 && h < 6 ? 1 : (1 - Smoothstep(60, 160, dCoast)) * 0.6;
            if (h < 0.6) sand = 1;                              // seabed
            var q = Places.QUARRY;
            var quarry = 1 - Smoothstep(q.r * 0.85, q.r * 1.1, Js.Hypot(gx - q.x, gz - q.z));
            // the open hill: rock outcrops on the high ground, moor broken by patches of grass and heath
            var outcrop = h > 200 ? Smoothstep(0.70, 0.80, Fbm2(gx / 60, gz / 60, 91, 4)) * Smoothstep(200, 300, h) : 0;
            var rock = Math.Max(Math.Max(Smoothstep(0.42, 0.7, slope), quarry), outcrop * 0.6);
            var patch = Fbm2(gx / 180, gz / 180, 77, 4);
            var moor = Clamp((h - 140) / 220 + (patch - 0.5) * 2.6, 0, 1);
            var farm = h > 0.6 ? FarmMask(gx, gz, h, slope) : 0;
            double wRock = rock, wSand = sand * (1 - wRock), left = Math.Max(0, 1 - wRock - wSand);
            // the farmland: each field's own crop, with a hedge along its hedged edges
            var wFarm = farm * left;
            if (wFarm > 0)
            {
                var (i, row, edge) = FieldLayout.FieldAt(gx, gz);
                var hedge = h < 105 ? 1 - Smoothstep(1.0, 5.0, edge) : 0;   // soft, so the 4 m texels do not show as steps
                var crop = FieldLayout.CropOf(i, row);
                var cropLayer = crop == Crop.Stubble ? SurfaceLayer.Stubble : crop == Crop.Ploughed ? SurfaceLayer.Ploughed : crop == Crop.Rough ? SurfaceLayer.Grass : SurfaceLayer.Pasture;
                w[(int)SurfaceLayer.Hedge] += (float)(wFarm * hedge);
                w[(int)cropLayer] += (float)(wFarm * (1 - hedge));
                lush = wFarm * (1 - hedge) * (crop == Crop.Pasture || crop == Crop.Silage ? 0.9 : crop == Crop.Stubble ? 0.25 : 0);
                rough = wFarm * (1 - hedge) * (crop == Crop.Rough ? 0.8 : 0) + wFarm * hedge * 0.9;
            }
            else { lush = 0; rough = 0; }
            var open = left - wFarm;
            var wMoor = moor * open;
            w[(int)SurfaceLayer.Rock] += (float)wRock;
            w[(int)SurfaceLayer.Sand] += (float)wSand;
            w[(int)SurfaceLayer.Moor] += (float)wMoor;
            w[(int)SurfaceLayer.Grass] += (float)(open - wMoor);
            lush += (open - wMoor) * 0.85;
            rough += wMoor * 0.55 + wSand * (dCoast > 30 ? 0.35 : 0);
        }

        /// <summary>Heights, surface cover and grass for one tile (pure computation, safe on a worker thread).</summary>
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
            t.alpha = new float[a, a, LAYERS];
            var core = t.size == CORE;
            if (core) t.grass = new[] { new int[a, a], new int[a, a] };
            var w = new float[LAYERS];
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
                    Surface(gx, gz, h, slope, w, out var lush, out var rough);
                    for (int l = 0; l < LAYERS; l++) t.alpha[j, i, l] = w[l];
                    if (core && h > 1.5)
                    {
                        // no grass on the tarmac, its verges' edge, or within a few metres of the river
                        var road = RoadNet.RoadEdgeDistance(gx, gz);
                        var clear = Smoothstep(0.5, 2.5, road) * Smoothstep(6, 12, Math.Abs(gz - RiverZ(gx)));
                        t.grass[0][j, i] = (int)(Clamp(lush * clear, 0, 1) * 255);
                        t.grass[1][j, i] = (int)(Clamp(rough * clear, 0, 1) * 255);
                    }
                }
            if (core) ClearUnderStructures(t, acell);
        }

        /// <summary>No grass through floors and pads: clear the grass under every structure, pad and paved area.</summary>
        static void ClearUnderStructures(Tile t, double acell)
        {
            void Clear(double x, double z, double hw, double hd, double rot)
            {
                double c = Math.Cos(rot), s = Math.Sin(rot), r = Js.Hypot(hw, hd);
                int i0 = (int)Math.Floor((x - r - t.x0) / acell), i1 = (int)Math.Ceiling((x + r - t.x0) / acell);
                int j0 = (int)Math.Floor((t.z0 + t.size - (z + r)) / acell), j1 = (int)Math.Ceiling((t.z0 + t.size - (z - r)) / acell);
                for (int j = Math.Max(0, j0); j <= Math.Min(t.alphaRes - 1, j1); j++)
                    for (int i = Math.Max(0, i0); i <= Math.Min(t.alphaRes - 1, i1); i++)
                    {
                        double gx = t.x0 + (i + 0.5) * acell - x, gz = t.z0 + t.size - (j + 0.5) * acell - z;
                        double lx = gx * c - gz * s, lz = gx * s + gz * c;
                        if (Math.Abs(lx) <= hw && Math.Abs(lz) <= hd) { t.grass[0][j, i] = 0; t.grass[1][j, i] = 0; }
                    }
            }
            bool Near(double x, double z, double r) => x + r >= t.x0 && x - r <= t.x0 + t.size && z + r >= t.z0 && z - r <= t.z0 + t.size;
            foreach (var st in WorldMap.STRUCTURES) if (Near(st.x, st.z, Js.Hypot(st.w, st.d))) Clear(st.x, st.z, st.w / 2 + 2, st.d / 2 + 2, st.rot);
            foreach (var p in Places.PAVED) if (Near(p.x, p.z, Js.Hypot(p.w, p.d))) Clear(p.x, p.z, p.w / 2 + 1, p.d / 2 + 1, p.rot);
            foreach (var s in WorldMap.SITES) if (s.radius > 0 && s.kind != SiteKind.Field && Near(s.x, s.z, s.radius * 2)) Clear(s.x, s.z, s.radius * 1.45, s.radius * 1.45, 0);
        }

        // ---- tile cache: computing the region takes about a minute, so each tile is kept on disk after the first
        // time, under a key that changes whenever the world data or this generator changes
        const string GeneratorVersion = "terrain-7";

        static string CacheDir()
        {
            unchecked
            {
                long h = 17;
                foreach (var s in WorldMap.SITES) h = h * 31 + (long)(s.x * 7 + s.z * 13 + (s.flatten ?? 0) * 3 + (s.deckHeight ?? 0));
                foreach (var f in Places.FLAT_AREAS) h = h * 31 + (long)(f.x + f.z * 3 + f.r * 5 + (f.drop ?? 0));
                foreach (var r in Places.ROADS) h = h * 31 + r.pts.Length * 7 + (long)(r.width * 10);
                h = h * 31 + WorldMap.STRUCTURES.Count;
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
                    if (r.ReadInt32() != 0x4B4C5453 || r.ReadInt32() != t.res || r.ReadInt32() != t.alphaRes || r.ReadInt32() != LAYERS) return false;
                    int n = t.res, a = t.alphaRes;
                    var hb = r.ReadBytes(n * n * 2); var ab = r.ReadBytes(a * a * LAYERS);
                    if (hb.Length != n * n * 2 || ab.Length != a * a * LAYERS) return false;
                    t.heights = new float[n, n];
                    for (int j = 0, k = 0; j < n; j++) for (int i = 0; i < n; i++, k += 2) t.heights[j, i] = (hb[k] | hb[k + 1] << 8) / 65535f;
                    t.alpha = new float[a, a, LAYERS];
                    for (int j = 0, k = 0; j < a; j++) for (int i = 0; i < a; i++) for (int l = 0; l < LAYERS; l++, k++) t.alpha[j, i, l] = ab[k] / 255f;
                    if (r.ReadBoolean())
                    {
                        t.grass = new int[GRASS_KINDS][,];
                        for (int g = 0; g < GRASS_KINDS; g++)
                        {
                            var gb = r.ReadBytes(a * a); if (gb.Length != a * a) return false;
                            t.grass[g] = new int[a, a];
                            for (int j = 0, k = 0; j < a; j++) for (int i = 0; i < a; i++, k++) t.grass[g][j, i] = gb[k];
                        }
                    }
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
                var hb = new byte[n * n * 2]; var ab = new byte[a * a * LAYERS];
                for (int j = 0, k = 0; j < n; j++) for (int i = 0; i < n; i++, k += 2) { var v = (ushort)Mathf.RoundToInt(t.heights[j, i] * 65535f); hb[k] = (byte)v; hb[k + 1] = (byte)(v >> 8); }
                for (int j = 0, k = 0; j < a; j++) for (int i = 0; i < a; i++) for (int l = 0; l < LAYERS; l++, k++) ab[k] = (byte)Mathf.RoundToInt(t.alpha[j, i, l] * 255f);
                var tmp = path + ".tmp";
                using (var w = new System.IO.BinaryWriter(System.IO.File.Create(tmp)))
                {
                    w.Write(0x4B4C5453); w.Write(n); w.Write(a); w.Write(LAYERS); w.Write(hb); w.Write(ab);
                    w.Write(t.grass != null);
                    if (t.grass != null)
                        foreach (var g in t.grass) { var gb = new byte[a * a]; for (int j = 0, k = 0; j < a; j++) for (int i = 0; i < a; i++, k++) gb[k] = (byte)g[j, i]; w.Write(gb); }
                }
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                System.IO.File.Move(tmp, path);
            }
            catch (Exception e) { Debug.LogWarning($"Terrain cache not written ({e.Message})"); }
        }

        // ---- generated stand-ins, used only when the photo layers are not assigned
        static float Hash(int x, int y, int s) { unchecked { uint h = (uint)(x * 374761393 + y * 668265263 + s * 2147483647); h = (h ^ (h >> 13)) * 1274126177u; return (h ^ (h >> 16)) / 4294967296f; } }
        static float TileNoise(float x, float y, int period, int seed)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy, ux = fx * fx * (3 - 2 * fx), uy = fy * fy * (3 - 2 * fy);
            int x0 = ((ix % period) + period) % period, y0 = ((iy % period) + period) % period, x1 = (x0 + 1) % period, y1 = (y0 + 1) % period;
            return Mathf.Lerp(Mathf.Lerp(Hash(x0, y0, seed), Hash(x1, y0, seed), ux), Mathf.Lerp(Hash(x0, y1, seed), Hash(x1, y1, seed), ux), uy);
        }

        static Texture2D Noise(string name, Color a, Color b)
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float n = 0, amp = 0.55f, tot = 0;
                    for (int o = 0; o < 4; o++) { var p = (int)(4 * Mathf.Pow(2, o)); n += amp * TileNoise(x * p / (float)size, y * p / (float)size, p, o * 31); tot += amp; amp *= 0.5f; }
                    var c = Color.Lerp(a, b, Mathf.Clamp01((n / tot - 0.5f) * 1.6f + 0.5f)); c.a = 0.05f;
                    px[y * size + x] = c;
                }
            tex.SetPixels(px); tex.Apply(true);
            return tex;
        }

        static TerrainLayer[] GeneratedLayers()
        {
            TerrainLayer L(string n, Color a, Color b, float tile) => new TerrainLayer { name = n, diffuseTexture = Noise(n, a, b), tileSize = new Vector2(tile, tile) };
            return new[]
            {
                L("Grass", new Color(0.24f, 0.31f, 0.15f), new Color(0.33f, 0.40f, 0.20f), 40), L("Moor", new Color(0.33f, 0.28f, 0.20f), new Color(0.46f, 0.40f, 0.28f), 50),
                L("Rock", new Color(0.38f, 0.37f, 0.35f), new Color(0.54f, 0.52f, 0.49f), 24), L("Sand", new Color(0.72f, 0.66f, 0.50f), new Color(0.82f, 0.77f, 0.61f), 20),
                L("Pasture", new Color(0.30f, 0.40f, 0.17f), new Color(0.40f, 0.48f, 0.22f), 40), L("Stubble", new Color(0.55f, 0.50f, 0.30f), new Color(0.66f, 0.60f, 0.38f), 30),
                L("Ploughed", new Color(0.28f, 0.21f, 0.15f), new Color(0.38f, 0.30f, 0.21f), 30), L("Hedge", new Color(0.10f, 0.16f, 0.07f), new Color(0.16f, 0.22f, 0.10f), 20),
            };
        }

        /// <summary>
        /// Build every tile under parent. Tiles come from the disk cache, or are computed on all cores and then
        /// cached. layers: one TerrainLayer per SurfaceLayer (generated stand-ins when null); grass: the two grass
        /// detail kinds (none when null).
        /// </summary>
        public static List<Terrain> Build(Transform parent, Material material, TerrainLayer[] layers, DetailPrototype[] grass)
        {
            var made = new List<Terrain>();
            var steps = BuildSteps(parent, material, layers, grass, made, null);
            while (steps.MoveNext()) { }
            return made;
        }

        /// <summary>The same build in steps a frame apart (the browser has a single thread: the page must keep
        /// drawing the loading screen). progress: 0..1.</summary>
        public static System.Collections.IEnumerator BuildSteps(Transform parent, Material material, TerrainLayer[] layers, DetailPrototype[] grass, List<Terrain> made, Action<float> progress)
        {
            WarmUp();
            var tiles = Plan();
            var t0 = Time.realtimeSinceStartup;
            var dir = CacheDir();
            System.IO.Directory.CreateDirectory(dir);
            int computed = 0;
            void One(Tile t)
            {
                var path = CachePath(dir, t);
                if (Load(path, t)) return;
                Compute(t);
                Save(path, t);
                System.Threading.Interlocked.Increment(ref computed);
            }
#if UNITY_WEBGL
            Debug.Log($"Terrain: {tiles.Count} tiles planned at {Time.realtimeSinceStartup - t0:F1} s");
            for (int i = 0; i < tiles.Count; i++)   // no worker threads in the browser: one tile a frame
            {
                var tt = Time.realtimeSinceStartup;
                One(tiles[i]);
                if (i < 3 || i % 10 == 0) Debug.Log($"Terrain tile {i + 1}/{tiles.Count} ({tiles[i].res}) took {Time.realtimeSinceStartup - tt:F2} s");
                progress?.Invoke(0.8f * (i + 1) / tiles.Count);
                yield return null;
            }
#else
            Parallel.ForEach(tiles, One);
#endif
            if (layers == null || layers.Length != LAYERS) layers = GeneratedLayers();
            var byCore = new Dictionary<(int, int), Terrain>();
            int n = 0;
            foreach (var t in tiles)
            {
                if (++n % 8 == 0) { progress?.Invoke(0.8f + 0.2f * n / tiles.Count); yield return null; }
                var td = new TerrainData { heightmapResolution = t.res, alphamapResolution = t.alphaRes, baseMapResolution = Math.Min(1024, t.alphaRes * 2) };
                td.size = new Vector3(t.size, (float)HRANGE, t.size);
                td.SetHeights(0, 0, t.heights);
                td.terrainLayers = layers;
                td.SetAlphamaps(0, 0, t.alpha);
                if (grass != null && t.grass != null)
                {
                    td.SetDetailScatterMode(DetailScatterMode.CoverageMode);
                    td.SetDetailResolution(t.alphaRes, 16);   // small patches: Unity drops a patch that holds too many blades
                    td.detailPrototypes = grass;
                    for (int g = 0; g < Math.Min(grass.Length, t.grass.Length); g++) td.SetDetailLayer(0, 0, g, t.grass[g]);
                    td.wavingGrassStrength = 0.35f; td.wavingGrassAmount = 0.25f; td.wavingGrassSpeed = 0.35f; td.wavingGrassTint = new Color(0.9f, 0.95f, 0.85f);
                }
                var go = new GameObject($"Terrain {t.x0},{t.z0}");
                go.transform.SetParent(parent, false);
                go.transform.position = new Vector3(t.x0, (float)HMIN, -(t.z0 + t.size));
                var terrain = go.AddComponent<Terrain>();
                terrain.terrainData = td;
                if (material != null) terrain.materialTemplate = material;
                terrain.heightmapPixelError = t.size == CORE ? 2 : 8;
                terrain.basemapDistance = 900;
                terrain.drawInstanced = true;
                terrain.detailObjectDistance = 160;
                terrain.detailObjectDensity = 1;
                terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;   // trees are drawn by TreeField
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
        }

        /// <summary>The terrain tile holding sim (x, z), or null.</summary>
        public static Terrain TileAt(IList<Terrain> tiles, double x, double z)
        {
            foreach (var t in tiles)
            {
                var p = t.transform.position; var s = t.terrainData.size;
                double tx0 = p.x, tz0 = -(p.z + s.z);   // sim z range [tz0, tz0 + size]
                if (x >= tx0 && x < tx0 + s.x && z >= tz0 && z < tz0 + s.z) return t;
            }
            return null;
        }
    }
}
