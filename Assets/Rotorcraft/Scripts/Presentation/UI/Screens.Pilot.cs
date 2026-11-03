// Licences, employers, the hangar, the logbook, settings and the controls guide (port of src/ui/screens.tsx and
// careerScreens.tsx).
using System;
using System.Collections.Generic;
using System.Linq;
using Rotorcraft.Game;
using Rotorcraft.Inputs;
using Rotorcraft.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static Rotorcraft.Presentation.UI.Ui;

namespace Rotorcraft.Presentation.UI
{
    public static partial class Screens
    {
        // ------------------------------------------------------------------------------- qualifications
        static VisualElement QualificationsView(GameApp app)
        {
            var c = app.Career;
            var open = St<string>(app, "open", null);
            var page = E("page", E("page-head", H1("Licences & Ratings"), Muted("Simulated qualifications. No real-world privileges.", "small")));
            foreach (var (cat, title) in new[] { (QualCategory.Licence, "Licences"), (QualCategory.Rating, "Ratings"), (QualCategory.Type, "Type ratings"), (QualCategory.Authorisation, "Employer authorisations") })
            {
                var list = E("list");
                foreach (var q in Qualifications.ALL.Where(x => x.category == cat))
                {
                    var qq = q;
                    var held = c.qualifications.TryGetValue(q.id, out var h) ? h : null;
                    var el = CareerManager.QualificationEligible(c, q.id);
                    var future = q.contentMilestone > Qualifications.CURRENT_MILESTONE;
                    var met = el.items.Count(i => i.met);
                    var cur = CareerManager.CurrencyOf(c, q.id);
                    var chip = held != null ? (cur == Currency.Expired ? Chip("Lapsed: recurrent check needed", "warn") : cur == Currency.Due ? Chip("Recurrent check due", "caution") : Chip($"Held since {Date(held.awardedAt)}", "ok"))
                        : future ? Chip($"Training arrives in Milestone {q.contentMilestone}") : Chip($"{met}/{el.items.Count} requirements met", "nav");
                    var item = E("item" + (held == null && future ? " locked" : ""),
                        E("item-main stack tight", Row(T(q.name, "item-title"), T(q.abbrev, "muted mono small")), E("item-meta", chip, T(q.issuedBy))),
                        Btn(open == q.id ? "Hide" : "Details", "small", () => Set(app, "open", open == qq.id ? null : qq.id)));
                    if (open == q.id)
                    {
                        var d = E("stack").With(x => { x.style.width = Length.Percent(100); x.style.marginTop = 8; });
                        d.Add(P(q.description));
                        d.Add(Kv(("Pass criteria", q.passCriteria, "body"), ("Restrictions", q.restrictions, "body"), ("Unlocks", q.unlocks, "body"), q.recurrent != null ? ("Recurrent", q.recurrent, "body") : (null, null, null)));
                        var reqs = E("stack tight");
                        foreach (var i in el.items) reqs.Add(T((i.met ? "✓ " : "○ ") + i.label, i.met ? "ok-t" : "muted"));
                        d.Add(reqs);
                        if (q.practicalTest != null && Missions.ById(q.practicalTest) != null && (held == null || cur == Currency.Due || cur == Currency.Expired))
                            d.Add(Row(Btn(held != null ? "Fly the recurrent check" : $"Brief: {Missions.ById(q.practicalTest).shortTitle}", "small", () => app.Go(Route.Brief(qq.practicalTest)))));
                        item.Add(d);
                    }
                    list.Add(item);
                }
                page.Add(Stack(H3(title), list));
            }
            return page;
        }

        // ------------------------------------------------------------------------------------ employers
        static VisualElement RepBars(Career c, string id)
        {
            var r = CareerManager.RepWith(c, id);
            var box = E("stack tight");
            foreach (var k in Progression.REP_KEYS)
            {
                var v = Progression.Get(r, k);
                box.Add(ScoreRow(Progression.REP_LABELS[k], v, Math.Round(v).ToString(), v < 35 ? "w" : v < 50 ? "c" : null));
            }
            return box;
        }

        static VisualElement EmployersView(GameApp app)
        {
            var c = app.Career;
            var grid = E("grid2");
            foreach (var e in Employers.ALL)
            {
                var missing = e.requires.Where(q => !CareerManager.HasQual(c, q)).ToList();
                var work = Missions.All.Where(m => Employers.EmployerOf(m) == e.id && m.category != MissionCategory.Test && m.category != MissionCategory.Training && m.category != MissionCategory.Practice).ToList();
                var st = CareerManager.StandingWith(c, e.id);
                var flown = c.reputation.ContainsKey(e.id);
                var p = Panel(RowBetween(H2(e.name), missing.Count > 0 ? Chip("Needs " + string.Join(", ", missing.Select(q => Qualifications.ById(q)?.abbrev ?? q))) : Chip($"Standing {st}", "ok")),
                    Placard(e.kind), Muted(e.description),
                    flown ? RepBars(c, e.id) : Muted("No flights for this operator yet. Everyone starts at 50.", "small"));
                if (work.Count > 0)
                {
                    var list = E("stack tight");
                    foreach (var m in work)
                    {
                        var mm = m;
                        var a = CareerManager.MissionAvailability(c, m);
                        var pay = Progression.OfferedPay(m, st);
                        list.Add(RowBetween(T(m.shortTitle + (CareerManager.PassedExercise(c, m.id) ? " ✓" : "")),
                            Row(pay > 0 ? Chip(Money(pay), "nav") : null, Btn(a.available ? "Brief" : "Locked", "small", () => app.Go(Route.Brief(mm.id)), disabled: !a.available))));
                    }
                    p.Add(list);
                }
                grid.Add(p);
            }
            var senior = E("stack tight");
            foreach (var i in CareerManager.SeniorStatus(c).items) senior.Add(T((i.met ? "✓ " : "○ ") + i.label, i.met ? "ok-t" : "muted"));
            return E("page", E("page-head", H1("Employers"), Muted("Fictional operators. Standing weighs safety twice as heavily as speed.", "small")), grid, Panel(H3("Senior Pilot"), senior));
        }

        // --------------------------------------------------------------------------------------- hangar
        /// <summary>Room for "0.0/2.5 h" on one line, the same in both rows so the bars line up.</summary>
        static void WideValue(VisualElement row) => row.Q<Label>(className: "scorerow-v").With(v => { v.style.width = 80; v.style.whiteSpace = WhiteSpace.NoWrap; });

        static VisualElement AirframePanel(GameApp app, string acId)
        {
            var c = app.Career;
            var a = CareerManager.Airframe(c, acId);
            var issues = CareerManager.Serviceability(c, acId);
            var due = a.hoursSinceInspection / CareerManager.INSPECTION_INTERVAL_H;
            var cost = CareerManager.MaintenanceCost(c, acId);
            var box = E("stack tight").With(x => { x.style.borderTopWidth = 1; x.style.borderTopColor = (Color)new Color32(0x2c, 0x3a, 0x44, 255); x.style.paddingTop = 8; });
            box.Add(ScoreRow("Condition", a.condition, $"{a.condition:0}", a.condition < CareerManager.SERVICEABLE_MIN ? "w" : a.condition < 85 ? "c" : null).With(WideValue));
            box.Add(ScoreRow("Inspection", Math.Min(100, due * 100), $"{a.hoursSinceInspection:F1}/{CareerManager.INSPECTION_INTERVAL_H} h", due >= 1 ? "w" : due > 0.8 ? "c" : null).With(WideValue));
            if (a.lastEvent != null) box.Add(Muted("Tech log: " + a.lastEvent, "small"));
            var pf = CareerManager.PowerFactor(c, acId);
            if (pf < 1) box.Add(Muted($"Worn engine: {pf * 100:0}% of rated power until maintained.", "small"));
            box.Add(Row(issues.Count > 0 ? Chip("Grounded", "warn") : Chip("Serviceable", "ok"),
                Btn((issues.Count > 0 ? "Carry out maintenance" : "Inspect now") + (cost > 0 ? $" · {Money(cost)}" : ""), "small", () => { app.CommitCareer(CareerManager.ApplyMaintenance(app.Career, acId)); app.Later(app.Refresh); }, disabled: a.condition >= 99.5 && a.hoursSinceInspection < 0.05)));
            box.Add(Muted("Hard landings, limit exceedances and flight hours wear the airframe. You pay for work on the school's Kestrel; operators maintain their own twins.", "small"));
            return box;
        }

        static string EquipmentText(Equipment e)
        {
            var l = new List<string>();
            if (e.radalt) l.Add("radar altimeter"); if (e.hoist) l.Add("rescue winch"); if (e.flir) l.Add("FLIR"); if (e.searchlight) l.Add("searchlight"); if (e.floats) l.Add("emergency floats"); if (e.autopilot) l.Add("autopilot");
            return l.Count > 0 ? string.Join(", ", l) : "Basic VFR instruments";
        }

        static VisualElement Hangar(GameApp app)
        {
            var c = app.Career;
            var grid = E("grid2");
            foreach (var ac in Aircraft.All.Values)
            {
                var pic = ac.requiredQualifications.All(q => CareerManager.HasQual(c, q));
                var dual = ac.trainingQualifications != null && ac.trainingQualifications.All(q => CareerManager.HasQual(c, q));
                var missing = ac.requiredQualifications.Where(q => !CareerManager.HasQual(c, q)).Select(q => Qualifications.ById(q)?.abbrev ?? q).ToList();
                var p = Panel(RowBetween(H2(ac.name), pic ? Chip("Rated", "ok") : dual ? Chip("Dual training", "nav") : Chip("Locked")),
                    AircraftPicture(ac.id),
                    Placard(ac.realWorldClass), Muted(ac.description),
                    Kv(("Engines", $"{ac.engines} · {ac.ratedPowerKW:0} kW"), ("Max mass", $"{N0(ac.maxGross)} kg"), ("Fuel", $"{N0(ac.fuelCapacity)} kg · {ac.fuelBurnKgH:0} kg/h max"),
                        ("Rotor", $"{ac.rotorDiameter} m · VNE {ac.vneKt:0} kt"), ("Equipment", EquipmentText(ac.equipment)), ("Cost", $"£{N0(ac.operatingCostPerHour)}/h")),
                    AirframePanel(app, ac.id));
                if (!pic) p.Add(Muted(dual ? $"Flown with an instructor or training captain until you hold {string.Join(", ", missing)}." : $"Training needs {string.Join(", ", (ac.trainingQualifications ?? new string[0]).Select(q => Qualifications.ById(q)?.abbrev ?? q).DefaultIfEmpty("nothing"))}; pilot-in-command needs {string.Join(", ", missing)}.", "small"));
                grid.Add(p);
            }
            return E("page", E("page-head", H1("Hangar"), Muted("Simulated types, not licensed reproductions of real products. The Peregrine is modelled on the AS365 N3 Dauphin.", "small")), grid);
        }

        /// <summary>A picture of the aircraft (rendered from the model at start-up), or nothing if none yet.</summary>
        static VisualElement AircraftPicture(string acId)
        {
            var tex = AircraftPortraits.Get(acId);
            if (tex == null) return null;
            var img = new Image { image = tex, scaleMode = ScaleMode.ScaleAndCrop };
            img.style.height = 170; img.style.borderTopLeftRadius = img.style.borderTopRightRadius = img.style.borderBottomLeftRadius = img.style.borderBottomRightRadius = 6;
            return img;
        }

        // --------------------------------------------------------------------------------------- records
        static VisualElement Records(GameApp app)
        {
            var c = app.Career;
            var s = c.stats;
            var page = E("page", E("page-head", H1("Logbook")),
                Panel(Stats(Stat(Hm(CareerManager.Hours(c) * 3600), "Total hours"), Stat(s.flights.ToString(), "Flights"), Stat(s.landings.ToString(), "Landings"), Stat(s.smoothLandings.ToString(), "Smooth"),
                    Stat(s.hardLandings.ToString(), "Hard"), Stat($"{s.distanceKm / 1.852:0}", "Nautical miles"), Stat(N0(s.maxAltFt), "Highest ft"), Stat($"{s.fuelKg:0}", "Fuel kg"))));
            var ext = Stats();
            foreach (var ac in Aircraft.All.Values) ext.Add(Stat($"{(s.perAircraftS.TryGetValue(ac.id, out var sec) ? sec : 0) / 3600:F1}", $"{ac.shortName} hours"));
            ext.Add(Stat(s.operational.ToString(), "Operational flights")); ext.Add(Stat(s.passengers.ToString(), "Passengers")); ext.Add(Stat(s.rescues.ToString(), "Casualties delivered"));
            ext.Add(Stat($"{Math.Round(s.longestFlightS / 60)}", "Longest flight min")); ext.Add(Stat(Money(c.money.earned), "Career earnings"));
            page.Add(Panel(ext));
            var m = c.money;
            page.Add(Panel(Stats(Stat(Money(m.balance), "Balance"), Stat(Money(m.loan), "Training loan", m.loan > 0 ? "caution-t" : null), Stat(Money(m.earned), "Earned"), Stat(Money(m.spent), "Spent"))));
            page.Add(H3("Training record"));
            var rows = new List<VisualElement[]>();
            foreach (var ms in Missions.All.Where(x => x.objectives.Length > 0 && (c.exercises.ContainsKey(x.id) || x.category == MissionCategory.Training || x.category == MissionCategory.Test)))
            {
                var rec = c.exercises.TryGetValue(ms.id, out var x0) ? x0 : null;
                var spark = E("spark");
                foreach (var hh in (rec?.history ?? new List<AttemptRecord>()).AsEnumerable().Reverse())
                {
                    var bar = E("spark-i" + (hh.outcome == FlightOutcome.Passed || hh.outcome == FlightOutcome.Completed ? " p" : hh.outcome == FlightOutcome.Crashed ? " x" : ""));
                    bar.style.height = Length.Percent(Math.Max(8, hh.overall));
                    bar.tooltip = $"{Date(hh.at)} · {hh.overall}%";
                    spark.Add(bar);
                }
                rows.Add(new VisualElement[] { Cell(ms.shortTitle), Cell((rec?.attempts ?? 0).ToString(), true), Cell((rec?.passes ?? 0).ToString(), true), Cell(rec != null ? $"{rec.bestScore}%" : "—", true), rec?.lastResult != null ? OutcomeChip(rec.lastResult.Value) : Muted("Not flown"), spark });
            }
            foreach (var md in Theory.MODULES)
            {
                var r = c.modules.TryGetValue(md.id, out var x1) ? x1 : null;
                rows.Add(new VisualElement[] { Cell("Ground school: " + md.title), Cell(r != null ? "1" : "0", true), Cell(r?.passed == true ? "1" : "0", true), Cell(r != null ? $"{r.bestPct:0}%" : "—", true), r != null ? Chip(r.passed ? "Passed" : "Not passed", r.passed ? "ok" : "caution") : Muted("Not started"), null });
            }
            foreach (var ex in Theory.EXAMS)
            {
                var r = c.exams.TryGetValue(ex.id, out var x2) ? x2 : null;
                rows.Add(new VisualElement[] { Cell(ex.title), Cell((r?.attempts ?? 0).ToString(), true), Cell(r?.passed == true ? "1" : "0", true), Cell(r != null ? $"{r.bestPct:0}%" : "—", true), r != null ? Chip(r.passed ? "Passed" : "Not passed", r.passed ? "ok" : "caution") : Muted("Not sat"), null });
            }
            page.Add(Panel(Table(new[] { "Exercise", "Attempts", "Passes", "Best", "Last result", "Recent scores" }, new[] { 3f, 0.8f, 0.8f, 0.8f, 1.3f, 1.2f }, rows, new[] { false, true, true, true, false, false })));
            page.Add(H3("Flight log"));
            if (c.log.Count == 0) page.Add(Panel(Muted("No entries yet.")));
            else page.Add(Panel(Table(new[] { "Date", "Flight", "Aircraft", "Result", "Score", "Landing", "Time" }, new[] { 0.8f, 2.6f, 1, 1.2f, 0.7f, 0.7f, 0.7f },
                c.log.Select(l => new VisualElement[] { Cell(Date(l.at)), Cell(Missions.ById(l.missionId)?.shortTitle ?? l.title), Cell(Aircraft.Find(l.aircraftId)?.shortName ?? l.aircraftId), OutcomeChip(l.outcome), Cell($"{l.overall}%", true), Cell(l.landingGrade ?? "—"), Cell(Hm(l.flightSeconds), true) }),
                new[] { false, false, false, false, true, false, true })));
            return page;
        }

        // -------------------------------------------------------------------------------------- settings
        static readonly Dictionary<string, string> KEY_LABELS = new Dictionary<string, string>
        {
            ["throttleUp"] = "Throttle open", ["throttleDown"] = "Throttle idle", ["governor"] = "Governor on/off", ["beacon"] = "Anti-collision beacon", ["navLights"] = "Position lights", ["strobe"] = "Strobes",
            ["landingLight"] = "Landing light", ["searchlight"] = "Searchlight", ["switches"] = "Switch panel", ["collectiveUp"] = "Collective up", ["collectiveDown"] = "Collective down", ["pedalLeft"] = "Left pedal",
            ["pedalRight"] = "Right pedal", ["cyclicFwd"] = "Cyclic forward", ["cyclicAft"] = "Cyclic back", ["cyclicLeft"] = "Cyclic left", ["cyclicRight"] = "Cyclic right", ["engine"] = "Engine start/stop",
            ["trim"] = "Trim cyclic", ["trimRelease"] = "Release trim", ["camera"] = "Camera", ["pause"] = "Pause", ["panel"] = "Instrument panel", ["map"] = "Map", ["apAlt"] = "Autopilot ALT (twins)",
            ["apHdg"] = "Autopilot HDG (twins)", ["apHover"] = "Autopilot hover hold (twins)", ["winch"] = "Winch lower / abort", ["transmit"] = "Radio transmit", ["flir"] = "FLIR camera", ["voice"] = "Voice command",
            ["tablet"] = "Crew tablet (freeplay)", ["doors"] = "Cabin doors open/shut (on the ground)", ["gear"] = "Landing gear up/down (retractable wheels)",
        };

        static string PrettyKey(Key k)
        {
            switch (k)
            {
                case Key.UpArrow: return "↑"; case Key.DownArrow: return "↓"; case Key.LeftArrow: return "←"; case Key.RightArrow: return "→";
                default: var s = k.ToString(); return s.StartsWith("Digit") ? s.Substring(5) : s;
            }
        }

        static VisualElement SettingsView(GameApp app)
        {
            var s = app.Settings;
            void Change(Action<GameSettings> f) { var n = app.Settings.Clone(); f(n); app.CommitSettings(n); app.Later(app.Refresh); }
            var binding = St<string>(app, "binding", null);
            var confirmDelete = St(app, "confirmDelete", false);
            var importMsg = St<string>(app, "importMsg", null);
            var page = E("page"); page.style.maxWidth = 860;
            page.Add(H1("Settings"));
            page.Add(Panel(H3("Handling"),
                Seg(new[] { (AssistMode.Beginner, "Beginner"), (AssistMode.Standard, "Standard"), (AssistMode.Realistic, "Realistic") }, s.assist, v => Change(n => n.assist = v)),
                Muted(s.assist == AssistMode.Beginner ? "Cyclic sets an attitude and the aircraft levels itself when released. Drift near the hover is gently damped and torque is compensated automatically. You still fly every control."
                    : s.assist == AssistMode.Standard ? "Attitude-command cyclic, but no drift damping. Collective changes make the nose swing until you correct with pedal."
                    : "Rate-command cyclic: the aircraft holds whatever attitude you leave it in and never levels itself. Full torque reaction needs constant pedal. Disc flap-back means forward cyclic is needed as speed builds.", "small"),
                Check("Training aids: target markers and height readout", s.showTrainingAids, v => Change(n => n.showTrainingAids = v))));
            var keys = new List<VisualElement[]>();
            foreach (var k in ControlSettings.DefaultKeys().Keys)
            {
                var kk = k;
                var label = binding == k ? "Press a key…" : PrettyKey(s.controls.KeyFor(k));
                keys.Add(new VisualElement[] { Cell(KEY_LABELS.TryGetValue(k, out var l) ? l : k), T(label, "mono"), Btn("Change", "small", () => Set(app, "binding", kk)) });
            }
            var ctl = Panel(H3("Controls"),
                E("grid2",
                    Slider("Cyclic sensitivity", 0.4f, 1.6f, (float)s.controls.cyclicSensitivity, v => $"{v * 100:0}%", v => Change(n => n.controls.cyclicSensitivity = v), 0.05f),
                    Slider("Pedal sensitivity", 0.4f, 1.6f, (float)s.controls.pedalSensitivity, v => $"{v * 100:0}%", v => Change(n => n.controls.pedalSensitivity = v), 0.05f),
                    Slider("Stick dead zone", 0, 0.3f, (float)s.controls.deadzone, v => $"{v * 100:0}%", v => Change(n => n.controls.deadzone = v), 0.01f)),
                Check("Invert cyclic fore/aft", s.controls.invertCyclicY, v => Change(n => n.controls.invertCyclicY = v)),
                Table(new[] { "Function", "Key", "" }, new[] { 3f, 1.2f, 1 }, keys),
                Row(Btn("Reset keys", "small ghost", () => Change(n => n.controls.keyBindings = ControlSettings.DefaultKeys()))));
            if (binding != null)
            {
                // the next key press becomes the binding (Escape cancels)
                ctl.schedule.Execute(() =>
                {
                    var kb = Keyboard.current;
                    if (kb == null || St<string>(app, "binding", null) != binding) return;
                    if (kb.escapeKey.wasPressedThisFrame) { Set(app, "binding", null); return; }
                    foreach (var kc in kb.allKeys)
                        if (kc.wasPressedThisFrame && kc.keyCode != Key.Escape) { app.ScreenState["binding"] = null; Change(n => n.controls.keyBindings[binding] = kc.keyCode); return; }
                }).Every(16);
            }
            page.Add(ctl);
            page.Add(ControllerSetup(app));
            page.Add(Panel(H3("Display and sound"),
                E("grid2",
                    Field("Graphics quality", Seg(new[] { (GraphicsQuality.High, "High"), (GraphicsQuality.Balanced, "Balanced"), (GraphicsQuality.Low, "Low") }, s.graphics, v => Change(n => n.graphics = v))),
                    Field("Default camera", Seg(new[] { (CameraPref.Chase, "Chase"), (CameraPref.Cockpit, "Cockpit") }, s.camera, v => Change(n => n.camera = v))),
                    Slider("Text size", 0.9f, 1.3f, s.textScale, v => $"{v * 100:0}%", v => Change(n => n.textScale = v), 0.05f),
                    Slider("Volume", 0, 1, s.volume, v => $"{v * 100:0}%", v => Change(n => n.volume = v), 0.05f)),
                Check("Sound effects", s.sound, v => Change(n => n.sound = v)),
                Check("Full screen", s.fullscreen, v => Change(n => n.fullscreen = v)),
                Row(Btn("Show first-time tips again", "small ghost", () => Change(n => n.seenHints.Clear()), disabled: s.seenHints.Count == 0)),
                Row(Btn("Restore all defaults", "small ghost", () => { app.CommitSettings(new GameSettings()); app.Later(app.Refresh); }))));
            page.Add(CloudPanel(app));
            var c = app.Career;
            page.Add(Panel(H3("Career save"),
                Muted(FileDialog.Available ? $"{c.pilotName} · started {Date(c.createdAt)} · saved on this computer. Export a backup to move it to another computer."
                    : $"{c.pilotName} · started {Date(c.createdAt)} · saved in this browser. Sign in with Google to carry it between the web and Windows versions."),
                !FileDialog.Available ? null : Row(Btn("Export save", "", () => { var p = app.Saves.ExportCareer(app.Career); Set(app, "importMsg", $"Saved to {p}"); }),
                    Btn("Import save", "", () =>
                    {
                        var path = FileDialog.OpenJson("Import a Rotorcraft save");
                        if (path == null) return;
                        var ic = app.Saves.ImportCareer(path);
                        if (ic == null) Set(app, "importMsg", "That file is not a Rotorcraft save, or it is from a newer version.");
                        else { app.CommitCareer(ic); Set(app, "importMsg", $"Loaded {ic.pilotName}'s career."); }
                    }),
                    Btn("Open save folder", "ghost", () => Application.OpenURL("file://" + app.Saves.Folder))),
                importMsg != null ? Muted(importMsg, "small") : null,
                !confirmDelete ? Row(Btn("Delete career", "danger", () => Set(app, "confirmDelete", true)))
                    : Row(T("This cannot be undone.", "err"), Btn("Delete permanently", "danger", () => { app.DeleteCareer(); app.Go("menu"); }), Btn("Cancel", "ghost", () => Set(app, "confirmDelete", false)))));
            return page;
        }

        /// <summary>Joystick, HOTAS and pedals: the live device list and axis readouts, presets and inversion.</summary>
        static VisualElement ControllerSetup(GameApp app)
        {
            var s = app.Settings;
            void Change(Action<GameSettings> f) { var n = app.Settings.Clone(); f(n); app.CommitSettings(n); app.Later(app.Refresh); }
            var p = Panel(H3("Joystick, HOTAS and pedals"));
            var devices = InputSystem.devices.Where(d => d is Joystick || d is Gamepad).ToList();
            if (devices.Count == 0) p.Add(Muted("No joystick or gamepad connected. Plug one in: it is picked up straight away."));
            foreach (var d in devices) p.Add(Row(E("padlamp on"), T(d.description.product ?? d.displayName), Muted(d is Gamepad ? "Gamepad" : "Joystick", "small")));
            p.Add(Check("Choose the layout automatically from the connected device", s.autoController, v => Change(n => n.autoController = v)));
            if (!s.autoController)
                p.Add(Field("Layout", Seg(new[] { ("gamepad", "Gamepad"), ("tflight", "T.Flight Stick X") }, s.controls.controller.name == "Gamepad" ? "gamepad" : "tflight",
                    v => Change(n => n.controls.controller = v == "gamepad" ? Controllers.PresetGamepad() : Controllers.PresetTFlightStickX()))));
            var ctl = s.autoController ? (devices.Any(d => d.description.product == Controllers.TFlightStickX) ? Controllers.PresetTFlightStickX() : Controllers.PresetGamepad()) : s.controls.controller;
            // live axis meters
            var meters = E("stack tight");
            var fills = new List<(VisualElement fill, AxisFunction f)>();
            foreach (var kv in ctl.axes)
            {
                var fill = E("meter-fill");
                var meter = E("meter", E("meter-mid"), fill);
                var f = kv.Key; var invert = s.controls.controller.Axis(f)?.invert ?? kv.Value.invert;
                meters.Add(Row(T(Controllers.AxisLabels[kv.Key]).With(l => l.style.width = 220), meter, T($"{kv.Value.control}{(kv.Value.invert ? " (inverted)" : "")}", "muted mono small")));
                fills.Add((fill, f));
            }
            p.Add(meters);
            var src = new UnityInputSource();
            meters.schedule.Execute(() =>
            {
                foreach (var (fill, f) in fills)
                {
                    var b = ctl.Axis(f);
                    var v = b == null ? 0 : src.ReadAxis(b) ?? 0;
                    if (b != null && b.invert) v = -v;
                    var x = Mathf.Clamp((float)v, -1, 1);
                    fill.style.left = Length.Percent(50 + Mathf.Min(0, x) * 50); fill.style.width = Length.Percent(Mathf.Abs(x) * 50);
                }
            }).Every(50);
            p.Add(Muted("Move the stick, twist grip and throttle: the bars should follow. The T.Flight Stick X preset uses the throttle lever as the collective and the twist as the pedals.", "small"));
            return p;
        }

        // -------------------------------------------------------------------------------------- tutorial
        static VisualElement Tutorial(GameApp app)
        {
            VisualElement Rows(params (string a, string b)[] r) => Table(new[] { "", "" }, new[] { 2f, 1.6f }, r.Select(x => new VisualElement[] { Cell(x.a), T(x.b, "mono") }));
            var page = E("page"); page.style.maxWidth = 900;
            page.Add(H1("Controls & Flying"));
            page.Add(E("grid2",
                Panel(H3("Keyboard"), Rows(("Collective up / down", "W / S (Shift = fine)"), ("Cyclic", "Arrow keys"), ("Pedals", "A / D"), ("Engine start / stop", "E"), ("Throttle open / idle", "R / F"),
                    ("Beacon · Nav · Strobe · Landing light", "B · N · O · L"), ("Governor · Switch panel", "G · K"), ("Trim cyclic · release trim", "T · U"), ("Camera", "C"), ("Panel / Map", "I / M"),
                    ("Pause", "P or Esc"), ("Autopilot ALT · HDG · hover hold", "1 · 2 · 3"), ("Radio transmit · Winch · FLIR", "V · X · J"), ("Searchlight", "H"), ("Crew tablet (freeplay)", "Tab"),
                    ("Cabin doors (on the ground)", "Z"), ("Look around (cockpit view)", "Right mouse drag · hat switch")), Muted("All keys can be changed in Settings.", "small")),
                Panel(H3("Joystick and gamepad"), Muted("Flight sticks, throttle quadrants, rudder pedals and gamepads all work. The Thrustmaster T.Flight Stick X is recognised automatically. Default gamepad layout:", "small"),
                    Kv(("Right stick", "Cyclic", "body"), ("Left stick ↑↓", "Raise / lower collective", "body"), ("Left stick ←→", "Pedals", "body"), ("A · X · Y", "Engine · Camera · Trim", "body"),
                        ("RT", "Release trim", "body"), ("Start · Back", "Pause · Map", "body"), ("LB · RB", "Throttle idle · open", "body"), ("LT", "Crew tablet in freeplay", "body"),
                        ("In menus", "D-pad or stick to move, A to select, B back", "body"))),
                Panel(H3("Your first hover"), P("1. Beacon on (B). Collective fully down, throttle closed. Press Start (or E)."), P("2. Let the engine idle a few seconds, then roll the throttle open (R). Wait for rotor RPM to reach 100%."),
                    P("3. Raise the collective slowly. The aircraft lifts at about half lever."), P("4. Stop raising as soon as you are light on the skids, then add a touch more."),
                    P("5. Use small cyclic inputs to stop drift. Look at the horizon, not the ground."), P("6. To land, lower the collective smoothly. Under 100 ft/min is a smooth landing."),
                    P("7. After landing: collective down, throttle idle (F), 15 seconds to cool, then engine off (E).")),
                Panel(H3("Lights"), P("Open the switch panel (Sw, or K) for the anti-collision beacon, position lights (red left, green right, white tail), strobes, landing light and the governor. The beacon goes on before every engine start. At dusk and night the position lights are required, and the landing light should be on for every approach. Helipads are marked by green perimeter lights, training spots by yellow lights, the taxiway by blue edge lights, and obstacles by red lights.")),
                Panel(H3("Reading the instruments"), P("The strip along the top shows indicated airspeed (kt), altitude above sea level (ft), vertical speed (ft/min), heading, rotor RPM (%), engine torque (%) and fuel (kg). Rotor RPM must stay in the green; torque above 100% is over the continuous limit. Open the full panel for analogue gauges, including an attitude indicator and a compass with a magenta bug pointing at your next target.")),
                Panel(H3("Twins, rescue and offshore"), P("In the Northstar and Guardian the buttons under the nav box engage the autopilot: ALT holds altitude, HDG holds heading, HVR holds a hover over the ground (engage below 12 kt; small cyclic moves the hold point, the lever changes the height). Moving the lever or pedals firmly drops a mode. When a radio call is due a TRANSMIT button appears. On a rescue, fly the search area until the crew call \"contact\", come to a steady hover beside the casualty at 50 to 150 ft, follow the winch operator's calls, and press WINCH when it turns green. Hold the hover until both are aboard.")),
                Panel(H3("The Peregrine"), P("Its rotor turns clockwise seen from above, so it takes right pedal as you lift, not left. Raise the wheels with Q once you are climbing away above about 60 kt and lower them before every approach: the switch panel shows GEAR DN or UP, and the crew call \"Gear!\" if you are low and slow with them up. A wheels-up landing damages the aircraft. On police jobs the camera (J) finds people by their heat; the searchlight (H) is for the last few hundred metres."))));
            page.Add(Row(Btn("Go to Ground school", "primary", () => app.Go("school"))));
            return page;
        }
    }
}
