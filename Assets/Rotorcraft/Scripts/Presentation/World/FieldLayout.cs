// The farmland's field layout (port of the layout parts of render/fields.ts): a grid of 250 x 175 m fields
// rotated about 20 degrees, each row shifted by a hashed amount, each field with its own crop, and hedges
// round most of them. fhash is the browser game's integer hash, exact, so both games lay the fields out alike.
using System;

namespace Rotorcraft.Presentation
{
    public enum Crop { Pasture, Stubble, Ploughed, Silage, Rough }

    public static class FieldLayout
    {
        public const double CELL_X = 250, CELL_Z = 175;
        const double R00 = 0.94, R01 = 0.34, R10 = -0.34, R11 = 0.94;

        /// <summary>The shader's fhash: an integer hash of a (floored) 2-D position and a salt, 0..1.</summary>
        public static double FHash(double px, double py, int salt)
        {
            unchecked
            {
                uint qx = (uint)((long)Math.Floor(px) + 32768), qy = (uint)((long)Math.Floor(py) + 32768);
                uint h = qx * 1597334677u ^ qy * 3812015801u ^ (uint)salt * 668265263u;
                h = (h ^ (h >> 16)) * 2246822519u;
                h ^= h >> 13;
                return (h >> 8) / 16777216.0;
            }
        }

        public static (double qx, double qy) ToQ(double x, double z) => (R00 * x + R01 * z, R10 * x + R11 * z);
        /// <summary>The shift of a row of fields along q.x (m).</summary>
        public static double RowShift(int row) => FHash(row, 0, 37) * CELL_X;
        /// <summary>Is there a hedge round this field?</summary>
        public static bool HedgeOn(int i, int row) => FHash(i, row, 5) >= 0.22;

        public static Crop CropOf(int i, int row)
        {
            var u = FHash(i, row, 0);
            return u < 0.38 ? Crop.Pasture : u < 0.56 ? Crop.Stubble : u < 0.72 ? Crop.Ploughed : u < 0.84 ? Crop.Silage : Crop.Rough;
        }

        /// <summary>The field at (x, z): its indices, and how far the point is from the nearest hedged edge (m).</summary>
        public static (int i, int row, double edge) FieldAt(double x, double z)
        {
            var (qx, qy) = ToQ(x, z);
            var row = (int)Math.Floor(qy / CELL_Z);
            var off = RowShift(row);
            var i = (int)Math.Floor((qx + off) / CELL_X);
            double u = qx + off - i * CELL_X, v = qy - row * CELL_Z;
            var edge = double.PositiveInfinity;
            if (HedgeOn(i, row) || HedgeOn(i - 1, row)) edge = Math.Min(edge, u);
            if (HedgeOn(i, row) || HedgeOn(i + 1, row)) edge = Math.Min(edge, CELL_X - u);
            if (HedgeOn(i, row)) edge = Math.Min(edge, Math.Min(v, CELL_Z - v));
            return (i, row, edge);
        }
    }
}
