// Live readout of every joystick's axes and buttons, for checking which way each control reads.
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Rotorcraft.EditorTools
{
    public sealed class StickMonitor : EditorWindow
    {
        [MenuItem("Rotorcraft/Joystick monitor")]
        static void Open() => GetWindow<StickMonitor>("Joystick monitor");

        void Update() => Repaint();

        void OnGUI()
        {
            var any = false;
            foreach (var d in InputSystem.devices)
            {
                if (!(d is Joystick)) continue;
                any = true;
                EditorGUILayout.LabelField(d.displayName, EditorStyles.boldLabel);
                foreach (var c in d.allControls)
                {
                    if (c is AxisControl a && !(c.parent is Vector2Control) && !(c.parent is DpadControl))
                        EditorGUILayout.Slider(c.name, a.ReadValue(), -1, 1);
                    else if (c is StickControl s)
                        EditorGUILayout.LabelField(c.name, $"x {s.x.ReadValue():+0.00;-0.00}   y {s.y.ReadValue():+0.00;-0.00}");
                    else if (c is DpadControl h)
                        EditorGUILayout.LabelField(c.name, h.ReadValue().ToString());
                }
                var pressed = "";
                foreach (var c in d.allControls)
                    if (c is ButtonControl b && !(c.parent is Vector2Control) && !(c.parent is DpadControl) && b.isPressed) pressed += c.name + " ";
                EditorGUILayout.LabelField("pressed", pressed.Length > 0 ? pressed : "(none)");
                EditorGUILayout.Space();
            }
            if (!any) EditorGUILayout.HelpBox("No joystick connected.", MessageType.Info);
            EditorGUILayout.HelpBox("If the values don't move, click inside this window first.", MessageType.None);
        }
    }
}
