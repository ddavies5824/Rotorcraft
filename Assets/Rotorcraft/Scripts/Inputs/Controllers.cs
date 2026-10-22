// Joystick, HOTAS, rudder pedal and gamepad mapping (port of src/input/controller.ts). A binding names the
// device by its product name ("T.Flight Stick X") or ANY, and the control by its Unity Input System name
// ("slider", "rz", "button5", "rightStick/x"), so a separate stick, throttle quadrant and pedal set can each
// drive different controls. The browser game numbers axes and buttons instead; Unity names them.
using System.Collections.Generic;

namespace Rotorcraft.Inputs
{
    public enum GameAction { Engine, Trim, TrimRelease, Camera, Pause, Panel, Map, Switches, ThrottleUp, ThrottleDown, Governor, Beacon, NavLights, Strobe, LandingLight, Searchlight, ApAlt, ApHdg, ApHover, Winch, Transmit, Flir, Voice, Tablet, Doors }
    /// <summary>LookX / LookY (cockpit head movement, e.g. a hat switch) are new in the Unity version.</summary>
    public enum AxisFunction { CyclicX, CyclicY, Collective, Pedals, Throttle, LookX, LookY }
    public enum ButtonFunction { Engine, ThrottleUp, ThrottleDown, Trim, TrimReset, Camera, Pause, Map, Panel, Switches, LandingLight, Beacon, NavLights, ApAlt, ApHdg, ApHover, Winch, Transmit, Voice, Tablet }
    /// <summary>Absolute = the lever axis position is the collective position; Rate = a spring stick raises/lowers it.</summary>
    public enum CollectiveMode { Absolute, Rate }

    public sealed class AxisBinding
    {
        public string device, control;
        public bool invert;
        public AxisBinding(string device, string control, bool invert = false) { this.device = device; this.control = control; this.invert = invert; }
        public AxisBinding Clone() => (AxisBinding)MemberwiseClone();
    }

    public sealed class ButtonBinding
    {
        public string device, control;
        public ButtonBinding(string device, string control) { this.device = device; this.control = control; }
        public ButtonBinding Clone() => (ButtonBinding)MemberwiseClone();
    }

    public sealed class ControllerSettings
    {
        public string name;
        public bool enabled = true;
        public CollectiveMode collectiveMode = CollectiveMode.Rate;
        public Dictionary<AxisFunction, AxisBinding> axes = new Dictionary<AxisFunction, AxisBinding>();
        public Dictionary<ButtonFunction, ButtonBinding> buttons = new Dictionary<ButtonFunction, ButtonBinding>();

        public AxisBinding Axis(AxisFunction f) => axes.TryGetValue(f, out var b) ? b : null;

        public ControllerSettings Clone()
        {
            var c = (ControllerSettings)MemberwiseClone();
            c.axes = new Dictionary<AxisFunction, AxisBinding>();
            foreach (var kv in axes) c.axes[kv.Key] = kv.Value.Clone();
            c.buttons = new Dictionary<ButtonFunction, ButtonBinding>();
            foreach (var kv in buttons) c.buttons[kv.Key] = kv.Value.Clone();
            return c;
        }
    }

    public static class Controllers
    {
        /// <summary>"Any connected device": used by presets so they work before the player has run setup.</summary>
        public const string ANY = "*";

        public static readonly IReadOnlyDictionary<AxisFunction, string> AxisLabels = new Dictionary<AxisFunction, string>
        {
            [AxisFunction.CyclicX] = "Cyclic left / right", [AxisFunction.CyclicY] = "Cyclic forward / back", [AxisFunction.Collective] = "Collective",
            [AxisFunction.Pedals] = "Pedals (yaw)", [AxisFunction.Throttle] = "Engine throttle (twist grip)", [AxisFunction.LookX] = "Look left / right", [AxisFunction.LookY] = "Look up / down",
        };
        public static readonly IReadOnlyDictionary<ButtonFunction, string> ButtonLabels = new Dictionary<ButtonFunction, string>
        {
            [ButtonFunction.Engine] = "Engine start / stop", [ButtonFunction.ThrottleUp] = "Throttle open", [ButtonFunction.ThrottleDown] = "Throttle idle", [ButtonFunction.Trim] = "Trim cyclic", [ButtonFunction.TrimReset] = "Release trim",
            [ButtonFunction.Camera] = "Camera", [ButtonFunction.Pause] = "Pause", [ButtonFunction.Map] = "Map", [ButtonFunction.Panel] = "Instrument panel", [ButtonFunction.Switches] = "Switch panel",
            [ButtonFunction.LandingLight] = "Landing light", [ButtonFunction.Beacon] = "Beacon", [ButtonFunction.NavLights] = "Position lights",
            [ButtonFunction.ApAlt] = "Autopilot ALT", [ButtonFunction.ApHdg] = "Autopilot HDG", [ButtonFunction.ApHover] = "Autopilot hover hold", [ButtonFunction.Winch] = "Winch (lower / abort)",
            [ButtonFunction.Transmit] = "Radio transmit", [ButtonFunction.Voice] = "Voice command (push to talk)", [ButtonFunction.Tablet] = "Crew tablet (freeplay)",
        };

        /// <summary>The action a controller button fires.</summary>
        public static GameAction ActionFor(ButtonFunction f)
        {
            switch (f)
            {
                case ButtonFunction.Engine: return GameAction.Engine;
                case ButtonFunction.ThrottleUp: return GameAction.ThrottleUp;
                case ButtonFunction.ThrottleDown: return GameAction.ThrottleDown;
                case ButtonFunction.Trim: return GameAction.Trim;
                case ButtonFunction.TrimReset: return GameAction.TrimRelease;
                case ButtonFunction.Camera: return GameAction.Camera;
                case ButtonFunction.Pause: return GameAction.Pause;
                case ButtonFunction.Map: return GameAction.Map;
                case ButtonFunction.Panel: return GameAction.Panel;
                case ButtonFunction.Switches: return GameAction.Switches;
                case ButtonFunction.LandingLight: return GameAction.LandingLight;
                case ButtonFunction.Beacon: return GameAction.Beacon;
                case ButtonFunction.NavLights: return GameAction.NavLights;
                case ButtonFunction.ApAlt: return GameAction.ApAlt;
                case ButtonFunction.ApHdg: return GameAction.ApHdg;
                case ButtonFunction.ApHover: return GameAction.ApHover;
                case ButtonFunction.Winch: return GameAction.Winch;
                case ButtonFunction.Transmit: return GameAction.Transmit;
                case ButtonFunction.Voice: return GameAction.Voice;
                default: return GameAction.Tablet;
            }
        }

        static ButtonBinding B(string device, string control) => new ButtonBinding(device, control);

        /// <summary>Standard gamepad (Xbox, PlayStation and the like), laid out as the browser game's gamepad preset.</summary>
        public static ControllerSettings PresetGamepad() => new ControllerSettings
        {
            name = "Gamepad",
            enabled = true, collectiveMode = CollectiveMode.Rate,
            axes =
            {
                [AxisFunction.CyclicX] = new AxisBinding(ANY, "rightStick/x"), [AxisFunction.CyclicY] = new AxisBinding(ANY, "rightStick/y"),
                [AxisFunction.Collective] = new AxisBinding(ANY, "leftStick/y"), [AxisFunction.Pedals] = new AxisBinding(ANY, "leftStick/x"),
            },
            buttons =
            {
                [ButtonFunction.Engine] = B(ANY, "buttonSouth"), [ButtonFunction.LandingLight] = B(ANY, "buttonEast"), [ButtonFunction.Camera] = B(ANY, "buttonWest"), [ButtonFunction.Trim] = B(ANY, "buttonNorth"),
                [ButtonFunction.ThrottleDown] = B(ANY, "leftShoulder"), [ButtonFunction.ThrottleUp] = B(ANY, "rightShoulder"), [ButtonFunction.Tablet] = B(ANY, "leftTrigger"), [ButtonFunction.TrimReset] = B(ANY, "rightTrigger"),
                [ButtonFunction.Map] = B(ANY, "select"), [ButtonFunction.Pause] = B(ANY, "start"), [ButtonFunction.Panel] = B(ANY, "leftStickPress"), [ButtonFunction.Voice] = B(ANY, "rightStickPress"),
                [ButtonFunction.ApAlt] = B(ANY, "dpad/up"), [ButtonFunction.ApHover] = B(ANY, "dpad/down"), [ButtonFunction.Transmit] = B(ANY, "dpad/left"), [ButtonFunction.Winch] = B(ANY, "dpad/right"),
            },
        };

        public const string TFlightStickX = "T.Flight Stick X";

        /// <summary>
        /// Thrustmaster T.Flight Stick X: stick for the cyclic, twist for the pedals, the throttle lever as an
        /// absolute collective, and the hat to look around. Directions as read in the Unity editor on 2026-10-22:
        /// throttle lever forward = slider −1 (so it is inverted), twist right = rz +1, stick forward = y +1.
        /// </summary>
        public static ControllerSettings PresetTFlightStickX()
        {
            const string D = TFlightStickX;
            return new ControllerSettings
            {
                name = "Thrustmaster T.Flight Stick X",
                enabled = true, collectiveMode = CollectiveMode.Absolute,
                axes =
                {
                    [AxisFunction.CyclicX] = new AxisBinding(D, "stick/x"), [AxisFunction.CyclicY] = new AxisBinding(D, "stick/y"),
                    [AxisFunction.Pedals] = new AxisBinding(D, "rz"), [AxisFunction.Collective] = new AxisBinding(D, "slider", invert: true),
                    [AxisFunction.LookX] = new AxisBinding(D, "hat/x"), [AxisFunction.LookY] = new AxisBinding(D, "hat/y"),
                },
                buttons =
                {
                    [ButtonFunction.Trim] = B(D, "trigger"), [ButtonFunction.Camera] = B(D, "button2"), [ButtonFunction.TrimReset] = B(D, "button3"), [ButtonFunction.LandingLight] = B(D, "button4"),
                    [ButtonFunction.Engine] = B(D, "button5"), [ButtonFunction.ThrottleUp] = B(D, "button6"), [ButtonFunction.ThrottleDown] = B(D, "button7"), [ButtonFunction.Winch] = B(D, "button8"),
                    [ButtonFunction.ApAlt] = B(D, "button9"), [ButtonFunction.ApHover] = B(D, "button10"), [ButtonFunction.Map] = B(D, "button11"), [ButtonFunction.Pause] = B(D, "button12"),
                },
            };
        }

        /// <summary>Map a lever axis (-1 = back/down … +1 = forward/up after inversion) to a 0..1 lever position.</summary>
        public static double LeverPosition(double v) => System.Math.Max(0, System.Math.Min(1, (v + 1) / 2));

        /// <summary>Centre dead zone with rescaling so full deflection still reaches ±1.</summary>
        public static double ApplyDeadzone(double v, double dz)
        {
            var a = System.Math.Abs(v);
            if (a <= dz) return 0;
            return System.Math.Sign(v) * System.Math.Min(1, (a - dz) / System.Math.Max(1e-6, 1 - dz));
        }
    }
}
