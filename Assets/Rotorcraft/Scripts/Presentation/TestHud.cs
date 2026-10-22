// A plain text readout for the Phase 1 test scene, until the real HUD is built with UI Toolkit.
using System.Text;
using Rotorcraft.Sim;
using UnityEngine;

namespace Rotorcraft.Presentation
{
    public sealed class TestHud : MonoBehaviour
    {
        public HelicopterController target;
        public bool showHelp = true;
        GUIStyle style, box;
        readonly StringBuilder sb = new StringBuilder();

        void Update()
        {
            if (UnityEngine.InputSystem.Keyboard.current?.f2Key.wasPressedThisFrame == true) showHelp = !showHelp;
        }

        void OnGUI()
        {
            if (target == null || target.State == null) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };
                style.normal.textColor = Color.white;
                box = new GUIStyle(GUI.skin.box);
            }
            var s = target.State; var o = s.@out; var c = target.CurrentControls;
            sb.Clear();
            sb.AppendLine($"<b>{target.Aircraft.name}</b>   {target.assist} handling   {target.ControllerName}");
            sb.AppendLine($"Engine {s.engine}   RPM {o.rpm:F0}%   Torque {o.torque:F0}%   {FlightModel.TempLimitsOf(target.Aircraft).label} {o.engineTemp:F0}°C");
            sb.AppendLine($"IAS {o.iasKt:F0} kt   GS {o.gsKt:F0} kt   VS {o.vsFpm:F0} ft/min");
            sb.AppendLine($"Height {o.aglM * SimMath.FT:F0} ft AGL   HDG {o.hdg:000}°   Fuel {s.fuel:F0} kg");
            sb.AppendLine($"Collective {c.collective * 100:F0}%   Cyclic {c.cyclicPitch:+0.00;-0.00} / {c.cyclicRoll:+0.00;-0.00}   Pedals {c.pedals:+0.00;-0.00}" + (target.Input.Trimmed ? "   TRIM" : ""));
            if (o.vrs > 0.2) sb.AppendLine("<color=#ff6060>VORTEX RING</color>");
            if (s.engine == EngineState.Running && o.rpm < target.Aircraft.rpmMin && !s.onGround) sb.AppendLine("<color=#ff6060>LOW ROTOR RPM</color>");
            if (target.Paused) sb.AppendLine("<color=#ffd060>PAUSED</color>");
            foreach (var m in target.Messages) sb.AppendLine($"<color=#c0d8ff>{m}</color>");
            if (showHelp)
            {
                sb.AppendLine();
                sb.AppendLine("<color=#a0a0a0>Stick: cyclic · twist: pedals · throttle lever: collective · hat: look · trigger: trim");
                sb.AppendLine("Keys: W/S collective · arrows cyclic · A/D pedals · E engine · R/F throttle · T/U trim · C camera");
                sb.AppendLine("F1 handling · Backspace reset · P pause · F2 hide help</color>");
            }
            var content = new GUIContent(sb.ToString());
            var h = style.CalcHeight(content, 720);
            GUI.Box(new Rect(10, 10, 740, h + 16), GUIContent.none, box);
            GUI.Label(new Rect(20, 18, 720, h), content, style);
        }
    }
}
