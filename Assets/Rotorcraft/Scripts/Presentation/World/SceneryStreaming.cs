// Streams the generated scenery around the camera (after the browser game's TileStreamer in render/tiles.ts): field
// boundaries in 512 m tiles and ground clutter in 64 m tiles, nearest first, freed once left well behind. On Windows
// the tiles are built on worker threads and only made into meshes here; in the browser, which has one thread, each
// job does a slice of work at a time within a few milliseconds a frame. How far each reaches follows the graphics
// quality as the browser game's presets do (Low, Balanced, High), with less in a headset and in the browser.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Rotorcraft.Sim;
using UnityEngine;

namespace Rotorcraft.Presentation
{
    public sealed class SceneryStreaming : MonoBehaviour
    {
        /// <summary>Square tiles of one kind of scenery.</summary>
        public sealed class TileSet
        {
            sealed class Tile
            {
                public int tx, tz;
                public ColourMesh mesh;
                public IEnumerator job;
                public Task task;
                public GameObject go;
                public bool built;
            }

            public readonly string name;
            public readonly int size;
            public float range;
            public bool shadows;
            readonly Func<int, int, ColourMesh, IEnumerator> job;
            readonly Transform root;
            readonly Dictionary<long, Tile> tiles = new Dictionary<long, Tile>();
            readonly List<Tile> queue = new List<Tile>();
            /// <summary>Builders from finished tiles, kept to build the next ones in (their lists already grown).</summary>
            readonly Stack<ColourMesh> spare = new Stack<ColourMesh>();
            int running;

            public TileSet(string name, int size, Transform parent, Func<int, int, ColourMesh, IEnumerator> job)
            {
                this.name = name; this.size = size; this.job = job;
                root = new GameObject(name).transform; root.SetParent(parent, false);
            }

            /// <summary>Tiles wanted but not yet drawn.</summary>
            public int Pending { get { var n = 0; foreach (var t in tiles.Values) if (!t.built) n++; return n; } }
            public int Built { get { var n = 0; foreach (var t in tiles.Values) if (t.go != null) n++; return n; } }
            public int Vertices { get { var n = 0; foreach (var t in tiles.Values) if (t.go != null) n += t.go.GetComponent<MeshFilter>().sharedMesh.vertexCount; return n; } }

            double Gap(Tile t, double x, double z)
            {
                double S = size;
                return Js.Hypot(Math.Max(Math.Max(t.tx * S - x, 0), x - (t.tx + 1) * S), Math.Max(Math.Max(t.tz * S - z, 0), z - (t.tz + 1) * S));
            }

            /// <summary>Start, continue or drop tiles for a camera at sim (x, z). Returns once budgetMs has gone (main-thread work only).</summary>
            public void Update(double cx, double cz, double budgetMs, System.Diagnostics.Stopwatch clock, int maxThreads)
            {
                double R = range, S = size, keep = R + S;
                // drop what is well behind
                List<long> drop = null;
                foreach (var kv in tiles) if (Gap(kv.Value, cx, cz) > keep || R <= 0) (drop ??= new List<long>()).Add(kv.Key);
                if (drop != null) foreach (var k in drop) { Free(tiles[k]); tiles.Remove(k); }
                if (R <= 0) return;
                for (var tz = (int)Math.Floor((cz - R) / S); tz <= (int)Math.Floor((cz + R) / S); tz++)
                    for (var tx = (int)Math.Floor((cx - R) / S); tx <= (int)Math.Floor((cx + R) / S); tx++)
                    {
                        var key = ((long)(tx + 100000) << 32) | (uint)(tz + 100000);
                        if (tiles.ContainsKey(key)) continue;
                        var t = new Tile { tx = tx, tz = tz };
                        if (Gap(t, cx, cz) > R) continue;
                        var o = new P3((tx + 0.5) * S, 0, (tz + 0.5) * S);
                        t.mesh = spare.Count > 0 ? spare.Pop().Reuse(o) : new ColourMesh(o);
                        tiles[key] = t;
                    }
                // finished on a worker: make the mesh here
                foreach (var t in tiles.Values)
                {
                    if (t.task == null || !t.task.IsCompleted) continue;
                    if (t.task.IsFaulted) Debug.LogWarning($"{name} tile {t.tx},{t.tz} failed: {t.task.Exception?.GetBaseException().Message}");
                    t.task = null; running--;
                    Finish(t);
                    if (clock.Elapsed.TotalMilliseconds > budgetMs) return;
                }
                queue.Clear();
                foreach (var t in tiles.Values) if (!t.built && t.task == null) queue.Add(t);
                if (queue.Count == 0) return;
                queue.Sort((a, b) => Gap(a, cx, cz).CompareTo(Gap(b, cx, cz)));
                foreach (var t in queue)
                {
                    if (maxThreads > 0)
                    {
                        if (running >= maxThreads) return;
                        var m = t.mesh; var j = job(t.tx, t.tz, m);
                        running++;
                        t.task = Task.Run(() => { while (j.MoveNext()) { } });
                        continue;
                    }
                    t.job ??= job(t.tx, t.tz, t.mesh);
                    while (true)
                    {
                        if (!t.job.MoveNext()) { Finish(t); break; }
                        if (clock.Elapsed.TotalMilliseconds > budgetMs) return;
                    }
                    if (clock.Elapsed.TotalMilliseconds > budgetMs) return;
                }
            }

            void Finish(Tile t)
            {
                t.built = true; t.job = null;
                t.go = t.mesh.Attach($"{name} {t.tx},{t.tz}", root, shadows);
                // the mesh data now lives on the GPU: keep the builder for another tile (a few, so a long flight holds no more than it needs)
                if (spare.Count < 6) spare.Push(t.mesh);
                t.mesh = null;
            }

            void Free(Tile t)
            {
                if (t.go != null) { UnityEngine.Object.Destroy(t.go.GetComponent<MeshFilter>().sharedMesh); UnityEngine.Object.Destroy(t.go); }
                // a job still running on a worker finishes into a mesh nobody keeps
                if (t.task != null) running--;
            }

            public void Clear() { foreach (var t in tiles.Values) Free(t); tiles.Clear(); running = 0; }
        }

        public TileSet Fields { get; private set; }
        public TileSet Clutter { get; private set; }
        double clutterSpacing = 1.6;
        readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();

        public static SceneryStreaming Current { get; private set; }
        void OnEnable() => Current = this;
        void OnDisable() { if (Current == this) Current = null; }

        void Awake()
        {
            Fields = new TileSet("Field boundaries", FieldBoundaries.TILE, transform, FieldBoundaries.TileJob) { shadows = true };
            Clutter = new TileSet("Ground clutter", GroundClutter.TILE, transform, (tx, tz, m) => GroundClutter.TileJob(tx, tz, clutterSpacing, m)) { shadows = false };
        }

        void OnDestroy() { Fields?.Clear(); Clutter?.Clear(); }

        /// <summary>How far the boundaries and clutter reach and how dense the clutter is, by graphics quality (the browser
        /// game's low, balanced and high presets), in a headset and in the browser.</summary>
        public static (float fields, float clutter, double spacing) Reach()
        {
            var q = QualitySettings.GetQualityLevel();
            var last = Mathf.Max(0, QualitySettings.names.Length - 1);
            (float, float, double) r = q <= 0 && last > 0 ? (1400f, 60f, 2.2) : q < last ? (2400f, 170f, 1.6) : (3400f, 240f, 1.3);
#if UNITY_WEBGL && !UNITY_EDITOR
            r = (Mathf.Min(r.Item1, 1400f), Mathf.Min(r.Item2, 60f), Math.Max(r.Item3, 2.2));
#endif
            if (VrGraphics.Active) r = (Mathf.Min(r.Item1, 1400f), Mathf.Min(r.Item2, 90f), Math.Max(r.Item3, 1.8));
            return r;
        }

        void Update()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var (fr, cr, sp) = Reach();
            if (Math.Abs(sp - clutterSpacing) > 1e-6) { clutterSpacing = sp; Clutter.Clear(); }
            Fields.range = fr; Clutter.range = cr;
            var p = cam.transform.position;
            double x = p.x, z = -p.z;
            clock.Restart();
#if UNITY_WEBGL && !UNITY_EDITOR
            // one thread: the boundaries first, then the clutter, within one budget a frame
            Fields.Update(x, z, 3, clock, 0);
            Clutter.Update(x, z, 4, clock, 0);
#else
            var threads = Mathf.Clamp(SystemInfo.processorCount / 2, 1, 4);
            Clutter.Update(x, z, 2, clock, threads);
            Fields.Update(x, z, 3, clock, Mathf.Max(1, threads / 2));
#endif
        }

        /// <summary>Build everything wanted round a point now (tests and screenshots).</summary>
        public void Settle(Vector3 at, float timeoutS = 120)
        {
            var (fr, cr, sp) = Reach();
            clutterSpacing = sp; Fields.range = fr; Clutter.range = cr;
            double x = at.x, z = -at.z;
            var t0 = Time.realtimeSinceStartup;
            do
            {
                clock.Restart();
                Fields.Update(x, z, 1e9, clock, 0);
                Clutter.Update(x, z, 1e9, clock, 0);
            }
            while (Fields.Pending + Clutter.Pending > 0 && Time.realtimeSinceStartup - t0 < timeoutS);
        }
    }
}
