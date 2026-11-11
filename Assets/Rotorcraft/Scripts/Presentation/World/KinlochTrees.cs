// Trees for the Kinloch terrain: the Blender trees (trees.glb, two versions of nine species) as terrain tree
// prototypes, each merged into one mesh and tinted per species as the browser game does (render/trees.ts), placed
// by the browser game's rules (placeTrees in render/scene.ts): forestry and woods where a slow noise says
// woodland (conifers higher up), garden trees in the settlements, trees along the river, the shelter belt at
// Moss Farm, and trees growing out of the hedgerows round the fields.
using System;
using System.Collections.Generic;
using System.Linq;
using Rotorcraft.Sim;
using Rotorcraft.World;
using UnityEngine;
using static Rotorcraft.Sim.SimMath;
using Terrain = UnityEngine.Terrain;

namespace Rotorcraft.Presentation
{
    public static class KinlochTrees
    {
        enum Setting { Wood, Hedge, Garden, River, Plantation, Upland }
        static readonly string[] Species = { "oak", "ash", "beech", "sycamore", "birch", "hawthorn", "pine", "spruce", "larch" };

        static readonly Dictionary<Setting, (string sp, int w)[]> MIX = new Dictionary<Setting, (string, int)[]>
        {
            [Setting.Wood] = new[] { ("oak", 3), ("ash", 2), ("beech", 2), ("sycamore", 2), ("birch", 1) },
            [Setting.Hedge] = new[] { ("oak", 3), ("ash", 3), ("sycamore", 1), ("hawthorn", 3) },
            [Setting.Garden] = new[] { ("sycamore", 2), ("birch", 2), ("beech", 1), ("oak", 1), ("hawthorn", 1) },
            [Setting.River] = new[] { ("ash", 2), ("birch", 2), ("sycamore", 1), ("oak", 1) },
            [Setting.Plantation] = new[] { ("spruce", 6), ("larch", 2), ("pine", 1) },
            [Setting.Upland] = new[] { ("pine", 4), ("birch", 2), ("spruce", 1), ("larch", 1) },
        };

        /// <summary>Foliage colour by species (multiplies the greens in the model).</summary>
        static readonly Dictionary<string, Color> LEAF = new Dictionary<string, Color>
        {
            ["oak"] = new Color(1.0f, 0.95f, 0.85f), ["ash"] = new Color(1.1f, 1.1f, 0.9f), ["beech"] = new Color(1.05f, 1.0f, 0.8f), ["sycamore"] = new Color(0.95f, 1.02f, 0.9f), ["birch"] = new Color(1.3f, 1.28f, 0.9f),
            ["hawthorn"] = new Color(0.9f, 0.92f, 0.8f), ["pine"] = new Color(0.78f, 0.86f, 0.95f), ["spruce"] = new Color(0.62f, 0.8f, 0.95f), ["larch"] = new Color(1.3f, 1.25f, 0.75f),
        };

        static double IHash(int a, int b)
        {
            unchecked
            {
                uint h = (uint)((a ^ unchecked((int)0x9e3779b9)) * unchecked((int)0x85ebca6b)) ^ (uint)((b + 0x632be5ab) * unchecked((int)0xc2b2ae35));
                h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12;
                return h / 4294967296.0;
            }
        }

        static string PickSpecies(Setting s, int seed)
        {
            var mix = MIX[s]; var total = mix.Sum(m => m.w);
            var u = IHash(seed, 11) * total;
            foreach (var (sp, w) in mix) { if ((u -= w) < 0) return sp; }
            return mix[0].sp;
        }

        sealed class Proto { public string name; public TreeField.Kind kind; public float height; }

        /// <summary>One prototype per tree variant: its parts merged into a single mesh, one submesh per material,
        /// plus the low crown drawn far away.</summary>
        static List<Proto> Prototypes(GameObject library)
        {
            var list = new List<Proto>();
            foreach (Transform part in library.transform)
            {
                var sp = part.name.Split('_')[0];
                if (!LEAF.ContainsKey(sp)) continue;
                var groups = new Dictionary<Material, List<CombineInstance>>();
                foreach (var mf in part.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mr = mf.GetComponent<MeshRenderer>(); if (mr == null || mf.sharedMesh == null) continue;
                    var m = mf.sharedMesh;
                    // relative to the tree's own origin (the foot of the trunk), turned so the game's axes hold (glTFast mirrors x)
                    var xf = Matrix4x4.Rotate(Quaternion.Euler(0, 180, 0)) * part.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    for (int s = 0; s < m.subMeshCount; s++)
                    {
                        var mat = mr.sharedMaterials[Math.Min(s, mr.sharedMaterials.Length - 1)];
                        if (!groups.TryGetValue(mat, out var l)) groups[mat] = l = new List<CombineInstance>();
                        l.Add(new CombineInstance { mesh = m, subMeshIndex = s, transform = xf });
                    }
                }
                if (groups.Count == 0) continue;
                var subs = new List<CombineInstance>(); var mats = new List<Material>();
                foreach (var kv in groups)
                {
                    var g = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    g.CombineMeshes(kv.Value.ToArray(), true, true);
                    subs.Add(new CombineInstance { mesh = g, transform = Matrix4x4.identity });
                    // foliage is painted per species; bark stays as modelled (every material is a copy, so instancing
                    // can be switched on without touching the imported asset)
                    var mat = new Material(kv.Key) { name = $"{kv.Key.name} {sp}" };
                    if (kv.Key.name.Contains("leaf"))
                    {
                        var c = mat.HasProperty("baseColorFactor") ? mat.GetColor("baseColorFactor") : mat.color;
                        var t = LEAF[sp];
                        var tinted = new Color(c.r * t.r, c.g * t.g, c.b * t.b, c.a);
                        if (mat.HasProperty("baseColorFactor")) mat.SetColor("baseColorFactor", tinted); else mat.color = tinted;
                    }
                    mats.Add(mat);
                }
                var mesh = new Mesh { name = part.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.CombineMeshes(subs.ToArray(), false, false);
                mesh.RecalculateBounds();
                var kind = new TreeField.Kind { name = part.name, near = mesh, nearMats = mats.ToArray() };
                var leaf = mats.FindIndex(m => m.name.Contains("leaf"));
                if (leaf >= 0)
                {
                    kind.far = TreeField.FarCrown(mesh, leaf, sp == "pine" || sp == "spruce" || sp == "larch");
                    var bark = mats.FindIndex(m => !m.name.Contains("leaf"));
                    kind.farMats = new[] { mats[leaf], mats[bark >= 0 ? bark : leaf] };
                }
                else { kind.far = mesh; kind.farMats = kind.nearMats; }
                list.Add(new Proto { name = part.name, kind = kind, height = Mathf.Max(1, mesh.bounds.max.y) });
            }
            return list;
        }

        struct Inst { public double x, y, z, h; public int seed; public Setting setting; }

        /// <summary>Where the trees stand (the browser game's placeTrees, plus hedgerow trees along the field edges).</summary>
        static List<Inst> Place(int max)
        {
            var R = Rng(7341);
            var o = new List<Inst>();
            GroundInfo? Ok(double x, double z)
            {
                var g = WorldMap.GroundAt(x, z);
                if (g.surface != Surface.Grass || g.slope > 0.45 || g.h < 3 || g.h > 400) return null;
                foreach (var s in WorldMap.SITES) if (Js.Hypot(x - s.x, z - s.z) < (s.flatten ?? 30) * 0.85 + 35) return null;
                foreach (var s in WorldMap.STRUCTURES) if (Math.Abs(x - s.x) < 60 && Math.Abs(z - s.z) < 60 && Js.Hypot(x - s.x, z - s.z) < Math.Max(s.w, s.d) * 0.7 + 6) return null;
                if (RoadNet.RoadEdgeDistance(x, z) < 6) return null;
                if (Math.Abs(z - Rotorcraft.World.Terrain.RiverZ(x)) < 12) return null;
                return g;
            }
            void Add(double x, double y, double z, double h, Setting s) => o.Add(new Inst { x = x, y = y, z = z, h = h, seed = (int)Math.Floor(R() * 1e6) + 1, setting = s });
            // forestry blocks and woods: dense stands where a slow noise says woodland, never out in the fields
            // (candidates every 7 m, three times as close as the browser game, so a wood reads as a wood from low level)
            var cand = new List<(double x, double z, double u)>();
            for (double gz = -7500; gz < 5000; gz += 7)
                for (double gx = -9000; gx < 1300; gx += 7)
                {
                    double x = gx + (R() - 0.5) * 6, z = gz + (R() - 0.5) * 6;
                    var dens = Fbm2(x / 700, z / 700, 31, 3);
                    if (dens < 0.6 || R() > (dens - 0.58) * 6) continue;   // thin at the edges, closed canopy inside
                    cand.Add((x, z, R()));
                }
            // every candidate, never a stride through the list: thinning evenly turns a wood into an orchard
            for (int i = 0; i < cand.Count && o.Count < max * 0.85; i++)
            {
                var (x, z, u) = cand[i];
                var g = Ok(x, z); if (g == null) continue;
                if (g.Value.h < 150 && KinlochTerrain.FarmMask(x, z, g.Value.h, g.Value.slope) > 0.3) continue;
                var h = 9 + u * 9;
                if (g.Value.h > 110 || u < 0.5) Add(x, g.Value.h, z, h, g.Value.h > 220 ? Setting.Upland : Setting.Plantation); else Add(x, g.Value.h, z, h * 0.8, Setting.Wood);
            }
            // garden and roadside trees in the settlements, riparian trees along the river
            foreach (var t in Places.SETTLEMENTS)
                for (int i = 0; i < 120; i++)
                {
                    double a = R() * Math.PI * 2, d = Math.Sqrt(R()) * t.r;
                    double x = t.x + Math.Cos(a) * d, z = t.z + Math.Sin(a) * d;
                    var g = Ok(x, z); if (g == null) continue;
                    Add(x, g.Value.h, z, 7 + R() * 6, Setting.Garden);
                }
            for (int i = 0; i < Places.RIVER.Length; i += 1)
            {
                if (R() < 0.3) continue;
                var p = Places.RIVER[i];
                double x = p.x + (R() - 0.5) * 30, z = p.z + (R() < 0.5 ? -1 : 1) * (16 + R() * 20);
                var g = Ok(x, z); if (g == null) continue;
                Add(x, g.Value.h, z, 6 + R() * 6, Setting.River);
            }
            // a shelter belt along the northern edge of Moss Farm (mentioned in the briefing)
            var mf = WorldMap.SiteById("moss_farm");
            for (int i = -7; i <= 7; i++)
            {
                double x = mf.x + i * 13, z = mf.z - 95 + (R() - 0.5) * 6;
                Add(x, WorldMap.TerrainHeight(x, z), z, 15 + R() * 5, Setting.Plantation);
            }
            // hedgerow trees: along the field edges in the core that have a hedge (not a wall or fence: FieldBoundaries), about every 75 m
            var c = KinlochTerrain.CoreArea;
            for (double gz = c.z0; gz < c.z1; gz += 9)
                for (double gx = c.x0; gx < c.x1; gx += 9)
                {
                    double x = gx + (R() - 0.5) * 8, z = gz + (R() - 0.5) * 8;
                    var (fi, row, edge) = FieldLayout.FieldAt(x, z);
                    if (edge > 2.5 || R() > 0.22) continue;
                    var (_, kind) = FieldBoundaries.NearestEdge(x, z);
                    if (kind != Boundary.Hedge) continue;
                    var th = WorldMap.TerrainHeight(x, z);
                    if (th < 1.5 || th > 105 || KinlochTerrain.FarmMask(x, z, th, 0) < 0.45) continue;
                    var g = Ok(x, z); if (g == null) continue;
                    Add(x, g.Value.h, z, 6 + R() * 7, Setting.Hedge);
                }
            return o;
        }

        /// <summary>Make the prototypes and plant the trees in an instanced tree field under the parent.</summary>
        public static TreeField Plant(Transform parent, GameObject library, int max = 14000)
        {
            if (library == null) return null;
            var protos = Prototypes(library);
            if (protos.Count == 0) return null;
            var go = new GameObject("Trees"); go.transform.SetParent(parent, false);
            var field = go.AddComponent<TreeField>();
            var index = new Dictionary<string, int>();
            foreach (var p in protos) index[p.name] = field.AddKind(p.kind);
            var all = Place(max);
            foreach (var t in all)
            {
                var sp = PickSpecies(t.setting, t.seed);
                var name = $"{sp}_{(IHash(t.seed, 3) < 0.5 ? 0 : 1)}";
                if (!index.TryGetValue(name, out var ki)) continue;
                var scale = (float)(t.h / protos[ki].height);
                var width = scale * (0.9f + 0.2f * (float)IHash(t.seed, 13));
                var pos = SimToUnity.Position(new Vec3(t.x, WorldMap.TerrainHeight(t.x, t.z), t.z));
                field.Add(ki, pos, (float)(IHash(t.seed, 9) * 360), new Vector3(width, scale, width));
            }
            field.Finish();
            Debug.Log($"Kinloch trees: {field.Count} placed from {protos.Count} Blender trees");
            return field;
        }
    }
}
