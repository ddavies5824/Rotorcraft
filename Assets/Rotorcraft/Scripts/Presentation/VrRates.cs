// Which refresh rate to ask a headset for. A frame that misses the headset's deadline makes the runtime halve the frame
// rate, so a Quest on Link at its default 120 Hz shows 60 frames a second if a frame takes anything over 8.3 ms. A rate
// the game can hold is better than a higher one it cannot.
using System.Collections.Generic;
using System.Linq;

namespace Rotorcraft.Presentation
{
    public static class VrRates
    {
        /// <summary>The choices in Settings; 0 is automatic.</summary>
        public static readonly int[] Choices = { 0, 72, 80, 90, 120 };
        const float HighestToTry = 90, LowestToTry = 72;

        /// <summary>
        /// The rates to try, in order, moving on when frames are too often late: what the player chose, or else from the
        /// fastest worth trying downwards. The last resort is the headset's highest, because half of that (60 at 120 Hz) beats
        /// half of the lowest (36 at 72 Hz) once the game cannot make even the lowest.
        /// </summary>
        public static float[] Ladder(IReadOnlyList<float> rates, int setting)
        {
            if (rates == null || rates.Count < 2) return new float[0];   // nothing to choose between
            if (setting != 0) return rates.Any(r => System.Math.Abs(r - setting) < 0.5f) ? new float[] { setting } : new float[0];
            var ladder = rates.Where(r => r >= LowestToTry && r <= HighestToTry).OrderByDescending(r => r).ToList();
            var top = rates.Max();
            if (!ladder.Contains(top)) ladder.Add(top);
            return ladder.ToArray();
        }
    }

    /// <summary>Says when too many frames in a few seconds have come late for the headset's rate.</summary>
    public sealed class VrFrameWatch
    {
        const float Window = 5, LateShare = 0.1f, LateBy = 1.4f;
        float time;
        int frames, late;

        public void Reset() { time = 0; frames = late = 0; }

        /// <summary>Count a frame; true if the window just ended with too many late.</summary>
        public bool Tick(float seconds, float hz)
        {
            time += seconds;
            frames++;
            if (seconds > LateBy / hz) late++;
            if (time < Window) return false;
            var tooMany = late > frames * LateShare;
            Reset();
            return tooMany;
        }
    }
}
