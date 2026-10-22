// A screenshot tour for checking a build: started with "-shots <folder>" on the command line, it walks through
// the menus with a fresh test career (saved in that folder, not the player's), flies a freeplay shift and a
// career exercise, captures each view as a .png and quits.
using System.Collections;
using System.IO;
using Rotorcraft.Game;
using Rotorcraft.Sim;
using UnityEngine;

namespace Rotorcraft.Presentation.UI
{
    public sealed class ShotTour : MonoBehaviour
    {
        public string folder;
        GameApp app;
        int n;

        public static string ArgFolder()
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-shots") return a[i + 1];
            return null;
        }

        IEnumerator Start()
        {
            app = GameApp.Instance;
            Directory.CreateDirectory(folder);
            yield return Wait(2.5f);
            yield return Shot("menu");
            app.Go("new"); yield return Shot("new-career");
            app.CommitCareer(CareerManager.CreateCareer("Alex Ross", null, CareerStart.Student));
            foreach (var r in new[] { "hq", "school", "missions", "employers", "quals", "hangar", "records", "settings" }) { app.Go(r); yield return Shot(r); }
            app.Go(Route.Module(Theory.MODULES[0].id)); yield return Shot("ground-school-module");
            app.Go(Route.Brief("ex01_controls")); yield return Shot("briefing");
            app.Go("freeplay"); yield return Shot("freeplay-setup");
            app.Go("company"); yield return Shot("company");
            app.Go("pilots"); yield return Shot("pilots");
            app.Go("account"); yield return Shot("account");

            // a freeplay shift in the Northstar: loading screen, then the HUD and its panels
            var cfg = new FreeplayConfig { aircraftId = "northstar", kit = Loadout.DefaultKit(Aircraft.Get("northstar")), fuelKg = 900, pace = FreeplayPace.Busy };
            app.FlyFreeplay(cfg);
            yield return Wait(1.2f); yield return Shot("loading");
            var f = app.flight;
            while (!f.Active || f.Runtime == null) yield return null;
            yield return Wait(5);
            var terrains = Terrain.activeTerrains;
            Debug.Log($"Tour: {terrains.Length} terrains; first: {(terrains.Length > 0 ? $"{terrains[0].materialTemplate?.shader?.name} supported={terrains[0].materialTemplate?.shader?.isSupported} instanced={terrains[0].drawInstanced} enabled={terrains[0].enabled} pos={terrains[0].transform.position}" : "-")}");
            yield return Shot("flight-chase");
            f.heli.SetCamera(CameraMode.Cockpit); yield return Wait(1); yield return Shot("flight-cockpit");
            f.ShowForTour(panel: true); yield return Wait(1); yield return Shot("flight-panel");
            f.ShowForTour(map: true); yield return Wait(1.5f); yield return Shot("flight-map");
            f.ShowForTour(tablet: true); yield return Wait(1); yield return Shot("flight-tablet");
            f.ShowForTour(); f.heli.SetCamera(CameraMode.Orbit); f.cam.orbitYaw = 120; yield return Wait(1); yield return Shot("flight-orbit");
            f.TogglePause(true); yield return Wait(0.5f); yield return Shot("flight-paused");
            f.Finish(FlightEnd.Complete);
            yield return Wait(1.5f); yield return Shot("shift-report");

            // a career exercise with the walk-round
            app.FlyMission("ex01_controls", 60, TimeOfDay.Day, "calm");
            while (!f.Active || f.Runtime == null) yield return null;
            yield return Wait(4);
            yield return Shot("preflight-walkround");
            Debug.Log($"Shot tour done: {n} pictures in {folder}");
            Application.Quit();
        }

        static IEnumerator Wait(float s) { var t = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < t) yield return null; }

        IEnumerator Shot(string name)
        {
            yield return Wait(0.8f);
            yield return new WaitForEndOfFrame();
            var path = Path.Combine(folder, $"{++n:00}-{name}.png");
            ScreenCapture.CaptureScreenshot(path);
            yield return null; yield return null;
        }
    }
}
