// The navigation map (port of src/ui/map.ts): shaded terrain, roads, towns, sites, the route, search areas,
// casualties found, freeplay callouts and the aircraft. The terrain picture is computed once on a worker thread
// (with hill shading, which the browser version did not have) and kept; the overlay is drawn with Painter2D and
// the names are labels, redrawn whenever the options change.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Rotorcraft.Sim;
using Rotorcraft.World;
using UnityEngine;
using UnityEngine.UIElements;
using WTerrain = Rotorcraft.World.Terrain;

namespace Rotorcraft.Presentation.UI
{
    public sealed class MapOptions
    {
        public (double x, double z, double hdg)? aircraft;
        public List<(double x, double z, string name)> route;
        public (double x, double z, string name)? target;
        public (double x, double z) center = (-1500, -2200);
        public double metresPerPx = 20;
        public ((double x, double z)[] corners, double coverage, string name)? search;
        public List<(double x, double z, string name)> found;
        public List<((double x, double z)[] corners, string name)> areas;
        public List<(double x, double z, string label, bool active, int priority)> callouts;
        public bool labels = true;
    }

    public sealed class MapView : VisualElement
    {
        public const double X0 = -9000, X1 = 6000, Z0 = -9400, Z1 = 5000;
        const int W = 1200;
        static Texture2D terrain;
        static Task<Color32[]> building;
        static int texH;

        public MapOptions Options = new MapOptions();
        readonly Image img;
        readonly VisualElement overlay, labelLayer;
        readonly List<Label> labelPool = new List<Label>();
        int labelsUsed;

        public MapView()
        {
            style.overflow = Overflow.Hidden;
            style.backgroundColor = (Color)new Color32(0x1a, 0x34, 0x42, 255);
            pickingMode = PickingMode.Ignore;
            img = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            img.style.position = Position.Absolute;
            Add(img);
            overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            overlay.style.position = Position.Absolute; overlay.style.left = 0; overlay.style.top = 0; overlay.style.right = 0; overlay.style.bottom = 0;
            overlay.generateVisualContent += Draw;
            Add(overlay);
            labelLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            labelLayer.style.position = Position.Absolute; labelLayer.style.left = 0; labelLayer.style.top = 0; labelLayer.style.right = 0; labelLayer.style.bottom = 0;
            Add(labelLayer);
            RegisterCallback<GeometryChangedEvent>(_ => Redraw());
            schedule.Execute(() => { if (terrain == null) TerrainTexture(); if (terrain != null && img.image != terrain) Redraw(); }).Every(300);
            Prepare();
        }

        /// <summary>Start computing the terrain picture (call early, e.g. at start-up).</summary>
        public static void Prepare()
        {
            if (terrain != null || building != null) return;
            texH = (int)Math.Round(W * (Z1 - Z0) / (X1 - X0));
            var h = texH;
#if UNITY_WEBGL
            building = Task.FromResult(Compute(h));   // no worker threads in the browser
#else
            building = Task.Run(() => Compute(h));
#endif
        }

        static Color32[] Compute(int h)
        {
            {
                var px = new Color32[W * h];
                var hts = new double[W * h];
                for (int j = 0; j < h; j++)
                    for (int i = 0; i < W; i++)
                        hts[j * W + i] = WTerrain.NaturalHeight(X0 + (i + 0.5) / W * (X1 - X0), Z0 + (j + 0.5) / h * (Z1 - Z0));
                double cell = (X1 - X0) / W;
                for (int j = 0; j < h; j++)
                    for (int i = 0; i < W; i++)
                    {
                        double x = X0 + (i + 0.5) / W * (X1 - X0);
                        var ht = hts[j * W + i];
                        double r, g, b;
                        if (ht <= 0.6 && x > 500) { r = 26; g = 52; b = 66; }
                        else
                        {
                            var t = Math.Min(1, ht / 520);
                            r = 70 + t * 80; g = 98 + t * 40; b = 62 + t * 50;
                            var band = Math.Floor(ht / 61) % 2 == 0 ? 0 : 8; r += band; g += band; b += band;
                            // hill shading: light from the north-west
                            var hl = hts[j * W + Math.Max(0, i - 1)]; var hr = hts[j * W + Math.Min(W - 1, i + 1)];
                            var hu = hts[Math.Max(0, j - 1) * W + i]; var hd = hts[Math.Min(h - 1, j + 1) * W + i];
                            double dx = (hr - hl) / (2 * cell), dz = (hd - hu) / (2 * cell);
                            var shade = Math.Max(-0.35, Math.Min(0.35, (dx + dz) * -0.9));
                            r *= 1 + shade; g *= 1 + shade; b *= 1 + shade;
                        }
                        // texture rows run bottom-up; the map's z grows down the screen
                        px[(h - 1 - j) * W + i] = new Color32((byte)Math.Max(0, Math.Min(255, r)), (byte)Math.Max(0, Math.Min(255, g)), (byte)Math.Max(0, Math.Min(255, b)), 255);
                    }
                return px;
            }
        }

        static Texture2D TerrainTexture()
        {
            if (terrain != null) return terrain;
            if (building == null) Prepare();
            if (!building.IsCompleted) return null;
            terrain = new Texture2D(W, texH, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, name = "Map terrain" };
            terrain.SetPixels32(building.Result);
            terrain.Apply(true);
            return terrain;
        }

        public void Redraw()
        {
            var r = contentRect;
            if (float.IsNaN(r.width) || r.width < 2) return;
            var o = Options;
            var tex = TerrainTexture();
            if (tex != null)
            {
                img.image = tex;
                var (sx0, sy0) = ToScreen(X0, Z0, r.width, r.height);
                img.style.left = (float)sx0; img.style.top = (float)sy0;
                img.style.width = (float)((X1 - X0) / o.metresPerPx); img.style.height = (float)((Z1 - Z0) / o.metresPerPx);
            }
            labelsUsed = 0;
            if (o.labels) PlaceLabels(r.width, r.height);
            for (int i = labelsUsed; i < labelPool.Count; i++) labelPool[i].style.display = DisplayStyle.None;
            overlay.MarkDirtyRepaint();
        }

        (double x, double y) ToScreen(double x, double z, float w, float h) => (w / 2 + (x - Options.center.x) / Options.metresPerPx, h / 2 + (z - Options.center.z) / Options.metresPerPx);

        Label Text(string s, double x, double y, Color c, int size = 11, bool centre = false, bool italic = false)
        {
            Label l;
            if (labelsUsed < labelPool.Count) l = labelPool[labelsUsed];
            else
            {
                l = new Label { pickingMode = PickingMode.Ignore };
                l.style.position = Position.Absolute; l.style.whiteSpace = WhiteSpace.NoWrap;
                l.style.unityTextOutlineColor = new Color(0.04f, 0.06f, 0.07f, 0.85f); l.style.unityTextOutlineWidth = 0.6f;
                labelPool.Add(l); labelLayer.Add(l);
            }
            labelsUsed++;
            l.style.display = DisplayStyle.Flex;
            l.text = s; l.style.color = c; l.style.fontSize = size;
            l.style.unityFontStyleAndWeight = italic ? FontStyle.Italic : FontStyle.Normal;
            l.style.left = (float)x; l.style.top = (float)(y - size * 0.75);
            l.style.translate = centre ? new Translate(Length.Percent(-50), 0) : new Translate(0, 0);
            return l;
        }

        void PlaceLabels(float w, float h)
        {
            var o = Options;
            var mpp = o.metresPerPx;
            foreach (var t in Places.SETTLEMENTS)
            {
                var (x, y) = ToScreen(t.x, t.z, w, h);
                if (x < -100 || x > w + 100 || y < -40 || y > h + 40) continue;
                Text(t.name.ToUpperInvariant(), x, y - t.r / mpp * 0.8 - 7, new Color(0.96f, 0.94f, 0.88f, 0.75f), 11, true, true);
            }
            foreach (var s in WorldMap.SITES)
            {
                var (x, y) = ToScreen(s.x, s.z, w, h);
                if (x < -40 || x > w + 40 || y < -20 || y > h + 20) continue;
                if (s.kind == SiteKind.Pad && mpp > 8) continue;
                var name = s.kind != SiteKind.Landmark && s.kind != SiteKind.Pad ? $"{s.name} {Math.Round(WorldMap.SiteElevation(s) * SimMath.FT)}′" : s.name;
                Text(name, x + 7, y, new Color(0.9f, 0.93f, 0.94f, 0.92f));
            }
            var amber = new Color32(0xf2, 0xb5, 0x3c, 255);
            if (o.areas != null) foreach (var a in o.areas) { var c = Centre(a.corners); var (x, y) = ToScreen(c.x, c.z, w, h); Text(a.name, x, y, amber, 11, true); }
            if (o.search is var sv && sv != null) { var c = Centre(sv.Value.corners); var (x, y) = ToScreen(c.x, c.z, w, h); Text($"{sv.Value.name} · {Math.Round(sv.Value.coverage * 100)}%", x, y, amber, 11, true); }
            var orange = new Color32(0xff, 0x6a, 0x2a, 255);
            if (o.found != null) foreach (var f in o.found) { var (x, y) = ToScreen(f.x, f.z, w, h); Text(f.name, x + 9, y, orange); }
            if (o.callouts != null) foreach (var c in o.callouts)
                {
                    var col = c.active ? new Color32(0xe0, 0x5c, 0xc8, 255) : c.priority == 1 ? new Color32(0xff, 0x5a, 0x4a, 255) : amber;
                    var (x, y) = ToScreen(c.x, c.z, w, h); Text(c.label, x + 10, y, col);
                }
            Text("N ↑", 8, 12, new Color(0.9f, 0.93f, 0.94f));
            Text("1 nm", 12 + 1852 / mpp, h - 9, new Color(0.9f, 0.93f, 0.94f));
        }

        static (double x, double z) Centre((double x, double z)[] cs) => (cs.Average(p => p.x), cs.Average(p => p.z));

        void Draw(MeshGenerationContext ctx)
        {
            var r = overlay.contentRect;
            float w = r.width, h = r.height;
            if (w < 2) return;
            var o = Options;
            var mpp = o.metresPerPx;
            var p = ctx.painter2D;
            Vector2 S(double x, double z) { var (sx, sy) = ToScreen(x, z, w, h); return new Vector2((float)sx, (float)sy); }
            // grid: 1 nm squares
            p.strokeColor = new Color(0.9f, 0.93f, 0.94f, 0.08f); p.lineWidth = 1;
            const double nm = 1852;
            for (var x = Math.Floor(X0 / nm) * nm; x < X1; x += nm) { p.BeginPath(); p.MoveTo(S(x, Z0)); p.LineTo(S(x, Z1)); p.Stroke(); }
            for (var z = Math.Floor(Z0 / nm) * nm; z < Z1; z += nm) { p.BeginPath(); p.MoveTo(S(X0, z)); p.LineTo(S(X1, z)); p.Stroke(); }
            // roads, towns and the wind farm
            p.strokeColor = new Color(0.93f, 0.89f, 0.78f, 0.45f); p.lineWidth = mpp < 12 ? 1.6f : 1f;
            foreach (var rd in Places.ROADS) { p.BeginPath(); for (int i = 0; i < rd.pts.Length; i++) { var v = S(rd.pts[i].x, rd.pts[i].z); if (i == 0) p.MoveTo(v); else p.LineTo(v); } p.Stroke(); }
            foreach (var t in Places.SETTLEMENTS) { p.fillColor = new Color(0.94f, 0.91f, 0.82f, 0.16f); p.BeginPath(); p.Arc(S(t.x, t.z), (float)(t.r / mpp * 0.8), Angle.Degrees(0), Angle.Degrees(360)); p.Fill(); }
            p.fillColor = new Color(1, 1, 1, 0.8f);
            foreach (var t in Places.TURBINES) { p.BeginPath(); p.Arc(S(t.x, t.z), 2, Angle.Degrees(0), Angle.Degrees(360)); p.Fill(); }
            // route
            var magenta = new Color32(0xe0, 0x5c, 0xc8, 255);
            if (o.route != null && o.route.Count > 1)
            {
                p.strokeColor = magenta; p.lineWidth = 2;
                for (int i = 1; i < o.route.Count; i++) Dashed(p, S(o.route[i - 1].x, o.route[i - 1].z), S(o.route[i].x, o.route[i].z), 6, 4);
            }
            // sites
            foreach (var s in WorldMap.SITES)
            {
                var v = S(s.x, s.z);
                if (v.x < -40 || v.x > w + 40 || v.y < -20 || v.y > h + 20) continue;
                if (s.kind == SiteKind.Pad && mpp > 8) continue;
                p.BeginPath();
                if (s.kind == SiteKind.Landmark) { p.fillColor = (Color)new Color32(0xf2, 0xb5, 0x3c, 255); p.MoveTo(v + new Vector2(0, -5)); p.LineTo(v + new Vector2(5, 4)); p.LineTo(v + new Vector2(-5, 4)); p.ClosePath(); }
                else { p.fillColor = (Color)new Color32(0xe6, 0xec, 0xef, 255); p.Arc(v, s.kind == SiteKind.Pad ? 2.5f : 4.5f, Angle.Degrees(0), Angle.Degrees(360)); }
                p.Fill();
            }
            var amber = new Color32(0xf2, 0xb5, 0x3c, 255);
            void Area((double x, double z)[] cs)
            {
                p.fillColor = new Color(0.95f, 0.71f, 0.24f, 0.14f);
                p.BeginPath(); for (int i = 0; i < cs.Length; i++) { var v = S(cs[i].x, cs[i].z); if (i == 0) p.MoveTo(v); else p.LineTo(v); } p.ClosePath(); p.Fill();
                p.strokeColor = amber; p.lineWidth = 1.5f;
                for (int i = 0; i < cs.Length; i++) Dashed(p, S(cs[i].x, cs[i].z), S(cs[(i + 1) % cs.Length].x, cs[(i + 1) % cs.Length].z), 5, 3);
            }
            if (o.areas != null) foreach (var a in o.areas) Area(a.corners);
            if (o.search != null) Area(o.search.Value.corners);
            var orange = new Color32(0xff, 0x6a, 0x2a, 255);
            if (o.found != null) foreach (var f in o.found)
                {
                    var v = S(f.x, f.z); p.strokeColor = orange; p.lineWidth = 2.5f;
                    p.BeginPath(); p.MoveTo(v + new Vector2(-6, -6)); p.LineTo(v + new Vector2(6, 6)); p.MoveTo(v + new Vector2(6, -6)); p.LineTo(v + new Vector2(-6, 6)); p.Stroke();
                }
            if (o.callouts != null) foreach (var c in o.callouts)
                {
                    var v = S(c.x, c.z);
                    p.fillColor = c.active ? (Color)magenta : c.priority == 1 ? new Color32(0xff, 0x5a, 0x4a, 255) : amber;
                    p.strokeColor = (Color)new Color32(0x11, 0x18, 0x1d, 255); p.lineWidth = 1.5f;
                    p.BeginPath(); p.MoveTo(v + new Vector2(0, -8)); p.LineTo(v + new Vector2(7, 5)); p.LineTo(v + new Vector2(-7, 5)); p.ClosePath(); p.Fill(); p.Stroke();
                }
            if (o.target != null)
            {
                var v = S(o.target.Value.x, o.target.Value.z);
                p.strokeColor = magenta; p.lineWidth = 2.5f;
                p.BeginPath(); p.Arc(v, 9, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
                if (o.aircraft != null) Dashed(p, S(o.aircraft.Value.x, o.aircraft.Value.z), v, 2, 4);
            }
            if (o.aircraft != null)
            {
                var a = o.aircraft.Value;
                var v = S(a.x, a.z);
                var rad = (float)(a.hdg * Math.PI / 180);
                Vector2 R(float x, float y) => v + new Vector2(x * Mathf.Cos(rad) - y * Mathf.Sin(rad), x * Mathf.Sin(rad) + y * Mathf.Cos(rad));
                p.fillColor = amber; p.strokeColor = (Color)new Color32(0x11, 0x18, 0x1d, 255); p.lineWidth = 1.5f;
                p.BeginPath(); p.MoveTo(R(0, -9)); p.LineTo(R(6, 7)); p.LineTo(R(0, 4)); p.LineTo(R(-6, 7)); p.ClosePath(); p.Fill(); p.Stroke();
            }
            // scale bar
            p.fillColor = (Color)new Color32(0xe6, 0xec, 0xef, 255);
            p.BeginPath(); p.MoveTo(new Vector2(8, h - 10)); p.LineTo(new Vector2(8 + (float)(nm / mpp), h - 10)); p.LineTo(new Vector2(8 + (float)(nm / mpp), h - 8)); p.LineTo(new Vector2(8, h - 8)); p.ClosePath(); p.Fill();
        }

        static void Dashed(Painter2D p, Vector2 a, Vector2 b, float dash, float gap)
        {
            var d = b - a; var len = d.magnitude;
            if (len < 0.5f) return;
            var u = d / len;
            p.BeginPath();
            for (float t = 0; t < len; t += dash + gap) { p.MoveTo(a + u * t); p.LineTo(a + u * Mathf.Min(len, t + dash)); }
            p.Stroke();
        }

        // ---------------------------------------------------------------------------------- helpers
        /// <summary>The planned route of a mission (start site, then each place an objective goes to).</summary>
        public static List<(double x, double z, string name)> RouteOf(Rotorcraft.Game.MissionDef m)
        {
            var s = WorldMap.SiteById(m.startSiteId);
            var pts = new List<(double x, double z, string name)> { (s.x, s.z, s.name) };
            foreach (var o in m.objectives)
            {
                (double x, double z, string name)? p = null;
                if (o.kind == Rotorcraft.Game.ObjectiveKind.Waypoint)
                {
                    var st = o.siteId != null ? WorldMap.FindSite(o.siteId) : null;
                    p = st != null ? (st.x, st.z, st.name) : (o.x ?? 0, o.z ?? 0, o.name ?? "");
                }
                else if ((o.kind == Rotorcraft.Game.ObjectiveKind.Land || o.kind == Rotorcraft.Game.ObjectiveKind.Hover || o.kind == Rotorcraft.Game.ObjectiveKind.Load) && o.siteId != null) { var st = WorldMap.SiteById(o.siteId); p = (st.x, st.z, st.name); }
                else if (o.kind == Rotorcraft.Game.ObjectiveKind.Land && o.x != null && o.z != null) p = (o.x.Value, o.z.Value, o.name ?? "");
                else if (o.kind == Rotorcraft.Game.ObjectiveKind.Land && o.nearTarget != null) { var tg = m.targets?.FirstOrDefault(t => t.id == o.nearTarget); if (tg?.x != null && tg.z != null) p = (tg.x.Value, tg.z.Value, tg.name); }
                else if (o.kind == Rotorcraft.Game.ObjectiveKind.Search) { var a = m.searchAreas?.FirstOrDefault(x => x.id == o.areaId); if (a != null) p = (a.cx, a.cz, a.name); }
                else if (o.kind == Rotorcraft.Game.ObjectiveKind.Winch) { var tg = m.targets?.FirstOrDefault(t => t.id == o.targetId); if (tg?.x != null && tg.z != null) p = (tg.x.Value, tg.z.Value, tg.name); }
                if (p != null) { var last = pts[pts.Count - 1]; if (last.x != p.Value.x || last.z != p.Value.z) pts.Add(p.Value); }
            }
            return pts;
        }
    }
}
