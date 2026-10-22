// The in-flight display while a mission runs: flight data on the left, the mission's items with the active
// one's progress on the right, the crew and tower messages at the bottom, and the debrief when the flight ends.
// Plain IMGUI for now; the finished HUD will be built with UI Toolkit.
using System.Linq;
using System.Text;
using Rotorcraft.Game;
using Rotorcraft.Sim;
using UnityEngine;

namespace Rotorcraft.Presentation
{
    public sealed class FlightHud : MonoBehaviour
    {
        public HelicopterController heli;
        public MissionDirector mission;
        public bool showHelp = true;

        GUIStyle label, small, title, box;
        readonly StringBuilder sb = new StringBuilder();

        void Update()
        {
            if (UnityEngine.InputSystem.Keyboard.current?.f2Key.wasPressedThisFrame == true) showHelp = !showHelp;
        }

        void Styles()
        {
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true, wordWrap = true };
            label.normal.textColor = Color.white;
            small = new GUIStyle(label) { fontSize = 13 };
            title = new GUIStyle(label) { fontSize = 22, fontStyle = FontStyle.Bold };
            box = new GUIStyle(GUI.skin.box);
        }

        static string Col(MessageKind k) => k == MessageKind.Warning ? "#ff7070" : k == MessageKind.Caution ? "#ffd070" : k == MessageKind.Good ? "#90ee90" : "#c8dcff";

        void Panel(Rect r, string text, GUIStyle st)
        {
            var c = new GUIContent(text);
            var h = st.CalcHeight(c, r.width - 20);
            GUI.Box(new Rect(r.x, r.y, r.width, h + 16), GUIContent.none, box);
            GUI.Label(new Rect(r.x + 10, r.y + 8, r.width - 20, h), c, st);
        }

        void OnGUI()
        {
            if (heli == null || heli.State == null) return;
            Styles();
            var s = heli.State; var o = s.@out; var c = heli.CurrentControls;
            var rt = mission != null ? mission.Runtime : null;

            // ---- flight data
            sb.Clear();
            sb.AppendLine($"<b>{heli.Aircraft.name}</b>   {heli.assist} handling");
            sb.AppendLine($"RPM {o.rpm:F0}%   TQ {o.torque:F0}%   {FlightModel.TempLimitsOf(heli.Aircraft).label} {o.engineTemp:F0}°C   Fuel {s.fuel:F0} kg");
            sb.AppendLine($"IAS {o.iasKt:F0} kt   GS {o.gsKt:F0} kt   VS {o.vsFpm:F0} fpm");
            sb.AppendLine($"ALT {o.altFt:F0} ft   AGL {o.aglM * SimMath.FT:F0} ft   HDG {o.hdg:000}°");
            sb.AppendLine($"Lever {c.collective * 100:F0}%   Engine {s.engine}   BCN {(s.sys.beacon ? "on" : "off")}   Throttle {(s.sys.throttle > 0.9 ? "open" : s.sys.throttle < 0.1 ? "idle" : "moving")}");
            if (o.vrs > 0.2) sb.AppendLine("<color=#ff6060>VORTEX RING</color>");
            if (s.engine == EngineState.Running && o.rpm < heli.Aircraft.rpmMin && !s.onGround) sb.AppendLine("<color=#ff6060>LOW ROTOR RPM</color>");
            if (heli.Paused && mission?.Result == null) sb.AppendLine("<color=#ffd060>PAUSED</color>");
            foreach (var m in heli.Messages) sb.AppendLine($"<color=#a0a0a0>{m}</color>");
            Panel(new Rect(10, 10, 470, 0), sb.ToString(), small);

            if (rt == null) return;

            // ---- mission items
            sb.Clear();
            sb.AppendLine($"<b>{mission.Def.title}</b>");
            foreach (var item in rt.objectives)
            {
                var mark = item.status == ObjectiveStatus.Complete ? "<color=#90ee90>✓</color>" : item.status == ObjectiveStatus.Failed ? "<color=#ff7070>✗</color>" : item.status == ObjectiveStatus.Active ? "<color=#ffd070>▶</color>" : "<color=#808080>·</color>";
                var text = item.status == ObjectiveStatus.Active ? $"<b>{item.def.label}</b>" : item.status == ObjectiveStatus.Pending ? $"<color=#a0a0a0>{item.def.label}</color>" : item.def.label;
                sb.AppendLine($"{mark} {text}");
                if (item.status == ObjectiveStatus.Active && !string.IsNullOrEmpty(item.detail)) sb.AppendLine($"     <color=#c8dcff>{item.detail}</color>{(item.progress > 0 && item.progress < 1 ? $"  {item.progress * 100:F0}%" : "")}");
            }
            var nav = rt.NavTargetNow();
            if (nav != null)
            {
                var (brg, dist) = World.WorldMap.BearingDistance(s.pos.x, s.pos.z, nav.x, nav.z);
                sb.AppendLine($"<color=#a0a0a0>→ {nav.name}: {brg:000}°, {(dist < 1000 ? $"{dist:F0} m" : $"{dist / 1852:F1} nm")}</color>");
            }
            if (rt.status == MissionStatus.Complete || rt.status == MissionStatus.Failed) sb.AppendLine("<color=#ffd070>Enter: end the flight and see the debrief</color>");
            Panel(new Rect(Screen.width - 470, 10, 460, 0), sb.ToString(), small);

            // ---- messages
            sb.Clear();
            foreach (var m in rt.messages.Skip(System.Math.Max(0, rt.messages.Count - 4))) sb.AppendLine($"<color={Col(m.kind)}><b>{m.from}:</b> {m.text}</color>");
            if (showHelp) sb.AppendLine("<color=#909090>B beacon · E engine · R/F throttle · W/S or lever: collective · Enter: end flight · Backspace: restart · F1 handling · F2 help</color>");
            Panel(new Rect(Screen.width / 2f - 380, Screen.height - 150, 760, 0), sb.ToString(), small);

            if (mission.Result != null) Debrief(mission.Result);
        }

        void Debrief(FlightResult r)
        {
            sb.Clear();
            var verdict = r.outcome == FlightOutcome.Passed ? "<color=#90ee90>PASSED</color>" : r.outcome == FlightOutcome.Completed ? "<color=#90ee90>COMPLETED</color>"
                : r.outcome == FlightOutcome.Crashed ? "<color=#ff7070>CRASHED</color>" : r.outcome == FlightOutcome.Aborted ? "<color=#ffd070>ENDED EARLY</color>" : "<color=#ff7070>NOT PASSED</color>";
            sb.AppendLine($"<size=24><b>{r.missionTitle}</b></size>");
            sb.AppendLine($"<size=20>{verdict}   Overall {r.overall}%</size>");
            sb.AppendLine();
            foreach (var kv in r.scores) sb.AppendLine($"{Assessment.CategoryLabels[kv.Key],-24}  <b>{kv.Value}%</b>");
            sb.AppendLine();
            if (r.bestLanding != null) sb.AppendLine($"Best landing: grade {Assessment.LandingGrade(r.bestLanding.score)}, {r.bestLanding.vsMs * 196.85:F0} ft/min{(r.bestLanding.distM != null ? $", {r.bestLanding.distM:F1} m from the spot" : "")}");
            sb.AppendLine($"Flight time {r.flightSeconds / 60:F1} min · fuel used {r.fuelUsedKg:F1} kg · max altitude {r.maxAltFt:F0} ft");
            foreach (var f in r.failedCriteria) sb.AppendLine($"<color=#ff9090>• {f}</color>");
            foreach (var w in r.warnings) sb.AppendLine($"<color=#ffd090>• {w}</color>");
            foreach (var e in r.exceedances) sb.AppendLine($"<color=#ffd090>• {e}</color>");
            sb.AppendLine();
            foreach (var rec in r.recommendations) sb.AppendLine($"<color=#c8dcff>{rec}</color>");
            sb.AppendLine();
            sb.AppendLine("<color=#a0a0a0>Enter: fly it again</color>");
            var w0 = 640f;
            Panel(new Rect(Screen.width / 2f - w0 / 2, 80, w0, 0), sb.ToString(), label);
        }
    }
}
