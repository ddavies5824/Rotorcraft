// Pictures of the Kinloch scene as it looks when playing (terrain grass, trees and everything the game draws
// only at run time). Explicit: run on purpose with
//   Unity.exe -batchmode -projectPath <project> -runTests -testPlatform PlayMode -testFilter KinlochShots
using System.Collections;
using System.IO;
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
    }
}
