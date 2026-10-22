// Rescue hoist (winch) (port of src/sim/hoist.ts). Simplified but physical: the hook is a pendulum on a
// variable-length cable hanging from the winch arm, pushed by wind and dragged by the aircraft's motion. The
// pilot's job is to hold a steady hover where the winch operator conns them; drift and swing make the job
// slower, and too much movement while the winchman is on the ground aborts the lift.
using System;
using System.Collections.Generic;
using static Rotorcraft.Sim.SimMath;

namespace Rotorcraft.Sim
{
    public enum HoistPhase { Stowed, Lowering, OnScene, Raising, Aboard, Aborted }

    public struct HoistTarget
    {
        public double x, z, y;
        public HoistTarget(double x, double z, double y) { this.x = x; this.z = z; this.y = y; }
    }

    public sealed class HoistState
    {
        public HoistPhase phase = HoistPhase.Stowed;
        public double cable;            // paid-out length, m
        public Vec3 hook = new Vec3();  // world position of hook/winchman
        public Vec3 hookVel = new Vec3();
        public bool attached;           // casualty attached to the winchman
        public double sceneTimer;       // time spent with the winchman at the casualty
        public double swingDeg;         // cable angle from vertical
        public double maxSwingDeg;
        public double dragM;            // horizontal distance between aircraft and grounded winchman
        public string abortReason;
        public double errSum, errN;     // hover accuracy while the winchman is on the wire or on scene
    }

    public static class Hoist
    {
        public const double maxCable = 90;      // m
        public const double speed = 1.3;        // m/s paid out / in
        public const double sceneTime = 10;     // s to secure the casualty in the strop
        public const double reachM = 6;         // winchman must land within this of the casualty
        public const double maxDragM = 14;      // aircraft drift from a grounded winchman before the lift is aborted
        public const double maxSwingDeg = 32;   // operator pauses the winch above this
        public const double minHeightFt = 30, maxHeightFt = 220;
        public const double loadKg = 90;        // winchman
        public const double casualtyKg = 85;

        public static HoistState NewHoist() => new HoistState();

        /// <summary>Winch arm position (starboard side, above the cabin door) in world space.</summary>
        public static Vec3 ArmPoint(HeliState s, double scale)
        {
            var a = BodyAxesOf(s.psi, s.theta, s.phi);
            Vec3 f = a.f, r = a.r, u = a.u;
            double lx = 1.7 * scale, ly = 2.5 * scale, lz = -1.4 * scale;
            return V3(s.pos.x + r.x * lx + u.x * ly - f.x * lz, s.pos.y + r.y * lx + u.y * ly - f.y * lz, s.pos.z + r.z * lx + u.z * ly - f.z * lz);
        }

        /// <summary>Can the operator start lowering? Returns a reason when not, null when it can.</summary>
        public static string CanStartLowering(HeliState s, HoistTarget target, double scale)
        {
            var a = ArmPoint(s, scale);
            var hFt = (a.y - target.y) * 3.28084;
            var horiz = Js.Hypot(a.x - target.x, a.z - target.z);
            var gs = Js.Hypot(s.vel.x, s.vel.z) * 1.944;
            if (s.onGround) return "We are on the ground.";
            if (hFt < minHeightFt) return $"Too low for the winch: {Js.Fixed(hFt, 0)} ft. Climb to at least {minHeightFt} ft.";
            if (hFt > maxHeightFt) return $"Too high: {Js.Fixed(hFt, 0)} ft. Descend below {maxHeightFt} ft.";
            if (horiz > 25) return $"Not overhead yet: {Js.Fixed(horiz, 0)} m to go.";
            if (gs > 4) return "Steady the hover first.";
            return null;
        }

        public static void StartLowering(HoistState h, HeliState s, double scale)
        {
            if (h.phase != HoistPhase.Stowed && h.phase != HoistPhase.Aborted) return;
            h.phase = HoistPhase.Lowering;
            h.hook = ArmPoint(s, scale);
            h.hookVel = V3(s.vel.x, s.vel.y, s.vel.z);
            h.cable = 0.5;
            h.attached = false;
            h.sceneTimer = 0;
            h.abortReason = null;
        }

        public static void AbortHoist(HoistState h, string reason)
        {
            if (h.phase == HoistPhase.Stowed || h.phase == HoistPhase.Aboard) return;
            h.abortReason = reason;
            h.phase = HoistPhase.Raising;
            h.attached = false;
        }

        /// <summary>Advance the hoist. `wind` is the local wind (m/s, world). Returns the suspended mass (kg) the aircraft carries.</summary>
        public static double StepHoist(HoistState h, HeliState s, HoistTarget target, Vec3 wind, double scale, double dt)
        {
            if (h.phase == HoistPhase.Stowed || h.phase == HoistPhase.Aboard) return h.phase == HoistPhase.Aboard ? loadKg + (h.attached ? casualtyKg : 0) : 0;
            var arm = ArmPoint(s, scale);
            var g = Ground.Current.GroundAt(h.hook.x, h.hook.z);
            var groundY = Math.Max(g.h, target.y);
            // winch drive
            var swingOk = h.swingDeg < maxSwingDeg;
            if (h.phase == HoistPhase.Lowering && swingOk) h.cable = Math.Min(maxCable, h.cable + speed * dt);
            if (h.phase == HoistPhase.Raising && swingOk) h.cable = Math.Max(0, h.cable - speed * dt);
            // hook dynamics: point mass on a cable
            var onGround = h.phase == HoistPhase.OnScene;
            if (!onGround)
            {
                const double k = 1.2; // aerodynamic drag on the man/hook (1/s)
                h.hookVel.x += (wind.x - h.hookVel.x) * k * 0.15 * dt;
                h.hookVel.z += (wind.z - h.hookVel.z) * k * 0.15 * dt;
                h.hookVel.y -= 9.81 * dt;
                h.hookVel.x *= 1 - 0.25 * dt; h.hookVel.z *= 1 - 0.25 * dt; // damping (winchman's own control)
                h.hook.x += h.hookVel.x * dt; h.hook.y += h.hookVel.y * dt; h.hook.z += h.hookVel.z * dt;
                // cable constraint (inextensible while taut)
                double dx = h.hook.x - arm.x, dy = h.hook.y - arm.y, dz = h.hook.z - arm.z;
                var d = Js.Hypot(dx, dy, dz);
                if (d == 0) d = 1e-6;
                if (d > h.cable)
                {
                    double nx = dx / d, ny = dy / d, nz = dz / d;
                    h.hook.x = arm.x + nx * h.cable; h.hook.y = arm.y + ny * h.cable; h.hook.z = arm.z + nz * h.cable;
                    double relx = h.hookVel.x - s.vel.x, rely = h.hookVel.y - s.vel.y, relz = h.hookVel.z - s.vel.z;
                    var radial = relx * nx + rely * ny + relz * nz;
                    if (radial > 0) { h.hookVel.x -= radial * nx; h.hookVel.y -= radial * ny; h.hookVel.z -= radial * nz; }
                }
                // reaching the ground / deck / water
                if (h.hook.y <= groundY + 0.9)
                {
                    h.hook.y = groundY + 0.9;
                    if (h.phase == HoistPhase.Lowering)
                    {
                        var miss = Js.Hypot(h.hook.x - target.x, h.hook.z - target.z);
                        if (miss <= reachM) { h.phase = HoistPhase.OnScene; h.hookVel = V3(); }
                        else { h.hookVel.y = Math.Max(0, h.hookVel.y); h.hookVel.x *= 0.5; h.hookVel.z *= 0.5; }
                    }
                    else h.hookVel.y = Math.Max(0, h.hookVel.y);
                }
            }
            else
            {
                // winchman on scene: cable paid out with slack while the casualty is secured
                h.sceneTimer += dt;
                h.cable = Math.Max(h.cable, Js.Hypot(h.hook.x - arm.x, h.hook.y - arm.y, h.hook.z - arm.z) + 1.5);
                if (h.sceneTimer >= sceneTime) { h.attached = true; h.phase = HoistPhase.Raising; h.hookVel = V3(); h.cable = Js.Hypot(h.hook.x - arm.x, h.hook.y - arm.y, h.hook.z - arm.z); }
            }
            h.dragM = Js.Hypot(arm.x - h.hook.x, arm.z - h.hook.z);
            var vert = Math.Max(0.1, arm.y - h.hook.y);
            h.swingDeg = Math.Atan2(h.dragM, vert) * 180 / Math.PI;
            if (h.phase != HoistPhase.OnScene) h.maxSwingDeg = Math.Max(h.maxSwingDeg, h.swingDeg);
            if (h.phase == HoistPhase.OnScene && h.dragM > maxDragM) AbortHoist(h, "The aircraft drifted off the winchman. Lift aborted.");
            if (h.phase == HoistPhase.Lowering || h.phase == HoistPhase.OnScene || (h.phase == HoistPhase.Raising && h.attached))
            {
                h.errSum += Math.Pow(Js.Hypot(arm.x - target.x, arm.z - target.z), 2) * dt; h.errN += dt;
            }
            if (h.phase == HoistPhase.Raising && h.cable <= 0.6) { h.phase = HoistPhase.Aboard; h.cable = 0; }
            var load = loadKg + (h.attached ? casualtyKg : 0);
            return h.phase == HoistPhase.OnScene ? 0 : load;
        }

        /// <summary>Winch operator's conning call: where the pilot should move, in the aircraft's own frame.</summary>
        public static string ConningCall(HoistState h, HeliState s, HoistTarget target, double scale)
        {
            var arm = ArmPoint(s, scale);
            var a = BodyAxesOf(s.psi, 0, 0);
            Vec3 f = a.f, r = a.r;
            var onHook = h.phase == HoistPhase.OnScene || (h.phase == HoistPhase.Raising && h.attached);
            var tx = onHook ? h.hook.x : target.x;
            var tz = onHook ? h.hook.z : target.z;
            double dx = tx - arm.x, dz = tz - arm.z;
            double fwd = dx * f.x + dz * f.z, right = dx * r.x + dz * r.z;
            var parts = new List<string>();
            string Say(double m) => m >= 15 ? $"{Js.Round(m / 5) * 5}" : m >= 4 ? $"{Js.Round(m)}" : "";
            if (Math.Abs(fwd) >= 2.5) parts.Add($"{(fwd > 0 ? "Forward" : "Back")} {Say(Math.Abs(fwd))}".Trim());
            if (Math.Abs(right) >= 2.5) parts.Add($"{(right > 0 ? "right" : "left")} {Say(Math.Abs(right))}".Trim());
            var hFt = (arm.y - target.y) * 3.28084;
            if (h.phase == HoistPhase.Stowed && hFt > 160) parts.Add("down 20");
            if (h.swingDeg > 20 && h.phase != HoistPhase.OnScene) parts.Add("steady, the hook is swinging");
            if (parts.Count == 0) return h.phase == HoistPhase.Stowed ? "Steady. Ready to winch." : "Steady… steady.";
            var call = string.Join(", ", parts);
            return char.ToUpperInvariant(call[0]) + call.Substring(1) + ".";
        }

        public static double HoistAccuracy(HoistState h) => h.errN > 0 ? Math.Sqrt(h.errSum / h.errN) : 0;
    }
}
