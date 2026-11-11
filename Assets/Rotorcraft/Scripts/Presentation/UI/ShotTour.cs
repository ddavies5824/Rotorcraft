// A screenshot tour for checking a build: started with "-shots <folder>" on the command line, it walks through
// the menus with a fresh test career (saved in that folder, not the player's), flies a freeplay shift and a
// career exercise, captures each view as a .png and quits. In the batch-mode editor (the bench: no window, nothing on
// the desktop) the world and the menus are drawn into textures and laid together instead of capturing the screen.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rotorcraft.Game;
using Rotorcraft.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rotorcraft.Presentation.UI
{
    public sealed class ShotTour : MonoBehaviour
    {
        public string folder;
        GameApp app;
        int n;
        /// <summary>Set when the tour has finished (the editor ignores Application.Quit).</summary>
        public static bool Done;
        const int ShotW = 1600, ShotH = 900;
        RenderTexture ui;

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
            if (Application.isBatchMode)
            {
                // the menus and HUD go to a texture for the whole tour, read at each picture; the panel only draws into one it
                // was made with, so the test that runs the tour gives the game's panel its texture before the scene opens
                ui = app.document.panelSettings.targetTexture;
                if (ui == null) Debug.LogWarning("Shot tour: the game panel has no target texture, so the pictures will have no menus");
            }
            yield return Wait(2.5f);
            var a = System.Environment.GetCommandLineArgs();
            if (System.Array.IndexOf(a, "-markings-only") >= 0)
            {
                app.CommitCareer(CareerManager.CreateCareer("Alex Ross", null, CareerStart.Student));
                yield return Markings();
                Debug.Log($"Shot tour done: {n} pictures in {folder}");
                Application.Quit();
                yield break;
            }
            yield return Page("menu");
            app.Go("new"); yield return Page("new-career");
            app.CommitCareer(CareerManager.CreateCareer("Alex Ross", null, CareerStart.Student));
            foreach (var r in new[] { "hq", "school", "missions", "employers", "quals", "hangar", "records", "settings", "tutorial" }) { app.Go(r); yield return Page(r); }
            app.Go(Route.Module(Theory.MODULES[0].id)); yield return Page("ground-school-module");
            app.Go(Route.Exam(Theory.EXAMS[0].id)); yield return Page("exam");
            app.Go(Route.Brief("ex01_controls")); yield return Page("briefing");
            app.Go("freeplay"); yield return Page("freeplay-setup");
            app.Go("company"); yield return Page("company");
            app.Go("pilots"); yield return Page("pilots");
            app.Go("account"); yield return Page("account");
            // the rename control, pressed as a player would
            app.ScreenState["rename"] = "  Ailsa  Munro! "; app.Refresh(); yield return null; yield return null;
            var renameBtn = app.document.rootVisualElement.Query<Button>().ToList().First(b => b.Query<Label>().ToList().Any(l => l.text == "CHANGE NAME") || b.text.ToUpperInvariant() == "CHANGE NAME");
            using (var e = NavigationSubmitEvent.GetPooled()) { e.target = renameBtn; renameBtn.SendEvent(e); }
            yield return Wait(0.5f);
            Debug.Log($"Tour: renamed to \"{app.Career.pilotName}\", saved copy: \"{app.Saves.LoadCareer().career?.pilotName}\"");
            yield return Page("account-renamed");

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
            f.ShowForTour(); f.heli.SetCamera(CameraMode.Cockpit); yield return Wait(1); yield return Shot("flight-cockpit");
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
            f.SkipForTour(); f.heli.SetCamera(CameraMode.Orbit); f.cam.orbitDist = 6f; f.cam.orbitPitch = 5; f.cam.orbitYaw = 30; yield return Wait(1.5f); yield return Shot("close-front-kestrel");
            f.cam.orbitYaw = 145; f.cam.orbitPitch = 10; yield return Wait(1); yield return Shot("close-rear-kestrel");
            f.cam.orbitDist = 12; f.heli.SetCamera(CameraMode.Cockpit); yield return Wait(1); yield return Shot("cockpit-kestrel");
            f.Finish(FlightEnd.Aborted); yield return Wait(1.5f); yield return Page("debrief");

            // every type: the cockpit with its live instruments, and from outside
            foreach (var id in new[] { "northstar", "guardian", "nightjar", "peregrine" })
            {
                var ac = Aircraft.Get(id);
                app.FlyFreeplay(new FreeplayConfig { aircraftId = id, kit = Loadout.DefaultKit(ac), fuelKg = ac.fuelCapacity * 0.6, pace = FreeplayPace.Off });
                while (!f.Active || f.Runtime == null) yield return null;
                yield return Wait(3);
                f.ShowForTour(); f.heli.SetCamera(CameraMode.Cockpit); yield return Wait(1); yield return Shot("cockpit-" + id);
                f.heli.SetCamera(CameraMode.Orbit); f.cam.orbitYaw = 35; f.cam.orbitPitch = 8; yield return Wait(1); yield return Shot("outside-" + id);
                f.cam.orbitDist = 5.2f; f.cam.orbitPitch = 4; f.cam.orbitYaw = 30; yield return Wait(1); yield return Shot("close-front-" + id);
                f.cam.orbitYaw = 145; f.cam.orbitPitch = 10; yield return Wait(1); yield return Shot("close-rear-" + id);
                f.cam.orbitDist = 12;
                f.Finish(FlightEnd.Complete); yield return Wait(1);
            }
            // the Peregrine in its other paint schemes and fits, and in the air with the wheels up
            foreach (var (skin, kit) in new[] { ("army", new List<KitId> { KitId.Seats, KitId.Searchlight }), ("navy", new List<KitId> { KitId.Floats, KitId.Hoist }), ("rescue", new List<KitId> { KitId.Medical, KitId.Hoist, KitId.Searchlight }) })
            {
                app.FlyFreeplay(new FreeplayConfig { aircraftId = "peregrine", kit = kit, skin = skin, fuelKg = 600, pace = FreeplayPace.Off });
                while (!f.Active || f.Runtime == null) yield return null;
                yield return Wait(3);
                f.ShowForTour(); f.heli.SetCamera(CameraMode.Orbit); f.cam.orbitDist = 14; f.cam.orbitYaw = 35; f.cam.orbitPitch = 8; yield return Wait(1); yield return Shot("peregrine-" + skin);
                f.cam.orbitDist = 12; f.Finish(FlightEnd.Complete); yield return Wait(1);
            }
            yield return Fly("peregrine", "calm", TimeOfDay.Day);
            var p0 = f.heli.State.pos.Clone();
            f.heli.State.sys.gearDown = false; f.heli.State.gearPos = 0;
            f.heli.SetCamera(CameraMode.Orbit); f.cam.orbitDist = 16; f.cam.orbitYaw = 110; f.cam.orbitPitch = 3;
            yield return Hold(new Vec3(p0.x, p0.y + 40, p0.z), 3, true); yield return Shot("peregrine-gear-up");
            f.cam.orbitDist = 12; f.Finish(FlightEnd.Complete); yield return Wait(1);
            yield return Effects();
            yield return Life();
            yield return Markings();
            Debug.Log($"Shot tour done: {n} pictures in {folder}");
            Done = true;
            Application.Quit();
        }

        FlightSession f;

        /// <summary>What is painted and placed on the ground at each kind of site, the road past the hangar, and the markers a mission puts in the world.</summary>
        IEnumerator Markings()
        {
            yield return Fly("kestrel", "calm", TimeOfDay.Day);
            var h1y = Rotorcraft.World.WorldMap.SiteElevation(Rotorcraft.World.WorldMap.SiteById("kin_h1"));
            f.heli.SetCamera(CameraMode.Orbit);
            IEnumerator Look(string name, double x, double y, double z, float dist, float pitch, float yaw)
            {
                f.cam.orbitDist = dist; f.cam.orbitPitch = pitch; f.cam.orbitYaw = yaw;
                yield return Hold(new Vec3(x, y, z), 1.5f);
                yield return Shot(name);
            }
            yield return Look("mark-heliport", 40, h1y + 60, -30, 230, 48, 200);
            yield return Look("mark-road-hangar", 105, h1y + 20, -60, 75, 20, 75);
            yield return Look("mark-road-above", 60, h1y + 40, -120, 150, 70, 0);
            foreach (var (id, dist, pitch) in new[] { ("kin_h1", 42f, 42f), ("kin_h2", 50f, 40f), ("kin_spot_a", 32f, 45f), ("moss_farm", 80f, 35f), ("glen_clearing", 50f, 40f), ("balnagown", 60f, 40f), ("portcraig_quay", 40f, 40f), ("ardoch_ski", 40f, 40f) })
            {
                var s = Rotorcraft.World.WorldMap.SiteById(id);
                yield return Look("mark-" + id, s.x, (s.deckHeight ?? Rotorcraft.World.WorldMap.SiteElevation(s)) + 12, s.z, dist, pitch, 30);
            }
            // a ring laid on a roof and on a helideck, where it lies on the deck instead of following the ground below
            var test = new MissionMarkers();
            foreach (var (id, dist, pitch) in new[] { ("kinloch_royal", 50f, 42f), ("cormorant_a", 60f, 38f) })
            {
                var s = Rotorcraft.World.WorldMap.SiteById(id);
                test.Show(new NavTarget { x = s.x, z = s.z, radiusM = s.radius * 1.3, elevationFt = s.deckHeight.Value * 3.28084, kind = ObjectiveKind.Land }, null);
                yield return Look("mark-" + id, s.x, s.deckHeight.Value + 12, s.z, dist, pitch, 30);
            }
            test.Clear();
            f.cam.orbitDist = 12; f.Finish(FlightEnd.Complete); yield return Wait(1);

            // a real exercise: the beacon on the way to a waypoint, the ring round a hover, and then round the landing
            app.FlyMission("cpl2_confined_area", 120, TimeOfDay.Day, "calm");
            while (!f.Active || f.Runtime == null) yield return null;
            yield return Wait(3);
            f.SkipForTour(); f.ShowForTour();
            var glen = Rotorcraft.World.WorldMap.SiteById("glen_clearing");
            var ge = Rotorcraft.World.WorldMap.SiteElevation(glen);
            var p0 = f.heli.State.pos.Clone();
            yield return Hold(new Vec3(p0.x, p0.y + 30, p0.z), 3, true);
            Debug.Log($"Tour: exercise on \"{f.Runtime.Active?.def.label}\", target {f.Runtime.NavTargetNow()?.name}");
            f.heli.SetCamera(CameraMode.Chase);
            f.heli.Input.trimX = 0.1;   // shows the trim tag beside the objective
            yield return Hold(new Vec3(glen.x + 1000, ge + 170, glen.z), 2, true, -System.Math.PI / 2); yield return Shot("marker-waypoint");
            f.heli.Input.trimX = 0;
            f.heli.SetCamera(CameraMode.Orbit); f.cam.orbitDist = 420; f.cam.orbitPitch = 32; f.cam.orbitYaw = 90;
            yield return Hold(new Vec3(glen.x + 320, ge + 170, glen.z), 2, true, -System.Math.PI / 2); yield return Shot("marker-waypoint-ring");
            yield return Hold(new Vec3(glen.x, ge + 150, glen.z), 2, true);
            Debug.Log($"Tour: exercise on \"{f.Runtime.Active?.def.label}\", target {f.Runtime.NavTargetNow()?.name}");
            f.cam.orbitDist = 26; f.cam.orbitPitch = 38; f.cam.orbitYaw = 40;
            yield return Hold(new Vec3(glen.x, ge + 3, glen.z), 2, true); yield return Shot("marker-hover");
            yield return Hold(new Vec3(glen.x, ge + 3, glen.z), 10, true);
            Debug.Log($"Tour: exercise on \"{f.Runtime.Active?.def.label}\", target {f.Runtime.NavTargetNow()?.name}");
            yield return Shot("marker-land");
            f.heli.ControlFilter = null; f.cam.orbitDist = 12; f.Finish(FlightEnd.Aborted); yield return Wait(1.5f);

            // a search: a post at each corner of the area
            app.FlyMission("sar_t1_search", 400, TimeOfDay.Day, "calm");
            while (!f.Active || f.Runtime == null) yield return null;
            yield return Wait(3);
            f.SkipForTour(); f.ShowForTour();
            p0 = f.heli.State.pos.Clone();
            yield return Hold(new Vec3(p0.x, p0.y + 30, p0.z), 3, true);
            var area = f.Runtime.NavTargetNow();
            Debug.Log($"Tour: exercise on \"{f.Runtime.Active?.def.label}\", target {area?.name}");
            if (area != null)
            {
                f.heli.SetCamera(CameraMode.Chase);
                yield return Hold(new Vec3(area.x + area.radiusM * 1.4, area.elevationFt / 3.28084 + 220, area.z), 2, true, -System.Math.PI / 2); yield return Shot("marker-search");
            }
            f.heli.ControlFilter = null; f.cam.orbitDist = 12; f.Finish(FlightEnd.Aborted); yield return Wait(1.5f);
        }

        IEnumerator Fly(string id, string weather, TimeOfDay tod)
        {
            f = app.flight;
            var ac = Aircraft.Get(id);
            app.FlyFreeplay(new FreeplayConfig { aircraftId = id, kit = Loadout.DefaultKit(ac), fuelKg = ac.fuelCapacity * 0.6, pace = FreeplayPace.Off, weatherId = weather, tod = tod });
            while (!f.Active || f.Runtime == null) yield return null;
            yield return Wait(3);
            f.ShowForTour();
        }

        /// <summary>Keep the aircraft at p (sim axes), still, for a while; `running` puts it in a hover with the rotor turning.</summary>
        IEnumerator Hold(Vec3 p, float seconds, bool running = false, double? psi = null)
        {
            var h = f.heli;
            if (running) h.ControlFilter = (c, dt) => c.WithCollective(0.6);
            var end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                var s = h.State;
                s.pos = p.Clone(); s.vel = new Vec3(0, 0, 0); s.p = s.q = s.r = 0; s.theta = s.phi = 0;
                if (psi.HasValue) s.psi = psi.Value;
                if (running) { s.engine = EngineState.Running; s.engineN = 1; s.rotorE = 1; s.govTarget = 1; s.crashed = false; }
                yield return null;
            }
        }

        /// <summary>The moving world: the crew walking out after a shutdown, traffic, people in town, a fishing boat, traffic at night.</summary>
        IEnumerator Life()
        {
            yield return Fly("northstar", "calm", TimeOfDay.Day);
            var start = f.heli.State.pos.Clone();
            // a short hop so the crew know the aircraft has been somewhere, then down and shut down
            yield return Hold(new Vec3(start.x, start.y + 0.6, start.z), 1.2f);
            yield return Hold(new Vec3(start.x, start.y + 0.03, start.z), 0.3f);
            f.heli.SetCamera(CameraMode.Orbit); f.cam.orbitDist = 26; f.cam.orbitPitch = 22; f.cam.orbitYaw = 70;
            yield return Wait(6); yield return Shot("life-crew-door");
            yield return Wait(7); yield return Shot("life-crew-out");
            f.cam.orbitDist = 12; f.Finish(FlightEnd.Complete); yield return Wait(1);

            yield return Fly("kestrel", "calm", TimeOfDay.Day);
            var road = Rotorcraft.World.Places.ROADS.First(r => r.name == "Kinloch road");
            var rp = road.pts[road.pts.Length / 2];
            f.heli.SetCamera(CameraMode.Orbit); f.cam.orbitDist = 60; f.cam.orbitPitch = 28;
            yield return Hold(new Vec3(rp.x, Rotorcraft.World.WorldMap.TerrainHeight(rp.x, rp.z) + 30, rp.z), 4); yield return Shot("life-traffic");
            // close up: the nearest car, walker and boat, seen past the aircraft (heading west, the camera behind)
            var life = FindAnyObjectByType<AmbientLife>();
            foreach (var (kind, h, d) in new[] { ("car", 8.0, 22f), ("walker", 6.0, 16f), ("boat", 14.0, 30f) })
            {
                var town = Rotorcraft.World.Places.SETTLEMENTS[0];
                var near = kind == "boat" ? new Vector2((float)Rotorcraft.World.Terrain.CoastX(-6000) + 2500, -6000) : new Vector2((float)town.x, (float)town.z);
                if (kind == "boat") yield return Hold(new Vec3(near.x, 200, near.y), 1, false, -System.Math.PI / 2);
                var at = life != null ? life.Nearest(kind, near) : null;
                if (at == null) continue;
                f.heli.SetCamera(CameraMode.Orbit); f.cam.orbitDist = d; f.cam.orbitPitch = 14; f.cam.orbitYaw = 0;
                var g = kind == "boat" ? 0 : Rotorcraft.World.WorldMap.TerrainHeight(at.Value.x + 18, at.Value.y);
                yield return Hold(new Vec3(at.Value.x + 18, g + h, at.Value.y), 0.3f, false, -System.Math.PI / 2);
                yield return Shot("life-" + kind);
            }
            f.cam.orbitDist = 12; f.Finish(FlightEnd.Complete); yield return Wait(1);

            yield return Fly("kestrel", "calm", TimeOfDay.Night);
            f.heli.SetCamera(CameraMode.Orbit); f.cam.orbitDist = 80; f.cam.orbitPitch = 25;
            yield return Hold(new Vec3(rp.x, Rotorcraft.World.WorldMap.TerrainHeight(rp.x, rp.z) + 40, rp.z), 4); yield return Shot("life-traffic-night");
            f.cam.orbitDist = 12; f.Finish(FlightEnd.Complete); yield return Wait(1);
        }

        /// <summary>Effects and weather: rain under a broken layer, inside and above the cloud, rotor spray and dust, engine smoke, a clear night and a platform flare.</summary>
        IEnumerator Effects()
        {
            yield return Fly("northstar", "showers", TimeOfDay.Day);
            var start = f.heli.State.pos.Clone();
            f.heli.SetCamera(CameraMode.Chase); yield return Wait(1.5f); yield return Shot("fx-rain");
            f.heli.SetCamera(CameraMode.Cockpit); yield return Wait(1); yield return Shot("fx-rain-cockpit");
            var baseM = 1400 / 3.28084;
            f.heli.SetCamera(CameraMode.Chase);
            yield return Hold(new Vec3(start.x, baseM - 60, start.z), 2); yield return Shot("fx-cloud-base");
            yield return Hold(new Vec3(start.x, baseM + 150, start.z), 2); yield return Shot("fx-in-cloud");
            f.heli.SetCamera(CameraMode.Orbit); f.cam.orbitDist = 40; f.cam.orbitPitch = 18;
            yield return Hold(new Vec3(start.x, baseM + Rotorcraft.Sim.Weather.CLOUD_DEPTH_M + 150, start.z), 2.5f); yield return Shot("fx-above-cloud");
            f.cam.orbitDist = 26; f.cam.orbitPitch = 12;
            yield return Hold(new Vec3(2500, 3.5, 0), 4, true); yield return Shot("fx-spray");
            var g = Rotorcraft.World.WorldMap.GroundAt(start.x + 90, start.z).h;
            yield return Hold(new Vec3(start.x + 90, g + 3.5, start.z), 4, true); yield return Shot("fx-dust");
            f.cam.orbitDist = 12; f.Finish(FlightEnd.Complete); yield return Wait(1);

            yield return Fly("kestrel", "breezy_w", TimeOfDay.Day);
            f.heli.SetCamera(CameraMode.Orbit); f.cam.orbitDist = 8; f.cam.orbitYaw = 115; f.cam.orbitPitch = 6;
            FlightModel.CommandEngine(f.heli.State, EngineCommand.Start);
            yield return Wait(2.2f); yield return Shot("fx-engine-start");
            yield return Wait(2.5f); yield return Shot("fx-engine-start-2");
            f.cam.orbitDist = 12; f.Finish(FlightEnd.Aborted); yield return Wait(1);

            yield return Fly("northstar", "north_sea", TimeOfDay.Day);
            f.heli.SetCamera(CameraMode.Chase);
            yield return Hold(new Vec3(20700, 70, -6026), 4, false, System.Math.PI / 2); yield return Shot("fx-platform-flare");
            yield return Hold(new Vec3(4000, 120, 0), 2, false, System.Math.PI / 2); yield return Shot("fx-sea");
            f.cam.orbitDist = 12; f.Finish(FlightEnd.Complete); yield return Wait(1);

            yield return Fly("northstar", "calm", TimeOfDay.Night);
            f.heli.SetCamera(CameraMode.Orbit); f.cam.orbitPitch = -10; f.cam.orbitDist = 14;
            yield return Wait(1.5f); yield return Shot("fx-night-sky");
            f.heli.SetCamera(CameraMode.Chase);
            yield return Hold(new Vec3(20700, 70, -6026), 4, false, System.Math.PI / 2); yield return Shot("fx-platform-flare-night");
            f.cam.orbitDist = 12; f.Finish(FlightEnd.Complete); yield return Wait(1);
        }

        /// <summary>A screen, then further down it a screenful at a time when it scrolls (up to four more).</summary>
        IEnumerator Page(string name)
        {
            yield return Shot(name);
            yield return null;
            ScrollView best = null; float extra = 0;
            if (app.document != null)
                foreach (var sv in app.document.rootVisualElement.Query<ScrollView>().ToList())
                {
                    var e = sv.contentContainer.layout.height - sv.contentViewport.layout.height;
                    if (sv.resolvedStyle.display != DisplayStyle.None && e > extra + 1) { extra = e; best = sv; }
                }
            if (best == null || extra < 40) yield break;
            var step = best.contentViewport.layout.height * 0.9f;
            for (int k = 1; k <= 4 && step > 1; k++)
            {
                var y = Mathf.Min(extra, step * k);
                best.scrollOffset = new Vector2(0, y);
                yield return Shot(name + "-" + (k + 1));
                if (y >= extra) break;
            }
            best.scrollOffset = Vector2.zero;
        }

        static IEnumerator Wait(float s) { var t = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < t) yield return null; }

        IEnumerator Shot(string name)
        {
            yield return Wait(0.8f);
            var path = Path.Combine(folder, $"{++n:00}-{name}.png");
            if (Application.isBatchMode) { yield return Offscreen(path); yield break; }
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(path);
            yield return null; yield return null;
        }

        /// <summary>No screen to capture: the camera into one texture, the menus and HUD into another (with a depth and stencil
        /// buffer, which rounded, clipped panels need), then the menus laid over the world.</summary>
        IEnumerator Offscreen(string path)
        {
            const int W = ShotW, H = ShotH;
            for (int i = 0; i < 2; i++) yield return null;
            var front = ui != null ? Read(ui) : new Color32[W * H];
            Color32[] back = null;
            var cam = Camera.main;
            if (cam != null && cam.isActiveAndEnabled)
            {
                var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
                if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, req)) UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req);
                else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
                back = Read(rt);
                rt.Release();
            }
            var px = new Color32[W * H];
            for (int i = 0; i < px.Length; i++)
            {
                var u = front[i]; var a = u.a / 255f;
                var b = back != null ? back[i] : new Color32(20, 26, 31, 255);
                // the panel is drawn with premultiplied alpha onto a clear texture
                px[i] = new Color32((byte)Mathf.Min(255, u.r + b.r * (1 - a)), (byte)Mathf.Min(255, u.g + b.g * (1 - a)), (byte)Mathf.Min(255, u.b + b.b * (1 - a)), 255);
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.SetPixels32(px); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
        }

        static Color32[] Read(RenderTexture rt)
        {
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var t = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); t.Apply();
            RenderTexture.active = prev;
            var px = t.GetPixels32();
            Destroy(t);
            return px;
        }
    }
}
