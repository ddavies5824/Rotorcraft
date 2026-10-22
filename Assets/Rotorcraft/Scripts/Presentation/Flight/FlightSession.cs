// One flight (port of the logic half of src/ui/FlightView.tsx): builds the world behind a loading screen the
// first time, puts the chosen aircraft on its start site, runs the mission engine (or the freeplay director)
// against the flight model, applies the autopilot, runs the pre-flight walk-round and cockpit checks, and ends
// with the debrief or the shift report. The HUD, panels and tablet are in the other parts of this class.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Rotorcraft.Game;
using Rotorcraft.Inputs;
using Rotorcraft.Presentation.UI;
using Rotorcraft.Sim;
using Rotorcraft.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Rotorcraft.Presentation
{
    public sealed class FlightSetup
    {
        public MissionDef mission;
        public AircraftConfig ac;
        public double fuelKg;
        public TimeOfDay tod;
        public string weatherId;
        /// <summary>Do the pre-flight checks in the aircraft (walk round, then cockpit) before engine start.</summary>
        public bool preflight;
        public bool checklistComplete, instrumentRated;
        /// <summary>Freeplay shift: callouts on the tablet instead of a fixed mission.</summary>
        public FreeplayConfig freeplay;
        public string loadingTitle, loadingSub;
        public Action<FlightResult> onFinish;
        public Action<FreeplaySummary, FlightResult> onFreeplayEnd;
        public Action onRestart;
    }

    public sealed partial class FlightSession : MonoBehaviour
    {
        public KinlochWorld world;
        public HelicopterController heli;
        public ChaseCamera cam;
        public AircraftLibrary library;
        public WorldActors actors;

        public bool Active { get; private set; }
        public FlightSetup Setup { get; private set; }
        public MissionRuntime Runtime { get; private set; }
        public FreeplayDirector Director { get; private set; }
        public Preflight Pre { get; private set; }
        public bool Paused { get; private set; }

        GameApp App => GameApp.Instance;
        ModelAnimator animator;
        double fpT;
        int fpVer = -1; double fpSyncT;
        float? crashAt;
        bool ending, preAnnounced, loading;
        (CheckId id, float t0, float yaw, float pitch, float dist)? preFocus;
        (float t0, int phase)? lookAnim;
        int wxVer = -1;

        void Awake()
        {
            animator = heli.GetComponent<ModelAnimator>() ?? heli.gameObject.AddComponent<ModelAnimator>();
            animator.heli = heli;
            heli.ActionFired += OnAction;
            heli.Stepped += OnStepped;
            heli.managed = true;
            ChaseCamera.PointerOverUi = PointerOverUi;
        }

        // ------------------------------------------------------------------------------------- start
        public void Begin(FlightSetup s)
        {
            if (loading) return;
            Setup = s;
            StopAllCoroutines();
            StartCoroutine(Run(s));
        }

        IEnumerator Run(FlightSetup s)
        {
            loading = true;
            Active = true;
            ending = false;
            var app = App;
            var load = ShowLoading(s.loadingTitle, s.loadingSub, s.freeplay != null);
            cam.menuBackdrop = false;
            ShowWorld(false);   // the loading screen covers it; the world appears once built
            float t0 = Time.realtimeSinceStartup;
            yield return null; yield return null;   // the loading screen is on screen before the heavy work starts
            if (!world.Built)
            {
                // step the build by hand so a failure reaches the player instead of freezing the loading screen
                var build = world.BuildRoutine((f, what) => load.Progress(f, what));
                Exception failure = null;
                while (true)
                {
                    bool more;
                    try { more = build.MoveNext(); } catch (Exception e) { failure = e; break; }
                    if (!more) break;
                    yield return build.Current;
                }
                if (failure != null)
                {
                    Debug.LogException(failure);
                    load.Fail($"The world could not be built: {failure.Message}", () => { load.Leave(); loading = false; Active = false; SetMenuBackdrop(true); app.FlightEnded(); });
                    yield break;
                }
            }
            ShowWorld(true);
            load.Progress(0.95f, "Preparing the aircraft");
            yield return null;

            // the aircraft and its model
            heli.SetAircraft(s.ac, library != null ? library.ModelFor(s.ac.id) : null);
            animator.Bind(heli.model, s.ac.id);
            var m = s.mission;
            Runtime = new MissionRuntime(m, s.ac, s.fuelKg, s.preflight ? false : s.checklistComplete, s.tod, new MissionOptions { weatherId = s.weatherId, instrumentRated = s.instrumentRated });
            var site = WorldMap.SiteById(m.startSiteId);
            heli.Configure(site.x, site.z, m.startHeadingDeg, s.fuelKg, Runtime.WeatherAt(0));
            heli.assist = app.Settings.assist;
            heli.ApplyControls(app.Settings.controls, app.Settings.autoController);
            heli.WeatherAt = t => Runtime.WeatherAt(t);
            heli.ControlFilter = ApplyAutopilot;
            heli.ResetToStart(m.payloadKg);
            heli.SetPaused(false);
            Paused = false;
            Director = s.freeplay != null ? new FreeplayDirector(s.freeplay, s.ac) : null;
            fpT = 0; fpVer = -1; fpSyncT = 0; crashAt = null; preAnnounced = false; preFocus = null; lookAnim = null; wxVer = Runtime.weatherVersion;
            SkyEnvironment.Current?.Apply(s.tod, Runtime.WeatherAt(0));
            actors?.Clear();
            SyncActors();
            // the pre-flight walk-round starts outside, beside the aircraft, with the crew door open
            Pre = s.preflight ? new Preflight(s.tod != TimeOfDay.Day) : null;
            if (Pre != null)
            {
                heli.SetCamera(CameraMode.Orbit);
                cam.orbitDist = 11; cam.orbitPitch = 7; cam.orbitYaw = 50;
                animator.Hold("crew_r", true); animator.Hold("crew_l", true);
                Runtime.Say(0, m.dual ? "Instructor" : "Crew", "Walk round first: click the fuel point, the rotor head, the tail rotor and the landing light. Then climb aboard for the cockpit checks.", MessageKind.Info);
            }
            else heli.SetCamera(app.Settings.camera == CameraPref.Cockpit ? CameraMode.Cockpit : CameraMode.Chase);
            cam.LookAhead(); cam.Snap();
            BuildHud();
            showPanel = Pre == null && Screen.width >= 1100;
            showMap = false; showSwitches = Pre != null; showTablet = false; hint = null;
            // let the first frames draw, then lift the loading screen (it stays at least long enough to read the tip)
            for (int i = 0; i < 4; i++) yield return null;
            while (Time.realtimeSinceStartup - t0 < 2.2f) yield return null;
            load.Leave();
            loading = false;
            app.FlightEnded();   // hides the menus (the flight is active)
        }

        // ------------------------------------------------------------------------------------- loop
        Controls ApplyAutopilot(Controls c, double dt)
        {
            if (Runtime == null) return c;
            var r = Autopilot.ApplyAP(Runtime.ap, heli.State, heli.Aircraft, c, App.Settings.assist, dt);
            if (r.dropped != null && r.dropped.Count > 0 && !heli.State.onGround) Runtime.Say(heli.State.t, "Autopilot", $"{string.Join(", ", r.dropped.Select(x => x.ToString().ToUpperInvariant()))} disengaged", MessageKind.Caution);
            return r.controls;
        }

        void OnStepped(HeliState s, IList<SimEvent> events, double dt, Controls c)
        {
            if (!Active || Runtime == null || loading) return;
            Runtime.Update(s, events, dt, c);
        }

        void Update()
        {
            if (!Active || loading || Runtime == null) return;
            var s = heli.State;
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (showTablet) showTablet = false;
                else TogglePause();
            }
            heli.Input.menuOpen = showTablet || Runtime.PendingDecision() != null || (Pre?.Active ?? false);
            if (!Paused)
            {
                if (Pre != null) StepPreflight();
                if (s.crashed && crashAt == null) crashAt = Time.unscaledTime;
                if (Director != null)
                {
                    if (s.t - fpT >= 0.25) { Director.Update(s.t, Runtime, s, s.t - fpT); fpT = s.t; }
                    if (Director.version != fpVer || s.t - fpSyncT > 5) SyncActors();
                }
                if (Runtime.weatherVersion != wxVer) { wxVer = Runtime.weatherVersion; SkyEnvironment.Current?.Apply(Setup.tod, Runtime.WeatherAt(s.t)); }
                UpdateDoors(s);
                cam.shake = s.onGround ? 0 : Mathf.Min(1, (float)(s.@out.etlShudder * 0.7 + s.@out.vrs * 0.8 + s.@out.bladeStall * 0.8));
                actors?.UpdateHoist(Runtime.hoist, s, heli.Aircraft);
            }
            else cam.shake = 0;
            UpdateHud();
        }

        /// <summary>Doors follow what is happening: open for loading passengers and freight, the cabin door for the winch.</summary>
        void UpdateDoors(HeliState s)
        {
            var a = Runtime.Active;
            var loadingNow = a != null && a.def.kind == ObjectiveKind.Load && s.onGround && a.progress > 0 && a.progress < 1;
            animator.HoldAll("cabin", loadingNow || (Runtime.hoist.phase != HoistPhase.Stowed && Runtime.hoist.phase != HoistPhase.Aboard));
            animator.Hold("rear", loadingNow && (a?.def.what?.Contains("patient") ?? false));
        }

        void SyncActors()
        {
            if (Director == null) { actors?.SetTargets(Runtime.targets, Runtime.seed); fpVer = -1; return; }
            var props = Director.SceneProps(heli.State.t);
            fpVer = Director.version; fpSyncT = heli.State.t;
            actors?.SetProps(props);
            actors?.SetTargets(FreeplayDirector.VisibleTargets(Runtime.targets), Runtime.seed);
        }

        // ------------------------------------------------------------------------------------- actions
        void OnAction(GameAction a)
        {
            if (!Active || loading || Runtime == null) return;
            var s = heli.State;
            switch (a)
            {
                case GameAction.Pause: TogglePause(); break;
                case GameAction.Panel: showPanel = !showPanel; break;
                case GameAction.Map: showMap = !showMap; break;
                case GameAction.Switches: showSwitches = !showSwitches; break;
                case GameAction.ApAlt: ApToggle(APMode.Alt); break;
                case GameAction.ApHdg: ApToggle(APMode.Hdg); break;
                case GameAction.ApHover: ApToggle(APMode.Hover); break;
                case GameAction.Winch: Runtime.Winch(s); break;
                case GameAction.Transmit: Runtime.Transmit(s); break;
                case GameAction.Flir: Runtime.ToggleFlir(); break;
                case GameAction.Tablet: if (Director != null) showTablet = !showTablet; break;
                case GameAction.Doors: if (!animator.TogglePlayer()) Runtime.Say(s.t, "Crew", "Doors stay shut in flight.", MessageKind.Caution); break;
                case GameAction.Voice: Runtime.Say(s.t, "Voice", "Voice commands are not available in this version: use the buttons or keys instead.", MessageKind.Caution); break;
                case GameAction.Engine: App.Later(() => { }); break;
            }
            hudDirty = true;
        }

        void ApToggle(APMode mode)
        {
            var r = Autopilot.ToggleAP(Runtime.ap, mode, heli.State, heli.Aircraft, heli.Input.CollectiveLever);
            if (!r.ok && r.reason != null) Runtime.Say(heli.State.t, "Autopilot", r.reason, MessageKind.Caution);
            hudDirty = true;
        }

        /// <summary>Open one of the panels (for the screenshot tour).</summary>
        public void ShowForTour(bool panel = false, bool map = false, bool tablet = false)
        {
            showPanel = panel; showMap = map; showTablet = tablet && Director != null; hudDirty = true;
        }

        public void TogglePause(bool? force = null)
        {
            Paused = force ?? !Paused;
            heli.SetPaused(Paused);
            AudioListener.pause = Paused;
            hudDirty = true;
        }

        public void SettingsChanged()
        {
            if (!Active) return;
            heli.assist = App.Settings.assist;
            heli.ApplyControls(App.Settings.controls, App.Settings.autoController);
            hudDirty = true;
        }

        /// <summary>Behind the menus: no flight, so either nothing rendered or a slow flyround of a built world.</summary>
        public void SetMenuBackdrop(bool menus)
        {
            if (!menus || Active) return;
            cam.menuBackdrop = world.Built;
            ShowWorld(world.Built);
        }

        int? worldMask;
        /// <summary>The camera always runs (it clears the screen under the menus); before the world is built it draws nothing.</summary>
        void ShowWorld(bool on)
        {
            var c = cam.GetComponent<Camera>();
            if (worldMask == null) worldMask = c.cullingMask;
            c.enabled = true;
            c.cullingMask = on ? worldMask.Value : 0;
            c.clearFlags = on ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            c.backgroundColor = new Color32(0x11, 0x18, 0x1d, 255);
        }

        // ------------------------------------------------------------------------------------- pre-flight
        /// <summary>Where the walk-round points are on the model (game model axes).</summary>
        static readonly Dictionary<string, (Vector3 fuel, float mastY, Vector3 tail, Vector3 landing)> WALK = new Dictionary<string, (Vector3, float, Vector3, Vector3)>
        {
            ["kestrel"] = (new Vector3(0.5f, 1.85f, 0.8f), 2.6f, new Vector3(-0.24f, 1.63f, 4.97f), new Vector3(0, 0.8f, -1.24f)),
            ["northstar"] = (new Vector3(1.42f, 1.2f, 0.9f), 3.55f, new Vector3(0.42f, 3.4f, 7.6f), new Vector3(-0.4f, 0.52f, -3.2f)),
            ["guardian"] = (new Vector3(1.6f, 1.35f, 1.0f), 3.95f, new Vector3(0.47f, 3.8f, 8.5f), new Vector3(-0.45f, 0.58f, -3.6f)),
            ["nightjar"] = (new Vector3(0.95f, 1.05f, 1.2f), 3.42f, new Vector3(0, 2.02f, 6.62f), new Vector3(0, 0.64f, -3.26f)),
        };

        Vector3 WalkPoint(CheckId id)
        {
            var w = WALK.TryGetValue(heli.Aircraft.id, out var x) ? x : WALK["kestrel"];
            Vector3 L(Vector3 v) => heli.transform.TransformPoint(new Vector3(v.x, v.y, -v.z));
            switch (id)
            {
                case CheckId.Fuel: return L(w.fuel);
                case CheckId.Rotor: return L(new Vector3(0, w.mastY + 0.15f, 0));
                case CheckId.Tail: return L(w.tail);
                default: return L(w.landing);
            }
        }

        void StepPreflight()
        {
            var pre = Pre; var s = heli.State;
            if (!pre.Active) return;
            // swing the walk-round camera to the part being inspected, then mark it checked
            if (preFocus != null)
            {
                var f = preFocus.Value;
                var d = Mathf.DeltaAngle(cam.orbitYaw, f.yaw);
                cam.orbitYaw += d * 0.08f; cam.orbitPitch += (f.pitch - cam.orbitPitch) * 0.08f; cam.orbitDist += (f.dist - cam.orbitDist) * 0.08f;
                if (Time.unscaledTime - f.t0 > 1.3f)
                {
                    var detail = f.id == CheckId.Fuel ? $"{s.fuel:0} kg on board, sample clear" : f.id == CheckId.Rotor ? "Blades, hub and pitch links: no damage" : f.id == CheckId.Tail ? "Tail rotor and gearbox: secure, no play" : "Position lights, beacon and landing light: working";
                    pre.Tick(f.id, detail);
                    preFocus = null; hudDirty = true;
                }
            }
            if (lookAnim != null)
            {
                // turn left, hold, swing right, hold, back to the front
                float[] ENDS = { 0.9f, 1.6f, 2.6f, 3.3f, 4.0f }; const float YAW = 86;
                var a = lookAnim.Value;
                var t = Time.unscaledTime - a.t0;
                if (a.phase < 5 && t > ENDS[a.phase]) { a.phase++; lookAnim = a; }
                if (a.phase >= 5) { cam.scriptedLookYaw = null; lookAnim = null; }
                else
                {
                    var start = a.phase > 0 ? ENDS[a.phase - 1] : 0; var u = Mathf.Clamp01((t - start) / (ENDS[a.phase] - start));
                    cam.scriptedLookYaw = new[] { -YAW * u, -YAW, -YAW + 2 * YAW * u, YAW, YAW * (1 - u) }[a.phase];
                }
            }
            var before = pre.stage;
            var c = heli.CurrentControls;
            var lookYaw = (cam.scriptedLookYaw ?? cam.lookYaw) * Mathf.Deg2Rad;
            var ticked = pre.Update(new PreflightInputs { cyclicRoll = c.cyclicRoll, cyclicPitch = c.cyclicPitch, pedals = c.pedals, collective = heli.Input.CollectiveLever, throttle = s.sys.throttle, beacon = s.sys.beacon, nav = s.sys.nav, lookYaw = lookYaw, engineOff = s.engine == EngineState.Off });
            if (ticked.Count > 0) hudDirty = true;
            if (!preAnnounced && (pre.stage == PreflightStage.Done || pre.stage == PreflightStage.Skipped) && before != pre.stage)
            {
                preAnnounced = true;
                var who = Setup.mission.dual ? "Instructor" : "Crew";
                if (pre.stage == PreflightStage.Done) { Runtime.CompleteChecklist(); Runtime.Say(s.t, who, "Pre-flight checks complete. Clear to start the engine.", MessageKind.Good); }
                else Runtime.Say(s.t, who, "Engine started before the checks were finished: the checklist counts as incomplete.", MessageKind.Caution);
                hudDirty = true;
            }
        }

        public void Inspect(CheckId id)
        {
            if (Pre == null || Pre.stage != PreflightStage.Outside || (Pre.Item(id)?.done ?? true)) return;
            if (heli.Camera != CameraMode.Orbit) heli.SetCamera(CameraMode.Orbit);
            var local = heli.transform.InverseTransformPoint(WalkPoint(id));
            var yaw = new Vector2(local.x, local.z).magnitude > 0.5f ? Mathf.Atan2(-local.x, -local.z) * Mathf.Rad2Deg : cam.orbitYaw;
            preFocus = (id, Time.unscaledTime, yaw, id == CheckId.Rotor ? 34 : 9, id == CheckId.Tail ? 9 : 8);
            hudDirty = true;
        }

        void PreAction(CheckId id)
        {
            var s = heli.State;
            if (id == CheckId.Harness) Pre.Tick(CheckId.Harness, "Doors latched, harness tight");
            else if (id == CheckId.Beacon && !s.sys.beacon) FlightModel.CommandSwitch(s, SwitchName.Beacon);
            else if (id == CheckId.Navlights && !s.sys.nav) FlightModel.CommandSwitch(s, SwitchName.Nav);
            else if (id == CheckId.Levers) { heli.Input.SetCollective(0); FlightModel.CommandThrottle(s, 0); }
            else if (id == CheckId.Lookout) lookAnim = (Time.unscaledTime, 0);
            else if (Pre.stage == PreflightStage.Outside) Inspect(id);
            hudDirty = true;
        }

        void ClimbAboard()
        {
            if (Pre == null || !Pre.Board()) return;
            heli.SetCamera(CameraMode.Cockpit); cam.LookAhead();
            animator.Hold("crew_r", false); animator.Hold("crew_l", false);
            if (Screen.width >= 1100) showPanel = true;
            showSwitches = true;
            Runtime.Say(heli.State.t, Setup.mission.dual ? "Instructor" : "Crew", "Strap in, then the cockpit checks: controls full and free, lever down, throttle closed, beacon on, and a good look round.", MessageKind.Info);
            hudDirty = true;
        }

        void SkipChecks()
        {
            if (Pre == null) return;
            Pre.Skip(); preAnnounced = true;
            animator.Hold("crew_r", false); animator.Hold("crew_l", false);
            Runtime.Say(heli.State.t, Setup.mission.dual ? "Instructor" : "Crew", "Checks skipped: that counts against procedures.", MessageKind.Caution);
            if (heli.Camera == CameraMode.Orbit) heli.SetCamera(App.Settings.camera == CameraPref.Cockpit ? CameraMode.Cockpit : CameraMode.Chase);
            hudDirty = true;
        }

        // ------------------------------------------------------------------------------------- end
        public void Finish(FlightEnd reason)
        {
            if (ending || Runtime == null) return;
            ending = true;
            var r = Runtime.Finish(heli.State, reason);
            r.timeOfDay = Setup.tod;
            r.weatherId = Setup.weatherId;
            var s = Setup;
            if (Director != null && s.onFreeplayEnd != null)
            {
                if (Director.active != null) Director.Abandon(heli.State.t, Runtime, heli.State);
                var sum = Director.Summary(r.flightSeconds);
                End();
                s.onFreeplayEnd(sum, r);
            }
            else { End(); s.onFinish?.Invoke(r); }
        }

        public void Restart()
        {
            var s = Setup;
            End();
            s.onRestart?.Invoke();
        }

        void End()
        {
            Active = false;
            Paused = false;
            AudioListener.pause = false;
            heli.SetPaused(true);
            showTablet = false;
            hudRoot?.RemoveFromHierarchy();
            hudRoot = null;
            actors?.Clear();
            cam.scriptedLookYaw = null;
            SetMenuBackdrop(true);
        }
    }
}
