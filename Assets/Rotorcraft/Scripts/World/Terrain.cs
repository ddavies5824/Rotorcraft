// Base terrain functions shared by the world data and the places built on it (port of src/world/terrain.ts).
// Pure functions with no dependencies on the rest of the world. Units: metres. Axes: x = east, y = up, z = south.
using System;
using Rotorcraft.Sim;
using static Rotorcraft.Sim.SimMath;

namespace Rotorcraft.World
{
    public enum SiteKind { Heliport, Pad, Field, Hospital, Helideck, Landmark }

    public sealed class Site
    {
        public string id, name;
        public SiteKind kind;
        public double x, z;
        /// <summary>Landing-area radius in metres (0 for landmarks that cannot be landed on).</summary>
        public double radius;
        /// <summary>Flatten radius on terrain (ground sites).</summary>
        public double? flatten;
        /// <summary>For helidecks/rooftops: absolute deck height above sea level.</summary>
        public double? deckHeight;
        public string description;
    }

    public enum BuildingStyle { None, Firestation, Police, School, Clubhouse, Distillery, Warehouse, Lifeboat, Lodge, Mrt, Shed, Crusher, Shop, Church, Office }
    public enum StructureKind { Hangar, Tower, House, Barn, Hospital, Lighthouse, Platform, Building, Turbine, Pier, Castle, Caravan }

    public sealed class Structure
    {
        public double x, z, w, d, h, @base;
        public StructureKind kind;
        public double rot;
        public string siteId;
        public BuildingStyle style;
        /// <summary>Hospital with a helipad on the roof (no parapet slab over the pad).</summary>
        public bool rooftop;
    }

    /// <summary>An area of ground levelled for a building plot, car park or quarry floor (drop digs below the natural level).</summary>
    public sealed class FlatArea
    {
        public double x, z, r;
        public double? drop, elev;
    }

    public struct Pt
    {
        public double x, z;
        public Pt(double x, double z) { this.x = x; this.z = z; }
    }

    public static class Terrain
    {
        public const double SEED = 1971;

        /// <summary>x-coordinate of the shoreline at a given z. Sea lies to the east (x greater).</summary>
        public static double CoastX(double z) =>
            1100 + 380 * Math.Sin(z / 1700) + 220 * Math.Sin(z / 640 + 1.3) + 90 * Math.Sin(z / 230 + 0.4);

        /// <summary>Unit tangent of the coastline at z (pointing south), the outward normal (towards the sea) and its rotation.</summary>
        public static (Pt t, Pt n, double rot) CoastFrame(double z)
        {
            var d = (CoastX(z + 5) - CoastX(z - 5)) / 10;
            var l = Js.Hypot(d, 1);
            var t = new Pt(d / l, 1 / l);
            return (t, new Pt(t.z, -t.x), Math.Atan2(t.x, t.z));
        }

        /// <summary>Centre line of the river in Glen Ardoch, matching the valley carved into the terrain.</summary>
        public static double RiverZ(double x) => 900 + 300 * Math.Sin(x / 1500);

        /// <summary>Natural terrain height ignoring sites and structures. Negative = seabed (water surface at 0).</summary>
        public static double NaturalHeight(double x, double z)
        {
            var d = CoastX(z) - x; // distance inland (m); negative offshore
            // The shore steps clearly through the waterline (sea bed below -2.5 m, beach above +1.2 m) so the
            // land and the water surface are never nearly coplanar, which would flicker on screen.
            if (d < 0) return Math.Max(-40, -2.5 + d * 0.06); // shelving seabed
            var beach = 1.2 + Clamp(d * 0.02, 0, 2.4);
            var inland = Smoothstep(150, 9000, d);
            var ridge = 520 * Math.Pow(inland, 1.35);
            var hills = (Fbm2(x / 1400, z / 1400, SEED, 5) - 0.35) * 260 * Smoothstep(300, 2600, d);
            var small = (Fbm2(x / 260, z / 260, SEED + 99, 3) - 0.5) * 14 * Smoothstep(80, 600, d);
            // River glen running west from the bridge, carved into the hills
            var glen = Math.Exp(-Math.Pow((z - 900 - 300 * Math.Sin(x / 1500)) / 260, 2)) * 120 * Smoothstep(1500, 6000, d);
            return Math.Max(1.2, beach + 8 * Smoothstep(0, 400, d) + ridge + hills + small - glen);
        }

        /// <summary>Level of a flat area (natural height at its centre, less any excavation), rounded so it is stable.</summary>
        public static double FlatElevation(FlatArea a)
        {
            if (a.elev == null) a.elev = Js.Round((NaturalHeight(a.x, a.z) - (a.drop ?? 0)) * 10) / 10;
            return a.elev.Value;
        }
    }
}
