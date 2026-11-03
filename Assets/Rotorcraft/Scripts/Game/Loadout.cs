// Freeplay loadouts (port of src/game/loadout.ts): role equipment the pilot fits before flight. Each item has a
// weight (it comes off the payload and changes the performance) and unlocks the kinds of callout that need it.
using System.Collections.Generic;
using System.Linq;
using Rotorcraft.Sim;

namespace Rotorcraft.Game
{
    public enum KitId { Medical, Hoist, Flir, Searchlight, Floats, Cargo, Seats, AuxFuel }

    public sealed class KitItem
    {
        public KitId id;
        public string name, @short, description;
        /// <summary>Mass in kg by aircraft (missing = cannot be fitted to that type).</summary>
        public Dictionary<string, double> mass;
    }

    public sealed class FittedAircraft { public AircraftConfig ac; public double kitKg; public List<string> problems; }

    public static class Loadout
    {
        static Dictionary<string, double> M(params (string ac, double kg)[] m) => m.ToDictionary(x => x.ac, x => x.kg);

        public static readonly List<KitItem> KIT = new List<KitItem>
        {
            new KitItem { id = KitId.Medical, name = "Stretcher, medical kit and paramedic", @short = "Medical", description = "Stretcher fit, monitor, oxygen and a critical-care paramedic. Needed to carry a patient.", mass = M(("nightjar", 110), ("northstar", 170), ("guardian", 150), ("peregrine", 140)) },
            new KitItem { id = KitId.Hoist, name = "Rescue hoist and winch crew", @short = "Hoist", description = "External rescue hoist with a winch operator and winchman. Needed to recover people from cliffs, the sea and ground you cannot land on.", mass = M(("nightjar", 190), ("northstar", 240), ("guardian", 0), ("peregrine", 200)) },
            new KitItem { id = KitId.Flir, name = "Thermal camera (FLIR)", @short = "FLIR", description = "Nose-mounted thermal imager: finds people by their body heat, by day or night. Press J to view through it.", mass = M(("nightjar", 75), ("northstar", 45), ("guardian", 0), ("peregrine", 60)) },
            new KitItem { id = KitId.Searchlight, name = "Searchlight", @short = "Light", description = "Steerable high-power searchlight for night landings and searches (H).", mass = M(("nightjar", 25), ("kestrel", 9), ("northstar", 30), ("guardian", 0), ("peregrine", 30)) },
            new KitItem { id = KitId.Floats, name = "Emergency flotation", @short = "Floats", description = "Inflatable floats. Required for flights over the sea and to the offshore platforms.", mass = M(("nightjar", 45), ("kestrel", 16), ("northstar", 0), ("guardian", 0), ("peregrine", 60)) },
            new KitItem { id = KitId.Cargo, name = "Freight rack and cargo restraint", @short = "Cargo", description = "Seats out, tie-down rails in. Needed for freight runs; carries up to the aircraft payload limit.", mass = M(("nightjar", 30), ("kestrel", 4), ("northstar", 60), ("guardian", 60), ("peregrine", 40)) },
            new KitItem { id = KitId.Seats, name = "Passenger cabin", @short = "Seats", description = "Passenger seats, life jackets and the cabin attendant pack. Needed for crew changes.", mass = M(("nightjar", 40), ("northstar", 0), ("guardian", 90), ("peregrine", 60)) },
            new KitItem { id = KitId.AuxFuel, name = "Auxiliary fuel tank", @short = "Aux fuel", description = "Extra cabin tank: more range for long offshore jobs, at the cost of weight.", mass = M(("nightjar", 55), ("northstar", 70), ("guardian", 70), ("peregrine", 70)) },
        };

        public static KitItem ById(KitId id) => KIT.First(k => k.id == id);

        /// <summary>Extra fuel capacity of the auxiliary tank (kg).</summary>
        public const double AUX_FUEL_KG = 380;

        public static bool CanFit(AircraftConfig ac, KitId id) => ById(id).mass.ContainsKey(ac.id);

        /// <summary>Sensible starting kit for each type.</summary>
        public static List<KitId> DefaultKit(AircraftConfig ac)
        {
            if (ac.id == "kestrel") return new List<KitId> { KitId.Searchlight };
            if (ac.id == "guardian") return new List<KitId> { KitId.Hoist, KitId.Flir, KitId.Searchlight, KitId.Floats, KitId.Medical };
            if (ac.id == "nightjar") return new List<KitId> { KitId.Medical, KitId.Searchlight };
            if (ac.id == "peregrine") return new List<KitId> { KitId.Flir, KitId.Searchlight };
            return new List<KitId> { KitId.Floats, KitId.Medical, KitId.Searchlight };
        }

        /// <summary>Apply a loadout: equipment flags follow the kit (so the HUD only offers what is fitted), kit mass
        /// is carried as payload, and the auxiliary tank raises the fuel capacity.</summary>
        public static FittedAircraft Fit(AircraftConfig b, IList<KitId> kit)
        {
            bool Has(KitId k) => kit.Contains(k) && CanFit(b, k);
            var problems = new List<string>();
            double kitKg = 0;
            foreach (var k in kit)
            {
                if (!ById(k).mass.TryGetValue(b.id, out var m)) { problems.Add($"{ById(k).name} cannot be fitted to the {b.shortName}."); continue; }
                kitKg += m;
            }
            if (Has(KitId.Cargo) && Has(KitId.Seats)) problems.Add("The freight rack and the passenger cabin cannot both be fitted.");
            if (kitKg > b.maxPayload) problems.Add($"Kit weighs {kitKg:0} kg, more than the {b.maxPayload:0} kg payload.");
            var ac = b.Clone();
            ac.equipment = b.equipment.Clone();
            ac.equipment.hoist = Has(KitId.Hoist); ac.equipment.flir = Has(KitId.Flir); ac.equipment.searchlight = Has(KitId.Searchlight);
            ac.equipment.floats = Has(KitId.Floats); ac.equipment.medical = Has(KitId.Medical);
            ac.fuelCapacity = b.fuelCapacity + (Has(KitId.AuxFuel) ? AUX_FUEL_KG : 0);
            ac.fitted = kit.Where(k => CanFit(b, k)).Select(k => k.ToString().ToLowerInvariant()).ToArray();
            return new FittedAircraft { ac = ac, kitKg = kitKg, problems = problems };
        }

        /// <summary>What the payload allows for patients, passengers or freight once the kit is aboard.</summary>
        public static double SpareLift(AircraftConfig ac, double kitKg) => System.Math.Max(0, ac.maxPayload - kitKg);
    }
}
