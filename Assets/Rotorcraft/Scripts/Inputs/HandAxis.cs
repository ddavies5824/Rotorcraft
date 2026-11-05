// One axis of a control held in the hand (VR): the control moves as far as the hand does, from wherever it was when
// taken hold of, so nothing jumps. Pushed past its stop it waits there, and comes straight back when the hand reverses
// rather than only after the hand has returned all the way.
using System;

namespace Rotorcraft.Inputs
{
    public struct HandAxis
    {
        /// <summary>Metres of hand movement from centre to full cyclic (the stick's own top moves about that far).</summary>
        public const double CyclicTravel = 0.12;
        /// <summary>Metres of hand movement from bottom to top of the collective lever.</summary>
        public const double LeverTravel = 0.25;

        readonly double min, max, travel;
        double origin;

        public HandAxis(double value, double min, double max, double travel, double hand)
        {
            this.min = min; this.max = max; this.travel = travel;
            origin = hand - value * travel;
        }

        public double Update(double hand)
        {
            var v = (hand - origin) / travel;
            if (v > max) { origin = hand - max * travel; return max; }
            if (v < min) { origin = hand - min * travel; return min; }
            return v;
        }
    }
}
