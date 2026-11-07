// Four-axis autopilot (AFCS upper modes) for the twin-engine types (port of src/sim/autopilot.ts). It writes
// ordinary control positions on top of the pilot's, so the flight model is unchanged and the pilot can always
// override: moving a control past a small threshold drops the mode on that axis (like the real
// "beep trim / force-trim release"), except in hover hold where the cyclic nudges the hold point.
using System;
using System.Collections.Generic;
using static Rotorcraft.Sim.SimMath;

namespace Rotorcraft.Sim
{
    public enum APMode { Alt, Hdg, Hover }

    public sealed class APState
    {
        public bool alt, hdg, hover;
        public double altTarget;      // metres AMSL (or AGL in hover with radalt)
        public double hdgTarget;      // radians
        public double hoverX, hoverZ, hoverAgl = 15;
        public double leverRef;       // where the pilot's lever was when ALT/HOVER engaged (moving it away overrides)
        public double colOut;         // collective the AP is currently commanding
        public double trim;           // the collective it has learnt holds this height
        public bool holding;          // the AP has the collective, or is still easing it back to the pilot's lever
    }

    public struct APToggleResult
    {
        public bool ok;
        public string reason;
        public APToggleResult(bool ok, string reason = null) { this.ok = ok; this.reason = reason; }
    }

    public struct APResult
    {
        public Controls controls;
        public List<APMode> dropped;
    }

    public static class Autopilot
    {
        // The collective channel: a learnt trim plus a small correction for climb rate error, moving no faster than an
        // autopilot's actuator would. (The original integrated the error alone, which never settled: it hunted up and
        // down with the lever sweeping most of its travel, even in still air.)
        const double HeightGain = 0.25;   // m/s of climb wanted per metre off target
        const double MaxClimb = 2.5;      // m/s
        const double ClimbGain = 0.05;    // lever per m/s of climb rate error
        const double TrimGain = 0.015;    // lever per second per m/s of climb rate error
        const double LeverRate = 0.2;     // lever per second
        const double CaptureLead = 1.2;   // s: a climb or descent in hand carries on this long while it is taken off
        const double ClimbLever = 0.045;  // lever a steady climb of 1 m/s holds above level flight, roughly
        const double TrimNearM = 4;       // the trim learns only this close to the height: on the way there it would learn the climb, and carry on past

        public static APState NewAP() => new APState();

        /// <summary>Take the collective from wherever it is now, so engaging never jolts the aircraft.</summary>
        static void TakeCollective(APState ap, double lever, HeliState s)
        {
            var now = ap.holding ? ap.colOut : lever;
            ap.leverRef = lever; ap.colOut = now; ap.trim = Clamp(now - s.vel.y * ClimbLever, 0, 1); ap.holding = true;
        }

        static double Lead(HeliState s) => Clamp(s.vel.y * CaptureLead, -8, 8);

        /// <summary>Engage or disengage a mode, capturing the current state as the target. Returns ok = false if not allowed.</summary>
        public static APToggleResult ToggleAP(APState ap, APMode mode, HeliState s, AircraftConfig ac, double lever)
        {
            if (!ac.equipment.autopilot) return new APToggleResult(false, "This aircraft has no autopilot.");
            if (s.onGround) return new APToggleResult(false, "Autopilot upper modes are for flight only.");
            if (mode == APMode.Alt)
            {
                ap.alt = !ap.alt;
                if (ap.alt) { ap.hover = false; ap.altTarget = s.pos.y + Lead(s); TakeCollective(ap, lever, s); }
            }
            else if (mode == APMode.Hdg)
            {
                ap.hdg = !ap.hdg;
                if (ap.hdg) { ap.hover = false; ap.hdgTarget = s.psi; }
            }
            else
            {
                if (!ap.hover && Js.Hypot(s.vel.x, s.vel.z) > 6) return new APToggleResult(false, "Slow below 12 kt before engaging hover hold.");
                ap.hover = !ap.hover;
                if (ap.hover) { ap.alt = false; ap.hdg = false; ap.hoverX = s.pos.x; ap.hoverZ = s.pos.z; ap.hoverAgl = Math.Max(3, s.@out.aglM + Lead(s)); ap.hdgTarget = s.psi; TakeCollective(ap, lever, s); }
            }
            return new APToggleResult(true);
        }

        public static bool AnyAP(APState ap) => ap.alt || ap.hdg || ap.hover;

        /// <summary>Desired attitude → cyclic position, for both attitude-command and rate-command handling.</summary>
        static double CyclicFor(double desired, double current, double rate, double maxAngle, AssistMode assist, double gainRate)
        {
            if (assist == AssistMode.Realistic) return Clamp(((desired - current) * 2.2 - rate * 0.6) / gainRate, -1, 1);
            return Clamp(desired / maxAngle, -1, 1);
        }

        /// <summary>
        /// Blend autopilot commands into the pilot's controls. `pilot` is the raw pilot input this frame.
        /// Mutates ap (targets, disengagements). Returns the controls actually sent to the flight model.
        /// </summary>
        public static APResult ApplyAP(APState ap, HeliState s, AircraftConfig ac, Controls pilot, AssistMode assist, double dt)
        {
            var dropped = new List<APMode>();
            if ((!AnyAP(ap) && !ap.holding) || s.onGround || s.crashed)
            {
                if (AnyAP(ap) && (s.onGround || s.crashed)) { ap.alt = ap.hdg = ap.hover = false; dropped.Add(APMode.Alt); }
                ap.holding = false;
                return new APResult { controls = pilot, dropped = dropped };
            }
            var o = pilot;
            var v = s.vel;
            // ---- vertical (ALT or HOVER height) ----
            if (ap.alt || ap.hover)
            {
                if (Math.Abs(pilot.collective - ap.leverRef) > 0.06)
                {
                    if (ap.hover) { ap.hoverAgl = Math.Max(3, ap.hoverAgl + Math.Sign(pilot.collective - ap.leverRef) * 2.5 * dt); }
                    else { ap.alt = false; dropped.Add(APMode.Alt); }
                }
                if (ap.alt || ap.hover)
                {
                    var err = ap.hover ? ap.hoverAgl - s.@out.aglM : ap.altTarget - s.pos.y;
                    var climbErr = Clamp(err * HeightGain, -MaxClimb, MaxClimb) - v.y;
                    var want = ap.trim + climbErr * ClimbGain;
                    var step = LeverRate * dt;
                    // the trim learns only while the lever can follow, or it would wind up against a limit
                    if (want > 0 && want < 1 && Math.Abs(want - ap.colOut) < step && Math.Abs(err) < TrimNearM) ap.trim = Clamp(ap.trim + climbErr * TrimGain * dt, 0, 1);
                    ap.colOut += Clamp(Clamp(want, 0, 1) - ap.colOut, -step, step);
                    o.collective = ap.colOut;
                }
            }
            // just disengaged: the collective eases back to the pilot's lever rather than jumping there
            if (ap.holding && !ap.alt && !ap.hover)
            {
                ap.colOut += Clamp(pilot.collective - ap.colOut, -LeverRate * dt, LeverRate * dt);
                o.collective = ap.colOut;
                if (Math.Abs(pilot.collective - ap.colOut) < 1e-3) ap.holding = false;
            }
            // ---- heading ----
            var gs = Js.Hypot(v.x, v.z);
            if (ap.hdg || ap.hover)
            {
                if (Math.Abs(pilot.pedals) > 0.12) { ap.hdgTarget = s.psi; }
                else
                {
                    var e = WrapPi(ap.hdgTarget - s.psi);
                    if (ap.hover || gs < 20) o.pedals = Clamp(e * 1.6 - s.r * 0.9, -1, 1);
                    else if (Math.Abs(pilot.cyclicRoll) < 0.12)
                    {
                        // coordinated turn back to heading: bank proportional to heading error
                        var bankWant = Clamp(e * 1.2, -20 * DEG, 20 * DEG);
                        o.cyclicRoll = CyclicFor(bankWant, s.phi, s.p, 28 * DEG, assist, ac.rollRate);
                    }
                    else ap.hdgTarget = s.psi;
                }
            }
            // ---- hover hold: ground position via cyclic; pilot cyclic moves the hold point ----
            if (ap.hover)
            {
                var axes = BodyAxesOf(s.psi, 0, 0);
                Vec3 f = axes.f, r = axes.r;
                if (Math.Abs(pilot.cyclicPitch) > 0.08 || Math.Abs(pilot.cyclicRoll) > 0.08)
                {
                    const double spd = 1.5; // m/s per full deflection
                    ap.hoverX += (f.x * pilot.cyclicPitch + r.x * pilot.cyclicRoll) * spd * dt;
                    ap.hoverZ += (f.z * pilot.cyclicPitch + r.z * pilot.cyclicRoll) * spd * dt;
                }
                double ex = ap.hoverX - s.pos.x, ez = ap.hoverZ - s.pos.z;
                double vwx = Clamp(ex * 0.35, -3, 3) - v.x, vwz = Clamp(ez * 0.35, -3, 3) - v.z;
                double fErr = vwx * f.x + vwz * f.z, rErr = vwx * r.x + vwz * r.z;
                var pitchWant = Clamp(-fErr * 0.06, -12 * DEG, 12 * DEG);   // nose down (negative theta) to accelerate forward
                var rollWant = Clamp(rErr * 0.07, -12 * DEG, 12 * DEG);
                o.cyclicPitch = -CyclicFor(pitchWant, s.theta, s.q, 22 * DEG, assist, ac.pitchRate);
                o.cyclicRoll = CyclicFor(rollWant, s.phi, s.p, 28 * DEG, assist, ac.rollRate);
            }
            return new APResult { controls = o, dropped = dropped };
        }

        public static string ApStatus(APState ap)
        {
            if (ap.hover) return $"HVR {Js.ToInt32(ap.hoverAgl * 3.28084)} ft";
            var p = new List<string>();
            if (ap.alt) p.Add($"ALT {Js.ToInt32(ap.altTarget * 3.28084)}");
            if (ap.hdg) p.Add($"HDG {Js.Fixed(HdgDeg(ap.hdgTarget), 0).PadLeft(3, '0')}");
            return string.Join(" · ", p);
        }
    }
}
