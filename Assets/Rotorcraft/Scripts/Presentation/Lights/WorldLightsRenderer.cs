// The region's fixed lights (WorldLights): every light point as a glow, and at dusk and night the light pools
// as real point lights, the nearest few dozen to the camera at any time (Forward+ lights them cheaply).
using System.Collections.Generic;
using Rotorcraft.Sim;
using Rotorcraft.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rotorcraft.Presentation
{
    public sealed class WorldLightsRenderer : MonoBehaviour
    {
        [Tooltip("Point lights kept for the nearest light pools.")]
        public int maxLights = 48;
        [Tooltip("Pools further than this are not lit (m).")]
        public float poolRange = 1800;
        [Tooltip("Light intensity per unit of pool colour.")]
        public float poolIntensity = 2.2f;

        readonly List<(Vector3 pos, float radius, Color col, bool nightOnly)> pools = new List<(Vector3, float, Color, bool)>();
        readonly List<Light> lights = new List<Light>();
        readonly List<int> nearest = new List<int>();
        float nextSort;

        public int PointCount { get; private set; }

        public static WorldLightsRenderer Build(Transform parent)
        {
            var go = new GameObject("World lights");
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<WorldLightsRenderer>();
            var (points, pools) = WorldLights.Build();
            var glow = new List<GlowPoint>(points.Count);
            foreach (var p in points) glow.Add(GlowPoint.From(p));
            GlowPoints.Fill(GlowPoints.Make("Light points", go.transform).sharedMesh, glow);
            r.PointCount = glow.Count;
            foreach (var p in pools)
            {
                var c = new Color(p.r, p.g, p.b);
                r.pools.Add((SimToUnity.Position(new Vec3(p.x, p.y, p.z)), (float)p.radius, c, p.nightOnly));
            }
            for (int i = 0; i < r.maxLights; i++)
            {
                var l = new GameObject("Pool light").AddComponent<Light>();
                l.transform.SetParent(go.transform, false);
                l.type = LightType.Point; l.shadows = LightShadows.None; l.renderMode = LightRenderMode.ForcePixel;
                l.enabled = false;
                r.lights.Add(l);
            }
            Debug.Log($"Kinloch lights: {glow.Count} light points, {pools.Count} light pools");
            return r;
        }

        void Update()
        {
            var env = SkyEnvironment.Current;
            float night = env != null ? env.Night : 0;
            var cam = Camera.main;
            if (cam == null) return;
            if (Time.unscaledTime < nextSort && night > 0) { Refresh(night); return; }
            nextSort = Time.unscaledTime + 0.3f;
            // the nearest pools that are lit at this time of day
            nearest.Clear();
            var eye = cam.transform.position;
            float range2 = poolRange * poolRange;
            for (int i = 0; i < pools.Count; i++)
            {
                if (pools[i].nightOnly && night <= 0) continue;
                if ((pools[i].pos - eye).sqrMagnitude < range2) nearest.Add(i);
            }
            nearest.Sort((a, b) => (pools[a].pos - eye).sqrMagnitude.CompareTo((pools[b].pos - eye).sqrMagnitude));
            for (int k = 0; k < lights.Count; k++)
            {
                var l = lights[k];
                if (k >= nearest.Count) { l.enabled = false; continue; }
                var p = pools[nearest[k]];
                l.transform.position = p.pos;
                l.range = p.radius;
                var m = Mathf.Max(p.col.r, Mathf.Max(p.col.g, p.col.b));
                l.color = p.col / Mathf.Max(0.001f, m);
                l.enabled = true;
            }
            Refresh(night);
        }

        void Refresh(float night)
        {
            for (int k = 0; k < lights.Count && k < nearest.Count; k++)
            {
                var p = pools[nearest[k]];
                var m = Mathf.Max(p.col.r, Mathf.Max(p.col.g, p.col.b));
                lights[k].intensity = m * poolIntensity * (p.nightOnly ? night : 1);
            }
        }
    }
}
