// The control settings part of the browser game's Settings (src/game/save.ts): sensitivities, dead zone,
// key bindings and the controller layout.
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace Rotorcraft.Inputs
{
    public sealed class ControlSettings
    {
        public double cyclicSensitivity = 1;   // 0.4..1.6
        public double pedalSensitivity = 1;
        public double deadzone = 0.08;         // 0..0.3
        public bool invertCyclicY;
        public ControllerSettings controller = Controllers.PresetGamepad();
        /// <summary>Key per function, named as in the browser game ("collectiveUp", "engine", "apAlt"…).</summary>
        public Dictionary<string, Key> keyBindings = DefaultKeys();

        public static Dictionary<string, Key> DefaultKeys() => new Dictionary<string, Key>
        {
            ["collectiveUp"] = Key.W, ["collectiveDown"] = Key.S,
            ["pedalLeft"] = Key.A, ["pedalRight"] = Key.D,
            ["cyclicFwd"] = Key.UpArrow, ["cyclicAft"] = Key.DownArrow, ["cyclicLeft"] = Key.LeftArrow, ["cyclicRight"] = Key.RightArrow,
            ["engine"] = Key.E, ["throttleUp"] = Key.R, ["throttleDown"] = Key.F, ["governor"] = Key.G,
            ["beacon"] = Key.B, ["navLights"] = Key.N, ["strobe"] = Key.O, ["landingLight"] = Key.L, ["searchlight"] = Key.H,
            ["trim"] = Key.T, ["trimRelease"] = Key.U, ["camera"] = Key.C, ["pause"] = Key.P, ["panel"] = Key.I, ["map"] = Key.M, ["switches"] = Key.K,
            ["apAlt"] = Key.Digit1, ["apHdg"] = Key.Digit2, ["apHover"] = Key.Digit3, ["winch"] = Key.X, ["transmit"] = Key.V, ["flir"] = Key.J, ["voice"] = Key.Y, ["tablet"] = Key.Tab, ["doors"] = Key.Z, ["gear"] = Key.Q,
        };

        /// <summary>The name an action's key binding is stored under: Engine → "engine", ApAlt → "apAlt".</summary>
        public static string KeyName(GameAction a)
        {
            var s = a.ToString();
            return char.ToLowerInvariant(s[0]) + s.Substring(1);
        }

        public Key KeyFor(string name) => keyBindings.TryGetValue(name, out var k) ? k : Key.None;

        public ControlSettings Clone()
        {
            var c = (ControlSettings)MemberwiseClone();
            c.controller = controller.Clone();
            c.keyBindings = new Dictionary<string, Key>(keyBindings);
            return c;
        }
    }
}
