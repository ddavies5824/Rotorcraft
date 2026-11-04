// A pointer that is not the mouse (the VR laser) working a UI panel: it sends the panel the same events the mouse
// does, so buttons, check boxes, sliders and scroll views all respond as usual. Positions are in the panel's own
// layout units, from its top left.
using UnityEngine;
using UnityEngine.UIElements;

namespace Rotorcraft.Presentation.UI
{
    public static class PanelPointer
    {
        static Event Make(EventType type, Vector2 at, Vector2 delta = default) => new Event { type = type, mousePosition = at, delta = delta, button = 0, clickCount = 1 };

        public static void Move(IPanel panel, Vector2 at)
        {
            using (var e = PointerMoveEvent.GetPooled(Make(EventType.MouseMove, at))) panel.visualTree.SendEvent(e);
        }

        public static void Down(IPanel panel, Vector2 at)
        {
            using (var e = PointerDownEvent.GetPooled(Make(EventType.MouseDown, at))) panel.visualTree.SendEvent(e);
        }

        public static void Up(IPanel panel, Vector2 at)
        {
            using (var e = PointerUpEvent.GetPooled(Make(EventType.MouseUp, at))) panel.visualTree.SendEvent(e);
        }

        /// <summary>Scroll by this many lines: positive scrolls down.</summary>
        public static void Scroll(IPanel panel, Vector2 at, float lines)
        {
            using (var e = WheelEvent.GetPooled(Make(EventType.ScrollWheel, at, new Vector2(0, lines)))) panel.visualTree.SendEvent(e);
        }
    }
}
