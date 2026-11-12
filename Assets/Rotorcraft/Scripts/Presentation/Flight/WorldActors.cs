// The people and things a flight puts in the world: freeplay scenery (casualties, paramedics, police and fire
// crews, vehicles with flashing beacons, stretchers and freight), rescue targets (walkers waving, climbers,
// swimmers, liferafts, crashed cars, vessels) and the rescue winch: the cable from the hoist arm with the
// winchman on it, and the casualty in the strop. People are the people pack's characters (NpcLibrary), each playing
// the clip for their pose; stretchers, liferafts and boats still come from people.glb, vehicles from vehicles.glb.
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
        [Tooltip("people.glb: stretcher, pallet, liferaft, boat (its old figures are no longer used).")]
        public GameObject people;
        [Tooltip("vehicles.glb: ambulance, police, fireEngine, rover, saloon...")]
        public GameObject vehicles;

        readonly List<GameObject> props = new List<GameObject>(), targets = new List<GameObject>();
        readonly List<Light> beacons = new List<Light>();
        LineRenderer cable;
        Npc winchman, strop;
        NpcLibrary npcs;
        Dictionary<string, GameObject> peopleParts, vehicleParts;


        readonly List<Vector2> propPatients = new List<Vector2>(), targetPatients = new List<Vector2>();
        /// <summary>People needing the crew (casualties on the ground, sim x and z), for the crew on foot.</summary>
        public IEnumerable<Vector2> Patients => propPatients.Concat(targetPatients).Where(p => !Hidden(p));

        // places whose figures have been taken away (a casualty lifted onto a stretcher): they stay gone through a resync
        readonly List<Vector2> hiddenAt = new List<Vector2>();
        bool Hidden(Vector2 sim) { foreach (var h in hiddenAt) if (Vector2.Distance(h, sim) < 3.5f) return true; return false; }

        /// <summary>Take away whatever stands or lies within r of a point (sim x, z): the casualty and the things round them.</summary>
        public void HideNear(Vector2 sim, float r)
        {
            hiddenAt.Add(sim);
            HideHidden();
        }

        void HideHidden()
        {
            foreach (var g in props.Concat(targets))
                if (g != null && Hidden(new Vector2(g.transform.position.x, -g.transform.position.z))) g.SetActive(false);
        }

        /// <summary>Is something of this kind (its model key, such as "v:ambulance") already standing within r?</summary>
        public bool AnyPropNear(string key, Vector3 at, float r)
        {
            foreach (var g in props) if (g != null && g.name.StartsWith(key) && Vector3.Distance(g.transform.position, at) < r) return true;
            return false;
        }

        void Awake()
        {
            Index();
            npcs = GetComponent<NpcLibrary>() ?? gameObject.AddComponent<NpcLibrary>();
            if (GetComponent<AmbientLife>() == null) gameObject.AddComponent<AmbientLife>();
        }

        // ---- for the ambient life and the crew on foot
        public GameObject SpawnVehicle(string kind, Transform parent) => Spawn(vehicleParts, kind, parent);
        public GameObject SpawnProp(string key, Transform parent) => Spawn(peopleParts, key, parent);
        /// <summary>Someone for a part (an outfit: casual, walker, medic, aircrew...), playing the clip for a pose (stand, walk, kneel...).</summary>
        public Npc MakePerson(string outfit, string pose, int seed, Transform parent) => Person(outfit, pose, seed, parent);

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
            props.Clear(); targets.Clear(); beacons.Clear(); propPatients.Clear(); targetPatients.Clear(); hiddenAt.Clear();
            if (cable != null) cable.enabled = false;
            if (winchman != null) { winchman.gameObject.SetActive(false); strop.gameObject.SetActive(false); }
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

        /// <summary>A character from the people pack for the role (NpcRoles), playing the clip for the pose, warm on the thermal camera.</summary>
        Npc Person(string outfit, string pose, int seed, Transform parent)
        {
            if (npcs == null) npcs = GetComponent<NpcLibrary>() ?? gameObject.AddComponent<NpcLibrary>();
            var n = npcs.SpawnRole(outfit, seed, pose, parent);
            if (n == null) return null;
            // the water clips have no ground under them: the root goes below the surface, the shoulders just out of it
            if (pose == "swim") n.transform.localPosition = new Vector3(0, -0.8f * n.Height, 0);
            HeatSource.Add(n.gameObject, HeatSource.Body);
            return n;
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
            Index();
            foreach (var p in defs ?? new List<PropDef>())
            {
                var y = p.onWater ? 0 : NpcLibrary.Ground((float)p.x, -(float)p.z);   // on the ground as drawn
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
                            // a police officer waving at a road crash is waving the aircraft in
                            Person(outfit, pose == "wave" && outfit == "police" ? "marshal" : pose, seed, t);
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
            HideHidden();
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
            Index();
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                var tg = list[i];
                var hdg = (seed % 628) / 100.0 + i * 1.7;
                var water = tg.kind == TargetKind.Swimmer || tg.kind == TargetKind.Liferaft || tg.kind == TargetKind.Vessel;
                var t = Place("Target " + tg.id, tg.x, water ? tg.y : NpcLibrary.Ground((float)tg.x, -(float)tg.z), tg.z, hdg, 0, targets);
                var sd = (int)(seed % 1000) + i * 13;
                if (tg.kind != TargetKind.Swimmer && tg.kind != TargetKind.Liferaft && tg.kind != TargetKind.Vessel) targetPatients.Add(new Vector2((float)tg.x, (float)tg.z));
                switch (tg.kind)
                {
                    case TargetKind.Walker:
                        Person("walker", "wave", sd, t); break;
                    case TargetKind.Climber:
                        Person("climber", "lie", sd, t);
                        Spawn(peopleParts, "pallet", t)?.With(x => { x.transform.localScale = new Vector3(0.3f, 0.3f, 0.4f); x.transform.localPosition = new Vector3(0.6f, 0, 0.4f); });
                        break;
                    case TargetKind.Swimmer: Person("swimmer", "swim", sd, t); break;
                    case TargetKind.Liferaft:
                        Spawn(peopleParts, "liferaft", t);
                        Person("sailor", "sit", sd, t)?.gameObject.With(x => x.transform.localPosition = new Vector3(0.35f, 0.15f, 0.1f));
                        Person("sailor", "sit", sd + 1, t)?.gameObject.With(x => { x.transform.localPosition = new Vector3(-0.4f, 0.15f, -0.2f); x.transform.localRotation = Quaternion.Euler(0, 160, 0); });
                        break;
                    case TargetKind.Vessel: HeatSource.Add(Spawn(peopleParts, "boat", t), HeatSource.Engine); break;
                    case TargetKind.Casualty:
                        // a crashed car with the casualty beside it, a passer-by kneeling and another on the phone
                        Spawn(vehicleParts, "hatch", t)?.With(x => { x.transform.localPosition = new Vector3(2.2f, 0, 1.2f); x.transform.localRotation = Quaternion.Euler(4, 35, 8); });
                        Person("casual", "lie", sd, t);
                        Person("casual", "kneel", sd + 1, t)?.gameObject.With(x => { x.transform.localPosition = new Vector3(-0.7f, 0, 0.2f); x.transform.localRotation = Quaternion.Euler(0, 90, 0); });
                        Person("casual", "phone", sd + 2, t)?.gameObject.With(x => { x.transform.localPosition = new Vector3(-2.6f, 0, 2.4f); x.transform.localRotation = Quaternion.Euler(0, 140, 0); });
                        break;
                    default: Person("dummy", "lie", sd, t); break;   // the practice dummy, in a survival suit
                }
            }
            HideHidden();
        }

        // -------------------------------------------------------------------------------- the winch
        /// <summary>The winchman on the wire, and with him the casualty in the strop once they are hooked on: the winch clips, chosen by what the
        /// winch is doing, hung from the hook by the winchman's gripping hand. On the ground he stands. The clips are still poses, so the
        /// body turns and sways on the wire here.</summary>
        public void UpdateHoist(HoistState h, HeliState s, AircraftConfig ac)
        {
            var on = h.phase == HoistPhase.Lowering || h.phase == HoistPhase.OnScene || h.phase == HoistPhase.Raising;
            if (!on)
            {
                if (cable != null) cable.enabled = false;
                if (winchman != null) { winchman.gameObject.SetActive(false); strop.gameObject.SetActive(false); }
                return;
            }
            EnsureWinch();
            if (winchman == null) return;
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
            var gH = NpcLibrary.Ground(hook.x, hook.z);
            var onGround = h.hook.y - 1.75 <= gH + 0.08;
            var heliPos = SimToUnity.Position(s.pos);
            var face = Mathf.Atan2(heliPos.x - hook.x, heliPos.z - hook.z) * Mathf.Rad2Deg + (onGround ? 0 : Mathf.Sin(Time.time * 0.4f) * 34);
            var both = h.attached && !onGround;
            winchman.gameObject.SetActive(true);
            strop.gameObject.SetActive(both);
            var clip = onGround ? "idle" : both ? "winch_double_rescuer" : h.phase == HoistPhase.Lowering ? "winch_descend" : h.phase == HoistPhase.Raising ? "winch_ascend" : "winch_hang_idle";
            winchman.Play(clip, 0.3f);
            var rot = Quaternion.Euler(0, face, onGround ? 0 : Mathf.Sin(Time.time * 1.3f) * 3);
            Vector3 root;
            if (onGround) root = new Vector3(hook.x, gH, hook.z);
            else root = hook - rot * Grip(winchman, clip);
            winchman.transform.SetPositionAndRotation(root, rot);
            if (both)
            {
                // the casualty in the strop, facing the winchman a little in front of him
                strop.Play("winch_double_casualty", 0.3f);
                strop.transform.SetPositionAndRotation(root + rot * new Vector3(0, 0, 0.3f), rot * Quaternion.Euler(0, 180, 0));
            }
        }

        readonly Dictionary<string, Vector3> grips = new Dictionary<string, Vector3>();

        /// <summary>Where the winchman's right hand holds the hook in a clip, from his feet (measured once: the clips are still poses).</summary>
        Vector3 Grip(Npc n, string clip)
        {
            if (grips.TryGetValue(clip, out var g)) return g;
            var hand = n.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "mixamorig:RightHand");
            if (hand == null) return new Vector3(0.22f, 1.88f, 0.3f);
            var at = n.transform.position; var rot = n.transform.rotation;
            n.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            n.Play(clip, 0, 1, 0, true);
            n.Evaluate(0);
            g = hand.position;
            n.transform.SetPositionAndRotation(at, rot);
            grips[clip] = g;
            return g;
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
            winchman = Person("winchman", "hang", 61, transform);
            strop = Person("casual", "hang", 77, transform);
            if (winchman == null || strop == null) return;
            winchman.name = "Winchman"; strop.name = "Casualty in the strop";
        }

        void Update()
        {
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
