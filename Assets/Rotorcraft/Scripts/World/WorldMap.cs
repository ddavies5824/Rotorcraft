// The operating region: a fictional stretch of the Scottish east coast around Kinloch Heliport (port of
// src/world/world.ts). Terrain is generated deterministically so the simulation and renderer agree on every
// height. Units: metres. Axes: x = east, y = up, z = south (north is -z).
using System;
using System.Collections.Generic;
using System.Linq;
using Rotorcraft.Sim;
using static Rotorcraft.Sim.SimMath;
using static Rotorcraft.World.Terrain;

namespace Rotorcraft.World
{
    public static class WorldMap
    {
        public static readonly List<Site> SITES;
        public static readonly List<Structure> STRUCTURES;
        /// <summary>Street lights placed along roads inside settlements (positions only; rendered by the light system).</summary>
        public static readonly List<Pt> STREET_LIGHTS = new List<Pt>();

        static WorldMap()
        {
            SITES = new List<Site>
            {
                new Site { id = "kin_h1", name = "Kinloch H1", kind = SiteKind.Heliport, x = 0, z = 0, radius = 9, flatten = 420, description = "Main helipad at Kinloch Heliport, home of the Kinloch Rotor School." },
                new Site { id = "kin_spot_a", name = "Training Spot A", kind = SiteKind.Pad, x = -150, z = -110, radius = 6, description = "Painted hover square west of the apron, used for hover and spot-turn practice." },
                new Site { id = "kin_spot_b", name = "Training Spot B", kind = SiteKind.Pad, x = -250, z = 170, radius = 6, description = "Remote training spot at the far end of the grass strip." },
                new Site { id = "moss_farm", name = "Moss Farm", kind = SiteKind.Field, x = -2900, z = -2300, radius = 25, flatten = 160, description = "Farm field approved for training landings. Trees on the northern edge." },
                new Site { id = "kin_h2", name = "Kinloch H2", kind = SiteKind.Heliport, x = 140, z = -115, radius = 13, description = "Large helipad beside the hangar for the twin-engine fleet (offshore and SAR)." },
                new Site { id = "balnagown", name = "Balnagown Hospital", kind = SiteKind.Hospital, x = -1300, z = -4700, radius = 12, flatten = 200, description = "Ground-level hospital helipad on the edge of town." },
                new Site { id = "inverhallan", name = "Inverhallan Infirmary", kind = SiteKind.Hospital, x = 250, z = -7600, radius = 12, flatten = 200, description = "Regional hospital with a ground-level helipad, 4 nm north of Kinloch along the coast." },
                new Site { id = "glen_clearing", name = "Glen Clearing", kind = SiteKind.Field, x = -4120, z = 1500, radius = 10, flatten = 45, description = "Small clearing in the forestry above the glen, used for confined-area training." },
                new Site { id = "beinn_ardoch", name = "Beinn Ardoch", kind = SiteKind.Landmark, x = -6400, z = 550, radius = 0, description = "Highest hill near Kinloch, 1,817 ft. Popular with walkers and climbers." },
                new Site { id = "tarbat_light", name = "Tarbat Light", kind = SiteKind.Landmark, x = 880, z = -3600, radius = 0, description = "White lighthouse on the headland. Common VFR reporting point." },
                new Site { id = "glen_bridge", name = "Glen Ardoch Bridge", kind = SiteKind.Landmark, x = -4300, z = 821, radius = 0, description = "Road bridge where the glen road crosses the river. VFR reporting point." },
                new Site { id = "cormorant_a", name = "Cormorant Alpha", kind = SiteKind.Helideck, x = 21000, z = -6000, radius = 11, deckHeight = 42, description = "Fictional North Sea production platform with a 22 m helideck." },
                new Site { id = "cormorant_b", name = "Cormorant Bravo", kind = SiteKind.Helideck, x = 30500, z = 3500, radius = 11, deckHeight = 38, description = "Bridge-linked accommodation platform, 15 nm east-north-east of Kinloch." },
                new Site { id = "kittiwake", name = "Kittiwake Alpha", kind = SiteKind.Helideck, x = 26500, z = -14500, radius = 11, deckHeight = 46, description = "Drilling platform 17 nm east of Tarbat Ness." },
            };
            SITES.AddRange(Places.NEW_SITES);

            STRUCTURES = new List<Structure>
            {
                new Structure { x = 55, z = -70, w = 40, d = 26, h = 11, kind = StructureKind.Hangar },
                new Structure { x = 95, z = 10, w = 8, d = 8, h = 16, kind = StructureKind.Tower },
                new Structure { x = 60, z = 45, w = 22, d = 12, h = 6, kind = StructureKind.House },
                new Structure { x = -1240, z = -4760, w = 70, d = 40, h = 14, kind = StructureKind.Hospital },
                new Structure { x = 880, z = -3600, w = 6, d = 6, h = 24, kind = StructureKind.Lighthouse },
            };
            STRUCTURES.AddRange(Places.NEW_STRUCTURES);

            BuildSettlements();

            // Platform structure under the helideck (legs + modules): the deck surface is the landing area.
            foreach (var s in SITES)
                if (s.kind == SiteKind.Helideck) STRUCTURES.Add(new Structure { x = s.x + 30, z = s.z, w = 45, d = 40, h = 38, @base = -20, kind = StructureKind.Platform, siteId = s.id });
        }

        // Settlements: houses along the roads (rooftops are solid for collision) and street lights.
        static void BuildSettlements()
        {
            long seed = 7;
            double R() { seed = seed * 16807 % 2147483647; return seed / 2147483647.0; }
            bool InSettlement(double x, double z) => Places.SETTLEMENTS.Any(t => Js.Hypot(x - t.x, z - t.z) < t.r);
            var fix = STRUCTURES.ToList();
            bool Clear(double x, double z) =>
                SITES.All(st => Js.Hypot(x - st.x, z - st.z) > (st.flatten ?? 40) * 0.75 + 25) && Math.Abs(z - RiverZ(x)) > 40
                && fix.All(st => Js.Hypot(x - st.x, z - st.z) > Math.Max(st.w, st.d) * 0.6 + 12)
                && Places.FLAT_AREAS.All(f => Js.Hypot(x - f.x, z - f.z) > f.r * 0.9)
                && Places.PAVED.All(p => Js.Hypot(x - p.x, z - p.z) > Math.Max(p.w, p.d) * 0.6 + 8);
            foreach (var road in Places.ROADS)
            {
                // walk the whole road at an irregular spacing (bends are many short segments, so the spacing
                // carries over from one segment to the next)
                double next = 0, along = 0;
                for (int i = 0; i < road.pts.Length - 1; i++)
                {
                    Pt a = road.pts[i], b = road.pts[i + 1];
                    var len = Js.Hypot(b.x - a.x, b.z - a.z);
                    if (len < 1e-6) continue;
                    double dx = (b.x - a.x) / len, dz = (b.z - a.z) / len;
                    var rot = Math.Atan2(dx, dz);
                    for (; next < along + len; next += 20 + R() * 14)
                    {
                        var d = next - along;
                        double cx = a.x + dx * d, cz = a.z + dz * d;
                        if (!InSettlement(cx, cz)) continue;
                        if (Math.Floor(next / 40) != Math.Floor((next - 20) / 40)) STREET_LIGHTS.Add(new Pt(cx - dz * (road.width / 2 + 1.5), cz + dx * (road.width / 2 + 1.5)));
                        foreach (var side in new[] { -1, 1 })
                        {
                            if (R() < 0.25) continue;
                            var off = road.width / 2 + 9 + R() * 6;
                            double hx = cx - dz * off * side, hz = cz + dx * off * side;
                            if (!Clear(hx, hz) || NaturalHeight(hx, hz) < 2) continue;
                            var big = R() < 0.15;
                            var w = big ? 16 + R() * 8 : 8 + R() * 4; var dd = 7 + R() * 3; var h = big ? 8 + R() * 3 : 5 + R() * 2.5;
                            // keep clear of every road, including the inside of a bend or another road nearby
                            if (RoadNet.RoadEdgeDistance(hx, hz) < Js.Hypot(w, dd) / 2 + 3) continue;
                            STRUCTURES.Add(new Structure { x = hx, z = hz, w = w, d = dd, h = h, kind = StructureKind.House, rot = rot });
                        }
                    }
                    along += len;
                }
            }
            // Moss Farm steading
            STRUCTURES.Add(new Structure { x = -3040, z = -2215, w = 13, d = 8, h = 6.5, kind = StructureKind.House, rot = 0.3 });
            STRUCTURES.Add(new Structure { x = -3010, z = -2260, w = 26, d = 14, h = 8, kind = StructureKind.Barn, rot = 0.3 });
        }

        /// <summary>
        /// Turbulence factor (0..1) in the wake of an offshore platform: strongest close behind the structure
        /// (downwind) and below about 40 m above the deck. (windToX, windToZ) is the unit vector the wind blows towards.
        /// </summary>
        public static double PlatformWake(double x, double y, double z, double windToX, double windToZ)
        {
            double k = 0;
            foreach (var s in SITES)
            {
                if (s.kind != SiteKind.Helideck) continue;
                double px = s.x + 30, pz = s.z;
                double dx = x - px, dz = z - pz;
                var along = dx * windToX + dz * windToZ;
                var across = Math.Abs(-dx * windToZ + dz * windToX);
                if (along < -30 || along > 260 || across > 70) continue;
                var vert = Math.Max(0, 1 - Math.Max(0, y - (s.deckHeight.Value + 40)) / 40);
                k = Math.Max(k, (1 - Math.Max(0, along) / 260) * (1 - across / 70) * vert);
            }
            return k;
        }

        /// <summary>Where a structure's walls start: absolute for platforms, piers and things built on them, else the ground.</summary>
        public static double StructureBase(Structure st, double ground) =>
            st.kind == StructureKind.Platform || st.kind == StructureKind.Pier || st.@base > 0 ? st.@base : ground;

        public static Site SiteById(string id) => SITES.FirstOrDefault(x => x.id == id) ?? throw new ArgumentException($"Unknown site {id}");
        public static Site FindSite(string id) => SITES.FirstOrDefault(x => x.id == id);

        static bool roadBaseSet;

        /// <summary>Terrain height including the roads (cut and filled to a smooth grade) and the flattened landing sites and building plots (no structures).</summary>
        public static double TerrainHeight(double x, double z)
        {
            if (!roadBaseSet) { RoadNet.SetRoadBase(Levelled); roadBaseSet = true; }
            return LevelSites(x, z, RoadNet.RoadGrade(x, z, NaturalHeight(x, z)));
        }

        /// <summary>The ground without roads: natural terrain with the landing sites and plots levelled.</summary>
        static double Levelled(double x, double z) => LevelSites(x, z, NaturalHeight(x, z));

        static double LevelSites(double x, double z, double h)
        {
            foreach (var s in SITES)
            {
                if (s.flatten == null || s.flatten == 0) continue;
                double dx = x - s.x, dz = z - s.z;
                var dist = Math.Sqrt(dx * dx + dz * dz);
                var fl = s.flatten.Value;
                if (dist < fl * 1.6)
                {
                    var target = SiteElevation(s);
                    var w = 1 - Smoothstep(fl * 0.7, fl * 1.6, dist);
                    h = h + (target - h) * w;
                }
            }
            foreach (var f in Places.FLAT_AREAS)
            {
                double dx = x - f.x, dz = z - f.z;
                var dist = Math.Sqrt(dx * dx + dz * dz);
                if (dist < f.r * 1.6)
                {
                    var w = f.drop.HasValue && f.drop.Value != 0 ? 1 - Smoothstep(f.r * 0.86, f.r * 1.02, dist) : 1 - Smoothstep(f.r * 0.7, f.r * 1.6, dist);
                    h = h + (FlatElevation(f) - h) * w;
                }
            }
            return h;
        }

        static readonly Dictionary<string, double> elevCache = new Dictionary<string, double>();

        /// <summary>Elevation of a site's landing surface above mean sea level (m).</summary>
        public static double SiteElevation(Site s)
        {
            if (s.deckHeight.HasValue) return s.deckHeight.Value;
            if (s.flatten == null || s.flatten == 0) return TerrainHeight(s.x, s.z); // pads/landmarks sit on (already flattened) terrain
            if (elevCache.TryGetValue(s.id, out var c)) return c;
            var v = Js.Round(NaturalHeight(s.x, s.z) * 10) / 10;
            elevCache[s.id] = v;
            return v;
        }

        // Spatial index of the structures (built on first use, after every place has been added).
        const double CELL = 100;
        static Dictionary<long, List<Structure>> grid;
        static readonly List<Structure> NONE = new List<Structure>();
        static long CellKey(long i, long j) => (i + 2000) * 4096 + (j + 2000);

        static List<Structure> StructuresNear(double x, double z)
        {
            if (grid == null)
            {
                grid = new Dictionary<long, List<Structure>>();
                foreach (var st in STRUCTURES)
                {
                    var r = Js.Hypot(st.w, st.d) / 2 + 1;
                    for (var i = (long)Math.Floor((st.x - r) / CELL); i <= (long)Math.Floor((st.x + r) / CELL); i++)
                        for (var j = (long)Math.Floor((st.z - r) / CELL); j <= (long)Math.Floor((st.z + r) / CELL); j++)
                        {
                            var k = CellKey(i, j);
                            if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<Structure>();
                            l.Add(st);
                        }
                }
            }
            return grid.TryGetValue(CellKey((long)Math.Floor(x / CELL), (long)Math.Floor(z / CELL)), out var found) ? found : NONE;
        }

        /// <summary>Height of whatever the aircraft would contact at (x,z): water, terrain, a pad, a deck or a roof.</summary>
        public static GroundInfo GroundAt(double x, double z)
        {
            // Helidecks and roofs first
            foreach (var s in SITES)
            {
                if (!s.deckHeight.HasValue) continue;
                double dx = x - s.x, dz = z - s.z;
                if (dx * dx + dz * dz <= s.radius * s.radius * 1.15) return new GroundInfo(s.deckHeight.Value, Surface.Deck, 0, s.id);
            }
            var t = TerrainHeight(x, z);
            GroundInfo? best = null;
            foreach (var st in StructuresNear(x, z))
            {
                double lx = x - st.x, lz = z - st.z;
                if (st.rot != 0) { double c = Math.Cos(st.rot), sn = Math.Sin(st.rot); var tx = lx * c - lz * sn; lz = lx * sn + lz * c; lx = tx; }
                if (Math.Abs(lx) <= st.w / 2 && Math.Abs(lz) <= st.d / 2)
                {
                    var top = StructureBase(st, t) + st.h;
                    if (best == null || top > best.Value.h) best = new GroundInfo(top, Surface.Roof, 0);
                }
            }
            if (best != null && best.Value.h > t) return best.Value;
            if (t <= 0.05) return new GroundInfo(0, Surface.Water, 0);
            const double e = 2;
            var sx = (TerrainHeight(x + e, z) - TerrainHeight(x - e, z)) / (2 * e);
            var sz = (TerrainHeight(x, z + e) - TerrainHeight(x, z - e)) / (2 * e);
            var slope = Math.Atan(Math.Sqrt(sx * sx + sz * sz));
            foreach (var s in SITES)
            {
                if (s.radius <= 0 || s.deckHeight.HasValue) continue;
                double dx = x - s.x, dz = z - s.z;
                if (dx * dx + dz * dz <= s.radius * s.radius) return new GroundInfo(t, s.kind == SiteKind.Field ? Surface.Grass : Surface.Pad, slope, s.id);
            }
            return new GroundInfo(t, slope > 0.35 ? Surface.Rock : Surface.Grass, slope);
        }

        /// <summary>Bearing (deg, true) and distance (m) from a to b.</summary>
        public static (double brg, double dist) BearingDistance(double ax, double az, double bx, double bz)
        {
            double dx = bx - ax, dz = bz - az;
            var brg = Math.Atan2(dx, -dz) * 180 / Math.PI;
            if (brg < 0) brg += 360;
            return (brg, Math.Sqrt(dx * dx + dz * dz));
        }
    }

    /// <summary>The Kinloch region as the flight model's ground: set Ground.Current = new WorldGround().</summary>
    public sealed class WorldGround : IGround
    {
        public GroundInfo GroundAt(double x, double z) => WorldMap.GroundAt(x, z);
        public double TerrainHeight(double x, double z) => WorldMap.TerrainHeight(x, z);
        public double PlatformWake(double x, double y, double z, double windToX, double windToZ) => WorldMap.PlatformWake(x, y, z, windToX, windToZ);
    }
}
