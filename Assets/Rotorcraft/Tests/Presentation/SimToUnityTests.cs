// The sim's (east, up, south) world must land in Unity with north = +Z, east = +X, and attitudes the right way round.
using NUnit.Framework;
using Rotorcraft.Sim;
using UnityEngine;

namespace Rotorcraft.Presentation.Tests
{
    public class SimToUnityTests
    {
        static HeliState At(double headingDeg, double pitchDeg = 0, double rollDeg = 0)
        {
            var s = FlightModel.CreateState(new CreateOptions { headingDeg = headingDeg, fuel = 10 });
            s.theta = pitchDeg * SimMath.DEG; s.phi = rollDeg * SimMath.DEG;
            return s;
        }

        static void Near(Vector3 want, Vector3 got) => Assert.That((want - got).magnitude < 1e-4f, $"want {want} got {got}");

        [Test]
        public void NorthIsPlusZAndEastIsPlusX()
        {
            Near(new Vector3(3, 2, 5), SimToUnity.Position(new Vec3(3, 2, -5)));   // sim z is south, so 5 m north
            Near(Vector3.forward, SimToUnity.Attitude(At(0)) * Vector3.forward);
            Near(Vector3.right, SimToUnity.Attitude(At(90)) * Vector3.forward);     // heading 090 = east
            Near(Vector3.back, SimToUnity.Attitude(At(180)) * Vector3.forward);
        }

        [Test]
        public void NoseUpPitchesUpAndRightRollDropsTheRightSide()
        {
            Assert.That((SimToUnity.Attitude(At(0, pitchDeg: 10)) * Vector3.forward).y > 0.15f, "nose up");
            var right = SimToUnity.Attitude(At(0, rollDeg: 20)) * Vector3.right;
            Assert.That(right.y < -0.3f, $"right side down in a right roll: {right}");
        }

        [Test]
        public void TheAttitudeIndicatorsHorizonTiltsAgainstTheBank()
        {
            var right = Rotorcraft.Presentation.UI.InstrumentPanel.OnHorizonCard(Vector2.right, 20 * SimMath.DEG);
            Assert.That(right.y < -0.3f, $"a right bank raises the right of the horizon (screen y runs down): {right}");
            var left = Rotorcraft.Presentation.UI.InstrumentPanel.OnHorizonCard(Vector2.right, -20 * SimMath.DEG);
            Assert.That(left.y > 0.3f, $"a left bank lowers the right of the horizon: {left}");
        }

        [Test]
        public void ARunningSimMovesTheAircraftTheWayItIsPointing()
        {
            var s = FlightModel.CreateState(new CreateOptions { headingDeg = 90, fuel = 10, engineRunning = true, airborne = new Airborne(50, 30) });
            var before = SimToUnity.Position(s.pos);
            for (int i = 0; i < 120; i++) FlightModel.Step(s, new Controls(0.5, 0, 0, 0), Aircraft.Get("kestrel"), Weather.Presets["calm"], new SimSettings(AssistMode.Beginner), FixedStepper.SIM_DT);
            var moved = SimToUnity.Position(s.pos) - before;
            Assert.That(moved.x > 20 && Mathf.Abs(moved.z) < 3, $"flying east moves along +X: {moved}");
        }
    }
}
