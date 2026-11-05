using NUnit.Framework;

namespace Rotorcraft.Presentation.Tests
{
    public class VrRatesTests
    {
        static readonly float[] Quest = { 72, 80, 90, 120 };

        [Test]
        public void AutomaticTriesTheRatesInTurnAndLastOfAllTheHighest()
        {
            // half of 120 is 60 frames a second, better than the 36 that a missed 72 Hz would give
            Assert.That(VrRates.Ladder(Quest, 0), Is.EqualTo(new float[] { 90, 80, 72, 120 }));
        }

        [Test]
        public void AChosenRateIsTheOnlyOneTried()
        {
            Assert.That(VrRates.Ladder(Quest, 80), Is.EqualTo(new float[] { 80 }));
            Assert.That(VrRates.Ladder(Quest, 144), Is.Empty, "a rate this headset does not offer");
        }

        [Test]
        public void AHeadsetThatOffersNoRatesIsLeftAlone()
        {
            Assert.That(VrRates.Ladder(new float[0], 0), Is.Empty);
            Assert.That(VrRates.Ladder(null, 90), Is.Empty);
        }

        [Test]
        public void AHeadsetWithOnlyOneRateIsNotAskedForAnything()
        {
            // Quest Link offers a game only the rate set in Meta's app
            Assert.That(VrRates.Ladder(new float[] { 120 }, 0), Is.Empty);
            Assert.That(VrRates.Ladder(new float[] { 120 }, 90), Is.Empty);
        }

        static bool Watch(VrFrameWatch w, float seconds, float frameSeconds, float hz)
        {
            var fired = false;
            for (float t = 0; t < seconds; t += frameSeconds) fired |= w.Tick(frameSeconds, hz);
            return fired;
        }

        [Test]
        public void FramesOnTimeAreFine()
        {
            Assert.False(Watch(new VrFrameWatch(), 30, 1 / 90f, 90));
        }

        [Test]
        public void ARunAtHalfTheRateIsNoticedWithinAWindow()
        {
            Assert.True(Watch(new VrFrameWatch(), 6, 2 / 90f, 90));
        }

        [Test]
        public void TheOddHitchIsForgiven()
        {
            var w = new VrFrameWatch();
            var fired = false;
            for (int i = 0; i < 90 * 30; i++) fired |= w.Tick(i % 200 == 0 ? 0.1f : 1 / 90f, 90);
            Assert.False(fired, "one long frame every couple of seconds");
        }

        [Test]
        public void ResetForgetsWhatWasCounted()
        {
            var w = new VrFrameWatch();
            Watch(w, 4, 2 / 90f, 90);
            w.Reset();
            Assert.False(Watch(w, 4, 1 / 90f, 90));
        }
    }
}
