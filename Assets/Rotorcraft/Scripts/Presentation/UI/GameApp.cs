// The game's front end (port of src/ui/App.tsx): holds the career, settings, freeplay record and company,
// routes between the screens, and hands over to the flight. Everything a screen shows comes from the career
// manager; screens only navigate or call its functions, so progress cannot be faked from the interface.
using System;
using System.Collections.Generic;
using System.IO;
using Rotorcraft.Game;
using Rotorcraft.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rotorcraft.Presentation.UI
{
    public sealed class Route
    {
        public string id;
        public string missionId, moduleId, examId;
        public FlightResult result;
        public CareerUpdate update;
        public FreeplaySummary summary;
        public FreeplayConfig cfg;
        public ShiftReport report;
        public static Route To(string id) => new Route { id = id };
        public static Route Brief(string missionId) => new Route { id = "brief", missionId = missionId };
        public static Route Module(string id) => new Route { id = "module", moduleId = id };
        public static Route Exam(string id) => new Route { id = "exam", examId = id };
    }

    public sealed class GameApp : MonoBehaviour
    {
        public static GameApp Instance { get; private set; }

        [Tooltip("The document the menus and the flight HUD are drawn in.")]
        public UIDocument document;
        public StyleSheet styleSheet;
        public FlightSession flight;

        public SaveManager Saves { get; private set; }
        public GameSettings Settings { get; private set; }
        public Career Career { get; private set; }
        public FreeplayRecord Freeplay { get; private set; }
        public Company Company { get; private set; }
        public string SaveProblem { get; private set; }
        public Route Current { get; private set; } = Route.To("menu");

        VisualElement root, screenLayer, overlayLayer;
        /// <summary>The screen being shown keeps its own little state (selected tab, answers) between redraws.</summary>
        public readonly Dictionary<string, object> ScreenState = new Dictionary<string, object>();
        readonly List<Action> deferred = new List<Action>();

        /// <summary>A screenshot tour, benchmark or trailer run: its own saves in its own folder, and never the player's cloud account.</summary>
        public bool Isolated { get; private set; }

        void Awake()
        {
            Instance = this;
            Vr.Load();
            AmbientRecordings.EnsureLoading();   // Windows: the world-sound recordings, read while the menus are up
            var shots = ShotTour.ArgFolder();
#if UNITY_WEBGL
            string perf = null;
#else
            var perf = PerfRun.ArgFolder();
#endif
            var saveRoot = shots ?? perf;
#if RC_TRAILER
            var trailerArgs = Environment.GetCommandLineArgs();
            var trailerAt = Array.IndexOf(trailerArgs, "-trailer");
            if (trailerAt >= 0 && trailerAt + 1 < trailerArgs.Length) saveRoot = trailerArgs[trailerAt + 1];
#endif
            Saves = new SaveManager(saveRoot != null ? Path.Combine(saveRoot, "saves") : null);
            Isolated = saveRoot != null;
            if (shots != null) gameObject.AddComponent<ShotTour>().folder = shots;
#if !UNITY_WEBGL
            if (perf != null) gameObject.AddComponent<PerfRun>().folder = perf;
#endif
            // web test hook: ?autofly starts a freeplay flight straight away (checks the world builds in a browser)
            if (Application.platform == RuntimePlatform.WebGLPlayer && Application.absoluteURL.Contains("autofly"))
                Later(() => FlyFreeplay(new FreeplayConfig { aircraftId = "kestrel", kit = Loadout.DefaultKit(Aircraft.Get("kestrel")), fuelKg = 60, pace = FreeplayPace.Normal }));
            Settings = Saves.LoadSettings();
            Freeplay = Saves.LoadFreeplay();
            Company = Saves.LoadCompany();
            var (c, err) = Saves.LoadCareer();
            Career = c;
            if (err == LoadError.Corrupt) SaveProblem = "Your saved career could not be read, so a backup copy was kept and a fresh start is available.";
            else if (err == LoadError.Unavailable) SaveProblem = "Saving is not available on this computer, so progress will not be kept after you quit.";
            ApplyDisplaySettings();
            if (!Isolated && !Application.isBatchMode && DiscordPresence.Available) gameObject.AddComponent<DiscordPresence>();
            Vr.Changed += () => { if (flight == null || !flight.Active) Render(); };
        }

        void OnEnable()
        {
            if (document == null) document = GetComponent<UIDocument>();
            root = document.rootVisualElement;
            root.Clear();
            if (styleSheet != null && !root.styleSheets.Contains(styleSheet)) root.styleSheets.Add(styleSheet);
            root.AddToClassList("app-root");
            root.style.flexGrow = 1;
            screenLayer = Ui.E("fill");
            overlayLayer = Ui.E("fill");
            overlayLayer.pickingMode = PickingMode.Ignore;
            root.Add(screenLayer);
            root.Add(overlayLayer);
            Render();
        }

        public VisualElement Overlay => overlayLayer;
        public VisualElement Root => root;

        // ---------------------------------------------------------------------------------- navigation
        public void Go(Route r)
        {
            if (r.id != Current.id || r.missionId != Current.missionId || r.moduleId != Current.moduleId || r.examId != Current.examId) ScreenState.Clear();
            Current = r;
            Render();
        }
        public void Go(string id) => Go(Route.To(id));

        /// <summary>Redraw the current screen (after state changed).</summary>
        public void Refresh() => Render();

        /// <summary>Run after the current UI event (so a click handler can rebuild the tree it lives in).</summary>
        public void Later(Action a) { lock (deferred) deferred.Add(a); }

        void Update()
        {
            List<Action> run = null;
            lock (deferred) { if (deferred.Count > 0) { run = new List<Action>(deferred); deferred.Clear(); } }
            if (run != null) foreach (var a in run) a();
            pad.Tick(PadScopes(), PadBack);
        }

        // ------------------------------------------------------------------------------- gamepad menus
        readonly MenuNavigation pad = new MenuNavigation();
        readonly List<VisualElement> padScopes = new List<VisualElement>();

        /// <summary>What the gamepad works now: a dialog over the screen, the screen, or in flight the card that has the controller.</summary>
        List<VisualElement> PadScopes()
        {
            padScopes.Clear();
            if (screenLayer == null) return padScopes;
            if (flight != null && flight.Active) { flight.PadScopes(padScopes); return padScopes; }
            var dialog = false;
            foreach (var c in overlayLayer.Children()) if (c.resolvedStyle.display != DisplayStyle.None && c.Query<Button>().First() != null) { dialog = true; break; }
            padScopes.Add(dialog ? overlayLayer : screenLayer);
            return padScopes;
        }

        /// <summary>B with no Back button on screen: home (the headquarters, or the main menu).</summary>
        bool PadBack()
        {
            if (flight != null && flight.Active) return flight.PadBack();
            var home = Career != null ? "hq" : "menu";
            if (Current.id == home || Current.id == "menu") return false;
            Go(home);
            return true;
        }

        void Render()
        {
            if (screenLayer == null) return;
            screenLayer.Clear();
            var inFlight = flight != null && flight.Active;
            screenLayer.style.display = inFlight ? DisplayStyle.None : DisplayStyle.Flex;
            if (inFlight) return;
            VisualElement view;
            try { view = Screens.Build(this, Current); }
            catch (Exception e)
            {
                Debug.LogException(e);
                view = Ui.E("app", Ui.Panel(Ui.H2("Something went wrong drawing this screen", "err"), Ui.P(e.Message), Ui.Btn("Main menu", "primary", () => Go("menu"))));
            }
            view.style.flexGrow = 1;
            screenLayer.Add(view);
            if (flight != null) flight.SetMenuBackdrop(true);
        }

        // ------------------------------------------------------------------------------------- commits
        public void CommitCareer(Career c)
        {
            Career = c;
            if (c != null && !Saves.SaveCareer(c) && Saves.Available) SaveProblem = "Saving failed: the disk may be full.";
            Cloud.Instance?.Changed();
        }

        public void DeleteCareer() { Saves.DeleteCareer(); Career = null; }

        public void CommitSettings(GameSettings s)
        {
            Settings = s;
            Saves.SaveSettings(s);
            ApplyDisplaySettings();
            if (flight != null) flight.SettingsChanged();
        }

        public void CommitFreeplay(FreeplayRecord r) { Freeplay = r; Saves.SaveFreeplay(r); Cloud.Instance?.Changed(); }
        public void CommitCompany(Company c) { Company = c; Saves.SaveCompany(c); Cloud.Instance?.Changed(); }

        /// <summary>Cloud sync brought newer data down.</summary>
        public void ApplyCloud(Career c, FreeplayRecord fp, Company co)
        {
            if (c != null) { Career = c; Saves.SaveCareer(c); SaveProblem = null; }
            if (fp != null) { Freeplay = fp; Saves.SaveFreeplay(fp); }
            if (co != null) { Company = co; Saves.SaveCompany(co); }
            if (flight == null || !flight.Active) Render();
        }

        void ApplyDisplaySettings()
        {
            if (document != null && document.panelSettings != null) document.panelSettings.scale = Settings.textScale;
            QualitySettings.SetQualityLevel(Settings.graphics == GraphicsQuality.Low ? 0 : Settings.graphics == GraphicsQuality.Balanced ? 1 : Math.Max(0, QualitySettings.names.Length - 1), true);
            VrGraphics.Refresh();
            AudioListener.volume = Settings.sound ? 1 : 0;
            RotorAudio.Master = Settings.volume;
#if !UNITY_EDITOR
            var mode = Settings.fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (Screen.fullScreenMode != mode) Screen.fullScreenMode = mode;
#endif
        }

        // ---------------------------------------------------------------------------------- flights
        public void StartCareer(string name, CareerStart start)
        {
            var c = CareerManager.CreateCareer(name, null, start);
            CommitCareer(c);
            Go(start == CareerStart.Student ? Route.Brief("ex01_controls") : Route.To("hq"));
        }

        /// <summary>Fly a career mission (from the briefing).</summary>
        public void FlyMission(string missionId, double fuelKg, TimeOfDay tod, string weatherId)
        {
            var m = Missions.ById(missionId);
            var b = Aircraft.WithSkin(Aircraft.Get(m.aircraftId), m.skin);
            if (m.kit != null) b = Loadout.Fit(b, m.kit).ac;
            var pf = CareerManager.PowerFactor(Career, b.id);
            var ac = b;
            if (pf < 1) { ac = b.Clone(); ac.ratedPowerKW = b.ratedPowerKW * pf; }
            flight.Begin(new FlightSetup
            {
                mission = m, ac = ac, fuelKg = fuelKg, tod = tod, weatherId = weatherId, preflight = true, checklistComplete = false,
                instrumentRated = CareerManager.HasQual(Career, "ir_h"), loadingTitle = m.title, loadingSub = $"{ac.name} · {m.employer}",
                onFinish = r =>
                {
                    var u = CareerManager.ApplyFlightResult(Career, r);
                    if (u.career != Career) CommitCareer(u.career);
                    Go(new Route { id = "debrief", result = r, update = u });
                },
                onRestart = () => FlyMission(missionId, fuelKg, tod, weatherId),
            });
            Render();
        }

        public void FlyFreeplay(FreeplayConfig cfg)
        {
            CommitFreeplay(new FreeplayRecord { shifts = Freeplay.shifts, jobs = Freeplay.jobs, earned = Freeplay.earned, seconds = Freeplay.seconds, bestShift = Freeplay.bestShift, last = cfg });
            var fit = Loadout.Fit(Aircraft.WithSkin(Aircraft.Get(cfg.aircraftId), cfg.skin), cfg.kit);
            var m = FreeplayData.FreeplayMission(cfg, fit.ac, fit.kitKg);
            flight.Begin(new FlightSetup
            {
                mission = m, ac = fit.ac, fuelKg = Math.Min(cfg.fuelKg, fit.ac.fuelCapacity), tod = cfg.tod, weatherId = m.weatherId, checklistComplete = true, instrumentRated = true,
                freeplay = cfg, loadingTitle = cfg.pace == FreeplayPace.Off ? "Free flight" : "Freeplay shift", loadingSub = $"{fit.ac.name} · {Rotorcraft.Sim.Weather.TimeLabels[cfg.tod]}",
                onFreeplayEnd = (sum, r) =>
                {
                    CommitFreeplay(SaveData.RecordShift(Freeplay, sum, cfg));
                    Go(new Route { id = "fpreport", summary = sum, cfg = cfg });
                },
                onFinish = _ => Go("freeplay"),
                onRestart = () => FlyFreeplay(cfg),
            });
            Render();
        }

        public void FlyCompanyShift(FreeplayConfig cfg)
        {
            var owned = Company?.fleet.Find(a => a.uid == cfg.companyUid);
            if (owned == null) { Go("company"); return; }
            var fit = Loadout.Fit(Aircraft.WithSkin(Aircraft.Get(owned.aircraftId), owned.skin), owned.kit);
            var ac = fit.ac;
            // a worn aircraft gives a little less power
            if (owned.condition < 0.6) { ac = ac.Clone(); ac.ratedPowerKW *= 0.97 + 0.05 * owned.condition; }
            var m = FreeplayData.FreeplayMission(cfg, ac, fit.kitKg);
            m.employer = Company.name; m.title = $"{Company.name} shift"; m.shortTitle = owned.reg;
            flight.Begin(new FlightSetup
            {
                mission = m, ac = ac, fuelKg = Math.Min(cfg.fuelKg, ac.fuelCapacity), tod = cfg.tod, weatherId = m.weatherId, checklistComplete = true, instrumentRated = true,
                freeplay = cfg, loadingTitle = Company.name, loadingSub = $"{owned.reg} · {ac.name}",
                onFreeplayEnd = (sum, r) =>
                {
                    var (co, rep) = Companies.SettleShift(Company, owned.uid, sum, r.outcome, r.damage, r.flightSeconds);
                    CommitCompany(co);
                    Go(new Route { id = "cpreport", report = rep });
                },
                onFinish = _ => Go("company"),
                onRestart = () => FlyCompanyShift(cfg),
            });
            Render();
        }

        /// <summary>The flight has ended (its callbacks have navigated): show the menus again.</summary>
        public void FlightEnded() => Render();

        void OnApplicationQuit()
        {
            if (Career != null) Saves.SaveCareer(Career);
            Saves.SaveSettings(Settings);
        }
    }
}
