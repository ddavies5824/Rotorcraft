// How the nose behaves with the pedals left alone: it follows the flight path at speed and stays put at the hover,
// so the pedals are for pointing it somewhere new rather than for constant correction.
using System;
using NUnit.Framework;
using static Rotorcraft.Sim.SimMath;

namespace Rotorcraft.Sim.Tests
{
    public class YawHandlingTests
    {
        static readonly string[] Types = { "kestrel", "northstar", "guardian", "nightjar", "peregrine" };
        static readonly AssistMode[] Modes = { AssistMode.Beginner, AssistMode.Standard, AssistMode.Realistic };
        const double DT = FixedStepper.SIM_DT;

        struct Flown { public double worstSlipDeg, turnDegS, headingChangeDeg; }

        /// <summary>The angle between the nose and the way the aircraft is moving (+ moving to the right of the nose).</summary>
        static double SlipDeg(HeliState s)
        {
            var a = BodyAxesOf(s.psi, s.theta, s.phi);
            double f = s.vel.x * a.f.x + s.vel.y * a.f.y + s.vel.z * a.f.z, r = s.vel.x * a.r.x + s.vel.y * a.r.y + s.vel.z * a.r.z;
            return Math.Atan2(r, Math.Abs(f)) * RAD;
        }

        static double CyclicFor(double desired, double current, double rate, double maxAngle, AssistMode assist, double gainRate) =>
            assist == AssistMode.Realistic ? Clamp(((desired - current) * 2.2 - rate * 0.6) / gainRate, -1, 1) : Clamp(desired / maxAngle, -1, 1);

        /// <summary>Hold height, speed and bank with the cyclic and collective; report the second half of the time.</summary>
        static Flown Fly(string type, AssistMode assist, double speedMs, double bankDeg, double secs = 30, string weather = "calm", Func<double, double> pedal = null)
        {
            var ac = Aircraft.Get(type);
            var s = TestHelpers.Create(0, 0, 90, ac.fuelCapacity * 0.5, true, new Airborne(300, speedMs));
            double lever = 0.55, pitchWant = 0, unwrapped = 0, psiMid = 0, last = s.psi, worst = 0;
            var wx = Weather.Presets[weather];
            var n = (int)(secs / DT);
            for (int i = 0; i < n; i++)
            {
                lever = Clamp(lever - s.vel.y * 0.3 * DT, 0, 1);
                var a = BodyAxesOf(s.psi, 0, 0);
                var fwd = s.vel.x * a.f.x + s.vel.z * a.f.z;
                pitchWant = Clamp(pitchWant + (fwd - speedMs) * 0.02 * DT, -18 * DEG, 12 * DEG);
                var pitch = Clamp((fwd - speedMs) * 0.03 + pitchWant, -18 * DEG, 12 * DEG);
                var c = new Controls(lever,
                    -CyclicFor(pitch, s.theta, s.q, 22 * DEG, assist, ac.pitchRate),
                    CyclicFor(bankDeg * DEG, s.phi, s.p, 28 * DEG, assist, ac.rollRate),
                    pedal != null ? pedal(i * DT) : 0);
                FlightModel.Step(s, c, ac, wx, new SimSettings(assist), DT);
                s.events.Clear();
                unwrapped += WrapPi(s.psi - last); last = s.psi;
                if (i == n / 2) psiMid = unwrapped;
                if (i >= n / 2) worst = Math.Max(worst, Math.Abs(SlipDeg(s)));
            }
            return new Flown { worstSlipDeg = worst, turnDegS = (unwrapped - psiMid) * RAD / ((n - n / 2) * DT), headingChangeDeg = unwrapped * RAD };
        }

        [Test]
        public void WithFeetOffTheNoseFollowsABankedTurn()
        {
            foreach (var type in Types)
                foreach (var assist in Modes)
                    foreach (var (kt, ms, bank, limit) in new[] { (80, 41.0, 25.0, 4.0), (50, 26.0, 20.0, 5.0), (25, 13.0, 15.0, 9.0) })
                    {
                        var f = Fly(type, assist, ms, bank);
                        Assert.That(f.worstSlipDeg < limit, $"{type} {assist}, {kt} kt at {bank} degrees of bank: flying {f.worstSlipDeg:F1} degrees sideways");
                        var wanted = G * Math.Tan(bank * DEG) / ms * RAD;
                        Assert.That(Math.Abs(f.turnDegS - wanted) < wanted * 0.2, $"{type} {assist}, {kt} kt: turning at {f.turnDegS:F1} degrees a second, a level turn is {wanted:F1}");
                    }
        }

        [Test]
        public void TheAssistedModesHoldTheNoseAtTheHover()
        {
            foreach (var type in Types)
            {
                foreach (var weather in new[] { "calm", "crosswind_n" })
                {
                    var f = Fly(type, AssistMode.Beginner, 0, 0, weather: weather);
                    Assert.That(Math.Abs(f.headingChangeDeg) < 3, $"{type} Beginner in {weather}: the nose moved {f.headingChangeDeg:F1} degrees");
                }
                var calm = Fly(type, AssistMode.Standard, 0, 0);
                Assert.That(Math.Abs(calm.turnDegS) < 1, $"{type} Standard: the nose is turning at {calm.turnDegS:F1} degrees a second");
            }
        }

        [Test]
        public void TheFinTakesTheTorqueLoadAtSpeed()
        {
            var f = Fly("kestrel", AssistMode.Realistic, 41, 0);
            Assert.That(f.worstSlipDeg < 5, $"the Kestrel in Realistic cruises {f.worstSlipDeg:F1} degrees sideways");
        }

        [Test]
        public void RealisticStillNeedsPedalInTheHoverAndASteadyPedalHoldsIt()
        {
            var free = Fly("kestrel", AssistMode.Realistic, 0, 0);
            Assert.That(free.turnDegS > 10, $"torque turns a Kestrel with no pedal: {free.turnDegS:F1} degrees a second");
            var held = Fly("kestrel", AssistMode.Realistic, 0, 0, pedal: _ => -0.3);
            Assert.That(Math.Abs(held.turnDegS) < 3, $"a steady 30% of left pedal, which trim can hold, stops it: {held.turnDegS:F1} degrees a second");
        }

        [Test]
        public void ThePedalsStillTurnTheAircraftAndItStopsWhenTheyAreLetGo()
        {
            foreach (var type in Types)
            {
                var ac = Aircraft.Get(type);
                var turning = Fly(type, AssistMode.Beginner, 0, 0, 10, pedal: _ => 0.5);
                var wanted = 0.5 * ac.yawRate * RAD;
                Assert.That(Math.Abs(turning.turnDegS - wanted) < wanted * 0.2, $"{type}: half pedal turns at {turning.turnDegS:F1} degrees a second, wanted {wanted:F1}");

                // half pedal for five seconds, then let go: the nose stops within a few degrees
                var moved = Fly(type, AssistMode.Beginner, 0, 0, 5, pedal: _ => 0.5).headingChangeDeg;
                var stopped = Fly(type, AssistMode.Beginner, 0, 0, 10, pedal: t => t < 5 ? 0.5 : 0);
                Assert.That(stopped.headingChangeDeg - moved < 15, $"{type}: ran on {stopped.headingChangeDeg - moved:F1} degrees after the pedal was let go");
                Assert.That(Math.Abs(stopped.turnDegS) < 3, $"{type}: still turning at {stopped.turnDegS:F1} degrees a second");
            }
        }
    }
}
