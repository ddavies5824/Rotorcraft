// The people pack in the game: the eleven characters and the 34 shared clips (set by GameSceneBuilder; read straight from
// the project in the editor), who plays each part of the game, the clip for each pose, and the crowd. Every Npc is
// evaluated here after whatever moves it has placed it this frame: every frame near the camera, every second or third
// frame further off with its small parts and shadow dropped, and not at all while off screen, as the pack's notes advise
// for the handhelds. People stand on the ground that is drawn (the terrain tiles' heights), not the world map's formula,
// which the coarser tiles can miss by a hand's breadth; decks and roofs come from the world map.
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Rotorcraft.Sim;
using Rotorcraft.World;
using UnityEngine;
using Terrain = UnityEngine.Terrain;

namespace Rotorcraft.Presentation
{
    [DefaultExecutionOrder(900)]
    public sealed class NpcLibrary : MonoBehaviour
    {
        /// <summary>Models/People/Prefabs (NpcImport.Generate): each character with its parts separate (&lt;id&gt;), and each look a part in the
        /// game asks for merged into two renderers (&lt;id&gt;@all, &lt;id&gt;@harness+lifejacket...).</summary>
        [Tooltip("Models/People/Prefabs: each character with separate parts, and each look merged into two renderers (NpcImport.Generate).")]
        public GameObject[] characters;
        [Tooltip("The clips in npc_animations.glb.")]
        public AnimationClip[] clips;
        public TextAsset charactersTable, clipsTable;
        [Tooltip("npc_ground.json: how far to lift each character in each clip to put the soles on the ground (NpcImport).")]
        public TextAsset groundTable;

        public static NpcLibrary Instance { get; private set; }

        /// <summary>Within DetailRange, every frame; to HalfRateRange every second frame, beyond that every third.</summary>
        public const float HalfRateRange = 150;
        /// <summary>Within DetailRange every part is drawn; within ShadowRange people cast shadows. Shorter than the pack's notes
        /// suggest (60 m for detail): measured by PeoplePerf, each part is a draw call into the view and every shadow cascade, and at
        /// a handheld's 1280 x 800 a face 25 m off is a few pixels (the benchmark tries other values).</summary>
        public static float DetailRange = 25, ShadowRange = 60;

        public sealed class NpcClip { public string name; public AnimationClip clip; public bool loop; public float length, speed; }
        sealed class Character { public GameObject prefab; public float height, hipHeight; }

        /// <summary>The merged prefab's name for a character with parts hidden: pilot@all, winchman_paramedic@harness+lifejacket.</summary>
        public static string Look(string id, string[] without) => id + "@" + (without == null || without.Length == 0 ? "all" : string.Join("+", without));

        /// <summary>Walking speeds of the in-place clips on the reference skeleton (m/s): how fast the planted foot moves back, measured by
        /// NpcTests.MeasureTheClipsInMotion (the notes' first guesses were 1.4, 3.0, 0.7 and 1.0). Npc.Stride scales them to the legs.</summary>
        static readonly Dictionary<string, float> Speeds = new Dictionary<string, float>
        {
            ["walk"] = 1.6f, ["run"] = 1.9f, ["injured_walk"] = 1.3f, ["crouch_walk"] = 1.4f, ["stretcher_carry_walk"] = 1.6f, ["swim"] = 0.8f,
        };

        /// <summary>Loops whose last frame does not meet the first (lie_back: the left hand jumps 96 degrees, NpcTests): played once and held.</summary>
        static readonly string[] PlayOnce = { "lie_back" };

        readonly Dictionary<string, Character> chars = new Dictionary<string, Character>();
        readonly Dictionary<string, GameObject> looks = new Dictionary<string, GameObject>();
        readonly Dictionary<string, NpcClip> clipBy = new Dictionary<string, NpcClip>();
        readonly List<Npc> crowd = new List<Npc>();
        readonly Dictionary<string, Dictionary<string, float>> lifts = new Dictionary<string, Dictionary<string, float>>();

        internal Dictionary<string, float> LiftFor(string id) => lifts.TryGetValue(id, out var d) ? d : null;
        HelicopterController heli;
        int frame, made;

        public int Count => crowd.Count;

        void Awake()
        {
            Instance = this;
            EnsureLoaded();
        }

        bool loaded;

        /// <summary>Read the tables and match the prefabs and clips (Awake does it; tests made in edit mode call it).</summary>
        public void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
#if UNITY_EDITOR
            // tests and the editor's own play mode: straight from the project
            const string dir = "Assets/Rotorcraft/Models/People";
            if (charactersTable == null) charactersTable = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(dir + "/npc_characters.json");
            if (clipsTable == null) clipsTable = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(dir + "/npc_clips.json");
            if (groundTable == null) groundTable = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(dir + "/npc_ground.json");
            if (clips == null || clips.Length == 0)
            {
                var list = new List<AnimationClip>();
                foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(dir + "/npc_animations.glb")) if (o is AnimationClip c) list.Add(c);
                clips = list.ToArray();
            }
            if (characters == null || characters.Length == 0)
            {
                var list = new List<GameObject>();
                foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { dir + "/Prefabs" })) list.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid)));
                characters = list.ToArray();
            }
#endif
            if (charactersTable == null || clipsTable == null) { Debug.LogWarning("NPC: the people pack's tables are missing"); return; }
            if (characters != null) foreach (var g in characters) if (g != null && g.name.Contains("@")) looks[g.name] = g;
            var ct = JObject.Parse(charactersTable.text);
            foreach (var p in ct.Properties())
            {
                // the separate-part prefab is only in the editor; the game has the merged looks
                GameObject prefab = null;
                if (characters != null) foreach (var g in characters) if (g != null && (g.name == p.Name || (prefab == null && g.name == p.Name + "@all"))) prefab = g;
                if (prefab == null) { Debug.LogWarning($"NPC: no prefab for {p.Name}"); continue; }
                chars[p.Name] = new Character { prefab = prefab, height = (float)p.Value["height_m"], hipHeight = (float)p.Value["hip_height_m"] };
            }
            if (groundTable != null)
                foreach (var p in JObject.Parse(groundTable.text).Properties())
                {
                    var d = new Dictionary<string, float>();
                    foreach (var q in ((JObject)p.Value).Properties()) d[q.Name] = (float)q.Value;
                    lifts[p.Name] = d;
                }
            var byName = new Dictionary<string, AnimationClip>();
            if (clips != null) foreach (var c in clips) if (c != null) byName[c.name] = c;
            foreach (var p in ((JObject)JObject.Parse(clipsTable.text)["clips"]).Properties())
            {
                if (!byName.TryGetValue(p.Name, out var clip)) { Debug.LogWarning($"NPC: no clip {p.Name}"); continue; }
                var loop = (bool)p.Value["loop"] && System.Array.IndexOf(PlayOnce, p.Name) < 0;
                clipBy[p.Name] = new NpcClip { name = p.Name, clip = clip, loop = loop, length = clip.length, speed = Speeds.TryGetValue(p.Name, out var v) ? v : 0 };
            }
        }

        public NpcClip Clip(string name) => name != null && clipBy.TryGetValue(name, out var c) ? c : null;

        /// <summary>The clips in the table's order.</summary>
        public IEnumerable<string> ClipNames => clipBy.Keys;

        public bool Has(string id) => chars.ContainsKey(id);

        /// <summary>A character from the pack, standing at its parent's origin, playing idle: the merged look (two renderers) unless
        /// `parts` asks for every part separate (measuring the soles), or no merged look was made for this combination.</summary>
        public Npc Spawn(string id, Transform parent, string[] without = null, bool parts = false)
        {
            if (!chars.TryGetValue(id, out var ch)) { Debug.LogWarning($"NPC: no character {id}"); return null; }
            var merged = !parts && looks.TryGetValue(Look(id, without), out var look) ? look : null;
            if (merged == null && ch.prefab.name.Contains("@")) Debug.LogWarning($"NPC: no merged look {Look(id, without)}; run NpcImport.Generate");
            var g = Instantiate(merged != null ? merged : ch.prefab, parent, false);
            g.name = id;
            var n = g.AddComponent<Npc>();
            n.Init(this, id, ch.height, ch.hipHeight);
            n.index = made++;
            if (merged == null) n.Without(without);
            crowd.Add(n);
            n.Play("idle", 0, 1, (n.index * 0.37f) % 1);
            return n;
        }

        /// <summary>Someone for a part in the game (an outfit name from the scenery and the loading parties), playing the clip for a pose.</summary>
        public Npc SpawnRole(string role, int seed, string pose, Transform parent)
        {
            var (id, without) = NpcRoles.For(role, seed);
            var n = Spawn(id, parent, without);
            if (n != null && pose != null) n.Play(NpcRoles.ClipFor(pose), 0, 1, (Mathf.Abs(seed) * 0.173f) % 1);
            return n;
        }

        internal void Remove(Npc n) => crowd.Remove(n);

        static readonly Unity.Profiling.ProfilerMarker CrowdMarker = new Unity.Profiling.ProfilerMarker("NPC.Crowd");
        /// <summary>False: every person in full detail every frame (the benchmark's comparison).</summary>
        public static bool Tiers = true;

        void LateUpdate()
        {
            using var _ = CrowdMarker.Auto();
            frame++;
            if (heli == null || !heli.isActiveAndEnabled) heli = FindAnyObjectByType<HelicopterController>();
            var dt = heli != null && heli.Paused ? 0 : Time.deltaTime;
            var cam = Camera.main;
            var eye = cam != null ? cam.transform.position : Vector3.zero;
            for (int i = crowd.Count - 1; i >= 0; i--)
            {
                var n = crowd[i];
                if (n == null) { crowd.RemoveAt(i); continue; }
                if (!n.isActiveAndEnabled) continue;
                n.pending += dt;
                var d = Vector3.Distance(eye, n.transform.position);
                n.SetDetail(!Tiers || d < DetailRange);
                n.SetShadows(!Tiers || d < ShadowRange);
                if (Tiers && !n.Seen) continue;
                var every = !Tiers || d < DetailRange ? 1 : d < HalfRateRange ? 2 : 3;
                if ((frame + n.index) % every != 0) continue;
                n.Evaluate(n.pending);
                n.pending = 0;
            }
        }

        // ---------------------------------------------------------------------------------------------- the ground
        static Terrain[] terrains;
        static float terrainsAt = -99;

        /// <summary>The height of the ground as drawn under a point (Unity x and z): the terrain tile's own height, or a deck or roof.</summary>
        public static float Ground(float x, float z)
        {
            var g = WorldMap.GroundAt(x, -z);
            if (g.surface == Surface.Deck || g.surface == Surface.Roof) return (float)g.h;
            if (g.surface == Surface.Water) return (float)g.h;
            var t = TerrainAt(x, z);
            if (t == null) return (float)g.h;
            return t.SampleHeight(new Vector3(x, 0, z)) + t.transform.position.y;
        }

        public static Vector3 OnGround(Vector3 p) { p.y = Ground(p.x, p.z); return p; }

        static Terrain TerrainAt(float x, float z)
        {
            if (terrains == null || Time.realtimeSinceStartup - terrainsAt > 5) { terrains = Terrain.activeTerrains; terrainsAt = Time.realtimeSinceStartup; }
            Terrain best = null;
            foreach (var t in terrains)
            {
                if (t == null || t.terrainData == null) continue;
                var p = t.transform.position; var s = t.terrainData.size;
                if (x < p.x || x > p.x + s.x || z < p.z || z > p.z + s.z) continue;
                if (best == null || s.x < best.terrainData.size.x) best = t;
            }
            return best;
        }
    }

    /// <summary>Who plays each part, and the clip for each pose. The pack has no police, soldiers, fire crew or hospital staff yet,
    /// so those are its nearest characters with parts hidden.</summary>
    public static class NpcRoles
    {
        static readonly string[] None = new string[0];
        static readonly string[] Casual = { "casualty_hiker_m", "casualty_hiker_f", "casualty_sailor", "casualty_older_walker" };

        /// <summary>Every look For can give (NpcImport makes a merged prefab of each).</summary>
        public static IEnumerable<(string id, string[] without)> Looks()
        {
            var seen = new HashSet<string>();
            foreach (var role in new[] { "pilot", "copilot", "aircrew", "winch_operator", "winchman", "medic", "immersion", "dummy", "worker", "fire", "hivis", "police", "marshaller", "armed", "soldier", "scrubs", "walker", "climber", "sailor", "fisher", "swimmer", "casual" })
                for (int seed = 0; seed < 8; seed++)
                {
                    var l = For(role, seed);
                    if (seen.Add(NpcLibrary.Look(l.id, l.without))) yield return l;
                }
        }

        public static (string id, string[] without) For(string role, int seed)
        {
            var odd = (seed & 1) == 1;
            switch (role)
            {
                case "pilot": return ("pilot", None);
                case "copilot": return ("copilot", None);
                case "aircrew": return (odd ? "copilot" : "pilot", None);
                case "winch_operator": return ("winch_operator", None);
                case "winchman": return ("winchman_paramedic", None);
                case "medic": return ("winchman_paramedic", new[] { "harness", "lifejacket", "helmet", "visor" });   // a paramedic on land
                case "immersion": case "dummy": return ("offshore_passenger", None);           // survival suit; the practice dummy
                case "worker": case "fire": return ("offshore_hdo", None);                       // hard hat and hi-vis
                case "hivis": case "police": case "marshaller": return ("ground_marshaller", None);
                case "armed": case "soldier": return ("ground_marshaller", new[] { "hivis", "eardefenders" });
                case "scrubs": return ("copilot", new[] { "helmet", "visor", "lifejacket" });
                case "walker": case "climber": return (odd ? "casualty_hiker_f" : "casualty_hiker_m", None);
                case "sailor": case "fisher": case "swimmer": return ("casualty_sailor", None);
                default: return (Casual[Mathf.Abs(seed) % Casual.Length], None);              // townsfolk, passers-by, golfers
            }
        }

        /// <summary>The pack's clip for one of the old figures' poses.</summary>
        public static string ClipFor(string pose)
        {
            switch (pose)
            {
                case "stand": return "idle";
                case "kneel": return "kneel_idle";
                case "lie": return "lie_back";
                case "sit": return "sit_ground";
                case "swim": return "tread_water";
                case "wave": case "wave2": return "wave_help";
                case "phone": return "idle_look_around";
                case "hang": return "winch_hang_idle";
                case "walk": case "walk0": return "walk";
                case "marshal": return "marshal_approach";
                default: return pose;
            }
        }
    }
}
