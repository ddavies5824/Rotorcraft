// Ground school, exams, the mission board, the briefing and the debrief (port of src/ui/screens.tsx).
using System;
using System.Collections.Generic;
using System.Linq;
using Rotorcraft.Game;
using Rotorcraft.Sim;
using Rotorcraft.World;
using UnityEngine;
using UnityEngine.UIElements;
using static Rotorcraft.Presentation.UI.Ui;

namespace Rotorcraft.Presentation.UI
{
    public static partial class Screens
    {
        static readonly (string title, Pathway[] paths)[] SCHOOL_GROUPS =
        {
            ("Private pilot · Kestrel K2", new[] { Pathway.Ppl, Pathway.None }), ("Commercial pilot · Kestrel K2", new[] { Pathway.Cpl }), ("Night rating", new[] { Pathway.Night }),
            ("Multi-engine type ratings", new[] { Pathway.Ns175, Pathway.S9 }), ("Instrument rating · Northstar 175", new[] { Pathway.Ir }),
            ("Rescue training · Guardian S9", new[] { Pathway.Winch, Pathway.Sar }), ("Offshore checks · Northstar 175", new[] { Pathway.Offshore }),
        };

        // -------------------------------------------------------------------------------- ground school
        static VisualElement FlightSchool(GameApp app)
        {
            var c = app.Career;
            var list = E("list");
            foreach (var m in Theory.MODULES)
            {
                var mm = m;
                var r = c.modules.TryGetValue(m.id, out var x) ? x : null;
                list.Add(E("item",
                    E("item-main stack tight", T(m.title, "item-title"), E("item-meta", T($"{m.minutes} min read · {m.quiz.Length}-question check"),
                        r?.passed == true ? Chip($"Passed {r.bestPct:0}%", "ok") : r != null ? Chip($"Best {r.bestPct:0}%", "caution") : Chip("Not started"))),
                    Btn(r?.passed == true ? "Review" : "Study", "", () => app.Go(Route.Module(mm.id)))));
            }
            foreach (var ex in Theory.EXAMS)
            {
                var ee = ex;
                var a = CareerManager.ExamAvailability(c, ex.id);
                var r = c.exams.TryGetValue(ex.id, out var x) ? x : null;
                var item = E("item" + (a.available || r?.passed == true ? "" : " locked"),
                    E("item-main stack tight", T(ex.title, "item-title"), E("item-meta", T(ex.description),
                        r?.passed == true ? Chip($"Passed {r.bestPct:0}%", "ok") : r != null ? Chip($"Best {r.bestPct:0}% · {r.attempts} attempt{(r.attempts > 1 ? "s" : "")}", "caution") : null)),
                    Btn(r?.passed == true ? "Passed" : "Sit exam", "", () => app.Go(Route.Exam(ee.id)), disabled: !a.available));
                if (!a.available && r?.passed != true) item.Add(Reasons(a.reasons));
                list.Add(item);
            }
            return E("page", E("page-head", Tight(Placard("Kinloch Rotor School"), H1("Ground school")), Muted("Theory modules and exams. Flying lessons are under Fly.", "small")), list);
        }

        static VisualElement Quiz(GameApp app, IList<Question> questions, bool submitted, int?[] answers, bool[] correct, bool explainAll, Action<int, int> pick)
        {
            var box = E("stack");
            for (int i = 0; i < questions.Count; i++)
            {
                var q = questions[i]; var qi = i;
                var opts = E("opts");
                for (int j = 0; j < q.options.Length; j++)
                {
                    var oj = j;
                    var cls = "opt" + (answers[i] == j ? " on" : "") + (submitted ? (j == q.answer ? " right" : answers[i] == j ? " wrong" : "") : "");
                    var b = new Button(() => { if (!submitted) pick(qi, oj); }) { text = q.options[j] }.Cls(cls);
                    opts.Add(b);
                }
                var qe = E("q", T($"{i + 1}. {q.q}"), opts);
                if (submitted && (explainAll || !correct[i])) qe.Add(Muted(q.explain, "small"));
                box.Add(qe);
            }
            return box;
        }

        static VisualElement ModuleView(GameApp app, string moduleId)
        {
            var mod = Theory.ModuleById(moduleId);
            if (mod == null) return P("Unknown module.");
            var answers = St(app, "answers", new int?[mod.quiz.Length]);
            app.ScreenState["answers"] = answers;
            var result = St<(int pct, bool[] correct)?>(app, "result", null);
            var page = E("page", Tight(Placard($"Ground school · {mod.minutes} min"), H1(mod.title, true)));
            page.style.maxWidth = 780;
            foreach (var s in mod.sections) page.Add(Tight(H3(s.heading, true), P(s.body)));
            var quiz = Panel(H3($"Knowledge check · pass {mod.passMark:0}%"),
                Quiz(app, mod.quiz, result != null, answers, result?.correct, true, (i, j) => { answers[i] = j; app.Later(app.Refresh); }));
            if (result == null)
                quiz.Add(Row(Btn("Check answers", "primary", () =>
                {
                    var g = Theory.GradeAnswers(mod.quiz, answers);
                    app.CommitCareer(CareerManager.ApplyModuleQuiz(app.Career, mod.id, g.pct));
                    Set(app, "result", ((int, bool[])?)g);
                }, disabled: answers.Any(a => a == null))));
            else
            {
                var pct = result.Value.pct;
                quiz.Add(Row(Chip($"{pct}% · {(pct >= mod.passMark ? "Passed" : "Not passed")}", pct >= mod.passMark ? "ok" : "caution"),
                    pct < mod.passMark ? Btn("Try again", "", () => { app.ScreenState.Remove("result"); app.ScreenState["answers"] = new int?[mod.quiz.Length]; app.Later(app.Refresh); }) : null,
                    Btn("Back to Ground school", "primary", () => app.Go("school"))));
            }
            page.Add(quiz);
            return page;
        }

        /// <summary>An exam that cannot be sat yet: what it is, and the modules to pass first (each a step away).</summary>
        static VisualElement LockedExam(GameApp app, ExamDef ex)
        {
            var c = app.Career;
            var passed = c.exams.TryGetValue(ex.id, out var er) && er.passed;
            var list = E("list");
            foreach (var id in ex.prerequisites)
            {
                var m = Theory.ModuleById(id);
                if (m == null) continue;
                var done = c.modules.TryGetValue(id, out var r) && r.passed;
                list.Add(E("item", E("item-main stack tight", T(m.title, "item-title"), E("item-meta", T($"{m.minutes} min read · {m.quiz.Length}-question check"))),
                    done ? Chip("Passed", "ok") : Btn("Study", "small", () => app.Go(Route.Module(id)))));
            }
            var page = E("page",
                E("page-head", Tight(Placard($"{ex.questionCount} questions · pass mark {ex.passMark:0}%"), H1(ex.title, true)), passed ? Chip("Passed", "ok") : Chip("Locked", "caution")),
                P(ex.description),
                Panel(H3(passed ? "Already passed" : "Pass these modules first"), list),
                Row(Btn("Ground school", "", () => app.Go("school"))));
            page.style.maxWidth = 780;
            return page;
        }

        static VisualElement ExamView(GameApp app, string examId)
        {
            var c = app.Career;
            var ex = Theory.ExamById(examId);
            if (ex == null) return P("Unknown exam.");
            var attempt = (c.exams.TryGetValue(examId, out var er) ? er.attempts : 0) + 1;
            var result = St<(int pct, bool[] correct)?>(app, "result", null);
            var questions = St<List<Question>>(app, "questions", null);
            if (questions == null) { questions = Theory.ExamQuestions(ex, attempt * 7919 + c.createdAt % 1000); app.ScreenState["questions"] = questions; }
            var answers = St(app, "answers", new int?[questions.Count]);
            app.ScreenState["answers"] = answers;
            var avail = CareerManager.ExamAvailability(c, examId);
            if (!avail.available && result == null) return LockedExam(app, ex);
            var shownAttempt = result != null ? attempt - 1 : attempt;
            var page = E("page", Tight(Placard($"Attempt {shownAttempt} · pass {ex.passMark:0}%"), H1(ex.title, true)));
            page.style.maxWidth = 780;
            var p = Panel(Quiz(app, questions, result != null, answers, result?.correct, false, (i, j) => { answers[i] = j; app.Later(app.Refresh); }));
            if (result == null)
                p.Add(Row(Btn("Submit paper", "primary", () =>
                {
                    var g = Theory.GradeAnswers(questions, answers);
                    app.CommitCareer(CareerManager.ApplyExam(app.Career, examId, g.pct));
                    Set(app, "result", ((int, bool[])?)g);
                }, disabled: answers.Any(a => a == null)), Muted($"{answers.Count(a => a != null)}/{questions.Count} answered")));
            else
            {
                var pct = result.Value.pct;
                p.Add(Row(Chip($"{pct}% · {(pct >= ex.passMark ? "Passed" : "Not passed")}", pct >= ex.passMark ? "ok" : "caution")));
                p.Add(Muted(pct >= ex.passMark ? "Theory requirement met." : "Review the modules you found hardest and re-sit. Each attempt draws a different paper."));
                p.Add(Row(Btn("Back to Ground school", "primary", () => app.Go("school"))));
            }
            page.Add(p);
            return page;
        }

        // -------------------------------------------------------------------------------- mission board
        static VisualElement MissionList(GameApp app, IEnumerable<MissionDef> missions)
        {
            var c = app.Career;
            var list = E("list");
            foreach (var m in missions)
            {
                var mm = m;
                var a = CareerManager.MissionAvailability(c, m);
                var rec = c.exercises.TryGetValue(m.id, out var x) ? x : null;
                var passed = rec != null && rec.passes > 0;
                var chip = passed ? Chip($"Passed · best {rec.bestScore}%", "ok") : rec != null ? Chip($"{rec.attempts} attempt{(rec.attempts > 1 ? "s" : "")} · best {rec.bestScore}%", "caution")
                    : a.future ? Chip("Coming later") : a.available ? Chip("Available", "nav") : Chip("Locked");
                var item = E("item" + (a.available ? "" : " locked"),
                    E("item-main stack tight", T(m.title, "item-title"), E("item-meta", T($"{Aircraft.Get(m.aircraftId).name} · ~{m.estMinutes:0} min · {Weather.Presets[m.weatherId].name}"), chip), Muted(m.summary, "small")),
                    Btn(passed ? "Fly again" : "Brief", a.available && !passed ? "primary" : "", () => app.Go(Route.Brief(mm.id)), disabled: !a.available && !(passed && m.objectives.Length > 0)));
                if (!a.available && a.reasons.Count > 0) item.Add(Reasons(a.reasons));
                list.Add(item);
            }
            return list;
        }

        static VisualElement MissionBoard(GameApp app)
        {
            var c = app.Career;
            var f = St(app, "filter", "next");
            var showLocked = St(app, "locked", false);
            bool Training(MissionDef m) => m.category == MissionCategory.Training || m.category == MissionCategory.Test || m.category == MissionCategory.Practice;
            bool Avail(MissionDef m) => CareerManager.MissionAvailability(c, m).available;
            bool Passed(MissionDef m) => CareerManager.PassedExercise(c, m.id);
            var list = Missions.All.Where(m => m.objectives.Length > 0 && (
                f == "next" ? Avail(m) && !Passed(m) && m.category != MissionCategory.Practice
                : f == "done" ? Passed(m)
                : (f == "training" ? Training(m) : !Training(m)) && (showLocked || Avail(m) || Passed(m)))).ToList();
            List<(string, List<MissionDef>)> groups;
            if (f == "next" || f == "done") groups = new List<(string, List<MissionDef>)> { ("", list) };
            else if (f == "training")
            {
                groups = SCHOOL_GROUPS.Select(g => (g.title, list.Where(m => m.category != MissionCategory.Practice && g.paths.Contains(m.pathway)).ToList())).ToList();
                groups.Add(("Practice", list.Where(m => m.category == MissionCategory.Practice).ToList()));
            }
            else groups = new List<(string, List<MissionDef>)>
            {
                ("Search and rescue", list.Where(m => m.category == MissionCategory.Sar).ToList()), ("Offshore", list.Where(m => m.category == MissionCategory.Offshore).ToList()),
                ("Air ambulance", list.Where(m => m.category == MissionCategory.Medical).ToList()), ("Utility", list.Where(m => m.category == MissionCategory.Utility).ToList()),
                ("Private flying", list.Where(m => m.category == MissionCategory.Private).ToList()),
            };
            var page = E("page", E("page-head", H1("Fly"), Btn("Freeplay ›", "small", () => app.Go("freeplay"))),
                Row(Seg(new[] { ("next", "Up next"), ("training", "Lessons"), ("work", "Work"), ("done", "Flown") }, f, v => { app.ScreenState["filter"] = v; app.Later(app.Refresh); }),
                    f == "training" || f == "work" ? Check("Show locked", showLocked, v => Set(app, "locked", v)).With(e => e.style.borderBottomWidth = 0) : null));
            if (list.Count == 0) page.Add(Muted(f == "next" ? "Nothing new right now: study in Ground school, or fly a lesson again for practice." : f == "done" ? "Nothing flown yet." : "Nothing available here yet. Tick “Show locked” to see what is coming and what it needs."));
            foreach (var (t, ms) in groups.Where(g => g.Item2.Count > 0)) page.Add(Stack(string.IsNullOrEmpty(t) ? null : H3(t), MissionList(app, ms)));
            return page;
        }

        // ------------------------------------------------------------------------------------- briefing
        public static (double cruiseBurn, double tripKg, double requiredKg) PlanFuel(MissionDef m, AircraftConfig ac)
        {
            var cruiseBurn = ac.idleBurnKgH + (ac.fuelBurnKgH - ac.idleBurnKgH) * 0.75;
            var tripKg = cruiseBurn * m.estMinutes / 60;
            return (cruiseBurn, tripKg, tripKg + m.minFuelKg);
        }

        public static MapOptions RouteMap(MissionDef m, float w, float h)
        {
            var route = MapView.RouteOf(m);
            double minX = route.Min(p => p.x), maxX = route.Max(p => p.x), minZ = route.Min(p => p.z), maxZ = route.Max(p => p.z);
            var span = Math.Max(1200, Math.Max(maxX - minX, (maxZ - minZ) * w / h)) * 1.5;
            return new MapOptions
            {
                route = route, center = ((maxX + minX) / 2, (maxZ + minZ) / 2), metresPerPx = span / w, aircraft = (route[0].x, route[0].z, m.startHeadingDeg),
                areas = (m.searchAreas ?? new SearchAreaDef[0]).Select(a => (Search.AreaCorners(a), a.name)).ToList(),
                found = (m.targets ?? new TargetDef[0]).Where(t => t.areaId == null && t.x != null && t.z != null).Select(t => (t.x.Value, t.z.Value, t.name)).ToList(),
            };
        }

        static VisualElement Briefing(GameApp app, string missionId)
        {
            var c = app.Career;
            var m = Missions.ById(missionId);
            if (m == null) return P("Unknown mission.");
            var ac = Aircraft.Get(m.aircraftId);
            var plan = PlanFuel(m, ac);
            var fuel = St(app, "fuel", Math.Round(ac.fuelCapacity * m.defaultFuelFrac));
            var tod = St(app, "tod", m.timeOfDay);
            var wxId = St(app, "wx", m.weatherId);
            var a = CareerManager.MissionAvailability(c, m);
            var wx = Weather.Presets.TryGetValue(wxId, out var w0) ? w0 : Weather.Presets[m.weatherId];
            var mass = ac.emptyMass + ac.crewMass + m.payloadKg + fuel;
            var overweight = mass > ac.maxGross;
            var hoge = FlightModel.HoverPowerPct(ac, mass);
            var enduranceMin = fuel / plan.cruiseBurn * 60;
            var fuelShort = fuel < plan.requiredKg;
            var start = WorldMap.SiteById(m.startSiteId);
            var legs = MapView.RouteOf(m);
            double dist = 0; for (int i = 1; i < legs.Count; i++) dist += WorldMap.BearingDistance(legs[i - 1].x, legs[i - 1].z, legs[i].x, legs[i].z).dist;
            var unserviceable = CareerManager.Serviceability(c, m.aircraftId);
            var af = CareerManager.Airframe(c, m.aircraftId);
            var pf = CareerManager.PowerFactor(c, m.aircraftId);
            var canFly = (a.available || (CareerManager.PassedExercise(c, m.id) && !a.future)) && unserviceable.Count == 0;

            var left = E("col stack");
            var bp = Panel(H3("Briefing"));
            foreach (var para in m.briefing) bp.Add(P(para));
            left.Add(bp);
            var ol = E("stack tight");
            for (int i = 0; i < m.objectives.Length; i++) ol.Add(E("obj-row", T($"{i + 1}", "obj-n"), T(m.objectives[i].label, "obj-l"), Up(m.objectives[i].phase, "obj-ph")));
            var mins = m.assessment.minimums != null && m.assessment.minimums.Count > 0 ? " · minimums: " + string.Join(", ", m.assessment.minimums.Select(kv => $"{CATEGORY_LABELS[kv.Key].ToLowerInvariant()} {kv.Value:0}%")) : "";
            left.Add(Panel(H3("Objectives"), ol, Muted($"{(m.assessment.passMark == 0 ? "Unassessed: scores are shown for feedback only" : $"Pass mark {m.assessment.passMark:0}%")}{mins}{(m.assessment.noExceedances ? " · no limit exceedances" : "")}{(m.assessment.maxHardLandings == 0 ? " · no hard landings" : "")}.", "small")));
            var risks = Panel(H3("Risks"));
            foreach (var r in m.risks) risks.Add(T("•  " + r));
            left.Add(risks);

            var right = E("col stack");
            var wp = Panel(H3($"Weather · {wx.name}"));
            if ((m.timeOptions?.Length ?? 0) > 1) wp.Add(Row(Muted("Time", "small"), Seg(m.timeOptions.Select(t => (t, Weather.TimeLabels[t])), tod, v => Set(app, "tod", v))));
            if (m.weatherOptions != null) wp.Add(Field("Conditions", Seg(m.weatherOptions.Select(x => (x, Weather.Presets[x].name)), wxId, v => Set(app, "wx", v))));
            if (tod != TimeOfDay.Day) wp.Add(Muted(tod == TimeOfDay.Dusk ? "Sunset was 15 minutes ago. Position lights and beacon on before start; the pads and village are lit." : "Full night, moon high in the south-east. Fly by the lights and your instruments. Landing light on for every approach.", "small"));
            wp.Add(T(Weather.Metar(wx), "metar"));
            if (m.weatherTimeline != null) foreach (var fc in m.weatherTimeline) wp.Add(Row(Chip("Forecast", "caution"), Muted($"{(Weather.Presets.TryGetValue(fc.id, out var fw) ? fw.name : fc.id)} possible from about {fc.min:0} min into the flight.", "small")));
            wp.Add(Kv(("Wind", $"{wx.windDirDeg:000}° {wx.windKt:0} kt{(wx.gustKt >= 4 ? $", gusts {wx.windKt + wx.gustKt:0} kt" : "")}"),
                ("Visibility", wx.visibilityM >= 10000 ? "10 km or more" : $"{wx.visibilityM / 1000:F1} km"),
                ("Cloud", wx.cloudBaseFt != null ? $"{wx.cloudCover} {wx.cloudBaseFt:0} ft" : "No significant cloud"),
                ("Temp / QNH", $"{wx.oatC:0} °C · {wx.qnh:0} hPa")));
            right.Add(wp);
            var mapBox = new MapView().Cls("map-box");
            mapBox.RegisterCallback<GeometryChangedEvent>(_ => { var r = mapBox.contentRect; if (r.width > 10) { mapBox.Options = RouteMap(m, r.width, r.height); mapBox.Redraw(); } });
            right.Add(Panel(H3("Route"), mapBox, Kv(("Depart", $"{start.name} · {Math.Round(WorldMap.SiteElevation(start) * SimMath.FT)} ft"), ("Distance", $"{dist / 1852:F1} nm"), ("Est. time", $"{m.estMinutes:0} min"))));
            var fp = Panel(H3("Fuel and mass"),
                Slider($"Fuel load · of {ac.fuelCapacity:0} kg", (float)Math.Round(ac.fuelCapacity * 0.15), (float)ac.fuelCapacity, (float)fuel, v => $"{v:0} kg", v => Set(app, "fuel", (double)Math.Round(v)), 1),
                Kv(("Required", $"{plan.requiredKg:0} kg (trip {plan.tripKg:0} + reserve {m.minFuelKg:0})", fuelShort ? "err" : null),
                    ("Endurance", $"{enduranceMin:0} min at cruise", null),
                    ("Take-off mass", $"{mass:0} / {ac.maxGross:0} kg", overweight ? "err" : null),
                    ("Hover power", $"{hoge:0}% torque out of ground effect", hoge > 95 ? "err" : null),
                    ("Aircraft", (unserviceable.Count > 0 ? "Unserviceable" : $"Condition {af.condition:0}% · {CareerManager.INSPECTION_INTERVAL_H - af.hoursSinceInspection:F1} h to inspection") + (pf < 1 ? $" · engine power {pf * 100:0}%" : ""), unserviceable.Count > 0 ? "err" : null)));
            if (overweight) fp.Add(T("Over maximum take-off mass. Reduce fuel.", "err"));
            else if (fuelShort) fp.Add(T("Below planned fuel. You may depart, but expect a fuel-management penalty if you land under reserve.", "caution-t"));
            right.Add(fp);
            if (m.manifest != null)
                right.Add(Panel(H3("Manifest"), Table(new[] { "Leg", "Passengers", "Payload" }, new[] { 3f, 1, 1 }, m.manifest.Select(l => new VisualElement[] { Cell(l.leg), Cell(l.pax.ToString(), true), Cell($"{l.kg:0} kg", true) }), new[] { false, true, true }),
                    Muted("Payload changes at each stop. Check hover power before lifting from a deck.", "small")));
            if (m.casualty != null) right.Add(Panel(H3("Casualty"), Kv(("Condition", m.casualty.condition, "body"), ("Target", $"Hospital within {m.casualty.goldenMin:0} min", null))));
            right.Add(Panel(H3("Contract and costs"), BriefingMoney(app, m)));

            var page = E("page",
                E("page-head", Tight(Placard($"{m.employer} · Briefing"), H1(m.title, true)), Chip(ac.name)),
                E("cols", left, right),
                E("footerbar",
                    Row(Muted("Handling"), Seg(new[] { (AssistMode.Beginner, "Beginner"), (AssistMode.Standard, "Standard"), (AssistMode.Realistic, "Realistic") }, app.Settings.assist, v => { var s = app.Settings.Clone(); s.assist = v; app.CommitSettings(s); app.Later(app.Refresh); })),
                    Row(Muted("Pre-flight checks are done in the aircraft", "small"), Btn("Back", "ghost", () => app.Go("missions")),
                        Btn("Go to the aircraft", "primary", () => app.FlyMission(m.id, fuel, tod, wxId), disabled: !canFly || overweight || m.objectives.Length == 0))));
            if (!canFly)
                page.Add(Panel(Row(Chip("Not yet available", "caution"), Muted(string.Join(" · ", a.reasons.Concat(unserviceable).Distinct())),
                    unserviceable.Count > 0 ? Btn("Hangar", "small", () => app.Go("hangar")) : null)));
            return page;
        }

        static VisualElement BriefingMoney(GameApp app, MissionDef m)
        {
            var c = app.Career;
            var e = Employers.EmployerOf(m);
            var st = CareerManager.StandingWith(c, e);
            var pay = Progression.OfferedPay(m, st);
            var ac = Aircraft.Get(m.aircraftId);
            var selfHire = (m.category == MissionCategory.Training || m.category == MissionCategory.Private || m.category == MissionCategory.Practice || (m.category == MissionCategory.Test && e == "school")) && ac.engines == 1;
            var est = selfHire ? Math.Round(ac.operatingCostPerHour * m.estMinutes / 60 / 5) * 5 : 0;
            var maint = CareerManager.MaintenanceCost(c, m.aircraftId);
            return Kv(("Operator", $"{Employers.ById(e)?.name} · standing {st}", "body"),
                pay > 0 ? ("Contract fee", $"up to {Money(pay)} (scaled by your result)", (string)null) : (null, null, null),
                est > 0 ? ("Aircraft hire", $"about {Money(est)} ({Money(ac.operatingCostPerHour)}/h)", null) : (null, null, null),
                m.category == MissionCategory.Test && e == "school" ? ("Examiner fee", "£350", null) : (null, null, null),
                ("Balance", Money(c.money.balance) + (c.money.loan > 0 ? $" · loan {Money(c.money.loan)}" : ""), null),
                maint > 0 ? ("Maintenance", $"{Money(maint)} if done now", null) : (null, null, null));
        }

        // -------------------------------------------------------------------------------------- debrief
        static VisualElement Debrief(GameApp app, Route route)
        {
            var c = app.Career;
            var r = route.result;
            var update = route.update;
            var m = Missions.ById(r.missionId);
            var cls = r.outcome == FlightOutcome.Passed || r.outcome == FlightOutcome.Completed ? "pass" : r.outcome == FlightOutcome.Aborted ? "neutral" : "fail";
            var label = r.outcome == FlightOutcome.Passed ? "Passed" : r.outcome == FlightOutcome.Completed ? "Completed" : r.outcome == FlightOutcome.Failed ? "Not passed" : r.outcome == FlightOutcome.Aborted ? "Incomplete" : "Accident";
            var rec = CareerManager.NextRecommendation(c);
            var page = E("page", Tight(Placard("Debrief"), H1(m.title, true)),
                E("outcome " + cls,
                    Tight(Up(label, "outcome-big"), Muted(r.crashReason ?? (r.failedCriteria.FirstOrDefault() ?? (r.pass ? "All objectives achieved within the pass criteria." : "")))),
                    Stats(Stat($"{r.overall}%", "Overall"), Stat(Ms(r.flightSeconds), "Airborne min:s"), Stat($"+{r.xp}", "XP"))));
            if (update != null && (update.awarded.Count > 0 || update.milestones.Count > 0 || update.remedial.Count > 0))
            {
                var p = Panel(H3("Career"));
                foreach (var q in update.awarded) p.Add(Row(Chip("Awarded", "ok"), T(Qualifications.ById(q)?.name, "b")));
                foreach (var id in update.milestones) p.Add(Row(Chip("Milestone", "nav"), T(CareerManager.MILESTONES.FirstOrDefault(x => x.id == id)?.title)));
                if (update.remedial.Count > 0) p.Add(Row(Chip("Remedial", "caution"), T("Before re-sitting: " + string.Join(", ", update.remedial.Select(x => Missions.ById(x)?.shortTitle)))));
                page.Add(p);
            }
            if (update != null) { var dc = DebriefCareer(update); if (dc != null) page.Add(dc); }
            if (update != null && update.messages.Any(x => x.StartsWith("Result rejected"))) page.Add(Panel(Row(Chip("Not recorded", "warn"), T(update.messages[0]))));
            var assess = Panel(H3("Assessment"));
            foreach (var kv in r.scores)
            {
                double? min = m.assessment.minimums != null && m.assessment.minimums.TryGetValue(kv.Key, out var mv) ? mv : (double?)null;
                assess.Add(ScoreRow(CATEGORY_LABELS[kv.Key], kv.Value, kv.Value.ToString(), kv.Value < 50 ? "w" : kv.Value < 70 ? "c" : null, min != null ? $"min {min:0}" : null));
            }
            foreach (var f in r.failedCriteria) assess.Add(T("✕ " + f, "err"));
            var objs = Panel(H3("Objectives"));
            for (int i = 0; i < r.objectives.Count; i++)
            {
                var o = r.objectives[i];
                var line = E("obj-row", T($"{i + 1}", "obj-n"), Tight(T(o.label), string.IsNullOrEmpty(o.detail) ? null : Muted(o.detail, "small")).With(s => s.style.flexGrow = 1),
                    o.status == ObjectiveStatus.Complete ? Chip("Done", "ok") : o.status == ObjectiveStatus.Failed ? Chip("Missed", "warn") : Chip("Not reached"));
                objs.Add(line);
            }
            page.Add(E("grid2", assess, objs));
            var lp = Panel(H3("Landings"));
            if (r.landings.Count == 0) lp.Add(Muted("No touchdowns recorded."));
            else lp.Add(Table(new[] { "Grade", "Descent", "Groundspeed", "From spot", "Attitude" }, new[] { 1.3f, 1, 1.1f, 1, 1 },
                r.landings.Select(l => new VisualElement[] { Chip($"{Assessment.LandingGrade(l.score)} · {l.score}", l.score >= 78 ? "ok" : l.score >= 50 ? "caution" : "warn"), Cell($"{l.vsMs * 196.85:0} fpm", true), Cell($"{l.gsMs * 1.944:F1} kt", true), Cell(l.distM == null ? "—" : $"{l.distM:F1} m", true), Cell($"{Math.Abs(l.roll):0}° / {Math.Abs(l.pitch):0}°", true) }),
                new[] { false, true, true, true, true }));
            var afNow = CareerManager.Airframe(c, r.aircraftId);
            lp.Add(Kv(("Fuel used", $"{r.fuelUsedKg:F1} kg · {r.fuelRemainingKg:F1} kg remaining"), ("Distance", $"{r.distanceKm / 1.852:F1} nm · max {r.maxAltFt:0} ft"),
                ("Aircraft", $"{(r.damage > 0.01 ? $"Damage {r.damage * 100:0}% · " : "")}condition now {afNow.condition:0}%{(CareerManager.Serviceability(c, r.aircraftId).Count > 0 ? " · maintenance needed" : "")}")));
            var notes = Panel();
            if ((r.decisions?.Count ?? 0) > 0 || (r.rescue?.Count ?? 0) > 0)
            {
                notes.Add(H3((r.rescue?.Count ?? 0) > 0 ? "Rescue and decisions" : "Decisions"));
                foreach (var d in r.decisions ?? new List<DecisionRecord>()) notes.Add(Row(T(d.good ? "✓" : "✕", d.good ? "ok-t" : "err"), T(d.chosen)));
                foreach (var x in r.rescue ?? new List<string>()) notes.Add(T("•  " + x));
            }
            notes.Add(H3("Instructor's notes"));
            foreach (var x in r.recommendations) notes.Add(T("•  " + x));
            if (r.exceedances.Count + r.warnings.Count > 0) { notes.Add(H3("Errors and warnings")); foreach (var x in r.exceedances.Concat(r.warnings)) notes.Add(T("•  " + x)); }
            page.Add(E("grid2", lp, notes));
            var row = Row(Btn("Fly again", "", () => app.Go(Route.Brief(r.missionId))));
            if (rec.kind == "mission" && rec.id != r.missionId) row.Add(Btn($"Next: {Missions.ById(rec.id)?.shortTitle}", "primary", () => app.Go(Route.Brief(rec.id))));
            if (rec.kind == "module") row.Add(Btn($"Next: {Theory.ModuleById(rec.id)?.title}", "primary", () => app.Go(Route.Module(rec.id))));
            if (rec.kind == "exam") row.Add(Btn($"Next: {Theory.ExamById(rec.id)?.title}", "primary", () => app.Go(Route.Exam(rec.id))));
            if (rec.kind == "hangar") row.Add(Btn($"Hangar: {rec.title}", "primary", () => app.Go("hangar")));
            row.Add(Btn("HQ", "ghost", () => app.Go("hq")));
            page.Add(row);
            return page;
        }

        /// <summary>Debrief section: money and reputation changes from this flight.</summary>
        static VisualElement DebriefCareer(CareerUpdate u)
        {
            var led = u.ledger ?? new List<Ledger>();
            var emp = u.employer != null ? Employers.ById(u.employer) : null;
            if (led.Count == 0 && emp == null) return null;
            var p = Panel(H3("Pay and reputation"));
            if (led.Count > 0) p.Add(Kv(led.Select(l => (l.label, l.amount == 0 ? "" : Money(l.amount), l.amount < 0 ? "err" : null)).ToArray()));
            else p.Add(Muted("No charges or fees for this flight."));
            if (emp != null)
            {
                var d = (u.standingAfter ?? 0) - (u.standingBefore ?? 0);
                p.Add(Row(T($"{emp.name}: standing {u.standingBefore} →"), T($"{u.standingAfter}", "b"), T($"({(d >= 0 ? "+" : "")}{d})", d < 0 ? "err" : d > 0 ? "ok-t" : ""), u.repNotes?.Count > 0 ? Muted("· " + string.Join(", ", u.repNotes)) : null));
            }
            if (u.renewed != null && u.renewed.Count > 0) p.Add(Row(Chip("Revalidated", "ok"), T(string.Join(", ", u.renewed.Select(q => Qualifications.ById(q)?.abbrev)))));
            return p;
        }
    }
}
