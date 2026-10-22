// Deterministic, reproducible weather (port of src/sim/weather.ts). No live weather service is used or required.
using System;
using System.Collections.Generic;
using System.Globalization;
using static Rotorcraft.Sim.SimMath;

namespace Rotorcraft.Sim
{
    public enum TimeOfDay { Day, Dusk, Night }
    // In order of increasing cover / intensity: blending steps through them by index.
    public enum CloudCover { SKC, FEW, SCT, BKN, OVC }
    public enum Precip { None, Drizzle, Rain, HeavyRain }

    public sealed class WeatherPreset
    {
        public string id, name;
        public double windDirDeg;   // direction the wind blows FROM, degrees true
        public double windKt;
        public double gustKt;       // peak gust above mean
        public double turbulence;   // 0..1
        public double visibilityM;
        public double? cloudBaseFt; // null = no significant cloud
        public CloudCover cloudCover;
        public Precip precip;
        public double oatC;         // outside air temperature at sea level
        public double qnh;          // hPa
        public bool night;
        public double seed;

        public WeatherPreset Clone() => (WeatherPreset)MemberwiseClone();
    }

    public struct TurbulenceRates { public double p, q, r; }

    public static class Weather
    {
        public static readonly IReadOnlyDictionary<TimeOfDay, string> TimeLabels = new Dictionary<TimeOfDay, string> { [TimeOfDay.Day] = "Day", [TimeOfDay.Dusk] = "Dusk", [TimeOfDay.Night] = "Night" };

        static WeatherPreset W(string id, string name, double dir, double kt, double gust, double turb, double vis, double? baseFt, CloudCover cover, Precip precip, double oat, double qnh, double seed) =>
            new WeatherPreset { id = id, name = name, windDirDeg = dir, windKt = kt, gustKt = gust, turbulence = turb, visibilityM = vis, cloudBaseFt = baseFt, cloudCover = cover, precip = precip, oatC = oat, qnh = qnh, night = false, seed = seed };

        public static readonly IReadOnlyDictionary<string, WeatherPreset> Presets = new Dictionary<string, WeatherPreset>
        {
            ["calm"] = W("calm", "Calm and clear", 240, 3, 0, 0.05, 30000, null, CloudCover.FEW, Precip.None, 14, 1021, 11),
            ["light_sw"] = W("light_sw", "Light south-westerly", 230, 8, 4, 0.15, 25000, 3500, CloudCover.SCT, Precip.None, 12, 1015, 23),
            ["breezy_w"] = W("breezy_w", "Fresh westerly, gusting", 270, 14, 9, 0.35, 18000, 2500, CloudCover.BKN, Precip.None, 10, 1008, 37),
            ["crosswind_n"] = W("crosswind_n", "Northerly crosswind", 350, 12, 6, 0.25, 15000, 2000, CloudCover.BKN, Precip.None, 8, 1011, 41),
            ["haar"] = W("haar", "Coastal haar (sea fog)", 90, 6, 2, 0.1, 1800, 400, CloudCover.OVC, Precip.None, 9, 1019, 53),
            ["glen_closing"] = W("glen_closing", "Cloud lowering on the hills, drizzle", 250, 18, 10, 0.4, 3500, 1100, CloudCover.OVC, Precip.Drizzle, 8, 1004, 71),
            ["fair_nw"] = W("fair_nw", "Fair, north-westerly breeze", 310, 11, 5, 0.2, 22000, 3000, CloudCover.SCT, Precip.None, 11, 1018, 83),
            ["glen_lowering"] = W("glen_lowering", "Cloud lowering in the glen, drizzle", 245, 16, 9, 0.38, 6000, 2100, CloudCover.OVC, Precip.Drizzle, 8, 1006, 79),
            ["north_sea"] = W("north_sea", "North Sea, fresh south-westerly", 225, 20, 8, 0.3, 20000, 2200, CloudCover.BKN, Precip.None, 9, 1009, 91),
            ["showers"] = W("showers", "Showers, moderate wind", 300, 16, 12, 0.45, 7000, 1400, CloudCover.BKN, Precip.Rain, 7, 1003, 67),
        };

        /// <summary>Mean wind vector (m/s) the air is moving TOWARD, in world axes.</summary>
        public static Vec3 MeanWind(WeatherPreset w)
        {
            var toward = (w.windDirDeg + 180) * DEG; // wind blows from dir, toward dir+180
            var ms = w.windKt / 1.943844;
            return V3(Math.Sin(toward) * ms, 0, -Math.Cos(toward) * ms);
        }

        /// <summary>
        /// Wind at a position and time including deterministic gusts. Gusts are smooth sums of sines,
        /// so a mission replayed with the same inputs experiences identical wind.
        /// Wind speed reduces close to the ground (simple boundary layer).
        /// </summary>
        public static Vec3 WindAt(WeatherPreset w, double t, double x, double y, double z, double agl)
        {
            var b = MeanWind(w);
            var spatial = (x + z) * 0.002;
            var g = Math.Max(0, SmoothNoise(t * 0.45 + spatial, w.seed) * 0.7 + 0.3); // 0..1 mostly
            var gustMs = (w.gustKt / 1.943844) * g;
            var mean = Js.Hypot(b.x, b.z);
            var dirJitter = SmoothNoise(t * 0.21 + 3.3, w.seed + 5) * 0.18 * Clamp(w.turbulence * 2, 0, 1);
            var total = mean + gustMs;
            var ang = Math.Atan2(b.x, -b.z) + dirJitter;
            var layer = Clamp(0.55 + 0.45 * Math.Min(1, agl / 60), 0.55, 1);
            var vert = SmoothNoise(t * 0.9 + 7.1 + spatial, w.seed + 9) * w.turbulence * 1.2;
            return V3(Math.Sin(ang) * total * layer, vert, -Math.Cos(ang) * total * layer);
        }

        /// <summary>Turbulence-induced angular disturbance rates (rad/s), deterministic.</summary>
        public static TurbulenceRates TurbulenceRatesAt(WeatherPreset w, double t)
        {
            var k = w.turbulence;
            return new TurbulenceRates
            {
                p = SmoothNoise(t * 1.7, w.seed + 21) * 0.09 * k,
                q = SmoothNoise(t * 1.5, w.seed + 22) * 0.06 * k,
                r = SmoothNoise(t * 1.3, w.seed + 23) * 0.07 * k,
            };
        }

        /// <summary>ISA-based density ratio sigma (rho/rho0) at geometric altitude (m) with temperature deviation.</summary>
        public static double DensityRatio(double altM, double oatSeaLevelC)
        {
            var isaT = 288.15 - 0.0065 * altM;
            var actualT = isaT + (oatSeaLevelC - 15);
            var pRatio = Math.Pow(1 - 2.25577e-5 * altM, 5.25588);
            return pRatio * (288.15 / actualT);
        }

        /// <summary>Visibility in metres, for rendering fog and for detection.</summary>
        public static double Visibility(WeatherPreset w) => w.visibilityM;

        static string Int(double v) => ((long)v).ToString(CultureInfo.InvariantCulture);

        /// <summary>Readable METAR-style string for briefings.</summary>
        public static string Metar(WeatherPreset w, string station = "EGKN")
        {
            var dir = Int(Js.Round(w.windDirDeg / 10) * 10).PadLeft(3, '0');
            var spd = Int(Js.Round(w.windKt)).PadLeft(2, '0');
            var gust = w.gustKt >= 8 ? "G" + Int(Js.Round(w.windKt + w.gustKt)).PadLeft(2, '0') : "";
            var vis = w.visibilityM >= 10000 ? "9999" : Int(Js.Round(w.visibilityM / 100) * 100).PadLeft(4, '0');
            var wx = w.precip == Precip.None ? (w.visibilityM < 5000 ? " BR" : "") : w.precip == Precip.Drizzle ? " -DZ" : w.precip == Precip.Rain ? " RA" : " +RA";
            var cloud = w.cloudBaseFt == null ? " NSC" : $" {w.cloudCover}{Int(Js.Round(w.cloudBaseFt.Value / 100)).PadLeft(3, '0')}";
            var dew = Js.Round(w.oatC - (w.visibilityM < 3000 ? 1 : 4));
            string T(double n) => (n < 0 ? "M" : "") + Int(Math.Abs(n)).PadLeft(2, '0');
            return $"{station} {dir}{spd}{gust}KT {vis}{wx}{cloud} {T(Js.Round(w.oatC))}/{T(dew)} Q{Int(w.qnh)}";
        }

        // ---------------------------------------------------------------------------------------------
        // Changing weather and cloud
        // ---------------------------------------------------------------------------------------------

        /// <summary>Smoothly blend weather a → b (t in 0..1). Numbers interpolate; categories step at the midpoint.</summary>
        public static WeatherPreset BlendWeather(WeatherPreset a, WeatherPreset b, double t)
        {
            var k = Math.Max(0, Math.Min(1, t));
            if (k <= 0) return a;
            if (k >= 1) return b;
            // wind direction takes the short way round
            var dd = ((b.windDirDeg - a.windDirDeg + 540) % 360) - 180;
            double baseA = a.cloudBaseFt ?? 6000, baseB = b.cloudBaseFt ?? 6000;
            return new WeatherPreset
            {
                id = a.id + ">" + b.id, name = k < 0.5 ? a.name : b.name,
                windDirDeg = (a.windDirDeg + dd * k + 360) % 360, windKt = Lerp(a.windKt, b.windKt, k), gustKt = Lerp(a.gustKt, b.gustKt, k), turbulence = Lerp(a.turbulence, b.turbulence, k),
                visibilityM = Math.Exp(Lerp(Math.Log(a.visibilityM), Math.Log(b.visibilityM), k)),
                cloudBaseFt = a.cloudBaseFt == null && b.cloudBaseFt == null ? (double?)null : Lerp(baseA, baseB, k),
                cloudCover = (CloudCover)(int)Js.Round(Lerp((int)a.cloudCover, (int)b.cloudCover, k)),
                precip = (Precip)(int)Js.Round(Lerp((int)a.precip, (int)b.precip, k)),
                oatC = Lerp(a.oatC, b.oatC, k), qnh = Js.Round(Lerp(a.qnh, b.qnh, k)), night = b.night, seed = a.seed,
            };
        }

        public static double CoverFraction(CloudCover c)
        {
            switch (c)
            {
                case CloudCover.SKC: return 0;
                case CloudCover.FEW: return 0.18;
                case CloudCover.SCT: return 0.38;
                case CloudCover.BKN: return 0.68;
                default: return 0.97;
            }
        }
        /// <summary>Cloud layer thickness (m) above the base.</summary>
        public const double CLOUD_DEPTH_M = 300;

        /// <summary>
        /// Cloud density 0..1 at a world position: the same noise field the renderer draws, so "in cloud"
        /// here matches what the pilot sees. sample(u, v, channel) reads the shared noise texture.
        /// </summary>
        public static double CloudDensityAt(WeatherPreset w, double x, double y, double z, double t, Func<double, double, int, double> sample, double windDirX, double windDirZ)
        {
            if (w.cloudBaseFt == null || w.cloudCover == CloudCover.SKC) return 0;
            var baseM = w.cloudBaseFt.Value / 3.28084;
            var vertical = Math.Max(0, Math.Min(1, (y - (baseM - 25)) / 50)) * (1 - Math.Max(0, Math.Min(1, (y - (baseM + CLOUD_DEPTH_M - 20)) / 60)));
            if (vertical <= 0) return 0;
            var cov = CoverFraction(w.cloudCover);
            double u = x / 3100 + windDirX * t * 0.00012, v = z / 3100 + windDirZ * t * 0.00012;
            var c = sample(u, v, 0) * 0.55 + sample(u * 2.7 + 0.31, v * 2.7 + 0.31, 1) * 0.3 + sample(u * 8.3 + 0.77, v * 8.3 + 0.77, 2) * 0.15;
            double lo = 1 - cov - 0.08, hi = 1 - cov + 0.22;
            var s = Math.Max(0, Math.Min(1, (c - lo) / (hi - lo)));
            return s * s * (3 - 2 * s) * vertical;
        }

        public static (double x, double z) WindDirUnit(WeatherPreset w)
        {
            var m = MeanWind(w);
            var l = Js.Hypot(m.x, m.z);
            return l > 0.1 ? (m.x / l, m.z / l) : (0.7, 0.7);
        }
    }
}
