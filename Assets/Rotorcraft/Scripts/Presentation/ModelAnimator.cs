// Moving parts of the aircraft model: the cockpit controls follow the pilot's inputs (cyclic, collective with
// its twist grip, pedals) and the doors open and close smoothly (hinged crew doors swing, cabin doors slide out
// and back along their rails). The parts are the pivoted groups the Blender script exports (ctl_cyclic_l,
// door_crew_r, door_cabin_l, door_rear_l...), with their hinge axes and travel taken from the model metadata.
// Model axes are the game's (x right, y up, z aft); glTFast mirrors x on import, which the conversions undo.
using System;
using System.Collections.Generic;
using System.Linq;
using Rotorcraft.Sim;
using UnityEngine;

namespace Rotorcraft.Presentation
{
    public sealed class ModelAnimator : MonoBehaviour
    {
        public HelicopterController heli;

        enum DoorKind { Hinge, Slide }
        sealed class Door { public Transform t; public string group; public DoorKind kind; public float open; public Vector3 outV, moveV; public Quaternion rest; public Vector3 restPos; public float pos, target; }
        sealed class Ctl { public Transform t; public string kind; public Quaternion rest; public Vector3 restPos; }

        readonly List<Door> doors = new List<Door>();
        readonly List<Ctl> controls = new List<Ctl>();
        Transform model;
        /// <summary>Door groups held open by the game (boarding, loading, the winch), as well as the player's toggle.</summary>
        readonly HashSet<string> held = new HashSet<string>();
        bool playerOpen;
        public event Action<bool> DoorMoved;   // true = opening (for a sound)

        /// <summary>Hinge angle (radians, + = outwards on the right) and slide travel per aircraft, from the Blender metadata.</summary>
        static readonly Dictionary<string, (string part, string group, DoorKind kind, float open, Vector3 outV, Vector3 move)[]> DOORS = new Dictionary<string, (string, string, DoorKind, float, Vector3, Vector3)[]>
        {
            ["kestrel"] = new[] { ("door_l", "crew_l", DoorKind.Hinge, -1.25f, Vector3.zero, Vector3.zero), ("door_r", "crew_r", DoorKind.Hinge, 1.25f, Vector3.zero, Vector3.zero) },
            ["northstar"] = new[]
            {
                ("door_crew_l", "crew_l", DoorKind.Hinge, -1.3f, Vector3.zero, Vector3.zero), ("door_crew_r", "crew_r", DoorKind.Hinge, 1.3f, Vector3.zero, Vector3.zero),
                ("door_cabin_l", "cabin_l", DoorKind.Slide, 0, new Vector3(-0.07f, 0, 0), new Vector3(0, 0, 0.75f)), ("door_cabin_r", "cabin_r", DoorKind.Slide, 0, new Vector3(0.07f, 0, 0), new Vector3(0, 0, 0.75f)),
            },
            ["guardian"] = new[]
            {
                ("door_crew_l", "crew_l", DoorKind.Hinge, -1.3f, Vector3.zero, Vector3.zero), ("door_crew_r", "crew_r", DoorKind.Hinge, 1.3f, Vector3.zero, Vector3.zero),
                ("door_cabin_l", "cabin_l", DoorKind.Slide, 0, new Vector3(-0.07f, 0, 0), new Vector3(0, 0, 1.45f)), ("door_cabin_r", "cabin_r", DoorKind.Slide, 0, new Vector3(0.07f, 0, 0), new Vector3(0, 0, 1.45f)),
            },
            ["nightjar"] = new[]
            {
                ("door_crew_l", "crew_l", DoorKind.Hinge, -1.25f, Vector3.zero, Vector3.zero), ("door_crew_r", "crew_r", DoorKind.Hinge, 1.25f, Vector3.zero, Vector3.zero),
                ("door_cabin_l", "cabin_l", DoorKind.Slide, 0, new Vector3(-0.075f, 0, 0), new Vector3(0, 0, 1.0f)), ("door_cabin_r", "cabin_r", DoorKind.Slide, 0, new Vector3(0.075f, 0, 0), new Vector3(0, 0, 1.0f)),
                ("door_rear_l", "rear", DoorKind.Hinge, -1.9f, Vector3.zero, Vector3.zero), ("door_rear_r", "rear", DoorKind.Hinge, 1.9f, Vector3.zero, Vector3.zero),
            },
        };

        void Awake() { if (heli == null) heli = GetComponent<HelicopterController>(); }

        /// <summary>Find the moving parts of the current model.</summary>
        public void Bind(Transform modelRoot, string aircraftId)
        {
            model = modelRoot;
            doors.Clear(); controls.Clear(); held.Clear(); playerOpen = false;
            if (model == null) return;
            var all = model.GetComponentsInChildren<Transform>(true);
            Transform Find(string name) => all.FirstOrDefault(t => t.name == name) ?? all.FirstOrDefault(t => t.name.StartsWith(name + "."));
            if (DOORS.TryGetValue(aircraftId, out var list))
                foreach (var (part, group, kind, open, outV, move) in list)
                {
                    var t = Find(part);
                    if (t == null) continue;
                    // the mirror on import: x flips, and rotations about y and z turn the other way
                    doors.Add(new Door { t = t, group = group, kind = kind, open = -open * Mathf.Rad2Deg, outV = new Vector3(-outV.x, outV.y, outV.z), moveV = new Vector3(-move.x, move.y, move.z), rest = t.localRotation, restPos = t.localPosition });
                }
            foreach (var t in all.Where(t => t.name.StartsWith("ctl_")))
            {
                var kind = t.name.Split('_')[1];
                controls.Add(new Ctl { t = t, kind = kind, rest = t.localRotation, restPos = t.localPosition });
            }
        }

        public bool AnyOpen => doors.Any(d => d.pos > 0.02f);
        public IEnumerable<string> Groups => doors.Select(d => d.group).Distinct();

        /// <summary>Hold a door group open (or let it close).</summary>
        public void Hold(string group, bool open) { if (open) held.Add(group); else held.Remove(group); }
        public void HoldAll(string prefix, bool open) { foreach (var g in Groups.Where(g => g.StartsWith(prefix)).ToList()) Hold(g, open); }
        /// <summary>The player's door key: open or shut every door (on the ground only).</summary>
        public bool TogglePlayer()
        {
            if (heli != null && !heli.State.onGround) return false;
            playerOpen = !playerOpen;
            return true;
        }

        static float Ease(float x) => x * x * (3 - 2 * x);

        void LateUpdate()
        {
            if (model == null || heli == null || heli.State == null) return;
            var dt = Time.deltaTime;
            // doors: a hinged door takes about 1.2 s to swing, a sliding door 1.8 s to run back
            var airborne = !heli.State.onGround;
            foreach (var d in doors)
            {
                var want = (playerOpen && !airborne) || held.Contains(d.group) ? 1f : 0f;
                if (playerOpen && airborne) playerOpen = false;
                if (want != d.target) { d.target = want; DoorMoved?.Invoke(want > 0); }
                var speed = d.kind == DoorKind.Hinge ? 1 / 1.2f : 1 / 1.8f;
                d.pos = Mathf.MoveTowards(d.pos, d.target, speed * dt);
                var e = Ease(d.pos);
                if (d.kind == DoorKind.Hinge) d.t.localRotation = d.rest * Quaternion.AngleAxis(d.open * e, Vector3.up);
                else
                {
                    // out first (the first fifth of the travel), then back along the rail
                    var outK = Mathf.Clamp01(d.pos / 0.2f); var slideK = Mathf.Clamp01((d.pos - 0.2f) / 0.8f);
                    d.t.localPosition = d.restPos + d.outV * Ease(outK) + d.moveV * Ease(slideK);
                }
            }
            // controls follow the pilot's hands (the actual inputs, smoothed a little as real linkages are)
            var c = heli.CurrentControls;
            var k = 1 - Mathf.Exp(-14 * dt);
            foreach (var ctl in controls)
            {
                switch (ctl.kind)
                {
                    case "cyclic":
                        // forward stick tips the top forward (game -z): rotation about +x is negative; right stick about z (mirrored)
                        var want = ctl.rest * Quaternion.Euler((float)-c.cyclicPitch * 11f, 0, (float)c.cyclicRoll * 10f);
                        ctl.t.localRotation = Quaternion.Slerp(ctl.t.localRotation, want, k);
                        break;
                    case "collective":
                        ctl.t.localRotation = Quaternion.Slerp(ctl.t.localRotation, ctl.rest * Quaternion.Euler((float)c.collective * 18f, 0, 0), k);
                        break;
                    case "pedalL":
                        ctl.t.localPosition = Vector3.Lerp(ctl.t.localPosition, ctl.restPos + new Vector3(0, 0, (float)c.pedals * 0.06f), k);
                        break;
                    case "pedalR":
                        ctl.t.localPosition = Vector3.Lerp(ctl.t.localPosition, ctl.restPos + new Vector3(0, 0, (float)-c.pedals * 0.06f), k);
                        break;
                }
            }
        }
    }
}
