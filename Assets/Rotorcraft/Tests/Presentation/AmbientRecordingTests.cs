using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rotorcraft.Presentation.Tests
{
    public class AmbientRecordingTests
    {
        const int Rate = 48000;

        static float[] Noise(int frames, int channels, int seed, float amp = 0.3f)
        {
            var r = new System.Random(seed); var x = new float[frames * channels];
            for (int i = 0; i < x.Length; i++) x[i] = (float)(r.NextDouble() * 2 - 1) * amp;
            return x;
        }

        static float Rms(float[] x) { double s = 0; foreach (var v in x) s += v * v; return (float)Math.Sqrt(s / x.Length); }

        static float[] Render(Action<AmbientSynth> set, double secs = 10)
        {
            var a = new AmbientSynth(Rate) { volume = 1 };
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

        static double Level(float[] x, double fromS = 2, double toS = double.MaxValue)
        {
            int from = (int)(fromS * Rate) * 2, to = (int)Math.Min(x.Length, Math.Min(int.MaxValue, toS * Rate * 2));
            double sum = 0; for (int i = from; i < to; i++) sum += x[i] * x[i];
            return Math.Sqrt(sum / Math.Max(1, to - from));
        }

        static double Peak(float[] x) { double p = 0; foreach (var v in x) p = Math.Max(p, Math.Abs(v)); return p; }

        static void Wav(string name, float[] x)
        {
            Directory.CreateDirectory("Logs");
            using var w = new BinaryWriter(File.Create($"Logs/ambient-{name}.wav"));
            w.Write("RIFF".ToCharArray()); w.Write(36 + x.Length * 2); w.Write("WAVEfmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)2);
            w.Write(Rate); w.Write(Rate * 4); w.Write((short)4); w.Write((short)16); w.Write("data".ToCharArray()); w.Write(x.Length * 2);
            foreach (var v in x) w.Write((short)Math.Max(-32767, Math.Min(32767, v * 32767)));
        }

        /// <summary>The biggest jump between neighbouring samples anywhere inside the loop, and the jump from its end back to its start.</summary>
        static (int inside, int seam) Jumps(short[] pcm, int frames, int channels)
        {
            int inside = 0, seam = 0;
            for (int c = 0; c < channels; c++)
            {
                for (int f = 1; f < frames; f++) inside = Math.Max(inside, Math.Abs(pcm[f * channels + c] - pcm[(f - 1) * channels + c]));
                seam = Math.Max(seam, Math.Abs(pcm[c] - pcm[(frames - 1) * channels + c]));
            }
            return (inside, seam);
        }

        [Test]
        public void TheLoopRunsStraightOnFromItsEndToItsStart()
        {
            // a slow wander on each side: neighbouring samples are close, so any seam would stand out
            var r = new System.Random(4); var x = new float[Rate * 6 * 2]; double a = 0, b = 0;
            for (int f = 0; f < Rate * 6; f++) { a = a * 0.9995 + (r.NextDouble() - 0.5) * 0.01; b = b * 0.9995 + (r.NextDouble() - 0.5) * 0.01; x[2 * f] = (float)a; x[2 * f + 1] = (float)b; }
            var pcm = Recording.MakeLoop(x, 2, Rate, out var frames);
            Assert.That(frames, Is.EqualTo(Rate * 5), "the overrun goes into the crossfade");
            var (inside, seam) = Jumps(pcm, frames, 2);
            Assert.That(seam, Is.LessThanOrEqualTo(inside), $"seam {seam}, largest step inside {inside}");
            // cut without the crossfade, the same stretch would jump
            var cut = Math.Abs(x[0] - x[(Rate * 5 - 1) * 2]) * 32767;
            Assert.That(cut, Is.GreaterThan(inside * 3), "the test wander moves far enough to show a seam");
        }

        [Test]
        public void ARecordingPlaysAtItsLayersLevelAndTheMadeUpOneDropsOut()
        {
            var noise = Noise(Rate * 4, 1, 1);
            var rec = Recording.From("leaves", noise, 1, Rate, Rms(noise), 0.5f);
            var level = Level(Render(a => { a.leaves = 1; a.leavesRec = rec; }), 4);
            Assert.That(level, Is.EqualTo(AmbientSynth.TargetRms("leaves")).Within(10).Percent);
            var silent = Recording.From("leaves", new float[Rate * 2], 1, Rate, 0.1f);
            Assert.That(Level(Render(a => { a.leaves = 1; a.leavesRec = silent; })), Is.LessThan(1e-4));
            // stereo, and played at another output rate (tones: white noise would lose level to the resampling, a recording hardly any)
            var st = new float[Rate * 4 * 2];
            for (int f = 0; f < Rate * 4; f++) { st[2 * f] = 0.3f * Mathf.Sin(2 * Mathf.PI * 440 * f / Rate); st[2 * f + 1] = 0.3f * Mathf.Sin(2 * Mathf.PI * 660 * f / Rate); }
            var rain = Recording.From("rain", st, 2, Rate, Rms(st), 0);
            var a44 = new AmbientSynth(44100) { volume = 1, rain = 1, rainRec = rain };
            var buf = new float[44100 * 12 * 2]; var blk = new float[2048];
            for (int at = 0; at + blk.Length <= buf.Length; at += blk.Length) { Array.Clear(blk, 0, blk.Length); a44.Render(blk, 2); Array.Copy(blk, 0, buf, at, blk.Length); }
            double s = 0; int n0 = 44100 * 2 * 6, n1 = buf.Length / blk.Length * blk.Length;
            for (int i = n0; i < n1; i++) s += buf[i] * buf[i];
            Assert.That(Math.Sqrt(s / (n1 - n0)), Is.EqualTo(AmbientSynth.TargetRms("rain")).Within(10).Percent);
        }

        [Test]
        public void RecordedBirdsAndGullsReplaceTheMadeUpCalls()
        {
            var silentBirds = Recording.From("birds", new float[Rate * 2], 1, Rate, 0.1f);
            var silentGulls = Recording.From("gulls", new float[Rate * 2], 1, Rate, 0.1f);
            Assert.That(Level(Render(a => { a.birds = 1; a.gulls = 1; a.birdsRec = silentBirds; a.gullsRec = silentGulls; }, 20)), Is.LessThan(1e-4));
            Assert.That(Level(Render(a => { a.birds = 1; a.gulls = 1; }, 20)), Is.GreaterThan(0.005), "without them, the made-up calls");
        }

        [Test]
        public void ABeachRecordingTakesOnlyTheShoreAndLeavesTheOpenSeaMadeUp()
        {
            var silent = Recording.From("shore", new float[Rate * 2], 1, Rate, 0.1f);
            Assert.That(Level(Render(a => { a.sea = 1; a.shore = 1; a.shoreRec = silent; })), Is.LessThan(1e-4), "on the beach: all recording");
            Assert.That(Level(Render(a => { a.sea = 1; a.shore = 0; a.shoreRec = silent; })), Is.GreaterThan(0.005), "out at sea: still the made-up wash");
        }

        [Test]
        public void AnOwlHootsNowAndThenNotAllNight()
        {
            var hoots = Noise(Rate * 10, 1, 3);
            var owl = Recording.From("owls", hoots, 1, Rate, Rms(hoots), 0, calls: true);
            var x = Render(a => { a.owls = 1; a.owlRec = owl; }, 180);
            int heard = 0, quiet = 0;
            for (int s = 2; s < 180; s++) { var l = Level(x, s, s + 1); if (l > 1e-3) heard++; else if (l < 1e-5) quiet++; }
            Assert.That(heard, Is.GreaterThan(10), "it hoots");
            Assert.That(quiet, Is.GreaterThan(20), "and is quiet in between");
        }

        [UnityTest]
        public IEnumerator TheRecordingsInTheSoundsFolderReadLoopAndSitAtTheRightLevel()
        {
            var folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Sounds");
            if (!File.Exists(Path.Combine(folder, "sounds.json"))) { Assert.Ignore("No Sounds folder beside Assets"); yield break; }
            var it = AmbientRecordings.Load(folder);
            while (it.MoveNext()) yield return it.Current;

            foreach (var layer in new[] { "birds", "leaves", "rain", "hum", "owls" })
            {
                var r = AmbientRecordings.Get(layer);
                Assert.That(r, Is.Not.Null, layer);
                Assert.That(r.frames / (double)r.rate, Is.GreaterThan(55), $"{layer}: long enough not to hear it come round");
                Assert.That(r.gain, Is.InRange(0.02f, 1.5f), layer);
                if (r.calls) continue;
                var (inside, seam) = Jumps(r.pcm, r.frames, r.channels);
                Assert.That(seam, Is.LessThanOrEqualTo(inside), $"{layer}: seam {seam}, largest step inside {inside}");
            }

            // each place with the recordings, beside the made-up version: Logs/ambient-rec-*.wav to listen to
            var places = new (string name, Action<AmbientSynth> set)[]
            {
                ("woods", a => { a.birds = 1; a.leaves = 0.8f; a.breeze = 0.3f; }),
                ("night-woods", a => { a.owls = 1; a.leaves = 0.5f; }),
                ("rain", a => { a.rain = 1; a.breeze = 0.4f; }),
                ("town", a => { a.hum = 1; a.birds = 0.3f; }),
            };
            foreach (var (name, set) in places)
            {
                var made = Render(set, 30);
                var rec = Render(a => { set(a); a.birdsRec = AmbientRecordings.Get("birds"); a.leavesRec = AmbientRecordings.Get("leaves"); a.rainRec = AmbientRecordings.Get("rain"); a.humRec = AmbientRecordings.Get("hum"); a.owlRec = AmbientRecordings.Get("owls"); }, 30);
                Wav("rec-" + name, rec);
                Debug.Log($"ambient {name}: made-up rms {Level(made):F4} peak {Peak(made):F3}, recorded rms {Level(rec):F4} peak {Peak(rec):F3}");
                Assert.That(Peak(rec), Is.LessThan(0.9), $"{name}: no clipping");
                if (name != "night-woods") Assert.That(Level(rec), Is.InRange(Level(made) * 0.5, Level(made) * 2), $"{name}: about as loud as before");
            }
        }
    }
}
