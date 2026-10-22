// The crew tablet for freeplay (port of src/ui/Tablet.tsx): incoming callouts, the job in hand, a moving map and
// the shift log. Opened with Tab (keyboard) or LT (controller); flying carries on underneath.
using System;
using System.Linq;
using Rotorcraft.Game;
using Rotorcraft.Presentation.UI;
using Rotorcraft.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static Rotorcraft.Presentation.UI.Ui;

namespace Rotorcraft.Presentation
{
    public sealed partial class FlightSession
    {
        enum TabPage { Calls, Job, Map, Log }
        static readonly (TabPage id, string label)[] TABS = { (TabPage.Calls, "Callouts"), (TabPage.Job, "Current job"), (TabPage.Map, "Map"), (TabPage.Log, "Shift log") };
        TabPage tabPage = TabPage.Calls;
        string tabSel, tabMsg;
        bool tabConfirm;
        MapView tabMap;
        Label tabClock, tabFuel, tabEarn;
        bool tabWasOpen;
        float tabT;

        static string Mmss(double s) { s = Math.Max(0, s); return $"{Math.Floor(s / 60)}:{Math.Floor(s % 60):00}"; }
        static string SMoney(double n) => (n < 0 ? "−" : "") + FreeplayData.Money(Math.Abs(n));

        void BuildTablet()
        {
            var d = Director; var s = heli.State; var m = Runtime; var ac = heli.Aircraft; var kit = d.cfg.kit;
            if (!tabWasOpen) { tabPage = d.active != null ? TabPage.Job : TabPage.Calls; tabWasOpen = true; }
            tablet.Clear();
            tabClock = T(Mmss(s.t)); tabFuel = T($"FUEL {s.fuel:0} kg"); tabEarn = T(SMoney(d.earned - d.fuelCost), "earn");
            var status = E("tab-status", T("KINLOCH OPS · MDT"), tabClock, tabFuel, tabEarn, Btn("Close", "small", () => { showTablet = false; hudDirty = true; }).With(b => b.style.marginLeft = 10));
            var tabs = E("tab-tabs");
            foreach (var (id, label) in TABS)
            {
                var page = id;
                var b = new Button(() => { tabPage = page; tabMsg = null; hudDirty = true; }) { text = label.ToUpperInvariant() }.Cls("tab-tab" + (tabPage == id ? " current" : ""));
                if (id == TabPage.Calls && d.offers.Count > 0) b.Add(T(d.offers.Count.ToString(), "tab-badge"));
                if (id == TabPage.Job && d.active != null) b.Add(T("●", "tab-badge").With(x => x.style.backgroundColor = (Color)new Color32(0x4c, 0xc3, 0x8a, 255)));
                tabs.Add(b);
            }
            var screen = E("tab-screen");
            var scroll = new ScrollView(ScrollViewMode.Vertical); scroll.style.flexGrow = 1;
            if (tabMsg != null) scroll.Add(T(tabMsg, "tab-msg"));
            switch (tabPage)
            {
                case TabPage.Calls:
                    if (d.offers.Count == 0) scroll.Add(Muted("No callouts waiting. Keep flying: the next will come through here and on the radio."));
                    foreach (var c in d.offers.OrderBy(x => x.priority)) scroll.Add(CallCard(c, s, ac, kit));
                    break;
                case TabPage.Job:
                    var a = d.active;
                    if (a == null) { scroll.Add(Muted("No job in hand. Accept a callout from the Callouts tab.")); break; }
                    scroll.Add(RowBetween(
                        Stack(Row(T($"P{a.callout.priority}", $"prio p{a.callout.priority}"), T(a.callout.title, "call-ttl-b").With(x => x.style.marginLeft = 7)), Muted(a.callout.place + (a.callout.destination != null ? $" → {a.callout.destination}" : ""), "small")),
                        Stack(T($"Accepted {Mmss(s.t - a.acceptedAt)} ago", "mono small"), T($"Respond within {a.callout.respondMin:0} min{(a.arrivedAt != null ? " · on scene ✓" : "")}", "mono small")).With(x => x.style.alignItems = Align.FlexEnd)));
                    var nt = m.NavTargetNow();
                    if (nt != null)
                    {
                        var bd = WorldMap.BearingDistance(s.pos.x, s.pos.z, nt.x, nt.z);
                        scroll.Add(T($"NEXT ▸ {nt.name} · {Math.Round(bd.brg) % 360:000}° · {(bd.dist < 1852 ? $"{bd.dist:0} m" : $"{bd.dist / 1852:F1} nm")}", "navline mono").With(x => x.style.marginTop = 7));
                    }
                    var steps = E("").With(x => x.style.marginTop = 7);
                    foreach (var it in m.JobItems(a.job))
                    {
                        var st = it.status.ToString().ToLowerInvariant();
                        var tx = E("step-tx", T(it.def.label));
                        if (it.status == ObjectiveStatus.Active && !string.IsNullOrEmpty(it.detail)) tx.Add(T(it.detail, "step-s"));
                        steps.Add(E("step " + st, T(it.status == ObjectiveStatus.Complete ? "✓" : it.status == ObjectiveStatus.Failed ? "✕" : it.status == ObjectiveStatus.Active ? "▶" : "○", "step-ic"), tx));
                    }
                    scroll.Add(steps);
                    foreach (var p in a.callout.details) scroll.Add(Muted(p, "small"));
                    if (!tabConfirm) scroll.Add(Row(Btn("Stand down", "small danger", () => { tabConfirm = true; hudDirty = true; })));
                    else scroll.Add(Row(T("Stand down from this job? It pays nothing.", "small"), Btn("Yes, stand down", "small danger", () => { tabConfirm = false; Director.Abandon(heli.State.t, Runtime, heli.State); SyncActors(); hudDirty = true; }), Btn("Cancel", "small", () => { tabConfirm = false; hudDirty = true; })));
                    break;
                case TabPage.Map:
                    tabMap = new MapView(); tabMap.AddToClassList("tab-map");
                    scroll.Add(tabMap);
                    tabT = 0;
                    break;
                case TabPage.Log:
                    scroll.Add(Row(T($"Jobs {d.history.Count(h => h.outcome == JobOutcome.Completed)}", "mono small"), T($"Earned {SMoney(d.earned)}", "mono small"), T($"Fuel {SMoney(-d.fuelCost)}", "mono small"), T($"Net {SMoney(d.earned - d.fuelCost)}", "mono small")));
                    if (d.history.Count == 0) scroll.Add(Muted("Nothing flown yet this shift."));
                    else
                    {
                        var rows = d.history.AsEnumerable().Reverse().Select(h => new VisualElement[] { T(h.title), T(h.outcome.ToString().ToLowerInvariant(), h.outcome == JobOutcome.Completed ? "" : "err"), T($"{h.quality:0}%", "num"), T(FreeplayData.Money(h.pay), "num"), Muted(string.Join(" · ", h.notes ?? new System.Collections.Generic.List<string>()), "small") }).ToList();
                        scroll.Add(Table(new[] { "Job", "Outcome", "Quality", "Pay", "" }, new[] { 2.2f, 1f, 0.8f, 0.9f, 2f }, rows, new[] { false, false, true, true, false }));
                    }
                    break;
            }
            if (tabPage != TabPage.Map) tabMap = null;
            screen.Add(scroll);
            var fuelSite = d.RefuelSite(s);
            var foot = E("tab-foot",
                fuelSite != null ? Btn(d.refuelling ? "Stop refuelling" : $"Refuel at {fuelSite.name}", "small" + (d.refuelling ? " primary" : ""), () => { d.refuelling = !d.refuelling && d.RefuelSite(heli.State) != null; hudDirty = true; }) : (VisualElement)Muted("Refuel on the ground at Kinloch or on a platform.", "small"),
                Muted("Tab / LT closes · [ ] or LB/RB change page", "small"));
            tablet.Add(E("tab-bezel", status, tabs, screen, foot));
        }

        VisualElement CallCard(Callout c, Rotorcraft.Sim.HeliState s, Rotorcraft.Sim.AircraftConfig ac, System.Collections.Generic.List<KitId> kit)
        {
            var bd = WorldMap.BearingDistance(s.pos.x, s.pos.z, c.x, c.z);
            var why = FreeplayData.CannotAccept(c.requires, c.needsTwin, ac, kit);
            var open = tabSel == c.id;
            var ete = bd.dist / (ac.engines == 2 ? 64 : 44) / 60;
            var id = c.id;
            var head = new Button(() => { tabSel = open ? null : id; hudDirty = true; }).Cls("call-head");
            head.Add(T($"P{c.priority}", $"prio p{c.priority}"));
            head.Add(E("call-ttl", T(c.title, "call-ttl-b"), T(c.place, "call-ttl-s")));
            head.Add(E("call-meta", T($"{Math.Round(bd.brg) % 360:000}° {bd.dist / 1852:F1} nm", "mono"), T(FreeplayData.Money(c.pay), "mono")));
            var card = E($"call p{c.priority}" + (open ? " open" : ""), head);
            if (open)
            {
                var body = E("call-body");
                body.Add(T($"From {c.from} · expires in {Mmss(c.expiresAt - s.t)} · respond within {c.respondMin:0} min · ETE ≈ {Math.Max(1, Math.Round(ete))} min{(c.destination != null ? $" · to {c.destination}" : "")}", "mono small"));
                foreach (var p in c.details) body.Add(P(p));
                var kl = Row();
                foreach (var k in c.requires) kl.Add(Chip((kit.Contains(k) ? "✓ " : "✕ ") + Loadout.ById(k).@short, kit.Contains(k) ? "ok" : "warn"));
                if (c.needsTwin) kl.Add(Chip("Twin", ac.engines == 2 ? "ok" : "warn"));
                foreach (var k in c.recommended) kl.Add(Chip((kit.Contains(k) ? "✓ " : "○ ") + Loadout.ById(k).@short + " (advised)", kit.Contains(k) ? "ok" : ""));
                body.Add(kl);
                body.Add(Row(Btn(Director.active != null ? "Busy on a job" : why ?? "Accept", "primary small", () =>
                {
                    var w = Director.Accept(id, heli.State.t, Runtime, heli.State);
                    tabMsg = w;
                    if (w == null) { tabPage = TabPage.Job; SyncActors(); }
                    hudDirty = true;
                }, disabled: why != null || Director.active != null)));
                card.Add(body);
            }
            return card;
        }

        /// <summary>Live parts of the open tablet: the clock, fuel, earnings, the moving map, and the page keys.</summary>
        void UpdateTablet()
        {
            var d = Director; var s = heli.State;
            if (tabClock != null) { tabClock.text = Mmss(s.t); tabFuel.text = $"FUEL {s.fuel:0} kg"; tabEarn.text = SMoney(d.earned - d.fuelCost); }
            var kb = Keyboard.current; var pad = Gamepad.current;
            var step = 0;
            if ((kb != null && kb.rightBracketKey.wasPressedThisFrame) || (pad != null && pad.rightShoulder.wasPressedThisFrame)) step = 1;
            if ((kb != null && kb.leftBracketKey.wasPressedThisFrame) || (pad != null && pad.leftShoulder.wasPressedThisFrame)) step = -1;
            if (step != 0) { var i = Array.FindIndex(TABS, x => x.id == tabPage); tabPage = TABS[(i + step + TABS.Length) % TABS.Length].id; hudDirty = true; }
            if (tabMap != null && Time.unscaledTime - tabT > 0.3f)
            {
                tabT = Time.unscaledTime;
                var nt = Runtime.NavTargetNow();
                tabMap.Options = new MapOptions
                {
                    aircraft = (s.pos.x, s.pos.z, s.@out.hdg), center = (s.pos.x, s.pos.z), metresPerPx = 16000.0 / Math.Max(200, tabMap.contentRect.width),
                    target = nt != null ? (nt.x, nt.z, nt.name) : ((double, double, string)?)null, search = Runtime.SearchOverlay(),
                    callouts = d.offers.Select(c => (c.x, c.z, c.title, false, c.priority)).Concat(d.active != null ? new[] { (d.active.callout.x, d.active.callout.z, d.active.callout.title, true, d.active.callout.priority) } : new (double, double, string, bool, int)[0]).ToList(),
                };
                tabMap.Redraw();
            }
        }
    }
}
