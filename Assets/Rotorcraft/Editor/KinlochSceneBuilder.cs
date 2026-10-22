// Builds the Kinloch scene: the generated world, the Kestrel on H1 with Exercise 1 loaded, chase camera, flight
// HUD, lighting, fog and post-processing. The world itself is generated when the scene plays, so the saved
// scene stays small; the preview builds it in the editor just to render pictures of it.
//   Unity.exe -batchmode -projectPath <project> -executeMethod Rotorcraft.EditorTools.KinlochSceneBuilder.BuildAndPreview -quit
using System.IO;
using Rotorcraft.Presentation;
using Rotorcraft.Sim;
using Rotorcraft.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Rotorcraft.EditorTools
{
    public static class KinlochSceneBuilder
    {
        const string ScenePath = "Assets/Rotorcraft/Scenes/Kinloch.unity";
        const string SettingsDir = "Assets/Rotorcraft/Settings";

        [MenuItem("Rotorcraft/Build Kinloch scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Directory.CreateDirectory(SettingsDir);

            // terrain material (URP Terrain Lit)
            var tmPath = $"{SettingsDir}/Terrain.mat";
            var terrainMat = AssetDatabase.LoadAssetAtPath<Material>(tmPath);
            if (terrainMat == null) { terrainMat = new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit")); AssetDatabase.CreateAsset(terrainMat, tmPath); }

            // the world, generated at play time
            var probe = new GameObject("Reflection probe").AddComponent<ReflectionProbe>();
            var h1 = WorldMap.SiteById("kin_h1");
            probe.transform.position = SimToUnity.Position(new Vec3(h1.x, WorldMap.SiteElevation(h1) + 12, h1.z));
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.size = new Vector3(3000, 800, 3000);
            probe.resolution = 256; probe.hdr = true;
            var worldGo = new GameObject("Kinloch");
            var world = worldGo.AddComponent<KinlochWorld>();
            world.landmarks = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Rotorcraft/Models/landmarks.glb");
            world.terrainMaterial = terrainMat;
            world.reflectionProbe = probe;

            // the Kestrel with Exercise 1
            var heli = new GameObject("Kestrel K2");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Rotorcraft/Models/kestrel_k2.glb"), heli.transform);
            model.name = "Model";
            model.transform.localRotation = Quaternion.Euler(0, 180, 0);
            var ctl = heli.AddComponent<HelicopterController>();
            ctl.model = model.transform;
            var co = FindDeep(model.transform, "cockpit_only"); if (co != null) co.gameObject.SetActive(false);
            var director = heli.AddComponent<MissionDirector>();
            director.heli = ctl;
            director.missionId = "ex01_controls";
            Ground.Current = new WorldGround();   // place it on the real H1, not on flat ground at sea level
            var start = FlightModel.CreateState(new CreateOptions { x = h1.x, z = h1.z, headingDeg = 270, fuel = 40 });
            heli.transform.SetPositionAndRotation(SimToUnity.Position(start.pos), SimToUnity.Attitude(start));

            // camera, HUD, post-processing
            var cam = Camera.main;
            cam.farClipPlane = 30000; cam.nearClipPlane = 0.3f;
            var chase = cam.gameObject.AddComponent<ChaseCamera>(); chase.target = ctl; chase.Snap();
            var hud = cam.gameObject.AddComponent<FlightHud>(); hud.heli = ctl; hud.mission = director;
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var volume = new GameObject("Post-processing").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>($"{SettingsDir}/TestFlight Volume.asset");

            // a bright Scottish afternoon: sun from the south-west, haze in the distance
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { l.transform.rotation = Quaternion.Euler(38, 30, 0); l.intensity = 1.5f; l.shadows = LightShadows.Soft; }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 3000; RenderSettings.fogEndDistance = 28000;
            RenderSettings.fogColor = new Color(0.68f, 0.76f, 0.84f);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true), new EditorBuildSettingsScene("Assets/Rotorcraft/Scenes/TestFlight.unity", true) };
            Debug.Log("Kinloch scene saved to " + ScenePath);
        }

        [MenuItem("Rotorcraft/Render Kinloch preview")]
        public static void Preview()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var world = Object.FindAnyObjectByType<KinlochWorld>();
            world.Build();                       // in the editor, only for the pictures (the scene is not saved)
            var cam = Camera.main;
            var heli = Object.FindAnyObjectByType<HelicopterController>().transform;
            cam.GetComponent<ChaseCamera>().Snap();
            Shot(cam, cam.transform.position, heli.position + Vector3.up * 1.5f, 55, "Logs/kinloch-chase.png");
            // over the heliport looking north-west to Kinloch town
            var town = SimToUnity.Position(new Vec3(-620, WorldMap.TerrainHeight(-620, -900), -900));
            Shot(cam, heli.position + new Vector3(250, 220, -250), town, 50, "Logs/kinloch-aerial.png");
            // high over the coast looking north along it to Tarbat Light
            var light = SimToUnity.Position(new Vec3(880, 20, -3600));
            Shot(cam, SimToUnity.Position(new Vec3(1600, 600, 1500)), light, 55, "Logs/kinloch-coast.png");
            // inland, up the glen towards Beinn Ardoch
            var ben = SimToUnity.Position(new Vec3(-6400, WorldMap.TerrainHeight(-6400, 550), 550));
            Shot(cam, SimToUnity.Position(new Vec3(-1800, 450, -600)), ben, 55, "Logs/kinloch-glen.png");
            world.Clear();
        }

        public static void BuildAndPreview() { Build(); Preview(); }

        static void Shot(Camera cam, Vector3 from, Vector3 at, float fov, string path)
        {
            cam.fieldOfView = fov;
            cam.transform.position = from;
            cam.transform.LookAt(at);
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex); rt.Release();
            Debug.Log("Preview written to " + Path.GetFullPath(path));
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = FindDeep(c, name); if (f != null) return f; }
            return null;
        }
    }
}
