/*
 * FlightDynamics — a simplified but physically-motivated helicopter model (port of src/sim/flightModel.ts).
 *
 * Documented approximations (see docs/FLIGHT_MODEL.md):
 *  - Rotor thrust  T = collective · Tmax · (RPM)² · σ · IGE · ETL − heave damping · m · w
 *  - Induced power from momentum theory, with the classic forward-flight inflow reduction
 *    (this is what produces translational lift's power saving), plus profile, parasite and climb power.
 *  - Rotor speed integrates rotor kinetic energy: d(E)/dt = P_engine − P_required. A governor
 *    adds power to hold 100% until available power runs out, so over-pitching droops the rotor.
 *    With the engine stopped, descent drives P_required negative — the basis of autorotation.
 *  - Cyclic commands rotor-disc tilt via first-order rate response; thrust acts along the body
 *    "up" axis. Pedals command yaw rate against torque reaction and weathervane effects.
 *  - Skid/wheel ground contact with friction, touchdown classification and dynamic-rollover risk.
 * The step function is pure with respect to its inputs (state, controls, aircraft, weather,
 * settings, dt) and the ground: identical inputs give identical outputs, independent of rendering frame rate.
 */
using System;
using System.Collections.Generic;
using static Rotorcraft.Sim.SimMath;

namespace Rotorcraft.Sim
{
    public enum AssistMode { Beginner, Standard, Realistic }
    public enum EngineState { Off, Starting, Running, Flameout }
    public enum SwitchName { Governor, Beacon, Nav, Strobe, LandingLight, Searchlight }
    public enum SystemFailure { TailRotor, Hydraulics }
    public enum EngineCommand { Start, Stop }

    public struct Controls
    {
        public double collective;   // 0..1 (lever position)
        public double cyclicPitch;  // -1..1, + = forward (nose down)
        public double cyclicRoll;   // -1..1, + = right
        public double pedals;       // -1..1, + = right yaw
        public Controls(double collective, double cyclicPitch, double cyclicRoll, double pedals) { this.collective = collective; this.cyclicPitch = cyclicPitch; this.cyclicRoll = cyclicRoll; this.pedals = pedals; }
        public static readonly Controls Idle = new Controls(0, 0, 0, 0);
        public Controls WithCollective(double v) { var c = this; c.collective = v; return c; }
        public Controls WithCyclicPitch(double v) { var c = this; c.cyclicPitch = v; return c; }
        public Controls WithCyclicRoll(double v) { var c = this; c.cyclicRoll = v; return c; }
        public Controls WithPedals(double v) { var c = this; c.pedals = v; return c; }
    }

    public struct SimSettings
    {
        public AssistMode assist;
        public SimSettings(AssistMode assist) { this.assist = assist; }
    }

    public enum SimEventType { Liftoff, Touchdown, Crash, Engine, Damage }
    public enum TouchdownSeverity { Smooth, Normal, Firm, Hard }

    /// <summary>One thing that happened during a step. Which fields are set depends on the type.</summary>
    public sealed class SimEvent
    {
        public SimEventType type;
        public double t;
        // touchdown
        public double vs, gs, roll, pitch, x, z;
        public Surface surface;
        public string siteId;
        public TouchdownSeverity severity;
        // crash, damage
        public string reason;
        public double amount;
        // engine
        public EngineState state;
    }

    public sealed class FlightOutputs
    {
        public double rpm;           // % rotor RPM
        public double torque;        // % engine torque (total)
        public double powerKW;
        public double powerRequiredKW;
        public double powerAvailableKW;
        public double thrustN;
        public double mass;          // kg
        public double sigma = 1;     // density ratio
        public double iasKt;
        public double gsKt;
        public double vsFpm;
        public double altFt;
        public double aglM;
        public double hdg;           // degrees
        public double trackDeg;
        public double fuelFlowKgH;
        public double ige = 1;       // thrust multiplier from ground effect
        public bool etl;
        public double etlShudder;    // 0..1 vibration as the aircraft passes through translational lift
        public double windKt;
        public double windFromDeg;
        public double windAvgKt, windAvgFromDeg;
        public double sideslipMs;
        public Surface surfaceBelow = Surface.Grass;
        public double hoverPowerOGE; // % torque needed to hover out of ground effect at current mass
        public double engineRpm;     // % engine RPM (splits from rotor RPM when the freewheel unit disengages)
        public double engineTemp = 15; // °C: cylinder head (piston) or turbine outlet (turbine)
        public double oilPress;      // % of normal
        public double vrs;           // 0..1 vortex ring state severity
        public double bladeStall;    // 0..1 retreating blade stall severity
    }

    /// <summary>Cockpit switches and engine controls. All start OFF/closed for a cold start.</summary>
    public sealed class Systems
    {
        public double throttle;        // 0 = idle, 1 = fully open (flight)
        public double throttleTarget;  // where the pilot is rolling the twist grip to
        public bool governor;          // engine RPM governor
        public bool beacon;            // red anti-collision beacon
        public bool nav;               // position lights (red left, green right, white tail)
        public bool strobe;            // white high-intensity strobes
        public bool landingLight;
        public bool searchlight;       // SAR aircraft only
        public bool gearDown = true;   // landing gear lever (retractable wheels only)

        public bool this[SwitchName n]
        {
            get
            {
                switch (n)
                {
                    case SwitchName.Governor: return governor;
                    case SwitchName.Beacon: return beacon;
                    case SwitchName.Nav: return nav;
                    case SwitchName.Strobe: return strobe;
                    case SwitchName.LandingLight: return landingLight;
                    default: return searchlight;
                }
            }
            set
            {
                switch (n)
                {
                    case SwitchName.Governor: governor = value; break;
                    case SwitchName.Beacon: beacon = value; break;
                    case SwitchName.Nav: nav = value; break;
                    case SwitchName.Strobe: strobe = value; break;
                    case SwitchName.LandingLight: landingLight = value; break;
                    default: searchlight = value; break;
                }
            }
        }
    }

    /// <summary>System failures: tail rotor drive (no pedal authority) and hydraulic boost (heavy, slow controls).</summary>
    public sealed class Failures
    {
        public bool tailRotor, hydraulics;
        public Failures Clone() => (Failures)MemberwiseClone();
    }

    public struct TempLimits { public string label; public double min, caution, max; }

    public sealed class HeliState
    {
        public double t;
        public Vec3 pos;
        public Vec3 vel;
        public double psi, theta, phi;  // heading, pitch (nose up +), roll (right +)
        public double p, q, r;          // roll, pitch, yaw rates (rad/s)
        public double rotorE;           // normalised rotor energy: (RPM/100%)²
        public double govTarget;        // governor target RPM fraction (ramps up on start)
        public EngineState engine;
        public double engineN;          // 0..1 start progress
        public int enginesOut;          // engines failed in flight (twins can continue one engine inoperative)
        public bool throttleIdle;       // instructor is holding the throttle closed (simulated engine failure)
        public Failures failures = new Failures();
        public Systems sys;
        public double engineTemp;
        public double fuel;             // kg
        public double payload;          // kg
        public bool onGround;
        public double damage;           // 0..1
        public bool crashed;
        public string crashReason;
        public double torqueSmoothed;
        public double windAvgX, windAvgZ;
        public bool windAvgSet;
        public double pedalTrim;
        /// <summary>Retractable wheels: 1 down and locked, 0 up; in between while they travel.</summary>
        public double gearPos = 1;
        public FlightOutputs @out;
        public List<SimEvent> events = new List<SimEvent>();
    }

    public sealed class Airborne
    {
        public double agl, speedMs;
        public Airborne(double agl, double speedMs) { this.agl = agl; this.speedMs = speedMs; }
    }

    public sealed class CreateOptions
    {
        public double x, z, headingDeg, fuel;
        public double payload;
        public bool engineRunning;
        public Airborne airborne;
    }

    public static class FlightModel
    {
        const double MIN_PITCH = 0.3;
        const double HEAVE_NEAR_LEVEL = 0.35;   // 1/s, on top of the aircraft's own heave damping
        const double ATT_PITCH_GAIN = 2.6, ATT_ROLL_GAIN = 2.8, ATT_DAMPING_RATIO = 0.85;

        /// <summary>The share of its own rate fed back in the attitude-command modes, to give that damping ratio with the rate response's lag.</summary>
        static double AttitudeDamping(double gain, double tau) => Math.Max(0, 2 * ATT_DAMPING_RATIO * Math.Sqrt(gain * tau) - 1);

        /// <summary>Engine temperature limits by engine type (°C).</summary>
        public static TempLimits TempLimitsOf(AircraftConfig ac) =>
            ac.engines == 1 ? new TempLimits { label = "CHT", min = 90, caution = 230, max = 260 } : new TempLimits { label = "TOT", min = 300, caution = 780, max = 850 };

        public static FlightOutputs EmptyOutputs() => new FlightOutputs();

        public static HeliState CreateState(CreateOptions opts)
        {
            var g = Ground.Current.GroundAt(opts.x, opts.z);
            var psi = opts.headingDeg * DEG;
            var running = opts.engineRunning;
            var air = opts.airborne;
            var s = new HeliState
            {
                t = 0,
                pos = V3(opts.x, g.h + (air?.agl ?? 0), opts.z),
                vel = air != null ? V3(Math.Sin(psi) * air.speedMs, 0, -Math.Cos(psi) * air.speedMs) : V3(),
                psi = psi, theta = 0, phi = 0, p = 0, q = 0, r = 0,
                rotorE = running ? 1 : 0,
                govTarget = running ? 1 : 0,
                engine = running ? EngineState.Running : EngineState.Off,
                engineN = running ? 1 : 0,
                throttleIdle = false,
                enginesOut = 0,
                failures = new Failures(),
                sys = new Systems { throttle = running ? 1 : 0, throttleTarget = running ? 1 : 0, governor = true, beacon = running, nav = running, strobe = false, landingLight = false, searchlight = false },
                engineTemp = running ? 180 : 15,
                fuel = opts.fuel,
                payload = opts.payload,
                onGround = air == null,
                damage = 0,
                crashed = false,
                crashReason = null,
                torqueSmoothed = 0,
                pedalTrim = 0,
                @out = EmptyOutputs(),
            };
            s.@out.rpm = running ? 100 : 0;
            s.@out.engineRpm = s.@out.rpm;
            s.@out.engineTemp = s.engineTemp;
            s.@out.aglM = s.pos.y - g.h;
            s.@out.altFt = s.pos.y * FT;
            s.@out.hdg = opts.headingDeg;
            return s;
        }

        public static double MassOf(HeliState s, AircraftConfig ac) => ac.emptyMass + ac.crewMass + s.fuel + s.payload;

        public static void FailSystem(HeliState s, SystemFailure system)
        {
            if (s.crashed) return;
            var f = s.failures?.Clone() ?? new Failures();
            if (system == SystemFailure.TailRotor) f.tailRotor = true; else f.hydraulics = true;
            s.failures = f;
        }

        /// <summary>Fail one engine (training or emergency). Single-engine aircraft flame out; twins continue OEI.</summary>
        public static void FailEngine(HeliState s, AircraftConfig ac)
        {
            if (s.crashed || s.engine != EngineState.Running) return;
            s.enginesOut = Math.Min(ac.engines, s.enginesOut + 1);
            if (s.enginesOut >= ac.engines) { s.engine = EngineState.Flameout; s.events.Add(new SimEvent { type = SimEventType.Engine, t = s.t, state = EngineState.Flameout }); }
        }

        /// <summary>Roll the throttle (twist grip) towards idle (0) or fully open (1). Blocked while the instructor holds it.</summary>
        public static bool CommandThrottle(HeliState s, double target)
        {
            if (s.crashed || s.throttleIdle) return false;
            s.sys.throttleTarget = Clamp(double.IsNaN(target) || double.IsInfinity(target) ? 0 : target, 0, 1);
            return true;
        }

        public static bool CommandSwitch(HeliState s, SwitchName name, bool? on = null)
        {
            if (s.crashed) return false;
            var cur = s.sys[name];
            s.sys[name] = on ?? !cur;
            return true;
        }

        /// <summary>Seconds the retractable wheels take to travel up or down.</summary>
        public const double GEAR_TRAVEL_S = 6;

        /// <summary>Move the landing gear lever. Weight on the wheels stops them coming up on the ground.</summary>
        public static bool CommandGear(HeliState s, AircraftConfig ac, bool? down = null)
        {
            if (s.crashed || !ac.retractableGear) return false;
            var want = down ?? !s.sys.gearDown;
            if (!want && s.onGround) return false;
            s.sys.gearDown = want;
            return true;
        }

        /// <summary>Engine start / stop commands are discrete and validated against state.</summary>
        public static bool CommandEngine(HeliState s, EngineCommand cmd)
        {
            if (s.crashed) return false;
            if (cmd == EngineCommand.Start)
            {
                if (s.engine != EngineState.Off || s.fuel <= 0) return false;
                s.engine = EngineState.Starting;
                s.engineN = 0;
                s.events.Add(new SimEvent { type = SimEventType.Engine, t = s.t, state = EngineState.Starting });
                return true;
            }
            if (s.engine == EngineState.Off) return false;
            s.engine = EngineState.Off;
            s.engineN = 0;
            s.events.Add(new SimEvent { type = SimEventType.Engine, t = s.t, state = EngineState.Off });
            return true;
        }

        static void Crash(HeliState s, string reason)
        {
            if (s.crashed) return;
            s.crashed = true;
            s.crashReason = reason;
            s.damage = 1;
            s.vel = V3();
            s.p = s.q = s.r = 0;
            s.engine = EngineState.Off;
            s.events.Add(new SimEvent { type = SimEventType.Crash, t = s.t, reason = reason });
        }

        static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        /// <summary>Sanitise raw control input: clamp, replace NaN and infinities with safe defaults.</summary>
        public static Controls SanitizeControls(Controls c)
        {
            double F(double v, double lo, double hi, double d) => Finite(v) ? Clamp(v, lo, hi) : d;
            return new Controls(F(c.collective, 0, 1, 0), F(c.cyclicPitch, -1, 1, 0), F(c.cyclicRoll, -1, 1, 0), F(c.pedals, -1, 1, 0));
        }

        static string GearName(AircraftConfig ac) => ac.landingGear == LandingGear.Skids ? "skids" : "wheels";

        public static void Step(HeliState s, Controls rawControls, AircraftConfig ac, WeatherPreset wx, SimSettings settings, double dt)
        {
            if (s.crashed) { s.t += dt; return; }
            var c = SanitizeControls(rawControls);
            var assist = settings.assist;
            var world = Ground.Current;

            // ---- environment ---------------------------------------------------------------------------
            var ground = world.GroundAt(s.pos.x, s.pos.z);
            var agl = Math.Max(0, s.pos.y - ground.h);
            if (ac.retractableGear) s.gearPos = Clamp(s.gearPos + (s.sys.gearDown ? dt : -dt) / GEAR_TRAVEL_S, 0, 1);
            else s.gearPos = 1;
            var dragF = ac.dragArea.f + (ac.retractableGear ? ac.gearDragArea * s.gearPos : 0);
            // a clockwise rotor (seen from above) reverses the torque reaction and the side the retreating blade is on
            var rotorSign = ac.rotorClockwise ? -1.0 : 1.0;
            var sigma = Weather.DensityRatio(Math.Max(0, s.pos.y), wx.oatC);
            var rho = 1.225 * sigma;
            var mass = MassOf(s, ac);
            var wind = Weather.WindAt(wx, s.t, s.pos.x, s.pos.y, s.pos.z, agl);
            // Offshore platforms leave a turbulent wake (and a downdraft) downwind of the structure.
            var windSpd = Js.Hypot(wind.x, wind.z);
            var wake = windSpd > 1 ? world.PlatformWake(s.pos.x, s.pos.y, s.pos.z, wind.x / windSpd, wind.z / windSpd) : 0;
            if (wake > 0) wind.y -= wake * windSpd * 0.22;
            var ax = BodyAxesOf(s.psi, s.theta, s.phi);
            Vec3 f = ax.f, rgt = ax.r, u = ax.u;
            var va = V3(s.vel.x - wind.x, s.vel.y - wind.y, s.vel.z - wind.z); // aircraft velocity relative to air
            double vaF = Dot(va, f), vaR = Dot(va, rgt), vaU = Dot(va, u);
            var vh = Math.Sqrt(vaF * vaF + vaR * vaR);

            // ---- rotor & thrust ------------------------------------------------------------------------
            var rpmFrac = Math.Sqrt(Math.Max(0, s.rotorE));
            var R = ac.rotorDiameter / 2;
            var hubAgl = agl + ac.hubHeight;
            var k = Math.Min(0.62, R / (4 * Math.Max(0.1, hubAgl)));
            var ige = 1 / (1 - k * k);
            ige = 1 + (Math.Min(ige, 1.18) - 1) * (1 - Smoothstep(4, 12, vh));
            var etlFactor = 1 + 0.09 * Smoothstep(5, 12, vh);
            var tMax = ac.thrustMaxFactor * ac.maxGross * G;
            // Blade pitch never reaches zero lift at the bottom stop, so the lever maps onto a pitch range with
            // a positive minimum. This is what lets a fully-lowered collective hold rotor RPM in autorotation.
            var pitchFrac = MIN_PITCH + (1 - MIN_PITCH) * c.collective;
            var thrust = pitchFrac * tMax * rpmFrac * rpmFrac * sigma * ige * etlFactor;
            // heave damping uses the vertical air velocity (inflow change from climb/descent), not the
            // forward-flight component through a tilted disc, which is accounted for as parasite power.
            thrust -= ac.heaveDamping * mass * va.y * rpmFrac;
            // Close to level flight the inflow resists a change of height much more firmly: without this a small movement of a lever
            // worked at a rate (keyboard, thumbstick) sets off a swing in height that grows. It fades out at the sink rates of an
            // autorotation, whose balance of power and descent it would otherwise upset. (Not in the browser game.)
            thrust -= HEAVE_NEAR_LEVEL * mass * va.y * (1 - Smoothstep(3, 9, Math.Abs(va.y))) * rpmFrac;
            // Vortex ring state: descending into your own downwash at low airspeed with power applied.
            var vh0 = Math.Sqrt((mass * G) / (2 * 1.225 * sigma * Math.PI * Math.Pow(ac.rotorDiameter / 2, 2)));
            var descent = -va.y;
            var vrs = Smoothstep(0.38 * vh0, 0.62 * vh0, descent) * (1 - Smoothstep(0.55 * vh0, 1.0 * vh0, vh)) * Smoothstep(0.35, 0.55, pitchFrac) * (agl > 3 ? 1 : 0);
            thrust *= 1 - 0.32 * vrs;
            // Retreating blade stall near VNE (worse when heavy and high).
            var ias = Math.Max(0, vaF) * Math.Sqrt(sigma) * 1.943844;
            var vStall = ac.vneKt * (1.06 - 0.12 * (mass / ac.maxGross - 0.7)) * Math.Sqrt(sigma);
            var bladeStall = Smoothstep(vStall - 8, vStall + 10, ias) * Smoothstep(0.5, 0.7, pitchFrac);
            thrust = Clamp(thrust, 0, 1.7 * ac.maxGross * G * rpmFrac * rpmFrac);

            // ---- power ---------------------------------------------------------------------------------
            var area = Math.PI * R * R;
            var ratedW = ac.ratedPowerKW * 1000;
            var vhInduced = thrust > 0 ? Math.Sqrt(thrust / (2 * rho * area)) : 0;
            var vi = vhInduced > 0 ? (vhInduced * vhInduced) / Math.Sqrt(vh * vh + vhInduced * vhInduced) : 0;
            var pInduced = 1.15 * thrust * vi / Math.Pow(ige, 1.5);
            var pProfile = ac.profilePowerFrac * ratedW * rpmFrac * rpmFrac * rpmFrac * (1 + 3 * Math.Pow(vh / 200, 2)) * (0.6 + 0.4 * sigma);
            var pParasite = 0.5 * rho * dragF * vh * vh * vh;
            var pClimb = mass * G * va.y; // potential-energy rate; negative in descent (autorotation source)
            var pFriction = 0.012 * ratedW * Math.Min(1, rpmFrac * 5);
            var pReq = pInduced + pProfile + pParasite + pClimb + pFriction;

            // engine controls: the twist grip rolls at a finite rate; the instructor can hold it closed
            if (s.throttleIdle) s.sys.throttleTarget = 0;
            s.sys.throttle += Clamp(s.sys.throttleTarget - s.sys.throttle, -0.7 * dt, 0.7 * dt);
            var thr = s.sys.throttle;
            // engine availability. Power available scales with throttle opening; idle still makes a little.
            double pAvail = 0;
            if (s.engine == EngineState.Starting)
            {
                s.engineN = Math.Min(1, s.engineN + dt / ac.startTime);
                pAvail = ratedW * (0.08 + 0.3 * thr) * s.engineN;
                s.govTarget = Math.Min(0.62, s.govTarget + (ac.spinUpRate / 100) * dt * s.engineN);
                if (s.engineN >= 1) { s.engine = EngineState.Running; s.events.Add(new SimEvent { type = SimEventType.Engine, t = s.t, state = EngineState.Running }); }
            }
            else if (s.engine == EngineState.Running)
            {
                // One engine inoperative: the survivor runs at its 2½-minute contingency rating (about 130% of its share).
                var oei = s.enginesOut <= 0 ? 1 : s.enginesOut >= ac.engines ? 0 : ((double)(ac.engines - s.enginesOut) / ac.engines) * 1.3;
                pAvail = ratedW * Math.Pow(sigma, 0.85) * (1 - 0.35 * s.damage) * (0.065 + 0.935 * thr) * oei;
                s.govTarget = Math.Min(1, s.govTarget + (ac.spinUpRate / 100) * dt);
            }
            else
            {
                s.govTarget = 0;
            }
            double pEngine = 0;
            if (pAvail > 0)
            {
                if (s.sys.governor || s.engine == EngineState.Starting)
                {
                    var eTarget = s.govTarget * s.govTarget;
                    pEngine = Clamp(pReq + 4 * (eTarget - s.rotorE) * ratedW, 0, pAvail);
                }
                else
                {
                    // Governor off: engine power follows the throttle, and the pilot must hold RPM by hand.
                    pEngine = pAvail * Clamp(0.45 + 0.6 * rpmFrac, 0, 1);
                }
            }
            var eRated = ratedW * ac.rotorEnergyTime;
            s.rotorE = Math.Max(0, s.rotorE + ((pEngine - pReq) / eRated) * dt);
            if (s.rotorE < 1e-5 && pEngine == 0) s.rotorE = 0;

            // fuel
            double fuelFlow = 0;
            if (s.engine == EngineState.Running || s.engine == EngineState.Starting)
            {
                fuelFlow = ac.idleBurnKgH + (ac.fuelBurnKgH - ac.idleBurnKgH) * (pEngine / ratedW);
                s.fuel = Math.Max(0, s.fuel - (fuelFlow * dt) / 3600);
                if (s.fuel <= 0)
                {
                    s.engine = EngineState.Flameout;
                    s.events.Add(new SimEvent { type = SimEventType.Engine, t = s.t, state = EngineState.Flameout });
                }
            }
            var torque = rpmFrac > 0.05 ? (pEngine / ratedW / Math.Max(rpmFrac, 0.3)) * 100 : 0;
            s.torqueSmoothed += (torque - s.torqueSmoothed) * Math.Min(1, dt / 1.5);
            // engine temperature: heats with power, cools with airflow; slow first-order response
            {
                var lim = TempLimitsOf(ac);
                var lit = s.engine == EngineState.Running || s.engine == EngineState.Starting;
                var powerFrac = pEngine / ratedW;
                var cooling = 1 - 0.28 * Smoothstep(5, 40, vh);
                var target = !lit ? wx.oatC : lim.min + (lim.caution - lim.min) * Clamp(powerFrac * 1.15 * cooling, 0, 1.6) + (s.engine == EngineState.Starting ? (lim.caution - lim.min) * 0.4 : 0);
                s.engineTemp += (target - s.engineTemp) * Math.Min(1, dt / (lit ? 25 : 90));
            }

            // ---- angular dynamics ----------------------------------------------------------------------
            var ctrlPower = rpmFrac * rpmFrac;
            var turb = Weather.TurbulenceRatesAt(wx, s.t);
            if (wake > 0) { var kw = wake * Math.Min(1.5, windSpd / 9); turb.p += SmoothNoise(s.t * 2.9, 61) * 0.22 * kw; turb.q += SmoothNoise(s.t * 2.3, 62) * 0.15 * kw; turb.r += SmoothNoise(s.t * 2.1, 63) * 0.18 * kw; }
            var gustScale = ac.gustSensitivity * (ac.hasAFCS ? 0.5 : 1) * (assist == AssistMode.Beginner ? 0.5 : 1);
            double pCmd, qCmd;
            if (assist == AssistMode.Realistic)
            {
                // Rate command: the aircraft holds whatever attitude you leave it in; disc flap-back raises the nose with speed.
                // (AFCS-equipped aircraft reject turbulence more strongly — see gustScale above.)
                qCmd = -c.cyclicPitch * ac.pitchRate + ac.blowback * vaF;
                pCmd = c.cyclicRoll * ac.rollRate - ac.blowback * 0.5 * vaR;
            }
            else
            {
                // Attitude command (training aid): cyclic sets a target attitude and the aircraft levels when released.
                var thetaT = -c.cyclicPitch * 22 * DEG;
                var phiT = c.cyclicRoll * 28 * DEG;
                if (assist == AssistMode.Beginner)
                {
                    // Drift damping near the hover: gently leans against any drift relative to the ground.
                    var gF = s.vel.x * f.x + s.vel.z * f.z;
                    var gR = s.vel.x * rgt.x + s.vel.z * rgt.z;
                    var hoverish = 1 - Smoothstep(6, 12, Js.Hypot(s.vel.x, s.vel.z));
                    if (Math.Abs(c.cyclicPitch) < 0.15) thetaT += Clamp(gF * 0.025, -0.1, 0.1) * hoverish;
                    if (Math.Abs(c.cyclicRoll) < 0.15) phiT += Clamp(-gR * 0.03, -0.12, 0.12) * hoverish;
                }
                // damped so that it settles on the attitude instead of rocking past it (the browser game overshot by up to 20% on the twins)
                qCmd = Clamp((thetaT - s.theta) * ATT_PITCH_GAIN, -ac.pitchRate, ac.pitchRate) - s.q * AttitudeDamping(ATT_PITCH_GAIN, ac.controlTau) + ac.blowback * 0.4 * vaF;
                pCmd = Clamp((phiT - s.phi) * ATT_ROLL_GAIN, -ac.rollRate, ac.rollRate) - s.p * AttitudeDamping(ATT_ROLL_GAIN, ac.controlTau);
            }
            qCmd = qCmd * ctrlPower + turb.q * gustScale;
            pCmd = pCmd * ctrlPower + turb.p * gustScale;
            // buffet in vortex ring; pitch-up and roll towards the retreating (left) side in blade stall
            if (vrs > 0) { qCmd += SmoothNoise(s.t * 6, 3) * 0.35 * vrs; pCmd += SmoothNoise(s.t * 5.3, 4) * 0.45 * vrs; }
            if (bladeStall > 0) { qCmd += 0.35 * bladeStall; pCmd -= 0.5 * bladeStall * rotorSign; }

            // Yaw: pedals vs torque reaction and weathervane
            var torqueFrac = torque / 100;
            // From about 15 kt forward (full by about 27 kt) the fin takes over: it carries most of the anti-torque load
            // and the nose follows a banked turn with the pedals left alone.
            var fin = Smoothstep(5, 14, vaF);
            double torqueYaw;
            if (assist == AssistMode.Realistic) torqueYaw = ac.torqueYaw * (ac.hasAFCS ? 0.25 : 1) * torqueFrac * (1 - 0.85 * fin);
            else if (assist == AssistMode.Standard) torqueYaw = ac.torqueYaw * (torque - s.torqueSmoothed) / 100 * 0.7 * (1 - 0.85 * fin); // transients only, a nudge rather than a kick
            else torqueYaw = 0;
            torqueYaw *= rotorSign;
            // the turn rate of a level turn at this bank and speed (a light aircraft without AFCS in Realistic needs some pedal)
            var followTurn = fin * (assist == AssistMode.Realistic && !ac.hasAFCS ? 0.7 : 1) * G * Math.Tan(Clamp(s.phi, -45 * DEG, 45 * DEG)) / Math.Max(vaF, 5);
            // the nose swings into the relative wind; near the hover the assisted modes hold it against that
            var windvane = ac.weathervane * vaR * (assist == AssistMode.Realistic ? 1 : fin);
            var rCmd = c.pedals * ac.yawRate * ctrlPower + torqueYaw * ctrlPower + followTurn + windvane + turb.r * gustScale;
            // pedals centred: the yaw rate settles quickly on what the turn needs, so the nose stops where it is put
            if (Math.Abs(c.pedals) < 0.05) rCmd -= (s.r - followTurn) * (assist != AssistMode.Realistic ? 1 : ac.hasAFCS ? 0.5 : 0);
            var fl = s.failures;
            if (fl != null && fl.tailRotor)
            {
                // no anti-torque: the fuselage turns against the rotor in proportion to power; airflow over the
                // fin (weathervaning) holds the nose straight at speed, so the answer is speed and a running landing
                rCmd = rotorSign * ac.torqueYaw * 1.4 * torqueFrac * ctrlPower + ac.weathervane * 2.2 * vaR + turb.r * gustScale;
            }
            var hyd = fl != null && fl.hydraulics;
            if (hyd) { qCmd *= 0.55; pCmd *= 0.55; rCmd *= 0.7; }

            var tau = ac.controlTau * (hyd ? 2.2 : 1);
            s.q += ((qCmd - s.q) / tau) * dt;
            s.p += ((pCmd - s.p) / tau) * dt;
            s.r += ((rCmd - s.r) / (tau * 1.6)) * dt;

            // ground restraint: the skids resist rotation while carrying weight
            var thrustUp = thrust * u.y;
            var groundLoad = s.onGround ? Clamp(1 - thrustUp / (mass * G), 0, 1) : 0;
            if (s.onGround)
            {
                var free = Math.Pow(1 - groundLoad, 2);
                s.q *= free;
                s.r *= free * 0.8 + 0.2 * (1 - groundLoad);
                // roll can build when light on the skids with lateral cyclic: dynamic rollover hazard
                s.p *= groundLoad > 0.7 ? 0 : free;
                var relax = Math.Min(1, dt * 6 * groundLoad);
                s.theta -= s.theta * relax;
                if (groundLoad > 0.7) s.phi -= s.phi * relax;
            }

            s.theta = Clamp(s.theta + s.q * dt, -80 * DEG, 80 * DEG);
            s.phi = Clamp(s.phi + s.p * dt, -88 * DEG, 88 * DEG);
            s.psi = WrapPi(s.psi + s.r * dt);
            if (s.psi < 0) s.psi += 2 * Math.PI;

            // ---- forces --------------------------------------------------------------------------------
            var ax2 = BodyAxesOf(s.psi, s.theta, s.phi);
            var q2 = 0.5 * rho;
            var dF = -q2 * dragF * vaF * Math.Abs(vaF);
            var dR = -q2 * ac.dragArea.s * vaR * Math.Abs(vaR);
            var dU = -q2 * ac.dragArea.v * vaU * Math.Abs(vaU) * 0.3;
            // rotor in-plane (H-force) damping: resists translation relative to the air, scaled by rotor speed
            var hDamp = (-0.1 * mass * rpmFrac) / (1 + vh / 6);
            var fx = ax2.u.x * thrust + ax2.f.x * dF + ax2.r.x * dR + ax2.u.x * dU + hDamp * (vaF * ax2.f.x + vaR * ax2.r.x);
            var fy = ax2.u.y * thrust + ax2.f.y * dF + ax2.r.y * dR + ax2.u.y * dU + hDamp * (vaF * ax2.f.y + vaR * ax2.r.y) - mass * G;
            var fz = ax2.u.z * thrust + ax2.f.z * dF + ax2.r.z * dR + ax2.u.z * dU + hDamp * (vaF * ax2.f.z + vaR * ax2.r.z);

            if (s.onGround)
            {
                // normal force and friction
                var normal = Math.Max(0, -fy);
                if (fy < 0) fy = 0;
                var mu = ac.landingGear == LandingGear.Skids || s.gearPos < 0.95 ? 0.5 : 0.35; // wheels with parking brake
                var hs0 = Js.Hypot(s.vel.x, s.vel.z);
                var fric = mu * normal;
                if (hs0 > 1e-4)
                {
                    var maxStop = (hs0 * mass) / dt;
                    var fApplied = Math.Min(fric, maxStop + Js.Hypot(fx, fz));
                    fx -= (s.vel.x / hs0) * fApplied;
                    fz -= (s.vel.z / hs0) * fApplied;
                }
                else
                {
                    var hf = Js.Hypot(fx, fz);
                    if (hf <= fric) { fx = 0; fz = 0; } else { fx -= (fx / hf) * fric; fz -= (fz / hf) * fric; }
                }
            }

            s.vel.x += (fx / mass) * dt;
            s.vel.y += (fy / mass) * dt;
            s.vel.z += (fz / mass) * dt;
            s.pos.x += s.vel.x * dt;
            s.pos.y += s.vel.y * dt;
            s.pos.z += s.vel.z * dt;

            // ---- ground contact ------------------------------------------------------------------------
            var g2 = world.GroundAt(s.pos.x, s.pos.z);
            if (s.pos.y <= g2.h)
            {
                if (!s.onGround)
                {
                    var vs = Math.Max(0, -s.vel.y);
                    var gs = Js.Hypot(s.vel.x, s.vel.z);
                    double roll = s.phi / DEG, pitch = s.theta / DEG;
                    if (g2.surface == Surface.Water && !ac.equipment.floats) Crash(s, "Ditched in the sea — this aircraft has no flotation gear");
                    else if (vs > ac.maxTouchdownMs * 2.1) Crash(s, $"Impact with the {(g2.surface == Surface.Water ? "sea" : "ground")} at {Js.Fixed(vs * 196.85, 0)} ft/min");
                    else if (gs > 9) Crash(s, $"Touched down at {Js.Fixed(gs * KT, 0)} kt ground speed — the {GearName(ac)} dug in");
                    else if (Math.Abs(roll) > 16 || Math.Abs(pitch) > 22) Crash(s, $"Touched down with {(Math.Abs(roll) > 16 ? $"{Js.Fixed(Math.Abs(roll), 0)}° of bank" : $"{Js.Fixed(Math.Abs(pitch), 0)}° of pitch")} — the aircraft rolled over");
                    else if (g2.slope > 0.24) Crash(s, "Landed on a slope too steep for the aircraft — it rolled over");
                    else if (ac.retractableGear && s.gearPos < 0.95 && (gs > 5 || vs > ac.maxTouchdownMs * 1.4)) Crash(s, "Landed with the wheels up — the aircraft slid onto its belly and broke up");
                    if (!s.crashed)
                    {
                        var severity = TouchdownSeverity.Smooth;
                        if (ac.retractableGear && s.gearPos < 0.95)
                        {
                            // a gentle wheels-up landing: the belly and the gear doors take it, and the aircraft is grounded
                            severity = TouchdownSeverity.Hard;
                            var dmg = 0.45;
                            s.damage = Clamp(s.damage + dmg, 0, 1);
                            s.events.Add(new SimEvent { type = SimEventType.Damage, t = s.t, amount = dmg, reason = "Wheels-up landing" });
                        }
                        else if (vs > ac.maxTouchdownMs)
                        {
                            severity = TouchdownSeverity.Hard;
                            var dmg = Clamp((vs - ac.maxTouchdownMs) * 0.25, 0.05, 0.6);
                            s.damage = Clamp(s.damage + dmg, 0, 1);
                            s.events.Add(new SimEvent { type = SimEventType.Damage, t = s.t, amount = dmg, reason = "Hard landing" });
                        }
                        else if (vs > 1.0 || gs > 4) severity = TouchdownSeverity.Firm;
                        else if (vs > 0.45 || gs > 1.5) severity = TouchdownSeverity.Normal;
                        s.events.Add(new SimEvent { type = SimEventType.Touchdown, t = s.t, vs = vs, gs = gs, roll = roll, pitch = pitch, x = s.pos.x, z = s.pos.z, surface = g2.surface, siteId = g2.siteId, severity = severity });
                    }
                    s.onGround = true;
                }
                s.pos.y = g2.h;
                if (s.vel.y < 0) s.vel.y = 0;
            }
            else if (s.onGround && s.pos.y > g2.h + 0.03)
            {
                s.onGround = false;
                s.events.Add(new SimEvent { type = SimEventType.Liftoff, t = s.t });
            }
            else if (s.onGround)
            {
                s.pos.y = Math.Max(s.pos.y, g2.h);
            }
            if (s.onGround && !s.crashed)
            {
                if (Math.Abs(s.phi) > 14 * DEG) Crash(s, "Dynamic rollover — too much lateral cyclic while light on the skids");
            }
            if (!s.onGround && !s.crashed && (Math.Abs(s.phi) > 80 * DEG || Math.Abs(s.theta) > 70 * DEG))
            {
                // Not an instant failure — but extreme attitudes overstress the aircraft.
                s.damage = Clamp(s.damage + 0.2 * dt, 0, 1);
                if (s.damage >= 1) Crash(s, "Structural failure after extreme attitudes");
            }

            s.t += dt;

            // ---- outputs -------------------------------------------------------------------------------
            var o = s.@out;
            var aglNow = Math.Max(0, s.pos.y - g2.h);
            var hs = Js.Hypot(s.vel.x, s.vel.z);
            var windH = Js.Hypot(wind.x, wind.z);
            o.rpm = rpmFrac * 100;
            o.torque = torque;
            o.powerKW = pEngine / 1000;
            o.powerRequiredKW = pReq / 1000;
            o.powerAvailableKW = pAvail / 1000;
            o.thrustN = thrust;
            o.mass = mass;
            o.sigma = sigma;
            o.iasKt = Math.Max(0, vaF) * Math.Sqrt(sigma) * KT;
            o.gsKt = hs * KT;
            o.vsFpm = s.vel.y * FT * 60;
            o.altFt = s.pos.y * FT;
            o.aglM = aglNow;
            o.hdg = HdgDeg(s.psi);
            o.trackDeg = hs > 0.5 ? HdgDeg(Math.Atan2(s.vel.x, -s.vel.z)) : o.hdg;
            o.fuelFlowKgH = fuelFlow;
            o.ige = ige;
            o.etl = vh > 8;
            o.etlShudder = s.onGround ? 0 : Smoothstep(4, 6.5, vh) * (1 - Smoothstep(8, 11, vh)) * rpmFrac;
            o.windKt = windH * KT;
            o.windFromDeg = HdgDeg(Math.Atan2(-wind.x, wind.z));
            // what the instruments show: the wind here, steadied over a few seconds so that the figures can be read
            var kWind = s.windAvgSet ? Math.Min(1, dt / 2.5) : 1;
            s.windAvgSet = true;
            s.windAvgX += (wind.x - s.windAvgX) * kWind;
            s.windAvgZ += (wind.z - s.windAvgZ) * kWind;
            o.windAvgKt = Js.Hypot(s.windAvgX, s.windAvgZ) * KT;
            o.windAvgFromDeg = HdgDeg(Math.Atan2(-s.windAvgX, s.windAvgZ));
            o.sideslipMs = vaR;
            o.surfaceBelow = g2.surface;
            // torque% to hover OGE at current mass/density (no wind) — useful planning number
            var tH = mass * G;
            var vhH = Math.Sqrt(tH / (2 * rho * area));
            o.vrs = vrs;
            o.bladeStall = bladeStall;
            o.engineTemp = s.engineTemp;
            var runningNow = s.engine == EngineState.Running || s.engine == EngineState.Starting;
            o.engineRpm = s.engine == EngineState.Starting ? Math.Max(o.rpm, s.engineN * 62) : runningNow ? (pEngine > 0.03 * ratedW ? o.rpm : Math.Min(o.rpm, 60 + 8 * thr)) : 0;
            o.oilPress = runningNow ? Clamp(o.engineRpm / 75, 0, 1.15) * 100 : 0;
            o.hoverPowerOGE = ((1.15 * tH * vhH + ac.profilePowerFrac * ratedW * (0.6 + 0.4 * sigma) + 0.012 * ratedW) / ratedW) * 100;
        }

        /// <summary>Planning estimate: torque % needed to hover out of ground effect at a mass and density.</summary>
        public static double HoverPowerPct(AircraftConfig ac, double massKg, double sigma = 1)
        {
            double R = ac.rotorDiameter / 2, area = Math.PI * R * R, rho = 1.225 * sigma, ratedW = ac.ratedPowerKW * 1000;
            var t = massKg * G;
            var vh = Math.Sqrt(t / (2 * rho * area));
            return ((1.15 * t * vh + ac.profilePowerFrac * ratedW * (0.6 + 0.4 * sigma) + 0.012 * ratedW) / ratedW) * 100;
        }
    }
}
