// Day, dusk and night (port of src/render/environment.ts): the key light (sun by day, low western light at dusk,
// moon at night), the sky, ambient light, fog and how bright the artificial lights look. Day is the scene as
// built; dusk and night are set relative to it. F4 steps through the times of day.
using Rotorcraft.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Rotorcraft.Presentation
{
    public sealed class SkyEnvironment : MonoBehaviour
    {
        public static SkyEnvironment Current { get; private set; }

        public Light sun;
        public TimeOfDay timeOfDay = TimeOfDay.Day;
        /// <summary>0 = full daylight … 1 = full night (drives artificial lights).</summary>
        public float Night { get; private set; }
        /// <summary>Atmospheric extinction from the visibility, per metre.</summary>
        public float FogDensity { get; private set; }
        public event System.Action Changed;
        /// <summary>The weather last applied.</summary>
        public WeatherPreset Weather => weather;

        WeatherPreset weather;
        // the day as built
        Quaternion daySunRot; float daySunIntensity; Color daySunColor; LightShadows daySunShadows;
        AmbientMode dayAmbientMode; Color daySky, dayEquator, dayGround; float dayAmbientIntensity, dayReflection = 1;
        Color dayFog; float dayFogStart, dayFogEnd;
        Material sky; float daySkyExposure = 1.3f, daySunSize = 0.04f; Color daySkyTint = new Color(0.5f, 0.5f, 0.5f), dayGroundColor = new Color(0.37f, 0.35f, 0.34f);
        bool captured;

        static readonly int RcNight = Shader.PropertyToID("_RcNight"), RcFog = Shader.PropertyToID("_RcFogDensity"), RcExposure = Shader.PropertyToID("_RcLightExposure");

        void Awake()
        {
            Current = this;
            Capture();
            if (GetComponent<WeatherEffects>() == null) gameObject.AddComponent<WeatherEffects>();
        }

        void OnDestroy() { if (Current == this) Current = null; }

        void Capture()
        {
            if (captured) return;
            if (sun == null) foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
            if (sun != null) { daySunRot = sun.transform.rotation; daySunIntensity = sun.intensity; daySunColor = sun.color; daySunShadows = sun.shadows; }
            dayAmbientMode = RenderSettings.ambientMode; daySky = RenderSettings.ambientSkyColor; dayEquator = RenderSettings.ambientEquatorColor; dayGround = RenderSettings.ambientGroundColor;
            dayAmbientIntensity = RenderSettings.ambientIntensity; dayReflection = RenderSettings.reflectionIntensity;
            dayFog = RenderSettings.fogColor; dayFogStart = RenderSettings.fogStartDistance; dayFogEnd = RenderSettings.fogEndDistance;
            if (RenderSettings.skybox != null)
            {
                sky = new Material(RenderSettings.skybox) { name = "Sky (time of day)" };   // never change the shared asset
                RenderSettings.skybox = sky;
                if (sky.HasProperty("_Exposure")) daySkyExposure = sky.GetFloat("_Exposure");
                if (sky.HasProperty("_SunSize")) daySunSize = sky.GetFloat("_SunSize");
                if (sky.HasProperty("_SkyTint")) daySkyTint = sky.GetColor("_SkyTint");
                if (sky.HasProperty("_GroundColor")) dayGroundColor = sky.GetColor("_GroundColor");
            }
            captured = true;
        }

        static Vector3 DirFrom(float elevDeg, float azDeg)
        {
            float e = elevDeg * Mathf.Deg2Rad, a = azDeg * Mathf.Deg2Rad;
            // sim (x east, y up, z south) towards the light, then into Unity's axes
            var sim = new Vector3(Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), -Mathf.Cos(a) * Mathf.Cos(e));
            return new Vector3(sim.x, sim.y, -sim.z);
        }

        static float Overcast(WeatherPreset w)
        {
            if (w == null) return 0;
            float[] cover = { 0, 0.05f, 0.2f, 0.55f, 0.9f };
            return Mathf.Clamp01(cover[(int)w.cloudCover] + (w.precip != Precip.None ? 0.1f : 0));
        }

        static Color Grey(Color c, float t) { var l = c.r * 0.3f + c.g * 0.55f + c.b * 0.15f; return Color.Lerp(c, new Color(l, l, l), t); }

        public void Apply(TimeOfDay tod, WeatherPreset w)
        {
            Capture();
            timeOfDay = tod; weather = w;
            var oc = Overcast(w);
            var vis = w != null ? (float)w.visibilityM : 30000;
            FogDensity = 3f / Mathf.Max(250, vis);
            // fog: as built in clear air, closing in with the visibility
            RenderSettings.fogStartDistance = Mathf.Min(dayFogStart, vis * 0.12f);
            RenderSettings.fogEndDistance = Mathf.Min(dayFogEnd, vis * 1.4f);
            float exposure;
            if (tod == TimeOfDay.Day)
            {
                exposure = 0.78f + oc * 0.45f;
                Night = 0;
                if (sun != null) { sun.transform.rotation = daySunRot; sun.intensity = daySunIntensity * (1 - 0.72f * oc); sun.color = daySunColor; sun.shadows = oc < 0.75f ? daySunShadows : LightShadows.None; }
                RenderSettings.ambientMode = dayAmbientMode; RenderSettings.ambientSkyColor = daySky; RenderSettings.ambientEquatorColor = dayEquator; RenderSettings.ambientGroundColor = dayGround;
                RenderSettings.ambientIntensity = dayAmbientIntensity;
                RenderSettings.fogColor = Grey(dayFog, oc * 0.7f);
                SetSky(daySkyExposure * (1 - 0.35f * oc), daySunSize * (1 - oc), Grey(daySkyTint, oc), dayGroundColor);
            }
            else if (tod == TimeOfDay.Dusk)
            {
                exposure = 1.7f + oc * 0.6f;
                Night = 0.75f;
                // the sun is just down; the key is the low glow in the west
                if (sun != null)
                {
                    sun.transform.rotation = Quaternion.LookRotation(-DirFrom(4, 262));
                    sun.intensity = 0.55f * 1.25f * (1 - 0.8f * oc);
                    sun.color = new Color(1.0f, 0.58f, 0.34f);
                    sun.shadows = LightShadows.Soft;
                }
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = Grey(new Color(0.16f, 0.18f, 0.32f), oc * 0.5f);
                RenderSettings.ambientEquatorColor = Grey(new Color(0.34f, 0.24f, 0.22f), oc * 0.6f);
                RenderSettings.ambientGroundColor = new Color(0.07f, 0.06f, 0.06f);
                RenderSettings.fogColor = Grey(new Color(0.42f, 0.30f, 0.27f), oc * 0.75f);
                SetSky(0.55f, 0, Grey(new Color(0.55f, 0.42f, 0.42f), oc * 0.6f), new Color(0.1f, 0.09f, 0.1f));
            }
            else
            {
                exposure = 2.7f;
                Night = 1;
                // moonlight from the south-south-east
                if (sun != null)
                {
                    sun.transform.rotation = Quaternion.LookRotation(-DirFrom(32, 150));
                    sun.intensity = 0.16f * (1 - 0.85f * oc);
                    sun.color = new Color(0.66f, 0.76f, 1.0f);
                    sun.shadows = LightShadows.Soft;
                }
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.045f, 0.058f, 0.095f);
                RenderSettings.ambientEquatorColor = new Color(0.038f, 0.044f, 0.062f);
                RenderSettings.ambientGroundColor = new Color(0.012f, 0.012f, 0.015f);
                RenderSettings.fogColor = Grey(new Color(0.03f, 0.04f, 0.065f), oc * 0.4f);
                SetSky(0.045f, 0, new Color(0.25f, 0.3f, 0.45f), new Color(0.01f, 0.01f, 0.012f));
            }
            // reflections outside the probe come from the sky cubemap captured at load (the day sky): dim them
            // with the light, or the sea shines white on the horizon at night
            RenderSettings.reflectionIntensity = tod == TimeOfDay.Day ? dayReflection : tod == TimeOfDay.Dusk ? dayReflection * 0.3f : dayReflection * 0.04f;
            Shader.SetGlobalFloat(RcNight, Night);
            Shader.SetGlobalFloat(RcFog, FogDensity);
            Shader.SetGlobalFloat(RcExposure, exposure * 0.96f);
            DynamicGI.UpdateEnvironment();
            foreach (var p in FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None)) if (p.refreshMode == ReflectionProbeRefreshMode.ViaScripting) p.RenderProbe();
            Changed?.Invoke();
        }

        void SetSky(float exposure, float sunSize, Color tint, Color ground)
        {
            if (sky == null) return;
            if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", exposure);
            if (sky.HasProperty("_SunSize")) sky.SetFloat("_SunSize", sunSize);
            if (sky.HasProperty("_SkyTint")) sky.SetColor("_SkyTint", tint);
            if (sky.HasProperty("_GroundColor")) sky.SetColor("_GroundColor", ground);
        }

        void Start()
        {
            if (weather == null) Apply(timeOfDay, Rotorcraft.Sim.Weather.Presets["calm"]);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f4Key.wasPressedThisFrame) Apply((TimeOfDay)(((int)timeOfDay + 1) % 3), weather);
        }
    }
}
