// What the player is doing, on their Discord profile (Rich Presence): "Playing Rotorcraft", the flight or screen, what is
// happening now, the aircraft's picture and the time since it started, with buttons for friends to play in the browser or
// see the website. Windows only (DiscordIpc); off in screenshot tours, benchmarks and trailer runs, and the player can turn it
// off in Settings. The wording comes from PresenceFacts so it can be tested without flying.
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Rotorcraft.Game;
using UnityEngine;

namespace Rotorcraft.Presentation.UI
{
    /// <summary>What is going on, read from the game once a second.</summary>
    public sealed class PresenceFacts
    {
        public string version = "";
        /// <summary>The menu screen (GameApp route id), and the mission, lesson or exam it is about.</summary>
        public string screen = "menu", screenTitle;
        public bool passed;
        /// <summary>The career's rank (null with no career) and flying hours.</summary>
        public string rank; public double hours;
        public string company;
        public bool flying, loading, paused, crashed, vr;
        public bool walkRound, cockpitChecks;
        public string flightTitle, aircraftId, aircraftName;
        /// <summary>The current objective or freeplay job, already worded (null: none).</summary>
        public string task;
        public bool freeplay, freeRoam;
        public int jobs; public double earned;
        public long gameStartMs, flightStartMs;
    }

    /// <summary>One moment of play as Discord shows it.</summary>
    public sealed class PresenceView
    {
        public string details, state, largeImage, largeText, smallImage, smallText;
        public long startMs;
    }

    public static class DiscordStatus
    {
        public const string Site = "https://rotorcraft.netlify.app";
        /// <summary>The pictures are on the website (Website/img/discord), so no art has to be uploaded to Discord.</summary>
        public const string Pictures = Site + "/img/discord/";
        public static readonly string[] Aircraft = { "kestrel", "northstar", "guardian", "nightjar", "peregrine" };
        static readonly CultureInfo En = CultureInfo.InvariantCulture;

        public static PresenceView Describe(PresenceFacts f)
        {
            var career = f.rank != null ? $"{f.rank} · {Hours(f.hours)}" : null;
            if (!f.flying)
                return new PresenceView
                {
                    details = MenuLine(f),
                    state = f.screen == "fpreport" && f.jobs > 0 ? Shift(f) : career,
                    largeImage = "logo", largeText = $"Rotorcraft {f.version}".Trim(),
                    startMs = f.gameStartMs,
                };
            var pictured = Array.IndexOf(Aircraft, f.aircraftId) >= 0;
            return new PresenceView
            {
                details = f.flightTitle,
                state = f.loading ? "Getting ready to fly" : f.crashed ? "Crashed" : f.paused ? "Paused"
                    : f.walkRound ? "Pre-flight walk-round" : f.cockpitChecks ? "Cockpit checks"
                    : f.task ?? (!f.freeplay ? null : f.freeRoam ? "Flying around Kinloch" : f.jobs > 0 ? $"Standing by, {Jobs(f.jobs)} done" : "Waiting for a callout"),
                largeImage = pictured ? f.aircraftId : "logo",
                largeText = (f.aircraftName ?? "Rotorcraft") + (f.vr ? " · in VR" : ""),
                smallImage = pictured ? "logo" : null,
                smallText = f.freeplay && f.jobs > 0 ? Shift(f) : career ?? "Rotorcraft",
                startMs = f.flightStartMs,
            };
        }

        static string MenuLine(PresenceFacts f)
        {
            var t = f.screenTitle;
            switch (f.screen)
            {
                case "menu": case "account": return "In the main menu";
                case "new": return "Starting a new career";
                case "hq": return "At headquarters";
                case "school": return "In ground school";
                case "module": return t != null ? $"Ground school: {t}" : "In ground school";
                case "exam": return t != null ? $"Exam: {t}" : "Sitting an exam";
                case "missions": case "employers": return "Choosing the next flight";
                case "brief": return t != null ? $"Briefing: {t}" : "In a briefing";
                case "debrief": return t == null ? "In a debrief" : f.passed ? $"Passed: {t}" : $"Debrief: {t}";
                case "quals": return "Checking qualifications";
                case "hangar": return "In the hangar";
                case "records": return "Reading the logbook";
                case "settings": return "Changing settings";
                case "tutorial": return "Reading the flying guide";
                case "pilots": return "Looking at the leaderboards";
                case "freeplay": return "Planning a freeplay shift";
                case "fpreport": return "Finished a freeplay shift";
                case "company": return f.company != null ? $"Running {f.company}" : "Starting a company";
                case "cpreport": return f.company != null ? $"Finished a {f.company} shift" : "Finished a company shift";
                default: return "In the menus";
            }
        }

        static string Hours(double h) => Math.Abs(h - 1) < 0.05 ? "1 hour" : $"{h.ToString(h < 100 ? "0.#" : "#,0", En)} hours";
        static string Jobs(int n) => n == 1 ? "1 job" : $"{n} jobs";
        static string Shift(PresenceFacts f) => f.earned > 0 ? $"{Jobs(f.jobs)} · £{f.earned.ToString("#,0", En)}" : Jobs(f.jobs);

        /// <summary>An objective's checklist label as a status: without its limits in brackets ("Land in the clearing (slope ≤ 6°)").</summary>
        public static string Task(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return null;
            var s = Regex.Replace(label, @"\s*\([^()]*\)\s*$", "").Trim();
            return s.Length >= 2 ? s : label.Trim();
        }

        /// <summary>Discord's activity object. Text must be 2 to 128 characters; anything shorter is left out.</summary>
        public static JObject ToActivity(PresenceView v)
        {
            var a = new JObject();
            void Text(JObject o, string key, string s)
            {
                s = s?.Trim();
                if (string.IsNullOrEmpty(s) || s.Length < 2) return;
                o[key] = s.Length <= 128 ? s : s.Substring(0, 127) + "…";
            }
            Text(a, "details", v.details);
            Text(a, "state", v.state);
            if (v.startMs > 0) a["timestamps"] = new JObject { ["start"] = v.startMs };
            var art = new JObject();
            if (v.largeImage != null) { art["large_image"] = Pictures + v.largeImage + ".png"; Text(art, "large_text", v.largeText); }
            if (v.smallImage != null) { art["small_image"] = Pictures + v.smallImage + ".png"; Text(art, "small_text", v.smallText); }
            if (art.Count > 0) a["assets"] = art;
            a["buttons"] = new JArray(
                new JObject { ["label"] = "Play in your browser", ["url"] = Site + "/play" },
                new JObject { ["label"] = "Rotorcraft website", ["url"] = Site });
            return a;
        }
    }

    public sealed class DiscordPresence : MonoBehaviour
    {
        /// <summary>The game's application in the Discord Developer Portal: its name there ("Rotorcraft") is what friends see
        /// after "Playing". Public, not a secret. Empty turns the whole feature off.</summary>
        public const string ApplicationId = "1570195708546519163";

        /// <summary>Windows, with an application to show the activity under.</summary>
        public static bool Available =>
            (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor) && ApplicationId.Length > 0;

        public static DiscordPresence Instance { get; private set; }

        long gameStartMs, flightStartMs;
        FlightSetup lastSetup;
        float nextAt;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        DiscordIpc ipc;
#endif

        static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        void Awake() { Instance = this; gameStartMs = Now(); }

        void Update()
        {
            if (Time.unscaledTime < nextAt) return;
            nextAt = Time.unscaledTime + 1;
            var app = GameApp.Instance;
            if (app == null || app.Settings == null) return;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (!app.Settings.discord || !Available) { Stop(); return; }
            if (ipc == null) ipc = new DiscordIpc(ApplicationId);
            ipc.Set(DiscordStatus.ToActivity(DiscordStatus.Describe(Read(app))));
#endif
        }

        /// <summary>One line for Settings: whether Discord is there and taking the activity.</summary>
        public string StatusLine()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (ipc == null) return "Off.";
            switch (ipc.Status)
            {
                case DiscordIpc.Link.Connected: return ipc.Problem != null ? $"Connected, but Discord would not show it: {ipc.Problem}" : "Connected. Discord shows it to others if its Activity Privacy settings allow.";
                case DiscordIpc.Link.Refused: return $"Discord turned the game down: {ipc.Problem}";
                default: return "Discord isn't running on this computer. The game picks it up when it starts.";
            }
#else
            return "";
#endif
        }

        PresenceFacts Read(GameApp app)
        {
            var f = new PresenceFacts { version = Application.version, gameStartMs = gameStartMs, vr = Vr.On, company = app.Company?.name };
            var c = app.Career;
            if (c != null) { f.rank = CareerManager.RankOf(c); f.hours = CareerManager.Hours(c); }
            var r = app.Current;
            f.screen = r.id;
            switch (r.id)
            {
                case "brief": f.screenTitle = Missions.ById(r.missionId)?.title; break;
                case "module": f.screenTitle = Theory.ModuleById(r.moduleId)?.title; break;
                case "exam": f.screenTitle = Theory.ExamById(r.examId)?.title; break;
                case "debrief": f.screenTitle = r.result?.missionTitle; f.passed = r.result?.pass ?? false; break;
                case "fpreport": f.jobs = r.summary?.jobs?.Count ?? 0; f.earned = r.summary?.earned ?? 0; break;
            }
            var fl = app.flight;
            var s = fl != null && fl.Active ? fl.Setup : null;
            if (s == null) { lastSetup = null; return f; }
            if (s != lastSetup) { lastSetup = s; flightStartMs = Now(); }   // a restart is a new flight
            f.flying = true; f.flightStartMs = flightStartMs;
            f.freeplay = s.freeplay != null;
            f.freeRoam = s.freeplay != null && s.freeplay.pace == FreeplayPace.Off;
            f.flightTitle = s.freeplay != null && s.freeplay.companyUid == null ? s.loadingTitle : s.mission?.title;   // "Free flight", "Freeplay shift", "<Company> shift"
            f.aircraftId = s.ac?.id; f.aircraftName = s.ac?.name;
            f.loading = fl.Loading || fl.Runtime == null;
            if (f.loading) return f;
            f.paused = fl.Paused;
            f.crashed = fl.heli != null && fl.heli.State != null && fl.heli.State.crashed;
            f.walkRound = fl.Pre != null && fl.Pre.stage == PreflightStage.Outside;
            f.cockpitChecks = fl.Pre != null && fl.Pre.stage == PreflightStage.Cockpit;
            var rt = fl.Runtime;
            if (fl.Director != null)
            {
                f.jobs = fl.Director.history.Count; f.earned = fl.Director.earned;
                f.task = fl.Director.active?.callout?.title;
            }
            else f.task = rt.status == MissionStatus.Complete ? "Flight complete" : rt.status == MissionStatus.Failed ? "Flight over" : DiscordStatus.Task(rt.Active?.def.label);
            return f;
        }

        void Stop()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            ipc?.Dispose();   // closing the connection clears the activity
            ipc = null;
#endif
        }

        void OnDestroy() { Stop(); if (Instance == this) Instance = null; }
        void OnApplicationQuit() => Stop();
    }
}
