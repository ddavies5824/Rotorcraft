// Flies one aircraft: reads the player's controls, steps the flight model at its fixed 1/120 s rate and poses
// the model. The imported .glb keeps the parts grouped at their pivots, so the rotor spins about "blades".
using System.Collections.Generic;
using Rotorcraft.Inputs;
using Rotorcraft.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rotorcraft.Presentation
{
    public sealed class HelicopterController : MonoBehaviour
    {
        public string aircraftId = "kestrel";
        public string weatherId = "calm";
        public AssistMode assist = AssistMode.Beginner;
        [Tooltip("Start position in the sim's world (x east, z south) and heading in degrees.")]
        public Vector2 startXZ = Vector2.zero;
        public float startHeadingDeg = 0;
        [Tooltip("The imported model, turned 180° about Y so its nose faces this object's forward.")]
        public Transform model;
        [Tooltip("Main rotor speed at 100% RPM, revolutions per minute (for the spinning blades only).")]
        public float rotorRpmAt100 = 530;

        public HeliState State { get; private set; }
        public AircraftConfig Aircraft { get; private set; }
        public WeatherPreset Weather { get; private set; }
        public InputManager Input { get; private set; }
        public Controls CurrentControls { get; private set; }
        public string ControllerName => inputSettings.controller.name;
        public bool CockpitView { get; private set; }
        public bool Paused { get; private set; }
        public Transform Eye { get; private set; }
        /// <summary>Recent events for the HUD (touchdowns, crashes, engine), newest last.</summary>
        public readonly List<string> Messages = new List<string>();

        ControlSettings inputSettings;
        FixedStepper stepper;
        Transform blades, tailRotor, cockpitOnly;
        float rotorAngle, tailAngle;
        [Tooltip("Tail rotor revolutions per main rotor revolution.")]
        public float tailRotorRatio = 6f;

        void Awake()
        {
            Aircraft = Rotorcraft.Sim.Aircraft.Get(aircraftId);
            Weather = Rotorcraft.Sim.Weather.Presets[weatherId];
            inputSettings = new ControlSettings { controller = ChooseController() };
            Input = new InputManager(inputSettings);
            Input.ActionFired += OnAction;
            if (model != null)
            {
                blades = FindDeep(model, "blades");
                tailRotor = FindDeep(model, "tail_rotor");
                cockpitOnly = FindDeep(model, "cockpit_only");
                Eye = FindDeep(model, "eye");
            }
            stepper = new FixedStepper(dt =>
            {
                FlightModel.Step(State, CurrentControls, Aircraft, Weather, new SimSettings(assist), dt);
                foreach (var e in State.events) Report(e);
                State.events.Clear();
            });
            ResetToStart();
            SetCockpitView(false);
        }

        void OnEnable() => InputSystem.onDeviceChange += OnDeviceChange;
        void OnDisable() => InputSystem.onDeviceChange -= OnDeviceChange;

        /// <summary>The T.Flight Stick X preset when that stick is plugged in, otherwise the gamepad layout.</summary>
        static ControllerSettings ChooseController()
        {
            foreach (var d in InputSystem.devices)
                if (d is Joystick && d.description.product == Controllers.TFlightStickX) return Controllers.PresetTFlightStickX();
            return Controllers.PresetGamepad();
        }

        void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (change != InputDeviceChange.Added && change != InputDeviceChange.Removed) return;
            var c = ChooseController();
            if (c.name == inputSettings.controller.name) return;
            inputSettings.controller = c;
            Input.UpdateSettings(inputSettings);
            Say($"Controls: {c.name}");
        }

        public void ResetToStart()
        {
            State = FlightModel.CreateState(new CreateOptions { x = startXZ.x, z = startXZ.y, headingDeg = startHeadingDeg, fuel = Aircraft.fuelCapacity * 0.8 });
            Input.ResetForFlight(0);
            stepper?.Reset();
            Messages.Clear();
            Say("On the pad, engine off. E (or button 5) starts the engine, R (button 6) opens the throttle.");
            ApplyPose();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.backspaceKey.wasPressedThisFrame) ResetToStart();
            if (kb != null && kb.f1Key.wasPressedThisFrame) { assist = (AssistMode)(((int)assist + 1) % 3); Say($"Handling: {assist}"); }
            var dt = Time.deltaTime;
            CurrentControls = Input.Sample(dt);
            if (!Paused) stepper.Advance(dt);
            ApplyPose();
            if (!Paused)
            {
                // the rotor turns anticlockwise seen from above (the fuselage torques the other way)
                var degrees = (float)(State.@out.rpm / 100) * rotorRpmAt100 * 6f * dt;
                rotorAngle = (rotorAngle - degrees) % 360f;
                tailAngle = (tailAngle + degrees * tailRotorRatio) % 360f;
                if (blades != null) blades.localRotation = Quaternion.Euler(0, rotorAngle, 0);
                if (tailRotor != null) tailRotor.localRotation = Quaternion.Euler(tailAngle, 0, 0);   // spins about its own x axis
            }
        }

        void ApplyPose()
        {
            transform.SetPositionAndRotation(SimToUnity.Position(State.pos), SimToUnity.Attitude(State));
        }

        void OnAction(GameAction a)
        {
            var s = State;
            switch (a)
            {
                case GameAction.Engine:
                    if (s.engine == EngineState.Off)
                    {
                        if (CurrentControls.collective > 0.2) Say("Lower the collective before starting (throttle lever fully back).");
                        else if (FlightModel.CommandEngine(s, EngineCommand.Start)) Say("Engine starting.");
                    }
                    else FlightModel.CommandEngine(s, EngineCommand.Stop);
                    break;
                case GameAction.ThrottleUp: FlightModel.CommandThrottle(s, 1); Say("Throttle open."); break;
                case GameAction.ThrottleDown: FlightModel.CommandThrottle(s, 0); Say("Throttle to idle."); break;
                case GameAction.Governor: FlightModel.CommandSwitch(s, SwitchName.Governor); Say(s.sys.governor ? "Governor on." : "Governor off."); break;
                case GameAction.Beacon: FlightModel.CommandSwitch(s, SwitchName.Beacon); break;
                case GameAction.NavLights: FlightModel.CommandSwitch(s, SwitchName.Nav); break;
                case GameAction.Strobe: FlightModel.CommandSwitch(s, SwitchName.Strobe); break;
                case GameAction.LandingLight: FlightModel.CommandSwitch(s, SwitchName.LandingLight); break;
                case GameAction.Trim: Input.SetTrimFromCurrent(); Say("Cyclic trimmed."); break;
                case GameAction.TrimRelease: Input.ClearTrim(); Say("Trim released."); break;
                case GameAction.Camera: SetCockpitView(!CockpitView); break;
                case GameAction.Pause: Paused = !Paused; break;
            }
        }

        void SetCockpitView(bool on)
        {
            CockpitView = on && Eye != null;
            if (cockpitOnly != null) cockpitOnly.gameObject.SetActive(CockpitView);
        }

        void Report(SimEvent e)
        {
            switch (e.type)
            {
                case SimEventType.Liftoff: Say("Lift-off."); break;
                case SimEventType.Touchdown: Say($"Touchdown: {e.severity}, {e.vs * 196.85:F0} ft/min."); break;
                case SimEventType.Crash: Say($"CRASH: {e.reason}. Backspace to reset."); break;
                case SimEventType.Damage: Say($"Damage: {e.reason}."); break;
                case SimEventType.Engine: Say($"Engine {e.state.ToString().ToLowerInvariant()}."); break;
            }
        }

        void Say(string m)
        {
            Messages.Add(m);
            if (Messages.Count > 5) Messages.RemoveAt(0);
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = FindDeep(c, name); if (f != null) return f; }
            return null;
        }
    }
}
