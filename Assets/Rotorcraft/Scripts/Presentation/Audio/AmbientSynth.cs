// The sound of the world round the aircraft, made as it plays (no recordings, so it costs the web build nothing): songbirds,
// blackbird-like whistles and the odd crow by day where there are trees and fields, gulls along the coast, an owl at night,
// leaves in the wind, waves on the shore and a wash out at sea, rain, a low hum over the towns and machinery on the platforms.
// Plain C#, like RotorSynth, and mixed in with it: what it hears is set by RotorAudio from where the camera is.
// On Windows, recordings (AmbientRecordings) stand in for the songbirds, leaves, rain, town, owls and, once they are added,
// the beach and gulls: each plays at the level of the layer it replaces and that layer's generated sound drops out.
using System;

namespace Rotorcraft.Presentation
{
    public sealed class AmbientSynth
    {
        readonly int rate;
        uint seed = 0x9e3779b9u;
        public float volume = 0.6f;
        public bool paused, inside;
        /// <summary>How much of each there is here, 0..1.</summary>
        public float birds, gulls, owls, leaves, sea, rain, hum, plant, breeze;
        /// <summary>The part of sea that is waves on a beach (a recording of a beach takes this part; the open sea stays generated).</summary>
        public float shore;
        /// <summary>Recordings standing in for layers: set on Windows once read, null plays the generated sound.</summary>
        public Recording birdsRec, leavesRec, rainRec, humRec, shoreRec, gullsRec, owlRec;

        /// <summary>How loud each generated layer is at full strength (per channel, volume 1): a recording is played at this level.</summary>
        public static float TargetRms(string layer) => layer switch
        {
            "birds" => 0.025f, "leaves" => 0.015f, "rain" => 0.059f, "hum" => 0.023f,
            "owls" => 0.02f,    // while calling
            "shore" => 0.035f, "gulls" => 0.018f,
            _ => 0.02f,
        };

        // smoothed per block
        float gBirds, gGulls, gOwls, gLeaves, gSea, gRain, gHum, gPlant, gBreeze, gMaster, gInside, gShore;
        readonly Bed bBirds = new Bed(), bLeaves = new Bed(), bRain = new Bed(), bHum = new Bed(), bShore = new Bed(), bGulls = new Bed();
        readonly Bout owlBout = new Bout();
        readonly Func<float> rand;
        double t;
        readonly Biquad leafBp = new Biquad(), seaLp = new Biquad(), surfBp = new Biquad(), rainBp = new Biquad(), humLp = new Biquad(), breezeLp = new Biquad(), cabinL = new Biquad(), cabinR = new Biquad(), rustleLp = new Biquad();

        enum Call { Trill, Whistle, Gull, Crow, Owl }
        sealed class Voice
        {
            public Call kind; public bool live;
            public double age, length;               // seconds
            public int notes; public double noteLen, gap;
            public double f0, f1, phase, pan, gain;
            public double vibrato;
        }
        readonly Voice[] voices = { new Voice(), new Voice(), new Voice(), new Voice(), new Voice() };

        public AmbientSynth(int sampleRate)
        {
            rate = sampleRate;
            rand = Rand;
            leafBp.BandPass(3400, 0.6, rate);
            rustleLp.LowPass(2.2, 0.7, rate);
            seaLp.LowPass(650, 0.7, rate);
            surfBp.BandPass(2200, 0.5, rate);
            rainBp.BandPass(5200, 0.45, rate);
            humLp.LowPass(220, 0.7, rate);
            breezeLp.LowPass(420, 0.7, rate);
            cabinL.LowPass(1300, 0.6, rate);
            cabinR.LowPass(1300, 0.6, rate);
        }

        float Noise() { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; return (seed / 4294967296f) * 2 - 1; }
        float Rand() => (Noise() + 1) * 0.5f;
        static float Approach(float cur, float target, float blockS, float tau) => cur + (target - cur) * (1 - (float)Math.Exp(-blockS / tau));

        /// <summary>Add a buffer's worth (interleaved, 1 or 2 channels) to what is there.</summary>
        public void Render(float[] data, int channels)
        {
            int n = data.Length / channels;
            float blockS = n / (float)rate;
            var on = !paused;
            gMaster = Approach(gMaster, on ? volume : 0, blockS, 0.25f);
            gBirds = Approach(gBirds, birds, blockS, 1.5f); gGulls = Approach(gGulls, gulls, blockS, 1.5f); gOwls = Approach(gOwls, owls, blockS, 2f);
            gLeaves = Approach(gLeaves, leaves, blockS, 1f); gSea = Approach(gSea, sea, blockS, 1.5f); gRain = Approach(gRain, rain, blockS, 1.5f);
            gHum = Approach(gHum, hum, blockS, 2f); gPlant = Approach(gPlant, plant, blockS, 1.5f); gBreeze = Approach(gBreeze, breeze, blockS, 1.5f);
            gInside = Approach(gInside, inside ? 1 : 0, blockS, 0.15f);
            gShore = Approach(gShore, Math.Min(shore, sea), blockS, 1.5f);
            if (gMaster < 1e-4f && !on) { t += blockS; return; }

            // recordings this block, and the generated layers they stand in for drop out
            var rBirds = birdsRec; var rLeaves = leavesRec; var rRain = rainRec; var rHum = humRec; var rShore = shoreRec; var rGulls = gullsRec; var rOwl = owlRec;
            float genLeaves = rLeaves != null ? 0 : gLeaves, genRain = rRain != null ? 0 : gRain, genHum = rHum != null ? 0 : gHum;
            float genSea = rShore != null ? Math.Max(0, gSea - gShore) : gSea;
            bool pBirds = bBirds.Begin(rBirds, gBirds, rate, n, rand), pLeaves = bLeaves.Begin(rLeaves, gLeaves, rate, n, rand), pRain = bRain.Begin(rRain, gRain, rate, n, rand),
                 pHum = bHum.Begin(rHum, gHum, rate, n, rand), pShore = bShore.Begin(rShore, gShore, rate, n, rand), pGulls = bGulls.Begin(rGulls, gGulls, rate, n, rand);

            // new calls: how often depends on how many birds there are here
            if (rBirds == null)
            {
                Start(Call.Trill, gBirds * 0.55f * blockS);
                Start(Call.Whistle, gBirds * 0.22f * blockS);
                Start(Call.Crow, gBirds * 0.03f * blockS);
            }
            if (rGulls == null) Start(Call.Gull, gGulls * 0.3f * blockS);
            if (rOwl == null) Start(Call.Owl, gOwls * 0.04f * blockS);
            else if (!owlBout.Live && Rand() < gOwls * 0.04f * blockS) owlBout.Start(rOwl, rate, Rand(), 8 + Rand() * 12, (Rand() - 0.5) * 1.2);
            var owlLevel = owlBout.Live ? gOwls * owlBout.rec.gain / 32768f : 0;

            double dt = 1.0 / rate;
            for (int i = 0; i < n; i++)
            {
                t += dt;
                // beds: leaves rustling in gusts, waves breaking with a swell, rain, a town's hum, a platform's machinery, the breeze
                var rustle = 0.35 + 0.65 * Math.Abs(rustleLp.Process(Noise() * 3));
                double bed = leafBp.Process(Noise()) * 0.14 * genLeaves * rustle;
                var swell = Math.Pow(Math.Max(0, Math.Sin(t * 2 * Math.PI / 8.3)), 3) + 0.25 * Math.Pow(Math.Max(0, Math.Sin(t * 2 * Math.PI / 5.1 + 1.7)), 4);
                bed += seaLp.Process(Noise()) * 0.45 * genSea * (0.35 + 0.65 * swell) + surfBp.Process(Noise()) * 0.09 * genSea * swell;
                bed += rainBp.Process(Noise()) * 0.16 * genRain;
                bed += humLp.Process(Noise()) * 0.4 * genHum;
                bed += (Math.Sin(t * 2 * Math.PI * 47) * 0.05 + Math.Sin(t * 2 * Math.PI * 94) * 0.025 + humLp.Process(Noise()) * 0.2) * gPlant;
                bed += breezeLp.Process(Noise()) * 0.35 * gBreeze;

                // calls, each placed a little left or right
                double left = bed, right = bed;
                foreach (var v in voices)
                {
                    if (!v.live) continue;
                    var s = Sing(v, dt);
                    left += s * (1 - v.pan) ; right += s * (1 + v.pan);
                }
                // recordings
                if (pBirds) bBirds.Mix(ref left, ref right);
                if (pLeaves) bLeaves.Mix(ref left, ref right);
                if (pRain) bRain.Mix(ref left, ref right);
                if (pHum) bHum.Mix(ref left, ref right);
                if (pShore) bShore.Mix(ref left, ref right);
                if (pGulls) bGulls.Mix(ref left, ref right);
                if (owlBout.Live) owlBout.Mix(ref left, ref right, owlLevel);
                // heard from inside the cockpit: muffled and quieter
                var keep = 1 - 0.65 * gInside;
                double l = left * keep, r = right * keep;
                if (gInside > 0.01f) { l = l * (1 - gInside) + cabinL.Process(l) * gInside; r = r * (1 - gInside) + cabinR.Process(r) * gInside; }
                if (channels == 1) data[i] += (float)((l + r) * 0.5 * gMaster);
                else { data[i * channels] += (float)(l * gMaster); data[i * channels + 1] += (float)(r * gMaster); }
            }
        }

        void Start(Call kind, float chance)
        {
            if (Rand() >= chance) return;
            Voice v = null;
            foreach (var x in voices) if (!x.live) { v = x; break; }
            if (v == null) return;
            v.live = true; v.kind = kind; v.age = 0; v.phase = 0; v.pan = (Rand() - 0.5) * 1.4; v.gain = 0.35 + 0.65 * Rand();
            switch (kind)
            {
                case Call.Trill:   // quick little rising and falling notes, high up
                    v.notes = 4 + (int)(Rand() * 9); v.noteLen = 0.04 + Rand() * 0.05; v.gap = 0.02 + Rand() * 0.04;
                    v.f0 = 2600 + Rand() * 2800; v.f1 = v.f0 * (0.7 + Rand() * 0.7); v.gain *= 0.13; break;
                case Call.Whistle: // a few fluting notes, falling, with a little vibrato
                    v.notes = 3 + (int)(Rand() * 4); v.noteLen = 0.13 + Rand() * 0.17; v.gap = 0.06 + Rand() * 0.08;
                    v.f0 = 1700 + Rand() * 1300; v.f1 = v.f0 * (0.82 + Rand() * 0.15); v.vibrato = 5 + Rand() * 3; v.gain *= 0.11; break;
                case Call.Gull:    // "kyow", falling, rough
                    v.notes = 1 + (int)(Rand() * 4); v.noteLen = 0.24 + Rand() * 0.15; v.gap = 0.12 + Rand() * 0.1;
                    v.f0 = 1150 + Rand() * 350; v.f1 = 650 + Rand() * 150; v.gain *= 0.07; break;
                case Call.Crow:    // two or three harsh caws
                    v.notes = 2 + (int)(Rand() * 2); v.noteLen = 0.22 + Rand() * 0.08; v.gap = 0.18; v.f0 = 640; v.f1 = 560; v.gain *= 0.06; break;
                default:           // an owl: hoo, hoo-hooo
                    v.notes = 3; v.noteLen = 0.32; v.gap = 0.28; v.f0 = 390; v.f1 = 360; v.gain *= 0.07; break;
            }
            v.length = v.notes * (v.noteLen + v.gap);
        }

        double Sing(Voice v, double dt)
        {
            v.age += dt;
            if (v.age >= v.length) { v.live = false; return 0; }
            var slot = v.noteLen + v.gap;
            var k = (int)(v.age / slot);
            var within = v.age - k * slot;
            if (within > v.noteLen) return 0;
            var u = within / v.noteLen;
            // each note a little lower or higher than the last, as birds sing
            var drift = v.kind == Call.Trill ? (k % 2 == 0 ? 1 : 0.86) : 1 - 0.04 * k;
            var f = (v.f0 + (v.f1 - v.f0) * u) * drift + (v.vibrato > 0 ? Math.Sin(v.age * 2 * Math.PI * v.vibrato) * 35 : 0);
            if (v.kind == Call.Owl && k == 2) f *= 0.96;
            v.phase += 2 * Math.PI * f * dt;
            var env = Math.Sin(Math.PI * u);
            env = v.kind == Call.Owl ? Math.Pow(env, 0.6) : env * env;
            double s;
            switch (v.kind)
            {
                case Call.Gull: s = Math.Sin(v.phase) + 0.45 * Math.Sin(2 * v.phase) + 0.25 * Math.Sin(3 * v.phase) + Noise() * 0.15; break;
                case Call.Crow: s = Math.Sign(Math.Sin(v.phase)) * 0.5 + Noise() * 0.6; break;
                default: s = Math.Sin(v.phase) + 0.15 * Math.Sin(2 * v.phase); break;
            }
            return s * env * v.gain;
        }

        /// <summary>A looped recording playing under everything, at its layer's level.</summary>
        sealed class Bed
        {
            Recording rec; double pos, step; float level;

            /// <summary>Take this block's recording and level; false when there is nothing to hear (it still moves on, so it doesn't restart in the same place).</summary>
            public bool Begin(Recording r, float g, int outRate, int n, Func<float> rand)
            {
                if (r != rec) { rec = r; pos = r == null ? 0 : Math.Floor(rand() * (r.frames - 1)); }   // each starts somewhere different
                if (r == null || r.frames < 2) return false;
                step = r.rate / (double)outRate;
                level = g * r.gain / 32768f;
                if (g < 1e-4f) { pos = (pos + n * step) % r.frames; return false; }
                return true;
            }

            public void Mix(ref double l, ref double r)
            {
                var x = rec; int i0 = (int)pos; var fr = pos - i0; int i1 = i0 + 1 >= x.frames ? 0 : i0 + 1;
                if (x.channels == 1) { var v = (x.pcm[i0] + (x.pcm[i1] - x.pcm[i0]) * fr) * level; l += v; r += v; }
                else
                {
                    int a = i0 * x.channels, b = i1 * x.channels;
                    l += (x.pcm[a] + (x.pcm[b] - x.pcm[a]) * fr) * level;
                    r += (x.pcm[a + 1] + (x.pcm[b + 1] - x.pcm[a + 1]) * fr) * level;
                }
                pos += step; if (pos >= x.frames) pos -= x.frames;
            }
        }

        /// <summary>A stretch of a recording of calls (an owl hooting), played now and then, faded in and out and placed left or right.</summary>
        sealed class Bout
        {
            public Recording rec; double pos, step, pan; int left, length, fade;
            public bool Live => left > 0;

            public void Start(Recording r, int outRate, double startFrac, double seconds, double pan)
            {
                if (r == null || r.frames < 2) return;
                rec = r; step = r.rate / (double)outRate; this.pan = pan;
                pos = Math.Floor(startFrac * (r.frames - 1));
                length = left = (int)(seconds * outRate);
                fade = (int)(0.6 * outRate);
            }

            public void Mix(ref double l, ref double r, float level)
            {
                var x = rec; int i0 = (int)pos; var fr = pos - i0; int i1 = i0 + 1 >= x.frames ? 0 : i0 + 1;
                double v = 0;
                for (int c = 0; c < x.channels; c++) v += x.pcm[i0 * x.channels + c] + (x.pcm[i1 * x.channels + c] - x.pcm[i0 * x.channels + c]) * fr;
                v /= x.channels;
                var env = Math.Min(1, Math.Min(length - left, left) / (double)fade);
                v *= level * env;
                l += v * (1 - pan); r += v * (1 + pan);
                pos += step; if (pos >= x.frames) pos -= x.frames;
                left--;
            }
        }

        sealed class Biquad
        {
            double b0, b1, b2, a1, a2, x1, x2, y1, y2;
            public void LowPass(double f, double q, int rate) => Set(f, q, rate, false);
            public void BandPass(double f, double q, int rate) => Set(f, q, rate, true);
            void Set(double f, double q, int rate, bool band)
            {
                var w = 2 * Math.PI * f / rate; var al = Math.Sin(w) / (2 * q); var c = Math.Cos(w); var a0 = 1 + al;
                if (band) { b0 = al / a0; b1 = 0; b2 = -al / a0; }
                else { b0 = (1 - c) / 2 / a0; b1 = (1 - c) / a0; b2 = (1 - c) / 2 / a0; }
                a1 = -2 * c / a0; a2 = (1 - al) / a0;
            }
            public double Process(double x)
            {
                var y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                return y;
            }
        }
    }
}
