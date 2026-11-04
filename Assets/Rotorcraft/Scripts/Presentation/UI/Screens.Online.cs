// Pilot login (Google sign-in for cloud saves and the pilot area) and the pilot area itself: leaderboards, the
// pilot directory and other pilots' profiles (port of src/ui/pilotScreens.tsx and CloudPanel.tsx).
using System;
using System.Collections.Generic;
using System.Linq;
using Rotorcraft.Game;
using UnityEngine;
using UnityEngine.UIElements;
using static Rotorcraft.Presentation.UI.Ui;

namespace Rotorcraft.Presentation.UI
{
    public static partial class Screens
    {
        static string Ago(long t)
        {
            if (t <= 0) return "—";
            var m = (int)Math.Round((CareerManager.Now() - t) / 60000.0);
            return m < 2 ? "just now" : m < 60 ? $"{m} min ago" : m < 48 * 60 ? $"{Math.Round(m / 60.0)} h ago" : $"{Math.Round(m / 1440.0)} days ago";
        }

        /// <summary>The one-line cloud status shown on the menu and the rail.</summary>
        static VisualElement CloudLine(GameApp app)
        {
            var c = Cloud.Instance;
            if (c == null || c.Status == CloudStatus.Unconfigured) return null;
            var on = c.IsSignedIn || c.Status == CloudStatus.SignedIn;
            var line = E("cloudline", E("cloud-dot" + (on ? " on" : "")),
                Muted(c.Conflict != null ? "Choose a save" : on ? $"Google Drive · {(c.LastSync != null ? "synced " + Ago(c.LastSync.Value) : "signed in")}" : c.Status == CloudStatus.Working ? (c.Message ?? "Working…") : "Not signed in", "small"));
            if (c.Conflict != null) line.Add(Btn("Resolve", "small", () => app.Go("account")));
            return line;
        }

        /// <summary>Cloud save controls (Settings) and the save conflict choice.</summary>
        static VisualElement CloudPanel(GameApp app)
        {
            var c = Cloud.Instance;
            var p = Panel(H3("Cloud save (Google Drive)"));
            if (c == null) return p;
            if (c.Status == CloudStatus.Unconfigured) p.Add(Muted("Google sign-in is not set up for this copy of the game yet: see Settings › Login.", "small"));
            else
            {
                p.Add(Row(!c.IsSignedIn ? Btn(c.Email != null ? "Reconnect with Google" : "Sign in with Google", "primary", c.SignIn, disabled: c.Status == CloudStatus.Working) : null,
                    c.IsSignedIn ? Btn("Sync now", "", () => _ = c.SyncNow(), disabled: c.Status == CloudStatus.Working) : null,
                    c.IsSignedIn || c.Email != null ? Btn("Sign out", "ghost", c.SignOut) : null));
                if (c.Email != null) p.Add(Muted($"{c.Email}{(c.LastSync != null ? $" · last synced {Ago(c.LastSync.Value)}" : "")}", "small"));
            }
            if (c.Message != null) p.Add(T(c.Message, c.Status == CloudStatus.Error ? "err small" : "muted small"));
            if (c.Conflict != null)
            {
                var (local, cloud, _, _) = c.Conflict.Value;
                p.Add(Panel(H3("Two different careers"),
                    P($"This computer: {local.pilotName}, {CareerManager.RankOf(local)}, {CareerManager.Hours(local):F1} h. Google Drive: {cloud.pilotName}, {CareerManager.RankOf(cloud)}, {CareerManager.Hours(cloud):F1} h."),
                    Row(Btn("Keep the Google Drive career", "primary", () => c.Resolve(true)), Btn("Keep this computer's career", "", () => c.Resolve(false)))));
            }
            return p;
        }

        static VisualElement ProfileCard(PilotProfile p, int rankPos = 0)
        {
            var pips = E("rankpips" + (p.rankLevel >= 7 ? " l7" : ""));
            for (int i = 0; i < 8; i++) pips.Add(E("rankpip" + (i <= p.rankLevel ? " on" : "")));
            double H(string id) => p.acHours != null && p.acHours.TryGetValue(id, out var v) ? v : 0;
            var maxAc = Math.Max(1, new[] { "kestrel", "northstar", "guardian", "nightjar", "peregrine" }.Max(H));
            var bars = E("stack tight");
            foreach (var (n, id) in new[] { ("Kestrel K2", "kestrel"), ("Northstar 175", "northstar"), ("Guardian S9", "guardian"), ("Nightjar N4", "nightjar"), ("Peregrine P3", "peregrine") })
                bars.Add(ScoreRow(n, H(id) / maxAc * 100, $"{H(id):F1} h", null));
            var quals = Row();
            foreach (var q in p.quals ?? new List<string>()) quals.Add(Chip(Qualifications.ById(q)?.abbrev ?? q, "ok"));
            return Stack(RowBetween(Tight(T(p.name, "acpick-nm"), Muted(p.rank + (rankPos > 0 ? $" · #{rankPos} overall" : ""), "small")), pips),
                Stats(Stat($"{p.hours:F1}", "Hours"), Stat(p.flights.ToString(), "Flights"), Stat(p.rescues.ToString(), "Rescues"), Stat(p.operational.ToString(), "Operational"), Stat($"{p.smoothPct}%", "Smooth landings"), Stat(p.fpJobs.ToString(), "Freeplay jobs")),
                bars, quals,
                Muted($"Flying since {(p.startedAt > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(p.startedAt).ToString("MMM yyyy") : "—")} · last active {Ago(p.updatedAt)} · freeplay {Money(p.fpEarned)} · {p.accidents} accident{(p.accidents == 1 ? "" : "s")}", "small"));
        }

        /// <summary>Change the pilot's name: at once in the game, and on the leaderboards with the publish that follows the save.
        /// Limited to the 24 characters a published profile can carry.</summary>
        static VisualElement RenameField(GameApp app)
        {
            void Save()
            {
                var n = CareerManager.RenamePilot(app.Career, St(app, "rename", app.Career.pilotName));
                if (n == null) { Set(app, "renameMsg", "A pilot name needs at least two letters or numbers."); return; }
                if (ReferenceEquals(n, app.Career)) { Set(app, "renameMsg", "That is already your pilot name."); return; }
                app.CommitCareer(n);
                app.ScreenState["rename"] = n.pilotName;
                var c = Cloud.Instance;
                Set(app, "renameMsg", $"You are now {n.pilotName}." + (c != null && c.PilotSignedIn && c.PilotVisible ? " The leaderboards show it in a few seconds." : ""));
            }
            var msg = St<string>(app, "renameMsg", null);
            return Stack(Field("Pilot name", Text(St(app, "rename", app.Career.pilotName), 24, "Your pilot name", v => app.ScreenState["rename"] = v)
                    .With(t => t.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Save(); }))),
                Row(Btn("Change name", "small", Save)),
                msg != null ? Muted(msg, "small") : null);
        }

        static VisualElement AccountScreen(GameApp app)
        {
            var c = Cloud.Instance;
            var me = Cloud.ProfileFromCareer(app.Career, app.Freeplay);
            var cfg = c?.Config ?? new OnlineConfig();
            var badge = E("login-badge", T(c != null && (c.IsSignedIn || c.PilotSignedIn) ? "✓" : "G"));
            var card = Panel(badge, H2(c != null && c.IsSignedIn ? "Signed in" : c != null && c.PilotSignedIn ? "Pilot area connected" : "Sign in with Google"));
            if (c?.Email != null) card.Add(Muted($"{c.Email} (only you see this)", "small"));
            if (c == null || c.Status == CloudStatus.Unconfigured) card.Add(T("Google sign-in is not set up for this copy of the game yet: see “Set up online play” below.", "caution-t small"));
            else card.Add(Row(!c.IsSignedIn ? Btn(c.Email != null ? "Reconnect with Google" : "Sign in with Google", "primary", c.SignIn, disabled: c.Status == CloudStatus.Working) : null,
                c.IsSignedIn ? Btn("Sync now", "", () => _ = c.SyncNow(), disabled: c.Status == CloudStatus.Working) : null,
                c.IsSignedIn || c.Email != null || c.PilotSignedIn ? Btn("Sign out", "ghost", c.SignOut) : null));
            if (c?.Message != null) card.Add(T(c.Message, c.Status == CloudStatus.Error ? "err small" : "muted small"));
            if (c?.PilotMessage != null) card.Add(T(c.PilotMessage, "err small"));
            card.Add(Muted("• Your save is kept in a hidden folder in your own Google Drive that only this game can see.", "small"));
            card.Add(Muted("• Sign-in happens on Google's own page in your browser: the game never sees your password.", "small"));
            card.Add(Muted("• Other pilots see only your pilot name and flying statistics: never your Google name, email or photo.", "small"));
            card.Add(Muted("• Without signing in, everything still works and saves on this computer.", "small"));
            var prof = Panel(H3("Your pilot profile"), me == null ? Muted("Start a career to get a pilot profile.") : ProfileCard(me), me == null ? null : RenameField(app));
            if (cfg.HasFirebase && c != null)
            {
                prof.Add(Check("Show me in the pilot directory and on the leaderboards", c.PilotVisible, v => c.SetVisible(v)));
                prof.Add(Row(c.PilotSignedIn && c.PilotVisible ? Btn("Publish now", "small", () => _ = c.PublishProfile(true)) : null,
                    Muted(c.PilotSignedIn ? (c.PilotVisible ? "Published after each save" : "Hidden from other pilots") : "Sign in to appear in the pilot area", "small")));
            }
            else prof.Add(Muted("The pilot area (leaderboards and other pilots) is not set up for this copy yet.", "small"));
            var conflict = c?.Conflict != null ? CloudPanel(app) : null;
            var gText = St(app, "gtext", "");
            var fText = St(app, "ftext", "");
            var setupMsg = St<string>(app, "setupMsg", null);
            var setup = Panel(H3("Set up online play"),
                Muted("Cloud saves need a Google OAuth client of type “Desktop app”; the pilot area also needs a free Firebase project. The step-by-step guide is in ONLINE-SETUP.md in the game folder. Values pasted here are kept on this computer.", "small"),
                Field($"Google OAuth client {(cfg.HasGoogle ? $"· set ({cfg.googleClientId.Substring(0, Math.Min(18, cfg.googleClientId.Length))}…)" : "· not set")}",
                    Text(gText, 4000, "Paste the client JSON from Google Cloud, or the client ID and secret", v => app.ScreenState["gtext"] = v).With(t => t.multiline = true)),
                Row(Btn("Save Google client", "small", () => Set(app, "setupMsg", c != null && c.SetGoogleClient(St(app, "gtext", "")) ? "Google client saved. You can sign in now." : "Could not find a client ID and secret in that text."))),
                Field($"Firebase web config {(cfg.HasFirebase ? $"· connected to {cfg.firebaseProjectId}" : "· not set")}",
                    Text(fText, 4000, "Paste the Firebase web config: const firebaseConfig = { apiKey: \"AIza…\", projectId: \"…\" }", v => app.ScreenState["ftext"] = v).With(t => t.multiline = true)),
                Row(Btn("Save Firebase config", "small", () => Set(app, "setupMsg", c != null && c.SetFirebase(St(app, "ftext", "")) ? "Saved. Sign in with Google again to join the pilot area." : "Could not find an apiKey and projectId in that text."))),
                setupMsg != null ? Muted(setupMsg, "small") : null);
            return E("page",
                E("page-head", Tight(H1("Pilot login"), Muted("One Google sign-in keeps your career on every computer you play on, and puts you in the pilot area with everyone else.")).With(x => x.style.flexShrink = 1),
                    Row(Btn("Pilot area", "", () => app.Go("pilots")), Btn("Back", "", () => app.Go(app.Career != null ? "hq" : "menu")))),
                conflict, E("grid2", card, prof), setup);
        }

        static readonly (string id, string label, string field, Func<PilotProfile, string> fmt)[] BOARDS =
        {
            ("rank", "Rank", "rankScore", p => $"Level {p.rankLevel + 1} of {Cloud.RANKS.Length}"), ("hours", "Flight hours", "hours", p => $"{p.hours:F1} h"),
            ("rescues", "Rescues", "rescues", p => p.rescues.ToString()), ("operational", "Operational flights", "operational", p => p.operational.ToString()),
            ("passengers", "Passengers flown", "passengers", p => p.passengers.ToString()), ("quals", "Qualifications", "qualCount", p => p.qualCount.ToString()),
            ("fpEarned", "Freeplay earnings", "fpEarned", p => Money(p.fpEarned)), ("fpJobs", "Freeplay jobs", "fpJobs", p => p.fpJobs.ToString()), ("fpBest", "Best freeplay shift", "fpBest", p => Money(p.fpBest)),
        };

        static VisualElement PilotsScreen(GameApp app)
        {
            var c = Cloud.Instance;
            var me = Cloud.ProfileFromCareer(app.Career, app.Freeplay);
            var tab = St(app, "tab", "boards");
            var boardId = St(app, "board", "rank");
            var board = BOARDS.First(b => b.id == boardId);
            var rows = St<List<PilotEntry>>(app, $"rows:{tab}:{boardId}", null);
            var err = St<string>(app, "err", null);
            var loading = St(app, "loading", false);
            var sel = St<PilotEntry>(app, "sel", null);
            var configured = c != null && c.Config.HasFirebase;
            async void Load()
            {
                app.ScreenState["loading"] = true; app.ScreenState["err"] = null;
                try { var r = await c.Query(tab == "boards" ? board.field : "updatedAt", tab == "boards" ? 50 : 100); app.ScreenState[$"rows:{tab}:{boardId}"] = r; }
                catch (Exception e) { app.ScreenState["err"] = e.Message; app.ScreenState[$"rows:{tab}:{boardId}"] = new List<PilotEntry>(); }
                app.ScreenState["loading"] = false;
                app.Later(app.Refresh);
            }
            if (configured && rows == null && !loading) app.Later(Load);
            var myPos = rows != null && c?.PilotUid != null ? rows.FindIndex(r => r.uid == c.PilotUid) + 1 : 0;
            var page = E("page", E("page-head", Tight(H1("Pilot area"), Muted("Leaderboards and the pilots flying out of Kinloch.")),
                Row(c != null && !c.PilotSignedIn ? Btn("Sign in", "primary", () => app.Go("account")) : null,
                    configured ? Btn("Refresh", "", () => { app.ScreenState.Remove($"rows:{tab}:{boardId}"); app.Later(app.Refresh); }, disabled: loading) : null,
                    Btn("Back", "", () => app.Go(app.Career != null ? "hq" : "menu")))));
            if (me != null)
                page.Add(Panel(ProfileCard(me, tab == "boards" && boardId == "rank" ? myPos : 0),
                    Muted(!configured ? "This is your profile. The shared pilot area is not set up for this copy of the game." : !c.PilotSignedIn ? "Only you can see this. Sign in to join the leaderboards." : !c.PilotVisible ? "You are hidden from other pilots (change this on the login page)." : myPos > 0 ? $"You are #{myPos} for {board.label.ToLowerInvariant()}." : "Your latest flying is published after each save.", "small"))
                    .With(x => { x.style.borderLeftWidth = 4; x.style.borderLeftColor = (Color)new Color32(0xe0, 0x5c, 0xc8, 255); }));
            if (!configured)
            {
                page.Add(Panel(P("The pilot area needs a shared online database, and this copy of the game has not been connected to one yet. It can be set up for free in about 15 minutes (see “Set up online play” on the login page)."), Row(Btn("Open the login page", "", () => app.Go("account")))));
                return page;
            }
            page.Add(Row(Seg(new[] { ("boards", "Leaderboards"), ("pilots", "Pilots") }, tab, v => Set(app, "tab", v)), loading ? Muted("Loading…", "small") : null, err != null ? T(err, "err small") : null));
            if (tab == "boards")
            {
                var picks = Row();
                foreach (var b in BOARDS) { var bid = b.id; var bt = new Button(() => Set(app, "board", bid)) { text = b.label.ToUpperInvariant() }.Cls("chip chip-btn" + (b.id == boardId ? " nav" : "")); picks.Add(bt); }
                var p = Panel(picks);
                if (rows != null && rows.Count == 0 && !loading) p.Add(Muted("Nobody on this board yet. Be the first: fly, and your profile publishes after each save."));
                if (rows != null && rows.Count > 0)
                {
                    // every column sized from a zero basis, so the header and the rows share the same widths
                    TE Col<TE>(TE e, float grow) where TE : VisualElement { e.style.flexGrow = grow; e.style.flexBasis = 0; e.style.flexShrink = 1; return e; }
                    var t = E("table");
                    t.Add(E("tr", Up("#", "th td").With(x => x.style.width = 40), Col(Up("Pilot", "th td"), 2), Col(Up("Rank", "th td"), 2),
                        Col(Up(board.id == "rank" ? "Level" : board.label, "th td num"), 1.4f), Col(Up("Hours", "th td num"), 1)));
                    for (int i = 0; i < rows.Count; i++)
                    {
                        var r = rows[i];
                        var tr = new Button(() => Set(app, "sel", r)).Cls("tr lb-row" + (r.uid == c.PilotUid ? " me" : "") + (i < 3 ? $" top{i + 1}" : ""));
                        tr.style.backgroundColor = r.uid == c.PilotUid ? new Color(0.88f, 0.36f, 0.78f, 0.14f) : new Color(0, 0, 0, 0); tr.style.borderLeftWidth = tr.style.borderRightWidth = tr.style.borderTopWidth = 0; tr.style.marginLeft = tr.style.marginRight = 0;
                        tr.Add(T($"{i + 1}", "lb-pos td")); tr.Add(Col(E("row nowrap", T(r.profile.name, "b"), r.uid == c.PilotUid ? Chip("You", "ok") : null), 2));
                        tr.Add(Col(Muted(r.profile.rank, "td"), 2)); tr.Add(Col(T(board.fmt(r.profile), "td num"), 1.4f)); tr.Add(Col(T($"{r.profile.hours:F1}", "td num"), 1));
                        t.Add(tr);
                    }
                    p.Add(t);
                }
                page.Add(p);
            }
            else
            {
                var q = St(app, "q", "");
                var shown = (rows ?? new List<PilotEntry>()).Where(x => q.Trim().Length == 0 || x.profile.name.ToLowerInvariant().Contains(q.Trim().ToLowerInvariant())).ToList();
                var grid = E("grid3");
                foreach (var e in shown)
                {
                    var ee = e;
                    var bt = new Button(() => Set(app, "sel", ee)).Cls("acpick" + (e.uid == c.PilotUid ? " on" : ""));
                    bt.Add(T(e.profile.name, "acpick-nm")); bt.Add(Muted(e.profile.rank, "small")); bt.Add(T($"{e.profile.hours:F1} h · {e.profile.rescues} rescues · active {Ago(e.profile.updatedAt)}", "mono small"));
                    grid.Add(bt);
                }
                page.Add(Panel(Text(q, 40, "Search pilots by name", v => { app.ScreenState["q"] = v; }).With(t => t.RegisterCallback<FocusOutEvent>(_ => app.Later(app.Refresh))),
                    rows != null && shown.Count == 0 && !loading ? Muted("No pilots found.") : null, grid));
            }
            if (sel != null)
            {
                var card = E("card", ProfileCard(sel.profile), Row(Btn("Close", "", () => Set(app, "sel", null))));
                card.style.width = 660;
                var cc = E("center-card", card);
                cc.style.position = Position.Absolute;
                page.Add(cc);
            }
            return page;
        }
    }
}
