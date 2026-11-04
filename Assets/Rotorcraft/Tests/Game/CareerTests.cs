// The career, ground school, freeplay and company rules (ports of src/game/career.ts, theory.ts, freeplay.ts and
// company.ts), plus checks that every mission's data refers to things that exist in the world.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Rotorcraft.Sim;
using Rotorcraft.World;

namespace Rotorcraft.Game.Tests
{
    public class CareerTests
    {
        IGround saved;
        [SetUp] public void SetUp() { saved = Ground.Current; Ground.Current = new WorldGround(); }
        [TearDown] public void TearDown() { Ground.Current = saved; }

        static FlightResult Result(string missionId, bool pass, int overall = 85, double flightSeconds = 300, FlightOutcome? outcome = null) => new FlightResult
        {
            missionId = missionId, missionTitle = Missions.ById(missionId).title, aircraftId = Missions.ById(missionId).aircraftId,
            outcome = outcome ?? (pass ? FlightOutcome.Passed : FlightOutcome.Failed), pass = pass, flightSeconds = flightSeconds, overall = overall,
            scores = new Dictionary<ScoreCategory, int> { [ScoreCategory.Landing] = pass ? 85 : 40, [ScoreCategory.Handling] = pass ? 85 : 50 },
            objectives = new List<ObjectiveResult>(), failedCriteria = new List<string>(), landings = new List<TouchdownRecord>(), exceedances = new List<string>(),
            warnings = new List<string>(), recommendations = new List<string>(), checklistComplete = true, xp = 50, fuelRemainingKg = 20,
        };

        [Test]
        public void EveryMissionIsWellFormed()
        {
            Assert.AreEqual(Missions.All.Count, Missions.All.Select(m => m.id).Distinct().Count(), "duplicate mission ids");
            Assert.GreaterOrEqual(Missions.All.Count, 40);
            foreach (var m in Missions.All)
            {
                Assert.NotNull(Aircraft.Find(m.aircraftId), $"{m.id}: aircraft {m.aircraftId}");
                Assert.NotNull(WorldMap.FindSite(m.startSiteId), $"{m.id}: start site");
                Assert.True(Weather.Presets.ContainsKey(m.weatherId), $"{m.id}: weather {m.weatherId}");
                foreach (var o in m.objectives)
                {
                    if (o.siteId != null) Assert.NotNull(WorldMap.FindSite(o.siteId), $"{m.id}: site {o.siteId}");
                    if (o.kind == ObjectiveKind.Search) Assert.True(m.searchAreas.Any(a => a.id == o.areaId), $"{m.id}: area {o.areaId}");
                    if (o.targetId != null) Assert.True(m.targets.Any(t => t.id == o.targetId), $"{m.id}: target {o.targetId}");
                    if (o.kind == ObjectiveKind.Decision) foreach (var opt in o.options) if (opt.siteId != null) Assert.NotNull(WorldMap.FindSite(opt.siteId), $"{m.id}: divert {opt.siteId}");
                }
                foreach (var e in m.prerequisites.exercises) Assert.NotNull(Missions.ById(e), $"{m.id}: prerequisite {e}");
                foreach (var q in m.prerequisites.quals) Assert.NotNull(Qualifications.ById(q), $"{m.id}: qual {q}");
                foreach (var t in m.prerequisites.theory) Assert.NotNull(Theory.ModuleById(t), $"{m.id}: module {t}");
                foreach (var x in m.prerequisites.exams) Assert.NotNull(Theory.ExamById(x), $"{m.id}: exam {x}");
            }
            foreach (var id in CareerManager.SYLLABUS) Assert.NotNull(Missions.ById(id), $"syllabus {id}");
        }

        [Test]
        public void RenamingKeepsTheCareerAndCleansTheName()
        {
            var c = CareerManager.CreateCareer("Legend", 1000);
            c.xp = 250;
            var n = CareerManager.RenamePilot(c, "  Ailsa  Munro!! ");
            Assert.AreEqual("Ailsa Munro", n.pilotName);
            Assert.AreEqual("Legend", c.pilotName);
            Assert.AreEqual(c.id, n.id);
            Assert.AreEqual(250, n.xp);
            Assert.Greater(n.updatedAt, c.updatedAt, "a rename must win the next cloud sync");
            Assert.AreSame(c, CareerManager.RenamePilot(c, "Legend"));
            foreach (var bad in new[] { null, "", "   ", "!!!", "A", "- -" }) Assert.IsNull(CareerManager.RenamePilot(c, bad), $"\"{bad}\"");
        }

        [Test]
        public void NewStudentStartsWithExerciseOne()
        {
            var c = CareerManager.CreateCareer("  Daniel  Davies!! ", 1000);
            Assert.AreEqual("Daniel Davies", c.pilotName);
            Assert.AreEqual("Student Pilot", CareerManager.RankOf(c));
            Assert.AreEqual(3000, c.money.balance);
            var next = CareerManager.NextRecommendation(c);
            Assert.AreEqual("ex01_controls", next.id);
            Assert.True(CareerManager.MissionAvailability(c, Missions.ById("ex01_controls")).available);
            Assert.False(CareerManager.MissionAvailability(c, Missions.ById("ex02_hover")).available);
        }

        [Test]
        public void PassingUnlocksTheNextExerciseAndCostsHire()
        {
            var c = CareerManager.CreateCareer("Test", 1000);
            var u = CareerManager.ApplyFlightResult(c, Result("ex01_controls", true), 2000);
            Assert.True(CareerManager.PassedExercise(u.career, "ex01_controls"));
            Assert.Contains("first_flight", u.milestones);
            Assert.Less(u.career.money.balance, 3000, "aircraft hire is paid");
            Assert.True(CareerManager.MissionAvailability(u.career, Missions.ById("ex02_hover")).available);
            Assert.AreEqual(1, u.career.log.Count);
            // the original is never changed
            Assert.False(CareerManager.PassedExercise(c, "ex01_controls"));
        }

        [Test]
        public void ResultForAnUnavailableMissionIsRejected()
        {
            var c = CareerManager.CreateCareer("Test", 1000);
            var u = CareerManager.ApplyFlightResult(c, Result("st_ppl", true), 2000);
            Assert.AreSame(c, u.career);
            StringAssert.StartsWith("Result rejected", u.messages[0]);
        }

        [Test]
        public void CommercialStartHoldsTheLicences()
        {
            var c = CareerManager.CreateCareer("Test", 1000, CareerStart.Commercial);
            Assert.AreEqual("Commercial Pilot", CareerManager.RankOf(c));
            Assert.True(CareerManager.HasQual(c, "cpl_h"));
            Assert.AreEqual(12000, c.money.balance);
            var next = CareerManager.NextRecommendation(c);
            Assert.AreNotEqual("ex01_controls", next.id, "PPL lessons are skipped");
        }

        [Test]
        public void ExamsNeedTheirModulesAndAreGraded()
        {
            var c = CareerManager.CreateCareer("Test", 1000);
            Assert.False(CareerManager.ExamAvailability(c, "exam_ppl").available);
            foreach (var m in new[] { "gs1_principles", "gs2_navigation", "gs3_airlaw", "gs4_emergencies" }) c = CareerManager.ApplyModuleQuiz(c, m, 100);
            Assert.True(CareerManager.ExamAvailability(c, "exam_ppl").available);
            var exam = Theory.ExamById("exam_ppl");
            var qs = Theory.ExamQuestions(exam, 42);
            Assert.AreEqual(12, qs.Count);
            Assert.AreEqual(12, qs.Select(q => q.id).Distinct().Count());
            CollectionAssert.AreEqual(qs.Select(q => q.id), Theory.ExamQuestions(exam, 42).Select(q => q.id), "seeded");
            var (pct, _) = Theory.GradeAnswers(qs, qs.Select(q => (int?)q.answer).ToList());
            Assert.AreEqual(100, pct);
            c = CareerManager.ApplyExam(c, "exam_ppl", pct);
            Assert.True(CareerManager.ExamPassed(c, "exam_ppl"));
            Assert.AreEqual(3000 - 120, c.money.balance);
        }

        [Test]
        public void FailedTestSetsRemedialTraining()
        {
            var c = CareerManager.CreateCareer("Test", 1000);
            foreach (var m in new[] { "gs1_principles", "gs2_navigation", "gs3_airlaw", "gs4_emergencies" }) c = CareerManager.ApplyModuleQuiz(c, m, 100);
            foreach (var id in new[] { "ex01_controls", "ex02_hover", "ex03_takeoff_landing", "ex04_circuits", "ex06_first_solo", "ex07_navigation" })
            {
                var u0 = CareerManager.ApplyFlightResult(c, Result(id, true, flightSeconds: 600), 2000);
                Assert.True(CareerManager.PassedExercise(u0.career, id), $"{id}: {string.Join("; ", u0.messages)}");
                c = u0.career;
            }
            c = CareerManager.ApplyExam(c, "exam_ppl", 100);
            // service the Kestrel (an inspection falls due every 2.5 h)
            c = CareerManager.ApplyMaintenance(c, "kestrel");
            Assert.True(CareerManager.MissionAvailability(c, Missions.ById("st_ppl")).available, string.Join("; ", CareerManager.MissionAvailability(c, Missions.ById("st_ppl")).reasons));
            var u = CareerManager.ApplyFlightResult(c, Result("st_ppl", false, 50), 3000);
            Assert.IsNotEmpty(u.remedial);
            Assert.False(CareerManager.MissionAvailability(u.career, Missions.ById("st_ppl")).available);
            // then pass it
            var c2 = u.career;
            foreach (var r in u.remedial) c2 = CareerManager.ApplyFlightResult(c2, Result(r, true), 4000).career;
            c2 = CareerManager.ApplyMaintenance(c2, "kestrel");
            var u2 = CareerManager.ApplyFlightResult(c2, Result("st_ppl", true), 5000);
            CollectionAssert.Contains(u2.awarded, "ppl_h");
            Assert.AreEqual("Private Pilot", CareerManager.RankOf(u2.career));
        }

        [Test]
        public void SaveRoundTrip()
        {
            var c = CareerManager.ApplyFlightResult(CareerManager.CreateCareer("Test", 1000), Result("ex01_controls", true), 2000).career;
            var json = SaveData.Write(c);
            var back = SaveData.ValidateCareer(SaveData.Read<Career>(json));
            Assert.True(CareerManager.PassedExercise(back, "ex01_controls"));
            Assert.AreEqual(c.money.balance, back.money.balance);
            Assert.IsNull(SaveData.ValidateCareer(SaveData.Read<Career>("{\"version\": 99, \"id\": \"x\", \"pilotName\": \"y\"}")));
        }

        [Test]
        public void FreeplayCalloutsForEveryAircraft()
        {
            foreach (var id in Aircraft.All.Keys)
            {
                var ac = Aircraft.Get(id);
                var cfg = new FreeplayConfig { aircraftId = id, kit = Loadout.DefaultKit(ac), baseSiteId = ac.engines == 2 ? "kin_h2" : "kin_h1", pace = FreeplayPace.Busy, seed = 1234 };
                var fit = Loadout.Fit(ac, cfg.kit);
                Assert.IsEmpty(fit.problems, id);
                var def = FreeplayData.FreeplayMission(cfg, fit.ac, fit.kitKg);
                var site = WorldMap.SiteById(cfg.baseSiteId);
                var s = FlightModel.CreateState(new CreateOptions { x = site.x, z = site.z, headingDeg = 270, fuel = fit.ac.fuelCapacity * 0.8 });
                var rt = new MissionRuntime(def, fit.ac, s.fuel, true);
                var dir = new FreeplayDirector(cfg, fit.ac);
                for (double t = 0; t < 1200 && dir.offers.Count == 0; t += 1) dir.Update(t, rt, s, 1);
                Assert.IsNotEmpty(dir.offers, $"{id}: no callouts in 20 minutes");
                var offer = dir.offers.FirstOrDefault(o => FreeplayData.CannotAccept(o.requires, o.needsTwin, fit.ac, cfg.kit) == null);
                if (offer == null) continue;
                Assert.IsNull(dir.Accept(offer.id, 1200, rt, s), id);
                Assert.IsNotEmpty(rt.JobItems(offer.id), id);
                dir.Abandon(1260, rt, s);
                Assert.AreEqual(1, dir.history.Count);
            }
        }

        [Test]
        public void CompanyBuysFliesAndSettles()
        {
            var c = Companies.CreateCompany("Test Air", StartPackage.Owner, 1000, 7);
            Assert.AreEqual(420_000, c.funds);
            var kestrel = c.market.First(m => m.aircraftId == "kestrel" && !m.used);
            var r = Companies.Buy(c, kestrel.id, false);
            Assert.True(r.ok, r.reason);
            c = r.company;
            Assert.AreEqual(1, c.fleet.Count);
            Assert.AreEqual(420_000 - 285_000, c.funds);
            var uid = c.fleet[0].uid;
            var sum = new FreeplaySummary { jobs = new List<JobRecord> { new JobRecord { id = "j", title = "Photos", type = CalloutType.Photo, pay = 800, quality = 90, outcome = JobOutcome.Completed, notes = new List<string>() } }, earned = 800, fuelCost = 60, flightSeconds = 1800, aircraftId = "kestrel" };
            var (after, report) = Companies.SettleShift(c, uid, sum, FlightOutcome.Completed, 0, 1800);
            Assert.AreEqual(1, after.shift);
            Assert.Greater(report.net, 0);
            Assert.Greater(after.reputation, c.reputation);
            Assert.Less(after.fleet[0].condition, 1);
            var back = SaveData.ValidateCompany(SaveData.Read<Company>(SaveData.Write(after)));
            Assert.AreEqual(after.funds, back.funds, 0.01);
        }
    }
}
