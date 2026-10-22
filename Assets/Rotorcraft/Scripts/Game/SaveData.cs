// Saved data checks and the freeplay record (port of the validation in src/game/save.ts, freeplayStore.ts and
// company.ts). Anything read back from disk (or the cloud) is normalised here: missing parts are filled in,
// numbers clamped, and unknown ids dropped, so a damaged or tampered file can never break the game.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Rotorcraft.Sim;
using Rotorcraft.World;

namespace Rotorcraft.Game
{
    /// <summary>Freeplay records: the last loadout used and running totals. Separate from the career save, so
    /// nothing earned in freeplay can be carried into the career.</summary>
    public sealed class FreeplayRecord
    {
        public int shifts, jobs;
        public double earned, seconds, bestShift;
        public FreeplayConfig last;
    }

    public static class SaveData
    {
        public static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            Converters = { new StringEnumConverter() },
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            Formatting = Formatting.Indented,
        };

        public static string Write<T>(T v) => JsonConvert.SerializeObject(v, Json);
        public static T Read<T>(string s) => JsonConvert.DeserializeObject<T>(s, Json);

        static double Num(double v, double lo, double hi, double d) => double.IsNaN(v) || double.IsInfinity(v) ? d : Math.Min(hi, Math.Max(lo, v));

        /// <summary>Validate and normalise a parsed career; null if it cannot be a career.</summary>
        public static Career ValidateCareer(Career v)
        {
            if (v == null || v.version > CareerManager.SAVE_VERSION || string.IsNullOrEmpty(v.id) || v.pilotName == null) return null;
            v.pilotName = CareerManager.SanitizeName(v.pilotName);
            v.flightSeconds = Num(v.flightSeconds, 0, 1e8, 0);
            v.xp = Math.Max(0, v.xp);
            v.priorSeconds = Num(v.priorSeconds, 0, 10 * 3600, 0);
            v.exercises = v.exercises ?? new Dictionary<string, ExerciseRecord>();
            foreach (var r in v.exercises.Values) r.history = r.history ?? new List<AttemptRecord>();
            v.modules = v.modules ?? new Dictionary<string, ModuleRecord>();
            v.exams = v.exams ?? new Dictionary<string, ExamRecord>();
            v.qualifications = (v.qualifications ?? new Dictionary<string, QualRecord>()).Where(kv => Qualifications.ById(kv.Key) != null).ToDictionary(kv => kv.Key, kv => kv.Value);
            v.remedial = v.remedial ?? new Dictionary<string, RemedialRecord>();
            v.log = (v.log ?? new List<FlightLogEntry>()).Take(200).ToList();
            v.stats = v.stats ?? new CareerStats();
            v.stats.perAircraftS = v.stats.perAircraftS ?? new Dictionary<string, double>();
            v.fleet = v.fleet ?? new Dictionary<string, AirframeState>();
            foreach (var a in v.fleet.Values) { a.condition = Num(a.condition, 0, 100, 100); a.hoursSinceInspection = Num(a.hoursSinceInspection, 0, 1e6, 0); }
            v.money = v.money ?? new Money { balance = 3000 };
            v.money.loan = Math.Max(0, v.money.loan);
            v.reputation = (v.reputation ?? new Dictionary<string, Reputation>()).Where(kv => Employers.ById(kv.Key) != null).ToDictionary(kv => kv.Key, kv => kv.Value ?? new Reputation());
            foreach (var r in v.reputation.Values) { r.safety = Num(r.safety, 0, 100, 50); r.professionalism = Num(r.professionalism, 0, 100, 50); r.reliability = Num(r.reliability, 0, 100, 50); r.performance = Num(r.performance, 0, 100, 50); }
            v.validity = v.validity ?? new Dictionary<string, double>();
            // saves from before recurrent checks: give existing recurrent qualifications a fresh validity period
            foreach (var q in v.qualifications.Keys)
                if (Progression.VALIDITY_HOURS.TryGetValue(q, out var vh) && !v.validity.ContainsKey(q)) v.validity[q] = CareerManager.Hours(v) + vh;
            return v;
        }

        public static FreeplayConfig ValidateFreeplayConfig(FreeplayConfig v)
        {
            if (v == null || Aircraft.Find(v.aircraftId) == null) return null;
            var ac = Aircraft.Get(v.aircraftId);
            v.kit = (v.kit ?? new List<KitId>()).Distinct().Where(k => Loadout.CanFit(ac, k)).ToList();
            if (WorldMap.FindSite(v.baseSiteId) == null) v.baseSiteId = "kin_h1";
            if (v.weatherId != "random" && !Weather.Presets.ContainsKey(v.weatherId ?? "")) v.weatherId = "calm";
            v.fuelKg = Num(v.fuelKg, 0, 5000, 0);
            v.seed = Num(v.seed, 0, 2147483648.0, 1);
            if (v.skin != null && !(ac.skins?.Any(s => s.id == v.skin) ?? false)) v.skin = null;
            return v;
        }

        public static FreeplayRecord ValidateFreeplay(FreeplayRecord v)
        {
            if (v == null) return new FreeplayRecord();
            v.shifts = Math.Max(0, v.shifts); v.jobs = Math.Max(0, v.jobs);
            v.earned = Num(v.earned, -1e9, 1e10, 0); v.seconds = Num(v.seconds, 0, 1e9, 0); v.bestShift = Num(v.bestShift, -1e9, 1e9, 0);
            v.last = ValidateFreeplayConfig(v.last);
            return v;
        }

        /// <summary>Add a finished shift to the totals.</summary>
        public static FreeplayRecord RecordShift(FreeplayRecord prev, FreeplaySummary sum, FreeplayConfig cfg)
        {
            var net = sum.earned - sum.fuelCost;
            return new FreeplayRecord
            {
                shifts = prev.shifts + 1, jobs = prev.jobs + sum.jobs.Count(j => j.outcome == JobOutcome.Completed), earned = prev.earned + net,
                seconds = prev.seconds + Math.Max(0, sum.flightSeconds), bestShift = Math.Max(prev.bestShift, net), last = cfg,
            };
        }

        /// <summary>Check a stored company. Anything malformed is dropped or clamped; null if it is not a company.</summary>
        public static Company ValidateCompany(Company v)
        {
            if (v == null || v.version != 1) return null;
            v.name = string.IsNullOrWhiteSpace(v.name) ? "Kinloch Helicopters" : v.name.Length > 40 ? v.name.Substring(0, 40) : v.name;
            v.fleet = (v.fleet ?? new List<OwnedAircraft>()).Where(a => a != null && Aircraft.Find(a.aircraftId) != null).Take(30).ToList();
            v.bases = new[] { "kin_h1" }.Concat((v.bases ?? new List<string>()).Where(b => Companies.BASES.Any(x => x.siteId == b))).Distinct().ToList();
            foreach (var a in v.fleet)
            {
                var ac = Aircraft.Get(a.aircraftId);
                a.hours = Num(a.hours, 0, 1e5, 0); a.condition = Num(a.condition, 0, 1, 1); a.damage = Num(a.damage, 0, 1, 0);
                a.kit = (a.kit ?? new List<KitId>()).Distinct().Where(k => Loadout.CanFit(ac, k)).ToList();
                if (!v.bases.Contains(a.baseId)) a.baseId = "kin_h1";
                if (a.skin != null && !(ac.skins?.Any(s => s.id == a.skin) ?? false)) a.skin = null;
            }
            v.contracts = (v.contracts ?? new List<ContractId>()).Distinct().Take(Companies.MAX_CONTRACTS).ToList();
            v.ledger = (v.ledger ?? new List<LedgerEntry>()).Take(80).ToList();
            v.market = (v.market ?? new List<MarketOffer>()).Where(m => m != null && Aircraft.Find(m.aircraftId) != null).Take(12).ToList();
            foreach (var m in v.market)
            {
                m.condition = Num(m.condition, 0.3, 1, 1); m.hours = Num(m.hours, 0, 1e5, 0);
                // never trust a stored price below the aircraft's value (a tampered save cannot buy a twin for £1)
                var floor = m.used ? Math.Round(Companies.AircraftValue(m.aircraftId, m.hours, m.condition, 0) * 0.9) : Companies.NEW_PRICE[m.aircraftId];
                m.price = Math.Max(floor, Num(m.price, 0, 1e8, floor));
            }
            v.stats = v.stats ?? new CompanyStats();
            v.reputation = Num(v.reputation, 0, 100, 40);
            v.funds = Num(v.funds, -1e9, 1e10, 0); v.loan = Num(v.loan, 0, 1e9, 0);
            return v;
        }
    }
}
