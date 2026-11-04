// The career manager (port of src/game/career.ts): every progression rule lives here, applied to plain data.
// The UI only displays what these functions return, so qualifications cannot be earned by manipulating the
// interface.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Rotorcraft.Sim;

namespace Rotorcraft.Game
{
    public enum CareerStart { Student, Commercial }

    public sealed class AttemptRecord { public long at; public int overall; public FlightOutcome outcome; public Dictionary<ScoreCategory, int> scores; public TimeOfDay? tod; public string weatherId; }
    public sealed class ExerciseRecord { public int attempts, passes, bestScore; public FlightOutcome? lastResult; public long lastAt; public long? lastPassAt; public List<AttemptRecord> history = new List<AttemptRecord>(); }
    public sealed class FlightLogEntry { public string id; public long at; public string missionId, title, aircraftId; public FlightOutcome outcome; public int overall; public double flightSeconds; public string landingGrade; public int xp; }
    public sealed class ModuleRecord { public bool read; public double bestPct; public bool passed; }
    public sealed class ExamRecord { public int attempts; public double bestPct; public bool passed; public long lastAt; }
    public sealed class QualRecord { public long awardedAt; }
    public sealed class RemedialRecord { public string[] required; public long since; }

    /// <summary>Condition 0..100. Below SERVICEABLE_MIN the aircraft is grounded; an inspection falls due every INSPECTION_INTERVAL_H.</summary>
    public sealed class AirframeState { public double condition = 100, hoursSinceInspection; public int inspections; public string lastEvent; public AirframeState Clone() => (AirframeState)MemberwiseClone(); }

    public sealed class CareerStats
    {
        public int flights, landings, smoothLandings, hardLandings, accidents;
        public double distanceKm, maxAltFt, fuelKg;
        public int passengers, rescues, operational;
        public double longestFlightS;
        public Dictionary<string, double> perAircraftS = new Dictionary<string, double>();
    }

    public sealed class Career
    {
        public int version;
        public string id, pilotName;
        public long createdAt, updatedAt;
        public double flightSeconds;
        public int xp;
        public Dictionary<string, ExerciseRecord> exercises = new Dictionary<string, ExerciseRecord>();
        public Dictionary<string, ModuleRecord> modules = new Dictionary<string, ModuleRecord>();
        public Dictionary<string, ExamRecord> exams = new Dictionary<string, ExamRecord>();
        public Dictionary<string, QualRecord> qualifications = new Dictionary<string, QualRecord>();
        public Dictionary<string, RemedialRecord> remedial = new Dictionary<string, RemedialRecord>();
        public List<FlightLogEntry> log = new List<FlightLogEntry>();
        /// <summary>Flight time credited from before the career (fast-track start), seconds.</summary>
        public double priorSeconds;
        /// <summary>Airframe condition per aircraft type (the school or operator's machine you fly).</summary>
        public Dictionary<string, AirframeState> fleet = new Dictionary<string, AirframeState>();
        public CareerStats stats = new CareerStats();
        public Money money = new Money();
        /// <summary>Reputation with each employer you have flown for.</summary>
        public Dictionary<string, Reputation> reputation = new Dictionary<string, Reputation>();
        /// <summary>Flight-hour time at which each recurrent qualification lapses.</summary>
        public Dictionary<string, double> validity = new Dictionary<string, double>();

        public Career Clone() => JsonConvert.DeserializeObject<Career>(JsonConvert.SerializeObject(this));
    }

    public sealed class ReqItem { public string label; public bool met; public ReqItem(string l, bool m) { label = l; met = m; } }
    public sealed class Availability { public bool available; public List<string> reasons; public bool future; }

    public sealed class CareerUpdate
    {
        public Career career;
        public List<string> awarded = new List<string>(), milestones = new List<string>(), remedial = new List<string>(), messages = new List<string>();
        public List<Ledger> ledger;
        public List<string> renewed;
        public string employer;
        public int? standingBefore, standingAfter;
        public List<string> repNotes;
    }

    public enum MilestoneState { Locked, Available, InProgress, Completed, Retry }

    public sealed class MilestoneDef
    {
        public string id, title, objective, criteria, reward, unlocks;
        public string[] prereqs, next;
        public Func<Career, bool> done;
        public string trackedMission;
        public int contentMilestone;
    }

    public sealed class Recommendation { public string kind, id, title, why; }

    public static class CareerManager
    {
        public const int SAVE_VERSION = 1;
        public const double SERVICEABLE_MIN = 60;
        public const double INSPECTION_INTERVAL_H = 2.5;

        public static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        static string Base36(long v) { const string d = "0123456789abcdefghijklmnopqrstuvwxyz"; var s = ""; do { s = d[(int)(v % 36)] + s; v /= 36; } while (v > 0); return s; }

        public static Career CreateCareer(string pilotName, long? nowMs = null, CareerStart start = CareerStart.Student)
        {
            var now = nowMs ?? Now();
            var fast = start == CareerStart.Commercial;
            var c = new Career
            {
                version = SAVE_VERSION, id = "c" + Base36(now), pilotName = SanitizeName(pilotName), createdAt = now, updatedAt = now,
                priorSeconds = fast ? 3 * 3600 : 0,
                money = new Money { balance = Progression.StartingFunds(start) },
            };
            if (fast)
            {
                // a commercial start has already done the private and commercial ground school
                foreach (var q in new[] { "ppl_h", "type_kestrel", "cpl_h", "night_h" }) c.qualifications[q] = new QualRecord { awardedAt = now };
                foreach (var m in new[] { "gs1_principles", "gs2_navigation", "gs3_airlaw", "gs4_emergencies", "gs5_performance", "gs6_decisions" }) c.modules[m] = new ModuleRecord { read = true, bestPct = 100, passed = true };
                foreach (var x in new[] { "exam_ppl", "exam_cpl" }) c.exams[x] = new ExamRecord { attempts = 1, bestPct = 100, passed = true, lastAt = now };
            }
            return c;
        }

        public static string SanitizeName(string n)
        {
            var s = Regex.Replace(n ?? "", @"[^\p{L}\p{N} .'-]", "");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            if (s.Length > 32) s = s.Substring(0, 32);
            return s.Length > 0 ? s : "Student Pilot";
        }

        /// <summary>The same career under a new pilot name; unchanged if it is the same name, null if the name is unusable (under two letters or numbers).</summary>
        public static Career RenamePilot(Career prev, string name)
        {
            if ((name ?? "").Count(char.IsLetterOrDigit) < 2) return null;
            var s = SanitizeName(name);
            if (s == prev.pilotName) return prev;
            var c = prev.Clone();
            c.pilotName = s;
            c.updatedAt = Now();
            return c;
        }

        public static double Hours(Career c) => (c.flightSeconds + c.priorSeconds) / 3600;
        public static AirframeState Airframe(Career c, string acId) => c.fleet != null && c.fleet.TryGetValue(acId, out var a) ? a : new AirframeState();
        public static bool HasQual(Career c, string id) => c.qualifications.ContainsKey(id);
        public static bool PassedExercise(Career c, string id) => c.exercises.TryGetValue(id, out var r) && r.passes > 0;
        public static bool ModulePassed(Career c, string id) => c.modules.TryGetValue(id, out var r) && r.passed;
        public static bool ExamPassed(Career c, string id) => c.exams.TryGetValue(id, out var r) && r.passed;

        public static Reputation RepWith(Career c, string e) => c.reputation != null && c.reputation.TryGetValue(e, out var r) ? r : new Reputation();
        public static int StandingWith(Career c, string e) => Progression.Standing(RepWith(c, e));
        /// <summary>Whether a held qualification is in date (qualifications without recurrent checks never lapse).</summary>
        public static Currency CurrencyOf(Career c, string id) =>
            HasQual(c, id) ? Progression.CurrencyOf(c.validity != null && c.validity.TryGetValue(id, out var v) ? v : (double?)null, Hours(c)) : Currency.None;
        public static bool IsCurrent(Career c, string id) => HasQual(c, id) && CurrencyOf(c, id) != Currency.Expired;

        static bool Operational(MissionCategory k) => k == MissionCategory.Sar || k == MissionCategory.Offshore || k == MissionCategory.Utility || k == MissionCategory.Medical || k == MissionCategory.Police || k == MissionCategory.Military;
        public static bool IsOperational(MissionDef m) => Operational(m.category);
        public static int OperationalPasses(Career c) => Missions.All.Where(m => Operational(m.category)).Sum(m => c.exercises.TryGetValue(m.id, out var r) ? r.passes : 0);

        /// <summary>Senior pilot: experience, a clean recent record and good standing with an operator.</summary>
        public static (bool met, List<ReqItem> items) SeniorStatus(Career c)
        {
            var recent = c.log.Take(10).ToList();
            var best = Math.Max(0, Employers.ALL.Where(e => e.id != "school").Select(e => c.reputation.ContainsKey(e.id) ? StandingWith(c, e.id) : 0).DefaultIfEmpty(0).Max());
            var items = new List<ReqItem>
            {
                new ReqItem($"8.0 h total flight time ({Hours(c):F1} h)", Hours(c) >= 8),
                new ReqItem($"8 successful operational flights ({OperationalPasses(c)})", OperationalPasses(c) >= 8),
                new ReqItem("No accidents in your last 10 flights", recent.Count >= 5 && !recent.Any(l => l.outcome == FlightOutcome.Crashed)),
                new ReqItem($"Standing of 65 with an operator (best {best})", best >= 65),
            };
            return (items.All(i => i.met), items);
        }

        public static string RankOf(Career c)
        {
            if (HasQual(c, "tc_auth")) return "Training Captain";
            if (SeniorStatus(c).met) return "Senior Pilot";
            if ((HasQual(c, "auth_sar") || HasQual(c, "auth_offshore")) && OperationalPasses(c) >= 4) return "Experienced Line Pilot";
            if (HasQual(c, "auth_sar") || HasQual(c, "auth_offshore") || OperationalPasses(c) > 0) return "Junior Line Pilot";
            if (HasQual(c, "cpl_h")) return "Commercial Pilot";
            if (HasQual(c, "ppl_h")) return "Private Pilot";
            if (PassedExercise(c, "ex06_first_solo")) return "Solo Student";
            return "Student Pilot";
        }

        /// <summary>The operator the pilot works for most (by successful flights), for the HQ header.</summary>
        public static string MainEmployerName(Career c)
        {
            var counts = new Dictionary<string, int>();
            foreach (var m in Missions.All.Where(m => Operational(m.category)))
            {
                var e = Employers.EmployerOf(m);
                counts[e] = (counts.TryGetValue(e, out var n) ? n : 0) + (c.exercises.TryGetValue(m.id, out var r) ? r.passes : 0);
            }
            var best = counts.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).FirstOrDefault();
            if (best.Key != null) return Employers.ById(best.Key).name;
            if (HasQual(c, "auth_sar")) return "Saltire Rescue";
            if (HasQual(c, "auth_offshore")) return "Pentland Offshore Aviation";
            return "Kinloch Rotor School";
        }

        // ------------------------------------------------------------------------------- availability
        public static Availability MissionAvailability(Career c, MissionDef m)
        {
            var reasons = new List<string>();
            var future = m.availableFromMilestone > Qualifications.CURRENT_MILESTONE || m.objectives.Length == 0;
            if (future) reasons.Add($"Content arrives in development Milestone {m.availableFromMilestone}");
            var p = m.prerequisites ?? new Prerequisites();
            foreach (var e in p.exercises ?? new string[0]) if (!PassedExercise(c, e)) reasons.Add($"Pass {Missions.ById(e)?.shortTitle ?? e}");
            foreach (var q in p.quals ?? new string[0])
            {
                if (!HasQual(c, q)) reasons.Add($"Hold {Qualifications.ById(q)?.abbrev ?? q}");
                else if (CurrencyOf(c, q) == Currency.Expired) reasons.Add($"{Qualifications.ById(q)?.abbrev ?? q} has lapsed: fly the {Missions.ById(Qualifications.ById(q)?.practicalTest ?? "")?.shortTitle ?? "recurrent check"}");
            }
            if (m.pathway == Pathway.Tc && m.category == MissionCategory.Test && !SeniorStatus(c).met) reasons.Add("Senior Pilot status (see the career roadmap)");
            if (m.minStanding is double ms && ms > 0)
            {
                var e = Employers.EmployerOf(m); var st = StandingWith(c, e);
                if (st < ms) reasons.Add($"Standing {ms:0} with {Employers.ById(e)?.name} (you have {st})");
            }
            foreach (var t in p.theory ?? new string[0]) if (!ModulePassed(c, t)) reasons.Add($"Ground school: {Theory.ModuleById(t)?.title ?? t}");
            foreach (var x in p.exams ?? new string[0]) if (!ExamPassed(c, x)) reasons.Add($"Pass the {Theory.ExamById(x)?.title ?? x}");
            if (p.minHours is double mh && mh > 0 && Hours(c) < mh) reasons.Add($"{mh:F1} h flight time (you have {Hours(c):F1} h)");
            if (c.remedial.TryGetValue(m.id, out var rem))
                foreach (var r in rem.required.Where(id => Missions.ById(id) != null))
                {
                    var rec = c.exercises.TryGetValue(r, out var x) ? x : null;
                    if (rec == null || rec.lastPassAt == null || rec.lastPassAt < rem.since) reasons.Add($"Remedial training: pass {Missions.ById(r)?.shortTitle ?? r} again");
                }
            var awards = Qualifications.ALL.Where(q => q.practicalTest == m.id).ToList();
            // a held test can be flown again as the recurrent check once its qualification is due or lapsed
            if (m.category == MissionCategory.Test && awards.Count > 0 && awards.All(q => HasQual(c, q.id) && (CurrencyOf(c, q.id) == Currency.Current || CurrencyOf(c, q.id) == Currency.None))) reasons.Add("Already passed");
            if (!future) reasons.AddRange(Serviceability(c, m.aircraftId));
            return new Availability { available = reasons.Count == 0, reasons = reasons, future = future };
        }

        /// <summary>Why an aircraft cannot fly right now (empty when serviceable).</summary>
        public static List<string> Serviceability(Career c, string acId)
        {
            var a = Airframe(c, acId);
            var name = Aircraft.Find(acId)?.name ?? acId;
            var r = new List<string>();
            if (a.condition < SERVICEABLE_MIN) r.Add($"{name} unserviceable (condition {a.condition:F0}%): maintenance needed in the Hangar");
            else if (a.hoursSinceInspection >= INSPECTION_INTERVAL_H) r.Add($"{name}: scheduled inspection due (Hangar)");
            return r;
        }

        /// <summary>Engine power available as a fraction of new, from airframe condition (a worn aircraft performs worse).</summary>
        public static double PowerFactor(Career c, string acId)
        {
            var cond = Airframe(c, acId).condition;
            return cond >= 90 ? 1 : 0.94 + 0.06 * Math.Max(0, cond - SERVICEABLE_MIN) / (90 - SERVICEABLE_MIN);
        }

        /// <summary>What maintaining the aircraft now would cost you (operators maintain their own twins).</summary>
        public static double MaintenanceCost(Career c, string acId)
        {
            var a = Airframe(c, acId);
            if ((Aircraft.Find(acId)?.engines ?? 2) > 1) return 0;
            return Progression.RepairCost(acId, 100 - a.condition) + (a.hoursSinceInspection > 0.05 ? 150 : 0);
        }

        public static Career ApplyMaintenance(Career prev, string acId, long? nowMs = null)
        {
            var now = nowMs ?? Now();
            var c = prev.Clone();
            var a = Airframe(c, acId);
            var bill = MaintenanceCost(prev, acId);
            if (bill > 0) c.money = Progression.ApplyLedger(c.money, new[] { new Ledger { label = $"Maintenance: {Aircraft.Find(acId)?.name}", amount = -bill } }).money;
            c.fleet[acId] = new AirframeState { condition = 100, hoursSinceInspection = 0, inspections = a.inspections + 1, lastEvent = $"Inspected and repaired {DateTimeOffset.FromUnixTimeMilliseconds(now).ToLocalTime():dd/MM/yyyy}" };
            c.updatedAt = now;
            return c;
        }

        public static Availability ExamAvailability(Career c, string id)
        {
            var ex = Theory.ExamById(id);
            if (ex == null) return new Availability { available = false, reasons = new List<string> { "Unknown exam" } };
            var reasons = ex.prerequisites.Where(m => !ModulePassed(c, m)).Select(m => $"Complete {Theory.ModuleById(m)?.title ?? m}").ToList();
            if (ExamPassed(c, id)) reasons.Add("Already passed");
            return new Availability { available = reasons.Count == 0, reasons = reasons };
        }

        // ----------------------------------------------------------------------------- applying results
        static readonly Regex OverLimit = new Regex(@"transient limit|overspeed|VNE|blade stall|above \d+ °C");

        public static CareerUpdate ApplyFlightResult(Career prev, FlightResult r, long? nowMs = null)
        {
            var now = nowMs ?? Now();
            var m = Missions.ById(r.missionId);
            if (m == null) return new CareerUpdate { career = prev, messages = { "Unknown mission; result not recorded." } };
            var beforeMilestones = CompletedMilestones(prev);
            // never trust a result for a mission the career could not legally fly
            var avail = MissionAvailability(prev, m);
            if (!avail.available) return new CareerUpdate { career = prev, messages = { $"Result rejected: {string.Join(", ", avail.reasons)}" } };
            var c = prev.Clone();
            var messages = new List<string>();

            var flightSeconds = Clamp(r.flightSeconds, 0, 6 * 3600);
            c.flightSeconds += flightSeconds;
            c.xp += (int)Clamp(r.xp, 0, 2000);
            var rec = c.exercises.TryGetValue(m.id, out var x0) ? x0 : new ExerciseRecord();
            rec.attempts += 1;
            rec.lastResult = r.outcome;
            rec.lastAt = now;
            var passed = r.pass && (r.outcome == FlightOutcome.Passed || r.outcome == FlightOutcome.Completed);
            if (passed) { rec.passes += 1; rec.lastPassAt = now; }
            rec.bestScore = Math.Max(rec.bestScore, (int)Clamp(r.overall, 0, 100));
            rec.history.Insert(0, new AttemptRecord { at = now, overall = (int)Clamp(r.overall, 0, 100), outcome = r.outcome, scores = r.scores, tod = r.timeOfDay, weatherId = r.weatherId });
            if (rec.history.Count > 12) rec.history.RemoveRange(12, rec.history.Count - 12);
            c.exercises[m.id] = rec;

            // stats
            var st = c.stats;
            st.flights += 1;
            st.landings += r.landings.Count;
            st.smoothLandings += r.landings.Count(l => l.severity == TouchdownSeverity.Smooth);
            st.hardLandings += r.landings.Count(l => l.severity == TouchdownSeverity.Hard);
            if (r.outcome == FlightOutcome.Crashed) st.accidents += 1;
            st.distanceKm += Clamp(r.distanceKm, 0, 2000);
            st.maxAltFt = Math.Max(st.maxAltFt, Clamp(r.maxAltFt, -100, 30000));
            st.fuelKg += Clamp(r.fuelUsedKg, 0, 10000);

            // airframe wear: hours, hard landings, damage and limit exceedances all cost condition
            double conditionLost;
            {
                var a = Airframe(c, m.aircraftId);
                var hard = r.landings.Count(l => l.severity == TouchdownSeverity.Hard);
                var overLimit = r.exceedances.Count(e => OverLimit.IsMatch(e));
                var wear = flightSeconds / 3600 * 2 + hard * 12 + overLimit * 5 + Clamp(r.damage, 0, 1) * 60;
                if (r.outcome == FlightOutcome.Crashed) wear = 100;
                var condition = Math.Max(0, a.condition - wear);
                conditionLost = a.condition - condition - flightSeconds / 3600 * 2;   // normal wear is in the hire rate
                string ev = null;
                if (r.outcome == FlightOutcome.Crashed) ev = "Written off in an accident: a replacement airframe needs preparing";
                else if (hard > 0) ev = $"{hard} hard landing{(hard > 1 ? "s" : "")}: inspection recommended";
                else if (overLimit > 0) ev = "Limit exceedance recorded in the technical log";
                c.fleet[m.aircraftId] = new AirframeState { condition = condition, hoursSinceInspection = a.hoursSinceInspection + flightSeconds / 3600, inspections = a.inspections, lastEvent = ev ?? a.lastEvent };
                if (condition < SERVICEABLE_MIN) messages.Add($"{Aircraft.Find(m.aircraftId)?.name ?? m.aircraftId} is unserviceable until maintained in the Hangar");
            }

            // flight log
            var best = r.bestLanding;
            c.log.Insert(0, new FlightLogEntry { id = $"f{Base36(now)}{c.log.Count}", at = now, missionId = m.id, title = m.title, aircraftId = r.aircraftId, outcome = r.outcome, overall = r.overall, flightSeconds = flightSeconds, landingGrade = best != null ? GradeFor(best.score) : null, xp = r.xp });
            if (c.log.Count > 200) c.log.RemoveRange(200, c.log.Count - 200);

            // tests: qualifications awarded or revalidated, or remedial training set
            var awarded = new List<string>(); var renewed = new List<string>(); var remedialSet = new List<string>();
            if (m.category == MissionCategory.Test)
            {
                if (passed)
                {
                    c.remedial.Remove(m.id);
                    foreach (var q in Qualifications.ALL)
                    {
                        if (q.practicalTest != m.id) continue;
                        Progression.VALIDITY_HOURS.TryGetValue(q.id, out var vh);
                        if (!c.qualifications.ContainsKey(q.id) && QualificationEligible(c, q.id).eligible)
                        {
                            c.qualifications[q.id] = new QualRecord { awardedAt = now };
                            awarded.Add(q.id);
                            if (vh > 0) c.validity[q.id] = Hours(c) + vh;
                        }
                        else if (c.qualifications.ContainsKey(q.id) && vh > 0) { c.validity[q.id] = Hours(c) + vh; renewed.Add(q.id); }
                    }
                    if (awarded.Count > 0) messages.Add($"Awarded: {string.Join(", ", awarded.Select(a => Qualifications.ById(a)?.abbrev ?? a))}");
                    if (renewed.Count > 0) messages.Add($"Revalidated: {string.Join(", ", renewed.Select(a => Qualifications.ById(a)?.abbrev ?? a))}");
                }
                else if (r.outcome != FlightOutcome.Aborted)
                {
                    var req = RemedialFor(r);
                    if (req.Count > 0)
                    {
                        c.remedial[m.id] = new RemedialRecord { required = req.ToArray(), since = now };
                        remedialSet.AddRange(req);
                        messages.Add($"Remedial training set: {string.Join(", ", req.Select(id => Missions.ById(id)?.shortTitle ?? id))}");
                    }
                }
            }
            // reputation with the operator
            var emp = Employers.EmployerOf(m);
            var standingBefore = StandingWith(c, emp);
            var rc = Progression.ReputationChange(RepWith(c, emp), r);
            c.reputation[emp] = rc.rep;
            var standingAfter = StandingWith(c, emp);
            // money: contract fee scaled by how well it went; hire, repairs and test fees
            var items = new List<Ledger>();
            var pay = Progression.OfferedPay(m, standingBefore);
            if (pay > 0 && passed) items.Add(new Ledger { label = $"Contract fee: {m.shortTitle}", amount = Progression.JsRound(pay * (0.6 + 0.4 * Clamp(r.overall, 0, 100) / 100) / 10) * 10 });
            else if (pay > 0 && (r.outcome == FlightOutcome.Failed || r.outcome == FlightOutcome.Completed)) items.Add(new Ledger { label = "Part payment (task not fully completed)", amount = Progression.JsRound(pay * 0.3 / 10) * 10 });
            var hire = Progression.HireCost(m, flightSeconds);
            if (hire > 0) items.Add(new Ledger { label = $"Aircraft hire: {Aircraft.Find(m.aircraftId)?.name} ({flightSeconds / 60:F0} min)", amount = -hire });
            if (m.category == MissionCategory.Test && Employers.EmployerOf(m) == "school" && r.outcome != FlightOutcome.Aborted) items.Add(new Ledger { label = $"Examiner fee: {m.shortTitle}", amount = -Progression.SKILL_TEST_FEE });
            var rep2 = Progression.RepairCost(m.aircraftId, Math.Max(0, conditionLost));
            if (rep2 > 0) items.Add(new Ledger { label = r.outcome == FlightOutcome.Crashed ? "Insurance excess and repairs" : "Repairs after hard landings or damage", amount = -rep2 });
            var led = Progression.ApplyLedger(c.money, items);
            c.money = led.money;
            // statistics
            st.perAircraftS[m.aircraftId] = (st.perAircraftS.TryGetValue(m.aircraftId, out var pa) ? pa : 0) + flightSeconds;
            st.longestFlightS = Math.Max(st.longestFlightS, flightSeconds);
            if (passed && Operational(m.category)) st.operational += 1;
            if (passed && m.passengers is int px) st.passengers += px;
            if (passed && ((r.rescue?.Any(s => s.Contains("handed over")) ?? false) || m.casualty != null)) st.rescues += 1;
            c.updatedAt = now;
            var after = CompletedMilestones(c);
            return new CareerUpdate
            {
                career = c, awarded = awarded, milestones = after.Where(id => !beforeMilestones.Contains(id)).ToList(), remedial = remedialSet, messages = messages,
                ledger = led.lines, renewed = renewed, employer = emp, standingBefore = standingBefore, standingAfter = standingAfter, repNotes = rc.notes,
            };
        }

        /// <summary>Choose remedial exercises from the weakest assessed categories of a failed test.</summary>
        public static List<string> RemedialFor(FlightResult r)
        {
            var test = Missions.ById(r.missionId);
            var weak = r.scores.Where(kv => kv.Value < 70).OrderBy(kv => kv.Value).Select(kv => kv.Key).ToList();
            if (r.outcome == FlightOutcome.Crashed) weak.Insert(0, ScoreCategory.Handling);
            var picks = new List<string>();
            foreach (var cat in weak)
            {
                var pool = Missions.All.Where(m => m.category == MissionCategory.Training && m.remedialFor != null && m.remedialFor.Contains(cat) && !picks.Contains(m.id) && (test == null || m.aircraftId == test.aircraftId)).ToList();
                var own = test?.remedial?.FirstOrDefault(id => pool.Any(p => p.id == id));
                var ex = own != null ? Missions.ById(own) : pool.FirstOrDefault(m => test == null || test.pathway == Pathway.None || m.pathway == test.pathway || m.pathway == Pathway.None) ?? pool.FirstOrDefault();
                if (ex != null) picks.Add(ex.id);
                if (picks.Count >= 2) break;
            }
            if (picks.Count == 0) picks.AddRange(test?.remedial?.Take(1) ?? new[] { "ex04_circuits" });
            return picks;
        }

        public static Career ApplyModuleQuiz(Career prev, string moduleId, double pct)
        {
            var mod = Theory.ModuleById(moduleId);
            if (mod == null) return prev;
            var c = prev.Clone();
            var cur = c.modules.TryGetValue(moduleId, out var m0) ? m0 : new ModuleRecord();
            var p = Clamp(pct, 0, 100);
            cur.read = true;
            cur.bestPct = Math.Max(cur.bestPct, p);
            cur.passed = cur.passed || p >= mod.passMark;
            c.modules[moduleId] = cur;
            c.updatedAt = Now();
            return c;
        }

        /// <summary>Mark a module as read without a quiz score (opening it in ground school).</summary>
        public static Career MarkModuleRead(Career prev, string moduleId)
        {
            if (Theory.ModuleById(moduleId) == null) return prev;
            var c = prev.Clone();
            var cur = c.modules.TryGetValue(moduleId, out var m0) ? m0 : new ModuleRecord();
            cur.read = true; c.modules[moduleId] = cur; c.updatedAt = Now();
            return c;
        }

        public static Career ApplyExam(Career prev, string examId, double pct, long? nowMs = null)
        {
            var now = nowMs ?? Now();
            var ex = Theory.ExamById(examId);
            if (ex == null || !ExamAvailability(prev, examId).available) return prev;
            var c = prev.Clone();
            var cur = c.exams.TryGetValue(examId, out var e0) ? e0 : new ExamRecord();
            var p = Clamp(pct, 0, 100);
            cur.attempts += 1;
            cur.bestPct = Math.Max(cur.bestPct, p);
            cur.passed = cur.passed || p >= ex.passMark;
            cur.lastAt = now;
            c.exams[examId] = cur;
            c.money = Progression.ApplyLedger(c.money, new[] { new Ledger { label = $"Exam fee: {ex.title}", amount = -Progression.EXAM_FEE } }).money;
            c.xp += cur.passed && p >= ex.passMark ? 100 : 10;
            c.updatedAt = now;
            return c;
        }

        // ------------------------------------------------------------------ qualification eligibility
        public static (bool eligible, List<ReqItem> items) QualificationEligible(Career c, string id)
        {
            var q = Qualifications.ById(id);
            if (q == null) return (false, new List<ReqItem>());
            var items = new List<ReqItem>();
            foreach (var p in q.prerequisites) items.Add(new ReqItem($"Hold {Qualifications.ById(p)?.abbrev ?? p}", HasQual(c, p)));
            if (q.requiresSenior) items.Add(new ReqItem("Senior Pilot status", SeniorStatus(c).met));
            if (q.requiredHours > 0) items.Add(new ReqItem($"{q.requiredHours:0.#} h flight time ({Hours(c):F1} h logged)", Hours(c) >= q.requiredHours));
            foreach (var e in q.requiredExercises) items.Add(new ReqItem($"Pass {Missions.ById(e)?.shortTitle ?? e}", PassedExercise(c, e)));
            foreach (var x in q.theoryExams) items.Add(new ReqItem(Theory.ExamById(x) != null ? $"Pass the {Theory.ExamById(x).title}" : $"Theory: {x} (future content)", ExamPassed(c, x)));
            string testLabel = null;
            if (q.practicalTest != null)
            {
                var tm = Missions.ById(q.practicalTest);
                testLabel = tm != null ? $"Pass the {tm.title}" : "Practical assessment (future content)";
                items.Add(new ReqItem(testLabel, PassedExercise(c, q.practicalTest)));
            }
            // eligibility for the award excludes the practical test itself (it is being passed right now)
            var eligible = items.Where(i => i.label != testLabel).All(i => i.met);
            return (eligible, items);
        }

        // --------------------------------------------------------------------------- career milestones
        static readonly string[] SAR_CALLOUTS = { "sar01_missing_walker", "sar02_coastal_swimmer", "sar03_mountain", "sar04_road_accident", "sar05_night_liferaft" };
        static readonly string[] WINCH_CALLOUTS = { "sar01_missing_walker", "sar02_coastal_swimmer", "sar03_mountain", "sar05_night_liferaft" };
        static readonly string[] POLICE_JOBS = { "pol01_missing_child", "pol02_night_suspects", "pol03_firearms" };
        static readonly string[] MILITARY_JOBS = { "mil01_insertion", "mil02_night_casevac", "mil03_deck" };

        public static readonly List<MilestoneDef> MILESTONES = new List<MilestoneDef>
        {
            new MilestoneDef { id = "first_flight", title = "First Flight", objective = "Lift a helicopter off the ground and put it back down.", criteria = "Pass Exercise 1.", reward = "50 XP", unlocks = "Hover training", prereqs = new string[0], next = new[] { "first_hover" }, done = c => PassedExercise(c, "ex01_controls") || HasQual(c, "ppl_h"), trackedMission = "ex01_controls", contentMilestone = 1 },
            new MilestoneDef { id = "first_hover", title = "First Hover", objective = "Hold a stable hover over a spot.", criteria = "Pass Exercise 2: 20 s inside a 4 m circle.", reward = "70 XP", unlocks = "Take-off and landing training", prereqs = new[] { "first_flight" }, next = new[] { "first_landing" }, done = c => PassedExercise(c, "ex02_hover") || HasQual(c, "ppl_h"), trackedMission = "ex02_hover", contentMilestone = 2 },
            new MilestoneDef { id = "first_landing", title = "First Precision Landing", objective = "Land on a spot within the assessment limits.", criteria = "Pass Exercise 3.", reward = "90 XP", unlocks = "Circuits", prereqs = new[] { "first_hover" }, next = new[] { "solo" }, done = c => PassedExercise(c, "ex03_takeoff_landing") || HasQual(c, "ppl_h"), trackedMission = "ex03_takeoff_landing", contentMilestone = 2 },
            new MilestoneDef { id = "solo", title = "Solo Flight", objective = "Fly a circuit with nobody beside you.", criteria = "Pass Exercise 4, then Exercise 5 (First Solo).", reward = "150 XP · Solo Student rank", unlocks = "Navigation training", prereqs = new[] { "first_landing" }, next = new[] { "nav_qualified" }, done = c => PassedExercise(c, "ex06_first_solo") || HasQual(c, "ppl_h"), trackedMission = "ex06_first_solo", contentMilestone = 3 },
            new MilestoneDef { id = "nav_qualified", title = "Navigation Qualified", objective = "Navigate a cross-country triangle with a field landing.", criteria = "Pass Exercise 6.", reward = "170 XP", unlocks = "PPL(H) skill test", prereqs = new[] { "solo" }, next = new[] { "ppl" }, done = c => PassedExercise(c, "ex07_navigation") || HasQual(c, "ppl_h"), trackedMission = "ex07_navigation", contentMilestone = 3 },
            new MilestoneDef { id = "ppl", title = "Private Pilot", objective = "Earn your Private Pilot Licence (Helicopters).", criteria = "Pass the theory exam and the PPL(H) skill test.", reward = "PPL(H) + Kestrel type rating · Private Pilot rank", unlocks = "Hour-building flights; CPL(H) training", prereqs = new[] { "nav_qualified" }, next = new[] { "commercial_ready" }, done = c => HasQual(c, "ppl_h"), trackedMission = "st_ppl", contentMilestone = 3 },
            new MilestoneDef { id = "commercial_ready", title = "Commercial Ready", objective = "Complete advanced training and earn a CPL(H).", criteria = "CPL(H) theory, emergencies, advanced navigation, skill test.", reward = "CPL(H) · Commercial Pilot rank", unlocks = "Instrument, night and multi-engine training", prereqs = new[] { "ppl" }, next = new[] { "first_sar", "offshore_ready", "police_ready", "military_ready" }, done = c => HasQual(c, "cpl_h"), contentMilestone = 7 },
            new MilestoneDef { id = "first_sar", title = "First SAR Callout", objective = "Complete your first search and rescue callout.", criteria = "Guardian S9 type rating, winch assessment, SAR line check, then any successful callout.", reward = "Saltire Rescue standing", unlocks = "Mountain and night SAR", prereqs = new[] { "commercial_ready" }, next = new[] { "rescue_specialist" }, done = c => SAR_CALLOUTS.Any(id => PassedExercise(c, id)), contentMilestone = 5 },
            new MilestoneDef { id = "rescue_specialist", title = "Rescue Specialist", objective = "Pass the rescue and hoist assessments and recover casualties by winch.", criteria = "Winch and SAR authorisations, and two different winch rescues.", reward = "Rescue Specialist standing", unlocks = "Senior pilot path", prereqs = new[] { "first_sar" }, next = new[] { "senior_pilot" }, done = c => HasQual(c, "auth_winch") && HasQual(c, "auth_sar") && WINCH_CALLOUTS.Count(id => PassedExercise(c, id)) >= 2, contentMilestone = 5 },
            new MilestoneDef { id = "offshore_ready", title = "Offshore Ready", objective = "Complete offshore familiarisation and authorisation.", criteria = "Offshore authorisation.", reward = "Pentland Offshore Aviation contract", unlocks = "Offshore rotations", prereqs = new[] { "commercial_ready" }, next = new[] { "first_rotation" }, done = c => HasQual(c, "auth_offshore"), contentMilestone = 6 },
            new MilestoneDef { id = "first_rotation", title = "First Offshore Rotation", objective = "Complete a passenger transport contract.", criteria = "Fly the Cormorant Bravo crew change.", reward = "Contract fee · Pentland standing", unlocks = "Multi-platform rotations", prereqs = new[] { "offshore_ready" }, next = new[] { "multi_platform" }, done = c => PassedExercise(c, "off02_crew_change"), trackedMission = "off02_crew_change", contentMilestone = 6 },
            new MilestoneDef { id = "multi_platform", title = "Multi-Platform Pilot", objective = "Complete a multi-installation route.", criteria = "Fly the three-platform rotation on time and on fuel.", reward = "Pentland standing", unlocks = "Offshore medevac", prereqs = new[] { "first_rotation" }, next = new[] { "senior_pilot" }, done = c => PassedExercise(c, "off03_multi_platform"), trackedMission = "off03_multi_platform", contentMilestone = 6 },
            new MilestoneDef { id = "police_ready", title = "Police Air Support", objective = "Qualify on the Peregrine and fly your first police job.", criteria = "Peregrine P3 type rating, police authorisation (thermal search training and a night line check), then any police callout.", reward = "Police Air Support Unit standing", unlocks = "Night searches and firearms deployments", prereqs = new[] { "commercial_ready" }, next = new[] { "senior_pilot" }, done = c => POLICE_JOBS.Any(id => PassedExercise(c, id)), contentMilestone = 8 },
            new MilestoneDef { id = "military_ready", title = "Military Tasking", objective = "Qualify for military contract flying and complete a tasking.", criteria = "Peregrine P3 type rating, military authorisation (low-level navigation and a tactical check), then any military tasking.", reward = "Joint Helicopter Detachment standing", unlocks = "Night casualty evacuation and deck landings", prereqs = new[] { "commercial_ready" }, next = new[] { "senior_pilot" }, done = c => MILITARY_JOBS.Any(id => PassedExercise(c, id)), contentMilestone = 8 },
            new MilestoneDef { id = "night_qualified", title = "Night Qualified", objective = "Complete night training.", criteria = "Night rating.", reward = "Night rating", unlocks = "Night search", prereqs = new[] { "commercial_ready" }, next = new[] { "senior_pilot" }, done = c => HasQual(c, "night_h"), contentMilestone = 7 },
            new MilestoneDef { id = "senior_pilot", title = "Senior Pilot", objective = "Meet the experience and performance requirements.", criteria = "8 h, 8 successful operational flights, no accidents in the last 10 flights, standing of 65 with an operator.", reward = "Senior Pilot rank", unlocks = "Training Captain path", prereqs = new[] { "rescue_specialist", "multi_platform", "first_sar", "first_rotation", "police_ready", "military_ready" }, next = new[] { "training_captain" }, done = c => SeniorStatus(c).met, contentMilestone = 8 },
            new MilestoneDef { id = "training_captain", title = "Training Captain", objective = "Qualify as a training captain and supervise line training.", criteria = "Training Captain authorisation and a line training flight as supervising captain.", reward = "Training Captain rank", unlocks = "Assessing other pilots", prereqs = new[] { "senior_pilot" }, next = new string[0], done = c => HasQual(c, "tc_auth") && PassedExercise(c, "tc2_line_training"), trackedMission = "tc1_instructor_check", contentMilestone = 8 },
        };

        static readonly Dictionary<Pathway, string> PATHWAY_GOAL = new Dictionary<Pathway, string>
        {
            [Pathway.Tc] = "tc_never", [Pathway.Ppl] = "ppl_h", [Pathway.None] = "ppl_h", [Pathway.Cpl] = "cpl_h", [Pathway.Night] = "night_h", [Pathway.Ns175] = "type_northstar",
            [Pathway.S9] = "type_guardian", [Pathway.Ir] = "ir_h", [Pathway.Winch] = "auth_winch", [Pathway.Sar] = "auth_sar", [Pathway.Offshore] = "auth_offshore",
            [Pathway.P3] = "type_peregrine", [Pathway.Police] = "auth_police", [Pathway.Military] = "auth_military",
        };

        /// <summary>Training syllabus in recommended order. Pathways run in parallel once their entry qualification is held.</summary>
        public static readonly string[] SYLLABUS = { "ex01_controls", "ex02_hover", "ex03_takeoff_landing", "ex04_circuits", "ex06_first_solo", "ex07_navigation", "st_ppl",
            "cpl1_advanced_nav", "cpl2_confined_area", "cpl3_emergencies", "st_cpl", "nt01_night_circuits", "st_night",
            "tr_s9", "st_s9", "sar_t1_search", "sar_t2_winch", "lc_sar",
            "tr_ns175", "st_ns175", "off01_familiarisation", "lc_offshore", "ir1_basic_instruments", "ir2_gps_approach", "st_ir",
            "tr_p3", "st_p3", "pol_t1_thermal", "lc_police", "mil_t1_lowlevel", "lc_military",
            "tc1_instructor_check", "tc2_line_training" };

        public static List<string> CompletedMilestones(Career c) => MILESTONES.Where(m => m.done(c)).Select(m => m.id).ToList();

        public static MilestoneState StateOf(Career c, MilestoneDef m)
        {
            if (m.done(c)) return MilestoneState.Completed;
            var prereqsMet = m.prereqs.Length == 0 || m.prereqs.Any(p => MILESTONES.FirstOrDefault(x => x.id == p)?.done(c) ?? false);
            if (!prereqsMet || m.contentMilestone > Qualifications.CURRENT_MILESTONE) return MilestoneState.Locked;
            var rec = m.trackedMission != null && c.exercises.TryGetValue(m.trackedMission, out var r) ? r : null;
            if (rec != null && rec.attempts > 0 && rec.passes == 0) return MilestoneState.Retry;
            if (rec != null && rec.attempts > 0) return MilestoneState.InProgress;
            return MilestoneState.Available;
        }

        /// <summary>The single most useful next thing to do, shown prominently on the HQ screen.</summary>
        public static Recommendation NextRecommendation(Career c)
        {
            // remedial first
            foreach (var kv in c.remedial)
                foreach (var r in kv.Value.required.Where(id => Missions.ById(id) != null))
                {
                    var rec = c.exercises.TryGetValue(r, out var x) ? x : null;
                    if (rec == null || rec.lastPassAt == null || rec.lastPassAt < kv.Value.since)
                        return new Recommendation { kind = "mission", id = r, title = Missions.ById(r)?.title ?? r, why = $"Remedial training before re-sitting the {Missions.ById(kv.Key)?.shortTitle}" };
                }
            // recurrent checks that are due or have lapsed
            foreach (var q in Qualifications.ALL)
            {
                var cur = CurrencyOf(c, q.id);
                if ((cur == Currency.Due || cur == Currency.Expired) && q.practicalTest != null && Missions.ById(q.practicalTest) != null)
                {
                    var m = Missions.ById(q.practicalTest);
                    if (MissionAvailability(c, m).available)
                        return new Recommendation { kind = "mission", id = m.id, title = m.title, why = $"{q.abbrev} {(cur == Currency.Due ? "is due for its recurrent check" : "has lapsed")}: fly the check to revalidate it." };
                }
            }
            (MissionDef m, double h)? hoursFor = null;
            foreach (var id in SYLLABUS)
            {
                var m = Missions.ById(id);
                if (m == null || PassedExercise(c, m.id)) continue;
                if (HasQual(c, PATHWAY_GOAL[m.pathway])) continue;   // e.g. a commercial start skips PPL lessons
                var awards = Qualifications.ALL.Where(q => q.practicalTest == m.id).ToList();
                if (awards.Count > 0 && awards.All(q => HasQual(c, q.id))) continue;
                var a = MissionAvailability(c, m);
                if (a.future) continue;
                if (a.available) return new Recommendation { kind = "mission", id = m.id, title = m.title, why = m.summary };
                var p = m.prerequisites ?? new Prerequisites();
                var structural = (p.exercises ?? new string[0]).All(e => PassedExercise(c, e)) && (p.quals ?? new string[0]).All(q => HasQual(c, q));
                if (!structural) continue;
                var svc = Serviceability(c, m.aircraftId);
                if (svc.Count > 0) return new Recommendation { kind = "hangar", id = m.aircraftId, title = $"Maintain the {Aircraft.Find(m.aircraftId)?.name ?? m.aircraftId}", why = svc[0] };
                var needMod = (p.theory ?? new string[0]).FirstOrDefault(t => !ModulePassed(c, t));
                if (needMod != null) return new Recommendation { kind = "module", id = needMod, title = Theory.ModuleById(needMod).title, why = $"Ground school needed before {m.shortTitle}" };
                var needExam = (p.exams ?? new string[0]).FirstOrDefault(x => !ExamPassed(c, x));
                if (needExam != null)
                {
                    var mods = Theory.ExamById(needExam).prerequisites.FirstOrDefault(md => !ModulePassed(c, md));
                    if (mods != null) return new Recommendation { kind = "module", id = mods, title = Theory.ModuleById(mods).title, why = "Complete ground school, then sit the theory exam" };
                    return new Recommendation { kind = "exam", id = needExam, title = Theory.ExamById(needExam).title, why = $"Required for the {m.shortTitle}" };
                }
                if (p.minHours is double mh && Hours(c) < mh && hoursFor == null) hoursFor = (m, mh);
            }
            // operational flying once authorised
            foreach (var m in Missions.All.Where(x => Operational(x.category)))
            {
                if (PassedExercise(c, m.id)) continue;
                if (MissionAvailability(c, m).available) return new Recommendation { kind = "mission", id = m.id, title = m.title, why = m.summary };
            }
            if (hoursFor is (MissionDef hm, double hh))
            {
                var builder = HasQual(c, "ppl_h") ? "pv_coastal_survey" : "ex04_circuits";
                return new Recommendation { kind = "mission", id = builder, title = Missions.ById(builder).title, why = $"Build flight time: {hh:0.#} h needed for the {hm.shortTitle} (you have {Hours(c):F1} h)" };
            }
            if (HasQual(c, "ppl_h")) return new Recommendation { kind = "mission", id = "pv_coastal_survey", title = "Hour Building · Coastal Survey", why = "Build hours and keep current." };
            return new Recommendation { kind = "none", id = "", title = "All available training complete", why = "" };
        }

        public static string GradeFor(int score) => score >= 90 ? "A" : score >= 78 ? "B" : score >= 65 ? "C" : score >= 50 ? "D" : "F";
        static double Clamp(double v, double lo, double hi) => double.IsNaN(v) || double.IsInfinity(v) ? lo : Math.Min(hi, Math.Max(lo, v));
    }
}
