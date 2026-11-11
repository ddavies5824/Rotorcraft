// Field boundaries in 3D (port of render/fields.ts): hawthorn hedges on the low ground, drystone walls on the higher
// pasture and post-and-wire fences, along the edges of the fields the terrain paints (FieldLayout) and beside the roads
// through the farmland, with a gateway in every field edge. Built in 512 m tiles around the camera by SceneryStreaming;
// the painted field edges carry on beyond. The trees growing out of the hedges are KinlochTrees' hedgerow trees, which
// stand only where this says there is a hedge.
using System;
using System.Collections;
using System.Collections.Generic;
using Rotorcraft.Sim;
using Rotorcraft.World;
using static Rotorcraft.Presentation.FieldLayout;
using static Rotorcraft.Presentation.RGBA;

namespace Rotorcraft.Presentation
{
    public enum Boundary { None, Hedge, Wall, Fence }

    public static class FieldBoundaries
    {
        public const int TILE = 512;
        const double STEP = 2.5;
        const double R00 = 0.94, R01 = 0.34, R10 = -0.34, R11 = 0.94, DET = R00 * R11 - R01 * R10;

        static (double x, double z) FromQ(double qx, double qy) => ((R11 * qx - R01 * qy) / DET, (-R10 * qx + R00 * qy) / DET);

        static float Fract(float x) => x - (float)Math.Floor(x);
        /// <summary>The terrain shader's hash12 (fract-based, no sine), in 32-bit floats as the browser game evaluates it.</summary>
        public static double Hash12(double px, double py)
        {
            const float k = 0.1031f;
            float x = Fract((float)px * k), y = Fract((float)py * k), z = x;
            float d = x * (y + 33.33f) + y * (z + 33.33f) + z * (x + 33.33f);
            x += d; y += d; z += d;
            return Fract((x + y) * z);
        }

        /// <summary>What runs along an edge: walls on the higher ground, hedges below, some fences, a few left open.</summary>
        public static Boundary KindOf(double seed, double ground, bool painted)
        {
            var u = Hash12(seed * 0.37 + 11.3, seed * 0.11 + 7.9);
            if (ground > 105) return u < 0.68 ? Boundary.Wall : u < 0.88 ? Boundary.Fence : Boundary.None;
            if (painted) return u < 0.7 ? Boundary.Hedge : u < 0.84 ? Boundary.Wall : Boundary.Fence;
            return u < 0.25 ? Boundary.Fence : u < 0.35 ? Boundary.Hedge : Boundary.None;
        }

        // the two edges each field draws: its left (shared with the field to the west) and its bottom
        static double LeftSeed(int i, int row) => i * 7919.0 + row * 104729.0 + 1;
        static double BottomSeed(int i, int row) => i * 7919.0 + row * 104729.0 + 2;
        /// <summary>Ground height the kind is chosen by: the middle of the field's bottom edge.</summary>
        static double FieldGround(int i, int row)
        {
            var off = RowShift(row);
            double left = i * CELL_X - off, bot = row * CELL_Z;
            var (ax, az) = FromQ(left, bot); var (cx, cz) = FromQ(left + CELL_X, bot);
            return WorldMap.TerrainHeight((ax + cx) / 2, (az + cz) / 2);
        }
        static Boundary LeftKind(int i, int row) => KindOf(LeftSeed(i, row), FieldGround(i, row), HedgeOn(i, row) || HedgeOn(i - 1, row));
        static Boundary BottomKind(int i, int row) => KindOf(BottomSeed(i, row), FieldGround(i, row), HedgeOn(i, row));

        /// <summary>The field edge nearest (x, z): how far it is (m) and what runs along it (before gateways and farmland checks).</summary>
        public static (double dist, Boundary kind) NearestEdge(double x, double z)
        {
            var (qx, qy) = ToQ(x, z);
            var row = (int)Math.Floor(qy / CELL_Z);
            var off = RowShift(row);
            var i = (int)Math.Floor((qx + off) / CELL_X);
            double u = qx + off - i * CELL_X, v = qy - row * CELL_Z;
            var best = (dist: u, kind: (Func<Boundary>)(() => LeftKind(i, row)));
            if (CELL_X - u < best.dist) best = (CELL_X - u, () => LeftKind(i + 1, row));
            if (v < best.dist) best = (v, () => BottomKind(i, row));
            if (CELL_Z - v < best.dist)
            {
                var i2 = (int)Math.Floor((qx + RowShift(row + 1)) / CELL_X);
                best = (CELL_Z - v, () => BottomKind(i2, row + 1));
            }
            return (best.dist, best.kind());
        }

        struct Pt { public double x, z, y; public bool ok; }

        /// <summary>
        /// Build one tile's boundaries into m, a slice of work per step (one field edge or one roadside run) so it can be
        /// spread over frames where there is only one thread.
        /// </summary>
        public static IEnumerator TileJob(int tx, int tz, ColourMesh m)
        {
            double x0 = tx * TILE, z0 = tz * TILE, x1 = x0 + TILE, z1 = z0 + TILE;
            bool InTile(double x, double z) => x >= x0 && x < x1 && z >= z0 && z < z1;
            var near = new List<Structure>();
            foreach (var s in WorldMap.STRUCTURES) if (s.x > x0 - 60 && s.x < x1 + 60 && s.z > z0 - 60 && s.z < z1 + 60) near.Add(s);
            bool Blocked(double x, double z)
            {
                foreach (var s in near) { var r = Math.Max(s.w, s.d) / 2 + 6; if (Math.Abs(x - s.x) < r && Math.Abs(z - s.z) < r) return true; }
                return false;
            }
            bool InTown(double x, double z)
            {
                foreach (var t in Places.SETTLEMENTS) if (Js.Hypot(x - t.x, z - t.z) < t.r * 0.85) return true;
                return false;
            }
            var dense = new List<(double x, double z, double f)>();
            var pts = new List<Pt>();
            var seg = new List<Pt>();

            // sample a run every STEP metres; points outside the tile are kept only next to one inside, so runs join up across
            // tile borders without being drawn twice
            void Run(List<(double x, double z)> poly, Boundary kind, double seed, bool roadside)
            {
                if (kind == Boundary.None || poly.Count < 2) return;
                double len = 0;
                for (int i = 0; i < poly.Count - 1; i++) len += Js.Hypot(poly[i + 1].x - poly[i].x, poly[i + 1].z - poly[i].z);
                if (len < 1) return;
                dense.Clear();
                double acc = 0;
                for (int i = 0; i < poly.Count - 1; i++)
                {
                    var (ax, az) = poly[i]; var (bx, bz) = poly[i + 1]; var l = Js.Hypot(bx - ax, bz - az);
                    var n = Math.Max(1, (int)Math.Ceiling(l / STEP));
                    for (int k = 0; k < n; k++) dense.Add((ax + (bx - ax) * k / n, az + (bz - az) * k / n, (acc + l * k / n) / len));
                    acc += l;
                }
                dense.Add((poly[poly.Count - 1].x, poly[poly.Count - 1].z, 1));
                pts.Clear();
                var gate = roadside ? -1 : 0.15 + 0.7 * Hash12(seed, 2.1);   // one gateway per field edge, about 4 m wide
                foreach (var (x, z, f) in dense)
                {
                    var ok = true; double y = 0;
                    if (x < x0 - STEP * 2 || x > x1 + STEP * 2 || z < z0 - STEP * 2 || z > z1 + STEP * 2) ok = false;
                    else if (Math.Abs(f - gate) * len < 2.2) ok = false;
                    else
                    {
                        y = WorldMap.TerrainHeight(x, z);
                        if (y < 1.5 || Blocked(x, z)) ok = false;
                        else
                        {
                            var rd = RoadNet.NearestRoad(x, z);
                            if (rd != null && rd.edge < (roadside ? 2.5 : 3.5)) ok = false;
                            if (ok)
                            {
                                // only through farmland (not on the open hill, the airfield, building plots or in the towns)
                                const double e = 3;
                                double sx = WorldMap.TerrainHeight(x + e, z) - WorldMap.TerrainHeight(x - e, z), sz = WorldMap.TerrainHeight(x, z + e) - WorldMap.TerrainHeight(x, z - e);
                                var slope = Math.Atan(Js.Hypot(sx, sz) / (2 * e));
                                if (KinlochTerrain.FarmMask(x, z, y, slope) < (roadside ? 0.2 : 0.45)) ok = false;
                            }
                            if (ok && roadside && (InTown(x, z) || Rotorcraft.World.Terrain.CoastX(z) - x < 150 || Math.Abs(z - Rotorcraft.World.Terrain.RiverZ(x)) < 25)) ok = false;
                        }
                    }
                    pts.Add(new Pt { x = x, z = z, y = ok ? y : 0, ok = ok });
                }
                var keep = new bool[pts.Count];
                for (int k = 0; k < pts.Count; k++)
                    keep[k] = pts[k].ok && (InTile(pts[k].x, pts[k].z) || (k > 0 && InTile(pts[k - 1].x, pts[k - 1].z)) || (k + 1 < pts.Count && InTile(pts[k + 1].x, pts[k + 1].z)));
                seg.Clear();
                for (int k = 0; k < pts.Count; k++)
                {
                    if (keep[k]) { seg.Add(pts[k]); continue; }
                    Flush(kind, seed);
                }
                Flush(kind, seed);
            }
            void Flush(Boundary kind, double seed)
            {
                if (seg.Count > 1)
                {
                    if (kind == Boundary.Hedge) Hedge(m, seg, seed);
                    else if (kind == Boundary.Wall) Wall(m, seg, seed);
                    else if (kind == Boundary.Fence) Fence(m, seg);
                }
                seg.Clear();
            }

            // ---- field edges overlapping the tile (in the turned field grid)
            var corners = new[] { ToQ(x0, z0), ToQ(x1, z0), ToQ(x0, z1), ToQ(x1, z1) };
            double qy0 = double.MaxValue, qy1 = double.MinValue, qx0 = double.MaxValue, qx1 = double.MinValue;
            foreach (var c in corners) { qx0 = Math.Min(qx0, c.qx); qx1 = Math.Max(qx1, c.qx); qy0 = Math.Min(qy0, c.qy); qy1 = Math.Max(qy1, c.qy); }
            var line = new List<(double x, double z)>();
            for (int row = (int)Math.Floor(qy0 / CELL_Z) - 1; row <= (int)Math.Floor(qy1 / CELL_Z) + 1; row++)
            {
                var off = RowShift(row);
                for (int i = (int)Math.Floor((qx0 + off) / CELL_X) - 1; i <= (int)Math.Floor((qx1 + off) / CELL_X) + 1; i++)
                {
                    double left = i * CELL_X - off, right = left + CELL_X, bot = row * CELL_Z, top = bot + CELL_Z;
                    var a = FromQ(left, bot); var b = FromQ(left, top); var c = FromQ(right, bot);
                    var g = WorldMap.TerrainHeight((a.x + c.x) / 2, (a.z + c.z) / 2);
                    line.Clear(); line.Add(a); line.Add(b);
                    var sv = LeftSeed(i, row);
                    Run(line, KindOf(sv, g, HedgeOn(i, row) || HedgeOn(i - 1, row)), sv, false);
                    yield return null;
                    line.Clear(); line.Add(a); line.Add(c);
                    var sh = BottomSeed(i, row);
                    Run(line, KindOf(sh, g, HedgeOn(i, row)), sh, false);
                    yield return null;
                }
            }

            // ---- roadside hedges and walls through the farmland, a few metres off the carriageway
            foreach (var L in RoadNet.Lines())
            {
                if (L.track) continue;
                foreach (var side in new[] { -1, 1 })
                {
                    var o = L.hw + 3.2;
                    var start = -1;
                    for (int i = 0; i < L.x.Length; i++)
                    {
                        var inside = L.x[i] > x0 - 40 && L.x[i] < x1 + 40 && L.z[i] > z0 - 40 && L.z[i] < z1 + 40 && !L.bridge[i];
                        if (inside && start < 0) start = i;
                        if ((!inside || i == L.x.Length - 1) && start >= 0)
                        {
                            var end = inside ? i : i - 1;
                            // runs of about 60 m: the kind changes now and then, like farm boundaries along a lane
                            for (int a = start; a < end; a += 15)
                            {
                                var b = Math.Min(end, a + 15);
                                line.Clear();
                                for (int k = a; k <= b; k++)
                                {
                                    int k0 = Math.Max(0, k - 1), k1 = Math.Min(L.x.Length - 1, k + 1);
                                    double dx = L.x[k1] - L.x[k0], dz = L.z[k1] - L.z[k0], l = Js.Hypot(dx, dz); if (l == 0) l = 1;
                                    line.Add((L.x[k] - dz / l * o * side, L.z[k] + dx / l * o * side));
                                }
                                var seed = Math.Floor(L.s[a] / 60) * 31 + (side > 0 ? 7 : 3) + L.name.Length * 1013;
                                var g = WorldMap.TerrainHeight(line[0].x, line[0].z);
                                Run(line, KindOf(seed, g, Hash12(seed, 9.1) < 0.6), seed, true);
                                yield return null;
                            }
                            start = -1;
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ shapes
        static (double x, double z) Along(List<Pt> p, int k)
        {
            Pt a = p[Math.Max(0, k - 1)], b = p[Math.Min(p.Count - 1, k + 1)];
            double dx = b.x - a.x, dz = b.z - a.z, l = Js.Hypot(dx, dz); if (l == 0) l = 1;
            return (dx / l, dz / l);
        }

        static readonly double[,] HedgeProfile = { { -1.0, -0.3 }, { -0.95, 0.7 }, { -0.55, 1.0 }, { 0, 1.08 }, { 0.55, 1.0 }, { 0.95, 0.7 }, { 1.0, -0.3 } };
        static readonly double[,] HedgeNormal = { { -1, -0.1 }, { -0.9, 0.4 }, { -0.45, 0.9 }, { 0, 1 }, { 0.45, 0.9 }, { 0.9, 0.4 }, { 1, -0.1 } };

        /// <summary>A hawthorn and blackthorn hedge: a lumpy rounded bank of foliage 1.6 to 2.4 m high, a few stretches turning brown.</summary>
        static void Hedge(ColourMesh m, List<Pt> p, double seed)
        {
            m.Texture(Pattern.Foliage);
            var b0 = m.VertexCount;
            int P = HedgeProfile.GetLength(0);
            for (int k = 0; k < p.Count; k++)
            {
                var (dx, dz) = Along(p, k); double nx = -dz, nz = dx;
                double u = Hash12(seed + k * 0.37, 4.2), v = Hash12(seed * 0.5 + k * 0.73, 8.6);
                double h = 1.6 + 0.7 * (0.5 + 0.5 * Math.Sin(k * 0.9 + seed)) * 0.6 + u * 0.5, w = 0.9 + v * 0.35;
                var shade = 0.82 + 0.3 * u;
                var autumn = Hash12(seed, (int)(k * 0.05)) < 0.25 ? 1 : 0;
                for (int j = 0; j < P; j++)
                {
                    double a = HedgeProfile[j, 0], bb = HedgeProfile[j, 1];
                    var top = j > 0 && j < P - 1;
                    // the browser game's colours, a third brighter: under Unity's lighting they came out nearly black
                    var c = (top ? M((0.16 + autumn * 0.08) * shade * (0.9 + 0.15 * bb), (0.25 - autumn * 0.03) * shade * (0.9 + 0.15 * bb), 0.09 * shade, 0.03) : M(0.1 * shade, 0.15 * shade, 0.06 * shade, 0.02)).Scaled(HedgeLift);
                    double ln = Js.Hypot(HedgeNormal[j, 0], HedgeNormal[j, 1]);
                    m.Vertex(new P3(p[k].x + nx * a * w, p[k].y + bb * h, p[k].z + nz * a * w), new P3(nx * HedgeNormal[j, 0] / ln, HedgeNormal[j, 1] / ln, nz * HedgeNormal[j, 0] / ln), c);
                }
            }
            for (int k = 0; k < p.Count - 1; k++)
                for (int j = 0; j < P - 1; j++)
                {
                    int a = b0 + k * P + j, b = a + P;
                    m.Tri(a, b, a + 1); m.Tri(a + 1, b, b + 1);
                }
            m.ClearTexture();
        }

        /// <summary>How much brighter than the browser game's colours hedges are drawn.</summary>
        public const double HedgeLift = 1.45;

        static readonly double[,] WallProfile = { { -0.4, -0.3 }, { -0.38, 0.0 }, { -0.27, 1.1 }, { -0.2, 1.33 }, { 0.2, 1.33 }, { 0.27, 1.1 }, { 0.38, 0.0 }, { 0.4, -0.3 } };
        static readonly double[,] WallNormal = { { -1, 0 }, { -1, 0.1 }, { -0.9, 0.3 }, { -0.3, 1 }, { 0.3, 1 }, { 0.9, 0.3 }, { 1, 0.1 }, { 1, 0 } };

        /// <summary>A drystone wall about 1.3 m high, battered, with a ragged row of coping stones along the top.</summary>
        static void Wall(ColourMesh m, List<Pt> p, double seed)
        {
            m.Texture(Pattern.Stone);
            var b0 = m.VertexCount;
            var tone = 0.85 + 0.25 * Hash12(seed, 3.3);
            RGBA stone = M(0.47 * tone, 0.45 * tone, 0.41 * tone, 0.06), cope = M(0.4 * tone, 0.39 * tone, 0.36 * tone, 0.06);
            int P = WallProfile.GetLength(0);
            for (int k = 0; k < p.Count; k++)
            {
                var (dx, dz) = Along(p, k); double nx = -dz, nz = dx;
                var bump = (Hash12(seed + k, 2.7) - 0.5) * 0.12;
                for (int j = 0; j < P; j++)
                {
                    double a = WallProfile[j, 0], b = WallProfile[j, 1], ln = Js.Hypot(WallNormal[j, 0], WallNormal[j, 1]);
                    var y = p[k].y + b + (b > 1.2 ? bump : 0);
                    m.Vertex(new P3(p[k].x + nx * a, y, p[k].z + nz * a), new P3(nx * WallNormal[j, 0] / ln, WallNormal[j, 1] / ln, nz * WallNormal[j, 0] / ln), j >= 3 && j <= 4 ? cope : stone);
                }
            }
            for (int k = 0; k < p.Count - 1; k++)
                for (int j = 0; j < P - 1; j++)
                {
                    int a = b0 + k * P + j, b = a + P;
                    m.Tri(a, b, a + 1); m.Tri(a + 1, b, b + 1);
                }
            m.ClearTexture();
        }

        /// <summary>Post-and-wire fence: timber posts every 2.5 m, three wires.</summary>
        static void Fence(ColourMesh m, List<Pt> p)
        {
            RGBA post = M(0.3, 0.24, 0.18, 0.02), wire = M(0.16, 0.16, 0.17, 0.0);
            for (int k = 0; k < p.Count; k++)
            {
                var (dx, dz) = Along(p, k);
                m.Box(p[k].x, p[k].y + 0.5, p[k].z, 0.1, 1.3, 0.1, post, Math.Atan2(dx, dz));
            }
            foreach (var hy in new[] { 0.45, 0.75, 1.05 })
            {
                var b0 = m.VertexCount;
                for (int k = 0; k < p.Count; k++)
                {
                    m.Vertex(new P3(p[k].x, p[k].y + hy, p[k].z), new P3(0, 1, 0), wire);
                    m.Vertex(new P3(p[k].x, p[k].y + hy + 0.025, p[k].z), new P3(0, 1, 0), wire);
                }
                for (int k = 0; k < p.Count - 1; k++) { var a = b0 + k * 2; m.Tri(a, a + 2, a + 1); m.Tri(a + 1, a + 2, a + 3); }
            }
        }
    }
}
