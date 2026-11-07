// A small arrow for the HUD's wind readout. Drawn pointing up; the HUD turns it to the way the wind is blowing with the aircraft's
// nose at the top of the picture, as a navigation display shows it: a headwind points down at you.
using UnityEngine;
using UnityEngine.UIElements;

namespace Rotorcraft.Presentation.UI
{
    public sealed class WindArrow : VisualElement
    {
        public WindArrow()
        {
            AddToClassList("ro-arrow");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += m =>
            {
                float w = contentRect.width, h = contentRect.height;
                var p = m.painter2D;
                p.fillColor = new Color(0.5f, 0.82f, 1f);
                p.BeginPath();
                p.MoveTo(new Vector2(w * 0.5f, 0));
                p.LineTo(new Vector2(w, h * 0.55f));
                p.LineTo(new Vector2(w * 0.64f, h * 0.55f));
                p.LineTo(new Vector2(w * 0.64f, h));
                p.LineTo(new Vector2(w * 0.36f, h));
                p.LineTo(new Vector2(w * 0.36f, h * 0.55f));
                p.LineTo(new Vector2(0, h * 0.55f));
                p.ClosePath();
                p.Fill();
            };
        }
    }
}
