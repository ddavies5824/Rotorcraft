// Port of the InputManager parts of tests/controller.test.ts, driven by a scripted input source instead of
// the browser Gamepad API, plus the T.Flight Stick X preset with the directions read off the real stick.
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Rotorcraft.Sim;
using UnityEngine.InputSystem;

namespace Rotorcraft.Inputs.Tests
{
    /// <summary>Pretend devices: a value per named control (axes -1..1, buttons 0 or 1) and a set of held keys.</summary>
    sealed class FakeSource : IInputSource
    {
        public readonly Dictionary<string, Dictionary<string, double>> devices = new Dictionary<string, Dictionary<string, double>>();
        public readonly HashSet<Key> keys = new HashSet<Key>();

        public void Set(string device, string control, double v)
        {
            if (!devices.TryGetValue(device, out var d)) devices[device] = d = new Dictionary<string, double>();
            d[control] = v;
        }

        public bool IsKeyDown(Key key) => keys.Contains(key);
        public bool AnyControllerConnected => devices.Count > 0;

        bool Find(string device, string control, out double v)
        {
            foreach (var kv in devices)
                if ((device == Controllers.ANY || kv.Key == device) && kv.Value.TryGetValue(control, out v)) return true;
            v = 0;
            return false;
        }

        public double? ReadAxis(AxisBinding b)
        {
            if (b == null || !Find(b.device, b.control, out var v) || double.IsNaN(v)) return null;
            return System.Math.Max(-1, System.Math.Min(1, b.invert ? -v : v));
        }

        public bool ReadButton(ButtonBinding b) => b != null && Find(b.device, b.control, out var v) && v > 0.5;
    }

    public class InputManagerTests
    {
        const string Stick = Controllers.TFlightStickX;

        static FakeSource TFlightAtRest(double slider)
        {
            var f = new FakeSource();
            foreach (var c in new[] { "stick/x", "stick/y", "rz", "hat/x", "hat/y" }) f.Set(Stick, c, 0);
            f.Set(Stick, "slider", slider);
            for (int i = 2; i <= 12; i++) f.Set(Stick, "button" + i, 0);
            f.Set(Stick, "trigger", 0);
            return f;
        }

        [Test]
        public void LeversMapTo0To1AndDeadZonesRescale()
        {
            Assert.AreEqual(0, Controllers.LeverPosition(-1));
            Assert.AreEqual(1, Controllers.LeverPosition(1));
            Assert.AreEqual(0.5, Controllers.LeverPosition(0));
            Assert.AreEqual(0, Controllers.ApplyDeadzone(0.05, 0.1));
            Assert.AreEqual(1, Controllers.ApplyDeadzone(1, 0.1));
            Assert.That(System.Math.Abs(Controllers.ApplyDeadzone(-0.55, 0.1) + 0.5) < 1e-9);
        }

        [Test]
        public void TFlightStickXDrivesCyclicPedalsAbsoluteCollectiveAndButtons()
        {
            // throttle lever fully forward reads slider -1 on the real stick: that is collective fully up
            var src = TFlightAtRest(-1);
            var settings = new ControlSettings { deadzone = 0.05, controller = Controllers.PresetTFlightStickX() };
            var im = new InputManager(settings, src);
            var actions = new List<GameAction>();
            im.ActionFired += actions.Add;
            var c = im.Sample(0.016);
            Assert.That(System.Math.Abs(c.collective - 1) < 1e-9, "lever forward = collective fully up");
            // stick right and forward (y +1 forward on this stick), twist right, lever pulled back, base button 5 held
            src.Set(Stick, "stick/x", 1); src.Set(Stick, "stick/y", 1); src.Set(Stick, "rz", 0.8); src.Set(Stick, "slider", 1); src.Set(Stick, "button5", 1);
            for (int i = 0; i < 30; i++) c = im.Sample(0.016);
            Assert.That(c.cyclicRoll > 0.9, $"roll {c.cyclicRoll}");
            Assert.That(c.cyclicPitch > 0.9, $"pitch {c.cyclicPitch} (stick forward = nose down)");
            Assert.That(c.pedals > 0.4, $"pedals {c.pedals} (twist right = right pedal)");
            Assert.AreEqual(0, c.collective, "lever pulled fully back");
            Assert.AreEqual(new[] { GameAction.Engine }, actions.ToArray(), "button 5 starts the engine once, not every frame");
            // the hat looks around the cockpit
            src.Set(Stick, "hat/x", 1);
            Assert.AreEqual(1, im.Look.x);
            // disabling controllers ignores the hardware
            settings.controller.enabled = false;
            src.Set(Stick, "slider", -1);
            c = im.Sample(0.016);
            Assert.AreEqual(0, c.collective, "lever change ignored when controllers are disabled");
        }

        [Test]
        public void ReleaseTrimEasesTheCyclicCentreBackToNeutral()
        {
            var src = new FakeSource();
            var im = new InputManager(new ControlSettings { controller = Controllers.PresetGamepad() }, src);
            var actions = new List<GameAction>();
            im.ActionFired += actions.Add;
            // hold the stick forward and right, then trim there
            src.Set("Pad", "rightStick/x", 0.6); src.Set("Pad", "rightStick/y", 0.5); src.Set("Pad", "rightTrigger", 0);
            for (int i = 0; i < 40; i++) im.Sample(0.016);
            im.SetTrimFromCurrent();
            Assert.That(im.Trimmed && im.trimX > 0.2 && im.trimY > 0.2, $"trimmed at {im.trimX}, {im.trimY}");
            src.Set("Pad", "rightStick/x", 0); src.Set("Pad", "rightStick/y", 0);
            for (int i = 0; i < 40; i++) im.Sample(0.016);
            var held = im.Sample(0.016);
            Assert.That(held.cyclicRoll > 0.2, "stick let go: the cyclic stays at the trim point");
            // RT asks for a release
            src.Set("Pad", "rightTrigger", 1); im.Sample(0.016); src.Set("Pad", "rightTrigger", 0);
            Assert.AreEqual(new[] { GameAction.TrimRelease }, actions.ToArray());
            im.ClearTrim();
            var x0 = im.trimX;
            im.Sample(0.1);
            Assert.That(im.trimX < x0 && im.trimX > 0, "eases back rather than snapping");
            for (int i = 0; i < 60; i++) im.Sample(0.016);
            Assert.That(!im.Trimmed && !im.trimReleasing, "back at centre within a second");
            var c = im.Sample(0.5);
            Assert.That(System.Math.Abs(c.cyclicRoll) < 0.02 && System.Math.Abs(c.cyclicPitch) < 0.02, "hands off: the cyclic returns to neutral");
            // trimming again during a release cancels it
            src.Set("Pad", "rightStick/x", 0.5); for (int i = 0; i < 40; i++) im.Sample(0.016); im.SetTrimFromCurrent();
            im.ClearTrim(); im.SetTrimFromCurrent();
            Assert.That(im.Trimmed && !im.trimReleasing);
            Assert.AreEqual(Key.U, new ControlSettings().KeyFor("trimRelease"));
        }

        [Test]
        public void TrimHoldsThePedalsWhereTheyWereUntilItIsReleased()
        {
            var src = new FakeSource();
            var im = new InputManager(new ControlSettings { controller = Controllers.PresetGamepad() }, src);
            src.Set("Pad", "leftStick/x", 0.6);
            Controls c = default;
            for (int i = 0; i < 40; i++) c = im.Sample(0.016);
            var held = c.pedals;
            Assert.That(held > 0.2, $"the stick moves the pedals: {held}");

            im.SetTrimFromCurrent();
            src.Set("Pad", "leftStick/x", 0);
            for (int i = 0; i < 40; i++) c = im.Sample(0.016);
            Assert.That(im.Trimmed && Math.Abs(c.pedals - held) < 0.01, $"let go, the pedals stay at the trim: {c.pedals} (trim {held})");

            src.Set("Pad", "leftStick/x", -0.8);
            for (int i = 0; i < 40; i++) c = im.Sample(0.016);
            Assert.That(c.pedals < held - 0.2, $"the stick still moves them from there: {c.pedals}");
            src.Set("Pad", "leftStick/x", 0);

            im.ClearTrim();
            for (int i = 0; i < 120; i++) c = im.Sample(0.016);
            Assert.That(!im.Trimmed && Math.Abs(c.pedals) < 0.01, $"released, they ease back to the middle: {c.pedals}");
        }

        [Test]
        public void MovingTheLeverOnASharedThumbstickDoesNotTurnTheNose()
        {
            var im = new InputManager(new ControlSettings { controller = Controllers.PresetGamepad() }, new FakeSource());
            Controls c = default;
            im.external = new ExternalSticks { pedals = 0.35, collective = 0.9 };                 // pushing up, a little off straight
            for (int i = 0; i < 40; i++) c = im.Sample(0.016);
            Assert.That(Math.Abs(c.pedals) < 0.01, $"no yaw from raising the lever: {c.pedals}");
            var lever = im.CollectiveLever;
            Assert.That(lever > 0.05, $"the lever rises: {lever}");

            im.external = new ExternalSticks { pedals = 0.9, collective = 0.35 };                 // pushing across, a little off straight
            for (int i = 0; i < 40; i++) c = im.Sample(0.016);
            Assert.That(c.pedals > 0.2, $"the pedals follow: {c.pedals}");
            Assert.That(Math.Abs(im.CollectiveLever - lever) < 0.001, $"the lever stays put: {im.CollectiveLever} (was {lever})");

            im.external = new ExternalSticks { pedals = 0.8, collective = 0.8 };                  // a real diagonal does both
            var before = im.CollectiveLever;
            for (int i = 0; i < 40; i++) c = im.Sample(0.016);
            Assert.That(c.pedals > 0.2 && im.CollectiveLever > before, $"both move on a diagonal: {c.pedals}, {im.CollectiveLever}");
        }

        [Test]
        public void VrSticksWorkAlongsideTheKeyboardAndAJoystick()
        {
            var src = new FakeSource();
            var im = new InputManager(new ControlSettings { controller = Controllers.PresetGamepad() }, src);
            src.Set("Pad", "rightStick/x", 0); src.Set("Pad", "rightStick/y", 0);

            // the VR sticks fly the aircraft: cyclic, pedals and a rising lever
            im.external = new ExternalSticks { cyclicX = 0.8, cyclicY = -0.6, pedals = 0.7, collective = 1 };
            var c = im.Sample(0.016);
            for (int i = 0; i < 40; i++) c = im.Sample(0.016);
            Assert.That(c.cyclicRoll > 0.2 && c.cyclicPitch < -0.1, $"cyclic follows the stick: {c.cyclicRoll}, {c.cyclicPitch}");
            Assert.That(c.pedals > 0.2, $"pedals follow the stick: {c.pedals}");
            Assert.That(im.CollectiveLever > 0.1, $"the lever rises: {im.CollectiveLever}");

            // a joystick being moved takes over from them
            src.Set("Pad", "rightStick/x", -0.9);
            for (int i = 0; i < 40; i++) c = im.Sample(0.016);
            Assert.That(c.cyclicRoll < -0.2, $"the joystick wins while it is moving: {c.cyclicRoll}");
            src.Set("Pad", "rightStick/x", 0);

            // the sticks let go, so the keyboard still works, and the lever stays where it was left
            im.external = default;
            var lever = im.CollectiveLever;
            src.keys.Add(new ControlSettings().KeyFor("cyclicLeft"));
            for (int i = 0; i < 40; i++) c = im.Sample(0.016);
            Assert.That(c.cyclicRoll < -0.1, $"the keyboard moves the cyclic: {c.cyclicRoll}");
            Assert.AreEqual(lever, im.CollectiveLever, 1e-9, "the lever holds its position");

            // trim from a VR button goes through the normal action
            var actions = new List<GameAction>();
            im.ActionFired += actions.Add;
            im.Fire(GameAction.Trim);
            Assert.AreEqual(new[] { GameAction.Trim }, actions.ToArray());
        }

        [Test]
        public void KeyboardMovesTheLeverAndFiresActionsOncePerPress()
        {
            var src = new FakeSource();
            var im = new InputManager(new ControlSettings(), src);
            var actions = new List<GameAction>();
            im.ActionFired += actions.Add;
            src.keys.Add(Key.W);
            for (int i = 0; i < 60; i++) im.Sample(1.0 / 60);
            Assert.That(System.Math.Abs(im.CollectiveLever - 0.38) < 1e-6, $"W raises the lever at 0.38 per second: {im.CollectiveLever}");
            src.keys.Clear();
            src.keys.Add(Key.E);
            im.Sample(0.016); im.Sample(0.016);
            src.keys.Add(Key.LeftShift); src.keys.Add(Key.L);   // Shift+L is a look key, not the landing light
            im.Sample(0.016);
            src.keys.Clear(); src.keys.Add(Key.Digit3);
            im.Sample(0.016);
            Assert.AreEqual(new[] { GameAction.Engine, GameAction.ApHover }, actions.ToArray());
        }
    }
}
