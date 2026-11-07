// Measures how each aircraft answers the stick and the wind, and writes a table to Logs/handling.txt. A test only so that it
// runs inside the bench: it holds no opinion. HandlingTests holds the limits, using the measurements here.
using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using static Rotorcraft.Sim.SimMath;

namespace Rotorcraft.Sim.Tests
{
    public class HandlingReport
    {
        const double DT = FixedStepper.SIM_DT, BankPerStick = 28, PitchPerStick = 22;
        public static readonly string[] Types = { "kestrel", "northstar", "guardian", "nightjar", "peregrine" };

        public struct Step { public double peak, settled, overshootPct, to90, peakRate; }
        public struct Hold { public double meanStick, peakStick, stickTravel, climbSwing, noseSwing, drift; }

        /// <summary>A hover trimmed in still air, 25 m up on flat ground, facing north.</summary>
        public static (HeliState s, double lever) Hover(AircraftConfig ac, AssistMode assist)
        {
            var s = TestHelpers.Create(0, 0, 0, ac.fuelCapacity * 0.5, true, new Airborne(25, 0));
            var lever = 0.55;
            var still = Weather.Presets["calm"];
            var noWind = new WeatherPreset { id = "still", name = "still", windKt = 0, gustKt = 0, turbulence = 0, visibilityM = 30000, oatC = still.oatC, qnh = still.qnh, seed = 1 };
            for (int i = 0; i < 30 / DT; i++)
            {
                lever = Clamp(lever + (-s.vel.y * 0.5 + (25 - s.@out.aglM) * 0.1) * DT, 0, 1);
                FlightModel.Step(s, new Controls(lever, 0, 0, 0), ac, noWind, new SimSettings(assist), DT);
                s.events.Clear();
            }
            return (s, lever);
        }

        /// <summary>The stick stepped to a position and left there, from a hover: how far and how fast the bank or pitch goes.</summary>
        public static Step StepStick(string type, AssistMode assist, bool roll, double stick)
        {
            var ac = Aircraft.Get(type);
            var (s, lever) = Hover(ac, assist);
            var calm = new WeatherPreset { id = "still", name = "still", windKt = 0, gustKt = 0, turbulence = 0, visibilityM = 30000, oatC = 14, qnh = 1021, seed = 1 };
            double peak = 0, to90 = -1, peakRate = 0;
            var want = stick * (roll ? BankPerStick : PitchPerStick) * DEG;
            for (int i = 0; i < 6 / DT; i++)
            {
                FlightModel.Step(s, new Controls(lever, roll ? 0 : stick, roll ? stick : 0, 0), ac, calm, new SimSettings(assist), DT);
                s.events.Clear();
                var a = Math.Abs(roll ? s.phi : s.theta);
                var r = Math.Abs(roll ? s.p : s.q);
                peak = Math.Max(peak, a);
                peakRate = Math.Max(peakRate, r);
                if (to90 < 0 && a >= want * 0.9) to90 = i * DT;
            }
            var settled = Math.Abs(roll ? s.phi : s.theta);
            return new Step { peak = peak * RAD, settled = settled * RAD, overshootPct = (peak / Math.Max(settled, 1e-6) - 1) * 100, to90 = to90, peakRate = peakRate * RAD };
        }

        /// <summary>
        /// A steady pilot holding a hover over one spot in this weather, with the nose held 90° off the wind so the wind is all on
        /// one side, then left alone on the pedals: how much stick it takes, how much the stick has to move, how far the height wanders.
        /// </summary>
        public static Hold HoldSpot(string type, AssistMode assist, string weather, double secs = 40)
        {
            var ac = Aircraft.Get(type);
            var (s, lever) = Hover(ac, assist);
            var wx = Weather.Presets[weather];
            s.psi = WrapPositive((wx.windDirDeg + 90) * DEG);
            double sum = 0, peak = 0, travel = 0, lastP = 0, lastR = 0, minVy = 0, maxVy = 0, noseStart = s.psi, nose = 0, drift = 0;
            var n = (int)(secs / DT);
            for (int i = 0; i < n; i++)
            {
                var a = BodyAxesOf(s.psi, 0, 0);
                // accelerate back towards the spot; the tilt that takes, as stick
                double ax = -s.pos.x * 0.12 - s.vel.x * 0.7, az = -s.pos.z * 0.12 - s.vel.z * 0.7;
                double wantF = ax * a.f.x + az * a.f.z, wantR = ax * a.r.x + az * a.r.z;
                var pitchStick = Clamp(Math.Atan(wantF / G) * RAD / PitchPerStick, -1, 1);
                var rollStick = Clamp(Math.Atan(wantR / G) * RAD / BankPerStick, -1, 1);
                // a slow hand on the lever: only the slow drift in height is taken out
                lever = Clamp(lever + (-s.vel.y * 0.05 + (25 - s.@out.aglM) * 0.02) * DT, 0, 1);
                FlightModel.Step(s, new Controls(lever, pitchStick, rollStick, 0), ac, wx, new SimSettings(assist), DT);
                s.events.Clear();
                if (i * DT < 10) { lastP = pitchStick; lastR = rollStick; continue; }   // what matters is the steady part
                var mag = Math.Sqrt(pitchStick * pitchStick + rollStick * rollStick);
                sum += mag; peak = Math.Max(peak, mag);
                travel += Math.Abs(pitchStick - lastP) + Math.Abs(rollStick - lastR);
                lastP = pitchStick; lastR = rollStick;
                minVy = Math.Min(minVy, s.vel.y); maxVy = Math.Max(maxVy, s.vel.y);
                nose = Math.Max(nose, Math.Abs(WrapPi(s.psi - noseStart)));
                drift = Math.Max(drift, Math.Sqrt(s.pos.x * s.pos.x + s.pos.z * s.pos.z));
            }
            var counted = n - 10 / DT;
            return new Hold { meanStick = sum / counted * 100, peakStick = peak * 100, stickTravel = travel / ((secs - 10) / 60), climbSwing = (maxVy - minVy) * 196.85, noseSwing = nose * RAD, drift = drift };
        }

        static double WrapPositive(double a) { a = WrapPi(a); return a < 0 ? a + 2 * Math.PI : a; }

        static readonly WeatherPreset StillAir = new WeatherPreset { id = "still", name = "still", windKt = 0, gustKt = 0, turbulence = 0, visibilityM = 30000, oatC = 14, qnh = 1021, seed = 1 };

        /// <summary>The steady climb one per cent more lever settles on (ft/min), against the same hover with the lever left alone.</summary>
        public static double FpmPerPercentLever(string type, AssistMode assist)
        {
            var ac = Aircraft.Get(type);
            double After(double extra)
            {
                var (s, lever) = Hover(ac, assist);
                for (int i = 0; i < 12 / DT; i++) { FlightModel.Step(s, new Controls(lever + extra, 0, 0, 0), ac, StillAir, new SimSettings(assist), DT); s.events.Clear(); }
                return s.vel.y * 196.85;
            }
            return After(0.01) - After(0);
        }

        /// <summary>Hands off everything in a hover, nose 90° off the wind, for 20 seconds: ground speed reached (kt) and how far the nose went (deg).</summary>
        public static (double kt, double nose) Blown(string type, AssistMode assist, string weather)
        {
            var ac = Aircraft.Get(type);
            var (s, lever) = Hover(ac, assist);
            var wx = Weather.Presets[weather];
            s.psi = WrapPositive((wx.windDirDeg + 90) * DEG);
            double start = s.psi, nose = 0;
            for (int i = 0; i < 20 / DT; i++)
            {
                lever = Clamp(lever + (-s.vel.y * 0.3 + (25 - s.@out.aglM) * 0.05) * DT, 0, 1);
                FlightModel.Step(s, new Controls(lever, 0, 0, 0), ac, wx, new SimSettings(assist), DT);
                s.events.Clear();
                nose = Math.Max(nose, Math.Abs(WrapPi(s.psi - start)));
            }
            return (Math.Sqrt(s.vel.x * s.vel.x + s.vel.z * s.vel.z) * 1.943844, nose * RAD);
        }

        /// <summary>How the height moves over a long time with a slow hand on a lever that moves at a rate (keyboard, thumbstick): the swing in the first and last ten seconds (ft/min). Growing means it is running away.</summary>
        public static (double first, double last) HeightHunt(string type, AssistMode assist)
        {
            var ac = Aircraft.Get(type);
            var (s, lever) = Hover(ac, assist);
            double a0 = 0, a1 = 0, b0 = 0, b1 = 0;
            const double Secs = 60;
            for (int i = 0; i < Secs / DT; i++)
            {
                lever = Clamp(lever + (-s.vel.y * 0.05 + (25 - s.@out.aglM) * 0.02) * DT, 0, 1);
                FlightModel.Step(s, new Controls(lever, 0, 0, 0), ac, StillAir, new SimSettings(assist), DT);
                s.events.Clear();
                var t = i * DT;
                if (t < 10) { a0 = Math.Min(a0, s.vel.y); a1 = Math.Max(a1, s.vel.y); }
                if (t > Secs - 10) { b0 = Math.Min(b0, s.vel.y); b1 = Math.Max(b1, s.vel.y); }
            }
            return ((a1 - a0) * 196.85, (b1 - b0) * 196.85);
        }

        [Test, Explicit("Writes a table: run on purpose")]
        public void WriteTheTable()
        {
            var o = new StringBuilder();
            o.AppendLine("LEVER, Standard: steady climb from 1% more lever ft/min; height swing with a slow hand on the lever, first 10 s then last 10 s of a minute ft/min");
            foreach (var t in Types)
            {
                var h = HeightHunt(t, AssistMode.Standard);
                o.AppendLine($"  {t,-10} {FpmPerPercentLever(t, AssistMode.Standard),5:0} per 1%    swing {h.first,6:0} -> {h.last,6:0}");
            }
            o.AppendLine();
            o.AppendLine("HANDS OFF for 20 s in a hover, nose 90 deg off the wind, Standard: ground speed reached kt, nose moved deg");
            foreach (var w in new[] { "calm", "light_sw", "fair_nw", "crosswind_n", "breezy_w", "showers", "glen_closing", "north_sea" })
            {
                var b = Blown("peregrine", AssistMode.Standard, w);
                var k = Blown("kestrel", AssistMode.Standard, w);
                o.AppendLine($"  {w,-13} {Weather.Presets[w].windKt,3:0}G{Weather.Presets[w].windKt + Weather.Presets[w].gustKt,-3:0} kt   peregrine {b.kt,5:0.0} kt nose {b.nose,5:0.0}    kestrel {k.kt,5:0.0} kt nose {k.nose,5:0.0}");
            }
            o.AppendLine();
            o.AppendLine("STICK STEPS from the hover, Standard: stick%  peak deg, settled deg, overshoot %, time to 90% s, peak rate deg/s");
            foreach (var t in Types)
                foreach (var (name, roll) in new[] { ("roll", true), ("pitch", false) })
                    foreach (var stick in new[] { 0.15, 0.5 })
                    {
                        var r = StepStick(t, AssistMode.Standard, roll, stick);
                        o.AppendLine($"  {t,-10} {name,-6} {stick * 100,3:0}%   peak {r.peak,5:0.0}  settled {r.settled,5:0.0}  over {r.overshootPct,5:0.0}%  to90 {r.to90,5:0.00}s  rate {r.peakRate,5:0.0}");
                    }
            o.AppendLine();
            o.AppendLine("HOLDING A SPOT, nose 90 deg off the wind, Standard: mean stick %, peak stick %, stick travel per minute (full throws), height rate swing ft/min, nose swing deg, furthest from the spot m");
            foreach (var t in new[] { "kestrel", "nightjar", "peregrine", "guardian" })
                foreach (var w in new[] { "calm", "light_sw", "fair_nw", "crosswind_n", "breezy_w", "showers", "glen_closing", "north_sea" })
                {
                    var h = HoldSpot(t, AssistMode.Standard, w);
                    var wx = Weather.Presets[w];
                    o.AppendLine($"  {t,-10} {w,-13} {wx.windKt,3:0}G{wx.windKt + wx.gustKt,-3:0} kt  stick {h.meanStick,5:0.0} peak {h.peakStick,5:0.0}  travel {h.stickTravel,5:0.0}  height swing {h.climbSwing,6:0}  nose {h.noseSwing,6:0.0}  away {h.drift,5:0.0}");
                }
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/handling.txt", o.ToString());
            TestContext.Out.WriteLine(o.ToString());
        }
    }
}
