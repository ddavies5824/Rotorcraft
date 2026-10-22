// AssessmentEngine: pure scoring functions with configurable thresholds (port of src/game/assessment.ts).
using System;
using System.Collections.Generic;
using Rotorcraft.Sim;
using static Rotorcraft.Sim.SimMath;

namespace Rotorcraft.Game
{
    public struct LandingInput { public double vsMs, gsMs, roll, pitch; public double? distM; public double maxDistM; }
    public struct LandingParts { public int vs, gs, attitude, accuracy; }

    /// <summary>
    /// A hover is marked the way an examiner marks it: on how steady it was over the time spent in it, not on the
    /// approach. drift: how much the position wandered (standard deviation, m); offset: how far the average
    /// position was from the spot; heightWander and yawWander likewise for height and heading; outOfBand: how far
    /// outside the height band it strayed (RMS). Dead still anywhere inside the circle and band scores 95-100.
    /// </summary>
    public struct HoverStats { public double drift, offset, radiusM, heightWander, outOfBand, halfBandM, yawWander, hdgTolDeg; public int resets; }

    public static class Assessment
    {
        public static readonly double[] VS_THRESHOLDS = { 0.45, 0.9, 1.5 };     // m/s: smooth (<90 fpm) / normal (<180) / firm (<300); above = hard
        public static readonly double[] GS_THRESHOLDS = { 0.6, 1.5, 3.0 };      // m/s ground speed at touchdown
        public static readonly double[] ATTITUDE_THRESHOLDS = { 3, 6, 10 };     // degrees of bank (pitch allowed 1.5x)

        static double Band(double v, double[] t, double s0 = 100, double s1 = 80, double s2 = 55)
        {
            if (v <= t[0]) return s0;
            if (v <= t[1]) return s0 - (s0 - s1) * ((v - t[0]) / (t[1] - t[0]));
            if (v <= t[2]) return s1 - (s1 - s2) * ((v - t[1]) / (t[2] - t[1]));
            return Math.Max(0, s2 - (v - t[2]) * 25);
        }

        /// <summary>Landing score 0..100 with component breakdown.</summary>
        public static (int score, LandingParts parts) ScoreLanding(LandingInput l)
        {
            var vs = Band(l.vsMs, VS_THRESHOLDS);
            var gs = Band(l.gsMs, GS_THRESHOLDS);
            var att = Band(Math.Max(Math.Abs(l.roll), Math.Abs(l.pitch) / 1.5), ATTITUDE_THRESHOLDS);
            double acc = 100;
            if (l.distM != null)
            {
                var r = l.distM.Value / Math.Max(0.5, l.maxDistM);
                acc = r <= 0.3 ? 100 : r <= 1 ? 100 - (r - 0.3) * 60 : Math.Max(0, 58 - (r - 1) * 80);
            }
            var score = vs * 0.55 + gs * 0.15 + att * 0.1 + acc * 0.2;
            if (l.vsMs > 1.8) score = Math.Min(score, 40); // a hard landing is never better than 'F'
            return ((int)Js.Round(score), new LandingParts { vs = (int)Js.Round(vs), gs = (int)Js.Round(gs), attitude = (int)Js.Round(att), accuracy = (int)Js.Round(acc) });
        }

        public static string LandingGrade(int score) => score >= 90 ? "A" : score >= 78 ? "B" : score >= 65 ? "C" : score >= 50 ? "D" : "F";

        public static int ScoreHover(HoverStats h)
        {
            var pos = Clamp(100 - (h.drift / h.radiusM) * 110 - Math.Max(0, h.offset / h.radiusM - 0.4) * 30, 0, 100);
            var alt = Clamp(100 - (h.heightWander / Math.Max(0.3, h.halfBandM)) * 70 - (h.outOfBand / Math.Max(0.3, h.halfBandM)) * 60, 0, 100);
            var hdg = h.hdgTolDeg > 0 ? Clamp(100 - (h.yawWander / h.hdgTolDeg) * 70, 0, 100) : 100;
            return (int)Js.Round(Clamp(pos * 0.45 + alt * 0.35 + hdg * 0.2 - Math.Max(0, h.resets - 2) * 4, 0, 100));
        }

        public static int ScoreWaypoint(double minDistM, double radiusM, double altFt, double? altMinFt = null, double? altMaxFt = null)
        {
            var acc = Clamp(100 - (minDistM / radiusM) * 45, 0, 100);
            double alt = 100;
            if (altMinFt != null && altFt < altMinFt) alt = Clamp(100 - (altMinFt.Value - altFt) * 0.35, 0, 100);
            if (altMaxFt != null && altFt > altMaxFt) alt = Clamp(100 - (altFt - altMaxFt.Value) * 0.35, 0, 100);
            return (int)Js.Round(acc * 0.4 + alt * 0.6);
        }

        public static int ScoreFuel(double remainingKg, double minFuelKg)
        {
            if (remainingKg >= minFuelKg + 5) return 100;
            if (remainingKg >= minFuelKg) return 85;
            return (int)Js.Round(Clamp((remainingKg / Math.Max(1, minFuelKg)) * 50, 0, 50));
        }

        public static readonly IReadOnlyDictionary<ScoreCategory, double> CategoryWeights = new Dictionary<ScoreCategory, double>
        {
            [ScoreCategory.Landing] = 1.5, [ScoreCategory.Hover] = 1.5, [ScoreCategory.Navigation] = 1.2, [ScoreCategory.Handling] = 1.2, [ScoreCategory.Procedures] = 0.8, [ScoreCategory.Fuel] = 0.6, [ScoreCategory.Safety] = 1.5,
            [ScoreCategory.Search] = 1.3, [ScoreCategory.Rescue] = 1.5, [ScoreCategory.Decisions] = 1.5, [ScoreCategory.Instrument] = 1.4, [ScoreCategory.Timeliness] = 0.8,
        };

        public static readonly IReadOnlyDictionary<ScoreCategory, string> CategoryLabels = new Dictionary<ScoreCategory, string>
        {
            [ScoreCategory.Landing] = "Landing quality", [ScoreCategory.Hover] = "Hover precision", [ScoreCategory.Navigation] = "Navigation accuracy", [ScoreCategory.Handling] = "Aircraft handling",
            [ScoreCategory.Procedures] = "Procedural compliance", [ScoreCategory.Fuel] = "Fuel management", [ScoreCategory.Safety] = "Safety and airmanship",
            [ScoreCategory.Search] = "Search effectiveness", [ScoreCategory.Rescue] = "Rescue and casualty care", [ScoreCategory.Decisions] = "Decision making", [ScoreCategory.Instrument] = "Instrument flying", [ScoreCategory.Timeliness] = "Timeliness",
        };

        public static int OverallScore(IDictionary<ScoreCategory, int> scores)
        {
            double sum = 0, w = 0;
            foreach (var kv in scores) { sum += kv.Value * CategoryWeights[kv.Key]; w += CategoryWeights[kv.Key]; }
            return w > 0 ? (int)Js.Round(sum / w) : 0;
        }
    }
}
