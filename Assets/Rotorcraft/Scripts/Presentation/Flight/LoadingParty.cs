// The people a load brings to the aircraft and takes from it: passengers walking out and aboard, or off and away (in survival
// suits offshore); workers carrying boxes between a stack and the door; a stretcher team with the patient and whoever goes with
// them, an ambulance waiting on land; soldiers and armed police filing aboard at a jog. Driven by the mission's load objective:
// where everyone is follows how far the loading has got, so it always ends as the load does, and lifting before it is secured
// starts it again. Purely visual, like the crew on foot.
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Rotorcraft.Game;
using Rotorcraft.Sim;
using Rotorcraft.World;
using UnityEngine;

namespace Rotorcraft.Presentation
{
    public sealed class LoadingParty : MonoBehaviour
    {
        const float WALK = 1.35f, JOG = 2.3f, CARRY = 1.05f, STRETCHER_HALF = 1.05f;

        enum Carry { None, Box, Stretcher }

        sealed class Figure
        {
            public string outfit; public int seed;
            public Npc npc;
            /// <summary>The walking clip: walk, run (soldiers and armed police), injured_walk (a walker helped aboard).</summary>
            public string gait = "walk";
        }

        sealed class Unit
        {
            public Figure a, b;                     // b: the second stretcher carrier
            public Carry carry;
            public bool inbound, roundTrip;         // inbound: towards the door; a round trip goes there and back
            public float t0, t1, speed;             // seconds into the load
            public Vector3 from, to, rest;          // on the ground: the far end, the door, and where someone off the aircraft stops
            public Vector3[] path;                  // from, the turns on the way (round the aircraft, never past the tail), to
            public float length;                    // along the path
            public float side;                      // walks this far to the side of the way (a companion beside the stretcher)
            public GameObject load;                 // box, or stretcher with the patient on it
            public Vector2? takeUpSim;              // the casualty figure lying there, taken up when the team gets to them
        }

        HelicopterController heli;
        WorldActors actors;
        ModelAnimator animator;
        Transform root;
        readonly List<Unit> units = new List<Unit>();
        readonly List<GameObject> scenery = new List<GameObject>();
        ObjectiveRuntime current;
        float lastTimer, lingering;
        string doorGroup = "cabin_r";
        Vector3 doorAt;
        float doorSide = 1;     // +1: the door is on the aircraft's right
        bool onDeck;

        void Awake() { heli = GetComponent<HelicopterController>(); }

        void OnDestroy() { if (root != null) Destroy(root.gameObject); }

        /// <summary>Each frame from the flight session: the active objective, or null.</summary>
        public void Show(ObjectiveRuntime active)
        {
            if (heli == null || heli.State == null) return;
            if (actors == null) actors = FindAnyObjectByType<WorldActors>();
            if (animator == null) animator = GetComponent<ModelAnimator>();
            if (root == null) root = new GameObject("Loading party").transform;
            var load = active != null && active.def.kind == ObjectiveKind.Load ? active : null;
            var s = heli.State;
            if (load != null && load != current)
            {
                Clear();
                current = load;
            }
            if (load == null)
            {
                // the load is done: whoever came off stays where they stopped until the aircraft has gone
                if (current != null) { current = null; lingering = 0; foreach (var u in units) if (u.inbound && !u.roundTrip) Hide(u); }
                if (units.Count == 0) return;
                lingering += heli.Paused ? 0 : Time.deltaTime;
                if (lingering > 90 || (!s.onGround && Vector3.Distance(transform.position, units[0].from) > 120)) { Clear(); return; }
                foreach (var u in units) if (!u.inbound || u.roundTrip) Pose(u, u.t1 + 1);
                return;
            }
            var t = (float)(load.progress * load.def.seconds);
            if (units.Count == 0 && t > 0 && s.onGround) Cast(load.def);
            if (units.Count == 0) return;
            if (t < lastTimer - 0.5f) { Clear(); current = load; lastTimer = 0; return; }   // lifted before it was secured: it starts again
            lastTimer = t;
            foreach (var u in units)
            {
                if (u.takeUpSim != null && t >= u.t0 + (u.t1 - u.t0) * (u.inbound ? 0 : 0.5f)) { actors?.HideNear(u.takeUpSim.Value, 3.5f); u.takeUpSim = null; }
                Pose(u, t);
            }
            if (animator != null) animator.crewHeld.Add(doorGroup);
        }

        // ------------------------------------------------------------------------------------- who comes
        void Cast(ObjectiveDef d)
        {
            if (actors == null) return;
            var what = (d.what ?? "").ToLowerInvariant();
            var ac = heli.Aircraft;
            var site = d.siteId != null ? WorldMap.SITES.Find(x => x.id == d.siteId) : null;
            var deck = site != null && (site.kind == SiteKind.Helideck || site.deckHeight != null);
            var offshore = site != null && site.kind == SiteKind.Helideck;
            var patient = what.Contains("patient") || what.Contains("casualty");
            var nightjarRear = patient && ac.id == "nightjar";
            var doorLocal = nightjarRear ? new Vector3(0, 0, 2.9f) : CrewOnFoot.CabinDoor(ac.id);
            doorGroup = nightjarRear ? "rear" : CrewOnFoot.CabinDoorGroup(ac.id);
            var door = Beside(doorLocal);
            var right = transform.right; right.y = 0; right.Normalize();
            var fwd = transform.forward; fwd.y = 0; fwd.Normalize();
            // everyone comes and goes on the door's side, the Nightjar's stretcher too (to its rear doors from the side, never up the tail)
            doorSide = Mathf.Sign(doorLocal.x == 0 ? 1 : doorLocal.x);
            doorAt = door; onDeck = deck;
            var outward = right * doorSide;
            var gatherDist = deck ? 7.5f : 14f;
            var gather = OnGround(door + outward * gatherDist + fwd * 1.5f);
            var seconds = (float)d.seconds;
            var seed = Mathf.Abs((d.what ?? "").GetHashCode() % 1000);

            // a casualty already lying here (a scene in freeplay, a walker who waved us down) is who the team goes to
            Vector2? lying = null;
            if (d.pickup && (patient || what.Contains("walker")))
            {
                float best = 70;
                foreach (var p in actors.Patients)
                {
                    var u = new Vector3(p.x, 0, -p.y);
                    var dd = Vector2.Distance(new Vector2(u.x, u.z), new Vector2(door.x, door.z));
                    if (dd < best) { best = dd; lying = p; }
                }
            }

            int people = Count(what);
            if (what.Contains("net six on")) { Group(2, false); Group(8, true); }
            else if (patient) Stretcher();
            else if (what.Contains("soldier")) Group(people > 0 ? people : 6, d.pickup, "soldier", JOG, "run");
            else if (what.Contains("firearms")) Group(4, d.pickup, "armed", JOG, "run");
            else if (what.Contains("walker")) Group(1, d.pickup, "walker", WALK, "injured_walk");
            else if (what.Contains("adviser")) Group(1, d.pickup, "hivis");
            else if (what.Contains("observer")) { Group(1, d.pickup, "police"); Boxes(1, d.pickup); }
            else if (what.Contains("passenger"))
            {
                Group(people > 0 ? people : 2, d.pickup);
                if (what.Contains("freight")) Boxes(3, d.pickup);
            }
            else Boxes(Mathf.Clamp(Mathf.RoundToInt((float)d.payloadKg / 60f), 2, 6), d.pickup);
            foreach (var u in units) Route(u);
            Schedule(seconds);

            void Group(int n, bool inbound, string outfit = null, float speed = WALK, string gait = "walk")
            {
                for (int i = 0; i < n; i++)
                {
                    var o = outfit ?? (offshore ? "immersion" : "casual");
                    var u = new Unit { a = new Figure { outfit = o, seed = seed + i * 7, gait = gait }, inbound = inbound, speed = speed };
                    var queue = gather + outward * (i % 3) * 0.9f + fwd * ((i / 3) - 1) * 0.9f;
                    if (lying != null && n == 1) queue = Unity(lying.Value);
                    u.from = inbound ? OnGround(queue) : door;
                    u.to = inbound ? door : OnGround(queue + outward * 2.5f);
                    u.rest = u.to;
                    if (lying != null && n == 1 && inbound) u.takeUpSim = lying;
                    units.Add(u);
                }
            }

            void Boxes(int n, bool pickup)
            {
                // two workers carry them between a stack and the door, one box a trip
                var o = offshore ? "worker" : "hivis";
                for (int i = 0; i < n; i++)
                {
                    var u = new Unit { a = new Figure { outfit = o, seed = seed + 50 + (i % 2) }, carry = Carry.Box, inbound = pickup, roundTrip = true, speed = CARRY };
                    var stack = OnGround(gather - fwd * 2.2f + outward * (i % 2) * 0.8f);
                    u.from = stack; u.to = door; u.rest = stack;
                    units.Add(u);
                }
            }

            void Stretcher()
            {
                // the stretcher team: two carriers and the patient, from the casualty or the ambulance to the door (or the other way),
                // and whoever travels with the patient alongside
                var start = lying != null && d.pickup ? Unity(lying.Value) : gather;
                var team = new Unit
                {
                    a = new Figure { outfit = deck ? "worker" : "medic", seed = seed + 11 },
                    b = new Figure { outfit = deck ? "worker" : "medic", seed = seed + 12 },
                    carry = Carry.Stretcher, inbound = d.pickup, roundTrip = true, speed = CARRY,
                    from = OnGround(start), to = door, rest = OnGround(start),
                    takeUpSim = lying != null && d.pickup ? lying : null,
                };
                units.Add(team);
                var with = what.Contains("nurse") ? "scrubs" : what.Contains("paramedic") || what.Contains("medic") ? "medic" : what.Contains("doctor") ? "scrubs" : null;
                if (with != null) units.Add(new Unit { a = new Figure { outfit = with, seed = seed + 13 }, inbound = d.pickup, speed = CARRY, from = d.pickup ? team.from : door, to = d.pickup ? door : team.rest, rest = team.rest, side = 0.9f });
                // an ambulance waits on land
                if (!deck && actors != null && !actors.AnyPropNear("v:ambulance", gather, 35))
                {
                    var v = actors.SpawnVehicle("ambulance", root);
                    if (v != null)
                    {
                        var at = OnGround(gather + outward * 6f);
                        v.transform.SetPositionAndRotation(at, Quaternion.LookRotation(fwd));
                        HeatSource.Add(v, HeatSource.Engine);
                        scenery.Add(v);
                    }
                }
            }
        }

        /// <summary>The way between a unit's far end and the door, through the turns Approach gives.</summary>
        void Route(Unit u)
        {
            var toDoor = (u.to - doorAt).sqrMagnitude < 0.01f;
            var vias = onDeck ? new List<Vector3>() : Approach(transform, (float)heli.Aircraft.rotorDiameter, doorAt, doorSide, toDoor ? u.from : u.to);   // a deck has no room to go round: it is all beside the door anyway
            if (!toDoor) vias.Reverse();
            var pts = new List<Vector3> { u.from }; pts.AddRange(vias); pts.Add(u.to);
            u.path = pts.ToArray();
            u.length = 0;
            for (int i = 1; i < u.path.Length; i++) u.length += Vector3.Distance(u.path[i - 1], u.path[i]);
        }

        /// <summary>
        /// The turns on the way from a place on the ground to the door. The last stretch always comes straight in from the side,
        /// abeam the door, where the pilot can see them and well clear of the tail rotor: from behind, out wide behind the tail first;
        /// from the far side, round the nose.
        /// </summary>
        public static List<Vector3> Approach(Transform aircraft, float rotorDiameter, Vector3 door, float doorSide, Vector3 far)
        {
            var vias = new List<Vector3>();
            var d = aircraft.InverseTransformPoint(door);
            var f = aircraft.InverseTransformPoint(far);
            float du = d.x * doorSide, fu = f.x * doorSide, fz = f.z;   // across: + out on the door's side
            var wide = du + 8f;                                        // where the last stretch in starts
            var zFront = rotorDiameter * 0.5f + 1.5f;                  // ahead of the nose
            var zBehind = -(rotorDiameter * 0.65f + 3f);               // behind the tail rotor
            Vector3 P(float across, float along) => OnGround(aircraft.TransformPoint(new Vector3(across * doorSide, 0, along)));
            if (fu >= du - 0.5f && fz >= d.z - 1.5f && fz <= d.z + 4) return vias;    // already abeam the door, or just ahead of it
            if (fu >= wide * 0.6f && fz >= d.z - 1.5f) { vias.Add(P(wide, d.z)); return vias; }   // out on the door's side: straight in
            if (fz < zBehind || fu >= 0)
            {
                // behind the tail, or on the door's side: out to the side first (where they are, if far enough out), along to abeam the door, then in
                var lateral = Mathf.Max(fu, du + 4f);
                if (lateral > fu + 0.3f) vias.Add(P(lateral, fz));
                vias.Add(P(lateral, d.z));
                return vias;
            }
            // the far side: out from the aircraft, forward past the nose, across and in
            var across = Mathf.Min(fu, -wide * 0.6f);
            if (fz < zFront)
            {
                if (across < fu - 0.5f) vias.Add(P(across, fz));
                vias.Add(P(across, zFront));
                vias.Add(P(wide, zFront));
            }
            else vias.Add(P(wide, fz));
            vias.Add(P(wide, d.z));
            return vias;
        }

        /// <summary>Where along its way a unit is at a fraction of it, and which way the way runs there.</summary>
        static Vector3 Along(Unit u, float k, out Vector3 dir)
        {
            var p = u.path;
            var want = Mathf.Clamp01(k) * u.length;
            for (int i = 1; i < p.Length; i++)
            {
                var seg = Vector3.Distance(p[i - 1], p[i]);
                if (want <= seg || i == p.Length - 1) { dir = p[i] - p[i - 1]; return Vector3.Lerp(p[i - 1], p[i], seg > 1e-4f ? Mathf.Clamp01(want / seg) : 1); }
                want -= seg;
            }
            dir = u.to - u.from;
            return u.to;
        }

        /// <summary>Times for everyone, so that the load ends as the timer does: off first, then on, one after another.</summary>
        void Schedule(float seconds)
        {
            var end = seconds * 0.9f;
            float cursor = 1.5f;
            foreach (var pass in new[] { false, true })
            {
                var batch = units.FindAll(u => u.inbound == pass);
                if (batch.Count == 0) continue;
                // how long each takes at its own pace, there and back for a round trip
                float Dur(Unit u) => u.length / u.speed * (u.roundTrip ? 2 : 1) + (u.roundTrip ? 1.5f : 0);
                var longest = 0f; foreach (var u in batch) longest = Mathf.Max(longest, Dur(u));
                var room = (pass ? end : (units.Exists(u => u.inbound) ? end * 0.45f : end)) - cursor;
                var gap = batch.Count > 1 ? Mathf.Clamp((room - longest) / (batch.Count - 1), 0.8f, 4f) : 0;
                // companions go with the stretcher, not after it
                var t = cursor;
                Unit lastTeam = null;
                foreach (var u in batch)
                {
                    var dur = Dur(u);
                    if (u.carry == Carry.None && lastTeam != null && u.side != 0)
                    {
                        // beside the stretcher: the half of its trip with the patient
                        var half = (lastTeam.t1 - lastTeam.t0) / 2;
                        u.t0 = u.inbound ? lastTeam.t0 : lastTeam.t0 + half; u.t1 = u.t0 + half;
                        continue;
                    }
                    var squeeze = Mathf.Min(1, (end - t) / Mathf.Max(0.1f, dur));
                    u.t0 = t; u.t1 = t + dur * Mathf.Max(0.5f, squeeze);
                    if (u.carry == Carry.Stretcher) lastTeam = u;
                    t += u.roundTrip && u.carry == Carry.Box ? dur * 0.5f : gap;
                }
                cursor = t + 1;
            }
        }

        // ------------------------------------------------------------------------------------- where they are now
        void Pose(Unit u, float t)
        {
            var k = Mathf.Clamp01((t - u.t0) / Mathf.Max(0.01f, u.t1 - u.t0));
            Vector3 pos; Vector3 dir; bool moving, shown, carrying;
            if (u.roundTrip)
            {
                // there and back: the first half to the door, the second home again
                var there = k < 0.5f;
                var leg = there ? k * 2 : (k - 0.5f) * 2;
                pos = Along(u, there ? leg : 1 - leg, out dir);
                if (!there) dir = -dir;
                moving = k > 0 && k < 1;
                shown = true;
                carrying = u.inbound ? there && k > 0 : !there && k < 1;
                if (k >= 1) carrying = !u.inbound && u.carry == Carry.Stretcher;   // the hospital team stands there with the patient
                if (k <= 0) carrying = u.inbound && u.carry == Carry.Stretcher;    // and the casualty's team waits ready to lift
                if (!moving) dir = transform.position - pos;
            }
            else
            {
                pos = Along(u, k, out dir);
                if (u.side != 0)
                {
                    // beside the stretcher, on the side away from the aircraft, closing in at the door
                    var across = Vector3.Cross(Vector3.up, new Vector3(dir.x, 0, dir.z)).normalized;
                    if (Vector3.Dot(across, pos - transform.position) < 0) across = -across;
                    pos += across * u.side * Mathf.Sin(Mathf.PI * Mathf.Min(1, k * 1.2f));
                }
                moving = k > 0 && k < 1;
                shown = u.inbound ? k < 1 : k > 0;
                carrying = false;
                if (!moving) dir = transform.position - pos;
            }
            dir.y = 0;
            if (dir.sqrMagnitude < 1e-4f) dir = transform.forward;
            var face = Quaternion.LookRotation(dir.normalized);
            var walked = u.length * (u.roundTrip ? k * 2 : k);
            if (u.carry == Carry.Stretcher)
            {
                // the carriers at either end, the stretcher between them at hand height, out of step with each other
                var along = face * Vector3.forward;
                Place(u.a, pos + along * STRETCHER_HALF, face, shown, moving, walked, carrying);
                Place(u.b, pos - along * STRETCHER_HALF, face, shown, moving, walked + 0.5f * (u.b.npc != null ? u.b.npc.Stride(CARRY_CLIP) : 1.6f), carrying);
                Load(u, carrying && shown, pos, face, 0.82f);
            }
            else
            {
                Place(u.a, pos, face, shown, moving, walked, carrying);
                // the carry clip holds the hands down at the sides (as for a stretcher's end handles), so a box is carried at the side,
                // long way forward, its top at the hand
                if (u.carry == Carry.Box) Load(u, carrying && shown, pos + face * new Vector3(0.36f, 0, 0.04f), face * Quaternion.Euler(0, 90, 0), 0.6f);
            }
        }

        const string CARRY_CLIP = "stretcher_carry_walk";

        /// <summary>One person of a unit: walking (in step with the ground covered), carrying (hands forward on the handles or the box),
        /// crouched under turning blades, or standing; a carrier standing with the load keeps hold of it.</summary>
        void Place(Figure f, Vector3 pos, Quaternion face, bool shown, bool moving, float walked, bool carrying)
        {
            if (f == null) return;
            if (shown) Ensure(f);
            if (f.npc == null) return;
            if (f.npc.gameObject.activeSelf != shown) f.npc.gameObject.SetActive(shown);
            if (!shown) return;
            var s = heli.State;
            var under = s.@out.rpm > 30 && Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(transform.position.x, transform.position.z)) < heli.Aircraft.rotorDiameter * 0.62f;
            if (carrying) f.npc.Cycle(CARRY_CLIP, moving ? walked / f.npc.Stride(CARRY_CLIP) : 0);
            else if (moving)
            {
                var clip = under && f.gait != "injured_walk" ? "crouch_walk" : f.gait;
                f.npc.Cycle(clip, walked / f.npc.Stride(clip));
            }
            else f.npc.Play("idle", 0.3f);
            f.npc.transform.SetPositionAndRotation(OnGround(pos), face);
        }

        void Load(Unit u, bool shown, Vector3 pos, Quaternion face, float height)
        {
            if (!shown) { if (u.load != null) u.load.SetActive(false); return; }
            if (u.load == null) u.load = u.carry == Carry.Stretcher ? MakeStretcher(u.a.seed) : MakeBox();
            if (u.load == null) return;
            u.load.SetActive(true);
            pos.y = Ground(pos) + height;
            u.load.transform.SetPositionAndRotation(pos, face);
        }

        GameObject MakeStretcher(int seed)
        {
            var holder = new GameObject("Stretcher with patient");
            holder.transform.SetParent(root, false);
            // laid along the way it is carried (the holder's z) whichever way the model was made, its underside at the holder, which is
            // carried at hand height. The model has the patient on it already, under a blanket.
            var bed = actors.SpawnProp("stretcher", holder.transform);
            if (bed != null) LayAlong(bed, -0.05f);
            return holder;
        }

        GameObject MakeBox()
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(g.GetComponent<Collider>());
            g.name = "Box";
            g.transform.SetParent(root, false);
            g.transform.localScale = new Vector3(0.46f, 0.34f, 0.36f);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null) { var m = new Material(shader); m.SetColor("_BaseColor", new Color(0.62f, 0.48f, 0.3f)); m.SetFloat("_Smoothness", 0.15f); g.GetComponent<Renderer>().sharedMaterial = m; }
            return g;
        }

        /// <summary>Turn a model to lie along its parent's z, centred over it with its underside at `bottom`; returns the height of its top.</summary>
        static float LayAlong(GameObject g, float bottom)
        {
            g.transform.localPosition = Vector3.zero; g.transform.localRotation = Quaternion.identity;
            var b = Bounds(g);
            if (b.size.x > b.size.z) { g.transform.localRotation = Quaternion.Euler(0, 90, 0); b = Bounds(g); }
            g.transform.localPosition = new Vector3(-b.center.x, bottom - b.min.y, -b.center.z);
            return bottom + b.size.y;
        }

        static Bounds Bounds(GameObject g)
        {
            var rs = g.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(g.transform.position, Vector3.zero);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            b.center -= g.transform.position;
            return b;
        }

        void Ensure(Figure f)
        {
            if (f.npc != null || actors == null) return;
            f.npc = actors.MakePerson(f.outfit, "stand", f.seed, root);
        }

        void Hide(Unit u)
        {
            foreach (var f in new[] { u.a, u.b }) if (f?.npc != null) f.npc.gameObject.SetActive(false);
            if (u.load != null) u.load.SetActive(false);
            u.t0 = u.t1 = float.MaxValue;
        }

        /// <summary>The flight is over: everyone goes.</summary>
        public void Reset() { Clear(); current = null; }

        void Clear()
        {
            if (root != null) foreach (Transform c in root) Destroy(c.gameObject);
            units.Clear(); scenery.Clear(); lastTimer = 0;
        }

        // ------------------------------------------------------------------------------------- the ground about the aircraft
        static readonly Regex Number = new Regex(@"\b(one|two|three|four|five|six|seven|eight|nine|ten|\d+)\b");
        static int Count(string what)
        {
            var m = Number.Match(what);
            if (!m.Success) return 0;
            var words = new[] { "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten" };
            var i = System.Array.IndexOf(words, m.Value);
            return i >= 0 ? i + 1 : int.TryParse(m.Value, out var n) ? Mathf.Clamp(n, 1, 12) : 0;
        }

        Vector3 Beside(Vector3 local)
        {
            var p = transform.TransformPoint(new Vector3(local.x, 0, -local.z));
            return OnGround(p);
        }

        static Vector3 Unity(Vector2 sim) => new Vector3(sim.x, 0, -sim.y);
        static Vector3 OnGround(Vector3 p) { p.y = Ground(p); return p; }
        static float Ground(Vector3 p) => NpcLibrary.Ground(p.x, p.z);
    }
}
