// Pictures of the Kinloch scene as it looks when playing (terrain grass, trees and everything the game draws
// only at run time). Explicit: run on purpose with
//   Unity.exe -batchmode -projectPath <project> -runTests -testPlatform PlayMode -testFilter KinlochShots
using System.Collections;
using System.IO;
using NUnit.Framework;
using Rotorcraft.Game;
using Rotorcraft.Presentation;
using Rotorcraft.Sim;
using Rotorcraft.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

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
