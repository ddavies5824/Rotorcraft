// What stands out to the thermal camera: people white hot, whatever the light, and running engines warm. Each swaps its materials
// for a plain glowing one while the camera is on (the browser game drew them through its "heat" material the same way).
using System.Collections.Generic;
using UnityEngine;

namespace Rotorcraft.Presentation
{
    public sealed class HeatSource : MonoBehaviour
    {
        public const float Body = 2.2f, Engine = 0.6f;

        static readonly List<HeatSource> All = new List<HeatSource>();
        static readonly Dictionary<float, Material> Hot = new Dictionary<float, Material>();
        static bool thermal;

        float heat;
        bool shown;
        Renderer[] rends;
        Material[][] own;

        /// <summary>Mark a finished object, once its own materials are set, as this hot.</summary>
        public static void Add(GameObject g, float heat)
        {
            if (g == null) return;
            var h = g.AddComponent<HeatSource>();
            h.heat = heat;
            All.Add(h);
            h.Show(thermal);
        }

        /// <summary>Hidden ones change too: a waving figure swaps between two frames, one of them always hidden.</summary>
        public static void SetThermal(bool on)
        {
            thermal = on;
            All.RemoveAll(h => h == null);
            foreach (var h in All) h.Show(on);
        }

        void OnDestroy() => All.Remove(this);

        void Show(bool on)
        {
            if (on == shown) return;
            var hot = on ? MaterialFor(heat) : null;
            if (on && hot == null) return;
            shown = on;
            if (rends == null) rends = GetComponentsInChildren<Renderer>(true);
            if (on) own = new Material[rends.Length][];
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null) continue;
                if (!on) { rends[i].sharedMaterials = own[i]; continue; }
                own[i] = rends[i].sharedMaterials;
                var swapped = new Material[own[i].Length];
                for (int k = 0; k < swapped.Length; k++) swapped[k] = hot;
                rends[i].sharedMaterials = swapped;
            }
        }

        static Material MaterialFor(float heat)
        {
            if (Hot.TryGetValue(heat, out var m) && m != null) return m;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return null;
            m = new Material(shader) { name = "Thermal " + heat };
            m.SetColor("_BaseColor", new Color(heat, heat, heat));
            return Hot[heat] = m;
        }
    }
}
