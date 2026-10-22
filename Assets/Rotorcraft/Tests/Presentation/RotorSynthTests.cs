// The helicopter sound rendered offline: it must be silent with the engine off, audible and never clipping at
// flight RPM, and louder with power and airspeed. Each case is also written to Logs/sound-*.wav to listen to.
using System;
using System.IO;
using NUnit.Framework;
using Rotorcraft.Presentation;

namespace Rotorcraft.Presentation.Tests
{
    public class RotorSynthTests
    {
        const int Rate = 48000;

        static float[] Render(Action<RotorSynth> set, float seconds = 3, string wav = null, Action<RotorSynth, int> perBlock = null)
        {
            var s = new RotorSynth(Rate);
            set(s);
            var outBuf = new float[(int)(Rate * seconds) * 2];
            var block = new float[1024 * 2];
            for (int b = 0, at = 0; at < outBuf.Length; b++)
            {
                perBlock?.Invoke(s, b);
                Array.Clear(block, 0, block.Length);
                s.Render(block, 2);
                var n = Math.Min(block.Length, outBuf.Length - at);
                Array.Copy(block, 0, outBuf, at, n); at += n;
            }
            if (wav != null) WriteWav($"Logs/{wav}.wav", outBuf, 2);
            return outBuf;
        }

        static double Rms(float[] d, int from = 0)
        {
            double sum = 0; for (int i = from; i < d.Length; i++) sum += d[i] * d[i];
            return Math.Sqrt(sum / Math.Max(1, d.Length - from));
        }

        static void WriteWav(string path, float[] d, int ch)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using var w = new BinaryWriter(File.Create(path));
            w.Write("RIFF".ToCharArray()); w.Write(36 + d.Length * 2); w.Write("WAVEfmt ".ToCharArray());
            w.Write(16); w.Write((short)1); w.Write((short)ch); w.Write(Rate); w.Write(Rate * ch * 2); w.Write((short)(ch * 2)); w.Write((short)16);
            w.Write("data".ToCharArray()); w.Write(d.Length * 2);
            foreach (var v in d) w.Write((short)Math.Round(Math.Max(-1, Math.Min(1, v)) * 32767));
        }

        [Test]
        public void SilentWithEverythingStopped()
        {
            var d = Render(s => { s.rpm = 0; s.torque = 0; s.engine = 0; s.airspeedKt = 0; });
            Assert.Less(Rms(d, Rate), 1e-3);
        }

        [Test]
        public void HoverIsAudibleAndNeverClips()
        {
            var d = Render(s => { s.rpm = 100; s.torque = 80; s.engine = 1; }, wav: "sound-hover");
            var rms = Rms(d, Rate);
            Assert.Greater(rms, 0.02, "too quiet");
            foreach (var v in d) { Assert.False(float.IsNaN(v)); Assert.LessOrEqual(Math.Abs(v), 1f); }
        }

        [Test]
        public void LouderWithPowerAndSpeed()
        {
            var idle = Rms(Render(s => { s.rpm = 100; s.torque = 15; s.engine = 1; }), Rate);
            var cruise = Rms(Render(s => { s.rpm = 100; s.torque = 85; s.engine = 1; s.airspeedKt = 100; }, wav: "sound-cruise"), Rate);
            Assert.Greater(cruise, idle);
        }

        [Test]
        public void StartUpAndHorn()
        {
            // engine start and rotor run-up over 8 s, then the low RPM horn while the rotor droops
            Render(s => { s.engine = 0.01f; }, 10, "sound-startup", (s, b) =>
            {
                var t = b * 1024f / Rate;
                s.engine = Math.Min(1, t / 4);
                s.rpm = Math.Min(100, Math.Max(0, (t - 1) * 14));
                s.torque = 10;
                s.horn = t > 8.5f;
                if (t > 8.5f) s.rpm = 88;
            });
            Assert.Pass();
        }

        [Test]
        public void ThumpOnTouchdown()
        {
            var d = Render(s => { s.rpm = 0; s.engine = 0; }, 0.5f, null, (s, b) => { if (b == 2) s.Thump(1.5f); });
            Assert.Greater(Rms(d), 0.01);
        }
    }
}
