using System.Linq;
using NUnit.Framework;
using Rotorcraft.Inputs;
using Rotorcraft.Sim;

namespace Rotorcraft.Presentation.Tests
{
    public class CockpitSwitchesTests
    {
        static HeliState Cold(string type) => FlightModel.CreateState(new CreateOptions { headingDeg = 0, fuel = Aircraft.Get(type).fuelCapacity * 0.5 });

        static CockpitSwitch Find(CockpitSwitch[] all, GameAction a) => all.Single(x => x.action == a);

        [Test]
        public void EveryAircraftHasAFullPanelWithEachSwitchInTheSamePlace()
        {
            var first = CockpitSwitches.Build(Cold("kestrel"), Aircraft.Get("kestrel"), Autopilot.NewAP(), false).Select(x => x.action).ToArray();
            Assert.That(first.Length, Is.EqualTo(CockpitSwitches.Columns * CockpitSwitches.Rows));
            foreach (var type in new[] { "northstar", "guardian", "nightjar", "peregrine" })
                Assert.That(CockpitSwitches.Build(Cold(type), Aircraft.Get(type), Autopilot.NewAP(), false).Select(x => x.action).ToArray(), Is.EqualTo(first), type);
        }

        [Test]
        public void WhatAnAircraftDoesNotHaveIsBlank()
        {
            foreach (var type in new[] { "kestrel", "northstar", "guardian", "nightjar", "peregrine" })
            {
                var ac = Aircraft.Get(type);
                var all = CockpitSwitches.Build(Cold(type), ac, Autopilot.NewAP(), false);
                Assert.That(Find(all, GameAction.Searchlight).available, Is.EqualTo(ac.equipment.searchlight), $"{type} searchlight");
                Assert.That(Find(all, GameAction.ApAlt).available, Is.EqualTo(ac.equipment.autopilot), $"{type} autopilot");
                Assert.That(Find(all, GameAction.Gear).available, Is.EqualTo(ac.retractableGear), $"{type} gear");
                foreach (var sw in all.Where(x => !x.available && x.action != GameAction.TrimRelease && x.action != GameAction.ThrottleUp && x.action != GameAction.Doors))
                    Assert.That(sw.label, Is.Empty, $"{type} {sw.action}");
            }
        }

        [Test]
        public void TheEngineSwitchFollowsTheEngine()
        {
            var ac = Aircraft.Get("guardian");
            var s = Cold("guardian");
            s.engine = EngineState.Off;
            var off = Find(CockpitSwitches.Build(s, ac, Autopilot.NewAP(), false), GameAction.Engine);
            Assert.That((off.label, off.on, off.available), Is.EqualTo(("START", false, true)));

            s.engine = EngineState.Running; s.onGround = true; s.sys.throttle = 0;
            var idle = Find(CockpitSwitches.Build(s, ac, Autopilot.NewAP(), false), GameAction.Engine);
            Assert.That((idle.label, idle.on, idle.available), Is.EqualTo(("ENG OFF", true, true)));

            s.sys.throttle = 1;
            var open = Find(CockpitSwitches.Build(s, ac, Autopilot.NewAP(), false), GameAction.Engine);
            Assert.That((open.label, open.available), Is.EqualTo(("ENGINE", false)), "no shutting down with the throttle open");

            s.sys.throttle = 0; s.onGround = false;
            var flying = Find(CockpitSwitches.Build(s, ac, Autopilot.NewAP(), false), GameAction.Engine);
            Assert.That((flying.label, flying.available), Is.EqualTo(("ENGINE", false)), "nor in the air");
        }

        [Test]
        public void TheThrottleSwitchAsksForTheOtherEnd()
        {
            var ac = Aircraft.Get("guardian");
            var s = Cold("guardian");
            s.engine = EngineState.Off;
            var off = CockpitSwitches.Build(s, ac, Autopilot.NewAP(), false).Single(x => x.label.StartsWith("THR"));
            Assert.False(off.available, "not with the engine off");
            s.engine = EngineState.Running; s.sys.throttleTarget = 0;
            var idle = CockpitSwitches.Build(s, ac, Autopilot.NewAP(), false).Single(x => x.label.StartsWith("THR"));
            Assert.That((idle.label, idle.action), Is.EqualTo(("THR OPEN", GameAction.ThrottleUp)));
            s.sys.throttleTarget = 1;
            var open = CockpitSwitches.Build(s, ac, Autopilot.NewAP(), false).Single(x => x.label.StartsWith("THR"));
            Assert.That((open.label, open.action, open.on), Is.EqualTo(("THR IDLE", GameAction.ThrottleDown, true)));
        }

        [Test]
        public void TheLampsShowTheSystems()
        {
            var ac = Aircraft.Get("guardian");
            var s = Cold("guardian");
            s.sys.beacon = true; s.sys.landingLight = true;
            var ap = Autopilot.NewAP();
            ap.hdg = true;
            var all = CockpitSwitches.Build(s, ac, ap, true);
            foreach (var a in new[] { GameAction.Beacon, GameAction.LandingLight, GameAction.ApHdg, GameAction.Trim }) Assert.True(Find(all, a).on, a.ToString());
            foreach (var a in new[] { GameAction.NavLights, GameAction.Strobe, GameAction.Searchlight, GameAction.ApAlt }) Assert.False(Find(all, a).on, a.ToString());
            Assert.True(Find(all, GameAction.TrimRelease).available);
            Assert.False(Find(CockpitSwitches.Build(s, ac, ap, false), GameAction.TrimRelease).available, "nothing to release");
        }
    }
}
