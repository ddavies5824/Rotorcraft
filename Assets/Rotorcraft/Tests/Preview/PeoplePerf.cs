// What the people pack costs, at a handheld's screen (1280 x 800) on Balanced graphics: the heliport with no people, then
// 50 people walking near the camera (10 to 55 m), the same 50 far off (110 to 300 m), and the near crowd again with the
// distance tiers off (every part, every frame, every shadow). Frame, main-thread and crowd (NPC.Crowd: evaluating the
// clips) times, batches and triangles go to Logs/people-perf.txt. Explicit: run on purpose.
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

namespace Rotorcraft.Preview.Tests
{
    [Explicit("Benchmark; run on purpose")]
    public class PeoplePerf
    {
        static readonly string[] Ids = { "pilot", "copilot", "winch_operator", "winchman_paramedic", "ground_marshaller", "offshore_hdo", "offshore_passenger", "casualty_hiker_m", "casualty_hiker_f", "casualty_sailor", "casualty_older_walker" };

        [UnityTest, Timeout(1800000)]
        public IEnumerator Crowd()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            LogAssert.ignoreFailingMessages = true;
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode("Assets/Rotorcraft/Scenes/Kinloch.unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null;
            yield return new WaitForSeconds(1);
            QualitySettings.SetQualityLevel(1, true);   // Balanced
            var cam = Camera.main;
            cam.GetComponent<ChaseCamera>().enabled = false;
            var heli = Object.FindAnyObjectByType<HelicopterController>();
            if (heli != null) heli.gameObject.SetActive(false);
            var rt = new RenderTexture(1280, 800, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.fieldOfView = 60;
            var host = new GameObject("people");
            var lib = host.AddComponent<NpcLibrary>();
            lib.EnsureLoaded();

            var pad = WorldMap.SiteById("kin_h1");
            var centre = NpcLibrary.OnGround(SimToUnity.Position(new Vec3(pad.x + 30, 0, pad.z + 30)));
            var crowd = new List<(Npc n, float r, float a, float speed)>();
            var rnd = new System.Random(5);
            var count = 50;
            void Make(float r0, float r1)
            {
                foreach (var c in crowd) Object.Destroy(c.n.gameObject);
                crowd.Clear();
                for (int i = 0; i < count; i++)
                {
                    var n = lib.Spawn(Ids[i % Ids.Length], host.transform);
                    crowd.Add((n, r0 + (float)rnd.NextDouble() * (r1 - r0), (float)rnd.NextDouble() * 6.283f, 1.1f + (float)rnd.NextDouble() * 0.6f));
                }
            }

            var main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            var npcTime = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "NPC.Crowd", 1);
            var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count", 1);
            var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count", 1);
            var sb = new StringBuilder();
            sb.AppendLine($"People benchmark, {rt.width}x{rt.height}, quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}, {SystemInfo.graphicsDeviceName}, {SystemInfo.processorType}");
            var legs = new (string name, float r0, float r1, bool tiers, float shadow, float detail)[]
            {
                ("no people", 0, 0, true, 60, 25),
                ("50 near, 10-55 m", 10, 55, true, 60, 25),
                ("50 far, 110-300 m", 110, 300, true, 60, 25),
                ("20 near, 10-55 m", 10, 55, true, 60, 25),
                ("50 near, tiers off", 10, 55, false, 60, 25),
                ("50 near, as the notes", 10, 55, true, 120, 60),
            };
            foreach (var leg in legs)
            {
                NpcLibrary.Tiers = leg.tiers; NpcLibrary.ShadowRange = leg.shadow; NpcLibrary.DetailRange = leg.detail;
                count = leg.name.StartsWith("20") ? 20 : 50;
                if (leg.r1 > 0) Make(leg.r0, leg.r1); else { foreach (var c in crowd) Object.Destroy(c.n.gameObject); crowd.Clear(); }
                const int N = 200;
                var dt = new List<float>(); var mt = new List<double>(); var nt = new List<double>(); var bc = new List<long>(); var tc = new List<long>();
                float t = 0;
                for (int i = 0; i < N + 30; i++)
                {
                    t += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                    // everyone walks round the centre, the camera turns slowly at 6 m, looking out across them
                    foreach (var (n, r, a, speed) in crowd)
                    {
                        var ang = a + t * speed / r;
                        var p = NpcLibrary.OnGround(centre + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * r);
                        n.transform.SetPositionAndRotation(p, Quaternion.LookRotation(new Vector3(-Mathf.Sin(ang), 0, Mathf.Cos(ang))));
                        n.Cycle("walk", (t * speed + a * 10) / n.Stride("walk"));
                    }
                    var look = t * 0.15f;
                    cam.transform.position = centre + Vector3.up * 6;
                    cam.transform.rotation = Quaternion.Euler(leg.r0 > 100 ? 4 : 14, look * Mathf.Rad2Deg, 0);
                    yield return null;
                    if (i < 30) continue;
                    dt.Add(Time.unscaledDeltaTime * 1000);
                    mt.Add(main.LastValue / 1e6); nt.Add(npcTime.LastValue / 1e6); bc.Add(batches.LastValue); tc.Add(tris.LastValue);
                }
                dt.Sort();
                sb.AppendLine($"{leg.name,-22} frame {dt.Average():F2} ms (worst 5% {dt[(int)(dt.Count * 0.95)]:F2})  main {mt.Average():F2} ms  crowd {nt.Average():F2} ms  batches {bc.Average():F0}  tris {tc.Average() / 1e6:F2} M");
            }
            NpcLibrary.Tiers = true; NpcLibrary.ShadowRange = 60; NpcLibrary.DetailRange = 25;
            main.Dispose(); npcTime.Dispose(); batches.Dispose(); tris.Dispose();
            cam.targetTexture = null; rt.Release();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/people-perf.txt", sb.ToString());
            Debug.Log(sb.ToString());
            Assert.Pass(sb.ToString());
        }
    }
}
