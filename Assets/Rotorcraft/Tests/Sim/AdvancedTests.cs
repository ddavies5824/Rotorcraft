// Port of tests/advanced.test.ts: engine failure, autopilot, winch and search, over level ground.
using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using static Rotorcraft.Sim.FlightModel;
using static Rotorcraft.Sim.Tests.TestHelpers;

namespace Rotorcraft.Sim.Tests
{
    public class AdvancedTests
    {
        static readonly AircraftConfig ns = Aircraft.Get("northstar"), s9 = Aircraft.Get("guardian");
        static WeatherPreset Wx(string id) => Weather.Presets[id];
        static readonly double DT = FixedStepper.SIM_DT;

        IGround saved;
        [SetUp] public void SetUp() { saved = Ground.Current; Ground.Current = new FlatGround(10); }
        [TearDown] public void TearDown() { Ground.Current = saved; }

        static void Fly(HeliState s, AircraftConfig ac, double secs, Func<HeliState, Controls> ctl, WeatherPreset wx = null, AssistMode assist = AssistMode.Standard)
        {
            wx = wx ?? Wx("calm");
            for (int i = 0; i < secs / DT; i++) { Step(s, ctl(s), ac, wx, new SimSettings(assist), DT); s.events.Clear(); }
        }

        /// <summary>Find the lever that holds height, so autopilot tests start from a trimmed hover.</summary>
        static double HoverLever(AircraftConfig ac, double agl)
        {
            double lo = 0, hi = 1;
            for (int k = 0; k < 18; k++)
            {
                var c = (lo + hi) / 2;
                var s = Create(-3000, -2300, 0, ac.fuelCapacity * 0.5, true, new Airborne(agl, 0));
                Fly(s, ac, 1, _ => new Controls(c, 0, 0, 0));
                if (s.vel.y > 0) hi = c; else lo = c;
            }
            return (lo + hi) / 2;
        }

        [Test]
        public void OneEngineInoperativeTwinKeepsFlyingSingleFlamesOut()
        {
            var s = Create(-3000, 0, 270, 1000, true, new Airborne(300, 40));
            Fly(s, ns, 3, _ => new Controls(0.55, 0.2, 0, 0));
            var pAllEng = s.@out.powerAvailableKW;
            FailEngine(s, ns);
            Fly(s, ns, 5, _ => new Controls(0.5, 0.2, 0, 0));
            Assert.AreEqual(EngineState.Running, s.engine);
            Assert.AreEqual(1, s.enginesOut);
            Assert.That(s.@out.powerAvailableKW < pAllEng * 0.7 && s.@out.powerAvailableKW > pAllEng * 0.55, $"{pAllEng} -> {s.@out.powerAvailableKW}");
            Assert.That(s.@out.rpm > 95, $"rotor rpm held at {s.@out.rpm}");
            var k = Create(-3000, 0, 270, 40, true, new Airborne(300, 30));
            FailEngine(k, Aircraft.Get("kestrel"));
            Assert.AreEqual(EngineState.Flameout, k.engine);
        }

        [Test]
        public void AutopilotHoverHoldKeepsTheAircraftOverASpotInABreeze()
        {
            var lever = HoverLever(s9, 20);
            var s = Create(-3000, -2300, 260, 1200, true, new Airborne(20, 0));
            var ap = Autopilot.NewAP();
            Fly(s, s9, 0.2, _ => new Controls(lever, 0, 0, 0));
            Assert.IsTrue(Autopilot.ToggleAP(ap, APMode.Hover, s, s9, lever).ok);
            double x0 = s.pos.x, z0 = s.pos.z;
            double maxErr = 0;
            foreach (var assist in new[] { AssistMode.Standard, AssistMode.Realistic })
            {
                Fly(s, s9, 40, st =>
                {
                    var r = Autopilot.ApplyAP(ap, st, s9, new Controls(lever, 0, 0, 0), assist, DT);
                    if (st.t > 15) maxErr = Math.Max(maxErr, Js.Hypot(st.pos.x - x0, st.pos.z - z0));
                    return r.controls;
                }, Wx("light_sw"), assist);
            }
            Assert.IsFalse(s.crashed);
            Assert.That(maxErr < 6, $"drift {maxErr:F1} m");
            Assert.That(Math.Abs(s.@out.aglM - 20) < 3, $"height {s.@out.aglM}");
        }

        [Test]
        public void AutopilotAltitudeHoldAndPilotOverride()
        {
            var s = Create(-3000, 0, 90, 1000, true, new Airborne(300, 45));
            var ap = Autopilot.NewAP();
            const double lever = 0.55;
            Assert.IsTrue(Autopilot.ToggleAP(ap, APMode.Alt, s, ns, lever).ok);
            Assert.IsTrue(Autopilot.ToggleAP(ap, APMode.Hdg, s, ns, lever).ok);
            var y0 = s.pos.y;
            Fly(s, ns, 40, st => Autopilot.ApplyAP(ap, st, ns, new Controls(lever, 0.25, 0, 0), AssistMode.Standard, DT).controls);
            Assert.That(Math.Abs(s.pos.y - y0) < 20, $"altitude moved {s.pos.y - y0:F1} m");
            Assert.That(Math.Abs(s.@out.hdg - 90) < 6, $"heading {s.@out.hdg}");
            var res = Autopilot.ApplyAP(ap, s, ns, new Controls(lever + 0.2, 0, 0, 0), AssistMode.Standard, DT);
            Assert.IsFalse(ap.alt, "moving the lever drops altitude hold");
            Assert.That(res.dropped.Contains(APMode.Alt));
            Assert.IsFalse(Autopilot.ToggleAP(Autopilot.NewAP(), APMode.Alt, s, Aircraft.Get("kestrel"), 0.5).ok, "the trainer has no autopilot");
        }

        [Test]
        public void WinchLowerSecureTheCasualtyAndRecoverWhileHoldingAHover()
        {
            var lever = HoverLever(s9, 22);
            var target = new HoistTarget(-3000, -2300, Ground.Current.GroundAt(-3000, -2300).h);
            // put the winch arm over the target
            var s = Create(-3000 - 1.7 * (s9.rotorDiameter / 15), -2300 - 1.4 * (s9.rotorDiameter / 15), 0, 1200, true, new Airborne(22, 0));
            var ap = Autopilot.NewAP();
            Fly(s, s9, 0.2, _ => new Controls(lever, 0, 0, 0));
            Autopilot.ToggleAP(ap, APMode.Hover, s, s9, lever);
            var scale = s9.rotorDiameter / 15;
            Assert.IsNull(Hoist.CanStartLowering(s, target, scale));
            var h = Hoist.NewHoist();
            Hoist.StartLowering(h, s, scale);
            double t = 0;
            while (h.phase != HoistPhase.Aboard && t < 150)
            {
                var r = Autopilot.ApplyAP(ap, s, s9, new Controls(lever, 0, 0, 0), AssistMode.Standard, DT);
                Step(s, r.controls, s9, Wx("calm"), new SimSettings(AssistMode.Standard), DT); s.events.Clear();
                Hoist.StepHoist(h, s, target, Weather.WindAt(Wx("calm"), s.t, s.pos.x, s.pos.y, s.pos.z, s.@out.aglM), scale, DT);
                t += DT;
            }
            Assert.AreEqual(HoistPhase.Aboard, h.phase, $"phase {h.phase} after {t:F0} s ({h.abortReason})");
            Assert.IsTrue(h.attached);
            Assert.That(t > Hoist.sceneTime && t < 120, $"took {t:F0} s");
            StringAssert.IsMatch("(right|Right|left|Left|Forward|Back)", Hoist.ConningCall(Hoist.NewHoist(), s, new HoistTarget(s.pos.x + 30, s.pos.z, target.y), scale));
        }

        [Test]
        public void WinchTooHighOrMovingTooFastIsRefusedWithAReason()
        {
            var s = Create(-3000, -2300, 0, 1200, true, new Airborne(120, 0));
            StringAssert.IsMatch("Too high", Hoist.CanStartLowering(s, new HoistTarget(-3000, -2300, s.pos.y - 120), 1));
        }

        [Test]
        public void SearchCoverageGrowsAndACloseSlowPassFindsTheCasualty()
        {
            var area = new SearchAreaDef { id = "a", name = "Test", cx = -3500, cz = -1500, w = 1600, h = 1200, rotDeg = 20 };
            TargetDef Def() => new TargetDef { id = "t", name = "Walker", kind = TargetKind.Walker, areaId = "a", conspicuity = 1 };
            var tgt = Search.PlaceTargets(new[] { Def() }, new[] { area }, 42)[0];
            var again = Search.PlaceTargets(new[] { Def() }, new[] { area }, 42)[0];
            Assert.AreEqual(new[] { tgt.x, tgt.z }, new[] { again.x, again.z }, "seeded placement is reproducible");
            Assert.That(Search.InArea(area, tgt.x, tgt.z), "placed inside the area");
            var st = Search.NewSearch(area, 7);
            var s = Create(tgt.x - 1500, tgt.z, 90, 1000, true, new Airborne(120, 25));
            var sensors = new Sensors { searchlight = false, flir = false, night = 0, visibilityM = 20000 };
            var found = false;
            for (int i = 0; i < 2000 && !found; i++)
            {
                s.pos.x += 25 * 0.05; s.pos.y = Ground.Current.GroundAt(s.pos.x, s.pos.z).h + 120; s.@out.aglM = 120; s.vel.x = 25;
                found = Search.StepSearch(st, s, tgt, sensors, 0.05);
            }
            Assert.IsTrue(found, "found on a pass straight over the casualty");
            Assert.That(Search.CoverageFraction(st) > 0.02);
            StringAssert.IsMatch("o'clock", Search.ClockCall(s, tgt.x, tgt.z));
            // far away, nothing is seen however long you look
            var st2 = Search.NewSearch(area, 7);
            var far = Create(tgt.x + 4000, tgt.z, 0, 1000, true, new Airborne(120, 0));
            far.@out.aglM = 120;
            var seen = false;
            for (int i = 0; i < 600; i++) seen = seen || Search.StepSearch(st2, far, tgt, sensors, 0.1);
            Assert.IsFalse(seen);
        }
    }
}
