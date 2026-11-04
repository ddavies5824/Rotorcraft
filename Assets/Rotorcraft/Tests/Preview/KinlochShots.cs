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

            var driver = (Rotorcraft.VR.VrDriver)Vr.Driver;
            driver.Attach(cam, doc, null);
            Assert.That(Vr.On, "attached");
            cam.fieldOfView = 90;   // with the next size, one eye of a Quest 3S: its view and its pixels
            var eye = At(-40, 45, 1.7);
            var ahead = Quaternion.LookRotation(At(55, -70, 1.7) - eye, Vector3.up);
            driver.Place(eye, ahead, true);
            for (int i = 0; i < 10; i++) yield return null;
            Capture(cam, "vr-seat", 1832, 1920);

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
