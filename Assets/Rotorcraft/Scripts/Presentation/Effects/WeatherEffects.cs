// The weather you can see (port of the rain, cloud layer, stars and in-cloud fog in the browser game's
// render/scene.ts), plus the flames of the offshore platforms' flares. Lives beside the SkyEnvironment and
// follows its weather and time of day: rain streaks round the camera that slant with the airflow, the cloud
// layer from the weather's base and cover (drawn from the same noise as Weather.CloudDensityAt), a whiteout when
// the camera is inside it, and stars and the moon on clear nights.
using System.Collections.Generic;
using Rotorcraft.Sim;
using Rotorcraft.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rotorcraft.Presentation
{
    public sealed class WeatherEffects : MonoBehaviour
    {
        const int DROPS = 4200, STARS = 1800;
        static readonly int RainVel = Shader.PropertyToID("_RainVel"), RainRel = Shader.PropertyToID("_RainRel"), RainBox = Shader.PropertyToID("_RainBox"),
            RainCol = Shader.PropertyToID("_RainCol"), RainAmount = Shader.PropertyToID("_RainAmount"), RainNear = Shader.PropertyToID("_RainNear"), RainTime = Shader.PropertyToID("_RainTime"),
            CloudCover = Shader.PropertyToID("_CloudCover"), CloudDrift = Shader.PropertyToID("_CloudDrift"), CloudLit = Shader.PropertyToID("_CloudLit"), CloudShade = Shader.PropertyToID("_CloudShade"),
            CloudBase = Shader.PropertyToID("_CloudBase"), CloudTop = Shader.PropertyToID("_CloudTop"), Stars = Shader.PropertyToID("_RcStars"), MoonDir = Shader.PropertyToID("_RcMoonDir"),
            ParticleLight = Shader.PropertyToID("_RcParticleLight");

        SkyEnvironment env;
        HelicopterController heli;
        MeshRenderer rain, clouds, stars, veil;
        Material veilMat;
        ParticleSystem flareFlames, flareSmoke;
        sealed class Flare { public Vector3 tip; public bool laidOut; public float accF, accS; }
        readonly List<Flare> flares = new List<Flare>();
        // the fog as the environment set it, before any whiteout
        float fogStart, fogEnd; Color fogColor; bool fogSaved;
        // the effects sit outside the world object, whose children are cleared when it rebuilds
        Transform root;

        void Awake()
        {
            root = new GameObject("Weather effects").transform;
            env = GetComponent<SkyEnvironment>();
            if (env != null) env.Changed += SaveFog;
            rain = Renderer("Rain", "Rotorcraft/Rain", RainMesh());
            clouds = Renderer("Cloud layer", "Rotorcraft/Clouds", CloudMesh());
            if (clouds != null) clouds.sharedMaterial.SetTexture("_Noise", FxNoise.NoiseTexture);
            stars = Renderer("Stars", "Rotorcraft/Stars", StarMesh());
            veil = Renderer("Cloud veil", "Rotorcraft/Veil", SphereMesh());
            if (veil != null) { veilMat = veil.sharedMaterial; veil.transform.localScale = Vector3.one * 700; }
            flareFlames = ParticleFx.Make("Flare flames", root, true, 420, 0, 0, 1.8f, 0.05f, 0.4f);
            flareSmoke = ParticleFx.Make("Flare smoke", root, false, 260, 0, 0, 8f, 0.05f, 0.5f);
            foreach (var st in WorldMap.STRUCTURES)
                if (st.kind == StructureKind.Platform)
                    flares.Add(new Flare { tip = SimToUnity.Position(new Vec3(st.x + 52, st.@base + st.h + 33, st.z - 26)) });
        }

        void OnDestroy()
        {
            if (env != null) env.Changed -= SaveFog;
            if (root != null) Destroy(root.gameObject);
        }

        void SaveFog() { fogStart = RenderSettings.fogStartDistance; fogEnd = RenderSettings.fogEndDistance; fogColor = RenderSettings.fogColor; fogSaved = true; }

        MeshRenderer Renderer(string name, string shaderName, Mesh mesh)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) { Debug.LogWarning(shaderName + " shader missing"); return null; }
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = new Material(shader) { name = name };
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.enabled = false;
            return r;
        }

        static Mesh RainMesh()
        {
            var rnd = new System.Random(7);
            var seed = new Vector3[DROPS * 4]; var end = new Vector2[DROPS * 4]; var tris = new int[DROPS * 6];
            for (int i = 0; i < DROPS; i++)
            {
                var s = new Vector3((float)rnd.NextDouble(), (float)rnd.NextDouble(), (float)rnd.NextDouble());
                for (int k = 0; k < 4; k++) { seed[i * 4 + k] = s; end[i * 4 + k] = new Vector2(k < 2 ? 0 : 1, k == 0 || k == 3 ? -1 : 1); }
                int b = i * 4; tris[i * 6] = b; tris[i * 6 + 1] = b + 1; tris[i * 6 + 2] = b + 2; tris[i * 6 + 3] = b; tris[i * 6 + 4] = b + 2; tris[i * 6 + 5] = b + 3;
            }
            var m = new Mesh { name = "Rain", indexFormat = IndexFormat.UInt32 };
            m.SetVertices(seed); m.SetUVs(0, end); m.SetTriangles(tris, 0, false);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 400000);
            return m;
        }

        static Mesh CloudMesh()
        {
            // two sheets 60 km across, at the base (y 0) and the top (y 1): the transform sets the heights
            const float H = 30000;
            var v = new List<Vector3>(); var t = new List<int>();
            for (int s = 0; s < 2; s++)
            {
                int b = v.Count;
                v.Add(new Vector3(-H, s, -H)); v.Add(new Vector3(-H, s, H)); v.Add(new Vector3(H, s, H)); v.Add(new Vector3(H, s, -H));
                t.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            var m = new Mesh { name = "Cloud layer" };
            m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateBounds();
            return m;
        }

        /// <summary>A sphere of diameter 1 (built here: a primitive would bring a collider, which the web build
        /// strips, and log an error).</summary>
        static Mesh SphereMesh()
        {
            const int LON = 32, LAT = 16;
            var v = new List<Vector3>(); var t = new List<int>();
            for (int j = 0; j <= LAT; j++)
            {
                var th = Mathf.PI * j / LAT;
                for (int i = 0; i <= LON; i++)
                {
                    var ph = Mathf.PI * 2 * i / LON;
                    v.Add(0.5f * new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)));
                }
            }
            for (int j = 0; j < LAT; j++)
                for (int i = 0; i < LON; i++)
                {
                    int a = j * (LON + 1) + i, b = a + LON + 1;
                    t.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
                }
            var m = new Mesh { name = "Cloud veil" };
            m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static Mesh StarMesh()
        {
            var rnd = new System.Random(19);
            var dir = new List<Vector3>(); var corner = new List<Vector2>(); var info = new List<Vector4>(); var tris = new List<int>();
            Vector2[] C = { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
            void Add(Vector3 d, Vector4 inf)
            {
                int b = dir.Count;
                for (int k = 0; k < 4; k++) { dir.Add(d); corner.Add(C[k]); info.Add(inf); }
                tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            for (int i = 0; i < STARS; i++)
            {
                // even over the sphere, upper half only
                var y = (float)rnd.NextDouble() * 1.05f - 0.05f; var a = (float)(rnd.NextDouble() * Mathf.PI * 2); var r = Mathf.Sqrt(1 - y * y);
                var mag = Mathf.Pow((float)rnd.NextDouble(), 7);
                Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r), new Vector4(2.2f + mag * 3.5f, 0.25f + mag * 2.2f, (float)rnd.NextDouble(), 0));
            }
            Add(Vector3.up, new Vector4(0, 1, 0, 1));   // the moon (its direction comes from the environment)
            var m = new Mesh { name = "Stars", indexFormat = IndexFormat.UInt32 };
            m.SetVertices(dir); m.SetUVs(0, corner); m.SetUVs(1, info); m.SetTriangles(tris, 0, false);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 400000);
            return m;
        }

        void LateUpdate()
        {
            if (env == null) env = SkyEnvironment.Current;
            var cam = Camera.main;
            if (env == null || cam == null) return;
            if (!fogSaved) SaveFog();
            if (heli == null || !heli.isActiveAndEnabled) heli = FindAnyObjectByType<HelicopterController>();
            var w = heli != null && heli.State != null && heli.WeatherAt != null ? heli.WeatherAt(heli.State.t) : env.Weather;
            var eye = cam.transform.position;
            var t = Time.time;
            ParticleFx.UpdateLight(env.sun);
            var light = (Color)(Vector4)Shader.GetGlobalVector(ParticleLight);
            var wind = w != null ? SimToUnity.Direction(Rotorcraft.Sim.Weather.MeanWind(w)) : Vector3.zero;

            // ---- cloud layer, and the whiteout inside it
            float inCloud = 0, cover = 0;
            bool layer = w != null && w.cloudBaseFt != null && w.cloudCover != Rotorcraft.Sim.CloudCover.SKC;
            if (clouds != null)
            {
                clouds.enabled = layer;
                if (layer)
                {
                    var baseM = (float)(w.cloudBaseFt.Value / 3.28084);
                    cover = (float)Rotorcraft.Sim.Weather.CoverFraction(w.cloudCover);
                    var (dx, dz) = Rotorcraft.Sim.Weather.WindDirUnit(w);
                    var tc = t % 100000f;
                    var ct = clouds.transform;
                    ct.position = new Vector3(Mathf.Round(eye.x / 200) * 200, baseM, Mathf.Round(eye.z / 200) * 200);
                    ct.localScale = new Vector3(1, (float)Rotorcraft.Sim.Weather.CLOUD_DEPTH_M, 1);
                    var m = clouds.sharedMaterial;
                    m.SetFloat(CloudCover, cover);
                    m.SetVector(CloudDrift, new Vector4((float)(dx * tc * 0.00012), (float)(dz * tc * 0.00012), 0, 0));
                    m.SetFloat(CloudBase, baseM); m.SetFloat(CloudTop, baseM + (float)Rotorcraft.Sim.Weather.CLOUD_DEPTH_M);
                    var lit = new Color(Mathf.Min(light.r * 1.05f, 1.1f), Mathf.Min(light.g * 1.05f, 1.1f), Mathf.Min(light.b * 1.08f, 1.15f));
                    m.SetColor(CloudLit, lit);
                    m.SetColor(CloudShade, lit * 0.55f);
                    var sim = SimToUnity.ToSim(eye);
                    inCloud = (float)Rotorcraft.Sim.Weather.CloudDensityAt(w, sim.x, sim.y, sim.z, tc, FxNoise.Sample, dx, dz);
                    if (veilMat != null) veilMat.SetColor("_VeilColor", new Color(lit.r * 0.82f, lit.g * 0.84f, lit.b * 0.88f, Mathf.Pow(inCloud, 0.7f)));
                }
            }
            if (veil != null) { veil.enabled = inCloud > 0.01f; veil.transform.position = eye; }
            RenderSettings.fogStartDistance = Mathf.Lerp(fogStart, 0, inCloud);
            RenderSettings.fogEndDistance = Mathf.Lerp(fogEnd, 45, Mathf.Pow(inCloud, 0.5f));
            RenderSettings.fogColor = Color.Lerp(fogColor, new Color(light.r * 0.8f, light.g * 0.82f, light.b * 0.86f), inCloud);

            // ---- stars and moon: clear nights, out of cloud
            if (stars != null)
            {
                var clear = 1 - Mathf.Clamp01((layer ? cover * 1.15f : 0) + (w != null && w.precip != Precip.None ? 1 : 0) + (w != null && w.visibilityM < 4000 ? 0.7f : 0));
                var k = env.timeOfDay == TimeOfDay.Night ? 1 : env.timeOfDay == TimeOfDay.Dusk ? 0.12f : 0;
                var s = k * clear * (1 - inCloud);
                stars.enabled = s > 0.01f;
                Shader.SetGlobalFloat(Stars, s);
                if (env.sun != null) Shader.SetGlobalVector(MoonDir, -env.sun.transform.forward);
            }

            // ---- rain round the camera, streaking with the airflow
            if (rain != null)
            {
                rain.enabled = w != null && w.precip != Precip.None;
                if (rain.enabled)
                {
                    var fall = w.precip == Precip.Drizzle ? 4f : 8f;
                    var vel = wind + Vector3.down * fall;
                    var own = heli != null && heli.State != null ? SimToUnity.Direction(heli.State.vel) : Vector3.zero;
                    var m = rain.sharedMaterial;
                    m.SetVector(RainVel, vel); m.SetVector(RainRel, vel - own);
                    m.SetVector(RainBox, new Vector4(50, 36, 50, 0));
                    var lum = (light.r + light.g + light.b) / 3 * 0.55f + 0.12f;
                    m.SetColor(RainCol, new Color(lum, lum * 1.03f, lum * 1.08f, w.precip == Precip.Drizzle ? 0.3f : 0.45f));
                    m.SetFloat(RainAmount, w.precip == Precip.Drizzle ? 0.35f : w.precip == Precip.Rain ? 0.62f : 1f);
                    m.SetFloat(RainNear, heli != null && heli.CockpitView ? 2.4f : 0.3f);
                    m.SetFloat(RainTime, t % 3600f);
                }
            }

            // ---- platform flares (and their smoke) within reach
            if (flareSmoke == null) return;
            ParticleFx.Wind(flareSmoke, wind * 0.8f);
            var dt = heli != null && heli.Paused ? 0 : Time.deltaTime;
            foreach (var ps in new[] { flareFlames, flareSmoke }) { var mm = ps.main; mm.simulationSpeed = dt > 0 ? 1 : 0; }
            if (dt > 0)
                foreach (var f in flares)
                {
                    var d = Vector2.Distance(new Vector2(f.tip.x, f.tip.z), new Vector2(eye.x, eye.z));
                    if (d > 9000) { f.laidOut = false; continue; }
                    AircraftEffects.Fire(f.tip, wind, dt, ref f.laidOut, flareFlames, flareSmoke, ref f.accF, ref f.accS, 1, d > 3500 ? 0.4f : 1);
                }
        }
    }
}
