// Pre-flight checks done in the aircraft (port of src/game/preflight.ts): a walk round the outside (inspect the
// fuel, rotors and lights by clicking them), then in the cockpit, where items tick themselves when they are
// actually done (controls moved through their full travel, lever down and throttle closed, beacon on, a look
// round for people).
using System.Collections.Generic;
using System.Linq;

namespace Rotorcraft.Game
{
    public enum CheckId { Fuel, Rotor, Tail, Lights, Harness, Controls, Levers, Beacon, Navlights, Lookout }
    public enum CheckZone { Outside, Cockpit }
    public enum PreflightStage { Outside, Cockpit, Done, Skipped }

    public sealed class CheckItem { public CheckId id; public CheckZone zone; public string label, how; public bool done; public string detail = ""; }

    public struct PreflightInputs { public double cyclicRoll, cyclicPitch, pedals, collective, throttle, lookYaw; public bool beacon, nav, engineOff; }

    public sealed class Preflight
    {
        /// <summary>Stick and pedal travel that counts as "full and free" (keyboard input reaches about 0.55).</summary>
        public const double TRAVEL = 0.45;
        public const double LOOK = 1.1;

        public readonly List<CheckItem> items;
        public PreflightStage stage = PreflightStage.Outside;
        bool l, r, f, b, pl, pr, lookL, lookR;

        public Preflight(bool night)
        {
            CheckItem It(CheckId id, CheckZone zone, string label, string how) => new CheckItem { id = id, zone = zone, label = label, how = how };
            items = new List<CheckItem>
            {
                It(CheckId.Fuel, CheckZone.Outside, "Fuel quantity and sample", "Click the fuel point on the aircraft"),
                It(CheckId.Rotor, CheckZone.Outside, "Main rotor blades and hub", "Click the rotor head"),
                It(CheckId.Tail, CheckZone.Outside, "Tail rotor and gearbox", "Click the tail rotor"),
                It(CheckId.Lights, CheckZone.Outside, "Lights and landing light", "Click the landing light"),
                It(CheckId.Harness, CheckZone.Cockpit, "Doors closed, harness fastened", "Fasten the harness"),
                It(CheckId.Levers, CheckZone.Cockpit, "Collective fully down, throttle closed", "Lower the lever (S) and close the throttle (F)"),
                It(CheckId.Controls, CheckZone.Cockpit, "Flight controls full and free", "Move the cyclic to each side, forward and back, and both pedals"),
                It(CheckId.Beacon, CheckZone.Cockpit, "Anti-collision beacon on", "Switch on the beacon (B)"),
            };
            if (night) items.Add(It(CheckId.Navlights, CheckZone.Cockpit, "Position lights on", "Switch on the position lights (N)"));
            items.Add(It(CheckId.Lookout, CheckZone.Cockpit, "Area clear of people and obstacles", "Look left and right before starting"));
        }

        public CheckItem Item(CheckId id) => items.FirstOrDefault(i => i.id == id);
        public bool ZoneDone(CheckZone z) => items.Where(i => i.zone == z).All(i => i.done);
        public bool Complete => items.All(i => i.done);
        public bool Active => stage == PreflightStage.Outside || stage == PreflightStage.Cockpit;

        /// <summary>A walk-round item has been looked at (or a cockpit item done by a button).</summary>
        public bool Tick(CheckId id, string detail = "Checked")
        {
            var i = Item(id);
            if (i == null || i.done || !Active) return false;
            if (i.zone == CheckZone.Cockpit && stage != PreflightStage.Cockpit) return false;
            i.done = true; i.detail = detail;
            return true;
        }

        /// <summary>Finish the walk-round and climb aboard (only once the outside items are done).</summary>
        public bool Board()
        {
            if (stage != PreflightStage.Outside || !ZoneDone(CheckZone.Outside)) return false;
            stage = PreflightStage.Cockpit;
            return true;
        }

        /// <summary>Give up on the checks: the flight carries on, marked as checklist incomplete.</summary>
        public void Skip() { if (Active) stage = PreflightStage.Skipped; }

        /// <summary>Called every frame in the cockpit: items tick themselves when they have really been done.</summary>
        public List<CheckId> Update(PreflightInputs s)
        {
            var ticked = new List<CheckId>();
            if (!Active) return ticked;
            if (!s.engineOff) { stage = Complete ? PreflightStage.Done : PreflightStage.Skipped; return ticked; }   // started before finishing
            if (stage != PreflightStage.Cockpit) return ticked;
            if (s.cyclicRoll < -TRAVEL) l = true; if (s.cyclicRoll > TRAVEL) r = true;
            if (s.cyclicPitch > TRAVEL) f = true; if (s.cyclicPitch < -TRAVEL) b = true;
            if (s.pedals < -TRAVEL) pl = true; if (s.pedals > TRAVEL) pr = true;
            if (s.lookYaw < -LOOK) lookL = true; if (s.lookYaw > LOOK) lookR = true;
            void Set(CheckId id, bool ok, string detail) { var i = Item(id); if (i != null && !i.done && ok) { i.done = true; i.detail = detail; ticked.Add(id); } }
            Set(CheckId.Controls, l && r && f && b && pl && pr, "Full and free movement");
            Set(CheckId.Levers, s.collective < 0.05 && s.throttle < 0.1, "Lever down, throttle closed");
            Set(CheckId.Beacon, s.beacon, "Beacon on");
            Set(CheckId.Navlights, s.nav, "Position lights on");
            Set(CheckId.Lookout, lookL && lookR, "Clear left and right");
            if (Complete) stage = PreflightStage.Done;
            return ticked;
        }

        /// <summary>How far through the control check the pilot is, for the checklist line.</summary>
        public string ControlsProgress()
        {
            var left = new List<string>();
            if (!l) left.Add("left"); if (!r) left.Add("right"); if (!f) left.Add("forward"); if (!b) left.Add("back");
            if (!pl) left.Add("left pedal"); if (!pr) left.Add("right pedal");
            return left.Count > 0 ? $"Still to move: {string.Join(", ", left)}" : "";
        }

        public string LookProgress() => !lookL && !lookR ? "" : !lookL ? "Now look left" : !lookR ? "Now look right" : "";
    }
}
