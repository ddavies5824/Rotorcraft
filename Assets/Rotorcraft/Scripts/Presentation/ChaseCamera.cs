// Chase camera behind the aircraft (it follows the heading, not the bank, so the horizon stays level), or the
// pilot's eye in the cockpit, where the hat switch or keypad looks around.
using UnityEngine;

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
        public float chaseFov = 55f, cockpitFov = 72f;
        [Tooltip("Head turn at full hat deflection, degrees (yaw, pitch).")]
        public Vector2 lookRange = new Vector2(100f, 40f);

        Camera cam;
        Vector3 velocity;
        Vector2 look;

        void Awake() => cam = GetComponent<Camera>();

        void LateUpdate()
        {
            if (target == null) return;
            if (target.CockpitView && target.Eye != null) Cockpit();
            else Chase(Time.deltaTime);
        }

        void Chase(float dt)
        {
            cam.fieldOfView = chaseFov;
            cam.nearClipPlane = 0.3f;
            var t = target.transform;
            var fwd = Vector3.ProjectOnPlane(t.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(t.up, Vector3.up);
            fwd.Normalize();
            var want = t.position - fwd * distance + Vector3.up * height;
            transform.position = dt <= 0 ? want : Vector3.SmoothDamp(transform.position, want, ref velocity, lag);
            transform.rotation = Quaternion.LookRotation(t.position + Vector3.up * lookHeight - transform.position, Vector3.up);
        }

        void Cockpit()
        {
            cam.fieldOfView = cockpitFov;
            cam.nearClipPlane = 0.03f;
            var l = target.Input.Look;
            look = Vector2.Lerp(look, new Vector2((float)l.x, (float)l.y), 1 - Mathf.Exp(-8f * Time.deltaTime));
            var t = target.transform;
            transform.position = target.Eye.position;
            transform.rotation = t.rotation * Quaternion.Euler(-look.y * lookRange.y, look.x * lookRange.x, 0) * Quaternion.Euler(8f, 0, 0); // eyes a little down, at the panel and the horizon
            velocity = Vector3.zero;
        }

        /// <summary>Jump straight to the chase position (for scene setup and resets).</summary>
        public void Snap()
        {
            if (cam == null) cam = GetComponent<Camera>();
            Chase(0);
        }
    }
}
