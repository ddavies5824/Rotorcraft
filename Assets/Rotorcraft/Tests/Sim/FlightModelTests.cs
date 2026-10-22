// Port of tests/flightModel.test.ts. The game's tests fly over its real terrain; these fly over level
// ground (FlatGround), so they check the flight model on its own.
using System;
using System.Linq;
using NUnit.Framework;
using static Rotorcraft.Sim.FlightModel;
using static Rotorcraft.Sim.Tests.TestHelpers;

namespace Rotorcraft.Sim.Tests
{
    public class FlightModelTests
    {
        static readonly AircraftConfig ac = Aircraft.Get("kestrel");
        static readonly Controls idle = Controls.Idle;
        static WeatherPreset Wx(string id) => Weather.Presets[id];

        IGround saved;
        [SetUp] public void SetUp() { saved = Ground.Current; Ground.Current = new FlatGround(10); }
        [TearDown] public void TearDown() { Ground.Current = saved; }

        [Test]
        public void ControlInputsAreClampedAndNaNSafe()
        {
            var c = SanitizeControls(new Controls(3, double.NaN, -5, double.PositiveInfinity));
            Assert.AreEqual(new Controls(1, 0, -1, 0), c);
        }

        [Test]
        public void EngineStartSpinsTheRotorUpTo100AndCannotDoubleStart()
        {
            var s = Create(0, 0, 0, 50);
            Assert.IsTrue(CommandEngine(s, EngineCommand.Start));
            Assert.IsFalse(CommandEngine(s, EngineCommand.Start));
            RunFor(s, 30, _ => idle, ac, Wx("calm"));
            Assert.AreEqual(EngineState.Running, s.engine);
            Assert.That(s.@out.rpm > 45 && s.@out.rpm < 80, $"idle rpm {s.@out.rpm}");
            CommandThrottle(s, 1);
            RunFor(s, 15, _ => idle, ac, Wx("calm"));
            Assert.That(s.@out.rpm > 99 && s.@out.rpm < 101, $"rpm {s.@out.rpm}");
            Assert.IsTrue(s.onGround, "stays on the ground with collective down");
        }

        [Test]
        public void CannotStartWithNoFuel()
        {
            var s = Create(0, 0, 0, 0);
            Assert.IsFalse(CommandEngine(s, EngineCommand.Start));
        }

        [Test]
        public void CollectiveControlsVerticalPerformance()
        {
            var s = Create(0, 0, 0, 50, engineRunning: true);
            RunFor(s, 3, _ => idle.WithCollective(0.3), ac, Wx("calm"));
            Assert.IsTrue(s.onGround, "0.3 collective is below hover power");
            RunFor(s, 4, _ => idle.WithCollective(0.8), ac, Wx("calm"));
            Assert.IsFalse(s.onGround);
            Assert.That(s.vel.y > 1, $"climbing at {s.vel.y}");
            RunFor(s, 3, _ => idle.WithCollective(0.3), ac, Wx("calm"));
            Assert.That(s.vel.y < -0.5, $"descending at {s.vel.y}");
        }

        [Test]
        public void ForwardCyclicAcceleratesGraduallyNotInstantly()
        {
            var s = Create(0, 0, 0, 50, true, new Airborne(100, 0));
            var pilot = new TestPilot(ac) { collective = 0.5 };
            Func<HeliState, Controls> ctl = st => pilot.ControlsFor(st, new PilotTarget(st.pos.x, st.pos.z, 100)).WithCyclicPitch(0.5);
            RunFor(s, 0.5, ctl, ac, Wx("calm"), AssistMode.Standard);
            var v1 = Js.Hypot(s.vel.x, s.vel.z);
            RunFor(s, 8, ctl, ac, Wx("calm"), AssistMode.Standard);
            var v2 = Js.Hypot(s.vel.x, s.vel.z);
            Assert.That(v1 < 2, $"after 0.5 s only {v1:F2} m/s");
            Assert.That(v2 > 10, $"after 8.5 s {v2:F2} m/s");
            Assert.That(s.vel.z < 0, "moving north (heading 000)");
        }

        [Test]
        public void PedalsYawTheAircraftWithAFiniteRate()
        {
            var s = Create(0, 0, 0, 50, true, new Airborne(50, 0));
            var pilot = new TestPilot(ac) { collective = 0.5 };
            RunFor(s, 1, st => pilot.ControlsFor(st, new PilotTarget(0, 0, 50)).WithPedals(1), ac, Wx("calm"), AssistMode.Standard);
            var h1 = s.@out.hdg;
            Assert.That(h1 > 20 && h1 < 120, $"heading after 1 s full right pedal: {h1}");
        }

        [Test]
        public void TestPilotHoldsAHoverWithin2MetresFor30SecondsInALightWind()
        {
            var s = Create(0, 0, 300, 50, engineRunning: true);
            var pilot = new TestPilot(ac);
            double maxErr = 0;
            RunFor(s, 40, st => { if (st.t > 10) maxErr = Math.Max(maxErr, Js.Hypot(st.pos.x, st.pos.z)); return pilot.ControlsFor(st, new PilotTarget(0, 0, 2)); }, ac, Wx("light_sw"));
            Assert.IsFalse(s.crashed);
            Assert.That(Math.Abs(s.@out.aglM - 2) < 0.6, $"agl {s.@out.aglM}");
            Assert.That(maxErr < 2.5, $"max position error {maxErr}");
        }

        [Test]
        public void FuelBurnsWithPowerAndTheEngineFlamesOutWhenEmpty()
        {
            var s = Create(0, 0, 0, 1, engineRunning: true);
            var m0 = MassOf(s, ac);
            var pilot = new TestPilot(ac);
            var ev = RunFor(s, 200, st => pilot.ControlsFor(st, new PilotTarget(0, 0, 1.5)), ac, Wx("calm"), AssistMode.Beginner, st => st.engine == EngineState.Flameout);
            Assert.AreEqual(EngineState.Flameout, s.engine);
            Assert.That(ev.Any(e => e.type == SimEventType.Engine && e.state == EngineState.Flameout));
            Assert.That(MassOf(s, ac) < m0);
        }

        [Test]
        public void OverPitchingBeyondAvailablePowerDroopsRotorRpm()
        {
            var s = Create(0, 0, 0, 50, true, new Airborne(100, 0));
            RunFor(s, 4, _ => idle.WithCollective(1), ac, Wx("calm"));
            Assert.That(s.@out.rpm < 95, $"rpm {s.@out.rpm}");
            Assert.That(s.@out.torque > 100, $"torque {s.@out.torque}");
        }

        [Test]
        public void AutorotationRotorRpmIsSustainedByDescent()
        {
            var s = Create(-2000, 0, 270, 50, true, new Airborne(450, 28));
            s.throttleIdle = true;
            double minRpm = 200;
            // a pilot managing rotor rpm with collective while holding ~55 kt
            var col = 0.1;
            RunFor(s, 25, st =>
            {
                col = Math.Min(1, Math.Max(0, col + (st.@out.rpm - 100) * 0.0008));
                minRpm = Math.Min(minRpm, st.@out.rpm);
                return new Controls(col, (st.@out.iasKt - 55) * -0.01 + 0.15, 0, 0);
            }, ac, Wx("calm"), AssistMode.Standard, st => st.@out.aglM < 60);
            Assert.That(minRpm > 85, $"min rpm {minRpm}");
            Assert.That(s.vel.y < -2, "descending");
        }

        [Test]
        public void AGentleLandingIsSmoothAFastOneCrashes()
        {
            var s = Create(0, 0, 0, 50, true, new Airborne(3, 0));
            var pilot = new TestPilot(ac) { collective = 0.47 };
            var ev = RunFor(s, 30, st => pilot.ControlsFor(st, new PilotTarget(0, 0, 0, descend: true)), ac, Wx("calm"), AssistMode.Beginner, st => st.onGround && st.t > 2);
            var td = ev.FirstOrDefault(e => e.type == SimEventType.Touchdown);
            Assert.IsNotNull(td);
            Assert.That(td.vs < 0.6, $"vs {td.vs}");
            Assert.IsFalse(s.crashed);
            Assert.IsFalse(double.IsNaN(s.pos.y));
            var s2 = Create(0, 0, 0, 50, true, new Airborne(40, 0));
            var ev2 = RunFor(s2, 20, _ => idle.WithCollective(0), ac, Wx("calm"), AssistMode.Beginner, st => st.onGround);
            Assert.IsTrue(s2.crashed, "dropping from 40 m with collective down is an accident");
            Assert.That(ev2.Any(e => e.type == SimEventType.Crash));
        }

        [Test]
        public void AircraftNeverSinksThroughTheGroundWhileParked()
        {
            var s = Create(0, 0, 0, 50, engineRunning: true);
            var h0 = s.pos.y;
            RunFor(s, 10, _ => new Controls(0.2, 0.5, 0, 0), ac, Wx("breezy_w"));
            Assert.That(Math.Abs(s.pos.y - h0) < 1e-6);
            Assert.That(Js.Hypot(s.pos.x, s.pos.z) < 0.5, "skids hold it in place");
        }

        [Test]
        public void SimulationIsDeterministicAndFrameRateIndependent()
        {
            HeliState Run(double frame)
            {
                var s = Create(0, 0, 45, 50, engineRunning: true);
                var c = new Controls(0.7, 0.2, -0.1, 0.1);
                var st = new FixedStepper(dt => Step(s, c, ac, Wx("breezy_w"), new SimSettings(AssistMode.Standard), dt));
                double t = 0;
                while (t < 10 - 1e-9) { st.Advance(frame); t += frame; }
                return s;
            }
            HeliState a = Run(1.0 / 30), b = Run(1.0 / 60), c2 = Run(1.0 / 144);
            foreach (var x in new[] { b, c2 })
                Assert.That(Math.Abs(a.pos.x - x.pos.x) < 0.2 && Math.Abs(a.pos.y - x.pos.y) < 0.2 && Math.Abs(a.pos.z - x.pos.z) < 0.2, $"positions differ {a.pos.x},{x.pos.x}");
        }

        [Test]
        public void FixedStepperCapsLongFramesInsteadOfExploding()
        {
            var n = 0;
            var st = new FixedStepper(_ => n++);
            st.Advance(5); // e.g. returning from a background tab
            Assert.That(n <= Math.Ceiling(0.1 / FixedStepper.SIM_DT) + 1, $"steps {n}");
            Assert.That(st.droppedTime > 4.8);
            st.Advance(double.NaN);
        }

        [Test]
        public void ThrottleAtIdleCannotSustainFlightRpmGovernorOffMakesRpmFollowThrottle()
        {
            var s = Create(0, 0, 0, 50, true, new Airborne(60, 0));
            CommandSwitch(s, SwitchName.Governor, false);
            var pilot = new TestPilot(ac) { collective = 0.5 };
            RunFor(s, 6, st => pilot.ControlsFor(st, new PilotTarget(0, 0, 60)), ac, Wx("calm"));
            var rpmOpen = s.@out.rpm;
            CommandThrottle(s, 0.75);
            RunFor(s, 6, st => pilot.ControlsFor(st, new PilotTarget(0, 0, 60)), ac, Wx("calm"));
            Assert.That(s.@out.rpm < rpmOpen - 3, $"rpm {rpmOpen} -> {s.@out.rpm}");
        }

        [Test]
        public void VortexRingStateLosesThrustAndBuffets()
        {
            var s = Create(-3000, 0, 0, 50, true, new Airborne(400, 0));
            double maxVrs = 0;
            RunFor(s, 12, st => { maxVrs = Math.Max(maxVrs, st.@out.vrs); return new Controls(st.t < 4 ? 0.2 : 0.55, 0, 0, 0); }, ac, Wx("calm"));
            Assert.That(maxVrs > 0.5, $"vrs {maxVrs}");
            // flying forward out of it clears the condition
            RunFor(s, 10, _ => new Controls(0.55, 0.6, 0, 0), ac, Wx("calm"), AssistMode.Standard);
            Assert.That(s.@out.vrs < 0.05, $"vrs after recovery {s.@out.vrs}");
        }

        [Test]
        public void EngineTemperatureRisesWithPowerAndLightsAreSwitchable()
        {
            var s = Create(0, 0, 0, 50, true, new Airborne(30, 0));
            var t0 = s.engineTemp;
            RunFor(s, 60, _ => new Controls(0.9, 0, 0, 0), ac, Wx("calm"), AssistMode.Beginner, st => st.@out.aglM > 300);
            Assert.That(s.engineTemp > t0, $"{t0} -> {s.engineTemp}");
            Assert.IsFalse(s.sys.landingLight);
            CommandSwitch(s, SwitchName.LandingLight);
            Assert.IsTrue(s.sys.landingLight);
        }
    }
}
