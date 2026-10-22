// Data-driven aircraft definitions (port of src/sim/aircraft.ts). All aircraft are GENERIC simulated types
// inspired by broad classes of real helicopters; they are not licensed reproductions of any manufacturer's product.
using System;
using System.Collections.Generic;

namespace Rotorcraft.Sim
{
    public enum AircraftRole { Training, Offshore, Sar, Hems }
    public enum LandingGear { Skids, Wheels }

    public sealed class Livery
    {
        public double[] body, stripe, tail;
        public Livery(double[] body, double[] stripe, double[] tail) { this.body = body; this.stripe = stripe; this.tail = tail; }
    }

    /// <summary>A paint scheme the player can choose.</summary>
    public sealed class Skin
    {
        public string id, name, callsign;
        public Livery livery;
    }

    public struct DragArea { public double f, s, v; }

    public sealed class Equipment
    {
        public bool radalt, hoist, flir, searchlight, floats, autopilot;
        /// <summary>Stretcher fit and paramedic aboard (set by the loadout).</summary>
        public bool medical;
        public Equipment Clone() => (Equipment)MemberwiseClone();
    }

    public sealed class AircraftConfig
    {
        public string id, name, shortName, description;
        public AircraftRole role;
        public string realWorldClass;     // the broad real-world class this generic type is loosely modelled on
        public int engines;               // 1 or 2
        // masses (kg)
        public double emptyMass, maxGross;
        public double fuelCapacity;       // kg
        public double crewMass;           // pilot (+ crew) mass included by default
        public double maxPayload;         // passengers/cargo/equipment kg
        // rotor & power
        public double rotorDiameter;      // m
        public double hubHeight;          // rotor hub above skid/wheel contact, m
        public double thrustMaxFactor;    // full-collective thrust as a multiple of maxGross weight (100% RPM, ISA SL)
        public double ratedPowerKW;       // total takeoff power at ISA sea level (all engines)
        public double rotorEnergyTime;    // rotor kinetic energy at 100% / rated power (s); higher = more rotor inertia
        public double profilePowerFrac;   // profile power as a fraction of rated power at 100% RPM, hover
        public double fuelBurnKgH;        // at rated power
        public double idleBurnKgH;
        public double startTime;          // s, engine start sequence to ground idle
        public double spinUpRate;         // % RPM per second when governor brings rotor to 100%
        // handling
        public double pitchRate;          // rad/s per full cyclic (rate command)
        public double rollRate;
        public double yawRate;            // rad/s per full pedal
        public double controlTau;         // s, first-order rate response time constant
        public double torqueYaw;          // rad/s of yaw induced per 100% torque (pedal must counter)
        public double weathervane;        // yaw rate per m/s sideslip velocity
        public double blowback;           // pitch rate (rad/s) per m/s forward airspeed (disc flap-back)
        public double heaveDamping;       // thrust loss per (m/s climb) as fraction of mass (1/s)
        public DragArea dragArea;         // equivalent flat-plate areas m^2
        public double gustSensitivity;    // multiplier on turbulence-induced rates
        public bool hasAFCS;              // automatic flight control system (stabilisation, heading hold)
        // limits
        public double vneKt;
        public double torqueMcp;          // % max continuous
        public double torqueMax;          // % transient limit
        public double rpmMin;             // % low rotor RPM warning
        public double rpmMax;             // % overspeed
        public LandingGear landingGear;
        public double maxTouchdownMs;     // max vertical speed for a normal landing before damage, m/s
        public Equipment equipment;
        public double operatingCostPerHour; // GBP, for career economy
        public string[] requiredQualifications;  // qualification ids required to fly it as pilot-in-command in the career
        public string[] trainingQualifications;  // quals sufficient to fly it in dual training (with instructor)
        public Livery livery;
        /// <summary>Other paint schemes (the first is the default livery above).</summary>
        public Skin[] skins;
        /// <summary>Radio callsign that goes with the chosen skin.</summary>
        public string callsign;

        public AircraftConfig Clone() => (AircraftConfig)MemberwiseClone();
    }

    public static class Aircraft
    {
        static double[] C(double r, double g, double b) => new[] { r, g, b };

        public static readonly IReadOnlyDictionary<string, AircraftConfig> All = new Dictionary<string, AircraftConfig>
        {
            ["kestrel"] = new AircraftConfig
            {
                id = "kestrel",
                name = "Kestrel K2",
                shortName = "K2",
                role = AircraftRole.Training,
                description = "Two-seat piston trainer. Light, responsive and inexpensive, with a low-inertia rotor that rewards smooth collective handling.",
                realWorldClass = "Light two-seat piston training helicopter",
                engines = 1,
                emptyMass = 410,
                maxGross = 640,
                fuelCapacity = 75,
                crewMass = 160, // student + instructor
                maxPayload = 40,
                rotorDiameter = 7.7,
                hubHeight = 2.6,
                thrustMaxFactor = 1.5,
                ratedPowerKW = 92,
                rotorEnergyTime = 2.2,
                profilePowerFrac = 0.17,
                fuelBurnKgH = 34,
                idleBurnKgH = 8,
                startTime = 4,
                spinUpRate = 9,
                pitchRate = 0.9,
                rollRate = 1.4,
                yawRate = 1.3,
                controlTau = 0.25,
                torqueYaw = 0.55,
                weathervane = 0.035,
                blowback = 0.006,
                heaveDamping = 0.32,
                dragArea = new DragArea { f = 0.75, s = 3.2, v = 4.5 },
                gustSensitivity = 1.0,
                hasAFCS = false,
                vneKt = 102,
                torqueMcp = 100,
                torqueMax = 110,
                rpmMin = 95,
                rpmMax = 107,
                landingGear = LandingGear.Skids,
                maxTouchdownMs = 1.8,
                equipment = new Equipment(),
                operatingCostPerHour = 260,
                requiredQualifications = new[] { "ppl_h" },
                trainingQualifications = new string[0],
                livery = new Livery(C(0.93, 0.93, 0.9), C(0.85, 0.36, 0.12), C(0.12, 0.2, 0.3)),
            },
            ["northstar"] = new AircraftConfig
            {
                id = "northstar",
                name = "Northstar 175",
                shortName = "NS175",
                role = AircraftRole.Offshore,
                description = "Twin-turbine medium helicopter configured for offshore crew change: 16 seats, emergency floats, large fuel tanks and a four-axis autopilot.",
                realWorldClass = "Medium twin-turbine offshore transport helicopter",
                engines = 2,
                emptyMass = 4900,
                maxGross = 7800,
                fuelCapacity = 1900,
                crewMass = 180,
                maxPayload = 1700,
                rotorDiameter = 14.8,
                hubHeight = 4.2,
                thrustMaxFactor = 1.4,
                ratedPowerKW = 2100,
                rotorEnergyTime = 4.5,
                profilePowerFrac = 0.13,
                fuelBurnKgH = 560,
                idleBurnKgH = 110,
                startTime = 12,
                spinUpRate = 6,
                pitchRate = 0.55,
                rollRate = 0.8,
                yawRate = 0.7,
                controlTau = 0.4,
                torqueYaw = 0.25,
                weathervane = 0.05,
                blowback = 0.004,
                heaveDamping = 0.26,
                dragArea = new DragArea { f = 2.0, s = 9, v = 14 },
                gustSensitivity = 0.6,
                hasAFCS = true,
                vneKt = 150,
                torqueMcp = 100,
                torqueMax = 110,
                rpmMin = 95,
                rpmMax = 106,
                landingGear = LandingGear.Wheels,
                maxTouchdownMs = 2.0,
                equipment = new Equipment { radalt = true, floats = true, autopilot = true },
                operatingCostPerHour = 4800,
                requiredQualifications = new[] { "cpl_h", "type_northstar" },
                trainingQualifications = new[] { "cpl_h" },
                livery = new Livery(C(0.9, 0.92, 0.94), C(0.1, 0.32, 0.55), C(0.1, 0.32, 0.55)),
            },
            ["guardian"] = new AircraftConfig
            {
                id = "guardian",
                name = "Guardian S9",
                shortName = "S9",
                role = AircraftRole.Sar,
                description = "Twin-turbine search and rescue helicopter with rescue winch, FLIR turret, searchlight, night-vision cockpit and auto-hover capable flight director.",
                realWorldClass = "Medium twin-turbine search and rescue helicopter",
                engines = 2,
                emptyMass = 5600,
                maxGross = 8500,
                fuelCapacity = 2300,
                crewMass = 360, // two pilots, winch operator, winchman
                maxPayload = 900,
                rotorDiameter = 15.6,
                hubHeight = 4.4,
                thrustMaxFactor = 1.42,
                ratedPowerKW = 2300,
                rotorEnergyTime = 4.8,
                profilePowerFrac = 0.13,
                fuelBurnKgH = 620,
                idleBurnKgH = 120,
                startTime = 12,
                spinUpRate = 6,
                pitchRate = 0.55,
                rollRate = 0.8,
                yawRate = 0.75,
                controlTau = 0.4,
                torqueYaw = 0.25,
                weathervane = 0.05,
                blowback = 0.004,
                heaveDamping = 0.27,
                dragArea = new DragArea { f = 2.4, s = 10, v = 15 },
                gustSensitivity = 0.55,
                hasAFCS = true,
                vneKt = 150,
                torqueMcp = 100,
                torqueMax = 110,
                rpmMin = 95,
                rpmMax = 106,
                landingGear = LandingGear.Wheels,
                maxTouchdownMs = 2.0,
                equipment = new Equipment { radalt = true, hoist = true, flir = true, searchlight = true, floats = true, autopilot = true },
                operatingCostPerHour = 5600,
                requiredQualifications = new[] { "cpl_h", "type_guardian" },
                trainingQualifications = new[] { "cpl_h" },
                livery = new Livery(C(0.88, 0.2, 0.12), C(0.95, 0.95, 0.95), C(0.95, 0.95, 0.95)),
            },
            ["nightjar"] = new AircraftConfig
            {
                id = "nightjar",
                name = "Nightjar N4",
                shortName = "N4",
                role = AircraftRole.Hems,
                description = "Light twin-turbine helicopter for air ambulance and police work: a deep cabin with sliding doors and rear clamshell doors for stretcher loading, a shrouded tail fan, skids and a four-axis autopilot. Quick, agile and quiet.",
                realWorldClass = "Light twin-engine multi-role helicopter (air ambulance, police)",
                engines = 2,
                emptyMass = 2050,
                maxGross = 3800,
                fuelCapacity = 720,
                crewMass = 270, // pilot, paramedic or police observer, doctor
                maxPayload = 900,
                rotorDiameter = 11.0,
                hubHeight = 3.42,
                thrustMaxFactor = 1.42,
                ratedPowerKW = 1300,
                rotorEnergyTime = 3.6,
                profilePowerFrac = 0.12,
                fuelBurnKgH = 300,
                idleBurnKgH = 70,
                startTime = 10,
                spinUpRate = 7,
                pitchRate = 0.72,
                rollRate = 1.0,
                yawRate = 0.95,
                controlTau = 0.3,
                torqueYaw = 0.26,
                weathervane = 0.05,
                blowback = 0.005,
                heaveDamping = 0.3,
                dragArea = new DragArea { f = 1.25, s = 6.5, v = 9 },
                gustSensitivity = 0.75,
                hasAFCS = true,
                vneKt = 145,
                torqueMcp = 100,
                torqueMax = 110,
                rpmMin = 95,
                rpmMax = 106,
                landingGear = LandingGear.Skids,
                maxTouchdownMs = 2.0,
                equipment = new Equipment { radalt = true, searchlight = true, autopilot = true, medical = true },
                operatingCostPerHour = 2600,
                requiredQualifications = new[] { "cpl_h" },
                trainingQualifications = new[] { "cpl_h" },
                livery = new Livery(C(0.95, 0.80, 0.06), C(0.08, 0.45, 0.2), C(0.95, 0.80, 0.06)),
                skins = new[]
                {
                    new Skin { id = "ambulance", name = "Air ambulance (yellow and green)", livery = new Livery(C(0.95, 0.80, 0.06), C(0.08, 0.45, 0.2), C(0.95, 0.80, 0.06)), callsign = "Helimed 7-1" },
                    new Skin { id = "police", name = "Police (blue and yellow)", livery = new Livery(C(0.06, 0.09, 0.22), C(0.95, 0.80, 0.06), C(0.06, 0.09, 0.22)), callsign = "Police 2-1" },
                    new Skin { id = "charity", name = "Charity air ambulance (red)", livery = new Livery(C(0.78, 0.07, 0.07), C(0.95, 0.95, 0.93), C(0.78, 0.07, 0.07)), callsign = "Helimed 5-2" },
                    new Skin { id = "doctor", name = "Doctor response (white and orange)", livery = new Livery(C(0.93, 0.93, 0.91), C(0.95, 0.42, 0.06), C(0.12, 0.2, 0.35)), callsign = "Helimed 9-9" },
                    new Skin { id = "corporate", name = "Corporate (silver and blue)", livery = new Livery(C(0.62, 0.64, 0.66), C(0.1, 0.28, 0.6), C(0.1, 0.28, 0.6)) },
                },
            },
        };

        public static AircraftConfig Get(string id)
        {
            if (id == null || !All.TryGetValue(id, out var a)) throw new ArgumentException($"Unknown aircraft \"{id}\"");
            return a;
        }

        /// <summary>The aircraft with this id, or null.</summary>
        public static AircraftConfig Find(string id) => id != null && All.TryGetValue(id, out var a) ? a : null;

        /// <summary>The aircraft wearing one of its skins (unknown skin ids leave the standard livery).</summary>
        public static AircraftConfig WithSkin(AircraftConfig ac, string skinId)
        {
            var sk = string.IsNullOrEmpty(skinId) || ac.skins == null ? null : Array.Find(ac.skins, s => s.id == skinId);
            if (sk == null) return ac;
            var copy = ac.Clone(); copy.livery = sk.livery; copy.callsign = sk.callsign;
            return copy;
        }
    }
}
