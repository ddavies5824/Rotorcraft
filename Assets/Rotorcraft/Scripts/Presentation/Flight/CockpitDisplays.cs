// Live instruments inside the cockpit. Every gauge face and glass screen in the aircraft model shows the real
// simulated readings: round dials (airspeed, altimeter, rotor RPM...) in the Kestrel's panel, and flight, nav and
// engine pages on the twins' screens. The gauges are the panel's vector drawings (InstrumentPanel), rendered
// into one texture by an off-screen UI panel and mapped onto quads laid just over the model's faces.
using System.Collections.Generic;
using System.Linq;
using Rotorcraft.Presentation.UI;
using Rotorcraft.Sim;
using UnityEngine;
using UnityEngine.UIElements;
using G = Rotorcraft.Presentation.UI.InstrumentPanel.Gauges;

namespace Rotorcraft.Presentation
{
    public sealed class CockpitDisplays : MonoBehaviour
    {
        const int ATLAS_W = 2048;
        sealed class Display { public InstrumentPanel panel; public GameObject quad; }
        readonly List<Display> displays = new List<Display>();
        readonly List<Renderer> hidden = new List<Renderer>();
        UIDocument doc;
        PanelSettings ps;
        RenderTexture rt;
        Material screenMat, dialMat;
        float lastSet;

        static readonly (G, Rect)[] PFD =
        {
            (G.Attitude, new Rect(0.25f, 0.09f, 0.5f, 0.56f)), (G.Airspeed, new Rect(0f, 0.12f, 0.25f, 0.38f)), (G.Altimeter, new Rect(0.75f, 0.12f, 0.25f, 0.38f)),
            (G.Tachometer, new Rect(0f, 0.56f, 0.25f, 0.38f)), (G.Compass, new Rect(0.3f, 0.64f, 0.4f, 0.36f)), (G.VerticalSpeed, new Rect(0.75f, 0.56f, 0.25f, 0.38f)),
        };
        static readonly (G, Rect)[] ND = { (G.Compass, new Rect(0.08f, 0.1f, 0.84f, 0.88f)) };
        static readonly (G, Rect)[] ENG =
        {
            (G.Tachometer, new Rect(0f, 0.09f, 0.34f, 0.45f)), (G.Torque, new Rect(0.33f, 0.09f, 0.34f, 0.45f)), (G.EngineTemp, new Rect(0.66f, 0.09f, 0.34f, 0.45f)),
            (G.Fuel, new Rect(0.16f, 0.54f, 0.34f, 0.45f)), (G.Oil, new Rect(0.5f, 0.54f, 0.34f, 0.45f)),
        };

        /// <summary>Screen pages from left to right for a cockpit with n screens.</summary>
        static (string title, (G, Rect)[] layout)[] Pages(int n, bool twin)
        {
            var pfd = ("PFD", PFD); var nd = ("NAV", ND); var eng = (twin ? "ENGINES" : "ENGINE", ENG);
            switch (n)
            {
                case 1: return new[] { pfd };
                case 2: return twin ? new[] { pfd, eng } : new[] { eng, nd };
                case 3: return new[] { pfd, eng, nd };
                case 4: return new[] { pfd, eng, nd, pfd };
                default: return Enumerable.Range(0, n).Select(i => i == n / 2 ? eng : i == 0 || i == n - 1 ? pfd : nd).ToArray();
            }
        }

        public void Clear()
        {
            foreach (var d in displays) if (d.quad != null) Destroy(d.quad);
            displays.Clear();
            foreach (var r in hidden) if (r != null) r.enabled = true;
            hidden.Clear();
            doc?.rootVisualElement?.Clear();
        }

        /// <summary>Find the model's gauge faces and screens and put live instruments on them.</summary>
        public void Bind(Transform model, AircraftConfig ac, ThemeStyleSheet theme, Transform eye)
        {
            Clear();
            if (model == null) return;
            var rends = model.GetComponentsInChildren<MeshRenderer>(true);
            string N(Renderer r) => r.name;
            var faces = rends.Where(r => N(r).StartsWith("Gauge face")).ToList();
            var screens = rends.Where(r => N(r).StartsWith("Screen") && !N(r).StartsWith("Screen bezel")).ToList();
            var needles = rends.Where(r => N(r).StartsWith("Needle:")).ToList();
            if (faces.Count == 0 && screens.Count == 0) return;
            var eyePos = eye != null ? eye.position : model.position + model.up * 1.5f;
            var up = model.up;

            // each face as a plane rectangle facing the pilot
            var planes = new List<(Renderer r, Vector3 c, Vector3 n, Vector3 right, Vector3 upv, float w, float h, bool dial)>();
            // a dial may be modelled in pieces (the attitude indicator's sky and ground halves): touching pieces are one
            var groups = new List<List<MeshRenderer>>();
            foreach (var f in faces)
            {
                var b = f.bounds; b.Expand(0.004f);
                var g = groups.FirstOrDefault(gr => gr.Any(x => x.bounds.Intersects(b)));
                if (g != null) g.Add(f); else groups.Add(new List<MeshRenderer> { f });
            }
            foreach (var s in screens) groups.Add(new List<MeshRenderer> { s });
            foreach (var grp in groups)
            {
                var r = grp[0];
                var vl = new List<Vector3>(); var tl = new List<int>();
                foreach (var part in grp)
                {
                    var mf = part.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    var m = part.transform.localToWorldMatrix;
                    var off = vl.Count;
                    vl.AddRange(mf.sharedMesh.vertices.Select(v => m.MultiplyPoint3x4(v)));
                    tl.AddRange(mf.sharedMesh.triangles.Select(i => i + off));
                }
                if (vl.Count == 0) continue;
                var vs = vl.ToArray();
                var centre = vs.Aggregate(Vector3.zero, (a, b) => a + b) / vs.Length;
                // the face is the largest flat area turned towards the pilot (screens are thin boxes, dials discs)
                var tris = tl.ToArray();
                var areaByDir = new Dictionary<Vector3Int, (Vector3 n, float a)>();
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    var cr = Vector3.Cross(vs[tris[t + 1]] - vs[tris[t]], vs[tris[t + 2]] - vs[tris[t]]);
                    var area = cr.magnitude * 0.5f;
                    if (area < 1e-9f) continue;
                    var tn = cr.normalized;
                    if (Vector3.Dot(tn, eyePos - centre) < 0) tn = -tn;
                    var key = Vector3Int.RoundToInt(tn * 8);
                    areaByDir.TryGetValue(key, out var acc);
                    areaByDir[key] = (acc.n + tn * area, acc.a + area);
                }
                var nrm = areaByDir.Count > 0 ? areaByDir.Values.OrderByDescending(x => x.a).First().n.normalized : (eyePos - centre).normalized;
                var upv = Vector3.ProjectOnPlane(up, nrm).normalized;
                if (upv.sqrMagnitude < 0.5f) upv = Vector3.ProjectOnPlane(model.forward, nrm).normalized;
                var right = Vector3.Cross(upv, -nrm).normalized;
                float r0 = float.MaxValue, r1 = float.MinValue, u0 = float.MaxValue, u1 = float.MinValue, front = float.MinValue;
                foreach (var v in vs)
                {
                    var d = v - centre; var a = Vector3.Dot(d, right); var b = Vector3.Dot(d, upv);
                    r0 = Mathf.Min(r0, a); r1 = Mathf.Max(r1, a); u0 = Mathf.Min(u0, b); u1 = Mathf.Max(u1, b); front = Mathf.Max(front, Vector3.Dot(d, nrm));
                }
                centre += right * (r0 + r1) / 2 + upv * (u0 + u1) / 2 + nrm * front;   // on the front surface
                float fw = r1 - r0, fh = u1 - u0;
                var dial = faces.Contains(r);
                if (dial) { var d = Mathf.Max(fw, fh); fw = fh = d; }
                // radio and nav-box displays (small or letterbox) keep their own glow
                if (!dial && (fw * fh < 0.008f || Mathf.Max(fw, fh) / Mathf.Max(0.001f, Mathf.Min(fw, fh)) > 2.6f)) continue;
                planes.Add((r, centre, nrm, right, upv, fw, fh, dial));
            }
            if (planes.Count == 0) return;

            // what each shows: a dial takes the instrument whose needle it carries; screens get pages left to right
            var assign = new List<(int plane, string title, (G, Rect)[] layout)>();
            var spare = new Queue<G>(new[] { G.Attitude, G.Torque, G.Fuel, G.EngineTemp, G.Oil, G.Compass, G.Tachometer });
            var used = new HashSet<G>();
            for (int i = 0; i < planes.Count; i++)
            {
                if (!planes[i].dial) continue;
                var c = planes[i].c; var rad = Mathf.Max(planes[i].w, planes[i].h) * 0.6f;
                var nd = needles.Where(x => Vector3.Distance(x.bounds.center, c) < rad).OrderBy(x => Vector3.Distance(x.bounds.center, c)).FirstOrDefault();
                G? g = null;
                if (nd != null)
                {
                    var name = N(nd).ToLowerInvariant();
                    g = name.Contains("airspeed") ? G.Airspeed : name.Contains("altimeter") ? G.Altimeter : name.Contains("heading") ? G.Compass : name.Contains("rpm") ? G.Tachometer : name.Contains("vertical") ? G.VerticalSpeed : name.Contains("torque") ? G.Torque : (G?)null;
                }
                while (g == null || used.Contains(g.Value)) { if (spare.Count == 0) { g = G.Attitude; break; } g = spare.Dequeue(); }
                used.Add(g.Value);
                assign.Add((i, null, new[] { (g.Value, new Rect(0, 0, 1, 1)) }));
            }
            var screenIdx = Enumerable.Range(0, planes.Count).Where(i => !planes[i].dial).OrderBy(i => Vector3.Dot(planes[i].c - model.position, model.right)).ToList();
            var pages = Pages(screenIdx.Count, ac.engines > 1);
            for (int k = 0; k < screenIdx.Count; k++) assign.Add((screenIdx[k], pages[k].title, pages[k].layout));

            // the modelled needles and dial markings are replaced by the drawn ones
            foreach (var r in rends)
                if (new[] { "Needle", "Gauge marks", "Gauge arc", "Horizon line", "Bank mark", "Aircraft symbol" }.Any(N(r).StartsWith)) { if (r.enabled) { r.enabled = false; hidden.Add(r); } }

            // pack the displays into one texture (shelves of equal height)
            var regions = new List<RectInt>();
            int x = 0, y = 0, shelf = 0;
            foreach (var (pi, title, _) in assign)
            {
                var pl = planes[pi];
                int h = pl.dial ? 256 : 384;
                int w = pl.dial ? 256 : Mathf.Clamp(Mathf.RoundToInt(h * pl.w / Mathf.Max(0.01f, pl.h)), 160, 900);
                if (x + w > ATLAS_W) { x = 0; y += shelf + 4; shelf = 0; }
                regions.Add(new RectInt(x, y, w, h));
                x += w + 4; shelf = Mathf.Max(shelf, h);
            }
            var atlasH = Mathf.NextPowerOfTwo(y + shelf);
            EnsurePanel(theme, atlasH);

            for (int k = 0; k < assign.Count; k++)
            {
                var (pi, title, layout) = assign[k];
                var pl = planes[pi]; var rg = regions[k];
                var panel = new InstrumentPanel { layout = layout, screenTitle = title };
                panel.style.position = Position.Absolute; panel.style.left = rg.x; panel.style.top = rg.y; panel.style.width = rg.width; panel.style.height = rg.height;
                panel.style.flexGrow = 0;
                doc.rootVisualElement.Add(panel);
                // the quad, a hair in front of the face, in the face's own part so it moves with it
                var size = pl.dial ? new Vector2(Mathf.Max(pl.w, pl.h), Mathf.Max(pl.w, pl.h)) * 1.02f : new Vector2(pl.w, pl.h) * 0.96f;
                var centre = pl.c + pl.n * 0.0008f;
                Vector3 Corner(float sx, float sy) => centre + pl.right * sx * size.x / 2 + pl.upv * sy * size.y / 2;
                float uL = rg.x / (float)ATLAS_W, uR = (rg.x + rg.width) / (float)ATLAS_W;
                float vT = 1 - rg.y / (float)atlasH, vB = 1 - (rg.y + rg.height) / (float)atlasH;
                var parent = pl.r.transform;
                var mesh = new Mesh { name = "Live display" };
                mesh.vertices = new[] { Corner(-1, -1), Corner(1, -1), Corner(1, 1), Corner(-1, 1) }.Select(parent.InverseTransformPoint).ToArray();
                mesh.uv = new[] { new Vector2(uL, vB), new Vector2(uR, vB), new Vector2(uR, vT), new Vector2(uL, vT) };
                mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                // face the pilot whichever way the mirror turned the winding
                if (Vector3.Dot(parent.TransformDirection(mesh.normals[0]), pl.n) < 0) { mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 }; mesh.RecalculateNormals(); }
                var q = new GameObject("Live display: " + (title ?? layout[0].Item1.ToString()));
                q.layer = pl.r.gameObject.layer;
                q.transform.SetParent(parent, false);
                q.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = q.AddComponent<MeshRenderer>();
                mr.sharedMaterial = pl.dial ? dialMat : screenMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                displays.Add(new Display { panel = panel, quad = q });
            }
        }

        void EnsurePanel(ThemeStyleSheet theme, int atlasH)
        {
            if (rt == null || rt.height != atlasH)
            {
                if (rt != null) rt.Release();
                rt = new RenderTexture(ATLAS_W, atlasH, 0, RenderTextureFormat.ARGB32) { name = "Cockpit displays", useMipMap = true, autoGenerateMips = true, anisoLevel = 4, filterMode = FilterMode.Trilinear };
                rt.Create();
            }
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                ps.name = "Cockpit displays";
                ps.scaleMode = PanelScaleMode.ConstantPixelSize; ps.scale = 1;
                ps.clearColor = true; ps.colorClearValue = new Color(0, 0, 0, 0);
            }
            ps.themeStyleSheet = theme;
            ps.targetTexture = rt;
            if (doc == null)
            {
                var go = new GameObject("Cockpit display panel");
                go.transform.SetParent(transform, false);
                go.SetActive(false);
                doc = go.AddComponent<UIDocument>();
                doc.panelSettings = ps;
                go.SetActive(true);
            }
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (screenMat == null) { screenMat = new Material(unlit) { name = "Live screen" }; screenMat.SetColor("_BaseColor", new Color(1.25f, 1.25f, 1.25f)); }
            if (dialMat == null)
            {
                dialMat = new Material(unlit) { name = "Live dial" };
                dialMat.SetFloat("_AlphaClip", 1); dialMat.SetFloat("_Cutoff", 0.5f); dialMat.EnableKeyword("_ALPHATEST_ON");
                dialMat.SetColor("_BaseColor", new Color(1.1f, 1.1f, 1.1f));
            }
            screenMat.SetTexture("_BaseMap", rt); screenMat.mainTexture = rt;
            dialMat.SetTexture("_BaseMap", rt); dialMat.mainTexture = rt;
        }

        /// <summary>New readings: every frame from the cockpit, a few times a second otherwise.</summary>
        public void Set(HeliState s, AircraftConfig ac, double? bearing, HsiData hsi, bool cockpitView)
        {
            if (displays.Count == 0) return;
            if (!cockpitView && Time.unscaledTime - lastSet < 0.2f) return;
            lastSet = Time.unscaledTime;
            foreach (var d in displays) d.panel.Set(s, ac, bearing, hsi);
        }

        void OnDestroy() { if (rt != null) rt.Release(); }
    }
}
