// Runs a mission against the flying aircraft: puts the aircraft at the mission's start site with its fuel and
// weather, feeds every sim step to the mission engine, and ends the flight with the debrief.
using Rotorcraft.Game;
using Rotorcraft.Sim;
using Rotorcraft.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rotorcraft.Presentation
{
    public sealed class MissionDirector : MonoBehaviour
    {
        public HelicopterController heli;
        public string missionId = "ex01_controls";
        [Tooltip("Treat the pre-flight checklist as done (there is no checklist screen yet).")]
        public bool checklistDone = true;

        public MissionDef Def { get; private set; }
        public MissionRuntime Runtime { get; private set; }
        /// <summary>The debrief, once the flight has ended.</summary>
        public FlightResult Result { get; private set; }

        void Start()
        {
            heli.Stepped += OnStepped;
            heli.WasReset += Begin;
            Load(missionId);
        }

        void OnDestroy()
        {
            if (heli == null) return;
            heli.Stepped -= OnStepped;
            heli.WasReset -= Begin;
        }

        /// <summary>Set up a mission and put the aircraft at its start.</summary>
        public void Load(string id)
        {
            missionId = id;
            Def = Missions.ById(id) ?? Missions.All[0];
            var site = WorldMap.SiteById(Def.startSiteId);
            var ac = Aircraft.Get(Def.aircraftId);
            heli.startHint = false;
            heli.Configure(site.x, site.z, Def.startHeadingDeg, ac.fuelCapacity * Def.defaultFuelFrac, Weather.Presets[Def.weatherId]);
            heli.ResetToStart();   // raises WasReset, which begins the mission
        }

        void Begin()
        {
            Result = null;
            heli.SetPaused(false);
            Runtime = new MissionRuntime(Def, heli.Aircraft, heli.State.fuel, checklistDone, Def.timeOfDay);
            heli.State.payload = Def.payloadKg;
            SkyEnvironment.Current?.Apply(Def.timeOfDay, heli.Weather);
        }

        void OnStepped(HeliState s, System.Collections.Generic.IList<SimEvent> events, double dt, Controls c)
        {
            if (Runtime == null || Result != null) return;
            Runtime.Update(s, events, dt, c);
        }

        /// <summary>Finish the flight and show the debrief.</summary>
        public void EndFlight()
        {
            if (Runtime == null || Result != null) return;
            var s = heli.State;
            var ended = s.crashed ? FlightEnd.Crash : Runtime.status == MissionStatus.Complete ? FlightEnd.Complete : FlightEnd.Aborted;
            Result = Runtime.Finish(s, ended);
            heli.SetPaused(true);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (Result == null && (kb.enterKey.wasPressedThisFrame || kb.f3Key.wasPressedThisFrame)) EndFlight();
            else if (Result != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) heli.ResetToStart();   // fly it again
        }
    }
}
