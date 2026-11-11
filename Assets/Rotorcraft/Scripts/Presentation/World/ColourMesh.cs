// Geometry coloured per vertex, built the way the browser game builds its scenery (render/meshBuilder.ts): the same
// primitives (box, pitched roof, wall rectangle, ellipsoid, tapered cylinder...) taking the same arguments in the sim's
// axes (x east, y up, z south), so its hedges, walls, clutter and houses port line for line. Vertices are stored in
// Unity's axes relative to an origin. Pure data: tiles are built on worker threads and only made into a Unity mesh on the
// main thread. Drawn with the "Rotorcraft/World Colour" shader (WorldColour.shader), which reads the colour's alpha as
// the browser game does: below 0.03 a window pane, otherwise the gloss.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rotorcraft.Presentation
{
    /// <summary>A point or direction in the sim's axes (x east, y up, z south).</summary>
    public struct P3
    {
        public double x, y, z;
        public P3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public static implicit operator P3((double x, double y, double z) t) => new P3(t.x, t.y, t.z);
        public static P3 operator -(P3 a, P3 b) => new P3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static P3 operator +(P3 a, P3 b) => new P3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static P3 operator *(P3 a, double k) => new P3(a.x * k, a.y * k, a.z * k);
        public static P3 Cross(P3 a, P3 b) => new P3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public double Length => Math.Sqrt(x * x + y * y + z * z);
        public P3 Normalized { get { var l = Length; return l > 0 ? new P3(x / l, y / l, z / l) : new P3(0, 0, 0); } }
    }

    /// <summary>A vertex colour as the browser game writes them: sRGB, alpha the material code.</summary>
    public struct RGBA
    {
        public float r, g, b, a;
        public RGBA(double r, double g, double b, double a) { this.r = (float)r; this.g = (float)g; this.b = (float)b; this.a = (float)a; }
        /// <summary>The browser game's M(): a colour with its gloss (0..1) in the alpha.</summary>
        public static RGBA M(double r, double g, double b, double gloss = 0.1) => new RGBA(r, g, b, 0.08 + 0.92 * gloss);
        /// <summary>Window glass (some rooms lit after dark).</summary>
        public static readonly RGBA Pane = new RGBA(0.03, 0.035, 0.04, 0.01);
        public RGBA Scaled(double k) => new RGBA(r * k, g * k, b * k, a);
        public Color32 ToColor32() => new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(r * 255), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(g * 255), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(b * 255), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255), 0, 255));
    }

    /// <summary>The surface patterns the shader draws (in place of the browser game's material textures).</summary>
    public enum Pattern { None = 0, Stone = 1, Harling = 2, Slate = 3, Cladding = 4, Foliage = 5 }

    public sealed class ColourMesh
    {
        readonly List<Vector3> pos = new List<Vector3>();
        readonly List<Vector3> nrm = new List<Vector3>();
        readonly List<Color32> col = new List<Color32>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> idx = new List<int>();
        /// <summary>The origin in sim coordinates (subtracted in double precision before going to floats).</summary>
        public P3 origin { get; private set; }
        float pattern;

        public ColourMesh(P3 origin) { this.origin = origin; }
        public ColourMesh() : this(new P3(0, 0, 0)) { }

        public int VertexCount => pos.Count;
        public int TriangleCount => idx.Count / 3;
        /// <summary>Unity-space position the mesh's object should stand at.</summary>
        public Vector3 UnityOrigin => new Vector3((float)origin.x, (float)origin.y, (float)-origin.z);

        public ColourMesh Texture(Pattern p) { pattern = (float)p; return this; }
        public ColourMesh ClearTexture() { pattern = 0; return this; }

        public int Vertex(P3 p, P3 n, RGBA c)
        {
            pos.Add(new Vector3((float)(p.x - origin.x), (float)(p.y - origin.y), (float)-(p.z - origin.z)));
            nrm.Add(new Vector3((float)n.x, (float)n.y, (float)-n.z));
            col.Add(c.ToColor32());
            uv.Add(new Vector2(pattern, 0));
            return pos.Count - 1;
        }

        /// <summary>A triangle, its front the side its vertices' normals point to.</summary>
        public void Tri(int a, int b, int c)
        {
            var f = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]);
            var n = nrm[a] + nrm[b] + nrm[c];
            idx.Add(a);
            if (Vector3.Dot(f, n) < 0) { idx.Add(c); idx.Add(b); } else { idx.Add(b); idx.Add(c); }
        }

        public static P3 FaceNormal(P3 a, P3 b, P3 c) => P3.Cross(b - a, c - a).Normalized;

        public ColourMesh Quad(P3 a, P3 b, P3 c, P3 d, RGBA color, P3? normal = null)
        {
            var n = normal ?? FaceNormal(a, b, c);
            var i = Vertex(a, n, color); Vertex(b, n, color); Vertex(c, n, color); Vertex(d, n, color);
            Tri(i, i + 1, i + 2); Tri(i, i + 2, i + 3);
            return this;
        }

        public ColourMesh QuadC(P3 a, P3 b, P3 c, P3 d, RGBA ca, RGBA cb, RGBA cc, RGBA cd, P3? normal = null)
        {
            var n = normal ?? FaceNormal(a, b, c);
            var i = Vertex(a, n, ca); Vertex(b, n, cb); Vertex(c, n, cc); Vertex(d, n, cd);
            Tri(i, i + 1, i + 2); Tri(i, i + 2, i + 3);
            return this;
        }

        /// <summary>Box centred at (cx, cy, cz), sized sx by sy by sz, turned rotY about y; ao darkens the foot of the walls.</summary>
        public ColourMesh Box(double cx, double cy, double cz, double sx, double sy, double sz, RGBA color, double rotY = 0, double ao = 0)
        {
            double hx = sx / 2, hy = sy / 2, hz = sz / 2, c = Math.Cos(rotY), s = Math.Sin(rotY);
            P3 P(double x, double y, double z) => new P3(cx + x * c + z * s, cy + y, cz - x * s + z * c);
            P3 N(double x, double y, double z) => new P3(x * c + z * s, y, -x * s + z * c);
            P3 v0 = P(-hx, -hy, hz), v1 = P(hx, -hy, hz), v2 = P(hx, hy, hz), v3 = P(-hx, hy, hz), v4 = P(-hx, -hy, -hz), v5 = P(hx, -hy, -hz), v6 = P(hx, hy, -hz), v7 = P(-hx, hy, -hz);
            var lo = new RGBA(color.r * (1 - ao), color.g * (1 - ao), color.b * (1 - ao), color.a);
            QuadC(v0, v1, v2, v3, lo, lo, color, color, N(0, 0, 1));
            QuadC(v5, v4, v7, v6, lo, lo, color, color, N(0, 0, -1));
            QuadC(v4, v0, v3, v7, lo, lo, color, color, N(-1, 0, 0));
            QuadC(v1, v5, v6, v2, lo, lo, color, color, N(1, 0, 0));
            Quad(v3, v2, v6, v7, color, N(0, 1, 0));
            Quad(v4, v5, v1, v0, color, N(0, -1, 0));
            return this;
        }

        /// <summary>Pitched roof: ridge along local z, eaves overhang included in sx and sz; the gable ends in `gable`.</summary>
        public ColourMesh Roof(double cx, double eaveY, double cz, double sx, double sz, double rise, RGBA color, double rotY = 0, RGBA? gable = null)
        {
            double c = Math.Cos(rotY), s = Math.Sin(rotY);
            P3 P(double x, double y, double z) => new P3(cx + x * c + z * s, eaveY + y, cz - x * s + z * c);
            double hx = sx / 2, hz = sz / 2;
            P3 a = P(-hx, 0, hz), b = P(hx, 0, hz), r1 = P(0, rise, hz), r0 = P(0, rise, -hz), a0 = P(-hx, 0, -hz), b0 = P(hx, 0, -hz);
            Quad(a, r1, r0, a0, color);
            Quad(b0, r0, r1, b, color);
            var g = gable ?? color;
            var n1 = FaceNormal(a, b, r1);
            var i = Vertex(a, n1, g); Vertex(b, n1, g); Vertex(r1, n1, g); Tri(i, i + 1, i + 2);
            var n2 = FaceNormal(b0, a0, r0);
            var j = Vertex(b0, n2, g); Vertex(a0, n2, g); Vertex(r0, n2, g); Tri(j, j + 1, j + 2);
            return this;
        }

        /// <summary>Vertical rectangle on a wall, centre (x, y, z), facing `face` (radians, 0 = +z).</summary>
        public ColourMesh VRect(double x, double y, double z, double w, double h, double face, RGBA color)
        {
            double nx = Math.Sin(face), nz = Math.Cos(face), tx = Math.Cos(face), tz = -Math.Sin(face);
            const double o = 0.04;
            P3 P(double u, double v) => new P3(x + nx * o + tx * u, y + v, z + nz * o + tz * u);
            return Quad(P(-w / 2, -h / 2), P(w / 2, -h / 2), P(w / 2, h / 2), P(-w / 2, h / 2), color, new P3(nx, 0, nz));
        }

        public ColourMesh Ellipsoid(P3 c, P3 r, RGBA color, int segU = 14, int segV = 9)
        {
            var b = VertexCount;
            for (int j = 0; j <= segV; j++)
            {
                var th = (double)j / segV * Math.PI;
                for (int i = 0; i <= segU; i++)
                {
                    var ph = (double)i / segU * Math.PI * 2;
                    double nx = Math.Sin(th) * Math.Cos(ph), ny = Math.Cos(th), nz = Math.Sin(th) * Math.Sin(ph);
                    Vertex(new P3(c.x + nx * r.x, c.y + ny * r.y, c.z + nz * r.z), new P3(nx / r.x, ny / r.y, nz / r.z).Normalized, color);
                }
            }
            for (int j = 0; j < segV; j++)
                for (int i = 0; i < segU; i++)
                {
                    int a = b + j * (segU + 1) + i, k = a + segU + 1;
                    Tri(a, a + 1, k); Tri(a + 1, k + 1, k);
                }
            return this;
        }

        /// <summary>Tapered cylinder between two points.</summary>
        public ColourMesh Cylinder(P3 p0, P3 p1, double r0, double r1, RGBA color, int seg = 10, bool caps = true)
        {
            var d = (p1 - p0).Normalized;
            P3 tmp = Math.Abs(d.y) < 0.9 ? new P3(0, 1, 0) : new P3(1, 0, 0);
            var e1 = P3.Cross(d, tmp).Normalized; var e2 = P3.Cross(d, e1);
            var b = VertexCount;
            for (int i = 0; i <= seg; i++)
            {
                double a = (double)i / seg * Math.PI * 2, ca = Math.Cos(a), sa = Math.Sin(a);
                var n = e1 * ca + e2 * sa;
                Vertex(p0 + n * r0, n, color);
                Vertex(p1 + n * r1, n, color);
            }
            for (int i = 0; i < seg; i++) { var a = b + i * 2; Tri(a, a + 2, a + 1); Tri(a + 1, a + 2, a + 3); }
            if (caps) { Disc(p0, d * -1, r0, color, seg); if (r1 > 0) Disc(p1, d, r1, color, seg); }
            return this;
        }

        public ColourMesh Disc(P3 c, P3 n, double r, RGBA color, int seg = 16)
        {
            P3 tmp = Math.Abs(n.y) < 0.9 ? new P3(0, 1, 0) : new P3(1, 0, 0);
            var e1 = P3.Cross(n, tmp).Normalized; var e2 = P3.Cross(n, e1);
            var ci = Vertex(c, n, color);
            for (int i = 0; i <= seg; i++)
            {
                var a = (double)i / seg * Math.PI * 2;
                Vertex(c + (e1 * Math.Cos(a) + e2 * Math.Sin(a)) * r, n, color);
            }
            for (int i = 0; i < seg; i++) Tri(ci, ci + 2 + i, ci + 1 + i);
            return this;
        }

        /// <summary>Horizontal rectangle turned rotY about y.</summary>
        public ColourMesh FlatRect(double cx, double y, double cz, double w, double d, RGBA color, double rotY = 0)
        {
            double c = Math.Cos(rotY), s = Math.Sin(rotY);
            P3 P(double x, double z) => new P3(cx + x * c + z * s, y, cz - x * s + z * c);
            return Quad(P(-w / 2, d / 2), P(w / 2, d / 2), P(w / 2, -d / 2), P(-w / 2, -d / 2), color, new P3(0, 1, 0));
        }

        /// <summary>Clear everything (the origin stays).</summary>
        public void Clear() { pos.Clear(); nrm.Clear(); col.Clear(); uv.Clear(); idx.Clear(); pattern = 0; }

        /// <summary>Empty it for reuse about a new origin, keeping the space its lists have grown to (no garbage for the next tile).</summary>
        public ColourMesh Reuse(P3 newOrigin) { Clear(); origin = newOrigin; return this; }

        /// <summary>Make the Unity mesh (main thread only); null when empty.</summary>
        public Mesh ToMesh(string name)
        {
            if (pos.Count == 0) return null;
            var m = new Mesh { name = name, indexFormat = pos.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(pos); m.SetNormals(nrm); m.SetColors(col); m.SetUVs(0, uv);
            m.SetTriangles(idx, 0, true);
            return m;
        }

        static Material material;
        /// <summary>The shared material for everything built here.</summary>
        public static Material Material
        {
            get
            {
                if (material == null)
                {
                    var shader = Shader.Find("Rotorcraft/World Colour");
                    material = shader != null ? new Material(shader) { name = "World colour" } : new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "World colour (stand-in)" };
                }
                return material;
            }
        }

        /// <summary>Make the mesh and stand it under parent with the shared material; null when empty.</summary>
        public GameObject Attach(string name, Transform parent, bool shadows = true)
        {
            var mesh = ToMesh(name);
            if (mesh == null) return null;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = UnityOrigin;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Material;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.lightProbeUsage = LightProbeUsage.Off;
            return go;
        }
    }
}
