// The flight HUD (port of the render half of src/ui/FlightView.tsx and Loading.tsx): readouts, annunciators,
// the objective and nav boxes, radio messages, the switch strip, autopilot and crew buttons, decisions, first-time
// hints, the trim chip, the instrument hood, the gauge panel, the map, the pre-flight checklist with its
// walk-round markers, pause, accident and completion cards, and the loading screen.
using System;
using System.Collections.Generic;
using System.Linq;
using Rotorcraft.Game;
using Rotorcraft.Inputs;
using Rotorcraft.Presentation.UI;
using Rotorcraft.Sim;
using Rotorcraft.World;
using UnityEngine;
using UnityEngine.UIElements;
using static Rotorcraft.Presentation.UI.Ui;

namespace Rotorcraft.Presentation
{
    public sealed partial class FlightSession
    {
        VisualElement hudRoot, readouts, hudBtns, objective, navbox, annun, radio, crewbar, switches, decisionBox, hintBox, trimChip, agl, panelBox, mapBox, preflight, hood, banner, center, tablet, hotspotLayer;
        InstrumentPanel instruments;
        MapView map;
        readonly Dictionary<string, (VisualElement box, Label v)> ro = new Dictionary<string, (VisualElement, Label)>();
        readonly Dictionary<CheckId, VisualElement> hotspots = new Dictionary<CheckId, VisualElement>();
        bool showPanel, showMap, showSwitches, showTablet;
        bool hudDirty = true;
        string hint; float hintAt;
        float hudT, mapT;
        int lastMsgId = -1;
        string lastKey = "";

        static readonly Dictionary<string, (string title, string text)> HINTS = new Dictionary<string, (string, string)>
        {
            ["Hover"] = ("Hover box", "Stay inside the ring and the height band until the timer completes. A short wobble outside pauses the timer; staying out for more than two seconds resets it."),
            ["Waypoint"] = ("Waypoints", "Fly over the marker within the altitude band shown in the nav box. The magenta bug on the compass points to it."),
            ["Radio"] = ("Radio calls", "When a call is due, a TRANSMIT button appears (V on the keyboard). Make the call before carrying on: it counts towards procedures."),
            ["Decision"] = ("Decisions", "The flight is not paused while you decide. Choose the option with the most safety margin: decisions are scored and affect your standing with the operator."),
            ["Load"] = ("Loading", "Stay on the ground with the rotor turning and the lever fully down until loading finishes. The aircraft gets heavier or lighter, so hover power changes."),
            ["Search"] = ("Searching", "There is no arrow to the casualty. Fly parallel tracks across the search area at 300–500 ft and about 60 kt; the map shows what you have covered. The crew call \"contact\" when they see them."),
            ["Winch"] = ("Winching", "Come to a steady hover 50–150 ft up beside the casualty and follow the winch operator's calls. Press WINCH (X) when it turns green, then hold the hover until both are aboard."),
            ["Hold"] = ("Precise holds", "Hold the heading and altitude inside the tolerances until the timer runs out. Small attitude changes, then wait for the instruments to settle."),
            ["Ap"] = ("Autopilot", "ALT (1) holds altitude, HDG (2) holds heading, HVR (3) holds a hover over the ground below 12 kt. Moving the lever or pedals firmly drops a mode."),
            ["hood"] = ("Instrument hood", "The outside view is blocked: fly on the instrument panel only. The attitude indicator is the centre of your scan."),
            ["FailEngine"] = ("Engine failure", "One engine has stopped. Keep the rotor RPM in the green, fly at about 70 kt and watch the torque: the remaining engine is working hard."),
            ["Failure"] = ("System failure", "Tail rotor failure: keep the speed up, use little power and plan a running landing. Hydraulic failure: controls are heavy; fly slowly and smoothly."),
            ["freeplay"] = ("Freeplay shift", "Callouts arrive on the crew tablet (Tab, or LT on a controller) and over the radio. Accept one your kit can do, fly it, and get paid on how well it went. Refuel on the ground at Kinloch or on a platform."),
            ["twin"] = ("Twin-engine helicopter", "Heavier and slower to respond than the Kestrel. The panel compass becomes an HSI showing your course (green) and the bearing to the target (magenta)."),
            ["walkround"] = ("Pre-flight checks", "Walk round the aircraft: click each green marker to inspect it. Drag to look round, scroll to zoom. Then climb aboard for the cockpit checks."),
        };

        string HintFor()
        {
            var d = Runtime.Active?.def;
            if (Pre != null && Pre.stage == PreflightStage.Outside) return "walkround";
            if (Runtime.freeplay && d == null) return "freeplay";
            if (d != null && d.hood) return "hood";
            if (d != null && HINTS.ContainsKey(d.kind.ToString())) return d.kind.ToString();
            if (heli.Aircraft.engines > 1) return "twin";
            return null;
        }

        bool PointerOverUi(Vector2 screenPos)
        {
            if (hudRoot?.panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(hudRoot.panel, new Vector2(screenPos.x, Screen.height - screenPos.y));
            var picked = hudRoot.panel.Pick(p);
            return picked != null && picked != hudRoot && picked.pickingMode == PickingMode.Position && !(picked is TextElement && picked.parent == hudRoot);
        }

        // ------------------------------------------------------------------------------------- loading
        public sealed class LoadingView
        {
            public VisualElement root; public VisualElement fill; public Label what;
            public void Progress(float f, string text)
            {
                fill.style.width = Length.Percent(Mathf.Clamp01(f) * 100);
                if (text != null) what.text = text;
            }
            public void Fail(string message, Action back)
            {
                what.text = "Something went wrong";
                what.AddToClassList("err");
                root.Add(E("loading-tip", T(message, "loading-tip-t"), Btn("Back to the menu", "primary small", back)));
            }

            public void Leave()
            {
                root.AddToClassList("leaving");
                root.pickingMode = PickingMode.Ignore;
                root.schedule.Execute(() => root.RemoveFromHierarchy()).StartingIn(500);
            }
        }

        static readonly (string kind, string text)[] TIPS =
        {
            ("all", "Before starting the engine, lower the collective fully (S) and close the throttle (F). Starting with the lever up or the throttle open is a procedural error."),
            ("all", "Switch the anti-collision beacon on (B) before you start the engine, and the position lights (N) at night."),
            ("all", "Once the engine is running, roll the throttle open (R). The governor then holds the rotor at 100%."),
            ("all", "Small inputs. Hold a key briefly and let the aircraft settle before adding more: a helicopter rewards patience."),
            ("all", "Near the ground the rotor gets help from ground effect, so a low hover needs less power than a high one. The briefing’s hover-power check shows the margin."),
            ("all", "Above about 15 to 20 knots the rotor gains translational lift and becomes more efficient: expect the aircraft to want to climb as it accelerates."),
            ("all", "Avoid descending steeply at low airspeed with power applied: that is how you get into vortex ring, where more lever only makes the sink worse. Fly forward out of it."),
            ("all", "Take off and land into wind where you can. The weather card in the briefing shows the wind direction and strength."),
            ("all", "Watch the torque gauge. Pulling more power than the limit is an exceedance and costs you marks."),
            ("all", "A soft touchdown matters more than precision. Arrive slowly, settle, then lower the lever fully."),
            ("all", "Press C to change the camera. In the cockpit view, hold the right mouse button and drag to look round; double right-click to look ahead again."),
            ("all", "Press P or Esc to pause. The map is on M, the instrument panel on I."),
            ("all", "The autopilot holds altitude (1), heading (2) and hover (3) in the twins."),
            ("all", "The Thrustmaster T.Flight Stick X is recognised automatically: the throttle lever is the collective and the twist is the pedals."),
            ("all", "Turn the landing light on with L when landing at night, and use the searchlight (H) on aircraft that carry one."),
            ("all", "Plan your fuel. The briefing works out the trip and a reserve; landing below the reserve is marked down."),
            ("all", "Sign in with Google to keep your save on every computer and to appear on the leaderboards in the pilot area."),
            ("all", "On the ground, Z opens and shuts the cabin doors. They close themselves before lift-off."),
            ("freeplay", "Open the crew tablet with Tab, or LT on a controller, to see callouts, accept a job and refuel."),
            ("freeplay", "Only accept jobs your kit can do. A winch rescue needs the hoist; a patient transfer needs the medical fit."),
            ("freeplay", "Heavier kit and more fuel leave less spare power. A lighter aircraft hovers more easily, especially at a high landing site."),
            ("freeplay", "You can refuel on the ground at Kinloch or on an offshore platform from the tablet."),
            ("career", "Pre-flight checks are done in the aircraft: walk round and click the markers, then climb aboard and do the cockpit items for real."),
            ("career", "Moving the cyclic fully left, right, forward and back and both pedals ticks the “controls full and free” check."),
            ("career", "Ground school modules and exams unlock licences. Study before a test flight: Home always shows your next step."),
            ("career", "Licences need recurrent checks. Home warns you when one is due or has lapsed."),
            ("career", "Failed a test? You will be given remedial training. Fly it, then try again."),
        };

        static readonly System.Random tipRand = new System.Random();
        static string PickTip(string kind, string not = null)
        {
            var pool = TIPS.Where(t => (t.kind == "all" || t.kind == kind) && t.text != not).Select(t => t.text).ToArray();
            return pool[tipRand.Next(pool.Length)];
        }

        LoadingView ShowLoading(string title, string sub, bool freeplay)
        {
            var kind = freeplay ? "freeplay" : "career";
            var tip = PickTip(kind);
            var fill = E("loading-bar-fill");
            var what = T("Loading", "eyebrow");
            var tipText = T(tip, "loading-tip-t");
            var root = E("loading",
                E("loading-glow"),
                E("loading-logo"),
                E("loading-what", what, T(title, "loading-t"), sub != null ? Muted(sub) : null),
                E("loading-bar", fill),
                E("loading-tip", Eyebrow("Tip"), tipText, Btn("Another tip", "small ghost", () => tipText.text = PickTip(kind, tipText.text))));
            App.Overlay.Add(root);
            root.pickingMode = PickingMode.Position;
            App.Overlay.pickingMode = PickingMode.Ignore;
            return new LoadingView { root = root, fill = fill, what = what };
        }

        // ------------------------------------------------------------------------------------- HUD
        void BuildHud()
        {
            hudRoot?.RemoveFromHierarchy();
            hudRoot = E("hud");
            hudRoot.pickingMode = PickingMode.Ignore;
            hood = E("hood", Up("Instrument hood: fly by the instruments", "hood-l")); hood.pickingMode = PickingMode.Ignore; hudRoot.Add(hood);
            readouts = E("readouts");
            ro.Clear();
            foreach (var (k, l) in new[] { ("ias", "IAS kt"), ("alt", "ALT ft"), ("vs", "V/S fpm"), ("hdg", "HDG"), ("rpm", "RRPM %"), ("tq", "TQ %"), ("fuel", "FUEL kg") })
            {
                var v = T("–", "ro-v");
                var box = E("ro", Up(l, "ro-l"), v);
                ro[k] = (box, v);
                readouts.Add(box);
            }
            hudBtns = E("hud-btns");
            hudRoot.Add(E("hud-top", readouts, hudBtns).With(x => x.pickingMode = PickingMode.Ignore));
            objective = E("objective"); hudRoot.Add(objective);
            navbox = E("navbox"); hudRoot.Add(navbox);
            annun = E("annun"); annun.pickingMode = PickingMode.Ignore; hudRoot.Add(annun);
            agl = E("agl"); agl.pickingMode = PickingMode.Ignore; hudRoot.Add(agl);
            radio = E("radio"); radio.pickingMode = PickingMode.Ignore; hudRoot.Add(radio);
            crewbar = E("crewbar"); hudRoot.Add(crewbar);
            switches = E("switches"); hudRoot.Add(switches);
            instruments = new InstrumentPanel();
            panelBox = E("panel-overlay", instruments); hudRoot.Add(panelBox);
            map = new MapView(); map.style.flexGrow = 1;
            mapBox = E("map-overlay", map); hudRoot.Add(mapBox);
            trimChip = new Button(() => { heli.Input.ClearTrim(); hudDirty = true; }).Cls("trimchip"); hudRoot.Add(trimChip);
            hintBox = E("hintcard"); hudRoot.Add(hintBox);
            decisionBox = E("decision"); hudRoot.Add(decisionBox);
            preflight = E("preflight"); hudRoot.Add(preflight);
            hotspotLayer = E("fill"); hotspotLayer.pickingMode = PickingMode.Ignore; hudRoot.Add(hotspotLayer);
            hotspots.Clear();
            foreach (var (id, l) in new[] { (CheckId.Fuel, "Fuel"), (CheckId.Rotor, "Rotor head"), (CheckId.Tail, "Tail rotor"), (CheckId.Lights, "Lights") })
            {
                var cid = id;
                var dot = E("hotspot-dot");
                var b = new Button(() => Inspect(cid)).Cls("hotspot");
                b.Add(dot); b.Add(Up(l, "hotspot-l"));
                dot.schedule.Execute(() => dot.ToggleInClassList("pulse")).Every(800);
                hotspots[id] = b;
                hotspotLayer.Add(b);
            }
            tablet = E("tablet"); hudRoot.Add(tablet);
            banner = E("banner"); hudRoot.Add(banner);
            center = E("center-card"); hudRoot.Add(center);
            App.Root.Add(hudRoot);
            hudRoot.BringToFront();
            App.Overlay.BringToFront();
            lastMsgId = -1; lastKey = ""; hudDirty = true;
        }

        void UpdateHud()
        {
            if (hudRoot == null) return;
            var s = heli.State; var o = s.@out; var ac = heli.Aircraft; var m = Runtime;
            var now = Time.unscaledTime;
            // readouts and annunciators about 14 times a second
            if (now - hudT > 0.07f)
            {
                hudT = now;
                void Set(string k, string v, string cls = "")
                {
                    var (box, lab) = ro[k];
                    if (lab.text != v) lab.text = v;
                    box.EnableInClassList("caution", cls == "caution"); box.EnableInClassList("warn", cls == "warn"); box.EnableInClassList("ok", cls == "ok");
                }
                Set("ias", o.iasKt < 1 ? "0" : $"{o.iasKt:0}", o.iasKt > ac.vneKt ? "warn" : "");
                Set("alt", $"{Math.Round(o.altFt / 10) * 10:0}");
                Set("vs", $"{(o.vsFpm >= 0 ? "+" : "")}{Math.Round(o.vsFpm / 10) * 10:0}", o.vsFpm < -600 && o.aglM < 30 ? "caution" : "");
                Set("hdg", $"{Math.Round(o.hdg) % 360:000}°");
                Set("rpm", $"{o.rpm:0}", s.engine == EngineState.Off && o.rpm < 5 ? "" : o.rpm < ac.rpmMin ? "warn" : o.rpm > ac.rpmMax ? "caution" : "ok");
                Set("tq", $"{o.torque:0}", o.torque > ac.torqueMax ? "warn" : o.torque > ac.torqueMcp ? "caution" : "");
                Set("fuel", s.fuel.ToString(s.fuel < 100 ? "F1" : "F0"), s.fuel < Setup.mission.minFuelKg ? "caution" : "");
                var ann = new List<(string t, bool c)>();
                if (s.engine != EngineState.Off && o.rpm < ac.rpmMin && o.rpm > 20) ann.Add(("LOW RPM", false));
                if (o.rpm > ac.rpmMax) ann.Add(("RPM HIGH", true));
                if (o.torque > ac.torqueMcp) ann.Add(("TORQUE", o.torque <= ac.torqueMax));
                if (s.throttleIdle) ann.Add(("THROTTLE IDLE", true));
                if (s.engine == EngineState.Flameout) ann.Add(("ENGINE OUT", false)); else if (s.enginesOut > 0) ann.Add(("ONE ENG INOP", false));
                if (s.failures != null && s.failures.tailRotor) ann.Add(("TAIL ROTOR", false));
                if (s.failures != null && s.failures.hydraulics) ann.Add(("HYD", false));
                if (s.fuel < Setup.mission.minFuelKg && s.engine != EngineState.Off) ann.Add(("LOW FUEL", true));
                if (s.engine == EngineState.Starting) ann.Add(("STARTING", true));
                if (o.iasKt > ac.vneKt) ann.Add(("VNE", false));
                if (!s.sys.governor && s.engine == EngineState.Running) ann.Add(("GOV OFF", true));
                var lim = FlightModel.TempLimitsOf(ac);
                if (s.engineTemp > lim.caution && s.engine != EngineState.Off) ann.Add(($"{lim.label} {s.engineTemp:0}°", s.engineTemp <= lim.max));
                if (s.engine == EngineState.Running && s.sys.throttle < 0.1 && !s.throttleIdle) ann.Add(("IDLE", true));
                if (o.vrs > 0.3) ann.Add(("VORTEX RING", false));
                if (ac.retractableGear)
                {
                    if (s.gearPos > 0.02 && s.gearPos < 0.98) ann.Add(("GEAR TRANSIT", true));
                    else if (s.gearPos <= 0.02 && !s.onGround && o.aglM < 60 && o.iasKt < 60) ann.Add(("GEAR UP", false));
                }
                var key = string.Join("|", ann.Select(a => a.t + a.c));
                if (annun.userData as string != key) { annun.userData = key; annun.Clear(); foreach (var (at, ac2) in ann) annun.Add(T(at, ac2 ? "ann c" : "ann")); }
                // AGL aid / radar altimeter
                var showAgl = (App.Settings.showTrainingAids || ac.equipment.radalt) && o.aglM < 60 && !s.onGround;
                agl.Show(showAgl);
                if (showAgl) { agl.Clear(); agl.Add(Row(Up(ac.equipment.radalt ? "Rad alt" : "Height AGL", "agl-tag"), T($"{o.aglM * SimMath.FT:0} ft · GS {o.gsKt:0} kt", "mono"))); }
                // nav box
                var t = m.NavTargetNow();
                navbox.Show(t != null);
                if (t != null)
                {
                    var (brg, dist) = WorldMap.BearingDistance(s.pos.x, s.pos.z, t.x, t.z);
                    var rel = ((brg - o.hdg + 540) % 360) - 180;
                    var dtxt = dist < 1852 ? $"{dist:0} m" : $"{dist / 1852:F1} nm";
                    var alt = t.altMinFt != null || t.altMaxFt != null ? $"ALT {(t.altMinFt?.ToString("0") ?? "—")}–{(t.altMaxFt?.ToString("0") ?? "—")} ft" : $"ELEV {t.elevationFt:0} ft";
                    var turn = Math.Abs(rel) < 5 ? "on course" : $"{(rel < 0 ? "◀ turn left" : "turn right ▶")} {Math.Abs(rel):0}°";
                    var ete = o.gsKt > 15 ? $" · ETE {Math.Round(dist / (o.gsKt / 1.9438) / 60)} min" : "";
                    navbox.Clear();
                    navbox.Add(T(t.name, "navbox-nm")); navbox.Add(T($"{Math.Round(brg) % 360:000}° · {dtxt}", "navbox-bd mono")); navbox.Add(T(turn + ete, "navbox-al mono")); navbox.Add(T(alt, "navbox-al mono"));
                }
                hood.Show(m.Hood);
                if (m.Hood) showPanel = true;
                // the panel and the walk-round markers
                panelBox.Show(showPanel);
                if (showPanel)
                {
                    var tt = m.NavTargetNow();
                    instruments.Set(s, ac, tt != null ? WorldMap.BearingDistance(s.pos.x, s.pos.z, tt.x, tt.z).brg : (double?)null, HsiFor());
                }
                PlaceHotspots();
            }
            {
                var nt = m.NavTargetNow();
                displays.Set(s, ac, nt != null ? WorldMap.BearingDistance(s.pos.x, s.pos.z, nt.x, nt.z).brg : (double?)null, HsiFor(), heli.Camera == CameraMode.Cockpit);
            }
            if (showMap && now - mapT > 0.25f)
            {
                mapT = now;
                var t2 = m.NavTargetNow();
                var d = Director;
                map.Options = new MapOptions
                {
                    aircraft = (s.pos.x, s.pos.z, o.hdg), center = (s.pos.x, s.pos.z), metresPerPx = 9000.0 / Math.Max(200, mapBox.contentRect.width),
                    route = d != null ? null : MapView.RouteOf(Setup.mission), target = t2 != null ? (t2.x, t2.z, t2.name) : ((double, double, string)?)null,
                    search = m.SearchOverlay(), found = m.targets.Where(x => m.found.Contains(x.id)).Select(x => (x.x, x.z, x.name)).ToList(),
                    callouts = d == null ? null : d.offers.Select(c => (c.x, c.z, c.title, false, c.priority)).Concat(d.active != null ? new[] { (d.active.callout.x, d.active.callout.z, d.active.callout.title, true, d.active.callout.priority) } : new (double, double, string, bool, int)[0]).ToList(),
                };
                map.Redraw();
            }
            mapBox.Show(showMap);
            // the rest is rebuilt when something it shows changes
            var a = m.Active;
            var dec = m.PendingDecision();
            var crashReady = s.crashed && crashAt != null && now - crashAt.Value > 1.5f;
            var stateKey = $"{a?.def.label}|{a?.detail}|{(int)((a?.progress ?? 0) * 40)}|{m.status}|{s.engine}|{s.onGround}|{s.sys.throttleTarget < 0.5}|{s.sys.throttle < 0.1}|{showPanel}|{showMap}|{showSwitches}|{showTablet}|{heli.Camera}|{Paused}|{crashReady}|{dec?.prompt}|{heli.Input.Trimmed}|{heli.Input.trimReleasing}|{Pre?.stage}|{preFocus?.id}|{string.Join(",", Pre?.items.Select(i => i.done) ?? new bool[0])}|{Pre?.ControlsProgress()}|{Pre?.LookProgress()}|{(Director != null ? $"{Director.offers.Count}{Director.active?.job}{Director.history.Count}{Director.refuelling}{Math.Round(Director.earned)}" : "")}|{string.Join(",", m.Actions(s).Select(x => $"{x.id}{x.enabled}{x.on}"))}|{m.ap.alt}{m.ap.hdg}{m.ap.hover}|{Autopilot.ApStatus(m.ap)}|{s.sys.beacon}{s.sys.nav}{s.sys.strobe}{s.sys.landingLight}{s.sys.searchlight}{s.sys.governor}|{(int)(s.sys.throttle * 20)}|{(int)s.engineTemp / 5}|{hint}|{cam.LookingAway}";
            if (stateKey != lastKey || hudDirty) { lastKey = stateKey; hudDirty = false; Rebuild(dec, crashReady); }
            // radio: the last three messages
            var last = m.messages.Count > 0 ? m.messages[m.messages.Count - 1].id : -1;
            radio.Show(!showPanel);
            if (last != lastMsgId)
            {
                lastMsgId = last;
                radio.Clear();
                foreach (var msg in m.messages.Skip(Math.Max(0, m.messages.Count - 3)))
                {
                    var el = E("msg " + (msg.kind == MessageKind.Caution ? "caution" : msg.kind == MessageKind.Warning ? "warning" : msg.kind == MessageKind.Good ? "good" : ""), Up(msg.from, "msg-from"), T(msg.text, "msg-t"));
                    if (msg.id == last) { el.AddToClassList("new"); el.schedule.Execute(() => el.RemoveFromClassList("new")).StartingIn(20); }
                    radio.Add(el);
                }
            }
            // first-time hints
            var hk = HintFor();
            if (hk != null && hint == null && !App.Settings.seenHints.Contains(hk) && HINTS.ContainsKey(hk))
            {
                hint = hk; hintAt = now;
                var ns = App.Settings.Clone(); ns.seenHints.Add(hk); App.CommitSettings(ns);
                hudDirty = true;
            }
            if (hint != null && now - hintAt > 22) { hint = null; hudDirty = true; }
            if (showTablet && Director != null) UpdateTablet();
        }

        HsiData HsiFor()
        {
            if (!heli.Aircraft.equipment.autopilot) return null;
            var t = Runtime.NavTargetNow(); var from = Runtime.legFrom;
            if (t == null || from == null) return null;
            var s = heli.State;
            var leg = WorldMap.BearingDistance(from.Value.x, from.Value.z, t.x, t.z);
            var to = WorldMap.BearingDistance(s.pos.x, s.pos.z, t.x, t.z);
            if (leg.dist < 200) return new HsiData { course = to.brg, xtkM = 0, distM = to.dist, name = t.name };
            double ux = (t.x - from.Value.x) / leg.dist, uz = (t.z - from.Value.z) / leg.dist;
            var xtk = (s.pos.x - from.Value.x) * -uz + (s.pos.z - from.Value.z) * ux;
            return new HsiData { course = leg.brg, xtkM = xtk, distM = to.dist, name = t.name };
        }

        void PlaceHotspots()
        {
            var outside = Pre != null && Pre.stage == PreflightStage.Outside && heli.Camera == CameraMode.Orbit;
            var camera = cam.GetComponent<Camera>();
            foreach (var kv in hotspots)
            {
                var el = kv.Value;
                var done = Pre?.Item(kv.Key)?.done ?? true;
                if (!outside || done || hudRoot.panel == null) { el.Show(false); continue; }
                var sp = camera.WorldToScreenPoint(WalkPoint(kv.Key));
                if (sp.z < 0) { el.Show(false); continue; }
                var pp = RuntimePanelUtils.ScreenToPanel(hudRoot.panel, new Vector2(sp.x, Screen.height - sp.y));
                el.Show(true);
                el.style.left = pp.x - 15; el.style.top = pp.y - 15;
                el.EnableInClassList("busy", preFocus?.id == kv.Key);
            }
        }

        void Rebuild((string prompt, (string id, string label)[] options)? dec, bool crashReady)
        {
            var s = heli.State; var m = Runtime; var ac = heli.Aircraft;
            // buttons along the top
            hudBtns.Clear();
            if (s.engine == EngineState.Off) hudBtns.Add(HB("Start", "start", () => { if (heli.CurrentControls.collective > 0.2) m.Say(s.t, "Crew", "Lower the collective before starting.", MessageKind.Caution); else FlightModel.CommandEngine(s, EngineCommand.Start); hudDirty = true; }));
            else if (s.onGround && s.engine != EngineState.Starting && s.sys.throttle < 0.1) hudBtns.Add(HB("Eng off", "", () => { FlightModel.CommandEngine(s, EngineCommand.Stop); hudDirty = true; }));
            if (s.engine != EngineState.Off) hudBtns.Add(HB(s.sys.throttleTarget < 0.5 ? "Thr open" : "Thr idle", s.sys.throttleTarget < 0.5 ? "start" : "", () => { FlightModel.CommandThrottle(s, s.sys.throttleTarget < 0.5 ? 1 : 0); hudDirty = true; }));
            hudBtns.Add(HB("Sw", showSwitches ? "on" : "", () => { showSwitches = !showSwitches; hudDirty = true; }));
            hudBtns.Add(HB("Panel", showPanel ? "on" : "", () => { showPanel = !showPanel; hudDirty = true; }));
            hudBtns.Add(HB("Map", showMap ? "on" : "", () => { showMap = !showMap; hudDirty = true; }));
            if (Director != null)
            {
                var tb = HB("Tablet", (showTablet ? "on " : "") + (Director.offers.Count > 0 ? "start" : ""), () => { showTablet = !showTablet; hudDirty = true; });
                if (Director.offers.Count > 0) tb.Add(T(Director.offers.Count.ToString(), "hbadge"));
                hudBtns.Add(tb);
            }
            if (s.onGround) hudBtns.Add(HB("Doors", "", () => { animator.TogglePlayer(); hudDirty = true; }));
            hudBtns.Add(HB(heli.Camera == CameraMode.Chase ? "Chase" : heli.Camera == CameraMode.Cockpit ? "Cockpit" : "Orbit", "", () => { heli.CycleCamera(); hudDirty = true; }));
            if (heli.Camera == CameraMode.Cockpit && cam.LookingAway) hudBtns.Add(HB("Ahead", "", () => { cam.LookAhead(); hudDirty = true; }));
            hudBtns.Add(HB("II", Paused ? "on" : "", () => TogglePause()));
            // objective, or the freeplay idle line
            objective.Clear();
            objective.RemoveFromClassList("idle");
            var a = m.Active;
            var preActive = Pre != null && Pre.Active;
            if (a == null && Director != null && !showTablet)
            {
                objective.AddToClassList("idle");
                objective.Add(Up(Director.cfg.pace == FreeplayPace.Off ? "Free roam" : $"Freeplay · {Director.history.Count(h => h.outcome == JobOutcome.Completed)} jobs · {Money(Director.earned - Director.fuelCost)}", "objective-ph"));
                objective.Add(T(Director.cfg.pace == FreeplayPace.Off ? "No callouts: fly where you like. Refuel from the tablet at Kinloch or a platform." : Director.offers.Count > 0 ? $"{Director.offers.Count} callout{(Director.offers.Count > 1 ? "s" : "")} waiting: open the tablet (Tab / LT)" : "Standing by for callouts", "objective-lb"));
                objective.Show(true);
            }
            else if (a != null && !preActive)
            {
                objective.Add(Up($"{a.def.phase} · {(Director?.active != null ? Director.active.callout.title : $"{m.index + 1}/{m.objectives.Count}")}", "objective-ph"));
                objective.Add(T(a.def.label, "objective-lb"));
                if (!string.IsNullOrEmpty(a.detail)) objective.Add(T(a.detail, "objective-dt"));
                if (a.progress > 0 && a.progress < 1) { var f = E("objective-pg-fill"); f.style.width = Length.Percent((float)a.progress * 100); objective.Add(E("objective-pg", f)); }
                objective.Show(true);
            }
            else objective.Show(false);
            // pre-flight checklist
            preflight.Clear();
            preflight.Show(preActive);
            if (preActive)
            {
                preflight.Add(E("pf-head", Up("Pre-flight checks", "pf-title"), T(Pre.stage == PreflightStage.Outside ? "Walk round the aircraft" : "In the cockpit", "pf-count")));
                foreach (var i in Pre.items.Where(x => x.zone == (Pre.stage == PreflightStage.Outside ? CheckZone.Outside : CheckZone.Cockpit)))
                {
                    var id = i.id;
                    var busy = preFocus?.id == i.id;
                    var extra = !i.done && i.id == CheckId.Controls ? Pre.ControlsProgress() : !i.done && i.id == CheckId.Lookout ? Pre.LookProgress() : "";
                    var li = E("pf-li" + (i.done ? " done" : ""), T(i.done ? "✓" : busy ? "…" : "○", "pf-ic"),
                        E("pf-tx", T(i.label, "pf-b"), T(i.done ? i.detail : busy ? "Checking…" : string.IsNullOrEmpty(extra) ? i.how : extra, "pf-s")));
                    if (!i.done && i.id != CheckId.Controls)
                        li.Add(Btn(i.zone == CheckZone.Outside ? "Inspect" : i.id == CheckId.Harness ? "Fasten" : i.id == CheckId.Lookout ? "Look round" : i.id == CheckId.Levers ? "Set" : "Switch on", "small", () => PreAction(id), disabled: busy));
                    preflight.Add(li);
                }
                if (Pre.stage == PreflightStage.Cockpit) preflight.Add(T((Pre.ZoneDone(CheckZone.Outside) ? "Walk-round complete ✓. " : "") + "Move the controls yourself to check them: they tick when they reach full travel.", "small muted"));
                preflight.Add(Row(Pre.stage == PreflightStage.Outside ? Btn("Climb aboard", "primary small", ClimbAboard, disabled: !Pre.ZoneDone(CheckZone.Outside)) : null, Btn("Skip checks", "ghost small", SkipChecks)));
            }
            // autopilot and crew actions
            crewbar.Clear();
            var actions = m.Actions(s);
            if (ac.equipment.autopilot)
                foreach (var (md, lab) in new[] { (APMode.Alt, "ALT"), (APMode.Hdg, "HDG"), (APMode.Hover, "HVR") })
                {
                    var on = md == APMode.Alt ? m.ap.alt : md == APMode.Hdg ? m.ap.hdg : m.ap.hover;
                    var mm = md;
                    crewbar.Add(HB(lab, "ap" + (on ? " on" : ""), () => ApToggle(mm)).With(b => b.tooltip = $"Autopilot {lab} ({(int)md + 1})"));
                }
            foreach (var act in actions)
            {
                var id = act.id;
                var b = HB(act.label, "crew" + (act.on == true ? " on" : "") + (id == CrewActionId.Transmit || (id == CrewActionId.Winch && act.enabled) ? " start" : ""), () =>
                {
                    if (id == CrewActionId.Transmit) m.Transmit(s); else if (id == CrewActionId.Winch) m.Winch(s); else m.ToggleFlir();
                    hudDirty = true;
                });
                b.tooltip = act.hint;
                b.SetEnabled(act.enabled || id == CrewActionId.Winch);
                crewbar.Add(b);
            }
            var aps = Autopilot.ApStatus(m.ap);
            if (!string.IsNullOrEmpty(aps)) crewbar.Add(T(aps, "apstat mono"));
            crewbar.Show(crewbar.childCount > 0);
            // switches
            switches.Clear();
            switches.Show(showSwitches);
            if (showSwitches)
            {
                var list = new List<(SwitchName sw, string label, string key, bool on)> { (SwitchName.Beacon, "BCN", "B", s.sys.beacon), (SwitchName.Nav, "NAV", "N", s.sys.nav), (SwitchName.Strobe, "STROBE", "O", s.sys.strobe), (SwitchName.LandingLight, "LDG LT", "L", s.sys.landingLight) };
                if (ac.equipment.searchlight) list.Add((SwitchName.Searchlight, "SRCH", "H", s.sys.searchlight));
                list.Add((SwitchName.Governor, "GOV", "G", s.sys.governor));
                foreach (var (sw, label, key, on) in list)
                {
                    var ss = sw;
                    var b = new Button(() => { FlightModel.CommandSwitch(s, ss); hudDirty = true; }).Cls("sw" + (on ? " on" : ""));
                    b.Add(E("sw-lamp")); b.Add(T(label)); b.Add(T(key, "sw-kbd"));
                    switches.Add(b);
                }
                if (ac.retractableGear)
                {
                    var gb = new Button(ToggleGear).Cls("sw" + (s.sys.gearDown ? " on" : ""));
                    gb.Add(E("sw-lamp")); gb.Add(T(s.sys.gearDown ? "GEAR DN" : "GEAR UP")); gb.Add(T("Q", "sw-kbd"));
                    switches.Add(gb);
                }
                switches.Add(T($"THR {s.sys.throttle * 100:0}% · {FlightModel.TempLimitsOf(ac).label} {s.engineTemp:0}° · OIL {s.@out.oilPress:0}%", "swinfo mono"));
            }
            // decision
            decisionBox.Clear();
            decisionBox.Show(dec != null);
            if (dec != null)
            {
                decisionBox.Add(T(dec.Value.prompt, "decision-q"));
                foreach (var (id, label) in dec.Value.options) { var oid = id; decisionBox.Add(Btn(label, "small", () => { m.Choose(oid, s); hudDirty = true; })); }
            }
            // hint
            hintBox.Clear();
            var showHint = hint != null && HINTS.ContainsKey(hint) && dec == null && !showTablet;
            hintBox.Show(showHint);
            if (showHint) { var (ttl, txt) = HINTS[hint]; hintBox.Add(Up(ttl, "hintcard-ttl")); hintBox.Add(T(txt, "hintcard-txt")); hintBox.Add(Row(Btn("Got it", "small", () => { hint = null; hudDirty = true; })).With(r => r.style.justifyContent = Justify.FlexEnd)); }
            // trim chip
            trimChip.Clear();
            trimChip.Show(heli.Input.Trimmed && !Paused);
            if (heli.Input.Trimmed)
            {
                trimChip.Add(T(heli.Input.trimReleasing ? "TRIM RELEASING" : "TRIM SET", "trimchip-b"));
                if (!heli.Input.trimReleasing) trimChip.Add(T($"release · {(heli.Input.lastSource == InputSourceKind.Controller ? "RT" : App.Settings.controls.KeyFor("trimRelease").ToString())}", "trimchip-s"));
                trimChip.EnableInClassList("releasing", heli.Input.trimReleasing);
            }
            // completion banner
            banner.Clear();
            var done = m.status == MissionStatus.Complete && !Paused && !s.crashed;
            banner.Show(done);
            if (done) banner.Add(E("banner-box", T(m.objectives.Any(x => x.status == ObjectiveStatus.Failed) ? "Flight items complete" : "Exercise complete"), Btn("Debrief", "primary small", () => Finish(FlightEnd.Complete))));
            // tablet
            tablet.Show(showTablet && Director != null && !Paused);
            if (showTablet && Director != null) BuildTablet(); else tabWasOpen = false;
            // accident, pause
            center.Clear();
            center.Show(crashReady || Paused);
            if (crashReady)
                center.Add(E("card", H2("Accident", "err"), P($"{s.crashReason}."), Muted("Nobody gets hurt in the simulator. Read the debrief, then try again."), Row(Btn("Debrief", "primary", () => Finish(FlightEnd.Crash)))));
            else if (Paused) center.Add(PauseCard());
        }

        Button HB(string text, string cls, Action click)
        {
            var b = new Button(click) { text = text.ToUpperInvariant() };
            b.AddToClassList("hb");
            foreach (var c in (cls ?? "").Split(' ')) if (c.Length > 0) b.AddToClassList(c);
            return b;
        }

        VisualElement PauseCard()
        {
            var app = App; var st = app.Settings;
            void Change(Action<GameSettings> f) { var n = app.Settings.Clone(); f(n); app.CommitSettings(n); hudDirty = true; }
            var pads = UnityEngine.InputSystem.InputSystem.devices.Where(d => d is UnityEngine.InputSystem.Joystick || d is UnityEngine.InputSystem.Gamepad).Select(d => d.description.product ?? d.displayName).ToList();
            var free = Director != null;
            return E("card pause-card",
                H2("Paused"),
                pads.Count > 0 ? Muted($"Controller: {string.Join(" + ", pads)} · layout {heli.ControllerName}", "small") : null,
                Muted(Setup.mission.title),
                Field("Handling", Seg(new[] { (AssistMode.Beginner, "Beginner"), (AssistMode.Standard, "Standard"), (AssistMode.Realistic, "Realistic") }, st.assist, v => Change(n => n.assist = v))),
                Check("Training aids (target markers, height readout)", st.showTrainingAids, v => Change(n => n.showTrainingAids = v)),
                Check("Sound", st.sound, v => Change(n => n.sound = v)),
                Slider("Volume", 0, 1, st.volume, v => $"{v * 100:0}%", v => Change(n => n.volume = v), 0.05f),
                Row(Btn("Resume", "primary", () => TogglePause(false)), Btn("Restart", "", Restart), Btn(free ? "End shift" : "End flight", "danger", () => Finish(Runtime.status == MissionStatus.Complete || free ? FlightEnd.Complete : FlightEnd.Aborted))),
                Muted(free ? "Ending the shift shows the shift report. A job in progress is stood down." : "Ending early records the flight as incomplete. Restart discards it.", "small"));
        }
    }
}
