// Port of the world parts of tests/terrain.test.ts, plus checks that the landing sites, decks and sea come
// out of GroundAt as the flight model needs them.
using System;
using System.Linq;
using NUnit.Framework;
using Rotorcraft.Sim;
using static Rotorcraft.World.WorldMap;

namespace Rotorcraft.World.Tests
{
    public class WorldTests
    {
        static bool Levelled(double x, double z) =>
            SITES.Any(t => t.flatten.HasValue && t.flatten > 0 && Js.Hypot(x - t.x, z - t.z) < t.flatten.Value * 1.6) ||
            Places.FLAT_AREAS.Any(f => Js.Hypot(x - f.x, z - f.z) < f.r * 1.6);

        [Test]
        public void RoadsAreLevelAcrossBankedBackAndSmoothAlong()
        {
            foreach (var L in RoadNet.Lines())
            {
                for (int i = 5; i < L.x.Length - 5; i += 37)
                {
                    if (L.bridge[i] || L.bridge[i + 1]) continue;
                    double dx = L.x[i + 1] - L.x[i], dz = L.z[i + 1] - L.z[i], l = Js.Hypot(dx, dz), nx = -dz / l, nz = dx / l;
                    // landing sites and building plots are levelled after the roads, so they win where they overlap
                    if (Levelled(L.x[i], L.z[i])) continue;
                    var c = TerrainHeight(L.x[i], L.z[i]);
                    foreach (var o in new[] { -L.hw, L.hw })
                    {
                        var h = TerrainHeight(L.x[i] + nx * o, L.z[i] + nz * o);
                        var other = RoadNet.NearestRoad(L.x[i] + nx * o, L.z[i] + nz * o);
                        if (other.road != L) continue;                      // at a junction another road's level wins
                        Assert.That(Math.Abs(h - c) < 0.15, $"{L.name} @{i}: edge {h:F2} vs centre {c:F2}");
                    }
                    // 40 m out the natural ground is untouched
                    double fx = L.x[i] + nx * 40, fz = L.z[i] + nz * 40;
                    if (RoadNet.NearestRoad(fx, fz) == null && !Levelled(fx, fz))
                        Assert.That(Math.Abs(TerrainHeight(fx, fz) - Terrain.NaturalHeight(fx, fz)) < 0.6);
                }
                // the profile has no steps: the grade changes gently from point to point
                for (int i = 1; i < L.h.Length - 1; i++)
                {
                    if (L.bridge[i - 1] || L.bridge[i + 1]) continue;
                    Assert.That(Math.Abs(L.h[i + 1] - 2 * L.h[i] + L.h[i - 1]) < 0.5, $"{L.name} kink at {i}");
                }
            }
            var first = RoadNet.Lines()[0];
            var hit = RoadNet.NearestRoad(first.x[50], first.z[50]);
            Assert.That(Math.Abs(RoadNet.ProfileAt(hit) - TerrainHeight(first.x[50], first.z[50])) < 0.05, "the surface on the centre line is the profile");
        }

        [Test]
        public void NoRoadRunsThroughABuilding()
        {
            var hits = new System.Collections.Generic.HashSet<string>();
            foreach (var L in RoadNet.Lines())
                foreach (var st in STRUCTURES)
                {
                    if (st.kind == StructureKind.Pier) continue;   // a road may run along a quay
                    double c = Math.Cos(st.rot), s = Math.Sin(st.rot), reach = Js.Hypot(st.w, st.d) / 2 + L.hw;
                    // walk the centre line in short steps: its points can be some way apart on a straight
                    for (int i = 0; i < L.x.Length - 1; i++)
                    {
                        if (Math.Min(L.x[i], L.x[i + 1]) > st.x + reach + 1 || Math.Max(L.x[i], L.x[i + 1]) < st.x - reach - 1 || Math.Min(L.z[i], L.z[i + 1]) > st.z + reach + 1 || Math.Max(L.z[i], L.z[i + 1]) < st.z - reach - 1) continue;
                        var steps = Math.Max(1, (int)Math.Ceiling(Js.Hypot(L.x[i + 1] - L.x[i], L.z[i + 1] - L.z[i])));
                        for (int k = 0; k <= steps; k++)
                        {
                            double dx = L.x[i] + (L.x[i + 1] - L.x[i]) * k / steps - st.x, dz = L.z[i] + (L.z[i + 1] - L.z[i]) * k / steps - st.z;
                            double lx = dx * c - dz * s, lz = dx * s + dz * c;
                            if (Math.Abs(lx) < st.w / 2 + L.hw && Math.Abs(lz) < st.d / 2 + L.hw) hits.Add($"{L.name} through {st.kind} {st.style} at {st.x:F0},{st.z:F0}");
                        }
                    }
                }
            Assert.That(hits, Is.Empty);
        }

        [Test]
        public void NoRoadCrossesALandingSite()
        {
            var hits = new System.Collections.Generic.HashSet<string>();
            foreach (var site in SITES)
            {
                // how far what is laid on the ground reaches: a pad's concrete surround, or a field's markers
                var reach = site.kind == SiteKind.Heliport || site.kind == SiteKind.Hospital ? site.radius * 1.9 : site.kind == SiteKind.Pad ? site.radius * 1.25 * Math.Sqrt(2) : site.radius;
                if (site.radius <= 0 || site.deckHeight.HasValue) continue;
                foreach (var L in RoadNet.Lines())
                    for (int i = 0; i < L.x.Length - 1; i++)
                    {
                        var steps = Math.Max(1, (int)Math.Ceiling(Js.Hypot(L.x[i + 1] - L.x[i], L.z[i + 1] - L.z[i])));
                        for (int k = 0; k <= steps; k++)
                        {
                            double x = L.x[i] + (L.x[i + 1] - L.x[i]) * k / steps, z = L.z[i] + (L.z[i + 1] - L.z[i]) * k / steps;
                            if (Js.Hypot(x - site.x, z - site.z) < reach + L.hw) hits.Add($"{L.name} across {site.name}");
                        }
                    }
            }
            Assert.That(hits, Is.Empty);
        }

        [Test]
        public void TheKinlochRoadKeepsOffTheHeliportsApronAndPads()
        {
            var road = RoadNet.Lines().First(l => l.name == "Kinloch road");
            var apron = Places.HELIPORT_APRON;
            var h2 = SiteById("kin_h2");
            for (int i = 0; i < road.x.Length; i++)
            {
                double x = road.x[i], z = road.z[i];
                Assert.That(Math.Abs(x - apron.x) > apron.w / 2 + road.hw || Math.Abs(z - apron.z) > apron.d / 2 + road.hw, $"on the apron at {x:F0},{z:F0}");
                Assert.That(Js.Hypot(x - h2.x, z - h2.z) > h2.radius * 1.9 + road.hw, $"on the H2 pad at {x:F0},{z:F0}");
            }
        }

        [Test]
        public void RoundedBendsKeepTheirEndsAndStayInsideTheCorner()
        {
            var r = Places.RoundCorners(new[] { new Pt(0, 0), new Pt(100, 0), new Pt(100, 100) }, 40);
            Assert.AreEqual(0, r[0].x); Assert.AreEqual(0, r[0].z);
            Assert.AreEqual(100, r[r.Length - 1].x); Assert.AreEqual(100, r[r.Length - 1].z);
            Assert.That(r.Length > 3);
            foreach (var p in r) Assert.That(p.x <= 100.0001 && p.z >= -0.0001, "inside the corner");
        }

        [Test]
        public void EveryGroundLandingSiteIsAPadOrFieldAtItsElevation()
        {
            foreach (var s in SITES)
            {
                if (s.radius <= 0) continue;
                var g = GroundAt(s.x, s.z);
                if (s.deckHeight.HasValue)
                {
                    Assert.AreEqual(Surface.Deck, g.surface, s.id);
                    Assert.AreEqual(s.deckHeight.Value, g.h, 1e-9, s.id);
                    continue;
                }
                Assert.AreEqual(s.id, g.siteId, $"{s.id} reports its own site");
                Assert.AreEqual(s.kind == SiteKind.Field ? Surface.Grass : Surface.Pad, g.surface, s.id);
                Assert.That(Math.Abs(g.h - SiteElevation(s)) < 0.3, $"{s.id}: ground {g.h:F2} vs site {SiteElevation(s):F2}");
                Assert.That(g.slope < 0.1, $"{s.id}: slope {g.slope:F3}");
            }
        }

        [Test]
        public void TheSeaIsWaterAndTheHillsAreHigh()
        {
            Assert.AreEqual(Surface.Water, GroundAt(Terrain.CoastX(0) + 800, 0).surface);
            var summit = SiteById("beinn_ardoch");
            Assert.That(TerrainHeight(summit.x, summit.z) > 300, "Beinn Ardoch stands well above the coast");
            Assert.That(SiteElevation(SiteById("kin_h1")) > 1 && SiteElevation(SiteById("kin_h1")) < 60, $"H1 at {SiteElevation(SiteById("kin_h1")):F1} m");
            Assert.Throws<ArgumentException>(() => SiteById("nowhere"));
        }

        [Test]
        public void PlacesAreDeterministicAndPopulated()
        {
            Assert.That(SITES.Count >= 20, $"{SITES.Count} sites");
            Assert.That(STRUCTURES.Count(s => s.kind == StructureKind.House) > 50, "houses along the roads in the settlements");
            Assert.That(Places.PROPS.Count > 200, $"{Places.PROPS.Count} props");
            Assert.AreEqual(9, Places.TURBINES.Count);
            Assert.That(STREET_LIGHTS.Count > 20);
            Assert.That(SITES.Select(s => s.id).Distinct().Count() == SITES.Count, "site ids are unique");
        }

        [Test]
        public void WorldGroundPlugsIntoTheFlightModel()
        {
            var saved = Ground.Current;
            try
            {
                Ground.Current = new WorldGround();
                var h1 = SiteById("kin_h1");
                var s = FlightModel.CreateState(new CreateOptions { x = h1.x, z = h1.z, headingDeg = 270, fuel = 40 });
                Assert.AreEqual(SiteElevation(h1), s.pos.y, 0.05, "parked on H1");
                for (int i = 0; i < 240; i++) FlightModel.Step(s, Controls.Idle, Aircraft.Get("kestrel"), Weather.Presets["calm"], new SimSettings(AssistMode.Beginner), FixedStepper.SIM_DT);
                Assert.IsTrue(s.onGround);
                Assert.IsFalse(s.crashed);
                Assert.AreEqual(Surface.Pad, s.@out.surfaceBelow);
            }
            finally { Ground.Current = saved; }
        }
    }
}
