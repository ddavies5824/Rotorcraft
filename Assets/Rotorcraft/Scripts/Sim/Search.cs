// Search model (port of src/sim/search.ts): coverage of a search area and the probability of the crew seeing
// the casualty. Detection is deliberately not a waypoint: the target sits somewhere in the area (seeded per
// attempt) and is only found by flying a sensible pattern at a sensible height and speed.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Rotorcraft.Sim
{
    public sealed class SearchAreaDef
    {
        public string id, name;
        public double cx, cz, w, h, rotDeg;
    }

    public enum TargetKind { Walker, Climber, Swimmer, Liferaft, Casualty, Dummy, Vessel }

    public sealed class TargetDef
    {
        public string id, name;
        public TargetKind kind;
        /// <summary>Fixed position, or random inside a search area (seeded per attempt).</summary>
        public double? x, z;
        public string areaId;
        public bool onWater;
        public double conspicuity;   // 1 = orange immersion suit / jacket, 0.5 = dark clothing
    }

    public sealed class PlacedTarget
    {
        public string id, name;
        public TargetKind kind;
        public double x, y, z;
        public bool onWater;
        public double conspicuity;
    }

    public sealed class SearchState
    {
        public SearchAreaDef area;
        public double cell;
        public int nx, nz;
        public byte[] covered;
        public bool detected;
        public double? detectT;
        public Func<double> rand;
        public double sinceStart;
    }

    public struct Sensors
    {
        public bool searchlight, flir;
        public double night, visibilityM;
    }

    public static class Search
    {
        /// <summary>Point in area-local coordinates (u across, v along), area centred at 0.</summary>
        public static (double u, double v) ToArea(SearchAreaDef a, double x, double z)
        {
            double r = a.rotDeg * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
            double dx = x - a.cx, dz = z - a.cz;
            return (dx * c - dz * s, dx * s + dz * c);
        }
        public static (double x, double z) FromArea(SearchAreaDef a, double u, double v)
        {
            double r = a.rotDeg * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
            return (a.cx + u * c + v * s, a.cz - u * s + v * c);
        }
        public static bool InArea(SearchAreaDef a, double x, double z) { var p = ToArea(a, x, z); return Math.Abs(p.u) <= a.w / 2 && Math.Abs(p.v) <= a.h / 2; }
        public static (double x, double z)[] AreaCorners(SearchAreaDef a) =>
            new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) }.Select(ij => FromArea(a, (ij.Item1 * a.w) / 2, (ij.Item2 * a.h) / 2)).ToArray();

        /// <summary>Place targets for this attempt. Random targets avoid steep rock and stay on land unless onWater.</summary>
        public static List<PlacedTarget> PlaceTargets(IEnumerable<TargetDef> defs, IList<SearchAreaDef> areas, double seed)
        {
            var R = SimMath.Rng(seed);
            var world = Ground.Current;
            var placed = new List<PlacedTarget>();
            foreach (var d in defs)
            {
                double x = d.x ?? 0, z = d.z ?? 0;
                var a = d.areaId != null ? areas.FirstOrDefault(q => q.id == d.areaId) : null;
                if (a != null && (d.x == null || d.z == null))
                {
                    for (int i = 0; i < 60; i++)
                    {
                        var p = FromArea(a, (R() - 0.5) * a.w * 0.85, (R() - 0.5) * a.h * 0.85);
                        var gp = world.GroundAt(p.x, p.z);
                        var water = gp.surface == Surface.Water;
                        if (d.onWater != water) continue;
                        if (!water && gp.slope > 0.5) continue;
                        x = p.x; z = p.z;
                        break;
                    }
                }
                var g = world.GroundAt(x, z);
                placed.Add(new PlacedTarget { id = d.id, name = d.name, kind = d.kind, x = x, z = z, y = d.onWater ? 0 : g.h, onWater = d.onWater, conspicuity = d.conspicuity });
            }
            return placed;
        }

        public static SearchState NewSearch(SearchAreaDef area, double seed, double cell = 100)
        {
            int nx = Math.Max(1, (int)Math.Ceiling(area.w / cell)), nz = Math.Max(1, (int)Math.Ceiling(area.h / cell));
            return new SearchState { area = area, cell = cell, nx = nx, nz = nz, covered = new byte[nx * nz], detected = false, detectT = null, rand = SimMath.Rng(Js.ToInt32(seed) ^ 0x5bd1e995), sinceStart = 0 };
        }

        /// <summary>Effective sweep half-width (m) for coverage and the detection range, from height, light and weather.</summary>
        public static double SweepHalfWidth(double aglM, Sensors sensors)
        {
            var aglFt = aglM * 3.28084;
            // best between about 200 and 500 ft: lower sees less ground, higher makes a person too small
            var heightF = aglFt < 100 ? 0.55 : aglFt < 200 ? 0.8 : aglFt <= 500 ? 1 : aglFt <= 1000 ? 0.75 : 0.45;
            var visF = Math.Min(1, sensors.visibilityM / 3000);
            var lightF = sensors.night > 0.5 ? (sensors.flir ? 0.8 : sensors.searchlight ? 0.35 : 0.12) : 1;
            return 180 * heightF * visF * lightF;
        }

        static bool LineOfSight(double ex, double ey, double ez, double tx, double ty, double tz)
        {
            var world = Ground.Current;
            for (int i = 1; i < 16; i++)
            {
                var k = i / 16.0;
                double x = ex + (tx - ex) * k, y = ey + (ty - ey) * k, z = ez + (tz - ez) * k;
                if (world.TerrainHeight(x, z) > y + 1) return false;
            }
            return true;
        }

        /// <summary>
        /// Advance coverage and detection by dt. Returns true on the step the target is first seen.
        /// Detection probability per second rises as range falls, falls with speed, and needs line of sight.
        /// </summary>
        public static bool StepSearch(SearchState st, HeliState s, PlacedTarget target, Sensors sensors, double dt)
        {
            st.sinceStart += dt;
            var half = SweepHalfWidth(s.@out.aglM, sensors);
            // coverage: mark cells within the sweep
            var p = ToArea(st.area, s.pos.x, s.pos.z);
            int cu = (int)Math.Floor((p.u + st.area.w / 2) / st.cell), cv = (int)Math.Floor((p.v + st.area.h / 2) / st.cell);
            var rc = (int)Math.Ceiling(half / st.cell);
            for (int j = cv - rc; j <= cv + rc; j++)
                for (int i = cu - rc; i <= cu + rc; i++)
                {
                    if (i < 0 || j < 0 || i >= st.nx || j >= st.nz) continue;
                    double cx = (i + 0.5) * st.cell - st.area.w / 2, cz = (j + 0.5) * st.cell - st.area.h / 2;
                    if (Js.Hypot(cx - p.u, cz - p.v) <= half) st.covered[j * st.nx + i] = 1;
                }
            if (st.detected) return false;
            double dx = target.x - s.pos.x, dz = target.z - s.pos.z;
            var horiz = Js.Hypot(dx, dz);
            var range = half * 1.6 * (0.6 + 0.4 * target.conspicuity) * (target.onWater ? 0.8 : 1);
            if (horiz < 45 && s.@out.aglM < 90) { st.detected = true; st.detectT = st.sinceStart; return true; } // close overhead: always seen
            if (horiz > range) return false;
            if (!LineOfSight(s.pos.x, s.pos.y + 1, s.pos.z, target.x, target.y + 1, target.z)) return false;
            var gsKt = Js.Hypot(s.vel.x, s.vel.z) * 1.944;
            var speedF = gsKt < 50 ? 1 : gsKt < 80 ? 0.7 : gsKt < 110 ? 0.4 : 0.2;
            var rate = 0.9 * (1 - horiz / range) * speedF; // detections per second
            if (st.rand() < 1 - Math.Exp(-rate * dt)) { st.detected = true; st.detectT = st.sinceStart; return true; }
            return false;
        }

        public static double CoverageFraction(SearchState st)
        {
            var n = 0;
            foreach (var c in st.covered) n += c;
            return (double)n / st.covered.Length;
        }

        /// <summary>"Two o'clock, 400 metres" relative to the aircraft's nose.</summary>
        public static string ClockCall(HeliState s, double x, double z)
        {
            var brg = Math.Atan2(x - s.pos.x, -(z - s.pos.z));
            var rel = (brg - s.psi) * 180 / Math.PI;
            rel = ((rel % 360) + 360) % 360;
            var clock = (int)Js.Round(rel / 30) % 12;
            if (clock == 0) clock = 12;
            var d = Js.Hypot(x - s.pos.x, z - s.pos.z);
            var dist = d < 150 ? $"{Math.Max(10, Js.Round(d / 10) * 10)} metres" : d < 1000 ? $"{Js.Round(d / 50) * 50} metres" : $"{Js.Fixed(d / 1852, 1)} miles";
            return $"{clock} o'clock, {dist}";
        }

        public static Sensors SensorsFor(WeatherPreset wx, double night, bool searchlight, bool flir) =>
            new Sensors { searchlight = searchlight, flir = flir, night = night, visibilityM = wx.visibilityM };
    }
}
