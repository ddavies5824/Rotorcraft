// On-screen controls for phones and tablets (port of ui/TouchControls.tsx): a collective lever that stays where it is
// left, a pedal strip and a cyclic stick that both spring back to centre (the cyclic's trim shows as a dot), and a TRIM
// button. Each control follows its own finger, so both thumbs work at once. They write into the InputManager's touch
// sticks and lever, which merge with the keyboard and controllers exactly as the browser game's touch input does. The
// collective is on the left and the cyclic on the right, or the other way round (Settings, "Touch layout").
using System;
using Rotorcraft.Inputs;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rotorcraft.Presentation.UI
{
    public sealed class TouchControls : VisualElement
    {
        /// <summary>Is this a touch device (a phone or tablet, or a touch screen)?</summary>
        public static bool Available => UnityEngine.InputSystem.Touchscreen.current != null;

        readonly InputManager input;
        readonly VisualElement lever, leverFill, leverKnob, pedal, pedalKnob, stick, stickKnob, trimDot;
        readonly Label leverVal;
        readonly Button trim;
        int leverId = -1, pedalId = -1, stickId = -1;
        float grab;
        bool swapped, sided;

        public TouchControls(InputManager input)
        {
            this.input = input;
            AddToClassList("touch");
            pickingMode = PickingMode.Ignore;

            lever = Part("t-lever");
            leverFill = Part("t-lever-fill"); lever.Add(leverFill);
            leverKnob = Part("t-lever-knob"); leverVal = new Label("0") { pickingMode = PickingMode.Ignore }; leverVal.AddToClassList("t-lever-val"); leverKnob.Add(leverVal); lever.Add(leverKnob);
            lever.Add(Lbl("COLL", "t-lbl t-lbl-lever"));
            Add(lever);

            pedal = Part("t-pedal");
            pedalKnob = Part("t-pedal-knob"); pedal.Add(pedalKnob);
            pedal.Add(Lbl("PEDALS", "t-lbl t-lbl-pedal"));
            Add(pedal);

            stick = Part("t-stick");
            var cross = Part("t-cross-h"); stick.Add(cross); stick.Add(Part("t-cross-v"));
            trimDot = Part("t-trimdot"); stick.Add(trimDot);
            stickKnob = Part("t-stick-knob"); stick.Add(stickKnob);
            stick.Add(Lbl("CYCLIC", "t-lbl t-lbl-stick"));
            Add(stick);

            trim = new Button() { text = "TRIM" };
            trim.AddToClassList("hb"); trim.AddToClassList("t-trim");
            trim.RegisterCallback<PointerDownEvent>(e => { input.Touch(GameAction.Trim); e.StopPropagation(); }, TrickleDown.TrickleDown);
            Add(trim);

            lever.pickingMode = pedal.pickingMode = stick.pickingMode = PickingMode.Position;

            // the lever moves by how far the finger moves from where it landed, so touching it never makes it jump
            lever.RegisterCallback<PointerDownEvent>(e => { lever.CapturePointer(e.pointerId); leverId = e.pointerId; grab = LeverAt(e.position); e.StopPropagation(); });
            lever.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (e.pointerId != leverId) return;
                var v = LeverAt(e.position);
                input.TouchCollective(input.CollectiveLever + v - grab);
                grab = v;
                e.StopPropagation();
            });
            void LeverUp(int id) { if (id != leverId) return; leverId = -1; if (lever.HasPointerCapture(id)) lever.ReleasePointer(id); }
            lever.RegisterCallback<PointerUpEvent>(e => LeverUp(e.pointerId));
            lever.RegisterCallback<PointerCancelEvent>(e => LeverUp(e.pointerId));
            lever.RegisterCallback<PointerCaptureOutEvent>(e => leverId = -1);

            pedal.RegisterCallback<PointerDownEvent>(e => { pedal.CapturePointer(e.pointerId); pedalId = e.pointerId; PedalAt(e.position); e.StopPropagation(); });
            pedal.RegisterCallback<PointerMoveEvent>(e => { if (e.pointerId == pedalId) { PedalAt(e.position); e.StopPropagation(); } });
            void PedalUp(int id) { if (id != pedalId) return; pedalId = -1; input.touch.pedalActive = false; input.touch.pedals = 0; if (pedal.HasPointerCapture(id)) pedal.ReleasePointer(id); }
            pedal.RegisterCallback<PointerUpEvent>(e => PedalUp(e.pointerId));
            pedal.RegisterCallback<PointerCancelEvent>(e => PedalUp(e.pointerId));
            pedal.RegisterCallback<PointerCaptureOutEvent>(e => { pedalId = -1; input.touch.pedalActive = false; input.touch.pedals = 0; });

            stick.RegisterCallback<PointerDownEvent>(e => { stick.CapturePointer(e.pointerId); stickId = e.pointerId; StickAt(e.position); e.StopPropagation(); });
            stick.RegisterCallback<PointerMoveEvent>(e => { if (e.pointerId == stickId) { StickAt(e.position); e.StopPropagation(); } });
            void StickUp(int id) { if (id != stickId) return; stickId = -1; input.touch.cyclicActive = false; if (stick.HasPointerCapture(id)) stick.ReleasePointer(id); }
            stick.RegisterCallback<PointerUpEvent>(e => StickUp(e.pointerId));
            stick.RegisterCallback<PointerCancelEvent>(e => StickUp(e.pointerId));
            stick.RegisterCallback<PointerCaptureOutEvent>(e => { stickId = -1; input.touch.cyclicActive = false; });

            RegisterCallback<DetachFromPanelEvent>(_ => Release());
        }

        static VisualElement Part(string cls) { var e = new VisualElement { pickingMode = PickingMode.Ignore }; e.AddToClassList(cls); return e; }
        static Label Lbl(string text, string cls) { var l = new Label(text) { pickingMode = PickingMode.Ignore }; foreach (var c in cls.Split(' ')) l.AddToClassList(c); return l; }

        float LeverAt(Vector2 p) { var r = lever.worldBound; return 1 - (p.y - r.yMin - 20) / Mathf.Max(1, r.height - 40); }

        void PedalAt(Vector2 p)
        {
            var r = pedal.worldBound;
            input.touch.pedals = Mathf.Clamp(((p.x - r.xMin) / Mathf.Max(1, r.width) - 0.5f) * 2, -1, 1);
            input.touch.pedalActive = true;
        }

        void StickAt(Vector2 p)
        {
            var r = stick.worldBound;
            var rad = r.width / 2 * 0.8f;
            float x = (p.x - r.center.x) / rad, y = -(p.y - r.center.y) / rad;
            var m = Mathf.Sqrt(x * x + y * y); if (m > 1) { x /= m; y /= m; }
            input.touch.cyclicX = x; input.touch.cyclicY = y; input.touch.cyclicActive = true;
            input.lastSource = InputSourceKind.Touch;
        }

        /// <summary>Let go of everything (the controls are being hidden).</summary>
        public void Release()
        {
            leverId = pedalId = stickId = -1;
            input.touch.cyclicActive = input.touch.pedalActive = false; input.touch.pedals = 0;
        }

        /// <summary>Keep the knobs where the controls are (the lever moves by keyboard and controller too), and lay them out.</summary>
        public void Refresh(bool swapped)
        {
            if (swapped != this.swapped || !sided)
            {
                this.swapped = swapped; sided = true;
                void Side(VisualElement e, float near)
                {
                    if (swapped) { e.style.right = near; e.style.left = StyleKeyword.Auto; }
                    else { e.style.left = near; e.style.right = StyleKeyword.Auto; }
                }
                void Other(VisualElement e, float near)
                {
                    if (swapped) { e.style.left = near; e.style.right = StyleKeyword.Auto; }
                    else { e.style.right = near; e.style.left = StyleKeyword.Auto; }
                }
                Side(lever, 14); Side(pedal, 14); Other(stick, 18);
            }
            // the stick is a square: 42% of the screen's height, at most 210 px; TRIM sits beside it
            var h = layout.height;
            if (float.IsNaN(h) || h <= 0) return;
            var size = Mathf.Min(h * 0.42f, 210);
            if (Mathf.Abs(stick.resolvedStyle.width - size) > 0.5f)
            {
                stick.style.width = size; stick.style.height = size;
                stick.style.borderTopLeftRadius = stick.style.borderTopRightRadius = stick.style.borderBottomLeftRadius = stick.style.borderBottomRightRadius = size / 2;
            }
            if (swapped) { trim.style.left = 18 + size + 8; trim.style.right = StyleKeyword.Auto; }
            else { trim.style.right = 18 + size + 8; trim.style.left = StyleKeyword.Auto; }
            pedal.style.width = Mathf.Min(layout.width * 0.3f, 220);

            var lh = lever.layout.height - 40;
            var v = (float)input.CollectiveLever;
            if (!float.IsNaN(lh) && lh > 0)
            {
                leverKnob.style.bottom = v * lh;
                leverFill.style.height = v * lh + 20;
            }
            var txt = (v * 100).ToString("0");
            if (leverVal.text != txt) leverVal.text = txt;
            var rr = size / 2;
            var t = input.touch;
            float cx = t.cyclicActive ? (float)t.cyclicX : 0, cy = t.cyclicActive ? (float)t.cyclicY : 0;
            stickKnob.style.left = rr + cx * rr * 0.8f; stickKnob.style.top = rr - cy * rr * 0.8f;
            trimDot.style.left = rr + (float)input.trimX * rr * 0.8f; trimDot.style.top = rr - (float)input.trimY * rr * 0.8f;
            var pw = pedal.layout.width - 58;
            if (!float.IsNaN(pw)) pedalKnob.style.left = 2 + pw / 2 + (t.pedalActive ? (float)t.pedals : 0) * pw / 2;
        }
    }
}
