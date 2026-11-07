// The rescue winch with the aircraft held perfectly still, so that only the winch is under test: what it does when the winchman
// reaches the ground short of the casualty, and that he always comes back up, however the lift ends.
using System;
using NUnit.Framework;
using static Rotorcraft.Sim.SimMath;

namespace Rotorcraft.Sim.Tests
{
    public class HoistTests
    {
        const double DT = FixedStepper.SIM_DT, Height = 22;
        static readonly AircraftConfig S9 = Aircraft.Get("guardian");
        static double Scale => S9.rotorDiameter / 15;
        static readonly Vec3 Still = V3(0, 0, 0);

        /// <summary>A hovering aircraft placed so that its winch arm is over (x, z), Height above the ground.</summary>
        static HeliState HoverWithArmOver(double x, double z)
        {
            var g = Ground.Current.GroundAt(x, z).h;
            var s = TestHelpers.Create(x, z, 0, 1200, true, new Airborne(Height, 0));
            s.pos.y = g + Height;
            MoveArmOver(s, x, z);
            return s;
        }

        static void MoveArmOver(HeliState s, double x, double z)
        {
            var arm = Hoist.ArmPoint(s, Scale);
            s.pos.x += x - arm.x; s.pos.z += z - arm.z;
        }

        static void Run(HoistState h, HeliState s, HoistTarget target, double secs, Func<bool> until = null)
        {
            for (int i = 0; i < secs / DT; i++)
            {
                if (until != null && until()) return;
                Hoist.StepHoist(h, s, target, Still, Scale, DT);
                s.t += DT;
            }
        }

        static double ArmToHook(HoistState h, HeliState s) { var a = Hoist.ArmPoint(s, Scale); return Js.Hypot(h.hook.x - a.x, h.hook.y - a.y, h.hook.z - a.z); }

        [Test]
        public void ShortOfTheCasualtyTheWinchStopsAtTheGroundAndHoldsHimJustOffIt()
        {
            var target = new HoistTarget(-3000, -2300, Ground.Current.GroundAt(-3000, -2300).h);
            var s = HoverWithArmOver(target.x + 10, target.z);
            var h = Hoist.NewHoist();
            Hoist.StartLowering(h, s, Scale);
            Run(h, s, target, 60);
            var g = Ground.Current.GroundAt(h.hook.x, h.hook.z).h;
            Assert.AreEqual(HoistPhase.Lowering, h.phase);
            Assert.That(h.heldOff, "the operator says he is holding him off the ground");
            Assert.That(h.cable, Is.LessThan(ArmToHook(h, s) + 0.5), $"no more cable than reaches him: {h.cable:F1} m out, {ArmToHook(h, s):F1} m to the hook");
            Assert.That(h.hook.y - g, Is.InRange(1.2, 2.5), "just off the ground, not lying on it");
        }

        [Test]
        public void MovedOverTheCasualtyHeGoesTheRestOfTheWayDown()
        {
            var target = new HoistTarget(-3000, -2300, Ground.Current.GroundAt(-3000, -2300).h);
            var s = HoverWithArmOver(target.x + 10, target.z);
            var h = Hoist.NewHoist();
            Hoist.StartLowering(h, s, Scale);
            Run(h, s, target, 40);
            MoveArmOver(s, target.x + 2, target.z);
            Run(h, s, target, 20, () => h.phase == HoistPhase.OnScene);
            Assert.AreEqual(HoistPhase.OnScene, h.phase);
            Assert.IsFalse(h.heldOff);
        }

        [Test]
        public void AnAbortAfterAMissBringsHimStraightBackUp()
        {
            var target = new HoistTarget(-3000, -2300, Ground.Current.GroundAt(-3000, -2300).h);
            var s = HoverWithArmOver(target.x + 10, target.z);
            var h = Hoist.NewHoist();
            Hoist.StartLowering(h, s, Scale);
            Run(h, s, target, 80);
            Hoist.AbortHoist(h, "Pilot abort.");
            var t0 = s.t;
            Run(h, s, target, 120, () => h.phase == HoistPhase.Aboard);
            Assert.AreEqual(HoistPhase.Aboard, h.phase);
            Assert.That(s.t - t0, Is.LessThan(Height / Hoist.speed + 6), "only the cable he hangs on to wind in, no slack");
        }

        [Test]
        public void HeComesUpEvenWhenTheAircraftHasDriftedAndTheCableIsSwinging()
        {
            var target = new HoistTarget(-3000, -2300, Ground.Current.GroundAt(-3000, -2300).h);
            var s = HoverWithArmOver(target.x, target.z);
            var h = Hoist.NewHoist();
            Hoist.StartLowering(h, s, Scale);
            Run(h, s, target, 120, () => h.phase == HoistPhase.Raising && h.attached);
            Assert.That(h.phase == HoistPhase.Raising && h.attached, $"the lift got under way: {h.phase}");
            MoveArmOver(s, target.x + 15, target.z);    // well past the angle at which the operator would rather wait
            var t0 = s.t;
            // and still drifting, so the cable never swings back under the aircraft by itself
            s.vel.x = 0.6;
            Run(h, s, target, 180, () => { s.pos.x += s.vel.x * DT; return h.phase == HoistPhase.Aboard; });
            Assert.AreEqual(HoistPhase.Aboard, h.phase, $"still {h.phase} with {h.cable:F1} m out, swinging {h.swingDeg:F0}°");
            Assert.That(s.t - t0, Is.LessThan(90));
        }

        [Test]
        public void OffTheLedgeHeIsNeverLeftHangingInMidAir()
        {
            // the casualty is on a ledge 6 m above the ground where the winchman would land
            var g = Ground.Current.GroundAt(-3000, -2300).h;
            var target = new HoistTarget(-3000, -2300, g + 6);
            var s = HoverWithArmOver(target.x + 12, target.z);
            s.pos.y = g + 30;
            var h = Hoist.NewHoist();
            Hoist.StartLowering(h, s, Scale);
            Run(h, s, target, 60);
            Assert.That(h.hook.y - Ground.Current.GroundAt(h.hook.x, h.hook.z).h, Is.LessThan(2.5), $"hook {h.hook.y - g:F1} m above the ground under it");
        }
    }
}
