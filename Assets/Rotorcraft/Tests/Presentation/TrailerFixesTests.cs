using NUnit.Framework;
using Rotorcraft.Presentation;
using UnityEngine;

namespace Rotorcraft.Presentation.Tests
{
    public class TrailerFixesTests
    {
        static readonly Vector3 Up = Vector3.up, Nose = Vector3.forward;

        [Test]
        public void AFlatDisplayHasItsTopTowardsTheNose()
        {
            // the normal differs from the aircraft's up only by rounding, in any direction: the answer must not follow it
            foreach (var noise in new[] { new Vector3(2e-4f, 0, 1e-4f), new Vector3(-3e-4f, 0, 2e-4f), new Vector3(1e-4f, 0, -4e-4f), Vector3.zero })
            {
                var top = CockpitDisplays.ScreenUp(Up, Nose, (Up + noise).normalized);
                Assert.That(Vector3.Angle(top, Nose), Is.LessThan(1f), noise.ToString());
            }
        }

        [Test]
        public void AnUprightDisplayHasItsTopUp()
        {
            var towardsPilot = Vector3.back;
            Assert.That(Vector3.Angle(CockpitDisplays.ScreenUp(Up, Nose, towardsPilot), Up), Is.LessThan(0.01f));
        }

        [Test]
        public void ALeaningDisplayHasItsTopUpTheSlope()
        {
            var leaning = new Vector3(0, Mathf.Sin(50 * Mathf.Deg2Rad), -Mathf.Cos(50 * Mathf.Deg2Rad));
            var top = CockpitDisplays.ScreenUp(Up, Nose, leaning);
            Assert.That(Vector3.Dot(top, leaning), Is.EqualTo(0).Within(1e-4f));
            Assert.That(top.y, Is.GreaterThan(0));
            Assert.That(top.z, Is.GreaterThan(0));
        }

        [Test]
        public void ARotorFacesItsYawInTheSimsAxes()
        {
            // the sim has x east and z south, Unity has z north: yaw 0 faces south, a quarter turn east
            Assert.That(Vector3.Distance(TurbineRotors.Facing(0), Vector3.back), Is.LessThan(1e-5f));
            Assert.That(Vector3.Distance(TurbineRotors.Facing(Mathf.PI / 2), Vector3.right), Is.LessThan(1e-5f));
        }

        [Test]
        public void APersonGlowsWhileTheThermalCameraIsOnAndGetsTheirClothesBack()
        {
            var person = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            var hidden = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                var clothes = person.GetComponent<Renderer>().sharedMaterial;
                HeatSource.Add(person, HeatSource.Body);
                HeatSource.Add(hidden, HeatSource.Body);
                hidden.SetActive(false);
                HeatSource.SetThermal(true);
                var hot = person.GetComponent<Renderer>().sharedMaterial;
                Assert.That(hot, Is.Not.EqualTo(clothes));
                Assert.That(hot.GetColor("_BaseColor").r, Is.EqualTo(HeatSource.Body));
                // hidden when it came on, as a waving figure's other frame is
                Assert.That(hidden.GetComponent<Renderer>().sharedMaterial, Is.EqualTo(hot));
                HeatSource.SetThermal(false);
                Assert.That(person.GetComponent<Renderer>().sharedMaterial, Is.EqualTo(clothes));
                Assert.That(hidden.GetComponent<Renderer>().sharedMaterial, Is.EqualTo(clothes));
            }
            finally
            {
                HeatSource.SetThermal(false);
                Object.DestroyImmediate(person);
                Object.DestroyImmediate(hidden);
            }
        }
    }
}
