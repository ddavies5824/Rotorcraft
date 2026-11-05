// The controls a hand can take hold of in the pilot's seat: the cyclic and the collective, found by name in the model.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Rotorcraft.VR
{
    public sealed class CockpitGrip
    {
        public readonly bool cyclic;
        readonly Transform part;
        readonly Renderer grip;

        CockpitGrip(bool cyclic, Transform part, Renderer grip) { this.cyclic = cyclic; this.part = part; this.grip = grip; }

        /// <summary>The centre of what the hand closes round, as the control stands now.</summary>
        public Vector3 Anchor => grip.bounds.center;

        /// <summary>How a hand holds it: the fist round the line from the control's pivot to its grip, the left hand mirrored.</summary>
        public Quaternion HandRotation(Transform aircraft, bool left)
        {
            var along = (Anchor - part.position).normalized;
            return cyclic
                ? Quaternion.LookRotation(along, aircraft.forward)                       // upright, knuckles forward
                : Quaternion.LookRotation(along, left ? -aircraft.right : aircraft.right);   // along the lever, back of the hand up
        }

        /// <summary>
        /// What a hand takes when its grip button is squeezed: a control it is reaching for if there is one within reach,
        /// or else its own (right hand cyclic, left hand collective) from wherever it is, so nobody has to bend down into
        /// the footwell to fly. Null if the other hand already has it.
        /// </summary>
        public static CockpitGrip ToTake(List<CockpitGrip> grips, Vector3 hand, bool left, float reach, CockpitGrip otherHolds)
        {
            return Within(grips, hand, reach, otherHolds) ?? grips.FirstOrDefault(g => g.cyclic != left && g != otherHolds);
        }

        /// <summary>The nearest control within reach of a hand that the other hand is not holding.</summary>
        public static CockpitGrip Within(List<CockpitGrip> grips, Vector3 hand, float reach, CockpitGrip otherHolds)
        {
            CockpitGrip nearest = null;
            var best = reach;
            foreach (var g in grips)
            {
                if (g == otherHolds) continue;
                var d = Vector3.Distance(hand, g.Anchor);
                if (d < best) { best = d; nearest = g; }
            }
            return nearest;
        }

        /// <summary>The cyclic and collective at the seat nearest the eye; empty if the model has none.</summary>
        public static List<CockpitGrip> Find(Transform model, Transform aircraft, Vector3 eye)
        {
            var found = new List<CockpitGrip>();
            if (model == null) return found;
            var side = aircraft.InverseTransformPoint(eye).x >= 0 ? "_r" : "_l";
            var all = model.GetComponentsInChildren<Transform>(true);
            void Add(string partName, bool cyclic, params string[] gripNames)
            {
                var part = all.FirstOrDefault(t => t.name == partName + side || t.name.StartsWith(partName + side + "."));
                if (part == null) return;
                var grip = part.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => gripNames.Any(r.name.StartsWith));
                if (grip != null) found.Add(new CockpitGrip(cyclic, part, grip));
            }
            Add("ctl_cyclic", true, "Cyclic grip");
            Add("ctl_collective", false, "Throttle grip", "Collective head", "Collective switch head");
            return found;
        }
    }
}
