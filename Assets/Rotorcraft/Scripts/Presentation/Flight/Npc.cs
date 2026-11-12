// One person from the people pack (Models/People): a character prefab driven by the pack's shared clips through a small
// playable graph that is only evaluated when NpcLibrary says so, which lets the crowd update far or hidden people less
// often. After every evaluation the hips' position is multiplied by this character's hip height over the reference
// skeleton's (1.0434 m): the clips were made on that skeleton, so without it shorter people float and taller ones sink.
// Scaling after the blend is the same as scaling each clip's hips track first, as the pack's notes ask, because a blend
// is a weighted sum. Never shown in the bind pose (an A-pose): the first clip is evaluated as soon as it is set.
// The pack's soles end up a few centimetres below its origin in most clips (on the reference skeleton too, so not from the
// scaling): NpcImport measures how far for each character and clip (npc_ground.json) and the rig is lifted by that,
// blended like the clips.
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Rotorcraft.Presentation
{
    public sealed class Npc : MonoBehaviour
    {
        public const string RigName = "npc_animation_rig", HipsBone = "mixamorig:Hips";
        public const float ReferenceHip = 1.0434f;
        /// <summary>False while NpcImport measures the soles: the clips as they come.</summary>
        public static bool Lifted = true;
        /// <summary>Small parts dropped in the distance: each is a draw call for a few pixels.</summary>
        public static readonly string[] SmallParts = { "brows", "eyes", "eardefenders", "visor", "packstraps", "harness" };

        public string Id { get; private set; }
        public float Height { get; private set; }
        public float HipHeight { get; private set; }
        /// <summary>Hip height over the reference skeleton's: the hips track's multiplier, and how much shorter (or longer) the stride is.</summary>
        public float Scale => HipHeight / ReferenceHip;
        /// <summary>The clip playing (or fading in).</summary>
        public string Clip => playing[cur]?.name;
        /// <summary>A clip that does not loop has reached its end.</summary>
        public bool Done => playing[cur] != null && !playing[cur].loop && phase[cur] == null && time[cur] >= playing[cur].length;

        internal float pending;
        internal int index;
        internal Renderer body;
        Renderer[] all, small, casters;
        bool detail = true, shadows = true, evaluated;
        Transform hips, rig;
        System.Collections.Generic.Dictionary<string, float> lift;
        NpcLibrary lib;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        readonly AnimationClipPlayable[] slot = new AnimationClipPlayable[2];
        readonly NpcLibrary.NpcClip[] playing = new NpcLibrary.NpcClip[2];
        readonly double[] time = new double[2];
        readonly float[] rate = new float[2];
        readonly float?[] phase = new float?[2];   // walking: where in the cycle, from the distance walked, not the clock
        int cur;
        float fadeT, fadeLen;

        internal void Init(NpcLibrary library, string id, float height, float hipHeight)
        {
            lib = library; Id = id; Height = height; HipHeight = hipHeight;
            foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == HipsBone) { hips = t; break; }
            rig = hips != null ? hips.parent : null;
            lift = library.LiftFor(id);
            all = GetComponentsInChildren<Renderer>(true);
            var smallList = new System.Collections.Generic.List<Renderer>();
            foreach (var r in all)
            {
                // a merged look has its figure as one renderer (<id>_figure) and the small parts separate (<id>_<part>)
                foreach (var p in SmallParts) if (r.name.StartsWith(id + "_" + p)) smallList.Add(r);
                if (r.name == id + "_suit" || r.name == id + "_figure") body = r;
            }
            small = smallList.ToArray();
            // the small parts never cast a shadow: a shadow of an eyebrow or a strap is invisible, and each is a draw per cascade
            foreach (var r in small) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            casters = System.Array.FindAll(all, r => System.Array.IndexOf(small, r) < 0);
            if (body == null && all.Length > 0) body = all[0];
            graph = PlayableGraph.Create("NPC " + id);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "pose", GetComponent<Animator>());
            mixer = AnimationMixerPlayable.Create(graph, 2);
            output.SetSourcePlayable(mixer);
            graph.Play();
        }

        /// <summary>Play a clip, fading from the one before. at: where to start, as a fraction of the clip (so a crowd is not in step).</summary>
        public Npc Play(string name, float fade = 0.25f, float speed = 1, float at = 0, bool restart = false)
        {
            if (!restart && playing[cur] != null && playing[cur].name == name && phase[cur] == null) { rate[cur] = speed; return this; }
            var c = Begin(name, fade);
            if (c < 0) return this;
            time[c] = at * playing[c].length; rate[c] = speed; phase[c] = null;
            if (!evaluated) Evaluate(0);
            return this;
        }

        /// <summary>Walk, run or limp in step with the ground covered: `cycles` is the distance walked over one cycle's stride, so feet
        /// stay planted whatever the speed. NpcLibrary.Stride gives a clip's stride for this character.</summary>
        public Npc Cycle(string name, float cycles, float fade = 0.2f)
        {
            if (playing[cur] == null || playing[cur].name != name || phase[cur] == null)
            {
                if (Begin(name, fade) < 0) return this;
            }
            phase[cur] = cycles - Mathf.Floor(cycles);
            if (!evaluated) Evaluate(0);
            return this;
        }

        /// <summary>Metres covered in one cycle of a walking clip by this character (the clip's speed, scaled to their legs).</summary>
        public float Stride(string clip) => lib.Clip(clip) is NpcLibrary.NpcClip c ? c.speed * c.length * Scale : 1.6f;

        int Begin(string name, float fade)
        {
            var c = lib != null ? lib.Clip(name) : null;
            if (c == null) { Debug.LogWarning($"NPC: no clip {name}"); return -1; }
            var next = slot[cur].IsValid() ? 1 - cur : cur;
            Drop(next);
            slot[next] = AnimationClipPlayable.Create(graph, c.clip);
            slot[next].SetApplyFootIK(false);
            slot[next].SetApplyPlayableIK(false);
            graph.Connect(slot[next], 0, mixer, next);
            playing[next] = c;
            if (next == cur || fade <= 0 || !evaluated)
            {
                Drop(1 - next);
                mixer.SetInputWeight(next, 1);
                fadeLen = 0;
            }
            else
            {
                mixer.SetInputWeight(next, 0);
                mixer.SetInputWeight(cur, 1);
                fadeT = 0; fadeLen = fade;
            }
            cur = next;
            return next;
        }

        void Drop(int s)
        {
            if (!slot[s].IsValid()) return;
            graph.Disconnect(mixer, s);
            slot[s].Destroy();
            playing[s] = null;
            mixer.SetInputWeight(s, 0);
        }

        /// <summary>Hide parts for the role (the harness on a land paramedic, a hi-vis vest...), by the part names in the pack.</summary>
        public Npc Without(params string[] parts)
        {
            if (parts == null) return this;
            foreach (var r in all)
                foreach (var p in parts)
                    if (r.name.StartsWith(Id + "_" + p)) r.gameObject.SetActive(false);
            return this;
        }

        /// <summary>Close up: every part. Further off the small parts go.</summary>
        internal void SetDetail(bool on)
        {
            if (on == detail) return;
            detail = on;
            foreach (var r in small) r.enabled = on;
        }

        internal void SetShadows(bool on)
        {
            if (on == shadows) return;
            shadows = on;
            foreach (var r in casters) r.shadowCastingMode = on ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        internal bool Seen => body != null && body.isVisible;

        /// <summary>Move the clips on by dt and pose the skeleton.</summary>
        public void Evaluate(float dt)
        {
            if (!graph.IsValid()) return;
            for (int s = 0; s < 2; s++)
            {
                if (!slot[s].IsValid()) continue;
                var c = playing[s];
                double t;
                if (phase[s] is float ph) t = ph * c.length;
                else
                {
                    time[s] += dt * rate[s];
                    t = c.loop ? time[s] % c.length : System.Math.Min(time[s], c.length);
                }
                slot[s].SetTime(t);
            }
            if (fadeLen > 0)
            {
                fadeT += dt;
                var w = Mathf.Clamp01(fadeT / fadeLen);
                mixer.SetInputWeight(cur, w);
                mixer.SetInputWeight(1 - cur, 1 - w);
                if (w >= 1) { fadeLen = 0; Drop(1 - cur); }
            }
            graph.Evaluate(0);
            if (hips != null) hips.localPosition *= Scale;
            if (rig != null)
            {
                float y = 0;
                if (Lifted && lift != null)
                    for (int s = 0; s < 2; s++)
                        if (slot[s].IsValid() && lift.TryGetValue(playing[s].name, out var l)) y += mixer.GetInputWeight(s) * l;
                rig.localPosition = new Vector3(0, y, 0);
            }
            evaluated = true;
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
            lib?.Remove(this);
        }
    }
}
