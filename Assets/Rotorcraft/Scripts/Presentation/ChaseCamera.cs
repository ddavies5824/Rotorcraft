// The flight camera: chase (behind the aircraft, following the heading but not the bank, so the horizon stays
// level), cockpit (the pilot's eye: right mouse drag, the hat switch or the keypad look round; double-click looks
// ahead) and orbit (round the aircraft: drag to turn, wheel to zoom; used for the pre-flight walk-round). With no
// flight running it slowly circles the heliport behind the menus. A little shake through translational lift,
// vortex ring and blade stall.
using Rotorcraft.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rotorcraft.Presentation
{
    [RequireComponent(typeof(Camera))]
    public sealed class ChaseCamera : MonoBehaviour
    {
        public HelicopterController target;
        public float distance = 13f;
        public float height = 3.5f;
        public float lookHeight = 1.6f;
        [Tooltip("Seconds for the camera to settle behind the aircraft.")]
        public float lag = 0.35f;
        public float chaseFov = 55f, cockpitFov = 72f, orbitFov = 50f;
        [Tooltip("Head turn at full hat deflection, degrees (yaw, pitch).")]
        public Vector2 lookRange = new Vector2(100f, 40f);
        public const float DefaultLookPitch = -8f;

        /// <summary>Behind the menus: circle the heliport slowly.</summary>
        public bool menuBackdrop;
        /// <summary>Orbit camera (degrees and metres), set by the walk-round as well as by the mouse.</summary>
        public float orbitYaw = 50, orbitPitch = 7, orbitDist = 12;
        /// <summary>Head position in the cockpit (degrees): the mouse adds to it; scripts may drive it.</summary>
        public float lookYaw, lookPitch = DefaultLookPitch;
        /// <summary>Scripted look (the pre-flight lookout): overrides the head turn while set.</summary>
        public float? scriptedLookYaw;
        public float shake;

        Camera cam;
        Vector3 velocity;
        Vector2 hat;
        float lastClick, menuT;
        Vector3? lastMouse;

        void Awake() => cam = GetComponent<Camera>();

        /// <summary>Is the mouse looking round (or the head turned away from straight ahead)?</summary>
        public bool LookingAway => Mathf.Abs(lookYaw) > 3 || Mathf.Abs(lookPitch - DefaultLookPitch) > 3;
        public void LookAhead() { lookYaw = 0; lookPitch = DefaultLookPitch; }

        void LateUpdate()
        {
            if (target == null) return;
            if (menuBackdrop) { Backdrop(Time.unscaledDeltaTime); return; }
            MouseInput();
            switch (target.Camera)
            {
                case CameraMode.Cockpit when target.Eye != null: Cockpit(); break;
                case CameraMode.Orbit: Orbit(); break;
                default: Chase(Time.deltaTime); break;
            }
            if (shake > 0.01f && !target.Paused)
            {
                // a gentle vibration of the view direction, as in the browser game: a fifth of a degree at most and
                // low frequencies only (one or two a second), so it never aliases against the frame rate
                var t = Time.time;
                var a = Mathf.Min(1, shake) * 0.0035f * Mathf.Rad2Deg;
                float n1 = Mathf.Sin(t * 7.3f) * 0.6f + Mathf.Sin(t * 11.9f + 1.3f) * 0.4f, n2 = Mathf.Sin(t * 8.7f + 2.1f) * 0.6f + Mathf.Sin(t * 13.1f) * 0.4f;
                transform.rotation *= Quaternion.Euler(n2 * a, n1 * a * 0.7f, 0);
            }
        }

        void MouseInput()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            // UI clicks are taken by the UI; the camera uses the right button (and the left in orbit view)
            var pos = (Vector3)mouse.position.ReadValue();
            var dragging = mouse.rightButton.isPressed || (target.Camera == CameraMode.Orbit && mouse.leftButton.isPressed && !OverUi(pos));
            if (dragging && lastMouse != null)
            {
                var d = pos - lastMouse.Value;
                if (target.Camera == CameraMode.Cockpit) { lookYaw = Mathf.Clamp(lookYaw + d.x * 0.22f, -150, 150); lookPitch = Mathf.Clamp(lookPitch + d.y * 0.18f, -70, 50); }
                else if (target.Camera == CameraMode.Orbit) { orbitYaw += d.x * 0.35f; orbitPitch = Mathf.Clamp(orbitPitch - d.y * 0.3f, -10, 75); }
            }
            lastMouse = dragging ? pos : (Vector3?)null;
            var wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f && !OverUi(pos))
            {
                if (target.Camera == CameraMode.Orbit) orbitDist = Mathf.Clamp(orbitDist * (wheel < 0 ? 1.1f : 0.9f), 5, 400);
                else if (target.Camera == CameraMode.Chase) distance = Mathf.Clamp(distance * (wheel < 0 ? 1.08f : 0.92f), 6, 60);
            }
            if (target.Camera == CameraMode.Cockpit && mouse.rightButton.wasPressedThisFrame)
            {
                if (Time.unscaledTime - lastClick < 0.32f) LookAhead();
                lastClick = Time.unscaledTime;
            }
        }

        /// <summary>Is the pointer over a UI element that takes clicks (so the camera should leave it alone)?</summary>
        public static System.Func<Vector2, bool> PointerOverUi;
        static bool OverUi(Vector2 screenPos) => PointerOverUi != null && PointerOverUi(screenPos);

        float ModelScale => target.Aircraft != null ? (float)(target.Aircraft.rotorDiameter / 7.7) : 1;

        void Chase(float dt)
        {
            cam.fieldOfView = chaseFov;
            cam.nearClipPlane = 0.3f;
            var t = target.transform;
            var k = Mathf.Max(1, ModelScale * 0.85f);
            var fwd = Vector3.ProjectOnPlane(t.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(t.up, Vector3.up);
            fwd.Normalize();
            var want = t.position - fwd * distance * k + Vector3.up * height * k;
            transform.position = dt <= 0 ? want : Vector3.SmoothDamp(transform.position, want, ref velocity, lag);
            transform.rotation = Quaternion.LookRotation(t.position + Vector3.up * lookHeight * k - transform.position, Vector3.up);
        }

        void Cockpit()
        {
            cam.fieldOfView = cockpitFov;
            cam.nearClipPlane = 0.03f;
            var l = target.Input.Look;
            hat = Vector2.Lerp(hat, new Vector2((float)l.x, (float)l.y), 1 - Mathf.Exp(-8f * Time.deltaTime));
            var yaw = scriptedLookYaw ?? lookYaw + hat.x * lookRange.x;
            var pitch = lookPitch + hat.y * lookRange.y;
            var t = target.transform;
            transform.position = target.Eye.position;
            transform.rotation = t.rotation * Quaternion.Euler(-pitch, yaw, 0);
            velocity = Vector3.zero;
        }

        void Orbit()
        {
            cam.fieldOfView = orbitFov;
            cam.nearClipPlane = 0.1f;
            var t = target.transform;
            var centre = t.position + Vector3.up * 1.4f * ModelScale;
            var rot = Quaternion.Euler(orbitPitch, t.eulerAngles.y + orbitYaw, 0);
            var pos = centre - rot * Vector3.forward * orbitDist * ModelScale;
            // never below the ground
            var g = WorldGroundY(pos);
            if (pos.y < g + 0.5f) pos.y = g + 0.5f;
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(centre - pos, Vector3.up);
            velocity = Vector3.zero;
        }

        static float WorldGroundY(Vector3 p)
        {
            var sim = SimToUnity.ToSim(p);
            return (float)Ground.Current.GroundAt(sim.x, sim.z).h;
        }

        void Backdrop(float dt)
        {
            menuT += dt;
            cam.fieldOfView = 50;
            cam.nearClipPlane = 0.3f;
            var c = SimToUnity.Position(new Vec3(-150, WorldGroundY(SimToUnity.Position(new Vec3(-150, 0, -60))) + 10, -60));
            var a = menuT * 0.03f;
            var pos = c + new Vector3(Mathf.Sin(a) * 520, 140 + Mathf.Sin(menuT * 0.07f) * 30, Mathf.Cos(a) * 520);
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(c - pos + Vector3.up * 40, Vector3.up);
        }

        /// <summary>Jump straight to the chase position (for scene setup and resets).</summary>
        public void Snap()
        {
            if (cam == null) cam = GetComponent<Camera>();
            Chase(0);
        }
    }
}
