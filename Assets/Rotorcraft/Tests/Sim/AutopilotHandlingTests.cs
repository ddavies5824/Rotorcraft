// How smoothly the autopilot flies: it should settle on its target without bobbing or sawing at the controls, and
// still hold its height when the aircraft is asked to do something.
using System;
using NUnit.Framework;
using static Rotorcraft.Sim.SimMath;

namespace Rotorcraft.Sim.Tests
{
    public class AutopilotHandlingTests
    {
        static readonly string[] Types = { "northstar", "guardian", "nightjar", "peregrine" };
        static readonly AssistMode[] Modes = { AssistMode.Beginner, AssistMode.Standard, AssistMode.Realistic };
        const double DT = FixedStepper.SIM_DT;

        static double CyclicFor(double desired, double current, double rate, double maxAngle, AssistMode assist, double gainRate) =>
            assist == AssistMode.Realistic ? Clamp(((desired - current) * 2.2 - rate * 0.6) / gainRate, -1, 1) : Clamp(desired / maxAngle, -1, 1);

        /// <summary>An aircraft settled by hand at a height and speed, ready for the autopilot.</summary>
        sealed class Flight
        {
            public readonly AircraftConfig ac;
            public readonly HeliState s;
            public readonly AssistMode assist;
            public readonly WeatherPreset wx;
            public double lever = 0.55, pitchWant;
            public double speedWant;
            public Controls held;

            public Flight(string type, AssistMode assist, double speedMs, double agl, string weather = "calm")
            {
                ac = Aircraft.Get(type);
                this.assist = assist;
                wx = Weather.Presets[weather];
                speedWant = speedMs;
                s = TestHelpers.Create(0, 0, 90, ac.fuelCapacity * 0.5, true, new Airborne(agl, speedMs));
                for (int i = 0; i < 20 / DT; i++) { held = HandFly(0); Step(held); }
            }

            /// <summary>Lever on the climb rate, cyclic on the speed, wings level.</summary>
            public Controls HandFly(double extraLever)
            {
                lever = Clamp(lever - s.vel.y * 0.3 * DT, 0, 1);
                return new Controls(lever + extraLever, Pitch(), CyclicFor(0, s.phi, s.p, 28 * DEG, assist, ac.rollRate), 0);
            }

            public double Pitch()
            {
                var a = BodyAxesOf(s.psi, 0, 0);
                var fwd = s.vel.x * a.f.x + s.vel.z * a.f.z;
                pitchWant = Clamp(pitchWant + (fwd - speedWant) * 0.02 * DT, -18 * DEG, 12 * DEG);
                return -CyclicFor(Clamp((fwd - speedWant) * 0.03 + pitchWant, -18 * DEG, 12 * DEG), s.theta, s.q, 22 * DEG, assist, ac.pitchRate);
            }

            public void Step(Controls c) { FlightModel.Step(s, c, ac, wx, new SimSettings(assist), DT); s.events.Clear(); }
        }

        struct Swings { public double overshootM, heightM, lever, maxVsFpm, leverRate; public int bobs; }

        /// <summary>Engage and leave the controls alone; the swings are measured once 15 seconds have passed.</summary>
        static Swings Hold(Flight f, bool hover, double secs = 45)
        {
            var ap = Autopilot.NewAP();
            if (hover) Assert.IsTrue(Autopilot.ToggleAP(ap, APMode.Hover, f.s, f.ac, f.held.collective).ok);
            else { Assert.IsTrue(Autopilot.ToggleAP(ap, APMode.Alt, f.s, f.ac, f.held.collective).ok); Assert.IsTrue(Autopilot.ToggleAP(ap, APMode.Hdg, f.s, f.ac, f.held.collective).ok); }
            double Height() => hover ? f.s.@out.aglM : f.s.pos.y;
            var target = hover ? ap.hoverAgl : ap.altTarget;
            var approach = Math.Sign(Math.Round(target - Height(), 1));     // which way the target lies at the start
            double overshoot = 0, yMin = 1e9, yMax = -1e9, cMin = 9, cMax = -9, vs = 0, rate = 0, last = f.held.collective;
            int bobs = 0, sign = 0;
            for (int i = 0; i < secs / DT; i++)
            {
                var r = Autopilot.ApplyAP(ap, f.s, f.ac, new Controls(f.held.collective, f.Pitch(), 0, 0), f.assist, DT);
                f.Step(r.controls);
                overshoot = Math.Max(overshoot, approach == 0 ? Math.Abs(Height() - target) : (Height() - target) * approach);   // how far past it
                rate = Math.Max(rate, Math.Abs(r.controls.collective - last) / DT); last = r.controls.collective;
                if (i * DT < 15) continue;
                yMin = Math.Min(yMin, Height()); yMax = Math.Max(yMax, Height());
                cMin = Math.Min(cMin, r.controls.collective); cMax = Math.Max(cMax, r.controls.collective);
                vs = Math.Max(vs, Math.Abs(f.s.vel.y) * 196.85);
                var sg = f.s.vel.y > 0.15 ? 1 : f.s.vel.y < -0.15 ? -1 : sign;
                if (sg != sign && sign != 0) bobs++;
                sign = sg;
            }
            Assert.IsFalse(f.s.crashed);
            return new Swings { overshootM = overshoot, heightM = yMax - yMin, lever = cMax - cMin, maxVsFpm = vs, leverRate = rate, bobs = bobs };
        }

        [Test]
        public void InStillAirItSettlesInsteadOfBobbing()
        {
            foreach (var type in Types)
                foreach (var assist in Modes)
                    foreach (var hover in new[] { false, true })
                    {
                        var w = Hold(new Flight(type, assist, hover ? 0 : 45, hover ? 20 : 300), hover);
                        var what = $"{type} {assist} {(hover ? "hover" : "cruise")}";
                        Assert.That(w.bobs == 0, $"{what}: changed direction {w.bobs} times");
                        Assert.That(w.heightM < 0.2, $"{what}: height swings {w.heightM:F2} m");
                        Assert.That(w.lever < 0.01, $"{what}: lever swings {w.lever:F3}");
                        Assert.That(w.maxVsFpm < 30, $"{what}: vertical speed reaches {w.maxVsFpm:F0} fpm");
                    }
        }

        [Test]
        public void InGustsItCorrectsGently()
        {
            foreach (var type in Types)
                foreach (var hover in new[] { false, true })
                {
                    var w = Hold(new Flight(type, AssistMode.Beginner, hover ? 0 : 45, hover ? 20 : 300, "breezy_w"), hover);
                    var what = $"{type} {(hover ? "hover" : "cruise")} in gusts";
                    Assert.That(w.heightM < 2, $"{what}: height swings {w.heightM:F2} m");
                    Assert.That(w.lever < 0.06, $"{what}: lever swings {w.lever:F3}");
                    Assert.That(w.maxVsFpm < 100, $"{what}: vertical speed reaches {w.maxVsFpm:F0} fpm");
                    Assert.That(w.bobs <= 6, $"{what}: changed direction {w.bobs} times");
                }
        }

        [Test]
        public void EngagingInAClimbTakesTheClimbOffSmoothly()
        {
            foreach (var type in Types)
            {
                var f = new Flight(type, AssistMode.Beginner, 45, 300);
                var climbLever = f.lever + 0.08;
                for (int i = 0; i < 6 / DT; i++) { f.held = new Controls(climbLever, f.Pitch(), 0, 0); f.Step(f.held); }
                Assert.That(f.s.vel.y > 1.5, $"{type} is climbing: {f.s.vel.y:F1} m/s");
                var w = Hold(f, false);
                Assert.That(w.overshootM < 1.5, $"{type} went {w.overshootM:F1} m past the height");
                Assert.That(w.heightM < 0.3 && w.bobs == 0, $"{type} settled: swings {w.heightM:F2} m, {w.bobs} direction changes");
                Assert.That(w.leverRate < 0.21, $"{type}: the lever moved at {w.leverRate:F2} of its travel a second");
            }
        }

        [Test]
        public void ItStillHoldsHeightThroughASteepTurnAndASlowdown()
        {
            foreach (var type in Types)
            {
                var f = new Flight(type, AssistMode.Beginner, 55, 300);
                var ap = Autopilot.NewAP();
                Assert.IsTrue(Autopilot.ToggleAP(ap, APMode.Alt, f.s, f.ac, f.held.collective).ok);
                var target = ap.altTarget;
                double worst = 0;
                void Fly(double secs, Func<double> roll)
                {
                    for (int i = 0; i < secs / DT; i++)
                    {
                        var r = Autopilot.ApplyAP(ap, f.s, f.ac, new Controls(f.held.collective, f.Pitch(), roll(), 0), f.assist, DT);
                        f.Step(r.controls);
                        worst = Math.Max(worst, Math.Abs(f.s.pos.y - target));
                    }
                }
                Fly(25, () => 30.0 / 28);                                   // full cyclic: the Beginner limit of 28 degrees of bank
                Fly(10, () => 0);
                Assert.That(worst < 12, $"{type} moved {worst:F1} m in the turn");
                worst = 0;
                f.speedWant = 15;                                           // from 107 kt down to 30 kt
                Fly(40, () => 0);
                Assert.That(worst < 12, $"{type} moved {worst:F1} m in the slowdown");
                Assert.That(ap.alt, $"{type} still has altitude hold");
            }
        }

        [Test]
        public void TheLeverStillMovesTheHoverHeightWithoutOvershoot()
        {
            var f = new Flight("guardian", AssistMode.Beginner, 0, 20);
            var ap = Autopilot.NewAP();
            Assert.IsTrue(Autopilot.ToggleAP(ap, APMode.Hover, f.s, f.ac, f.held.collective).ok);
            var start = ap.hoverAgl;
            for (int i = 0; i < 6 / DT; i++) f.Step(Autopilot.ApplyAP(ap, f.s, f.ac, new Controls(f.held.collective + 0.1, 0, 0, 0), f.assist, DT).controls);
            Assert.That(ap.hoverAgl > start + 10, $"lever up raises the hold height: {ap.hoverAgl:F1} m, from {start:F1} m");
            double most = 0;
            for (int i = 0; i < 40 / DT; i++)
            {
                f.Step(Autopilot.ApplyAP(ap, f.s, f.ac, new Controls(f.held.collective, 0, 0, 0), f.assist, DT).controls);
                most = Math.Max(most, f.s.@out.aglM);
            }
            Assert.That(most < ap.hoverAgl + 1, $"reached {most:F1} m for a hold height of {ap.hoverAgl:F1} m");
            Assert.That(Math.Abs(f.s.@out.aglM - ap.hoverAgl) < 0.5, $"settled at {f.s.@out.aglM:F1} m for a hold height of {ap.hoverAgl:F1} m");
        }

        [Test]
        public void TheCollectiveEasesBackToTheLeverWhenItDisengages()
        {
            var f = new Flight("guardian", AssistMode.Beginner, 45, 300);
            var ap = Autopilot.NewAP();
            var lever = f.held.collective;
            Assert.IsTrue(Autopilot.ToggleAP(ap, APMode.Alt, f.s, f.ac, lever).ok);
            Controls Pilot(double l) => new Controls(l, f.Pitch(), 0, 0);
            lever += 0.05;                                                  // the lever nudged, not far enough to override
            double held = lever;
            for (int i = 0; i < 20 / DT; i++) { var r = Autopilot.ApplyAP(ap, f.s, f.ac, Pilot(lever), f.assist, DT); held = r.controls.collective; f.Step(r.controls); }
            Assert.That(ap.alt && Math.Abs(held - lever) > 0.03, $"the autopilot's collective {held:F3} differs from the lever {lever:F3}");

            Assert.IsTrue(Autopilot.ToggleAP(ap, APMode.Alt, f.s, f.ac, lever).ok);
            Assert.IsFalse(ap.alt);
            var last = held; double worst = 0; Controls c = default;
            for (int i = 0; i < 3 / DT; i++) { c = Autopilot.ApplyAP(ap, f.s, f.ac, Pilot(lever), f.assist, DT).controls; worst = Math.Max(worst, Math.Abs(c.collective - last) / DT); last = c.collective; f.Step(c); }
            Assert.That(worst < 0.21, $"the collective moved at {worst:F2} of its travel a second");
            Assert.That(Math.Abs(c.collective - lever) < 1e-6, $"it ends on the lever: {c.collective:F3}, lever {lever:F3}");
            Assert.IsFalse(ap.holding);

            // moving the lever firmly takes over the same way
            Assert.IsTrue(Autopilot.ToggleAP(ap, APMode.Alt, f.s, f.ac, lever).ok);
            for (int i = 0; i < 5 / DT; i++) f.Step(Autopilot.ApplyAP(ap, f.s, f.ac, Pilot(lever), f.assist, DT).controls);
            var before = Autopilot.ApplyAP(ap, f.s, f.ac, Pilot(lever), f.assist, DT).controls.collective;
            var res = Autopilot.ApplyAP(ap, f.s, f.ac, Pilot(lever + 0.2), f.assist, DT);
            Assert.That(res.dropped.Contains(APMode.Alt), "moving the lever drops altitude hold");
            Assert.That(Math.Abs(res.controls.collective - before) < 0.01, $"no jolt: {before:F3} to {res.controls.collective:F3}");
        }
    }
}
