// Port of the mission-engine and assessment parts of tests/game.test.ts and tests/walk.test.ts, flown over the
// real Kinloch world.
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Rotorcraft.Sim;
using Rotorcraft.Sim.Tests;
using Rotorcraft.World;
using static Rotorcraft.Sim.FlightModel;

namespace Rotorcraft.Game.Tests
{
    public class MissionTests
    {
        static readonly AircraftConfig ac = Aircraft.Get("kestrel");
        static readonly Controls idle = Controls.Idle;
        IGround saved;
        [SetUp] public void SetUp() { saved = Ground.Current; Ground.Current = new WorldGround(); }
        [TearDown] public void TearDown() { Ground.Current = saved; }

        /// <summary>tests/helpers.ts runFor, with the mission updated after each step.</summary>
        static List<SimEvent> RunFor(HeliState s, double seconds, Func<HeliState, Controls> ctl, WeatherPreset wx, MissionRuntime mission = null, AssistMode assist = AssistMode.Beginner, Func<HeliState, bool> until = null)
        {
            var all = new List<SimEvent>();
            var n = (int)Js.Round(seconds / FixedStepper.SIM_DT);
            for (int i = 0; i < n; i++)
            {
                var c = ctl(s);
                Step(s, c, ac, wx, new SimSettings(assist), FixedStepper.SIM_DT);
                var ev = s.events.ToList(); s.events.Clear();
                all.AddRange(ev);
                mission?.Update(s, ev, FixedStepper.SIM_DT, c);
                if (until != null && until(s)) break;
            }
            return all;
        }

        static HeliState At(double x, double z, double hdg, double fuel, bool running = false, Airborne air = null) =>
            CreateState(new CreateOptions { x = x, z = z, headingDeg = hdg, fuel = fuel, engineRunning = running, airborne = air });

        static (FlightResult result, HeliState s) FlyEx01(bool precise = true)
        {
            var def = Missions.ById("ex01_controls");
            var site = WorldMap.SiteById(def.startSiteId);
            var s = At(site.x, site.z, def.startHeadingDeg, 45);
            var m = new MissionRuntime(def, ac, s.fuel, true);
            var pilot = new TestPilot(ac);
            var wx = Weather.Presets[def.weatherId];
            CommandSwitch(s, SwitchName.Beacon, true);
            CommandEngine(s, EngineCommand.Start);
            CommandThrottle(s, 1);
            foreach (var e in s.events.ToList()) m.Update(s, new[] { e }, 0, idle);
            s.events.Clear();
            RunFor(s, 25, _ => idle, wx, m); // spool up
            pilot.collective = 0.35;
            RunFor(s, 30, st => pilot.ControlsFor(st, new PilotTarget(site.x, site.z, 2)), wx, m, AssistMode.Beginner, _ => m.Active?.def.kind == ObjectiveKind.Land);
            RunFor(s, 40, st => pilot.ControlsFor(st, new PilotTarget(site.x, site.z, 0, descend: precise)), wx, m, AssistMode.Beginner, _ => m.Active?.def.kind == ObjectiveKind.Shutdown);
            // shutdown: lever down, throttle to idle, cool down, engine off
            CommandThrottle(s, 0);
            RunFor(s, 17, _ => idle, wx, m);
            CommandEngine(s, EngineCommand.Stop);
            RunFor(s, 3, _ => idle, wx, m, AssistMode.Beginner, _ => m.status == MissionStatus.Complete);
            return (m.Finish(s, FlightEnd.Complete), s);
        }

        [Test]
        public void Exercise1FlownByAnAutopilotPassesEndToEnd()
        {
            var (result, _) = FlyEx01();
            Assert.That(result.objectives.All(o => o.status == ObjectiveStatus.Complete), string.Join(", ", result.objectives.Select(o => $"{o.label}: {o.status} {o.detail}")));
            Assert.AreEqual(6, result.objectives.Count);
            Assert.AreEqual(100, result.scores[ScoreCategory.Procedures], string.Join("; ", result.warnings));
            Assert.AreEqual(FlightOutcome.Passed, result.outcome, string.Join("; ", result.failedCriteria));
            Assert.That(result.scores[ScoreCategory.Landing] >= 70, $"landing {result.scores[ScoreCategory.Landing]}");
            Assert.That(result.flightSeconds > 10);
            Assert.That(result.fuelUsedKg > 0);
        }

        [Test]
        public void ObjectivesCannotCompleteOutOfOrder()
        {
            var s = At(0, 0, 0, 45, running: true);
            var m = new MissionRuntime(Missions.ById("ex01_controls"), ac, s.fuel, true);
            // Engine is already running but the mission is at 'engineStart'; lifting off must not skip ahead past the hover.
            m.Update(s, new SimEvent[0], 0.01, idle);
            Assert.AreEqual(ObjectiveKind.LiftOff, m.Active?.def.kind, "start and RPM complete from state; lift-off still requires being airborne");
        }

        [Test]
        public void AbortingEarlyYieldsAnAbortedNonPassingResult()
        {
            var s = At(0, 0, 0, 45);
            var m = new MissionRuntime(Missions.ById("ex02_hover"), ac, s.fuel, false);
            var r = m.Finish(s, FlightEnd.Aborted);
            Assert.AreEqual(FlightOutcome.Aborted, r.outcome);
            Assert.IsFalse(r.pass);
            Assert.That(r.warnings.Any(w => w.Contains("checklist")));
        }

        [Test]
        public void ACrashFailsTheMissionWithAReason()
        {
            var s = At(0, 0, 0, 45, true, new Airborne(60, 0));
            var m = new MissionRuntime(Missions.ById("ex01_controls"), ac, s.fuel, true);
            RunFor(s, 20, _ => idle, Weather.Presets["calm"], m, AssistMode.Beginner, st => st.crashed);
            var r = m.Finish(s, FlightEnd.Crash);
            Assert.AreEqual(FlightOutcome.Crashed, r.outcome);
            Assert.AreEqual(0, r.overall);
            Assert.IsNotNull(r.crashReason);
        }

        [Test]
        public void LandingScoringThresholds()
        {
            var smooth = Assessment.ScoreLanding(new LandingInput { vsMs = 0.2, gsMs = 0.2, roll = 1, pitch = 1, distM = 0.5, maxDistM = 6 });
            var hard = Assessment.ScoreLanding(new LandingInput { vsMs = 2.5, gsMs = 2, roll = 6, pitch = 4, distM = 5, maxDistM = 6 });
            Assert.That(smooth.score >= 95);
            Assert.AreEqual("A", Assessment.LandingGrade(smooth.score));
            Assert.That(hard.score < 50, $"{hard.score}");
            Assert.AreEqual((int)Js.Round((100 * 1.5 + 50 * 1.2) / 2.7), Assessment.OverallScore(new Dictionary<ScoreCategory, int> { [ScoreCategory.Landing] = 100, [ScoreCategory.Handling] = 50 }));
        }

        [Test]
        public void HoverMarkingDeadStillScoresHighWanderingScoresLower()
        {
            var still = Assessment.ScoreHover(new HoverStats { drift = 0.05, offset = 2.5, radiusM = 5, heightWander = 0.05, outOfBand = 0, halfBandM = 1.5, yawWander = 0.5, hdgTolDeg = 20, resets = 0 });
            Assert.That(still >= 92, $"still: {still}");
            var wobbly = Assessment.ScoreHover(new HoverStats { drift = 1.8, offset = 1, radiusM = 5, heightWander = 0.8, outOfBand = 0.3, halfBandM = 1.5, yawWander = 8, hdgTolDeg = 20, resets = 1 });
            Assert.That(wobbly < 70 && wobbly > 20, $"wobbly: {wobbly}");
        }

        [Test]
        public void StartingWithoutTheBeaconAndShuttingDownHotAreRecorded()
        {
            var s = At(0, 0, 0, 45);
            var m = new MissionRuntime(Missions.ById("ex01_controls"), ac, s.fuel, true);
            CommandEngine(s, EngineCommand.Start);
            CommandThrottle(s, 1);
            RunFor(s, 30, _ => idle, Weather.Presets["calm"], m);
            CommandEngine(s, EngineCommand.Stop);
            RunFor(s, 1, _ => idle, Weather.Presets["calm"], m);
            var st = string.Join(" | ", m.Stats.proceduralErrors);
            Assert.That(st.Contains("beacon"), st);
            Assert.That(st.Contains("cool-down"), st);
        }

        /// <summary>Seconds counted as inadvertent cloud while a Northstar flies 4 s at about 3,100 ft over the sea, in cloud for that mission's weather (or the one given).</summary>
        static double ImcSeconds(string id, string weatherId = null)
        {
            var def = Missions.ById(id);
            var ns = Aircraft.Get("northstar");
            var site = WorldMap.SiteById(def.startSiteId);
            var s = At(site.x + 3000, site.z, 90, 600, true, new Airborne(950, 40));
            var m = new MissionRuntime(def, ns, s.fuel, true, TimeOfDay.Day, new MissionOptions { weatherId = weatherId });
            var wx = Weather.Presets[weatherId ?? def.weatherId];
            for (int i = 0; i < 4 / FixedStepper.SIM_DT; i++)
            {
                Step(s, idle, ns, wx, new SimSettings(AssistMode.Beginner), FixedStepper.SIM_DT);
                var ev = s.events.ToList(); s.events.Clear();
                m.Update(s, ev, FixedStepper.SIM_DT, idle);
            }
            Assert.That(s.@out.altFt, Is.GreaterThan(wx.cloudBaseFt.Value + 50), $"{id}: still up in the cloud");
            Assert.That(m.Hood, Is.False, $"{id}: hood down");
            return m.Stats.imcSec;
        }

        [Test]
        public void InstrumentLessonsGoIntoCloudWithoutItCountingAsInadvertent()
        {
            // the exercises climb above the broken cloud and take the hood off there: the instructor or examiner is in command
            var ns = Aircraft.Get("northstar");
            foreach (var def in Missions.All.Where(d => d.pathway == Pathway.Ir))
                Assert.That(new MissionRuntime(def, ns, 600, true).CloudAllowed, def.id);
            Assert.That(new MissionRuntime(Missions.ById("tr_ns175"), ns, 600, true).CloudAllowed, Is.False, "a type rating, without the instrument rating");
            Assert.That(new MissionRuntime(Missions.ById("tr_ns175"), ns, 600, true, TimeOfDay.Day, new MissionOptions { instrumentRated = true }).CloudAllowed, "once rated");
            // flown: up in the cloud on the way out to the first fix, hood down
            Assert.That(ImcSeconds("ir2_gps_approach"), Is.EqualTo(0), "GPS approach");
            // the same aircraft and cloud on any other flight, without the rating: still inadvertent
            Assert.That(ImcSeconds("tr_ns175", "breezy_w"), Is.GreaterThan(2), "type rating flight in cloud");
        }

        /// <summary>
        /// The three holds of Basic Instruments, flown as asked: a 500 ft/min climb or a rate-one turn onto each, then held with the
        /// given wander either side. Returns the instrument score and each hold's line.
        /// </summary>
        static (int instrument, string holds) FlyBasicInstrumentHolds(double altWander, double hdgWander)
        {
            var def = Missions.ById("ir1_basic_instruments");
            var ns = Aircraft.Get("northstar");
            var site = WorldMap.SiteById(def.startSiteId);
            var s = At(site.x + 3000, site.z, 90, 600, true, new Airborne(490, 40));
            var m = new MissionRuntime(def, ns, s.fuel, true);
            Step(s, idle, ns, Weather.Presets["calm"], new SimSettings(AssistMode.Beginner), FixedStepper.SIM_DT);   // fills in the outputs
            s.events.Clear();
            const double dt = 0.1;
            double alt = 1600, hdg = 90;
            void Tick()
            {
                s.t += dt; s.onGround = false;
                s.@out.altFt = alt + altWander * Math.Sin(s.t * 0.4);
                s.psi = (hdg + hdgWander * Math.Sin(s.t * 0.3 + 1)) * Math.PI / 180;
                m.Update(s, new SimEvent[0], dt, idle);
            }
            for (int i = 0; i < 100 && m.Active?.def.kind != ObjectiveKind.Hold; i++) Tick();
            var targets = new[] { (2000.0, 90.0), (2000.0, 180.0), (3000.0, 270.0) };
            foreach (var (toAlt, toHdg) in targets)
            {
                var hold = m.Active;
                Assert.That(hold?.def.kind, Is.EqualTo(ObjectiveKind.Hold), "the next hold is up");
                for (int i = 0; i < 4000 && hold.status != ObjectiveStatus.Complete; i++)
                {
                    alt += Math.Sign(toAlt - alt) * Math.Min(Math.Abs(toAlt - alt), 500.0 / 60 * dt);   // 500 ft/min
                    hdg += Math.Sign(toHdg - hdg) * Math.Min(Math.Abs(toHdg - hdg), 3 * dt);             // rate one
                    Tick();
                }
                Assert.That(hold.status, Is.EqualTo(ObjectiveStatus.Complete), hold.def.label);
            }
            var r = m.Finish(s, FlightEnd.Aborted);
            return (r.scores[ScoreCategory.Instrument], string.Join("; ", r.objectives.Where(o => o.label.StartsWith("Hood")).Select(o => o.detail)));
        }

        [Test]
        public void HoldsAreMarkedOnHowTheyAreHeldNotOnTheTurnOrClimbOntoThem()
        {
            // the turn to 180° and the climb to 3,000 ft used to count as error, so a clean flight scored 40, 0 and 0: 13%
            var (clean, cleanHolds) = FlyBasicInstrumentHolds(15, 1);
            Assert.That(clean, Is.GreaterThanOrEqualTo(85), cleanHolds);
            var (sloppy, sloppyHolds) = FlyBasicInstrumentHolds(110, 7);
            Assert.That(sloppy, Is.LessThan(clean - 20), $"wandering about the band still costs: {sloppyHolds}");
            Assert.That(sloppy, Is.GreaterThanOrEqualTo(30), $"but held inside it is not a fail: {sloppyHolds}");
        }

        [Test]
        public void EveryMissionIsWellFormed()
        {
            foreach (var m in Missions.All)
            {
                Assert.IsNotNull(WorldMap.FindSite(m.startSiteId), $"{m.id}: start site {m.startSiteId}");
                foreach (var o in m.objectives)
                    if (o.siteId != null) Assert.IsNotNull(WorldMap.FindSite(o.siteId), $"{m.id}: {o.label} site {o.siteId}");
                var last = m.objectives[m.objectives.Length - 1].kind;
                Assert.That(last == ObjectiveKind.Land || last == ObjectiveKind.Shutdown, m.id);
                Assert.That(Weather.Presets.ContainsKey(m.weatherId), m.weatherId);
                Assert.IsNotNull(Aircraft.Get(m.aircraftId));
            }
            Assert.AreEqual(Missions.All.Count, Missions.All.Select(m => m.id).Distinct().Count(), "mission ids are unique");
        }
    }
}
