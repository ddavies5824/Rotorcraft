// The aircraft's own lights, switched from the cockpit (B beacon, N position lights, O strobes, L landing light):
// glows at the positions the Blender model gives (port of updateAircraftLights in the browser game's
// render/scene.ts), position lights shining only into their own sectors, plus real lights after dark: the
// beacon's red sweep and the strobe flashes washing the airframe and ground, and the landing light's beam.
using System.Collections.Generic;
using System.Linq;
using Rotorcraft.Sim;
using Rotorcraft.World;
using UnityEngine;

namespace Rotorcraft.Presentation
{
    public sealed class AircraftLights : MonoBehaviour
    {
        /// <summary>Light mounts in the model's own axes (x right, y up, z aft, metres), as in each .rcm's metadata.</summary>
        public sealed class Mounts
        {
            public Vector3 navL, navR, tail, beacon;
            public Vector3[] strobes;
            public Vector3 landing, landingDir = new Vector3(0, -0.32f, -1);
            public Vector3? search, searchDir, cabin;
        }

        public static readonly Dictionary<string, Mounts> MOUNTS = new Dictionary<string, Mounts>
        {
            ["kestrel"] = new Mounts
            {
                navL = new Vector3(-0.7f, 1.06f, 0.32f), navR = new Vector3(0.7f, 1.06f, 0.32f), tail = new Vector3(0, 1.66f, 5.16f), beacon = new Vector3(0, 2.31f, 0.62f),
                strobes = new[] { new Vector3(0, 2.6f, 5.04f) }, landing = new Vector3(0, 0.8f, -1.24f), landingDir = new Vector3(0, -0.32f, -1),
            },
            ["northstar"] = new Mounts
            {
                navL = new Vector3(-1.62f, 1.05f, 0.35f), navR = new Vector3(1.62f, 1.05f, 0.35f), tail = new Vector3(0, 2.5f, 8.02f), beacon = new Vector3(0, 3.36f, 1.7f),
                strobes = new[] { new Vector3(0, 4.3f, 7.75f), new Vector3(0, 0.82f, 0.4f) }, landing = new Vector3(-0.4f, 0.52f, -3.2f), landingDir = new Vector3(0, -0.3f, -1),
                search = new Vector3(0.5f, 0.36f, -3.36f), searchDir = new Vector3(0, -0.8f, -0.6f), cabin = new Vector3(0, 1.8f, -0.5f),
            },
            ["nightjar"] = new Mounts
            {
                navL = new Vector3(-1.07f, 2.4f, 5.48f), navR = new Vector3(1.07f, 2.4f, 5.48f), tail = new Vector3(0, 2.02f, 7.33f), beacon = new Vector3(0, 3.1f, 6.87f),
                strobes = new[] { new Vector3(0, 3.1f, 6.9f), new Vector3(0, 0.56f, -0.4f) }, landing = new Vector3(0, 0.64f, -3.26f), landingDir = new Vector3(0, -0.3f, -1),
                search = new Vector3(0.72f, 0.38f, -2.46f), searchDir = new Vector3(0, -0.7f, -0.7f), cabin = new Vector3(0, 1.6f, -0.2f),
            },
            ["peregrine"] = new Mounts
            {
                navL = new Vector3(-1.37f, 1.86f, 5.6f), navR = new Vector3(1.37f, 1.86f, 5.6f), tail = new Vector3(0, 2.58f, 7.64f), beacon = new Vector3(0, 4.01f, 7.58f),
                strobes = new[] { new Vector3(0, 4.02f, 7.62f), new Vector3(0, 0.45f, -0.6f) }, landing = new Vector3(0, 0.52f, -3.77f), landingDir = new Vector3(0, -0.3f, -1),
                search = new Vector3(0.62f, 0.28f, -2.42f), searchDir = new Vector3(0, -0.7f, -0.7f), cabin = new Vector3(0, 1.7f, -0.3f),
            },
        };

        public HelicopterController heli;
        [Tooltip("Landing light intensity (URP units) and reach (m).")]
        public float landingIntensity = 60, landingRange = 260;

        Mounts m;
        MeshFilter glowMesh;
        Light landing, search, beaconLight, strobeLight;
        readonly List<GlowPoint> glow = new List<GlowPoint>();

        /// <summary>A larger version of another type's mounts (the Guardian is built from the Northstar's lines).</summary>
        static Mounts Scaled(Mounts b, float k) => new Mounts
        {
            navL = b.navL * k, navR = b.navR * k, tail = b.tail * k, beacon = b.beacon * k, strobes = b.strobes.Select(s => s * k).ToArray(),
            landing = b.landing * k, landingDir = b.landingDir, search = b.search * k, searchDir = b.searchDir, cabin = b.cabin * k,
        };

        /// <summary>A model-space mount into this object's axes (the model is turned to face +z).</summary>
        static Vector3 Local(Vector3 v) => new Vector3(v.x, v.y, -v.z);

        void Start() => Configure();

        /// <summary>(Re)build the lights for the aircraft being flown.</summary>
        public void Configure()
        {
            if (heli == null) heli = GetComponent<HelicopterController>();
            if (heli == null) return;
            foreach (var l in new Component[] { glowMesh, landing, search, beaconLight, strobeLight }) if (l != null) Destroy(l.gameObject);
            search = null;
            if (!MOUNTS.TryGetValue(heli.aircraftId, out m)) m = heli.aircraftId == "guardian" ? Scaled(MOUNTS["northstar"], 1.12f) : MOUNTS["kestrel"];
            glowMesh = GlowPoints.Make("Aircraft light points", transform);
            landing = Spot("Landing light", m.landing, m.landingDir, 0.32f, 0.18f, landingRange);
            if (m.search.HasValue) search = Spot("Searchlight", m.search.Value, m.searchDir.Value, 0.2f, 0.1f, 400);
            beaconLight = Point("Beacon wash", m.beacon, new Color(1, 0.04f, 0.02f), 18);
            strobeLight = Point("Strobe flash", m.strobes[0], new Color(0.95f, 0.95f, 1), 30);
        }

        Light Spot(string name, Vector3 pos, Vector3 dir, float outer, float inner, float range)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.transform.SetParent(transform, false);
            l.transform.localPosition = Local(pos);
            l.transform.localRotation = Quaternion.LookRotation(Local(dir).normalized);
            l.type = LightType.Spot; l.range = range;
            l.spotAngle = outer * 2 * Mathf.Rad2Deg; l.innerSpotAngle = inner * 2 * Mathf.Rad2Deg;
            l.color = new Color(1, 0.95f, 0.85f); l.intensity = 0;
            l.shadows = LightShadows.Soft; l.shadowNearPlane = 0.6f;
            l.renderMode = LightRenderMode.ForcePixel;
            l.enabled = false;
            return l;
        }

        Light Point(string name, Vector3 pos, Color c, float range)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.transform.SetParent(transform, false);
            l.transform.localPosition = Local(pos) + Vector3.up * 0.15f;
            l.type = LightType.Point; l.range = range; l.color = c; l.intensity = 0;
            l.shadows = LightShadows.None; l.renderMode = LightRenderMode.ForcePixel;
            l.enabled = false;
            return l;
        }

        static float Smoothstep(float a, float b, float x) { var t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }

        void LateUpdate()
        {
            if (heli == null || heli.State == null || m == null) return;
            var s = heli.State; var sys = s.sys;
            var env = SkyEnvironment.Current;
            float night = env != null ? env.Night : 0, dark = Mathf.Max(0, (night - 0.15f) / 0.85f);
            var t = Time.time;
            bool alive = !s.crashed;

            // glows (not from the cockpit, where they would sit in the pilot's eyes)
            glow.Clear();
            var cam = Camera.main;
            if (alive && !heli.CockpitView && cam != null)
            {
                var eye = transform.InverseTransformPoint(cam.transform.position);
                // azimuth of the eye seen from a light: 0 dead ahead, + to the right
                float Az(Vector3 l) { var v = eye - Local(l); return Mathf.Atan2(v.x, v.z); }
                const float edge = 6 * Mathf.Deg2Rad, lim = 110 * Mathf.Deg2Rad;
                float Sector(float a, float lo, float hi) => Smoothstep(lo - edge, lo + edge, a) * (1 - Smoothstep(hi - edge, hi + edge, a));
                void Add(Vector3 l, Color c, float size, LightMode mode, float period = 1, float phase = 0, float dayVis = 0.15f) =>
                    glow.Add(new GlowPoint { pos = Local(l), color = new Color(c.r, c.g, c.b, dayVis), size = size, mode = mode, period = period, phase = phase });
                if (sys.nav)
                {
                    float fL = Sector(Az(m.navL), -lim, 0.05f), fR = Sector(Az(m.navR), -0.05f, lim), fT = Smoothstep(lim - edge, lim + edge, Mathf.Abs(Az(m.tail)));
                    if (fL > 0.01f) Add(m.navL, new Color(1.3f, 0.05f, 0.03f) * fL, 0.3f, LightMode.Steady);
                    if (fR > 0.01f) Add(m.navR, new Color(0.1f, 1.3f, 0.35f) * fR, 0.3f, LightMode.Steady);
                    if (fT > 0.01f) Add(m.tail, new Color(1.1f, 1.05f, 0.95f) * fT, 0.26f, LightMode.Steady);
                }
                if (sys.beacon) Add(m.beacon, new Color(1.6f, 0.08f, 0.04f), 0.6f, LightMode.Beacon, 1.0f, 0, 0.45f);
                if (sys.strobe) for (int i = 0; i < m.strobes.Length; i++) Add(m.strobes[i], new Color(3.2f, 3.2f, 3.5f), 1.1f, LightMode.Strobe, 1.3f, i * 0.3f, 1.0f);
                // landing light and searchlight: a modest glow from the side, blinding down the beam
                void Beam(Vector3 pos, Vector3 dir, float I)
                {
                    var to = (eye - Local(pos)).normalized;
                    var facing = Mathf.Max(0, Vector3.Dot(Local(dir).normalized, to));
                    var f = 0.18f + Mathf.Pow(facing, 3) * 0.6f + Mathf.Pow(facing, 14) * 5;
                    glow.Add(new GlowPoint { pos = Local(pos), color = new Color(I * f, I * f * 0.97f, I * f * 0.92f, 0.6f), size = 0.8f, mode = LightMode.Lamp, period = 1 });
                }
                if (sys.landingLight) Beam(m.landing, m.landingDir, 1);
                if (sys.searchlight && m.search.HasValue) Beam(m.search.Value, m.searchDir.Value, 1.4f);
                if (sys.nav && m.cabin.HasValue && night > 0.5f) Add(m.cabin.Value, new Color(0.6f, 0.5f, 0.35f), 0.6f, LightMode.Steady, 1, 0, 0);
            }
            GlowPoints.Fill(glowMesh.sharedMesh, glow);

            // real light: the beams, and after dark the beacon sweep and strobe flashes
            var beamOn = alive && sys.landingLight;
            landing.enabled = beamOn; landing.intensity = landingIntensity * (0.35f + 0.65f * dark);
            if (search != null) { search.enabled = alive && sys.searchlight; search.intensity = landingIntensity * 1.6f * (0.35f + 0.65f * dark); }
            float ph = t % 1.0f;
            var bk = (Mathf.Pow(Mathf.Max(Mathf.Cos(ph * 6.2832f), 0), 10) * 1.6f + 0.04f) * 0.9f * dark;
            beaconLight.enabled = alive && sys.beacon && dark > 0; beaconLight.intensity = bk * 6;
            float st = (t / 1.3f) % 1;
            var flash = (st < 0.04f ? 1 : 0) + (st > 0.11f && st < 0.15f ? 1 : 0);
            strobeLight.enabled = alive && sys.strobe && dark > 0 && flash > 0; strobeLight.intensity = 10 * dark;
        }
    }
}
