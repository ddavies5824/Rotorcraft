// Data-driven mission and training-exercise definitions (port of src/game/missions.ts). Adding a mission =
// adding an entry here; the mission engine, briefing and debrief need no code changes.
// The advanced and career missions follow in MissionsAdvanced and MissionsCareer.
using System.Collections.Generic;
using System.Linq;
using Rotorcraft.Sim;

namespace Rotorcraft.Game
{
    /// <summary>Short constructors for objectives, so the mission tables read like the browser game's.</summary>
    public static class Obj
    {
        public static ObjectiveDef EngineStart(string label, string phase, string say = null) => new ObjectiveDef { kind = ObjectiveKind.EngineStart, label = label, phase = phase, say = say };
        public static ObjectiveDef RotorRpm(string label, string phase, double rpm, string say = null) => new ObjectiveDef { kind = ObjectiveKind.RotorRpm, label = label, phase = phase, rpm = rpm, say = say };
        public static ObjectiveDef LiftOff(string label, string phase, double minAglM, string say = null) => new ObjectiveDef { kind = ObjectiveKind.LiftOff, label = label, phase = phase, minAglM = minAglM, say = say };
        public static ObjectiveDef Hover(string label, string phase, string siteId, double radiusM, double minAglM, double maxAglM, double seconds, double? headingTolDeg = null, string say = null) =>
            new ObjectiveDef { kind = ObjectiveKind.Hover, label = label, phase = phase, siteId = siteId, radiusM = radiusM, minAglM = minAglM, maxAglM = maxAglM, seconds = seconds, headingTolDeg = headingTolDeg, say = say };
        public static ObjectiveDef Climb(string label, string phase, string siteId, double aboveFt, string say = null) => new ObjectiveDef { kind = ObjectiveKind.Climb, label = label, phase = phase, siteId = siteId, aboveFt = aboveFt, say = say };
        public static ObjectiveDef WaypointSite(string label, string phase, string siteId, double radiusM, double? altMinFt = null, double? altMaxFt = null, string say = null) =>
            new ObjectiveDef { kind = ObjectiveKind.Waypoint, label = label, phase = phase, siteId = siteId, radiusM = radiusM, altMinFt = altMinFt, altMaxFt = altMaxFt, say = say };
        public static ObjectiveDef WaypointAt(string label, string phase, string name, double x, double z, double radiusM, double? altMinFt = null, double? altMaxFt = null, string say = null) =>
            new ObjectiveDef { kind = ObjectiveKind.Waypoint, label = label, phase = phase, name = name, x = x, z = z, radiusM = radiusM, altMinFt = altMinFt, altMaxFt = altMaxFt, say = say };
        public static ObjectiveDef Land(string label, string phase, string siteId, double maxDistM, string say = null) => new ObjectiveDef { kind = ObjectiveKind.Land, label = label, phase = phase, siteId = siteId, maxDistM = maxDistM, say = say };
        public static ObjectiveDef Shutdown(string label, string phase, string say = null) => new ObjectiveDef { kind = ObjectiveKind.Shutdown, label = label, phase = phase, say = say };

        /// <summary>An objective list from single objectives and groups of them (the TypeScript tables spread helper groups in).</summary>
        public static ObjectiveDef[] Objs(params object[] items)
        {
            var list = new List<ObjectiveDef>();
            foreach (var it in items)
            {
                if (it is ObjectiveDef o) list.Add(o);
                else if (it is IEnumerable<ObjectiveDef> many) list.AddRange(many);
            }
            return list.ToArray();
        }
    }

    public static class Missions
    {
        const string SCHOOL = "Kinloch Rotor School";

        static AssessmentCriteria Pass(double mark, params (ScoreCategory c, double min)[] minimums) =>
            new AssessmentCriteria { passMark = mark, minimums = minimums.ToDictionary(m => m.c, m => m.min) };

        public static readonly List<MissionDef> All = new List<MissionDef>
        {
            new MissionDef
            {
                id = "ex01_controls", title = "Exercise 1 · Effects of Controls", shortTitle = "Effects of Controls", category = MissionCategory.Training, employer = SCHOOL, aircraftId = "kestrel",
                summary = "Start the engine, bring the rotor to flying RPM, lift into a low hover over H1 and set it back down.",
                briefing = new[]
                {
                    "Your instructor, Captain Morag Fraser, will talk you through the start. Switch the anti-collision beacon on, lower the collective fully and close the throttle before starting. Starting with the lever raised or the throttle open is a procedural error.",
                    "The engine starts at idle. Roll the throttle open (R) and the governor will hold the rotor at 100%. The Kestrel lifts off at roughly half lever.",
                    "Once the rotor is at 100%, raise the collective slowly until the skids go light, then keep raising until you are just clear of the ground. Small inputs. The Kestrel has a light rotor and reacts quickly.",
                    "Hold any hover you can manage for ten seconds near the pad, then lower the collective smoothly to land. A soft touchdown matters more than precision today.",
                },
                risks = new[] { "Over-controlling the cyclic near the ground", "Lateral cyclic while light on the skids can roll the aircraft (dynamic rollover)" },
                weatherId = "calm", startSiteId = "kin_h1", startHeadingDeg = 270, defaultFuelFrac = 0.6, minFuelKg = 15, payloadKg = 0, dual = true,
                objectives = new[]
                {
                    Obj.EngineStart("Beacon on, start the engine (collective down, throttle closed)", "Start", "Beacon on with B, collective fully down, throttle closed, area clear. Start the engine when ready."),
                    Obj.RotorRpm("Throttle open: rotor to 100% RPM", "Start", 98, "Good start. Let it warm at idle, then roll the throttle open with R. The governor brings the rotor up to 100 per cent."),
                    Obj.LiftOff("Lift off into a hover", "Hover", 1, "Rotor in the green. Raise the collective slowly until we lift off."),
                    Obj.Hover("Hover near H1 for 10 s", "Hover", "kin_h1", 15, 0.8, 6, 10, say: "Now hold it there. Look ahead, not down, and make tiny corrections."),
                    Obj.Land("Land on or near H1", "Landing", "kin_h1", 20, "Lovely. Lower the collective slowly and put her down."),
                    Obj.Shutdown("Shut down: throttle idle, 15 s cool-down, engine off", "Shutdown", "Collective fully down. Throttle to idle with F, let the engine cool for fifteen seconds, then shut down with E."),
                },
                assessment = Pass(50),
                prerequisites = new Prerequisites(), estMinutes = 4, xp = 50, availableFromMilestone = 1, maxRangeM = 1500, remedialFor = new[] { ScoreCategory.Handling },
            },
            new MissionDef
            {
                id = "ex02_hover", title = "Exercise 2 · Hovering", shortTitle = "Hovering", category = MissionCategory.Training, employer = SCHOOL, aircraftId = "kestrel",
                summary = "Hover taxi to Training Spot A and hold a stable hover inside the painted square, then land on it.",
                briefing = new[]
                {
                    "Take off from H1 and hover-taxi to Training Spot A, 190 m north-west of the main pad. Keep below 15 ft and below a brisk walking pace.",
                    "Over Spot A, hold the hover inside a 5 m circle between 3 and 12 ft for fifteen seconds, keeping your heading within 25° of where you started. A brief wobble outside just pauses the timer; staying out for more than two seconds resets it.",
                    "A light south-westerly is blowing. You will need a little into-wind cyclic to stop the drift.",
                },
                risks = new[] { "Drift with the wind while concentrating on height", "Chasing the aircraft with large cyclic inputs" },
                weatherId = "light_sw", startSiteId = "kin_h1", startHeadingDeg = 300, defaultFuelFrac = 0.6, minFuelKg = 15, payloadKg = 0, dual = true,
                objectives = new[]
                {
                    Obj.EngineStart("Start the engine", "Start"),
                    Obj.RotorRpm("Rotor to 100% RPM", "Start", 98),
                    Obj.LiftOff("Lift into the hover", "Departure", 1),
                    Obj.WaypointSite("Hover-taxi to Spot A", "Hover taxi", "kin_spot_a", 8, altMaxFt: 140, say: "Spot A is ahead and slightly left. Keep it slow."),
                    Obj.Hover("Hold 15 s inside 5 m at 3–12 ft", "Hover", "kin_spot_a", 5, 0.8, 3.7, 15, 25, "Settle over the spot. Fifteen seconds, steady as you can."),
                    Obj.Land("Land on Spot A", "Landing", "kin_spot_a", 6, "Good. Land on the spot."),
                },
                assessment = Pass(60, (ScoreCategory.Hover, 40)),
                prerequisites = new Prerequisites { exercises = new[] { "ex01_controls" } }, estMinutes = 5, xp = 70, availableFromMilestone = 2, maxRangeM = 1500, remedialFor = new[] { ScoreCategory.Hover },
            },
            new MissionDef
            {
                id = "ex03_takeoff_landing", title = "Exercise 3 · Take-off and Landing", shortTitle = "Take-off & Landing", category = MissionCategory.Training, employer = SCHOOL, aircraftId = "kestrel",
                summary = "Transition from the hover into forward flight, fly to Spot B, land precisely, then return to H1.",
                briefing = new[]
                {
                    "From the hover over H1, lower the nose gently and accelerate. Through about 15 kt you will feel a shudder and the aircraft will want to climb: that is translational lift. Let it climb to around 300 ft.",
                    "Spot B is 300 m to the south-west. Plan a shallow approach: steadily reduce speed and height together so you arrive in a hover over the spot rather than diving at it.",
                    "Land within 6 m of the spot centre, lift again and return to H1 for a final landing.",
                },
                risks = new[] { "High rate of descent at low speed close to the ground", "Arriving fast and flaring hard over the spot" },
                weatherId = "light_sw", startSiteId = "kin_h1", startHeadingDeg = 230, defaultFuelFrac = 0.6, minFuelKg = 15, payloadKg = 0, dual = true,
                objectives = new[]
                {
                    Obj.EngineStart("Start the engine", "Start"),
                    Obj.RotorRpm("Rotor to 100% RPM", "Start", 98),
                    Obj.LiftOff("Lift into the hover", "Departure", 1),
                    Obj.Climb("Transition and climb to 300 ft above the field", "Departure", "kin_h1", 300, "Nose forward gently. Feel the translational lift and climb away."),
                    Obj.Land("Approach and land on Spot B", "Approach", "kin_spot_b", 6, "Spot B is south-west. Set up a shallow approach."),
                    Obj.LiftOff("Lift off from Spot B", "Return", 1),
                    Obj.Land("Return and land on H1", "Landing", "kin_h1", 8, "Nicely done. Bring us back to H1."),
                },
                assessment = Pass(60, (ScoreCategory.Landing, 50)),
                prerequisites = new Prerequisites { exercises = new[] { "ex02_hover" } }, estMinutes = 7, xp = 90, availableFromMilestone = 2, maxRangeM = 3000, remedialFor = new[] { ScoreCategory.Landing },
            },
            new MissionDef
            {
                id = "ex04_circuits", title = "Exercise 4 · Circuits", shortTitle = "Circuits", category = MissionCategory.Training, employer = SCHOOL, aircraftId = "kestrel",
                summary = "Fly a left-hand circuit at 500 ft above the heliport, joining each leg at its marker, and land on H1.",
                briefing = new[]
                {
                    "Circuit height is 500 ft above the field (620 ft on the altimeter with Kinloch QNH set). Depart west, turn left onto crosswind at the first marker, then downwind, base and final.",
                    "Fly each leg accurately: the markers are 200 m wide. Hold 550 to 700 ft on the downwind leg and aim for 60 kt.",
                    "On final, set up a constant-angle approach to H1 and land.",
                },
                risks = new[] { "Losing height in turns", "Fast, steep final approach" },
                weatherId = "light_sw", startSiteId = "kin_h1", startHeadingDeg = 270, defaultFuelFrac = 0.65, minFuelKg = 15, payloadKg = 0, dual = true,
                objectives = new[]
                {
                    Obj.EngineStart("Start the engine", "Start"),
                    Obj.RotorRpm("Rotor to 100% RPM", "Start", 98),
                    Obj.LiftOff("Lift into the hover", "Departure", 1),
                    Obj.Climb("Climb to 500 ft above the field", "Upwind", "kin_h1", 450),
                    Obj.WaypointAt("Crosswind marker", "Crosswind", "Crosswind", -1100, 300, 200, 520, 760, "Turn left onto crosswind."),
                    Obj.WaypointAt("Downwind marker", "Downwind", "Downwind", 0, 1150, 200, 550, 700, "Downwind. Check height and speed, 620 feet, 60 knots."),
                    Obj.WaypointAt("Base marker", "Base", "Base", 850, 600, 200, 450, 720, "Turn base and start your descent."),
                    Obj.Land("Final approach, land on H1", "Final", "kin_h1", 8, "Final for H1. Constant angle, check your rate of descent."),
                },
                assessment = Pass(60, (ScoreCategory.Navigation, 50), (ScoreCategory.Landing, 50)),
                prerequisites = new Prerequisites { exercises = new[] { "ex03_takeoff_landing" } }, estMinutes = 8, xp = 110, availableFromMilestone = 2, maxRangeM = 4000, remedialFor = new[] { ScoreCategory.Navigation, ScoreCategory.Landing },
            },
            new MissionDef
            {
                id = "ex06_first_solo", title = "Exercise 5 · First Solo", shortTitle = "First Solo", category = MissionCategory.Training, employer = SCHOOL, aircraftId = "kestrel",
                summary = "Your instructor steps out. Fly one circuit on your own and land on H1. The aircraft is lighter and climbs better without her.",
                briefing = new[]
                {
                    "Captain Fraser has signed your logbook for solo. You fly the same left-hand circuit as Exercise 4, with no one beside you.",
                    "You are about 80 kg lighter: the aircraft lifts off with less collective and climbs more readily. Expect it, and do not let it surprise you in the hover.",
                    "Land on H1. Kinloch Tower will be listening.",
                },
                risks = new[] { "Different hover power with the lighter aircraft", "Rushing the approach" },
                weatherId = "calm", startSiteId = "kin_h1", startHeadingDeg = 270, defaultFuelFrac = 0.6, minFuelKg = 15, payloadKg = -80, dual = false,
                objectives = new[]
                {
                    Obj.EngineStart("Start the engine", "Start"),
                    Obj.RotorRpm("Rotor to 100% RPM", "Start", 98),
                    Obj.LiftOff("Lift into the hover", "Departure", 1, "Kinloch Tower: student solo, cleared for departure. Good luck."),
                    Obj.Climb("Climb to 500 ft above the field", "Upwind", "kin_h1", 450),
                    Obj.WaypointAt("Crosswind marker", "Crosswind", "Crosswind", -1100, 300, 200, 520, 760),
                    Obj.WaypointAt("Downwind marker", "Downwind", "Downwind", 0, 1150, 200, 550, 700),
                    Obj.WaypointAt("Base marker", "Base", "Base", 850, 600, 200, 450, 720),
                    Obj.Land("Land on H1", "Final", "kin_h1", 8, "Kinloch Tower: final approach, land at your discretion."),
                },
                assessment = new AssessmentCriteria { passMark = 65, minimums = new Dictionary<ScoreCategory, double> { [ScoreCategory.Landing] = 55 }, maxHardLandings = 0 },
                prerequisites = new Prerequisites { exercises = new[] { "ex04_circuits" } }, estMinutes = 6, xp = 150, availableFromMilestone = 3, maxRangeM = 4000, remedialFor = new[] { ScoreCategory.Landing, ScoreCategory.Navigation },
            },
            new MissionDef
            {
                id = "ex07_navigation", title = "Exercise 6 · Local Navigation", shortTitle = "Navigation", category = MissionCategory.Training, employer = SCHOOL, aircraftId = "kestrel",
                summary = "A triangle: north to Tarbat Light, land at Moss Farm, then home to Kinloch.",
                briefing = new[]
                {
                    "Leg 1: Kinloch to Tarbat Light, 014°, 2.0 nm. Overfly the lighthouse between 800 and 1,500 ft.",
                    "Leg 2: Tarbat Light to Moss Farm, about 245°, 2.0 nm. The field sits at 364 ft with trees on its northern edge. Recce the field from height, then land inside the marked area.",
                    "Leg 3: Moss Farm to Kinloch H1, 128°, 2.0 nm. Plan your fuel: land back at Kinloch with at least 15 kg.",
                },
                risks = new[] { "Rising ground inland: watch your height above terrain", "Trees on the approach to Moss Farm" },
                weatherId = "light_sw", startSiteId = "kin_h1", startHeadingDeg = 15, defaultFuelFrac = 0.85, minFuelKg = 15, payloadKg = 0, dual = true,
                objectives = new[]
                {
                    Obj.EngineStart("Start the engine", "Start"),
                    Obj.RotorRpm("Rotor to 100% RPM", "Start", 98),
                    Obj.LiftOff("Lift into the hover", "Departure", 1),
                    Obj.WaypointSite("Overfly Tarbat Light (800–1,500 ft)", "Leg 1", "tarbat_light", 300, 800, 1500, "Kinloch Tower: depart north, report Tarbat Light."),
                    Obj.Land("Land at Moss Farm", "Leg 2", "moss_farm", 25, "Tarbat Light abeam. Turn left for Moss Farm, about two-four-five."),
                    Obj.LiftOff("Depart Moss Farm", "Leg 3", 2),
                    Obj.Land("Return and land on H1", "Leg 3", "kin_h1", 10, "Head home to Kinloch, one-two-eight."),
                },
                assessment = Pass(65, (ScoreCategory.Navigation, 55), (ScoreCategory.Fuel, 50)),
                prerequisites = new Prerequisites { exercises = new[] { "ex06_first_solo" }, theory = new[] { "gs2_navigation" } }, estMinutes = 12, xp = 170, availableFromMilestone = 3, maxRangeM = 9000, remedialFor = new[] { ScoreCategory.Navigation, ScoreCategory.Fuel },
            },
            new MissionDef
            {
                id = "st_ppl", title = "PPL(H) Skill Test", shortTitle = "PPL(H) Skill Test", category = MissionCategory.Test, employer = "Examiner: Mr Alasdair Grant", aircraftId = "kestrel",
                summary = "The practical test for your private licence. Precision hover, navigation to Glen Ardoch Bridge, a field landing at Moss Farm and return to H1.",
                briefing = new[]
                {
                    "The examiner will assess everything you have learned. The wind is a fresh northerly across the heliport: expect drift in the hover and a crosswind on approach.",
                    "Hover over Spot A within 5 m for 12 seconds, between 3 and 13 ft, heading within 20°. Then route to Glen Ardoch Bridge (258°, 2.4 nm) between 1,200 and 2,000 ft, land at Moss Farm, and return to H1.",
                    "Pass standard: overall 70%, no hard landings, no torque or rotor-speed exceedances, all items completed. A fail is not the end: you will be set remedial training and can re-sit.",
                },
                risks = new[] { "Crosswind drift in the hover and on approach", "Higher ground towards the glen" },
                weatherId = "crosswind_n", startSiteId = "kin_h1", startHeadingDeg = 300, defaultFuelFrac = 0.9, minFuelKg = 15, payloadKg = 0, dual = true,
                objectives = new[]
                {
                    Obj.EngineStart("Start the engine", "Start"),
                    Obj.RotorRpm("Rotor to 100% RPM", "Start", 98),
                    Obj.LiftOff("Lift into the hover", "Hover", 1),
                    Obj.Hover("Hover over Spot A: 5 m, 12 s", "Hover", "kin_spot_a", 5, 0.9, 4, 12, 20, "Examiner: hover over Spot A, please. Twelve seconds."),
                    Obj.WaypointSite("Glen Ardoch Bridge (1,200–2,000 ft)", "Navigation", "glen_bridge", 300, 1200, 2000, "Examiner: route to Glen Ardoch Bridge, report overhead."),
                    Obj.Land("Field landing at Moss Farm", "Field landing", "moss_farm", 20, "Examiner: Moss Farm for a field landing, your approach."),
                    Obj.LiftOff("Depart Moss Farm", "Return", 2),
                    Obj.Land("Return and land on H1", "Return", "kin_h1", 8, "Examiner: return to Kinloch, land on H1."),
                    Obj.Shutdown("Cool-down and shut down", "Shutdown", "Examiner: shut down when ready."),
                },
                assessment = new AssessmentCriteria { passMark = 70, minimums = new Dictionary<ScoreCategory, double> { [ScoreCategory.Hover] = 40, [ScoreCategory.Landing] = 55, [ScoreCategory.Navigation] = 55 }, noExceedances = true, maxHardLandings = 0 },
                prerequisites = new Prerequisites { exercises = new[] { "ex01_controls", "ex02_hover", "ex03_takeoff_landing", "ex04_circuits", "ex06_first_solo", "ex07_navigation" }, exams = new[] { "exam_ppl" }, minHours = 0.5 },
                estMinutes = 14, xp = 400, availableFromMilestone = 3, maxRangeM = 9000,
            },
            new MissionDef
            {
                id = "pv_coastal_survey", title = "Hour Building · Coastal Survey", shortTitle = "Coastal Survey", category = MissionCategory.Private, employer = "Private flight", aircraftId = "kestrel",
                summary = "Hire the Kestrel and build hours: a coastal route north past Tarbat Light to Balnagown and back over the village.",
                briefing = new[]
                {
                    "Your first flight as a licensed private pilot. Fly Kinloch, Tarbat Light (overfly, 600–1,200 ft), Balnagown Hospital (overfly only: you may not land there privately), then return to H1.",
                    "Hours flown count towards the CPL(H) experience requirement.",
                },
                risks = new[] { "Sea fog can roll in along this coast", "Stay clear of the hospital helipad" },
                weatherId = "breezy_w", timeOfDay = TimeOfDay.Dusk, timeOptions = new[] { TimeOfDay.Day, TimeOfDay.Dusk }, startSiteId = "kin_h1", startHeadingDeg = 15, defaultFuelFrac = 0.9, minFuelKg = 15, payloadKg = -80, dual = false,
                objectives = new[]
                {
                    Obj.EngineStart("Start the engine", "Start"),
                    Obj.RotorRpm("Rotor to 100% RPM", "Start", 98),
                    Obj.LiftOff("Lift into the hover", "Departure", 1),
                    Obj.WaypointSite("Overfly Tarbat Light", "Coast", "tarbat_light", 350, 600, 1200),
                    Obj.WaypointSite("Overfly Balnagown Hospital", "Town", "balnagown", 400, 700, 1500),
                    Obj.WaypointAt("Overfly Kinloch village", "Return", "Kinloch village", -700, -900, 350, 600, 1300),
                    Obj.Land("Land on H1", "Landing", "kin_h1", 10),
                },
                assessment = Pass(50),
                prerequisites = new Prerequisites { quals = new[] { "ppl_h" } }, estMinutes = 12, xp = 120, availableFromMilestone = 3, maxRangeM = 10000,
            },
            new MissionDef
            {
                id = "nt01_night_circuits", title = "Night Rating · Night Familiarisation", shortTitle = "Night Familiarisation", category = MissionCategory.Training, employer = SCHOOL, aircraftId = "kestrel",
                summary = "Your first flight after dark, with an instructor: lights, a night circuit and an approach to the floodlit pad.",
                briefing = new[]
                {
                    "Night flying begins with the lights. Before start: beacon (B) and position lights (N) on. Strobes (O) are for the air; turn them off on the ground so you do not dazzle the ground crew.",
                    "H1 is marked by green perimeter lights inside a white ring, and the apron is floodlit. Spot B is marked by four yellow lights. The taxiway has blue edge lights.",
                    "Fly a left-hand circuit at 500 ft above the field. On base, switch the landing light on (L). Judge your approach by the shape of the pad lights, not by the ground, which you cannot see well. Then land on H1 and shut down.",
                    "This exercise counts towards the night rating, which becomes available in a later milestone.",
                },
                risks = new[] { "Loss of visual references over dark ground", "Misjudging height on a night approach: use the altimeter and VSI", "Spatial disorientation in turns" },
                weatherId = "calm", timeOfDay = TimeOfDay.Night, timeOptions = new[] { TimeOfDay.Night }, startSiteId = "kin_h1", startHeadingDeg = 270, defaultFuelFrac = 0.7, minFuelKg = 15, payloadKg = 0, dual = true,
                objectives = new[]
                {
                    Obj.EngineStart("Beacon and position lights on, start the engine", "Start", "Beacon and position lights on before start, please."),
                    Obj.RotorRpm("Throttle open: rotor to 100% RPM", "Start", 98),
                    Obj.LiftOff("Lift into the hover", "Departure", 1, "Strobes on once we are airborne. Lift into the hover."),
                    Obj.Climb("Climb to 500 ft above the field", "Upwind", "kin_h1", 450),
                    Obj.WaypointAt("Crosswind marker", "Crosswind", "Crosswind", -1100, 300, 220, 500, 780, "Look at the village lights to the north-west. A good night reference."),
                    Obj.WaypointAt("Downwind marker", "Downwind", "Downwind", 0, 1150, 220, 540, 720),
                    Obj.WaypointAt("Base marker", "Base", "Base", 850, 600, 220, 420, 740, "Landing light on. Pick out the green pad lights."),
                    Obj.Land("Night approach and landing on H1", "Final", "kin_h1", 10, "Steady approach. Check your rate of descent on the VSI."),
                    Obj.Shutdown("Cool-down and shut down", "Shutdown", "Well flown. Throttle idle, cool down, shut down, and strobes off."),
                },
                assessment = Pass(60, (ScoreCategory.Landing, 50)),
                prerequisites = new Prerequisites { quals = new[] { "ppl_h" } }, estMinutes = 9, xp = 160, availableFromMilestone = 3, maxRangeM = 4000, remedialFor = new ScoreCategory[0],
            },
            new MissionDef
            {
                id = "pr_free_flight", title = "Practice · Free Flight", shortTitle = "Free Flight", category = MissionCategory.Practice, employer = "Kinloch Rotor School", aircraftId = "kestrel",
                summary = "Unassessed practice with an instructor on board. Choose the weather and time of day, fly where you like inside the local area, and land back on H1.",
                briefing = new[]
                {
                    "Use this to practise anything: hovering, spot turns, approaches, autorotations, night flying. Your instructor will step in only for safety calls.",
                    "The flight is logged as dual practice. It does not count as a pass for any exercise.",
                },
                risks = new[] { "Stay within 5 km of Kinloch" },
                weatherId = "calm", timeOfDay = TimeOfDay.Day, timeOptions = new[] { TimeOfDay.Day, TimeOfDay.Dusk, TimeOfDay.Night }, weatherOptions = new[] { "calm", "light_sw", "breezy_w", "crosswind_n", "showers", "haar" },
                startSiteId = "kin_h1", startHeadingDeg = 270, defaultFuelFrac = 0.8, minFuelKg = 15, payloadKg = 0, dual = true,
                objectives = new[]
                {
                    Obj.EngineStart("Start the engine", "Start"),
                    Obj.RotorRpm("Throttle open: rotor to 100% RPM", "Start", 98),
                    Obj.LiftOff("Lift off", "Free flight", 1),
                    Obj.Land("When finished, land on H1", "Free flight", "kin_h1", 25),
                },
                assessment = Pass(0),
                prerequisites = new Prerequisites(), estMinutes = 15, xp = 20, availableFromMilestone = 3, maxRangeM = 5000,
            },
        }.Concat(MissionsAdvanced.All).Concat(MissionsCareer.All).ToList();

        public static MissionDef ById(string id) => All.FirstOrDefault(m => m.id == id);
    }
}
