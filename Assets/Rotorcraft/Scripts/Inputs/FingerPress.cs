// A fingertip pressing a flat panel of buttons in VR. One touch is one press: the finger has to come from in front of the
// panel, reach it over a button, and back away again before it can press anything else, so a hand wandering round or
// through the panel does not set things off.
namespace Rotorcraft.Inputs
{
    public sealed class FingerPress
    {
        /// <summary>How near the surface (m) counts as touching, how far off it re-arms, how far behind it stops counting, and how near a button shows the finger is over it.</summary>
        public const float Touch = 0.006f, Rearm = 0.018f, Behind = 0.06f, Near = 0.07f;

        readonly int columns, rows;
        readonly float width, height;
        bool armed;

        /// <summary>The button the finger is close to, or -1.</summary>
        public int Hover { get; private set; } = -1;

        public FingerPress(int columns, int rows, float width, float height)
        {
            this.columns = columns;
            this.rows = rows;
            this.width = width;
            this.height = height;
        }

        /// <summary>The finger is not available (the hand is holding something or out of sight): it must come back from in front.</summary>
        public void Release()
        {
            armed = false;
            Hover = -1;
        }

        /// <summary>
        /// Where the fingertip is: x right and y up from the panel's centre, depth out from its face towards the pilot, in
        /// metres. Buttons count from the top left, along each row. Returns the button pressed just now, or -1.
        /// </summary>
        public int Update(float x, float y, float depth)
        {
            var over = System.Math.Abs(x) <= width / 2 && System.Math.Abs(y) <= height / 2;
            if (depth >= Rearm) armed = true;
            // beyond the edge and level with the panel or behind it: coming back over a button from there is not a press
            else if (!over && depth <= Touch) armed = false;
            Hover = over && depth < Near && depth > -Behind ? Button(x, y) : -1;
            if (!over || !armed || depth > Touch || depth <= -Behind) return -1;
            armed = false;
            return Hover;
        }

        int Button(float x, float y)
        {
            var c = (int)((x + width / 2) / width * columns);
            var r = (int)((height / 2 - y) / height * rows);
            if (c >= columns) c = columns - 1;
            if (r >= rows) r = rows - 1;
            return r * columns + c;
        }
    }
}
