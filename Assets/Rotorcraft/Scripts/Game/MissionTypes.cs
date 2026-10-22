// Shared game-layer types: mission and exercise definitions, results and records (port of src/game/types.ts).
using System.Collections.Generic;
using Rotorcraft.Sim;

namespace Rotorcraft.Game
{
    public enum ObjectiveKind { EngineStart, RotorRpm, LiftOff, Hover, Climb, Waypoint, Land, Autorotation, PowerRecovery, Shutdown, Radio, Decision, Load, Search, Winch, FailEngine, Failure, Hold, Ap }
    public enum ObjectiveStatus { Pending, Active, Complete, Failed }
    public enum DecisionEffect { Continue, Return, Divert }

    public sealed class DecisionOption
    {
        public string id, label;
        /// <summary>Whether this is a sound decision in the circumstances (scored under Decisions).</summary>
        public bool good;
        public DecisionEffect effect;
        public string siteId;   // for divert
        public string reply;    // what the crew/ATC/examiner says next
    }

    /// <summary>One item of a mission. Which fields matter depends on the kind (as in the TypeScript union).</summary>
    public sealed class ObjectiveDef
    {
        public ObjectiveKind kind;
        public string label, phase, say;
        /// <summary>Minutes after the start of the mission by which this item should be done (late = timeliness penalty).</summary>
        public double? deadlineMin;
        /// <summary>Simulated instrument flight: the outside view is blocked while this objective is active.</summary>
        public bool hood;
        /// <summary>Freeplay: the callout this item belongs to.</summary>
        public string job;
        // rotorRpm
        public double rpm;
        // liftOff, hover
        public double minAglM;
        // hover, climb, waypoint, land, load
        public string siteId;
        // hover, waypoint, load (optional for load)
        public double? radiusM;
        // hover
        public double maxAglM, seconds;
        public double? headingTolDeg;
        // climb
        public double aboveFt;
        // waypoint, land, load
        public double? x, z;
        public string name;
        public double? altMinFt, altMaxFt;
        // land
        public string nearTarget;
        public double maxDistM;
        public double? maxSlopeDeg;
        public bool deliver;
        // autorotation
        public double untilAglFt, rpmMin, rpmMax, minIasKt;
        // powerRecovery
        public double minAglFt;
        // radio
        public string call, reply, from;
        // decision
        public string prompt;
        public DecisionOption[] options;
        // load
        public double payloadKg;
        public string what;
        public bool pickup;
        // search, winch
        public string areaId, targetId;
        public double? hintAfterMin;
        // failure
        public SystemFailure system;
        // hold
        public double altFt, altTolFt, headingDeg, hdgTolDeg;
        // ap
        public APMode mode;

        public ObjectiveDef Clone() => (ObjectiveDef)MemberwiseClone();
    }

    public enum ScoreCategory { Landing, Hover, Navigation, Handling, Procedures, Fuel, Safety, Search, Rescue, Decisions, Instrument, Timeliness }
    public enum MissionCategory { Training, Test, Private, Practice, Sar, Offshore, Utility, Medical, Freeplay }
    /// <summary>Career pathways: used to order recommendations and hide finished training.</summary>
    public enum Pathway { None, Ppl, Cpl, Night, Ir, Ns175, S9, Offshore, Sar, Winch, Tc }

    public sealed class AssessmentCriteria
    {
        public double passMark;                   // overall %
        public Dictionary<ScoreCategory, double> minimums = new Dictionary<ScoreCategory, double>();
        public bool noExceedances;                // any torque/rpm/Vne exceedance is a fail
        public int? maxHardLandings;
    }

    public sealed class Prerequisites
    {
        public string[] exercises = new string[0], quals = new string[0], theory = new string[0], exams = new string[0];
        public double? minHours;
    }

    public sealed class WeatherChange { public double min; public string id, report; }
    public sealed class ManifestLeg { public string leg; public int pax; public double kg; }
    public sealed class Casualty { public string name, condition; public double goldenMin; }

    public sealed class MissionDef
    {
        public string id, title, shortTitle;
        public MissionCategory category;
        public string employer, aircraftId, summary;
        public string[] briefing = new string[0];   // paragraphs
        public string[] risks = new string[0];
        public string weatherId;
        /// <summary>Weather that changes during the flight: preset ids by minute, blended over about 90 s.</summary>
        public WeatherChange[] weatherTimeline;
        /// <summary>Default time of day, and which times the pilot may choose in the briefing.</summary>
        public TimeOfDay timeOfDay = TimeOfDay.Day;
        public TimeOfDay[] timeOptions;
        /// <summary>Practice flights let the pilot pick the weather.</summary>
        public string[] weatherOptions;
        public string startSiteId;
        /// <summary>Where a "return to base" decision sends the aircraft (defaults to the start site).</summary>
        public string baseSiteId;
        public double startHeadingDeg, defaultFuelFrac;
        public double minFuelKg;                    // planned minimum landing fuel (reserve)
        public double payloadKg;
        /// <summary>Passenger and freight manifest shown in the briefing (offshore).</summary>
        public ManifestLeg[] manifest;
        public bool dual;                           // instructor/examiner/training captain on board
        public ObjectiveDef[] objectives = new ObjectiveDef[0];
        public AssessmentCriteria assessment = new AssessmentCriteria();
        public Prerequisites prerequisites = new Prerequisites();
        public double estMinutes;
        public int xp;
        /// <summary>Development milestone that makes this playable.</summary>
        public int availableFromMilestone;
        public double? maxRangeM;
        public ScoreCategory[] remedialFor;         // categories this exercise trains (used for remedial suggestions)
        /// <summary>Exercises to repeat after failing this test (overrides the automatic choice).</summary>
        public string[] remedial;
        public Pathway pathway;
        public SearchAreaDef[] searchAreas;
        public TargetDef[] targets;
        public Casualty casualty;
        /// <summary>Callsign used on the radio.</summary>
        public string callsign;
        /// <summary>Operator the flight is for (defaults from the category and pathway).</summary>
        public string employerId;
        /// <summary>Contract fee in pounds for a fully successful flight (operational work only).</summary>
        public double? pay;
        /// <summary>Minimum standing with the employer before it offers this contract (0..100).</summary>
        public double? minStanding;
        /// <summary>Passengers carried (for the logbook).</summary>
        public int? passengers;

        public MissionDef Clone() => (MissionDef)MemberwiseClone();
    }

    public sealed class TouchdownRecord
    {
        public double t, vsMs, gsMs, roll, pitch;
        public double? distM;
        public string siteId;
        public TouchdownSeverity severity;
        public int score;
    }

    public sealed class ObjectiveResult { public string label, phase, detail; public ObjectiveStatus status; }
    public sealed class DecisionRecord { public string prompt, chosen; public bool good; }

    public enum FlightOutcome { Passed, Completed, Failed, Aborted, Crashed }
    public enum FlightEnd { Complete, Aborted, Crash }

    public sealed class FlightResult
    {
        public string missionId, missionTitle, aircraftId;
        public FlightOutcome outcome;
        public bool pass;
        public long startedAt;
        public double flightSeconds;               // airborne time
        public double blockSeconds;                // engine start to end
        public List<ObjectiveResult> objectives;
        public Dictionary<ScoreCategory, int> scores;
        public int overall;
        public List<string> failedCriteria;
        public List<TouchdownRecord> landings;
        public TouchdownRecord bestLanding;
        public List<string> exceedances, warnings, recommendations;
        public double fuelUsedKg, fuelRemainingKg, damage, maxAltFt, distanceKm;
        public string crashReason;
        public bool checklistComplete;
        public int xp;
        public TimeOfDay timeOfDay;
        public string weatherId;
        public List<DecisionRecord> decisions;
        /// <summary>Search and rescue summary lines for the debrief.</summary>
        public List<string> rescue;
    }
}
