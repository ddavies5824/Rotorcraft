// The crew on foot (port of src/render/crew.ts). After a landing the people aboard get out and walk: at an
// incident the paramedic and the winchman go to the casualty and kneel beside them; after shutting down,
// everyone, pilot included, walks away from the aircraft (towards the hangar at a base). They wait for their
// door to open, duck under turning blades, leave and return clear of the tail rotor, and hurry back aboard
// when the pilot raises the collective or starts up. Purely visual: the flight model and the mission rules do
// not depend on it.
using System.Collections.Generic;
using System.Linq;
using Rotorcraft.Sim;
using Rotorcraft.World;
using UnityEngine;

namespace Rotorcraft.Presentation
{
    public sealed class CrewOnFoot : MonoBehaviour
    {
        const float WALK = 1.45f, RUN = 2.6f;

        /// <summary>Where each door group lets people out, in the model's axes (x right, z aft): the pilot's door and the others' door.</summary>
        static readonly Dictionary<string, ((Vector3 pos, string group) pilot, (Vector3 pos, string group) others)> DOORS = new Dictionary<string, ((Vector3, string), (Vector3, string))>
        {
            ["kestrel"] = ((new Vector3(0.8f, 0, 0.13f), "crew_r"), (new Vector3(-0.8f, 0, 0.13f), "crew_l")),
            ["northstar"] = ((new Vector3(1.25f, 0, -2.7f), "crew_r"), (new Vector3(1.25f, 0, -1.7f), "cabin_r")),
            ["guardian"] = ((new Vector3(1.4f, 0, -3.0f), "crew_r"), (new Vector3(1.4f, 0, -0.55f), "cabin_r")),
            ["nightjar"] = ((new Vector3(1.05f, 0, -1.9f), "crew_r"), (new Vector3(1.05f, 0, -0.46f), "cabin_r")),
        };

        enum State { Aboard, Leaving, Out, There, Back, Boarding }
        sealed class Member
        {
            public string outfit, role; public int seed;
            public State state; public Vector3 pos; public Vector3 face = Vector3.forward;
            public List<Vector3> path = new List<Vector3>(); public float phase; public bool kneel, toCasualty;
            public string door; public float doorT = 99;
            public GameObject walk, stand, kneelGo; public MeshFilter[] walkFilters; public Mesh[][] frames;
        }

        HelicopterController heli;
        WorldActors actors;
        ModelAnimator animator;
        readonly List<Member> crew = new List<Member>();
        Transform root;
        string aircraftId;
        float airFor, landedFor, shutFor;
        bool flown;

        void Start() => Configure();

        void OnDestroy() { if (root != null) Destroy(root.gameObject); }

        /// <summary>Who is aboard, from the aircraft and its kit.</summary>
        public void Configure()
        {
            if (heli == null) heli = GetComponent<HelicopterController>();
            if (heli == null || heli.Aircraft == null) return;
            if (root == null)
            {
                root = new GameObject("Crew on foot").transform;
                heli.WasReset += () => { flown = false; foreach (var m in crew) m.state = State.Aboard; };
            }
            foreach (var m in crew) foreach (var g in new[] { m.walk, m.stand, m.kneelGo }) if (g != null) Destroy(g);
            crew.Clear();
            aircraftId = heli.Aircraft.id;
            var ac = heli.Aircraft;
            crew.Add(new Member { outfit = "aircrew", role = "pilot", seed = 81 });
            if (ac.engines > 1) crew.Add(new Member { outfit = "aircrew", role = "crew", seed = 82 });
            if (ac.equipment.medical) crew.Add(new Member { outfit = "medic", role = "medic", seed = 83 });
            if (ac.equipment.hoist) crew.Add(new Member { outfit = "winchman", role = "winchman", seed = 84 });
            flown = false; airFor = landedFor = shutFor = 0;
        }

        ((Vector3 pos, string group) pilot, (Vector3 pos, string group) others) Doors()
        {
            if (DOORS.TryGetValue(aircraftId ?? "", out var d)) return d;
            return DOORS["northstar"];
        }

        /// <summary>A model-axes point (x right, z aft) on the ground beside the aircraft, in Unity space.</summary>
        Vector3 Beside(Vector3 local)
        {
            var p = transform.TransformPoint(new Vector3(local.x, 0, -local.z));
            p.y = Ground(p);
            return p;
        }

        static float Ground(Vector3 p) => (float)WorldMap.GroundAt(p.x, -p.z).h;

        void Ensure(Member m)
        {
            if (m.walk != null || actors == null) return;
            var helmet = m.outfit == "aircrew" || m.outfit == "winchman";
            var prefix = helmet ? "helmet_walk" : "pose_walk";
            m.frames = new Mesh[AmbientLife.WALK_FRAMES][];
            for (int f = 0; f < AmbientLife.WALK_FRAMES; f++) m.frames[f] = actors.FrameMeshes(prefix + f);
            m.walk = actors.MakePerson(m.outfit, "walk0", m.seed, root);
            m.stand = actors.MakePerson(m.outfit, "stand", m.seed, root);
            m.kneelGo = actors.MakePerson(m.outfit, "kneel", m.seed, root);
            if (m.walk != null) m.walkFilters = m.walk.GetComponentsInChildren<MeshFilter>();
            foreach (var g in new[] { m.walk, m.stand, m.kneelGo }) if (g != null) g.SetActive(false);
        }

        void LateUpdate()
        {
            if (heli == null || heli.State == null || crew.Count == 0) return;
            if (actors == null) actors = FindAnyObjectByType<WorldActors>();
            if (animator == null) animator = heli.GetComponent<ModelAnimator>();
            var s = heli.State; var ac = heli.Aircraft;
            var dt = heli.Paused ? 0 : Time.deltaTime;
            float rpm = (float)(s.@out.rpm / 100), lift = (float)(s.@out.thrustN / (s.@out.mass * 9.81 + 1)), R = (float)ac.rotorDiameter / 2;
            if (s.crashed) { Hide(); return; }

            // airborne only after half a second off the ground (a skid bouncing on touchdown is not a take-off)
            airFor = s.onGround ? 0 : airFor + dt;
            var air = airFor > 0.5f;
            if (air) flown = true;
            var settled = flown && !air && lift < 0.55f;
            landedFor = settled ? landedFor + dt : 0;
            shutFor = settled && rpm < 0.12f ? shutFor + dt : 0;
            var lifting = air || lift > 0.7f;
            if (air) foreach (var m in crew) m.state = State.Aboard;

            var here = transform.position;
            Vector3? patient = null;
            if (actors != null)
                foreach (var p in actors.Patients)
                {
                    var u = new Vector3(p.x, 0, -p.y);
                    var d = Vector2.Distance(new Vector2(u.x, u.z), new Vector2(here.x, here.z));
                    if (d < 160 && (patient == null || d < Vector2.Distance(new Vector2(patient.Value.x, patient.Value.z), new Vector2(here.x, here.z)))) patient = u;
                }
            var hangar = WorldMap.STRUCTURES.FirstOrDefault(st => st.kind == StructureKind.Hangar && Js.Hypot(st.x - here.x, st.z + here.z) < 250);
            var doors = Doors();
            var right = transform.right; right.y = 0; right.Normalize();
            var fwd = transform.forward; fwd.y = 0; fwd.Normalize();

            for (int idx = 0; idx < crew.Count; idx++)
            {
                var m = crew[idx];
                var pilot = m.role == "pilot";
                var (doorLocal, group) = pilot ? doors.pilot : doors.others;
                var doorPt = Beside(doorLocal + new Vector3(0, 0, pilot ? 0 : (idx - 1) * 0.3f));
                var side = Mathf.Sign(doorLocal.x);
                m.door = group;

                // who goes out, and where to
                var toCasualty = (m.role == "medic" || m.role == "winchman") && patient != null && landedFor > 2.5f;
                var shutdown = shutFor > 3 + idx * 1.2f;
                if (m.state == State.Aboard && !lifting && (toCasualty || shutdown))
                {
                    m.state = State.Leaving; m.toCasualty = toCasualty; m.pos = doorPt;
                    // step out sideways, clear of the rotor and well away from the tail, then go to the goal
                    var clear = here + right * side * R * 1.15f + fwd * R * 0.35f;
                    Vector3 goal;
                    if (toCasualty) { var a = idx * 1.7f; goal = patient.Value + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 1.1f; m.kneel = true; }
                    else if (hangar != null) { goal = new Vector3((float)hangar.x + (idx - 1) * 1.6f, 0, -(float)(hangar.z + hangar.d / 2 + 4 + (idx % 2) * 1.3)); m.kneel = false; }
                    else { goal = clear + (clear - here) * 0.8f + fwd * idx * 1.4f; m.kneel = false; }
                    m.path = new List<Vector3> { clear, goal };
                }
                // back aboard: when the pilot lifts, when the engines start again after a shutdown, or when the casualty has gone
                var recall = lifting || (!m.toCasualty && rpm > 0.15f) || (m.toCasualty && patient == null);
                if ((m.state == State.Out || m.state == State.There || m.state == State.Leaving) && recall)
                {
                    m.state = State.Back;
                    var near = Vector2.Distance(new Vector2(m.pos.x, m.pos.z), new Vector2(doorPt.x, doorPt.z)) < R * 0.9f;
                    m.path = near ? new List<Vector3> { doorPt } : new List<Vector3> { here + right * side * R * 1.15f, doorPt };
                }

                // through the door: wait for it to open
                if (m.state == State.Leaving)
                {
                    if (animator == null || animator.Openness(group) >= 0.85f) { m.state = State.Out; m.doorT = 0; }
                }
                else if (m.state == State.Boarding)
                {
                    m.doorT = 0;
                    if (animator == null || animator.Openness(group) >= 0.85f) m.state = State.Aboard;
                }
                else if (m.state == State.Out || m.state == State.Back)
                {
                    var turning = rpm > 0.3f;
                    var speed = m.state == State.Back || turning ? RUN : WALK;
                    var left = speed * dt;
                    m.doorT += dt;
                    while (left > 0 && m.path.Count > 0)
                    {
                        var p = m.path[0];
                        var d = new Vector2(p.x - m.pos.x, p.z - m.pos.z);
                        var dist = d.magnitude;
                        if (dist < 0.05f) { m.path.RemoveAt(0); continue; }
                        m.face = new Vector3(d.x, 0, d.y) / dist;
                        var step = Mathf.Min(dist, left);
                        m.pos += m.face * step; left -= step;
                    }
                    m.phase = (m.phase + dt * speed / AmbientLife.STRIDE) % 1;
                    if (m.path.Count == 0) m.state = m.state == State.Back ? State.Boarding : State.There;
                }
                if (m.state == State.There)
                {
                    var look = m.kneel && patient != null ? patient.Value - m.pos : here - m.pos;
                    look.y = 0;
                    if (look.sqrMagnitude > 0.01f) m.face = look.normalized;
                }
            }

            // doors the crew need open now: someone waiting to pass, or just through
            if (animator != null)
            {
                animator.crewHeld.Clear();
                foreach (var m in crew) if (m.state == State.Leaving || m.state == State.Boarding || ((m.state == State.Out || m.state == State.Back) && (m.doorT < 1.6f || (m.state == State.Back && m.path.Count <= 1)))) animator.crewHeld.Add(m.door);
            }

            // draw: walking figures get a frame of the walk cycle, and stoop under turning blades
            foreach (var m in crew)
            {
                var show = m.state == State.Out || m.state == State.Back || m.state == State.There;
                if (show) Ensure(m);
                var walking = m.state == State.Out || m.state == State.Back;
                var there = m.state == State.There;
                if (m.walk != null) m.walk.SetActive(show && walking);
                if (m.stand != null) m.stand.SetActive(show && there && !m.kneel);
                if (m.kneelGo != null) m.kneelGo.SetActive(show && there && m.kneel);
                if (!show) continue;
                var g = walking ? m.walk : m.kneel ? m.kneelGo : m.stand;
                if (g == null) continue;
                var pos = new Vector3(m.pos.x, Ground(m.pos), m.pos.z);
                var rot = Quaternion.LookRotation(m.face);
                if (walking)
                {
                    AmbientLife.SetFrame(m.walkFilters, m.frames[Mathf.FloorToInt(m.phase * AmbientLife.WALK_FRAMES) % AmbientLife.WALK_FRAMES]);
                    var under = rpm > 0.3f && Vector2.Distance(new Vector2(m.pos.x, m.pos.z), new Vector2(here.x, here.z)) < R * 1.25f;
                    if (under) { rot *= Quaternion.Euler(18, 0, 0); pos += Vector3.down * 0.1f; }
                }
                g.transform.SetPositionAndRotation(pos, rot);
            }
        }

        void Hide()
        {
            foreach (var m in crew) { m.state = State.Aboard; foreach (var g in new[] { m.walk, m.stand, m.kneelGo }) if (g != null) g.SetActive(false); }
            if (animator != null) animator.crewHeld.Clear();
        }
    }
}
