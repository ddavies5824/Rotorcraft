// The region's fixed lights (port of src/render/lights.ts): light points, drawn as glowing sprites, and light
// pools that light the ground round them at night. Layouts follow common UK conventions: green TLOF perimeter
// lights and floodlit pads at helipads (CAP 437-style yellow circle and green perimeter on a helideck), blue
// taxiway edge lights, red obstruction lights, sodium street lighting. Sim axes: x east, y up, z south.
using System;
using System.Collections.Generic;
using static Rotorcraft.World.WorldMap;

namespace Rotorcraft.World
{
    /// <summary>How a light point shines: steady, double strobe, rotating beacon, lighthouse flash, flame, slow
    /// blink (obstruction), or a steady lamp that throws diffraction spikes when bright.</summary>
    public enum LightMode { Steady = 0, Strobe = 1, Beacon = 2, Lighthouse = 3, Flame = 4, SlowBlink = 5, Lamp = 6 }

    public struct LightPoint
    {
        public double x, y, z;
        public float r, g, b;
        /// <summary>How much of it shows by day (0 = night only, 1 = as bright as at night).</summary>
        public float dayVis;
        /// <summary>Apparent size, metres.</summary>
        public float size;
        public LightMode mode;
        public float period, phase;
    }

    public struct LightPool
    {
        public double x, y, z, radius;
        public float r, g, b;
        public bool nightOnly;
    }

    public static class WorldLights
    {
        public static readonly float[] Green = { 0.12f, 1.0f, 0.3f }, White = { 1.0f, 0.94f, 0.82f }, Yellow = { 1.0f, 0.72f, 0.14f },
            Red = { 1.0f, 0.06f, 0.03f }, Blue = { 0.12f, 0.3f, 1.0f }, Sodium = { 1.0f, 0.5f, 0.16f }, Led = { 0.9f, 0.95f, 1.0f }, Flame = { 1.0f, 0.42f, 0.08f };

        public static (List<LightPoint> points, List<LightPool> pools) Build()
        {
            var points = new List<LightPoint>();
            var pools = new List<LightPool>();
            void P(double x, double y, double z, float[] c, float size = 0.5f, LightMode mode = LightMode.Steady, float dayVis = 0, float period = 1, float phase = 0) =>
                points.Add(new LightPoint { x = x, y = y, z = z, r = c[0], g = c[1], b = c[2], size = size, mode = mode, dayVis = dayVis, period = period, phase = phase });
            void Pool(double x, double y, double z, double radius, float r, float g, float b, bool nightOnly = true) =>
                pools.Add(new LightPool { x = x, y = y, z = z, radius = radius, r = r, g = g, b = b, nightOnly = nightOnly });
            void Ring(double cx, double y, double cz, double r, int n, float[] c, float size = 0.35f)
            {
                for (int i = 0; i < n; i++) { var a = (double)i / n * Math.PI * 2; P(cx + Math.Cos(a) * r, y, cz + Math.Sin(a) * r, c, size); }
            }

            foreach (var s in SITES)
            {
                var y = (s.deckHeight ?? SiteElevation(s)) + 0.25;
                if (s.kind == SiteKind.Heliport || s.kind == SiteKind.Hospital)
                {
                    Ring(s.x, y, s.z, s.radius * 1.05, 16, Green);                 // TLOF perimeter
                    Ring(s.x, y, s.z, s.radius * 1.75, 12, White, 0.3f);           // FATO perimeter
                    Pool(s.x + s.radius * 2.2, y + 7, s.z - s.radius * 2.2, 45, 1.2f, 1.1f, 0.95f);
                    P(s.x + s.radius * 2.2, y + 7, s.z - s.radius * 2.2, White, 1.2f);
                    Pool(s.x - s.radius * 2.2, y + 7, s.z + s.radius * 2.2, 45, 1.2f, 1.1f, 0.95f);
                    P(s.x - s.radius * 2.2, y + 7, s.z + s.radius * 2.2, White, 1.2f);
                }
                else if (s.kind == SiteKind.Helideck)
                {
                    Ring(s.x, y, s.z, s.radius * 1.02, 20, Green, 0.4f);            // perimeter
                    Ring(s.x, y + 0.02, s.z, s.radius * 0.55, 16, Yellow, 0.3f);    // touchdown circle
                    foreach (var (dx, dz) in new[] { (-2.5, -3.0), (-2.5, 0.0), (-2.5, 3.0), (2.5, -3.0), (2.5, 0.0), (2.5, 3.0), (0.0, 0.0) }) P(s.x + dx, y + 0.03, s.z + dz, Green, 0.25f);   // lit H
                    Pool(s.x, y + 12, s.z + 14, 55, 3.2f, 3.0f, 2.6f);
                }
                else if (s.kind == SiteKind.Pad)
                {
                    foreach (var (dx, dz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) }) P(s.x + dx * s.radius, y, s.z + dz * s.radius, Yellow, 0.3f);
                }
            }
            // heliport: apron floodlights, taxiway edge lights, windsock light
            var b0 = SITES[0];
            var be = SiteElevation(b0);
            foreach (var (dx, dz) in new[] { (-50, -70), (90, -70), (90, 30), (-50, 30) })
            {
                P(b0.x + dx, be + 10, b0.z + dz, White, 1.4f);
                Pool(b0.x + dx, be + 10, b0.z + dz, 60, 1.0f, 0.94f, 0.82f);
            }
            double tc = Math.Cos(0.2), ts = Math.Sin(0.2);
            for (int i = -6; i <= 6; i++)
                foreach (var side in new[] { -1, 1 })
                {
                    double lx = i * 20, lz = side * 8;
                    P(b0.x - 160 + lx * tc + lz * ts, be + 0.3, b0.z + 30 - lx * ts + lz * tc, Blue, 0.3f);
                }
            P(b0.x + 40, be + 6.4, b0.z + 40, White, 0.5f);
            foreach (var st in STRUCTURES)
            {
                var g = StructureBase(st, TerrainHeight(st.x, st.z));
                if (st.kind == StructureKind.Hangar || st.kind == StructureKind.Hospital)
                {
                    foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) }) P(st.x + sx * st.w / 2, g + st.h + 1.3, st.z + sz * st.d / 2, Red, 0.4f, LightMode.Steady, 0.15f);
                }
                else if (st.kind == StructureKind.Tower)
                {
                    P(st.x, g + st.h + 3.2, st.z, Red, 0.5f, LightMode.SlowBlink, 0.25f, 1.5f);
                    Pool(st.x, g + st.h + 2, st.z, 16, 1.2f, 1.0f, 0.7f);
                }
                else if (st.kind == StructureKind.Turbine)
                {
                    P(st.x, g + st.h + 3.6, st.z, Red, 1.1f, LightMode.SlowBlink, 0.3f, 2.0f, (float)(((st.x % 7) + 7) % 7 / 7));   // nacelle obstruction light
                }
                else if (st.kind == StructureKind.Building && (st.style == BuildingStyle.Firestation || st.style == BuildingStyle.Police || st.style == BuildingStyle.Mrt || st.style == BuildingStyle.Lifeboat))
                {
                    Pool(st.x, g + st.h, st.z, 30, 1.0f, 0.95f, 0.85f);
                    P(st.x, g + st.h + 0.5, st.z, White, 0.7f);
                }
                else if (st.kind == StructureKind.Lighthouse)
                {
                    P(st.x, g + st.h + 1.6, st.z, White, 3.5f, LightMode.Lighthouse, 0.3f, 10, 0);     // Fl W 10s
                }
                else if (st.kind == StructureKind.Platform)
                {
                    var top = st.@base + st.h;
                    P(st.x + 14, top + 61, st.z + 10, Red, 1.0f, LightMode.Steady, 0.3f);               // derrick top
                    P(st.x + 14, top + 30, st.z + 10, Red, 0.7f, LightMode.Steady, 0.2f);
                    for (int i = 0; i < 26; i++) { var a = i * 2.4; P(st.x + Math.Cos(a) * 21, top - 2 - (i % 3) * 5, st.z + Math.Sin(a) * 19, i % 4 == 0 ? Sodium : White, 0.6f); }
                    P(st.x + 52, top + 35, st.z - 26, Flame, 5, LightMode.Flame, 0.6f, 1, 0.3f);        // flare tip
                    P(st.x + 52, top + 38, st.z - 26, Flame, 3, LightMode.Flame, 0.6f, 1, 0.7f);
                    Pool(st.x, top + 5, st.z, 70, 2.4f, 2.0f, 1.5f);
                }
            }
            foreach (var p in STREET_LIGHTS)
            {
                var y = TerrainHeight(p.x, p.z) + 7;
                P(p.x, y, p.z, Sodium, 1.4f);
                Pool(p.x, y, p.z, 28, 0.9f, 0.45f, 0.15f);
            }
            // bridge parapet lamps
            var br = Places.BRIDGE;
            foreach (var k in new[] { -1, 1 })
                P(br.x + Math.Sin(br.rot) * br.len * 0.5 * k, TerrainHeight(br.x, br.z) + 9, br.z + Math.Cos(br.rot) * br.len * 0.5 * k, Sodium, 0.8f);
            return (points, pools);
        }
    }
}
