// Builds the game scene (the one the player runs): the Kinloch world (generated behind the first loading
// screen), the aircraft with its models, the camera, lighting and post-processing, the UI document with the
// game's style sheet, and the game, cloud and flight-session objects. Also makes the Windows and web builds.
//   Unity.exe -batchmode -projectPath <project> -executeMethod Rotorcraft.EditorTools.GameSceneBuilder.BuildWindows -quit
using System.IO;
using Rotorcraft.Presentation;
using Rotorcraft.Presentation.UI;
using Rotorcraft.Sim;
using Rotorcraft.World;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using Terrain = UnityEngine.Terrain;

namespace Rotorcraft.EditorTools
{
    public static class GameSceneBuilder
    {
        public const string ScenePath = "Assets/Rotorcraft/Scenes/Game.unity";
        const string SettingsDir = "Assets/Rotorcraft/Settings";
        const string UiDir = "Assets/Rotorcraft/UI";
        const string Models = "Assets/Rotorcraft/Models";

        static GameObject Model(string file)
        {
            var path = $"{Models}/{file}";
            var g = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (g == null) Debug.LogWarning("Missing model " + path);
            return g;
        }

        static PanelSettings Panel()
        {
            var tssPath = $"{UiDir}/RotorcraftTheme.tss";
            if (!File.Exists(tssPath))
            {
                File.WriteAllText(tssPath, "@import url(\"unity-theme://default\");\n@import url(\"Rotorcraft.uss\");\n");
                AssetDatabase.ImportAsset(tssPath);
            }
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(tssPath);
            var psPath = $"{UiDir}/Game Panel.asset";
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(psPath);
            if (ps == null) { ps = ScriptableObject.CreateInstance<PanelSettings>(); AssetDatabase.CreateAsset(ps, psPath); }
            ps.themeStyleSheet = theme;
            // sized like the browser at 1080p: 1 px of the style sheet = 1 px at 1920 wide, scaled with the window
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            ps.sortingOrder = 10;
            EditorUtility.SetDirty(ps);
            return ps;
        }

        [MenuItem("Rotorcraft/Build game scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Directory.CreateDirectory(SettingsDir);
            var tmPath = $"{SettingsDir}/Terrain.mat";
            var terrainMat = AssetDatabase.LoadAssetAtPath<Material>(tmPath);
            if (terrainMat == null) { terrainMat = new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit")); AssetDatabase.CreateAsset(terrainMat, tmPath); }

            // the world: generated behind the first loading screen
            var h1 = WorldMap.SiteById("kin_h1");
            var probe = new GameObject("Reflection probe").AddComponent<ReflectionProbe>();
            probe.transform.position = SimToUnity.Position(new Vec3(h1.x, WorldMap.SiteElevation(h1) + 12, h1.z));
            probe.mode = ReflectionProbeMode.Realtime; probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.size = new Vector3(3000, 800, 3000); probe.resolution = 256; probe.hdr = true;
            var world = new GameObject("Kinloch").AddComponent<KinlochWorld>();
            world.buildOnAwake = false;
            world.landmarks = Model("landmarks.glb");
            world.trees = Model("trees.glb");
            world.terrainMaterial = terrainMat;
            world.reflectionProbe = probe;
            world.surfaceLayers = KinlochLayers();
            ShaderKeeperTerrain(terrainMat, world.surfaceLayers);

            // the aircraft (its model is swapped in for each flight)
            var heliGo = new GameObject("Aircraft");
            heliGo.SetActive(false);
            var heli = heliGo.AddComponent<HelicopterController>();
            heli.managed = true; heli.startHint = false;
            heliGo.AddComponent<ModelAnimator>().heli = heli;
            heliGo.transform.position = SimToUnity.Position(new Vec3(h1.x, WorldMap.SiteElevation(h1), h1.z));
            heliGo.SetActive(true);

            // camera and post-processing
            var cam = Camera.main;
            cam.farClipPlane = 30000; cam.nearClipPlane = 0.3f;
            var chase = cam.gameObject.AddComponent<ChaseCamera>(); chase.target = heli; chase.menuBackdrop = true;
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true; camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var volume = new GameObject("Post-processing").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>($"{SettingsDir}/TestFlight Volume.asset");
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { l.transform.rotation = Quaternion.Euler(38, 30, 0); l.intensity = 1.5f; l.shadows = LightShadows.Soft; }
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 3000; RenderSettings.fogEndDistance = 28000;
            RenderSettings.fogColor = new Color(0.68f, 0.76f, 0.84f);

            // the game: UI, saves, cloud, flights
            var game = new GameObject("Game");
            game.SetActive(false);
            var doc = game.AddComponent<UIDocument>();
            doc.panelSettings = Panel();
            var lib = game.AddComponent<AircraftLibrary>();
            lib.kestrel = Model("kestrel_k2.glb"); lib.northstar = Model("northstar_175.glb"); lib.guardian = Model("guardian_s9.glb"); lib.nightjar = Model("nightjar_n4.glb");
            var actors = game.AddComponent<WorldActors>();
            actors.people = Model("people.glb"); actors.vehicles = Model("vehicles.glb");
            var flight = game.AddComponent<FlightSession>();
            flight.world = world; flight.heli = heli; flight.cam = chase; flight.library = lib; flight.actors = actors;
            game.AddComponent<Cloud>();
            var app = game.AddComponent<GameApp>();
            app.document = doc; app.flight = flight;
            app.styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>($"{UiDir}/Rotorcraft.uss");
            game.SetActive(true);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("Game scene saved to " + ScenePath);
        }

        /// <summary>
        /// The world's terrain is generated at run time, so a build would not know it needs the terrain shaders'
        /// layer, normal-map, add-pass and grass variants. A tiny terrain far below the world, with the same
        /// material, layers and a grass detail, makes the build keep them.
        /// </summary>
        static void ShaderKeeperTerrain(Material mat, TerrainLayer[] layers)
        {
            var path = $"{SettingsDir}/Shader keeper terrain.asset";
            var td = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
            if (td == null) { td = new TerrainData(); AssetDatabase.CreateAsset(td, path); }
            td.heightmapResolution = 33;
            td.size = new Vector3(8, 1, 8);
            td.terrainLayers = layers;
            td.alphamapResolution = 16;
            var a = new float[16, 16, layers.Length];
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) a[y, x, (x + y) % layers.Length] = 1;
            td.SetAlphamaps(0, 0, a);
            var grassTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{UiDir}/Images/LoadingGlow.png");
            td.SetDetailResolution(16, 16);
            td.detailPrototypes = new[] { new DetailPrototype { prototypeTexture = grassTex, renderMode = DetailRenderMode.GrassBillboard, usePrototypeMesh = false } };
            var d = new int[16, 16]; d[8, 8] = 1; td.SetDetailLayer(0, 0, 0, d);
            EditorUtility.SetDirty(td);
            var go = Terrain.CreateTerrainGameObject(td);
            go.name = "Shader keeper (terrain variants for the build)";
            go.transform.position = new Vector3(0, -20000, 0);
            var t = go.GetComponent<Terrain>();
            t.materialTemplate = mat; t.drawInstanced = true;
            go.GetComponent<TerrainCollider>().enabled = false;
        }

        static TerrainLayer[] KinlochLayers()
        {
            var m = typeof(KinlochSceneBuilder).GetMethod("SurfaceLayers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            return (TerrainLayer[])m.Invoke(null, null);
        }

        /// <summary>
        /// Shaders the game finds by name (world materials, light points, the winch cable) are only kept in a
        /// build when something references them: a material for each in Resources does that, with the keyword
        /// variants the code switches on.
        /// </summary>
        static void KeepShaders()
        {
            var dir = "Assets/Rotorcraft/Resources/Shader refs";
            Directory.CreateDirectory(dir);
            void Keep(string shader, string name, params string[] keywords)
            {
                var s = Shader.Find(shader);
                if (s == null) { Debug.LogWarning("Shader not found: " + shader); return; }
                var path = $"{dir}/{name}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(s); AssetDatabase.CreateAsset(m, path); }
                m.shader = s;
                foreach (var k in keywords) m.EnableKeyword(k);
                EditorUtility.SetDirty(m);
            }
            Keep("Universal Render Pipeline/Lit", "Lit");
            Keep("Universal Render Pipeline/Lit", "Lit emissive", "_EMISSION");
            Keep("Universal Render Pipeline/Lit", "Lit transparent", "_SURFACE_TYPE_TRANSPARENT");
            Keep("Universal Render Pipeline/Unlit", "Unlit");
            Keep("Universal Render Pipeline/Unlit", "Unlit cutout", "_ALPHATEST_ON");
            Keep("Rotorcraft/Light Points", "Light points");
            // the terrain and the instanced trees are made at run time, so the build cannot see their instancing
            // variants in use: keep them all
            var tm = AssetDatabase.LoadAssetAtPath<Material>($"{SettingsDir}/Terrain.mat");
            if (tm != null) { tm.enableInstancing = true; EditorUtility.SetDirty(tm); }
            foreach (var m in new[] { "Lit", "Lit emissive", "Unlit" }) { var mm = AssetDatabase.LoadAssetAtPath<Material>($"{dir}/{m}.mat"); if (mm != null) { mm.enableInstancing = true; EditorUtility.SetDirty(mm); } }
            var gs = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var strip = gs.FindProperty("m_InstancingStripping");
            if (strip != null) { strip.intValue = 2; gs.ApplyModifiedPropertiesWithoutUndo(); }   // keep all instancing variants
            AssetDatabase.SaveAssets();
        }

        static void Common()
        {
            KeepShaders();
            Build();
            PlayerSettings.companyName = "Rotorcraft";
            PlayerSettings.productName = "Rotorcraft";
            PlayerSettings.bundleVersion = "1.0.0";
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>($"{UiDir}/Images/logo-192.jpg");
            if (icon != null) PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.show = false;
        }

        static void Report(BuildReport r, string what)
        {
            var s = r.summary;
            Debug.Log($"{what} build: {s.result}, {s.totalSize / 1048576.0:F0} MB, {s.totalErrors} errors, at {s.outputPath}");
            if (s.result != BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(1);
        }

        [MenuItem("Rotorcraft/Build for Windows")]
        public static void BuildWindows()
        {
            Common();
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultScreenWidth = 1920; PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            var out_ = Path.GetFullPath("../Builds/Windows/Rotorcraft.exe");
            var r = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = out_, target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
            Report(r, "Windows");
        }

        [MenuItem("Rotorcraft/Build for the web")]
        public static void BuildWeb()
        {
            Common();
            PlayerSettings.WebGL.template = "PROJECT:Rotorcraft";   // Assets/WebGLTemplates/Rotorcraft: full window, the game's own loading page
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;   // works on any static host, Netlify included
            PlayerSettings.WebGL.memorySize = 1024;
            PlayerSettings.WebGL.initialMemorySize = 512;
            PlayerSettings.WebGL.maximumMemorySize = 4096;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.runInBackground = true;
            var out_ = Path.GetFullPath("../Builds/Web");
            var r = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = out_, target = BuildTarget.WebGL, options = BuildOptions.None });
            Report(r, "Web");
        }
    }
}
