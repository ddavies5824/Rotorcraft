// Small 3D detail on the ground close to the camera (port of render/clutter.ts), so the land does not look like a
// painted floor when hovering or landing: grass tufts in the pastures, tussocks and rushes on rough grazing and by the
// river, heather clumps and moor-grass on the hill, stones on the plough and on steep ground, gorse on the banks,
// marram grass in the dunes. What grows where follows the land cover the terrain paints (the field layout is shared
// exactly; the moorland and rough patches use the port's own noise, as the terrain does). Built in 64 m tiles around
// the camera by SceneryStreaming.
using System;
using System.Collections;
using System.Collections.Generic;
using Rotorcraft.Sim;
using Rotorcraft.World;
using static Rotorcraft.Presentation.RGBA;
using static Rotorcraft.Sim.SimMath;

namespace Rotorcraft.Presentation
{
    public static class GroundClutter
    {
        public const int TILE = 64;

        public enum Kind { None, Tuft, Long, Rush, Tussock, Heather, Moorgrass, Stone, Boulder, Gorse, Marram }

        /// <summary>Scatter clutter over one tile; spacing is the sampling grid (m), smaller is denser.</summary>
        public static IEnumerator TileJob(int tx, int tz, double spacing, ColourMesh m)
        {
            const int S = TILE;
            double x0 = tx * S, z0 = tz * S;
            var near = new Near(x0, z0);
            var n = (int)Math.Floor(S / spacing);
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    double u = FieldLayout.FHash(tx * 4096 + i, tz * 4096 + j, 101), v = FieldLayout.FHash(tx * 4096 + i, tz * 4096 + j, 202), w = FieldLayout.FHash(tx * 4096 + i, tz * 4096 + j, 303);
                    double x = x0 + (i + u) * spacing, z = z0 + (j + v) * spacing;
                    var kind = Classify(x, z, w, near);
                    if (kind != Kind.None) Place(m, kind, x, z, FieldLayout.FHash(x * 7, z * 7, 404), FieldLayout.FHash(x * 7, z * 7, 505));
                }
                yield return null;
            }
        }

        /// <summary>What stands near a tile, gathered once for all its points.</summary>
        public sealed class Near
        {
            public readonly List<Structure> structs = new List<Structure>();
            public readonly List<Paved> paved = new List<Paved>();
            public readonly List<FlatArea> flats = new List<FlatArea>();
            public readonly List<Site> sites = new List<Site>();
            public Near(double x0, double z0)
            {
                bool In(double x, double z, double r) => x > x0 - r && x < x0 + TILE + r && z > z0 - r && z < z0 + TILE + r;
                foreach (var s in WorldMap.STRUCTURES) if (In(s.x, s.z, 60)) structs.Add(s);
                foreach (var p in Places.PAVED) if (In(p.x, p.z, 80)) paved.Add(p);
                foreach (var p in new[] { Places.HELIPORT_APRON, Places.HELIPORT_TAXIWAY }) if (In(p.x, p.z, 200)) paved.Add(p);
                foreach (var f in Places.FLAT_AREAS) if (In(f.x, f.z, 300)) flats.Add(f);
                foreach (var s in WorldMap.SITES) if (In(s.x, s.z, 500)) sites.Add(s);
            }
        }

        public static Kind Classify(double x, double z, double w, Near near)
        {
            var y = WorldMap.TerrainHeight(x, z);
            if (y < 1.4) return Kind.None;
            foreach (var s in near.structs) { var r = Math.Max(s.w, s.d) / 2 + 1.5; if (Math.Abs(x - s.x) < r && Math.Abs(z - s.z) < r) return Kind.None; }
            foreach (var p in near.paved) { var r = Math.Max(p.w, p.d) / 2 + 1; if (Math.Abs(x - p.x) < r && Math.Abs(z - p.z) < r) return Kind.None; }
            foreach (var f in near.flats) if (Js.Hypot(x - f.x, z - f.z) < f.r * 0.95) return Kind.None;
            foreach (var s in near.sites) if (Js.Hypot(x - s.x, z - s.z) < s.radius * 2.5 + 4) return Kind.None;   // keep landing areas clear
            foreach (var s in near.sites)
                if ((s.kind == SiteKind.Heliport || s.kind == SiteKind.Hospital) && s.flatten.HasValue && Js.Hypot(x - s.x, z - s.z) < s.flatten.Value * 0.6) return w < 0.05 ? Kind.Tuft : Kind.None;   // mown
            var rd = RoadNet.NearestRoad(x, z);
            if (rd != null)
            {
                var town = false;
                foreach (var t in Places.SETTLEMENTS) if (Js.Hypot(x - t.x, z - t.z) < t.r * 0.85) { town = true; break; }
                if (rd.edge < (town ? 3 : 0.7)) return Kind.None;
                if (rd.edge < 2.2) return w < 0.45 ? Kind.Long : Kind.None;   // uncut verge
            }
            // rushes along the river banks
            double rw = 5 + 10 * Smoothstep(-2000, 1000, x), dr = Math.Abs(z - Rotorcraft.World.Terrain.RiverZ(x));
            if (dr < rw + 0.5) return Kind.None;
            if (dr < rw + 4) return w < 0.5 ? Kind.Rush : Kind.None;
            // dunes behind the beach
            var coast = Rotorcraft.World.Terrain.CoastX(z) - x;
            if (coast < 170 && y < 14) return w < 0.3 ? Kind.Marram : Kind.None;
            const double e = 2;
            double gx = (WorldMap.TerrainHeight(x + e, z) - WorldMap.TerrainHeight(x - e, z)) / (2 * e), gz = (WorldMap.TerrainHeight(x, z + e) - WorldMap.TerrainHeight(x, z - e)) / (2 * e);
            var len = Math.Sqrt(gx * gx + 1 + gz * gz);
            var slope = 1 - 1 / len;
            if (slope > 0.22) return w < 0.06 ? Kind.Boulder : w < 0.2 ? Kind.Stone : Kind.None;   // steep, rocky ground
            var moor = Smoothstep(140, 260, y);
            if (moor > 0 && FieldLayout.FHash(x * 3, z * 3, 606) < moor)
            {
                var drift = Fbm2(x / 331, z / 331, 41, 3) * 0.6 + Fbm2(x / 1931, z / 1931, 43, 3) * 0.4;
                var heath = Smoothstep(0.3, 0.75, drift);
                if (w < 0.008) return Kind.Boulder;
                if (w < 0.32 * heath) return Kind.Heather;
                if (w < 0.32 * heath + 0.3) return Kind.Moorgrass;
                return Kind.None;
            }
            var farm = y < 220 ? KinlochTerrain.FarmMask(x, z, y, Math.Acos(1 / len)) : 0;
            if (farm > 0.5)
            {
                var (qx0, qy) = FieldLayout.ToQ(x, z);
                var row = (int)Math.Floor(qy / FieldLayout.CELL_Z);
                var qx = qx0 + FieldLayout.RowShift(row);
                var col = (int)Math.Floor(qx / FieldLayout.CELL_X);
                double fx = qx / FieldLayout.CELL_X - col, fz = qy / FieldLayout.CELL_Z - row;
                double ex = Math.Min(fx, 1 - fx) * FieldLayout.CELL_X, ez = Math.Min(fz, 1 - fz) * FieldLayout.CELL_Z;
                var edge = Math.Min(ex, ez);
                if (edge < 4) return Kind.None;                                  // under the hedge or wall
                if (edge < 7) return w < 0.55 ? Kind.Long : Kind.None;           // headland
                var crop = FieldLayout.CropOf(col, row);
                switch (crop)
                {
                    case Crop.Pasture: return w < 0.03 ? Kind.Rush : w < 0.32 ? Kind.Tuft : Kind.None;   // grazed
                    case Crop.Stubble: return Kind.None;                                                  // flat
                    case Crop.Ploughed: return w < 0.08 ? Kind.Stone : Kind.None;                        // stones turned up
                    case Crop.Silage: return w < 0.2 ? Kind.Tuft : Kind.None;                            // aftermath
                    default: return w < 0.06 ? Kind.Rush : w < 0.42 ? Kind.Tussock : Kind.None;          // rough grazing
                }
            }
            // unfarmed grassland: rougher in patches, the odd gorse bush on the banks
            var rough = Smoothstep(0.45, 0.7, Fbm2(x / 97, z / 97, 47, 3));
            if (slope > 0.06 && w < 0.004) return Kind.Gorse;
            if (w < 0.02 + 0.05 * rough) return Kind.Rush;
            if (w < 0.25 + 0.3 * rough) return rough > 0.5 ? Kind.Tussock : Kind.Tuft;
            return Kind.None;
        }

        // ------------------------------------------------------------------------------------------------ shapes
        static readonly RGBA tuftLo = M(0.27, 0.34, 0.15, 0.02), tuftHi = M(0.45, 0.5, 0.25, 0.03),
            longLo = M(0.21, 0.25, 0.12, 0.02), longHi = M(0.4, 0.42, 0.23, 0.03),
            rushLo = M(0.14, 0.2, 0.09, 0.02), rushHi = M(0.3, 0.36, 0.16, 0.05),
            heathLo = M(0.23, 0.18, 0.17, 0.02), heathHi = M(0.4, 0.3, 0.33, 0.02),   // lighter and more purple than the browser game's, which read as black here
            moorLo = M(0.3, 0.27, 0.18, 0.02), moorHi = M(0.55, 0.5, 0.36, 0.03),
            marramLo = M(0.42, 0.42, 0.3, 0.02), marramHi = M(0.68, 0.64, 0.46, 0.03),
            stone = M(0.46, 0.44, 0.4, 0.08), gorse = M(0.13, 0.2, 0.09, 0.03);

        static void Blades(ColourMesh m, double x, double y, double z, int count, double h, double spread, double lean, RGBA lo, RGBA hi, double a, double b)
        {
            for (int k = 0; k < count; k++)
            {
                double ang = a * 6.283 + k * 2.399, c = Math.Cos(ang), s = Math.Sin(ang);
                double bx = x + c * spread * 0.3, bz = z + s * spread * 0.3;
                var hh = h * (0.7 + 0.5 * ((b * 7 + k * 0.37) % 1));
                var tip = new P3(bx + c * lean * hh, y + hh, bz + s * lean * hh);
                var wd = 0.012 + spread * 0.018;
                var nrm = new P3(c * 0.4, 0.9, s * 0.4);
                var i0 = m.Vertex(new P3(bx - s * wd, y - 0.02, bz + c * wd), nrm, lo);
                var i1 = m.Vertex(new P3(bx + s * wd, y - 0.02, bz - c * wd), nrm, lo);
                var i2 = m.Vertex(tip, nrm, hi);
                m.Tri(i0, i1, i2);
            }
        }

        static void Dome(ColourMesh m, double x, double y, double z, double r, double h, RGBA lo, RGBA hi, double a, int sides = 7)
        {
            var b0 = m.VertexCount;
            for (int k = 0; k < sides; k++)
            {
                double ang = (double)k / sides * 6.283 + a * 3, rr = r * (0.8 + 0.4 * ((a * 13 + k * 0.618) % 1));
                m.Vertex(new P3(x + Math.Cos(ang) * rr, y - 0.03, z + Math.Sin(ang) * rr), new P3(Math.Cos(ang) * 0.7, 0.5, Math.Sin(ang) * 0.7), lo);
            }
            for (int k = 0; k < sides; k++)
            {
                double ang = (double)k / sides * 6.283 + a * 3 + 0.3, rr = r * 0.62;
                m.Vertex(new P3(x + Math.Cos(ang) * rr, y + h * 0.75, z + Math.Sin(ang) * rr), new P3(Math.Cos(ang) * 0.5, 0.85, Math.Sin(ang) * 0.5), hi);
            }
            var top = m.Vertex(new P3(x, y + h, z), new P3(0, 1, 0), hi);
            for (int k = 0; k < sides; k++)
            {
                var k1 = (k + 1) % sides;
                m.Tri(b0 + k, b0 + sides + k, b0 + k1);
                m.Tri(b0 + k1, b0 + sides + k, b0 + sides + k1);
                m.Tri(b0 + sides + k, top, b0 + sides + k1);
            }
        }

        public static void Place(ColourMesh m, Kind kind, double x, double z, double a, double b)
        {
            var y = WorldMap.TerrainHeight(x, z);
            switch (kind)
            {
                case Kind.Tuft: Blades(m, x, y, z, 9, 0.16 + 0.12 * b, 0.9, 0.35, tuftLo, tuftHi, a, b); break;
                case Kind.Long: Blades(m, x, y, z, 11, 0.32 + 0.25 * b, 1.0, 0.4, longLo, longHi, a, b); break;
                case Kind.Rush: Blades(m, x, y, z, 11, 0.7 + 0.35 * b, 0.5, 0.1, rushLo, rushHi, a, b); break;
                case Kind.Tussock: Dome(m, x, y - 0.05, z, 0.25 + 0.15 * b, 0.2, tuftLo, tuftHi, a, 6); Blades(m, x, y + 0.15, z, 6, 0.35 + 0.2 * b, 0.7, 0.45, longLo, longHi, a, b); break;
                case Kind.Heather: Dome(m, x, y - 0.08, z, 0.45 + 0.4 * b, 0.2 + 0.15 * a, heathLo, heathHi, a, 6); Blades(m, x, y, z, 4, 0.3 + 0.2 * b, 0.9, 0.5, moorLo, moorHi, a, b); break;
                case Kind.Moorgrass: Blades(m, x, y, z, 8, 0.35 + 0.25 * b, 0.7, 0.45, moorLo, moorHi, a, b); break;
                case Kind.Marram: Blades(m, x, y, z, 9, 0.5 + 0.35 * b, 0.6, 0.35, marramLo, marramHi, a, b); break;
                case Kind.Stone: Dome(m, x, y, z, 0.12 + 0.15 * b, 0.1 + 0.12 * a, stone, stone, a, 5); break;
                case Kind.Boulder: Dome(m, x, y - 0.1, z, 0.5 + 0.7 * b, 0.4 + 0.6 * a, stone, stone, a, 6); break;
                case Kind.Gorse: m.Ellipsoid(new P3(x, y + 0.6, z), new P3(0.9 + 0.5 * b, 0.75, 0.9 + 0.5 * a), gorse, 7, 4); break;
            }
        }
    }
}
