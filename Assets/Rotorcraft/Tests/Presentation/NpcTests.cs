using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace Rotorcraft.Presentation.Tests
{
    /// <summary>The people pack in Unity: every clip binds to every character, the hips scaling puts each one's feet on the ground,
    /// and the clips are measured for what the notes leave to tuning (walking speeds) and for jumps a still frame cannot show.
    /// Writes Logs/npc-clips.txt.</summary>
    public class NpcTests
    {
        NpcLibrary lib;
        GameObject host;
        readonly List<GameObject> made = new List<GameObject>();

        static readonly string[] Ids = { "pilot", "copilot", "winch_operator", "winchman_paramedic", "casualty_hiker_m", "casualty_hiker_f", "casualty_sailor", "casualty_older_walker", "ground_marshaller", "offshore_hdo", "offshore_passenger" };

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("people");
            lib = host.AddComponent<NpcLibrary>();
            lib.EnsureLoaded();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var g in made) if (g != null) Object.DestroyImmediate(g);
            made.Clear();
            Object.DestroyImmediate(host);
        }

        Npc Make(string id)
        {
            var n = lib.Spawn(id, null, null, true);   // every part separate, to find the shoes
            Assert.IsNotNull(n, id);
            made.Add(n.gameObject);
            return n;
        }

        /// <summary>The lowest point of the shoes (or of everything, for clips that rest on the body), in world space.</summary>
        static float Lowest(Npc n, bool shoesOnly = true)
        {
            var low = float.MaxValue;
            var mesh = new Mesh();
            foreach (var r in n.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (shoesOnly && !r.name.EndsWith("_shoes")) continue;
                r.BakeMesh(mesh, true);
                var m = r.transform.localToWorldMatrix;
                foreach (var v in mesh.vertices) low = Mathf.Min(low, m.MultiplyPoint3x4(v).y);
            }
            Object.DestroyImmediate(mesh);
            return low;
        }

        static Transform Bone(Npc n, string name) => n.GetComponentsInChildren<Transform>().First(t => t.name == "mixamorig:" + name);

        [Test]
        public void EveryCharacterAndClipIsThere()
        {
            foreach (var id in Ids) Assert.IsTrue(lib.Has(id), id);
            foreach (var c in new[] { "idle", "walk", "run", "wave_help", "lie_back", "tread_water", "winch_hang_idle", "winch_descend", "winch_ascend", "winch_double_rescuer", "winch_double_casualty", "marshal_hover", "stretcher_carry_walk", "pilot_seated_controls", "thumbs_up", "kneel_idle", "sit_ground", "crouch_walk", "injured_walk" })
                Assert.IsNotNull(lib.Clip(c), c);
        }

        /// <summary>The clips' curves are addressed from the animation file's rig; each prefab's rig is renamed to match, so a clip
        /// moves every bone of every character, not just some.</summary>
        [Test]
        public void TheSharedClipsMoveEveryCharactersBones()
        {
            foreach (var id in Ids)
            {
                var n = Make(id);
                var arm = Bone(n, "LeftArm");
                var bind = arm.localRotation;
                n.Play("wave_help", 0, 1, 0.3f, true);
                n.Evaluate(0);
                Assert.Greater(Quaternion.Angle(bind, arm.localRotation), 20, $"{id}: the left arm did not move from the bind pose");
            }
        }

        /// <summary>A merged look (two renderers) is the same person as the separate parts: same vertices, same shape in a pose.</summary>
        [Test]
        public void MergedLooksMatchTheirParts()
        {
            foreach (var (id, without) in new[] { "pilot", "medic", "soldier", "scrubs", "walker" }.Select(role => NpcRoles.For(role, 1)))
            {
                var merged = lib.Spawn(id, null, without); made.Add(merged.gameObject);
                var parts = lib.Spawn(id, null, without, true); made.Add(parts.gameObject);
                var rs = merged.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Assert.AreEqual(1, rs.Count(r => !Npc.SmallParts.Any(sp => r.name.StartsWith(id + "_" + sp))), $"{id}: the figure is one renderer");
                foreach (var n in new[] { merged, parts }) { n.Play("wave_help", 0, 0, 0.4f, true); n.Evaluate(0); }
                (int verts, Bounds b) Shape(Npc n)
                {
                    var mesh = new Mesh(); int v = 0; var b = new Bounds(); var first = true;
                    foreach (var r in n.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        if (!r.gameObject.activeInHierarchy) continue;
                        r.BakeMesh(mesh, true);
                        var m = r.transform.localToWorldMatrix;
                        foreach (var x in mesh.vertices) { var w = m.MultiplyPoint3x4(x); if (first) { b = new Bounds(w, Vector3.zero); first = false; } else b.Encapsulate(w); }
                        v += mesh.vertexCount;
                    }
                    Object.DestroyImmediate(mesh);
                    return (v, b);
                }
                var (mv, mb) = Shape(merged); var (pv, pb) = Shape(parts);
                Assert.GreaterOrEqual(mv, pv, $"{id}: vertices");
                Assert.Less(Vector3.Distance(mb.min, pb.min) + Vector3.Distance(mb.max, pb.max), 0.002f, $"{id}: merged {mb} against parts {pb}");
            }
        }

        /// <summary>The required hips step: with it every character stands on the ground in idle; without it the short ones float and the
        /// tall ones sink (the notes say by up to about 20 cm).</summary>
        [Test]
        public void HipsScalingPutsEveryonesFeetOnTheGround()
        {
            var log = new StringBuilder("Soles in idle, with the hips scaled and the measured lift (and as they would be without the hips step):\n");
            var fails = new List<string>();
            foreach (var id in Ids)
            {
                var n = Make(id);
                n.Play("idle", 0, 1, 0, true);
                n.Evaluate(0);
                var sole = Lowest(n);
                var hips = Bone(n, "Hips");
                var y = hips.localPosition.y;
                hips.localPosition /= n.Scale;   // as the clip alone would leave it
                var unscaled = Lowest(n);
                log.AppendLine($"  {id,-22} hip {n.HipHeight:F3} m  scale {n.Scale:F3}  sole {sole * 100:+0.0;-0.0} cm  (without the hips step {unscaled * 100:+0.0;-0.0} cm)");
                if (System.Math.Abs(sole) > 0.015f) fails.Add($"{id} {sole * 100:F1} cm");
                Assert.AreEqual(n.HipHeight, y, 0.06f, $"{id}: hips at {y:F3} m");
            }
            Write("soles", log.ToString());
            Assert.IsEmpty(fails, "soles off the ground in idle: " + string.Join(", ", fails));
        }

        /// <summary>Standing clips keep their feet on the ground all the way through; clips that rest on the body stay on it.</summary>
        [Test]
        public void ClipsStayOnTheGround()
        {
            var log = new StringBuilder("Lowest point through each clip on the co-pilot (hip 0.989 m), with the measured lift, cm from the ground (min .. max):\n");
            var standing = new[] { "idle", "idle_look_around", "talk", "point", "wave_help", "injured_idle", "marshal_hover", "marshal_approach", "marshal_land", "marshal_hold", "marshal_shutdown", "thumbs_up", "thumbs_up_big", "walk", "run", "injured_walk", "crouch_walk", "stretcher_carry_walk" };
            var resting = new[] { "sit_ground", "lie_back", "kneel_idle" };
            var problems = new List<string>();
            foreach (var clip in standing.Concat(resting))
            {
                var n = Make("copilot");
                n.Play(clip, 0, 1, 0, true);
                var c = lib.Clip(clip);
                var ys = new List<float>();
                for (int i = 0; i < 24; i++)
                {
                    n.Play(clip, 0, 0, i / 24f, true);
                    n.Evaluate(0);
                    ys.Add(Lowest(n, !resting.Contains(clip)));
                }
                ys.Sort();
                float lo = ys[0], mid = ys[ys.Count / 2], hi = ys[ys.Count - 1];
                log.AppendLine($"  {clip,-22} {lo * 100:+0.0;-0.0} .. {hi * 100:+0.0;-0.0}   median {mid * 100:+0.0;-0.0}");
                var settled = resting.Contains(clip) ? lo : mid;
                if (System.Math.Abs(settled) > 0.015f || lo < -0.045f) problems.Add($"{clip} {lo * 100:F1}/{mid * 100:F1} cm");
                Object.DestroyImmediate(n.gameObject);
            }
            Write("ground", log.ToString());
            Assert.IsEmpty(problems, "lowest point off the ground: " + string.Join(", ", problems));
        }

        /// <summary>What still frames cannot show: for each clip, the biggest turn of any bone between two frames a 30th of a second
        /// apart (a pop), and for loops the jump from the last frame back to the first (a seam). Also the speed the planted foot moves
        /// at in the walking clips, which is the speed the game must move the person at for the feet not to slide. Report only:
        /// the hand-made clips are first passes, and this is how they are judged in motion.</summary>
        [Test]
        public void MeasureTheClipsInMotion()
        {
            var log = new StringBuilder("Clip motion on the pilot (reference skeleton), 30 samples a second:\n  clip                    seconds  worst frame-to-frame turn  (bone)      loop seam  (bone)\n");
            var n = Make("pilot");
            var bones = n.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("mixamorig:") && !t.name.Contains("Hand") || t.name.EndsWith("Hand")).ToArray();
            var clipNames = new[] { "idle", "idle_look_around", "walk", "run", "talk", "point", "wave_help", "injured_walk", "injured_idle", "sit_ground", "lie_back", "tread_water", "swim", "sit_chair", "stand_to_sit", "sit_to_stand", "kneel_idle", "crouch_walk", "hang_idle", "climb_ladder", "thumbs_up_big", "winch_hang_idle", "winch_descend", "winch_ascend", "winch_double_rescuer", "winch_double_casualty", "marshal_hover", "marshal_approach", "marshal_land", "marshal_hold", "marshal_shutdown", "stretcher_carry_walk", "pilot_seated_controls", "thumbs_up" };
            foreach (var name in clipNames)
            {
                var c = lib.Clip(name);
                if (c == null) { log.AppendLine($"  {name}: missing"); continue; }
                Quaternion[] Pose(float t)
                {
                    n.Play(name, 0, 1, Mathf.Clamp01(t / c.length), true);
                    n.Evaluate(0);
                    return bones.Select(b => b.localRotation).ToArray();
                }
                var steps = Mathf.Max(2, Mathf.RoundToInt(c.length * 30));
                var prev = Pose(0); var first = prev;
                float worst = 0; string worstBone = "";
                for (int i = 1; i <= steps; i++)
                {
                    var p = Pose(c.length * i / steps * 0.999f);
                    for (int b = 0; b < bones.Length; b++) { var a = Quaternion.Angle(prev[b], p[b]); if (a > worst) { worst = a; worstBone = bones[b].name.Replace("mixamorig:", ""); } }
                    prev = p;
                }
                var seam = "";
                if (c.loop)
                {
                    float s = 0; string sb = "";
                    for (int b = 0; b < bones.Length; b++) { var a = Quaternion.Angle(prev[b], first[b]); if (a > s) { s = a; sb = bones[b].name.Replace("mixamorig:", ""); } }
                    seam = $"{s,5:F1}°  ({sb})";
                }
                else seam = "  (one-shot)";
                log.AppendLine($"  {name,-24} {c.length,5:F2}   {worst,5:F1}°  ({worstBone,-12})   {seam}");
            }
            // the planted foot's speed in the walking clips
            log.AppendLine("\nPlanted-foot speed in the in-place clips (m/s, reference skeleton; the notes' starting values in brackets):");
            foreach (var (name, guess) in new[] { ("walk", 1.4f), ("run", 3.0f), ("injured_walk", 0.7f), ("crouch_walk", 1.0f), ("stretcher_carry_walk", 1.4f) })
            {
                var c = lib.Clip(name);
                var left = Bone(n, "LeftToeBase"); var right = Bone(n, "RightToeBase");
                var speeds = new List<float>();
                var steps = Mathf.RoundToInt(c.length * 60);
                Vector3? pl = null, pr = null;
                for (int i = 0; i <= steps; i++)
                {
                    n.Play(name, 0, 1, (float)i / steps * 0.999f, true);
                    n.Evaluate(0);
                    var l = left.position; var r = right.position;
                    if (pl != null)
                    {
                        // the lower foot is the one on the ground
                        // the planted foot travels backwards under the body at the walking speed
                        var lower = l.y < r.y;
                        var (now, before) = lower ? (l, pl.Value) : (r, pr.Value);
                        var other = lower ? r.y : l.y;
                        if (now.y < 0.1f && other - now.y > 0.02f) speeds.Add(-(now.z - before.z) * steps / c.length);
                    }
                    pl = l; pr = r;
                }
                speeds.Sort();
                var median = speeds.Count > 0 ? speeds[speeds.Count / 2] : 0;
                log.AppendLine($"  {name,-22} {median:F2} ({guess:F1})");
            }
            Write("clips", log.ToString());
            Assert.Pass(log.ToString());
        }

        static void Write(string tag, string text)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText($"Logs/npc-{tag}.txt", text + "\n");
            Debug.Log(text);
        }
    }
}
