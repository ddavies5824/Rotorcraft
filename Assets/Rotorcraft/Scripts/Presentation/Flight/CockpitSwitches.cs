// The aircraft's switches, as a panel a hand can reach for in VR: each stays in the same place in every aircraft so the hand
// learns where it is, and one the aircraft does not have is left blank. Every press goes through the game's ordinary action,
// the same as its key or button.
using System;
using Rotorcraft.Inputs;
using Rotorcraft.Sim;

namespace Rotorcraft.Presentation
{
    public struct CockpitSwitch
    {
        public string label;
        public bool on, available;
        public GameAction action;
    }

    public static class CockpitSwitches
    {
        public const int Columns = 5, Rows = 3;
        const string Blank = "";

        public static CockpitSwitch[] Build(HeliState s, AircraftConfig ac, APState ap, bool trimmed)
        {
            var sys = s.sys;
            var e = ac.equipment;
            var off = s.engine == EngineState.Off;
            // on the ground the engine may only be shut down once the throttle is back at idle, as on the HUD
            var canStop = s.onGround && s.engine != EngineState.Starting && sys.throttle < 0.1;
            var open = sys.throttleTarget >= 0.5;
            return new[]
            {
                Sw(off ? "START" : canStop ? "ENG OFF" : "ENGINE", !off, off || canStop, GameAction.Engine),
                Sw(open ? "THR IDLE" : "THR OPEN", open, !off, open ? GameAction.ThrottleDown : GameAction.ThrottleUp),
                Sw("GOV", sys.governor, true, GameAction.Governor),
                Sw("TRIM", trimmed, true, GameAction.Trim),
                Sw("TRIM REL", false, trimmed, GameAction.TrimRelease),

                Sw("BCN", sys.beacon, true, GameAction.Beacon),
                Sw("NAV", sys.nav, true, GameAction.NavLights),
                Sw("STROBE", sys.strobe, true, GameAction.Strobe),
                Sw("LDG LT", sys.landingLight, true, GameAction.LandingLight),
                Sw(e.searchlight ? "SRCH LT" : Blank, sys.searchlight, e.searchlight, GameAction.Searchlight),

                Sw(e.autopilot ? "AP ALT" : Blank, ap.alt, e.autopilot, GameAction.ApAlt),
                Sw(e.autopilot ? "AP HDG" : Blank, ap.hdg, e.autopilot, GameAction.ApHdg),
                Sw(e.autopilot ? "AP HVR" : Blank, ap.hover, e.autopilot, GameAction.ApHover),
                Sw(ac.retractableGear ? (sys.gearDown ? "GEAR DN" : "GEAR UP") : Blank, sys.gearDown, ac.retractableGear, GameAction.Gear),
                Sw("DOORS", false, s.onGround, GameAction.Doors),
            };
        }

        static CockpitSwitch Sw(string label, bool on, bool available, GameAction action) => new CockpitSwitch { label = label, on = on && available, available = available, action = action };
    }
}
