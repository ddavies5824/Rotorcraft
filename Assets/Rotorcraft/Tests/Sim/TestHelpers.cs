// Port of tests/helpers.ts: a simple pilot used only by tests, and a runner that steps the sim.
using System;
using System.Collections.Generic;
using static Rotorcraft.Sim.SimMath;

namespace Rotorcraft.Sim.Tests
{
    public struct PilotTarget
    {
        public double x, z, agl;
        public double? maxSpeed;
        public bool descend;
        public PilotTarget(double x, double z, double agl, double? maxSpeed = null, bool descend = false) { this.x = x; this.z = z; this.agl = agl; this.maxSpeed = maxSpeed; this.descend = descend; }
    }

    /// <summary>A simple autopilot used only by tests: flies the aircraft toward a point/height using the
    /// same Controls a player uses (beginner assist: cyclic sets attitude).</summary>
    public sealed class TestPilot
    {
        public double collective;
        public readonly AircraftConfig ac;
        public TestPilot(AircraftConfig ac) { this.ac = ac; }

        public Controls ControlsFor(HeliState s, PilotTarget target)
        {
            // vertical: desired climb rate from height error
            var errH = target.agl - s.@out.aglM;
            var vsDesired = target.descend ? -0.35 : Clamp(errH * 0.6, -1.2, 2.5);
            collective = Clamp(collective + ((vsDesired - s.vel.y) * 0.04) * FixedStepper.SIM_DT * 60, 0, 1);
            // horizontal: desired ground velocity toward target, capped
            double dx = target.x - s.pos.x, dz = target.z - s.pos.z;
            var dist = Js.Hypot(dx, dz);
            var vmax = target.maxSpeed ?? 3;
            var vdes = Math.Min(vmax, dist * 0.35);
            double vx = dist > 0.01 ? (dx / dist) * vdes : 0, vz = dist > 0.01 ? (dz / dist) * vdes : 0;
            var a = BodyAxesOf(s.psi, 0, 0);
            double ex = vx - s.vel.x, ez = vz - s.vel.z;
            double fwdErr = ex * a.f.x + ez * a.f.z, rgtErr = ex * a.r.x + ez * a.r.z;
            return new Controls(collective, Clamp(fwdErr * 0.25, -0.6, 0.6), Clamp(rgtErr * 0.25, -0.6, 0.6), Clamp(-s.r * 0.8, -1, 1));
        }
    }

    public static class TestHelpers
    {
        public static List<SimEvent> RunFor(HeliState s, double seconds, Func<HeliState, Controls> ctl, AircraftConfig ac, WeatherPreset wx, AssistMode assist = AssistMode.Beginner, Func<HeliState, bool> until = null)
        {
            var all = new List<SimEvent>();
            var n = (int)Js.Round(seconds / FixedStepper.SIM_DT);
            for (int i = 0; i < n; i++)
            {
                var c = ctl(s);
                FlightModel.Step(s, c, ac, wx, new SimSettings(assist), FixedStepper.SIM_DT);
                all.AddRange(s.events);
                s.events.Clear();
                if (until != null && until(s)) break;
            }
            return all;
        }

        public static HeliState Create(double x, double z, double headingDeg, double fuel, bool engineRunning = false, Airborne airborne = null, double payload = 0) =>
            FlightModel.CreateState(new CreateOptions { x = x, z = z, headingDeg = headingDeg, fuel = fuel, engineRunning = engineRunning, airborne = airborne, payload = payload });
    }
}
