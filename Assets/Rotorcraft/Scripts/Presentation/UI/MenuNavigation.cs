// Menus with a gamepad (the browser game had none; this is the port's own). The D-pad or left stick moves a highlight
// to the nearest button, tick box or slider in that direction on screen; A presses it; B presses the screen's Back
// button (or resumes from the pause card); LB and RB step through the tabs; the right stick scrolls. Left and right
// on a slider move the slider. A flight stick's hat and trigger work the same way. The highlight only shows once the
// pad is used and goes when the mouse moves, so it never gets in the way of the mouse or a touch screen.
//
// Unity's own UI navigation would also act on a gamepad's D-pad and A button (in tab order, not by position), so
// navigation events that come from a pad are stopped at the root of each navigated tree; the keyboard's are left alone.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Rotorcraft.Presentation.UI
{
    public sealed class MenuNavigation
    {
        public const string FocusClass = "pad-focus";
        const float RepeatDelay = 0.38f, RepeatEvery = 0.11f, StickDeadzone = 0.55f;

        VisualElement current;
        Vector2 lastCentre = new Vector2(-1e4f, -1e4f);
        bool showing;
        Vector2Int heldDir;
        float nextRepeat;
        readonly HashSet<VisualElement> guarded = new HashSet<VisualElement>();
        readonly List<VisualElement> found = new List<VisualElement>();
        bool prevA, prevB, prevLB, prevRB, prevTrigger;

        /// <summary>What the last tick did, for tests.</summary>
        public string Why { get; private set; }

        /// <summary>The element the highlight is on (for tests).</summary>
        public VisualElement Current => showing ? current : null;

        /// <summary>
        /// Read the pad and act within scopes (the trees that have the controller now; none: nothing to do). back is what
        /// B does when there is no Back button in view (return true if it did something).
        /// </summary>
        public void Tick(IList<VisualElement> scopes, Func<bool> back = null)
        {
            var pads = Gamepad.all;
            var stick = Joystick.current;
            if (scopes == null || scopes.Count == 0 || (pads.Count == 0 && stick == null)) { Why = "no scope or no pad"; Hide(); return; }
            foreach (var s in scopes) Guard(s);
            if (Mouse.current != null && Mouse.current.delta.ReadValue().sqrMagnitude > 4) Hide();

            // what the pad asks for this frame
            var dir = Vector2.zero; bool a = false, b = false, lb = false, rb = false; float scroll = 0;
            // every pad counts (the one Unity calls current is only the last to have sent anything)
            for (int i = 0; i < pads.Count; i++)
            {
                var pad = pads[i];
                var d = pad.dpad.ReadValue();
                var ls = pad.leftStick.ReadValue();
                if (d.sqrMagnitude < 0.25f && ls.magnitude > StickDeadzone) d = ls;
                if (dir.sqrMagnitude < 0.25f) dir = d;
                a |= pad.buttonSouth.isPressed; b |= pad.buttonEast.isPressed;
                lb |= pad.leftShoulder.isPressed; rb |= pad.rightShoulder.isPressed;
                var rs = pad.rightStick.ReadValue().y;
                if (Mathf.Abs(rs) > 0.2f) scroll = rs;
            }
            if (stick != null && dir.sqrMagnitude < 0.25f)
            {
                var hat = stick.hatswitch?.ReadValue() ?? Vector2.zero;
                if (hat.sqrMagnitude > 0.25f) dir = hat;
            }
            var trigger = stick != null && stick.trigger != null && stick.trigger.isPressed;
            bool pressA = (a && !prevA) || (trigger && !prevTrigger), pressB = b && !prevB, pressLB = lb && !prevLB, pressRB = rb && !prevRB;
            prevA = a; prevB = b; prevLB = lb; prevRB = rb; prevTrigger = trigger;

            var step = Vector2Int.zero;
            if (dir.sqrMagnitude > 0.25f) step = Mathf.Abs(dir.x) > Mathf.Abs(dir.y) ? new Vector2Int(Math.Sign(dir.x), 0) : new Vector2Int(0, Math.Sign(dir.y));
            var moveNow = false;
            if (step != heldDir) { heldDir = step; if (step != Vector2Int.zero) { moveNow = true; nextRepeat = Time.unscaledTime + RepeatDelay; } }
            else if (step != Vector2Int.zero && Time.unscaledTime >= nextRepeat) { moveNow = true; nextRepeat = Time.unscaledTime + RepeatEvery; }

            if (!moveNow && !pressA && !pressB && !pressLB && !pressRB && scroll == 0) { Why = $"idle (dir {dir})"; KeepCurrent(scopes); return; }

            Collect(scopes);
            if (found.Count == 0) { Why = "nothing to navigate"; Hide(); return; }
            if (current == null || !found.Contains(current)) current = Nearest(lastCentre) ?? Default();

            if (scroll != 0) Scroll(scroll);
            if (pressLB || pressRB) { Tab(pressRB ? 1 : -1); return; }
            if (pressB) { if (!PressBack() && back != null) back(); return; }
            if (!showing)
            {
                // the first touch of the pad only shows where the highlight is
                if (moveNow || pressA) Show(current);
                return;
            }
            if (moveNow)
            {
                if (current is Slider sl && step.x != 0) { Nudge(sl, step.x); return; }
                var next = Toward(current, new Vector2(step.x, -step.y));
                if (next != null) Show(next);
            }
            if (pressA && current != null) Press(current);
        }

        /// <summary>The trees navigated have been rebuilt: find the highlight's element again, nearest where it was.</summary>
        void KeepCurrent(IList<VisualElement> scopes)
        {
            if (!showing || current == null) return;
            if (current.panel != null && current.resolvedStyle.display != DisplayStyle.None) return;
            Collect(scopes);
            var again = Nearest(lastCentre);
            if (again != null) Show(again); else Hide();
        }

        void Hide()
        {
            if (current != null) current.RemoveFromClassList(FocusClass);
            showing = false;
        }

        void Show(VisualElement e)
        {
            if (current != null && current != e) current.RemoveFromClassList(FocusClass);
            current = e; showing = true;
            e.AddToClassList(FocusClass);
            if (e.focusable) e.Focus();
            lastCentre = e.worldBound.center;
            // bring it into view inside its scroll view
            for (var p = e.parent; p != null; p = p.parent) if (p is ScrollView sv) { sv.ScrollTo(e); break; }
        }

        /// <summary>Stop navigation events from a pad at the root of a tree (Unity would otherwise move and press as well).</summary>
        void Guard(VisualElement root)
        {
            if (!guarded.Add(root)) return;
            root.RegisterCallback<NavigationMoveEvent>(e => { if (!own.Remove(e) && !FromKeyboard()) { e.StopImmediatePropagation(); root.focusController?.IgnoreEvent(e); } }, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationSubmitEvent>(e => { if (!own.Remove(e) && !FromKeyboard()) { e.StopImmediatePropagation(); root.focusController?.IgnoreEvent(e); } }, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationCancelEvent>(e => { if (!own.Remove(e) && !FromKeyboard()) { e.StopImmediatePropagation(); root.focusController?.IgnoreEvent(e); } }, TrickleDown.TrickleDown);
        }

        /// <summary>The events this has sent itself (which the guard lets through, whenever they are dispatched).</summary>
        static readonly HashSet<EventBase> own = new HashSet<EventBase>();

        /// <summary>A navigation event from the keyboard (left to Unity, as before) rather than a pad: one of its keys is down.</summary>
        static bool FromKeyboard()
        {
            var kb = Keyboard.current;
            return kb != null && (kb.upArrowKey.isPressed || kb.downArrowKey.isPressed || kb.leftArrowKey.isPressed || kb.rightArrowKey.isPressed
                || kb.enterKey.isPressed || kb.numpadEnterKey.isPressed || kb.spaceKey.isPressed || kb.escapeKey.isPressed || kb.tabKey.isPressed);
        }

        /// <summary>Press an element as a click would: buttons, tick rows (which take NavigationSubmitEvent) and the rest.</summary>
        void Press(VisualElement e)
        {
            using (var ev = NavigationSubmitEvent.GetPooled()) { ev.target = e; if (own.Count > 64) own.Clear(); own.Add(ev); e.SendEvent(ev); }
        }

        static void Nudge(Slider s, int dir)
        {
            var span = s.highValue - s.lowValue;
            s.value = Mathf.Clamp(s.value + dir * span * 0.05f, Mathf.Min(s.lowValue, s.highValue), Mathf.Max(s.lowValue, s.highValue));
            // the game's sliders save their value on a navigation move (Ui.Slider); a move with no direction changes nothing else
            using (var ev = NavigationMoveEvent.GetPooled(Vector2.zero)) { ev.target = s; if (own.Count > 64) own.Clear(); own.Add(ev); s.SendEvent(ev); }
        }

        void Scroll(float amount)
        {
            ScrollView sv = null;
            if (current != null) for (var p = current.parent; p != null && sv == null; p = p.parent) sv = p as ScrollView;
            if (sv == null) return;
            sv.scrollOffset += new Vector2(0, -amount * 18);
        }

        /// <summary>LB and RB: the previous or next tab (sub-tabs before the side rail).</summary>
        void Tab(int dir)
        {
            foreach (var cls in new[] { "subtab", "rail-btn" })
            {
                var tabs = found.FindAll(x => x.ClassListContains(cls));
                if (tabs.Count < 2) continue;
                var at = tabs.FindIndex(x => x.ClassListContains("current"));
                var next = tabs[((at < 0 ? 0 : at) + dir + tabs.Count) % tabs.Count];
                Press(next);
                return;
            }
        }

        /// <summary>B: the Back button in view, if there is one.</summary>
        bool PressBack()
        {
            foreach (var e in found)
                if (e is Button bt && (bt.text ?? "").StartsWith("BACK", StringComparison.OrdinalIgnoreCase)) { Press(bt); return true; }
            foreach (var e in found)
                if (e is Button bt)
                    foreach (var l in bt.Query<Label>().ToList())
                        if ((l.text ?? "").StartsWith("BACK", StringComparison.OrdinalIgnoreCase)) { Press(bt); return true; }
            return false;
        }

        // ------------------------------------------------------------------------------------------- finding
        void Collect(IList<VisualElement> scopes)
        {
            found.Clear();
            foreach (var s in scopes) if (s != null && Visible(s)) Walk(s);
        }

        void Walk(VisualElement e)
        {
            foreach (var c in e.Children())
            {
                if (!Visible(c)) continue;
                if (Navigable(c)) { found.Add(c); if (!(c is ScrollView)) continue; }
                Walk(c);
            }
        }

        static bool Visible(VisualElement e)
        {
            var s = e.resolvedStyle;
            if (s.display == DisplayStyle.None || s.visibility == Visibility.Hidden || s.opacity < 0.05f) return false;
            return true;
        }

        static bool Navigable(VisualElement e)
        {
            if (!e.enabledInHierarchy) return false;
            var r = e.worldBound;
            if (float.IsNaN(r.width) || r.width < 2 || r.height < 2) return false;
            if (e is Button || e is Slider) return true;
            if (e is TextField || e is ScrollView) return false;
            return e.focusable && e.pickingMode == PickingMode.Position && e.ClassListContains("check");
        }

        VisualElement Default()
        {
            // the screen's main button if it has one, otherwise the top left
            VisualElement best = null;
            foreach (var e in found) if (e.ClassListContains("primary")) { best = e; break; }
            if (best != null) return best;
            var bs = float.MaxValue;
            foreach (var e in found) { var c = e.worldBound.center; var k = c.y * 3 + c.x; if (k < bs) { bs = k; best = e; } }
            return best;
        }

        VisualElement Nearest(Vector2 at)
        {
            if (at.x < -1e3f) return null;
            VisualElement best = null; var bd = float.MaxValue;
            foreach (var e in found) { var d = (e.worldBound.center - at).sqrMagnitude; if (d < bd) { bd = d; best = e; } }
            return best;
        }

        /// <summary>The nearest element in a direction on screen (panel space: y down), preferring ones in line.</summary>
        VisualElement Toward(VisualElement from, Vector2 dir)
        {
            var a = from.worldBound;
            VisualElement best = null; var bs = float.MaxValue;
            foreach (var e in found)
            {
                if (e == from) continue;
                var r = e.worldBound;
                // the gap between the two boxes along the direction, and how far off the line they are
                float along, across;
                if (dir.x != 0)
                {
                    along = dir.x > 0 ? r.xMin - a.xMax : a.xMin - r.xMax;
                    if (dir.x > 0 ? r.center.x <= a.center.x + 1 : r.center.x >= a.center.x - 1) continue;
                    across = Mathf.Max(0, Mathf.Max(r.yMin - a.yMax, a.yMin - r.yMax));
                }
                else
                {
                    along = dir.y > 0 ? r.yMin - a.yMax : a.yMin - r.yMax;
                    if (dir.y > 0 ? r.center.y <= a.center.y + 1 : r.center.y >= a.center.y - 1) continue;
                    across = Mathf.Max(0, Mathf.Max(r.xMin - a.xMax, a.xMin - r.xMax));
                }
                var score = Mathf.Max(0, along) + across * 3 + (across > 0 ? 40 : 0);
                if (score < bs) { bs = score; best = e; }
            }
            return best;
        }
    }
}
