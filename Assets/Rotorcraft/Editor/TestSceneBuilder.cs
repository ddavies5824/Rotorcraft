// Builds the Phase 1 test scene (the Kestrel on a pad on level ground, chase camera, test HUD) and renders a
// preview image of it. Rebuild any time from the menu, or in batch mode:
//   Unity.exe -batchmode -projectPath <project> -executeMethod Rotorcraft.EditorTools.TestSceneBuilder.BuildAndPreview -quit
using System.IO;
using Rotorcraft.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Rotorcraft.EditorTools
{
    public static class TestSceneBuilder
    {
        const string ScenePath = "Assets/Rotorcraft/Scenes/TestFlight.unity";
        const string MaterialDir = "Assets/Rotorcraft/Materials";
        const string SettingsDir = "Assets/Rotorcraft/Settings";
        const string ModelPath = "Assets/Rotorcraft/Models/kestrel_k2.glb";
        const string PreviewPath = "Logs/test-scene-preview.png";

        [MenuItem("Rotorcraft/Build test scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var grass = Mat("Grass", new Color(0.30f, 0.42f, 0.22f), 0.05f);
            var concrete = Mat("Pad concrete", new Color(0.55f, 0.55f, 0.53f), 0.15f);
            var paint = Mat("Pad paint", new Color(0.92f, 0.92f, 0.9f), 0.2f);

            // level ground, 4 km square, at the sim's ground height (FlatGround, 0 m)
            var ground = Prim(PrimitiveType.Plane, "Ground", grass);
            ground.transform.localScale = new Vector3(400, 1, 400);

            // a 16 m concrete pad with a white circle and H, centred on the sim's origin
            var pad = new GameObject("Pad").transform;
            var disc = Prim(PrimitiveType.Cylinder, "Pad surface", concrete, pad);
            // stacked discs, each top 2 mm above the one below so they never fight over the same depth
            disc.transform.localPosition = new Vector3(0, 0f, 0);
            disc.transform.localScale = new Vector3(16, 0.005f, 16);          // top at 5 mm
            var ring = Prim(PrimitiveType.Cylinder, "Pad circle", paint, pad);
            ring.transform.localPosition = new Vector3(0, 0.005f, 0);
            ring.transform.localScale = new Vector3(12, 0.002f, 12);          // top at 7 mm
            var inner = Prim(PrimitiveType.Cylinder, "Pad circle inner", concrete, pad);
            inner.transform.localPosition = new Vector3(0, 0.007f, 0);
            inner.transform.localScale = new Vector3(11.2f, 0.002f, 11.2f);   // top at 9 mm
            foreach (var (name, pos, size) in new[] { ("H left", new Vector3(-1.6f, 0.01f, 0), new Vector3(0.6f, 0.002f, 5f)), ("H right", new Vector3(1.6f, 0.01f, 0), new Vector3(0.6f, 0.002f, 5f)), ("H bar", new Vector3(0, 0.01f, 0), new Vector3(3.2f, 0.002f, 0.6f)) })
            {
                var b = Prim(PrimitiveType.Cube, name, paint, pad);
                b.transform.localPosition = pos; b.transform.localScale = size;
            }

            // the helicopter: the imported model turned 180° (glTFast flips X), so its nose faces +Z = north
            var heli = new GameObject("Kestrel K2");
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, heli.transform);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0, 180, 0);
            var ctl = heli.AddComponent<HelicopterController>();
            ctl.model = model.transform;
            var cockpitOnly = FindDeep(model.transform, "cockpit_only");
            if (cockpitOnly != null) cockpitOnly.gameObject.SetActive(false);

            // camera and HUD
            var camGo = Camera.main.gameObject;
            var cam = camGo.GetComponent<Camera>();
            cam.farClipPlane = 6000;
            var chase = camGo.AddComponent<ChaseCamera>();
            chase.target = ctl;
            chase.Snap();
            var hud = camGo.AddComponent<TestHud>();
            hud.target = ctl;

            // afternoon sun from the south-west
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { l.transform.rotation = Quaternion.Euler(42, 35, 0); l.intensity = 1.6f; l.shadows = LightShadows.Soft; }

            // filmic colour (ACES), a little bloom on bright highlights, anti-aliasing
            Directory.CreateDirectory(SettingsDir);
            var profilePath = $"{SettingsDir}/TestFlight Volume.asset";
            AssetDatabase.DeleteAsset(profilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);
            var tonemap = profile.Add<Tonemapping>(true); tonemap.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true); bloom.threshold.Override(1.1f); bloom.intensity.Override(0.25f);
            var colour = profile.Add<ColorAdjustments>(true); colour.postExposure.Override(0.2f); colour.contrast.Override(8f); colour.saturation.Override(6f);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            AssetDatabase.SaveAssets();
            var volume = new GameObject("Post-processing").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camData.antialiasingQuality = AntialiasingQuality.High;

            // a reflection probe, so the paint, metal and glass reflect the sky and the ground
            var probe = new GameObject("Reflection probe").AddComponent<ReflectionProbe>();
            probe.transform.position = new Vector3(0, 3, 0);
            probe.mode = ReflectionProbeMode.Baked;
            probe.size = new Vector3(600, 200, 600);
            probe.resolution = 256;
            probe.hdr = true;

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            // environment lighting and the probe, baked without the helicopter in them (no lightmaps: nothing is static)
            var lightingPath = $"{SettingsDir}/TestFlight Lighting.lighting";
            var lighting = AssetDatabase.LoadAssetAtPath<LightingSettings>(lightingPath);
            if (lighting == null) { lighting = new LightingSettings(); AssetDatabase.CreateAsset(lighting, lightingPath); }
            lighting.bakedGI = false;
            lighting.realtimeGI = false;
            Lightmapping.lightingSettings = lighting;
            heli.SetActive(false);
            Lightmapping.Bake();
            heli.SetActive(true);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("Test scene saved to " + ScenePath);
        }

        [MenuItem("Rotorcraft/Render test scene preview")]
        public static void Preview()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var cam = Camera.main;
            var chase = cam.GetComponent<ChaseCamera>();
            if (chase != null) chase.Snap();
            Render(cam, PreviewPath);
            // a closer three-quarter view, to check the model's colours and orientation
            var heli = Object.FindAnyObjectByType<HelicopterController>().transform;
            cam.transform.position = heli.position + new Vector3(-6.5f, 2.6f, 6.5f);
            cam.transform.LookAt(heli.position + Vector3.up * 1.3f);
            Render(cam, "Logs/test-scene-closeup.png");
            // from the right, level with the cabin: the cockpit should show through the canopy
            cam.transform.position = heli.position + new Vector3(4.2f, 1.7f, 1.4f);
            cam.transform.LookAt(heli.position + new Vector3(0, 1.35f, 0.5f));
            Render(cam, "Logs/test-scene-canopy.png");
        }

        public static void BuildAndPreview() { Build(); Preview(); }

        static void Render(Camera cam, string path)
        {
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Debug.Log("Preview written to " + Path.GetFullPath(path));
        }

        static Material Mat(string name, Color c, float smoothness)
        {
            Directory.CreateDirectory(MaterialDir);
            var path = $"{MaterialDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(m);
            return m;
        }

        static GameObject Prim(PrimitiveType type, string name, Material m, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = FindDeep(c, name); if (f != null) return f; }
            return null;
        }
    }
}
