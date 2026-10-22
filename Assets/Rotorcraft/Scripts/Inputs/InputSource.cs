// Where the InputManager reads keys and controller controls from: Unity's Input System in the game,
// a scripted fake in tests.
using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Rotorcraft.Inputs
{
    public interface IInputSource
    {
        bool IsKeyDown(Key key);
        bool AnyControllerConnected { get; }
        /// <summary>Axis value in [-1, 1] after inversion, or null if the device or control is missing.</summary>
        double? ReadAxis(AxisBinding binding);
        bool ReadButton(ButtonBinding binding);
    }

    public sealed class UnityInputSource : IInputSource
    {
        public bool IsKeyDown(Key key)
        {
            var kb = Keyboard.current;
            return kb != null && key != Key.None && kb[key].isPressed;
        }

        static IEnumerable<InputDevice> ConnectedControllers()
        {
            foreach (var d in InputSystem.devices)
                if (d.enabled && (d is Joystick || d is Gamepad)) yield return d;
        }

        public bool AnyControllerConnected
        {
            get { foreach (var _ in ConnectedControllers()) return true; return false; }
        }

        /// <summary>Exact product (or display) name first; ANY = the first connected controller that has the control.</summary>
        static InputControl Find(string device, string control)
        {
            if (string.IsNullOrEmpty(control)) return null;
            foreach (var d in ConnectedControllers())
            {
                if (device != Controllers.ANY && d.description.product != device && d.displayName != device) continue;
                var c = d.TryGetChildControl(control);
                if (c != null) return c;
            }
            return null;
        }

        public double? ReadAxis(AxisBinding b)
        {
            if (b == null || !(Find(b.device, b.control) is AxisControl c)) return null;
            double v = c.ReadValue();
            if (double.IsNaN(v) || double.IsInfinity(v)) return null;
            return Math.Max(-1, Math.Min(1, b.invert ? -v : v));
        }

        public bool ReadButton(ButtonBinding b) => b != null && Find(b.device, b.control) is ButtonControl c && c.isPressed;
    }
}
