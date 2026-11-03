// Employers, reputation, the economy and recurrent checks (port of src/game/employers.ts and progression.ts).
// Pure functions over plain data, called by the career manager when a flight result is applied. Deliberately
// forgiving: a bad flight costs some standing and money, but can never make progress impossible.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Rotorcraft.Sim;

namespace Rotorcraft.Game
{
    public sealed class EmployerDef
    {
        public string id, name, kind, description;
        /// <summary>Qualifications needed before the employer offers contract work.</summary>
        public string[] requires;
        /// <summary>Base for contract pay; missions scale it by length and difficulty.</summary>
        public double payRate;
    }

    public static class Employers
    {
        public static readonly List<EmployerDef> ALL = new List<EmployerDef>
        {
            new EmployerDef { id = "school", name = "Kinloch Rotor School", kind = "Training school", description = "Flight training from first lesson to commercial licence, instrument and type ratings. Later, a place to instruct.", requires = new string[0], payRate = 0 },
            new EmployerDef { id = "highland", name = "Highland Utility Services", kind = "Utility operator", description = "Power-line patrols, survey and light-lift work across the glens. A first commercial job for new CPL holders.", requires = new[] { "cpl_h" }, payRate = 9 },
            new EmployerDef { id = "northern", name = "Northern Air Ambulance", kind = "Medical transport", description = "Inter-hospital transfers and scene responses in the Northstar. Twin-engine and instrument rated pilots only.", requires = new[] { "type_northstar", "ir_h" }, payRate = 12 },
            new EmployerDef { id = "pentland", name = "Pentland Offshore Aviation", kind = "Offshore transport", description = "Crew changes, freight and medevac to the North Sea platforms.", requires = new[] { "type_northstar", "auth_offshore" }, payRate = 12 },
            new EmployerDef { id = "police", name = "Police Air Support Unit", kind = "Police aviation", description = "Air support for the police from Kinloch in the Peregrine: searches for missing people and suspects with the thermal camera and searchlight, by day and night, and moving officers fast.", requires = new[] { "type_peregrine", "auth_police" }, payRate = 12 },
            new EmployerDef { id = "military", name = "Joint Helicopter Detachment", kind = "Military aviation (contract)", description = "Support flying for the armed forces on the Highland ranges in the Peregrine: troop insertions, casualty evacuation, low-level navigation and deck landings.", requires = new[] { "type_peregrine", "auth_military" }, payRate = 13 },
            new EmployerDef { id = "saltire", name = "Saltire Rescue", kind = "Search and rescue contractor", description = "Coastguard search and rescue from Kinloch: searches, winch rescues, mountain and night callouts.", requires = new[] { "type_guardian", "auth_sar" }, payRate = 13 },
        };

        public static EmployerDef ById(string id) => ALL.FirstOrDefault(e => e.id == id);

        /// <summary>Which operator a mission is flown for.</summary>
        public static string EmployerOf(MissionDef m)
        {
            if (!string.IsNullOrEmpty(m.employerId)) return m.employerId;
            if (m.category == MissionCategory.Sar || m.pathway == Pathway.Sar || m.pathway == Pathway.Winch || m.pathway == Pathway.S9) return "saltire";
            if (m.category == MissionCategory.Offshore || m.pathway == Pathway.Offshore) return "pentland";
            if (m.category == MissionCategory.Police || m.pathway == Pathway.Police || m.pathway == Pathway.P3) return "police";
            if (m.category == MissionCategory.Military || m.pathway == Pathway.Military) return "military";
            if (m.category == MissionCategory.Utility) return "highland";
            if (m.category == MissionCategory.Medical) return "northern";
            return "school";
        }
    }

    public sealed class Reputation
    {
        public double safety = 50, professionalism = 50, reliability = 50, performance = 50;
        public Reputation Clone() => (Reputation)MemberwiseClone();
    }

    public sealed class Money { public double balance, loan, earned, spent; public Money Clone() => (Money)MemberwiseClone(); }
    public sealed class Ledger { public string label; public double amount; }

    public enum Currency { Current, Due, Expired, None }

    public static class Progression
    {
        public static readonly string[] REP_KEYS = { "safety", "professionalism", "reliability", "performance" };
        public static readonly Dictionary<string, string> REP_LABELS = new Dictionary<string, string> { ["safety"] = "Safety", ["professionalism"] = "Professionalism", ["reliability"] = "Reliability", ["performance"] = "Mission performance" };

        public static double Get(Reputation r, string key) => key switch { "safety" => r.safety, "professionalism" => r.professionalism, "reliability" => r.reliability, _ => r.performance };
        static void Set(Reputation r, string key, double v) { switch (key) { case "safety": r.safety = v; break; case "professionalism": r.professionalism = v; break; case "reliability": r.reliability = v; break; default: r.performance = v; break; } }

        /// <summary>Overall standing with an employer: safety counts double, because good decisions matter as much as speed.</summary>
        public static int Standing(Reputation r) => JsRound((r.safety * 2 + r.professionalism + r.reliability + r.performance) / 5);

        static double ClampRep(double v) => Math.Max(0, Math.Min(100, v));
        public static int JsRound(double v) => (int)Math.Floor(v + 0.5);

        static readonly Regex Serious = new Regex("transient|overspeed|VNE|blade stall|°C");

        /// <summary>How one flight changes the reputation with its employer: the new values and the reasons.</summary>
        public static (Reputation rep, List<string> notes) ReputationChange(Reputation prev, FlightResult r)
        {
            var notes = new List<string>();
            var d = new Reputation { safety = 0, professionalism = 0, reliability = 0, performance = 0 };
            var hard = r.landings.Count(l => l.severity == TouchdownSeverity.Hard);
            if (r.outcome == FlightOutcome.Crashed) { d.safety -= 18; notes.Add("Accident"); }
            else
            {
                d.safety += 2;
                if (hard > 0) { d.safety -= 5 * hard; notes.Add($"{hard} hard landing{(hard > 1 ? "s" : "")}"); }
                var serious = r.exceedances.Count(e => Serious.IsMatch(e));
                if (serious > 0) { d.safety -= 3 * serious; notes.Add("Limit exceedances"); }
                if (r.warnings.Any(w => w.StartsWith("In cloud"))) { d.safety -= 6; notes.Add("Flew into cloud"); }
                var dec = r.scores.TryGetValue(ScoreCategory.Decisions, out var dv) ? dv : (int?)null;
                if ((dec ?? 100) < 60) { d.safety -= 6; notes.Add("Unsafe decision"); }
                if ((dec ?? 0) >= 90) { d.safety += 3; notes.Add("Sound decisions"); }
                if (r.fuelRemainingKg < 0.5) { d.safety -= 8; notes.Add("Ran the tanks dry"); }
            }
            var proc = r.scores.TryGetValue(ScoreCategory.Procedures, out var pv) ? pv : 80;
            d.professionalism += JsRound((proc - 75) / 8.0);
            if (!r.checklistComplete) d.professionalism -= 2;
            if (r.outcome == FlightOutcome.Aborted) { d.reliability -= 3; notes.Add("Flight not completed"); }
            else if (r.outcome != FlightOutcome.Crashed) d.reliability += 2;
            if (r.scores.TryGetValue(ScoreCategory.Timeliness, out var tv)) d.reliability += JsRound((tv - 75) / 8.0);
            if (r.outcome != FlightOutcome.Aborted && r.outcome != FlightOutcome.Crashed) d.performance += JsRound((r.overall - 70) / 6.0);
            var rep = prev.Clone();
            foreach (var k in REP_KEYS) Set(rep, k, ClampRep(Math.Round((Get(prev, k) + Get(d, k)) * 10) / 10));
            return (rep, notes);
        }

        // ------------------------------------------------------------------------------------ economy
        public static double StartingFunds(CareerStart s) => s == CareerStart.Student ? 3000 : 12000;
        public const double EXAM_FEE = 120;
        public const double SKILL_TEST_FEE = 350;
        /// <summary>Share of later earnings that repays the training loan.</summary>
        public const double LOAN_REPAY_SHARE = 0.25;

        static bool Operational(MissionCategory c) => c == MissionCategory.Sar || c == MissionCategory.Offshore || c == MissionCategory.Utility || c == MissionCategory.Medical || c == MissionCategory.Police || c == MissionCategory.Military;

        /// <summary>Contract fee offered for a mission at the given standing (0 for training and private flights).</summary>
        public static double OfferedPay(MissionDef m, double standingWithEmployer = 50)
        {
            var emp = Employers.ById(Employers.EmployerOf(m));
            var b = m.pay ?? (emp != null && emp.payRate > 0 && Operational(m.category) ? JsRound(m.estMinutes * emp.payRate * 10 / 10) * 10 : 0);
            if (b == 0) return 0;
            return JsRound(b * (0.8 + 0.4 * standingWithEmployer / 100) / 10) * 10;
        }

        /// <summary>Hire cost for flights you pay for yourself: training and private flying in the school's Kestrel.</summary>
        public static double HireCost(MissionDef m, double flightSeconds)
        {
            var self = m.category == MissionCategory.Training || m.category == MissionCategory.Private || m.category == MissionCategory.Practice || (m.category == MissionCategory.Test && Employers.EmployerOf(m) == "school");
            if (!self) return 0;
            var ac = Aircraft.Find(m.aircraftId);
            // twin training is paid by the employer sponsoring the type rating; the pilot pays for the light trainer
            if (ac == null || ac.engines > 1) return 0;
            return JsRound(ac.operatingCostPerHour * Math.Max(0.1, flightSeconds / 3600) / 5) * 5;
        }

        /// <summary>Repair bill after damage or hard landings in a hired aircraft (operators cover their own fleet).</summary>
        public static double RepairCost(string acId, double conditionLost)
        {
            var ac = Aircraft.Find(acId);
            if (ac == null || ac.engines > 1) return 0;
            return JsRound(conditionLost * 25 / 5) * 5;
        }

        /// <summary>Apply ledger items. Earnings first repay part of the loan; costs that cannot be paid from the
        /// balance go onto the training loan instead of blocking progress.</summary>
        public static (Money money, List<Ledger> lines) ApplyLedger(Money prev, IEnumerable<Ledger> items)
        {
            var m = (prev ?? new Money()).Clone();
            var lines = new List<Ledger>();
            foreach (var it in items)
            {
                if (it.amount == 0) continue;
                if (it.amount > 0)
                {
                    m.earned += it.amount;
                    var repay = Math.Min(m.loan, JsRound(it.amount * LOAN_REPAY_SHARE));
                    m.loan -= repay; m.balance += it.amount - repay;
                    lines.Add(it);
                    if (repay > 0) lines.Add(new Ledger { label = "Loan repayment", amount = -repay });
                }
                else
                {
                    var cost = -it.amount;
                    m.spent += cost;
                    var fromBalance = Math.Min(Math.Max(0, m.balance), cost);
                    m.balance -= fromBalance;
                    m.loan += cost - fromBalance;
                    lines.Add(it);
                    if (cost - fromBalance > 0) lines.Add(new Ledger { label = "Added to training loan", amount = 0 });
                }
            }
            return (m, lines);
        }

        // ---------------------------------------------------------------------------- recurrent checks
        /// <summary>Validity in flight hours (game-scaled) for qualifications that need recurrent checks.</summary>
        public static readonly Dictionary<string, double> VALIDITY_HOURS = new Dictionary<string, double>
        {
            ["type_northstar"] = 5, ["type_guardian"] = 5, ["type_peregrine"] = 5, ["auth_police"] = 4, ["auth_military"] = 4, ["ir_h"] = 6, ["auth_offshore"] = 4, ["auth_sar"] = 4, ["auth_winch"] = 4, ["tc_auth"] = 8,
        };

        /// <summary>Whether a held qualification is still valid at hoursNow. "Due" is the last hour before expiry.</summary>
        public static Currency CurrencyOf(double? validUntil, double hoursNow)
        {
            if (validUntil == null) return Currency.None;
            if (hoursNow > validUntil) return Currency.Expired;
            if (hoursNow > validUntil - 1) return Currency.Due;
            return Currency.Current;
        }
    }
}
