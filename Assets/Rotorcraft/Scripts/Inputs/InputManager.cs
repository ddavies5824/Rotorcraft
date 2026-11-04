// InputManager (port of src/input/input.ts): merges keyboard and controllers into one Controls value each frame.
// Collective is a positional lever (it stays where you leave it, like the real friction-held lever);
// cyclic and pedals are spring-centred, with a trim offset for the cyclic.
// The browser version reacts to keydown events; this one polls, firing an action on the frame a key goes down.
// Touch controls are not ported yet.
using System;
using System.Collections.Generic;
using Rotorcraft.Sim;
using UnityEngine.InputSystem;
using static Rotorcraft.Sim.SimMath;

namespace Rotorcraft.Inputs
{
    public enum InputSourceKind { None, Keyboard, Controller }

    /// <summary>Stick positions from a device outside the player's bindings (the VR controllers), each -1..1.</summary>
    public struct ExternalSticks { public double cyclicX, cyclicY, pedals, collective; }

    public sealed class InputManager
    {
        /// <summary>Set each frame by the device's owner. Merged like a controller, so any other input still works beside it; a bound controller that is moving takes priority.</summary>
        public ExternalSticks external;
        ControlSettings settings;
        readonly IInputSource source;
        double collective;
        double cycX, cycY;
        double pedal;
        public double trimX, trimY;
        /// <summary>Trim easing back to centre after a release (so the aircraft is not jolted).</summary>
        public bool trimReleasing;
        public event Action<GameAction> ActionFired;
        public InputSourceKind lastSource = InputSourceKind.None;
        readonly Dictionary<ButtonFunction, bool> padPrev = new Dictionary<ButtonFunction, bool>();
        readonly HashSet<Key> keysDown = new HashSet<Key>();
        /// <summary>Engine throttle lever from a joystick/throttle-quadrant axis (0 idle … 1 open), or null if not bound.</summary>
        public double? throttleLever;
        double? lastLever;
        /// <summary>True while an on-screen dialog (decision, pause) has the controller: buttons drive the menu, not the aircraft.</summary>
        public bool menuOpen;

        static readonly GameAction[] DirectKeyActions =
        {
            GameAction.ThrottleUp, GameAction.ThrottleDown, GameAction.Governor, GameAction.Beacon, GameAction.NavLights, GameAction.Strobe, GameAction.LandingLight, GameAction.Searchlight,
            GameAction.Switches, GameAction.ApAlt, GameAction.ApHdg, GameAction.ApHover, GameAction.Winch, GameAction.Transmit, GameAction.Flir, GameAction.Voice, GameAction.Tablet, GameAction.Doors, GameAction.Gear,
        };
        static readonly Key[] LookKeys = { Key.I, Key.J, Key.K, Key.L };

        HashSet<Key> watchedKeys;

        public InputManager(ControlSettings settings, IInputSource source = null)
        {
            this.source = source ?? new UnityInputSource();
            UpdateSettings(settings);
        }

        public void UpdateSettings(ControlSettings s)
        {
            settings = s;
            watchedKeys = new HashSet<Key>(s.keyBindings.Values) { Key.Escape };
        }
        void Emit(GameAction a) => ActionFired?.Invoke(a);
        /// <summary>An action from a device outside the player's bindings.</summary>
        public void Fire(GameAction a) { lastSource = InputSourceKind.Controller; Emit(a); }

        bool K(Key k) => k != Key.None && source.IsKeyDown(k);
        bool Shift => K(Key.LeftShift) || K(Key.RightShift);

        /// <summary>Fire actions for keys that went down since the last frame, as the browser's keydown handler does.</summary>
        void PollKeys()
        {
            foreach (var key in watchedKeys)
            {
                var down = K(key);
                var was = keysDown.Contains(key);
                if (down) keysDown.Add(key); else keysDown.Remove(key);
                if (!down || was) continue;
                lastSource = InputSourceKind.Keyboard;
                if (Shift && Array.IndexOf(LookKeys, key) >= 0) continue; // cockpit look keys
                OnKeyDown(key);
            }
        }

        void OnKeyDown(Key key)
        {
            var kb = settings;
            foreach (var a in DirectKeyActions) if (key == kb.KeyFor(ControlSettings.KeyName(a))) { Emit(a); return; }
            if (key == kb.KeyFor("engine")) Emit(GameAction.Engine);
            else if (key == kb.KeyFor("trim")) Emit(GameAction.Trim);
            else if (key == kb.KeyFor("trimRelease")) Emit(GameAction.TrimRelease);
            else if (key == kb.KeyFor("camera")) Emit(GameAction.Camera);
            else if (key == kb.KeyFor("pause") || key == Key.Escape) Emit(GameAction.Pause);
            else if (key == kb.KeyFor("panel")) Emit(GameAction.Panel);
            else if (key == kb.KeyFor("map")) Emit(GameAction.Map);
        }

        /// <summary>Cockpit head movement from the keyboard (numeric keypad, or Shift + I/J/K/L) or a bound hat, -1..1 per axis.</summary>
        public (double x, double y) Look
        {
            get
            {
                var sh = Shift;
                double x = (K(Key.Numpad6) || (sh && K(Key.L)) ? 1 : 0) - (K(Key.Numpad4) || (sh && K(Key.J)) ? 1 : 0);
                double y = (K(Key.Numpad8) || (sh && K(Key.I)) ? 1 : 0) - (K(Key.Numpad2) || (sh && K(Key.K)) ? 1 : 0);
                var cs = settings.controller;
                if (cs.enabled && x == 0 && y == 0)
                {
                    x = source.ReadAxis(cs.Axis(AxisFunction.LookX)) ?? 0;
                    y = source.ReadAxis(cs.Axis(AxisFunction.LookY)) ?? 0;
                }
                return (x, y);
            }
        }

        public void SetCollective(double v) { collective = Clamp(v, 0, 1); }
        public double CollectiveLever => collective;
        public void ResetForFlight(double collective = 0) { this.collective = collective; cycX = cycY = 0; pedal = 0; trimX = trimY = 0; trimReleasing = false; keysDown.Clear(); }

        public void SetTrimFromCurrent() { trimReleasing = false; trimX = Clamp(cycX, -0.6, 0.6); trimY = Clamp(cycY, -0.6, 0.6); }
        /// <summary>Release the trim: the cyclic's centre eases back to neutral over about a second.</summary>
        public void ClearTrim() { if (Trimmed) trimReleasing = true; }
        public bool Trimmed => Math.Abs(trimX) > 0.005 || Math.Abs(trimY) > 0.005;

        double Shape(double v, double sens)
        {
            var dz = settings.deadzone;
            var a = Math.Abs(v);
            if (a < dz) return 0;
            var n = (a - dz) / (1 - dz);
            // mild expo: finer control near centre
            return Math.Sign(v) * Clamp((0.45 * n + 0.55 * n * n * n) * sens, 0, 1);
        }

        /// <summary>Called once per rendered frame with real elapsed time.</summary>
        public Controls Sample(double dt)
        {
            var s = settings;
            PollKeys();
            // ---- joystick / HOTAS / pedals / gamepad, through the player's bindings ----
            (double x, double y)? padCyc = null;
            double padPed = 0, padCol = 0;
            double? padLever = null;
            throttleLever = null;
            var cs = s.controller;
            if (cs.enabled && source.AnyControllerConnected)
            {
                double? cx = source.ReadAxis(cs.Axis(AxisFunction.CyclicX)), cy = source.ReadAxis(cs.Axis(AxisFunction.CyclicY));
                var dz = Math.Max(0.02, s.deadzone);
                if ((cx != null && Math.Abs(cx.Value) > dz) || (cy != null && Math.Abs(cy.Value) > dz)) padCyc = (cx ?? 0, cy ?? 0);
                var ped = source.ReadAxis(cs.Axis(AxisFunction.Pedals));
                if (ped != null) padPed = ped.Value;
                var col = source.ReadAxis(cs.Axis(AxisFunction.Collective));
                if (col != null) { if (cs.collectiveMode == CollectiveMode.Absolute) padLever = Controllers.LeverPosition(col.Value); else padCol = col.Value; }
                var thr = source.ReadAxis(cs.Axis(AxisFunction.Throttle));
                if (thr != null) throttleLever = Controllers.LeverPosition(thr.Value);
                foreach (var kv in cs.buttons)
                {
                    var down = source.ReadButton(kv.Value);
                    padPrev.TryGetValue(kv.Key, out var was);
                    if (down && !was && !(menuOpen && kv.Key != ButtonFunction.Pause && kv.Key != ButtonFunction.Tablet))
                    {
                        lastSource = InputSourceKind.Controller;
                        Emit(Controllers.ActionFor(kv.Key));
                    }
                    padPrev[kv.Key] = down;
                }
                if (padCyc != null || Math.Abs(padPed) > 0.1 || Math.Abs(padCol) > 0.1) lastSource = InputSourceKind.Controller;
            }
            {
                var ex = external;
                var dz = Math.Max(0.02, s.deadzone);
                if (padCyc == null && (Math.Abs(ex.cyclicX) > dz || Math.Abs(ex.cyclicY) > dz)) { padCyc = (ex.cyclicX, ex.cyclicY); lastSource = InputSourceKind.Controller; }
                if (Math.Abs(padPed) <= 0.05 && Math.Abs(ex.pedals) > 0.05) { padPed = ex.pedals; lastSource = InputSourceKind.Controller; }
                if (Math.Abs(padCol) <= 0.12 && Math.Abs(ex.collective) > 0.12) { padCol = ex.collective; lastSource = InputSourceKind.Controller; }
            }
            // ---- collective (positional) ----
            var colRate = Shift ? 0.12 : 0.38;
            if (K(s.KeyFor("collectiveUp")) || K(Key.PageUp)) collective += colRate * dt;
            if (K(s.KeyFor("collectiveDown")) || K(Key.PageDown)) collective -= colRate * dt;
            if (Math.Abs(padCol) > 0.12) collective += Shape(padCol, 1) * 0.45 * dt;
            // An absolute hardware lever sets the collective whenever it moves; between movements the
            // keyboard can still adjust it.
            if (padLever != null)
            {
                if (lastLever == null || Math.Abs(padLever.Value - lastLever.Value) > 0.004) { collective = padLever.Value; lastSource = InputSourceKind.Controller; }
                lastLever = padLever;
            }
            collective = Clamp(collective, 0, 1);
            // ---- cyclic (spring-centred to trim) ----
            double tx = 0, ty = 0;
            if (K(s.KeyFor("cyclicLeft"))) tx -= 1;
            if (K(s.KeyFor("cyclicRight"))) tx += 1;
            if (K(s.KeyFor("cyclicFwd"))) ty += 1;
            if (K(s.KeyFor("cyclicAft"))) ty -= 1;
            var keyCyc = tx != 0 || ty != 0;
            if (trimReleasing)
            {
                var step = dt * 0.75;                                    // full trim (0.6) back to centre in under a second
                trimX -= Clamp(trimX, -step, step);
                trimY -= Clamp(trimY, -step, step);
                if (!Trimmed) { trimX = trimY = 0; trimReleasing = false; }
            }
            double targetX = trimX, targetY = trimY;
            if (keyCyc) { targetX = trimX + tx * 0.55 * s.cyclicSensitivity; targetY = trimY + ty * 0.55 * s.cyclicSensitivity; }
            if (padCyc != null) { targetX = trimX + Shape(padCyc.Value.x, s.cyclicSensitivity); targetY = trimY + Shape(padCyc.Value.y, s.cyclicSensitivity); }
            if (s.invertCyclicY) targetY = trimY - (targetY - trimY);
            var rate = keyCyc ? 2.2 : 9; // keyboard ramps; analogue follows quickly
            cycX += Clamp(targetX - cycX, -rate * dt, rate * dt);
            cycY += Clamp(targetY - cycY, -rate * dt, rate * dt);
            // ---- pedals ----
            double tp = 0;
            if (K(s.KeyFor("pedalLeft"))) tp -= 0.7 * s.pedalSensitivity;
            if (K(s.KeyFor("pedalRight"))) tp += 0.7 * s.pedalSensitivity;
            if (Math.Abs(padPed) > 0.05) tp = Shape(padPed, s.pedalSensitivity);
            var prate = tp != 0 && Math.Abs(padPed) <= 0.05 ? 2.5 : 8;
            pedal += Clamp(tp - pedal, -prate * dt, prate * dt);
            return new Controls(collective, Clamp(cycY, -1, 1), Clamp(cycX, -1, 1), Clamp(pedal, -1, 1));
        }
    }
}
