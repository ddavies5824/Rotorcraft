using System;
using System.IO;
using NUnit.Framework;
using Rotorcraft.Presentation;

namespace Rotorcraft.Presentation.Tests
{
    public class AmbientSynthTests
    {
        const int Rate = 48000;

        static float[] Render(Action<AmbientSynth> set, double secs = 12, bool inside = false)
        {
            var a = new AmbientSynth(Rate) { volume = 0.6f, inside = inside };
            set(a);
            var all = new float[(int)(secs * Rate) * 2];
            var block = new float[1024 * 2];
            for (int at = 0; at + block.Length <= all.Length; at += block.Length)
            {
                Array.Clear(block, 0, block.Length);
                a.Render(block, 2);
                Array.Copy(block, 0, all, at, block.Length);
            }
            return all;
        }

        static (double rms, double peak) Level(float[] x, int from = Rate * 2 * 3)
        {
            double sum = 0, peak = 0; int n = 0;
            for (int i = from; i < x.Length; i++) { Assert.That(float.IsNaN(x[i]) || float.IsInfinity(x[i]), Is.False); sum += x[i] * x[i]; peak = Math.Max(peak, Math.Abs(x[i])); n++; }
            return (Math.Sqrt(sum / Math.Max(1, n)), peak);
        }

        static void Wav(string name, float[] x)
        {
            Directory.CreateDirectory("Logs");
            using var w = new BinaryWriter(File.Create($"Logs/ambient-{name}.wav"));
            w.Write("RIFF".ToCharArray()); w.Write(36 + x.Length * 2); w.Write("WAVEfmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)2);
            w.Write(Rate); w.Write(Rate * 4); w.Write((short)4); w.Write((short)16); w.Write("data".ToCharArray()); w.Write(x.Length * 2);
            foreach (var v in x) w.Write((short)Math.Max(-32767, Math.Min(32767, v * 32767)));
        }

        [Test]
        public void EachPlaceSoundsLikeSomethingAndNothingClips()
        {
            var places = new (string name, Action<AmbientSynth> set, double min)[]
            {
                ("woods", a => { a.birds = 1; a.leaves = 0.8f; a.breeze = 0.3f; }, 0.01),
                ("coast", a => { a.gulls = 1; a.sea = 1; a.breeze = 0.5f; }, 0.02),
                ("night-woods", a => { a.owls = 1; a.leaves = 0.5f; }, 0.005),
                ("rain", a => { a.rain = 1; a.breeze = 0.4f; }, 0.01),
                ("town", a => { a.hum = 1; a.birds = 0.3f; }, 0.005),
                ("platform", a => { a.plant = 1; a.sea = 0.6f; a.gulls = 0.5f; }, 0.01),
            };
            foreach (var (name, set, min) in places)
            {
                var x = Render(set);
                Wav(name, x);
                var (rms, peak) = Level(x);
                Assert.That(rms, Is.GreaterThan(min), $"{name}: heard at all");
                Assert.That(peak, Is.LessThan(0.9), $"{name}: no clipping");
            }
        }

        [Test]
        public void SilentWhenPausedAndWhenThereIsNothingThere()
        {
            Assert.That(Level(Render(a => { a.birds = 1; a.sea = 1; a.paused = true; })).rms, Is.LessThan(1e-4));
            Assert.That(Level(Render(a => { })).rms, Is.LessThan(1e-4));
        }

        [Test]
        public void FromInsideTheCockpitItIsQuieter()
        {
            Action<AmbientSynth> coast = a => { a.gulls = 1; a.sea = 1; a.birds = 1; };
            Assert.That(Level(Render(coast, inside: true)).rms, Is.LessThan(Level(Render(coast)).rms * 0.6));
        }

        [Test]
        public void BirdsSingInTheWoods()
        {
            // calls come and go: the level over each second moves a good deal, where a steady bed would not
            var x = Render(a => a.birds = 1, 20);
            double lo = double.MaxValue, hi = 0;
            for (int s = 3; s < 19; s++)
            {
                double sum = 0; for (int i = s * Rate * 2; i < (s + 1) * Rate * 2; i++) sum += x[i] * x[i];
                var r = Math.Sqrt(sum / (Rate * 2)); lo = Math.Min(lo, r); hi = Math.Max(hi, r);
            }
            Assert.That(hi, Is.GreaterThan(lo * 3), $"quiet {lo:F4}, busy {hi:F4}");
        }
    }
}
