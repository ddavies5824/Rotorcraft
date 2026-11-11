// Houses and farm buildings modelled rather than boxed (port of render/houses.ts): a few Scottish types (harled
// cottage with dormers, two-storey villa with a bay window, modern bungalow with a garage, a semi-detached pair) with
// framed windows, sills and painted margins, doors under canopies, chimney stacks with pots, skews on the gables,
// gutters and downpipes, and a front garden facing the road (wall or hedge with gate piers, path, lawn, and now and then
// a car on the drive, which AmbientLife parks there). Farm steadings get a shed with roller doors, roof lights and an
// open-fronted lean-to of round bales. Each house picks its type and colours from its own seed (the browser game draws
// them from one stream shared by every structure, which cannot be reproduced here), so a house is the same every time.
using System;
using System.Collections.Generic;
using Rotorcraft.Sim;
using Rotorcraft.World;
using static Rotorcraft.Presentation.RGBA;

namespace Rotorcraft.Presentation
{
    public static class HouseModels
    {
        /// <summary>A car standing on a house's drive: sim position, ground, the way its nose points (radians, the box
        /// convention: 0 faces +z), kind and paint.</summary>
        public struct Parked { public double x, z, y, heading; public string kind; public int paint; }

        /// <summary>Every drive with a car on it, filled as the houses are built.</summary>
        public static readonly List<Parked> Drives = new List<Parked>();

        static readonly RGBA Pane = RGBA.Pane;

        delegate (double x, double z) Place(double lx, double lz);

        static Place Frame(Structure s)
        {
            double c = Math.Cos(s.rot), sn = Math.Sin(s.rot);
            return (lx, lz) => (s.x + lx * c + lz * sn, s.z - lx * sn + lz * c);
        }

        public static void House(ColourMesh m, Structure s, double g, Func<double> R)
        {
            var rot = s.rot;
            var W = Frame(s);
            // which local x side faces the road
            var rd = RoadNet.NearestRoad(s.x, s.z);
            var front = 1;
            if (rd != null)
            {
                var L = rd.road; int i1 = Math.Min(rd.i + 1, L.x.Length - 1);
                double px = L.x[rd.i] + (L.x[i1] - L.x[rd.i]) * rd.t, pz = L.z[rd.i] + (L.z[i1] - L.z[rd.i]) * rd.t;
                var lx = (px - s.x) * Math.Cos(rot) - (pz - s.z) * Math.Sin(rot);
                front = lx >= 0 ? 1 : -1;
            }
            var type = s.w > 15 ? "semi" : s.h > 7 ? "villa" : R() < 0.45 ? "cottage" : "bungalow";
            RGBA[] harls = { M(0.88, 0.86, 0.81, 0.05), M(0.92, 0.9, 0.86, 0.05), M(0.84, 0.78, 0.66, 0.05), M(0.78, 0.8, 0.78, 0.05), M(0.86, 0.82, 0.74, 0.05) };
            var harl = harls[(int)(R() * 5)];
            var stoneWall = R() < (type == "villa" ? 0.5 : 0.2);
            var walls = stoneWall ? M(0.62, 0.55, 0.47, 0.05) : harl;
            var wallPattern = stoneWall ? Pattern.Stone : Pattern.Harling;
            var roofCol = type == "bungalow" ? new[] { M(0.36, 0.2, 0.15, 0.2), M(0.25, 0.25, 0.27, 0.25) }[(int)(R() * 2)] : M(0.21, 0.22, 0.25, 0.35);
            var trim = new[] { M(0.92, 0.92, 0.9, 0.2), M(0.15, 0.25, 0.2, 0.2), M(0.12, 0.15, 0.3, 0.2), M(0.55, 0.12, 0.1, 0.2), M(0.2, 0.2, 0.22, 0.2) }[(int)(R() * 5)];
            var doorCol = new[] { M(0.55, 0.1, 0.08, 0.35), M(0.1, 0.2, 0.42, 0.35), M(0.12, 0.28, 0.18, 0.35), M(0.9, 0.88, 0.82, 0.35), M(0.35, 0.22, 0.12, 0.3) }[(int)(R() * 5)];
            var margin = stoneWall ? M(0.72, 0.66, 0.56, 0.05) : M(0.7, 0.68, 0.64, 0.05);
            double w = s.w, d = s.d;
            var eave = type == "cottage" ? 3.3 : type == "bungalow" ? 2.8 : Math.Max(5.4, s.h - 1.5);
            var rise = type == "bungalow" ? 1.8 + R() * 0.6 : type == "cottage" ? 3.6 : 2.8 + R() * 0.8;

            // walls on a plinth (taken well down so a slope never shows under it), then the roof: ridge along the road, gables at the ends
            m.Texture(Pattern.Stone).Box(s.x, g - 0.7, s.z, w + 0.12, 2.2, d + 0.12, M(0.45, 0.42, 0.38, 0.05), rot).ClearTexture();
            m.Texture(wallPattern).Box(s.x, g + eave / 2 - 0.3, s.z, w, eave + 0.6, d, walls, rot, 0.3);
            m.Texture(Pattern.Slate).Roof(s.x, g + eave, s.z, w + 0.5, d + 0.35, rise, roofCol, rot, walls).ClearTexture();
            // skews (gable copes) and the ridge
            if (type != "bungalow")
                foreach (var e in new[] { -1, 1 })
                    foreach (var sd in new[] { -1, 1 })
                    {
                        var a1 = W(sd * (w / 2 + 0.25), e * (d / 2 + 0.05)); var a2 = W(0, e * (d / 2 + 0.05));
                        var len = Js.Hypot(w / 2 + 0.25, rise);
                        var tilt = Math.Atan2(rise, w / 2 + 0.25);
                        Skew(m, (a1.x + a2.x) / 2, g + eave + rise / 2 + 0.08, (a1.z + a2.z) / 2, len, rot, -sd * tilt, margin);
                    }
            var r0 = W(0, 0);
            m.Box(r0.x, g + eave + rise + 0.05, r0.z, 0.28, 0.16, d + 0.4, M(0.3, 0.3, 0.32, 0.2), rot);
            // chimney stacks on the gables (one off-centre on a bungalow), with pots
            var stacks = type == "bungalow" ? new[] { d * 0.2 } : new[] { -d / 2 + 0.4, d / 2 - 0.4 };
            foreach (var lz in stacks)
            {
                if (type == "semi" && Math.Abs(lz) < 1) continue;
                var c = W(0, lz);
                var top = g + eave + rise + 1.0;
                m.Texture(wallPattern).Box(c.x, top - 1.05, c.z, 1.1, 2.1, 0.7, walls, rot).ClearTexture();
                m.Box(c.x, top + 0.05, c.z, 1.25, 0.12, 0.85, margin, rot);
                foreach (var px in new[] { -0.28, 0.28 }) { var p = W(px, lz); m.Cylinder(new P3(p.x, top + 0.1, p.z), new P3(p.x, top + 0.5, p.z), 0.13, 0.11, M(0.55, 0.32, 0.22, 0.1), 8); }
            }
            // gutters along both eaves, a downpipe at each end of the front
            var black = M(0.12, 0.12, 0.13, 0.3);
            foreach (var sd in new[] { -1, 1 }) { var gp = W(sd * (w / 2 + 0.3), 0); m.Box(gp.x, g + eave - 0.05, gp.z, 0.14, 0.13, d + 0.35, black, rot); }
            foreach (var e in new[] { -1, 1 }) { var p = W(front * (w / 2 + 0.12), e * (d / 2 - 0.2)); m.Cylinder(new P3(p.x, g, p.z), new P3(p.x, g + eave, p.z), 0.05, 0.05, black, 6, false); }

            // ---- the front: windows either side of the door; the back gets windows too
            var floors = type == "villa" || type == "semi" ? 2 : 1;
            void Fz(double lz, int side, double y, double ww, double hh) => Window(m, W, rot, side, w / 2, lz, g + y, ww, hh, trim, margin, true);
            var doors = type == "semi" ? new[] { -d / 4 - 0.6, d / 4 + 0.6 } : new[] { type == "bungalow" ? -d * 0.15 : 0 };
            var bays = type == "semi" ? new[] { -d / 2 + 1.4, -d / 4 + 1.0, d / 4 - 1.0, d / 2 - 1.4 } : type == "bungalow" ? new[] { d * 0.15, d * 0.35, -d * 0.38 } : new[] { -d / 4 - 0.4, d / 4 + 0.4 };
            foreach (var lz in bays)
                for (int f = 0; f < floors; f++)
                {
                    if (type == "villa" && f == 0 && lz < 0) { BayWindow(m, W, rot, front, w / 2, lz, g, walls, trim, margin, roofCol); continue; }
                    Fz(lz, front, 1.0 + f * 2.75, type == "bungalow" ? 1.6 : 1.0, 1.35);
                }
            if (floors == 2) foreach (var dz in doors) Fz(dz, front, 3.75, 0.7, 1.0);   // stair window over the door
            foreach (var dz in doors) Door(m, W, rot, front, w / 2, dz, g, doorCol, trim, roofCol);
            var nb = Math.Max(2, (int)Math.Floor(d / 3.2));
            for (int i = 0; i < nb; i++)
            {
                var lz = -d / 2 + (i + 0.5) * d / nb;
                for (int f = 0; f < floors; f++) Fz(lz, -front, 1.1 + f * 2.75, 0.9, 1.15);
            }
            if (type == "cottage") foreach (var lz in new[] { -d / 4, d / 4 }) Dormer(m, W, rot, front, w, eave, rise, lz, g, walls, roofCol, trim, margin);
            // a garage beside the bungalow
            if (type == "bungalow")
            {
                var gl = d / 2 + 1.8;
                var gp = W(front * 0.5, gl);
                m.Texture(Pattern.Harling).Box(gp.x, g + 1.3 - 0.75, gp.z, w - 1, 2.6 + 1.5, 3.4, walls, rot).ClearTexture();
                m.Box(gp.x, g + 2.65, gp.z, w - 0.6, 0.15, 3.8, M(0.2, 0.2, 0.22, 0.2), rot);
                var gd = W(front * (w / 2), gl);
                m.VRect(gd.x, g + 1.05, gd.z, 2.4, 2.1, rot + front * Math.PI / 2, M(0.8, 0.8, 0.78, 0.3));
            }

            // ---- front garden: path to the door, a boundary wall or hedge on the road side with gate piers at each path, maybe a car
            const double depth = 6.5;
            foreach (var dz in doors) { var p = W(front * (w / 2 + depth / 2), dz); m.FlatRect(p.x, g + 0.05, p.z, depth, 1.1, M(0.6, 0.58, 0.55, 0.05), rot); }
            var hedge = R() < 0.45;
            // the boundary in lengths between the gateways
            var cuts = new List<double> { -(d / 2 + 1) };
            foreach (var dz in doors) { cuts.Add(dz - 0.55); cuts.Add(dz + 0.55); }
            cuts.Add(d / 2 + 1);
            for (int k = 0; k + 1 < cuts.Count; k += 2)
            {
                double z0 = cuts[k], z1 = cuts[k + 1];
                if (z1 - z0 < 0.2) continue;
                var b = W(front * (w / 2 + depth), (z0 + z1) / 2);
                if (hedge) m.Texture(Pattern.Foliage).Box(b.x, g + 0.55 - 0.3, b.z, 0.8, 1.1 + 0.6, z1 - z0, M(0.12, 0.22, 0.08, 0.03).Scaled(FieldBoundaries.HedgeLift), rot).ClearTexture();
                else
                {
                    m.Texture(Pattern.Stone).Box(b.x, g + 0.42 - 0.3, b.z, 0.35, 0.84 + 0.6, z1 - z0, M(0.55, 0.5, 0.44, 0.05), rot).ClearTexture();
                    m.Box(b.x, g + 0.88, b.z, 0.42, 0.08, z1 - z0 + 0.05, margin, rot);
                }
            }
            foreach (var dz in doors)
                foreach (var e in new[] { -0.75, 0.75 }) { var q = W(front * (w / 2 + depth), dz + e); m.Box(q.x, g + 0.6 - 0.2, q.z, 0.4, 1.2 + 0.4, 0.4, margin, rot); }
            if (R() < 0.45)
            {
                var kind = new[] { "hatch", "saloon", "estate", "suv" }[(int)(R() * 4)];
                var paint = (int)(R() * 13);
                var c = W(front * (w / 2 + 3), d / 2 + (type == "bungalow" ? 1.8 : 2.4));
                m.FlatRect(c.x, g + 0.04, c.z, 5.5, 3, M(0.5, 0.47, 0.42, 0.05), rot);
                lock (Drives) Drives.Add(new Parked { x = c.x, z = c.z, y = g, heading = rot + front * Math.PI / 2, kind = kind, paint = paint });
            }
        }

        /// <summary>A sloping gable cope (skew), pitched `tilt` about the house's long axis.</summary>
        static void Skew(ColourMesh m, double x, double y, double z, double len, double rot, double tilt, RGBA col)
        {
            double c = Math.Cos(rot), s = Math.Sin(rot), ct = Math.Cos(tilt), st = Math.Sin(tilt);
            var ax = new P3(c * ct * len / 2, st * len / 2, -s * ct * len / 2);   // along the slope
            const double t = 0.18, wdt = 0.32;
            P3 up = new P3(-c * st * t, ct * t, s * st * t), side = new P3(s * wdt / 2, 0, c * wdt / 2);
            P3 P(double a, double b, double e) => new P3(x + ax.x * a + up.x * b + side.x * e, y + ax.y * a + up.y * b + side.y * e, z + ax.z * a + up.z * b + side.z * e);
            m.Quad(P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), col);
            m.Quad(P(-1, 0, -1), P(1, 0, -1), P(1, 1, -1), P(-1, 1, -1), col);
            m.Quad(P(-1, 0, 1), P(1, 0, 1), P(1, 1, 1), P(-1, 1, 1), col);
        }

        /// <summary>A sash window: glass, glazing bars, frame, a painted margin round it and a sill below.</summary>
        static void Window(ColourMesh m, Place W, double rot, int side, double half, double lz, double y, double ww, double hh, RGBA trim, RGBA margin, bool bars)
        {
            var face = rot + side * Math.PI / 2;
            (double x, double z) Off(double o) => W(side * (half + o), lz);
            var mm = Off(0.02); m.VRect(mm.x, y + hh / 2, mm.z, ww + 0.36, hh + 0.36, face, margin);
            var gl = Off(0.03); m.VRect(gl.x, y + hh / 2, gl.z, ww, hh, face, Pane);
            var f = Off(0.045);
            m.VRect(f.x, y + hh + 0.02, f.z, ww + 0.04, 0.07, face, trim);
            m.VRect(f.x, y - 0.02, f.z, ww + 0.04, 0.07, face, trim);
            foreach (var e in new[] { -1, 1 }) { var p = W(side * (half + 0.045), lz + e * (ww / 2)); m.VRect(p.x, y + hh / 2, p.z, 0.07, hh, face, trim); }
            if (bars)
            {
                m.VRect(f.x, y + hh / 2, f.z, ww, 0.05, face, trim);
                m.VRect(f.x, y + hh / 2, f.z, 0.05, hh, face, trim);
            }
            var sl = W(side * (half + 0.1), lz);
            m.Box(sl.x, y - 0.1, sl.z, 0.2, 0.08, ww + 0.3, margin, rot);
        }

        static void Door(ColourMesh m, Place W, double rot, int side, double half, double lz, double g, RGBA col, RGBA trim, RGBA roofCol)
        {
            var face = rot + side * Math.PI / 2;
            var a = W(side * (half + 0.02), lz); m.VRect(a.x, g + 1.15, a.z, 1.3, 2.4, face, trim);
            var b = W(side * (half + 0.035), lz); m.VRect(b.x, g + 1.05, b.z, 0.95, 2.1, face, col);
            var c = W(side * (half + 0.045), lz); m.VRect(c.x, g + 1.85, c.z, 0.6, 0.35, face, Pane);   // the glazed panel
            var h = W(side * (half + 0.06), lz + 0.33); m.Box(h.x, g + 1.0, h.z, 0.06, 0.06, 0.06, M(0.75, 0.65, 0.3, 0.9), rot);
            // a step and a little canopy
            var st = W(side * (half + 0.35), lz); m.Box(st.x, g + 0.08, st.z, 0.7, 0.16, 1.4, M(0.55, 0.53, 0.5, 0.05), rot);
            var cp = W(side * (half + 0.45), lz); m.Box(cp.x, g + 2.45, cp.z, 0.9, 0.12, 1.7, roofCol, rot);
        }

        static void BayWindow(ColourMesh m, Place W, double rot, int side, double half, double lz, double g, RGBA walls, RGBA trim, RGBA margin, RGBA roofCol)
        {
            var b = W(side * (half + 0.45), lz);
            m.Texture(Pattern.Stone).Box(b.x, g + 0.45, b.z, 0.9, 0.9, 2.4, walls, rot).ClearTexture();
            m.Box(b.x, g + 1.1, b.z, 0.9, 1.3, 2.4, Pane, rot);
            foreach (var e in new[] { -1.15, -0.4, 0.4, 1.15 }) { var p = W(side * (half + 0.92), lz + e); m.Box(p.x, g + 1.1, p.z, 0.06, 1.3, 0.08, trim, rot); }
            m.Box(b.x, g + 0.92, b.z, 1.0, 0.08, 2.5, margin, rot);
            m.Box(b.x, g + 1.85, b.z, 1.05, 0.2, 2.55, margin, rot);
            m.Box(b.x, g + 2.05, b.z, 1.0, 0.2, 2.45, roofCol, rot);
        }

        static void Dormer(ColourMesh m, Place W, double rot, int side, double w, double eave, double rise, double lz, double g, RGBA walls, RGBA roofCol, RGBA trim, RGBA margin)
        {
            var lx = side * (w / 4 + 0.1);
            var d = W(lx, lz);
            var y = g + eave + rise * 0.3;
            m.Texture(Pattern.Harling).Box(d.x, y + 0.6, d.z, 1.2, 1.4, 1.5, walls, rot).ClearTexture();
            m.Texture(Pattern.Slate).Roof(d.x, y + 1.3, d.z, 1.4, 1.7, 0.7, roofCol, rot, walls).ClearTexture();
            Window(m, W, rot, side, Math.Abs(lx) + 0.6, lz, y + 0.2, 0.8, 0.9, trim, margin, true);
        }

        /// <summary>A farm shed: block-work base, profiled sheeting, roller doors, a strip of roof lights, an open-fronted lean-to of bales.</summary>
        public static void Barn(ColourMesh m, Structure s, double g)
        {
            var rot = s.rot;
            var W = Frame(s);
            RGBA wall = M(0.36, 0.42, 0.36, 0.3), roof = M(0.5, 0.52, 0.5, 0.35), block = M(0.62, 0.6, 0.56, 0.05);
            m.Box(s.x, g + 1.0 - 0.75, s.z, s.w, 2.0 + 1.5, s.d, block, rot);
            m.Texture(Pattern.Cladding).Box(s.x, g + 2 + (s.h - 2) / 2, s.z, s.w - 0.05, s.h - 2, s.d - 0.05, wall, rot, 0.25);
            m.Roof(s.x, g + s.h, s.z, s.w + 0.6, s.d + 0.6, s.w * 0.12, roof, rot, wall).ClearTexture();
            // translucent roof lights
            for (int i = 0; i < 4; i++)
                foreach (var sd in new[] { -1, 1 })
                {
                    var p = W(sd * s.w * 0.22, -s.d / 2 + (i + 0.5) * s.d / 4);
                    m.Box(p.x, g + s.h + s.w * 0.06 + 0.02, p.z, 1.0, 0.03, 1.6, M(0.75, 0.78, 0.72, 0.4), rot);
                }
            // two roller doors on the yard side, a personnel door
            foreach (var lz in new[] { -s.d / 4, s.d / 4 }) { var p = W(s.w / 2 + 0.03, lz); m.Texture(Pattern.Cladding).VRect(p.x, g + 2.3, p.z, 4.2, 4.6, rot + Math.PI / 2, M(0.55, 0.57, 0.6, 0.4)).ClearTexture(); }
            var pd = W(s.w / 2 + 0.03, 0);
            m.VRect(pd.x, g + 1.05, pd.z, 0.95, 2.1, rot + Math.PI / 2, M(0.2, 0.3, 0.22, 0.3));
            // lean-to along one side: posts and a mono-pitch roof, round bales inside
            var lt = W(-s.w / 2 - 2.5, 0);
            m.Texture(Pattern.Cladding).Box(lt.x, g + 3.6, lt.z, 5.4, 0.15, s.d, roof, rot).ClearTexture();
            for (int i = 0; i <= 4; i++) { var q = W(-s.w / 2 - 4.9, -s.d / 2 + i * s.d / 4); m.Box(q.x, g + 1.8, q.z, 0.2, 3.6, 0.2, M(0.3, 0.32, 0.34, 0.4), rot); }
            for (int i = 0; i < 5; i++)
            {
                // the bale lies along the shed: its axis runs along the shed's local z
                var ax = W(-s.w / 2 - 2.5 + (i % 2) * 1.6, -s.d / 2 + 2 + i * 2.4 + 0.6);
                var bx = W(-s.w / 2 - 2.5 + (i % 2) * 1.6, -s.d / 2 + 2 + i * 2.4 - 0.6);
                m.Cylinder(new P3(bx.x, g + 0.75, bx.z), new P3(ax.x, g + 0.75, ax.z), 0.75, 0.75, M(0.75, 0.66, 0.38, 0.05), 12);
            }
        }
    }
}
