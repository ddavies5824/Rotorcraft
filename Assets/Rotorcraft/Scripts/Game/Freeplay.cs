// Freeplay (port of src/game/freeplay.ts): an open-ended shift. Random callouts arrive on the crew tablet; the
// pilot accepts the ones the aircraft and its loadout can do, flies them, and is paid on the result. Each
// callout is turned into ordinary mission items (land, load, search, winch...) so it is judged by the same rules
// as the career missions: nothing on the tablet can complete a job by itself.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Rotorcraft.Sim;
using Rotorcraft.World;
using static Rotorcraft.World.WorldMap;

namespace Rotorcraft.Game
{
    public enum CalloutType { Rta, Medical, HillWalker, Climber, Swimmer, Liferaft, Vessel, Missing, Transfer, Offshore, Cargo, CrewChange, Police, Photo, LineCheck, Bloods }
    public enum PropRole { None, Patient, Cargo, Pax }
    public enum FreeplayPace { Off, Quiet, Normal, Busy }

    /// <summary>Scenery for a callout. model: "v:kind:paint" vehicle, "P:look:pose:seed" person, "s:n:seed"
    /// stretcher, "m:name" misc prop.</summary>
    public sealed class PropDef { public string model; public double x, z, rot, tilt; public bool beacons, onWater; public PropRole role; }

    public sealed class JobBuild { public List<ObjectiveDef> objectives = new List<ObjectiveDef>(); public List<TargetDef> targets = new List<TargetDef>(); public List<SearchAreaDef> areas = new List<SearchAreaDef>(); }

    public sealed class Callout
    {
        public string id;
        public CalloutType type;
        public string title, from;
        public int priority;   // 1..3
        public string place;
        public double x, z;
        public string[] details;
        public KitId[] requires, recommended;
        public bool needsTwin;
        public double pay, createdAt, expiresAt, respondMin;
        public string destination;
        /// <summary>Built when accepted (deadlines are relative to the acceptance time).</summary>
        public Func<string, double, JobBuild> build;
        public List<PropDef> props = new List<PropDef>();
    }

    public enum JobOutcome { Completed, Abandoned, Failed }
    public sealed class JobRecord { public string id, title; public CalloutType type; public double pay, quality, lateMin, minutes; public JobOutcome outcome; public List<string> notes; }

    public sealed class FreeplayConfig
    {
        public string aircraftId = "kestrel";
        public List<KitId> kit = new List<KitId>();
        public string baseSiteId = "kin_h1";
        public double fuelKg;
        public TimeOfDay tod = TimeOfDay.Day;
        public string weatherId = "calm";
        /// <summary>How often callouts come in; Off is free roam with no callouts.</summary>
        public FreeplayPace pace = FreeplayPace.Normal;
        public double seed = 1;
        /// <summary>Company shifts: callout types the company has contracts for come up more often.</summary>
        public List<CalloutType> favour;
        /// <summary>Paint scheme (for aircraft that have several).</summary>
        public string skin;
        /// <summary>Company shifts: the company aircraft flown.</summary>
        public string companyUid;
        public FreeplayConfig Clone() { var c = (FreeplayConfig)MemberwiseClone(); c.kit = new List<KitId>(kit); c.favour = favour?.ToList(); return c; }
    }

    public sealed class FreeplaySummary { public List<JobRecord> jobs; public double earned, fuelCost, flightSeconds; public string aircraftId; }

    public sealed class ActiveJob { public Callout callout; public string job; public double acceptedAt; public double? arrivedAt; public int hardAtStart; public List<PlacedTarget> targets; }

    public static class FreeplayData
    {
        public static readonly Dictionary<FreeplayPace, (double lo, double hi)> PACE = new Dictionary<FreeplayPace, (double, double)>
        {
            [FreeplayPace.Off] = (double.PositiveInfinity, double.PositiveInfinity), [FreeplayPace.Quiet] = (150, 300), [FreeplayPace.Normal] = (90, 200), [FreeplayPace.Busy] = (45, 110),
        };

        static IEnumerable<Site> Hospitals() => SITES.Where(s => s.kind == SiteKind.Hospital);
        static readonly HashSet<string> MAJOR = new HashSet<string> { "inverhallan", "kinloch_royal" };
        static IEnumerable<Site> Platforms() => SITES.Where(s => s.kind == SiteKind.Helideck);
        static string Nm(double m) => Js.Fixed(m / 1852, 1);
        public static string Money(double v) => "£" + Math.Round(v).ToString("#,0", CultureInfo.GetCultureInfo("en-GB"));

        static Site Nearest(IEnumerable<Site> sites, double x, double z) => sites.Aggregate((a, b) => Js.Hypot(b.x - x, b.z - z) < Js.Hypot(a.x - x, a.z - z) ? b : a);

        /// <summary>A flat, open landing spot near (x, z): grass or hard standing, gentle slope, clear of buildings.</summary>
        public static (double x, double z)? FindLandingSpot(double x, double z, Func<double> R, double minD = 25, double maxD = 160)
        {
            for (int i = 0; i < 80; i++)
            {
                double a = R() * Math.PI * 2, d = minD + R() * (maxD - minD) * (0.3 + i / 110.0);
                double px = x + Math.Cos(a) * d, pz = z + Math.Sin(a) * d;
                var g = GroundAt(px, pz);
                if (g.surface != Surface.Grass && g.surface != Surface.Pad) continue;
                if (g.slope > 5 * Math.PI / 180 || g.h < 2) continue;
                if (STRUCTURES.Any(s => Js.Hypot(s.x - px, s.z - pz) < Math.Max(s.w, s.d) / 2 + 22)) continue;
                // the whole disc should be landable
                var ok = true;
                for (int k = 0; k < 6 && ok; k++)
                {
                    var bb = k / 6.0 * Math.PI * 2;
                    var q = GroundAt(px + Math.Cos(bb) * 10, pz + Math.Sin(bb) * 10);
                    if (q.surface == Surface.Water || q.surface == Surface.Roof || Math.Abs(q.h - g.h) > 1.6) ok = false;
                }
                if (ok) return (Js.Round(px), Js.Round(pz));
            }
            return null;
        }

        static readonly string[] MAIN_ROADS = { "Coast road", "Kinloch road", "Glen road", "Inverhallan road", "Ski road", "Castle road" };

        static (double x, double z, double rot, string road) RoadPoint(Func<double> R)
        {
            var roads = Places.ROADS.Where(r => MAIN_ROADS.Contains(r.name)).ToArray();
            var road = roads[(int)Math.Floor(R() * roads.Length)];
            var i = (int)Math.Floor(R() * (road.pts.Length - 1));
            var a = road.pts[i]; var b = road.pts[i + 1];
            var f = R();
            return (a.x + (b.x - a.x) * f, a.z + (b.z - a.z) * f, Math.Atan2(b.x - a.x, b.z - a.z), road.name);
        }

        static readonly string[] DIRS = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };

        public static string PlaceName(double x, double z)
        {
            var named = SITES.Where(s => s.kind != SiteKind.Helideck).Select(s => (s.name, s.x, s.z)).Concat(Places.POI.Select(p => (p.name, p.x, p.z)));
            var n = named.Aggregate((a, b) => Js.Hypot(b.x - x, b.z - z) < Js.Hypot(a.x - x, a.z - z) ? b : a);
            var (brg, dist) = BearingDistance(n.x, n.z, x, z);
            if (dist < 350) return $"at {n.name}";
            return $"{Nm(dist)} nm {DIRS[(int)Js.Round(brg / 45) % 8]} of {n.name}";
        }

        static readonly string[] CAS_NAMES = { "Ewan", "Morag", "Callum", "Isla", "Fraser", "Kirsty", "Ruaridh", "Eilidh", "Hamish", "Catriona", "Alasdair", "Fiona", "Iain", "Shona", "Lachlan", "Mhairi" };

        public sealed class Ctx { public Func<double> R; public double t; public AircraftConfig ac; public List<KitId> kit; public TimeOfDay tod; public Site @base; public string id; public int seq; }

        public sealed class Template { public CalloutType type; public double weight; public KitId[] requires, recommended; public bool needsTwin, day; public Func<Ctx, Callout> make; }

        static double Cruise(AircraftConfig ac) => ac.engines == 2 ? 64 : 44;

        /// <summary>Minutes the job allows to reach the scene: transit at cruise plus start-up and a margin.</summary>
        public static double RespondFor(Ctx c, double x, double z) => Math.Ceiling(Js.Hypot(x - c.@base.x, z - c.@base.z) / Cruise(c.ac) / 60 + 6);

        static double KitMass(Ctx c) => c.kit.Sum(k => Loadout.ById(k).mass.TryGetValue(c.ac.id, out var m) ? m : 0);

        static ObjectiveDef O(ObjectiveKind k, string label, string phase, string job) => new ObjectiveDef { kind = k, label = label, phase = phase, job = job };

        static List<ObjectiveDef> MedevacItems(string job, double tMin, double respond, (double x, double z, string name) lz, Site hosp, double kg, double sceneS)
        {
            var land = O(ObjectiveKind.Land, $"Land at the scene: {lz.name}", "Callout", job); land.x = lz.x; land.z = lz.z; land.name = lz.name; land.maxDistM = 45; land.maxSlopeDeg = 8; land.deadlineMin = tMin + respond;
            var load = O(ObjectiveKind.Load, "Paramedic treats and loads the patient", "On scene", job); load.what = "patient"; load.payloadKg = kg; load.seconds = sceneS; load.pickup = true; load.x = lz.x; load.z = lz.z; load.name = lz.name; load.radiusM = 60;
            var fly = O(ObjectiveKind.Land, $"Fly the patient to {hosp.name}", "Transfer", job); fly.siteId = hosp.id; fly.maxDistM = Math.Max(12, hosp.radius * 1.5); fly.deliver = true;
            var hand = O(ObjectiveKind.Load, "Hand over to the hospital team", "Handover", job); hand.what = "patient"; hand.payloadKg = kg; hand.seconds = 20; hand.siteId = hosp.id;
            return new List<ObjectiveDef> { land, load, fly, hand };
        }

        static List<ObjectiveDef> WinchItems(string job, double tMin, double respond, string target, string what, Site hosp, (string area, string name)? search = null)
        {
            var o = new List<ObjectiveDef>();
            if (search is (string area, string name))
            {
                var s = O(ObjectiveKind.Search, $"Search {name}", "Search", job); s.areaId = area; s.targetId = target; s.hintAfterMin = 7; s.deadlineMin = tMin + respond + 4; o.Add(s);
            }
            var w = O(ObjectiveKind.Winch, $"Winch the {what} aboard", "Rescue", job); w.targetId = target; w.deadlineMin = search == null ? tMin + respond + 3 : (double?)null; o.Add(w);
            var fly = O(ObjectiveKind.Land, $"Fly the casualty to {hosp.name}", "Transfer", job); fly.siteId = hosp.id; fly.maxDistM = Math.Max(12, hosp.radius * 1.5); fly.deliver = true; o.Add(fly);
            var hand = O(ObjectiveKind.Load, "Hand over to the hospital team", "Handover", job); hand.what = "casualty"; hand.payloadKg = 0; hand.seconds = 15; hand.siteId = hosp.id; o.Add(hand);
            return o;
        }

        static ObjectiveDef LandAt(string label, string phase, Site s, double maxDist, string job, double? deadline = null)
        { var o = O(ObjectiveKind.Land, label, phase, job); o.siteId = s.id; o.maxDistM = maxDist; o.deadlineMin = deadline; return o; }
        static ObjectiveDef LoadAt(string label, string phase, string what, double kg, double seconds, Site s, string job, bool pickup = false)
        { var o = O(ObjectiveKind.Load, label, phase, job); o.what = what; o.payloadKg = kg; o.seconds = seconds; o.siteId = s?.id; o.pickup = pickup; return o; }
        static ObjectiveDef Radio(string label, string phase, string call, string reply, string from, string job)
        { var o = O(ObjectiveKind.Radio, label, phase, job); o.call = call; o.reply = reply; o.from = from; return o; }

        static PropDef Prop(string model, double x, double z, double rot, PropRole role = PropRole.None, bool beacons = false, double tilt = 0) => new PropDef { model = model, x = x, z = z, rot = rot, role = role, beacons = beacons, tilt = tilt };
        static int Pick(Func<double> R, int n) => (int)Math.Floor(R() * n);

        static readonly Dictionary<PoiKind, (string title, string text)> MEDICAL_WHAT = new Dictionary<PoiKind, (string, string)>
        {
            [PoiKind.Golf] = ("Cardiac arrest on the course", "A golfer has collapsed on the 4th; CPR in progress with a defibrillator from the clubhouse."),
            [PoiKind.Camp] = ("Serious burns at the caravan park", "A gas stove flared in an awning. Adult with burns to the arms and face."),
            [PoiKind.Ski] = ("Skier with a spinal injury", "Fall on the upper slope, now at the lodge. Immobilised, needs a smooth ride."),
            [PoiKind.Quarry] = ("Crush injury at the quarry", "A worker caught between a dump truck and the face. Conscious, leg injury, losing blood."),
            [PoiKind.Castle] = ("Fall from the castle wall", "A visitor fell about four metres from the curtain wall. Head injury."),
            [PoiKind.Distillery] = ("Scald injury at the distillery", "A worker scalded by a split steam line. Burns to the legs."),
            [PoiKind.Harbour] = ("Fisherman with a crushed hand", "Caught in a winch on the quay. Painful, needs the hand surgeons."),
            [PoiKind.School] = ("Anaphylaxis at the playing field", "A spectator stung by a wasp, severe allergic reaction, adrenaline given twice."),
            [PoiKind.Farm] = ("Farmer trampled by cattle", "Chest injuries, breathing difficulty. The field is on the farm road."),
            [PoiKind.Beach] = ("Drowning casualty recovered", "Pulled from the surf by lifeguards. Breathing, but needs hospital fast."),
        };

        public static readonly List<Template> TEMPLATES = new List<Template>
        {
            // ---- road traffic collision: land beside the road, load, fly to a major hospital
            new Template { type = CalloutType.Rta, weight = 3, requires = new[] { KitId.Medical }, recommended = new[] { KitId.Searchlight }, needsTwin = true, make = c =>
            {
                var rp = RoadPoint(c.R);
                var lz = FindLandingSpot(rp.x, rp.z, c.R, 35, 170);
                if (lz == null) return null;
                var hosp = Nearest(Hospitals().Where(h => MAJOR.Contains(h.id)), rp.x, rp.z);
                var name = CAS_NAMES[Pick(c.R, CAS_NAMES.Length)];
                var place = PlaceName(rp.x, rp.z);
                double cr = Math.Cos(rp.rot), sr = Math.Sin(rp.rot);
                (double, double) Along(double d, double side = 0) => (rp.x + sr * d + cr * side, rp.z + cr * d - sr * side);
                var (cx, cz) = Along(0, 2); var (ox, oz) = Along(7, -1.5); var (px, pz) = Along(-16, 0); var (ax, az) = Along(4, 6); var (bx, bz) = Along(-4, 6);
                var l = lz.Value;
                return new Callout
                {
                    title = "Road traffic collision", from = "Ambulance control", priority = 1, place = $"{rp.road}, {place}", x = rp.x, z = rp.z, destination = hosp.name,
                    details = new[] { $"Two-car collision on the {rp.road}. Driver ({name}, 40s) trapped, now freed by the fire service: suspected pelvic injury, needs the trauma unit at {hosp.name}.", "Police have closed the road. A landing site has been chosen in the field beside it: watch for wires and onlookers." },
                    pay = 2600,
                    props = new List<PropDef>
                    {
                        Prop($"v:hatch:{Pick(c.R, 13)}", cx, cz, rp.rot + 0.6, tilt: 0.05), Prop($"v:saloon:{Pick(c.R, 13)}", ox, oz, rp.rot + Math.PI - 0.4),
                        Prop("v:police:0", px, pz, rp.rot, beacons: true), Prop("v:fireEngine:0", ax, az, rp.rot + 0.1, beacons: true),
                        Prop("P:casual:lie:301", bx, bz, rp.rot + 1.2, PropRole.Patient), Prop("P:medic:kneel:302", bx - 0.9, bz + 0.3, rp.rot + 2.8),
                        Prop("P:fire:stand:303", ax - 3, az + 2, rp.rot + 2), Prop("P:police:wave:304", (l.x + bx) / 2, (l.z + bz) / 2, Math.Atan2(l.x - bx, l.z - bz)),
                        Prop("P:casual:phone:305", px - 4, pz + 3, rp.rot),
                    },
                    build = (job, tMin) => new JobBuild { objectives = MedevacItems(job, tMin, RespondFor(c, rp.x, rp.z), (l.x, l.z, "field by the collision"), hosp, 85, 75) },
                };
            } },
            // ---- medical emergency at a named place
            new Template { type = CalloutType.Medical, weight = 3, requires = new[] { KitId.Medical }, recommended = new KitId[0], needsTwin = true, make = c =>
            {
                var kinds = new[] { PoiKind.Golf, PoiKind.Camp, PoiKind.Ski, PoiKind.Quarry, PoiKind.Castle, PoiKind.Distillery, PoiKind.Harbour, PoiKind.School, PoiKind.Farm, PoiKind.Beach };
                var pois = Places.POI.Where(p => kinds.Contains(p.kind)).ToArray();
                var p = pois[Pick(c.R, pois.Length)];
                var lz = FindLandingSpot(p.x, p.z, c.R, 30, 220);
                if (lz == null) return null;
                var l = lz.Value;
                var what = MEDICAL_WHAT.TryGetValue(p.kind, out var w) ? w : ("Medical emergency", "Patient needs hospital.");
                var hosp = Nearest(c.R() < 0.5 ? Hospitals() : Hospitals().Where(h => MAJOR.Contains(h.id)), p.x, p.z);
                double mx = (l.x + p.x) / 2, mz = (l.z + p.z) / 2;
                var look = p.kind == PoiKind.Golf ? "golfer" : p.kind == PoiKind.Quarry ? "worker" : p.kind == PoiKind.Harbour ? "fisher" : "casual";
                return new Callout
                {
                    title = what.Item1, from = "Ambulance control", priority = p.kind == PoiKind.Golf || p.kind == PoiKind.School ? 1 : 2, place = p.name, x = p.x, z = p.z, destination = hosp.name,
                    details = new[] { what.Item2, $"Land as close as you safely can; the crew will meet you. Take the patient to {hosp.name}." }, pay = 2200,
                    props = new List<PropDef>
                    {
                        Prop("P:casual:lie:401", mx, mz, c.R() * 6, PropRole.Patient), Prop("P:medic:kneel:402", mx + 0.9, mz, -1.6),
                        Prop($"P:{look}:stand:403", mx - 2, mz + 1.5, 2.2), Prop("P:casual:wave:404", l.x + 25, l.z + 5, Math.Atan2(-25, -5)),
                        Prop("v:rover:0", mx + 10, mz - 6, 0.8, beacons: true),
                    },
                    build = (job, tMin) => new JobBuild { objectives = MedevacItems(job, tMin, RespondFor(c, p.x, p.z), (l.x, l.z, $"landing site at {p.name}"), hosp, 80, 60) },
                };
            } },
            // ---- injured hill walker on landable ground
            new Template { type = CalloutType.HillWalker, weight = 2, requires = new[] { KitId.Medical }, recommended = new[] { KitId.Searchlight }, needsTwin = true, make = c =>
            {
                for (int k = 0; k < 30; k++)
                {
                    double x = -7600 + c.R() * 4300, z = -3500 + c.R() * 5500;
                    var g = GroundAt(x, z);
                    if (g.h < 160 || g.slope > 0.09 || g.surface != Surface.Grass) continue;
                    var lz = FindLandingSpot(x, z, c.R, 20, 90);
                    if (lz == null) continue;
                    var l = lz.Value;
                    var hosp = Nearest(Hospitals(), x, z);
                    var name = CAS_NAMES[Pick(c.R, CAS_NAMES.Length)];
                    return new Callout
                    {
                        title = "Injured hill walker", from = "Police Scotland", priority = 2, place = PlaceName(x, z), x = x, z = z, destination = hosp.name,
                        details = new[] { $"{name} has a suspected broken ankle and cannot walk off the hill. Their partner is with them and will wave you in. {(g.h > 400 ? "Expect strong winds on the tops." : "")}", $"Grid reference from a phone app: accurate to about 50 m. Land nearby, load the patient, and take them to {hosp.name}." },
                        pay = 2400,
                        props = new List<PropDef> { Prop("P:walker:lie:501", x, z, c.R() * 6, PropRole.Patient), Prop("P:walker:wave:502", x + 3, z + 2, Math.Atan2(l.x - x, l.z - z)) },
                        build = (job, tMin) => new JobBuild { objectives = MedevacItems(job, tMin, RespondFor(c, x, z), (l.x, l.z, "walkers on the hill"), hosp, 80, 50) },
                    };
                }
                return null;
            } },
            // ---- climber on a crag: winch only
            new Template { type = CalloutType.Climber, weight = 2, requires = new[] { KitId.Hoist }, recommended = new[] { KitId.Medical, KitId.Searchlight }, needsTwin = true, make = c =>
            {
                for (int k = 0; k < 60; k++)
                {
                    double x = -7600 + c.R() * 4500, z = -3500 + c.R() * 5500;
                    var g = GroundAt(x, z);
                    if (g.slope < 0.42 || g.slope > 0.85 || g.h < 150) continue;
                    var hosp = Nearest(Hospitals().Where(h => MAJOR.Contains(h.id)), x, z);
                    return new Callout
                    {
                        title = "Fallen climber on a crag", from = "Coastguard", priority = 1, place = PlaceName(x, z), x = x, z = z, destination = hosp.name,
                        details = new[] { "A climber has fallen and is stuck on a ledge. Too steep to land: this is a winch job.", $"Position is known from the climbing partner. Recover by winch and fly to {hosp.name}." },
                        pay = 3200,
                        build = (job, tMin) => new JobBuild
                        {
                            objectives = WinchItems(job, tMin, RespondFor(c, x, z), $"{job}_t", "climber", hosp),
                            targets = new List<TargetDef> { new TargetDef { id = $"{job}_t", name = "Fallen climber", kind = TargetKind.Climber, x = x, z = z, conspicuity = 0.9 } },
                        },
                    };
                }
                return null;
            } },
            // ---- swimmer or paddle-boarder swept offshore
            new Template { type = CalloutType.Swimmer, weight = 2, requires = new[] { KitId.Hoist }, recommended = new[] { KitId.Floats, KitId.Flir }, needsTwin = true, make = c =>
            {
                double z = -6500 + c.R() * 9000, x = Rotorcraft.World.Terrain.CoastX(z) + 250 + c.R() * 900;
                var hosp = Nearest(Hospitals(), x, z);
                var id = $"a{c.seq}";
                return new Callout
                {
                    title = "Swimmer swept out to sea", from = "Coastguard", priority = 1, place = PlaceName(x, z), x = x, z = z, destination = hosp.name,
                    details = new[] { "A paddle-boarder has lost their board in an offshore wind. Last seen 15 minutes ago; the search area allows for the drift.", $"Search, winch, and take them to {hosp.name}. Floats strongly advised over the water." },
                    pay = 3000,
                    build = (job, tMin) => new JobBuild
                    {
                        objectives = WinchItems(job, tMin, RespondFor(c, x, z), $"{job}_t", "swimmer", hosp, ($"{job}_{id}", "the drift area")),
                        targets = new List<TargetDef> { new TargetDef { id = $"{job}_t", name = "Swimmer", kind = TargetKind.Swimmer, areaId = $"{job}_{id}", onWater = true, conspicuity = 0.6 } },
                        areas = new List<SearchAreaDef> { new SearchAreaDef { id = $"{job}_{id}", name = "Drift area", cx = x, cz = z, w = 900, h = 700, rotDeg = 20 } },
                    },
                };
            } },
            // ---- liferaft further offshore
            new Template { type = CalloutType.Liferaft, weight = 1, requires = new[] { KitId.Hoist, KitId.Floats }, recommended = new[] { KitId.Flir, KitId.AuxFuel }, needsTwin = true, make = c =>
            {
                double z = -12000 + c.R() * 16000, cx0 = Rotorcraft.World.Terrain.CoastX(z), x = cx0 + 6000 + c.R() * 12000;
                var hosp = Nearest(Hospitals().Where(h => MAJOR.Contains(h.id)), cx0, z);
                var id = $"r{c.seq}";
                return new Callout
                {
                    title = "Liferaft sighted, fishing vessel lost", from = "Coastguard", priority = 1, place = $"{Nm(x - cx0)} nm offshore", x = x, z = z, destination = hosp.name,
                    details = new[] { "A small fishing boat sent a distress call and then disappeared from the plot. An EPIRB places a liferaft in the area shown.", $"Search, winch the survivor, and fly them to {hosp.name}. Check your fuel before you go: this is well offshore." },
                    pay = 4200,
                    build = (job, tMin) => new JobBuild
                    {
                        objectives = WinchItems(job, tMin, RespondFor(c, x, z), $"{job}_t", "survivor", hosp, ($"{job}_{id}", "the EPIRB area")),
                        targets = new List<TargetDef> { new TargetDef { id = $"{job}_t", name = "Liferaft", kind = TargetKind.Liferaft, areaId = $"{job}_{id}", onWater = true, conspicuity = 1 } },
                        areas = new List<SearchAreaDef> { new SearchAreaDef { id = $"{job}_{id}", name = "EPIRB area", cx = x, cz = z, w = 1600, h = 1200, rotDeg = 0 } },
                    },
                };
            } },
            // ---- crewman injured aboard a fishing vessel: winch from the deck
            new Template { type = CalloutType.Vessel, weight = 1, requires = new[] { KitId.Hoist }, recommended = new[] { KitId.Floats, KitId.Medical }, needsTwin = true, make = c =>
            {
                double z = -7000 + c.R() * 10000, cx0 = Rotorcraft.World.Terrain.CoastX(z), x = cx0 + 1500 + c.R() * 4000;
                var hosp = Nearest(Hospitals(), cx0, z);
                var off = PlaceName(cx0 - 400, z); if (off.StartsWith("at ")) off = off.Substring(3);
                return new Callout
                {
                    title = "Injured crewman on a trawler", from = "Coastguard", priority = 2, place = $"{Nm(x - cx0)} nm off {off}", x = x, z = z, destination = hosp.name,
                    details = new[] { "A deckhand has a deep cut to the leg from a trawl warp. The skipper is holding the boat head to wind for you.", $"Winch the casualty from the deck and fly them to {hosp.name}." },
                    pay = 3600,
                    build = (job, tMin) => new JobBuild
                    {
                        objectives = WinchItems(job, tMin, RespondFor(c, x, z), $"{job}_t", "deckhand", hosp),
                        targets = new List<TargetDef> { new TargetDef { id = $"{job}_t", name = "Fishing vessel", kind = TargetKind.Vessel, x = x, z = z, onWater = true, conspicuity = 1 } },
                    },
                };
            } },
            // ---- missing person search
            new Template { type = CalloutType.Missing, weight = 2, requires = new KitId[0], recommended = new[] { KitId.Flir, KitId.Searchlight, KitId.Hoist }, needsTwin = false, make = c =>
            {
                double x = -7000 + c.R() * 4200, z = -3000 + c.R() * 5000;
                if (GroundAt(x, z).h < 120) return null;
                var id = $"m{c.seq}"; var mrt = SiteById("ardoch_mrt");
                var winch = c.kit.Contains(KitId.Hoist) && Loadout.CanFit(c.ac, KitId.Hoist);
                var hosp = Nearest(Hospitals(), x, z);
                return new Callout
                {
                    title = "Missing hill walker", from = "Police Scotland", priority = 2, place = PlaceName(x, z), x = x, z = z, destination = winch ? hosp.name : mrt.name,
                    details = new[] { "A walker is overdue from a solo route and is not answering their phone. The mountain rescue team are searching on foot; they want you on the high ground.", winch ? $"When you find them, winch them aboard and take them to {hosp.name}." : $"When you find them, land nearby, pick them up and take them to the team at {mrt.name}." },
                    pay = winch ? 2900 : 2400,
                    build = (job, tMin) =>
                    {
                        string t = $"{job}_t", area = $"{job}_{id}";
                        var search = O(ObjectiveKind.Search, "Search the hill for the walker", "Search", job); search.areaId = area; search.targetId = t; search.hintAfterMin = 6; search.deadlineMin = tMin + RespondFor(c, x, z) + 6;
                        List<ObjectiveDef> rest;
                        if (winch) { rest = WinchItems(job, tMin, 0, t, "walker", hosp); rest[0].deadlineMin = null; }
                        else
                        {
                            var near = O(ObjectiveKind.Land, "Land near the walker", "Rescue", job); near.nearTarget = t; near.maxDistM = 160; near.maxSlopeDeg = 9;
                            var aboard = O(ObjectiveKind.Load, "Walker aboard", "Rescue", job); aboard.what = "walker"; aboard.payloadKg = 80; aboard.seconds = 35; aboard.pickup = true;
                            rest = new List<ObjectiveDef> { near, aboard, LandAt($"Fly them to {mrt.name}", "Transfer", mrt, 15, job), LoadAt("Hand over to the team", "Handover", "walker", 80, 15, mrt, job) };
                        }
                        var b = new JobBuild { targets = new List<TargetDef> { new TargetDef { id = t, name = "Missing walker", kind = TargetKind.Walker, areaId = area, conspicuity = 0.6 } }, areas = new List<SearchAreaDef> { new SearchAreaDef { id = area, name = "Search area", cx = x, cz = z, w = 1300, h = 1000, rotDeg = 30 } } };
                        b.objectives.Add(search); b.objectives.AddRange(rest);
                        return b;
                    },
                };
            } },
            // ---- inter-hospital transfer
            new Template { type = CalloutType.Transfer, weight = 2, requires = new[] { KitId.Medical }, recommended = new KitId[0], needsTwin = true, make = c =>
            {
                var hs = Hospitals().ToArray();
                var a = hs[Pick(c.R, hs.Length)];
                var others = hs.Where(h => h != a && MAJOR.Contains(h.id)).ToArray();
                if (others.Length == 0) return null;
                var b = Nearest(others, a.x, a.z);
                return new Callout
                {
                    title = "Inter-hospital transfer", from = "Ambulance control", priority = 3, place = a.name, x = a.x, z = a.z, destination = b.name,
                    details = new[] { $"A patient at {a.name} needs the specialist unit at {b.name}. Stable, but time-critical within the hour.", "The ward team will bring the patient to the pad." },
                    pay = 1800,
                    props = new List<PropDef> { Prop("s:1:601", a.x + a.radius * 1.7, a.z + 4, 1.2, PropRole.Patient), Prop("P:scrubs:stand:602", a.x + a.radius * 1.7 + 1.2, a.z + 5, -1.6), Prop("P:medic:stand:603", a.x + a.radius * 1.7 - 1, a.z + 2, -1.6) },
                    build = (job, tMin) => new JobBuild { objectives = new List<ObjectiveDef>
                    {
                        LandAt($"Land at {a.name}", "Callout", a, Math.Max(12, a.radius * 1.5), job, tMin + RespondFor(c, a.x, a.z) + 8),
                        LoadAt("Patient and nurse aboard", "Pickup", "patient and nurse", 160, 45, a, job, true),
                        Deliver(LandAt($"Fly to {b.name}", "Transfer", b, Math.Max(12, b.radius * 1.5), job)),
                        LoadAt("Hand over to the unit", "Handover", "patient and nurse", 160, 20, b, job),
                    } },
                };
            } },
            // ---- offshore medevac
            new Template { type = CalloutType.Offshore, weight = 2, requires = new[] { KitId.Medical, KitId.Floats }, recommended = new[] { KitId.AuxFuel }, needsTwin = true, make = c =>
            {
                var ps = Platforms().ToArray();
                var p = ps[Pick(c.R, ps.Length)];
                var hosp = SiteById("inverhallan");
                return new Callout
                {
                    title = $"Medevac from {p.name}", from = "Platform medic", priority = 2, place = p.name, x = p.x, z = p.z, destination = hosp.name,
                    details = new[] { $"A worker on {p.name} has severe chest pain. The platform medic has treated them; they need hospital.", $"Land on the helideck ({Nm(Js.Hypot(p.x, p.z))} nm offshore), collect the patient and fly them to {hosp.name}. Check fuel for the round trip." },
                    pay = 3800,
                    props = new List<PropDef> { Prop("s:1:701", p.x - p.radius * 0.95, p.z + 4, 0.3, PropRole.Patient), Prop("P:worker:stand:702", p.x - p.radius * 0.95 - 1.2, p.z + 5, 1.4), Prop("P:hivis:stand:703", p.x - p.radius * 0.9, p.z - 5, 1.2) },
                    build = (job, tMin) => new JobBuild { objectives = new List<ObjectiveDef>
                    {
                        LandAt($"Land on {p.name}", "Callout", p, 8, job, tMin + RespondFor(c, p.x, p.z) + 5),
                        LoadAt("Patient and medic aboard", "Pickup", "patient and medic", 170, 50, p, job, true),
                        Deliver(LandAt($"Fly to {hosp.name}", "Transfer", hosp, 18, job)),
                        LoadAt("Hand over to the hospital team", "Handover", "patient and medic", 170, 20, hosp, job),
                    } },
                };
            } },
            // ---- freight
            new Template { type = CalloutType.Cargo, weight = 2, requires = new[] { KitId.Cargo }, recommended = new KitId[0], needsTwin = false, make = c =>
            {
                var twin = c.ac.engines == 2;
                var dests = twin ? Platforms().Concat(new[] { SiteById("ardoch_mrt"), SiteById("ardoch_ski"), SiteById("portcraig_quay"), SiteById("moss_farm") }).ToArray()
                                 : new[] { SiteById("ardoch_mrt"), SiteById("ardoch_ski"), SiteById("moss_farm"), SiteById("balnagown_park") };
                var d = dests[Pick(c.R, dests.Length)];
                var from = twin ? SiteById("kin_h2") : SiteById("kin_h1");
                var spare = Math.Max(0, c.ac.maxPayload - KitMass(c));
                var kg = twin ? Js.Round(Math.Min(spare, 300 + c.R() * 900) / 10) * 10 : Js.Round(Math.Min(spare, 12 + c.R() * 14));
                if (kg < 8) return null;
                var what = twin ? (d.kind == SiteKind.Helideck ? "pump spares" : d.id == "ardoch_ski" ? "snow-fence posts" : d.id == "ardoch_mrt" ? "rescue equipment" : "fencing materials") : "camera equipment";
                return new Callout
                {
                    title = $"Freight: {what} to {d.name}", from = "Kinloch Ops", priority = 3, place = from.name, x = from.x, z = from.z, destination = d.name,
                    details = new[] { $"{kg:0} kg of {what} waiting at {from.name} for {d.name}.", "Paid on delivery. Heavier loads reduce your performance: check torque in the hover." },
                    pay = Js.Round(900 + kg * (twin ? 1.4 : 25) + Js.Hypot(d.x - from.x, d.z - from.z) * 0.12),
                    props = new List<PropDef> { Prop("m:pallet", from.x + 10, from.z + from.radius + 6, 0, PropRole.Cargo), Prop("m:pallet", from.x + 11.5, from.z + from.radius + 6, 0, PropRole.Cargo), Prop("P:hivis:stand:801", from.x + 8, from.z + from.radius + 5, -1) },
                    build = (job, tMin) => new JobBuild { objectives = new List<ObjectiveDef>
                    {
                        LandAt($"Land at {from.name} for the freight", "Pickup", from, Math.Max(12, from.radius * 1.5), job, tMin + 25),
                        LoadAt($"Load {kg:0} kg of {what}", "Pickup", what, kg, 40, from, job, true),
                        LandAt($"Deliver to {d.name}", "Delivery", d, d.kind == SiteKind.Helideck ? 8 : Math.Max(15, d.radius * 1.4), job),
                        LoadAt("Unload the freight", "Delivery", what, kg, 40, d, job),
                    } },
                };
            } },
            // ---- crew change
            new Template { type = CalloutType.CrewChange, weight = 1, requires = new[] { KitId.Seats, KitId.Floats }, recommended = new[] { KitId.AuxFuel }, needsTwin = true, make = c =>
            {
                var ps = Platforms().ToArray();
                var p = ps[Pick(c.R, ps.Length)];
                var h2 = SiteById("kin_h2");
                var spare = Math.Max(0, c.ac.maxPayload - KitMass(c));
                var pax = Math.Max(2, Math.Min(12, (int)Math.Floor(spare / 100)));
                var back = Math.Max(1, pax - 1 - Pick(c.R, 3));
                var props = Enumerable.Range(0, Math.Min(6, pax)).Select(i => Prop($"P:{(i % 2 == 1 ? "worker" : "casual")}:stand:{900 + i}", h2.x - 18 + i * 1.1, h2.z + 16 + (i % 2), 3.1, PropRole.Pax)).ToList();
                return new Callout
                {
                    title = $"Crew change: {p.name}", from = "Kinloch Ops", priority = 3, place = h2.name, x = h2.x, z = h2.z, destination = p.name,
                    details = new[] { $"{pax} passengers out to {p.name}, {back} back to Kinloch.", "Brief the passengers, fly the round trip, and keep to the slot: the platform has a crane lift booked after you." },
                    pay = 2200 + pax * 120, props = props,
                    build = (job, tMin) => new JobBuild { objectives = new List<ObjectiveDef>
                    {
                        LandAt($"Land at {h2.name} for the passengers", "Check-in", h2, 18, job, tMin + 20),
                        LoadAt($"Board {pax} passengers", "Check-in", $"{pax} passengers", pax * 100, 50, h2, job, true),
                        LandAt($"Land on {p.name}", "Outbound", p, 8, job, tMin + 45),
                        LoadAt("Passengers off, return crew on", "Turnround", "passengers", (pax - back) * 100, 60, p, job),
                        LandAt($"Return to {h2.name}", "Inbound", h2, 18, job),
                        LoadAt("Disembark the passengers", "Inbound", $"{back} passengers", back * 100, 30, h2, job),
                    } },
                };
            } },
            // ---- police search
            new Template { type = CalloutType.Police, weight = 2, requires = new KitId[0], recommended = new[] { KitId.Flir, KitId.Searchlight }, needsTwin = false, make = c =>
            {
                var towns = new[] { ("Kinloch", -620.0, -900.0), ("Balnagown", -1250.0, -4350.0), ("Inverhallan", 420.0, -7250.0) };
                var tw = towns[Pick(c.R, towns.Length)];
                double x = tw.Item2 + (c.R() - 0.5) * 1600, z = tw.Item3 + (c.R() - 0.5) * 1600;
                var id = $"p{c.seq}";
                return new Callout
                {
                    title = $"Police: vulnerable missing person near {tw.Item1}", from = "Police Scotland", priority = 2, place = PlaceName(x, z), x = x, z = z,
                    details = new[] { "An elderly man with dementia has walked away from home. He is wearing dark clothes.", "Search the area, then report his position so the officers on foot can reach him." },
                    pay = 1500, props = new List<PropDef> { Prop("v:police:0", tw.Item2 + 40, tw.Item3 + 30, 1, beacons: true) },
                    build = (job, tMin) =>
                    {
                        var s = O(ObjectiveKind.Search, "Search for the missing man", "Search", job); s.areaId = $"{job}_{id}"; s.targetId = $"{job}_t"; s.hintAfterMin = 6; s.deadlineMin = tMin + RespondFor(c, x, z) + 8;
                        return new JobBuild
                        {
                            objectives = new List<ObjectiveDef> { s, Radio("Report his position to the police", "Report", "Police control, found him, standing by overhead with the light on him for your officers.", "Copied, officers two minutes away. Thank you, you can stand down.", "Police", job) },
                            targets = new List<TargetDef> { new TargetDef { id = $"{job}_t", name = "Missing man", kind = TargetKind.Walker, areaId = $"{job}_{id}", conspicuity = 0.4 } },
                            areas = new List<SearchAreaDef> { new SearchAreaDef { id = $"{job}_{id}", name = "Police search area", cx = x, cz = z, w = 1100, h = 900, rotDeg = 0 } },
                        };
                    },
                };
            } },
            // ---- aerial photography
            new Template { type = CalloutType.Photo, weight = 2, requires = new KitId[0], recommended = new KitId[0], needsTwin = false, day = true, make = c =>
            {
                var p = Places.POI[Pick(c.R, Places.POI.Count)];
                var g = GroundAt(p.x, p.z).h * 3.28;
                var lo = Js.Round((g + 500) / 50) * 50; var hi = lo + 400;
                return new Callout
                {
                    title = $"Aerial photos of {p.name}", from = "Kinloch Ops", priority = 3, place = p.name, x = p.x, z = p.z,
                    details = new[] { $"A client wants oblique photographs of {p.name}. Two passes, from opposite sides, between {lo:0} and {hi:0} ft.", "Smooth and steady: the photographer is leaning out of the door." },
                    pay = 750,
                    build = (job, tMin) => new JobBuild { objectives = new List<ObjectiveDef>
                    {
                        new ObjectiveDef { kind = ObjectiveKind.Waypoint, label = $"First pass, west side ({lo:0}–{hi:0} ft)", phase = "Photos", name = $"{p.name} west", x = p.x - 450, z = p.z, radiusM = 250, altMinFt = lo, altMaxFt = hi, deadlineMin = tMin + 30, job = job },
                        new ObjectiveDef { kind = ObjectiveKind.Waypoint, label = $"Second pass, east side ({lo:0}–{hi:0} ft)", phase = "Photos", name = $"{p.name} east", x = p.x + 450, z = p.z, radiusM = 250, altMinFt = lo, altMaxFt = hi, job = job },
                        Radio("Tell the client the photos are done", "Photos", "Kinloch Ops, photo flight complete, returning.", "Lovely, the client is delighted. Thanks.", "Ops", job),
                    } },
                };
            } },
            // ---- overhead line check
            new Template { type = CalloutType.LineCheck, weight = 1, requires = new KitId[0], recommended = new KitId[0], needsTwin = false, day = true, make = c =>
            {
                var poles = Places.POWER_POLES;
                if (poles.Count < 30) return null;
                var i0 = Pick(c.R, poles.Count - 25);
                var pts = new[] { poles[i0], poles[i0 + 12], poles[i0 + 24] };
                return new Callout
                {
                    title = "Fault patrol on the glen line", from = "Highland Utility Services", priority = 3, place = PlaceName(pts[1].x, pts[1].z), x = pts[1].x, z = pts[1].z,
                    details = new[] { "The 33 kV line up the glen has tripped. Fly the section shown so the observer can look for damage, then report.", "Stay above the wires." },
                    pay = 950,
                    build = (job, tMin) =>
                    {
                        var b = new JobBuild();
                        for (int k = 0; k < pts.Length; k++)
                            b.objectives.Add(new ObjectiveDef { kind = ObjectiveKind.Waypoint, label = $"Pole {i0 + k * 12 + 1}", phase = "Patrol", name = $"Pole {i0 + k * 12 + 1}", x = pts[k].x, z = pts[k].z, radiusM = 220, deadlineMin = k == 0 ? tMin + 30 : (double?)null, job = job });
                        b.objectives.Add(Radio("Report the damage", "Patrol", $"Highland Control, broken crossarm on pole {i0 + 13}, line down across the field.", "Copied, a crew is on its way. Thank you.", "Highland Control", job));
                        return b;
                    },
                };
            } },
            // ---- urgent blood delivery (light helicopter)
            new Template { type = CalloutType.Bloods, weight = 2, requires = new[] { KitId.Cargo }, recommended = new KitId[0], needsTwin = false, make = c =>
            {
                if (c.ac.engines != 1) return null;
                var hs = Hospitals().ToArray();
                var a = hs[Pick(c.R, hs.Length)]; var b = Nearest(hs.Where(h => h != a), a.x, a.z);
                return new Callout
                {
                    title = "Urgent blood delivery", from = "NHS blood service", priority = 2, place = a.name, x = a.x, z = a.z, destination = b.name,
                    details = new[] { $"Rare-group blood for a patient in theatre at {b.name}. A cool box (8 kg) is waiting at {a.name}.", "Quick turnaround: rotors running for the pickup." },
                    pay = 650, props = new List<PropDef> { Prop("P:scrubs:stand:1001", a.x + a.radius * 1.6, a.z, -1.6) },
                    build = (job, tMin) => new JobBuild { objectives = new List<ObjectiveDef>
                    {
                        LandAt($"Land at {a.name}", "Pickup", a, Math.Max(12, a.radius * 1.5), job, tMin + RespondFor(c, a.x, a.z) + 3),
                        LoadAt("Cool box aboard", "Pickup", "blood cool box", 8, 15, a, job, true),
                        LandAt($"Deliver to {b.name}", "Delivery", b, Math.Max(12, b.radius * 1.5), job),
                        LoadAt("Hand over the cool box", "Delivery", "blood cool box", 8, 10, b, job),
                    } },
                };
            } },
        };

        static ObjectiveDef Deliver(ObjectiveDef o) { o.deliver = true; return o; }

        /// <summary>Can this aircraft type ever do this kind of job (with the right kit)?</summary>
        public static bool TypePossible(Template t, AircraftConfig ac) => !(t.needsTwin && ac.engines < 2) && t.requires.All(k => Loadout.CanFit(ac, k));

        /// <summary>Why the current loadout cannot take a callout (null = it can).</summary>
        public static string CannotAccept(KitId[] requires, bool needsTwin, AircraftConfig ac, IList<KitId> kit)
        {
            if (needsTwin && ac.engines < 2) return "Needs a twin-engine aircraft";
            var missing = requires.Where(k => !kit.Contains(k)).ToList();
            if (missing.Count > 0) return $"Needs: {string.Join(", ", missing.Select(k => Loadout.ById(k).@short))}";
            return null;
        }

        /// <summary>The open-ended mission the freeplay runtime starts with (no items until a callout is accepted).</summary>
        public static MissionDef FreeplayMission(FreeplayConfig cfg, AircraftConfig ac, double kitKg)
        {
            var start = SiteById(cfg.baseSiteId);
            return new MissionDef
            {
                id = "freeplay", title = "Freeplay shift", shortTitle = "Freeplay", category = MissionCategory.Freeplay, employer = "Kinloch Ops", aircraftId = ac.id,
                summary = cfg.pace == FreeplayPace.Off ? $"free flight from {start.name}. Start up when ready: no callouts, fly wherever you like." : $"freeplay shift from {start.name}. Start up when ready. Callouts will come through on the tablet (Tab, or LT on a controller).",
                weatherId = cfg.weatherId == "random" ? "calm" : cfg.weatherId, timeOfDay = cfg.tod, startSiteId = start.id, startHeadingDeg = 270,
                defaultFuelFrac = 1, minFuelKg = ac.engines == 2 ? 250 : 10, payloadKg = kitKg, dual = false, objectives = new ObjectiveDef[0],
                assessment = new AssessmentCriteria { passMark = 0 }, prerequisites = new Prerequisites(), estMinutes = 60, xp = 0, availableFromMilestone = 8,
                callsign = ac.callsign ?? (ac.role == AircraftRole.Sar ? "Rescue 9-1-1" : ac.engines == 2 ? "Helimed 7-1" : "Kestrel Kilo Two"),
            };
        }
    }

    /// <summary>Runs a freeplay shift: callouts, the job in hand, pay and refuelling.</summary>
    public sealed class FreeplayDirector
    {
        public readonly FreeplayConfig cfg;
        public readonly AircraftConfig ac;
        public List<Callout> offers = new List<Callout>();
        public ActiveJob active;
        public readonly List<JobRecord> history = new List<JobRecord>();
        public double earned, fuelCost;
        public bool refuelling;
        /// <summary>Incremented whenever the scenery (props or casualties) changes.</summary>
        public int version;

        sealed class PropSet { public string job; public List<PropDef> defs; public double? until; }
        List<PropSet> props = new List<PropSet>();
        double nextAt;
        int seq;
        readonly Func<double> R;
        readonly Site @base;

        public FreeplayDirector(FreeplayConfig cfg, AircraftConfig ac)
        {
            this.cfg = cfg; this.ac = ac;
            R = SimMath.Rng(cfg.seed);
            @base = SiteById(cfg.baseSiteId);
            nextAt = cfg.pace == FreeplayPace.Off ? double.PositiveInfinity : 15;
        }

        /// <summary>Called a few times a second with the live runtime.</summary>
        public void Update(double t, MissionRuntime rt, HeliState s, double dt)
        {
            // expire old offers (with a radio call so the crew know)
            foreach (var o in offers.Where(o => t > o.expiresAt).ToList())
            {
                rt.Say(t, o.from, $"{o.title}: another unit has taken it. Removed from the tablet.", MessageKind.Info);
                DropProps(o.id);
            }
            offers = offers.Where(o => t <= o.expiresAt).ToList();
            // new callouts
            if (t >= nextAt)
            {
                var (lo, hi) = FreeplayData.PACE[cfg.pace];
                nextAt = t + lo + R() * (hi - lo);
                if (offers.Count < 4)
                {
                    var c = Generate(t);
                    if (c == null) nextAt = t + 10;   // nothing suitable this time: try again shortly
                    else
                    {
                        offers.Add(c);
                        props.Add(new PropSet { job = c.id, defs = c.props, until = null });
                        version++;
                        rt.Say(t, c.from, $"{(c.priority == 1 ? "PRIORITY ONE: " : "")}{c.title}, {c.place}. Details on the tablet.", c.priority == 1 ? MessageKind.Warning : MessageKind.Caution);
                    }
                }
            }
            // the job in hand
            var a = active;
            if (a != null)
            {
                var items = rt.JobItems(a.job);
                var first = items.FirstOrDefault();
                if (a.arrivedAt == null && first != null && first.status == ObjectiveStatus.Complete) a.arrivedAt = t;
                // casualty or freight loaded: take them out of the scenery
                var pickup = items.FirstOrDefault(i => i.def.kind == ObjectiveKind.Load && i.def.pickup);
                var winched = items.FirstOrDefault(i => i.def.kind == ObjectiveKind.Winch);
                if ((pickup != null && pickup.status == ObjectiveStatus.Complete) || (winched != null && winched.status == ObjectiveStatus.Complete))
                {
                    var p = props.FirstOrDefault(x => x.job == a.callout.id);
                    if (p != null && p.defs.Any(d => d.role != PropRole.None)) { p.defs = p.defs.Where(d => d.role == PropRole.None).ToList(); version++; }
                    if (winched != null && winched.status == ObjectiveStatus.Complete)
                        foreach (var tg in a.targets) if (!tg.id.EndsWith("_gone")) { tg.id += "_gone"; version++; }
                }
                if (items.Count > 0 && items.All(i => i.status == ObjectiveStatus.Complete || i.status == ObjectiveStatus.Failed)) Finish(t, rt, JobOutcome.Completed);
            }
            // fuel from the bowser at Kinloch or a platform
            if (refuelling)
            {
                var g = GroundAt(s.pos.x, s.pos.z);
                var site = RefuelSite(s);
                if (!s.onGround || site == null || g.surface == Surface.Water) { refuelling = false; rt.Say(t, "Refuel", "Refuelling stopped.", MessageKind.Info); }
                else
                {
                    double rate = ac.engines == 2 ? 14 : 1.6, price = ac.engines == 2 ? 1.1 : 2.6;
                    var add = Math.Min(rate * dt, ac.fuelCapacity - s.fuel);
                    s.fuel += add; fuelCost += add * price;
                    if (s.fuel >= ac.fuelCapacity - 0.5) { refuelling = false; rt.Say(t, "Refuel", $"Tanks full: {ac.fuelCapacity:0} kg.", MessageKind.Good); }
                }
            }
        }

        /// <summary>Where the aircraft can take fuel right now (null = not here).</summary>
        public Site RefuelSite(HeliState s)
        {
            if (!s.onGround) return null;
            return SITES.FirstOrDefault(x => (x.kind == SiteKind.Heliport || x.kind == SiteKind.Helideck) && Js.Hypot(x.x - s.pos.x, x.z - s.pos.z) < Math.Max(25, x.radius * 2));
        }

        Callout Generate(double t)
        {
            var night = cfg.tod != TimeOfDay.Day;
            var kit = cfg.kit;
            var pool = FreeplayData.TEMPLATES.Where(tp => FreeplayData.TypePossible(tp, ac) && !(tp.day && night)).ToList();
            if (pool.Count == 0) return null;
            double Weight(FreeplayData.Template tp) => tp.weight * (FreeplayData.CannotAccept(tp.requires, tp.needsTwin, ac, kit) != null ? 0.35 : 1.6) * (night && tp.type == CalloutType.Police ? 1.6 : 1) * (cfg.favour != null && cfg.favour.Contains(tp.type) ? 2.5 : 1);
            for (int attempt = 0; attempt < 6; attempt++)
            {
                var r = R() * pool.Sum(Weight);
                var tp = pool[0];
                foreach (var p in pool) { r -= Weight(p); if (r <= 0) { tp = p; break; } }
                var id = $"job{++seq}";
                var ctx = new FreeplayData.Ctx { R = R, t = t, ac = ac, kit = kit, tod = cfg.tod, @base = @base, id = id, seq = seq };
                var made = tp.make(ctx);
                if (made == null) continue;
                var respondMin = FreeplayData.RespondFor(ctx, made.x, made.z);
                var dist = Js.Hypot(made.x - @base.x, made.z - @base.z);
                // an incident on the doorstep is no flight at all: those go to a road ambulance
                if (dist < 1500 && !new[] { CalloutType.Transfer, CalloutType.Cargo, CalloutType.CrewChange, CalloutType.Bloods, CalloutType.Offshore }.Contains(tp.type)) continue;
                var life = made.priority == 1 ? 6 * 60 : made.priority == 2 ? 10 * 60 : 16 * 60;
                made.id = id; made.type = tp.type; made.requires = tp.requires; made.recommended = tp.recommended.Where(k => Loadout.CanFit(ac, k)).ToArray(); made.needsTwin = tp.needsTwin;
                made.createdAt = t; made.expiresAt = t + life; made.respondMin = respondMin; made.pay = Js.Round((made.pay + dist / 1000 * 70) / 10) * 10;
                return made;
            }
            return null;
        }

        /// <summary>Accept a callout: its items go into the plan. The runtime re-checks everything in flight. Returns why not, or null.</summary>
        public string Accept(string id, double t, MissionRuntime rt, HeliState s)
        {
            if (active != null) return "Finish or stand down from the current job first.";
            var c = offers.FirstOrDefault(o => o.id == id);
            if (c == null) return "That callout is no longer available.";
            var why = FreeplayData.CannotAccept(c.requires, c.needsTwin, ac, cfg.kit);
            if (why != null) return why;
            var built = c.build(c.id, t / 60);
            var jobSeed = unchecked((uint)((long)cfg.seed ^ (seq * 2654435761L)));
            var placed = rt.AppendJob(built.objectives, built.targets, built.areas, s, jobSeed);
            offers = offers.Where(o => o.id != id).ToList();
            active = new ActiveJob { callout = c, job = c.id, acceptedAt = t, arrivedAt = null, hardAtStart = rt.Stats.hardLandings, targets = placed };
            version++;
            rt.Say(t, c.from, $"{c.title}: accepted. {(c.destination != null ? $"Destination {c.destination}. " : "")}Respond within {c.respondMin:0} minutes.", MessageKind.Good);
            return null;
        }

        /// <summary>Stand down from the current job.</summary>
        public void Abandon(double t, MissionRuntime rt, HeliState s)
        {
            if (active == null) return;
            rt.CancelJob(active.job, s);
            foreach (var tg in active.targets) if (!tg.id.EndsWith("_gone")) tg.id += "_gone";
            Finish(t, rt, JobOutcome.Abandoned);
        }

        void Finish(double t, MissionRuntime rt, JobOutcome outcome)
        {
            var a = active;
            var items = rt.JobItems(a.job);
            var scored = items.Where(i => i.status == ObjectiveStatus.Complete && i.score != null).Select(i => (double)i.score.Value).ToList();
            var failed = items.Count(i => i.status == ObjectiveStatus.Failed);
            var quality = outcome == JobOutcome.Abandoned ? 0 : Js.Round(Math.Max(0, (scored.Count > 0 ? scored.Average() : 80) - failed * 25));
            var arrive = a.arrivedAt ?? t;
            var lateMin = Math.Max(0, (arrive - a.acceptedAt) / 60 - a.callout.respondMin);
            var hard = rt.Stats.hardLandings - a.hardAtStart;
            var notes = new List<string>();
            double pay = 0;
            if (outcome == JobOutcome.Completed && failed < items.Count)
            {
                var k = (0.55 + 0.45 * quality / 100) * Math.Max(0.5, 1 - lateMin * 0.06) * Math.Max(0.4, 1 - hard * 0.2);
                pay = Js.Round(a.callout.pay * k / 10) * 10;
                if (a.callout.priority == 1 && lateMin == 0) { pay += Js.Round(a.callout.pay * 0.15 / 10) * 10; notes.Add("Priority response bonus"); }
                if (lateMin > 0) notes.Add($"Arrived {lateMin:F1} min late");
                if (hard > 0) notes.Add($"{hard} hard landing{(hard > 1 ? "s" : "")}");
                if (failed > 0) notes.Add($"{failed} item{(failed > 1 ? "s" : "")} not achieved");
                rt.Say(t, a.callout.from, $"{a.callout.title}: complete. Thank you, crew. {FreeplayData.Money(pay)} to the company.", MessageKind.Good);
            }
            else rt.Say(t, a.callout.from, $"{a.callout.title}: {(outcome == JobOutcome.Abandoned ? "stood down. Another unit will take it." : "not completed.")}", MessageKind.Caution);
            earned += pay;
            history.Add(new JobRecord { id = a.job, title = a.callout.title, type = a.callout.type, pay = pay, quality = quality, lateMin = lateMin, outcome = outcome, minutes = (t - a.acceptedAt) / 60, notes = notes });
            var ps = props.FirstOrDefault(x => x.job == a.callout.id);
            if (ps != null) ps.until = t + 90;
            active = null;
            version++;
        }

        void DropProps(string job) { props = props.Where(p => p.job != job).ToList(); version++; }

        /// <summary>Scenery to draw now (props of offered and active callouts, and of finished ones for a while).</summary>
        public List<PropDef> SceneProps(double t)
        {
            var before = props.Count;
            props = props.Where(p => p.until == null || p.until > t).ToList();
            if (props.Count != before) version++;
            return props.SelectMany(p => p.defs).ToList();
        }

        /// <summary>Casualties still to be found or recovered (rescued ones are no longer drawn).</summary>
        public static List<PlacedTarget> VisibleTargets(IEnumerable<PlacedTarget> all) => all.Where(x => !x.id.EndsWith("_gone")).ToList();

        public FreeplaySummary Summary(double flightSeconds) => new FreeplaySummary { jobs = history.ToList(), earned = earned, fuelCost = Math.Round(fuelCost), flightSeconds = flightSeconds, aircraftId = ac.id };
    }
}
