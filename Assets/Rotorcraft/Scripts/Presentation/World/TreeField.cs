// Draws the region's trees with GPU instancing. Unity's terrain trees draw every tree with its own draw call,
// which costs hundreds of milliseconds a frame with tens of thousands of trees; here the trees are kept in 256 m
// cells, the cells in view are gathered each frame and each tree kind is drawn in a handful of instanced calls:
// the full Blender tree near the camera, a low crown (ellipsoid or cone, a few dozen triangles) further out.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rotorcraft.Presentation
{
    public sealed class TreeField : MonoBehaviour
    {
        public sealed class Kind
        {
            public string name;
            public Mesh near, far;
            public Material[] nearMats, farMats;
        }

        sealed class Cell
        {
            public Bounds bounds;
            public List<Matrix4x4>[] trees;            // per kind
        }

        [Tooltip("Full trees out to this distance (m); low crowns beyond.")]
        public float nearDistance = 450;
        [Tooltip("No trees beyond this distance (m).")]
        public float farDistance = 3200;
        [Tooltip("Low crowns cast shadows too (costs a second pass over the far trees).")]
        public bool farShadows = false;

        const float CellSize = 256;
        readonly List<Kind> kinds = new List<Kind>();
        readonly Dictionary<long, Cell> cells = new Dictionary<long, Cell>();
        List<Matrix4x4>[] nearBuf, farBuf;
        RenderParams[][] nearParams, farParams;
        readonly Plane[] planes = new Plane[6];
        public int Count { get; private set; }
        public int DrawnNear { get; private set; }
        public int DrawnFar { get; private set; }

        public int AddKind(Kind k) { kinds.Add(k); return kinds.Count - 1; }

        public void Add(int kind, Vector3 pos, float yawDeg, Vector3 scale)
        {
            var cx = Mathf.FloorToInt(pos.x / CellSize); var cz = Mathf.FloorToInt(pos.z / CellSize);
            var key = ((long)cx << 32) ^ (uint)cz;
            if (!cells.TryGetValue(key, out var c))
            {
                cells[key] = c = new Cell { trees = new List<Matrix4x4>[kinds.Count] };
                c.bounds = new Bounds(new Vector3((cx + 0.5f) * CellSize, pos.y, (cz + 0.5f) * CellSize), new Vector3(CellSize, 1, CellSize));
            }
            if (c.trees.Length < kinds.Count) System.Array.Resize(ref c.trees, kinds.Count);
            (c.trees[kind] ??= new List<Matrix4x4>()).Add(Matrix4x4.TRS(pos, Quaternion.Euler(0, yawDeg, 0), scale));
            // the cell's box covers every tree in it, with room for crowns and their shadows
            c.bounds.Encapsulate(pos + Vector3.up * (scale.y * 1.2f + 30));
            c.bounds.Encapsulate(pos - Vector3.up * 2);
            Count++;
        }

        /// <summary>Call once every tree is added.</summary>
        public void Finish()
        {
            foreach (var c in cells.Values) c.bounds.Expand(new Vector3(40, 0, 40));
            nearBuf = new List<Matrix4x4>[kinds.Count]; farBuf = new List<Matrix4x4>[kinds.Count];
            nearParams = new RenderParams[kinds.Count][]; farParams = new RenderParams[kinds.Count][];
            for (int k = 0; k < kinds.Count; k++)
            {
                nearBuf[k] = new List<Matrix4x4>(4096); farBuf[k] = new List<Matrix4x4>(4096);
                nearParams[k] = Params(kinds[k].nearMats, ShadowCastingMode.On);
                farParams[k] = Params(kinds[k].farMats, farShadows ? ShadowCastingMode.On : ShadowCastingMode.Off);
            }
        }

        RenderParams[] Params(Material[] mats, ShadowCastingMode shadows)
        {
            var r = new RenderParams[mats.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                mats[i].enableInstancing = true;
                r[i] = new RenderParams(mats[i])
                {
                    shadowCastingMode = shadows, receiveShadows = true, layer = gameObject.layer,
                    worldBounds = new Bounds(Vector3.zero, Vector3.one * 100000),
                    lightProbeUsage = LightProbeUsage.Off, reflectionProbeUsage = ReflectionProbeUsage.BlendProbes,
                };
            }
            return r;
        }

        void Update()
        {
            var cam = Camera.main;
            if (cam == null || nearBuf == null) return;
            // a headset's two eyes see more than the camera's own frustum says: cull with a wide one so no trees go missing at the edges
            if (Vr.On) GeometryUtility.CalculateFrustumPlanes(Matrix4x4.Perspective(140, 1, cam.nearClipPlane, cam.farClipPlane) * cam.worldToCameraMatrix, planes);
            else GeometryUtility.CalculateFrustumPlanes(cam, planes);
            for (int k = 0; k < kinds.Count; k++) { nearBuf[k].Clear(); farBuf[k].Clear(); }
            var eye = cam.transform.position;
            float near2 = nearDistance * nearDistance, far2 = farDistance * farDistance;
            foreach (var c in cells.Values)
            {
                var d2 = c.bounds.SqrDistance(eye);
                if (d2 > far2 || !GeometryUtility.TestPlanesAABB(planes, c.bounds)) continue;
                var buf = d2 < near2 ? nearBuf : farBuf;
                for (int k = 0; k < c.trees.Length; k++) if (c.trees[k] != null) buf[k].AddRange(c.trees[k]);
            }
            int dn = 0, df = 0;
            for (int k = 0; k < kinds.Count; k++)
            {
                dn += Draw(kinds[k].near, nearParams[k], nearBuf[k]);
                df += Draw(kinds[k].far, farParams[k], farBuf[k]);
            }
            DrawnNear = dn; DrawnFar = df;
        }

        static int Draw(Mesh mesh, RenderParams[] rp, List<Matrix4x4> list)
        {
            if (list.Count == 0 || mesh == null) return 0;
            for (int s = 0; s < rp.Length && s < mesh.subMeshCount; s++) Graphics.RenderMeshInstanced(rp[s], mesh, s, list);
            return list.Count;
        }

        /// <summary>A low stand-in for a tree seen from afar: the crown as an ellipsoid (broadleaf) or a cone
        /// (conifer) filling the leaf geometry's box, in the leaf material. Null if the tree has no leaves.</summary>
        public static Mesh FarCrown(Mesh full, int leafSubmesh, bool conifer)
        {
            var verts = full.vertices; var idx = full.GetIndices(leafSubmesh);
            if (idx.Length == 0) return null;
            var b = new Bounds(verts[idx[0]], Vector3.zero);
            foreach (var i in idx) b.Encapsulate(verts[i]);
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            const int seg = 8;
            if (conifer)
            {
                float r = Mathf.Max(b.extents.x, b.extents.z), y0 = b.min.y, y1 = b.max.y, slope = r / (y1 - y0);
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i * Mathf.PI * 2 / seg, a1 = (i + 1) * Mathf.PI * 2 / seg, am = (a0 + a1) / 2;
                    var p0 = new Vector3(b.center.x + Mathf.Cos(a0) * r, y0, b.center.z + Mathf.Sin(a0) * r);
                    var p1 = new Vector3(b.center.x + Mathf.Cos(a1) * r, y0, b.center.z + Mathf.Sin(a1) * r);
                    var top = new Vector3(b.center.x, y1, b.center.z);
                    var nm = new Vector3(Mathf.Cos(am), slope, Mathf.Sin(am)).normalized;
                    int s0 = v.Count;
                    v.Add(p0); v.Add(top); v.Add(p1);
                    n.Add(new Vector3(Mathf.Cos(a0), slope, Mathf.Sin(a0)).normalized); n.Add(nm); n.Add(new Vector3(Mathf.Cos(a1), slope, Mathf.Sin(a1)).normalized);
                    t.Add(s0); t.Add(s0 + 1); t.Add(s0 + 2);
                }
            }
            else
            {
                // a lumpy, slightly tall crown (a smooth squat ellipsoid reads as a pebble from the air)
                const int rings = 6, ring = seg + 1;
                var e = new Vector3(b.extents.x * 0.88f, b.extents.y * 1.02f, b.extents.z * 0.88f);
                var rnd = new System.Random(full.name.GetHashCode());
                var bump = new float[(rings + 1) * ring];
                for (int j = 0; j <= rings; j++) for (int i = 0; i < seg; i++) bump[j * ring + i] = 0.86f + 0.24f * (float)rnd.NextDouble();
                for (int j = 0; j <= rings; j++) bump[j * ring + seg] = bump[j * ring];
                for (int j = 0; j <= rings; j++)
                    for (int i = 0; i <= seg; i++)
                    {
                        float th = Mathf.PI * j / rings, ph = Mathf.PI * 2 * i / seg;
                        var u = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                        var k = j == 0 || j == rings ? 1 : bump[j * ring + i];
                        v.Add(b.center + Vector3.Scale(u, e) * k);
                        n.Add(new Vector3(u.x / e.x, u.y / e.y, u.z / e.z).normalized);
                    }
                for (int j = 0; j < rings; j++)
                    for (int i = 0; i < seg; i++)
                    {
                        int a = j * ring + i, c = a + ring;
                        t.Add(a); t.Add(a + 1); t.Add(c); t.Add(a + 1); t.Add(c + 1); t.Add(c);
                    }
            }
            // the winding must face outwards: flip if a triangle from the middle (never a degenerate pole) faces in
            var q = t.Count / 6 * 3;
            var tri = Vector3.Cross(v[t[q + 1]] - v[t[q]], v[t[q + 2]] - v[t[q]]);
            if (Vector3.Dot(tri, n[t[q]]) < 0) t.Reverse();
            // a four-sided trunk from the ground up into the crown, as its own submesh (bark)
            var trunk = new List<int>();
            float tr = Mathf.Max(0.12f, Mathf.Max(b.extents.x, b.extents.z) * 0.07f), ty = b.min.y + b.extents.y * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                float a0 = (i + 0.5f) * Mathf.PI / 2, a1 = (i + 1.5f) * Mathf.PI / 2;
                var d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)); var d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                var nm = (d0 + d1).normalized;
                int s0 = v.Count;
                v.Add(d0 * tr); v.Add(d1 * tr); v.Add(d1 * tr * 0.6f + Vector3.up * ty); v.Add(d0 * tr * 0.6f + Vector3.up * ty);
                for (int r = 0; r < 4; r++) n.Add(nm);
                // seen from outside, (s0, s0+3, s0+2) and (s0, s0+2, s0+1) wind clockwise
                trunk.Add(s0); trunk.Add(s0 + 3); trunk.Add(s0 + 2); trunk.Add(s0); trunk.Add(s0 + 2); trunk.Add(s0 + 1);
            }
            var check = Vector3.Cross(v[trunk[1]] - v[trunk[0]], v[trunk[2]] - v[trunk[0]]);
            if (Vector3.Dot(check, n[trunk[0]]) < 0) trunk.Reverse();
            var m = new Mesh { name = full.name + " far", subMeshCount = 2 };
            m.SetVertices(v); m.SetNormals(n);
            var cols = new Color[v.Count]; for (int i = 0; i < cols.Length; i++) cols[i] = new Color(0.62f, 0.62f, 0.62f, 1);   // the full tree's crown is shaded inside
            m.colors = cols;
            m.SetUVs(0, new Vector2[v.Count]);
            m.SetTriangles(t, 0); m.SetTriangles(trunk, 1);
            m.RecalculateBounds();
            return m;
        }
    }
}
