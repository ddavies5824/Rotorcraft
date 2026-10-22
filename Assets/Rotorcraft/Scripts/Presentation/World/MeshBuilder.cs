// Collects triangles per material and turns them into one Unity mesh with a submesh for each material.
// Positions are given in the sim's world (x east, y up, z south) and converted to Unity's here.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rotorcraft.Presentation
{
    public sealed class MeshBuilder
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly Dictionary<Material, List<int>> tris = new Dictionary<Material, List<int>>();
        readonly Vector3 origin;

        /// <summary>origin: a Unity-space point subtracted from every vertex (keeps big worlds precise).</summary>
        public MeshBuilder(Vector3 origin = default) { this.origin = origin; }

        public int VertexCount => verts.Count;

        public static Vector3 U(double x, double y, double z) => new Vector3((float)x, (float)y, (float)-z);

        int V(Vector3 p, Vector3 n, Vector2 uv) { verts.Add(p - origin); normals.Add(n); uvs.Add(uv); return verts.Count - 1; }

        List<int> List(Material m) { if (!tris.TryGetValue(m, out var l)) tris[m] = l = new List<int>(); return l; }

        /// <summary>
        /// A flat quad a-b-c-d (in order round its edge) in Unity space, facing the side `facing` points to (up
        /// when not given). Unity draws the side a triangle's Cross(p1 - p0, p2 - p0) points to, so the corners
        /// are wound to suit.
        /// </summary>
        public void Quad(Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 uvScale = default, Vector3? facing = null)
        {
            var n = Vector3.Cross(b - a, c - a).normalized;
            if (Vector3.Dot(n, facing ?? Vector3.up) < 0) { (b, d) = (d, b); n = -n; }
            if (uvScale == default) uvScale = Vector2.one;
            var e1 = (b - a).magnitude * uvScale.x; var e2 = (d - a).magnitude * uvScale.y;
            int i0 = V(a, n, new Vector2(0, 0)), i1 = V(b, n, new Vector2(e1, 0)), i2 = V(c, n, new Vector2(e1, e2)), i3 = V(d, n, new Vector2(0, e2));
            var l = List(m);
            l.Add(i0); l.Add(i1); l.Add(i2); l.Add(i0); l.Add(i2); l.Add(i3);
        }

        public void Tri(Material m, Vector3 a, Vector3 b, Vector3 c, Vector3? facing = null)
        {
            var n = Vector3.Cross(b - a, c - a).normalized;
            if (Vector3.Dot(n, facing ?? Vector3.up) < 0) { (b, c) = (c, b); n = -n; }
            int i0 = V(a, n, Vector2.zero), i1 = V(b, n, Vector2.right), i2 = V(c, n, Vector2.up);
            var l = List(m); l.Add(i0); l.Add(i1); l.Add(i2);
        }

        /// <summary>A box in Unity space: centre, half sizes along its own axes (right, up, forward).</summary>
        public void Box(Material m, Vector3 c, Vector3 half, Quaternion rot, bool bottom = false)
        {
            Vector3 X = rot * new Vector3(half.x, 0, 0), Y = rot * new Vector3(0, half.y, 0), Z = rot * new Vector3(0, 0, half.z);
            Vector3 P(int sx, int sy, int sz) => c + X * sx + Y * sy + Z * sz;
            Quad(m, P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), default, Y);        // top
            if (bottom) Quad(m, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), default, -Y);
            Quad(m, P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1), default, -Z);   // -z
            Quad(m, P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), P(-1, -1, 1), default, Z);        // +z
            Quad(m, P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), P(-1, -1, -1), default, -X);   // -x
            Quad(m, P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), default, X);        // +x
        }

        /// <summary>Make the mesh and the matching material array; null if nothing was added.</summary>
        public (Mesh mesh, Material[] materials)? Build(string name)
        {
            if (verts.Count == 0) return null;
            var mesh = new Mesh { name = name, indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(verts); mesh.SetNormals(normals); mesh.SetUVs(0, uvs);
            var mats = new List<Material>();
            mesh.subMeshCount = tris.Count;
            var k = 0;
            foreach (var kv in tris) { mesh.SetTriangles(kv.Value, k++, false); mats.Add(kv.Key); }
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return (mesh, mats.ToArray());
        }

        /// <summary>Build and attach to a new child object of parent, positioned at the builder's origin.</summary>
        public GameObject Attach(string name, Transform parent, bool shadows = true)
        {
            var b = Build(name);
            if (b == null) return null;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = origin;
            go.AddComponent<MeshFilter>().sharedMesh = b.Value.mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = b.Value.materials;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            go.isStatic = true;
            return go;
        }
    }
}
