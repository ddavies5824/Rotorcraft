// The helicopter's sound, synthesised sample by sample as the browser game does with WebAudio (audio/audio.ts):
// nothing recorded, so nothing to license. Rotor wash is brown noise through a low-pass filter, beaten by the
// blade passage (here a sharp "whop" pulse with a low body tone rather than the browser game's sine); the engine is
// a filtered sawtooth (a whine on top for a turbine); the tail rotor a faint buzz; wind a band of noise rising
// with airspeed; the low rotor RPM horn a warbling square wave; touchdowns a short low thump.
// Plain C# so it can be rendered offline in tests; RotorAudio feeds it from Unity's audio thread.
using System;

namespace Rotorcraft.Presentation
{
    public sealed class RotorSynth
    {
        // ---- what the aircraft is doing (set from the game thread; read once per block)
        public volatile float rpm;           // % rotor RPM
        public volatile float torque;        // % torque
        public volatile float engine;        // 0..1: 1 running, else start progress
        public volatile float airspeedKt;
        public volatile bool horn;
        public volatile bool paused;
        public volatile bool inside;         // heard from the cockpit: wash and wind muffled
        public float volume = 0.6f;
        public bool turbine;
        public float bladePassHz = 17.6f;    // blade passages a second at 100% RPM (blades × rev/s)
        public float tailBuzzHz = 106f;      // tail rotor blade passages a second at 100%

        readonly int rate;
        uint seed = 0x9e3779b9;
        float brown, brown2;
        double slapPhase, engPhase, whinePhase, tailPhase, hornPhase, bodyPhase, warblePhase, thumpPhase;
        readonly Biquad rotorLp = new Biquad(), engLp = new Biquad(), windBp = new Biquad(), hornLp = new Biquad(), tailLp = new Biquad(), cabinLp = new Biquad();
        // smoothed values (per block, then ramped per sample)
        float gMaster, gRotor, gEng, gWind, gHorn, gTail, fRotor = 400, fSlap = 17.6f, fEng = 40, gInside;
        float thumpGain, thumpAge = 10;
        volatile float pendingThump = -1;

        public RotorSynth(int sampleRate)
        {
            rate = sampleRate;
            engLp.LowPass(600, 0.707, rate);
            windBp.BandPass(900, 0.5, rate);
            hornLp.LowPass(1800, 0.707, rate);
            tailLp.LowPass(700, 0.707, rate);
            cabinLp.LowPass(1400, 0.6, rate);
        }

        /// <summary>A touchdown: a low thump, stronger with the sink rate (m/s).</summary>
        public void Thump(float strength) => pendingThump = Math.Min(1, 0.25f + strength * 0.4f);

        float Noise()
        {
            seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
            return (seed / 4294967296f) * 2 - 1;
        }

        static float Approach(float cur, float target, float blockS, float tau) => cur + (target - cur) * (1 - (float)Math.Exp(-blockS / tau));

        /// <summary>Fill an interleaved buffer (adds to what is there: mix over silence or other sound).</summary>
        public void Render(float[] data, int channels)
        {
            int n = data.Length / channels;
            float blockS = n / (float)rate;
            // targets, as the browser game sets them
            var r = Math.Max(0, rpm) / 100f;
            var on = !paused;
            var tMaster = on ? volume : 0;
            var tRotor = Math.Min(1, r * r) * (0.5f + 0.3f * Math.Min(1.2f, torque / 100f));
            var tFRotor = 250 + 450 * r + torque * 3;
            var tSlap = Math.Max(0.5f, bladePassHz * r);
            var tEng = engine > 0 ? 0.05f + 0.06f * engine : 0;
            var tFEng = 30 + 70 * Math.Max(r, engine * 0.6f);
            var tWind = Math.Min(0.35f, (airspeedKt / 120f) * (airspeedKt / 120f) * 0.35f) * (inside ? 0.35f : 1);
            var tHorn = horn ? 0.12f : 0;
            var tTail = 0.035f * Math.Min(1, r * r);
            var tInside = inside ? 1f : 0f;
            // the block's start and end values (ramped across it so nothing clicks)
            float m0 = gMaster, ro0 = gRotor, e0 = gEng, w0 = gWind, h0 = gHorn, ta0 = gTail, in0 = gInside;
            gMaster = Approach(gMaster, tMaster, blockS, 0.05f);
            gRotor = Approach(gRotor, tRotor, blockS, 0.08f);
            fRotor = Approach(fRotor, tFRotor, blockS, 0.1f);
            fSlap = Approach(fSlap, tSlap, blockS, 0.05f);
            gEng = Approach(gEng, tEng, blockS, 0.15f);
            fEng = Approach(fEng, tFEng, blockS, 0.1f);
            gWind = Approach(gWind, tWind, blockS, 0.2f);
            gHorn = Approach(gHorn, tHorn, blockS, 0.02f);
            gTail = Approach(gTail, tTail, blockS, 0.1f);
            gInside = Approach(gInside, tInside, blockS, 0.1f);
            rotorLp.LowPass(fRotor, 0.707, rate);
            var p = pendingThump; if (p >= 0) { pendingThump = -1; thumpGain = p; thumpAge = 0; thumpPhase = 0; }
            if (m0 < 1e-4f && gMaster < 1e-4f) return;   // silent: leave the buffer as it is

            double dSlap = fSlap / (double)rate, dEng = fEng / (double)rate, dTail = tailBuzzHz * r / (double)rate;
            double dBody = fSlap * 4 / (double)rate, dWhine = (900 + 5200 * Math.Max(r, engine * 0.7f)) / (double)rate;
            double dHorn = 620.0 / rate, dWarble = 3.0 / rate, dThump = 70.0 / rate;
            for (int i = 0; i < n; i++)
            {
                float k = (i + 1) / (float)n;
                float gm = m0 + (gMaster - m0) * k, gr = ro0 + (gRotor - ro0) * k, ge = e0 + (gEng - e0) * k, gw = w0 + (gWind - w0) * k;
                float gh = h0 + (gHorn - h0) * k, gt = ta0 + (gTail - ta0) * k, gi = in0 + (gInside - in0) * k;
                // brown noise, two independent streams (wash and wind)
                brown = (brown + 0.02f * Noise()) / 1.02f;
                brown2 = (brown2 + 0.02f * Noise()) / 1.02f;
                // rotor: filtered wash beaten by the blade passage, with a low body note on each pass
                slapPhase += dSlap; if (slapPhase >= 1) slapPhase -= 1;
                bodyPhase += dBody; if (bodyPhase >= 1) bodyPhase -= 1;
                var c = 0.5 + 0.5 * Math.Cos(2 * Math.PI * slapPhase);
                var pulse = (float)(c * c * c * c);
                var wash = (float)rotorLp.Process(brown * 3.5f) * (0.35f + 0.9f * pulse);
                var body = (float)Math.Sin(2 * Math.PI * bodyPhase) * pulse * 0.35f;
                var rotor = (wash + body) * gr;
                // engine: sawtooth through a low-pass; a turbine also whines
                engPhase += dEng; if (engPhase >= 1) engPhase -= 1;
                var eng = (float)engLp.Process(engPhase * 2 - 1) * ge;
                if (turbine)
                {
                    whinePhase += dWhine; if (whinePhase >= 1) whinePhase -= 1;
                    eng += (float)Math.Sin(2 * Math.PI * whinePhase) * ge * 0.25f;
                }
                // tail rotor buzz
                tailPhase += dTail; if (tailPhase >= 1) tailPhase -= 1;
                var tail = (float)tailLp.Process(tailPhase * 2 - 1) * gt;
                // wind
                var wind = (float)windBp.Process(brown2 * 3.5f) * gw;
                // low rotor RPM horn, warbling at 3 Hz
                hornPhase += dHorn; if (hornPhase >= 1) hornPhase -= 1;
                warblePhase += dWarble; if (warblePhase >= 1) warblePhase -= 1;
                var hornS = (float)hornLp.Process(hornPhase < 0.5 ? 1 : -1) * gh * (warblePhase < 0.5 ? 1 : 0.55f);
                // touchdown thump: holds 20 ms, then dies away
                float thump = 0;
                if (thumpAge < 0.4f)
                {
                    thumpPhase += dThump;
                    var env = thumpAge < 0.02f ? 1 : (float)Math.Exp(-(thumpAge - 0.02f) / 0.06f);
                    thump = (float)Math.Sin(2 * Math.PI * thumpPhase) * thumpGain * env;
                    thumpAge += 1f / rate;
                }
                // from the cockpit, the outside sound comes through the canopy muffled
                var outside = rotor + tail + wind;
                if (gi > 0.001f) outside = outside * (1 - gi) + (float)cabinLp.Process(outside) * gi * 0.8f;
                var s = (outside + eng + hornS + thump) * gm;
                s = s / (1 + Math.Abs(s) * 0.3f);   // a soft limiter: loud moments never clip
                for (int ch = 0; ch < channels; ch++) data[i * channels + ch] += s;
            }
        }

        /// <summary>A second-order filter (RBJ audio cookbook), transposed direct form II.</summary>
        sealed class Biquad
        {
            double b0, b1, b2, a1, a2, z1, z2;

            public void LowPass(double f, double q, int rate)
            {
                var w = 2 * Math.PI * Math.Min(f, rate * 0.45) / rate; var al = Math.Sin(w) / (2 * q); var cw = Math.Cos(w); var a0 = 1 + al;
                b0 = (1 - cw) / 2 / a0; b1 = (1 - cw) / a0; b2 = b0; a1 = -2 * cw / a0; a2 = (1 - al) / a0;
            }

            public void BandPass(double f, double q, int rate)
            {
                var w = 2 * Math.PI * f / rate; var al = Math.Sin(w) / (2 * q); var cw = Math.Cos(w); var a0 = 1 + al;
                b0 = al / a0; b1 = 0; b2 = -al / a0; a1 = -2 * cw / a0; a2 = (1 - al) / a0;
            }

            public double Process(double x)
            {
                var y = b0 * x + z1;
                z1 = b1 * x - a1 * y + z2;
                z2 = b2 * x - a2 * y;
                return y;
            }
        }
    }
}
