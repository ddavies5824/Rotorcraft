// The road network as the ground sees it (port of src/world/roadNet.ts). Every road is resampled every few
// metres and given a smooth height profile (the terrain along it, averaged over ~80 m so the road does not
// follow every bump), and the terrain is cut and filled to that profile across the carriageway with banks
// either side. The flight model, the renderer and the traffic all use the same surface.
using System;
using System.Collections.Generic;
using Rotorcraft.Sim;
using static Rotorcraft.Sim.SimMath;

namespace Rotorcraft.World
{
    public sealed class RoadLine
    {
        public string name; public double width, hw;
        /// <summary>Gravel track (no tarmac, grass down the middle).</summary>
        public bool track;
        public double[] x, z, s;
        /// <summary>Profile height of the road surface at each point (filled on first height query).</summary>
        public double[] h;
        /// <summary>True on the bridge span (the deck is a structure; the glen below is left alone).</summary>
        public bool[] bridge;
        /// <summary>Road markings strength 0..1 (none on narrow roads, faded out at junctions).</summary>
        public double[] marked;
        /// <summary>How fully the terrain is graded to the road (1; under the bridge it falls away from each abutment).</summary>
        public double[] grade;
    }

    public sealed class RoadHit
    {
        public RoadLine road; public int index;
        /// <summary>Segment from point i to i+1, fraction t along it.</summary>
        public int i; public double t;
        /// <summary>Distance from the centre line (m) and distance along the road (m).</summary>
        public double d, s;
        /// <summary>d minus half the carriageway width: negative on the road.</summary>
        public double edge;
    }

    public static class RoadNet
    {
        /// <summary>Spacing of the resampled road points (m).</summary>
        public const double ROAD_STEP = 4;
        /// <summary>How far from a road's centre line the terrain can be affected (m): the widest bank plus margin.</summary>
        public const double ROAD_REACH = 40;
        /// <summary>Width of the level strip either side of the carriageway (verge), m.</summary>
        public const double VERGE = 1.2;
        /// <summary>Greatest bank width (m); banks are about 1 in 2.2.</summary>
        const double MAX_BANK = 28;
        const double CELL = 32;

        static RoadLine[] lines;
        static Dictionary<long, int[]> grid;
        static bool profiled;
        static Func<double, double, double> baseFn;
        static double bridgeDeckH;

        static long Key(long i, long j) => (i + 4096) * 8192 + (j + 4096);

        /// <summary>The resampled roads (geometry only until the first height query).</summary>
        public static RoadLine[] Lines() { if (lines == null) Build(); return lines; }

        static void Build()
        {
            var br = Places.BRIDGE;
            var built = new List<RoadLine>();
            foreach (var r in Places.ROADS)
            {
                var xs = new List<double>(); var zs = new List<double>();
                for (int i = 0; i < r.pts.Length - 1; i++)
                {
                    Pt a = r.pts[i], b = r.pts[i + 1];
                    var n0 = Math.Max(1, (int)Math.Ceiling(Js.Hypot(b.x - a.x, b.z - a.z) / ROAD_STEP));
                    for (int k = 0; k < n0; k++) { xs.Add(a.x + (b.x - a.x) * k / n0); zs.Add(a.z + (b.z - a.z) * k / n0); }
                }
                var last = r.pts[r.pts.Length - 1]; xs.Add(last.x); zs.Add(last.z);
                var n = xs.Count; var s = new double[n];
                for (int i = 1; i < n; i++) s[i] = s[i - 1] + Js.Hypot(xs[i] - xs[i - 1], zs[i] - zs[i - 1]);
                var bridge = new bool[n];
                for (int i = 0; i < n; i++) if (Js.Hypot(xs[i] - br.x, zs[i] - br.z) < br.len / 2 + 3) bridge[i] = true;
                // the embankment runs on under each end of the bridge and slopes away into the glen
                var grade = new double[n];
                for (int i = 0; i < n; i++) grade[i] = 1;
                int b0 = -1, b1 = -1;
                for (int i = 0; i < n; i++) if (bridge[i]) { if (b0 < 0) b0 = i; b1 = i; }
                if (b0 >= 0) for (int i = b0; i <= b1; i++) grade[i] = 1 - Smoothstep(0, 12, Math.Min(s[i] - s[b0], s[b1] - s[i]));
                var marked = new double[n];
                for (int i = 0; i < n; i++) marked[i] = r.width >= 6 ? 1 : 0;
                built.Add(new RoadLine { name = r.name, width = r.width, hw = r.width / 2, track = r.width <= 4.5, x = xs.ToArray(), z = zs.ToArray(), s = s, h = new double[n], bridge = bridge, marked = marked, grade = grade });
            }
            lines = built.ToArray();
            // spatial index: each segment is listed in every cell its reach touches
            var cells = new Dictionary<long, List<int>>();
            for (int li = 0; li < lines.Length; li++)
            {
                var L = lines[li];
                for (int i = 0; i < L.x.Length - 1; i++)
                {
                    double x0 = Math.Min(L.x[i], L.x[i + 1]) - ROAD_REACH, x1 = Math.Max(L.x[i], L.x[i + 1]) + ROAD_REACH;
                    double z0 = Math.Min(L.z[i], L.z[i + 1]) - ROAD_REACH, z1 = Math.Max(L.z[i], L.z[i + 1]) + ROAD_REACH;
                    for (var a = (long)Math.Floor(x0 / CELL); a <= (long)Math.Floor(x1 / CELL); a++)
                        for (var b = (long)Math.Floor(z0 / CELL); b <= (long)Math.Floor(z1 / CELL); b++)
                        {
                            var k = Key(a, b);
                            if (!cells.TryGetValue(k, out var l)) cells[k] = l = new List<int>();
                            l.Add(li * 65536 + i);
                        }
                }
            }
            grid = new Dictionary<long, int[]>();
            foreach (var kv in cells) grid[kv.Key] = kv.Value.ToArray();
            // markings fade out where another road joins or crosses (no centre line painted through a junction)
            for (int li = 0; li < lines.Length; li++)
            {
                var L = lines[li];
                for (int i = 0; i < L.x.Length; i++)
                {
                    if (L.marked[i] == 0) continue;
                    var o = NearestRoad(L.x[i], L.z[i], li);
                    if (o != null) L.marked[i] *= Smoothstep(o.road.hw + L.hw + 6, o.road.hw + L.hw + 22, o.d);
                }
            }
        }

        /// <summary>
        /// Nearest road to (x, z) within ROAD_REACH, by distance to the carriageway edge (so a wide road wins
        /// over a lane where they meet). exclude skips one road; before only considers roads listed earlier.
        /// </summary>
        public static RoadHit NearestRoad(double x, double z, int exclude = -1, int before = int.MaxValue)
        {
            if (grid == null) Build();
            if (!grid.TryGetValue(Key((long)Math.Floor(x / CELL), (long)Math.Floor(z / CELL)), out var list)) return null;
            RoadHit best = null; var bestE = double.PositiveInfinity;
            foreach (var code in list)
            {
                int li = code >> 16, i = code & 65535;
                if (li == exclude || li >= before) continue;
                var L = lines[li];
                double ax = L.x[i], az = L.z[i], dx = L.x[i + 1] - ax, dz = L.z[i + 1] - az;
                var l2 = dx * dx + dz * dz; if (l2 == 0) l2 = 1e-9;
                var t = ((x - ax) * dx + (z - az) * dz) / l2; t = t < 0 ? 0 : t > 1 ? 1 : t;
                double px = ax + dx * t - x, pz = az + dz * t - z;
                var d = Math.Sqrt(px * px + pz * pz);
                if (d > ROAD_REACH) continue;
                var e = d - L.hw;
                if (e < bestE) { bestE = e; best = new RoadHit { road = L, index = li, i = i, t = t, d = d, s = L.s[i] + (L.s[i + 1] - L.s[i]) * t, edge = e }; }
            }
            return best;
        }

        /// <summary>Distance from (x, z) to the nearest carriageway edge (m); large when no road is near.</summary>
        public static double RoadEdgeDistance(double x, double z)
        {
            var h = NearestRoad(x, z);
            return h != null ? h.edge : ROAD_REACH;
        }

        /// <summary>
        /// Give the roads their height profiles. base is the terrain without roads (natural ground with the
        /// landing sites and building plots levelled). Used on the first height query.
        /// </summary>
        public static void SetRoadBase(Func<double, double, double> b) { baseFn = b; profiled = false; }

        static void Profile()
        {
            if (profiled) return;
            profiled = true;
            var L0 = Lines();
            var bas = baseFn ?? ((x, z) => 0);
            // the bridge deck: level with the higher abutment
            var br = Places.BRIDGE;
            double bc = Math.Sin(br.rot), bs = Math.Cos(br.rot);
            var deck = Math.Max(bas(br.x + bc * br.len / 2, br.z + bs * br.len / 2), bas(br.x - bc * br.len / 2, br.z - bs * br.len / 2)) + 0.3;
            bridgeDeckH = deck;
            for (int li = 0; li < L0.Length; li++)
            {
                var L = L0[li];
                var n = L.x.Length;
                var raw = new double[n];
                for (int i = 0; i < n; i++) raw[i] = Math.Max(1.0, bas(L.x[i], L.z[i]));
                // two passes of a moving average (~80 m triangular window); the window shrinks at the ends so a
                // road still arrives exactly at the level of the place it leads to
                const int W = 10;
                var a = raw;
                for (int pass = 0; pass < 2; pass++)
                {
                    var b = new double[n];
                    for (int i = 0; i < n; i++)
                    {
                        var w = Math.Min(W, Math.Min(i, n - 1 - i));
                        double sum = 0; for (int k = i - w; k <= i + w; k++) sum += a[k];
                        b[i] = sum / (2 * w + 1);
                    }
                    a = b;
                }
                // over the bridge: the deck, with the approaches ramped to it over 40 m
                int bi0 = -1, bi1 = -1;
                for (int i = 0; i < n; i++) if (L.bridge[i]) { if (bi0 < 0) bi0 = i; bi1 = i; }
                if (bi0 >= 0)
                    for (int i = 0; i < n; i++)
                    {
                        var ds = i < bi0 ? L.s[bi0] - L.s[i] : i > bi1 ? L.s[i] - L.s[bi1] : 0;
                        a[i] = a[i] + (deck - a[i]) * (1 - Smoothstep(0, 40, ds));
                    }
                // where this road meets one already profiled, blend into that road's level so the junction is flush
                for (int i = 0; i < n; i++)
                {
                    var o = NearestRoad(L.x[i], L.z[i], li, li);
                    if (o == null) continue;
                    var k = 1 - Smoothstep(o.road.hw + L.hw, o.road.hw + L.hw + 70, o.d);
                    if (k > 0) a[i] = a[i] + (ProfileAt(o) - a[i]) * k;
                }
                // a last light smoothing so the blend into a junction has no kink (ends and the bridge deck kept)
                var c = (double[])a.Clone();
                for (int i = 0; i < n; i++)
                {
                    var w = Math.Min(5, Math.Min(i, n - 1 - i));
                    if (w == 0 || L.bridge[i]) continue;
                    double sum = 0; for (int k = i - w; k <= i + w; k++) sum += a[k];
                    c[i] = sum / (2 * w + 1);
                }
                Array.Copy(c, L.h, n);
            }
        }

        /// <summary>Height of the bridge deck surface (m).</summary>
        public static double BridgeDeck() { Profile(); return bridgeDeckH; }

        /// <summary>Road surface height at a hit (profile interpolated along the road).</summary>
        public static double ProfileAt(RoadHit h)
        {
            Profile();
            var H = h.road.h;
            return H[h.i] + (H[h.i + 1] - H[h.i]) * h.t;
        }

        /// <summary>
        /// Cut and fill: the terrain h0 at (x, z) brought to the road's level across the carriageway and verge,
        /// then blended back to the natural ground over a bank about 1 in 2.2 (at least 4 m wide).
        /// </summary>
        public static double RoadGrade(double x, double z, double h0)
        {
            var hit = NearestRoad(x, z);
            if (hit == null) return h0;
            var L = hit.road;
            var g = L.grade[hit.i] + (L.grade[hit.i + 1] - L.grade[hit.i]) * hit.t;
            if (g <= 0) return h0;
            var hr = ProfileAt(hit);
            var dh = hr - h0;
            var core = L.hw + VERGE;
            var bank = Math.Min(MAX_BANK, Math.Max(4, Math.Abs(dh) * 2.2));
            var w = (1 - Smoothstep(core, core + bank, hit.d)) * g;
            return h0 + dh * w;
        }

        /// <summary>For tests: forget everything (the next query rebuilds).</summary>
        public static void Reset() { lines = null; grid = null; profiled = false; }
    }
}
