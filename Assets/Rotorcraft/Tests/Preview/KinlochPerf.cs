// Frame-rate benchmark for the Kinloch scene: the camera flies a few short legs (low at the heliport, low over
// the woods, high over the farmland) and the frame time, main-thread time, batches and triangles are written to
// Logs/perf.txt. Explicit: run on purpose with
//   Unity.exe -batchmode -projectPath <project> -runTests -testPlatform PlayMode -testFilter KinlochPerf
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Rotorcraft.Presentation;
using Rotorcraft.Sim;
using Rotorcraft.World;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Terrain = UnityEngine.Terrain;

namespace Rotorcraft.Preview.Tests
{
    [Explicit("Benchmark; run on purpose")]
    public class KinlochPerf
    {
        static Vector3 At(double x, double z, double above) => SimToUnity.Position(new Vec3(x, WorldMap.TerrainHeight(x, z) + above, z));

        [UnityTest, Timeout(1800000)]
        public IEnumerator Fly()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            SceneManager.LoadScene("Kinloch");
            yield return null;
            yield return new WaitForSeconds(1);
            var cam = Camera.main;
            cam.GetComponent<ChaseCamera>().enabled = false;
            var heli = Object.FindAnyObjectByType<HelicopterController>();
            if (heli != null) heli.SetPaused(true);
            var rt = new RenderTexture(2560, 1440, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;

            var legs = new (string name, Vector2 a, Vector2 b, float agl)[]
            {
                ("heliport low", new Vector2(80, 120), new Vector2(-150, -60), 12),
                ("woods low", new Vector2(-3300, -1500), new Vector2(-4200, -400), 60),
                ("farmland 300m", new Vector2(-1500, -1500), new Vector2(-3000, -2600), 300),
                ("village low", new Vector2(-600, 400), new Vector2(-1400, 900), 40),
                ("- no grass", new Vector2(80, 120), new Vector2(-150, -60), 12),
                ("- no trees", new Vector2(80, 120), new Vector2(-150, -60), 12),
                ("- no shadows", new Vector2(80, 120), new Vector2(-150, -60), 12),
                ("night heliport", new Vector2(80, 120), new Vector2(-150, -60), 12),
                ("night town", new Vector2(-300, -300), new Vector2(-900, -1000), 80),
            };
            var terrains = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            var treeField = Object.FindAnyObjectByType<TreeField>();
            var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
            var main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count", 1);
            var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count", 1);
            var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count", 1);
            var gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            var gcCount = System.GC.CollectionCount(0);
            var sb = new StringBuilder();
            sb.AppendLine($"Kinloch benchmark, {rt.width}x{rt.height}, {SystemInfo.graphicsDeviceName}, {SystemInfo.processorType}");
            var all = new List<float>();
            foreach (var leg in legs)
            {
                // the last legs repeat the heliport with one thing switched off, to see what each costs
                foreach (var t in terrains) t.drawTreesAndFoliage = leg.name != "- no grass";
                if (treeField != null) treeField.enabled = leg.name != "- no trees";
                if (sun != null) sun.shadows = leg.name == "- no shadows" ? LightShadows.None : LightShadows.Soft;
                if (leg.name.StartsWith("night") && SkyEnvironment.Current != null && SkyEnvironment.Current.timeOfDay != TimeOfDay.Night)
                {
                    SkyEnvironment.Current.Apply(TimeOfDay.Night, Rotorcraft.Sim.Weather.Presets["calm"]);
                    if (heli != null) heli.State.sys.beacon = heli.State.sys.nav = heli.State.sys.strobe = heli.State.sys.landingLight = true;
                }
                const int N = 150;
                var gpu = new List<double>(); var timings = new FrameTiming[1];
                var gc = new List<long>(); var gc0 = System.GC.CollectionCount(0);
                var dt = new List<float>(); var mt = new List<double>(); var bc = new List<long>(); var sp = new List<long>(); var tc = new List<long>();
                // at 120 kt (62 m/s) along the leg, as a helicopter would fly it
                var dir = (leg.b - leg.a).normalized; float flown = 0;
                for (int i = 0; i < N + 20; i++)
                {
                    flown += 62f * Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                    var p = leg.a + dir * flown;
                    var ahead = p + dir * 300;
                    cam.transform.position = At(p.x, p.y, leg.agl);
                    cam.transform.LookAt(At(ahead.x, ahead.y, leg.agl * 0.7));
                    yield return null;
                    FrameTimingManager.CaptureFrameTimings();
                    if (i < 20) continue;                          // let the terrain settle into the new view
                    if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0) gpu.Add(timings[0].gpuFrameTime);
                    dt.Add(Time.unscaledDeltaTime * 1000);
                    gc.Add(gcAlloc.LastValue);
                    mt.Add(main.LastValue / 1e6); bc.Add(batches.LastValue); sp.Add(setPass.LastValue); tc.Add(tris.LastValue);
                }
                dt.Sort(); all.AddRange(dt);
                Debug.Log($"benchmark leg done: {leg.name}");
                sb.AppendLine($"{leg.name,-14} frame {dt.Average():F1} ms ({1000 / dt.Average():F0} fps, worst 5% {dt[(int)(dt.Count * 0.95)]:F1} ms)  main {mt.Average():F1} ms  gpu {(gpu.Count > 0 ? gpu.Average().ToString("F1") : "?")} ms  batches {bc.Average():F0}  setpass {sp.Average():F0}  tris {tc.Average() / 1e6:F1} M  gc {gc.Average() / 1024:F0} KB/frame, {System.GC.CollectionCount(0) - gc0} collections, {dt.Count(x => x > 25)} frames over 25 ms");
            }
            sb.AppendLine($"overall        frame {all.Average():F1} ms ({1000 / all.Average():F0} fps)");
            main.Dispose(); batches.Dispose(); setPass.Dispose(); tris.Dispose(); gcAlloc.Dispose();
            cam.targetTexture = null; rt.Release();
            Directory.CreateDirectory("Logs");
            File.AppendAllText("Logs/perf.txt", sb + "\n");
            Debug.Log(sb.ToString());
            Assert.Pass(sb.ToString());
        }
    }
}
