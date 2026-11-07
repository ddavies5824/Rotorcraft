// How the aircraft answers the controls and the wind. Each limit was measured against what it was when the player reported the
// controls as twitchy and the wind as far too strong, and sits well on the good side of that (see the numbers beside each).
using System;
using NUnit.Framework;
using static Rotorcraft.Sim.SimMath;

namespace Rotorcraft.Sim.Tests
{
    public class HandlingTests
    {
        static readonly string[] Types = HandlingReport.Types;
        static readonly string[] Winds = { "calm", "light_sw", "fair_nw", "crosswind_n", "breezy_w", "showers", "glen_closing", "glen_lowering", "north_sea", "haar" };
        const double DT = FixedStepper.SIM_DT;

        [Test]
        public void TheCyclicSettlesOnTheAttitudeInsteadOfRockingPastIt()
        {
            // up to 20% on the twins before
            foreach (var type in Types)
                foreach (var assist in new[] { AssistMode.Beginner, AssistMode.Standard })
                    foreach (var roll in new[] { true, false })
                    {
                        var r = HandlingReport.StepStick(type, assist, roll, 0.5);
                        Assert.That(r.overshootPct, Is.LessThan(5), $"{type} {assist} {(roll ? "roll" : "pitch")}");
                    }
        }

        [Test]
        public void OnePerCentOfLeverIsAGentleChangeOfHeight()
        {
            // 42 to 86 ft/min before
            foreach (var type in Types)
                Assert.That(HandlingReport.FpmPerPercentLever(type, AssistMode.Standard), Is.LessThan(50), type);
        }

        [Test]
        public void AHandOnTheLeverDoesNotSetOffAGrowingSwingInHeight()
        {
            // from about 400 to over 3,500 ft/min within a minute on the twins before
            foreach (var type in Types)
            {
                var h = HandlingReport.HeightHunt(type, AssistMode.Standard);
                Assert.That(h.last, Is.LessThan(100), $"{type}: {h.first:0} then {h.last:0} ft/min");
            }
        }

        [Test]
        public void TheNoseStaysPutInAHoverWhateverTheWind()
        {
            // up to 54 degrees in 20 seconds before, on Standard
            foreach (var type in Types)
                foreach (var assist in new[] { AssistMode.Beginner, AssistMode.Standard })
                    foreach (var w in Winds)
                        Assert.That(HandlingReport.Blown(type, assist, w).nose, Is.LessThan(5), $"{type} {assist} in {w}");
        }

        [Test]
        public void HoldingAHoverInTheWindTakesASmallSteadyHand()
        {
            // up to 13% of the stick on average and 21% at the peak before, with the height swinging by 2,400 ft/min
            foreach (var type in new[] { "kestrel", "guardian", "peregrine" })
                foreach (var w in new[] { "breezy_w", "showers", "glen_closing", "north_sea" })
                {
                    var h = HandlingReport.HoldSpot(type, AssistMode.Standard, w);
                    Assert.That(h.meanStick, Is.LessThan(10), $"{type} in {w}: mean stick");
                    Assert.That(h.peakStick, Is.LessThan(13), $"{type} in {w}: peak stick");
                    Assert.That(h.climbSwing, Is.LessThan(400), $"{type} in {w}: height swing");
                }
        }

        [Test]
        public void TheWindIsNeverPushedFarInTwentySeconds()
        {
            // 12.7 kt before, in the strongest wind
            foreach (var type in Types)
                foreach (var w in Winds)
                    Assert.That(HandlingReport.Blown(type, AssistMode.Standard, w).kt, Is.LessThan(10), $"{type} in {w}");
        }

        [Test]
        public void TheFirmerLeverLeavesAnAutorotationAlone()
        {
            // the firmer damping is only near level flight: lever down with the engine off still comes down fast enough to keep the rotor turning
            foreach (var type in Types)
            {
                var ac = Aircraft.Get(type);
                var s = TestHelpers.Create(-2000, 0, 270, ac.fuelCapacity * 0.3, true, new Airborne(600, 28));
                s.throttleIdle = true;
                var wx = Weather.Presets["calm"];
                double rpmLow = 200, sinkSum = 0; int counted = 0;
                for (int i = 0; i < 25 / DT; i++)
                {
                    FlightModel.Step(s, new Controls(0, (s.@out.iasKt - 60) * -0.01 + 0.15, 0, 0), ac, wx, new SimSettings(AssistMode.Standard), DT);
                    s.events.Clear();
                    if (i * DT > 12) { rpmLow = Math.Min(rpmLow, s.@out.rpm); sinkSum += -s.vel.y * 196.85; counted++; }
                }
                Assert.That(rpmLow, Is.GreaterThan(ac.rpmMin - 5), $"{type}: rotor speed");
                Assert.That(sinkSum / counted, Is.InRange(1000, 3500), $"{type}: descent in ft/min");
            }
        }

        [Test]
        public void TheInstrumentsShowTheWindWhereItComesFromSteadied()
        {
            var ac = Aircraft.Get("peregrine");
            var (s, lever) = HandlingReport.Hover(ac, AssistMode.Standard);
            var wx = Weather.Presets["north_sea"];
            double rawLow = 999, rawHigh = 0, shownLow = 999, shownHigh = 0;
            for (int i = 0; i < 60 / DT; i++)
            {
                FlightModel.Step(s, new Controls(lever, 0, 0, 0), ac, wx, new SimSettings(AssistMode.Standard), DT);
                s.events.Clear();
                if (i * DT < 10) continue;
                rawLow = Math.Min(rawLow, s.@out.windKt); rawHigh = Math.Max(rawHigh, s.@out.windKt);
                shownLow = Math.Min(shownLow, s.@out.windAvgKt); shownHigh = Math.Max(shownHigh, s.@out.windAvgKt);
                var off = Math.Abs(WrapPi((s.@out.windAvgFromDeg - wx.windDirDeg) * DEG)) * RAD;
                Assert.That(off, Is.LessThan(15), $"reads {s.@out.windAvgFromDeg:0} for a wind from {wx.windDirDeg:0}");
            }
            Assert.That(shownHigh - shownLow, Is.LessThan(rawHigh - rawLow), "the figure shown moves less than the gusts do");
            Assert.That(shownLow, Is.GreaterThan(wx.windKt * 0.4), "near the ground the wind is less, but it reads as a wind");
            Assert.That(shownHigh, Is.LessThan(wx.windKt + wx.gustKt + 1));
        }

        [Test]
        public void WindBriefingsShowGusts()
        {
            var metar = Weather.Metar(Weather.Presets["breezy_w"], "EGKN");
            StringAssert.Contains("G13KT", metar);
        }
    }
}
