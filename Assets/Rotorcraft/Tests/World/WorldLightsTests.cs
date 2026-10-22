// The region's fixed lights: every helipad lit, street lights along the settlement roads, nothing underground.
using System;
using System.Linq;
using NUnit.Framework;
using Rotorcraft.Sim;
using static Rotorcraft.World.WorldMap;

namespace Rotorcraft.World.Tests
{
    public class WorldLightsTests
    {
        [Test]
        public void HelipadsAreRingedWithGreen()
        {
            var (points, _) = WorldLights.Build();
            foreach (var s in SITES.Where(s => s.kind == SiteKind.Heliport || s.kind == SiteKind.Hospital || s.kind == SiteKind.Helideck))
            {
                var green = points.Count(p => p.g > 0.9f && p.r < 0.2f && Math.Abs(Js.Hypot(p.x - s.x, p.z - s.z) - s.radius) < s.radius * 0.1);
                Assert.GreaterOrEqual(green, 16, $"{s.id} perimeter lights");
            }
        }

        [Test]
        public void StreetLightsAndPoolsAboveGround()
        {
            var (points, pools) = WorldLights.Build();
            Assert.Greater(STREET_LIGHTS.Count, 50);
            Assert.GreaterOrEqual(pools.Count, STREET_LIGHTS.Count);
            foreach (var p in points)
            {
                Assert.False(double.IsNaN(p.x) || double.IsNaN(p.y) || double.IsNaN(p.z));
                if (Math.Abs(p.x) < 15000 && Math.Abs(p.z) < 15000) Assert.GreaterOrEqual(p.y, TerrainHeight(p.x, p.z) - 0.5, $"light at {p.x:F0},{p.z:F0} underground");
            }
        }
    }
}
