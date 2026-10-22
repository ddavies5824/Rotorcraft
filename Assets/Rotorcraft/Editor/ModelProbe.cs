// Reports where an imported aircraft's marker nodes ended up, to check the importer's axis conversion.
//   Unity.exe -batchmode -projectPath <project> -executeMethod Rotorcraft.EditorTools.ModelProbe.Report -quit
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Rotorcraft.EditorTools
{
    public static class ModelProbe
    {
        [MenuItem("Rotorcraft/Probe Kestrel model")]
        public static void Report()
        {
            var sb = new StringBuilder();
            var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Rotorcraft/Models/kestrel_k2.glb");
            if (go == null) sb.AppendLine("kestrel_k2.glb did not import as a GameObject");
            else
            {
                sb.AppendLine($"root {go.name} children {go.transform.childCount}");
                foreach (var name in new[] { "Kestrel K2", "blades", "tail_rotor", "eye", "door_r", "light_navR", "light_landing", "body" })
                {
                    var t = Find(go.transform, name);
                    sb.AppendLine(t == null ? $"{name}: missing" : $"{name}: path {Path(t)} local {t.localPosition:F3} world {t.position:F3} rot {t.localRotation.eulerAngles:F1}");
                }
                var b = new Bounds();
                var first = true;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
                sb.AppendLine($"renderers {go.GetComponentsInChildren<Renderer>(true).Length} bounds {b.min:F2} .. {b.max:F2}");
                var mr = go.GetComponentInChildren<MeshRenderer>();
                if (mr != null) sb.AppendLine($"first material {mr.sharedMaterial?.name} shader {mr.sharedMaterial?.shader?.name}");
                var mf = go.GetComponentInChildren<MeshFilter>();
                if (mf != null) sb.AppendLine($"first mesh has colors: {mf.sharedMesh.colors32?.Length > 0}");
            }
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/model-probe.txt", sb.ToString());
            Debug.Log(sb.ToString());
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = Find(c, name); if (f != null) return f; }
            return null;
        }
        static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
    }
}
