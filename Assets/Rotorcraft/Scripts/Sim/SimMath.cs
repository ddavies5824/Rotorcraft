// Small vector helpers and deterministic noise used by the simulation (port of src/sim/math.ts).
// World axes: x = east, y = up, z = south (right-handed, OpenGL convention). North is -z.
// Everything is double precision, as in the TypeScript game, so the numbers match it.
using System;

namespace Rotorcraft.Sim
{
    /// <summary>A mutable vector, like the plain {x, y, z} objects of the TypeScript sim.</summary>
    public sealed class Vec3
    {
        public double x, y, z;
        public Vec3(double x = 0, double y = 0, double z = 0) { this.x = x; this.y = y; this.z = z; }
        public Vec3 Clone() => new Vec3(x, y, z);
        public override string ToString() => FormattableString.Invariant($"({x}, {y}, {z})");
    }

    public struct BodyAxes { public Vec3 f, r, u; }

    public static class SimMath
    {
        public static Vec3 V3(double x = 0, double y = 0, double z = 0) => new Vec3(x, y, z);
        public static Vec3 Add(Vec3 a, Vec3 b) => new Vec3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vec3 Sub(Vec3 a, Vec3 b) => new Vec3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vec3 Scale(Vec3 a, double s) => new Vec3(a.x * s, a.y * s, a.z * s);
        public static double Dot(Vec3 a, Vec3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static double Len(Vec3 a) => Math.Sqrt(a.x * a.x + a.y * a.y + a.z * a.z);
        public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static Vec3 Norm(Vec3 a) { var l = Len(a); if (l == 0) l = 1; return Scale(a, 1 / l); }

        public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
        public static double Lerp(double a, double b, double t) => a + (b - a) * t;
        public static double Smoothstep(double e0, double e1, double x)
        {
            var t = Clamp((x - e0) / (e1 - e0), 0, 1);
            return t * t * (3 - 2 * t);
        }
        public const double DEG = Math.PI / 180;
        public const double RAD = 180 / Math.PI;
        public const double KT = 1.943844; // m/s -> knots
        public const double FT = 3.28084;  // m -> ft
        public const double G = 9.80665;

        /// <summary>Wrap angle to [-PI, PI).</summary>
        public static double WrapPi(double a)
        {
            a = (a + Math.PI) % (2 * Math.PI);
            if (a < 0) a += 2 * Math.PI;
            return a - Math.PI;
        }
        /// <summary>Heading in degrees 0..360 from radians.</summary>
        public static double HdgDeg(double rad)
        {
            var d = (rad * RAD) % 360;
            if (d < 0) d += 360;
            return d;
        }

        /// <summary>Body axes from heading (psi, clockwise from north), pitch (theta, nose up +), roll (phi, right wing down +).</summary>
        public static BodyAxes BodyAxesOf(double psi, double theta, double phi)
        {
            double sp = Math.Sin(psi), cp = Math.Cos(psi);
            var f0 = V3(sp, 0, -cp);
            var r0 = V3(cp, 0, sp);
            double st = Math.Sin(theta), ct = Math.Cos(theta);
            var f = V3(f0.x * ct, st, f0.z * ct);
            var u1 = V3(-f0.x * st, ct, -f0.z * st);
            double sr = Math.Sin(phi), cr = Math.Cos(phi);
            var r = V3(r0.x * cr - u1.x * sr, r0.y * cr - u1.y * sr, r0.z * cr - u1.z * sr);
            var u = V3(u1.x * cr + r0.x * sr, u1.y * cr + r0.y * sr, u1.z * cr + r0.z * sr);
            return new BodyAxes { f = f, r = r, u = u };
        }

        /// <summary>Deterministic pseudo-random generator (mulberry32), bit for bit the same as the game's.</summary>
        public static Func<double> Rng(double seed)
        {
            uint a = Js.ToUint32(seed);
            return () =>
            {
                unchecked
                {
                    a += 0x6d2b79f5;
                    uint t = a;
                    t = (t ^ (t >> 15)) * (t | 1);
                    t ^= t + (t ^ (t >> 7)) * (t | 61);
                    return (t ^ (t >> 14)) / 4294967296.0;
                }
            };
        }

        /// <summary>Smooth deterministic 1-D noise in roughly [-1, 1] built from incommensurate sines.</summary>
        public static double SmoothNoise(double t, double seed)
        {
            var s = seed * 12.9898;
            return
                0.5 * Math.Sin(t * 0.73 + s) +
                0.3 * Math.Sin(t * 1.61 + s * 1.7) +
                0.2 * Math.Sin(t * 3.37 + s * 2.3);
        }

        /// <summary>Deterministic 2-D value noise with smooth interpolation, output in [0,1].</summary>
        static double Hash2(double ix, double iz, double seed)
        {
            unchecked
            {
                uint h = Js.ToUint32(ix) * 374761393u ^ Js.ToUint32(iz) * 668265263u ^ Js.ToUint32(seed) * 2147483647u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return (h ^ (h >> 16)) / 4294967296.0;
            }
        }
        public static double ValueNoise2(double x, double z, double seed)
        {
            double ix = Math.Floor(x), iz = Math.Floor(z);
            double fx = x - ix, fz = z - iz;
            double ux = fx * fx * (3 - 2 * fx), uz = fz * fz * (3 - 2 * fz);
            double a = Hash2(ix, iz, seed), b = Hash2(ix + 1, iz, seed);
            double c = Hash2(ix, iz + 1, seed), d = Hash2(ix + 1, iz + 1, seed);
            return Lerp(Lerp(a, b, ux), Lerp(c, d, ux), uz);
        }
        public static double Fbm2(double x, double z, double seed, int octaves = 4)
        {
            double amp = 0.5, freq = 1, sum = 0, norm = 0;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * ValueNoise2(x * freq, z * freq, seed + i * 17);
                norm += amp;
                amp *= 0.5;
                freq *= 2.03;
            }
            return sum / norm;
        }
    }

    /// <summary>The JavaScript number rules the TypeScript game relies on, where C# differs.</summary>
    public static class Js
    {
        const double TwoPow32 = 4294967296.0;

        /// <summary>JavaScript ToUint32 (what `x >>> 0` does).</summary>
        public static uint ToUint32(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
            var m = Math.Truncate(v) % TwoPow32;
            if (m < 0) m += TwoPow32;
            return (uint)m;
        }
        /// <summary>JavaScript ToInt32 (what `x | 0` does).</summary>
        public static int ToInt32(double v) => unchecked((int)ToUint32(v));

        /// <summary>Math.round: halves round up (towards +infinity), unlike C#'s banker's rounding.</summary>
        public static double Round(double v) => Math.Floor(v + 0.5);

        public static double Hypot(double a, double b) => Math.Sqrt(a * a + b * b);
        public static double Hypot(double a, double b, double c) => Math.Sqrt(a * a + b * b + c * c);

        /// <summary>Number.prototype.toFixed, with the invariant culture (a dot, never a comma).</summary>
        public static string Fixed(double v, int digits) => v.ToString("F" + digits, System.Globalization.CultureInfo.InvariantCulture);
    }
}
