using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Rotorcraft.Presentation;
using Rotorcraft.Sim;
using Rotorcraft.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Rotorcraft.Preview.Tests
{
    /// <summary>
    /// The people pack in the game's own scene and light, for looking at: every character in a row on the grass and on a slope
    /// (Logs/people-lineup-*.png: feet on the ground, shadows, lit like the terrain, near and far), every clip as a strip of frames
    /// on a character it suits (Logs/people-clips-*.png, rows listed in people-clips.txt), and the winch (Logs/people-winch.png).
    /// </summary>
    public class PeopleShots
    {
        static IEnumerator OpenScene()
        {
            LogAssert.ignoreFailingMessages = true;
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode("Assets/Rotorcraft/Scenes/Kinloch.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            yield return new WaitForSeconds(1);
            Camera.main.GetComponent<ChaseCamera>().enabled = false;
            foreach (var hud in Object.FindObjectsByType<FlightHud>(FindObjectsSortMode.None)) hud.enabled = false;
            Directory.CreateDirectory("Logs");
            UnityEditor.ShaderUtil.allowAsyncCompilation = false;   // or the first frame draws a material in the editor's placeholder
#else
            yield return null;
#endif
        }

        static NpcLibrary Library()
        {
            var host = new GameObject("people");
            var lib = host.AddComponent<NpcLibrary>();
            lib.EnsureLoaded();
            return lib;
        }

        static void Capture(Camera cam, Texture2D sheet, int x, int y, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            var prev = RenderTexture.active; RenderTexture.active = rt;
            sheet.ReadPixels(new Rect(0, 0, w, h), x, y);
            RenderTexture.active = prev;
            rt.Release();
        }

        static void Save(Texture2D t, string name) { t.Apply(); File.WriteAllBytes($"Logs/{name}.png", t.EncodeToPNG()); Object.Destroy(t); }

        static readonly string[] Ids = { "pilot", "copilot", "winch_operator", "winchman_paramedic", "ground_marshaller", "offshore_hdo", "offshore_passenger", "casualty_hiker_m", "casualty_hiker_f", "casualty_sailor", "casualty_older_walker" };

        /// <summary>A piece of open land: flat grass near the heliport, or a slope of 10 to 20 degrees.</summary>
        static Vector3 Spot(bool slope)
        {
            var pad = WorldMap.SiteById("kin_h1");
            for (int r = 60; r < 3000; r += 40)
                for (int a = 0; a < 24; a++)
                {
                    double x = pad.x + r * System.Math.Cos(a * 0.2618), z = pad.z + r * System.Math.Sin(a * 0.2618);
                    var g = WorldMap.GroundAt(x, z);
                    if (g.surface != Surface.Grass && g.surface != Surface.Rock) continue;
                    var ok = slope ? g.slope > 0.17 && g.slope < 0.35 : g.slope < 0.04;
                    if (!ok || WorldMap.GroundAt(x + 8, z).surface == Surface.Water) continue;
                    return NpcLibrary.OnGround(SimToUnity.Position(new Vec3(x, 0, z)));
                }
            return NpcLibrary.OnGround(SimToUnity.Position(new Vec3(pad.x + 60, 0, pad.z + 60)));
        }

        [UnityTest, Timeout(1200000)]
        public IEnumerator Lineup()
        {
            yield return OpenScene();
            Object.FindAnyObjectByType<HelicopterController>().gameObject.SetActive(false);
            var lib = Library();
            var cam = Camera.main;
            foreach (var slope in new[] { false, true })
            {
                var at = Spot(slope);
                var row = new GameObject("row").transform;
                var right = Vector3.right;
                for (int i = 0; i < Ids.Length; i++)
                {
                    var p = NpcLibrary.OnGround(at + right * (i - 5) * 1.05f);
                    var n = lib.Spawn(Ids[i], row);
                    n.transform.SetPositionAndRotation(p, Quaternion.Euler(0, 180, 0));   // facing the camera, which is on -z
                    n.Play(i % 3 == 0 ? "idle_look_around" : "idle", 0, 1, i * 0.09f, true);
                }
                var tag = slope ? "slope" : "flat";
                foreach (var tod in new[] { TimeOfDay.Day, TimeOfDay.Dusk })
                {
                    if (slope && tod == TimeOfDay.Dusk) continue;
                    SkyEnvironment.Current?.Apply(tod, Rotorcraft.Sim.Weather.Presets["calm"]);
                    var views = new (string name, Vector3 from, Vector3 to, float fov)[]
                    {
                        ("front", at + new Vector3(0, 1.6f, -9.5f), at + Vector3.up * 0.95f, 62),
                        ("feet", at + new Vector3(-1.2f, 0.35f, -3.2f), at + new Vector3(-1.6f, 0.15f, 0), 50),
                        ("above", at + new Vector3(4, 9, -9), at + Vector3.up * 0.5f, 50),
                        ("80m", at + new Vector3(30, 25, -70), at + Vector3.up * 0.9f, 20),
                    };
                    var sheet = new Texture2D(1600 * 2, 900 * 2, TextureFormat.RGB24, false);
                    for (int v = 0; v < views.Length; v++)
                    {
                        cam.fieldOfView = views[v].fov;
                        cam.transform.position = views[v].from;
                        cam.transform.LookAt(views[v].to);
                        for (int f = 0; f < 8; f++) yield return null;
                        Capture(cam, sheet, (v % 2) * 1600, (1 - v / 2) * 900, 1600, 900);
                    }
                    Save(sheet, $"people-lineup-{tag}-{tod.ToString().ToLowerInvariant()}");
                }
                Object.Destroy(row.gameObject);
                yield return null;
            }
            Assert.Pass("pictures written to Logs/");
        }

        /// <summary>The character each clip is shown on, and how far up its root goes to keep it in view above the grass.</summary>
        static (string id, float up) For(string clip)
        {
            if (clip.StartsWith("winch_double_casualty")) return ("casualty_hiker_m", 1.0f);
            if (clip.StartsWith("winch_") || clip == "hang_idle" || clip == "climb_ladder") return ("winchman_paramedic", clip == "climb_ladder" ? 0 : 1.0f);
            if (clip.StartsWith("marshal_")) return ("ground_marshaller", 0);
            if (clip == "pilot_seated_controls" || clip == "sit_chair" || clip.Contains("_to_")) return ("pilot", 0);
            if (clip == "stretcher_carry_walk" || clip == "kneel_idle") return ("winchman_paramedic", 0);
            if (clip == "thumbs_up" || clip == "thumbs_up_big" || clip == "point") return ("offshore_hdo", 0);
            if (clip == "tread_water") return ("casualty_sailor", 1.4f);
            if (clip == "swim") return ("casualty_sailor", 0.7f);
            if (clip.StartsWith("injured") || clip == "wave_help" || clip == "lie_back" || clip == "sit_ground") return ("casualty_hiker_f", 0);
            if (clip == "crouch_walk" || clip == "run") return ("copilot", 0);
            return ("casualty_older_walker", 0);
        }

        [UnityTest, Timeout(1800000)]
        public IEnumerator ClipStrips()
        {
            yield return OpenScene();
            Object.FindAnyObjectByType<HelicopterController>().gameObject.SetActive(false);
            SkyEnvironment.Current?.Apply(TimeOfDay.Day, Rotorcraft.Sim.Weather.Presets["calm"]);
            var lib = Library();
            var cam = Camera.main;
            var at = Spot(false);
            var names = lib.ClipNames.ToList();
            const int W = 300, H = 380, Frames = 6, Rows = 6;
            var index = new System.Text.StringBuilder();
            for (int page = 0; page * Rows < names.Count; page++)
            {
                var sheet = new Texture2D(W * Frames, H * Rows, TextureFormat.RGB24, false);
                index.AppendLine($"people-clips-{page + 1}.png (rows top to bottom; frames left to right through the clip):");
                for (int r = 0; r < Rows && page * Rows + r < names.Count; r++)
                {
                    var clip = names[page * Rows + r];
                    var (id, up) = For(clip);
                    var n = lib.Spawn(id, null);
                    var lying = clip == "lie_back" || clip == "swim" || clip == "sit_ground";
                    n.transform.SetPositionAndRotation(at + Vector3.up * up, Quaternion.Euler(0, 210, 0));
                    var c = lib.Clip(clip);
                    index.AppendLine($"  row {r + 1}: {clip} on {id} ({c.length:F2} s, {(c.loop ? "loop" : "once")}), frames at {string.Join(", ", Enumerable.Range(0, Frames).Select(f => $"{c.length * f / Frames:F2}"))} s");
                    for (int f = 0; f < Frames; f++)
                    {
                        n.Play(clip, 0, 0, (float)f / Frames, true);
                        n.Evaluate(0);
                        cam.fieldOfView = 34;
                        var look = at + Vector3.up * (up + (lying ? 0.45f : 0.95f));
                        cam.transform.position = look + new Vector3(2.2f, lying ? 2.6f : 0.6f, -4.4f);
                        cam.transform.LookAt(look);
                        for (int k = 0; k < 2; k++) yield return null;
                        Capture(cam, sheet, f * W, (Rows - 1 - r) * H, W, H);
                    }
                    Object.Destroy(n.gameObject);
                    yield return null;
                }
                Save(sheet, $"people-clips-{page + 1}");
            }
            File.WriteAllText("Logs/people-clips.txt", index.ToString());
            Assert.Pass(index.ToString());
        }

        /// <summary>The townsfolk (AmbientLife's walkers) in each town: they exist, walk (the hips move between frames), stand on the drawn
        /// ground, and are drawn close up and from a circling aircraft's height. Logs/people-town-*.png.</summary>
        [UnityTest, Timeout(1200000)]
        public IEnumerator Townsfolk()
        {
            yield return OpenScene();
            Object.FindAnyObjectByType<HelicopterController>().gameObject.SetActive(false);
            SkyEnvironment.Current?.Apply(TimeOfDay.Day, Rotorcraft.Sim.Weather.Presets["calm"]);
            var host = new GameObject("World actors");
            host.SetActive(false);
            var actors = host.AddComponent<WorldActors>();
#if UNITY_EDITOR
            actors.people = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Rotorcraft/Models/people.glb");
            actors.vehicles = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Rotorcraft/Models/vehicles.glb");
#endif
            host.SetActive(true);
            var cam = Camera.main;
            // in batch mode a camera with no target draws nothing, so every person reads as off screen and is (rightly) not animated
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt;
            var report = new System.Text.StringBuilder();
            var shots = 0;
            foreach (var town in Places.SETTLEMENTS)
            {
                var c = NpcLibrary.OnGround(SimToUnity.Position(new Vec3(town.x, 0, town.z)));
                cam.fieldOfView = 50;
                cam.transform.position = c + new Vector3(0, 120, -160);
                cam.transform.LookAt(c);
                for (int f = 0; f < 40; f++) yield return null;
                var near = Object.FindObjectsByType<Npc>(FindObjectsSortMode.None).Where(n => n.isActiveAndEnabled && Vector3.Distance(n.transform.position, c) < 400).ToList();
                if (near.Count == 0) { report.AppendLine($"{town.name}: no walkers within 400 m"); continue; }
                // walking: the hips move; on the ground: the root is at the drawn ground (or the 0 m shore floor)
                var n0 = near.FirstOrDefault(n => n.GetComponentInChildren<SkinnedMeshRenderer>().isVisible) ?? near[0];
                var hips = n0.GetComponentsInChildren<Transform>().First(t => t.name == Npc.HipsBone);
                var h0 = hips.localRotation;
                for (int f = 0; f < 10; f++) yield return null;
                var turned = Quaternion.Angle(h0, hips.localRotation);
                var off = near.Max(n => Mathf.Abs(n.transform.position.y - Mathf.Max(NpcLibrary.Ground(n.transform.position.x, n.transform.position.z), 0)));
                report.AppendLine($"{town.name}: {near.Count} walkers within 400 m, clip {n0.Clip}, hips turned {turned:F1} deg in 10 frames, furthest root from the ground {off * 100:F1} cm");
                if (shots++ < 2)
                {
                    var sheet = new Texture2D(1600 * 2, 900, TextureFormat.RGB24, false);
                    Capture(cam, sheet, 0, 0, 1600, 900);
                    var p = n0.transform.position;
                    cam.transform.position = p + n0.transform.forward * 5 + Vector3.up * 1.6f + n0.transform.right * 2;
                    cam.transform.LookAt(p + Vector3.up * 0.9f);
                    for (int f = 0; f < 4; f++) yield return null;
                    Capture(cam, sheet, 1600, 0, 1600, 900);
                    Save(sheet, $"people-town-{shots}");
                }
            }
            cam.targetTexture = null; rt.Release();
            File.WriteAllText("Logs/people-town.txt", report.ToString());
            Assert.Pass(report.ToString());
        }

        /// <summary>The winch with the new winchman: going down, on scene just off the ground, standing on the ground, and coming up with
        /// the casualty in the strop. The aircraft is left out; the wire comes down from where its hoist arm would be.</summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator Winch()
        {
            yield return OpenScene();
            var heli = Object.FindAnyObjectByType<HelicopterController>();
            var s = heli.State; var ac = heli.Aircraft;
            heli.gameObject.SetActive(false);
            var host = new GameObject("World actors");
            host.SetActive(false);
            var actors = host.AddComponent<WorldActors>();
#if UNITY_EDITOR
            actors.people = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Rotorcraft/Models/people.glb");
            actors.vehicles = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Rotorcraft/Models/vehicles.glb");
#endif
            host.SetActive(true);
            var cam = Camera.main;
            SkyEnvironment.Current?.Apply(TimeOfDay.Day, Rotorcraft.Sim.Weather.Presets["calm"]);
            var spot = Spot(false);
            var sim = new Vec3(spot.x, spot.y, -spot.z);
            s.pos = new Vec3(sim.x, spot.y + 22, sim.z);
            var gy = spot.y;
            var stages = new (string name, HoistPhase phase, double hookY, bool attached)[]
            {
                ("lowering", HoistPhase.Lowering, gy + 12, false),
                ("on scene", HoistPhase.OnScene, gy + 1.75 + 0.6, false),
                ("on the ground", HoistPhase.OnScene, gy + 1.75, false),
                ("raising with the casualty", HoistPhase.Raising, gy + 8, true),
            };
            var sheet = new Texture2D(1000 * 2, 800 * 2, TextureFormat.RGB24, false);
            for (int k = 0; k < stages.Length; k++)
            {
                var st = stages[k];
                var h = new HoistState { phase = st.phase, hook = new Vec3(sim.x + 1.5, st.hookY, sim.z + 0.5), attached = st.attached };
                for (int f = 0; f < 6; f++) { actors.UpdateHoist(h, s, ac); yield return null; }
                var hook = SimToUnity.Position(h.hook);
                var look = hook + Vector3.down * (st.hookY - gy < 3 ? 0.9f : 1.1f);
                cam.fieldOfView = 40;
                cam.transform.position = look + new Vector3(4.5f, 0.8f, -4.5f);
                cam.transform.LookAt(look);
                for (int f = 0; f < 3; f++) { actors.UpdateHoist(h, s, ac); yield return null; }
                Capture(cam, sheet, (k % 2) * 1000, (1 - k / 2) * 800, 1000, 800);
            }
            Save(sheet, "people-winch");
            Assert.Pass("pictures written to Logs/");
        }
    }
}
