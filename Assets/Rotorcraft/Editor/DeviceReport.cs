// Lists the input devices Unity can see and every control on them, so presets can be wired to the exact
// control names a device reports. Run from the menu, or in batch mode:
//   Unity.exe -batchmode -projectPath <project> -executeMethod Rotorcraft.EditorTools.DeviceReport.WriteReport -quit
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine.InputSystem;

namespace Rotorcraft.EditorTools
{
    public static class DeviceReport
    {
        const string ReportPath = "Logs/input-devices.txt";

        [MenuItem("Rotorcraft/Write input device report")]
        public static void WriteReport()
        {
            InputSystem.Update();
            var sb = new StringBuilder();
            sb.AppendLine($"{InputSystem.devices.Count} devices");
            foreach (var d in InputSystem.devices)
            {
                var desc = d.description;
                sb.AppendLine();
                sb.AppendLine($"== {d.displayName}  layout={d.layout}  path={d.path}");
                sb.AppendLine($"   product=\"{desc.product}\" manufacturer=\"{desc.manufacturer}\" interface={desc.interfaceName}");
                if (!string.IsNullOrEmpty(desc.capabilities)) sb.AppendLine($"   capabilities={desc.capabilities}");
                foreach (var c in d.allControls)
                    sb.AppendLine($"   {c.path,-40} {c.layout,-10} \"{c.displayName}\"");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, sb.ToString());
            UnityEngine.Debug.Log($"Input device report written to {Path.GetFullPath(ReportPath)}");
        }
    }
}
