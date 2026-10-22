// Fixed-timestep driver (port of src/sim/simLoop.ts). Rendering calls Advance() with real elapsed time; the
// simulation always steps in fixed increments so behaviour is identical at 30, 60 or 120 fps. Long frames (tab
// switches, slow devices) are capped so the aircraft never "teleports"; the excess is discarded and reported,
// which reads to the player as a brief slow-down rather than a physics explosion.
using System;

namespace Rotorcraft.Sim
{
    public sealed class FixedStepper
    {
        public const double SIM_DT = 1.0 / 120;
        public const double MAX_FRAME = 0.1; // seconds of real time processed per frame at most

        double acc;
        public double droppedTime;
        public int steps;
        readonly Action<double> stepFn;
        readonly double dt;

        public FixedStepper(Action<double> stepFn, double dt = SIM_DT) { this.stepFn = stepFn; this.dt = dt; }

        /// <summary>Advance by real elapsed seconds. Returns interpolation alpha in [0,1).</summary>
        public double Advance(double elapsed)
        {
            if (double.IsNaN(elapsed) || double.IsInfinity(elapsed) || elapsed < 0) elapsed = 0;
            if (elapsed > MAX_FRAME)
            {
                droppedTime += elapsed - MAX_FRAME;
                elapsed = MAX_FRAME;
            }
            acc += elapsed;
            while (acc >= dt)
            {
                stepFn(dt);
                acc -= dt;
                steps++;
            }
            return acc / dt;
        }

        public void Reset() { acc = 0; }
    }
}
