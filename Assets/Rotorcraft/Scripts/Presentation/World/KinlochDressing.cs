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
        public Material asphalt, apron, gravel, marking, concrete, padPaint, padYellow, padRed, padGreen, orange, harl, harlCream, slate, metalRoof, redMetal, stone, timber, white, glassDark, water, steel;

        static Material Lit(string name, Color c, float smooth = 0.1f, float metal = 0)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            return m;
        }

        public static WorldMaterials Make() => new WorldMaterials
        {
            asphalt = Lit("Asphalt", new Color(0.13f, 0.13f, 0.14f), 0.15f),
            apron = Lit("Apron", new Color(0.24f, 0.24f, 0.25f), 0.15f),
            gravel = Lit("Gravel track", new Color(0.42f, 0.39f, 0.33f), 0.05f),
            marking = Lit("Road marking", new Color(0.85f, 0.85f, 0.8f), 0.2f),
            concrete = Lit("Concrete", new Color(0.55f, 0.55f, 0.53f), 0.12f),
            padPaint = Lit("Pad paint", new Color(0.92f, 0.92f, 0.88f), 0.25f),
            padYellow = Lit("Pad yellow", new Color(0.92f, 0.72f, 0.15f), 0.25f),
            padRed = Lit("Pad red", new Color(0.85f, 0.12f, 0.1f), 0.25f),
            padGreen = Lit("Pad green", new Color(0.12f, 0.36f, 0.2f), 0.2f),
            orange = Lit("Marker orange", new Color(0.95f, 0.42f, 0.1f), 0.3f),
            harl = Lit("Harl white", new Color(0.86f, 0.85f, 0.81f), 0.08f),
            harlCream = Lit("Harl cream", new Color(0.82f, 0.77f, 0.66f), 0.08f),
            slate = Lit("Slate", new Color(0.22f, 0.23f, 0.26f), 0.35f),
            metalRoof = Lit("Metal roof", new Color(0.36f, 0.40f, 0.43f), 0.4f, 0.3f),
            redMetal = Lit("Red cladding", new Color(0.45f, 0.14f, 0.11f), 0.3f, 0.2f),
            stone = Lit("Stone", new Color(0.48f, 0.46f, 0.42f), 0.05f),
            timber = Lit("Timber", new Color(0.30f, 0.22f, 0.15f), 0.1f),
            white = Lit("White", new Color(0.9f, 0.9f, 0.88f), 0.4f),
            glassDark = Lit("Window", new Color(0.05f, 0.07f, 0.09f), 0.85f),
            water = SeaSurface.Material,
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
            sea.AddComponent<SeaSurface>();
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
            Heliport(root, M);
            foreach (var s in WorldMap.SITES)
            {
                if (s.radius <= 0 || s.kind == SiteKind.Landmark) continue;
                // decks and roofs sit on the top of their model (a hospital roof's membrane stands 6 cm proud)
                var e = s.deckHeight ?? WorldMap.SiteElevation(s);
                var onRoof = s.kind == SiteKind.Hospital && s.deckHeight.HasValue;
                if (onRoof) e += 0.07;
                var paint = new MeshBuilder(U(s.x, e, s.z));
                var props = new MeshBuilder(U(s.x, e, s.z));
                switch (s.kind)
                {
                    case SiteKind.Heliport:
                    case SiteKind.Hospital:
                        PadCircle(paint, M, s, e, onRoof);
                        break;
                    case SiteKind.Helideck:
                        HelideckPaint(paint, M, s, e);
                        break;
                    case SiteKind.Pad:
                        PaintedSquare(paint, M, s, e);
                        break;
                    case SiteKind.Field:
                        FieldMarkers(props, M, s);
                        break;
                }
                paint.Attach(s.name, root, false);
                props.Attach(s.name + " markers", root);
            }
        }

        // how far each layer of a pad lies above the ground: little, because the aircraft stands on the ground under them
        const double ApronLift = 0.02, PadLift = 0.05, PaintLift = 0.07;

        /// <summary>A rectangle lying flat, w by d, turned by rot in the sim's sense, with its centre at sim x, y, z.</summary>
        static void FlatRect(MeshBuilder b, Material m, double x, double y, double z, double w, double d, double rot = 0)
        {
            var r = Yaw(rot);
            var c = U(x, y, z);
            Vector3 P(float lx, float lz) => c + r * new Vector3(lx, 0, -lz);
            float hw = (float)w / 2, hd = (float)d / 2;
            b.Quad(m, P(-hw, hd), P(hw, hd), P(hw, -hd), P(-hw, -hd), new Vector2(0.25f, 0.25f));
        }

        /// <summary>The letter H, in bars sized from the pad's radius.</summary>
        static void LetterH(MeshBuilder b, Material m, double x, double y, double z, double r)
        {
            var k = r * 0.5;
            FlatRect(b, m, x - k * 0.45, y, z, k * 0.18, k * 1.1);
            FlatRect(b, m, x + k * 0.45, y, z, k * 0.18, k * 1.1);
            FlatRect(b, m, x, y, z, k * 0.9, k * 0.16);
        }

        /// <summary>Heliport and hospital pads: a concrete surround, a tarmac circle, a coloured ring round it and an H.</summary>
        static void PadCircle(MeshBuilder b, WorldMaterials M, Site s, double e, bool onRoof)
        {
            var r = (float)s.radius;
            var c = U(s.x, e + PadLift, s.z);
            var hospital = s.kind == SiteKind.Hospital;
            var hue = hospital ? M.padRed : M.padYellow;
            Ring(b, M.concrete, c, r * 1.25f, r * (onRoof ? 1.45f : 1.9f), 40);
            Disc(b, M.asphalt, c, r * 1.25f, 40);
            Ring(b, hue, U(s.x, e + PaintLift, s.z), r * 0.95f, r * 1.03f, 48);
            LetterH(b, hospital ? M.padRed : M.padPaint, s.x, e + PaintLift, s.z, r);
            if (hospital && !onRoof) FlatRect(b, M.padPaint, s.x + r * 1.5, e + PaintLift, s.z, 2.5, r * 2);
        }

        /// <summary>A helideck: its green plate is part of the platform model, so only the paint is added.</summary>
        static void HelideckPaint(MeshBuilder b, WorldMaterials M, Site s, double e)
        {
            var r = (float)s.radius;
            var y = e + PaintLift;
            Ring(b, M.padPaint, U(s.x, y, s.z), r * 0.95f, r * 1.03f, 48);
            Ring(b, M.padYellow, U(s.x, y, s.z), r * 0.5f, r * 0.6f, 40);
            LetterH(b, M.padPaint, s.x, y, s.z, r);
        }

        /// <summary>A hover square: yellow lines round a slab (a quay is its own slab), with a white spot at its centre.</summary>
        static void PaintedSquare(MeshBuilder b, WorldMaterials M, Site s, double e)
        {
            var r = s.radius;
            var y = e + PaintLift;
            if (!s.deckHeight.HasValue) FlatRect(b, M.concrete, s.x, e + PadLift, s.z, r * 2.5, r * 2.5);
            foreach (var (dx, dz, w, d) in new[] { (0.0, -r, 2 * r + 0.6, 0.6), (0.0, r, 2 * r + 0.6, 0.6), (-r, 0.0, 0.6, 2 * r), (r, 0.0, 0.6, 2 * r) })
                FlatRect(b, M.padYellow, s.x + dx, y, s.z + dz, w, d);
            FlatRect(b, M.padPaint, s.x, y, s.z, 1, 1);
        }

        /// <summary>A landing field: four orange markers on the ground at its edge, and a windsock.</summary>
        static void FieldMarkers(MeshBuilder b, WorldMaterials M, Site s)
        {
            for (int i = 0; i < 4; i++)
            {
                var a = i * Math.PI / 2 + Math.PI / 4;
                double x = s.x + Math.Cos(a) * s.radius, z = s.z + Math.Sin(a) * s.radius;
                b.Box(M.orange, U(x, WorldMap.TerrainHeight(x, z) + 0.35, z), Vector3.one * 0.35f, Quaternion.identity);
            }
            Windsock(b, M, s.x + 4, WorldMap.TerrainHeight(s.x + 4, s.z), s.z, 4.4f);
        }

        /// <summary>A pole with a banded sock streaming from its top (it does not follow the wind).</summary>
        static void Windsock(MeshBuilder b, WorldMaterials M, double x, double ground, double z, float height)
        {
            b.Box(M.steel, U(x, ground + height / 2, z), new Vector3(0.07f, height / 2, 0.07f), Quaternion.identity);
            var root = U(x, ground + height - 0.2, z);
            const int Bands = 4;
            for (int i = 0; i < Bands; i++)
            {
                float t0 = (float)i / Bands, t1 = (float)(i + 1) / Bands;
                Sleeve(b, i % 2 == 0 ? M.orange : M.padPaint, root + new Vector3(t0 * 2.2f, -t0 * 0.3f, 0), root + new Vector3(t1 * 2.2f, -t1 * 0.3f, 0), Mathf.Lerp(0.32f, 0.14f, t0), Mathf.Lerp(0.32f, 0.14f, t1));
            }
        }

        /// <summary>One length of a tapered tube along Unity's x axis, open at both ends.</summary>
        static void Sleeve(MeshBuilder b, Material m, Vector3 p0, Vector3 p1, float r0, float r1)
        {
            const int Sides = 8;
            for (int k = 0; k < Sides; k++)
            {
                float a0 = k * Mathf.PI * 2 / Sides, a1 = (k + 1) * Mathf.PI * 2 / Sides;
                var d0 = new Vector3(0, Mathf.Cos(a0), Mathf.Sin(a0)); var d1 = new Vector3(0, Mathf.Cos(a1), Mathf.Sin(a1));
                b.Quad(m, p0 + d0 * r0, p1 + d0 * r1, p1 + d1 * r1, p0 + d1 * r0, default, (d0 + d1).normalized);
            }
        }

        /// <summary>A round tower, tapering from p0 up to p1, open at both ends.</summary>
        static void Tower(MeshBuilder b, Material m, Vector3 p0, Vector3 p1, float r0, float r1)
        {
            const int Sides = 14;
            for (int k = 0; k < Sides; k++)
            {
                float a0 = k * Mathf.PI * 2 / Sides, a1 = (k + 1) * Mathf.PI * 2 / Sides;
                var d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)); var d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                b.Quad(m, p0 + d0 * r0, p1 + d0 * r1, p1 + d1 * r1, p0 + d1 * r0, default, (d0 + d1).normalized);
            }
        }

        /// <summary>Kinloch Heliport: the tarmac apron, the taxiway and its centre line, floodlight masts, a windsock and the fuel bowser.</summary>
        static void Heliport(Transform root, WorldMaterials M)
        {
            var b0 = WorldMap.SITES[0];
            var e = WorldMap.SiteElevation(b0);
            var ground = new MeshBuilder(U(b0.x, e, b0.z));
            Paved a = Places.HELIPORT_APRON, t = Places.HELIPORT_TAXIWAY;
            FlatRect(ground, M.apron, a.x, e + ApronLift, a.z, a.w, a.d, a.rot);
            FlatRect(ground, M.apron, t.x, e + ApronLift, t.z, t.w, t.d, t.rot);
            double tc = Math.Cos(t.rot), ts = Math.Sin(t.rot);
            for (int i = -5; i <= 5; i++)
                FlatRect(ground, M.padYellow, t.x + tc * i * 22, e + ApronLift + 0.01, t.z - ts * i * 22, 12, 0.3, t.rot);
            ground.Attach("Heliport apron", root, false);

            var props = new MeshBuilder(U(b0.x, e, b0.z));
            foreach (var (dx, dz) in new[] { (-50, -70), (90, -70), (90, 30), (-50, 30) })
            {
                props.Box(M.steel, U(b0.x + dx, e + 5, b0.z + dz), new Vector3(0.125f, 5, 0.125f), Quaternion.identity);
                props.Box(M.slate, U(b0.x + dx, e + 10, b0.z + dz), new Vector3(0.6f, 0.2f, 0.3f), Quaternion.identity);
            }
            Windsock(props, M, b0.x + 40, e, b0.z + 40, 6);
            props.Box(M.padYellow, U(b0.x + 70, e + 1.4, b0.z - 20), new Vector3(1.2f, 1.3f, 3.5f), Quaternion.identity);
            props.Box(M.slate, U(b0.x + 70, e + 0.45, b0.z - 20), new Vector3(1.25f, 0.45f, 3.6f), Quaternion.identity);
            props.Attach("Heliport masts and bowser", root);
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
            // houses and farm sheds are modelled (HouseModels), coloured per vertex, in the same cells
            var homes = new Dictionary<(int, int), ColourMesh>();
            ColourMesh Home(double x, double z)
            {
                var k = ((int)Math.Floor(x / 1000), (int)Math.Floor(z / 1000));
                if (!homes.TryGetValue(k, out var b)) homes[k] = b = new ColourMesh(new P3(k.Item1 * 1000 + 500, 0, k.Item2 * 1000 + 500));
                return b;
            }
            lock (HouseModels.Drives) HouseModels.Drives.Clear();
            int placed = 0, modelled = 0;
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
                if (st.kind == StructureKind.House) { HouseModels.House(Home(st.x, st.z), st, ground, SimMath.Rng(Hash(st.x, st.z) + 4)); modelled++; continue; }
                if (st.kind == StructureKind.Barn) { HouseModels.Barn(Home(st.x, st.z), st, ground); modelled++; continue; }
                Footprint(Cell(st.x, st.z), M, st, ground);
            }
            foreach (var kv in cells) kv.Value.Attach($"Buildings {kv.Key.Item1},{kv.Key.Item2}", root);
            foreach (var kv in homes) kv.Value.Attach($"Houses {kv.Key.Item1},{kv.Key.Item2}", root);
            TurbineRotors.Add(root, M.white);
            Debug.Log($"Kinloch structures: {placed} Blender landmarks, {modelled} houses and farm sheds modelled, {WorldMap.STRUCTURES.Count - placed - modelled} built from footprints, {HouseModels.Drives.Count} cars on drives");
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
                        // a tapering tower on a concrete foot and the nacelle behind the rotor, turned the way the rotor faces (TurbineRotors)
                        var face = Vector3.forward;
                        foreach (var t in Places.TURBINES) if (Js.Hypot(t.x - st.x, t.z - st.z) < 1) face = TurbineRotors.Facing(t.yaw);
                        Tower(b, M.white, c + Vector3.down, c + Vector3.up * h, 2.2f, 1.4f);
                        Tower(b, M.concrete, c + Vector3.down, c + Vector3.up * 0.6f, 4f, 4f);
                        b.Box(M.white, c + Vector3.up * (h + TurbineRotors.HubAboveTower) - face * 1.8f, new Vector3(1.7f, 1.8f, 5f), Quaternion.LookRotation(face));
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
