// The instrument panel (port of src/ui/instruments.ts): ten round analogue gauges drawn from the simulated
// outputs: airspeed, attitude, altimeter, vertical speed, compass (an HSI in the twins), dual tachometer, torque,
// fuel, engine temperature and oil pressure. Vector drawing (Painter2D), so it stays sharp at any size.
using System;
using Rotorcraft.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rotorcraft.Presentation.UI
{
    public sealed class HsiData { public double course, xtkM, distM; public string name; }

    public sealed class InstrumentPanel : VisualElement
    {
        static readonly Color FACE = Hex(0x0b0e10), BEZEL = Hex(0x262d32), TICK = Hex(0xe8ecee), DIM = Hex(0x8d989e), GREEN = Hex(0x4fc27a), AMBER = Hex(0xf2b53c), RED = Hex(0xef5a4c), NAV = Hex(0xe05cc8), SKY = Hex(0x3f7fb4), EARTH = Hex(0x7a5634), HSI = Hex(0x4fd47a);
        static Color Hex(int v) => new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f);

        public HeliState s;
        public AircraftConfig ac;
        public double? bearingToTarget;
        public HsiData hsi;

        public InstrumentPanel()
        {
            generateVisualContent += Draw;
            pickingMode = PickingMode.Ignore;
            style.flexGrow = 1;
        }

        public void Set(HeliState state, AircraftConfig aircraft, double? bearing, HsiData h) { s = state; ac = aircraft; bearingToTarget = bearing; hsi = h; MarkDirtyRepaint(); }

        static Vector2 P(Vector2 c, float a, float r) => c + new Vector2(Mathf.Sin(a) * r, -Mathf.Cos(a) * r);
        static Angle Deg(float rad) => Angle.Radians(rad);

        static void Disc(Painter2D p, Vector2 c, float r, Color col) { p.fillColor = col; p.BeginPath(); p.Arc(c, r, Angle.Degrees(0), Angle.Degrees(360)); p.Fill(); }

        /// <summary>Bezel, face and the dial's name, just below the hub (clear of the scale numbers at the ends of the sweep).</summary>
        static void Gauge(MeshGenerationContext m, Vector2 c, float r, string label, float labelY = 0.3f)
        {
            var p = m.painter2D;
            Disc(p, c, r + 4, BEZEL); Disc(p, c, r, FACE);
            if (!string.IsNullOrEmpty(label)) Text(m, label, c + new Vector2(0, r * labelY), r * 0.14f, DIM);
        }

        /// <summary>A digital readout in a dark window, so the needles crossing it never hide the figures.</summary>
        static void Readout(MeshGenerationContext m, string t, Vector2 centre, float size, Color col)
        {
            var p = m.painter2D;
            float hw = t.Length * size * 0.3f + size * 0.25f, hh = size * 0.62f;
            p.fillColor = Hex(0x1a2025); p.strokeColor = BEZEL; p.lineWidth = 1;
            p.BeginPath(); p.MoveTo(centre + new Vector2(-hw, -hh)); p.LineTo(centre + new Vector2(hw, -hh)); p.LineTo(centre + new Vector2(hw, hh)); p.LineTo(centre + new Vector2(-hw, hh)); p.ClosePath(); p.Fill(); p.Stroke();
            Text(m, t, centre, size, col);
        }

        static void Text(MeshGenerationContext m, string t, Vector2 centre, float size, Color col)
        {
            // DrawText anchors at the top left: centre it roughly on its width
            var w = t.Length * size * 0.6f;
            m.DrawText(t, centre - new Vector2(w / 2, size * 0.62f), size, col);
        }

        /// <summary>Arc along the dial (angles measured from straight up, clockwise).</summary>
        static void DialArc(Painter2D p, Vector2 c, float r, float a0, float a1, Color col, float w)
        {
            p.strokeColor = col; p.lineWidth = w; p.lineCap = LineCap.Butt;
            p.BeginPath(); p.Arc(c, r, Deg(a0 - Mathf.PI / 2), Deg(a1 - Mathf.PI / 2)); p.Stroke();
        }

        static void Needle(Painter2D p, Vector2 c, float len, float ang, Color col, float w = 3)
        {
            p.strokeColor = col; p.lineWidth = w; p.lineCap = LineCap.Round;
            p.BeginPath(); p.MoveTo(P(c, ang + Mathf.PI, len * 0.15f)); p.LineTo(P(c, ang, len)); p.Stroke();
            Disc(p, c, w * 1.6f, col);
        }

        static void Line(Painter2D p, Vector2 a, Vector2 b, Color col, float w) { p.strokeColor = col; p.lineWidth = w; p.lineCap = LineCap.Butt; p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke(); }

        /// <summary>Linear dial: ticks and labels; returns value → angle.</summary>
        static Func<double, float> Dial(MeshGenerationContext m, Vector2 c, float r, double min, double max, float s0, float s1, double major, double labelEvery, Func<double, string> fmt)
        {
            Func<double, float> ang = v => s0 + (float)((v - min) / (max - min)) * (s1 - s0);
            for (var v = min; v <= max + 1e-6; v += major)
            {
                var a = ang(v);
                var lab = Math.Abs((v - min) % labelEvery) < 1e-6 || Math.Abs((v - min) % labelEvery - labelEvery) < 1e-6;
                Line(m.painter2D, P(c, a, r * (lab ? 0.78f : 0.86f)), P(c, a, r * 0.95f), TICK, lab ? 2 : 1);
                if (lab) Text(m, fmt(v), P(c, a, r * 0.6f), r * 0.18f, TICK);
            }
            return ang;
        }

        /// <summary>Which gauges to draw and where (cells as fractions of the element); null = the standard 5 × 2 panel.</summary>
        public (Gauges gauge, Rect cell)[] layout;
        /// <summary>A glass display: dark screen, a page title, gauges drawn smaller with their own frames.</summary>
        public string screenTitle;

        public enum Gauges { Airspeed, Attitude, Altimeter, VerticalSpeed, Compass, Tachometer, Torque, Fuel, EngineTemp, Oil }
        static readonly (Gauges g, int i, int j)[] STANDARD = { (Gauges.Airspeed, 0, 0), (Gauges.Attitude, 1, 0), (Gauges.Altimeter, 2, 0), (Gauges.VerticalSpeed, 3, 0), (Gauges.Compass, 0, 1), (Gauges.Tachometer, 1, 1), (Gauges.Torque, 2, 1), (Gauges.Fuel, 3, 1), (Gauges.EngineTemp, 4, 0), (Gauges.Oil, 4, 1) };

        void Draw(MeshGenerationContext m)
        {
            if (s == null || ac == null) return;
            var rect = contentRect;
            float w = rect.width, h = rect.height;
            if (w < 50 || h < 30) return;
            if (screenTitle != null)
            {
                var p = m.painter2D;
                p.fillColor = Hex(0x05080a); p.BeginPath(); p.MoveTo(Vector2.zero); p.LineTo(new Vector2(w, 0)); p.LineTo(new Vector2(w, h)); p.LineTo(new Vector2(0, h)); p.ClosePath(); p.Fill();
                Text(m, screenTitle, new Vector2(w / 2, h * 0.045f + 4), Mathf.Max(10, h * 0.05f), DIM);
            }
            if (layout == null)
            {
                const int cols = 5, rows = 2;
                float cw = w / cols, rh = h / rows, r = Mathf.Min(cw, rh) * 0.42f;
                foreach (var (g, i, j) in STANDARD) DrawGauge(m, g, new Vector2(cw * (i + 0.5f), rh * (j + 0.5f)), r);
            }
            else
                foreach (var (g, cell) in layout)
                {
                    var cr = new Rect(cell.x * w, cell.y * h, cell.width * w, cell.height * h);
                    DrawGauge(m, g, cr.center, Mathf.Min(cr.width, cr.height) * (screenTitle != null ? 0.40f : 0.455f));
                }
        }

        /// <summary>
        /// Where a point on the attitude indicator's horizon card goes at this bank (phi positive is right wing down;
        /// screen y runs down). The card turns the opposite way to the aircraft, so a right bank raises its right side.
        /// </summary>
        public static Vector2 OnHorizonCard(Vector2 v, double phi)
        {
            float cos = Mathf.Cos((float)-phi), sin = Mathf.Sin((float)-phi);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }

        void DrawGauge(MeshGenerationContext m, Gauges g, Vector2 c, float r)
        {
            var o = s.@out;
            var p = m.painter2D;
            switch (g)
            {
            // 1. Airspeed
            case Gauges.Airspeed:
            {
                Gauge(m, c, r, "KNOTS");
                var max = Math.Ceiling(ac.vneKt / 20) * 20 + 20;
                var ang = Dial(m, c, r, 0, max, -2.6f, 2.6f, 10, 20, v => v.ToString("0"));
                DialArc(p, c, r * 0.93f, ang(20), ang(ac.vneKt * 0.9), GREEN, 4);
                DialArc(p, c, r * 0.93f, ang(ac.vneKt * 0.9), ang(ac.vneKt), AMBER, 4);
                Line(p, P(c, ang(ac.vneKt), r * 0.8f), P(c, ang(ac.vneKt), r * 0.98f), RED, 3);
                Needle(p, c, r * 0.85f, ang(Math.Min(max, o.iasKt)), TICK);
            } break;
            // 2. Attitude
            case Gauges.Attitude:
            {
                Disc(p, c, r + 4, BEZEL);
                var pitchPx = (float)(s.theta * SimMath.RAD) * (r / 30);
                // the horizon: a half-disc of sky above the tilted line, earth below (clipped to the face)
                Vector2 Rot(Vector2 v) => c + OnHorizonCard(v, s.phi);
                Disc(p, c, r, EARTH);
                // sky: the part of the face above the line y = pitchPx (in the rolled frame)
                var d = Mathf.Clamp(pitchPx, -r * 0.999f, r * 0.999f);
                var half = Mathf.Acos(Mathf.Clamp(d / r, -1, 1));   // angle from the down axis to where the line meets the rim
                p.fillColor = SKY; p.BeginPath();
                // the arc from one crossing round the top to the other, in the rolled frame
                var startA = Mathf.PI / 2 + half; var endA = Mathf.PI / 2 - half + Mathf.PI * 2;
                p.MoveTo(Rot(new Vector2(Mathf.Cos(startA) * r, Mathf.Sin(startA) * r)));
                for (int i = 1; i <= 40; i++) { var a = Mathf.Lerp(startA, endA, i / 40f); p.LineTo(Rot(new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r))); }
                p.ClosePath(); p.Fill();
                if (Mathf.Abs(pitchPx) < r) { var hx = Mathf.Sqrt(r * r - d * d); Line(p, Rot(new Vector2(-hx, d)), Rot(new Vector2(hx, d)), TICK, 2); }
                for (int pp = -30; pp <= 30; pp += 10)
                {
                    if (pp == 0) continue;
                    var yy = pitchPx - pp * (r / 30);
                    if (Mathf.Abs(yy) > r * 0.8f) continue;
                    var ww = r * (Math.Abs(pp) % 20 == 0 ? 0.32f : 0.18f);
                    Line(p, Rot(new Vector2(-ww, yy)), Rot(new Vector2(ww, yy)), TICK, 1.2f);
                }
                foreach (var b in new[] { -60, -30, -20, -10, 0, 10, 20, 30, 60 }) { var a = b * Mathf.Deg2Rad; Line(p, P(c, a, r * 0.86f), P(c, a, r * 0.98f), TICK, 1.5f); }
                p.fillColor = TICK; p.BeginPath(); p.MoveTo(Rot(new Vector2(0, -r * 0.84f))); p.LineTo(Rot(new Vector2(-5, -r * 0.74f))); p.LineTo(Rot(new Vector2(5, -r * 0.74f))); p.ClosePath(); p.Fill();
                p.strokeColor = AMBER; p.lineWidth = 4; p.lineCap = LineCap.Round;
                p.BeginPath(); p.MoveTo(c + new Vector2(-r * 0.55f, 0)); p.LineTo(c + new Vector2(-r * 0.18f, 0)); p.LineTo(c + new Vector2(-r * 0.1f, r * 0.1f)); p.Stroke();
                p.BeginPath(); p.MoveTo(c + new Vector2(r * 0.55f, 0)); p.LineTo(c + new Vector2(r * 0.18f, 0)); p.LineTo(c + new Vector2(r * 0.1f, r * 0.1f)); p.Stroke();
                Disc(p, c, 3, AMBER);
            } break;
            // 3. Altimeter (1,000 ft a turn of the long needle, 10,000 the short)
            case Gauges.Altimeter:
            {
                Gauge(m, c, r, "ALT FT", 0.43f);
                Dial(m, c, r, 0, 1000, 0, Mathf.PI * 2 * 0.999f, 100, 100, v => (v / 100 % 10).ToString("0"));
                var alt = Math.Max(0, o.altFt);
                Readout(m, alt.ToString("0"), c + new Vector2(0, r * 0.22f), r * 0.17f, TICK);
                Needle(p, c, r * 0.55f, (float)(alt % 10000 / 10000 * Math.PI * 2), TICK, 5);
                Needle(p, c, r * 0.85f, (float)(alt % 1000 / 1000 * Math.PI * 2), TICK, 2.5f);
            } break;
            // 4. Vertical speed (±2,000 ft/min)
            case Gauges.VerticalSpeed:
            {
                Gauge(m, c, r, "VS ×100");
                float Ang(double v) => -Mathf.PI / 2 + (float)(Math.Max(-2000, Math.Min(2000, v)) / 2000) * Mathf.PI * 0.85f;
                for (var v = -2000; v <= 2000; v += 500)
                {
                    var a = Ang(v);
                    Line(p, P(c, a, r * 0.8f), P(c, a, r * 0.95f), TICK, 1.5f);
                    if (v % 1000 == 0) Text(m, Math.Abs(v / 100).ToString(), P(c, a, r * 0.62f), r * 0.17f, TICK);
                }
                Needle(p, c, r * 0.85f, Ang(o.vsFpm), o.vsFpm < -600 && o.aglM < 30 ? AMBER : TICK);
            } break;
            // 5. Compass card with the target bug (an HSI in the twins)
            case Gauges.Compass:
            {
                Gauge(m, c, r, "");
                var hd = (float)(o.hdg * Mathf.Deg2Rad);
                for (int a = 0; a < 360; a += 10)
                {
                    var rad = a * Mathf.Deg2Rad - hd; var big = a % 30 == 0;
                    Line(p, P(c, rad, r * (big ? 0.8f : 0.87f)), P(c, rad, r * 0.95f), TICK, big ? 2 : 1);
                    if (big) Text(m, a == 0 ? "N" : a == 90 ? "E" : a == 180 ? "S" : a == 270 ? "W" : (a / 10).ToString(), P(c, rad, r * 0.64f), r * 0.2f, a % 90 == 0 ? TICK : DIM);
                }
                if (hsi != null)
                {
                    var crs = (float)(hsi.course * Mathf.Deg2Rad) - hd;
                    Line(p, P(c, crs, r * 0.42f), P(c, crs, r * 0.9f), HSI, 3);
                    Line(p, P(c, crs + Mathf.PI, r * 0.42f), P(c, crs + Mathf.PI, r * 0.9f), HSI, 3);
                    p.fillColor = HSI; p.BeginPath(); p.MoveTo(P(c, crs, r * 0.95f)); p.LineTo(P(c, crs - 0.12f, r * 0.8f)); p.LineTo(P(c, crs + 0.12f, r * 0.8f)); p.ClosePath(); p.Fill();
                    var right = new Vector2(Mathf.Cos(crs), Mathf.Sin(crs));
                    foreach (var dot in new[] { -2, -1, 1, 2 }) Disc(p, c + right * dot * r * 0.16f, 2.5f, TICK);
                    var dev = (float)Math.Max(-1, Math.Min(1, -hsi.xtkM / 926));
                    var along = new Vector2(Mathf.Sin(crs), -Mathf.Cos(crs));
                    Line(p, c + right * dev * r * 0.32f - along * r * 0.38f, c + right * dev * r * 0.32f + along * r * 0.38f, HSI, 4);
                    if (bearingToTarget != null)
                    {
                        var br = (float)(bearingToTarget.Value * Mathf.Deg2Rad) - hd;
                        p.fillColor = NAV; p.BeginPath(); p.MoveTo(P(c, br, r * 0.99f)); p.LineTo(P(c, br - 0.09f, r * 0.88f)); p.LineTo(P(c, br + 0.09f, r * 0.88f)); p.ClosePath(); p.Fill();
                    }
                    Text(m, $"{hsi.distM / 1852:F1} NM", c + new Vector2(0, r * 1.12f), r * 0.15f, HSI);
                    Text(m, $"CRS {(int)Math.Round(hsi.course) % 360:000}", c + new Vector2(-r * 0.62f, -r * 1.04f), r * 0.15f, HSI);
                }
                else if (bearingToTarget != null)
                {
                    var br = (float)(bearingToTarget.Value * Mathf.Deg2Rad) - hd;
                    p.fillColor = NAV; p.BeginPath(); p.MoveTo(P(c, br, r * 0.97f)); p.LineTo(P(c, br - 0.12f, r * 0.8f)); p.LineTo(P(c, br + 0.12f, r * 0.8f)); p.ClosePath(); p.Fill();
                    Line(p, P(c, br, r * 0.5f), P(c, br + Mathf.PI, r * 0.5f), NAV, 3);
                }
                p.fillColor = AMBER; p.BeginPath(); p.MoveTo(c + new Vector2(0, -r * 0.98f)); p.LineTo(c + new Vector2(-6, -r * 1.12f)); p.LineTo(c + new Vector2(6, -r * 1.12f)); p.ClosePath(); p.Fill();
                Line(p, c + new Vector2(0, -r * 0.3f), c + new Vector2(0, r * 0.3f), AMBER, 2);
                Line(p, c + new Vector2(-r * 0.2f, -r * 0.05f), c + new Vector2(r * 0.2f, -r * 0.05f), AMBER, 2);
            } break;
            // 6. Dual tachometer: rotor (needle) and engine (dimmer)
            case Gauges.Tachometer:
            {
                Gauge(m, c, r, "RPM %");
                var ang = Dial(m, c, r, 0, 120, -2.4f, 2.4f, 10, 20, v => v.ToString("0"));
                DialArc(p, c, r * 0.93f, ang(ac.rpmMin), ang(ac.rpmMax), GREEN, 5);
                DialArc(p, c, r * 0.93f, ang(60), ang(ac.rpmMin), AMBER, 5);
                Needle(p, c, r * 0.7f, ang(Math.Min(120, o.engineRpm)), DIM, 3);
                Needle(p, c, r * 0.88f, ang(Math.Min(120, o.rpm)), o.rpm < ac.rpmMin && s.engine != EngineState.Off ? RED : TICK, 3);
            } break;
            // 7. Torque
            case Gauges.Torque:
            {
                Gauge(m, c, r, "TQ %");
                var ang = Dial(m, c, r, 0, 120, -2.4f, 2.4f, 10, 20, v => v.ToString("0"));
                DialArc(p, c, r * 0.93f, ang(0), ang(ac.torqueMcp), GREEN, 5);
                DialArc(p, c, r * 0.93f, ang(ac.torqueMcp), ang(ac.torqueMax), AMBER, 5);
                Needle(p, c, r * 0.88f, ang(Math.Min(120, o.torque)), o.torque > ac.torqueMcp ? AMBER : TICK);
                Text(m, $"OGE {o.hoverPowerOGE:0}%", c + new Vector2(0, r * 0.64f), r * 0.13f, DIM);
            } break;
            // 8. Fuel
            case Gauges.Fuel:
            {
                Gauge(m, c, r, "FUEL KG", 0.6f);
                var ang = Dial(m, c, r, 0, 1, -2.0f, 2.0f, 0.25, 0.5, v => v == 0 ? "E" : v == 1 ? "F" : "½");
                var frac = s.fuel / ac.fuelCapacity;
                Readout(m, s.fuel.ToString(ac.fuelCapacity < 200 ? "F1" : "F0"), c + new Vector2(0, r * 0.36f), r * 0.17f, TICK);
                Needle(p, c, r * 0.86f, ang(frac), frac < 0.15 ? AMBER : TICK);
            } break;
            // 9. Engine temperature (CHT for a piston, TOT for a turbine)
            case Gauges.EngineTemp:
            {
                var lim = FlightModel.TempLimitsOf(ac);
                Gauge(m, c, r, $"{lim.label} °C");
                double lo = ac.engines == 1 ? 0 : 200, hi = ac.engines == 1 ? 300 : 900;
                var ang = Dial(m, c, r, lo, hi, -2.3f, 2.3f, ac.engines == 1 ? 25 : 50, ac.engines == 1 ? 100 : 200, v => v.ToString("0"));
                DialArc(p, c, r * 0.93f, ang(lim.min), ang(lim.caution), GREEN, 5);
                DialArc(p, c, r * 0.93f, ang(lim.caution), ang(lim.max), AMBER, 5);
                Line(p, P(c, ang(lim.max), r * 0.8f), P(c, ang(lim.max), r * 0.98f), RED, 3);
                Needle(p, c, r * 0.86f, ang(Math.Max(lo, Math.Min(hi, s.engineTemp))), s.engineTemp > lim.max ? RED : s.engineTemp > lim.caution ? AMBER : TICK);
            } break;
            // 10. Oil pressure and throttle position
            case Gauges.Oil:
            {
                Gauge(m, c, r, "OIL %");
                var ang = Dial(m, c, r, 0, 120, -2.3f, 2.3f, 10, 40, v => v.ToString("0"));
                DialArc(p, c, r * 0.93f, ang(25), ang(40), AMBER, 5);
                DialArc(p, c, r * 0.93f, ang(40), ang(110), GREEN, 5);
                Needle(p, c, r * 0.86f, ang(Math.Min(120, o.oilPress)), o.oilPress < 25 && s.engine == EngineState.Running ? RED : TICK);
                Text(m, $"THR {s.sys.throttle * 100:0}%", c + new Vector2(0, r * 0.64f), r * 0.13f, DIM);
                if (!s.sys.governor) Text(m, "GOV OFF", c + new Vector2(0, r * 0.8f), r * 0.13f, AMBER);
            } break;
            }
        }
    }
}
