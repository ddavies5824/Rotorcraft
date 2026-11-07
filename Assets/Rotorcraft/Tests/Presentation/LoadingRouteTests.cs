using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Rotorcraft.Sim;
using UnityEngine;

namespace Rotorcraft.Presentation.Tests
{
    public class LoadingRouteTests
    {
        /// <summary>
        /// From anywhere round the aircraft, the way a stretcher team or passengers take to the door: never along or under the tail
        /// boom, never through the fuselage, and the last stretch in from the side, abeam the door.
        /// </summary>
        [Test]
        public void EveryoneComesInFromTheSideAndNeverPastTheTail()
        {
            var go = new GameObject("aircraft");
            try
            {
                var t = go.transform;
                t.SetPositionAndRotation(new Vector3(30, 0, -40), Quaternion.Euler(0, 35, 0));
                var doors = new (string id, Vector3 local)[]
                {
                    ("kestrel", CrewOnFoot.CabinDoor("kestrel")), ("peregrine", CrewOnFoot.CabinDoor("peregrine")),
                    ("northstar", CrewOnFoot.CabinDoor("northstar")), ("guardian", CrewOnFoot.CabinDoor("guardian")),
                    ("nightjar", new Vector3(0, 0, 2.9f)),   // its rear doors, for a stretcher
                };
                var problems = new List<string>();
                foreach (var (id, local) in doors)
                {
                    var rd = (float)Aircraft.Get(id).rotorDiameter;
                    var side = Mathf.Sign(local.x == 0 ? 1 : local.x);
                    var door = t.TransformPoint(new Vector3(local.x, 0, -local.z));
                    var dl = t.InverseTransformPoint(door);
                    var zBehind = -(rd * 0.65f + 3);
                    for (int a = 0; a < 360; a += 15)
                        foreach (var r in new[] { 6f, 12f, 25f, 45f })
                        {
                            var far = t.TransformPoint(new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * r, 0, Mathf.Cos(a * Mathf.Deg2Rad) * r));
                            var pts = new List<Vector3> { far };
                            pts.AddRange(LoadingParty.Approach(t, rd, door, side, far));
                            pts.Add(door);
                            var loc = pts.Select(p => { var q = t.InverseTransformPoint(p); return new Vector2(q.x, q.z); }).ToList();
                            var total = 0f; for (int i = 1; i < loc.Count; i++) total += Vector2.Distance(loc[i - 1], loc[i]);
                            var walked = 0f;
                            for (int i = 1; i < loc.Count; i++)
                            {
                                var seg = Vector2.Distance(loc[i - 1], loc[i]);
                                for (float s = 0; s < seg; s += 0.25f)
                                {
                                    var p = Vector2.Lerp(loc[i - 1], loc[i], s / seg);
                                    var gone = walked + s;
                                    if (gone < 3 || total - gone < 1.5f) continue;   // getting up from where they lay, and arriving at the door
                                    var tail = Mathf.Abs(p.x) < 2.5f && p.y < dl.z - 1 && p.y > zBehind + 1;
                                    var body = Mathf.Abs(p.x) < 0.9f && p.y >= dl.z - 1 && p.y < rd * 0.3f;
                                    if (tail || body) { problems.Add($"{id} from {a}° {r} m: {(tail ? "past the tail" : "through the fuselage")} at ({p.x:F1}, {p.y:F1})"); goto next; }
                                }
                                walked += seg;
                            }
                            // the last stretch: in from the side (unless they were beside the door already)
                            if (pts.Count > 2)
                            {
                                var last = loc[loc.Count - 1] - loc[loc.Count - 2];
                                if (Mathf.Abs(last.x) < 2 * Mathf.Abs(last.y) || last.x * side > 0) problems.Add($"{id} from {a}° {r} m: last stretch ({last.x:F1}, {last.y:F1}) not in from the side");
                            }
                            next:;
                        }
                }
                Assert.That(problems, Is.Empty, string.Join("\n", problems.Take(20)));
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
