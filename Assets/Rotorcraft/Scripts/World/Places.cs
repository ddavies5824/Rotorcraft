// Places around Kinloch: roads, towns and everything built in the landscape, as plain data (port of
// src/world/places.ts). The renderer draws it, the flight model collides with the structures, and freeplay
// callouts use the named places as incident locations. Everything is fictional.
// Props are placed with a seeded generator, so the calls below must stay in the browser game's order.
using System;
using System.Collections.Generic;
using System.Linq;
using Rotorcraft.Sim;
using static Rotorcraft.World.Terrain;

namespace Rotorcraft.World
{
    public enum VehicleKind { Hatch, Saloon, Estate, Suv, Van, Pickup, Ambulance, Police, FireEngine, Rover, DumpTruck, Excavator, Bus, Buggy, Tractor }
    public enum PersonKind { Casual, Hivis, Medic, Police, Fire, Golfer, Walker, Fisher, Mrt, Worker, Skier, Scrubs, Aircrew, Winchman }
    public enum PersonPose { Stand, Sit, Wave, WalkA, WalkB, Kneel, Lie, Phone, Point, Hang, Hug }
    public enum BoatKind { Trawler, Creel, Lifeboat, Yacht, Rib }
    public enum PropType { Vehicle, Person, Boat, Tent, Flag, Goal, Bench, Stretcher, Cone, Gravel, Dog, Pylon, Tow, Sign, Container, Pallet }

    public sealed class Prop
    {
        public PropType type;
        public double x, z, rot;
        public VehicleKind vehicle; public int paint; public bool lights;   // vehicles
        public PersonKind person; public PersonPose pose; public int seed;  // people
        public BoatKind boat;                                                // boats
        public double s = 1;                                                 // scale of other props
    }

    public enum PavedColor { Tarmac, Concrete, Gravel }
    /// <summary>A painted tarmac or concrete area (car parks, yards, quays), drawn flat on the ground.</summary>
    public sealed class Paved { public double x, z, w, d, rot; public bool bays; public PavedColor color = PavedColor.Tarmac; }

    public sealed class Road { public string name; public double width; public Pt[] pts; }
    public sealed class Settlement { public string name; public double x, z, r; }
    public sealed class Turbine { public double x, z, hub, r, yaw; }
    public enum PoiKind { Golf, Camp, Ski, Quarry, Castle, Distillery, Harbour, Beach, Summit, School, Farm, Windfarm }
    public sealed class Poi { public string id, name; public double x, z; public PoiKind kind; }

    public static class Places
    {
        public static readonly Pt[] RIVER;
        public static readonly (double x, double z, double len, double width, double rot) BRIDGE = (-4300, 821, 70, 7, Math.Atan2(-4300 - -4200, 821 - 300));
        /// <summary>The road network with its bends rounded (the bridge, which is straight, is left as it is).</summary>
        public static readonly Road[] ROADS;
        public static readonly Settlement[] SETTLEMENTS;
        public static readonly List<FlatArea> FLAT_AREAS = new List<FlatArea>();
        public static readonly List<Site> NEW_SITES = new List<Site>();
        public static readonly List<Structure> NEW_STRUCTURES = new List<Structure>();
        public static readonly List<Prop> PROPS = new List<Prop>();
        public static readonly List<Paved> PAVED = new List<Paved>();
        /// <summary>Kinloch Heliport's apron and taxiway. Drawn lower than other paved areas, so the pads lie on top of them.</summary>
        public static readonly Paved HELIPORT_APRON = new Paved { x = 20, z = -20, w = 150, d = 110 }, HELIPORT_TAXIWAY = new Paved { x = -160, z = 30, w = 260, d = 14, rot = 0.2 };
        /// <summary>Wind turbines: tower base, hub height, rotor radius and the direction the rotor faces (rad).</summary>
        public static readonly List<Turbine> TURBINES = new List<Turbine>();
        /// <summary>Overhead line poles (33 kV wood poles up the glen) and ski-tow pylons.</summary>
        public static readonly List<Pt> POWER_POLES = new List<Pt>();
        public static readonly List<Pt> SKI_TOW = new List<Pt>();
        /// <summary>Golf course: greens, bunkers (x, z, radius) and fairways.</summary>
        public static readonly List<Pt> GOLF_GREENS = new List<Pt>(), GOLF_TEES = new List<Pt>();
        public static readonly List<(double x, double z, double r)> GOLF_BUNKERS = new List<(double, double, double)>();
        public static readonly List<Pt[]> GOLF_FAIRWAYS = new List<Pt[]>();
        public static readonly (double x, double z, double r) QUARRY = (-2000, -2800, 165);
        /// <summary>Named places a freeplay callout can be sent to (scene of an incident).</summary>
        public static readonly List<Poi> POI = new List<Poi>();

        static readonly Road[] RAW_ROADS;
        static readonly Func<double> R;

        static Pt P(double x, double z) => new Pt(x, z);

        static Pt[] CoastRoad()
        {
            var pts = new List<Pt>();
            for (double z = -9000; z <= 4000; z += 120) pts.Add(P(CoastX(z) - 280 + 40 * Math.Sin(z / 600), z));
            return pts.ToArray();
        }

        /// <summary>Point `a` metres seaward of the coastline at z, and `b` metres along it (southwards).</summary>
        static Pt CoastPt(double z, double a, double b = 0) { var f = CoastFrame(z); return P(CoastX(z) + f.n.x * a + f.t.x * b, z + f.n.z * a + f.t.z * b); }
        static double CoastRot(double z) => CoastFrame(z).rot;

        /// <summary>
        /// Replace each sharp bend in a road with a circular arc (radius up to maxR, smaller where the straights
        /// are short), so roads curve like real ones. Points for which keep() is true are kept exactly.
        /// </summary>
        public static Pt[] RoundCorners(Pt[] pts, double maxR = 90, Func<Pt, bool> keep = null)
        {
            if (pts.Length < 3) return (Pt[])pts.Clone();
            var out_ = new List<Pt> { pts[0] };
            for (int i = 1; i < pts.Length - 1; i++)
            {
                Pt A = pts[i - 1], B = pts[i], C = pts[i + 1];
                double l1 = Js.Hypot(B.x - A.x, B.z - A.z), l2 = Js.Hypot(C.x - B.x, C.z - B.z);
                double u1x = (B.x - A.x) / l1, u1z = (B.z - A.z) / l1, u2x = (C.x - B.x) / l2, u2z = (C.z - B.z) / l2;
                var turn = Math.Atan2(u1x * u2z - u1z * u2x, u1x * u2x + u1z * u2z);
                var th = Math.Abs(turn);
                if (th < 0.05 || (keep != null && keep(B)) || !(l1 > 0 && l2 > 0)) { out_.Add(B); continue; }
                var t = Math.Min(Math.Min(maxR * Math.Tan(th / 2), l1 * 0.45), l2 * 0.45);
                var n = Math.Max(2, (int)Math.Ceiling(th / 0.1));
                // quadratic Bezier through the corner: tangent to both straights, close to a circular arc for these angles
                Pt P0 = P(B.x - u1x * t, B.z - u1z * t), P2 = P(B.x + u2x * t, B.z + u2z * t);
                for (int k = 0; k <= n; k++)
                {
                    double f = (double)k / n, a = (1 - f) * (1 - f), b = 2 * f * (1 - f), c = f * f;
                    out_.Add(P(a * P0.x + b * B.x + c * P2.x, a * P0.z + b * B.z + c * P2.z));
                }
            }
            out_.Add(pts[pts.Length - 1]);
            return out_.ToArray();
        }

        // ---- helpers that use the seeded generator (each call consumes numbers in the browser game's order)
        static T Pick<T>(T[] a) => a[(int)Math.Floor(R() * a.Length)];

        static void Person(PersonKind kind, PersonPose pose, double x, double z, double? rot = null)
        {
            var r = rot ?? R() * 6.28;
            PROPS.Add(new Prop { type = PropType.Person, person = kind, pose = pose, x = x, z = z, rot = r, seed = (int)Math.Floor(R() * 1e6) });
        }

        static void Vehicle(VehicleKind kind, double x, double z, double rot, bool lights = false) =>
            PROPS.Add(new Prop { type = PropType.Vehicle, vehicle = kind, x = x, z = z, rot = rot, paint = (int)Math.Floor(R() * 1000), lights = lights });

        static void Building(double x, double z, double w, double d, double h, BuildingStyle style, double rot = 0) =>
            NEW_STRUCTURES.Add(new Structure { x = x, z = z, w = w, d = d, h = h, @base = 0, kind = StructureKind.Building, style = style, rot = rot });

        static void Other(PropType type, double x, double z, double rot, double s = 1) => PROPS.Add(new Prop { type = type, x = x, z = z, rot = rot, s = s });

        static readonly VehicleKind[] CarParkKinds = { VehicleKind.Hatch, VehicleKind.Hatch, VehicleKind.Saloon, VehicleKind.Estate, VehicleKind.Suv, VehicleKind.Suv, VehicleKind.Hatch, VehicleKind.Van, VehicleKind.Pickup };

        /// <summary>Rows of parked cars in a car park (local x across the bays, local z along the row).</summary>
        static void CarPark(double cx, double cz, double rot, int rows, int perRow, double fill)
        {
            double c = Math.Cos(rot), s = Math.Sin(rot);
            double W = perRow * 2.7 + 2, D = rows * 11 - 2;
            PAVED.Add(new Paved { x = cx, z = cz, w = W, d = D, rot = rot, bays = true });
            for (int r = 0; r < rows; r++)
                for (int i = 0; i < perRow; i++)
                {
                    if (R() > fill) continue;
                    double lx = -W / 2 + 2.35 + i * 2.7, lz = -D / 2 + 2.6 + r * 11;
                    var facing = r % 2 == 1 ? rot + Math.PI : rot;
                    var k = Pick(CarParkKinds);
                    Vehicle(k, cx + lx * c + lz * s, cz - lx * s + lz * c, facing + (R() - 0.5) * 0.06);
                }
        }

        static Places()
        {
            var river = new List<Pt>();
            for (double x = -9000; x <= 1500; x += 60)
            {
                var z = RiverZ(x);
                if (x > CoastX(z) + 40) break;
                river.Add(P(x, z));
            }
            RIVER = river.ToArray();

            RAW_ROADS = new[]
            {
                new Road { name = "Kinloch road", width = 6.5, pts = new[] { P(175, 57.2), P(118, 30), P(108, -70), P(88, -125), P(-150, -330), P(-420, -650), P(-640, -930), P(-860, -1220), P(-1030, -1700), P(-1140, -2400), P(-1210, -3200), P(-1260, -3900), P(-1300, -4420), P(-1302, -4450), P(-1348, -4610), P(-1352, -4790), P(-1330, -4960), P(-1400, -5600) } },
                new Road { name = "Coast road", width = 7, pts = CoastRoad() },
                new Road { name = "Heliport link", width = 6, pts = new[] { P(88, 36), P(95, 60), P(380, 50), P(700, 30), P(CoastX(0) - 280, 0) } },
                new Road { name = "Glen road", width = 5.5, pts = new[] { P(-860, -1220), P(-1500, -1500), P(-2200, -1900), P(-2800, -2120), P(-3350, -1500), P(-3900, -500), P(-4200, 300), P(-4300, 821), P(-4380, 1500), P(-4600, 2400), P(-4900, 3300) } },
                new Road { name = "Inverhallan road", width = 6.5, pts = new[] { P(-1400, -5600), P(-1250, -6150), P(-800, -6700), P(-250, -7150), P(120, -7420), P(215, -7560) } },
                new Road { name = "Inverhallan link", width = 6, pts = new[] { P(215, -7560), P(520, -7420), P(900, -7330), P(CoastX(-7300) - 280 + 40 * Math.Sin(-7300.0 / 600), -7300) } },
                new Road { name = "Ski road", width = 5, pts = new[] { P(-4380, 1500), P(-4800, 1420), P(-5300, 1330), P(-5750, 1230), P(-6060, 1170) } },
                new Road { name = "Castle road", width = 5, pts = new[] { P(-1500, -1500), P(-1780, -1050), P(-2000, -700), P(-2250, -150), P(-2400, 300) } },
                new Road { name = "Quarry road", width = 6, pts = new[] { P(-2200, -1900), P(-2120, -2300), P(-2040, -2620) } },
                new Road { name = "Mountain rescue track", width = 4.5, pts = new[] { P(-3919, -450), P(-4040, -455), P(-4110, -460) } },
                new Road { name = "Police yard", width = 6, pts = new[] { P(-1302, -4450), P(-1150, -4452), P(-1045, -4455) } },
                new Road { name = "Harbour road", width = 6, pts = new[] { CoastPt(-2200, -280), CoastPt(-2200, -150, -20), CoastPt(-2200, -55, -20) } },
                new Road { name = "Caravan park lane", width = 5, pts = new[] { P(CoastX(2300) - 280 + 40 * Math.Sin(2300.0 / 600), 2300), P(870, 2330), P(720, 2360) } },
                new Road { name = "Infirmary road", width = 6, pts = new[] { P(-750, -1085), P(-900, -1015), P(-1035, -1000) } },
                new Road { name = "Golf club drive", width = 5, pts = new[] { P(-150, -330), P(-80, -900), P(-40, -1330) } },
                new Road { name = "School lane", width = 5.5, pts = new[] { P(-1290, -4150), P(-1450, -4100), P(-1560, -4075) } },
            };
            ROADS = RAW_ROADS.Select(r => new Road { name = r.name, width = r.width, pts = RoundCorners(r.pts, 90, p => Js.Hypot(p.x - BRIDGE.x, p.z - BRIDGE.z) < BRIDGE.len) }).ToArray();

            SETTLEMENTS = new[]
            {
                new Settlement { name = "Kinloch", x = -620, z = -900, r = 520 },
                new Settlement { name = "Balnagown", x = -1250, z = -4350, r = 700 },
                new Settlement { name = "Portcraig", x = CoastX(-2200) - 280, z = -2200, r = 260 },
                new Settlement { name = "Inverhallan", x = 420, z = -7250, r = 650 },
                new Settlement { name = "Ardoch", x = -4250, z = 380, r = 260 },
            };

            R = SimMath.Rng(20261019);
            const double PI = Math.PI;

            // ---- Kinloch Royal Infirmary: city hospital with a rooftop helipad ----------------------------
            {
                var f = new FlatArea { x = -1000, z = -1080, r = 85 };
                FLAT_AREAS.Add(f);
                var roof = FlatElevation(f) + 24;
                NEW_STRUCTURES.Add(new Structure { x = -1000, z = -1080, w = 58, d = 40, h = 24, kind = StructureKind.Hospital, rooftop = true });
                NEW_STRUCTURES.Add(new Structure { x = -1000, z = -1030, w = 44, d = 22, h = 10, kind = StructureKind.Hospital });
                NEW_SITES.Add(new Site { id = "kinloch_royal", name = "Kinloch Royal Infirmary", kind = SiteKind.Hospital, x = -1008, z = -1086, radius = 10, deckHeight = roof, description = "City hospital with a raised rooftop helipad (24 m above the street). Emergency department and trauma unit." });
                CarPark(-1060, -1012, 0, 2, 12, 0.85);
                Vehicle(VehicleKind.Ambulance, -955, -1022, PI * 0.5); Vehicle(VehicleKind.Ambulance, -955, -1014, PI * 0.5, true);
                Person(PersonKind.Medic, PersonPose.Stand, -948, -1028); Person(PersonKind.Scrubs, PersonPose.Stand, -962, -1006); Person(PersonKind.Casual, PersonPose.WalkA, -975, -1005, 1.4);
            }

            // ---- Inverhallan: regional infirmary and town ---------------------------------------------------
            {
                NEW_STRUCTURES.Add(new Structure { x = 335, z = -7672, w = 92, d = 50, h = 20, kind = StructureKind.Hospital });
                NEW_STRUCTURES.Add(new Structure { x = 392, z = -7600, w = 34, d = 28, h = 12, kind = StructureKind.Hospital });
                CarPark(250, -7520, 0, 2, 14, 0.8);
                Vehicle(VehicleKind.Ambulance, 300, -7560, 0); Vehicle(VehicleKind.Ambulance, 306, -7560, 0); Vehicle(VehicleKind.Ambulance, 312, -7560, 0, true);
                Person(PersonKind.Scrubs, PersonPose.Stand, 268, -7586); Person(PersonKind.Medic, PersonPose.Stand, 272, -7588); Person(PersonKind.Medic, PersonPose.Phone, 300, -7570);
                Building(560, -7380, 26, 16, 12, BuildingStyle.Church, 0.2);
                Building(700, -7260, 40, 22, 9, BuildingStyle.Shop, 0.1);
                Building(480, -7150, 30, 18, 14, BuildingStyle.Office, -0.1);
            }

            // ---- Balnagown: hospital car park, police station with helipad, school playing field -----------
            {
                CarPark(-1180, -4690, 0, 2, 10, 0.75);
                Vehicle(VehicleKind.Ambulance, -1192, -4728, 0);
                Person(PersonKind.Scrubs, PersonPose.Stand, -1285, -4712); Person(PersonKind.Medic, PersonPose.Stand, -1287, -4688);
                NEW_SITES.Add(new Site { id = "balnagown_police", name = "Balnagown Police Station", kind = SiteKind.Pad, x = -1000, z = -4450, radius = 7, flatten = 70, description = "Police division headquarters with a small helipad in the yard." });
                Building(-955, -4498, 32, 16, 9, BuildingStyle.Police);
                Vehicle(VehicleKind.Police, -1030, -4478, 0); Vehicle(VehicleKind.Police, -1024, -4478, 0); Vehicle(VehicleKind.Van, -1018, -4479, 0); Vehicle(VehicleKind.Police, -1012, -4478, 0);
                Person(PersonKind.Police, PersonPose.Stand, -1012, -4466); Person(PersonKind.Police, PersonPose.Phone, -1000, -4470);
                NEW_SITES.Add(new Site { id = "balnagown_park", name = "Balnagown Academy Field", kind = SiteKind.Field, x = -1600, z = -4150, radius = 25, flatten = 95, description = "School playing field, used as an air ambulance landing site. Watch for goalposts and floodlight poles." });
                Building(-1625, -4048, 56, 18, 9, BuildingStyle.School);
                Other(PropType.Goal, -1600, -4200, 0); Other(PropType.Goal, -1600, -4100, PI);
                for (int i = 0; i < 7; i++) Person(PersonKind.Casual, R() < 0.5 ? PersonPose.WalkA : PersonPose.Stand, -1600 + (R() - 0.5) * 50, -4150 + (R() - 0.5) * 70);
                CarPark(-1520, -4040, 0, 1, 10, 0.7);
            }

            // ---- Kinloch fire station -----------------------------------------------------------------------
            {
                var rot = Math.Atan2(-270, -320);
                Building(-250, -520, 30, 16, 8, BuildingStyle.Firestation, rot + PI / 2);
                NEW_STRUCTURES.Add(new Structure { x = -238, z = -540, w = 4, d = 4, h = 16, kind = StructureKind.Tower });
                PAVED.Add(new Paved { x = -268, z = -505, w = 30, d = 14, rot = rot + PI / 2, color = PavedColor.Concrete });
                Vehicle(VehicleKind.FireEngine, -272, -498, rot + PI / 2 + PI / 2); Vehicle(VehicleKind.FireEngine, -264, -508, rot + PI / 2 + PI / 2);
                Person(PersonKind.Fire, PersonPose.Stand, -276, -510); Person(PersonKind.Fire, PersonPose.Stand, -280, -506);
            }

            // ---- Portcraig harbour and lifeboat station -----------------------------------------------------
            {
                const double z0 = -2200;
                var rot = CoastRot(z0);
                Pt At(double a, double b) => CoastPt(z0, a, b);
                const double quayTop = 2.6, armTop = 3.6;
                var q = At(-8, 0);
                NEW_STRUCTURES.Add(new Structure { x = q.x, z = q.z, w = 46, d = 170, h = quayTop + 6, @base = -6, kind = StructureKind.Pier, rot = rot });
                var n1 = At(55, -88);
                NEW_STRUCTURES.Add(new Structure { x = n1.x, z = n1.z, w = 150, d = 12, h = armTop + 8, @base = -8, kind = StructureKind.Pier, rot = rot });
                var e1 = At(126, -20);
                NEW_STRUCTURES.Add(new Structure { x = e1.x, z = e1.z, w = 12, d = 150, h = armTop + 8, @base = -8, kind = StructureKind.Pier, rot = rot });
                var lb = At(-14, -66);
                NEW_STRUCTURES.Add(new Structure { x = lb.x, z = lb.z, w = 16, d = 22, h = 9, @base = quayTop, kind = StructureKind.Building, style = BuildingStyle.Lifeboat, rot = rot });
                var fs = At(-16, 10);
                NEW_STRUCTURES.Add(new Structure { x = fs.x, z = fs.z, w = 14, d = 30, h = 6, @base = quayTop, kind = StructureKind.Building, style = BuildingStyle.Shed, rot = rot });
                var pad = At(-12, 58);
                NEW_SITES.Add(new Site { id = "portcraig_quay", name = "Portcraig Quay", kind = SiteKind.Pad, x = pad.x, z = pad.z, radius = 6, deckHeight = quayTop, description = "Helicopter landing spot on the harbour quay, beside the lifeboat station." });
                // boats in the basin and on the moorings
                for (int i = 0; i < 6; i++) { var p = At(30 + (i % 3) * 26, -60 + Math.Floor(i / 3.0) * 30 + R() * 6); PROPS.Add(new Prop { type = PropType.Boat, boat = i == 0 ? BoatKind.Lifeboat : i < 3 ? BoatKind.Trawler : BoatKind.Creel, x = p.x, z = p.z, rot = rot + (R() - 0.5) * 0.3 }); }
                for (int i = 0; i < 4; i++) { var p = At(170 + R() * 120, -200 + i * 70); PROPS.Add(new Prop { type = PropType.Boat, boat = i % 2 == 1 ? BoatKind.Yacht : BoatKind.Creel, x = p.x, z = p.z, rot = R() * 6.28 }); }
                for (int i = 0; i < 5; i++) { var p = At(-12 + R() * 14, -40 + R() * 90); Person(i < 3 ? PersonKind.Fisher : PersonKind.Casual, i == 4 ? PersonPose.WalkA : PersonPose.Stand, p.x, p.z); }
                for (int i = 0; i < 6; i++) { var p = At(-4 + R() * 8, 20 + i * 4); Other(i % 2 == 1 ? PropType.Container : PropType.Pallet, p.x, p.z, rot); }
                var cp = At(-40, 30); Vehicle(VehicleKind.Van, cp.x, cp.z, rot); var cp2 = At(-40, 36); Vehicle(VehicleKind.Pickup, cp2.x, cp2.z, rot);
                var h = At(60, -20);
                POI.Add(new Poi { id = "harbour", name = "Portcraig harbour", x = h.x, z = h.z, kind = PoiKind.Harbour });
            }

            // ---- Ardoch: mountain rescue base at the foot of the glen -------------------------------------
            {
                NEW_SITES.Add(new Site { id = "ardoch_mrt", name = "Ardoch Mountain Rescue", kind = SiteKind.Pad, x = -4150, z = -450, radius = 7, flatten = 75, description = "Mountain rescue team base with a helipad, at the foot of Glen Ardoch." });
                Building(-4192, -492, 24, 14, 7, BuildingStyle.Mrt);
                Vehicle(VehicleKind.Rover, -4120, -490, PI); Vehicle(VehicleKind.Rover, -4113, -490, PI); Vehicle(VehicleKind.Pickup, -4106, -490, PI);
                Person(PersonKind.Mrt, PersonPose.Stand, -4130, -480); Person(PersonKind.Mrt, PersonPose.Stand, -4126, -476); Person(PersonKind.Mrt, PersonPose.Point, -4170, -470);
            }

            // ---- Beinn Ardoch ski centre --------------------------------------------------------------------
            {
                FLAT_AREAS.Add(new FlatArea { x = -6150, z = 1160, r = 95 });
                NEW_SITES.Add(new Site { id = "ardoch_ski", name = "Beinn Ardoch Ski Centre", kind = SiteKind.Pad, x = -6110, z = 1195, radius = 8, description = "Car park landing spot at the ski centre, 1,270 ft up the north side of Beinn Ardoch." });
                Building(-6200, 1118, 28, 14, 8, BuildingStyle.Lodge, 0.2);
                CarPark(-6140, 1150, 0.1, 1, 12, 0.7);
                for (int i = 0; i <= 8; i++) { var t = i / 8.0; SKI_TOW.Add(P(-6215 + (-6385 - -6215) * t, 1095 + (640 - 1095) * t)); }
                for (int i = 0; i < 6; i++) Person(PersonKind.Skier, i < 2 ? PersonPose.WalkA : PersonPose.Stand, -6190 + R() * 40, 1100 + R() * 30);
                POI.Add(new Poi { id = "ski", name = "Beinn Ardoch ski slopes", x = -6320, z = 820, kind = PoiKind.Ski });
            }

            // ---- Kinloch golf links -------------------------------------------------------------------------
            {
                Building(-40, -1350, 26, 13, 6, BuildingStyle.Clubhouse, 0.15);
                CarPark(10, -1335, 0.15, 1, 10, 0.75);
                var holes = new[] { (P(-10, -1420), P(180, -1560)), (P(200, -1600), P(260, -1840)), (P(230, -1880), P(40, -1960)), (P(0, -1930), P(-140, -1760)), (P(-150, -1720), P(-60, -1500)) };
                foreach (var (tee, green) in holes)
                {
                    GOLF_TEES.Add(tee); GOLF_GREENS.Add(green);
                    GOLF_FAIRWAYS.Add(new[] { tee, P((tee.x * 2 + green.x) / 3, (tee.z * 2 + green.z) / 3), P((tee.x + green.x * 2) / 3, (tee.z + green.z * 2) / 3), green });
                    var bx = green.x + (R() - 0.5) * 30; var bz = green.z + (R() - 0.5) * 30; var br = 5 + R() * 4;
                    GOLF_BUNKERS.Add((bx, bz, br));
                    Other(PropType.Flag, green.x, green.z, 0);
                }
                for (int i = 0; i < 4; i++) { var g = GOLF_GREENS[i]; Person(PersonKind.Golfer, i % 2 == 1 ? PersonPose.Stand : PersonPose.WalkA, g.x + 8 + R() * 6, g.z + 6); Person(PersonKind.Golfer, PersonPose.Stand, g.x + 12, g.z + 3); }
                Vehicle(VehicleKind.Buggy, GOLF_GREENS[1].x + 18, GOLF_GREENS[1].z + 4, 0.4);
                Vehicle(VehicleKind.Buggy, GOLF_GREENS[3].x + 16, GOLF_GREENS[3].z, 2.1);
                POI.Add(new Poi { id = "golf", name = "Kinloch golf links", x = 120, z = -1700, kind = PoiKind.Golf });
            }

            // ---- Sandend caravan park -----------------------------------------------------------------------
            {
                for (int i = 0; i < 5; i++)
                    for (int j = 0; j < 4; j++)
                    {
                        if (R() < 0.15) continue;
                        var x = 620 + i * 24 + (R() - 0.5) * 3; double z = 2390 + j * 30;
                        NEW_STRUCTURES.Add(new Structure { x = x, z = z, w = 3.4, d = 9.5, h = 2.9, kind = StructureKind.Caravan, rot = (R() - 0.5) * 0.1 });
                        if (R() < 0.55) { var k = Pick(new[] { VehicleKind.Hatch, VehicleKind.Estate, VehicleKind.Suv }); Vehicle(k, x + 5, z + 2, PI + (R() - 0.5) * 0.2); }
                    }
                for (int i = 0; i < 8; i++) { var x = 610 + R() * 110; var z = 2330 + R() * 40; var rot = R() * 6.28; var s = 0.8 + R() * 0.6; Other(PropType.Tent, x, z, rot, s); }
                for (int i = 0; i < 10; i++) Person(PersonKind.Casual, Pick(new[] { PersonPose.Stand, PersonPose.Sit, PersonPose.Sit, PersonPose.WalkA }), 610 + R() * 120, 2340 + R() * 130);
                Building(600, 2350, 14, 9, 4.5, BuildingStyle.Shop, 0);
                POI.Add(new Poi { id = "camp", name = "Sandend caravan park", x = 680, z = 2440, kind = PoiKind.Camp });
                POI.Add(new Poi { id = "beach", name = "Sandend beach", x = CoastX(2600) - 60, z = 2600, kind = PoiKind.Beach });
                Person(PersonKind.Walker, PersonPose.WalkA, CoastX(2700) - 40, 2700); Other(PropType.Dog, CoastX(2700) - 37, 2703, 1);
                Person(PersonKind.Casual, PersonPose.WalkB, CoastX(-500) - 50, -500); Other(PropType.Dog, CoastX(-500) - 47, -497, 2);
            }

            // ---- Glen Ardoch distillery and Dunardoch Castle ------------------------------------------------
            {
                FLAT_AREAS.Add(new FlatArea { x = -2440, z = 380, r = 95 });
                Building(-2470, 360, 22, 14, 10, BuildingStyle.Distillery, 0.3);
                Building(-2420, 410, 40, 12, 7, BuildingStyle.Warehouse, 0.3);
                Building(-2400, 345, 36, 12, 7, BuildingStyle.Warehouse, 0.3);
                CarPark(-2490, 420, 0.3, 1, 8, 0.7);
                Person(PersonKind.Casual, PersonPose.Stand, -2450, 395); Person(PersonKind.Casual, PersonPose.WalkA, -2455, 398);
                POI.Add(new Poi { id = "distillery", name = "Glen Ardoch distillery", x = -2440, z = 380, kind = PoiKind.Distillery });
                FLAT_AREAS.Add(new FlatArea { x = -2120, z = -630, r = 60 });
                NEW_STRUCTURES.Add(new Structure { x = -2120, z = -630, w = 13, d = 11, h = 19, kind = StructureKind.Castle, rot = 0.25 });
                for (int i = 0; i < 4; i++) Person(PersonKind.Casual, Pick(new[] { PersonPose.Stand, PersonPose.Point, PersonPose.WalkA }), -2095 + R() * 20, -610 + R() * 20);
                CarPark(-2050, -700, 0.5, 1, 6, 0.6);
                POI.Add(new Poi { id = "castle", name = "Dunardoch Castle", x = -2120, z = -630, kind = PoiKind.Castle });
            }

            // ---- Craigmore quarry ---------------------------------------------------------------------------
            {
                FLAT_AREAS.Add(new FlatArea { x = QUARRY.x, z = QUARRY.z, r = QUARRY.r, drop = 16 });
                NEW_SITES.Add(new Site { id = "craigmore_quarry", name = "Craigmore Quarry", kind = SiteKind.Field, x = -1960, z = -2770, radius = 15, description = "Working quarry floor, 45 ft below the surrounding ground. Dust and machinery." });
                Building(-2060, -2880, 18, 12, 11, BuildingStyle.Crusher, 0.4);
                Building(-1930, -2690, 14, 8, 4, BuildingStyle.Office, 0.1);
                Vehicle(VehicleKind.DumpTruck, -2010, -2810, 0.8); Vehicle(VehicleKind.DumpTruck, -2060, -2770, 2.2); Vehicle(VehicleKind.Excavator, -2080, -2840, 1.2);
                Other(PropType.Gravel, -2100, -2760, 0, 1.4); Other(PropType.Gravel, -2110, -2800, 0, 1.1); Other(PropType.Gravel, -1920, -2850, 0, 1.6);
                Person(PersonKind.Worker, PersonPose.Stand, -2000, -2795); Person(PersonKind.Worker, PersonPose.Point, -1990, -2800); Person(PersonKind.Worker, PersonPose.WalkA, -1925, -2700);
                Vehicle(VehicleKind.Pickup, -1915, -2680, 1.6); Vehicle(VehicleKind.Hatch, -1915, -2674, 1.6);
                POI.Add(new Poi { id = "quarry", name = "Craigmore quarry", x = QUARRY.x, z = QUARRY.z, kind = PoiKind.Quarry });
            }

            // ---- Heliport: crew, cars and the fuel bowser driver ------------------------------------------
            {
                CarPark(30, 95, 0, 1, 10, 0.8);
                Person(PersonKind.Hivis, PersonPose.Stand, 120, -95); Person(PersonKind.Hivis, PersonPose.Wave, 60, -30, 0.6); Person(PersonKind.Hivis, PersonPose.Stand, 72, -14);
                Vehicle(VehicleKind.Pickup, 82, -24, 0.2);
            }

            // ---- Ardoch Hill wind farm ----------------------------------------------------------------------
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    var x = -7650 + i * 380 + (R() - 0.5) * 80; var z = -3250 + j * 360 + (R() - 0.5) * 80;
                    TURBINES.Add(new Turbine { x = x, z = z, hub = 72, r = 38, yaw = -PI / 2 + 0.35 });
                    NEW_STRUCTURES.Add(new Structure { x = x, z = z, w = 4.4, d = 4.4, h = 72, kind = StructureKind.Turbine });
                }
            POI.Add(new Poi { id = "windfarm", name = "Ardoch Hill wind farm", x = -7270, z = -2890, kind = PoiKind.Windfarm });

            // ---- The 33 kV line up the glen (the one the utility patrol follows) --------------------------
            {
                var glen = RAW_ROADS.First(r => r.name == "Glen road").pts.Take(7).ToArray();
                double carry = 0;
                for (int i = 0; i < glen.Length - 1; i++)
                {
                    Pt a = glen[i], b = glen[i + 1];
                    double len = Js.Hypot(b.x - a.x, b.z - a.z), dx = (b.x - a.x) / len, dz = (b.z - a.z) / len;
                    for (var d = carry; d < len; d += 90) POWER_POLES.Add(P(a.x + dx * d + dz * 24, a.z + dz * d - dx * 24));
                    carry = 90 - ((len - carry) % 90);
                }
            }

            // ---- Hill walkers, summit and farm -------------------------------------------------------------
            POI.Add(new Poi { id = "summit", name = "Beinn Ardoch summit", x = -6400, z = 560, kind = PoiKind.Summit });
            POI.Add(new Poi { id = "farm", name = "Moss Farm", x = -2950, z = -2250, kind = PoiKind.Farm });
            POI.Add(new Poi { id = "school", name = "Balnagown Academy", x = -1600, z = -4150, kind = PoiKind.School });
            Person(PersonKind.Walker, PersonPose.Stand, -6390, 575); Person(PersonKind.Walker, PersonPose.Sit, -6396, 580); Person(PersonKind.Walker, PersonPose.WalkA, -6200, 900, 0.3); Person(PersonKind.Walker, PersonPose.WalkB, -5600, 1600, 2);
            Vehicle(VehicleKind.Tractor, -2925, -2290, 1.2); Person(PersonKind.Worker, PersonPose.Stand, -2930, -2283);
        }
    }
}
