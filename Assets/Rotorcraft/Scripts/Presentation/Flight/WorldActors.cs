// The people and things a flight puts in the world: freeplay scenery (casualties, paramedics, police and fire
// crews, vehicles with flashing beacons, stretchers and freight), rescue targets (walkers waving, climbers,
// swimmers, liferafts, crashed cars, vessels) and the rescue winch: the cable from the hoist arm with the
// winchman on it, and the casualty in the strop. Figures come from people.glb, dressed by tinting their
// clothing materials; vehicles from vehicles.glb.
using System.Collections.Generic;
using System.Linq;
using Rotorcraft.Game;
using Rotorcraft.Sim;
using Rotorcraft.World;
using UnityEngine;

namespace Rotorcraft.Presentation
{
    public sealed class WorldActors : MonoBehaviour
    {
        [Tooltip("people.glb: pose_*, helmet_*, stretcher, pallet, liferaft, boat.")]
        public GameObject people;
        [Tooltip("vehicles.glb: ambulance, police, fireEngine, rover, saloon...")]
        public GameObject vehicles;

        readonly List<GameObject> props = new List<GameObject>(), targets = new List<GameObject>();
        readonly List<(GameObject a, GameObject b)> wavers = new List<(GameObject, GameObject)>();
        readonly List<Light> beacons = new List<Light>();
        LineRenderer cable;
        GameObject winchman, winchmanGround, strop;
        Dictionary<string, GameObject> peopleParts, vehicleParts;

        static readonly Color[] CASUAL_TOP = { C(0.1f, 0.14f, 0.28f), C(0.55f, 0.08f, 0.07f), C(0.12f, 0.3f, 0.18f), C(0.4f, 0.41f, 0.42f), C(0.62f, 0.55f, 0.42f), C(0.07f, 0.07f, 0.08f), C(0.2f, 0.36f, 0.62f), C(0.36f, 0.16f, 0.4f), C(0.1f, 0.38f, 0.4f), C(0.8f, 0.78f, 0.74f) };
        static readonly Color[] CASUAL_LEGS = { C(0.14f, 0.2f, 0.34f), C(0.08f, 0.08f, 0.09f), C(0.5f, 0.45f, 0.33f), C(0.3f, 0.3f, 0.32f), C(0.18f, 0.24f, 0.38f) };
        static readonly Color[] OUTDOOR = { C(0.75f, 0.12f, 0.08f), C(0.95f, 0.45f, 0.05f), C(0.1f, 0.35f, 0.6f), C(0.2f, 0.45f, 0.2f), C(0.85f, 0.75f, 0.1f) };
        static readonly Color[] SKIN = { C(0.8f, 0.6f, 0.49f), C(0.68f, 0.48f, 0.36f), C(0.45f, 0.3f, 0.2f), C(0.88f, 0.7f, 0.6f) };
        static readonly Color[] HAIR = { C(0.16f, 0.11f, 0.07f), C(0.05f, 0.04f, 0.03f), C(0.45f, 0.3f, 0.15f), C(0.55f, 0.53f, 0.5f) };
        static Color C(float r, float g, float b) => new Color(r, g, b);

        readonly List<Vector2> propPatients = new List<Vector2>(), targetPatients = new List<Vector2>();
        /// <summary>People needing the crew (casualties on the ground, sim x and z), for the crew on foot.</summary>
        public IEnumerable<Vector2> Patients => propPatients.Concat(targetPatients);

        void Awake()
        {
            Index();
            if (GetComponent<AmbientLife>() == null) gameObject.AddComponent<AmbientLife>();
        }

        // ---- for the ambient life and the crew on foot
        public GameObject SpawnVehicle(string kind, Transform parent) => Spawn(vehicleParts, kind, parent);
        public GameObject SpawnProp(string key, Transform parent) => Spawn(peopleParts, key, parent);
        public GameObject MakePerson(string outfit, string pose, int seed, Transform parent) { Index(); return Person(outfit, pose, seed, parent); }
        /// <summary>The meshes of a library figure, in the order its parts are found (for swapping walk frames).</summary>
        public Mesh[] FrameMeshes(string key)
        {
            Index();
            return peopleParts.TryGetValue(key, out var g) ? g.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).ToArray() : null;
        }

        void Index()
        {
            if (peopleParts != null) return;
            peopleParts = Children(people);
            vehicleParts = Children(vehicles);
        }

        static Dictionary<string, GameObject> Children(GameObject root)
        {
            var d = new Dictionary<string, GameObject>();
            if (root == null) return d;
            // the glb root, then its single top node with one child per model
            var top = root.transform.childCount == 1 ? root.transform.GetChild(0) : root.transform;
            foreach (Transform c in top) d[c.name] = c.gameObject;
            return d;
        }

        public void Clear()
        {
            foreach (var g in props.Concat(targets)) if (g != null) Destroy(g);
            props.Clear(); targets.Clear(); wavers.Clear(); beacons.Clear(); propPatients.Clear(); targetPatients.Clear();
            if (cable != null) cable.enabled = false;
            if (winchman != null) { winchman.SetActive(false); winchmanGround.SetActive(false); strop.SetActive(false); }
        }

        // -------------------------------------------------------------------------------- spawning
        GameObject Spawn(Dictionary<string, GameObject> from, string key, Transform parent)
        {
            Index();
            if (!from.TryGetValue(key, out var src)) return null;
            var g = Instantiate(src, parent, false);
            g.name = key;
            g.transform.localPosition = Vector3.zero; g.transform.localRotation = Quaternion.identity;
            foreach (var r in g.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; }
            return g;
        }

        /// <summary>A figure dressed for the role (casual, walker, medic, police, fire, hivis, worker, scrubs, winchman).</summary>
        GameObject Person(string outfit, string pose, int seed, Transform parent)
        {
            var helmet = outfit == "fire" || outfit == "worker" || outfit == "winchman" || outfit == "climber" || outfit == "aircrew";
            var key = helmet && peopleParts.ContainsKey("helmet_" + pose) ? "helmet_" + pose : "pose_" + pose;
            var g = Spawn(peopleParts, key, parent);
            if (g == null) return null;
            var rnd = new System.Random(seed);
            T Pick<T>(T[] a) => a[rnd.Next(a.Length)];
            Color top, legs, hat = Color.white;
            switch (outfit)
            {
                case "medic": top = C(0.75f, 0.9f, 0.12f); legs = C(0.08f, 0.24f, 0.14f); break;
                case "police": top = C(0.92f, 0.88f, 0.1f); legs = C(0.06f, 0.06f, 0.08f); break;
                case "fire": top = C(0.12f, 0.12f, 0.14f); legs = C(0.12f, 0.12f, 0.14f); hat = C(0.9f, 0.85f, 0.15f); break;
                case "hivis": top = C(0.92f, 0.88f, 0.1f); legs = C(0.1f, 0.11f, 0.15f); break;
                case "worker": top = C(0.98f, 0.45f, 0.06f); legs = C(0.12f, 0.14f, 0.22f); hat = rnd.NextDouble() < 0.5 ? C(0.92f, 0.92f, 0.9f) : C(0.92f, 0.8f, 0.1f); break;
                case "scrubs": top = legs = Pick(new[] { C(0.35f, 0.55f, 0.75f), C(0.2f, 0.45f, 0.45f), C(0.15f, 0.22f, 0.45f) }); break;
                case "winchman": top = C(0.95f, 0.45f, 0.08f); legs = C(0.95f, 0.45f, 0.08f); hat = C(0.92f, 0.92f, 0.9f); break;
                case "aircrew": top = legs = C(0.2f, 0.24f, 0.19f); hat = C(0.9f, 0.9f, 0.88f); break;   // flight suit and helmet
                case "walker": case "climber": top = Pick(OUTDOOR); legs = C(0.12f, 0.13f, 0.14f); hat = C(0.85f, 0.2f, 0.1f); break;
                default: top = Pick(CASUAL_TOP); legs = Pick(CASUAL_LEGS); break;
            }
            var skin = Pick(SKIN); var hair = Pick(HAIR);
            foreach (var r in g.GetComponentsInChildren<Renderer>())
            {
                var mats = r.materials;
                foreach (var m in mats)
                {
                    var n = m.name.Replace(" (Instance)", "");
                    if (n.StartsWith("Top")) Tint(m, top, outfit == "hivis" || outfit == "police" || outfit == "medic" || outfit == "worker" || outfit == "winchman" ? 0.35f : 0);
                    else if (n.StartsWith("Legs")) Tint(m, legs);
                    else if (n.StartsWith("Skin")) Tint(m, skin);
                    else if (n.StartsWith("Hair")) Tint(m, hair);
                    else if (n.StartsWith("Helmet")) Tint(m, hat);
                }
                r.materials = mats;
            }
            HeatSource.Add(g, HeatSource.Body);
            return g;
        }

        static void Tint(Material m, Color c, float glow = 0)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
            // fluorescent cloth stands out in dull light, as real hi-vis does
            if (glow > 0 && m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * glow); }
        }

        Transform Place(string name, double x, double y, double z, double rotRad, double tilt, List<GameObject> into)
        {
            var g = new GameObject(name);
            g.transform.SetParent(transform, false);
            g.transform.position = SimToUnity.Position(new Vec3(x, y, z));
            g.transform.rotation = Quaternion.Euler(0, (float)(-rotRad * Mathf.Rad2Deg), 0) * Quaternion.Euler(0, 0, (float)(tilt * Mathf.Rad2Deg));
            into.Add(g);
            return g.transform;
        }

        // -------------------------------------------------------------------------------- freeplay scenery
        public void SetProps(List<PropDef> defs)
        {
            foreach (var g in props) if (g != null) Destroy(g);
            props.Clear(); beacons.Clear(); propPatients.Clear();
            wavers.RemoveAll(w => w.a == null);
            Index();
            foreach (var p in defs ?? new List<PropDef>())
            {
                var y = p.onWater ? 0 : WorldMap.GroundAt(p.x, p.z).h;
                var t = Place(p.model, p.x, y, p.z, p.rot, p.tilt, props);
                var parts = p.model.Split(':');
                switch (parts[0])
                {
                    case "P":
                        {
                            var outfit = parts.Length > 1 ? parts[1] : "casual"; var pose = parts.Length > 2 ? parts[2] : "stand";
                            var seed = parts.Length > 3 && int.TryParse(parts[3], out var sd) ? sd : 1;
                            if (outfit == "phone") outfit = "casual";
                            if (pose == "lie") propPatients.Add(new Vector2((float)p.x, (float)p.z));
                            if (pose == "wave") { var a = Person(outfit, "wave", seed, t); var b = Person(outfit, "wave2", seed, t); if (a != null && b != null) wavers.Add((a, b)); }
                            else Person(outfit, pose, seed, t);
                            break;
                        }
                    case "s": Spawn(peopleParts, "stretcher", t); break;
                    case "m": Spawn(peopleParts, parts.Length > 1 ? parts[1] : "pallet", t); break;
                    case "v":
                        {
                            var v = Spawn(vehicleParts, parts.Length > 1 ? parts[1] : "saloon", t);
                            HeatSource.Add(v, HeatSource.Engine);
                            if (p.beacons && v != null) AddBeacons(t, parts[1] == "police" ? new Color(0.2f, 0.4f, 1f) : parts[1] == "fireEngine" ? new Color(0.3f, 0.45f, 1f) : new Color(0.25f, 0.45f, 1f));
                            break;
                        }
                }
            }
        }

        void AddBeacons(Transform vehicle, Color c)
        {
            foreach (var side in new[] { -0.45f, 0.45f })
            {
                var l = new GameObject("Beacon").AddComponent<Light>();
                l.transform.SetParent(vehicle, false);
                l.transform.localPosition = new Vector3(side, 2.1f, 0.4f);
                l.type = LightType.Point; l.range = 22; l.color = c; l.intensity = 0; l.shadows = LightShadows.None;
                beacons.Add(l);
            }
        }

        // -------------------------------------------------------------------------------- rescue targets
        public void SetTargets(IList<PlacedTarget> list, uint seed)
        {
            foreach (var g in targets) if (g != null) Destroy(g);
            targets.Clear(); targetPatients.Clear();
            wavers.RemoveAll(w => w.a == null);
            Index();
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                var tg = list[i];
                var hdg = (seed % 628) / 100.0 + i * 1.7;
                var t = Place("Target " + tg.id, tg.x, tg.y, tg.z, hdg, 0, targets);
                var sd = (int)(seed % 1000) + i * 13;
                if (tg.kind != TargetKind.Swimmer && tg.kind != TargetKind.Liferaft && tg.kind != TargetKind.Vessel) targetPatients.Add(new Vector2((float)tg.x, (float)tg.z));
                switch (tg.kind)
                {
                    case TargetKind.Walker:
                        { var a = Person("walker", "wave", sd, t); var b = Person("walker", "wave2", sd, t); if (a != null && b != null) wavers.Add((a, b)); break; }
                    case TargetKind.Climber:
                        Person("climber", "lie", sd, t);
                        Spawn(peopleParts, "pallet", t)?.With(x => { x.transform.localScale = new Vector3(0.3f, 0.3f, 0.4f); x.transform.localPosition = new Vector3(0.6f, 0, 0.4f); });
                        break;
                    case TargetKind.Swimmer: Person("casual", "swim", sd, t); break;
                    case TargetKind.Liferaft:
                        Spawn(peopleParts, "liferaft", t);
                        Person("casual", "sit", sd, t)?.With(x => x.transform.localPosition = new Vector3(0.35f, 0.15f, 0.1f));
                        Person("casual", "sit", sd + 1, t)?.With(x => { x.transform.localPosition = new Vector3(-0.4f, 0.15f, -0.2f); x.transform.localRotation = Quaternion.Euler(0, 160, 0); });
                        break;
                    case TargetKind.Vessel: HeatSource.Add(Spawn(peopleParts, "boat", t), HeatSource.Engine); break;
                    case TargetKind.Casualty:
                        // a crashed car with the casualty beside it, a passer-by kneeling and another on the phone
                        Spawn(vehicleParts, "hatch", t)?.With(x => { x.transform.localPosition = new Vector3(2.2f, 0, 1.2f); x.transform.localRotation = Quaternion.Euler(4, 35, 8); });
                        Person("casual", "lie", sd, t);
                        Person("casual", "kneel", sd + 1, t)?.With(x => { x.transform.localPosition = new Vector3(-0.7f, 0, 0.2f); x.transform.localRotation = Quaternion.Euler(0, 90, 0); });
                        Person("casual", "phone", sd + 2, t)?.With(x => { x.transform.localPosition = new Vector3(-2.6f, 0, 2.4f); x.transform.localRotation = Quaternion.Euler(0, 140, 0); });
                        break;
                    default: Person("worker", "lie", sd, t); break;   // training dummy in orange
                }
            }
        }

        // -------------------------------------------------------------------------------- the winch
        public void UpdateHoist(HoistState h, HeliState s, AircraftConfig ac)
        {
            var on = h.phase == HoistPhase.Lowering || h.phase == HoistPhase.OnScene || h.phase == HoistPhase.Raising;
            if (!on)
            {
                if (cable != null) cable.enabled = false;
                if (winchman != null) { winchman.SetActive(false); winchmanGround.SetActive(false); strop.SetActive(false); }
                return;
            }
            EnsureWinch();
            var arm = Hoist.ArmPoint(s, ac.rotorDiameter / 15);
            var a = SimToUnity.Position(arm); var hook = SimToUnity.Position(h.hook);
            cable.enabled = true;
            // a little sag in the wire when it is slack on scene
            var slack = h.phase == HoistPhase.OnScene ? 0.6f : 0.05f;
            for (int i = 0; i < cable.positionCount; i++)
            {
                var u = i / (float)(cable.positionCount - 1);
                cable.SetPosition(i, Vector3.Lerp(a, hook, u) + Vector3.down * slack * 4 * u * (1 - u));
            }
            var gH = (float)WorldMap.GroundAt(h.hook.x, h.hook.z).h;
            var onGround = h.hook.y - 1.75 <= gH + 0.08;
            var heliPos = SimToUnity.Position(s.pos);
            var face = Mathf.Atan2(heliPos.x - hook.x, heliPos.z - hook.z) * Mathf.Rad2Deg + (onGround ? 0 : Mathf.Sin(Time.time * 0.4f) * 34);
            winchman.SetActive(!onGround); winchmanGround.SetActive(onGround);
            var body = onGround ? winchmanGround : winchman;
            body.transform.position = onGround ? new Vector3(hook.x, gH, hook.z) : hook + Vector3.down * 1.75f;
            body.transform.rotation = Quaternion.Euler(0, face, onGround ? 0 : Mathf.Sin(Time.time * 1.3f) * 3);
            strop.SetActive(h.attached && !onGround);
            if (h.attached)
            {
                strop.transform.position = hook + Vector3.down * 1.8f + body.transform.forward * 0.35f;
                strop.transform.rotation = Quaternion.Euler(0, face + 180, 0);
            }
        }

        void EnsureWinch()
        {
            if (cable != null) return;
            Index();
            cable = new GameObject("Winch cable").AddComponent<LineRenderer>();
            cable.transform.SetParent(transform, false);
            cable.positionCount = 12; cable.widthMultiplier = 0.035f; cable.useWorldSpace = true;
            cable.material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(0.12f, 0.12f, 0.13f) };
            cable.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var holder = new GameObject("Winch crew").transform; holder.SetParent(transform, false);
            winchman = Person("winchman", "hang", 61, holder) ?? new GameObject("winchman");
            winchmanGround = Person("winchman", "stand", 61, holder) ?? new GameObject("winchman ground");
            strop = Person("casual", "hang", 77, holder) ?? new GameObject("strop");
            winchman.transform.SetParent(transform, true); winchmanGround.transform.SetParent(transform, true); strop.transform.SetParent(transform, true);
            Destroy(holder.gameObject);
        }

        void Update()
        {
            // people waving at the aircraft: two frames
            var odd = Mathf.FloorToInt(Time.time * 2.4f) % 2 == 1;
            foreach (var (a, b) in wavers) { if (a == null) continue; a.SetActive(!odd); b.SetActive(odd); }
            // emergency vehicle beacons: alternating blue flashes
            for (int i = 0; i < beacons.Count; i++)
            {
                if (beacons[i] == null) continue;
                var ph = (Time.time * 2.2f + (i % 2) * 0.5f) % 1f;
                beacons[i].intensity = ph < 0.18f ? 9 : 0;
            }
        }
    }

    static class GoExt
    {
        public static GameObject With(this GameObject g, System.Action<GameObject> f) { f(g); return g; }
    }
}
