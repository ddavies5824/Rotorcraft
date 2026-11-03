// Freeplay and company screens (port of src/ui/freeplayScreens.tsx and companyScreens.tsx).
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
        static readonly (string id, string label, bool twinOnly)[] FP_BASES =
        {
            ("kin_h1", "Kinloch Heliport (H1)", false), ("kin_h2", "Kinloch Heliport (H2, twin pad)", true),
            ("inverhallan", "Inverhallan Infirmary (air ambulance base)", true), ("ardoch_mrt", "Ardoch Mountain Rescue base", false),
        };
        static readonly (TimeOfDay, string)[] TIMES = { (TimeOfDay.Day, "Day"), (TimeOfDay.Dusk, "Dusk"), (TimeOfDay.Night, "Night") };
        static IEnumerable<(string, string)> WxChoices() => new[] { ("random", "Random (flyable)") }.Concat(Weather.Presets.Values.Select(w => (w.id, w.name)));
        static readonly System.Random Rand = new System.Random();
        static string RandomWx(int n) { var ids = new[] { "calm", "light_sw", "breezy_w", "crosswind_n", "calm", "light_sw" }.Where(Weather.Presets.ContainsKey).ToArray(); return ids[Rand.Next(Math.Min(n, ids.Length))]; }
        static string Pct(double v) => $"{Math.Round(v * 100)}%";

        static VisualElement SkinPicker(AircraftConfig ac, string value, Action<string> onChange)
        {
            var seg = E("seg");
            var cur = value ?? ac.skins?.FirstOrDefault()?.id;
            foreach (var s in ac.skins ?? new Skin[0])
            {
                var ss = s;
                Color Col(double[] c) => new Color(Mathf.Pow(Mathf.Min(1, (float)c[0]), 1 / 2.2f), Mathf.Pow(Mathf.Min(1, (float)c[1]), 1 / 2.2f), Mathf.Pow(Mathf.Min(1, (float)c[2]), 1 / 2.2f));
                var sw = E("row nowrap");
                foreach (var (c, w) in new[] { (s.livery.body, 14), (s.livery.stripe, 7), (s.livery.tail, 7) }) { var v = E(); v.style.width = w; v.style.height = 14; v.style.backgroundColor = Col(c); v.style.marginRight = 0; sw.Add(v); }
                sw.style.marginRight = 6;
                var b = new Button(() => onChange(ss.id)).Cls("seg-btn");
                b.style.flexDirection = FlexDirection.Row; b.style.alignItems = Align.Center;
                b.Add(sw); b.Add(T(System.Text.RegularExpressions.Regex.Replace(s.name, @" \(.*\)$", "").ToUpperInvariant()));
                if (cur == s.id) b.AddToClassList("on");
                b.tooltip = s.name;
                seg.Add(b);
            }
            return seg;
        }

        static VisualElement KitRow(string name, string desc, string right, bool on, bool enabled, Action toggle)
        {
            var row = E("kitrow" + (enabled ? "" : " na"), E("check-box" + (on ? " on-box" : ""), T(on ? "✓" : "", "check-tick")).With(b => { if (on) { b.style.backgroundColor = (Color)new Color32(0x5f, 0xd0, 0x8a, 255); b.style.borderTopColor = b.style.borderBottomColor = b.style.borderLeftColor = b.style.borderRightColor = (Color)new Color32(0x5f, 0xd0, 0x8a, 255); } }),
                E("kitrow-main", T(name, "b"), Muted(desc, "small")), T(right, "mono small"));
            if (enabled) { row.RegisterCallback<ClickEvent>(_ => toggle()); row.focusable = true; row.RegisterCallback<NavigationSubmitEvent>(_ => toggle()); }
            return row;
        }

        // ------------------------------------------------------------------------------------ freeplay
        static VisualElement FreeplaySetup(GameApp app)
        {
            var last = app.Freeplay.last;
            var acId = St(app, "ac", last?.aircraftId ?? "guardian");
            var b = Aircraft.Get(acId);
            var kit = St(app, "kit", last != null && last.aircraftId == acId ? last.kit.ToList() : Loadout.DefaultKit(b));
            app.ScreenState["kit"] = kit;
            var baseId = St(app, "base", last?.baseSiteId ?? (b.engines == 2 ? "kin_h2" : "kin_h1"));
            var tod = St(app, "tod", last?.tod ?? TimeOfDay.Day);
            var wx = St(app, "wx", "random");
            var pace = St(app, "pace", last?.pace ?? FreeplayPace.Normal);
            var skin = St(app, "skin", last?.aircraftId == acId ? last?.skin : null);
            var fuelFrac = St(app, "fuelFrac", 0.8f);
            var fitted = Loadout.Fit(b, kit);
            var cap = fitted.ac.fuelCapacity;
            var fuel = Math.Round(cap * fuelFrac);
            var mass = b.emptyMass + b.crewMass + fitted.kitKg + fuel;
            var over = mass > b.maxGross;
            void PickAircraft(string id)
            {
                var ac = Aircraft.Get(id);
                app.ScreenState["ac"] = id; app.ScreenState["kit"] = Loadout.DefaultKit(ac); app.ScreenState["skin"] = null;
                var bs = FP_BASES.FirstOrDefault(x => x.id == baseId);
                if (bs.twinOnly && ac.engines < 2) app.ScreenState["base"] = "kin_h1";
                if (ac.engines == 2 && baseId == "kin_h1") app.ScreenState["base"] = "kin_h2";
                app.Later(app.Refresh);
            }
            var picks = E("acrow");
            foreach (var ac in Aircraft.All.Values)
            {
                var id = ac.id;
                var bt = new Button(() => PickAircraft(id)).Cls("acpick" + (id == acId ? " on" : ""));
                var pic = AircraftPicture(id); if (pic != null) { pic.style.height = 110; pic.style.width = Length.Percent(100); pic.style.marginBottom = 6; bt.Add(pic); }
                bt.Add(T(ac.name, "acpick-nm")); bt.Add(Muted(ac.realWorldClass, "small"));
                bt.Add(T($"{ac.engines} engine{(ac.engines > 1 ? "s" : "")} · payload {ac.maxPayload:0} kg · fuel {ac.fuelCapacity:0} kg", "mono small"));
                picks.Add(bt);
            }
            var kitList = E("list");
            foreach (var k in Loadout.KIT)
            {
                var kk = k;
                var fits = Loadout.CanFit(b, k.id);
                var m = fits ? k.mass[acId] : 0;
                kitList.Add(KitRow(k.name, k.description, !fits ? "n/a" : m == 0 ? "built in" : $"{m:0} kg", fits && kit.Contains(k.id), fits, () =>
                {
                    var l = St(app, "kit", new List<KitId>()).ToList();
                    if (l.Contains(kk.id)) l.Remove(kk.id); else l.Add(kk.id);
                    Set(app, "kit", l);
                }));
            }
            var loadout = Panel(H3("Loadout"), kitList);
            foreach (var p in fitted.problems) loadout.Add(T(p, "err small"));
            var shift = Panel();
            if (b.skins != null) shift.Add(Field("Paint scheme", SkinPicker(b, skin, v => Set(app, "skin", v))));
            shift.Add(H3("Shift"));
            shift.Add(Field("Base", Seg(FP_BASES.Where(x => !x.twinOnly || b.engines == 2).Select(x => (x.id, x.label)), baseId, v => Set(app, "base", v))));
            shift.Add(Field("Time", Seg(TIMES, tod, v => Set(app, "tod", v))));
            shift.Add(Field("Weather", Seg(WxChoices(), wx, v => Set(app, "wx", v))));
            shift.Add(Field("Callouts", Seg(new[] { (FreeplayPace.Off, "Free roam"), (FreeplayPace.Quiet, "Quiet"), (FreeplayPace.Normal, "Normal"), (FreeplayPace.Busy, "Busy") }, pace, v => Set(app, "pace", v))));
            shift.Add(Slider($"Fuel{(kit.Contains(KitId.AuxFuel) ? $" (aux +{Loadout.AUX_FUEL_KG:0})" : "")}", 0.15f, 1, fuelFrac, v => $"{Math.Round(cap * v):0} kg of {cap:0} kg", v => Set(app, "fuelFrac", v), 0.05f));
            shift.Add(Kv(("All-up weight", $"{Math.Round(mass)} kg of {b.maxGross:0} kg{(over ? " · OVERWEIGHT" : "")}", over ? "err" : null), ("Kit", $"{fitted.kitKg:0} kg", null),
                ("Spare lift", $"{Loadout.SpareLift(b, fitted.kitKg):0} kg for patients, passengers or freight", null), ("Starts at", WorldMap.SiteById(baseId).name, "body")));
            shift.Add(Row(Btn("Start shift", "primary", () =>
            {
                var wxId = wx == "random" ? RandomWx(6) : wx;
                app.FlyFreeplay(new FreeplayConfig { aircraftId = acId, kit = kit.ToList(), baseSiteId = baseId, fuelKg = fuel, tod = tod, weatherId = wxId, pace = pace, seed = Rand.Next(), skin = skin != null && (b.skins?.Any(s => s.id == skin) ?? false) ? skin : null });
            }, disabled: fitted.problems.Count > 0), over ? T("Over maximum weight: take less fuel or kit, or the take-off will be marked.", "caution-t small") : null));
            var page = E("page",
                E("page-head", Tight(H1("Freeplay"), Muted("Pick an aircraft and its kit, then fly a shift. Callouts arrive at random on the crew tablet: accept the ones you are equipped for. Freeplay does not affect your career.")).With(x => { x.style.flexShrink = 1; x.style.maxWidth = 760; }),
                    Btn("Back", "", () => app.Go(app.Career != null ? "hq" : "menu"))),
                picks, E("grid2", loadout, shift));
            var r = app.Freeplay;
            if (r.shifts > 0) page.Add(Panel(Stats(Stat(r.shifts.ToString(), "Shifts"), Stat(r.jobs.ToString(), "Jobs done"), Stat(Money(r.earned), "Net earnings"), Stat(Money(r.bestShift), "Best shift"), Stat($"{r.seconds / 3600:F1}", "Hours"))));
            page.Add(Panel(H3("On the tablet"), Muted("Open it with Tab or LT on a controller. Each callout lists the kit it needs; the hoist opens up cliff and sea rescues, the medical fit is needed for patients, floats for anything offshore, the freight rack for cargo and the passenger cabin for crew changes. Refuel on the ground at Kinloch or on a platform from the tablet.", "small")));
            return page;
        }

        static VisualElement FreeplayReport(GameApp app, Route route)
        {
            var sum = route.summary;
            var done = sum.jobs.Where(j => j.outcome == JobOutcome.Completed).ToList();
            var net = sum.earned - sum.fuelCost;
            var page = E("page", E("page-head", H1("Shift report"), Muted(Aircraft.Get(sum.aircraftId).name)),
                Panel(Stats(Stat(done.Count.ToString(), "Jobs completed"), Stat(Money(sum.earned), "Earned"), Stat(Money(-sum.fuelCost), "Fuel"), Stat(Money(net), "Net", net < 0 ? "err" : null), Stat($"{sum.flightSeconds / 60:0}", "Minutes airborne"))));
            page.Add(sum.jobs.Count == 0 ? Panel(Muted("No jobs taken this shift."))
                : Panel(Table(new[] { "Job", "Result", "Quality", "Minutes", "Pay", "Notes" }, new[] { 2.4f, 1, 0.8f, 0.8f, 0.9f, 2.4f },
                    sum.jobs.Select(j => new VisualElement[] { Cell(j.title), T(j.outcome.ToString().ToLowerInvariant(), j.outcome == JobOutcome.Completed ? "td" : "td err"), Cell($"{j.quality:0}%", true), Cell($"{j.minutes:0}", true), Cell(Money(j.pay), true), Muted(string.Join(" · ", j.notes), "small") }),
                    new[] { false, false, true, true, true, false })));
            var r = app.Freeplay;
            page.Add(Panel(Stats(Stat(r.shifts.ToString(), "Shifts in total"), Stat(r.jobs.ToString(), "Jobs in total"), Stat(Money(r.earned), "All-time net"), Stat(Money(r.bestShift), "Best shift"))));
            var cfg = route.cfg;
            page.Add(Row(Btn("Fly another shift", "primary", () => { var n = cfg.Clone(); n.seed = Rand.Next(); app.FlyFreeplay(n); }), Btn("Change loadout", "", () => app.Go("freeplay")), Btn("Menu", "", () => app.Go("menu"))));
            return page;
        }

        // ------------------------------------------------------------------------------------- company
        static VisualElement CompanyStart(GameApp app)
        {
            var name = St(app, "coname", "Kinloch Helicopters");
            var pkg = St(app, "pkg", StartPackage.Owner);
            var picks = E("grid2");
            foreach (var kv in Companies.PACKAGES)
            {
                var k = kv.Key; var P = kv.Value;
                var bt = new Button(() => Set(app, "pkg", k)).Cls("acpick" + (pkg == k ? " on" : ""));
                bt.Add(T(P.label, "acpick-nm")); bt.Add(T($"{Money(P.funds)} cash{(P.loan > 0 ? $" · {Money(P.loan)} loan" : "")}", "mono small")); bt.Add(Muted(P.text, "small"));
                picks.Add(bt);
            }
            return E("page",
                E("page-head", Tight(H1("Start a company"), Muted("Run your own helicopter operator: buy aircraft, fit them out, lease bases, win contracts and fly the jobs yourself. Company mode has its own save and never changes your career.")).With(x => { x.style.flexShrink = 1; x.style.maxWidth = 760; }), Btn("Back", "", () => app.Go("menu"))),
                Panel(Field("Company name", Text(name, 40, "Company name", v => app.ScreenState["coname"] = v)), picks,
                    Row(Btn($"Found {(string.IsNullOrWhiteSpace(name) ? "the company" : name.Trim())}", "primary", () => { var n = St(app, "coname", name); if (string.IsNullOrWhiteSpace(n)) return; app.CommitCompany(Companies.CreateCompany(n, St(app, "pkg", StartPackage.Owner), CareerManager.Now())); app.Later(app.Refresh); }))));
        }

        static VisualElement CompanyHub(GameApp app)
        {
            var c = app.Company;
            var flyUid = St<string>(app, "flyUid", null);
            if (flyUid != null && c.fleet.Any(a => a.uid == flyUid)) return ShiftSetup(app, flyUid);
            var tab = St(app, "tab", c.fleet.Count > 0 ? "overview" : "market");
            var msg = St<string>(app, "msg", null);
            void Act(CompanyResult r) { if (r.ok) { app.CommitCompany(r.company); app.ScreenState["msg"] = null; } else app.ScreenState["msg"] = r.reason; app.Later(app.Refresh); }
            var fleetValue = c.fleet.Sum(Companies.AircraftValue);
            var page = E("page",
                E("page-head", Tight(H1(c.name), Muted($"Shift {c.shift} · reputation {Math.Round(c.reputation)} / 100 (pay ×{Companies.RepMultiplier(c.reputation):F2})")), Btn("Menu", "", () => app.Go("menu"))),
                Panel(Stats(Stat(Money(c.funds), "Funds", c.funds < 0 ? "err" : null), Stat(Money(c.loan), "Loan"), Stat(Money(fleetValue), "Fleet value"), Stat(c.fleet.Count.ToString(), "Aircraft"), Stat(c.stats.jobs.ToString(), "Jobs done"), Stat($"{c.stats.hours:F1}", "Hours flown"))));
            var tabs = E("subtabs");
            foreach (var (id, l) in new[] { ("overview", "Overview"), ("fleet", "Fleet"), ("market", "Market"), ("bases", "Bases"), ("contracts", "Contracts"), ("finance", "Finance") })
            {
                var i = id;
                var b = new Button(() => { app.ScreenState["tab"] = i; app.ScreenState["msg"] = null; app.Later(app.Refresh); }) { text = l.ToUpperInvariant() }.Cls("subtab" + (tab == id ? " current" : ""));
                tabs.Add(b);
            }
            page.Add(tabs);
            if (msg != null) page.Add(Panel(Row(Chip("Note", "caution"), T(msg))));
            VisualElement FleetLine(OwnedAircraft a)
            {
                var why = Companies.Grounded(a);
                return RowBetween(Row(T(a.reg, "mono b"), T(Aircraft.Get(a.aircraftId).name), Muted($"at {Companies.BASES.FirstOrDefault(x => x.siteId == a.baseId)?.name}", "small")),
                    why != null ? T(why, "caution-t small") : Btn("Fly", "small primary", () => Set(app, "flyUid", a.uid)));
            }
            if (tab == "overview")
            {
                var left = Panel(H3("Fly a shift"));
                if (c.fleet.Count == 0) { left.Add(Muted("No aircraft yet. Buy one in the market to start flying.")); left.Add(Row(Btn("Go to the market", "primary", () => Set(app, "tab", "market")))); }
                else foreach (var a in c.fleet) left.Add(FleetLine(a));
                left.Add(H3("Contracts"));
                if (c.contracts.Count == 0) left.Add(Muted("None signed. Contracts pay a retainer every shift and better rates for their jobs.", "small"));
                foreach (var id in c.contracts) { var k = Companies.Contract(id); left.Add(T($"{k.title} · {k.client} · {Money(k.retainer)} a shift", "small")); }
                var right = Panel(H3("Recent accounts"), Table(new[] { "#", "Item", "Amount" }, new[] { 0.4f, 3, 1 },
                    c.ledger.Take(18).Select(e => new VisualElement[] { Muted($"#{e.shift}", "small"), Cell(e.label), T(Money(e.amount), "td num" + (e.amount < 0 ? " err" : "")) }), new[] { false, false, true }));
                page.Add(E("grid2", left, right));
            }
            else if (tab == "fleet")
            {
                if (c.fleet.Count == 0) page.Add(Panel(Muted("No aircraft yet.")));
                foreach (var a in c.fleet)
                {
                    var aa = a;
                    var ac = Aircraft.Get(a.aircraftId);
                    var why = Companies.Grounded(a);
                    var svc = Companies.ServiceCost(a);
                    var built = Companies.BuiltInKit(a.aircraftId);
                    var p = Panel(RowBetween(T($"{a.reg} · {ac.name}", "h2"), why != null ? Chip(why, "caution") : Btn("Fly a shift", "primary", () => Set(app, "flyUid", aa.uid))),
                        Kv(("Hours", $"{a.hours:F1}", null), ("Condition", Pct(a.condition), a.condition < 0.5 ? "caution-t" : null), ("Damage", a.damage > 0 ? Pct(a.damage) : "none", a.damage > 0 ? "err" : null),
                            ("Value", $"{Money(Companies.AircraftValue(a))} (sells for {Money(Companies.SalePrice(a))})", null)));
                    if (ac.skins != null) p.Add(Field("Paint scheme", SkinPicker(ac, a.skin, id => Act(Companies.SetSkin(c, aa.uid, id)))));
                    p.Add(Field("Base", Seg(c.bases.Select(b => (b, Companies.BASES.First(x => x.siteId == b).name)), a.baseId, v => Act(Companies.SetBase(c, aa.uid, v)))));
                    var kl = E("list");
                    foreach (var k in Loadout.KIT.Where(k => Loadout.CanFit(ac, k.id)))
                    {
                        var kk = k; var has = a.kit.Contains(k.id); var isBuilt = built.Contains(k.id);
                        kl.Add(KitRow(k.name, k.description, isBuilt ? "built in" : has ? $"sell back {Money(Companies.FitPrice(a.aircraftId, k.id) * 0.5)}" : Money(Companies.FitPrice(a.aircraftId, k.id)), has, !isBuilt, () => Act(Companies.FitKit(c, aa.uid, kk.id, !has))));
                    }
                    p.Add(kl);
                    var confirmSell = St<string>(app, "sell", null) == a.uid;
                    p.Add(Row(Btn(svc > 0 ? $"Service and repair ({Money(svc)})" : "Serviceable", "small", () => Act(Companies.Service(c, aa.uid)), disabled: svc <= 0 || c.funds < svc),
                        !confirmSell ? Btn($"Sell ({Money(Companies.SalePrice(a))})", "small", () => Set(app, "sell", aa.uid))
                            : Row(T($"Sell {a.reg}?", "caution-t"), Btn("Yes, sell", "small danger", () => { app.ScreenState["sell"] = null; Act(Companies.Sell(c, aa.uid)); }), Btn("No", "small ghost", () => Set(app, "sell", null)))));
                    page.Add(p);
                }
            }
            else if (tab == "market")
            {
                page.Add(Panel(Muted("New aircraft from the factory and used ones from other operators. The list changes every few shifts. Buy outright, or put down 30% and finance the rest (the loan is charged interest each shift).", "small"),
                    Table(new[] { "Aircraft", "Reg", "Hours", "Condition", "Price", "Insurance / shift", "" }, new[] { 2.6f, 0.8f, 0.8f, 0.8f, 1.1f, 1, 1.8f },
                        c.market.Select(o =>
                        {
                            var ac = Aircraft.Get(o.aircraftId); var id = o.id;
                            return new VisualElement[] { Tight(T(ac.name, "b"), Muted($"{(o.used ? "used" : "new")} · {ac.realWorldClass}", "small")), T(o.reg, "mono"), Cell(N0(o.hours), true), Cell(Pct(o.condition), true), Cell(Money(o.price), true), Cell(Money(Companies.INSURANCE[o.aircraftId]), true),
                                Row(Btn("Buy", "small primary", () => Act(Companies.Buy(c, id, false)), disabled: c.funds < o.price), Btn("Finance", "small", () => Act(Companies.Buy(c, id, true)), disabled: c.funds < o.price * 0.3)) };
                        }), new[] { false, false, true, true, true, true, false })));
            }
            else if (tab == "bases")
            {
                var g = E("grid2");
                foreach (var bd in Companies.BASES)
                {
                    var on = c.bases.Contains(bd.siteId); var sid = bd.siteId;
                    var here = string.Join(", ", c.fleet.Where(a => a.baseId == bd.siteId).Select(a => a.reg));
                    g.Add(Panel(H3(bd.name), Muted(bd.note + (bd.twinOnly ? " Twins only." : ""), "small"), T(bd.lease > 0 ? $"{Money(bd.lease)} a shift · set-up {Money(bd.lease * 2)}" : "Included", "mono small"),
                        T(here.Length > 0 ? here : "No aircraft based here", "small"), bd.lease > 0 ? Row(Btn(on ? "End lease" : "Lease", on ? "small" : "small primary", () => Act(Companies.LeaseBase(c, sid, !on)))) : null));
                }
                page.Add(g);
            }
            else if (tab == "contracts")
            {
                var g = E("grid2");
                foreach (var k in Companies.CONTRACTS)
                {
                    var on = c.contracts.Contains(k.id); var ok = c.fleet.Any(a => Companies.Meets(a, k)); var kid = k.id;
                    var needs = string.Join(", ", new[] { k.needsTwin ? "twin" : "any aircraft" }.Concat(k.kit.Select(x => Loadout.ById(x).@short.ToLowerInvariant()))) + (k.bases != null ? "; based at " + string.Join(" or ", k.bases.Select(b => Companies.BASES.First(x => x.siteId == b).name)) : "");
                    var p = Panel(H3(k.title), Muted($"{k.client}. {k.text}", "small"),
                        Kv(("Retainer", $"{Money(k.retainer)} a shift", null), ("Needs", needs, "body"), ("Reputation", $"{k.minRep:0}+", c.reputation < k.minRep ? "err" : null)),
                        on ? Chip("Signed", "ok") : ok ? Chip("An aircraft qualifies") : Chip("No qualifying aircraft", "caution"),
                        Row(Btn(on ? "Cancel (reputation −4)" : "Sign", on ? "small" : "small primary", () => Act(Companies.SignContract(c, kid, !on)), disabled: !on && c.contracts.Count >= Companies.MAX_CONTRACTS)));
                    g.Add(p);
                }
                page.Add(g);
            }
            else if (tab == "finance")
            {
                var amt = St(app, "amt", 50_000f);
                var lim = Companies.LoanLimit(c);
                var costs = new List<VisualElement[]>();
                foreach (var a in c.fleet) costs.Add(new VisualElement[] { Cell($"Insurance {a.reg}"), Cell(Money(Companies.INSURANCE[a.aircraftId]), true) });
                foreach (var bd in c.bases.Select(b => Companies.BASES.First(x => x.siteId == b)).Where(b => b.lease > 0)) costs.Add(new VisualElement[] { Cell($"Lease {bd.name}"), Cell(Money(bd.lease), true) });
                if (c.loan > 0) costs.Add(new VisualElement[] { Cell("Loan interest"), Cell(Money(c.loan * Companies.LOAN_RATE), true) });
                page.Add(E("grid2",
                    Panel(H3("Loan"), Kv(("Owed", Money(c.loan)), ("Interest", $"{Companies.LOAN_RATE * 100:F1}% a shift ({Money(c.loan * Companies.LOAN_RATE)})"), ("Can borrow", $"{Money(lim)} more")),
                        Slider("Amount", 10_000, 2_000_000, amt, v => Money(v), v => Set(app, "amt", v), 10_000),
                        Row(Btn("Borrow", "small", () => Act(Companies.Borrow(c, amt)), disabled: amt <= 0 || amt > lim), Btn("Repay", "small", () => Act(Companies.Repay(c, Math.Min(amt, c.funds))), disabled: c.loan <= 0 || c.funds <= 0))),
                    Panel(H3("Running costs each shift"), Table(new[] { "Item", "Amount" }, new[] { 3f, 1 }, costs, new[] { false, true }),
                        Muted("Plus fuel and crew for the aircraft you fly. Aircraft wear with every hour and need servicing; a hard landing adds damage, and a crash writes the aircraft off (insurance pays 70% of its value).", "small"))));
            }
            var closing = St(app, "closing", false);
            page.Add(!closing ? Row(Btn("Close the company…", "small", () => Set(app, "closing", true)))
                : Row(T($"Close {c.name}? The company and its fleet are deleted.", "err"), Btn("Close it", "small danger", () => { app.CommitCompany(null); app.ScreenState.Clear(); app.Later(app.Refresh); }), Btn("Cancel", "small ghost", () => Set(app, "closing", false))));
            return page;
        }

        static VisualElement ShiftSetup(GameApp app, string uid)
        {
            var c = app.Company;
            var a = c.fleet.First(x => x.uid == uid);
            var b = Aircraft.Get(a.aircraftId);
            var fitted = Loadout.Fit(b, a.kit);
            var tod = St(app, "stod", TimeOfDay.Day);
            var wx = St(app, "swx", "random");
            var pace = St(app, "space", FreeplayPace.Normal);
            var fuelFrac = St(app, "sfuel", 0.8f);
            var fuel = Math.Round(fitted.ac.fuelCapacity * fuelFrac);
            var p = Panel(Field("Callouts", Seg(new[] { (FreeplayPace.Off, "Free roam"), (FreeplayPace.Quiet, "Quiet"), (FreeplayPace.Normal, "Normal"), (FreeplayPace.Busy, "Busy") }, pace, v => Set(app, "space", v))),
                Field("Time", Seg(TIMES, tod, v => Set(app, "stod", v))),
                Field("Weather", Seg(WxChoices(), wx, v => Set(app, "swx", v))),
                Slider($"Fuel{(a.kit.Contains(KitId.AuxFuel) ? $" (aux +{Loadout.AUX_FUEL_KG:0})" : "")}", 0.15f, 1, fuelFrac, v => $"{Math.Round(fitted.ac.fuelCapacity * v):0} kg of {fitted.ac.fuelCapacity:0} kg", v => Set(app, "sfuel", v), 0.05f),
                Muted($"Kit: {(a.kit.Count > 0 ? string.Join(", ", a.kit.Select(k => Loadout.ById(k).@short)) : "none")}. Change it in the Fleet tab.", "small"));
            foreach (var pr in fitted.problems) p.Add(T(pr, "err small"));
            p.Add(Row(Btn("Start shift", "primary", () =>
            {
                var wxId = wx == "random" ? RandomWx(4) : wx;
                app.ScreenState["flyUid"] = null;
                app.FlyCompanyShift(Companies.ShiftConfig(c, uid, tod, wxId, pace, fuel, Rand.Next()));
            }, disabled: fitted.problems.Count > 0)));
            return E("page", E("page-head", Tight(H1($"{a.reg} · {b.name}"), Muted($"From {Companies.BASES.FirstOrDefault(x => x.siteId == a.baseId)?.name}. Fly free, or take callouts from the tablet: pay goes to {c.name}.")),
                Btn("Back", "", () => Set(app, "flyUid", null))), p);
        }

        static VisualElement CompanyReport(GameApp app, Route route)
        {
            var c = app.Company;
            var r = route.report;
            if (c == null) return E("page", H1("Shift accounts"), Btn("Menu", "", () => app.Go("menu")));
            var lines = Table(new[] { "Item", "Amount" }, new[] { 3f, 1 }, r.lines.Select(l => new VisualElement[] { Cell(l.label), T(Money(l.amount), "td num" + (l.amount < 0 ? " err" : "")) })
                .Concat(new[] { new VisualElement[] { T("Net", "b td"), T(Money(r.net), "td num b") } }), new[] { false, true });
            var ac = Panel(H3($"{r.reg} · {r.name}"));
            if (r.writtenOff) ac.Add(T("Written off in the crash. The insurance payout is in the accounts.", "err"));
            else
            {
                ac.Add(T($"Condition {Pct(r.conditionBefore)} → {Pct(r.conditionAfter)}{(r.damage > 0 ? $" · damage {Pct(r.damage)}" : "")}", "small"));
                if (r.damage > 0.3) ac.Add(T("Grounded until repaired.", "caution-t small"));
            }
            return E("page", E("page-head", H1($"Shift {c.shift} accounts"), Muted(c.name)),
                Panel(Stats(Stat(Money(r.net), "Net for the shift", r.net < 0 ? "err" : null), Stat(Money(c.funds), "Funds now"), Stat($"{Math.Round(r.repBefore)} → {Math.Round(r.repAfter)}", "Reputation"), Stat($"{r.hours * 60:0}", "Minutes flown"))),
                E("grid2", Panel(lines), ac),
                Row(Btn("Back to the company", "primary", () => app.Go("company"))));
        }
    }
}
