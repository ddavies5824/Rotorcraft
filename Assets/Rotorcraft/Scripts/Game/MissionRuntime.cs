// MissionRuntime: a state machine that runs a MissionDef against the live simulation (port of
// src/game/missionEngine.ts). Objectives activate strictly in order; completion is decided from simulated
// aircraft state and simulator events. The few crew actions that are buttons (radio calls, decisions, the
// winch) are only accepted when the aircraft state allows them, so results cannot be faked from the interface.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Rotorcraft.Sim;
using Rotorcraft.World;
using static Rotorcraft.Sim.SimMath;

namespace Rotorcraft.Game
{
    public enum MissionStatus { Running, Complete, Failed }
    public enum MessageKind { Info, Caution, Warning, Good }

    public sealed class RadioMessage { public int id; public double t; public string from, text; public MessageKind kind; }

    /// <summary>Hover steadiness: running sums over the time actually spent in the hover (position, height, heading).</summary>
    public sealed class HoverSums { public double n, x, z, xx, zz, a, aa, h, hh, @out; }

    public sealed class ObjectiveRuntime
    {
        public ObjectiveDef def;
        public ObjectiveStatus status = ObjectiveStatus.Pending;
        public double progress;      // 0..1
        public string detail = "";
        public int? score;
        public double t0;            // activation time
        // hover / hold tracking
        public double timer; public int resets; public double errPos, errAlt, errHdg, samples; public double? refHdg; public bool inside;
        public HoverSums hv;
        // waypoint tracking
        public double minDist = double.PositiveInfinity;
        // autorotation tracking
        public double rpmIn, rpmTotal, minRpm = 999;
        public double settled;
        /// <summary>Set when the item was added by a decision (return/divert), for the debrief.</summary>
        public bool added;
        public ObjectiveRuntime(ObjectiveDef d) { def = d; }
    }

    public sealed class NavTarget { public string name; public double x, z, radiusM; public double? altMinFt, altMaxFt; public double elevationFt; public ObjectiveKind kind; }

    public enum CrewActionId { Transmit, Winch, Flir }
    /// <summary>A crew action the HUD can offer as a button. enabled is advisory; the runtime re-checks on use.</summary>
    public sealed class CrewAction { public CrewActionId id; public string label, hint; public bool enabled; public bool? on; }

    public sealed class MissionOptions
    {
        public uint? seed;
        public string weatherId;
        /// <summary>Pilot holds an instrument rating (cloud is not penalised in IFR-equipped aircraft).</summary>
        public bool instrumentRated;
    }

    public sealed class MissionStats
    {
        public int hardLandings, offTarget, winchAborts;
        public double unstableSec, lowLevelSec, imcSec;
        public Dictionary<string, double> exceedances;
        public List<string> proceduralErrors;
        public List<DecisionRecord> decisions;
    }

    public sealed class MissionRuntime
    {
        /// <summary>Autorotation: seconds after the throttle is closed before low rotor RPM can fail the exercise,
        /// and seconds of entry that are not counted in the RPM score.</summary>
        public const double AUTOROTATION_GRACE_S = 10, AUTOROTATION_ENTRY_S = 3;
        /// <summary>Seconds a hover may stray outside its limits before the timer resets (it pauses meanwhile).</summary>
        public const double HOVER_GRACE_S = 2;
        /// <summary>Seconds over which a weather change in a mission's timeline blends in.</summary>
        public const double WEATHER_BLEND_S = 90;

        static readonly Regex SPEAKERS = new Regex(@"^(Kinloch Tower|Examiner|Instructor|Tower|Coastguard|Aberdeen Radar|Crew|Winch op|Ops|Co-pilot|Cormorant A|Cormorant B|Kittiwake|Balnagown|Police|Ambulance control):\s*(.*)$", RegexOptions.Singleline);

        public readonly MissionDef def;
        public readonly AircraftConfig ac;
        public readonly TimeOfDay tod;
        public bool checklistComplete;
        public readonly List<ObjectiveRuntime> objectives;
        public int index;
        public MissionStatus status = MissionStatus.Running;
        public readonly List<RadioMessage> messages = new List<RadioMessage>();
        /// <summary>Autopilot upper modes (twins). The flight view blends these into the controls.</summary>
        public readonly APState ap = Autopilot.NewAP();
        public readonly HoistState hoist = Hoist.NewHoist();
        public readonly List<PlacedTarget> targets;
        /// <summary>Freeplay: search areas added with callouts.</summary>
        readonly List<SearchAreaDef> extraAreas = new List<SearchAreaDef>();
        public readonly bool freeplay;
        public readonly uint seed;
        public SearchState search;
        /// <summary>Targets the crew have found (shown on the map).</summary>
        public readonly HashSet<string> found = new HashSet<string>();
        public bool flir;
        /// <summary>Cargo and passengers carried (kg), excluding anything on the hoist.</summary>
        public double cargoKg;
        /// <summary>Increments whenever the weather changes, so the renderer knows to update.</summary>
        public int weatherVersion;
        public double? completeAt;
        /// <summary>Where the current leg started (for the HSI course and cross-track error).</summary>
        public (double x, double z)? legFrom;

        readonly string announcer;
        readonly bool ifr;
        int msgId;
        readonly Dictionary<string, double> lastCaution = new Dictionary<string, double>();
        readonly double startFuel;
        readonly long startedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        double airborne, block, distance;
        (double x, double z)? lastPos;
        double maxAltFt;
        readonly List<TouchdownRecord> landings = new List<TouchdownRecord>();
        int offTarget, hardLandings;
        readonly Dictionary<string, double> exceedances = new Dictionary<string, double>();
        readonly List<string> warnings = new List<string>();
        readonly List<string> proceduralErrors = new List<string>();
        double unstableSec, outOfRangeSec, lowLevelSec, imcSec;
        string crashReason;
        bool inAutorotation;
        double coolDown;            // seconds at ground idle before shutdown
        double engineRunT;
        readonly HashSet<string> flags = new HashSet<string>();
        readonly List<DecisionRecord> decisions = new List<DecisionRecord>();
        readonly List<(string label, double minLate)> late = new List<(string, double)>();
        int deadlineItems;
        readonly List<string> rescueLog = new List<string>();
        int winchAborts;
        double? winchStartT, casualtyAboardT, deliveredT, enteredAreaT;
        readonly List<int> searchScores = new List<int>(), rescueScores = new List<int>(), instrumentScores = new List<int>();
        bool hintGiven;
        // weather
        readonly WeatherPreset baseWx;
        WeatherPreset wxNow;
        string wxKey = "";
        int wxAnnounced = -1;
        /// <summary>Timeline entries brought forward (seconds) so a decision never refers to weather that has not arrived.</summary>
        readonly Dictionary<int, double> wxStart = new Dictionary<int, double>();

        static string Announcer(MissionDef m)
        {
            if (m.category == MissionCategory.Freeplay) return "Ops";
            if (m.category == MissionCategory.Test) return "Examiner";
            if (m.category == MissionCategory.Sar || m.category == MissionCategory.Police || m.category == MissionCategory.Military) return "Crew";
            if (m.category == MissionCategory.Offshore) return "Co-pilot";
            return m.dual ? "Instructor" : "Kinloch Tower";
        }

        static string F0(double v) => Js.Fixed(v, 0);
        static string F1(double v) => Js.Fixed(v, 1);
        static string F2(double v) => Js.Fixed(v, 2);

        public MissionRuntime(MissionDef def, AircraftConfig ac, double startFuel, bool checklistComplete, TimeOfDay tod = TimeOfDay.Day, MissionOptions opts = null)
        {
            opts = opts ?? new MissionOptions();
            this.def = def; this.ac = ac; this.startFuel = startFuel; this.checklistComplete = checklistComplete; this.tod = tod;
            seed = opts.seed ?? (uint)new Random().Next();
            announcer = Announcer(def);
            freeplay = def.category == MissionCategory.Freeplay;
            baseWx = (opts.weatherId != null && Weather.Presets.TryGetValue(opts.weatherId, out var w1)) ? w1
                : Weather.Presets.TryGetValue(def.weatherId ?? "", out var w2) ? w2 : Weather.Presets["calm"];
            wxNow = baseWx;
            cargoKg = def.payloadKg;
            targets = Search.PlaceTargets(def.targets ?? new TargetDef[0], def.searchAreas ?? new SearchAreaDef[0], seed);
            // a target at a reported position (no search area) is known from the start
            foreach (var d in def.targets ?? new TargetDef[0]) if (d.areaId == null && d.x != null && d.z != null) found.Add(d.id);
            ifr = opts.instrumentRated && ac.equipment.autopilot;
            objectives = def.objectives.Select(d => new ObjectiveRuntime(d)).ToList();
            if (!checklistComplete) proceduralErrors.Add("Pre-flight checklist not completed");
            var opening = def.category == MissionCategory.Freeplay ? $"Kinloch Ops: {def.summary}"
                : def.category == MissionCategory.Sar || def.category == MissionCategory.Police || def.category == MissionCategory.Military ? $"{def.callsign ?? "Rescue 9-1-2"}, tasking: {def.summary}"
                : def.dual || def.category == MissionCategory.Offshore ? $"{def.shortTitle}. {def.summary}" : $"Kinloch Tower: {def.shortTitle}, start-up approved when ready.";
            Say(0, announcer, opening, MessageKind.Info);
            Activate(0, 0, null);
        }

        public ObjectiveRuntime Active => index < objectives.Count ? objectives[index] : null;

        /// <summary>Simulated instrument flight: the outside view is hooded while the active objective says so.</summary>
        public bool Hood => Active != null && Active.def.hood && status == MissionStatus.Running;

        public void Say(double t, string from, string text, MessageKind kind = MessageKind.Info)
        {
            var m = SPEAKERS.Match(text);
            if (m.Success) { from = m.Groups[1].Value; text = m.Groups[2].Value; }
            messages.Add(new RadioMessage { id = ++msgId, t = t, from = from, text = text, kind = kind });
            if (messages.Count > 40) messages.RemoveAt(0);
        }

        void Caution(double t, string key, string text, MessageKind kind = MessageKind.Caution, double every = 8, string from = null)
        {
            if ((lastCaution.TryGetValue(key, out var last) ? last : -1e9) + every > t) return;
            lastCaution[key] = t;
            Say(t, from ?? (def.dual || def.category == MissionCategory.Sar || def.category == MissionCategory.Offshore || def.category == MissionCategory.Police || def.category == MissionCategory.Military ? announcer : "Caution"), text, kind);
        }

        // ------------------------------------------------------------------------------- weather
        /// <summary>Weather at the current mission time, including any scripted change.</summary>
        public WeatherPreset WeatherAt(double t)
        {
            var tl = def.weatherTimeline;
            if (tl == null || tl.Length == 0) return baseWx;
            double Start(int i) => wxStart.TryGetValue(i, out var v) ? v : tl[i].min * 60;
            var idx = -1;
            for (int i = 0; i < tl.Length; i++) if (t >= Start(i)) idx = i;
            if (idx < 0) return baseWx;
            var from = idx == 0 ? baseWx : Weather.Presets.TryGetValue(tl[idx - 1].id, out var f) ? f : baseWx;
            var to = Weather.Presets.TryGetValue(tl[idx].id, out var tt) ? tt : from;
            var k = Clamp((t - Start(idx)) / WEATHER_BLEND_S, 0, 1);
            var key = $"{idx}:{Js.Round(k * 30)}";
            if (key != wxKey)
            {
                wxKey = key;
                wxNow = k >= 1 ? to : Weather.BlendWeather(from, to, k);
                weatherVersion++;
            }
            if (wxAnnounced < idx)
            {
                wxAnnounced = idx;
                if (tl[idx].report != null) Say(t, "Kinloch Tower", tl[idx].report, MessageKind.Caution);
            }
            return wxNow;
        }

        // ------------------------------------------------------------------------------- objectives
        void Activate(int i, double t, HeliState s)
        {
            index = i;
            var o = i < objectives.Count ? objectives[i] : null;
            if (o == null && freeplay) { index = objectives.Count; return; }   // waiting for the next callout
            if (o == null)
            {
                status = MissionStatus.Complete;
                completeAt = t;
                var failed = objectives.Count(x => x.status == ObjectiveStatus.Failed);
                Say(t, announcer, failed > 0 ? "That completes the flight, though not every item was achieved. End the flight for your debrief." : "That completes the task. Shut down or end the flight when ready for your debrief.", failed > 0 ? MessageKind.Caution : MessageKind.Good);
                return;
            }
            o.status = ObjectiveStatus.Active;
            o.t0 = t;
            if (s != null) legFrom = (s.pos.x, s.pos.z);
            var d = o.def;
            if (d.say != null) Say(t, announcer, d.say, d.kind == ObjectiveKind.Autorotation || d.kind == ObjectiveKind.FailEngine || d.kind == ObjectiveKind.Failure ? MessageKind.Warning : MessageKind.Info);
            if (s != null && d.kind == ObjectiveKind.Autorotation) { s.throttleIdle = true; inAutorotation = true; }
            if (s != null && d.kind == ObjectiveKind.PowerRecovery) { s.throttleIdle = false; s.sys.throttleTarget = 1; inAutorotation = false; }
            if (s != null && d.kind == ObjectiveKind.FailEngine)
            {
                FlightModel.FailEngine(s, ac);
                Say(t, "Crew", ac.engines > 1 ? "Engine failure! Number two engine has failed. One engine inoperative." : "Engine failure!", MessageKind.Warning);
            }
            if (s != null && d.kind == ObjectiveKind.Failure)
            {
                FlightModel.FailSystem(s, d.system);
                Say(t, "Caution", d.system == SystemFailure.TailRotor ? "Loud bang, the nose is swinging: TAIL ROTOR FAILURE. Keep speed up, reduce power gently, plan a running landing." : "HYDRAULIC FAILURE: controls heavy. Slow to about 60 knots and fly smoothly.", MessageKind.Warning);
            }
            if (d.kind == ObjectiveKind.Search)
            {
                var area = Area(d.areaId);
                if (area != null) search = Rotorcraft.Sim.Search.NewSearch(area, seed);
                enteredAreaT = null;
                hintGiven = false;
            }
            if (d.kind == ObjectiveKind.Radio) o.detail = $"Press TRANSMIT: “{d.call}”";
            if (d.kind == ObjectiveKind.Decision)
            {
                o.detail = "Decision needed";
                // the decision is about the forecast change: make sure it has arrived (mostly blended in)
                var tl = def.weatherTimeline ?? new WeatherChange[0];
                for (int k = 0; k < tl.Length; k++)
                {
                    var st = wxStart.TryGetValue(k, out var v) ? v : tl[k].min * 60;
                    if (st > t) { wxStart[k] = t - WEATHER_BLEND_S * 0.75; break; }
                }
                WeatherAt(t);
            }
        }

        void FinishDeadline(ObjectiveRuntime o, double t)
        {
            var dl = o.def.deadlineMin;
            if (dl == null) return;
            deadlineItems++;
            var minLate = t / 60 - dl.Value;
            if (minLate > 0)
            {
                late.Add((o.def.label, minLate));
                Say(t, announcer, $"{o.def.label}: {F1(minLate)} min behind the planned time.", MessageKind.Caution);
            }
        }

        void Complete(ObjectiveRuntime o, double t, HeliState s, string detail = null)
        {
            o.status = ObjectiveStatus.Complete;
            o.progress = 1;
            if (detail != null) o.detail = detail;
            FinishDeadline(o, t);
            Activate(index + 1, t, s);
        }

        void Fail(ObjectiveRuntime o, double t, HeliState s, string detail)
        {
            o.status = ObjectiveStatus.Failed;
            o.detail = detail;
            Say(t, announcer, detail, MessageKind.Warning);
            if (o.def.kind == ObjectiveKind.Autorotation) { s.throttleIdle = false; s.sys.throttleTarget = 1; inAutorotation = false; }
            Activate(index + 1, t, s);
        }

        /// <summary>The pre-flight checks were finished in the aircraft before engine start.</summary>
        public void CompleteChecklist()
        {
            if (checklistComplete) return;
            checklistComplete = true;
            proceduralErrors.Remove("Pre-flight checklist not completed");
        }

        SearchAreaDef Area(string id) => def.searchAreas?.FirstOrDefault(a => a.id == id) ?? extraAreas.FirstOrDefault(a => a.id == id);

        // ------------------------------------------------------------------------------- freeplay jobs
        /// <summary>Add a callout's items to the end of the plan (and its casualties and search areas to the world).</summary>
        public List<PlacedTarget> AppendJob(IEnumerable<ObjectiveDef> objs, IList<TargetDef> newTargets, IList<SearchAreaDef> areas, HeliState s, uint jobSeed)
        {
            extraAreas.AddRange(areas);
            var placed = Rotorcraft.Sim.Search.PlaceTargets(newTargets, (def.searchAreas ?? new SearchAreaDef[0]).Concat(extraAreas).ToList(), jobSeed);
            targets.AddRange(placed);
            foreach (var d in newTargets) if (d.areaId == null && d.x != null && d.z != null) found.Add(d.id);
            var idle = index >= objectives.Count;
            foreach (var d in objs) objectives.Add(new ObjectiveRuntime(d));
            if (idle && status == MissionStatus.Running) Activate(index, s.t, s);
            return placed;
        }

        /// <summary>Drop the unfinished items of a callout (the crew stood down or were re-tasked).</summary>
        public void CancelJob(string job, HeliState s)
        {
            var keep = objectives.Where((o, i) => i < index || o.def.job != job || o.status == ObjectiveStatus.Complete).ToList();
            var activeWasJob = Active?.def.job == job;
            objectives.Clear();
            objectives.AddRange(keep);
            if (hoist.phase != HoistPhase.Stowed) { Hoist.AbortHoist(hoist, "Job cancelled."); hoist.phase = HoistPhase.Stowed; hoist.attached = false; }
            search = null;
            index = Math.Min(index, objectives.Count);
            if (activeWasJob || index >= objectives.Count) Activate(index, s.t, s);
        }

        /// <summary>Items belonging to a callout, with their status (for the tablet).</summary>
        public List<ObjectiveRuntime> JobItems(string job) => objectives.Where(o => o.def.job == job).ToList();

        /// <summary>Forget a delivered casualty's weight (freeplay runs several rescues in one flight).</summary>
        void ResetCasualty() { casualtyAboardT = null; deliveredT = null; }
        public PlacedTarget Target(string id) => targets.FirstOrDefault(x => x.id == id);

        /// <summary>Position of a land/load objective's landing point, or null if not yet known (target not found).</summary>
        (double x, double z, string name, double elevM)? LandPoint(ObjectiveDef d)
        {
            if (d.kind == ObjectiveKind.Land || d.kind == ObjectiveKind.Load)
            {
                if (d.siteId != null) { var p = WorldMap.SiteById(d.siteId); return (p.x, p.z, p.name, p.deckHeight ?? WorldMap.SiteElevation(p)); }
                if (d.kind == ObjectiveKind.Land && d.nearTarget != null)
                {
                    var tg = Target(d.nearTarget);
                    if (tg == null || !found.Contains(tg.id)) return null;
                    return (tg.x, tg.z, $"Near the {tg.name.ToLowerInvariant()}", tg.y);
                }
                if (d.x != null && d.z != null) return (d.x.Value, d.z.Value, d.name ?? "Landing site", Ground.Current.GroundAt(d.x.Value, d.z.Value).h);
            }
            return null;
        }

        public NavTarget NavTargetNow()
        {
            for (int i = index; i < objectives.Count; i++)
            {
                var d = objectives[i].def;
                if (d.kind == ObjectiveKind.Waypoint)
                {
                    var p = d.siteId != null ? WorldMap.SiteById(d.siteId) : null;
                    double x = p != null ? p.x : d.x ?? 0, z = p != null ? p.z : d.z ?? 0;
                    return new NavTarget { name = p != null ? p.name : d.name ?? "Waypoint", x = x, z = z, radiusM = d.radiusM ?? 0, altMinFt = d.altMinFt, altMaxFt = d.altMaxFt, elevationFt = Ground.Current.GroundAt(x, z).h * FT, kind = d.kind };
                }
                if (d.kind == ObjectiveKind.Hover)
                {
                    var p = WorldMap.SiteById(d.siteId);
                    return new NavTarget { name = p.name, x = p.x, z = p.z, radiusM = d.radiusM ?? 0, elevationFt = WorldMap.SiteElevation(p) * FT, kind = d.kind };
                }
                if (d.kind == ObjectiveKind.Land || (d.kind == ObjectiveKind.Load && (d.siteId != null || d.x != null)))
                {
                    var lp = LandPoint(d);
                    if (lp == null) return null;
                    return new NavTarget { name = lp.Value.name, x = lp.Value.x, z = lp.Value.z, radiusM = d.kind == ObjectiveKind.Land ? d.maxDistM : d.radiusM ?? 15, elevationFt = lp.Value.elevM * FT, kind = d.kind };
                }
                if (d.kind == ObjectiveKind.Search)
                {
                    var a = Area(d.areaId);
                    if (a == null) return null;
                    return new NavTarget { name = a.name, x = a.cx, z = a.cz, radiusM = Math.Max(a.w, a.h) / 2, elevationFt = Ground.Current.GroundAt(a.cx, a.cz).h * FT, kind = d.kind };
                }
                if (d.kind == ObjectiveKind.Winch)
                {
                    var tg = Target(d.targetId);
                    if (tg == null || !found.Contains(tg.id)) return null;
                    return new NavTarget { name = tg.name, x = tg.x, z = tg.z, radiusM = Hoist.reachM, elevationFt = tg.y * FT, kind = d.kind };
                }
                if (i > index && (d.kind == ObjectiveKind.Climb || d.kind == ObjectiveKind.LiftOff)) continue;
                if (d.kind == ObjectiveKind.Climb || d.kind == ObjectiveKind.Autorotation || d.kind == ObjectiveKind.PowerRecovery) return null;
                // radio, decision, load (no site), failEngine, hold, ap, engine items: look further ahead
            }
            return null;
        }

        // ------------------------------------------------------------------------------- crew actions
        static HoistTarget HT(PlacedTarget t) => new HoistTarget(t.x, t.z, t.y);
        double Scale => ac.rotorDiameter / 15;

        /// <summary>Buttons the HUD should show for the current situation.</summary>
        public List<CrewAction> Actions(HeliState s)
        {
            var o = new List<CrewAction>();
            var a = Active;
            if (a != null && a.def.kind == ObjectiveKind.Radio && status == MissionStatus.Running) o.Add(new CrewAction { id = CrewActionId.Transmit, label = "TRANSMIT", enabled = true, hint = a.def.call });
            var winchObj = a != null && a.def.kind == ObjectiveKind.Winch ? a.def : null;
            if (ac.equipment.hoist && (winchObj != null || (hoist.phase != HoistPhase.Stowed && hoist.phase != HoistPhase.Aboard)))
            {
                var h = hoist;
                if (h.phase == HoistPhase.Stowed || h.phase == HoistPhase.Aborted)
                {
                    var tg = winchObj != null ? Target(winchObj.targetId) : null;
                    var why = tg != null ? Hoist.CanStartLowering(s, HT(tg), Scale) : "No winch task";
                    o.Add(new CrewAction { id = CrewActionId.Winch, label = "WINCH ▼", enabled = why == null, hint = why ?? "Lower the winchman" });
                }
                else o.Add(new CrewAction { id = CrewActionId.Winch, label = "WINCH ✕", enabled = h.phase == HoistPhase.Lowering || h.phase == HoistPhase.OnScene, hint = "Abort the lift" });
            }
            if (ac.equipment.flir) o.Add(new CrewAction { id = CrewActionId.Flir, label = "FLIR", enabled = true, on = flir });
            return o;
        }

        /// <summary>The pending decision, if the active objective is one.</summary>
        public (string prompt, (string id, string label)[] options)? PendingDecision()
        {
            var a = Active;
            if (a == null || a.def.kind != ObjectiveKind.Decision || status != MissionStatus.Running) return null;
            return (a.def.prompt, a.def.options.Select(o => (o.id, o.label)).ToArray());
        }

        public bool Transmit(HeliState s)
        {
            var a = Active;
            if (a == null || a.def.kind != ObjectiveKind.Radio || status != MissionStatus.Running) return false;
            Say(s.t, def.callsign ?? "You", a.def.call, MessageKind.Info);
            Say(s.t, a.def.from ?? "Kinloch Tower", a.def.reply, MessageKind.Info);
            a.score = 100;
            Complete(a, s.t, s, "Call made");
            return true;
        }

        public bool Choose(string optionId, HeliState s)
        {
            var a = Active;
            if (a == null || a.def.kind != ObjectiveKind.Decision || status != MissionStatus.Running) return false;
            var opt = a.def.options.FirstOrDefault(o => o.id == optionId);
            if (opt == null) return false;
            decisions.Add(new DecisionRecord { prompt = a.def.prompt, chosen = opt.label, good = opt.good });
            Say(s.t, announcer, opt.reply, opt.good ? MessageKind.Good : MessageKind.Caution);
            a.score = opt.good ? 100 : 20;
            a.status = ObjectiveStatus.Complete;
            a.progress = 1;
            a.detail = opt.label;
            FinishDeadline(a, s.t);
            if (opt.effect != DecisionEffect.Continue)
            {
                var siteId = opt.effect == DecisionEffect.Divert ? opt.siteId ?? def.startSiteId : def.baseSiteId ?? def.startSiteId;
                var site = WorldMap.SiteById(siteId);
                var hadShutdown = objectives.Skip(index + 1).Any(o => o.def.kind == ObjectiveKind.Shutdown);
                // drop what is left of the plan and replace it with the new destination
                objectives.RemoveRange(index + 1, objectives.Count - index - 1);
                var add = new List<ObjectiveDef> { new ObjectiveDef { kind = ObjectiveKind.Land, label = $"{(opt.effect == DecisionEffect.Divert ? "Divert" : "Return")} and land at {site.name}", phase = opt.effect == DecisionEffect.Divert ? "Diversion" : "Return", siteId = siteId, maxDistM = Math.Max(12, site.radius * 1.5) } };
                if (hadShutdown) add.Add(new ObjectiveDef { kind = ObjectiveKind.Shutdown, label = "Shut down", phase = "Shutdown" });
                foreach (var d in add) objectives.Add(new ObjectiveRuntime(d) { added = true });
                flags.Add("replanned");
                if (def.category == MissionCategory.Test) flags.Add("abandoned");
            }
            Activate(index + 1, s.t, s);
            return true;
        }

        public bool Winch(HeliState s)
        {
            var h = hoist;
            if (!ac.equipment.hoist) return false;
            if (h.phase == HoistPhase.Lowering || h.phase == HoistPhase.OnScene) { Hoist.AbortHoist(h, "Pilot abort."); winchAborts++; Say(s.t, "Winch op", "Abort acknowledged, bringing him up.", MessageKind.Caution); return true; }
            var a = Active;
            if (a == null || a.def.kind != ObjectiveKind.Winch || (h.phase != HoistPhase.Stowed && h.phase != HoistPhase.Aborted)) return false;
            var tg = Target(a.def.targetId);
            if (tg == null) return false;
            var why = Hoist.CanStartLowering(s, HT(tg), Scale);
            if (why != null) { Say(s.t, "Winch op", why, MessageKind.Caution); return false; }
            Hoist.StartLowering(h, s, Scale);
            if (winchStartT == null) winchStartT = s.t;
            Say(s.t, "Winch op", "Winchman going out the door… winchman going down.", MessageKind.Info);
            return true;
        }

        public bool ToggleFlir() { if (!ac.equipment.flir) return false; flir = !flir; return flir; }

        /// <summary>For the map: the search area, coverage and found targets.</summary>
        public ((double x, double z)[] corners, double coverage, string name)? SearchOverlay()
        {
            if (search == null) return null;
            return (Rotorcraft.Sim.Search.AreaCorners(search.area), Rotorcraft.Sim.Search.CoverageFraction(search), search.area.name);
        }

        // ------------------------------------------------------------------------------- per step
        public void Update(HeliState s, IList<SimEvent> events, double dt, Controls controls)
        {
            var t = s.t;
            var o = s.@out;
            var wx = WeatherAt(t);
            // ---- events --------------------------------------------------------------------------------
            foreach (var e in events)
            {
                if (e.type == SimEventType.Engine && e.state == EngineState.Starting)
                {
                    if (!s.sys.beacon) { ProcError("beacon", "Engine started without the anti-collision beacon on"); Caution(t, "bcn", "Beacon should be on before start (B, or the BCN switch).", MessageKind.Caution, 0); }
                    if (s.sys.throttle > 0.2) { ProcError("startthr", "Engine started with the throttle open"); Caution(t, "startthr", "Throttle closed for the start, please.", MessageKind.Caution, 0); }
                    if (tod != TimeOfDay.Day && !s.sys.nav) Caution(t, "navstart", "Position lights on, it is getting dark.", MessageKind.Caution, 0);
                }
                if (e.type == SimEventType.Engine && e.state == EngineState.Off && engineRunT > 5)
                {
                    if (s.@out.rpm > 80 || coolDown < 12) ProcError("cooldown", $"Engine shut down after only {F0(coolDown)} s at idle (cool-down is 15 s)");
                }
                if (e.type == SimEventType.Liftoff && tod != TimeOfDay.Day && !s.sys.nav) { ProcError("navlights", "Took off at dusk or night without position lights"); Caution(t, "navoff", "Your position lights are off.", MessageKind.Caution, 0); }
                if (e.type == SimEventType.Liftoff && !s.sys.governor) Caution(t, "govoff", "Governor is off: you are managing rotor RPM by hand.", MessageKind.Caution, 0);
                if (e.type == SimEventType.Liftoff && FlightModel.MassOf(s, ac) > ac.maxGross + 1) { ProcError("overweight", $"Took off {F0(FlightModel.MassOf(s, ac) - ac.maxGross)} kg above maximum weight"); Caution(t, "mtow", "We are over maximum weight!", MessageKind.Warning, 0); }
                if (e.type == SimEventType.Engine && e.state == EngineState.Starting && controls.collective > 0.1)
                {
                    proceduralErrors.Add("Engine started with the collective raised");
                    Caution(t, "startcol", "Collective should be fully down for the start.", MessageKind.Caution, 0);
                }
                if (e.type == SimEventType.Engine && e.state == EngineState.Flameout) Say(t, announcer, s.fuel <= 0.1 ? "Engine flameout: fuel exhausted! Lower the collective and autorotate." : "Total power loss! Lower the collective and autorotate.", MessageKind.Warning);
                if (e.type == SimEventType.Liftoff && o.rpm < 96)
                {
                    proceduralErrors.Add($"Lifted off with rotor RPM at {F0(o.rpm)}%");
                    Caution(t, "lowrpmlift", "Never lift with the rotor below 100 per cent.", MessageKind.Caution, 0);
                }
                if (e.type == SimEventType.Touchdown) OnTouchdown(e, s);
                if (e.type == SimEventType.Crash)
                {
                    crashReason = e.reason;
                    status = MissionStatus.Failed;
                    Say(t, "System", $"Accident: {e.reason}.", MessageKind.Warning);
                    var a = Active;
                    if (a != null) { a.status = ObjectiveStatus.Failed; a.detail = "Aircraft lost"; }
                    foreach (var r in objectives) if (r.status == ObjectiveStatus.Pending) r.status = ObjectiveStatus.Failed;
                }
            }
            if (status == MissionStatus.Failed) return;

            // ---- metrics -------------------------------------------------------------------------------
            var running = s.engine == EngineState.Running || s.engine == EngineState.Starting;
            if (running || o.rpm > 5) block += dt;
            if (!s.onGround) airborne += dt;
            if (lastPos != null) distance += Js.Hypot(s.pos.x - lastPos.Value.x, s.pos.z - lastPos.Value.z);
            lastPos = (s.pos.x, s.pos.z);
            maxAltFt = Math.Max(maxAltFt, o.altFt);
            var isAirborne = !s.onGround;
            if (s.engine == EngineState.Running)
            {
                engineRunT += dt;
                if (s.onGround && s.sys.throttle < 0.1) coolDown += dt; else if (s.sys.throttle > 0.3) coolDown = 0;
                if (s.sys.throttle < 0.05 && engineRunT > 6 && !s.throttleIdle && Active?.def.kind == ObjectiveKind.RotorRpm) Caution(t, "thrhint", "Engine is idling. Roll the throttle open (R, or the THR button).", MessageKind.Info, 12);
                var lim = FlightModel.TempLimitsOf(ac);
                if (s.engineTemp > lim.max) { Exceed(dt, $"{lim.label} above {lim.max} °C"); Caution(t, "temp", $"{lim.label} high! Reduce power and gain airspeed.", MessageKind.Warning, 10); }
            }
            if (isAirborne)
            {
                if (s.@out.vrs > 0.45) { unstableSec += dt; Caution(t, "vrs", "Vortex ring! Lower the lever slightly and fly forward out of it.", MessageKind.Warning, 8); }
                if (s.@out.bladeStall > 0.3) { Exceed(dt, "Retreating blade stall"); Caution(t, "rbs", "Vibration and pitch-up: retreating blade stall. Slow down!", MessageKind.Warning, 8); }
                if (tod != TimeOfDay.Day && !s.sys.landingLight && Active?.def.kind == ObjectiveKind.Land && o.aglM * FT < 200 && o.gsKt < 50) { Caution(t, "ldglt", "Landing light on for the approach (L).", MessageKind.Caution, 25); if (o.aglM * FT < 60) ProcError("ldglt", "Night approach without the landing light"); }
                if (o.torque > ac.torqueMax) Exceed(dt, $"Torque above {ac.torqueMax}% transient limit");
                else if (o.torque > ac.torqueMcp) Exceed(dt, $"Torque above {ac.torqueMcp}% continuous limit");
                if (o.torque > ac.torqueMcp) Caution(t, "tq", $"Torque {F0(o.torque)}%! Ease the collective.");
                if (o.rpm < ac.rpmMin && !inAutorotation && s.engine == EngineState.Running) { Exceed(dt, "Rotor RPM below the green arc"); Caution(t, "rpm", "Low rotor RPM! Lower the collective.", MessageKind.Warning, 5); }
                if (o.rpm > ac.rpmMax) { Exceed(dt, "Rotor overspeed"); Caution(t, "rpmhi", "Rotor overspeed: raise the collective a little.", MessageKind.Warning, 5); }
                if (o.iasKt > ac.vneKt) { Exceed(dt, $"Airspeed above VNE {ac.vneKt} kt"); Caution(t, "vne", "Too fast! Above never-exceed speed.", MessageKind.Warning, 5); }
                if (Math.Abs(s.phi) > 45 * DEG) Exceed(dt, "Bank angle above 45°");
                var aglFt = o.aglM * FT;
                if (aglFt < 100 && o.vsFpm < -600 && o.gsKt < 30) { unstableSec += dt; Caution(t, "ros", "High rate of descent close to the ground! Check it with collective.", MessageKind.Warning, 6); }
                var near = DistToNearestSite(s);
                var lowWork = def.category == MissionCategory.Sar || def.category == MissionCategory.Offshore || def.category == MissionCategory.Police || def.category == MissionCategory.Military || def.pathway == Pathway.Military || freeplay;
                if (aglFt < 150 && o.iasKt > 45 && near > 600 && !lowWork) { lowLevelSec += dt; Caution(t, "low", "You are very low. Climb to a safe height.", MessageKind.Caution, 10); }
                if (aglFt < 100 && o.iasKt > 60 && lowWork) { lowLevelSec += dt; Caution(t, "low", "Too low for that speed. Climb or slow down.", MessageKind.Caution, 10); }
                // flying into cloud: only the instrument-equipped twins can legitimately continue
                var inCloud = wx.cloudBaseFt != null && (wx.cloudCover == CloudCover.BKN || wx.cloudCover == CloudCover.OVC) && o.altFt > wx.cloudBaseFt.Value + 50;
                if (inCloud && !Hood && ifr) Caution(t, "imc", "In cloud on instruments. Monitor the attitude and your height above the terrain.", MessageKind.Info, 30);
                else if (inCloud && !Hood)
                {
                    imcSec += dt;
                    Caution(t, "imc", ac.equipment.autopilot ? "We are in cloud. Climb to safety altitude on instruments or descend clear." : "Inadvertent cloud! Level the aircraft on instruments and turn back or descend clear.", MessageKind.Warning, 12);
                }
            }
            if (def.maxRangeM != null)
            {
                var start = WorldMap.SiteById(def.startSiteId);
                if (Js.Hypot(s.pos.x - start.x, s.pos.z - start.z) > def.maxRangeM.Value)
                {
                    outOfRangeSec += dt;
                    Caution(t, "range", "You are leaving the exercise area. Turn back towards Kinloch.", MessageKind.Caution, 20);
                }
            }
            if (s.fuel < def.minFuelKg && running) Caution(t, "fuel", $"Fuel {F0(s.fuel)} kg: below planned reserve. Land as soon as practicable.", MessageKind.Warning, 30);

            // ---- winch (runs whatever the active objective, so a lift in progress completes) ----------
            if (hoist.phase != HoistPhase.Stowed)
            {
                var a = Active;
                var tg = a != null && a.def.kind == ObjectiveKind.Winch ? Target(a.def.targetId) : null;
                var tgt = tg != null ? HT(tg) : new HoistTarget(hoist.hook.x, hoist.hook.z, Ground.Current.GroundAt(hoist.hook.x, hoist.hook.z).h);
                var w = Weather.WindAt(wx, t, hoist.hook.x, hoist.hook.y, hoist.hook.z, 10);
                var before = hoist.phase;
                var wasAbort = hoist.abortReason;
                Hoist.StepHoist(hoist, s, tgt, w ?? V3(), Scale, dt);
                if (hoist.abortReason != null && hoist.abortReason != wasAbort) { winchAborts++; Say(t, "Winch op", hoist.abortReason, MessageKind.Warning); }
                if (before != HoistPhase.OnScene && hoist.phase == HoistPhase.OnScene) Say(t, "Winch op", "Winchman is on scene with the casualty. Steady… hold this hover.", MessageKind.Info);
                if (before == HoistPhase.OnScene && hoist.phase == HoistPhase.Raising && hoist.attached) Say(t, "Winch op", "Casualty in the strop. Taking the weight… both coming up.", MessageKind.Info);
                if (hoist.phase == HoistPhase.Aboard && before != HoistPhase.Aboard)
                {
                    if (hoist.attached) { casualtyAboardT = t; Say(t, "Winch op", "Winchman and casualty in the door… both aboard. Clear forward and up.", MessageKind.Good); }
                    else { Say(t, "Winch op", "Winchman back aboard.", MessageKind.Info); hoist.phase = HoistPhase.Stowed; }
                }
                if (hoist.phase == HoistPhase.Aborted) hoist.phase = HoistPhase.Stowed;
            }
            // The winchman is part of the crew mass; the aircraft is lighter while he is on the ground, and
            // carries the casualty once attached until handover.
            var casualtyKg = hoist.attached || (casualtyAboardT != null && deliveredT == null) ? Hoist.casualtyKg : 0;
            s.payload = cargoKg + casualtyKg - (hoist.phase == HoistPhase.OnScene ? Hoist.loadKg : 0);

            // ---- active objective ----------------------------------------------------------------------
            for (int guard = 0; guard < 4 && status == MissionStatus.Running; guard++)
            {
                var a = Active;
                if (a == null) break;
                var before = index;
                UpdateObjective(a, s, dt, wx);
                if (index == before) break;
            }
        }

        void ProcError(string key, string text) { if (!flags.Add(key)) return; proceduralErrors.Add(text); }

        void Exceed(double dt, string label) => exceedances[label] = (exceedances.TryGetValue(label, out var v) ? v : 0) + dt;

        double DistToNearestSite(HeliState s)
        {
            var best = double.PositiveInfinity;
            foreach (var r in objectives)
            {
                if (r.def.siteId == null) continue;
                var lp = WorldMap.SiteById(r.def.siteId);
                best = Math.Min(best, Js.Hypot(s.pos.x - lp.x, s.pos.z - lp.z));
            }
            var start = WorldMap.SiteById(def.startSiteId);
            return Math.Min(best, Js.Hypot(s.pos.x - start.x, s.pos.z - start.z));
        }

        void UpdateObjective(ObjectiveRuntime a, HeliState s, double dt, WeatherPreset wx)
        {
            var d = a.def;
            var o = s.@out;
            var t = s.t;
            switch (d.kind)
            {
                case ObjectiveKind.EngineStart:
                    if (s.engine == EngineState.Starting || s.engine == EngineState.Running) Complete(a, t, s, "Engine started");
                    break;
                case ObjectiveKind.RotorRpm:
                    a.progress = Math.Min(1, o.rpm / d.rpm);
                    a.detail = $"{F0(o.rpm)}%";
                    if (o.rpm >= d.rpm && s.engine == EngineState.Running) Complete(a, t, s, $"Rotor at {F0(o.rpm)}%");
                    break;
                case ObjectiveKind.LiftOff:
                    if (!s.onGround && o.aglM >= d.minAglM) Complete(a, t, s, "Airborne");
                    break;
                case ObjectiveKind.Climb:
                    {
                        var target = WorldMap.SiteElevation(WorldMap.SiteById(d.siteId)) * FT + d.aboveFt;
                        a.progress = Math.Max(0, Math.Min(1, (o.altFt - (target - d.aboveFt)) / d.aboveFt));
                        a.detail = $"{F0(o.altFt)} / {F0(target)} ft";
                        if (!s.onGround && o.altFt >= target) Complete(a, t, s, $"Reached {F0(o.altFt)} ft");
                        break;
                    }
                case ObjectiveKind.Waypoint:
                    {
                        var p = d.siteId != null ? WorldMap.SiteById(d.siteId) : null;
                        double x = p != null ? p.x : d.x ?? 0, z = p != null ? p.z : d.z ?? 0;
                        var dist = Js.Hypot(s.pos.x - x, s.pos.z - z);
                        var radius = d.radiusM ?? 0;
                        a.detail = $"{F2(dist / 1852)} nm";
                        if (dist <= radius && !s.onGround)
                        {
                            a.minDist = Math.Min(a.minDist, dist);
                            if (dist > a.minDist + 8 || dist < radius * 0.25)
                            {
                                a.score = Assessment.ScoreWaypoint(a.minDist, radius, o.altFt, d.altMinFt, d.altMaxFt);
                                var inBand = (d.altMinFt == null || o.altFt >= d.altMinFt) && (d.altMaxFt == null || o.altFt <= d.altMaxFt);
                                if (!inBand) Say(t, announcer, $"Passed {(p != null ? p.name : d.name)} at {F0(o.altFt)} ft, outside the planned {d.altMinFt ?? 0}–{(d.altMaxFt != null ? d.altMaxFt.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "∞")} ft.", MessageKind.Caution);
                                Complete(a, t, s, $"Passed {F0(a.minDist)} m from centre at {F0(o.altFt)} ft");
                            }
                        }
                        break;
                    }
                case ObjectiveKind.Hover:
                    {
                        var p = WorldMap.SiteById(d.siteId);
                        double dx = s.pos.x - p.x, dz = s.pos.z - p.z;
                        var dist = Js.Hypot(dx, dz);
                        var agl = o.aglM;
                        var radius = d.radiusM ?? 0;
                        var inside = !s.onGround && dist <= radius && agl >= d.minAglM && agl <= d.maxAglM;
                        if (inside && a.refHdg == null) a.refHdg = s.psi;
                        var hdgErr = a.refHdg == null ? 0 : Math.Abs(WrapPi(s.psi - a.refHdg.Value)) / DEG;
                        var hdgOk = d.headingTolDeg == null || d.headingTolDeg == 0 || hdgErr <= d.headingTolDeg;
                        // judge the hover itself, not the approach to it: sample only while over the spot near the right height
                        if (!s.onGround && dist <= radius * 1.5 && agl >= d.minAglM - 1 && agl <= d.maxAglM + 1.5)
                        {
                            var hv = a.hv ?? (a.hv = new HoverSums());
                            var he = a.refHdg == null ? 0 : WrapPi(s.psi - a.refHdg.Value) / DEG;
                            var ob = agl < d.minAglM ? d.minAglM - agl : agl > d.maxAglM ? agl - d.maxAglM : 0;
                            hv.n += dt; hv.x += dx * dt; hv.z += dz * dt; hv.xx += dx * dx * dt; hv.zz += dz * dz * dt;
                            hv.a += agl * dt; hv.aa += agl * agl * dt; hv.h += he * dt; hv.hh += he * he * dt; hv.@out += ob * ob * dt;
                            a.samples += dt;
                        }
                        if (inside && hdgOk)
                        {
                            a.timer += dt;
                            a.inside = true;
                            a.settled = 0;
                        }
                        else if (a.inside && a.settled + dt < HOVER_GRACE_S)
                        {
                            // a brief excursion pauses the timer rather than resetting it
                            a.settled += dt;
                        }
                        else
                        {
                            a.settled = 0;
                            if (a.inside && a.timer > 1)
                            {
                                a.resets++;
                                var why = dist > radius ? "drifted out of the circle" : !hdgOk ? "heading wandered" : agl > d.maxAglM ? "too high" : "too low";
                                Caution(t, "hoverreset", $"Timer reset: {why}.", MessageKind.Caution, 3);
                            }
                            a.timer = 0;
                            a.inside = false;
                            a.refHdg = inside ? a.refHdg : null;
                        }
                        a.progress = Math.Min(1, a.timer / d.seconds);
                        a.detail = $"{F0(a.timer)} / {d.seconds} s{(a.settled > 0 ? " · paused, get back in" : "")} · {F1(dist)} m · {F0(agl * FT)} ft";
                        if (a.timer >= d.seconds)
                        {
                            var hv = a.hv ?? new HoverSums();
                            var n = Math.Max(dt, hv.n);
                            double M(double v) => v / n;
                            double Sd(double s1, double s2) => Math.Sqrt(Math.Max(0, M(s2) - Math.Pow(M(s1), 2)));
                            a.score = Assessment.ScoreHover(new HoverStats
                            {
                                drift = Js.Hypot(Sd(hv.x, hv.xx), Sd(hv.z, hv.zz)), offset = Js.Hypot(M(hv.x), M(hv.z)), radiusM = radius,
                                heightWander = Sd(hv.a, hv.aa), outOfBand = Math.Sqrt(M(hv.@out)), halfBandM = (d.maxAglM - d.minAglM) / 2,
                                yawWander = Sd(hv.h, hv.hh), hdgTolDeg = d.headingTolDeg ?? 0, resets = a.resets,
                            });
                            Say(t, announcer, a.score >= 75 ? "Very steady hover. Well done." : "That will do. Keep working on smaller inputs.", a.score >= 75 ? MessageKind.Good : MessageKind.Info);
                            Complete(a, t, s, $"Held for {d.seconds} s (score {a.score})");
                        }
                        break;
                    }
                case ObjectiveKind.Land:
                    {
                        if (s.onGround && a.score == null && (a.added || freeplay))
                        {
                            var lp0 = LandPoint(d);
                            if (lp0 != null && Js.Hypot(s.pos.x - lp0.Value.x, s.pos.z - lp0.Value.z) <= d.maxDistM) { a.score = 100; a.detail = "Already on the ground there"; }
                        }
                        if (s.onGround && a.score != null)
                        {
                            a.settled += dt;
                            if (a.settled > 1.5)
                            {
                                if (d.deliver) Deliver(t);
                                Complete(a, t, s, a.detail);
                            }
                        }
                        else if (!s.onGround)
                        {
                            a.settled = 0;
                            var lp = LandPoint(d);
                            if (lp != null)
                            {
                                var dist = Js.Hypot(s.pos.x - lp.Value.x, s.pos.z - lp.Value.z);
                                a.detail = $"{(dist < 1000 ? F0(dist) + " m" : F1(dist / 1852) + " nm")} to {lp.Value.name}";
                            }
                            else a.detail = "Find the casualty first";
                            if (a.score != null) a.score = null; // lifted again after a landing: re-land
                        }
                        break;
                    }
                case ObjectiveKind.Autorotation:
                    {
                        var aglFt = o.aglM * FT;
                        a.timer += dt;
                        var inGrace = a.timer < AUTOROTATION_GRACE_S;
                        if (a.timer > AUTOROTATION_ENTRY_S)
                        {
                            a.rpmTotal += dt;
                            if (o.rpm >= d.rpmMin && o.rpm <= d.rpmMax) a.rpmIn += dt;
                            a.minRpm = Math.Min(a.minRpm, o.rpm);
                        }
                        a.detail = inGrace && o.rpm < d.rpmMin
                            ? $"RPM {F0(o.rpm)}%: lower the lever · {Math.Ceiling(AUTOROTATION_GRACE_S - a.timer)} s to recover"
                            : $"RPM {F0(o.rpm)}% · {F0(o.iasKt)} kt · {F0(aglFt)} ft";
                        a.progress = Math.Min(1, a.timer / 25);
                        a.settled = o.rpm < 80 ? a.settled + dt : 0;
                        if (o.rpm < 80 && inGrace) Caution(t, "autorpm", $"Rotor RPM {F0(o.rpm)}%! Collective fully down. Keep it down.", MessageKind.Warning, 3);
                        if (!inGrace && a.settled > 1.5)
                        {
                            a.score = 0;
                            Fail(a, t, s, $"Rotor RPM stayed at {F0(o.rpm)}%. I have control. Power restored.");
                        }
                        else if (aglFt <= d.untilAglFt)
                        {
                            var rpmScore = (a.rpmIn / Math.Max(1, a.rpmTotal)) * 100;
                            a.score = (int)Js.Round(Math.Min(100, rpmScore * 0.8 + (o.iasKt >= d.minIasKt ? 20 : Math.Max(0, 20 - (d.minIasKt - o.iasKt)))));
                            Complete(a, t, s, $"RPM in band {F0(rpmScore)}% of the time, lowest {F0(a.minRpm)}%");
                        }
                        break;
                    }
                case ObjectiveKind.PowerRecovery:
                    {
                        var aglFt = o.aglM * FT;
                        a.detail = $"{F0(aglFt)} ft · {F0(o.vsFpm)} fpm";
                        if (s.onGround || aglFt < d.minAglFt)
                        {
                            if (a.timer == 0) { a.timer = 1; a.score = 30; Fail(a, t, s, $"Recovery too low: {F0(aglFt)} ft."); }
                        }
                        else if (o.vsFpm > -150)
                        {
                            a.score = (int)Js.Round(Math.Min(100, 60 + (aglFt - d.minAglFt) / 4));
                            Complete(a, t, s, $"Descent arrested at {F0(aglFt)} ft");
                        }
                        break;
                    }
                case ObjectiveKind.Shutdown:
                    a.detail = s.engine == EngineState.Off ? $"Rotor {F0(o.rpm)}%" : s.sys.throttle > 0.1 ? "Throttle to idle (F)" : $"Cool-down {F0(Math.Min(15, coolDown))} / 15 s, then engine off (E)";
                    a.progress = s.engine == EngineState.Off ? 1 : Math.Min(1, coolDown / 15);
                    if (s.onGround && (s.engine == EngineState.Off || s.engine == EngineState.Flameout)) { a.score = flags.Contains("cooldown") ? 50 : 100; Complete(a, t, s, flags.Contains("cooldown") ? "Shut down without full cool-down" : "Shut down after cool-down"); }
                    break;
                case ObjectiveKind.Radio:
                case ObjectiveKind.Decision:
                    // completed by Transmit() / Choose()
                    break;
                case ObjectiveKind.Load:
                    {
                        var lp = LandPoint(d);
                        var atSite = lp == null || Js.Hypot(s.pos.x - lp.Value.x, s.pos.z - lp.Value.z) <= (d.radiusM ?? 25);
                        var ok = s.onGround && atSite && s.engine == EngineState.Running;
                        if (ok) a.timer += dt;
                        else if (a.timer > 0 && !s.onGround) { a.timer = 0; Caution(t, "loadlift", $"We lifted before the {d.what} was secured.", MessageKind.Caution, 5); }
                        a.progress = Math.Min(1, a.timer / d.seconds);
                        a.detail = !s.onGround ? $"Land{(lp != null ? " at " + lp.Value.name : "")} to {(d.pickup ? "load" : "unload")} {d.what}" : !atSite ? $"Not at {lp?.name}" : s.engine != EngineState.Running ? "Engine must be running (rotors turning)" : $"{(d.pickup ? "Loading" : "Unloading")} {d.what}: {F0(a.timer)} / {d.seconds} s · collective down";
                        if (ok && a.timer >= d.seconds)
                        {
                            cargoKg = d.pickup ? cargoKg + d.payloadKg : Math.Max(0, cargoKg - d.payloadKg);
                            if (d.pickup && cargoKg > ac.maxPayload) Caution(t, "payload", $"Load is {F0(cargoKg)} kg, above the {ac.maxPayload} kg limit.", MessageKind.Warning, 0);
                            a.score = 100;
                            Complete(a, t, s, $"{char.ToUpperInvariant(d.what[0])}{d.what.Substring(1)} {(d.pickup ? "aboard" : "off")} · payload now {F0(cargoKg)} kg");
                            Say(t, announcer, $"{(d.pickup ? "Loaded" : "Unloaded")} and secure. Clear to lift.", MessageKind.Info);
                        }
                        break;
                    }
                case ObjectiveKind.Search:
                    {
                        var st = search;
                        var tg = Target(d.targetId);
                        if (st == null || tg == null) { Complete(a, t, s, "Search area not defined"); break; }
                        double night = tod == TimeOfDay.Night ? 1 : tod == TimeOfDay.Dusk ? 0.5 : 0;
                        var inside = Rotorcraft.Sim.Search.InArea(st.area, s.pos.x, s.pos.z);
                        if (inside && enteredAreaT == null && !s.onGround)
                        {
                            enteredAreaT = t;
                            Say(t, "Crew", $"Entering the {st.area.name} search area. Suggest {(night > 0.5 ? "300 ft, 50 knots, searchlight or FLIR" : "300 to 500 ft, 60 knots")}, parallel tracks.", MessageKind.Info);
                        }
                        if (!s.onGround)
                        {
                            var hit = Rotorcraft.Sim.Search.StepSearch(st, s, tg, Rotorcraft.Sim.Search.SensorsFor(wx, night, s.sys.searchlight, flir), dt);
                            if (hit)
                            {
                                found.Add(tg.id);
                                var mins = (t - (enteredAreaT ?? a.t0)) / 60;
                                a.score = (int)Js.Round(Clamp(100 - Math.Max(0, mins - 3) * 7 - (hintGiven ? 15 : 0), 30, 100));
                                searchScores.Add(a.score.Value);
                                rescueLog.Add($"{tg.name} found after {F1(mins)} min in the search area ({Js.Round(Rotorcraft.Sim.Search.CoverageFraction(st) * 100)}% covered)");
                                Say(t, "Crew", $"Contact! {Rotorcraft.Sim.Search.ClockCall(s, tg.x, tg.z)}. That's the {tg.name.ToLowerInvariant()}.", MessageKind.Good);
                                Complete(a, t, s, $"Found after {F1(mins)} min");
                                break;
                            }
                        }
                        var cov = Rotorcraft.Sim.Search.CoverageFraction(st);
                        a.progress = cov;
                        a.detail = $"{st.area.name} · {Js.Round(cov * 100)}% searched{(inside ? "" : " · not in the area")}";
                        var searching = enteredAreaT != null ? (t - enteredAreaT.Value) / 60 : 0;
                        if (!hintGiven && d.hintAfterMin != null && searching >= d.hintAfterMin)
                        {
                            hintGiven = true;
                            var call = Regex.Replace(Rotorcraft.Sim.Search.ClockCall(s, tg.x + 120, tg.z - 80), @"^\d+ o'clock, ", "about ");
                            var brg = Js.Round(((Math.Atan2(tg.x - s.pos.x, -(tg.z - s.pos.z)) * 180 / Math.PI + 360) % 360));
                            if (def.category == MissionCategory.Police || def.pathway == Pathway.Police) Say(t, "Police", $"Update: a caller reports someone {call} from you, bearing {brg}.", MessageKind.Info);
                            else Say(t, "Coastguard", $"Update: a sighting places the casualty {call} from you, bearing {brg}.", MessageKind.Info);
                        }
                        break;
                    }
                case ObjectiveKind.Winch:
                    {
                        var tg = Target(d.targetId);
                        if (tg == null) { Complete(a, t, s, "No casualty"); break; }
                        found.Add(tg.id);   // a known position (training)
                        var h = hoist;
                        var call = Hoist.ConningCall(h, s, HT(tg), Scale);
                        if (!s.onGround) Caution(t, "conn", call, MessageKind.Info, h.phase == HoistPhase.Stowed ? 5 : 3, "Winch op");
                        a.detail = h.phase == HoistPhase.Stowed ? call : h.phase == HoistPhase.Lowering ? $"Lowering {F0(h.cable)} m · swing {F0(h.swingDeg)}°" : h.phase == HoistPhase.OnScene ? $"On scene {F0(h.sceneTimer)} / {Hoist.sceneTime} s · hold the hover" : h.phase == HoistPhase.Raising ? $"Raising {F0(h.cable)} m{(h.attached ? " with casualty" : "")}" : "Aboard";
                        a.progress = h.phase == HoistPhase.Stowed ? 0 : h.phase == HoistPhase.Lowering ? 0.25 : h.phase == HoistPhase.OnScene ? 0.5 + 0.25 * h.sceneTimer / Hoist.sceneTime : h.phase == HoistPhase.Raising ? 0.8 : 1;
                        if (casualtyAboardT != null && h.phase == HoistPhase.Aboard)
                        {
                            var acc = Hoist.HoistAccuracy(h);
                            var mins = winchStartT != null ? (t - winchStartT.Value) / 60 : 0;
                            a.score = (int)Js.Round(Clamp(100 - acc * 4 - Math.Max(0, h.maxSwingDeg - 15) * 1.2 - winchAborts * 15 - Math.Max(0, mins - 2) * 5, 20, 100));
                            rescueScores.Add(a.score.Value);
                            rescueLog.Add($"Winch: hover error {F1(acc)} m RMS, max swing {F0(h.maxSwingDeg)}°, {winchAborts} abort{(winchAborts == 1 ? "" : "s")}, {F1(mins)} min");
                            h.phase = HoistPhase.Stowed; h.attached = false; h.maxSwingDeg = 0; h.errSum = 0; h.errN = 0;
                            Complete(a, t, s, $"Recovered · score {a.score}");
                        }
                        break;
                    }
                case ObjectiveKind.FailEngine:
                    a.timer += dt;
                    a.minRpm = Math.Min(a.minRpm, o.rpm);
                    a.detail = $"Rotor {F0(o.rpm)}% · {F0(o.iasKt)} kt · torque {F0(o.torque)}%";
                    a.progress = Math.Min(1, a.timer / 10);
                    if (a.timer >= 10 && (o.rpm >= ac.rpmMin - 2 || s.onGround))
                    {
                        a.score = (int)Js.Round(Clamp(100 - Math.Max(0, ac.rpmMin - a.minRpm) * 6, 0, 100));
                        Complete(a, t, s, $"Controlled: lowest rotor RPM {F0(a.minRpm)}%");
                    }
                    break;
                case ObjectiveKind.Failure:
                    a.timer += dt;
                    a.detail = $"{F0(o.iasKt)} kt · {F0(o.aglM * FT)} ft · yaw rate {F0(s.r / DEG)}°/s";
                    a.progress = Math.Min(1, a.timer / 12);
                    if (a.timer >= 12) { a.score = (int)Js.Round(Clamp(100 - Math.Max(0, Math.Abs(s.r / DEG) - 5) * 3, 30, 100)); Complete(a, t, s, "Aircraft under control"); }
                    break;
                case ObjectiveKind.Hold:
                    {
                        var hdg = HdgDeg(s.psi);
                        var he = Math.Abs(((hdg - d.headingDeg + 540) % 360) - 180);
                        var ae = Math.Abs(o.altFt - d.altFt);
                        var inside = !s.onGround && ae <= d.altTolFt && he <= d.hdgTolDeg;
                        if (!s.onGround) { a.errAlt += ae * ae * dt; a.errHdg += he * he * dt; a.samples += dt; }
                        var hdgLabel = ((int)d.headingDeg).ToString().PadLeft(3, '0');
                        if (inside) a.timer += dt;
                        else { if (a.timer > 2) { a.resets++; Caution(t, "holdreset", ae > d.altTolFt ? $"Altitude {F0(o.altFt)} ft: target {d.altFt}." : $"Heading {F0(hdg)}°: target {hdgLabel}.", MessageKind.Caution, 4); } a.timer = 0; }
                        a.progress = Math.Min(1, a.timer / d.seconds);
                        a.detail = $"{hdgLabel}° / {d.altFt} ft · {F0(a.timer)} of {d.seconds} s";
                        if (a.timer >= d.seconds)
                        {
                            var n = Math.Max(dt, a.samples);
                            a.score = (int)Js.Round(Clamp(100 - (Math.Sqrt(a.errAlt / n) / d.altTolFt) * 35 - (Math.Sqrt(a.errHdg / n) / d.hdgTolDeg) * 35 - Math.Max(0, a.resets - 1) * 6, 0, 100));
                            instrumentScores.Add(a.score.Value);
                            Complete(a, t, s, $"Held {d.seconds} s (score {a.score})");
                        }
                        break;
                    }
                case ObjectiveKind.Gear:
                    a.progress = d.gearDown ? s.gearPos : 1 - s.gearPos;
                    a.detail = s.gearPos >= 0.98 ? "Wheels down and locked" : s.gearPos <= 0.02 ? "Wheels up" : $"Wheels travelling {F0((d.gearDown ? s.gearPos : 1 - s.gearPos) * 100)}% · gear lever (Q)";
                    if (d.gearDown ? s.gearPos >= 0.98 : s.gearPos <= 0.02 && !s.onGround) { a.score = 100; Complete(a, t, s, d.gearDown ? "Three greens" : "Gear up"); }
                    break;
                case ObjectiveKind.Ap:
                    {
                        var on = d.mode == APMode.Alt ? ap.alt : d.mode == APMode.Hdg ? ap.hdg : ap.hover;
                        var m = d.mode.ToString().ToUpperInvariant();
                        a.detail = $"Engage {m}{(d.mode == APMode.Hover ? " (HVR)" : "")} on the autopilot";
                        if (on && !s.onGround) { a.score = 100; Complete(a, t, s, $"{m} engaged"); }
                        break;
                    }
            }
        }

        void Deliver(double t)
        {
            if (freeplay) { if (casualtyAboardT != null) rescueLog.Add($"Casualty handed over at {F0(t / 60)} min"); ResetCasualty(); return; }
            if (deliveredT != null) return;
            deliveredT = t;
            var c = def.casualty;
            if (c != null)
            {
                var mins = t / 60;
                var inTime = mins <= c.goldenMin;
                rescueLog.Add($"{c.name} handed over {F0(mins)} min after tasking ({(inTime ? "within" : "outside")} the {c.goldenMin} min target)");
                Say(t, announcer, inTime ? $"{c.name} handed to the medical team. Good job, everyone." : $"{c.name} handed over. That took longer than we would like.", inTime ? MessageKind.Good : MessageKind.Caution);
            }
        }

        void OnTouchdown(SimEvent e, HeliState s)
        {
            var a = Active;
            var landObj = a != null && a.def.kind == ObjectiveKind.Land ? a.def : null;
            var lp = landObj != null ? LandPoint(landObj) : null;
            double? dist = lp != null ? Js.Hypot(e.x - lp.Value.x, e.z - lp.Value.z) : (double?)null;
            var sc = Assessment.ScoreLanding(new LandingInput { vsMs = e.vs, gsMs = e.gs, roll = e.roll, pitch = e.pitch, distM = dist, maxDistM = landObj?.maxDistM ?? 10 });
            var rec = new TouchdownRecord { t = e.t, vsMs = e.vs, gsMs = e.gs, roll = e.roll, pitch = e.pitch, distM = dist, siteId = e.siteId, severity = e.severity, score = sc.score };
            if (e.severity == TouchdownSeverity.Hard) { hardLandings++; Say(s.t, announcer, $"Hard landing, {F0(e.vs * 196.85)} feet per minute. The aircraft will need an inspection.", MessageKind.Warning); }
            // unprepared sites: the slope must be within limits
            var g = Ground.Current.GroundAt(e.x, e.z);
            var maxSlope = landObj?.maxSlopeDeg ?? (landObj != null && landObj.siteId == null ? 8 : 90);
            var slopeDeg = g.slope / DEG;
            if (landObj != null && a != null && dist != null && dist <= landObj.maxDistM && slopeDeg > maxSlope)
            {
                rec.score = Math.Min(sc.score, 50);
                landings.Add(rec);
                offTarget++;
                Say(s.t, announcer, $"Slope is {F0(slopeDeg)}°, more than the {maxSlope}° limit. Lift and find a flatter spot.", MessageKind.Caution);
            }
            else if (landObj != null && a != null && dist != null && dist <= landObj.maxDistM)
            {
                rec.score = sc.score;
                landings.Add(rec);
                a.score = sc.score;
                a.detail = $"Grade {Assessment.LandingGrade(sc.score)} · {F0(e.vs * 196.85)} fpm · {F1(dist.Value)} m from centre";
                if (e.severity != TouchdownSeverity.Hard) Say(s.t, announcer, sc.score >= 85 ? "Beautiful landing." : sc.score >= 65 ? "Good landing." : "Down safely, but that one needs work.", sc.score >= 65 ? MessageKind.Good : MessageKind.Info);
            }
            else
            {
                rec.score = Math.Min(sc.score, 60);
                landings.Add(rec);
                if (landObj != null && lp != null && dist != null)
                {
                    offTarget++;
                    Say(s.t, announcer, $"That is {F0(dist.Value)} m from {lp.Value.name}. Lift and reposition onto the landing area.", MessageKind.Caution);
                }
                else if (a != null && (a.def.kind == ObjectiveKind.Hover || a.def.kind == ObjectiveKind.Waypoint)) Say(s.t, announcer, "We touched down. Lift back into the hover and carry on.", MessageKind.Info);
            }
        }

        static double Avg(IEnumerable<double> a) { var l = a.ToList(); return l.Count > 0 ? l.Sum() / l.Count : 0; }
        static int R(double v) => (int)Js.Round(v);

        /// <summary>Build the debrief. ended explains why the flight finished.</summary>
        public FlightResult Finish(HeliState s, FlightEnd ended)
        {
            var d = def;
            var scores = new Dictionary<ScoreCategory, int>();
            var crashed = crashReason != null || s.crashed;
            double ScoreOf(ObjectiveRuntime o) => o.status == ObjectiveStatus.Complete ? o.score ?? 0 : 0;
            var landObjs = objectives.Where(o => o.def.kind == ObjectiveKind.Land).ToList();
            if (landObjs.Count > 0) scores[ScoreCategory.Landing] = Math.Max(0, R(Avg(landObjs.Select(ScoreOf)) - offTarget * 8));
            var hov = objectives.Where(o => o.def.kind == ObjectiveKind.Hover).ToList();
            if (hov.Count > 0) scores[ScoreCategory.Hover] = R(Avg(hov.Select(ScoreOf)));
            var nav = objectives.Where(o => o.def.kind == ObjectiveKind.Waypoint).ToList();
            if (nav.Count > 0) scores[ScoreCategory.Navigation] = Math.Max(0, R(Avg(nav.Select(ScoreOf)) - outOfRangeSec * 0.2));
            // handling
            double handling = 100;
            double Sec(string label) => exceedances.Where(kv => kv.Key.StartsWith(label)).Sum(kv => kv.Value);
            handling -= Math.Min(40, Sec("Torque above " + ac.torqueMcp) * 3);
            handling -= Math.Min(50, Sec("Torque above " + ac.torqueMax) * 12);
            handling -= Math.Min(40, Sec("Rotor RPM below") * 6);
            handling -= Math.Min(30, Sec("Rotor overspeed") * 3);
            handling -= Math.Min(40, Sec("Airspeed above") * 4);
            handling -= Math.Min(20, Sec("Bank angle") * 1);
            var emerg = objectives.Where(o => o.def.kind == ObjectiveKind.Autorotation || o.def.kind == ObjectiveKind.PowerRecovery || o.def.kind == ObjectiveKind.FailEngine || o.def.kind == ObjectiveKind.Failure).ToList();
            if (emerg.Count > 0) handling = handling * 0.5 + Avg(emerg.Select(o => (double)(o.score ?? 0))) * 0.5;
            scores[ScoreCategory.Handling] = Math.Max(0, R(handling));
            scores[ScoreCategory.Procedures] = Math.Max(0, 100 - proceduralErrors.Count * 20 - objectives.Count(o => (o.def.kind == ObjectiveKind.Radio || o.def.kind == ObjectiveKind.Ap || o.def.kind == ObjectiveKind.Gear) && o.status != ObjectiveStatus.Complete) * 15);
            scores[ScoreCategory.Fuel] = Assessment.ScoreFuel(s.fuel, d.minFuelKg);
            var safety = 100 - unstableSec * 6 - lowLevelSec * 1.5 - hardLandings * 25 - s.damage * 60 - outOfRangeSec * 0.3 - imcSec * 1.5;
            if (crashed) safety = 0;
            scores[ScoreCategory.Safety] = Math.Max(0, R(safety));
            // role categories, only when the mission has those elements
            var searchObjs = objectives.Where(o => o.def.kind == ObjectiveKind.Search).ToList();
            if (searchObjs.Count > 0) scores[ScoreCategory.Search] = R(Avg(searchObjs.Select(ScoreOf)));
            var winchObjs = objectives.Where(o => o.def.kind == ObjectiveKind.Winch).ToList();
            if (winchObjs.Count > 0 || d.casualty != null)
            {
                var parts = winchObjs.Select(ScoreOf).ToList();
                if (d.casualty != null)
                {
                    if (deliveredT == null) parts.Add(0);
                    else { var mins = deliveredT.Value / 60; parts.Add(R(Clamp(100 - Math.Max(0, mins - d.casualty.goldenMin) * 4, 20, 100))); }
                }
                scores[ScoreCategory.Rescue] = R(Avg(parts));
            }
            var decObjs = objectives.Where(o => o.def.kind == ObjectiveKind.Decision).ToList();
            if (decObjs.Count > 0) scores[ScoreCategory.Decisions] = R(Avg(decObjs.Select(ScoreOf)));
            var holdObjs = objectives.Where(o => o.def.kind == ObjectiveKind.Hold).ToList();
            if (holdObjs.Count > 0) scores[ScoreCategory.Instrument] = Math.Max(0, R(Avg(holdObjs.Select(ScoreOf)) - imcSec * 0.5));
            var hasDeadlines = objectives.Any(o => o.def.deadlineMin != null);
            if (hasDeadlines)
            {
                var missed = objectives.Count(o => o.def.deadlineMin != null && o.status != ObjectiveStatus.Complete);
                scores[ScoreCategory.Timeliness] = Math.Max(0, R(100 - late.Sum(l => Math.Min(40, l.minLate * 8)) - missed * 40));
            }
            var overall = crashed ? 0 : Assessment.OverallScore(scores);

            var exceedanceList = exceedances.Where(kv => kv.Value > 0.3).Select(kv => $"{kv.Key} for {F1(kv.Value)} s").ToList();
            var allComplete = objectives.All(o => o.status == ObjectiveStatus.Complete);
            var failedCriteria = new List<string>();
            if (crashed) failedCriteria.Add($"Accident: {crashReason ?? s.crashReason}");
            if (ended == FlightEnd.Aborted && !allComplete) failedCriteria.Add("Flight ended before all items were completed");
            foreach (var o in objectives) if (o.status == ObjectiveStatus.Failed && !crashed) failedCriteria.Add($"Not achieved: {o.def.label}");
            if (overall < d.assessment.passMark && !crashed) failedCriteria.Add($"Overall {overall}% is below the {d.assessment.passMark}% pass mark");
            foreach (var kv in d.assessment.minimums)
                if (scores.TryGetValue(kv.Key, out var v) && v < kv.Value) failedCriteria.Add($"{Assessment.CategoryLabels[kv.Key]} {v}% is below the {kv.Value}% minimum");
            if (d.assessment.noExceedances)
            {
                var crit = exceedances.Where(kv => (kv.Key.Contains($"{ac.torqueMax}%") && kv.Value > 0.2) || (kv.Key.Contains($"{ac.torqueMcp}%") && kv.Value > 3) || (kv.Key.StartsWith("Rotor RPM below") && kv.Value > 1) || (kv.Key.StartsWith("Airspeed above") && kv.Value > 0.5));
                foreach (var kv in crit) failedCriteria.Add($"Exceedance: {kv.Key}");
            }
            if (d.assessment.maxHardLandings != null && hardLandings > d.assessment.maxHardLandings) failedCriteria.Add($"{hardLandings} hard landing{(hardLandings > 1 ? "s" : "")}");
            if (d.category == MissionCategory.Test && decisions.Any(x => !x.good)) failedCriteria.Add("Unsafe decision during the test");
            if (flags.Contains("abandoned")) failedCriteria.Add("Test discontinued: the remaining items were not flown");
            var pass = allComplete && failedCriteria.Count == 0;

            var outcome = crashed ? FlightOutcome.Crashed : !allComplete ? (ended == FlightEnd.Aborted ? FlightOutcome.Aborted : FlightOutcome.Failed)
                : (d.category == MissionCategory.Private || d.category == MissionCategory.Practice) ? (pass ? FlightOutcome.Completed : FlightOutcome.Failed) : pass ? FlightOutcome.Passed : FlightOutcome.Failed;
            var landingsScored = landings.Where(l => l.distM != null).ToList();
            var best = landingsScored.Count > 0 ? landingsScored.Aggregate((x, y) => y.score > x.score ? y : x) : landings.FirstOrDefault();
            var warns = proceduralErrors.Concat(warnings).ToList();
            if (unstableSec > 0.5) warns.Add($"High rate of descent near the ground for {F1(unstableSec)} s");
            if (lowLevelSec > 2) warns.Add($"Low-level fast flight for {F0(lowLevelSec)} s");
            if (outOfRangeSec > 2) warns.Add($"Outside the exercise area for {F0(outOfRangeSec)} s");
            if (imcSec > 2) warns.Add($"In cloud for {F0(imcSec)} s");
            if (s.fuel < d.minFuelKg) warns.Add($"Landed with {F0(s.fuel)} kg fuel, below the {d.minFuelKg} kg reserve");
            foreach (var l in late) warns.Add($"{l.label}: {F1(l.minLate)} min late");
            if (flags.Contains("replanned")) warns.Add("The plan was changed in flight (return or diversion)");

            return new FlightResult
            {
                missionId = d.id,
                missionTitle = d.title,
                aircraftId = ac.id,
                outcome = outcome,
                pass = pass,
                startedAt = startedAt,
                flightSeconds = airborne,
                blockSeconds = block,
                objectives = objectives.Select(o => new ObjectiveResult { label = o.def.label + (o.added ? " (replanned)" : ""), phase = o.def.phase, status = o.status, detail = o.detail }).ToList(),
                scores = scores,
                overall = overall,
                failedCriteria = failedCriteria,
                landings = landings.ToList(),
                bestLanding = best,
                exceedances = exceedanceList,
                warnings = warns,
                recommendations = Recommendations(scores, Stats),
                fuelUsedKg = Math.Max(0, startFuel - s.fuel),
                fuelRemainingKg = s.fuel,
                damage = s.damage,
                maxAltFt = maxAltFt,
                distanceKm = distance / 1000,
                crashReason = crashReason ?? s.crashReason,
                checklistComplete = checklistComplete,
                xp = pass ? d.xp : allComplete && !crashed ? R(d.xp * 0.4) : R(Math.Min(d.xp * 0.2, airborne / 6)),
                timeOfDay = tod,
                weatherId = baseWx.id,
                decisions = decisions.Count > 0 ? decisions.ToList() : null,
                rescue = rescueLog.Count > 0 ? rescueLog.ToList() : null,
            };
        }

        /// <summary>Internal metrics exposed read-only for the recommendations generator and tests.</summary>
        public MissionStats Stats => new MissionStats
        {
            hardLandings = hardLandings, offTarget = offTarget, unstableSec = unstableSec, lowLevelSec = lowLevelSec, imcSec = imcSec,
            exceedances = new Dictionary<string, double>(exceedances), proceduralErrors = proceduralErrors.ToList(), decisions = decisions.ToList(), winchAborts = winchAborts,
        };

        static List<string> Recommendations(Dictionary<ScoreCategory, int> scores, MissionStats st)
        {
            var r = new List<string>();
            bool Below(ScoreCategory c, int lim) => scores.TryGetValue(c, out var v) && v < lim;
            if (Below(ScoreCategory.Hover, 70)) r.Add("Hover: look well ahead at a fixed reference and make small, early cyclic corrections. Repeat Exercise 2 to build the habit.");
            if (Below(ScoreCategory.Landing, 70)) r.Add("Landing: arrive in a steady hover over the spot first, then lower the collective slowly. Aim for under 100 ft/min at touchdown.");
            if (st.offTarget > 0) r.Add("Landing accuracy: if you are not over the landing area, or the slope is too steep, lift and reposition rather than landing short.");
            if (Below(ScoreCategory.Navigation, 70)) r.Add("Navigation: plan each leg's heading and altitude before departure and check them against the instruments every few seconds.");
            if (st.exceedances.Keys.Any(k => k.StartsWith("Torque"))) r.Add("Power: watch the torque gauge when raising the collective. Over-pitching risks rotor droop and engine damage, and with one engine out the margin is small.");
            if (st.exceedances.Keys.Any(k => k.StartsWith("Rotor RPM below"))) r.Add("Rotor RPM: if it decays, lower the collective immediately. Low rotor RPM is unrecoverable if left too long.");
            if (st.unstableSec > 0.5) r.Add("Approaches: keep the descent rate below 500 ft/min when slow and close to the ground (vortex ring and heavy-landing risk).");
            if (st.imcSec > 2) r.Add("Weather: turn back or divert before the cloud reaches you. Entering cloud without an instrument rating and an equipped aircraft is a leading cause of fatal accidents.");
            if (Below(ScoreCategory.Search, 70)) r.Add("Search: fly parallel tracks spaced about twice the sweep width, at 300–500 ft and 60 kt or slower. Faster or higher means the crew sees less.");
            if (Below(ScoreCategory.Rescue, 70)) r.Add($"Winching: come to a high hover beside the casualty, then follow the winch operator's calls with very small inputs. Pick an outside reference and hold it.{(st.winchAborts > 0 ? " Drifting off a grounded winchman aborts the lift." : "")}");
            if (st.decisions.Any(d => !d.good)) r.Add($"Decisions: {string.Join(", ", st.decisions.Where(d => !d.good).Select(d => $"“{d.chosen}”"))} was not the safe choice. Review the briefing risks and the weather before committing.");
            if (Below(ScoreCategory.Instrument, 70)) r.Add("Instrument flying: scan attitude, then altitude and heading. Make small attitude changes and wait for the instruments to settle. Use the autopilot to reduce workload.");
            if (Below(ScoreCategory.Timeliness, 80)) r.Add("Timeliness: plan the route and fuel so each stop is reached within its slot. Avoid extra circuits on arrival.");
            if (st.proceduralErrors.Count > 0) r.Add($"Procedures: {string.Join("; ", st.proceduralErrors)}.");
            if (Below(ScoreCategory.Fuel, 80)) r.Add("Fuel: plan to land with at least the reserve. Check fuel at each turning point.");
            if (r.Count == 0) r.Add("A tidy flight. Try the next task, or fly this one in Realistic handling mode for a greater challenge.");
            return r;
        }
    }
}
