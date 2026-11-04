// The mission's target marked in the world (the browser game's markers in render/scene.ts): a magenta ring on the
// ground round a landing or hover target, a tall beacon with a ring round it for a waypoint, and a post at each
// corner of a search area. A ring follows the ground rather than lying flat, so a hillside cannot hide half of it,
// except on a raised deck or roof, where it lies on the deck.
using Rotorcraft.Game;
using Rotorcraft.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rotorcraft.Presentation
{
    public sealed class MissionMarkers
    {
        const double FT = 3.28084;
        static readonly Color Tint = new Color(0.88f, 0.36f, 0.78f).linear;   // vertex colours reach the shader as they are, with no conversion

        GameObject root;
        Material material;
        Mesh ringMesh, columnMesh;
        Transform ring, beacon;
        readonly Transform[] posts = new Transform[4];

        // what is on show, so the meshes are only rebuilt when the target changes
        bool hasNav, hasArea;
        ObjectiveKind navKind;
        double navX, navZ, navR, navFt, areaX, areaZ;

        /// <summary>Show this target (null for none) and search area corners (null for none).</summary>
        public void Show(NavTarget nav, (double x, double z)[] corners)
        {
            if (nav == null && corners == null && root == null) return;
            if (root == null && !Make()) return;
            ShowTarget(nav);
            ShowArea(corners);
        }

        public void Clear() => Show(null, null);

        void ShowTarget(NavTarget nav)
        {
            var same = nav == null ? !hasNav : hasNav && nav.kind == navKind && nav.x == navX && nav.z == navZ && nav.radiusM == navR && nav.elevationFt == navFt;
            if (same) return;
            hasNav = nav != null;
            if (nav == null) { ring.gameObject.SetActive(false); beacon.gameObject.SetActive(false); return; }
            navKind = nav.kind; navX = nav.x; navZ = nav.z; navR = nav.radiusM; navFt = nav.elevationFt;

            var y = nav.elevationFt / FT;
            var ground = System.Math.Max(WorldMap.TerrainHeight(nav.x, nav.z), 0);
            var waypoint = nav.kind == ObjectiveKind.Waypoint;
            var onDeck = !waypoint && y > ground + 2;
            if (nav.radiusM > 0.5)
            {
                BuildRing(nav.x, nav.z, nav.radiusM, onDeck ? y : ground, onDeck);
                ring.position = MeshBuilder.U(nav.x, onDeck ? y : ground, nav.z);
                ring.gameObject.SetActive(true);
            }
            else ring.gameObject.SetActive(false);

            beacon.gameObject.SetActive(waypoint);
            if (waypoint)
            {
                var r = (float)System.Math.Max(6, nav.radiusM * 0.08);
                beacon.position = MeshBuilder.U(nav.x, ground, nav.z);
                beacon.localScale = new Vector3(r, 400, r);
            }
        }

        void ShowArea((double x, double z)[] corners)
        {
            var same = corners == null ? !hasArea : hasArea && corners[0].x == areaX && corners[0].z == areaZ;
            if (same) return;
            hasArea = corners != null;
            for (int i = 0; i < posts.Length; i++)
            {
                posts[i].gameObject.SetActive(hasArea);
                if (!hasArea) continue;
                var (x, z) = corners[i];
                posts[i].position = MeshBuilder.U(x, System.Math.Max(WorldMap.TerrainHeight(x, z), 0), z);
                posts[i].localScale = new Vector3(3, 120, 3);
            }
            if (hasArea) { areaX = corners[0].x; areaZ = corners[0].z; }
        }

        bool Make()
        {
            var shader = Shader.Find("Rotorcraft/Mission Marker");
            if (shader == null) { Debug.LogWarning("Mission marker shader not found"); return false; }
            material = new Material(shader) { name = "Mission marker" };
            root = new GameObject("Mission markers");
            ringMesh = new Mesh { name = "Marker ring", indexFormat = IndexFormat.UInt16 };
            columnMesh = Column();
            ring = Part("Ring", ringMesh);
            beacon = Part("Beacon", columnMesh);
            for (int i = 0; i < posts.Length; i++) posts[i] = Part("Search area corner", columnMesh);
            return true;
        }

        Transform Part(string name, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            go.SetActive(false);
            return go.transform;
        }

        /// <summary>A band about 5% of the radius wide, solid at its outer edge and fading away inwards.</summary>
        void BuildRing(double x, double z, double radius, double baseY, bool flat)
        {
            var n = Mathf.Clamp((int)(radius * 0.8), 48, 160);
            var band = Mathf.Clamp((float)radius * 0.05f, 0.5f, 12f);
            var rOut = (float)radius; var rMid = Mathf.Max(0, rOut - band); var rIn = Mathf.Max(0, rOut - band * 2f);
            var lift = flat ? 0.2f : 0.4f + (float)radius * 0.002f;
            var verts = new Vector3[3 * (n + 1)]; var norms = new Vector3[verts.Length]; var cols = new Color[verts.Length];
            var tris = new int[12 * n];
            Color solid = Tint, faint = Tint; solid.a = 0.9f; faint.a = 0;
            for (int i = 0; i <= n; i++)
            {
                var a = i * Mathf.PI * 2 / n;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                // one height across the band: cheap, and a band this narrow lies on what is under its middle
                var h = flat ? 0 : (float)(System.Math.Max(WorldMap.TerrainHeight(x + c * rMid, z + s * rMid), 0) - baseY);
                var y = h + lift;
                verts[i * 3] = new Vector3(c * rOut, y, -s * rOut);
                verts[i * 3 + 1] = new Vector3(c * rMid, y, -s * rMid);
                verts[i * 3 + 2] = new Vector3(c * rIn, y, -s * rIn);
                cols[i * 3] = solid; cols[i * 3 + 1] = solid; cols[i * 3 + 2] = faint;
                norms[i * 3] = norms[i * 3 + 1] = norms[i * 3 + 2] = Vector3.up;
                if (i == n) break;
                for (int k = 0; k < 2; k++)
                {
                    int a0 = i * 3 + k, a1 = a0 + 1, b0 = a0 + 3, b1 = a1 + 3, t = i * 12 + k * 6;
                    tris[t] = a0; tris[t + 1] = a1; tris[t + 2] = b1; tris[t + 3] = a0; tris[t + 4] = b1; tris[t + 5] = b0;
                }
            }
            ringMesh.Clear();
            ringMesh.vertices = verts; ringMesh.normals = norms; ringMesh.colors = cols; ringMesh.triangles = tris;
            ringMesh.RecalculateBounds();
        }

        /// <summary>An open unit column, one wide and one tall from its base, strongest at the foot and fading upwards.</summary>
        static Mesh Column()
        {
            const int N = 24;
            var verts = new Vector3[2 * (N + 1)]; var norms = new Vector3[verts.Length]; var cols = new Color[verts.Length];
            var tris = new int[6 * N];
            Color foot = Tint, top = Tint; foot.a = 0.45f; top.a = 0.04f;
            for (int i = 0; i <= N; i++)
            {
                var a = i * Mathf.PI * 2 / N;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                verts[i * 2] = d; verts[i * 2 + 1] = d + Vector3.up;
                norms[i * 2] = norms[i * 2 + 1] = d;
                cols[i * 2] = foot; cols[i * 2 + 1] = top;
                if (i == N) break;
                int t = i * 6, v = i * 2;
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 3; tris[t + 3] = v; tris[t + 4] = v + 3; tris[t + 5] = v + 2;
            }
            var m = new Mesh { name = "Marker column", vertices = verts, normals = norms, colors = cols, triangles = tris };
            m.RecalculateBounds();
            return m;
        }
    }
}
