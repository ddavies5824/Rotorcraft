// Pictures of the Kinloch scene as it looks when playing (terrain grass, trees and everything the game draws
// only at run time). Explicit: run on purpose with
//   Unity.exe -batchmode -projectPath <project> -runTests -testPlatform PlayMode -testFilter KinlochShots
using System.Collections;
using System.IO;
using NUnit.Framework;
using Rotorcraft.Game;
using Rotorcraft.Presentation;
using Rotorcraft.Presentation.UI;
using Rotorcraft.Sim;
using Rotorcraft.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Rotorcraft.Preview.Tests
{
    [Explicit("Renders pictures; run on purpose")]
    public class KinlochShots
    {
        static Vector3 At(double x, double z, double above) => SimToUnity.Position(new Vec3(x, WorldMap.TerrainHeight(x, z) + above, z));

        [UnityTest]
        public IEnumerator Render()
        {
            SceneManager.LoadScene("Kinloch");
            yield return null;                                   // the world builds in Awake
            yield return new WaitForSeconds(1);
            var cam = Camera.main;
            cam.GetComponent<ChaseCamera>().enabled = false;
            foreach (var hud in Object.FindObjectsByType<FlightHud>(FindObjectsSortMode.None)) hud.enabled = false;
            Directory.CreateDirectory("Logs");
            var shots = new (string name, Vector3 from, Vector3 to, float fov)[]
            {
                ("play-ground", At(-70, 60, 1.7), At(-370, 0, 2), 60),
                ("play-hedge", At(-1750, -2050, 2.0), At(-2100, -2150, 4), 60),
                ("play-fields", At(-2100, -1700, 160), At(-2900, -2300, 0), 55),
                ("play-heliport", At(60, 110, 25), At(0, 0, 1), 55),
                ("play-woods", At(-3600, -1200, 220), At(-4300, -300, 60), 55),
            };
            // the same at dusk and at night, with the Kestrel's lights on and the engine running
            var heli = Object.FindAnyObjectByType<HelicopterController>();
            var heliPos = heli.transform.position;
            var night = new (string name, Vector3 from, Vector3 to, float fov)[]
            {
                ("heliport", heliPos + heli.transform.rotation * new Vector3(9, 3.5f, 14), heliPos + Vector3.up * 1.2f, 55),
                ("town", At(200, 300, 180), At(-700, -900, 0), 55),
                ("village", At(-500, 300, 60), At(-1100, 700, 0), 60),
            };
            var list = new System.Collections.Generic.List<(string name, Vector3 from, Vector3 to, float fov)>(shots);
            foreach (var tod in new[] { TimeOfDay.Dusk, TimeOfDay.Night }) foreach (var n in night) list.Add(($"{tod}:{n.name}", n.from, n.to, n.fov));
            foreach (var s0 in list)
            {
                var s = s0;
                var colon = s.name.IndexOf(':');
                if (colon > 0)
                {
                    var tod = (TimeOfDay)System.Enum.Parse(typeof(TimeOfDay), s.name.Substring(0, colon));
                    if (SkyEnvironment.Current.timeOfDay != tod) SkyEnvironment.Current.Apply(tod, Rotorcraft.Sim.Weather.Presets["calm"]);
                    heli.State.sys.beacon = heli.State.sys.nav = heli.State.sys.strobe = heli.State.sys.landingLight = true;
                    s.name = $"play-{tod.ToString().ToLowerInvariant()}-{s.name.Substring(colon + 1)}";
                }
                cam.fieldOfView = s.fov;
                cam.transform.position = s.from;
                cam.transform.LookAt(s.to);
                for (int i = 0; i < 6; i++) yield return null;   // let the terrain stream its detail for the new view
                var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                var req = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes($"Logs/{s.name}.png", tex.EncodeToPNG());
                Object.Destroy(tex); rt.Release();
            }
            Assert.Pass("pictures written to Logs/");
        }

        static void Capture(Camera cam, string name, int w = 1600, int h = 900)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes($"Logs/{name}.png", tex.EncodeToPNG());
            Object.Destroy(tex); rt.Release();
        }

        /// <summary>
        /// Each aircraft hovering over the Kinloch pad, seen at a Quest eye's field of view from the pilot's seat (down
        /// past the panel, steeply down, and down towards the pilot's own side) and from outside, below the nose.
        /// </summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator SeatViews()
        {
            LogAssert.ignoreFailingMessages = true;
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode("Assets/Rotorcraft/Scenes/Kinloch.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            yield return new WaitForSeconds(1);
            var cam = Camera.main;
            cam.GetComponent<ChaseCamera>().enabled = false;
            foreach (var hud in Object.FindObjectsByType<FlightHud>(FindObjectsSortMode.None)) hud.enabled = false;
            Object.FindAnyObjectByType<HelicopterController>().gameObject.SetActive(false);
            cam.nearClipPlane = 0.03f;
            cam.fieldOfView = 90;
            Directory.CreateDirectory("Logs");
            var tag = System.Environment.GetEnvironmentVariable("RC_SHOT_TAG") ?? "seat";
            var pad = S("kin_h1");
            var at = SimToUnity.Position(new Vec3(pad.x, WorldMap.SiteElevation(pad) + 7, pad.z));
            const int Cell = 800;
            foreach (var (id, file) in new[] { ("kestrel", "kestrel_k2"), ("northstar", "northstar_175"), ("guardian", "guardian_s9"), ("nightjar", "nightjar_n4"), ("peregrine", "peregrine_p3") })
            {
                var holder = new GameObject(id);
                holder.transform.SetPositionAndRotation(at, Quaternion.identity);
                var model = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Rotorcraft/Models/Aircraft/{file}.gltf"), holder.transform);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.Euler(0, 180, 0);   // as HelicopterController does: the nose along +Z
                AircraftDress.Apply(model.transform, Aircraft.Get(id));
                Transform eye = null;
                foreach (var t in model.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "cockpit_only") t.gameObject.SetActive(true);
                    if (t.name == "eye") eye = t;
                }
                Assert.That(eye != null, id + " has an eye");
                var sheet = new Texture2D(Cell * 2, Cell * 2, TextureFormat.RGB24, false);
                var side = eye.position.x >= at.x ? 1 : -1;
                var views = new (Vector3 from, Quaternion look)[]
                {
                    (eye.position, Quaternion.Euler(45, 0, 0)),
                    (eye.position, Quaternion.Euler(68, 0, 0)),
                    (eye.position, Quaternion.Euler(55, side * 40, 0)),
                    (at + new Vector3(-2.5f, -3.4f, eye.position.z - at.z + 4.5f), Quaternion.LookRotation(eye.position + Vector3.down * 1.1f - (at + new Vector3(-2.5f, -3.4f, eye.position.z - at.z + 4.5f)))),
                };
                for (int v = 0; v < views.Length; v++)
                {
                    cam.transform.SetPositionAndRotation(views[v].from, views[v].look);
                    for (int i = 0; i < 4; i++) yield return null;
                    var rt = new RenderTexture(Cell, Cell, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                    var req = new RenderPipeline.StandardRequest { destination = rt };
                    if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                    else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, Cell, Cell), (v % 2) * Cell, (1 - v / 2) * Cell);
                    RenderTexture.active = prev;
                    rt.Release();
                }
                sheet.Apply();
                File.WriteAllBytes($"Logs/{tag}-{id}.png", sheet.EncodeToPNG());
                Object.Destroy(sheet);
                Object.Destroy(holder);
                yield return null;
            }
            Assert.Pass("pictures written to Logs/");
#else
            yield return null;
#endif
        }

        /// <summary>
        /// Each aircraft from the pilot's seat at a Quest eye's field of view: both hands on the controls, an open hand and
        /// a closing one in the air, and a hand ready to take hold (it turns blue).
        /// </summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator HandsInEachCockpit()
        {
            LogAssert.ignoreFailingMessages = true;
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode("Assets/Rotorcraft/Scenes/Kinloch.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            yield return new WaitForSeconds(1);
            var cam = Camera.main;
            cam.GetComponent<ChaseCamera>().enabled = false;
            foreach (var hud in Object.FindObjectsByType<FlightHud>(FindObjectsSortMode.None)) hud.enabled = false;
            Object.FindAnyObjectByType<HelicopterController>().gameObject.SetActive(false);
            cam.nearClipPlane = 0.03f;
            cam.fieldOfView = 90;
            Directory.CreateDirectory("Logs");
            var pad = S("kin_h1");
            var at = SimToUnity.Position(new Vec3(pad.x, WorldMap.SiteElevation(pad) + 1.2, pad.z));
            const int Cell = 800;
            foreach (var (id, file) in new[] { ("kestrel", "kestrel_k2"), ("northstar", "northstar_175"), ("guardian", "guardian_s9"), ("nightjar", "nightjar_n4"), ("peregrine", "peregrine_p3") })
            {
                var holder = new GameObject(id);
                holder.transform.SetPositionAndRotation(at, Quaternion.identity);
                var model = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Rotorcraft/Models/Aircraft/{file}.gltf"), holder.transform);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.Euler(0, 180, 0);
                AircraftDress.Apply(model.transform, Aircraft.Get(id));
                Transform eye = null;
                foreach (var t in model.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "cockpit_only") t.gameObject.SetActive(true);
                    if (t.name == "eye") eye = t;
                }
                var grips = Rotorcraft.VR.CockpitGrip.Find(model.transform, holder.transform, eye.position);
                Assert.That(grips.Count == 2 && grips.Exists(g => g.cyclic) && grips.Exists(g => !g.cyclic), $"{id} has a cyclic and a collective to hold: {grips.Count}");
                var cyclic = grips.Find(g => g.cyclic); var lever = grips.Find(g => !g.cyclic);
                foreach (var g in grips) Assert.That(Vector3.Distance(g.Anchor, eye.position) < 1.4f, $"{id}: {(g.cyclic ? "cyclic" : "collective")} is within reach of the seat: {Vector3.Distance(g.Anchor, eye.position):F2} m");

                // a squeeze far from anything: each hand takes its own control, from wherever it is
                var faraway = eye.position + new Vector3(0, -0.2f, 0.1f);
                Assert.That(Rotorcraft.VR.CockpitGrip.ToTake(grips, faraway, false, 0.18f, null) == cyclic, $"{id}: the right hand takes the cyclic from anywhere");
                Assert.That(Rotorcraft.VR.CockpitGrip.ToTake(grips, faraway, true, 0.18f, null) == lever, $"{id}: the left hand takes the collective from anywhere");
                // right beside the other control: it takes that one instead, so the hands can swap
                Assert.That(Rotorcraft.VR.CockpitGrip.ToTake(grips, lever.Anchor, false, 0.18f, null) == lever, $"{id}: a right hand beside the collective takes the collective");
                Assert.That(Rotorcraft.VR.CockpitGrip.ToTake(grips, cyclic.Anchor, true, 0.18f, null) == cyclic, $"{id}: a left hand beside the cyclic takes the cyclic");
                // a control already held is not taken away
                Assert.That(Rotorcraft.VR.CockpitGrip.ToTake(grips, faraway, false, 0.18f, cyclic) == null, $"{id}: the right hand leaves a cyclic the other hand holds");
                Assert.That(Rotorcraft.VR.CockpitGrip.ToTake(grips, cyclic.Anchor, true, 0.18f, cyclic) == lever, $"{id}: a left hand beside a held cyclic takes its own");

                var right = new Rotorcraft.VR.VrHand(holder.transform, false, 5);
                var left = new Rotorcraft.VR.VrHand(holder.transform, true, 5);
                var sheet = new Texture2D(Cell * 2, Cell, TextureFormat.RGB24, false);
                // on the controls, looking down between them
                right.root.SetPositionAndRotation(cyclic.Anchor, cyclic.HandRotation(holder.transform, false)); right.SetCurl(1);
                left.root.SetPositionAndRotation(lever.Anchor, lever.HandRotation(holder.transform, true)); left.SetCurl(1);
                var mid = (cyclic.Anchor + lever.Anchor) / 2;
                cam.transform.SetPositionAndRotation(eye.position, Quaternion.LookRotation(mid - eye.position, Vector3.up));
                for (int i = 0; i < 4; i++) yield return null;
                CaptureInto(cam, sheet, 0, Cell);
                // in the air in front of the face, posed as OpenXR reports a controller held out with the thumb up and leaning
                // forward: little finger to thumb up and forward, its Y back towards the wrist. An open right hand, a closing left.
                var ahead = eye.position + new Vector3(0, -0.15f, 0.42f);
                var gripPose = Quaternion.LookRotation(new Vector3(0, 0.8f, 0.6f), new Vector3(0, 0.6f, -0.8f));
                right.root.SetPositionAndRotation(ahead + new Vector3(0.14f, 0, 0), gripPose * Rotorcraft.VR.VrHand.FromGripPose); right.SetCurl(0);
                left.root.SetPositionAndRotation(ahead + new Vector3(-0.14f, 0, 0), gripPose * Rotorcraft.VR.VrHand.FromGripPose); left.SetCurl(0.6f); left.SetReady(true);
                cam.transform.SetPositionAndRotation(eye.position, Quaternion.LookRotation(ahead - eye.position, Vector3.up));
                for (int i = 0; i < 4; i++) yield return null;
                CaptureInto(cam, sheet, 1, Cell);
                sheet.Apply();
                File.WriteAllBytes($"Logs/hands-{id}.png", sheet.EncodeToPNG());
                Object.Destroy(sheet);
                right.Dispose(); left.Dispose();
                Object.Destroy(holder);
                yield return null;
            }
#else
            yield return null;
#endif
        }

        /// <summary>The VR switch panel in each cockpit: within reach and clear of what the pilot needs to see, with a fingertip on a switch; left, looking at it; right, the wide view ahead and down.</summary>
        [UnityTest]
        public IEnumerator SwitchPanelInEachCockpit()
        {
            LogAssert.ignoreFailingMessages = true;
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode("Assets/Rotorcraft/Scenes/Kinloch.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            yield return new WaitForSeconds(1);
            var cam = Camera.main;
            cam.GetComponent<ChaseCamera>().enabled = false;
            foreach (var hud in Object.FindObjectsByType<FlightHud>(FindObjectsSortMode.None)) hud.enabled = false;
            Object.FindAnyObjectByType<HelicopterController>().gameObject.SetActive(false);
            cam.nearClipPlane = 0.03f;
            Directory.CreateDirectory("Logs");
            var theme = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Rotorcraft/UI/Game Panel.asset").themeStyleSheet;
            var pad = S("kin_h1");
            var at = SimToUnity.Position(new Vec3(pad.x, WorldMap.SiteElevation(pad) + 1.2, pad.z));
            const int Cell = 800;
            foreach (var (id, file) in new[] { ("kestrel", "kestrel_k2"), ("northstar", "northstar_175"), ("guardian", "guardian_s9"), ("nightjar", "nightjar_n4"), ("peregrine", "peregrine_p3") })
            {
                var holder = new GameObject(id);
                holder.transform.SetPositionAndRotation(at, Quaternion.identity);
                var model = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Rotorcraft/Models/Aircraft/{file}.gltf"), holder.transform);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.Euler(0, 180, 0);
                AircraftDress.Apply(model.transform, Aircraft.Get(id));
                Transform eye = null;
                foreach (var t in model.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "cockpit_only") t.gameObject.SetActive(true);
                    if (t.name == "eye") eye = t;
                }
                var ac = Aircraft.Get(id);
                var state = FlightModel.CreateState(new CreateOptions { headingDeg = 0, fuel = ac.fuelCapacity * 0.5 });
                state.sys.beacon = true; state.sys.nav = true; state.sys.governor = true;
                var ap = Autopilot.NewAP(); ap.hdg = true;
                var panel = new Rotorcraft.VR.VrSwitchPanel(theme, 5);
                panel.Mount(holder.transform, eye.position);
                var hover = 7;
                panel.Draw(CockpitSwitches.Build(state, ac, ap, false), hover, -1);
                var reach = Vector3.Distance(panel.root.position, eye.position);
                Assert.That(reach < 0.6f, $"{id}: the panel is {reach:F2} m from the eye");

                // a right hand reaching in with its fingertip on the middle switch
                var hand = new Rotorcraft.VR.VrHand(holder.transform, false, 5);
                var target = panel.root.TransformPoint(new Vector3(0, 0, -0.002f));
                var towards = (panel.root.forward + Vector3.up * 0.2f).normalized;
                hand.root.rotation = Quaternion.LookRotation(Vector3.Cross(towards, Vector3.right), towards);
                hand.SetCurl(0, 1);
                hand.root.position += target - hand.IndexTip;
                Assert.That(Vector3.Distance(hand.IndexTip, target) < 0.001f, $"{id}: the fingertip reaches the switch");
                var press = new Rotorcraft.Inputs.FingerPress(CockpitSwitches.Columns, CockpitSwitches.Rows, Rotorcraft.VR.VrSwitchPanel.Width, Rotorcraft.VR.VrSwitchPanel.Height);
                press.Update(0, 0, 0.1f);
                var local = panel.root.InverseTransformPoint(hand.IndexTip);
                Assert.That(press.Update(local.x, local.y, -local.z), Is.EqualTo(hover), $"{id}: touching there presses the middle switch");

                var sheet = new Texture2D(Cell * 2, Cell, TextureFormat.RGB24, false);
                cam.fieldOfView = 60;
                cam.transform.SetPositionAndRotation(eye.position, Quaternion.LookRotation(panel.root.position - eye.position, Vector3.up));
                for (int i = 0; i < 6; i++) yield return null;
                CaptureInto(cam, sheet, 0, Cell);
                cam.fieldOfView = 110;
                cam.transform.SetPositionAndRotation(eye.position, Quaternion.LookRotation(holder.transform.forward + Vector3.down * 0.65f, Vector3.up));
                for (int i = 0; i < 4; i++) yield return null;
                CaptureInto(cam, sheet, 1, Cell);
                sheet.Apply();
                File.WriteAllBytes($"Logs/switches-{id}.png", sheet.EncodeToPNG());
                Object.Destroy(sheet);
                hand.Dispose();
                panel.Dispose();
                Object.Destroy(holder);
                yield return null;
            }
#else
            yield return null;
#endif
        }

        static GameObject Dressed(string id, string file, AircraftConfig ac, Vector3 at, float heading)
        {
#if UNITY_EDITOR
            var holder = new GameObject(id);
            holder.transform.SetPositionAndRotation(at, Quaternion.Euler(0, heading, 0));
            var model = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Rotorcraft/Models/Aircraft/{file}.gltf"), holder.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0, 180, 0);
            AircraftDress.Apply(model.transform, ac);
            return holder;
#else
            return null;
#endif
        }

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
#else
            yield return null;
#endif
        }

        /// <summary>The wind farm from two distances, a moment apart: blades, turning, facing the way the nacelle does.</summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator WindFarm()
        {
            yield return OpenScene();
            var cam = Camera.main;
            Object.FindAnyObjectByType<HelicopterController>().gameObject.SetActive(false);
            var t = Places.TURBINES[4];
            var face = TurbineRotors.Facing(t.yaw);
            var tower = SimToUnity.Position(new Vec3(t.x, WorldMap.TerrainHeight(t.x, t.z), t.z));
            var hub = tower + Vector3.up * (float)(t.hub + TurbineRotors.HubAboveTower) + face * TurbineRotors.HubAhead;
            var side = Vector3.Cross(Vector3.up, face);
            cam.fieldOfView = 55;
            cam.transform.position = hub + face * 170 + side * 90 - Vector3.up * 20;
            cam.transform.LookAt(hub);
            for (int i = 0; i < 20; i++) yield return null;
            Capture(cam, "fix-turbine-near");
            yield return new WaitForSeconds(0.7f);
            Capture(cam, "fix-turbine-near-later");
            cam.transform.position = hub + side * 150 + Vector3.up * 5;
            cam.transform.LookAt(hub);
            for (int i = 0; i < 6; i++) yield return null;
            Capture(cam, "fix-turbine-side");
            cam.transform.position = hub + face * 620 + side * 200 + Vector3.up * 70;
            cam.transform.LookAt(hub - Vector3.up * 30);
            for (int i = 0; i < 20; i++) yield return null;
            Capture(cam, "fix-turbine-farm");
            Assert.Pass("pictures written to Logs/");
        }

        /// <summary>The belly strobe at the moment of its flash, close up at dusk on the Guardian, and from further off.</summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator StrobeFlash()
        {
            yield return OpenScene();
            var cam = Camera.main;
            Object.FindAnyObjectByType<HelicopterController>().gameObject.SetActive(false);
            SkyEnvironment.Current.Apply(TimeOfDay.Dusk, Rotorcraft.Sim.Weather.Presets["calm"]);
            var pad = S("kin_h1");
            var at = SimToUnity.Position(new Vec3(pad.x, WorldMap.SiteElevation(pad) + 14, pad.z));
            var holder = Dressed("guardian", "guardian_s9", Aircraft.Get("guardian"), at, 0);
            const float Period = 2000;
            var glow = GlowPoints.Make("strobe", holder.transform);
            GlowPoints.Fill(glow.sharedMesh, new[]
            {
                new GlowPoint { pos = new Vector3(0, 0.63f, 0.45f), color = new Color(3.2f, 3.2f, 3.5f, 1), size = 1.1f, mode = Rotorcraft.World.LightMode.Strobe, period = Period, phase = 0.01f - Time.timeSinceLevelLoad / Period },
                new GlowPoint { pos = new Vector3(0, 3.47f, -7.73f), color = new Color(3.2f, 3.2f, 3.5f, 1), size = 1.1f, mode = Rotorcraft.World.LightMode.Strobe, period = Period, phase = 0.01f - Time.timeSinceLevelLoad / Period },
            });
            cam.fieldOfView = 44;
            foreach (var (name, away) in new[] { ("near", 15f), ("mid", 34f), ("far", 110f) })
            {
                cam.transform.position = at + new Vector3(away, -away * 0.1f, 1);
                cam.transform.LookAt(at + Vector3.up * 1.2f);
                for (int i = 0; i < 4; i++) yield return null;
                Capture(cam, "fix-strobe-" + name, 1200, 675);
            }
            Assert.Pass("pictures written to Logs/");
        }

        /// <summary>Each twin's console, at the pad and turned far out to sea: only real screens get instruments, and none lies askew.</summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator ConsoleDisplays()
        {
            yield return OpenScene();
            var cam = Camera.main;
            Object.FindAnyObjectByType<HelicopterController>().gameObject.SetActive(false);
            cam.nearClipPlane = 0.03f;
            cam.fieldOfView = 90;
#if UNITY_EDITOR
            var theme = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Rotorcraft/UI/Game Panel.asset").themeStyleSheet;
#else
            ThemeStyleSheet theme = null;
#endif
            var pad = S("kin_h1");
            var near = SimToUnity.Position(new Vec3(pad.x, WorldMap.SiteElevation(pad) + 1.2, pad.z));
            var results = new System.Text.StringBuilder();
            foreach (var (id, file) in new[] { ("northstar", "northstar_175"), ("guardian", "guardian_s9"), ("nightjar", "nightjar_n4"), ("peregrine", "peregrine_p3"), ("kestrel", "kestrel_k2") })
                foreach (var (where, at, heading) in new[] { ("pad", near, 0f), ("pad37", near, 37f), ("sea", near + new Vector3(29000, 40, 1700), 211f) })
                {
                    var ac = Aircraft.Get(id);
                    var holder = Dressed(id, file, ac, at, heading);
                    Transform eye = null, model = holder.transform.GetChild(0);
                    foreach (var t in model.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name == "cockpit_only") t.gameObject.SetActive(true);
                        if (t.name == "eye") eye = t;
                    }
                    var displays = new GameObject("displays").AddComponent<CockpitDisplays>();
                    displays.Bind(model, ac, theme, eye);
                    var state = FlightModel.CreateState(new CreateOptions { headingDeg = 0, fuel = ac.fuelCapacity * 0.5 });
                    displays.Set(state, ac, null, default, true);
                    results.Append($"{id} {where}:");
                    foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if (!r.name.StartsWith("Live display")) continue;
                        var below = model.InverseTransformPoint(eye.position).y - model.InverseTransformPoint(r.bounds.center).y;
                        results.Append($" {r.name.Substring(14)} {below:F2}");
                        if (id == "northstar" || id == "guardian") Assert.That(below, Is.LessThan(0.62f), $"{id} at {where}: {r.name} is on the console");
                    }
                    results.AppendLine();
                    if (where != "pad37")
                    {
                        cam.transform.SetPositionAndRotation(eye.position, holder.transform.rotation * Quaternion.Euler(34, 0, 0));
                        for (int i = 0; i < 8; i++) yield return null;
                        Capture(cam, $"fix-console-{id}-{where}", 1000, 800);
                    }
                    Object.Destroy(displays.gameObject);
                    Object.Destroy(holder);
                    yield return null;
                }
            File.WriteAllText("Logs/fix-console.txt", results.ToString());
            Debug.Log("CONSOLE:\n" + results);
            Assert.Pass("pictures written to Logs/");
        }

        /// <summary>Every paint scheme of the Nightjar, and that no two come out the same.</summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator NightjarPaint()
        {
            yield return OpenScene();
            var cam = Camera.main;
            Object.FindAnyObjectByType<HelicopterController>().gameObject.SetActive(false);
            var pad = S("kin_h1");
            var at = SimToUnity.Position(new Vec3(pad.x, WorldMap.SiteElevation(pad) + 1.2, pad.z));
            var skins = Aircraft.Get("nightjar").skins;
            const int W = 640, H = 400;
            var sheet = new Texture2D(W * 3, H * 2, TextureFormat.RGB24, false);
            var seen = new System.Collections.Generic.List<(string id, Color c)>();
            for (int k = 0; k < skins.Length; k++)
            {
                var ac = Aircraft.WithSkin(Aircraft.Get("nightjar"), skins[k].id);
                var holder = Dressed("nightjar", "nightjar_n4", ac, at, 0);
                cam.fieldOfView = 40;
                cam.transform.position = at + new Vector3(12, 2.2f, 7);
                cam.transform.LookAt(at + Vector3.up * 0.8f);
                for (int i = 0; i < 6; i++) yield return null;
                var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                var req = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
                var prev = RenderTexture.active; RenderTexture.active = rt;
                sheet.ReadPixels(new Rect(0, 0, W, H), (k % 3) * W, (1 - k / 3) * H);
                var one = new Texture2D(W, H, TextureFormat.RGB24, false);
                one.ReadPixels(new Rect(0, 0, W, H), 0, 0); one.Apply();
                RenderTexture.active = prev; rt.Release();
                var sum = Color.black; var n = 0;
                for (int y = H / 3; y < H * 2 / 3; y += 3) for (int x = W / 3; x < W * 2 / 3; x += 3) { sum += one.GetPixel(x, y); n++; }
                seen.Add((skins[k].id, sum / n));
                Object.Destroy(one);
                Object.Destroy(holder);
                yield return null;
            }
            sheet.Apply();
            File.WriteAllBytes("Logs/fix-paint-nightjar.png", sheet.EncodeToPNG());
            var report = string.Join("\n", seen.ConvertAll(s => $"{s.id}: {s.c.r:F3} {s.c.g:F3} {s.c.b:F3}"));
            File.WriteAllText("Logs/fix-paint-nightjar.txt", report);
            for (int a = 0; a < seen.Count; a++)
                for (int b = a + 1; b < seen.Count; b++)
                {
                    var d = Mathf.Abs(seen[a].c.r - seen[b].c.r) + Mathf.Abs(seen[a].c.g - seen[b].c.g) + Mathf.Abs(seen[a].c.b - seen[b].c.b);
                    Assert.That(d, Is.GreaterThan(0.01f), $"{seen[a].id} and {seen[b].id} look the same\n{report}");
                }
        }

        /// <summary>The thermal camera over the heliport and the town, by day, at night and in sea fog, with the ordinary view beside it; and that going off puts everything back.</summary>
        [UnityTest, Timeout(1200000)]
        public IEnumerator ThermalCamera()
        {
            yield return OpenScene();
            var cam = Camera.main;
            var env = SkyEnvironment.Current;
#if UNITY_EDITOR
            // the game sets these up in its own scene, not in this one; with them come the cars and walkers round the camera
            var host = new GameObject("World actors");
            host.SetActive(false);
            var actors = host.AddComponent<WorldActors>();
            actors.people = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Rotorcraft/Models/people.glb");
            actors.vehicles = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Rotorcraft/Models/vehicles.glb");
            Assert.That(actors.people != null && actors.vehicles != null, "the people and vehicle libraries load");
            host.SetActive(true);
#else
            WorldActors actors = null;
#endif
            var holder = new GameObject("people").transform;
            int seed = 5;
            foreach (var (x, z, outfit, pose) in new[] { (34.0, 28.0, "walker", "wave"), (28.0, 36.0, "casual", "stand"), (44.0, 40.0, "medic", "kneel"), (22.0, 22.0, "casual", "lie"), (50.0, 26.0, "worker", "stand") })
            {
                var p = actors.MakePerson(outfit, pose, seed++, holder);
                p.transform.position = SimToUnity.Position(new Vec3(x, WorldMap.TerrainHeight(x, z), z));
            }
            var car = actors.SpawnVehicle("saloon", holder);
            car.transform.position = SimToUnity.Position(new Vec3(62, WorldMap.TerrainHeight(62, 30), 30));
            HeatSource.Add(car, HeatSource.Engine);
            cam.fieldOfView = 55;
            var views = new (string name, Vector3 from, Vector3 to)[] { ("close", At(58, 58, 7), At(38, 32, 1)), ("heliport", At(90, 90, 26), At(30, 25, 1)), ("town", At(200, 300, 180), At(-700, -900, 0)), ("low", At(-1750, -2050, 40), At(-2100, -2150, 0)) };
            foreach (var wx in new[] { "calm", "haar" })
                foreach (var tod in new[] { TimeOfDay.Day, TimeOfDay.Night })
                {
                    if (wx == "haar" && tod == TimeOfDay.Night) continue;
                    foreach (var (name, from, to) in views)
                    {
                        env.SetThermal(false);
                        env.Apply(tod, Rotorcraft.Sim.Weather.Presets[wx]);
                        var sun = (env.sun.intensity, env.sun.shadows);
                        cam.transform.position = from;
                        cam.transform.LookAt(to);
                        for (int i = 0; i < 8; i++) yield return null;
                        Capture(cam, $"fix-thermal-{wx}-{tod}-{name}-plain", 1000, 562);
                        env.SetThermal(true);
                        Assert.That(env.Thermal && env.Night == 0 && env.timeOfDay == tod, "thermal lighting keeps the time of day for later");
                        for (int i = 0; i < 8; i++) yield return null;
                        Capture(cam, $"fix-thermal-{wx}-{tod}-{name}-flir", 1000, 562);
                        env.SetThermal(false);
                        Assert.That(env.sun.intensity, Is.EqualTo(sun.intensity).Within(1e-3f), "the sun comes back");
                        Assert.That(env.sun.shadows, Is.EqualTo(sun.shadows), "the shadows come back");
                    }
                }
            Assert.Pass("pictures written to Logs/");
        }

        /// <summary>Each twin side on, with a main rotor blade turned to point straight back over the tail: the worst case for a clash.</summary>
        [UnityTest, Timeout(600000)]
        public IEnumerator RotorClearsTheTail()
        {
            yield return OpenScene();
            var cam = Camera.main;
            Object.FindAnyObjectByType<HelicopterController>().gameObject.SetActive(false);
            var pad = S("kin_h1");
            var at = SimToUnity.Position(new Vec3(pad.x, WorldMap.SiteElevation(pad), pad.z));
            const int W = 960, H = 540;
            var sheet = new Texture2D(W * 2, H * 2, TextureFormat.RGB24, false);
            int k = 0;
            foreach (var (id, file) in new[] { ("northstar", "northstar_175"), ("guardian", "guardian_s9") })
            {
                var holder = Dressed(id, file, Aircraft.Get(id), at, 0);
                Transform blades = null;
                foreach (var t in holder.GetComponentsInChildren<Transform>(true)) if (t.name == "blades") blades = t;
                foreach (var spin in new[] { 0f, 45f })
                {
                    // find the turn that puts a blade straight back over the tail, then add the second view's offset
                    if (blades != null) blades.localRotation = Quaternion.Euler(0, spin, 0);
                    cam.fieldOfView = 40;
                    cam.transform.position = at + new Vector3(24, 3.5f, -3);
                    cam.transform.LookAt(at + new Vector3(0, 3, -3.5f));
                    for (int i = 0; i < 4; i++) yield return null;
                    CaptureInto(cam, sheet, k % 2, W, H, k / 2);
                    k++;
                }
                Object.Destroy(holder);
                yield return null;
            }
            sheet.Apply();
            File.WriteAllBytes("Logs/rotor-clearance.png", sheet.EncodeToPNG());
            Assert.Pass("pictures written to Logs/");
        }

        /// <summary>Each kind of load at four stages, on the Kinloch pad: who comes, how they move, and that they finish with the load.</summary>
        [UnityTest, Timeout(1200000)]
        public IEnumerator LoadingScenes()
        {
            yield return OpenScene();
            var cam = Camera.main;
#if UNITY_EDITOR
            var host = new GameObject("World actors");
            host.SetActive(false);
            var actors = host.AddComponent<WorldActors>();
            actors.people = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Rotorcraft/Models/people.glb");
            actors.vehicles = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Rotorcraft/Models/vehicles.glb");
            host.SetActive(true);
#endif
            var heli = Object.FindAnyObjectByType<HelicopterController>();
            var party = heli.GetComponent<LoadingParty>() ?? heli.gameObject.AddComponent<LoadingParty>();
            heli.State.onGround = true;
            heli.State.engine = EngineState.Running;
            var scenes = new (string name, string what, bool pickup, double seconds, string site)[]
            {
                ("passengers-on", "four passengers", true, 30, "kin_h1"),
                ("offshore-off-on", "passengers (net six on)", true, 45, "cormorant_b"),
                ("patient-pickup", "patient and paramedic", true, 40, "kin_h1"),
                ("patient-handover", "patient", false, 20, "kin_h1"),
                ("soldiers", "eight soldiers", true, 30, "kin_h1"),
                ("freight-off", "freight", false, 25, "kin_h1"),
            };
            const int W = 800, H = 450;
            var stages = new[] { 0.06, 0.3, 0.6, 0.97 };
            foreach (var sc in scenes)
            {
                var sheet = new Texture2D(W * 2, H * 2, TextureFormat.RGB24, false);
                var obj = new Rotorcraft.Game.ObjectiveRuntime(new Rotorcraft.Game.ObjectiveDef { kind = Rotorcraft.Game.ObjectiveKind.Load, what = sc.what, pickup = sc.pickup, seconds = sc.seconds, siteId = sc.site, payloadKg = 400 });
                party.Reset();
                var at = heli.transform.position;
                var right = heli.transform.right;
                cam.fieldOfView = 50;
                cam.transform.position = at + right * 15 + heli.transform.forward * 16 + Vector3.up * 7;
                cam.transform.LookAt(at + right * 6 + Vector3.up * 0.8f);
                for (int k = 0; k < stages.Length; k++)
                {
                    // the stages in order, as the loading does, so that everyone is made when they first come
                    for (double p = k == 0 ? 0.01 : stages[k - 1]; p <= stages[k] + 1e-6; p += 0.02)
                    {
                        obj.progress = System.Math.Min(p, stages[k]);
                        party.Show(obj);
                        yield return null;
                    }
                    for (int i = 0; i < 3; i++) yield return null;
                    CaptureInto(cam, sheet, k % 2, W, H, k / 2);
                }
                sheet.Apply();
                File.WriteAllBytes($"Logs/load-{sc.name}.png", sheet.EncodeToPNG());
                Object.Destroy(sheet);
                party.Show(null);    // the load finished
                yield return null;
            }
            party.Reset();
            Assert.Pass("pictures written to Logs/");
        }

        /// <summary>
        /// A stretcher team close up and from above, to a Nightjar (its rear doors) and a Northstar (the cabin door): one patient
        /// on the stretcher, and the team coming in from the side.
        /// </summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator StretcherCloseUps()
        {
            yield return OpenScene();
            var cam = Camera.main;
#if UNITY_EDITOR
            var host = new GameObject("World actors");
            host.SetActive(false);
            var actors = host.AddComponent<WorldActors>();
            actors.people = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Rotorcraft/Models/people.glb");
            actors.vehicles = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Rotorcraft/Models/vehicles.glb");
            host.SetActive(true);
            var heli = Object.FindAnyObjectByType<HelicopterController>();
            var party = heli.GetComponent<LoadingParty>() ?? heli.gameObject.AddComponent<LoadingParty>();
            foreach (var (id, file) in new[] { ("nightjar", "nightjar_n4"), ("northstar", "northstar_175") })
            {
                heli.SetAircraft(Aircraft.Get(id), UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Rotorcraft/Models/Aircraft/{file}.gltf"));
                heli.State.onGround = true;
                heli.State.engine = EngineState.Running;
                yield return null;
                const int W = 800, H = 450;
                var sheet = new Texture2D(W * 2, H * 2, TextureFormat.RGB24, false);
                var obj = new Rotorcraft.Game.ObjectiveRuntime(new Rotorcraft.Game.ObjectiveDef { kind = Rotorcraft.Game.ObjectiveKind.Load, what = "patient and paramedic", pickup = true, seconds = 40, siteId = "kin_h1", payloadKg = 110 });
                party.Reset();
                var t = heli.transform;
                var right = t.right; right.y = 0; right.Normalize();
                var fwd = t.forward; fwd.y = 0; fwd.Normalize();
                var stages = new[] { 0.12, 0.22, 0.32, 0.4 };
                for (int k = 0; k < stages.Length; k++)
                {
                    for (double p = k == 0 ? 0.01 : stages[k - 1]; p <= stages[k] + 1e-6; p += 0.01) { obj.progress = System.Math.Min(p, stages[k]); party.Show(obj); yield return null; }
                    for (int i = 0; i < 3; i++) yield return null;
                    if (k < 2)
                    {
                        // close, from ahead and to the right: the team and what is on the stretcher
                        cam.fieldOfView = 40;
                        cam.transform.position = t.position + right * 11 + fwd * 7 + Vector3.up * 2.6f;
                        cam.transform.LookAt(t.position + right * 5 - fwd * 1 + Vector3.up * 0.9f);
                    }
                    else
                    {
                        // from above: where they walk round the aircraft
                        cam.fieldOfView = 55;
                        cam.transform.position = t.position + right * 6 + Vector3.up * 30;
                        cam.transform.rotation = Quaternion.LookRotation(Vector3.down, fwd);
                    }
                    CaptureInto(cam, sheet, k % 2, W, H, k / 2);
                }
                sheet.Apply();
                File.WriteAllBytes($"Logs/load-stretcher-{id}.png", sheet.EncodeToPNG());
                Object.Destroy(sheet);
                party.Reset();
                yield return null;
            }
            Assert.Pass("pictures written to Logs/");
#else
            yield return null;
#endif
        }

        static void CaptureInto(Camera cam, Texture2D sheet, int column, int w, int h, int row)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            var prev = RenderTexture.active; RenderTexture.active = rt;
            sheet.ReadPixels(new Rect(0, 0, w, h), column * w, (1 - row) * h);
            RenderTexture.active = prev;
            rt.Release();
        }

        static void CaptureInto(Camera cam, Texture2D sheet, int slot, int cell)
        {
            var rt = new RenderTexture(cell, cell, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            var prev = RenderTexture.active; RenderTexture.active = rt;
            sheet.ReadPixels(new Rect(0, 0, cell, cell), slot * cell, 0);
            RenderTexture.active = prev;
            rt.Release();
        }

        /// <summary>The top right corner of the flight HUD, from the game's own style sheet: the nav box, autopilot and crew buttons and map must stack, never overlap.</summary>
        [UnityTest]
        public IEnumerator HudCornerStacks()
        {
#if UNITY_EDITOR
            foreach (var (w, h) in new[] { (1920, 1080), (1280, 720), (1024, 600) })
            {
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);   // rounded corners that clip need a stencil
                rt.Create();
                var ps = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Rotorcraft/UI/Game Panel.asset"));
                ps.scaleMode = PanelScaleMode.ConstantPixelSize; ps.scale = 1; ps.targetTexture = rt;
                ps.clearColor = true; ps.colorClearValue = new Color(0.25f, 0.33f, 0.4f, 1);
                var host = new GameObject("HUD");
                host.SetActive(false);
                var doc = host.AddComponent<UIDocument>();
                doc.panelSettings = ps;
                host.SetActive(true);
                yield return null;
                var root = doc.rootVisualElement;
                root.styleSheets.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Rotorcraft/UI/Rotorcraft.uss"));
                root.style.flexGrow = 1;
                var nav = Ui.E("navbox", Ui.T("Kinloch Royal helipad", "navbox-nm"), Ui.T("084° · 12.4 nm", "navbox-bd mono"), Ui.T("Turn right 12° · ETE 6:10", "navbox-al mono"), Ui.T("Cross at 1500 to 2000 ft", "navbox-al mono"));
                var crew = Ui.E("crewbar");
                foreach (var lab in new[] { "ALT", "HDG", "HVR", "WINCH", "FLIR" }) { var b = new Button { text = lab }; b.AddToClassList("hb"); b.AddToClassList(lab.Length == 3 ? "ap" : "crew"); if (lab == "ALT") b.AddToClassList("on"); crew.Add(b); }
                crew.Add(Ui.T("ALT 1540 · HDG 084", "apstat mono"));
                var map = Ui.E("map-overlay");
                root.Add(Ui.E("hud", Ui.E("hud-right", nav, crew, map)));
                for (int i = 0; i < 5; i++) yield return null;
                Rect n = nav.worldBound, c = crew.worldBound, m = map.worldBound;
                Assert.That(n.yMax <= c.yMin + 0.5f, $"{w}x{h}: the buttons sit below the nav box: nav {n}, buttons {c}");
                Assert.That(c.yMax <= m.yMin + 0.5f, $"{w}x{h}: the map sits below the buttons: buttons {c}, map {m}");
                Assert.That(m.yMax <= h, $"{w}x{h}: the map stays on the screen: {m}");
                Assert.That(n.xMax > w - 12 && c.xMax > w - 12, $"{w}x{h}: both keep to the right edge: nav {n}, buttons {c}");
                Assert.That(n.height > 40 && c.height > 30 && m.height >= 139, $"{w}x{h}: all drawn: nav {n}, buttons {c}, map {m}");
                if (w == 1280)
                {
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
                    RenderTexture.active = prev;
                    Directory.CreateDirectory("Logs");
                    File.WriteAllBytes("Logs/hud-corner.png", tex.EncodeToPNG());
                    Object.Destroy(tex);
                }
                Object.Destroy(host); rt.Release();
                yield return null;
            }
#else
            yield return null;
#endif
        }

        /// <summary>The real attitude gauge drawn at a left bank, level and a right bank, and read back: sky on the side the aircraft banks away from.</summary>
        [UnityTest]
        public IEnumerator AttitudeIndicator()
        {
#if UNITY_EDITOR
            const int Size = 400;
            var rt = new RenderTexture(Size * 3, Size, 0, RenderTextureFormat.ARGB32);
            rt.Create();
            var ps = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Rotorcraft/UI/Game Panel.asset"));
            ps.scaleMode = PanelScaleMode.ConstantPixelSize; ps.scale = 1;
            ps.targetTexture = rt;
            var host = new GameObject("Gauges");
            host.SetActive(false);
            var doc = host.AddComponent<UIDocument>();
            doc.panelSettings = ps;
            host.SetActive(true);
            yield return null;
            var banks = new[] { -20.0, 0, 20.0 };
            var ac = Aircraft.Get("kestrel");
            for (int i = 0; i < banks.Length; i++)
            {
                var s = FlightModel.CreateState(new CreateOptions { headingDeg = 0, fuel = 10 });
                s.phi = banks[i] * SimMath.DEG;
                var gauge = new InstrumentPanel { layout = new[] { (InstrumentPanel.Gauges.Attitude, new Rect(0, 0, 1, 1)) } };
                gauge.style.position = Position.Absolute; gauge.style.left = i * Size; gauge.style.top = 0; gauge.style.width = Size; gauge.style.height = Size; gauge.style.flexGrow = 0;
                doc.rootVisualElement.Add(gauge);
                gauge.Set(s, ac, null, null);
            }
            for (int i = 0; i < 5; i++) yield return null;
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            Directory.CreateDirectory("Logs");
            File.WriteAllBytes("Logs/attitude-left-level-right.png", tex.EncodeToPNG());

            // a little above the middle of the dial, out towards each side (screen y runs down, texture rows run up)
            bool IsSky(int gauge, float side)
            {
                var r = Size * 0.455f;
                var c = tex.GetPixel(Mathf.RoundToInt((gauge + 0.5f) * Size + side * 0.6f * r), Mathf.RoundToInt(Size * 0.5f + 0.1f * r));
                return c.b > c.r;
            }
            Assert.That(IsSky(0, 1) && !IsSky(0, -1), "left bank: the horizon's left side rises, so earth on the left and sky on the right");
            Assert.That(IsSky(1, 1) && IsSky(1, -1), "level: sky on both sides just above the middle");
            Assert.That(IsSky(2, -1) && !IsSky(2, 1), "right bank: the horizon's right side rises, so earth on the right and sky on the left");
            Object.Destroy(tex); Object.Destroy(host); rt.Release();
#else
            yield return null;
#endif
        }

        /// <summary>
        /// The VR seat without a headset: the rig, the UI panel and the laser seen from where the head would be, and the
        /// laser pressing real UI: a button, a check row and a scroll view.
        /// </summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator VrSeat()
        {
            LogAssert.ignoreFailingMessages = true;
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode("Assets/Rotorcraft/Scenes/Kinloch.unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null;
            yield return new WaitForSeconds(1);
            var cam = Camera.main;
            cam.GetComponent<ChaseCamera>().enabled = false;
            foreach (var hud in Object.FindObjectsByType<FlightHud>(FindObjectsSortMode.None)) hud.enabled = false;
            Directory.CreateDirectory("Logs");

            var host = new GameObject("Test UI");
            host.SetActive(false);
            var doc = host.AddComponent<UIDocument>();
#if UNITY_EDITOR
            doc.panelSettings = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Rotorcraft/UI/Game Panel.asset"));
            var sheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Rotorcraft/UI/Rotorcraft.uss");
#else
            StyleSheet sheet = null;
#endif
            host.SetActive(true);
            yield return null;
            var root = doc.rootVisualElement;
            if (sheet != null) root.styleSheets.Add(sheet);
            root.AddToClassList("app-root");
            root.style.flexGrow = 1;
            // the shape of the flight HUD: instruments along the top, a button at the far right, the objective on the left
            int pressed = 0, ticked = 0;
            var tiles = Ui.E("row");
            foreach (var s in new[] { "IAS KT 92", "ALT FT 640", "V/S FPM +120", "HDG 270°", "RRPM % 100", "TQ % 62", "FUEL KG 180" }) { var t = Ui.T(s); t.style.marginRight = 16; tiles.Add(t); }
            tiles.style.position = Position.Absolute; tiles.style.top = 8; tiles.style.left = 10;
            root.Add(tiles);
            var button = Ui.Btn("Start", "small", () => pressed++);
            button.style.position = Position.Absolute; button.style.top = 8; button.style.right = 10;
            root.Add(button);
            var check = Ui.Check("Training aids", false, _ => ticked++);
            check.style.position = Position.Absolute; check.style.top = 120; check.style.left = 10; check.style.width = 280;
            root.Add(check);
            var scroller = new ScrollView();
            scroller.style.position = Position.Absolute; scroller.style.top = 180; scroller.style.left = 10; scroller.style.width = 300; scroller.style.height = 120;
            for (int i = 0; i < 40; i++) scroller.Add(Ui.T($"Line {i}"));
            root.Add(scroller);
            var wide = new Label("Readable from the seat? The quick brown fox jumps over the lazy dog: 0123456789");
            wide.style.position = Position.Absolute; wide.style.left = 340; wide.style.top = 360; wide.style.fontSize = 13; wide.style.color = Color.white;
            root.Add(wide);

            Vr.Load();
            var driver = (Rotorcraft.VR.VrDriver)Vr.Driver;
            driver.Attach(cam, doc, null);
            Assert.That(Vr.On, "attached");
            cam.fieldOfView = 90;   // with the next size, one eye of a Quest 3S: its view and its pixels
            var eye = At(-40, 45, 1.7);
            var ahead = Quaternion.LookRotation(At(55, -70, 1.7) - eye, Vector3.up);
            driver.Place(eye, ahead, true);
            for (int i = 0; i < 10; i++) yield return null;
            Capture(cam, "vr-seat", 1832, 1920);

            // the game's menu turns the world off with a culling mask of nothing: the panel must still draw
            var worldMask = cam.cullingMask;
            var clearFlags = cam.clearFlags; var background = cam.backgroundColor;
            cam.cullingMask = 0; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color32(0x11, 0x18, 0x1d, 255);
            for (int i = 0; i < 3; i++) yield return null;
            Capture(cam, "vr-seat-menu", 1832, 1920);
            var menuShot = new Texture2D(2, 2);
            menuShot.LoadImage(File.ReadAllBytes("Logs/vr-seat-menu.png"));
            int lit = 0;
            foreach (var px in menuShot.GetPixels32()) if (px.r > 60 || px.g > 60 || px.b > 60) lit++;
            Object.Destroy(menuShot);
            Assert.That(lit > 1000, $"at the menu the panel shows in the headset: {lit} lit pixels");
            cam.cullingMask = worldMask; cam.clearFlags = clearFlags; cam.backgroundColor = background;

            var ui = root.panel;
            PanelPointer.Move(ui, button.worldBound.center);
            PanelPointer.Down(ui, button.worldBound.center);
            yield return null;
            PanelPointer.Up(ui, button.worldBound.center);
            yield return null;
            Assert.AreEqual(1, pressed, "the laser pressed the button");

            PanelPointer.Move(ui, check.worldBound.center);
            PanelPointer.Down(ui, check.worldBound.center);
            yield return null;
            PanelPointer.Up(ui, check.worldBound.center);
            yield return null;
            Assert.AreEqual(1, ticked, "the laser ticked the check row");

            PanelPointer.Move(ui, scroller.worldBound.center);
            PanelPointer.Scroll(ui, scroller.worldBound.center, 5);
            yield return null;
            Assert.That(scroller.scrollOffset.y > 0, $"the stick scrolled the list: {scroller.scrollOffset.y}");

            // the seat tilted with the aircraft, and a head that has moved and turned then recentred
            driver.Place(eye + Vector3.up * 40, Quaternion.Euler(-8, ahead.eulerAngles.y, 12), false);
            for (int i = 0; i < 6; i++) yield return null;
            Capture(cam, "vr-seat-banked", 1832, 1920);

            // the headset reports its height only after the first recentring (or goes on later): at the menu the seat
            // catches up by itself, so the panel is in front of the eyes and not down by the floor
            var tracking = cam.GetComponent("TrackedPoseDriver") as Behaviour;
            tracking.enabled = false;
            driver.Place(eye, ahead, true);
            cam.transform.localPosition = new Vector3(0.05f, 1.6f, 0);
            cam.transform.localRotation = Quaternion.identity;
            driver.Place(eye, ahead, true);
            Assert.That(cam.transform.position.y - eye.y > 1, $"the head starts well above the seat: {cam.transform.position.y - eye.y} m");
            var settle = Time.unscaledTime + 1;
            while (Time.unscaledTime < settle) { driver.Place(eye, ahead, true); yield return null; }
            Assert.That((cam.transform.position - eye).magnitude < 0.01f, $"the seat caught up with the head: {(cam.transform.position - eye).magnitude} m off");
            var panelShown = GameObject.Find("VR panel").transform.position;
            Assert.That(Mathf.Abs(panelShown.y - cam.transform.position.y + 0.1f) < 0.01f, $"the panel is at eye height: {panelShown.y - cam.transform.position.y} m from the eyes");
            tracking.enabled = true;

            cam.transform.localPosition = new Vector3(0.2f, 1.1f, -0.1f);
            cam.transform.localRotation = Quaternion.Euler(0, 40, 0);
            driver.Recentre();
            driver.Place(eye, ahead, true);
            Assert.That((cam.transform.position - eye).magnitude < 0.001f, $"recentred: the head is at the seat, {(cam.transform.position - eye).magnitude} m off");
            Assert.That(Vector3.Angle(cam.transform.forward, ahead * Vector3.forward) < 0.1f, "recentred: the head looks straight ahead");
            driver.End();
            Assert.That(!Vr.On && cam.transform.parent == null, "left VR: the camera is back on its own");
            Assert.Pass("pictures written to Logs/");
        }

        static NavTarget Nav(Site s, ObjectiveKind kind, double radius) =>
            new NavTarget { name = s.name, x = s.x, z = s.z, radiusM = radius, elevationFt = WorldMap.SiteElevation(s) * 3.28084, kind = kind };

        static Site S(string id) => WorldMap.SiteById(id);

        /// <summary>A view from a little off to one side of a site, looking down at it, measured from its own surface so it works on a roof or deck.</summary>
        static (Vector3 from, Vector3 to) Near(Site s, double off, double above)
        {
            var y = WorldMap.SiteElevation(s);
            return (SimToUnity.Position(new Vec3(s.x + off, y + above, s.z + off)), SimToUnity.Position(new Vec3(s.x, y, s.z)));
        }

        /// <summary>The paint and props at each kind of site, the road past the hangar, and every kind of mission marker.</summary>
        [UnityTest, Timeout(900000)]   // the terrain takes minutes to compute when it is not in the cache
        public IEnumerator Markings()
        {
            LogAssert.ignoreFailingMessages = true;   // this is for looking at; the preview scene logs complaints about old references as it opens
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode("Assets/Rotorcraft/Scenes/Kinloch.unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null;
            yield return new WaitForSeconds(1);
            var cam = Camera.main;
            cam.GetComponent<ChaseCamera>().enabled = false;
            foreach (var hud in Object.FindObjectsByType<FlightHud>(FindObjectsSortMode.None)) hud.enabled = false;
            Directory.CreateDirectory("Logs");
            var markers = new MissionMarkers();
            Site glen = S("glen_clearing"), farm = S("moss_farm");
            var area = Rotorcraft.Game.Missions.ById("sar_t1_search").searchAreas[0];
            var corners = Search.AreaCorners(area);
            var views = new (string name, NavTarget nav, (double x, double z)[] area, (Vector3 from, Vector3 to) view)[]
            {
                ("mark-heliport", null, null, (At(175, 135, 125), At(30, -40, 0))),
                ("mark-road-hangar", null, null, (At(180, -180, 42), At(92, -50, 2))),
                ("mark-road-above", null, null, (At(70, -110, 340), At(70, -109, 0))),
                ("mark-h1-land", Nav(S("kin_h1"), ObjectiveKind.Land, 20), null, Near(S("kin_h1"), 30, 16)),
                ("mark-spot-a-hover", Nav(S("kin_spot_a"), ObjectiveKind.Hover, 15), null, Near(S("kin_spot_a"), 26, 15)),
                ("mark-field-land", Nav(farm, ObjectiveKind.Land, 30), null, Near(farm, 62, 32)),
                ("mark-waypoint-far", Nav(glen, ObjectiveKind.Waypoint, 250), null, (At(glen.x + 1400, glen.z, 190), At(glen.x, glen.z, 90))),
                ("mark-waypoint-near", Nav(glen, ObjectiveKind.Waypoint, 250), null, (At(glen.x + 420, glen.z + 420, 400), At(glen.x, glen.z, 0))),
                ("mark-glen-hover", Nav(glen, ObjectiveKind.Hover, 5), null, Near(glen, 15, 13)),
                ("mark-hospital-land", Nav(S("balnagown"), ObjectiveKind.Land, 15), null, Near(S("balnagown"), 36, 24)),
                ("mark-roof-land", Nav(S("kinloch_royal"), ObjectiveKind.Land, 13), null, Near(S("kinloch_royal"), 32, 22)),
                ("mark-quay-land", Nav(S("portcraig_quay"), ObjectiveKind.Land, 9), null, Near(S("portcraig_quay"), 24, 17)),
                ("mark-ski-pad", null, null, Near(S("ardoch_ski"), 26, 18)),
                ("mark-deck-land", Nav(S("cormorant_a"), ObjectiveKind.Land, 14), null, Near(S("cormorant_a"), 30, 24)),
                ("mark-search-area", null, corners, (At(area.cx + System.Math.Max(area.w, area.h) * 0.8, area.cz, 320), At(area.cx, area.cz, 0))),
                ("mark-heliport-again", null, null, (At(175, 135, 125), At(30, -40, 0))),
                ("mark-junction", null, null, (At(150, 150, 70), At(120, 40, 0))),
            };
            foreach (var v in views)
            {
                markers.Show(v.nav, v.area);
                cam.fieldOfView = 55;
                cam.transform.position = v.view.from;
                cam.transform.LookAt(v.view.to);
                for (int i = 0; i < 10; i++) yield return null;   // let the terrain stream its detail for the new view
                var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                var req = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes($"Logs/{v.name}.png", tex.EncodeToPNG());
                Object.Destroy(tex); rt.Release();
            }
            Assert.Pass("pictures written to Logs/");
        }
    }
}
