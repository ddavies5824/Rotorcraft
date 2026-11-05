// A benchmark of the real game, started with "-perf <folder>" on the command line (add -vr to measure the headset, or
// -perfscale 1.425 at 2560x1312 to draw as many pixels as a Quest 3S on Link does): it flies a Guardian to a few places with
// a saved game of its own in that folder, not the player's, measures how long the frames take, then turns features off one
// at a time to show what each costs. It writes <folder>/perf.txt (and markers.txt from a development build) and quits.
#if !UNITY_WEBGL
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Rotorcraft.Game;
using Rotorcraft.Sim;
using Rotorcraft.World;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using SysTerrain = UnityEngine.Terrain;

namespace Rotorcraft.Presentation.UI
{
    public sealed class PerfRun : MonoBehaviour
    {
        public string folder;
        readonly StringBuilder report = new StringBuilder();
        FlightSession f;

        public static string ArgFolder() => Arg("-perf");

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        IEnumerator Start()
        {
            var app = GameApp.Instance;
            Directory.CreateDirectory(folder);
            QualitySettings.vSyncCount = 0;
            yield return new WaitForSecondsRealtime(3);
            var asked = Array.IndexOf(Environment.GetCommandLineArgs(), "-vr") >= 0;
            for (int i = 0; asked && Vr.Available && !Vr.On && i < 40; i++) yield return new WaitForSecondsRealtime(0.25f);
            f = app.flight;
            var ac = Aircraft.Get("guardian");
            app.FlyFreeplay(new FreeplayConfig { aircraftId = "guardian", kit = Loadout.DefaultKit(ac), fuelKg = ac.fuelCapacity * 0.6, pace = FreeplayPace.Off, weatherId = "calm", tod = TimeOfDay.Day });
            while (!f.Active || f.Runtime == null) yield return null;
            yield return new WaitForSecondsRealtime(4);
            f.SkipForTour();
#if DEVELOPMENT_BUILD
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-touchcheck") >= 0)
            {
                yield return TouchCheck();
                File.WriteAllText(Path.Combine(folder, "touch.txt"), report.ToString());
                Application.Quit();
                yield break;
            }
#endif

            var urp = UniversalRenderPipeline.asset;
            var scale = float.TryParse(Arg("-perfscale"), out var sc) ? sc : 1f;
            urp.renderScale = scale;
            var cam = Camera.main;
            var camData = cam.GetUniversalAdditionalCameraData();
            Line($"VR on: {Vr.On}   screen {Screen.width}x{Screen.height}, drawn at {Screen.width * scale:F0}x{Screen.height * scale:F0}   quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}   development build {Debug.isDebugBuild}");
            Line($"{"",-26}{"fps",6}{"frame",7}{"p95",7}{"main",7}{"render",7}{"gpu",7}{"batches",9}{"tris(k)",9}");

            var village = Spot(-1000, 650, 40);
            var places = new (string name, Vec3 at)[] { ("heliport low", Spot(-40, 45, 12)), ("village low", village), ("woods low", Spot(-3750, -950, 60)), ("farmland 300 m", Spot(-2200, -2000, 300)), ("sea 150 m", Spot(1800, -1000, 150)) };
            foreach (var p in places) yield return Measure(p.name, p.at);

            if (Debug.isDebugBuild) yield return Markers("markers.txt", village);

            // what each thing costs, over the woods, the heaviest place
            var at = places[2].at;
            var sun = RenderSettings.sun != null ? RenderSettings.sun : FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
            var terrains = FindObjectsByType<SysTerrain>(FindObjectsSortMode.None);
            var trees = FindAnyObjectByType<TreeField>();
            var life = FindAnyObjectByType<AmbientLife>();
            var volume = FindAnyObjectByType<Volume>();
            Line("");
            yield return Measure("woods again", at);
            yield return Without("no ambient occlusion", at, () => { foreach (var o in VrGraphics.AmbientOcclusion()) o.SetActive(false); }, () => { foreach (var o in VrGraphics.AmbientOcclusion()) o.SetActive(true); });
            yield return Without("no sun shadows", at, () => sun.shadows = LightShadows.None, () => sun.shadows = LightShadows.Soft);
            yield return Without("shadows to 150 m", at, () => urp.shadowDistance = 150, () => urp.shadowDistance = 300);
            yield return Without("2 shadow cascades", at, () => urp.shadowCascadeCount = 2, () => urp.shadowCascadeCount = 4);
            yield return Without("shadow map 2048", at, () => urp.mainLightShadowmapResolution = 2048, () => urp.mainLightShadowmapResolution = 4096);
            yield return Without("no trees", at, () => trees.enabled = false, () => trees.enabled = true);
            yield return Without("full trees to 300 m", at, () => trees.nearDistance = 300, () => trees.nearDistance = 450);
            yield return Without("trees to 2500 m", at, () => trees.farDistance = 2500, () => trees.farDistance = 3200);
            yield return Without("no grass", at, () => { foreach (var t in terrains) t.drawTreesAndFoliage = false; }, () => { foreach (var t in terrains) t.drawTreesAndFoliage = true; });
            yield return Without("terrain casts no shadows", at, () => { foreach (var t in terrains) t.shadowCastingMode = ShadowCastingMode.Off; }, () => { foreach (var t in terrains) t.shadowCastingMode = ShadowCastingMode.On; });
            yield return Without("terrain pixel error x2", at, () => { foreach (var t in terrains) t.heightmapPixelError *= 2; }, () => { foreach (var t in terrains) t.heightmapPixelError /= 2; });
            yield return Without("no ambient life", at, () => life.enabled = false, () => life.enabled = true);
            yield return Without("no SMAA", at, () => camData.antialiasing = AntialiasingMode.None, () => camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing);
            yield return Without("FXAA", at, () => camData.antialiasing = AntialiasingMode.FastApproximateAntialiasing, () => camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing);
            yield return Without("no post-processing", at, () => camData.renderPostProcessing = false, () => camData.renderPostProcessing = true);
            yield return Without("no HDR", at, () => urp.supportsHDR = false, () => urp.supportsHDR = true);
            yield return Without("no opaque texture", at, () => urp.supportsCameraOpaqueTexture = false, () => urp.supportsCameraOpaqueTexture = true);
            yield return Without("no depth texture", at, () => urp.supportsCameraDepthTexture = false, () => urp.supportsCameraDepthTexture = true);
            yield return Without("half the pixels", at, () => urp.renderScale = scale * 0.707f, () => urp.renderScale = scale);
            if (volume != null) yield return Without("no volume", at, () => volume.enabled = false, () => volume.enabled = true);

            Line("");
            Line("With the headset's settings:");
            VrGraphics.Apply(true);
            foreach (var p in places) yield return Measure(p.name, p.at);
            if (Debug.isDebugBuild) yield return Markers("markers-vr.txt", village);
            VrGraphics.Apply(false);
            Line("");
            yield return Measure("woods at the end", at);
            File.WriteAllText(Path.Combine(folder, "perf.txt"), report.ToString());
            Debug.Log("Perf run done");
            Application.Quit();
        }

#if DEVELOPMENT_BUILD
        /// <summary>The whole way from a hand to the aircraft in the running game: a stand-in controller pushes a fingertip into the switches of the VR panel, one touch at a time, and the aircraft's systems are read back.</summary>
        IEnumerator TouchCheck()
        {
            var device = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.XR.XRController>("Stand-in right hand");
            UnityEngine.InputSystem.InputSystem.SetDeviceUsage(device, UnityEngine.InputSystem.CommonUsages.RightHand);
            yield return null;
            var rig = GameObject.Find("VR rig")?.transform;
            var panel = GameObject.Find("VR switch panel")?.transform;
            var hand = rig != null ? rig.Find("Right hand") : null;
            Line($"VR on {Vr.On}, rig {rig != null}, panel showing {panel != null}, hand {hand != null}");
            if (rig == null || panel == null || hand == null) yield break;
            var s = f.heli.State;
            // the hand's fingers point straight into the panel; the stand-in reports a grip pose, turned from the hand as the driver turns it
            var turn = Quaternion.Inverse(rig.rotation) * Quaternion.LookRotation(-panel.up, panel.forward) * Quaternion.Euler(0, 0, 180);
            Transform tip() => hand.Find("Model").GetChild(2).GetChild(1).GetChild(1);
            IEnumerator FingerTo(int cell, float depth, int frames)
            {
                float x = -0.086f + 0.043f * (cell % 5), y = 0.043f - 0.043f * (cell / 5);
                for (int i = 0; i < frames; i++)
                {
                    var target = panel.TransformPoint(x, y, -depth);
                    var reach = rig.InverseTransformVector(tip().TransformPoint(0, 0.028f, 0) - hand.position);
                    UnityEngine.InputSystem.InputSystem.QueueDeltaStateEvent(device.deviceRotation, turn);
                    UnityEngine.InputSystem.InputSystem.QueueDeltaStateEvent(device.devicePosition, rig.InverseTransformPoint(target) - reach);
                    yield return null;
                }
            }
            IEnumerator Touch(int cell)
            {
                yield return FingerTo(cell, 0.06f, 30);
                yield return FingerTo(cell, 0.03f, 6);
                yield return FingerTo(cell, 0.0f, 6);
                yield return FingerTo(cell, 0.06f, 12);
            }
            void Check(string what, bool ok) => Line($"{(ok ? "ok    " : "FAILED")} {what}");

            yield return FingerTo(5, 0.2f, 30);
            Check("the hand follows the stand-in controller", hand.gameObject.activeSelf);
            Check("nothing has changed yet", !s.sys.beacon && !s.sys.nav);
            yield return FingerTo(5, 0.06f, 30);
            yield return Touch(5);
            Check("touching BCN turns the beacon on", s.sys.beacon);
            Check("and nothing else", !s.sys.nav && !s.sys.strobe);
            yield return FingerTo(5, 0.06f, 20);
            Check("backing away leaves it on", s.sys.beacon);
            yield return Touch(5);
            Check("touching it again turns it off", !s.sys.beacon);
            yield return Touch(6);
            Check("touching NAV turns the nav lights on", s.sys.nav);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "touch.png"));
            yield return null; yield return null;
            var gov = s.sys.governor;
            yield return Touch(2);
            Check("touching GOV changes the governor", s.sys.governor != gov);
            yield return Touch(9);
            Check("touching SRCH LT turns the searchlight on", s.sys.searchlight);
            Check($"with the engine still off ({s.engine})", s.engine == EngineState.Off);
            yield return Touch(0);
            Check($"touching START starts the engine ({s.engine})", s.engine != EngineState.Off);
            yield return FingerTo(5, 0.2f, 10);
            UnityEngine.InputSystem.InputSystem.RemoveDevice(device);
        }
#endif

        static Vec3 Spot(double x, double z, double above) => new Vec3(x, WorldMap.TerrainHeight(x, z) + above, z);

        IEnumerator Without(string name, Vec3 at, Action off, Action on)
        {
            off();
            yield return Measure(name, at);
            on();
        }

        IEnumerator Measure(string name, Vec3 at)
        {
            yield return Hold(at, 1.5f);
            var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count", 1);
            var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count", 1);
            var timings = new FrameTiming[1];
            var frameMs = new List<double>(); var mains = new List<double>(); var renders = new List<double>(); var gpus = new List<double>();
            long batchSum = 0, trisSum = 0;
            var start = Time.realtimeSinceStartup; var end = start + 5f;
            while (Time.realtimeSinceStartup < end)
            {
                Pin(at);
                yield return null;
                frameMs.Add(Time.unscaledDeltaTime * 1000);
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
                {
                    mains.Add(timings[0].cpuMainThreadFrameTime - timings[0].cpuMainThreadPresentWaitTime);
                    renders.Add(timings[0].cpuRenderThreadFrameTime);
                    gpus.Add(timings[0].gpuFrameTime);
                }
                batchSum += batches.LastValue; trisSum += tris.LastValue;
            }
            batches.Dispose(); tris.Dispose();
            var seconds = Time.realtimeSinceStartup - start;
            int n = Math.Max(1, frameMs.Count);
            Line($"{name,-26}{n / seconds,6:F0}{Mid(frameMs),7:F1}{Pct(frameMs, 0.95),7:F1}{Mid(mains),7:F1}{Mid(renders),7:F1}{Mid(gpus),7:F1}{batchSum / n,9}{trisSum / n / 1000,9}");
        }

        /// <summary>The slowest named pieces of work (a development build only): nested ones are counted in their parents too.</summary>
        IEnumerator Markers(string file, Vec3 at)
        {
            UnityEngine.Profiling.Profiler.enabled = true;   // brings out each script's own timing
            yield return Hold(at, 1.5f);
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var names = new List<string>(); var recorders = new List<ProfilerRecorder>();
            foreach (var h in handles)
            {
                var d = ProfilerRecorderHandle.GetDescription(h);
                if (d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds) continue;
                names.Add($"{d.Category.Name}/{d.Name}");
                recorders.Add(ProfilerRecorder.StartNew(d.Category, d.Name, 1));
            }
            var sums = new double[recorders.Count];
            int frames = 0;
            var end = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < end)
            {
                Pin(at);
                yield return null;
                frames++;
                for (int i = 0; i < sums.Length; i++) sums[i] += recorders[i].LastValue;
            }
            foreach (var r in recorders) r.Dispose();
            UnityEngine.Profiling.Profiler.enabled = false;
            var text = new StringBuilder($"{names.Count} markers over {frames} frames, ms per frame\n");
            foreach (var i in Enumerable.Range(0, sums.Length).OrderByDescending(i => sums[i]).Take(140))
                text.AppendLine($"{sums[i] / 1e6 / Math.Max(1, frames),8:F2}  {names[i]}");
            File.WriteAllText(Path.Combine(folder, file), text.ToString());
        }

        static double Mid(List<double> v) => Pct(v, 0.5);

        static double Pct(List<double> v, double p)
        {
            if (v.Count == 0) return 0;
            v.Sort();
            return v[Math.Min(v.Count - 1, (int)(v.Count * p))];
        }

        IEnumerator Hold(Vec3 at, float seconds)
        {
            var end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) { Pin(at); yield return null; }
        }

        void Pin(Vec3 at)
        {
            var s = f.heli.State;
            s.pos = at.Clone(); s.vel = new Vec3(0, 0, 0); s.p = s.q = s.r = 0; s.theta = s.phi = 0; s.psi = 3.4;
            s.engine = EngineState.Running; s.engineN = 1; s.rotorE = 1; s.govTarget = 1; s.crashed = false; s.onGround = false;
        }

        void Line(string text)
        {
            report.AppendLine(text);
            Debug.Log("PERF " + text);
        }
    }
}
#endif
