// Company mode (port of src/game/company.ts): run your own helicopter operator. Buy aircraft (new or used, cash
// or on finance), fit them out, lease bases, sign contracts and fly shifts: free roam with the tablet callouts,
// or with them off. Each shift is settled against the company: job pay (better with reputation and contracts),
// fuel, crew, insurance, base leases, loan interest, wear and damage. Kept in its own save, apart from the career.
// Prices are scaled for the game (a light single about a quarter of a million, a medium twin a few million).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Rotorcraft.Sim;

namespace Rotorcraft.Game
{
    public sealed class BaseDef { public string siteId, name; public double lease; public bool twinOnly; public string note; }
    public enum ContractId { Survey, Police, Ambulance, Offshore, Sar }
    public sealed class ContractDef { public ContractId id; public string client, title; public double retainer, minRep; public bool needsTwin; public KitId[] kit; public CalloutType[] covers; public string[] bases; public string text; }
    public enum StartPackage { Owner, Backed }

    public sealed class OwnedAircraft
    {
        public string uid, aircraftId, reg;
        public double hours;
        /// <summary>1 = just overhauled; wears with flying. Below 0.35 it is grounded until serviced.</summary>
        public double condition = 1;
        /// <summary>Unrepaired damage 0..1 (hard landings); above 0.3 it is grounded until repaired.</summary>
        public double damage;
        public List<KitId> kit = new List<KitId>();
        public string baseId = "kin_h1";
        /// <summary>Paint scheme, for types that have several.</summary>
        public string skin;
    }
    public sealed class MarketOffer { public string id, aircraftId, reg; public double hours, condition, price; public bool used; }
    public sealed class LedgerEntry { public int shift; public string label; public double amount; }
    public sealed class CompanyStats { public int jobs; public double hours, earned; public int crashes, shifts; }

    public sealed class Company
    {
        public int version = 1;
        public string name;
        public long founded;
        public double funds, loan;
        /// <summary>0..100: raises pay, opens contracts.</summary>
        public double reputation;
        public int shift;
        public List<OwnedAircraft> fleet = new List<OwnedAircraft>();
        public List<string> bases = new List<string>();
        public List<ContractId> contracts = new List<ContractId>();
        public List<LedgerEntry> ledger = new List<LedgerEntry>();
        public CompanyStats stats = new CompanyStats();
        public List<MarketOffer> market = new List<MarketOffer>();
        public int marketShift = -1;
        public int seq;
        public Company Clone() => JsonConvert.DeserializeObject<Company>(JsonConvert.SerializeObject(this));
    }

    public sealed class CompanyResult { public bool ok; public Company company; public string reason; public static CompanyResult Ok(Company c) => new CompanyResult { ok = true, company = c }; public static CompanyResult No(string r) => new CompanyResult { ok = false, reason = r }; }

    public sealed class ShiftLine { public string label; public double amount; }
    public sealed class ShiftReport
    {
        public List<ShiftLine> lines; public double net, repBefore, repAfter;
        public string reg, name; public double hours, conditionBefore, conditionAfter, damage; public bool writtenOff;
    }

    public static class Companies
    {
        public static readonly Dictionary<string, double> NEW_PRICE = new Dictionary<string, double> { ["kestrel"] = 285_000, ["northstar"] = 3_900_000, ["guardian"] = 5_200_000, ["nightjar"] = 2_600_000 };
        /// <summary>Wear per flight hour (condition 1 = fresh from overhaul).</summary>
        static readonly Dictionary<string, double> WEAR_PER_HOUR = new Dictionary<string, double> { ["kestrel"] = 0.022, ["northstar"] = 0.016, ["guardian"] = 0.016, ["nightjar"] = 0.017 };
        /// <summary>Per shift, per aircraft owned.</summary>
        public static readonly Dictionary<string, double> INSURANCE = new Dictionary<string, double> { ["kestrel"] = 650, ["northstar"] = 4_200, ["guardian"] = 5_400, ["nightjar"] = 2_900 };
        /// <summary>Crew pay for a shift flown.</summary>
        public static readonly Dictionary<string, double> CREW_PAY = new Dictionary<string, double> { ["kestrel"] = 280, ["northstar"] = 2_100, ["guardian"] = 2_600, ["nightjar"] = 1_500 };
        public static readonly Dictionary<KitId, double> KIT_PRICE = new Dictionary<KitId, double> { [KitId.Medical] = 85_000, [KitId.Hoist] = 420_000, [KitId.Flir] = 165_000, [KitId.Searchlight] = 26_000, [KitId.Floats] = 38_000, [KitId.Cargo] = 12_000, [KitId.Seats] = 30_000, [KitId.AuxFuel] = 55_000 };

        public static readonly List<BaseDef> BASES = new List<BaseDef>
        {
            new BaseDef { siteId = "kin_h1", name = "Kinloch Heliport (H1)", lease = 0, note = "Your first hangar space: included." },
            new BaseDef { siteId = "kin_h2", name = "Kinloch Heliport (H2, twin pad)", lease = 1_400, twinOnly = true, note = "Large pad and hangar bay beside the tower for the twins." },
            new BaseDef { siteId = "inverhallan", name = "Inverhallan Infirmary", lease = 2_300, twinOnly = true, note = "On-site air ambulance base at the regional hospital." },
            new BaseDef { siteId = "ardoch_mrt", name = "Ardoch Mountain Rescue base", lease = 700, note = "Up the glen, close to the hills and the climbing crags." },
        };

        public static readonly List<ContractDef> CONTRACTS = new List<ContractDef>
        {
            new ContractDef { id = ContractId.Survey, client = "Grampian Grid (fictional)", title = "Line inspection and aerial survey", retainer = 700, minRep = 0, needsTwin = false, kit = new KitId[0], covers = new[] { CalloutType.LineCheck, CalloutType.Photo }, text = "Power-line patrols and photo flights. Any aircraft will do." },
            new ContractDef { id = ContractId.Police, client = "North East Police (fictional)", title = "Police air support", retainer = 3_200, minRep = 40, needsTwin = false, kit = new[] { KitId.Flir, KitId.Searchlight }, covers = new[] { CalloutType.Police, CalloutType.Missing }, text = "Searches and pursuits, day and night. Thermal camera and searchlight required." },
            new ContractDef { id = ContractId.Ambulance, client = "Highland Air Ambulance Trust (fictional)", title = "Air ambulance", retainer = 6_000, minRep = 45, needsTwin = true, kit = new[] { KitId.Medical }, covers = new[] { CalloutType.Medical, CalloutType.Rta, CalloutType.Transfer, CalloutType.Bloods }, bases = new[] { "inverhallan", "kin_h2" }, text = "Scene response and hospital transfers from a hospital or Kinloch base. Twin with the medical fit." },
            new ContractDef { id = ContractId.Offshore, client = "Firth Energy (fictional)", title = "Offshore crew change", retainer = 8_500, minRep = 50, needsTwin = true, kit = new[] { KitId.Floats, KitId.Seats }, covers = new[] { CalloutType.Offshore, CalloutType.CrewChange, CalloutType.Cargo }, text = "Crew changes and freight to the platforms. Twin with floats and the passenger cabin." },
            new ContractDef { id = ContractId.Sar, client = "East Coast SAR (fictional)", title = "Search and rescue", retainer = 11_500, minRep = 60, needsTwin = true, kit = new[] { KitId.Hoist, KitId.Flir }, covers = new[] { CalloutType.HillWalker, CalloutType.Climber, CalloutType.Swimmer, CalloutType.Liferaft, CalloutType.Vessel, CalloutType.Missing }, text = "Coast and mountain rescue, 24 hours. Twin with hoist and thermal camera." },
        };
        public const int MAX_CONTRACTS = 2;

        public static readonly Dictionary<StartPackage, (string label, double funds, double loan, string text)> PACKAGES = new Dictionary<StartPackage, (string, double, double, string)>
        {
            [StartPackage.Owner] = ("Owner-pilot", 420_000, 0, "Your savings and a small hangar at Kinloch. Enough for a Kestrel and some running money."),
            [StartPackage.Backed] = ("Investor-backed", 2_200_000, 1_200_000, "Investors put in money and arrange a loan: more to start with, but interest to pay every shift."),
        };
        /// <summary>The tablet shows what the crew are told a job is worth; the company bills the client at full commercial rates.</summary>
        public const double COMMERCIAL_RATE = 3.5;
        /// <summary>Interest charged on the loan per shift flown.</summary>
        public const double LOAN_RATE = 0.004;

        static double R1000(double v) => Math.Round(v / 1000, MidpointRounding.AwayFromZero) * 1000;
        public static string Money(double n) => $"{(n < 0 ? "−" : "")}£{Math.Abs(Math.Round(n)).ToString("#,0", CultureInfo.GetCultureInfo("en-GB"))}";

        /// <summary>A small seeded generator so the market is the same after a reload.</summary>
        static Func<double> Rnd(long seed) { uint s = unchecked((uint)seed); if (s == 0) s = 1; return () => { s = unchecked(s * 1664525u + 1013904223u); return s / 4294967296.0; }; }

        static string NewReg(Func<double> R, HashSet<string> taken)
        {
            const string L = "ABCDEFGHJKLMNOPRSTUVWXYZ";
            for (;;)
            {
                var reg = "G-K" + new string(Enumerable.Range(0, 3).Select(_ => L[(int)Math.Floor(R() * L.Length)]).ToArray());
                if (!taken.Contains(reg)) return reg;
            }
        }

        /// <summary>Value of an aircraft today: new price less depreciation for hours and condition.</summary>
        public static double AircraftValue(string aircraftId, double hours, double condition, double damage)
        {
            var p = NEW_PRICE.TryGetValue(aircraftId, out var v) ? v : 0;
            var age = Math.Max(0.35, 1 - hours / 9000);
            return R1000(p * age * (0.7 + 0.3 * condition) * (1 - 0.6 * damage));
        }
        public static double AircraftValue(OwnedAircraft a) => AircraftValue(a.aircraftId, a.hours, a.condition, a.damage);
        /// <summary>What a dealer pays you (a margin under its value).</summary>
        public static double SalePrice(OwnedAircraft a) => R1000(AircraftValue(a) * 0.85);
        /// <summary>Cost of servicing to condition 1 and repairing all damage.</summary>
        public static double ServiceCost(OwnedAircraft a)
        {
            var p = NEW_PRICE.TryGetValue(a.aircraftId, out var v) ? v : 0;
            return Math.Round(((1 - a.condition) * p * 0.06 + a.damage * p * 0.45) / 100, MidpointRounding.AwayFromZero) * 100;
        }
        /// <summary>Why the aircraft cannot fly now, or null.</summary>
        public static string Grounded(OwnedAircraft a)
        {
            if (a.damage > 0.3) return "Damaged: repair before flight";
            if (a.condition < 0.35) return "Overdue maintenance: service before flight";
            return null;
        }
        /// <summary>Kit built into the type (no cost, cannot be removed).</summary>
        public static List<KitId> BuiltInKit(string aircraftId) => Loadout.KIT.Where(k => k.mass.TryGetValue(aircraftId, out var m) && m == 0).Select(k => k.id).ToList();
        public static double FitPrice(string aircraftId, KitId k) => BuiltInKit(aircraftId).Contains(k) ? 0 : KIT_PRICE[k];

        public static Company RefreshMarket(Company c0)
        {
            var c = c0.Clone();
            var R = Rnd(c.seq * 7919L + c.shift * 104729L + 17);
            var taken = new HashSet<string>(c.fleet.Select(a => a.reg));
            var offers = new List<MarketOffer>();
            var ids = Aircraft.All.Keys.ToList();
            foreach (var id in ids)
            {
                var reg = NewReg(R, taken); taken.Add(reg);
                offers.Add(new MarketOffer { id = $"m{c.shift}-{id}-new", aircraftId = id, reg = reg, hours = 0, condition = 1, price = NEW_PRICE[id], used = false });
            }
            for (int i = 0; i < 4; i++)
            {
                var id = ids[(int)Math.Floor(R() * ids.Count)];
                var hours = Math.Round(400 + R() * 5200); var condition = Math.Round((0.45 + R() * 0.5) * 100) / 100;
                var reg = NewReg(R, taken); taken.Add(reg);
                var value = AircraftValue(id, hours, condition, 0);
                offers.Add(new MarketOffer { id = $"m{c.shift}-{i}", aircraftId = id, reg = reg, hours = hours, condition = condition, price = R1000(value * (0.92 + R() * 0.12)), used = true });
            }
            c.market = offers; c.marketShift = c.shift;
            return c;
        }

        public static Company CreateCompany(string name, StartPackage pkg, long now, long? seed = null)
        {
            var P = PACKAGES[pkg];
            var n = (name ?? "").Trim();
            if (n.Length > 40) n = n.Substring(0, 40);
            var c = new Company
            {
                name = n.Length > 0 ? n : "Kinloch Helicopters", founded = now, funds = P.funds, loan = P.loan, reputation = 40, shift = 0,
                bases = new List<string> { "kin_h1" }, ledger = new List<LedgerEntry> { new LedgerEntry { shift = 0, label = $"{P.label} start", amount = P.funds } },
                seq = (int)(Math.Abs(seed ?? now) % 100000),
            };
            return RefreshMarket(c);
        }

        static Company Entry(Company c, string label, double amount)
        {
            c.funds += amount;
            c.ledger.Insert(0, new LedgerEntry { shift = c.shift, label = label, amount = amount });
            if (c.ledger.Count > 80) c.ledger.RemoveRange(80, c.ledger.Count - 80);
            return c;
        }

        public static CompanyResult Buy(Company c0, string offerId, bool finance)
        {
            var o = c0.market.FirstOrDefault(m => m.id == offerId);
            if (o == null) return CompanyResult.No("That aircraft has been sold.");
            var deposit = finance ? Math.Round(o.price * 0.3) : o.price;
            // the aircraft itself is security for 70% of its price
            if (finance && o.price - deposit > LoanLimit(c0) + o.price * 0.7 + 1) return CompanyResult.No("The bank will not lend that much against the fleet.");
            if (c0.funds < deposit) return CompanyResult.No($"Not enough funds (need {Money(deposit)}).");
            var ac = Aircraft.Get(o.aircraftId);
            var c = c0.Clone();
            var home = ac.engines == 2 ? (c.bases.FirstOrDefault(b => b != "kin_h1") ?? "kin_h1") : "kin_h1";
            c.seq += 1;
            c.fleet.Add(new OwnedAircraft { uid = $"a{c.seq}", aircraftId = o.aircraftId, reg = o.reg, hours = o.hours, condition = o.condition, damage = 0, kit = BuiltInKit(o.aircraftId), baseId = home });
            c.market.RemoveAll(m => m.id == offerId);
            Entry(c, $"Bought {ac.name} {o.reg}{(finance ? " (30% deposit, rest financed)" : "")}", -deposit);
            if (finance) c.loan += o.price - deposit;
            return CompanyResult.Ok(c);
        }

        public static CompanyResult Sell(Company c0, string uid)
        {
            var a = c0.fleet.FirstOrDefault(x => x.uid == uid);
            if (a == null) return CompanyResult.No("Not in the fleet.");
            var c = c0.Clone();
            c.fleet.RemoveAll(x => x.uid == uid);
            return CompanyResult.Ok(Entry(c, $"Sold {Aircraft.Get(a.aircraftId).name} {a.reg}", SalePrice(a)));
        }

        public static CompanyResult Service(Company c0, string uid)
        {
            var a = c0.fleet.FirstOrDefault(x => x.uid == uid);
            if (a == null) return CompanyResult.No("Not in the fleet.");
            var cost = ServiceCost(a);
            if (cost <= 0) return CompanyResult.No("Nothing to do: the aircraft is in good order.");
            if (c0.funds < cost) return CompanyResult.No($"Not enough funds (need {Money(cost)}).");
            var c = c0.Clone();
            var b = c.fleet.First(x => x.uid == uid); b.condition = 1; b.damage = 0;
            return CompanyResult.Ok(Entry(c, $"Service and repairs: {a.reg}", -cost));
        }

        public static CompanyResult FitKit(Company c0, string uid, KitId k, bool on)
        {
            var a = c0.fleet.FirstOrDefault(x => x.uid == uid);
            if (a == null) return CompanyResult.No("Not in the fleet.");
            if (!Loadout.CanFit(Aircraft.Get(a.aircraftId), k)) return CompanyResult.No("That kit does not fit this type.");
            if (BuiltInKit(a.aircraftId).Contains(k)) return CompanyResult.No("Built in to this type.");
            if (on == a.kit.Contains(k)) return CompanyResult.Ok(c0);
            var c = c0.Clone();
            var b = c.fleet.First(x => x.uid == uid);
            if (on)
            {
                var p = KIT_PRICE[k];
                if (c.funds < p) return CompanyResult.No($"Not enough funds (need {Money(p)}).");
                b.kit.Add(k);
                return CompanyResult.Ok(Entry(c, $"Fitted {Loadout.ById(k).@short} to {a.reg}", -p));
            }
            // removed kit is sold back at half price
            b.kit.Remove(k);
            return CompanyResult.Ok(Entry(c, $"Removed {Loadout.ById(k).@short} from {a.reg}", Math.Round(KIT_PRICE[k] * 0.5)));
        }

        /// <summary>Repaint an aircraft (free: the paint shop is part of the service).</summary>
        public static CompanyResult SetSkin(Company c0, string uid, string skin)
        {
            var a = c0.fleet.FirstOrDefault(x => x.uid == uid);
            if (a == null || !(Aircraft.Get(a.aircraftId).skins?.Any(s => s.id == skin) ?? false)) return CompanyResult.No("No such paint scheme.");
            var c = c0.Clone(); c.fleet.First(x => x.uid == uid).skin = skin;
            return CompanyResult.Ok(c);
        }

        public static CompanyResult SetBase(Company c0, string uid, string siteId)
        {
            var a = c0.fleet.FirstOrDefault(x => x.uid == uid);
            if (a == null || !c0.bases.Contains(siteId)) return CompanyResult.No("Lease that base first.");
            var b = BASES.First(x => x.siteId == siteId);
            if (b.twinOnly && Aircraft.Get(a.aircraftId).engines < 2) return CompanyResult.No("That pad is for the twins.");
            var c = c0.Clone(); c.fleet.First(x => x.uid == uid).baseId = siteId;
            return CompanyResult.Ok(c);
        }

        public static CompanyResult LeaseBase(Company c0, string siteId, bool on)
        {
            var b = BASES.FirstOrDefault(x => x.siteId == siteId);
            if (b == null) return CompanyResult.No("No such base.");
            if (siteId == "kin_h1") return CompanyResult.No("Kinloch H1 is your home base.");
            var c = c0.Clone();
            if (on)
            {
                if (c.bases.Contains(siteId)) return CompanyResult.Ok(c0);
                var setup = b.lease * 2;
                if (c.funds < setup) return CompanyResult.No($"Not enough funds for the set-up fee ({Money(setup)}).");
                c.bases.Add(siteId);
                return CompanyResult.Ok(Entry(c, $"Leased {b.name} (set-up fee)", -setup));
            }
            // aircraft based there go home to H1
            c.bases.Remove(siteId);
            foreach (var a in c.fleet) if (a.baseId == siteId) a.baseId = "kin_h1";
            return CompanyResult.Ok(c);
        }

        /// <summary>Does an aircraft (as fitted) meet a contract?</summary>
        public static bool Meets(OwnedAircraft a, ContractDef k) =>
            (!k.needsTwin || Aircraft.Get(a.aircraftId).engines == 2) && k.kit.All(x => a.kit.Contains(x)) && (k.bases == null || k.bases.Contains(a.baseId));

        public static ContractDef Contract(ContractId id) => CONTRACTS.First(x => x.id == id);

        public static CompanyResult SignContract(Company c0, ContractId id, bool on)
        {
            var k = Contract(id);
            var c = c0.Clone();
            if (!on) { c.contracts.Remove(id); c.reputation = Math.Max(0, c.reputation - 4); return CompanyResult.Ok(c); }
            if (c.contracts.Contains(id)) return CompanyResult.Ok(c0);
            if (c.contracts.Count >= MAX_CONTRACTS) return CompanyResult.No($"At most {MAX_CONTRACTS} contracts at a time.");
            if (c.reputation < k.minRep) return CompanyResult.No($"Needs a reputation of {k.minRep:0} (you have {Math.Round(c.reputation)}).");
            if (!c.fleet.Any(a => Meets(a, k))) return CompanyResult.No("No aircraft in the fleet meets the contract yet.");
            c.contracts.Add(id);
            return CompanyResult.Ok(c);
        }

        /// <summary>How much more the bank will lend: 50% of the fleet's value plus £150k, less what is owed.</summary>
        public static double LoanLimit(Company c) => Math.Max(0, R1000(c.fleet.Sum(AircraftValue) * 0.5 + 150_000 - c.loan));

        public static CompanyResult Borrow(Company c0, double amount)
        {
            amount = Math.Round(amount);
            if (amount <= 0) return CompanyResult.No("Enter an amount.");
            if (amount > LoanLimit(c0)) return CompanyResult.No($"The bank will lend up to {Money(LoanLimit(c0))} more.");
            var c = c0.Clone(); c.loan += amount;
            return CompanyResult.Ok(Entry(c, "Loan drawn", amount));
        }

        public static CompanyResult Repay(Company c0, double amount)
        {
            amount = Math.Min(Math.Round(amount), c0.loan);
            if (amount <= 0) return CompanyResult.No("Nothing to repay.");
            if (c0.funds < amount) return CompanyResult.No("Not enough funds.");
            var c = c0.Clone(); c.loan -= amount;
            return CompanyResult.Ok(Entry(c, "Loan repaid", -amount));
        }

        /// <summary>The freeplay set-up for a company shift with one of its aircraft.</summary>
        public static FreeplayConfig ShiftConfig(Company c, string uid, TimeOfDay tod, string weatherId, FreeplayPace pace, double fuelKg, double seed)
        {
            var a = c.fleet.First(x => x.uid == uid);
            var favour = c.contracts.Select(Contract).Where(k => Meets(a, k)).SelectMany(k => k.covers).ToList();
            return new FreeplayConfig { aircraftId = a.aircraftId, kit = a.kit.ToList(), baseSiteId = a.baseId, tod = tod, weatherId = weatherId, pace = pace, fuelKg = fuelKg, seed = seed, favour = favour, skin = a.skin, companyUid = uid };
        }

        /// <summary>Pay multiplier from reputation: 0.85 at 0, 1 at 40, 1.25 at 100.</summary>
        public static double RepMultiplier(double rep) => rep < 40 ? 0.85 + 0.15 * rep / 40 : 1 + 0.25 * (rep - 40) / 60;

        /// <summary>Settle a flown shift against the company.</summary>
        public static (Company company, ShiftReport report) SettleShift(Company c0, string uid, FreeplaySummary sum, FlightOutcome outcome, double damage, double flightSeconds)
        {
            var a = c0.fleet.FirstOrDefault(x => x.uid == uid) ?? throw new ArgumentException("Aircraft not in the fleet");
            var ac = Aircraft.Get(a.aircraftId);
            var lines = new List<ShiftLine>();
            var mul = RepMultiplier(c0.reputation);
            // job pay: contract jobs pay 30% more when flown with an aircraft that meets the contract
            var covered = new HashSet<CalloutType>(c0.contracts.Select(Contract).Where(k => Meets(a, k)).SelectMany(k => k.covers));
            double jobPay = 0, contractExtra = 0;
            foreach (var j in sum.jobs) { var p = Math.Round(j.pay * COMMERCIAL_RATE); jobPay += p; if (covered.Contains(j.type) && p > 0) contractExtra += Math.Round(p * 0.3); }
            if (jobPay != 0) lines.Add(new ShiftLine { label = $"Job fees at commercial rates ({sum.jobs.Count(j => j.outcome == JobOutcome.Completed)} completed)", amount = jobPay });
            var repBonus = Math.Round(jobPay * (mul - 1));
            if (repBonus != 0) lines.Add(new ShiftLine { label = $"Reputation {(repBonus > 0 ? "premium" : "discount")} (×{mul:F2})", amount = repBonus });
            if (contractExtra != 0) lines.Add(new ShiftLine { label = "Contract rates", amount = contractExtra });
            foreach (var id in c0.contracts) { var k = Contract(id); if (Meets(a, k)) lines.Add(new ShiftLine { label = $"Retainer: {k.title}", amount = k.retainer }); }
            if (sum.fuelCost != 0) lines.Add(new ShiftLine { label = "Fuel", amount = -sum.fuelCost });
            if (flightSeconds > 60) lines.Add(new ShiftLine { label = $"Crew ({(ac.engines == 2 ? "pilots and crew" : "pilot")})", amount = -CREW_PAY[a.aircraftId] });
            foreach (var f in c0.fleet) lines.Add(new ShiftLine { label = $"Insurance: {f.reg}", amount = -INSURANCE[f.aircraftId] });
            foreach (var b in c0.bases) { var d = BASES.FirstOrDefault(x => x.siteId == b); if (d != null && d.lease > 0) lines.Add(new ShiftLine { label = $"Lease: {d.name}", amount = -d.lease }); }
            if (c0.loan > 0) lines.Add(new ShiftLine { label = "Loan interest", amount = -Math.Round(c0.loan * LOAN_RATE) });
            // the aircraft: wear with hours, damage from the flight; a crash writes it off (insurance pays 70%)
            var hours = flightSeconds / 3600;
            var crashed = outcome == FlightOutcome.Crashed;
            var condAfter = Math.Max(0, a.condition - hours * (WEAR_PER_HOUR.TryGetValue(a.aircraftId, out var w) ? w : 0.02));
            var dmgAfter = Math.Min(1, a.damage + Math.Max(0, damage));
            var c = c0.Clone();
            var mine = c.fleet.First(x => x.uid == uid);
            mine.hours = Math.Round((mine.hours + hours) * 10) / 10; mine.condition = Math.Round(condAfter * 1000) / 1000; mine.damage = Math.Round(dmgAfter * 1000) / 1000;
            if (crashed)
            {
                var payout = R1000(AircraftValue(a.aircraftId, a.hours, a.condition, 0) * 0.7);
                lines.Add(new ShiftLine { label = $"Insurance payout: {a.reg} written off", amount = payout });
                c.fleet.Remove(mine);
            }
            // reputation: good, punctual work builds it; stand-downs, failures, damage and crashes cost it
            var rep = c0.reputation;
            foreach (var j in sum.jobs) rep += j.outcome == JobOutcome.Completed ? (j.quality - 65) / 18 - j.lateMin * 0.3 : j.outcome == JobOutcome.Abandoned ? -3 : -5;
            rep -= Math.Max(0, damage) * 20;
            if (crashed) rep -= 18;
            rep = Math.Max(0, Math.Min(100, rep));
            var net = lines.Sum(l => l.amount);
            c.shift = c0.shift + 1;
            c.reputation = Math.Round(rep * 10) / 10;
            c.stats = new CompanyStats { jobs = c0.stats.jobs + sum.jobs.Count(j => j.outcome == JobOutcome.Completed), hours = Math.Round((c0.stats.hours + hours) * 10) / 10, earned = c0.stats.earned + net, crashes = c0.stats.crashes + (crashed ? 1 : 0), shifts = c0.stats.shifts + 1 };
            c.funds += net;
            var newEntries = lines.Select(l => new LedgerEntry { shift = c.shift, label = l.label, amount = l.amount }).Reverse().ToList();
            c.ledger = newEntries.Concat(c.ledger).Take(80).ToList();
            // contracts lapse if no aircraft can meet them any more
            c.contracts = c.contracts.Where(id => c.fleet.Any(f => Meets(f, Contract(id)))).ToList();
            if (c.shift - c.marketShift >= 4) c = RefreshMarket(c);
            return (c, new ShiftReport { lines = lines, net = net, repBefore = c0.reputation, repAfter = c.reputation, reg = a.reg, name = ac.name, hours = hours, conditionBefore = a.condition, conditionAfter = condAfter, damage = dmgAfter, writtenOff = crashed });
        }
    }
}
