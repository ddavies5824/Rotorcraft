// Glowing light points as one mesh for the "Rotorcraft/Light Points" shader: four vertices per light at its
// centre, the corners in UV0 and size, flash mode, period and phase in UV1 (the shader turns each into a
// camera-facing glow of a sensible size in pixels).
using System.Collections.Generic;
using Rotorcraft.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rotorcraft.Presentation
{
    public struct GlowPoint
    {
        public Vector3 pos;
        /// <summary>HDR colour; alpha is how much of it shows by day.</summary>
        public Color color;
        public float size;
        public LightMode mode;
        public float period, phase;

        public static GlowPoint From(LightPoint p) => new GlowPoint
        {
            pos = SimToUnity.Position(new Rotorcraft.Sim.Vec3(p.x, p.y, p.z)),
            color = new Color(p.r, p.g, p.b, p.dayVis), size = p.size, mode = p.mode, period = p.period, phase = p.phase,
        };
    }

    public static class GlowPoints
    {
        static Material material;

        /// <summary>The shared additive material (one is enough: everything varies per vertex).</summary>
        public static Material Material
        {
            get
            {
                if (material == null)
                {
                    var shader = Shader.Find("Rotorcraft/Light Points");
                    if (shader == null) { Debug.LogWarning("Rotorcraft/Light Points shader missing"); return null; }
                    material = new Material(shader) { name = "Light points" };
                    // daylight values until a SkyEnvironment says otherwise (scenes without one still show the lights)
                    if (Shader.GetGlobalFloat("_RcLightExposure") == 0) Shader.SetGlobalFloat("_RcLightExposure", 0.75f);
                }
                return material;
            }
        }

        static readonly List<Vector3> pos = new List<Vector3>();
        static readonly List<Color> col = new List<Color>();
        static readonly List<Vector2> corner = new List<Vector2>();
        static readonly List<Vector4> anim = new List<Vector4>();
        static readonly List<int> tris = new List<int>();
        static readonly Vector2[] Corners = { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };

        /// <summary>Fill a mesh with the points (replacing what it held).</summary>
        public static void Fill(Mesh mesh, IList<GlowPoint> points)
        {
            pos.Clear(); col.Clear(); corner.Clear(); anim.Clear(); tris.Clear();
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                var a = new Vector4(p.size, (float)p.mode, p.period, p.phase);
                int b = pos.Count;
                for (int c = 0; c < 4; c++) { pos.Add(p.pos); col.Add(p.color); corner.Add(Corners[c]); anim.Add(a); }
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2); tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }
            mesh.Clear();
            if (pos.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(pos); mesh.SetColors(col); mesh.SetUVs(0, corner); mesh.SetUVs(1, anim);
            mesh.SetTriangles(tris, 0, false);
            // the quads grow in the shader, so the box must not cull them: make it generous
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 200000);
        }

        /// <summary>A renderer for a set of points under the parent (local positions).</summary>
        public static MeshFilter Make(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = new Mesh { name = name };
            mf.sharedMesh.MarkDynamic();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Material;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return mf;
        }
    }
}
