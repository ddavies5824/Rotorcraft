// VR for the Windows game, through OpenXR, so a Quest over Link or Virtual Desktop and any other PC headset all work
// the same way. The game runs on a normal screen until VR is started from Settings or with -vr on the command line.
//
// The main camera goes inside a rig. The rig is the player's seat, moved by ChaseCamera; the headset moves the camera
// inside it. The flat UI (menus and flight HUD) is drawn into a texture shown on a panel fixed in front of the seat,
// and the right controller's laser works it with the trigger.
//
// Hands: squeeze a grip button to take hold of a control, from wherever the hand is: the right hand takes the cyclic and the
// left the collective, or whichever one a hand is right beside. While a hand holds something its thumbstick is free to
// work the pedals. Letting go of the cyclic returns it to the trim; the collective stays put. A free hand's index finger
// presses the switches on the panel beside the seat.
// Controllers: right stick cyclic; left stick pedals (across) and collective (up and down); A set trim; B release
// trim; X show or hide the HUD; Y tablet; menu button pause; click the right stick to recentre the view; click the
// left stick to change camera. Keyboard, joystick, pedals and gamepad keep working alongside them.
using System.Collections;
using System.Collections.Generic;
using Rotorcraft.Inputs;
using Rotorcraft.Presentation;
using Rotorcraft.Presentation.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.UIElements;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR.Input;
using ProviderStats = UnityEngine.XR.Provider.XRStats;

namespace Rotorcraft.VR
{
    [DefaultExecutionOrder(10000)]   // LateUpdate after anything that sets the camera's culling mask
    public sealed class VrDriver : MonoBehaviour, IVrDriver
    {
        // The panel, laser and dot are on Unity's UI layer, which nothing else in the world uses: the game's menus turn
        // the world off with a culling mask of nothing (on a screen the UI draws on top by itself), and in VR the
        // menus are an object in the world.
        const int PanelLayer = 5;
        // Outside a flight, a head this far (m) from the seat for this long (s) means the seat is wrong.
        const float ReseatDistance = 0.25f, ReseatAfter = 0.4f;
        // How close (m) a hand must be to a control to take that one rather than its own, and how far the grip button must go in and out.
        const float GrabRadius = 0.18f, GripOn = 0.6f, GripOff = 0.35f;
        // How near the switch panel (m) a hand starts to point.
        const float PointWithin = 0.12f;

        sealed class Hand
        {
            public bool left;
            public VrHand view;
            public InputAction position, rotation, grip, haptic;
            public CockpitGrip near, held;
            public HandAxis first, second;
            public Vector3 smoothed;
            public bool squeezed;
            public float curl, pointing;
            public FingerPress press;
        }
        Hand leftHand, rightHand;
        List<CockpitGrip> grips = new List<CockpitGrip>();
        Transform gripsModel;
        VrSwitchPanel switches;
        CockpitSwitch[] switchStates;
        float switchesAt;

        // The panel: wide enough to fill the middle of the view without turning the head for the HUD's edges.
        const float PanelDistance = 2.2f, PanelWidth = 2.6f, PanelDrop = -0.1f;
        const int TextureW = 2048, TextureH = 1152;
        // The UI lays out as if on a screen this size, larger than usual: text must be readable on a headset's pixels.
        static readonly Vector2Int PanelLayout = new Vector2Int(1280, 720);
        const string NoHeadset = "No headset found. Check that Quest Link or Virtual Desktop is connected and is your active OpenXR runtime, then try again.";

        public bool Running { get; private set; }
        public bool Starting { get; private set; }
        public string Problem { get; private set; }
        public IReadOnlyList<float> RefreshRates => HeadsetRate.Rates;

        Camera view;
        Transform parentBefore;
        Transform rig, panel;
        MeshRenderer panelRenderer;
        Material panelMaterial;
        RenderTexture panelTexture;
        PanelSettings panelSettings;
        Vector2Int layoutBefore;
        bool clearBefore;
        Color clearColorBefore;
        UIDocument document;
        TrackedPoseDriver headDriver;
        LineRenderer laser;
        Transform dot;
        Label stats;
        FlightSession flight;

        Vector3 headNeutral;
        float recentreYaw;
        float recentreAt, awayFor;
        bool hudVisible = true, pressing;
        Vector2 lastPoint;
        float fps = 72, statsAt;

        XRDisplaySubsystem display;
        float[] ladder;
        int ladderStep, begunSeen, settingAsked = -1;
        readonly VrFrameWatch watch = new VrFrameWatch();
        readonly List<float> minute = new List<float>(8000);
        float minuteTime, cpuSum, gpuSum;
        int timed;

        InputAction rightStick, leftStick, buttonA, buttonB, buttonX, buttonY, menu, rightClick, leftClick, trigger, aimPosition, aimRotation;
        InputAction[] all;

        void Start()
        {
#if RC_TRAILER
            var trailer = TrailerRun.ArgFolder();
            if (trailer != null) { gameObject.AddComponent<TrailerRun>().folder = trailer; return; }
#endif
            var asked = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-vr") >= 0;
            Debug.Log($"VR: available{(asked ? ", asked for on the command line" : "")}");
            if (asked) Begin();
        }

        // ------------------------------------------------------------------------------------ start and stop
        public void Begin()
        {
            if (Running || Starting) return;
            Problem = null;
            Debug.Log("VR: starting the headset");
            StartCoroutine(StartHeadset());
        }

        IEnumerator StartHeadset()
        {
            Starting = true;
            Vr.RaiseChanged();
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager != null && manager.activeLoader == null) yield return manager.InitializeLoader();
            Starting = false;
            if (manager == null || manager.activeLoader == null)
            {
                Problem = NoHeadset;
                Debug.Log($"VR: could not start ({(manager == null ? "no XR settings in this build" : "no OpenXR runtime or headset")})");
                Vr.RaiseChanged();
                yield break;
            }
            manager.StartSubsystems();
            display = manager.activeLoader.GetLoadedSubsystem<XRDisplaySubsystem>();
            VrGraphics.Apply(true);
            var app = GameApp.Instance;
            Attach(app.flight.cam.GetComponent<Camera>(), app.document, app.flight);
            Debug.Log($"VR: running with {manager.activeLoader.name}");
            Vr.RaiseChanged();
        }

        public void End()
        {
            if (!Running) return;
            Detach();
            VrGraphics.Apply(false);
            display = null;
            ladder = null;
            settingAsked = -1;
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager != null && manager.activeLoader != null)
            {
                manager.StopSubsystems();
                manager.DeinitializeLoader();
            }
            Vr.RaiseChanged();
        }

        void OnDestroy() { if (Running) End(); }

        // -------------------------------------------------------------------------------------- the seat
        /// <summary>Everything but the headset itself: the rig, panel, laser and controls. Public so a test can look at the result without a headset.</summary>
        public void Attach(Camera camera, UIDocument doc, FlightSession session)
        {
            view = camera;
            document = doc;
            flight = session;
            rig = new GameObject("VR rig").transform;
            rig.SetPositionAndRotation(camera.transform.position, Quaternion.Euler(0, camera.transform.eulerAngles.y, 0));
            parentBefore = camera.transform.parent;
            camera.transform.SetParent(rig, false);
            camera.transform.localPosition = Vector3.zero;
            camera.transform.localRotation = Quaternion.identity;
            headNeutral = Vector3.zero;
            recentreYaw = 0;
            headDriver = camera.gameObject.AddComponent<TrackedPoseDriver>();
            headDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            headDriver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            headDriver.ignoreTrackingState = true;
            headDriver.positionInput = new InputActionProperty(new InputAction("Head position", InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            headDriver.rotationInput = new InputActionProperty(new InputAction("Head rotation", InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
            MakePanel();
            MakeLaser();
            MakeControls();
            switches = new VrSwitchPanel(doc.panelSettings.themeStyleSheet, PanelLayer);
            switches.SetVisible(false);
            hudVisible = true;
            pressing = false;
            recentreAt = Time.unscaledTime + 1;   // once the headset has reported where it is
            Running = true;
        }

        void Detach()
        {
            // when the game is quitting any of these may already be gone, and End must still get on to shutting XR down
            Running = false;
            foreach (var a in all) { a.Disable(); a.Dispose(); }
            all = null;
            if (flight != null && flight.heli != null && flight.heli.Input != null) { flight.heli.Input.external = default; flight.heli.Input.held = default; }
            leftHand?.view.Dispose(); rightHand?.view.Dispose();
            leftHand = rightHand = null;
            grips.Clear(); gripsModel = null;
            switches?.Dispose();
            switches = null;
            if (headDriver != null) Destroy(headDriver);
            if (view != null) view.transform.SetParent(parentBefore, true);
            if (stats != null) { stats.RemoveFromHierarchy(); stats = null; }
            if (panelSettings != null)
            {
                panelSettings.SetScreenToPanelSpaceFunction(null);
                panelSettings.targetTexture = null;
                panelSettings.referenceResolution = layoutBefore;
                panelSettings.clearColor = clearBefore;
                panelSettings.colorClearValue = clearColorBefore;
            }
            if (panelTexture != null) { panelTexture.Release(); Destroy(panelTexture); }
            if (panelMaterial != null) Destroy(panelMaterial);
            if (rig != null) Destroy(rig.gameObject);
        }

        public void Recentre()
        {
            if (view == null) return;
            var t = view.transform;
            var ahead = Vector3.ProjectOnPlane(t.localRotation * Vector3.forward, Vector3.up);
            recentreYaw = ahead.sqrMagnitude > 1e-4f ? -Quaternion.LookRotation(ahead).eulerAngles.y : 0;
            headNeutral = t.localPosition;
            // the seat has moved under the hands, so nothing they hold can stay where it was
            if (leftHand != null) leftHand.held = null;
            if (rightHand != null) rightHand.held = null;
        }

        public void Place(Vector3 eye, Quaternion look, bool level)
        {
            if (rig == null) return;
            var facing = level ? Quaternion.Euler(0, look.eulerAngles.y, 0) : look;
            var r = facing * Quaternion.Euler(0, recentreYaw, 0);
            rig.SetPositionAndRotation(eye - r * headNeutral, r);
            panel.SetPositionAndRotation(eye + facing * new Vector3(0, PanelDrop, PanelDistance), facing);
        }

        public bool PanelPoint(Vector3 world, out Vector2 at)
        {
            at = default;
            var ui = document != null ? document.rootVisualElement?.panel : null;
            if (ui == null || panel == null) return false;
            var eye = view.transform.position;
            var ray = new Ray(eye, world - eye);
            if (!new Plane(panel.forward, panel.position).Raycast(ray, out var d)) return false;
            return OnPanel(ui, ray.GetPoint(d), out at);
        }

        /// <summary>A point on the panel's plane in the panel's layout units, from its top left; false if it is off the edge.</summary>
        bool OnPanel(IPanel ui, Vector3 point, out Vector2 at)
        {
            var local = panel.InverseTransformPoint(point);
            var size = ui.visualTree.layout.size;
            at = new Vector2((local.x + 0.5f) * size.x, (0.5f - local.y) * size.y);
            return Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.y) <= 0.5f;
        }

        // ------------------------------------------------------------------------------------- the UI panel
        void MakePanel()
        {
            panelTexture = new RenderTexture(TextureW, TextureH, 24, RenderTextureFormat.ARGB32) { name = "VR panel", useMipMap = true, autoGenerateMips = true, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            panelTexture.Create();
            panelSettings = document.panelSettings;
            layoutBefore = panelSettings.referenceResolution;
            clearBefore = panelSettings.clearColor;
            clearColorBefore = panelSettings.colorClearValue;
            panelSettings.targetTexture = panelTexture;
            panelSettings.referenceResolution = PanelLayout;
            panelSettings.clearColor = true;
            panelSettings.colorClearValue = new Color(0, 0, 0, 0);
            // the desktop mouse still works the panel, from the whole game window
            panelSettings.SetScreenToPanelSpaceFunction(s => new Vector2(s.x / Screen.width * TextureW, (1 - s.y / Screen.height) * TextureH));

            panelMaterial = new Material(Shader.Find("Rotorcraft/VR Panel")) { name = "VR panel", mainTexture = panelTexture };
            var height = PanelWidth * TextureH / TextureW;
            var mesh = new Mesh { name = "VR panel" };
            mesh.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            var go = new GameObject("VR panel") { layer = PanelLayer };
            panel = go.transform;
            panel.SetParent(rig, false);
            panel.localPosition = new Vector3(0, PanelDrop, PanelDistance);
            panel.localScale = new Vector3(PanelWidth, height, 1);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            panelRenderer = go.AddComponent<MeshRenderer>();
            panelRenderer.sharedMaterial = panelMaterial;
            panelRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            panelRenderer.receiveShadows = false;

            if (GameApp.Instance != null && GameApp.Instance.Root != null)
            {
                stats = new Label("VR") { pickingMode = PickingMode.Ignore };
                stats.style.position = Position.Absolute; stats.style.right = 8; stats.style.bottom = 4;
                stats.style.fontSize = 12; stats.style.color = new Color(1, 1, 1, 0.7f);
                GameApp.Instance.Root.Add(stats);
            }
        }

        void MakeLaser()
        {
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var beam = new Material(unlit) { name = "VR laser", renderQueue = 4010 };   // over the panel, which draws over everything
            beam.SetColor("_BaseColor", new Color(0.55f, 0.85f, 1f));
            var go = new GameObject("VR laser") { layer = PanelLayer };
            go.transform.SetParent(rig, false);
            laser = go.AddComponent<LineRenderer>();
            laser.sharedMaterial = beam;
            laser.positionCount = 2;
            laser.useWorldSpace = true;
            laser.startWidth = 0.004f; laser.endWidth = 0.002f;
            laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            laser.receiveShadows = false;
            laser.enabled = false;
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "VR laser dot";
            ball.layer = PanelLayer;
            Destroy(ball.GetComponent<Collider>());
            ball.GetComponent<MeshRenderer>().sharedMaterial = beam;
            ball.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dot = ball.transform;
            dot.SetParent(rig, false);
            dot.localScale = Vector3.one * 0.016f;
            ball.SetActive(false);
        }

        // ----------------------------------------------------------------------------------- the controllers
        static InputAction Stick(string hand) => new InputAction(hand + " stick", InputActionType.Value, $"<XRController>{{{hand}Hand}}/thumbstick", expectedControlType: "Vector2");
        static InputAction Button(string hand, string control) => new InputAction($"{hand} {control}", InputActionType.Button, $"<XRController>{{{hand}Hand}}/{control}");

        void MakeControls()
        {
            rightStick = Stick("Right");
            leftStick = Stick("Left");
            buttonA = Button("Right", "primaryButton");
            buttonB = Button("Right", "secondaryButton");
            buttonX = Button("Left", "primaryButton");
            buttonY = Button("Left", "secondaryButton");
            menu = Button("Left", "menu");
            rightClick = Button("Right", "thumbstickClicked");
            leftClick = Button("Left", "thumbstickClicked");
            trigger = Button("Right", "triggerPressed");
            aimPosition = new InputAction("Aim position", InputActionType.Value, "<XRController>{RightHand}/pointerPosition", expectedControlType: "Vector3");
            aimRotation = new InputAction("Aim rotation", InputActionType.Value, "<XRController>{RightHand}/pointerRotation", expectedControlType: "Quaternion");
            leftHand = MakeHand(true);
            rightHand = MakeHand(false);
            all = new[] { rightStick, leftStick, buttonA, buttonB, buttonX, buttonY, menu, rightClick, leftClick, trigger, aimPosition, aimRotation,
                leftHand.position, leftHand.rotation, leftHand.grip, leftHand.haptic, rightHand.position, rightHand.rotation, rightHand.grip, rightHand.haptic };
            foreach (var a in all) a.Enable();
        }

        Hand MakeHand(bool left)
        {
            var side = left ? "LeftHand" : "RightHand";
            return new Hand
            {
                left = left,
                view = new VrHand(rig, left, PanelLayer),
                press = new FingerPress(CockpitSwitches.Columns, CockpitSwitches.Rows, VrSwitchPanel.Width, VrSwitchPanel.Height),
                position = new InputAction(side + " position", InputActionType.Value, $"<XRController>{{{side}}}/devicePosition", expectedControlType: "Vector3"),
                rotation = new InputAction(side + " rotation", InputActionType.Value, $"<XRController>{{{side}}}/deviceRotation", expectedControlType: "Quaternion"),
                grip = new InputAction(side + " grip", InputActionType.Value, $"<XRController>{{{side}}}/grip", expectedControlType: "Axis"),
                haptic = new InputAction(side + " haptic", InputActionType.PassThrough, $"<XRController>{{{side}}}/haptic"),
            };
        }

        void LateUpdate()
        {
            if (!Running || view == null) return;
            view.cullingMask |= 1 << PanelLayer;
            if (leftHand == null) return;
            // last of all, so the hands sit on the controls where they have just been drawn
            var heli = flight != null ? flight.heli : null;
            var input = heli != null ? heli.Input : null;
            var canHold = flight != null && flight.Active && !flight.Paused && input != null && heli.CockpitView;
            if (heli != null && heli.model != gripsModel)
            {
                gripsModel = heli.model;
                grips = heli.Eye != null ? CockpitGrip.Find(heli.model, heli.transform, heli.Eye.position) : new List<CockpitGrip>();
                Debug.Log($"VR: {grips.Count} controls to hold in this aircraft");
                leftHand.held = rightHand.held = null;
                if (heli.Eye != null) switches.Mount(heli.transform, heli.Eye.position);
            }
            var panelShows = canHold && hudVisible && heli.Eye != null && flight.Runtime != null;
            switches.SetVisible(panelShows);
            var hold = default(HeldControls);
            UpdateHand(leftHand, rightHand, canHold, heli, input, ref hold);
            UpdateHand(rightHand, leftHand, canHold, heli, input, ref hold);
            if (input != null) input.held = hold;
            if (panelShows) DrawSwitches(heli, input);
        }

        /// <summary>A free hand's index finger against the switch panel: it folds its other fingers as it comes near, and a touch is a press.</summary>
        void TouchSwitches(Hand hand, HelicopterController heli, InputManager input)
        {
            var wanted = 0f;
            if (hand.squeezed || !switches.Visible) hand.press.Release();
            else
            {
                var at = switches.root.InverseTransformPoint(hand.view.IndexTip);
                var pressed = hand.press.Update(at.x, at.y, -at.z);
                // the panel's face looks back along its own -z, at the pilot
                if (-at.z < PointWithin && -at.z > -PointWithin && Mathf.Abs(at.x) < VrSwitchPanel.Width / 2 + PointWithin && Mathf.Abs(at.y) < VrSwitchPanel.Height / 2 + PointWithin) wanted = 1;
                if (pressed >= 0)
                {
                    var sw = Current(heli, input)[pressed];
                    Debug.Log($"VR: {(hand.left ? "left" : "right")} finger pressed {(sw.available ? sw.label : "a blank switch")}");
                    Buzz(hand, sw.available ? 0.6f : 0.1f, sw.available ? 0.05f : 0.02f);
                    if (sw.available) input.Fire(sw.action);
                    switchesAt = 0;
                }
            }
            hand.pointing = Mathf.MoveTowards(hand.pointing, wanted, Time.unscaledDeltaTime * 8);
        }

        CockpitSwitch[] Current(HelicopterController heli, InputManager input) =>
            CockpitSwitches.Build(heli.State, heli.Aircraft, flight.Runtime.ap, input.Trimmed);

        void DrawSwitches(HelicopterController heli, InputManager input)
        {
            if (flight.Runtime == null) return;
            // the states are looked at a few times a second, unless something has just been pressed
            if (switchStates == null || Time.unscaledTime >= switchesAt)
            {
                switchesAt = Time.unscaledTime + 0.1f;
                switchStates = Current(heli, input);
            }
            switches.Draw(switchStates, leftHand.press.Hover, rightHand.press.Hover);
        }

        void UpdateHand(Hand hand, Hand other, bool canHold, HelicopterController heli, InputManager input, ref HeldControls hold)
        {
            var local = hand.position.ReadValue<Vector3>();
            var turn = hand.rotation.ReadValue<Quaternion>();
            var tracked = turn.x != 0 || turn.y != 0 || turn.z != 0 || turn.w != 0;   // all zero: asleep or out of sight
            hand.view.SetVisible(tracked);
            if (!tracked) { hand.held = null; hand.near = null; hand.view.SetReady(false); hand.press.Release(); return; }
            turn = Quaternion.Normalize(turn);

            var squeeze = hand.grip.ReadValue<float>();
            var was = hand.squeezed;
            hand.squeezed = squeeze > (was ? GripOff : GripOn);
            if (hand.held != null && (!hand.squeezed || !canHold)) { hand.held = null; Buzz(hand, 0.2f, 0.03f); }

            var world = rig.TransformPoint(local);
            if (hand.held == null)
            {
                var nearest = canHold ? CockpitGrip.Within(grips, world, GrabRadius, other.held) : null;
                if (nearest != null && hand.near != nearest) Buzz(hand, 0.15f, 0.02f);
                hand.near = nearest;
                if (hand.squeezed && !was)
                {
                    var take = canHold ? CockpitGrip.ToTake(grips, world, hand.left, GrabRadius, other.held) : null;
                    Debug.Log($"VR: {(hand.left ? "left" : "right")} grip squeezed: {(take == null ? $"nothing to take (can hold {canHold}, {grips.Count} controls)" : take.cyclic ? "took the cyclic" : "took the collective")}");
                    if (take != null) TakeHold(hand, take, input, local);
                }
            }
            hand.view.SetReady(hand.held == null && hand.near != null);

            hand.curl = Mathf.MoveTowards(hand.curl, hand.held != null ? 1 : Mathf.Max(squeeze, hand.near != null ? 0.25f : 0), Time.unscaledDeltaTime * 8);
            if (hand.held != null) hand.pointing = 0;
            hand.view.SetCurl(hand.curl, hand.pointing);
            if (hand.held == null)
            {
                hand.view.root.localPosition = local;
                hand.view.root.localRotation = turn * VrHand.FromGripPose;
                TouchSwitches(hand, heli, input);
                return;
            }
            hand.press.Release();
            // the control follows the hand, measured in the aircraft (the seat moves and tilts with it); a little
            // smoothing takes out the tremor of the tracking, and the hand is drawn on the control itself
            hand.smoothed = Vector3.Lerp(hand.smoothed, SeatVector(local), 1 - Mathf.Exp(-Time.unscaledDeltaTime * 40));
            if (hand.held.cyclic) { hold.cyclic = true; hold.cyclicX = hand.first.Update(hand.smoothed.x); hold.cyclicY = hand.second.Update(hand.smoothed.z); }
            else { hold.lever = true; hold.leverPosition = hand.first.Update(hand.smoothed.y); }
            hand.view.root.SetPositionAndRotation(hand.held.Anchor, hand.held.HandRotation(heli.transform, hand.left));
        }

        void TakeHold(Hand hand, CockpitGrip grip, InputManager input, Vector3 local)
        {
            hand.held = grip;
            hand.near = null;
            hand.smoothed = SeatVector(local);
            if (grip.cyclic)
            {
                hand.first = new HandAxis(input.CyclicX, -1, 1, HandAxis.CyclicTravel, hand.smoothed.x);
                hand.second = new HandAxis(input.CyclicY, -1, 1, HandAxis.CyclicTravel, hand.smoothed.z);
            }
            else hand.first = new HandAxis(input.CollectiveLever, 0, 1, HandAxis.LeverTravel, hand.smoothed.y);
            Buzz(hand, 0.4f, 0.05f);
        }

        /// <summary>A position from the head tracking space, with its axes along the aircraft's: x right, y up, z forward.</summary>
        Vector3 SeatVector(Vector3 local) => Quaternion.Euler(0, recentreYaw, 0) * local;

        static void Buzz(Hand hand, float strength, float seconds) => OpenXRInput.SendHapticImpulse(hand.haptic, strength, seconds);

        void Update()
        {
            if (!Running || all == null) return;
            if (recentreAt > 0 && Time.unscaledTime >= recentreAt) { recentreAt = 0; Recentre(); }
            if (rightClick.WasPressedThisFrame()) Recentre();

            var input = flight != null && flight.heli != null ? flight.heli.Input : null;
            var inFlight = flight != null && flight.Active && input != null;
            var flying = inFlight && !flight.Paused;
            // Nothing else seats the player at the menus, and the first recentring may have come before the headset
            // reported where it is, or before it was put on: left there, the menus would be down by the floor.
            if (!inFlight && view != null && (view.transform.localPosition - headNeutral).magnitude > ReseatDistance)
            {
                awayFor += Time.unscaledDeltaTime;
                if (awayFor >= ReseatAfter) { awayFor = 0; Recentre(); }
            }
            else awayFor = 0;
            var right = rightStick.ReadValue<Vector2>();
            var left = leftStick.ReadValue<Vector2>();
            // a thumbstick works as it always has until its hand is holding something, and then it is only for the pedals
            var rightBusy = rightHand.held != null;
            var leftBusy = leftHand.held != null;
            var cyclicHeld = (rightHand.held?.cyclic ?? false) || (leftHand.held?.cyclic ?? false);
            var leverHeld = (rightHand.held != null && !rightHand.held.cyclic) || (leftHand.held != null && !leftHand.held.cyclic);
            var thumbCyclic = cyclicHeld || rightBusy ? Vector2.zero : right;
            var thumbLever = leverHeld || leftBusy ? 0 : left.y;
            var pedals = rightBusy && Mathf.Abs(right.x) > Mathf.Abs(left.x) ? right.x : left.x;
            if (input != null) input.external = flying ? new ExternalSticks { cyclicX = thumbCyclic.x, cyclicY = thumbCyclic.y, pedals = pedals, collective = thumbLever } : default;
            if (flying && !input.menuOpen)
            {
                if (buttonA.WasPressedThisFrame()) input.Fire(GameAction.Trim);
                if (buttonB.WasPressedThisFrame()) input.Fire(GameAction.TrimRelease);
                if (leftClick.WasPressedThisFrame()) input.Fire(GameAction.Camera);
            }
            if (inFlight)
            {
                if (buttonY.WasPressedThisFrame()) input.Fire(GameAction.Tablet);
                if (menu.WasPressedThisFrame()) flight.TogglePause();
                if (buttonX.WasPressedThisFrame()) hudVisible = !hudVisible;
            }
            // at the menus nothing stands between the seat and the panel, so a hand reaching towards it stays in front of it
            panelMaterial.SetFloat("_ZTest", (float)(inFlight ? UnityEngine.Rendering.CompareFunction.Always : UnityEngine.Rendering.CompareFunction.LessEqual));
            // the panel is always there when something on it needs an answer
            panelRenderer.enabled = hudVisible || !inFlight || flight.Paused || input.menuOpen;
            UpdatePointer(flying ? 0 : right.y);
            UpdateRate(flying);
            UpdateStats(flying);
        }

        /// <summary>Ask the headset for a rate when it starts showing the game or the setting changes, and step down if frames keep coming late.</summary>
        void UpdateRate(bool flying)
        {
            var setting = GameApp.Instance != null ? GameApp.Instance.Settings.vrRefreshHz : 0;
            if (begunSeen != HeadsetRate.Begun || setting != settingAsked)
            {
                begunSeen = HeadsetRate.Begun;
                settingAsked = setting;
                ladder = VrRates.Ladder(HeadsetRate.Rates, setting);
                ladderStep = 0;
                watch.Reset();
                if (ladder.Length > 0 && begunSeen > 0) Ask();
                return;
            }
            if (!flying || ladder == null || ladderStep >= ladder.Length - 1) { watch.Reset(); return; }
            if (watch.Tick(Time.unscaledDeltaTime, ladder[ladderStep]))
            {
                Debug.Log($"VR: too many frames late at {ladder[ladderStep]} Hz");
                ladderStep++;
                Ask();
            }
        }

        void Ask()
        {
            if (!HeadsetRate.Request(ladder[ladderStep])) ladder = null;
        }

        void UpdatePointer(float scroll)
        {
            var ui = document != null ? document.rootVisualElement?.panel : null;
            var from = rig.TransformPoint(aimPosition.ReadValue<Vector3>());
            var aim = aimRotation.ReadValue<Quaternion>();
            var tracked = aim.x != 0 || aim.y != 0 || aim.z != 0 || aim.w != 0;   // all zero: the controller is asleep or out of sight
            var ray = new Ray(from, rig.rotation * aim * Vector3.forward);
            var on = false;
            var hit = Vector3.zero;
            if (tracked && rightHand.held == null && ui != null && panelRenderer.enabled && new Plane(panel.forward, panel.position).Raycast(ray, out var d) && d > 0)
            {
                hit = ray.GetPoint(d);
                if (OnPanel(ui, hit, out var point))
                {
                    on = true;
                    lastPoint = point;
                    PanelPointer.Move(ui, lastPoint);
                    if (trigger.WasPressedThisFrame()) { PanelPointer.Down(ui, lastPoint); pressing = true; }
                    if (Mathf.Abs(scroll) > 0.2f) PanelPointer.Scroll(ui, lastPoint, -scroll * Time.unscaledDeltaTime * 30);
                }
            }
            if (pressing && trigger.WasReleasedThisFrame()) { pressing = false; if (ui != null) PanelPointer.Up(ui, lastPoint); }
            laser.enabled = on;
            dot.gameObject.SetActive(on);
            if (on)
            {
                laser.SetPosition(0, from);
                laser.SetPosition(1, hit);
                dot.position = hit;
            }
        }

        /// <summary>
        /// The frame rate in the corner of the panel, so a slow view can be told from a problem with the headset; and each
        /// minute of flying in the log, from the headset's own timings where it gives them.
        /// </summary>
        void UpdateStats(bool flying)
        {
            var dt = Time.unscaledDeltaTime;
            fps = Mathf.Lerp(fps, 1 / Mathf.Max(dt, 0.001f), 0.05f);
            var hz = 0f;
            var appCpu = 0f; var appGpu = 0f;
            if (display != null)
            {
                display.TryGetDisplayRefreshRate(out hz);
                ProviderStats.TryGetStat(display, "perfmetrics.appcputime", out appCpu);
                ProviderStats.TryGetStat(display, "perfmetrics.appgputime", out appGpu);
            }
            if (flying)
            {
                minute.Add(dt);
                minuteTime += dt;
                if (appCpu > 0) { cpuSum += appCpu; gpuSum += appGpu; timed++; }
                if (minuteTime >= 60) LogMinute(hz);
            }
            if (stats == null || Time.unscaledTime < statsAt) return;
            statsAt = Time.unscaledTime + 0.5f;
            stats.text = $"VR {fps:0} fps{(hz > 0 ? $" at {hz:0} Hz" : "")}{(appCpu > 0 ? $" · cpu {appCpu:0.0} gpu {appGpu:0.0} ms" : "")}";
        }

        void LogMinute(float hz)
        {
            minute.Sort();
            var late = hz > 0 ? minute.FindAll(d => d > 1.4f / hz).Count * 100 / minute.Count : 0;
            Debug.Log($"VR: minute of flying: {minute.Count / minuteTime:0} fps at {hz:0} Hz, frames {minute[minute.Count / 2] * 1000:0.0} ms, slowest 5% over {minute[minute.Count * 95 / 100] * 1000:0.0} ms, {late}% late"
                + (timed > 0 ? $", headset's timing cpu {cpuSum / timed:0.0} gpu {gpuSum / timed:0.0} ms" : ""));
            minute.Clear();
            minuteTime = cpuSum = gpuSum = 0;
            timed = 0;
        }
    }
}
