// The gamepad in the menus and the on-screen sticks, driven the way a player would: a virtual gamepad pressing the
// D-pad, A and B over a small screen of buttons, a tick box and a slider; and a mouse pointer standing in for a finger on
// the touch controls, checked through the controls the InputManager hands the flight model.
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Rotorcraft.Inputs;
using Rotorcraft.Presentation.UI;
using Rotorcraft.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Rotorcraft.Preview.Tests
{
    public sealed class MenuPadTests
    {
        GameObject host;
        Gamepad pad;
        InputSettings.BackgroundBehavior background;
        InputSettings.EditorInputBehaviorInPlayMode editorInput;

        // the batch-mode editor has no focused window, and the Input System drops a pad's input without focus
        [SetUp]
        public void SetUp()
        {
            background = InputSystem.settings.backgroundBehavior; editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        }

        [TearDown]
        public void TearDown()
        {
            if (pad != null) InputSystem.RemoveDevice(pad);
            if (host != null) Object.Destroy(host);
            InputSystem.settings.backgroundBehavior = background; InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
        }

        IEnumerator Panel()
        {
            host = new GameObject("pad test UI");
            var doc = host.AddComponent<UIDocument>();
#if UNITY_EDITOR
            // the game's own panel (its theme sizes the slider) and style sheet
            doc.panelSettings = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Rotorcraft/UI/Game Panel.asset"));
#endif
            doc.panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            doc.panelSettings.scale = 1;
            yield return null;
            doc.rootVisualElement.style.flexGrow = 1;
#if UNITY_EDITOR
            doc.rootVisualElement.styleSheets.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Rotorcraft/UI/Rotorcraft.uss"));
#endif
        }

        static Button B(string text, VisualElement into, List<string> log)
        {
            var b = new Button(() => log.Add(text)) { text = text };
            b.style.width = 120; b.style.height = 40; b.style.marginRight = 20; b.style.marginBottom = 20;
            into.Add(b);
            return b;
        }

        [UnityTest]
        public IEnumerator TheGamepadMovesByPositionPressesAndGoesBack()
        {
            yield return Panel();
            var root = host.GetComponent<UIDocument>().rootVisualElement;
            var log = new List<string>();
            var screen = new VisualElement(); screen.style.paddingLeft = 40; screen.style.paddingTop = 40; root.Add(screen);
            VisualElement Row() { var r = new VisualElement(); r.style.flexDirection = FlexDirection.Row; screen.Add(r); return r; }
            var r1 = Row(); var a0 = B("A0", r1, log); var a1 = B("A1", r1, log); var a2 = B("A2", r1, log);
            var r2 = Row(); var b0 = B("B0", r2, log); var b1 = B("B1", r2, log); var b2 = B("B2", r2, log);
            b1.AddToClassList("primary");
            var ticked = false;
            var check = Ui.Check("Tick", false, v => { ticked = v; log.Add("tick"); });
            check.style.width = 200; check.style.height = 30;
            screen.Add(check);
            var committed = -1f;
            var field = Ui.Slider("Volume", 0, 1, 0.5f, v => $"{v * 100:0}%", v => committed = v, 0.05f); field.style.width = 300; screen.Add(field);
            var slider = field.Q<Slider>();
            var back = B("BACK", Row(), log);

            pad = InputSystem.AddDevice<Gamepad>();
            var nav = new MenuNavigation();
            var scopes = new List<VisualElement> { screen };
            var wentHome = false;
            for (int i = 0; i < 3; i++) yield return null;   // layout
            IEnumerator Push(GamepadButton button)
            {
                InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(button)); InputSystem.Update();
                nav.Tick(scopes, () => wentHome = true);
                yield return null;
                InputSystem.QueueStateEvent(pad, new GamepadState()); InputSystem.Update();
                nav.Tick(scopes, () => wentHome = true);
                yield return null;
            }

            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.DpadRight)); InputSystem.Update();
            Debug.Log($"Pad test: {Gamepad.all.Count} pads, dpad {pad.dpad.ReadValue()}, right {pad.dpad.right.isPressed}");
            InputSystem.QueueStateEvent(pad, new GamepadState()); InputSystem.Update();
            yield return Push(GamepadButton.DpadRight);
            Assert.AreSame(b1, nav.Current, nav.Why + ": " + "the first press shows the highlight on the screen's main button, without moving");
            Assert.IsTrue(b1.ClassListContains(MenuNavigation.FocusClass));
            yield return Push(GamepadButton.DpadRight);
            Assert.AreSame(b2, nav.Current, "right moves to the button beside it");
            yield return Push(GamepadButton.DpadUp);
            Assert.AreSame(a2, nav.Current, "up moves to the button above");
            yield return Push(GamepadButton.DpadLeft);
            yield return Push(GamepadButton.DpadLeft);
            Assert.AreSame(a0, nav.Current, "left twice reaches the first button in the row");
            Assert.IsFalse(a2.ClassListContains(MenuNavigation.FocusClass), "the highlight leaves the button it moved off");
            yield return Push(GamepadButton.South);
            CollectionAssert.AreEqual(new[] { "A0" }, log, "A presses the highlighted button, once");
            yield return Push(GamepadButton.DpadDown);
            yield return Push(GamepadButton.DpadDown);
            Assert.AreSame(check, nav.Current, "down reaches the tick box under the buttons");
            yield return Push(GamepadButton.South);
            Assert.IsTrue(ticked, "A ticks the box");
            yield return Push(GamepadButton.DpadDown);
            Assert.AreSame(slider, nav.Current, "down reaches the slider");
            yield return Push(GamepadButton.DpadRight);
            Assert.Greater(slider.value, 0.5f, "right on a slider moves the slider");
            Assert.AreEqual(slider.value, committed, 1e-3f, "and the setting is saved, as dragging it does");
            Assert.AreSame(slider, nav.Current, "and the highlight stays on it");
            yield return Push(GamepadButton.East);
            Assert.Contains("BACK", log, "B presses the Back button on screen");
            Assert.IsFalse(wentHome, "the screen's own Back button comes before going home");

            // a screen with no Back button: B goes home
            back.RemoveFromHierarchy();
            yield return null;
            yield return Push(GamepadButton.East);
            Assert.IsTrue(wentHome, "with no Back button B goes home");
        }

        static void Send<T>(VisualElement target, EventType type, Vector2 at) where T : PointerEventBase<T>, new()
        {
            var ev = new Event { type = type, mousePosition = at, button = 0, clickCount = 1 };
            using (var e = PointerEventBase<T>.GetPooled(ev)) { e.target = target; target.SendEvent(e); }
        }

        [UnityTest]
        public IEnumerator TheOnScreenSticksFlyTheAircraft()
        {
            yield return Panel();
            var root = host.GetComponent<UIDocument>().rootVisualElement;
            root.AddToClassList("hud");
            var input = new InputManager(new ControlSettings());
            var tc = new TouchControls(input);
            tc.style.position = Position.Absolute; tc.style.left = 0; tc.style.top = 0; tc.style.width = 1280; tc.style.height = 720;
            root.Add(tc);
            for (int i = 0; i < 4; i++) { tc.Refresh(false); yield return null; }
            var stick = tc.Q(className: "t-stick"); var lever = tc.Q(className: "t-lever"); var pedal = tc.Q(className: "t-pedal");
            Assert.Greater(stick.worldBound.width, 100, "the stick is laid out");
            Assert.Greater(stick.worldBound.center.x, 640, "standard layout: the cyclic on the right");
            Assert.Less(lever.worldBound.center.x, 640, "and the collective on the left");

            // cyclic: a thumb pushed right and forward of centre, then let go
            var c = stick.worldBound.center; var rad = stick.worldBound.width / 2 * 0.8f;
            Send<PointerDownEvent>(stick, EventType.MouseDown, c + new Vector2(rad * 0.6f, -rad * 0.5f));
            Assert.IsTrue(input.touch.cyclicActive);
            Assert.AreEqual(0.6, input.touch.cyclicX, 0.02); Assert.AreEqual(0.5, input.touch.cyclicY, 0.02);
            Controls k = default;
            for (int i = 0; i < 30; i++) k = input.Sample(0.05);
            Assert.Greater(k.cyclicRoll, 0.2, "the cyclic goes right");
            Assert.Greater(k.cyclicPitch, 0.15, "and forward");
            Assert.AreEqual(InputSourceKind.Touch, input.lastSource);
            Send<PointerUpEvent>(stick, EventType.MouseUp, c);
            Assert.IsFalse(input.touch.cyclicActive, "letting go releases the stick");
            for (int i = 0; i < 30; i++) k = input.Sample(0.05);
            Assert.AreEqual(0, k.cyclicRoll, 0.01, "and it springs back to centre");

            // collective: dragging the lever up from where the thumb lands raises it by that much, with no jump
            var l = lever.worldBound; var travel = l.height - 40;
            Send<PointerDownEvent>(lever, EventType.MouseDown, new Vector2(l.center.x, l.yMax - 30));
            Assert.AreEqual(0, input.CollectiveLever, 1e-6, "touching the lever does not move it");
            Send<PointerMoveEvent>(lever, EventType.MouseMove, new Vector2(l.center.x, l.yMax - 30 - travel * 0.4f));
            Assert.AreEqual(0.4, input.CollectiveLever, 0.02, "dragging up 40% of the travel raises the lever 40%");
            Send<PointerUpEvent>(lever, EventType.MouseUp, new Vector2(l.center.x, l.yMax - 30 - travel * 0.4f));
            for (int i = 0; i < 10; i++) k = input.Sample(0.05);
            Assert.AreEqual(0.4, k.collective, 0.02, "the lever stays where it is left");

            // pedals: a thumb near the right end, then let go
            var p = pedal.worldBound;
            Send<PointerDownEvent>(pedal, EventType.MouseDown, new Vector2(p.xMax - p.width * 0.05f, p.center.y));
            Assert.Greater(input.touch.pedals, 0.8);
            for (int i = 0; i < 30; i++) k = input.Sample(0.05);
            Assert.Greater(k.pedals, 0.5, "right pedal");
            Send<PointerUpEvent>(pedal, EventType.MouseUp, p.center);
            for (int i = 0; i < 30; i++) k = input.Sample(0.05);
            Assert.AreEqual(0, k.pedals, 0.01, "the pedals centre when let go");

            // swapped layout
            for (int i = 0; i < 3; i++) { tc.Refresh(true); yield return null; }
            Assert.Less(stick.worldBound.center.x, 640, "swapped: the cyclic on the left");
            Assert.Greater(lever.worldBound.center.x, 640, "and the collective on the right");
        }
    }
}
