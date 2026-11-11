// Moving life in the world (port of src/world/traffic.ts): road traffic keeping left at each road's speed, with
// head and tail lights after dark; people walking up and down the pavements in the towns; fishing boats working
// offshore; cars parked on the drives of the modelled houses. Positions are pure functions of the clock, so nothing is saved. Only what is near the camera is
// shown; the models come from the world actors' libraries and are made the first time they come into range.
using System.Collections.Generic;
using System.Linq;
using Rotorcraft.Sim;
using Rotorcraft.World;
using UnityEngine;

namespace Rotorcraft.Presentation
{
    public sealed class AmbientLife : MonoBehaviour
    {
        public const int WALK_FRAMES = 8;
        public const float STRIDE = 1.4f;
        const float RANGE = 2600, PED_RANGE = 1400, FAR = 1000, FAR_WALKER = 300;
        const int OutOfRangeEvery = 32, FarEvery = 4;

        sealed class Path
        {
            public List<Vector2> pts = new List<Vector2>(); public List<float> cum = new List<float>(); public float len, width;
            public (float x, float z, float dx, float dz) At(float s)
            {
                s = Mathf.Clamp(s, 0, len - 0.01f);
                int lo = 0, hi = cum.Count - 1;
                while (hi - lo > 1) { var m = (lo + hi) >> 1; if (cum[m] <= s) lo = m; else hi = m; }
                var a = pts[lo]; var b = pts[hi]; var seg = cum[hi] - cum[lo]; if (seg <= 0) seg = 1;
                var f = (s - cum[lo]) / seg;
                return (a.x + (b.x - a.x) * f, a.y + (b.y - a.y) * f, (b.x - a.x) / seg, (b.y - a.y) / seg);
            }
        }

        static Path Densify(Pt[] p, float width, float step = 12)
        {
            var path = new Path { width = width };
            for (int i = 0; i < p.Length - 1; i++)
            {
                var n = Mathf.Max(1, Mathf.CeilToInt((float)(Js.Hypot(p[i + 1].x - p[i].x, p[i + 1].z - p[i].z) / step)));
                for (int k = 0; k < n; k++) path.pts.Add(new Vector2((float)(p[i].x + (p[i + 1].x - p[i].x) * k / n), (float)(p[i].z + (p[i + 1].z - p[i].z) * k / n)));
            }
            path.pts.Add(new Vector2((float)p[p.Length - 1].x, (float)p[p.Length - 1].z));
            path.cum.Add(0);
            for (int i = 1; i < path.pts.Count; i++) path.cum.Add(path.cum[i - 1] + Vector2.Distance(path.pts[i], path.pts[i - 1]));
            path.len = path.cum[path.cum.Count - 1];
            return path;
        }

        static readonly Dictionary<string, (float speed, float per100m, string[] kinds)> ROADS = new Dictionary<string, (float, float, string[])>
        {
            ["Coast road"] = (21, 0.1f, new[] { "hatch", "saloon", "estate", "suv", "van", "hatch", "pickup", "van" }),
            ["Kinloch road"] = (15, 0.12f, new[] { "hatch", "saloon", "estate", "suv", "van", "hatch" }),
            ["Heliport link"] = (11, 0.12f, new[] { "hatch", "suv", "pickup" }),
            ["Glen road"] = (13, 0.05f, new[] { "suv", "pickup", "hatch", "estate" }),
            ["Inverhallan road"] = (18, 0.08f, new[] { "hatch", "saloon", "estate", "van", "suv" }),
            ["Inverhallan link"] = (11, 0.12f, new[] { "hatch", "saloon", "van" }),
            ["Ski road"] = (11, 0.05f, new[] { "suv", "estate" }),
            ["Castle road"] = (11, 0.05f, new[] { "hatch", "estate", "suv" }),
            ["Quarry road"] = (7, 0.15f, new[] { "pickup" }),
        };
        static readonly Color[] PAINT =
        {
            new Color(0.75f, 0.75f, 0.76f), new Color(0.08f, 0.08f, 0.09f), new Color(0.92f, 0.92f, 0.9f), new Color(0.5f, 0.06f, 0.06f), new Color(0.1f, 0.18f, 0.38f),
            new Color(0.32f, 0.33f, 0.35f), new Color(0.55f, 0.56f, 0.58f), new Color(0.12f, 0.3f, 0.2f), new Color(0.6f, 0.5f, 0.35f), new Color(0.2f, 0.35f, 0.6f),
            new Color(0.85f, 0.4f, 0.1f), new Color(0.15f, 0.15f, 0.2f), new Color(0.7f, 0.12f, 0.1f),
        };

        sealed class Car { public Path path; public int dir; public float speed, offset, s0; public string kind; public Color paint; public GameObject go; public Vector3[] heads, tails; public bool far; }
        sealed class Walker { public Path path; public float s0, span, side, speed, phase; public int seed; public GameObject go; public MeshFilter[] filters; public Mesh[][] frames; public bool far; }
        sealed class Boat { public float cx, cz, rx, rz, w, ph, scale; public GameObject go; }
        /// <summary>A car standing on a house's drive (HouseModels): shown only near the camera, and cold on the thermal camera.</summary>
        sealed class ParkedCar { public HouseModels.Parked at; public GameObject go; }
        readonly List<ParkedCar> parked = new List<ParkedCar>();
        int parkedFrom = -1;
        const float PARKED_RANGE = 800;

        WorldActors actors;
        readonly List<Car> cars = new List<Car>();
        readonly List<Walker> walkers = new List<Walker>();
        readonly List<Boat> boats = new List<Boat>();
        Transform root;
        MeshFilter lights;
        readonly List<GlowPoint> glow = new List<GlowPoint>();
        float clock;
        int frame;
        HelicopterController heli;

        void Awake()
        {
            actors = GetComponent<WorldActors>();
            root = new GameObject("Ambient life").transform;
            root.SetParent(transform, false);
            lights = GlowPoints.Make("Traffic lights", root);
            var R = SimMath.Rng(77);
            float F() => (float)R();
            foreach (var road in Places.ROADS)
            {
                if (!ROADS.TryGetValue(road.name, out var cfg)) continue;
                var path = Densify(road.pts, (float)road.width);
                foreach (var dir in new[] { 1, -1 })
                {
                    var n = Mathf.Max(1, Mathf.RoundToInt(path.len / 100 * cfg.per100m));
                    var speed = cfg.speed * (0.9f + F() * 0.2f);
                    var gap = path.len / n;
                    for (int i = 0; i < n; i++)
                        cars.Add(new Car { path = path, dir = dir, speed = speed, offset = (float)road.width / 4, s0 = i * gap + F() * gap * 0.5f, kind = cfg.kinds[(int)(F() * cfg.kinds.Length)], paint = PAINT[(int)(F() * PAINT.Length)] });
                }
            }
            // pedestrians: on the pavements of the roads through each town, walking up and down a stretch
            for (int i = 0; i < 42; i++)
            {
                var town = Places.SETTLEMENTS[i % Places.SETTLEMENTS.Length];
                var cands = Places.ROADS.Where(r => r.pts.Any(p => Js.Hypot(p.x - town.x, p.z - town.z) < town.r)).ToArray();
                if (cands.Length == 0) continue;
                var road = cands[(int)(F() * cands.Length)];
                var path = Densify(road.pts, (float)road.width);
                float s0 = 0;
                for (int k = 0; k < 30; k++) { var s = F() * path.len; var p = path.At(s); if (Js.Hypot(p.x - town.x, p.z - town.z) < town.r * 0.8) { s0 = s; break; } }
                walkers.Add(new Walker { path = path, s0 = s0, span = 40 + F() * 90, side = F() < 0.5f ? -1 : 1, speed = 1.15f + F() * 0.4f, phase = F() * 100, seed = 300 + i * 7 });
            }
            // fishing boats working offshore
            for (int i = 0; i < 4; i++)
            {
                var z = -6000 + i * 2600 + F() * 600;
                boats.Add(new Boat { cx = (float)Rotorcraft.World.Terrain.CoastX(z) + 1500 + F() * 2500, cz = z, rx = 500 + F() * 600, rz = 300 + F() * 500, w = (1.6f + F()) / 600, ph = F() * 6.28f, scale = i % 2 == 1 ? 1.7f : 1 });
            }
        }

        /// <summary>Height of the road surface: the graded terrain, or the bridge deck.</summary>
        static float RoadY(float x, float z)
        {
            var b = Places.BRIDGE;
            if (Js.Hypot(x - b.x, z - b.z) < b.len / 2 + 2) return (float)RoadNet.BridgeDeck();
            return Mathf.Max((float)WorldMap.TerrainHeight(x, z), 0.4f);
        }

        static Vector3 U(float x, float y, float z) => new Vector3(x, y, -z);
        static Quaternion Facing(float fx, float fz, float pitch = 0) => Quaternion.LookRotation(new Vector3(fx, 0, -fz)) * Quaternion.Euler(-pitch * Mathf.Rad2Deg, 0, 0);

        GameObject MakeCar(Car c, bool running = true)
        {
            var g = actors.SpawnVehicle(c.kind, root);
            if (g == null) return null;
            foreach (var r in g.GetComponentsInChildren<Renderer>())
            {
                var mats = r.materials;
                foreach (var m in mats) if (m.name.StartsWith("RC paint")) { if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c.paint); m.color = c.paint; }
                r.materials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // lamps at the corners of the body, from its size (the front is +z)
            var b = new Bounds(Vector3.zero, Vector3.zero); var first = true;
            foreach (var r in g.GetComponentsInChildren<Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
            float hw = b.extents.x * 0.68f, y = b.min.y + Mathf.Min(0.75f, b.size.y * 0.42f);
            c.heads = new[] { new Vector3(-hw, y, b.max.z - 0.05f), new Vector3(hw, y, b.max.z - 0.05f) };
            c.tails = new[] { new Vector3(-hw, y + 0.1f, b.min.z + 0.05f), new Vector3(hw, y + 0.1f, b.min.z + 0.05f) };
            if (running) HeatSource.Add(g, HeatSource.Engine);
            return g;
        }

        /// <summary>Show the cars on the drives near the camera (they never move, so each is looked at now and then).</summary>
        void ParkedCars(float ex, float ez)
        {
            int count;
            lock (HouseModels.Drives) count = HouseModels.Drives.Count;
            if (count != parkedFrom)
            {
                foreach (var pc in parked) if (pc.go != null) Destroy(pc.go);
                parked.Clear();
                lock (HouseModels.Drives) foreach (var d in HouseModels.Drives) parked.Add(new ParkedCar { at = d });
                parkedFrom = count;
            }
            for (int i = 0; i < parked.Count; i++)
            {
                var pc = parked[i];
                if (pc.go != null && (i + frame) % OutOfRangeEvery != 0) continue;
                float x = (float)pc.at.x, z = (float)pc.at.z;
                var near = (x - ex) * (x - ex) + (z - ez) * (z - ez) < PARKED_RANGE * PARKED_RANGE;
                if (!near) { if (pc.go != null && pc.go.activeSelf) pc.go.SetActive(false); continue; }
                if (pc.go == null)
                {
                    pc.go = MakeCar(new Car { kind = pc.at.kind, paint = PAINT[pc.at.paint % PAINT.Length] }, false);
                    if (pc.go == null) { parked.RemoveAt(i--); continue; }
                    pc.go.name = "Parked " + pc.at.kind;
                    pc.go.transform.SetPositionAndRotation(U(x, (float)pc.at.y, z), Facing((float)System.Math.Sin(pc.at.heading), (float)System.Math.Cos(pc.at.heading)));
                }
                if (!pc.go.activeSelf) pc.go.SetActive(true);
            }
        }

        void MakeWalker(Walker w)
        {
            var frames = new Mesh[WALK_FRAMES][];
            for (int f = 0; f < WALK_FRAMES; f++) frames[f] = actors.FrameMeshes("pose_walk" + f);
            if (frames[0] == null || frames[0].Length == 0) return;
            w.go = actors.MakePerson("casual", "walk0", w.seed, root);
            if (w.go == null) return;
            w.filters = w.go.GetComponentsInChildren<MeshFilter>();
            w.frames = frames;
            foreach (var r in w.go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void Update()
        {
            if (actors == null) return;
            if (heli == null || !heli.isActiveAndEnabled) heli = FindAnyObjectByType<HelicopterController>();
            if (heli == null || !heli.Paused) clock += Time.deltaTime;
            var cam = Camera.main;
            if (cam == null) return;
            var eye = cam.transform.position;
            float ex = eye.x, ez = -eye.z, t = clock;
            var env = SkyEnvironment.Current;
            var night = env != null && env.Night > 0.5f;
            glow.Clear();
            frame++;

            for (int i = 0; i < cars.Count; i++)
            {
                var c = cars[i];
                // working out where a car is costs far more than drawing it, so what is far off moves less often: a car out of
                // sight is looked at twice a second, and one over a kilometre away, which only moves a pixel or so, four times less often
                var shown = c.go != null && c.go.activeSelf;
                if (!shown && (i + frame) % OutOfRangeEvery != 0) continue;
                if (shown && c.far && (i + frame) % FarEvery != 0)
                {
                    if (night) CarLights(c);
                    continue;
                }
                var s = (c.s0 + t * c.speed) % c.path.len;
                if (c.dir < 0) s = c.path.len - s;
                var p = c.path.At(s);
                float fx = p.dx * c.dir, fz = p.dz * c.dir;
                // keep left of the centre line
                float x = p.x + fz * c.offset, z = p.z - fx * c.offset;
                var d2 = (x - ex) * (x - ex) + (z - ez) * (z - ez);
                if (d2 >= RANGE * RANGE) { if (c.go != null && c.go.activeSelf) c.go.SetActive(false); continue; }
                c.far = d2 > FAR * FAR;
                if (c.go == null) { c.go = MakeCar(c); if (c.go == null) continue; }
                if (!c.go.activeSelf) c.go.SetActive(true);
                var y = RoadY(x, z);
                var pitch = Mathf.Atan2(RoadY(x + fx * 2, z + fz * 2) - RoadY(x - fx * 2, z - fz * 2), 4);
                c.go.transform.SetPositionAndRotation(U(x, y, z), Facing(fx, fz, pitch));
                if (night) CarLights(c);
            }

            for (int i = 0; i < walkers.Count; i++)
            {
                var w = walkers[i];
                var shown = w.go != null && w.go.activeSelf;
                if (!shown && (i + frame) % OutOfRangeEvery != 0) continue;
                if (shown && w.far && (i + frame) % FarEvery != 0) continue;
                var u = (t * w.speed + w.phase) % (w.span * 2);
                var back = u > w.span;
                var p = w.path.At(w.s0 + (back ? w.span * 2 - u : u));
                var off = (w.path.width / 2 + 1.8f) * w.side;
                float x = p.x - p.dz * off, z = p.z + p.dx * off;
                var d2 = (x - ex) * (x - ex) + (z - ez) * (z - ez);
                if (d2 >= PED_RANGE * PED_RANGE) { if (w.go != null && w.go.activeSelf) w.go.SetActive(false); continue; }
                w.far = d2 > FAR_WALKER * FAR_WALKER;
                if (w.go == null) { MakeWalker(w); if (w.go == null) continue; }
                if (!w.go.activeSelf) w.go.SetActive(true);
                float fx = back ? -p.dx : p.dx, fz = back ? -p.dz : p.dz;
                var pose = Mathf.FloorToInt((t * w.speed + w.phase) / STRIDE % 1 * WALK_FRAMES) % WALK_FRAMES;
                SetFrame(w.filters, w.frames[pose]);
                w.go.transform.SetPositionAndRotation(U(x, Mathf.Max((float)WorldMap.TerrainHeight(x, z), 0.4f), z), Facing(fx, fz));
            }

            ParkedCars(ex, ez);

            foreach (var b in boats)
            {
                var a = b.ph + t * b.w;
                float x = b.cx + Mathf.Cos(a) * b.rx, z = b.cz + Mathf.Sin(a) * b.rz;
                var near = (x - ex) * (x - ex) + (z - ez) * (z - ez) < RANGE * RANGE * 4;
                if (!near) { if (b.go != null && b.go.activeSelf) b.go.SetActive(false); continue; }
                if (b.go == null)
                {
                    b.go = actors.SpawnProp("boat", root);
                    if (b.go == null) continue;
                    b.go.transform.localScale = Vector3.one * b.scale;
                    HeatSource.Add(b.go, HeatSource.Engine);
                }
                if (!b.go.activeSelf) b.go.SetActive(true);
                float vx = -Mathf.Sin(a) * b.rx, vz = Mathf.Cos(a) * b.rz;
                var roll = Mathf.Sin(t * 0.83f + b.ph) * 2.5f;
                b.go.transform.SetPositionAndRotation(U(x, Mathf.Sin(t * 0.9f + b.ph) * 0.15f, z), Facing(vx, vz, Mathf.Sin(t * 0.7f + b.ph) * 0.02f) * Quaternion.Euler(0, 0, roll));
                if (night)
                {
                    var tr = b.go.transform;
                    glow.Add(new GlowPoint { pos = tr.TransformPoint(new Vector3(0, 3.6f, 1.2f)), color = new Color(1.1f, 1.05f, 0.95f, 0), size = 0.45f, mode = LightMode.Steady, period = 1 });
                    glow.Add(new GlowPoint { pos = tr.TransformPoint(new Vector3(0.8f, 2.6f, -0.6f)), color = new Color(0.1f, 1.2f, 0.3f, 0), size = 0.3f, mode = LightMode.Steady, period = 1 });
                    glow.Add(new GlowPoint { pos = tr.TransformPoint(new Vector3(-0.8f, 2.6f, -0.6f)), color = new Color(1.2f, 0.05f, 0.03f, 0), size = 0.3f, mode = LightMode.Steady, period = 1 });
                }
            }
            lights.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            GlowPoints.Fill(lights.sharedMesh, glow);
        }

        void CarLights(Car c)
        {
            var tr = c.go.transform;
            foreach (var h in c.heads) glow.Add(new GlowPoint { pos = tr.TransformPoint(h), color = new Color(1.0f, 0.95f, 0.82f, 0), size = 0.32f, mode = LightMode.Steady, period = 1 });
            foreach (var h in c.tails) glow.Add(new GlowPoint { pos = tr.TransformPoint(h), color = new Color(1.0f, 0.05f, 0.03f, 0), size = 0.22f, mode = LightMode.Steady, period = 1 });
        }

        /// <summary>For the screenshot tour: where the nearest shown car, walker or boat is (sim x, z), if any.</summary>
        public Vector2? Nearest(string kind, Vector2 sim)
        {
            IEnumerable<GameObject> gos = kind == "car" ? cars.Select(c => c.go) : kind == "walker" ? walkers.Select(w => w.go) : boats.Select(b => b.go);
            Vector2? best = null; float bd = float.MaxValue;
            foreach (var g in gos)
            {
                if (g == null || !g.activeSelf) continue;
                var p = new Vector2(g.transform.position.x, -g.transform.position.z);
                var d = Vector2.Distance(p, sim);
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }

        /// <summary>Show one frame of a walk: each part of the figure gets that frame's mesh.</summary>
        public static void SetFrame(MeshFilter[] filters, Mesh[] frame)
        {
            if (filters == null || frame == null) return;
            for (int i = 0; i < filters.Length && i < frame.Length; i++) if (filters[i].sharedMesh != frame[i]) filters[i].sharedMesh = frame[i];
        }
    }
}
