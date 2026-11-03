// Flies one aircraft: reads the player's controls, steps the flight model at its fixed 1/120 s rate and poses
// the model. The imported .glb keeps the parts grouped at their pivots, so the rotor spins about "blades".
// The flight session can swap the aircraft and its model, filter the controls through the autopilot, and give
// the weather of the moment (missions change it as they go).
using System;
using System.Collections.Generic;
using Rotorcraft.Inputs;
using Rotorcraft.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rotorcraft.Presentation
{
    public enum CameraMode { Chase, Cockpit, Orbit }

    public sealed class HelicopterController : MonoBehaviour
    {
        public string aircraftId = "kestrel";
        public string weatherId = "calm";
        public AssistMode assist = AssistMode.Beginner;
        [Tooltip("Start position in the sim's world (x east, z south) and heading in degrees.")]
        public Vector2 startXZ = Vector2.zero;
        public float startHeadingDeg = 0;
        [Tooltip("Fuel at the start (kg); below zero means 80% of capacity.")]
        public float startFuelKg = -1;
        [Tooltip("Show the start-up hint (off when a mission gives its own briefing).")]
        public bool startHint = true;
        [Tooltip("Run by a FlightSession: no flight until Begin, and no built-in keys (Backspace, F1).")]
        public bool managed;

        /// <summary>After every fixed sim step: the state, the step's events, the step length and the controls flown.</summary>
        public event Action<HeliState, IList<SimEvent>, double, Controls> Stepped;
        /// <summary>When the aircraft is put back at its start (Backspace, or a new flight).</summary>
        public event Action WasReset;
        /// <summary>Game actions from the keyboard or controller (the session handles the ones the aircraft does not).</summary>
        public event Action<GameAction> ActionFired;
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
        public bool CockpitView => Camera == CameraMode.Cockpit && Eye != null;
        public CameraMode Camera { get; private set; } = CameraMode.Chase;
        public bool Paused { get; private set; }
        public Transform Eye { get; private set; }
        /// <summary>Recent events for the HUD (touchdowns, crashes, engine), newest last.</summary>
        public readonly List<string> Messages = new List<string>();
        /// <summary>Autopilot (and anything else) between the pilot's controls and the flight model.</summary>
        public Func<Controls, double, Controls> ControlFilter;
        /// <summary>The weather at a sim time (missions change it as they go).</summary>
        public Func<double, WeatherPreset> WeatherAt;
        /// <summary>A hardware throttle axis moved: the session commands the twist grip.</summary>
        public double? lastThrottleLever;

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
            if (model != null) FindParts();
            stepper = new FixedStepper(dt =>
            {
                var c = ControlFilter != null ? ControlFilter(CurrentControls, dt) : CurrentControls;
                var wx = WeatherAt != null ? WeatherAt(State.t) : Weather;
                FlightModel.Step(State, c, Aircraft, wx, new SimSettings(assist), dt);
                foreach (var e in State.events) Report(e);
                Stepped?.Invoke(State, State.events, dt, CurrentControls);
                State.events.Clear();
            });
            if (!managed) ResetToStart();
            else State = FlightModel.CreateState(new CreateOptions { x = startXZ.x, z = startXZ.y, headingDeg = startHeadingDeg, fuel = Aircraft.fuelCapacity * 0.5 });
            SetCamera(CameraMode.Chase);
            // every flyable aircraft has its sound and lights; scenes need not add them
            if (GetComponent<RotorAudio>() == null) gameObject.AddComponent<RotorAudio>();
            if (GetComponent<AircraftLights>() == null) gameObject.AddComponent<AircraftLights>();
            if (GetComponent<AircraftEffects>() == null) gameObject.AddComponent<AircraftEffects>();
            if (GetComponent<CrewOnFoot>() == null) gameObject.AddComponent<CrewOnFoot>();
        }

        void FindParts()
        {
            blades = FindDeep(model, "blades");
            tailRotor = FindDeep(model, "tail_rotor");
            cockpitOnly = FindDeep(model, "cockpit_only");
            Eye = FindDeep(model, "eye");
            if (cockpitOnly != null) cockpitOnly.gameObject.SetActive(CockpitView);
        }

        void OnEnable() => InputSystem.onDeviceChange += OnDeviceChange;
        void OnDisable() => InputSystem.onDeviceChange -= OnDeviceChange;

        /// <summary>The T.Flight Stick X preset when that stick is plugged in, otherwise the gamepad layout.</summary>
        public static ControllerSettings ChooseController()
        {
            foreach (var d in InputSystem.devices)
                if (d is Joystick && d.description.product == Controllers.TFlightStickX) return Controllers.PresetTFlightStickX();
            return Controllers.PresetGamepad();
        }

        bool autoController = true;

        void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (!autoController || (change != InputDeviceChange.Added && change != InputDeviceChange.Removed)) return;
            var c = ChooseController();
            if (c.name == inputSettings.controller.name) return;
            inputSettings.controller = c;
            Input.UpdateSettings(inputSettings);
            Say($"Controls: {c.name}");
        }

        /// <summary>Use the player's control settings (sensitivities, keys, controller layout).</summary>
        public void ApplyControls(ControlSettings s, bool auto)
        {
            autoController = auto;
            inputSettings = s.Clone();
            if (auto) inputSettings.controller = ChooseController();
            Input.UpdateSettings(inputSettings);
        }

        /// <summary>Fly a different aircraft: its configuration and its model (an instance of the imported .glb).</summary>
        public void SetAircraft(AircraftConfig ac, GameObject modelPrefab)
        {
            Aircraft = ac;
            aircraftId = ac.id;
            if (modelPrefab != null)
            {
                if (model != null) Destroy(model.gameObject);
                var m = Instantiate(modelPrefab, transform);
                m.name = "Model";
                m.transform.localPosition = Vector3.zero;
                m.transform.localRotation = Quaternion.Euler(0, 180, 0);
                model = m.transform;
                FindParts();
            }
            AircraftDress.Apply(model, ac);
            rotorRpmAt100 = ac.engines > 1 ? (ac.rotorDiameter > 15 ? 265 : ac.rotorClockwise ? 350 : 330) : 530;
            tailRotorRatio = ac.engines > 1 ? 4.6f : 6f;
            GetComponent<RotorAudio>()?.Configure();
            GetComponent<AircraftLights>()?.Configure();
            GetComponent<AircraftEffects>()?.Configure();
            GetComponent<CrewOnFoot>()?.Configure();
        }

        /// <summary>Where and how the next flight starts (call ResetToStart to begin it).</summary>
        public void Configure(double x, double z, double headingDeg, double fuelKg, WeatherPreset weather)
        {
            startXZ = new Vector2((float)x, (float)z); startHeadingDeg = (float)headingDeg; startFuelKg = (float)fuelKg;
            if (weather != null) Weather = weather;
        }

        public void ResetToStart(double payload = 0)
        {
            State = FlightModel.CreateState(new CreateOptions { x = startXZ.x, z = startXZ.y, headingDeg = startHeadingDeg, fuel = startFuelKg >= 0 ? startFuelKg : Aircraft.fuelCapacity * 0.8, payload = payload });
            Input.ResetForFlight(0);
            stepper?.Reset();
            Paused = false;
            Messages.Clear();
            lastThrottleLever = null;
            if (startHint && !managed) Say("On the pad, engine off. E (or button 5) starts the engine, R (button 6) opens the throttle.");
            ApplyPose();
            WasReset?.Invoke();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (!managed && kb != null && kb.backspaceKey.wasPressedThisFrame) ResetToStart();
            if (!managed && kb != null && kb.f1Key.wasPressedThisFrame) { assist = (AssistMode)(((int)assist + 1) % 3); Say($"Handling: {assist}"); }
            var dt = Time.deltaTime;
            if (!Paused)
            {
                CurrentControls = Input.Sample(dt);
                // a hardware throttle axis drives the engine twist grip directly
                var tl = Input.throttleLever;
                if (tl != null && (lastThrottleLever == null || Math.Abs(tl.Value - lastThrottleLever.Value) > 0.01)) { FlightModel.CommandThrottle(State, tl.Value); lastThrottleLever = tl; }
                stepper.Advance(dt);
            }
            ApplyPose();
            if (!Paused)
            {
                // most rotors turn anticlockwise seen from above (the fuselage torques the other way); French-designed ones clockwise
                var degrees = (float)(State.@out.rpm / 100) * rotorRpmAt100 * 6f * dt;
                rotorAngle = (rotorAngle + (Aircraft.rotorClockwise ? degrees : -degrees)) % 360f;
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
            if (Paused && a != GameAction.Pause) { ActionFired?.Invoke(a); return; }
            switch (a)
            {
                case GameAction.Engine:
                    if (s.engine == EngineState.Off)
                    {
                        if (CurrentControls.collective > 0.2) Say("Lower the collective before starting (throttle lever fully back).");
                        else if (FlightModel.CommandEngine(s, EngineCommand.Start)) Say("Engine starting.");
                    }
                    else if (s.onGround) FlightModel.CommandEngine(s, EngineCommand.Stop);
                    break;
                case GameAction.ThrottleUp: FlightModel.CommandThrottle(s, 1); Say("Throttle open."); break;
                case GameAction.ThrottleDown: FlightModel.CommandThrottle(s, 0); Say("Throttle to idle."); break;
                case GameAction.Governor: FlightModel.CommandSwitch(s, SwitchName.Governor); Say(s.sys.governor ? "Governor on." : "Governor off."); break;
                case GameAction.Beacon: FlightModel.CommandSwitch(s, SwitchName.Beacon); break;
                case GameAction.NavLights: FlightModel.CommandSwitch(s, SwitchName.Nav); break;
                case GameAction.Strobe: FlightModel.CommandSwitch(s, SwitchName.Strobe); break;
                case GameAction.LandingLight: FlightModel.CommandSwitch(s, SwitchName.LandingLight); break;
                case GameAction.Searchlight: if (Aircraft.equipment.searchlight) FlightModel.CommandSwitch(s, SwitchName.Searchlight); break;
                case GameAction.Trim: Input.SetTrimFromCurrent(); Say("Cyclic trimmed."); break;
                case GameAction.TrimRelease: Input.ClearTrim(); Say("Trim released."); break;
                case GameAction.Camera: CycleCamera(); break;
                case GameAction.Pause: if (!managed) Paused = !Paused; break;
            }
            ActionFired?.Invoke(a);
        }

        /// <summary>Hold the simulation (debrief and menus).</summary>
        public void SetPaused(bool on) { if (Paused && !on) stepper?.Reset(); Paused = on; }

        public void CycleCamera() => SetCamera(Camera == CameraMode.Chase ? CameraMode.Cockpit : Camera == CameraMode.Cockpit ? CameraMode.Orbit : CameraMode.Chase);

        public void SetCamera(CameraMode m)
        {
            Camera = m == CameraMode.Cockpit && Eye == null ? CameraMode.Chase : m;
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

        public static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = FindDeep(c, name); if (f != null) return f; }
            return null;
        }
    }
}
