// Menu and career screens (port of src/ui/screens.tsx and App.tsx's routing). Everything shown comes from the
// career data through the career manager; buttons only navigate or call its functions.
using System;
using System.Collections.Generic;
using System.Linq;
using Rotorcraft.Game;
using Rotorcraft.Sim;
using UnityEngine;
using UnityEngine.UIElements;
using static Rotorcraft.Presentation.UI.Ui;

namespace Rotorcraft.Presentation.UI
{
    public static partial class Screens
    {
        public static readonly Dictionary<ScoreCategory, string> CATEGORY_LABELS = new Dictionary<ScoreCategory, string>
        {
            [ScoreCategory.Landing] = "Landing quality", [ScoreCategory.Hover] = "Hover precision", [ScoreCategory.Navigation] = "Navigation accuracy", [ScoreCategory.Handling] = "Aircraft handling",
            [ScoreCategory.Procedures] = "Procedural compliance", [ScoreCategory.Fuel] = "Fuel management", [ScoreCategory.Safety] = "Safety and airmanship",
            [ScoreCategory.Search] = "Search effectiveness", [ScoreCategory.Rescue] = "Rescue and casualty care", [ScoreCategory.Decisions] = "Decision making", [ScoreCategory.Instrument] = "Instrument flying", [ScoreCategory.Timeliness] = "Timeliness",
        };

        public static VisualElement Build(GameApp app, Route r)
        {
            switch (r.id)
            {
                case "menu": return MainMenu(app);
                case "account": return Standalone(app, AccountScreen(app));
                case "pilots": return Standalone(app, PilotsScreen(app));
                case "freeplay": return Standalone(app, FreeplaySetup(app));
                case "fpreport": return Standalone(app, FreeplayReport(app, r));
                case "company": return Standalone(app, app.Company == null ? CompanyStart(app) : CompanyHub(app));
                case "cpreport": return Standalone(app, CompanyReport(app, r));
                case "new": return NewCareer(app);
            }
            if (app.Career == null) return NewCareer(app);
            VisualElement body;
            switch (r.id)
            {
                case "hq": body = HQ(app); break;
                case "school": body = FlightSchool(app); break;
                case "module": body = ModuleView(app, r.moduleId); break;
                case "exam": body = ExamView(app, r.examId); break;
                case "missions": body = MissionBoard(app); break;
                case "employers": body = EmployersView(app); break;
                case "brief": body = Briefing(app, r.missionId); break;
                case "debrief": body = Debrief(app, r); break;
                case "quals": body = QualificationsView(app); break;
                case "hangar": body = Hangar(app); break;
                case "records": body = Records(app); break;
                case "settings": body = SettingsView(app); break;
                case "tutorial": body = Tutorial(app); break;
                default: body = HQ(app); break;
            }
            return Shell(app, r.id, body);
        }

        static T St<T>(GameApp app, string key, T def) => app.ScreenState.TryGetValue(key, out var v) && v is T t ? t : def;
        static void Set(GameApp app, string key, object v, bool refresh = true) { app.ScreenState[key] = v; if (refresh) app.Later(app.Refresh); }

        // ---------------------------------------------------------------------------------------- shell
        sealed class Section { public string label, go; public string[] ids; public (string id, string label)[] tabs; }
        static readonly Section[] SECTIONS =
        {
            new Section { label = "Home", go = "hq", ids = new[] { "hq" } },
            new Section { label = "Fly", go = "missions", ids = new[] { "missions", "brief", "debrief" } },
            new Section { label = "Ground school", go = "school", ids = new[] { "school", "module", "exam" } },
            new Section { label = "My pilot", go = "quals", ids = new[] { "quals", "employers", "records" }, tabs = new[] { ("quals", "Licences"), ("employers", "Employers"), ("records", "Logbook") } },
            new Section { label = "Hangar", go = "hangar", ids = new[] { "hangar" } },
            new Section { label = "Settings", go = "settings", ids = new[] { "settings", "tutorial" }, tabs = new[] { ("settings", "Settings"), ("tutorial", "Controls"), ("account", "Login") } },
        };

        static VisualElement Shell(GameApp app, string screen, VisualElement body)
        {
            var rail = E("rail");
            rail.Add(new Button(() => app.Go("menu")) { text = "‹ MENU" }.Cls("rail-brand"));
            foreach (var sec in SECTIONS)
            {
                var s = sec;
                var b = new Button(() => app.Go(s.go)) { text = s.label.ToUpperInvariant() }.Cls("rail-btn");
                if (s.ids.Contains(screen)) b.AddToClassList("current");
                rail.Add(b);
            }
            rail.Add(Spacer());
            rail.Add(CloudLine(app));
            rail.Add(T(app.Career.pilotName, "rail-foot"));
            var content = Scroll("content");
            var sec0 = SECTIONS.FirstOrDefault(x => x.ids.Contains(screen));
            var inner = E("page-wrap");
            if (app.SaveProblem != null) inner.Add(Panel(Row(Chip("Save", "caution"), T(app.SaveProblem))).With(p => p.style.borderTopColor = p.style.borderBottomColor = p.style.borderLeftColor = p.style.borderRightColor = (Color)new Color32(0xf2, 0xb5, 0x3c, 255)));
            if (sec0?.tabs != null)
            {
                var tabs = E("subtabs");
                foreach (var (id, l) in sec0.tabs) { var i = id; var b = new Button(() => app.Go(i)) { text = l.ToUpperInvariant() }.Cls("subtab"); if (screen == id) b.AddToClassList("current"); tabs.Add(b); }
                inner.Add(tabs);
            }
            inner.Add(body);
            content.Add(inner);
            // keep the scroll position across redraws of the same screen
            var key = "scroll:" + screen;
            if (app.ScreenState.TryGetValue(key, out var off) && off is Vector2 v) content.schedule.Execute(() => content.scrollOffset = v);
            content.verticalScroller.valueChanged += _ => app.ScreenState[key] = content.scrollOffset;
            return E("shell", rail, content);
        }

        static VisualElement Standalone(GameApp app, VisualElement page)
        {
            var s = Scroll("content");
            var wrap = E("standalone");
            wrap.style.alignItems = Align.Center;
            page.style.width = Length.Percent(100);
            page.style.maxWidth = 1360;
            wrap.Add(page);
            s.Add(wrap);
            return E("app", s);
        }

        // ------------------------------------------------------------------------------------ main menu
        static VisualElement MainMenu(GameApp app)
        {
            var c = app.Career;
            var menu = E("menu");
            var map = new MapView();
            map.AddToClassList("menu-map");
            var ex7 = Missions.ById("ex07_navigation");
            map.Options = new MapOptions { center = (-1500, -1500), metresPerPx = 11000.0 / 1400, route = MapView.RouteOf(ex7), labels = false };
            menu.Add(map);
            menu.Add(E("menu-shade"));
            var rec = c != null ? CareerManager.NextRecommendation(c) : null;
            var brand = E("menu-brand", E("menu-logo"), Up("Rotorcraft", "menu-title"),
                T("Learn to fly helicopters at Kinloch on the Scottish coast, earn your licences, and work towards offshore and rescue flying.", "menu-tag"));
            if (app.SaveProblem != null) brand.Add(T(app.SaveProblem, "err small"));
            brand.Add(T($"Version {Application.version}", "muted small menu-ver"));
            menu.Add(brand);
            var list = E("menu-list");
            if (c != null)
            {
                list.Add(Btn("Continue", "primary big", () => { if (rec != null && rec.kind != "none") OpenRec(app, rec); else app.Go("hq"); },
                    $"{(rec != null && rec.kind != "none" ? $"Next: {rec.title}" : "Career hub")} · {c.pilotName}, {CareerManager.RankOf(c)}"));
                list.Add(Btn("Career", "", () => app.Go("hq"), "Your pilot, lessons, missions and hangar"));
            }
            else list.Add(Btn("Start a career", "primary big", () => app.Go("new"), "From your first lesson, or as a qualified pilot"));
            list.Add(Btn("Freeplay", "", () => app.Go("freeplay"), "Pick a helicopter and take random callouts"));
            list.Add(Btn("Company", "", () => app.Go("company"), "Run your own operator: buy aircraft, win contracts, fly the jobs"));
            list.Add(Btn("Pilot area", "", () => app.Go("pilots"), "Leaderboards and other pilots"));
            list.Add(Btn(c != null ? "Settings" : "Sign in", "", () => app.Go(c != null ? "settings" : "account"), c != null ? "Controls, graphics, sign-in" : "Google login: your save on every device"));
            if (Application.platform != RuntimePlatform.WebGLPlayer) list.Add(Btn("Quit", "ghost", Application.Quit, "Back to the desktop"));   // a browser tab cannot quit
            list.Add(CloudLine(app));
            if (c != null) list.Add(Btn("Start a new career", "ghost small menu-new", () => app.Go("new")));
            menu.Add(list);
            return menu;
        }

        static void OpenRec(GameApp app, Recommendation r)
        {
            if (r.kind == "mission") app.Go(Route.Brief(r.id));
            else if (r.kind == "module") app.Go(Route.Module(r.id));
            else if (r.kind == "exam") app.Go(Route.Exam(r.id));
            else if (r.kind == "hangar") app.Go("hangar");
            else app.Go("hq");
        }

        // ------------------------------------------------------------------------------------ new career
        static VisualElement NewCareer(GameApp app)
        {
            var hasCareer = app.Career != null;
            var name = St(app, "name", "");
            var start = St(app, "start", CareerStart.Student);
            var confirm = St(app, "confirm", false);
            var importMsg = St<string>(app, "importMsg", null);
            void Submit()
            {
                if (hasCareer && !confirm) { Set(app, "confirm", true); return; }
                app.StartCareer(St(app, "name", ""), St(app, "start", CareerStart.Student));
            }
            var form = Panel(
                Placard("Kinloch Rotor School · Enrolment"),
                H2("New pilot career"),
                Seg(new[] { (CareerStart.Student, "Student pilot"), (CareerStart.Commercial, "Commercial pilot") }, start, v => Set(app, "start", v)),
                Muted(start == CareerStart.Student
                    ? "You start as a student with no qualifications. Your first lesson, Effects of Controls, begins straight after this."
                    : "Skip basic training: you arrive at Kinloch holding a PPL(H), CPL(H), night rating and Kestrel type rating, with 3 hours credited. Next: multi-engine type ratings, then offshore or rescue training."),
                Field("Pilot name", Text(name, 32, "e.g. Ailsa Munro", v => app.ScreenState["name"] = v).With(t => t.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Submit(); }))),
                hasCareer && confirm ? T("This replaces your existing career and its saved progress. Press Start again to confirm.", "err") : null,
                Row(Btn(hasCareer && confirm ? "Replace and start" : start == CareerStart.Student ? "Start training" : "Report for duty", "primary", Submit),
                    Btn("Back", "ghost", () => app.Go("menu"))),
                !FileDialog.Available ? null : Row(Btn("Load a save file", "small ghost", () =>
                {
                    var path = FileDialog.OpenJson("Load a Rotorcraft save");
                    if (path == null) return;
                    var c = app.Saves.ImportCareer(path);
                    if (c == null) { Set(app, "importMsg", "Not a Rotorcraft save."); return; }
                    app.CommitCareer(c); app.Go("hq");
                }), importMsg != null ? T(importMsg, "err") : null),
                CloudLine(app),
                Muted("A game. Qualifications earned here are simulated and confer no real-world privileges.", "small"));
            form.style.width = 500;
            var wrap = E("app");
            wrap.style.alignItems = Align.Center; wrap.style.justifyContent = Justify.Center;
            wrap.Add(form);
            return wrap;
        }

        // ------------------------------------------------------------------------------------------- HQ
        static VisualElement HQ(GameApp app)
        {
            var c = app.Career;
            var rec = CareerManager.NextRecommendation(c);
            var quals = Qualifications.ALL.Where(q => CareerManager.HasQual(c, q.id)).ToList();
            var chips = Row(Chip(CareerManager.RankOf(c), "nav"));
            foreach (var q in quals.Take(6)) chips.Add(Chip(q.abbrev, "ok"));
            if (quals.Count > 6) chips.Add(Chip($"+{quals.Count - 6}"));
            var page = E("page",
                E("page-head", Tight(Placard(CareerManager.MainEmployerName(c)), H1(c.pilotName), chips)));
            if (rec.kind != "none")
                page.Add(E("next big", Tight(Eyebrow("Your next step"), T(rec.title, "next-t"), Muted(rec.why)).With(s => { s.style.flexShrink = 1; s.style.marginRight = 14; }),
                    Btn(rec.kind == "mission" ? "Go to briefing" : rec.kind == "exam" ? "Sit exam" : rec.kind == "hangar" ? "Open hangar" : "Study", "primary", () => OpenRec(app, rec))));
            else page.Add(E("next big", Tight(Eyebrow("All caught up"), T("Pick any flight", "next-t")), Btn("Fly", "primary", () => app.Go("missions"))));
            var alerts = CurrencyAlerts(app);
            if (alerts != null) page.Add(alerts);
            var steps = CareerManager.MILESTONES.Select(m => (m, st: CareerManager.StateOf(c, m))).ToList();
            var current = steps.FirstOrDefault(x => x.st == MilestoneState.InProgress || x.st == MilestoneState.Retry || x.st == MilestoneState.Available);
            if (current.m == null) current = steps[steps.Count - 1];
            var strip = E("progress-strip");
            foreach (var (m, st) in steps)
            {
                var step = E("pstep " + (st == MilestoneState.Completed ? "completed" : ""), E("pstep-bar"), T(m.title, "pstep-l"));
                if (m == current.m) step.AddToClassList("cur");
                step.tooltip = $"{m.title}: {m.objective}";
                strip.Add(step);
            }
            page.Add(strip);
            page.Add(Panel(Stats(Stat(Hm(CareerManager.Hours(c) * 3600), "Flight hours"), Stat(c.stats.flights.ToString(), "Flights"),
                Stat(Money(c.money.balance), "Balance" + (c.money.loan > 0 ? $" · loan {Money(c.money.loan)}" : "")), Stat(quals.Count.ToString(), "Qualifications"))));
            page.Add(E("grid2", AircraftCard(app, rec), SchoolCard(app)));
            if (c.log.Count > 0)
            {
                var p = Panel(RowBetween(H3("Recent flights"), Btn("Logbook", "small ghost", () => app.Go("records"))));
                foreach (var l in c.log.Take(3)) p.Add(RowBetween(T(Missions.ById(l.missionId)?.shortTitle ?? l.title), Row(OutcomeChip(l.outcome), T($"{l.overall}%", "mono muted"))));
                page.Add(p);
            }
            return page;
        }

        /// <summary>The aircraft the next lesson or job is flown in, and its state.</summary>
        static VisualElement AircraftCard(GameApp app, Recommendation rec)
        {
            var c = app.Career;
            var acId = (rec.kind == "mission" ? Missions.ById(rec.id)?.aircraftId : null) ?? "kestrel";
            var ac = Aircraft.Get(acId);
            var a = CareerManager.Airframe(c, acId);
            var grounded = CareerManager.Serviceability(c, acId).Count > 0;
            return Panel(RowBetween(H3("Your aircraft"), Btn("Hangar", "small ghost", () => app.Go("hangar"))),
                AircraftPicture(acId),
                RowBetween(T(ac.name, "h2"), grounded ? Chip("Grounded", "warn") : Chip("Serviceable", "ok")),
                ScoreRow("Condition", a.condition, $"{a.condition:0}", a.condition < CareerManager.SERVICEABLE_MIN ? "w" : a.condition < 85 ? "c" : null));
        }

        /// <summary>Theory progress: modules and exams passed, and what to read next.</summary>
        static VisualElement SchoolCard(GameApp app)
        {
            var c = app.Career;
            bool Passed<TR>(Dictionary<string, TR> d, string id, Func<TR, bool> ok) => d.TryGetValue(id, out var r) && ok(r);
            var modules = Theory.MODULES.Count(m => Passed(c.modules, m.id, r => r.passed));
            var exams = Theory.EXAMS.Count(e => Passed(c.exams, e.id, r => r.passed));
            var next = Theory.MODULES.FirstOrDefault(m => !Passed(c.modules, m.id, r => r.passed));
            var p = Panel(RowBetween(H3("Ground school"), Btn("Open", "small ghost", () => app.Go("school"))),
                Stats(Stat($"{modules}/{Theory.MODULES.Count}", "Modules passed"), Stat($"{exams}/{Theory.EXAMS.Count}", "Exams passed")),
                Bar(100.0 * (modules + exams) / (Theory.MODULES.Count + Theory.EXAMS.Count)).With(b => b.style.flexGrow = 0),
                Spacer());   // the card is as tall as the aircraft beside it: keep the next module at the foot
            if (next != null)
            {
                var nm = next;
                p.Add(RowBetween(Tight(Eyebrow("Next to read"), T(next.title), Muted($"{next.minutes} min read · {next.quiz.Length}-question check", "small")),
                    Btn("Study", "small", () => app.Go(Route.Module(nm.id)))));
            }
            else p.Add(Muted("Every theory module passed. Exams open as your flying progresses."));
            return p;
        }

        static Label OutcomeChip(FlightOutcome o)
        {
            switch (o)
            {
                case FlightOutcome.Passed: return Chip("Passed", "ok");
                case FlightOutcome.Completed: return Chip("Completed", "ok");
                case FlightOutcome.Failed: return Chip("Not passed", "caution");
                case FlightOutcome.Aborted: return Chip("Incomplete");
                default: return Chip("Accident", "warn");
            }
        }

        /// <summary>Recurrent qualifications that are due or lapsed, for the HQ.</summary>
        static VisualElement CurrencyAlerts(GameApp app)
        {
            var c = app.Career;
            var alert = Qualifications.ALL.Where(q => Progression.VALIDITY_HOURS.ContainsKey(q.id) && CareerManager.HasQual(c, q.id))
                .Select(q => (q, cur: CareerManager.CurrencyOf(c, q.id), until: c.validity.TryGetValue(q.id, out var u) ? u : 0))
                .Where(i => i.cur == Currency.Due || i.cur == Currency.Expired).ToList();
            if (alert.Count == 0) return null;
            var p = Panel(H3("Recurrent checks"));
            foreach (var (q, cur, until) in alert)
            {
                var qq = q;
                p.Add(RowBetween(Row(T(q.abbrev), Muted("· " + q.name, "small")),
                    Row(cur == Currency.Expired ? Chip("Lapsed", "warn") : Chip($"Due in {until - CareerManager.Hours(c):F1} h", "caution"),
                        q.practicalTest != null ? Btn("Check", "small", () => app.Go(Route.Brief(qq.practicalTest))) : null)));
            }
            p.Add(Muted("A lapsed qualification blocks the work that needs it until you pass its check again. Validity runs in flight hours.", "small"));
            return p;
        }
    }
}
