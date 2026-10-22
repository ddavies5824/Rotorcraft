// Everything laid on the Kinloch terrain: the sea, the roads (meshes along each road's smoothed profile, with
// dashed centre lines), the bridge, landing pads with their markings, car parks and yards, the Blender
// landmarks placed by key, and the remaining structures (houses, barns, sheds, piers, turbines...) built
// from their footprints. Positions come from the ported world data, so everything sits where the flight model
// expects it.
using System;
using System.Collections.Generic;
using System.Linq;
using Rotorcraft.Sim;
using Rotorcraft.World;
using UnityEngine;
using static Rotorcraft.Presentation.MeshBuilder;

namespace Rotorcraft.Presentation
{
    public sealed class WorldMaterials
    {
        public Material asphalt, gravel, marking, concrete, padPaint, padGreen, harl, harlCream, slate, metalRoof, redMetal, stone, timber, white, glassDark, water, steel;

        static Material Lit(string name, Color c, float smooth = 0.1f, float metal = 0)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            return m;
        }

        public static WorldMaterials Make() => new WorldMaterials
        {
            asphalt = Lit("Asphalt", new Color(0.13f, 0.13f, 0.14f), 0.15f),
            gravel = Lit("Gravel track", new Color(0.42f, 0.39f, 0.33f), 0.05f),
            marking = Lit("Road marking", new Color(0.85f, 0.85f, 0.8f), 0.2f),
            concrete = Lit("Concrete", new Color(0.55f, 0.55f, 0.53f), 0.12f),
            padPaint = Lit("Pad paint", new Color(0.92f, 0.92f, 0.88f), 0.25f),
            padGreen = Lit("Pad green", new Color(0.12f, 0.36f, 0.2f), 0.2f),
            harl = Lit("Harl white", new Color(0.86f, 0.85f, 0.81f), 0.08f),
            harlCream = Lit("Harl cream", new Color(0.82f, 0.77f, 0.66f), 0.08f),
            slate = Lit("Slate", new Color(0.22f, 0.23f, 0.26f), 0.35f),
            metalRoof = Lit("Metal roof", new Color(0.36f, 0.40f, 0.43f), 0.4f, 0.3f),
            redMetal = Lit("Red cladding", new Color(0.45f, 0.14f, 0.11f), 0.3f, 0.2f),
            stone = Lit("Stone", new Color(0.48f, 0.46f, 0.42f), 0.05f),
            timber = Lit("Timber", new Color(0.30f, 0.22f, 0.15f), 0.1f),
            white = Lit("White", new Color(0.9f, 0.9f, 0.88f), 0.4f),
            glassDark = Lit("Window", new Color(0.05f, 0.07f, 0.09f), 0.85f),
            water = Lit("Sea", new Color(0.04f, 0.12f, 0.16f), 0.92f),
            steel = Lit("Steel", new Color(0.55f, 0.57f, 0.58f), 0.5f, 0.8f),
        };
    }

    public static class KinlochDressing
    {
        const float RAD = 180f / Mathf.PI;

        /// <summary>Unity yaw for a structure turned by rot in the sim (its local x maps to (cos rot, -sin rot) in sim x, z).</summary>
        public static Quaternion Yaw(double rot) => Quaternion.Euler(0, (float)(-rot * RAD), 0);

        public static void Build(Transform parent, WorldMaterials M, GameObject landmarks)
        {
            Sea(parent, M);
            Roads(parent, M);
            Bridge(parent, M);
            Pads(parent, M);
            Paved(parent, M);
            Structures(parent, M, landmarks);
        }

        // ------------------------------------------------------------------------------------------ sea
        static void Sea(Transform parent, WorldMaterials M)
        {
            var b = new MeshBuilder();
            var d = KinlochTerrain.Domain;
            const float far = 60000;
            // one big quad per side of the land, so there is no seam at the coast
            b.Quad(M.water, new Vector3(d.x0 - far, 0, far), new Vector3(d.x1 + far, 0, far), new Vector3(d.x1 + far, 0, -d.z1 - far), new Vector3(d.x0 - far, 0, -d.z1 - far), new Vector2(0.01f, 0.01f));
            var sea = b.Attach("Sea", parent, false);
            sea.isStatic = true;
        }

        // ---------------------------------------------------------------------------------------- roads
        static void Roads(Transform parent, WorldMaterials M)
        {
            var root = new GameObject("Roads").transform; root.SetParent(parent, false);
            const float lift = 0.22f;   // clear of the terrain's simplified distant mesh
            foreach (var L in RoadNet.Lines())
            {
                var mat = L.track ? M.gravel : M.asphalt;
                var n = L.x.Length;
                for (int start = 0; start < n - 1; start += 200)
                {
                    var end = Math.Min(n - 1, start + 200);
                    var b = new MeshBuilder(U(L.x[start], 0, L.z[start]));
                    for (int i = start; i < end; i++)
                    {
                        var (aL, aR) = Edge(L, i, L.hw, lift);
                        var (bL, bR) = Edge(L, i + 1, L.hw, lift);
                        b.Quad(mat, aR, bR, bL, aL, new Vector2(0.25f, 0.25f));
                        // dashed centre line on marked roads: 3 m dashes every 9 m
                        if (L.marked[i] > 0.5 && (L.s[i] % 9) < 3)
                        {
                            var (cL, cR) = Edge(L, i, 0.06, lift + 0.01f);
                            var (dL, dR) = Edge(L, i + 1, 0.06, lift + 0.01f);
                            b.Quad(M.marking, cR, dR, dL, cL);
                        }
                    }
                    b.Attach($"{L.name} {start}", root, false);
                }
            }
        }

        static (Vector3 left, Vector3 right) Edge(RoadLine L, int i, double half, float lift)
        {
            int a = Math.Max(0, i - 1), c = Math.Min(L.x.Length - 1, i + 1);
            double tx = L.x[c] - L.x[a], tz = L.z[c] - L.z[a], l = Js.Hypot(tx, tz); if (l == 0) l = 1;
            double nx = -tz / l, nz = tx / l;   // to the left of travel in the sim's x, z
            var h = RoadNet.ProfileAt(new RoadHit { road = L, i = Math.Min(i, L.x.Length - 2), t = i >= L.x.Length - 1 ? 1 : 0 }) + lift;
            return (U(L.x[i] + nx * half, h, L.z[i] + nz * half), U(L.x[i] - nx * half, h, L.z[i] - nz * half));
        }

        static void Bridge(Transform parent, WorldMaterials M)
        {
            var br = Places.BRIDGE;
            var b = new MeshBuilder();
            var deck = RoadNet.BridgeDeck();
            var rot = Quaternion.Euler(0, (float)(Math.Atan2(Math.Sin(br.rot), -Math.Cos(br.rot)) * RAD), 0);   // along the bridge in Unity space
            var c = U(br.x, deck - 0.6, br.z);
            b.Box(M.stone, c, new Vector3((float)br.width / 2 + 0.6f, 0.6f, (float)br.len / 2), rot, true);
            foreach (var side in new[] { -1, 1 })
                b.Box(M.stone, c + rot * new Vector3(side * ((float)br.width / 2 + 0.4f), 1.1f, 0), new Vector3(0.25f, 0.55f, (float)br.len / 2), rot);
            // piers down into the glen
            foreach (var f in new[] { -0.25f, 0.25f })
            {
                var p = c + rot * new Vector3(0, 0, f * (float)br.len);
                var gx = p.x; var gz = -p.z;
                var ground = (float)WorldMap.TerrainHeight(gx, gz);
                var top = c.y - 0.6f; var mid = (top + ground) / 2 - 1;
                b.Box(M.stone, new Vector3(p.x, mid, p.z), new Vector3(1.6f, (top - ground) / 2 + 1, 1.2f), rot);
            }
            b.Attach("Glen Ardoch Bridge", parent);
        }

        // ----------------------------------------------------------------------------------------- pads
        static void Pads(Transform parent, WorldMaterials M)
        {
            var root = new GameObject("Landing sites").transform; root.SetParent(parent, false);
            foreach (var s in WorldMap.SITES)
            {
                if (s.radius <= 0 || s.deckHeight.HasValue || s.kind == SiteKind.Field || s.kind == SiteKind.Landmark || s.kind == SiteKind.Helideck) continue;
                var e = WorldMap.SiteElevation(s);
                var b = new MeshBuilder(U(s.x, e, s.z));
                var r = (float)s.radius;
                var c = U(s.x, e, s.z);
                Disc(b, M.concrete, c + Vector3.up * 0.03f, r * 1.35f, 40);
                Ring(b, M.padPaint, c + Vector3.up * 0.045f, r * 0.92f, r * 1.0f, 48);
                if (s.kind == SiteKind.Heliport || s.kind == SiteKind.Hospital)
                {
                    // hospital pads get a red-cross style white H on green? keep the CAP H
                    var hs = r * 0.3f;
                    b.Box(M.padPaint, c + new Vector3(-hs * 0.55f, 0.05f, 0), new Vector3(hs * 0.12f, 0.005f, hs), Quaternion.identity);
                    b.Box(M.padPaint, c + new Vector3(hs * 0.55f, 0.05f, 0), new Vector3(hs * 0.12f, 0.005f, hs), Quaternion.identity);
                    b.Box(M.padPaint, c + new Vector3(0, 0.05f, 0), new Vector3(hs * 0.55f, 0.005f, hs * 0.12f), Quaternion.identity);
                }
                else
                {
                    // training spots: a painted square
                    var q = r * 0.75f;
                    foreach (var (dx, dz, w, d) in new[] { (0f, q, q, 0.15f), (0f, -q, q, 0.15f), (q, 0f, 0.15f, q), (-q, 0f, 0.15f, q) })
                        b.Box(M.padPaint, c + new Vector3(dx, 0.05f, dz), new Vector3(w, 0.005f, d), Quaternion.identity);
                }
                b.Attach(s.name, root, false);
            }
        }

        static void Disc(MeshBuilder b, Material m, Vector3 c, float r, int seg)
        {
            for (int k = 0; k < seg; k++)
            {
                float a0 = k * Mathf.PI * 2 / seg, a1 = (k + 1) * Mathf.PI * 2 / seg;
                b.Tri(m, c, c + new Vector3(Mathf.Cos(a1) * r, 0, Mathf.Sin(a1) * r), c + new Vector3(Mathf.Cos(a0) * r, 0, Mathf.Sin(a0) * r));
            }
        }

        static void Ring(MeshBuilder b, Material m, Vector3 c, float r0, float r1, int seg)
        {
            for (int k = 0; k < seg; k++)
            {
                float a0 = k * Mathf.PI * 2 / seg, a1 = (k + 1) * Mathf.PI * 2 / seg;
                Vector3 P(float a, float r) => c + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                b.Quad(m, P(a0, r0), P(a0, r1), P(a1, r1), P(a1, r0));   // faces up by default
            }
        }

        static void Paved(Transform parent, WorldMaterials M)
        {
            var b = new MeshBuilder();
            foreach (var p in Places.PAVED)
            {
                var mat = p.color == PavedColor.Concrete ? M.concrete : p.color == PavedColor.Gravel ? M.gravel : M.asphalt;
                var h = (float)WorldMap.TerrainHeight(p.x, p.z) + 0.1f;
                var rot = Yaw(p.rot);
                var c = U(p.x, h, p.z);
                Vector3 P(float lx, float lz) => c + rot * new Vector3(lx, 0, -lz);   // footprint local z runs with sim z
                float hw = (float)p.w / 2, hd = (float)p.d / 2;
                b.Quad(mat, P(-hw, hd), P(hw, hd), P(hw, -hd), P(-hw, -hd), new Vector2(0.25f, 0.25f));
                if (p.bays)
                    for (float lx = -hw + 1; lx < hw; lx += 2.7f)
                        foreach (var lz in new[] { -hd + 2.6f, hd - 2.6f })
                            b.Box(M.marking, P(lx, lz) + Vector3.up * 0.01f, new Vector3(0.06f, 0.005f, 2.4f), rot);
            }
            b.Attach("Car parks and yards", parent, false);
        }

        // ----------------------------------------------------------------------------------- structures
        static string LandmarkKey(Structure s) =>
            $"{(s.kind == StructureKind.Building ? s.style.ToString().ToLowerInvariant() : s.kind.ToString().ToLowerInvariant())}@{Js.Round(s.x)},{Js.Round(s.z)}";

        static void Structures(Transform parent, WorldMaterials M, GameObject landmarks)
        {
            var root = new GameObject("Structures").transform; root.SetParent(parent, false);
            var parts = new Dictionary<string, Transform>();
            if (landmarks != null) foreach (Transform c in landmarks.transform) parts[c.name] = c;
            // procedural buildings are gathered into 1 km cells so each cell is one draw per material
            var cells = new Dictionary<(int, int), MeshBuilder>();
            MeshBuilder Cell(double x, double z)
            {
                var k = ((int)Math.Floor(x / 1000), (int)Math.Floor(z / 1000));
                if (!cells.TryGetValue(k, out var b)) cells[k] = b = new MeshBuilder(U(k.Item1 * 1000 + 500, 0, k.Item2 * 1000 + 500));
                return b;
            }
            int placed = 0;
            foreach (var st in WorldMap.STRUCTURES)
            {
                var ground = WorldMap.StructureBase(st, WorldMap.TerrainHeight(st.x, st.z));
                if (parts.TryGetValue(LandmarkKey(st), out var part))
                {
                    var go = UnityEngine.Object.Instantiate(part.gameObject, root);
                    go.name = part.name;
                    var g = st.kind == StructureKind.Platform ? st.@base : ground;
                    // the model's parts face the game's way after a half turn (glTFast mirrors x), then the structure's own turn
                    go.transform.SetPositionAndRotation(U(st.x, g, st.z), Yaw(st.rot) * Quaternion.Euler(0, 180, 0));
                    go.transform.localScale = Vector3.one;
                    placed++;
                    continue;
                }
                Footprint(Cell(st.x, st.z), M, st, ground);
            }
            foreach (var kv in cells) kv.Value.Attach($"Buildings {kv.Key.Item1},{kv.Key.Item2}", root);
            Debug.Log($"Kinloch structures: {placed} Blender landmarks, {WorldMap.STRUCTURES.Count - placed} built from footprints");
        }

        static int Hash(double x, double z) { unchecked { var h = (int)(x * 73856093) ^ (int)(z * 19349663); return h & 0x7fffffff; } }

        /// <summary>A structure built from its footprint: walls from a little below the ground, and a roof for its kind.</summary>
        static void Footprint(MeshBuilder b, WorldMaterials M, Structure st, double ground)
        {
            var rot = Yaw(st.rot);
            float w = (float)st.w / 2, d = (float)st.d / 2, h = (float)st.h;
            var baseY = (float)ground;
            var c = U(st.x, baseY, st.z);
            const float sink = 1.5f;   // walls start below the ground so slopes never show a gap
            switch (st.kind)
            {
                case StructureKind.House:
                case StructureKind.Barn:
                    {
                        var wall = st.kind == StructureKind.Barn ? M.redMetal : (Hash(st.x, st.z) % 3 == 0 ? M.harlCream : M.harl);
                        var roofM = st.kind == StructureKind.Barn ? M.metalRoof : M.slate;
                        var eave = h * 0.62f;
                        b.Box(wall, c + Vector3.up * (eave - sink) / 2, new Vector3(w, (eave + sink) / 2, d), rot);
                        // gable roof along the long side
                        var alongX = w >= d;
                        float span = alongX ? d : w, len = alongX ? w : d, ridge = h - eave;
                        Vector3 L(float a, float s, float y) => c + rot * (alongX ? new Vector3(a, y, s) : new Vector3(s, y, a));
                        float o = 0.35f;   // overhang
                        b.Quad(roofM, L(-len - o, -span - o, eave - 0.1f), L(-len - o, 0, h), L(len + o, 0, h), L(len + o, -span - o, eave - 0.1f));
                        b.Quad(roofM, L(len + o, span + o, eave - 0.1f), L(len + o, 0, h), L(-len - o, 0, h), L(-len - o, span + o, eave - 0.1f));
                        // gable ends face out along the ridge
                        var outA = L(-1, 0, 0) - L(0, 0, 0); var outB = -outA;
                        b.Tri(wall, L(-len, -span, eave), L(-len, span, eave), L(-len, 0, h - 0.05f), outA);
                        b.Tri(wall, L(len, span, eave), L(len, -span, eave), L(len, 0, h - 0.05f), outB);
                        // a door and a few windows on the front
                        Windows(b, M, c, rot, w, d, eave, 1);
                        break;
                    }
                case StructureKind.Caravan:
                    b.Box(M.white, c + Vector3.up * (h / 2 + 0.2f), new Vector3(w, h / 2, d), rot, true);
                    break;
                case StructureKind.Pier:
                    b.Box(M.stone, c + Vector3.up * (h / 2), new Vector3(w, h / 2, d), rot);
                    break;
                case StructureKind.Turbine:
                    {
                        b.Box(M.white, c + Vector3.up * (h / 2), new Vector3(1.6f, h / 2, 1.6f), rot);
                        b.Box(M.white, c + Vector3.up * (h + 1.5f), new Vector3(1.8f, 1.6f, 5f), rot);
                        break;
                    }
                case StructureKind.Tower:
                    b.Box(M.concrete, c + Vector3.up * (h - sink) / 2, new Vector3(w, (h + sink) / 2, d), rot);
                    break;
                default:
                    {
                        // buildings with a style, hospitals and hangars not in the library: a block with a flat roof
                        var wall = st.style == BuildingStyle.Church || st.style == BuildingStyle.Lifeboat ? M.stone
                            : st.style == BuildingStyle.Warehouse || st.style == BuildingStyle.Shed || st.style == BuildingStyle.Crusher || st.kind == StructureKind.Hangar ? M.metalRoof
                            : st.style == BuildingStyle.Firestation ? M.redMetal : M.harl;
                        b.Box(wall, c + Vector3.up * (h - sink) / 2, new Vector3(w, (h + sink) / 2, d), rot);
                        b.Box(M.slate, c + Vector3.up * (h + 0.15f), new Vector3(w + 0.2f, 0.15f, d + 0.2f), rot);
                        Windows(b, M, c, rot, w, d, h, Math.Max(1, (int)(h / 3.2f)));
                        break;
                    }
            }
        }

        static void Windows(MeshBuilder b, WorldMaterials M, Vector3 c, Quaternion rot, float w, float d, float top, int floors)
        {
            for (int f = 0; f < floors; f++)
            {
                var y = 1.3f + f * 3.2f;
                if (y + 1 > top) break;
                foreach (var side in new[] { -1, 1 })
                    for (float x = -w + 1.4f; x <= w - 1.4f; x += 2.8f)
                        b.Box(M.glassDark, c + rot * new Vector3(x, y, side * (d + 0.02f)), new Vector3(0.5f, 0.6f, 0.03f), rot);
            }
        }
    }
}
