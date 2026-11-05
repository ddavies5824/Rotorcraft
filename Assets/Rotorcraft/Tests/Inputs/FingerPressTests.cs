using NUnit.Framework;

namespace Rotorcraft.Inputs.Tests
{
    public class FingerPressTests
    {
        // five buttons by three, each 4 cm square
        static FingerPress Panel() => new FingerPress(5, 3, 0.2f, 0.12f);

        [Test]
        public void TouchingFromInFrontPressesTheButtonUnderTheFinger()
        {
            var p = Panel();
            Assert.That(p.Update(0, 0, 0.1f), Is.EqualTo(-1), "still in the air");
            Assert.That(p.Update(0, 0, 0.03f), Is.EqualTo(-1), "getting close");
            Assert.That(p.Hover, Is.EqualTo(7), "the middle button shows the finger is over it");
            Assert.That(p.Update(0, 0, 0.004f), Is.EqualTo(7));
        }

        [Test]
        public void ButtonsCountFromTheTopLeftAlongEachRow()
        {
            foreach (var (x, y, button) in new[] { (-0.09f, 0.05f, 0), (0.09f, 0.05f, 4), (-0.09f, 0, 5), (0.09f, -0.05f, 14), (0, -0.05f, 12) })
            {
                var p = Panel();
                p.Update(x, y, 0.1f);
                Assert.That(p.Update(x, y, 0), Is.EqualTo(button), $"at {x}, {y}");
            }
        }

        [Test]
        public void OneTouchIsOnePress()
        {
            var p = Panel();
            p.Update(0, 0, 0.1f);
            Assert.That(p.Update(0, 0, 0), Is.EqualTo(7));
            for (int i = 0; i < 30; i++) Assert.That(p.Update(0, 0, -0.002f * i), Is.EqualTo(-1), "pushing further in");
            Assert.That(p.Update(0.05f, 0, 0), Is.EqualTo(-1), "sliding across to the next button while still touching");
            Assert.That(p.Update(0.05f, 0, 0.012f), Is.EqualTo(-1), "backing off a little is not enough");
            Assert.That(p.Update(0.05f, 0, 0), Is.EqualTo(-1));
            p.Update(0.05f, 0, 0.03f);
            Assert.That(p.Update(0.05f, 0, 0), Is.EqualTo(8), "backed right off, so it can press again");
        }

        [Test]
        public void AFingerArrivingFromBehindOrTheSideDoesNotPress()
        {
            var behind = Panel();
            behind.Update(0, 0, -0.2f);
            Assert.That(behind.Update(0, 0, -0.03f), Is.EqualTo(-1), "the hand has come through from behind");
            Assert.That(behind.Update(0, 0, 0), Is.EqualTo(-1));

            var side = Panel();
            side.Update(0.3f, 0, 0.1f);
            side.Update(0.3f, 0, -0.01f);
            Assert.That(side.Update(0.05f, 0, -0.01f), Is.EqualTo(-1), "slid in from beyond the edge, level with the panel");
        }

        [Test]
        public void MissingThePanelDoesNothing()
        {
            var p = Panel();
            p.Update(0.3f, 0, 0.1f);
            Assert.That(p.Update(0.3f, 0, 0), Is.EqualTo(-1));
            Assert.That(p.Hover, Is.EqualTo(-1));
        }

        [Test]
        public void AFingerThatWasTakenAwayMustComeBackFromInFront()
        {
            var p = Panel();
            p.Update(0, 0, 0.1f);
            p.Release();
            Assert.That(p.Hover, Is.EqualTo(-1));
            Assert.That(p.Update(0, 0, 0), Is.EqualTo(-1), "the hand let go of a control with its finger already on the panel");
            p.Update(0, 0, 0.1f);
            Assert.That(p.Update(0, 0, 0), Is.EqualTo(7));
        }
    }
}
