using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Rotorcraft.Presentation.UI;
using Debug = UnityEngine.Debug;

namespace Rotorcraft.Presentation.Tests
{
    public class DiscordPresenceTests
    {
        static PresenceFacts Career() => new PresenceFacts { version = "1.0.0", rank = "Junior Line Pilot", hours = 42.46, gameStartMs = 1000, flightStartMs = 2000 };

        static PresenceFacts Flying(Action<PresenceFacts> set)
        {
            var f = Career();
            f.flying = true; f.flightTitle = "SAR · Missing Walker"; f.aircraftId = "guardian"; f.aircraftName = "Guardian S9";
            set(f);
            return f;
        }

        [Test]
        public void MenusSayWhereThePlayerIsAndTheirRank()
        {
            var f = Career(); f.screen = "brief"; f.screenTitle = "SAR · Missing Walker";
            var v = DiscordStatus.Describe(f);
            Assert.AreEqual("Briefing: SAR · Missing Walker", v.details);
            Assert.AreEqual("Junior Line Pilot · 42.5 hours", v.state);
            Assert.AreEqual("logo", v.largeImage);
            Assert.AreEqual("Rotorcraft 1.0.0", v.largeText);
            Assert.AreEqual(1000, v.startMs, "the menus count from the start of the game");

            f.screen = "debrief"; f.screenTitle = "PPL(H) Skill Test"; f.passed = true;
            Assert.AreEqual("Passed: PPL(H) Skill Test", DiscordStatus.Describe(f).details);
            f.screen = "something new";
            Assert.AreEqual("In the menus", DiscordStatus.Describe(f).details);
            var none = new PresenceFacts { screen = "menu" };
            Assert.IsNull(DiscordStatus.Describe(none).state, "no career, nothing to say under the main menu");
        }

        [Test]
        public void ACareerFlightShowsTheObjectiveAndTheAircraft()
        {
            var v = DiscordStatus.Describe(Flying(f => { f.task = DiscordStatus.Task("Land in the clearing (slope ≤ 6°)"); f.vr = true; }));
            Assert.AreEqual("SAR · Missing Walker", v.details);
            Assert.AreEqual("Land in the clearing", v.state, "the limits in brackets are left out");
            Assert.AreEqual("guardian", v.largeImage);
            Assert.AreEqual("Guardian S9 · in VR", v.largeText);
            Assert.AreEqual("logo", v.smallImage);
            Assert.AreEqual("Junior Line Pilot · 42.5 hours", v.smallText);
            Assert.AreEqual(2000, v.startMs, "a flight counts from its own start");
            Assert.AreEqual("Hover over H2 for 10 s", DiscordStatus.Task("Hover over H2 for 10 s"));
            Assert.AreEqual("(x)", DiscordStatus.Task("(x)"), "a label that is all brackets stays as it is");
        }

        [Test]
        public void LoadingCrashesPausesAndChecksComeBeforeTheObjective()
        {
            Assert.AreEqual("Getting ready to fly", DiscordStatus.Describe(Flying(f => { f.loading = true; f.task = "x y"; })).state);
            Assert.AreEqual("Crashed", DiscordStatus.Describe(Flying(f => { f.crashed = true; f.paused = true; f.task = "x y"; })).state);
            Assert.AreEqual("Paused", DiscordStatus.Describe(Flying(f => { f.paused = true; f.task = "x y"; })).state);
            Assert.AreEqual("Pre-flight walk-round", DiscordStatus.Describe(Flying(f => { f.walkRound = true; f.task = "x y"; })).state);
            Assert.AreEqual("Cockpit checks", DiscordStatus.Describe(Flying(f => { f.cockpitChecks = true; f.task = "x y"; })).state);
        }

        [Test]
        public void FreeplayShowsTheCalloutOrThatItIsWaiting()
        {
            PresenceFacts Shift(Action<PresenceFacts> set) => Flying(f => { f.freeplay = true; f.flightTitle = "Freeplay shift"; f.aircraftId = "nightjar"; f.aircraftName = "Nightjar N4"; set(f); });
            Assert.AreEqual("Waiting for a callout", DiscordStatus.Describe(Shift(f => { })).state);
            var busy = DiscordStatus.Describe(Shift(f => { f.jobs = 2; f.earned = 12400; }));
            Assert.AreEqual("Standing by, 2 jobs done", busy.state);
            Assert.AreEqual("2 jobs · £12,400", busy.smallText);
            Assert.AreEqual("Injured hill walker", DiscordStatus.Describe(Shift(f => { f.task = "Injured hill walker"; })).state);
            Assert.AreEqual("Flying around Kinloch", DiscordStatus.Describe(Shift(f => { f.freeRoam = true; })).state);
            var report = new PresenceFacts { screen = "fpreport", jobs = 1, earned = 0 };
            Assert.AreEqual("1 job", DiscordStatus.Describe(report).state);
        }

        [Test]
        public void AnAircraftWithNoPictureFallsBackToTheLogo()
        {
            var v = DiscordStatus.Describe(Flying(f => { f.aircraftId = "prototype"; f.aircraftName = "Prototype"; }));
            Assert.AreEqual("logo", v.largeImage);
            Assert.IsNull(v.smallImage, "the logo is not shown twice");
        }

        [Test]
        public void TheActivityKeepsToDiscordsLimits()
        {
            var v = new PresenceView { details = new string('a', 200), state = "x", largeImage = "kestrel", largeText = "Kestrel K2", smallImage = "logo", smallText = "  ", startMs = 1700000000000 };
            var a = DiscordStatus.ToActivity(v);
            Assert.AreEqual(128, ((string)a["details"]).Length);
            Assert.IsTrue(((string)a["details"]).EndsWith("…"));
            Assert.IsNull(a["state"], "Discord refuses text under 2 characters, so it is left out");
            Assert.AreEqual(1700000000000, (long)a["timestamps"]["start"]);
            Assert.AreEqual("https://rotorcraft.netlify.app/img/discord/kestrel.png", (string)a["assets"]["large_image"]);
            Assert.AreEqual("Kestrel K2", (string)a["assets"]["large_text"]);
            Assert.IsNull(a["assets"]["small_text"]);
            var buttons = (JArray)a["buttons"];
            Assert.AreEqual(2, buttons.Count, "Discord allows two buttons");
            foreach (var b in buttons)
            {
                Assert.LessOrEqual(((string)b["label"]).Length, 32);
                StringAssert.StartsWith("https://", (string)b["url"]);
            }
            Assert.AreEqual(0, new[] { "logo" }.Concat(DiscordStatus.Aircraft).Count(n => n.Any(char.IsUpper)), "picture names match the website's lower-case files");
        }

        // ------------------------------------------------------------------------- the real Discord app (Windows)
#if UNITY_EDITOR_WIN
        static bool DiscordRunning()
        {
            try { return Directory.GetFiles(@"\\.\pipe\").Any(p => p.Contains("discord-ipc-")); }
            catch { return true; }   // cannot list pipes here: just try
        }

        static void WaitFor(Func<bool> done, int ms)
        {
            var t = Stopwatch.StartNew();
            while (!done() && t.ElapsedMilliseconds < ms) Thread.Sleep(100);
        }

        /// <summary>The pipe, the framing and the handshake against the Discord app on this computer, with an application id
        /// Discord does not know: it must answer, and turn the game down rather than leave it waiting.</summary>
        [Test, Explicit("talks to the Discord app on this computer")]
        public void DiscordAnswersAnUnknownApplication()
        {
            if (!DiscordRunning()) Assert.Ignore("Discord is not running");
            using (var ipc = new DiscordIpc("1"))
            {
                WaitFor(() => ipc.Status != DiscordIpc.Link.Waiting, 15000);
                Debug.Log($"Discord: status {ipc.Status}, problem {ipc.Problem}");
                Assert.AreEqual(DiscordIpc.Link.Refused, ipc.Status, ipc.Problem);
                Assert.IsNotEmpty(ipc.Problem);
            }
        }

        /// <summary>A flight shown on the player's Discord profile and Discord's answer to it (written to Logs/discord-reply.json).
        /// RC_DISCORD_HOLD keeps it up for that many seconds, to look at it in Discord.</summary>
        [Test, Explicit("sets the activity on the Discord account signed in on this computer")]
        public void DiscordTakesAFlight()
        {
            if (!DiscordPresence.Available) Assert.Ignore("no Discord application id yet");
            if (!DiscordRunning()) Assert.Ignore("Discord is not running");
            using (var ipc = new DiscordIpc(DiscordPresence.ApplicationId))
            {
                var f = Flying(x => { x.task = "Winch the walker aboard"; x.flightStartMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 754000; });
                ipc.Set(DiscordStatus.ToActivity(DiscordStatus.Describe(f)));
                WaitFor(() => ipc.Replies > 0 || ipc.Status == DiscordIpc.Link.Refused, 20000);
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/discord-reply.json", ipc.LastReply ?? "(none)");
                Debug.Log($"Discord: status {ipc.Status}, reply {ipc.LastReply}");
                Assert.AreEqual(DiscordIpc.Link.Connected, ipc.Status, ipc.Problem);
                Assert.Greater(ipc.Replies, 0, "Discord answered the update");
                Assert.IsNull(ipc.Problem, "Discord accepted the activity");
                var data = JObject.Parse(ipc.LastReply)["data"];
                Assert.AreEqual("SAR · Missing Walker", (string)data["details"]);
                Assert.AreEqual("Winch the walker aboard", (string)data["state"]);
                Assert.IsNotNull(data["assets"]?["large_image"], "the picture came through");
                if (int.TryParse(Environment.GetEnvironmentVariable("RC_DISCORD_HOLD"), out var hold)) Thread.Sleep(hold * 1000);
                // a second update is merged and sent once the rate limit allows; clearing then empties the profile
                ipc.Set(null);
                var before = ipc.Replies;
                WaitFor(() => ipc.Replies > before, DiscordIpc.MinGapMs + 5000);
                Assert.Greater(ipc.Replies, before, "the clear went through after the rate limit");
            }
        }
#endif
    }
}
