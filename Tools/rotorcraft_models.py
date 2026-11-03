"""
Rotorcraft: aircraft, landmark, vehicle and tree models for the game, built in Blender (4.1 or newer).

HOW TO RUN
  1. Start Blender and choose File > New > General. (The script clears the scene it runs in.)
  2. Open the Scripting tab at the top. In the text editor choose Text > Open and pick this file.
  3. Press the Run Script button (the triangle). Blender looks frozen while it works: that is normal.
  4. When it finishes, the files are in the same folder as this script (or F:\\Rotorcraft Blender).
     For each model built:  <name>.rcm (the model for the game: attach these in the chat), <name>.blend,
     <name>.glb and preview renders (<name>_*.png);  plus rotorcraft_build_log.txt.
     In the .glb each part sits under an empty at its pivot (rotor hub, door hinge, control pivot), with
     the game's metadata as glTF extras (rc_*), plus empties for the lights, seats and eye. The .glb also has
     physically based materials (clear glass, clear-coated paint, metals), the baked shading as COLOR_0, and a
     modelled tail rotor; none of that changes the .rcm.

WHICH MODELS: set BUILD just below, e.g. BUILD = ["landmarks"], ["northstar", "guardian"] or ["kestrel"].
  "landmarks" is the hospitals, Kinloch hangar and towers, lighthouse, castle, distillery and oil platforms:
  one landmarks.rcm holding every placed structure, built to the footprint the game gives it.
  "vehicles" is the road vehicles (hatchback, saloon, estate, SUV, pickup, van, police car, ambulance,
  mountain rescue 4x4 and fire engine): one vehicles.rcm. The game paints each car itself.
  "nightjar" is the Nightjar N4 light twin (air ambulance / police) with opening doors and a furnished cabin.
  Every aircraft now has opening doors, a cabin interior and crew seats: rebuild all four.
  "trees" is two versions each of oak, ash, beech, sycamore, birch, hawthorn, Scots pine, spruce and larch:
  one trees.rcm. The game scales, turns and colours each tree (autumn shades) as it plants them.

All three are original designs, not copies of real types: the Kestrel K2 two-seat piston trainer, and the
Northstar 175 (offshore transport) and Guardian S9 (search and rescue), two versions of one medium twin.
Kit that the game fits or removes per flight (hoist, FLIR, searchlight, floats) is kept as separate parts.
Shading (ambient occlusion) is baked into the vertex colours, because the game draws vertex colours.

The geometry half is plain Python with no Blender calls, so it also runs outside Blender:
  python rotorcraft_models.py <model> out.rcm      (no bevels, smoothing or baked shading)

Axes: the game uses x right, y up, z towards the tail; numbers below are game coordinates in metres.
Blender gets them turned so the nose points along +Y and up is +Z.
"""
import math, os, sys, json, struct, time

VERSION = "rotorcraft_models.py 3.4"
BUILD = os.environ.get("RC_BUILD", "kestrel,northstar,guardian,nightjar").split(",")   # set RC_BUILD to build fewer

# ============================================================================ materials
# key: (r, g, b, gloss, livery slot, emissive)   slot 0 fixed colour; 1 body; 2 stripe; 3 tail.
# For livery slots, r,g,b multiply the livery colour (so 0.82 is a shade of it).
MATS = {
    "paint":     (1.0, 1.0, 1.0, 0.38, 1, False),
    "belly":     (0.82, 0.82, 0.82, 0.30, 1, False),
    "seam":      (0.42, 0.42, 0.42, 0.20, 1, False),
    "stripe":    (1.0, 1.0, 1.0, 0.36, 2, False),
    "accent":    (1.0, 1.0, 1.0, 0.36, 3, False),
    "accentdk":  (0.62, 0.62, 0.62, 0.30, 3, False),
    "glass":     (0.05, 0.07, 0.09, 0.90, 0, False),
    "frame":     (0.08, 0.085, 0.09, 0.40, 0, False),
    "metal":     (0.58, 0.60, 0.62, 0.60, 0, False),
    "chrome":    (0.78, 0.79, 0.80, 0.85, 0, False),
    "darkmetal": (0.16, 0.17, 0.18, 0.45, 0, False),
    "engine":    (0.22, 0.23, 0.25, 0.50, 0, False),
    "fins":      (0.30, 0.30, 0.32, 0.55, 0, False),
    "exhaust":   (0.36, 0.30, 0.26, 0.35, 0, False),
    "rubber":    (0.035, 0.035, 0.04, 0.12, 0, False),
    "belt":      (0.06, 0.06, 0.065, 0.15, 0, False),
    "red":       (0.62, 0.06, 0.05, 0.55, 0, False),
    "yellow":    (0.85, 0.66, 0.10, 0.40, 0, False),
    "navred":    (0.60, 0.05, 0.05, 0.80, 0, False),
    "navgreen":  (0.05, 0.55, 0.15, 0.80, 0, False),
    "lens":      (0.90, 0.90, 0.85, 1.00, 0, False),
    "beacon":    (0.70, 0.08, 0.05, 0.80, 0, False),
    "blade":     (0.13, 0.13, 0.14, 0.45, 0, False),
    "bladele":   (0.50, 0.52, 0.55, 0.70, 0, False),
    "bladetip":  (1.0, 1.0, 1.0, 0.40, 2, False),
    # cockpit
    "trim":      (0.16, 0.165, 0.17, 0.20, 0, False),
    "floor":     (0.07, 0.072, 0.075, 0.10, 0, False),
    "panel":     (0.05, 0.05, 0.055, 0.30, 0, False),
    "glareshield": (0.03, 0.03, 0.035, 0.10, 0, False),
    "seat":      (0.20, 0.17, 0.14, 0.15, 0, False),
    "seatframe": (0.18, 0.18, 0.19, 0.40, 0, False),
    "harness":   (0.25, 0.27, 0.30, 0.20, 0, False),
    "grip":      (0.05, 0.05, 0.05, 0.20, 0, False),
    "headset":   (0.10, 0.11, 0.12, 0.35, 0, False),
    "screen_g":  (0.25, 0.90, 0.35, 0.0, 0, True),
    "screen_a":  (0.95, 0.55, 0.12, 0.0, 0, True),
    "lamp_r":    (0.90, 0.15, 0.08, 0.0, 0, True),
    "lamp_a":    (0.95, 0.70, 0.10, 0.0, 0, True),
    "compass":   (0.90, 0.85, 0.70, 0.0, 0, True),
    "gauge_face": (0.022, 0.022, 0.025, 0.25, 0, False),
    "gauge_mark": (0.92, 0.92, 0.88, 0.30, 0, False),
    "needle":    (0.96, 0.96, 0.92, 0.30, 0, False),
    "ai_sky":    (0.16, 0.40, 0.72, 0.45, 0, False),
    "ai_ground": (0.40, 0.25, 0.12, 0.40, 0, False),
    "arc_g":     (0.12, 0.62, 0.24, 0.40, 0, False),
    "arc_y":     (0.90, 0.72, 0.10, 0.40, 0, False),
    "arc_r":     (0.80, 0.10, 0.08, 0.40, 0, False),
    # medium twins
    "belly_ns":  (0.70, 0.72, 0.74, 0.60, 0, False),
    "belly_sar": (0.20, 0.21, 0.23, 0.50, 0, False),
    "radome":    (0.18, 0.19, 0.20, 0.55, 0, False),
    "radarfair": (0.25, 0.26, 0.28, 0.50, 0, False),
    "intake":    (0.05, 0.05, 0.055, 0.20, 0, False),
    "hoistyellow": (0.95, 0.75, 0.10, 0.55, 0, False),
    "floatpack": (0.85, 0.85, 0.82, 0.50, 0, False),
    "tipwhite":  (0.92, 0.92, 0.90, 0.50, 0, False),
    "flirgrey":  (0.62, 0.63, 0.60, 0.50, 0, False),
    "frame2":    (0.11, 0.115, 0.12, 0.35, 0, False),
    "panel2":    (0.08, 0.085, 0.09, 0.25, 0, False),
    "trim_ns":   (0.32, 0.31, 0.29, 0.18, 0, False),
    "trim_sar":  (0.13, 0.135, 0.14, 0.20, 0, False),
    "seat_ns":   (0.14, 0.16, 0.22, 0.15, 0, False),
    "seat_sar":  (0.10, 0.11, 0.13, 0.15, 0, False),
    "harness_sar": (0.75, 0.55, 0.10, 0.30, 0, False),
    "visor":     (0.10, 0.18, 0.12, 0.60, 0, False),
    "btn_grey":  (0.70, 0.70, 0.70, 0.40, 0, False),
    "fire_red":  (0.85, 0.08, 0.06, 0.50, 0, False),
    "disp":      (0.03, 0.06, 0.12, 0.0, 0, True),
    "sym_cyan":  (0.15, 0.80, 0.95, 0.0, 0, True),
    "lamp_g":    (0.20, 0.85, 0.30, 0.0, 0, True),
    "lamp_w":    (0.90, 0.90, 0.85, 0.0, 0, True),
    "warn_r":    (0.95, 0.10, 0.06, 0.0, 0, True),
    "btn_w":     (0.85, 0.85, 0.80, 0.0, 0, True),
}
# landmarks: textured with the game's material library (src/render/materials.ts, same order and sizes)
TEX_LAYERS = ["none", "concrete", "brick", "harling", "slate", "cladding", "asphalt", "stone", "deckplate", "timber", "metalpanel", "gravel", "felt"]
TEX_SIZE = {"none": 1, "concrete": 4, "brick": 1.35, "harling": 3, "slate": 1.2, "cladding": 2, "asphalt": 4, "stone": 3, "deckplate": 1, "timber": 2.4, "metalpanel": 3, "gravel": 2, "felt": 4}
LANDMARK_MATS = {   # key: (r, g, b, gloss, texture)
    "conc_white": (0.86, 0.86, 0.83, 0.10, "concrete"), "conc_grey": (0.55, 0.55, 0.53, 0.10, "concrete"), "conc_dark": (0.40, 0.40, 0.39, 0.10, "concrete"),
    "harl_white": (0.92, 0.91, 0.88, 0.06, "harling"), "harl_red": (0.75, 0.12, 0.09, 0.15, "harling"), "harl_cream": (0.86, 0.82, 0.72, 0.06, "harling"),
    "slate_roof": (0.24, 0.25, 0.28, 0.30, "slate"), "clad_wall": (0.60, 0.63, 0.65, 0.35, "cladding"), "clad_roof": (0.36, 0.40, 0.43, 0.40, "cladding"),
    "clad_door": (0.42, 0.45, 0.47, 0.30, "cladding"), "stone_castle": (0.50, 0.47, 0.42, 0.05, "stone"), "stone_dark": (0.38, 0.36, 0.33, 0.05, "stone"),
    "brick_red": (0.55, 0.27, 0.19, 0.05, "brick"), "felt_roof": (0.30, 0.30, 0.32, 0.10, "felt"), "timber_door": (0.30, 0.20, 0.13, 0.10, "timber"),
    "deck_green": (0.12, 0.30, 0.20, 0.20, "deckplate"), "deck_grey": (0.45, 0.47, 0.47, 0.30, "deckplate"), "module_grey": (0.62, 0.64, 0.64, 0.30, "metalpanel"),
    "module_white": (0.90, 0.90, 0.88, 0.20, "metalpanel"), "module_orange": (0.85, 0.48, 0.18, 0.30, "metalpanel"), "steel_yellow": (0.80, 0.62, 0.12, 0.35, "metalpanel"),
    "steel_dark": (0.12, 0.12, 0.12, 0.20, None), "rail_white": (0.72, 0.72, 0.70, 0.35, None), "sign_blue": (0.10, 0.30, 0.60, 0.30, None), "sign_white": (0.90, 0.90, 0.88, 0.30, None),
    "door_dark": (0.08, 0.08, 0.09, 0.20, None), "net": (0.20, 0.22, 0.22, 0.10, None), "copper": (0.62, 0.38, 0.20, 0.60, None), "verdigris": (0.33, 0.55, 0.48, 0.30, None),
    "pot": (0.62, 0.32, 0.20, 0.10, None), "lifeboat": (0.95, 0.42, 0.08, 0.50, None),
}
for _k, (_r, _g, _b, _gl, _t) in LANDMARK_MATS.items(): MATS[_k] = (_r, _g, _b, _gl, 0, False)
MATS["window"] = (0.03, 0.035, 0.04, 0.9, 0, False)                    # night-lit window glass in the game
MATS["lantern_glow"] = (1.0, 0.93, 0.78, 0.0, 0, True)
MATS["deck_light"] = (0.95, 0.85, 0.30, 0.0, 0, True)
MAT_TEX = {k: v[4] for k, v in LANDMARK_MATS.items() if v[4]}
LIVERY_PREVIEW = {1: (0.93, 0.93, 0.90), 2: (0.85, 0.36, 0.12), 3: (0.12, 0.20, 0.30)}


# Physically based materials for the .glb only (the .rcm keeps the game's own colour, gloss and slot data):
# key: (metallic, roughness, clear coat). Keys not listed get a roughness from their gloss.
GLTF_PBR = {
    "paint": (0.0, 0.30, 1.0), "stripe": (0.0, 0.30, 1.0), "accent": (0.0, 0.30, 1.0), "accentdk": (0.0, 0.34, 1.0),
    "belly": (0.0, 0.38, 0.6), "seam": (0.0, 0.55, 0.0), "bladetip": (0.0, 0.35, 0.6),
    "frame": (0.0, 0.42, 0.3), "frame2": (0.0, 0.42, 0.3),
    "metal": (1.0, 0.34, 0.0), "chrome": (1.0, 0.07, 0.0), "darkmetal": (0.85, 0.42, 0.0), "engine": (0.7, 0.48, 0.0),
    "fins": (0.8, 0.40, 0.0), "exhaust": (0.6, 0.62, 0.0), "bladele": (1.0, 0.28, 0.0), "blade": (0.0, 0.42, 0.3),
    "rubber": (0.0, 0.88, 0.0), "belt": (0.0, 0.80, 0.0), "grip": (0.0, 0.75, 0.0),
    "seat": (0.0, 0.70, 0.0), "harness": (0.0, 0.78, 0.0), "floor": (0.0, 0.85, 0.0), "trim": (0.0, 0.72, 0.0),
    "panel": (0.0, 0.55, 0.0), "glareshield": (0.0, 0.90, 0.0), "headset": (0.0, 0.45, 0.0), "seatframe": (0.7, 0.45, 0.0),
    "red": (0.0, 0.32, 0.5), "yellow": (0.0, 0.35, 0.5), "hoistyellow": (0.0, 0.35, 0.5), "fire_red": (0.0, 0.32, 0.5),
}
# Transparent materials in the .glb: key: (tint r, g, b, alpha, roughness)
GLTF_GLASS = {
    "glass": (0.60, 0.68, 0.72, 0.16, 0.02),     # lightly tinted, clear enough to see the crew and panel
    "window": (0.55, 0.60, 0.64, 0.30, 0.03),
    "visor": (0.25, 0.40, 0.30, 0.45, 0.04),
    "lens": (0.95, 0.95, 0.92, 0.35, 0.02),
}
# Parts built only for the .glb (the game draws these itself, so they stay out of the .rcm)
GLTF_ONLY_PARTS = {"tail_rotor"}


def tex_layer(key):
    t = MAT_TEX.get(key)
    return TEX_LAYERS.index(t) if t else 0


def box_uv(pos, nrm, layer):
    """Texture coordinates as the game's MeshBuilder.texture() makes them: box mapping in real-world metres."""
    import numpy as np
    size = np.array([TEX_SIZE[TEX_LAYERS[l]] for l in range(len(TEX_LAYERS))])[layer]
    ax, ay, az = np.abs(nrm[:, 0]), np.abs(nrm[:, 1]), np.abs(nrm[:, 2])
    u = np.where(ay > 0.55, pos[:, 0], np.where(ax > az, pos[:, 2], pos[:, 0]))
    v = np.where(ay > 0.55, pos[:, 2], pos[:, 1])
    uv = np.stack([u / size, v / size], axis=1)
    uv[layer == 0] = 0
    return uv


def mat_alpha(key):
    if key == "window": return 0.01
    r, g, b, gloss, slot, emis = MATS[key]
    return 0.045 if emis else 0.08 + 0.92 * gloss


def mat_preview_rgb(key):
    r, g, b, gloss, slot, emis = MATS[key]
    if slot:
        L = LIVERY_PREVIEW[slot]
        return (r * L[0], g * L[1], b * L[2])
    return (r, g, b)


# ============================================================================ vector helpers
def add(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def mul(a, s): return (a[0] * s, a[1] * s, a[2] * s)
def dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
def length(a): return math.sqrt(dot(a, a))
def norm(a):
    l = length(a)
    return (a[0] / l, a[1] / l, a[2] / l) if l > 1e-12 else (0.0, 1.0, 0.0)
def lerp(a, b, t): return a + (b - a) * t
def clamp01(x): return 0.0 if x < 0 else 1.0 if x > 1 else x
def smooth(e0, e1, x):
    t = clamp01((x - e0) / (e1 - e0)); return t * t * (3 - 2 * t)


def pchip(xs, ys):
    n = len(xs); d = [(ys[i + 1] - ys[i]) / (xs[i + 1] - xs[i]) for i in range(n - 1)]; m = []
    for i in range(n):
        if i == 0: m.append(d[0])
        elif i == n - 1: m.append(d[n - 2])
        elif d[i - 1] * d[i] <= 0: m.append(0.0)
        else: m.append(3 * (xs[i + 1] - xs[i - 1]) / ((2 * xs[i + 1] - xs[i] - xs[i - 1]) / d[i - 1] + (xs[i + 1] + xs[i] - 2 * xs[i - 1]) / d[i]))
    def f(x):
        if x <= xs[0]: return ys[0]
        if x >= xs[-1]: return ys[-1]
        i = 0
        while x > xs[i + 1]: i += 1
        h = xs[i + 1] - xs[i]; t = (x - xs[i]) / h; t2 = t * t; t3 = t2 * t
        return (2 * t3 - 3 * t2 + 1) * ys[i] + (t3 - 2 * t2 + t) * h * m[i] + (-2 * t3 + 3 * t2) * ys[i + 1] + (t3 - t2) * h * m[i + 1]
    return f


def basis_from(d):
    """Two unit vectors perpendicular to d."""
    tmp = (0.0, 1.0, 0.0) if abs(d[1]) < 0.9 else (1.0, 0.0, 0.0)
    e1 = norm(cross(d, tmp)); e2 = cross(d, e1)
    return e1, e2


# ============================================================================ geometry objects
class Obj:
    """One Blender object: vertices, faces (each wound counter-clockwise seen from outside) and a material key per face."""
    def __init__(self, name, part, **opts):
        self.name, self.part = name, part
        self.v, self.f, self.m = [], [], []
        # smooth: smooth shading; sharp: edges sharper than this (deg) stay hard; bevel: bevel width (m);
        # subsurf: subdivision levels; ao: bake shading onto it
        self.o = dict(smooth=True, sharp=42.0, bevel=0.0, segs=3, subsurf=0, ao=True)
        self.o.update(opts)

    def vert(self, p): self.v.append((float(p[0]), float(p[1]), float(p[2]))); return len(self.v) - 1
    def face(self, idx, mat): self.f.append(tuple(idx)); self.m.append(mat)


class Model:
    def __init__(self): self.objs = []; self.count = {}
    def new(self, name, part, **opts):
        n = self.count.get(name, 0); self.count[name] = n + 1
        o = Obj(name if n == 0 else "%s.%03d" % (name, n), part, **opts); self.objs.append(o); return o


def oriented_quad(o, ids, outward, mat):
    """Add quad ids (a,b,c,d) wound so its normal agrees with `outward`."""
    P = [o.v[i] for i in ids]
    fn = add(cross(sub(P[1], P[0]), sub(P[2], P[0])), cross(sub(P[2], P[0]), sub(P[3], P[0])))
    if dot(fn, outward) >= 0: o.face(ids, mat)
    else: o.face((ids[0], ids[3], ids[2], ids[1]), mat)


def surface(o, nu, nv, P, paint, umap=None, inward=False):
    """Parametric surface P(u,v), u in [0,1] along, v in [0,1) around (closed). paint(c, n, u, v) -> material key
    or None to leave the quad out. Faces are wound outward (away from each ring's centroid) unless inward."""
    umap = umap or (lambda i: i / nu)
    grid, cent = [], []
    for i in range(nu + 1):
        u = umap(i); row = [P(u, j / nv) for j in range(nv)]
        grid.append(row)
        c = (0.0, 0.0, 0.0)
        for p in row: c = add(c, p)
        cent.append(mul(c, 1.0 / nv))
    ids = [[None] * nv for _ in range(nu + 1)]
    def vid(i, j):
        if ids[i][j] is None: ids[i][j] = o.vert(grid[i][j])
        return ids[i][j]
    for i in range(nu):
        for j in range(nv):
            j2 = (j + 1) % nv
            ps = [grid[i][j], grid[i + 1][j], grid[i + 1][j2], grid[i][j2]]
            c = mul(add(add(ps[0], ps[1]), add(ps[2], ps[3])), 0.25)
            ring_c = mul(add(cent[i], cent[i + 1]), 0.5)
            out = sub(c, ring_c)
            if length(out) < 1e-9: out = sub(cent[min(nu, i + 1)], cent[max(0, i)])
            fn = add(cross(sub(ps[1], ps[0]), sub(ps[2], ps[0])), cross(sub(ps[2], ps[0]), sub(ps[3], ps[0])))
            if length(fn) < 1e-14: continue                                    # degenerate (pole)
            n = norm(fn) if dot(fn, out) >= 0 else mul(norm(fn), -1)
            mat = paint(c, n, (umap(i) + umap(i + 1)) / 2, (j + 0.5) / nv)
            if mat is None: continue
            q = (vid(i, j), vid(i + 1, j), vid(i + 1, j2), vid(i, j2))
            oriented_quad(o, q, mul(n, -1) if inward else n, mat)
    return grid


def cap_fan(o, ring, nn, mat):
    c = (0.0, 0.0, 0.0)
    for p in ring: c = add(c, p)
    c = mul(c, 1.0 / len(ring))
    ci = o.vert(c); rid = [o.vert(p) for p in ring]; n = len(ring)
    flip = dot(cross(sub(ring[0], c), sub(ring[1], c)), nn) < 0
    for j in range(n):
        a, b = rid[j], rid[(j + 1) % n]
        o.face((ci, b, a) if flip else (ci, a, b), mat)


def tube(m, part, name, pts, r, mat, seg=12, caps=True, steps=6, **opts):
    """Swept round tube along a Catmull-Rom smoothed polyline. r: number or function of t in [0,1]."""
    o = m.new(name, part, **opts)
    path = []
    for i in range(len(pts) - 1):
        p0, p1, p2, p3 = pts[max(0, i - 1)], pts[i], pts[i + 1], pts[min(len(pts) - 1, i + 2)]
        for k in range(steps):
            t = k / steps; t2 = t * t; t3 = t2 * t
            path.append(tuple(0.5 * (2 * p1[a] + (-p0[a] + p2[a]) * t + (2 * p0[a] - 5 * p1[a] + 4 * p2[a] - p3[a]) * t2 + (-p0[a] + 3 * p1[a] - 3 * p2[a] + p3[a]) * t3) for a in range(3)))
    path.append(tuple(pts[-1]))
    n = len(path)
    tang = [norm(sub(path[min(n - 1, i + 1)], path[max(0, i - 1)])) for i in range(n)]
    e1, _ = basis_from(tang[0]); frames = []
    for i in range(n):
        e1 = norm(sub(e1, mul(tang[i], dot(e1, tang[i])))); frames.append((e1, cross(tang[i], e1)))
    rad = (lambda t: r) if not callable(r) else r
    def P(u, v):
        i = int(round(u * (n - 1))); a = v * math.pi * 2; f1, f2 = frames[i]; rr = rad(i / (n - 1))
        return add(path[i], add(mul(f1, math.cos(a) * rr), mul(f2, math.sin(a) * rr)))
    surface(o, n - 1, seg, P, lambda *_: mat)
    if caps:
        for (pi, nn, t) in ((0, mul(tang[0], -1), 0.0), (n - 1, tang[n - 1], 1.0)):
            f1, f2 = frames[pi]; rr = rad(t)
            ring = [add(path[pi], add(mul(f1, math.cos(j / seg * 2 * math.pi) * rr), mul(f2, math.sin(j / seg * 2 * math.pi) * rr))) for j in range(seg)]
            cap_fan(o, ring, nn, mat)
    return o


def cyl(m, part, name, p0, p1, r0, r1, mat, seg=16, caps=True, mat_cap=None, **opts):
    o = m.new(name, part, **opts)
    d = norm(sub(p1, p0)); e1, e2 = basis_from(d)
    ring0, ring1 = [], []
    for j in range(seg):
        a = j / seg * 2 * math.pi; rv = add(mul(e1, math.cos(a)), mul(e2, math.sin(a)))
        ring0.append(o.vert(add(p0, mul(rv, r0)))); ring1.append(o.vert(add(p1, mul(rv, r1))))
    for j in range(seg):
        j2 = (j + 1) % seg
        a = j / seg * 2 * math.pi; rv = add(mul(e1, math.cos(a + math.pi / seg)), mul(e2, math.sin(a + math.pi / seg)))
        oriented_quad(o, (ring0[j], ring1[j], ring1[j2], ring0[j2]), rv, mat)
    if caps:
        for ring, nn, c, rr in ((ring0, mul(d, -1), p0, r0), (ring1, d, p1, r1)):
            if rr <= 0: continue
            ci = o.vert(c)
            for j in range(seg):
                a, b = ring[j], ring[(j + 1) % seg]
                P = [o.v[ci], o.v[a], o.v[b]]
                if dot(cross(sub(P[1], P[0]), sub(P[2], P[0])), nn) >= 0: o.face((ci, a, b), mat_cap or mat)
                else: o.face((ci, b, a), mat_cap or mat)
    return o


def ring_band(m, part, name, c, axis, r_in, r_out, h, mat, seg=24, **opts):
    """Short thick ring (a strap, a fin, a clamp) around an axis."""
    a = norm(axis); p0 = sub(c, mul(a, h / 2)); p1 = add(c, mul(a, h / 2))
    o = m.new(name, part, **opts)
    e1, e2 = basis_from(a)
    rings = []
    for (p, r) in ((p0, r_out), (p1, r_out), (p1, r_in), (p0, r_in)):
        rings.append([o.vert(add(p, add(mul(e1, math.cos(j / seg * 2 * math.pi) * r), mul(e2, math.sin(j / seg * 2 * math.pi) * r)))) for j in range(seg)])
    outs = [(0, 1, None), (1, 2, a), (2, 3, None), (3, 0, mul(a, -1))]
    for (ra, rb, nn) in outs:
        for j in range(seg):
            j2 = (j + 1) % seg; ang = (j + 0.5) / seg * 2 * math.pi
            radial = add(mul(e1, math.cos(ang)), mul(e2, math.sin(ang)))
            outward = nn if nn is not None else (radial if ra == 0 else mul(radial, -1))
            oriented_quad(o, (rings[ra][j], rings[rb][j], rings[rb][j2], rings[ra][j2]), outward, mat)
    return o


def box(m, part, name, c, size, mat, axes=None, bevel=0.0, mats=None, **opts):
    """Box centred on c with full sizes (sx, sy, sz) along axes (ex, ey, ez) (default world axes).
    mats: optional per-face material dict with keys '+x','-x','+y','-y','+z','-z'."""
    ex, ey, ez = axes or ((1.0, 0, 0), (0, 1.0, 0), (0, 0, 1.0))
    o = m.new(name, part, bevel=bevel, **opts)
    hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
    def P(x, y, z): return add(c, add(add(mul(ex, x * hx), mul(ey, y * hy)), mul(ez, z * hz)))
    v = {}
    for x in (-1, 1):
        for y in (-1, 1):
            for z in (-1, 1): v[(x, y, z)] = o.vert(P(x, y, z))
    faces = {
        "+x": ((1, -1, -1), (1, 1, -1), (1, 1, 1), (1, -1, 1), ex), "-x": ((-1, -1, -1), (-1, -1, 1), (-1, 1, 1), (-1, 1, -1), mul(ex, -1)),
        "+y": ((-1, 1, -1), (-1, 1, 1), (1, 1, 1), (1, 1, -1), ey), "-y": ((-1, -1, -1), (1, -1, -1), (1, -1, 1), (-1, -1, 1), mul(ey, -1)),
        "+z": ((-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1), ez), "-z": ((-1, -1, -1), (-1, 1, -1), (1, 1, -1), (1, -1, -1), mul(ez, -1)),
    }
    for k, (a, b, cc, d, nn) in faces.items():
        oriented_quad(o, (v[a], v[b], v[cc], v[d]), nn, (mats or {}).get(k, mat))
    return o


def ellipsoid(m, part, name, c, r, mat, nu=16, nv=10, paint=None, **opts):
    o = m.new(name, part, **opts)
    top = o.vert((c[0], c[1] + r[1], c[2])); bot = o.vert((c[0], c[1] - r[1], c[2]))
    rings = []
    for i in range(1, nv):
        th = i / nv * math.pi
        rings.append([o.vert((c[0] + math.sin(th) * math.cos(j / nu * 2 * math.pi) * r[0], c[1] + math.cos(th) * r[1], c[2] + math.sin(th) * math.sin(j / nu * 2 * math.pi) * r[2])) for j in range(nu)])
    pm = paint or (lambda p: mat)
    def outward(ids):
        q = (0.0, 0.0, 0.0)
        for i in ids: q = add(q, o.v[i])
        q = mul(q, 1.0 / len(ids)); return sub(q, c), q
    for j in range(nu):
        j2 = (j + 1) % nu
        for (pole, ring) in ((top, rings[0]), (bot, rings[-1])):
            ids = (pole, ring[j], ring[j2]); out, q = outward(ids)
            P = [o.v[i] for i in ids]
            if dot(cross(sub(P[1], P[0]), sub(P[2], P[0])), out) >= 0: o.face(ids, pm(q))
            else: o.face((pole, ring[j2], ring[j]), pm(q))
        for i in range(len(rings) - 1):
            ids = (rings[i][j], rings[i + 1][j], rings[i + 1][j2], rings[i][j2]); out, q = outward(ids)
            oriented_quad(o, ids, out, pm(q))
    return o


def naca(x, t): return 5 * t * (0.2969 * math.sqrt(x) - 0.126 * x - 0.3516 * x * x + 0.2843 * x ** 3 - 0.1036 * x ** 4)


def wing(m, part, name, rootLE, tipLE, rootChord, tipChord, thick, cd, td, paint, nu=8, nv=20, ufeats=(), **opts):
    """Aerofoil surface. ufeats: span fractions that get a row of their own (so a paint line there is straight)."""
    o = m.new(name, part, **opts)
    sp = norm(sub(tipLE, rootLE))
    def P(u, v):
        le = add(rootLE, mul(sub(tipLE, rootLE), u)); c = lerp(rootChord, tipChord, u)
        up = v < 0.5; s = v * 2 if up else (1 - v) * 2
        x = 0.5 - 0.5 * math.cos(math.pi * s); y = naca(max(1e-4, x), thick) * (1 if up else -1)
        return add(le, add(mul(cd, x * c), mul(td, y * c)))
    rows = sorted(set([i / nu for i in range(nu + 1)] + list(ufeats)))
    surface(o, len(rows) - 1, nv, P, lambda c, n, u, v: paint(c), umap=lambda i: rows[i])
    for u, nn in ((0, mul(sp, -1)), (1, sp)):
        ring = [P(u, j / nv) for j in range(nv)]
        cap_fan(o, ring, nn, paint(ring[0]))
    return o


# ============================================================================ opening doors cut from a finished model
def carve_door(m, side, z0, z1, ylo, yhi, door_part, src_parts=("body", "cockpit"), min_x=0.25, jamb=0.035):
    """Move everything inside a door outline (one side of the aircraft, z0..z1, ylo(z)..yhi(z)) into its own part
    so the game can open it: whole objects that lie inside go across as they are; faces of larger objects (the
    skin, the lining) are cut out by their centres. The raw edge of the cut skin gets a dark jamb in the body
    and a matching edge on the door, so an open door shows a finished frame."""
    def inside(p):
        x, y, z = p
        return x * side > min_x and z0 < z < z1 and ylo(z) < y < yhi(z)
    moved = []
    for o in list(m.objs):
        if o.part not in src_parts or not o.f: continue
        if all(inside(v) for v in o.v):
            o.part = door_part; continue
        keep_f, keep_m, cut = [], [], []
        for f, mat in zip(o.f, o.m):
            c = mul(add(add(o.v[f[0]], o.v[f[1]]), o.v[f[2]]), 1.0 / 3.0) if len(f) == 3 else mul(add(add(o.v[f[0]], o.v[f[1]]), add(o.v[f[2]], o.v[f[3]])), 0.25)
            if inside(c): cut.append((f, mat))
            else: keep_f.append(f); keep_m.append(mat)
        if not cut: continue
        o.f, o.m = keep_f, keep_m
        d = m.new(o.name + " (door)", door_part, **o.o)
        remap = {}
        for f, mat in cut:
            ids = []
            for i in f:
                if i not in remap: remap[i] = d.vert(o.v[i])
                ids.append(remap[i])
            d.face(tuple(ids), mat)
        moved.append((o, cut))
    m.objs = [o for o in m.objs if o.f]
    # jambs along the boundary of the cut skin (edges used by exactly one cut face)
    for o, cut in moved:
        if o.part != "body": continue
        count = {}
        for f, _ in cut:
            for k in range(len(f)):
                e = (f[k], f[(k + 1) % len(f)]); key = tuple(sorted(e))
                count[key] = count.get(key, 0) + 1
        jb, jd = m.new("Door jamb", "body"), m.new("Door edge", door_part)
        cz = (z0 + z1) / 2
        for (a, b), n in count.items():
            if n != 1: continue
            pa, pb = o.v[a], o.v[b]
            ia, ib = (pa[0] - side * jamb, pa[1], pa[2]), (pb[0] - side * jamb, pb[1], pb[2])
            mid = mul(add(pa, pb), 0.5)
            yc = (ylo(cz) + yhi(cz)) / 2
            towards = (0.0, yc - mid[1], cz - mid[2])                     # into the opening
            oriented_quad(jb, (jb.vert(pa), jb.vert(pb), jb.vert(ib), jb.vert(ia)), towards, "seal")
            oriented_quad(jd, (jd.vert(pa), jd.vert(pb), jd.vert(ib), jd.vert(ia)), mul(towards, -1), "seal")
    return door_part


def cabin_lining(m, H, part, z0, z1, floor_y, mat="trim_ns", inset=0.97, n=40):
    """Inside of a cabin: the hull section turned inward, a floor, and a bulkhead at each end."""
    o = m.new("Cabin lining", part)
    surface(o, n, 48, lambda u, v: H.P(lerp(z0, z1, u), -math.pi / 2 + v * 2 * math.pi, inset), lambda c, nn, u, v: "floor" if c[1] < floor_y + 0.02 else mat, inward=True)
    w = H.w((z0 + z1) / 2) * inset * 2
    box(m, part, "Cabin floor", (0, floor_y, (z0 + z1) / 2), (w * 0.92, 0.02, z1 - z0), "floor")
    cap_fan(m.new("Rear bulkhead", part), [H.P(z1, j / 64 * 2 * math.pi, inset) for j in range(64)], (0, 0, -1), mat)


# ============================================================================ the hull
CABIN = [  # z, yc, half-width, upper and lower half-heights, superellipse exponent
    (-1.48, 1.20, 0.02, 0.02, 0.02, 2.0), (-1.36, 1.20, 0.38, 0.42, 0.34, 2.0), (-1.08, 1.26, 0.58, 0.64, 0.46, 2.1),
    (-0.62, 1.30, 0.68, 0.74, 0.50, 2.3), (-0.05, 1.31, 0.68, 0.72, 0.50, 2.6), (0.45, 1.33, 0.58, 0.58, 0.44, 2.8),
    (0.78, 1.38, 0.36, 0.34, 0.32, 2.4), (0.90, 1.42, 0.08, 0.08, 0.08, 2.0),
]


class Hull:
    """Lofted body through stations (z, yc, half-width, upper and lower half-heights, superellipse exponent[, xc])."""
    def __init__(self, st):
        zs = [s[0] for s in st]
        self.yc, self.w, self.ht, self.hb, self.ne = (pchip(zs, [s[k] for s in st]) for k in range(1, 6))
        self.xc = pchip(zs, [s[6] if len(s) > 6 else 0.0 for s in st])
        self.z0, self.z1 = zs[0], zs[-1]

    def P(self, z, th, inset=1.0):
        c, s = math.cos(th), math.sin(th); e = 2 / max(1.6, self.ne(z))
        x = self.xc(z) + self.w(z) * math.copysign(abs(c) ** e, c) * inset
        y = self.yc(z) + (self.ht(z) if s >= 0 else self.hb(z)) * math.copysign(abs(s) ** e, s) * inset
        return (x, y, z)

    def th_at_y(self, z, y, side):
        lo, hi = -math.pi / 2, math.pi / 2
        for _ in range(40):
            mid = (lo + hi) / 2
            if self.P(z, mid)[1] < y: lo = mid
            else: hi = mid
        t = (lo + hi) / 2
        return t if side > 0 else math.pi - t

    def point(self, z, th, off=0.0):
        """Surface point pushed `off` metres out along the surface normal (for parts laid on the skin)."""
        p = self.P(z, th)
        if off == 0.0: return p
        dz, dt = 1e-3, 1e-3
        a = sub(self.P(z + dz, th), self.P(z - dz, th)); b = sub(self.P(z, th + dt), self.P(z, th - dt))
        n = norm(cross(b, a))
        if dot(n, sub(p, (self.xc(z), self.yc(z), z))) < 0: n = mul(n, -1)
        return add(p, mul(n, off))

    def on_side(self, z, y, side, off=0.0):
        """Point on the given side (+1 right, -1 left) at height y."""
        return self.point(z, self.th_at_y(z, y, side), off)


def waist(z): return 1.31 + 0.08 * clamp01((-z - 0.9) / 0.5) - 0.02
def near(a, b, w): return abs(a - b) < w

DOOR_FWD, DOOR_AFT = -0.16, 0.42


def cabin_paint(p, n=None):
    x, y, z = p
    w = waist(z)
    door_edge = near(z, DOOR_FWD, 0.02) or near(z, DOOR_AFT, 0.02)
    if y > w and z < 0.36:
        if y > 1.98 and z > -0.3: return "paint"                                   # roof panel
        return "glass"
    if z < -1.0 and y > 1.0 and abs(x) > 0.12: return "glass"                      # chin windows
    if door_edge and abs(x) > 0.35 and y > 0.9: return "seam"                      # door shut lines
    if near(y, 0.98, 0.012) and z > DOOR_FWD and z < DOOR_AFT and abs(x) > 0.3: return "seam"   # door bottom
    if w - 0.13 < y < w - 0.03: return "stripe"                                     # cheatline
    if y < 0.98: return "belly"
    return "paint"


# ============================================================================ surface detail: seams, rivets, fasteners
def hull_sample(H, z, th):
    """Point on a lofted surface and its outward normal."""
    p = H.P(z, th); dz, dt = 1e-3, 1e-3
    a = sub(H.P(z + dz, th), H.P(z - dz, th)); b = sub(H.P(z, th + dt), H.P(z, th - dt))
    n = norm(cross(b, a))
    if dot(n, sub(p, (H.xc(z), H.yc(z), z))) < 0: n = mul(n, -1)
    return p, n


def ring_path(H, z, th0, th1, step):
    """Samples every `step` metres round a station, from angle th0 to th1."""
    fine = [hull_sample(H, z, lerp(th0, th1, i / 400)) for i in range(401)]
    out, acc = [fine[0]], 0.0
    for a, b in zip(fine, fine[1:]):
        acc += length(sub(b[0], a[0]))
        if acc >= step: out.append(b); acc = 0.0
    return out


def length_path(H, z0, z1, th, step):
    """Samples every `step` metres along the surface at a fixed angle th."""
    n = max(2, int(abs(z1 - z0) / step) + 1)
    return [hull_sample(H, lerp(z0, z1, i / (n - 1)), th) for i in range(n)]


def runs(samples, keep):
    """Split samples into the contiguous runs where keep(point) is true."""
    out, cur = [], []
    for s in samples:
        if keep(s[0]): cur.append(s)
        elif cur: out.append(cur); cur = []
    if cur: out.append(cur)
    return [r for r in out if len(r) > 1]


def seam(m, part, name, samples, mat="seam", width=0.004, lift=0.0006):
    """A panel joint: a thin dark strip laid on the skin along samples [(point, normal)]."""
    if len(samples) < 2: return None
    o = m.new(name, part, sharp=80.0)
    ids = []
    for i, (p, n) in enumerate(samples):
        t = norm(sub(samples[min(i + 1, len(samples) - 1)][0], samples[max(i - 1, 0)][0]))
        s = norm(cross(n, t)); q = add(p, mul(n, lift))
        ids.append((o.vert(add(q, mul(s, width / 2))), o.vert(add(q, mul(s, -width / 2)))))
    for i in range(len(ids) - 1):
        oriented_quad(o, (ids[i][0], ids[i + 1][0], ids[i + 1][1], ids[i][1]), samples[i][1], mat)
    return o


def rivets(m, part, name, samples, mat, r=0.0032, h=0.0013, seg=6):
    """Domed rivet heads at each sample [(point, normal)], all in one object. mat: key or function of the point
    (None leaves that rivet out, e.g. where the skin is glass)."""
    o = m.new(name, part, sharp=85.0)
    for p, n in samples:
        k = mat(p) if callable(mat) else mat
        if k is None: continue
        e1, e2 = basis_from(n)
        top = o.vert(add(p, mul(n, h)))
        base = add(p, mul(n, -0.0003))
        ring = [o.vert(add(base, add(mul(e1, math.cos(j / seg * 2 * math.pi) * r), mul(e2, math.sin(j / seg * 2 * math.pi) * r)))) for j in range(seg)]
        for j in range(seg):
            a, b = ring[j], ring[(j + 1) % seg]
            fn = cross(sub(o.v[a], o.v[top]), sub(o.v[b], o.v[top]))
            o.face((top, a, b) if dot(fn, n) >= 0 else (top, b, a), k)
    return o if o.f else None


def fasteners(m, part, name, samples, r=0.0075):
    """Quarter-turn cowling fasteners: a flush steel disc with a screwdriver slot."""
    for p, n in samples:
        e1, _ = basis_from(n)
        cyl(m, part, name, add(p, mul(n, -0.0004)), add(p, mul(n, 0.0012)), r, r * 0.92, "chrome", 12)
        box(m, part, name + " slot", add(p, mul(n, 0.0014)), (r * 1.5, r * 0.25, 0.0006), "darkmetal", axes=(e1, cross(n, e1), n))


def gauge(m, part, F, cx, cy, r, name, sweep=(225.0, -45.0), marks=25, major=5, arcs=(), needle_deg=None, kind="dial"):
    """A round instrument laid on face F: bezel, face, scale marks, coloured arcs, needle and cover glass. The
    needle is its own object, "Needle: <name>", so a live panel can turn it. Angles in degrees, 0 = right, CCW."""
    ez = F.ez
    ring_band(m, part, "Gauge bezel", F.at(cx, cy, 0.004), ez, r * 0.98, r * 1.2, 0.009, "darkmetal", 32)
    if kind == "attitude":
        # sky over ground with a white horizon bar and the little orange aircraft
        for half, mat in ((0, "ai_sky"), (1, "ai_ground")):
            o = m.new("Gauge face", part)
            c = o.vert(F.at(cx, cy, 0.0012))
            pts = [o.vert(F.at(cx + r * math.cos(math.pi * (half + i / 16)), cy + r * math.sin(math.pi * (half + i / 16)), 0.0012)) for i in range(17)]
            for i in range(16): o.face((c, pts[i], pts[i + 1]), mat)
        F.obox((cx, cy, 0.0018), (r * 1.7, r * 0.04, 0.0008), "gauge_mark", "Horizon line")
        for a in (60, 75, 90, 105, 120):
            d = math.radians(a)
            F.obox((cx + r * 0.84 * math.cos(d), cy + r * 0.84 * math.sin(d), 0.0018), (r * 0.03, r * 0.16, 0.0008), "gauge_mark", "Bank mark")
        F.obox((cx, cy - r * 0.08, 0.0035), (r * 0.9, r * 0.07, 0.001), "yellow", "Aircraft symbol")
        F.obox((cx, cy - r * 0.02, 0.0035), (r * 0.08, r * 0.14, 0.001), "yellow", "Aircraft symbol")
    else:
        cyl(m, part, "Gauge face", F.at(cx, cy, -0.002), F.at(cx, cy, 0.0012), r, r, "gauge_face", 32)
        o = m.new("Gauge marks", part)
        a0, a1 = sweep
        for i in range(marks):
            a = math.radians(lerp(a0, a1, i / max(1, marks - 1)))
            big = i % major == 0
            r0, r1, w = r * (0.68 if big else 0.8), r * 0.92, r * (0.055 if big else 0.028)
            d, s = (math.cos(a), math.sin(a)), (-math.sin(a), math.cos(a))
            q = [F.at(cx + d[0] * rr + s[0] * ww, cy + d[1] * rr + s[1] * ww, 0.0016) for rr, ww in ((r0, -w / 2), (r1, -w / 2), (r1, w / 2), (r0, w / 2))]
            oriented_quad(o, tuple(o.vert(p) for p in q), ez, "gauge_mark")
        for (f0, f1, mat) in arcs:                                  # coloured range arcs on the rim, as fractions of the sweep
            o = m.new("Gauge arc", part)
            n = 10
            for i in range(n):
                aa, bb = math.radians(lerp(a0, a1, lerp(f0, f1, i / n))), math.radians(lerp(a0, a1, lerp(f0, f1, (i + 1) / n)))
                q = [F.at(cx + math.cos(t) * rr, cy + math.sin(t) * rr, 0.0017) for t, rr in ((aa, r * 0.93), (aa, r * 0.99), (bb, r * 0.99), (bb, r * 0.93))]
                oriented_quad(o, tuple(o.vert(p) for p in q), ez, mat)
        a = math.radians(needle_deg if needle_deg is not None else a0)
        d, s = (math.cos(a), math.sin(a), 0.0), (-math.sin(a), math.cos(a), 0.0)
        ax = (add(mul(F.ex, d[0]), mul(F.ey, d[1])), add(mul(F.ex, s[0]), mul(F.ey, s[1])), ez)
        box(m, part, "Needle: " + name, F.at(cx + d[0] * r * 0.32, cy + d[1] * r * 0.32, 0.003), (r * 0.9, r * 0.055, 0.0012), "needle", axes=ax)
        cyl(m, part, "Needle hub", F.at(cx, cy, 0.0024), F.at(cx, cy, 0.0045), r * 0.11, r * 0.09, "darkmetal", 12)
    cyl(m, part, "Gauge glass", F.at(cx, cy, 0.0062), F.at(cx, cy, 0.0072), r * 1.02, r * 1.02, "glass", 32)


# ============================================================================ exterior
CHIN_Z, ROOF_Z, GLASS_END_Z = -1.0, -0.3, 0.36
KESTREL_BOOM = [(1.4, 1.70, 0.20, 0.20, 0.20, 2), (2.0, 1.67, 0.15, 0.15, 0.15, 2), (4.7, 1.64, 0.085, 0.085, 0.085, 2),
                (5.05, 1.63, 0.07, 0.07, 0.07, 2), (5.12, 1.63, 0.02, 0.02, 0.02, 2)]
SKIN_SEGS = (8, 6, 2, 3, 9, 6)          # vertices per side in each band: belly, lower body, stripe, upper body, side glass, roof


def cabin_rows(H, n=84):
    """Stations along the cabin, denser at both ends, with a row exactly on every paint line across it."""
    base = [lerp(H.z0, H.z1, 0.55 * (0.5 - 0.5 * math.cos(math.pi * i / n)) + 0.45 * i / n) for i in range(n + 1)]
    feats = [CHIN_Z, ROOF_Z, DOOR_FWD - 0.02, DOOR_FWD + 0.02, DOOR_AFT - 0.02, DOOR_AFT + 0.02, GLASS_END_Z]
    keep = [z for i, z in enumerate(base) if i in (0, n) or min(abs(z - f) for f in feats) > 0.012]
    return sorted(set(keep + feats))


def cabin_ring(H, z):
    """Angles round one section, with vertices exactly on the belly line, the stripe edges, the waist and the
    roof edge on both sides, and the band (0 belly .. 5 roof) of each quad that starts at that vertex."""
    w = waist(z)
    seq = [-math.pi / 2] + [H.th_at_y(z, y, 1) for y in (0.98, w - 0.13, w - 0.03, w, 1.98)] + [math.pi / 2]
    for i in range(1, len(seq)): seq[i] = min(math.pi / 2, max(seq[i], seq[i - 1]))
    right, rband = [], []
    for k, cnt in enumerate(SKIN_SEGS):
        for j in range(cnt): right.append(lerp(seq[k], seq[k + 1], j / cnt)); rband.append(k)
    angles = right + [math.pi / 2] + [math.pi - a for a in reversed(right[1:])]
    bands = rband + list(reversed(rband))           # quad j joins vertex j and j+1 (the last one wraps to the bottom)
    return angles, bands


def skin_paint(band, z):
    if band == 0: return "belly"
    door = near(z, DOOR_FWD, 0.021) or near(z, DOOR_AFT, 0.021)
    if z < CHIN_Z: return "glass"                                               # glazed nose and chin windows
    if band in (1, 2, 3): return "seam" if door else ("stripe" if band == 2 else "paint")
    if band == 4: return "glass" if z < GLASS_END_Z else "paint"
    return "glass" if z < ROOF_Z else "paint"                                   # roof: glass forward, panel aft


def build_cabin_skin(m, H, part):
    rows = cabin_rows(H)
    shell, glass = m.new("Cabin shell", part), m.new("Canopy glass", part)
    for o, inset, want_glass in ((shell, 1.0, False), (glass, 0.994, True)):
        rings, ring_bands = [], []
        for z in rows:
            ang, bands = cabin_ring(H, z)
            rings.append([H.P(z, a, inset) for a in ang]); ring_bands.append(bands)
        nv = len(rings[0]); ids = {}
        def vid(i, j):
            if (i, j) not in ids: ids[(i, j)] = o.vert(rings[i][j])
            return ids[(i, j)]
        for i in range(len(rows) - 1):
            zc = 0.5 * (rows[i] + rows[i + 1]); centre = (0.0, H.yc(zc), zc)
            for j in range(nv):
                j2 = (j + 1) % nv
                mat = skin_paint(ring_bands[i][j], zc)
                if (mat == "glass") != want_glass: continue
                ps = [rings[i][j], rings[i + 1][j], rings[i + 1][j2], rings[i][j2]]
                fn = add(cross(sub(ps[1], ps[0]), sub(ps[2], ps[0])), cross(sub(ps[2], ps[0]), sub(ps[3], ps[0])))
                if length(fn) < 1e-12: continue                                 # bands of zero width near the nose
                c = mul(add(add(ps[0], ps[1]), add(ps[2], ps[3])), 0.25)
                oriented_quad(o, (vid(i, j), vid(i + 1, j), vid(i + 1, j2), vid(i, j2)), sub(c, centre), mat)


def build_exterior(m, H):
    E = "body"
    # ---- cabin shell (painted) and glazing (set 6 mm inside, under the frames)
    build_cabin_skin(m, H, E)

    # ---- canopy frames: raised round frames on the surface
    fr = 0.021
    bow = [H.P(z, math.pi / 2, 1.0) for z in [-1.36 + 0.1 * i for i in range(13)]]
    tube(m, E, "Frame centre bow", bow, fr, "frame", 8, steps=2)
    for zf in (DOOR_FWD, 0.30):
        for side in (1, -1):
            t0 = H.th_at_y(zf, waist(zf), side); t1 = math.pi / 2 - 0.25 if side > 0 else math.pi / 2 + 0.25
            tube(m, E, "Frame door post", [H.P(zf, t0 + (t1 - t0) * i / 8, 1.0) for i in range(9)], fr, "frame", 8, steps=2)
    for side in (1, -1):
        tube(m, E, "Frame sill", [H.P(z, H.th_at_y(z, waist(z), side), 1.0) for z in [-1.3 + 0.13 * i for i in range(13)]], fr * 0.9, "frame", 8, steps=2)
    tube(m, E, "Frame roof bow", [H.P(-0.12, 0.35 + (math.pi - 0.7) * i / 12, 1.0) for i in range(13)], fr * 1.1, "frame", 8, steps=2)
    tube(m, E, "Frame windscreen arch", [H.P(-1.05, 0.55 + (math.pi - 1.1) * i / 12, 1.0) for i in range(13)], fr * 0.8, "frame", 8, steps=2)

    # ---- doors: handles, hinges, vent
    for side in (1, -1):
        hp = H.P(0.28, H.th_at_y(0.28, 1.2, side), 1.0)
        box(m, E, "Door handle", add(hp, (side * 0.012, 0, 0)), (0.02, 0.025, 0.11), "chrome", bevel=0.006)
        for y in (1.15, 1.75):
            th = H.th_at_y(DOOR_FWD + 0.03, y, side); hp = H.P(DOOR_FWD + 0.03, th, 1.0)
            cyl(m, E, "Door hinge", add(hp, (0, -0.03, 0)), add(hp, (0, 0.03, 0)), 0.012, 0.012, "darkmetal", 10)
        th = H.th_at_y(0.08, 1.62, side); vp = H.P(0.08, th, 1.0)
        cyl(m, E, "Door vent", vp, add(vp, (side * 0.012, 0, 0)), 0.045, 0.04, "frame", 20)

    # ---- engine bay: transmission cowling with louvres
    cowl = [(0.35, 1.95, 0.05, 0.05, 0.05, 2), (0.5, 1.95, 0.36, 0.24, 0.28, 2.6), (1.05, 1.95, 0.40, 0.27, 0.30, 3),
            (1.42, 1.82, 0.20, 0.18, 0.20, 2.4), (1.55, 1.78, 0.04, 0.04, 0.04, 2)]
    Hc = Hull(cowl)
    o = m.new("Upper cowling", E)
    def Pc(u, v): return Hc.P(lerp(0.35, 1.55, u), v * 2 * math.pi)
    def cowl_paint(c, n, u, v):
        x, y, z = c
        if y > 2.12: return "accent"
        if abs(x) > 0.3 and 0.62 < z < 0.98 and 1.86 < y < 2.06 and int((z - 0.62) / 0.045) % 2 == 0: return "frame"   # louvres
        if near(z, 0.62, 0.008): return "seam"
        return "paint"
    surface(o, 40, 36, Pc, cowl_paint)
    box(m, E, "Firewall", (0, 1.38, 0.52), (0.9, 0.9, 0.025), "darkmetal")

    # ---- engine: crankcase, finned cylinders, heads, fan, alternator, belts
    box(m, E, "Crankcase", (0, 1.18, 0.95), (0.52, 0.30, 0.48), "engine", bevel=0.03)
    box(m, E, "Sump", (0, 0.99, 0.95), (0.40, 0.10, 0.40), "darkmetal", bevel=0.02)
    for x in (-1, 1):
        for z in (0.82, 1.08):
            p0, p1 = (x * 0.26, 1.2, z), (x * 0.46, 1.2, z)
            cyl(m, E, "Cylinder barrel", p0, p1, 0.06, 0.06, "fins", 16)
            for i in range(6):
                xx = x * (0.29 + i * 0.026)
                ring_band(m, E, "Cooling fin", (xx, 1.2, z), (1, 0, 0), 0.06, 0.085, 0.006, "fins", 12)
            box(m, E, "Cylinder head", (x * 0.49, 1.2, z), (0.07, 0.15, 0.15), "engine", bevel=0.015)
            tube(m, E, "Pushrod tube", [(x * 0.27, 1.12, z + 0.06), (x * 0.47, 1.14, z + 0.06)], 0.008, "metal", 8)
            tube(m, E, "Ignition lead", [(x * 0.50, 1.28, z), (x * 0.38, 1.36, z - 0.05), (x * 0.1, 1.38, 0.95)], 0.005, "red", 6)
    cyl(m, E, "Fan scroll", (0, 1.18, 0.62), (0, 1.18, 0.72), 0.25, 0.25, "darkmetal", 28, caps=False)
    cyl(m, E, "Fan hub", (0, 1.18, 0.60), (0, 1.18, 0.66), 0.06, 0.05, "metal", 16)
    for i in range(9):
        a = i / 9 * 2 * math.pi; d = (math.cos(a), math.sin(a), 0)
        box(m, E, "Fan blade", add((0, 1.18, 0.64), mul(d, 0.14)), (0.16, 0.01, 0.05), "fins", axes=(d, (-d[1], d[0], 0), (0, 0, 1)))
    cyl(m, E, "Alternator", (0.18, 1.02, 0.66), (0.18, 1.02, 0.80), 0.055, 0.055, "metal", 16)
    # belt drive to the upper sheave
    cyl(m, E, "Lower sheave", (0, 1.18, 0.56), (0, 1.18, 0.60), 0.11, 0.11, "metal", 24)
    cyl(m, E, "Upper sheave", (0, 1.86, 0.56), (0, 1.86, 0.60), 0.13, 0.13, "metal", 24)
    for x in (-1, 1):
        box(m, E, "Drive belt", (x * 0.12, 1.52, 0.58), (0.012, 0.68, 0.035), "belt")
    # exhaust and muffler with heat shield
    tube(m, E, "Exhaust pipe", [(0.46, 1.12, 0.82), (0.36, 1.0, 0.95), (0.30, 0.98, 1.18)], 0.03, "exhaust", 10)
    cyl(m, E, "Muffler", (0.30, 0.98, 1.12), (0.30, 0.98, 1.40), 0.075, 0.075, "exhaust", 18)
    tube(m, E, "Tailpipe", [(0.30, 0.98, 1.38), (0.32, 0.94, 1.55), (0.33, 0.92, 1.66)], 0.04, "exhaust", 12)
    # fuel tanks with straps and the filler on the right
    for x in (-0.42, 0.42):
        cyl(m, E, "Fuel tank", (x, 1.82, 0.52), (x, 1.82, 1.08), 0.16, 0.16, "paint", 20, caps=False)
        ellipsoid(m, E, "Tank end", (x, 1.82, 0.52), (0.16, 0.16, 0.05), "paint", 20, 6)
        ellipsoid(m, E, "Tank end", (x, 1.82, 1.08), (0.16, 0.16, 0.05), "paint", 20, 6)
        for z in (0.64, 0.96):
            ring_band(m, E, "Tank strap", (x, 1.82, z), (0, 0, 1), 0.16, 0.168, 0.03, "darkmetal", 28)
    cyl(m, E, "Fuel filler neck", (0.50, 1.90, 0.80), (0.55, 1.97, 0.80), 0.035, 0.035, "metal", 16)
    cyl(m, E, "Fuel cap", (0.55, 1.97, 0.80), (0.57, 2.00, 0.80), 0.045, 0.045, "red", 16)
    # steel-tube frame
    for x in (-1, 1):
        tube(m, E, "Frame tube", [(x * 0.36, 0.95, 0.55), (x * 0.32, 1.70, 0.75), (x * 0.18, 2.0, 1.25)], 0.025, "metal", 8, steps=4)
        tube(m, E, "Frame tube", [(x * 0.36, 0.95, 1.30), (x * 0.22, 1.70, 1.25), (x * 0.08, 1.75, 1.55)], 0.025, "metal", 8, steps=4)
        tube(m, E, "Frame tube", [(x * 0.36, 0.95, 0.55), (x * 0.36, 0.95, 1.30)], 0.022, "metal", 10)
        tube(m, E, "Frame diagonal", [(x * 0.36, 0.95, 0.55), (x * 0.24, 1.72, 1.25)], 0.018, "metal", 10)

    # ---- mast, stationary swashplate
    cyl(m, E, "Mast fairing", (0, 2.06, 0.14), (0, 2.18, 0.12), 0.11, 0.08, "accent", 24)
    cyl(m, E, "Mast", (0, 2.15, 0.10), (0, 2.56, 0.0), 0.055, 0.048, "metal", 20)
    cyl(m, E, "Swashplate (fixed)", (0, 2.22, 0.08), (0, 2.255, 0.075), 0.17, 0.17, "darkmetal", 32, bevel=0.006)
    for a in (0.6, 2.6, 4.6):
        d = (math.cos(a), 0, math.sin(a))
        tube(m, E, "Control rod", [add((0, 2.0, 0.2), mul(d, 0.16)), add((0, 2.22, 0.08), mul(d, 0.15))], 0.008, "metal", 8)

    # ---- tail boom with joint bands and cheatline (the registration goes on the bare stretch ahead of the stripe)
    Hb = Hull(KESTREL_BOOM)
    o = m.new("Tail boom", E)
    def boom_paint(c, n, u, v):
        x, y, z = c
        if 3.55 < z < 4.3 and abs(y - 1.66) < 0.035: return "stripe"
        if 3.95 < z < 4.15 and abs(x) > 0.05 and y > 1.6: return "accent"            # registration panel
        return "accent" if z > 4.3 else "paint"
    surface(o, 60, 24, lambda u, v: Hb.P(lerp(1.4, 5.12, u), v * 2 * math.pi), boom_paint)
    for z in (1.95, 3.0):
        r = Hb.w(z)
        ring_band(m, E, "Boom joint band", (0, Hb.yc(z), z), (0, 0, 1), r * 0.99, r + 0.008, 0.03, "seam", 32)
    tube(m, E, "Boom strut", [(0.3, 0.98, 1.3), (0.12, 1.55, 2.1)], 0.018, "metal", 10)
    tube(m, E, "Boom strut", [(-0.3, 0.98, 1.3), (-0.12, 1.55, 2.1)], 0.018, "metal", 10)

    # ---- fins, stabilisers, tail guard, gearbox
    fin = lambda c: "stripe" if (c[1] > 2.25 or c[1] < 1.22) else "accent"
    wing(m, E, "Upper fin", (0, 1.64, 4.62), (0, 2.55, 4.98), 0.42, 0.26, 0.12, (0, 0, 1), (1, 0, 0), fin, 10, 22)
    wing(m, E, "Lower fin", (0, 1.62, 4.70), (0, 1.08, 4.84), 0.34, 0.24, 0.12, (0, 0, 1), (1, 0, 0), fin, 6, 22)
    for s in (-1, 1):
        wing(m, E, "Stabiliser", (s * 0.06, 1.64, 4.38), (s * 0.52, 1.66, 4.46), 0.30, 0.20, 0.11, (0, 0, 1), (0, 1, 0), lambda c: "accent", 6, 20)
        box(m, E, "Stabiliser end plate", (s * 0.53, 1.66, 4.55), (0.012, 0.12, 0.2), "accentdk", bevel=0.004)
    tube(m, E, "Tail guard", [(0, 1.12, 4.55), (0, 0.98, 4.8), (0, 1.05, 5.1)], 0.018, "metal", 10)
    ellipsoid(m, E, "Tail gearbox", (-0.04, 1.63, 4.97), (0.10, 0.10, 0.12), "darkmetal", 18, 10)
    cyl(m, E, "Tail output shaft", (-0.04, 1.63, 4.97), (-0.20, 1.63, 4.97), 0.025, 0.025, "metal", 12)
    cyl(m, E, "Tail drive cover", (0, 1.76, 1.6), (0, 1.69, 4.6), 0.03, 0.025, "accentdk", 12)

    # ---- landing gear: skids with toes, crosstubes, saddles, fittings, wear shoes, steps, wheel bosses
    for x in (-0.95, 0.95):
        tube(m, E, "Skid", [(x, 0.34, -2.0), (x, 0.16, -1.86), (x, 0.06, -1.6), (x, 0.05, -0.6), (x, 0.05, 0.9), (x, 0.08, 1.38)], 0.042, "metal", 14)
        box(m, E, "Wear shoe", (x, 0.012, 0.15), (0.05, 0.024, 0.5), "darkmetal", bevel=0.006)
        box(m, E, "Wear shoe", (x, 0.012, -1.0), (0.05, 0.024, 0.4), "darkmetal", bevel=0.006)
        cyl(m, E, "Wheel boss", (x * 1.0 + (0.03 if x > 0 else -0.03), 0.07, 0.28), (x * 1.0 + (0.06 if x > 0 else -0.06), 0.07, 0.28), 0.03, 0.03, "darkmetal", 12)
        for z in (-0.85, 0.62):
            ring_band(m, E, "Skid saddle", (x, 0.06, z), (0, 0, 1), 0.04, 0.052, 0.08, "darkmetal", 20)
    for z in (-0.85, 0.62):
        tube(m, E, "Crosstube", [(-0.95, 0.06, z), (-0.9, 0.42, z), (-0.62, 0.8, z), (0, 0.9, z), (0.62, 0.8, z), (0.9, 0.42, z), (0.95, 0.06, z)], 0.04, "metal", 14)
        for x in (-0.42, 0.42):
            box(m, E, "Crosstube fitting", (x, 0.86, z), (0.1, 0.07, 0.12), "darkmetal", bevel=0.01)
    for x in (-0.72, 0.72):
        box(m, E, "Step", (x, 0.58, -0.85), (0.16, 0.02, 0.1), "frame", bevel=0.004)

    # ---- lights, aerials, probes
    for s, col in ((-1, "navred"), (1, "navgreen")):
        ellipsoid(m, E, "Nav light fairing", (s * 0.66, 1.06, 0.32), (0.035, 0.035, 0.06), "frame", 14, 8)
        ellipsoid(m, E, "Nav light lens", (s * 0.69, 1.06, 0.32), (0.022, 0.03, 0.05), col, 14, 8)
    cyl(m, E, "Landing light housing", (0, 0.86, -1.12), (0, 0.80, -1.22), 0.07, 0.075, "frame", 24)
    cyl(m, E, "Landing light lens", (0, 0.80, -1.22), (0, 0.79, -1.235), 0.062, 0.058, "lens", 24)
    cyl(m, E, "Beacon base", (0, 2.20, 0.62), (0, 2.23, 0.62), 0.05, 0.045, "frame", 16)
    ellipsoid(m, E, "Beacon", (0, 2.26, 0.62), (0.04, 0.035, 0.04), "beacon", 16, 8)
    ellipsoid(m, E, "Tail light", (0, 1.66, 5.15), (0.025, 0.025, 0.03), "lens", 12, 6)
    ellipsoid(m, E, "Strobe", (0, 2.57, 5.0), (0.025, 0.025, 0.035), "lens", 12, 6)
    tube(m, E, "Roof aerial", [(0.18, 2.02, -0.1), (0.18, 2.20, -0.02), (0.18, 2.34, 0.06)], 0.006, "darkmetal", 6)
    box(m, E, "Belly aerial", (0, 0.72, 0.1), (0.012, 0.12, 0.1), "frame", bevel=0.004)
    tube(m, E, "Pitot tube", [(0, 2.04, -0.72), (0, 2.12, -0.74), (0, 2.13, -0.9)], 0.008, "chrome", 8)
    box(m, E, "Placard: no step", (0.57, 1.88, 0.9), (0.004, 0.04, 0.08), "yellow")
    box(m, E, "Placard: no step", (-0.57, 1.88, 0.9), (0.004, 0.04, 0.08), "yellow")
    # GPS dome on the cowling, ELT whip on the boom, static ports and wicks on the boom and tail surfaces
    c = Hc.point(1.15, math.pi / 2, 0.0)
    ellipsoid(m, E, "GPS antenna", (c[0], c[1] + 0.01, c[2]), (0.06, 0.022, 0.085), "radarfair", 16, 6)
    c = Hb.point(2.4, math.pi / 2 - 0.6, 0.0)
    cyl(m, E, "ELT whip base", c, add(c, (0, 0.025, 0)), 0.018, 0.013, "darkmetal", 10)
    tube(m, E, "ELT whip", [c, (c[0], c[1] + 0.2, c[2] + 0.08), (c[0], c[1] + 0.36, c[2] + 0.22)], 0.005, "darkmetal", 6)
    skin_details(m, E, Hb, roof_z=(), steps=[], ports=[(1.62, 1.70)], wicks=[(0.06, 0.52, 4.68, 4.66, 1.65)], whip=None)
    for y, z in ((2.4, 5.2), (2.15, 5.15)):
        tube(m, E, "Static wick", [(0, y, z - 0.01), (0, y + 0.004, z + 0.14)], 0.004, "darkmetal", 6)

    # ---- skin detail: panel joints, rivet rows, cowling fasteners
    def cab_mat(p):
        k = cabin_paint(p)
        return None if k == "glass" else k
    full = (-math.pi / 2, 3 * math.pi / 2)                       # all the way round, starting under the belly
    for zs in (-0.62, 0.62):                                     # lower nose panel joint, and the joint aft of the doors
        for r in runs(ring_path(H, zs, *full, 0.004), lambda p: cabin_paint(p) != "glass"):
            seam(m, E, "Panel joint", r)
        for dz in (-0.013, 0.013):
            rivets(m, E, "Rivets", ring_path(H, zs + dz, *full, 0.038), cab_mat)
    for side in (1, -1):                                         # rivet line along the belly skin
        rivets(m, E, "Rivets", [hull_sample(H, z, H.th_at_y(z, 0.995, side)) for z in [-0.9 + 0.042 * i for i in range(41)]], cab_mat)
    def boom_mat(p): return boom_paint(p, None, 0, 0)
    for zs in (1.95, 3.0):                                       # either side of the joint bands
        for dz in (-0.03, 0.03):
            rivets(m, E, "Rivets", ring_path(Hb, zs + dz, *full, 0.032), boom_mat)
    seam(m, E, "Panel joint", ring_path(Hb, 4.3, *full, 0.004))
    for dz in (-0.014, 0.014):
        rivets(m, E, "Rivets", ring_path(Hb, 4.3 + dz, *full, 0.03), boom_mat)
    for th in (math.pi / 2 - 0.55, math.pi / 2 + 0.55, -math.pi / 2):   # stringers either side of the drive cover, and underneath
        rivets(m, E, "Rivets", length_path(Hb, 1.5, 4.6, th, 0.05), boom_mat)
    fasteners(m, E, "Cowl fastener", ring_path(Hc, 0.62, 0.35, math.pi - 0.35, 0.09))
    seam(m, E, "Panel joint", ring_path(Hc, 1.3, -0.2, math.pi + 0.2, 0.004))
    fasteners(m, E, "Cowl fastener", ring_path(Hc, 1.3, -0.1, math.pi + 0.1, 0.1))

    # ---- engine accessories: oil cooler and lines, carburettor and air box, oil filler, plug leads
    box(m, E, "Oil cooler", (-0.2, 1.0, 0.6), (0.18, 0.12, 0.04), "fins", bevel=0.004)
    for i in range(7):
        box(m, E, "Oil cooler fin", (-0.2, 0.952 + i * 0.016, 0.578), (0.17, 0.004, 0.006), "darkmetal")
    tube(m, E, "Oil line", [(-0.12, 1.02, 0.6), (-0.05, 1.05, 0.75), (0.0, 1.06, 0.85)], 0.006, "rubber", 6)
    tube(m, E, "Oil line", [(-0.26, 1.02, 0.6), (-0.3, 1.06, 0.78), (-0.2, 1.08, 0.9)], 0.006, "rubber", 6)
    box(m, E, "Carburettor", (0, 0.93, 1.08), (0.08, 0.08, 0.08), "metal", bevel=0.008)
    tube(m, E, "Air intake", [(0, 0.92, 1.12), (-0.08, 0.9, 1.25), (-0.2, 0.92, 1.32)], 0.035, "rubber", 12)
    box(m, E, "Air box", (-0.24, 0.93, 1.36), (0.14, 0.1, 0.12), "darkmetal", bevel=0.01)
    cyl(m, E, "Oil filler", (0.22, 1.32, 1.12), (0.22, 1.38, 1.12), 0.018, 0.018, "metal", 12)
    cyl(m, E, "Oil filler cap", (0.22, 1.38, 1.12), (0.22, 1.40, 1.12), 0.024, 0.022, "yellow", 14)
    for x in (-1, 1):
        for z in (0.82, 1.08):
            cyl(m, E, "Plug boot", (x * 0.53, 1.25, z), (x * 0.565, 1.27, z), 0.009, 0.008, "rubber", 8)


# ============================================================================ rotor (part "blades", hub at the origin)
def blade(m, a, R=3.85, rootR=0.22, chord=0.18, thick=0.12, twist_deg=-7.0, root_frac=0.55, tip_mat="bladetip", fittings=True, nu=26, part="blades", name="Main rotor blade"):
    o = m.new(name, part)
    d = (math.sin(a), 0.0, math.cos(a)); lead = norm(cross((0, 1, 0), d)); back = mul(lead, -1); up = (0, 1, 0)
    def P(u, v):
        r = rootR + (R - rootR) * u; root = smooth(0, 0.08, u); tipT = smooth(0.9, 1, u)
        c = chord * lerp(root_frac, 1, root) * (1 - 0.55 * tipT); sweep = tipT * chord * 0.45
        tw = math.radians(twist_deg) * (u - 0.7); droop = -0.004 * R * u * u
        upS = v < 0.5; s = v * 2 if upS else (1 - v) * 2
        x = 0.5 - 0.5 * math.cos(math.pi * s); y = naca(max(1e-4, x), thick * lerp(1.6, 1, root)) * (1 if upS else -1)
        cx, cy = (x - 0.25) * c + sweep, y * c
        ct, st = math.cos(tw), math.sin(tw)
        return add(add(mul(d, r), mul(back, cx * ct - cy * st)), mul(up, cx * st + cy * ct + droop))
    def paint(c, n, u, v):
        if u > 0.955 or 0.93 < u < 0.945: return tip_mat
        if u < 0.06: return "darkmetal"
        if v < 0.05 or v > 0.95: return "bladele"
        return "blade"
    surface(o, nu, 20, P, paint, lambda i: (i / nu) if i < nu // 2 else 1 - (1 - i / nu) ** 2 * 2)
    cap_fan(o, [P(1, j / 20) for j in range(20)], d, tip_mat)
    # trim tab near the tip, on the trailing edge
    if not fittings: return
    te = P(0.82, 0.5)
    box(m, "blades", "Trim tab", add(te, mul(back, 0.012)), (0.025, 0.003, 0.16), "blade", axes=(back, up, d))
    # grip, bolts and pitch horn
    cyl(m, "blades", "Blade grip", mul(d, 0.12), mul(d, 0.34), 0.045, 0.038, "metal", 18, bevel=0.004)
    for r in (0.22, 0.3):
        cyl(m, "blades", "Grip bolt", add(mul(d, r), (0, 0.05, 0)), add(mul(d, r), (0, -0.05, 0)), 0.009, 0.009, "chrome", 10)
    horn = add(mul(d, 0.24), mul(lead, 0.07))
    box(m, "blades", "Pitch horn", add(mul(d, 0.24), mul(lead, 0.045)), (0.07, 0.02, 0.03), "darkmetal", axes=(lead, up, d), bevel=0.004)
    lower = add(mul(d, 0.12), add(mul(lead, 0.08), (0, -0.31, 0)))
    tube(m, "blades", "Pitch link", [add(horn, (0, -0.01, 0)), lower], 0.009, "chrome", 8)
    ellipsoid(m, "blades", "Rod end", lower, (0.016, 0.016, 0.016), "darkmetal", 10, 6)


def build_tail_rotor(m, tr):
    """The tail rotor for the .glb (part "tail_rotor", hub at the origin, spinning about x). The game draws its
    own from the same meta["tailRotor"] settings, so this part is kept out of the .rcm."""
    R, n = tr["r"], tr["blades"]
    tip = "bladetip" if tr.get("tip") == "stripe" else "hoistyellow" if isinstance(tr.get("tip"), list) and tr["tip"][1] > 0.5 else "darkmetal"
    shrouded = n > 6                                       # a fan in a shroud: many short, stubby blades
    start = len(m.objs)
    for i in range(n):
        blade(m, i * 2 * math.pi / n, R=R * 0.97, rootR=R * (0.32 if shrouded else 0.16), chord=R * (0.26 if shrouded else 0.2), thick=0.1,
              twist_deg=-4.0, root_frac=0.8, tip_mat=tip, fittings=False, nu=12, part="tail_rotor", name="Tail rotor blade")
    cyl(m, "tail_rotor", "Tail rotor hub", (0, -0.05, 0), (0, 0.06, 0), R * (0.3 if shrouded else 0.12), R * (0.26 if shrouded else 0.1), "darkmetal", 20, bevel=0.004)
    cyl(m, "tail_rotor", "Tail rotor hub cap", (0, 0.06, 0), (0, 0.09, 0), R * (0.2 if shrouded else 0.08), R * 0.04, "metal", 16)
    # built spinning about y; turn it so it spins about x (the game's tail rotor axis): (x, y, z) -> (y, -x, z)
    for o in m.objs[start:]:
        o.v = [(y, -x, z) for (x, y, z) in o.v]


def build_rotor(m):
    for i in range(2): blade(m, i * math.pi)
    box(m, "blades", "Yoke", (0, 0.02, 0), (0.12, 0.07, 0.6), "darkmetal", bevel=0.012)
    cyl(m, "blades", "Hub", (0, -0.04, 0), (0, 0.10, 0), 0.07, 0.055, "metal", 20, bevel=0.005)
    cyl(m, "blades", "Teeter bolt", (-0.09, 0.03, 0), (0.09, 0.03, 0), 0.016, 0.016, "chrome", 12)
    cyl(m, "blades", "Hub cap", (0, 0.10, 0), (0, 0.13, 0), 0.04, 0.02, "metal", 16)
    cyl(m, "blades", "Swashplate (rotating)", (0, -0.335, 0), (0, -0.305, 0), 0.155, 0.155, "metal", 32, bevel=0.005)
    tube(m, "blades", "Scissor link", [(0, -0.30, 0.06), (0.0, -0.18, 0.12), (0, -0.07, 0.05)], 0.008, "darkmetal", 8)


# ============================================================================ cockpit (part "cockpit")
def build_cockpit(m, H):
    C = "cockpit"
    # lining of the lower cabin and roof (the bubble above the waist stays open: the game draws the world there)
    o = m.new("Cabin lining", C)
    def keep(p):
        x, y, z = p
        if y < waist(z) - 0.02 and not (z < -1.0 and y > 1.0 and abs(x) > 0.12): return "floor" if y < 0.95 and z > -0.9 else "trim"   # one colour where the chin glass shows it
        if y > 1.98 and z > -0.3: return "trim"
        return None
    surface(o, 72, 56, lambda u, v: H.P(lerp(-1.42, 0.34, u), v * 2 * math.pi, 0.985), lambda c, n, u, v: keep(c), inward=True)
    box(m, C, "Floor", (0, 0.86, -0.55), (1.15, 0.02, 1.6), "floor")
    # rear bulkhead
    o = m.new("Rear bulkhead", C)
    ring = [H.P(0.32, j / 64 * 2 * math.pi, 0.985) for j in range(64)]
    cap_fan(o, ring, (0, 0, -1), "trim")
    # inside frames (same lines as outside)
    fr = 0.022
    tube(m, C, "Frame centre bow", [H.P(z, math.pi / 2, 0.975) for z in [-1.3 + 0.1 * i for i in range(13)]], fr, "frame", 8, steps=2)
    for zf in (DOOR_FWD, 0.30):
        for side in (1, -1):
            t0 = H.th_at_y(zf, waist(zf), side); t1 = math.pi / 2 - 0.25 if side > 0 else math.pi / 2 + 0.25
            tube(m, C, "Frame door post", [H.P(zf, t0 + (t1 - t0) * i / 8, 0.975) for i in range(9)], 0.02, "frame", 8, steps=2)
    for side in (1, -1):
        tube(m, C, "Frame sill", [H.P(z, H.th_at_y(z, waist(z), side), 0.97) for z in [-1.25 + 0.15 * i for i in range(11)]], 0.018, "frame", 8, steps=2)
    tube(m, C, "Frame roof bow", [H.P(-0.12, 0.35 + (math.pi - 0.7) * i / 10, 0.97) for i in range(11)], 0.025, "frame", 8, steps=2)

    # ---- console (the gauge face is drawn by the game on the panel quad)
    pz = -1.15
    box(m, C, "Instrument console", (0, 1.24, pz - 0.06), (0.68, 0.32, 0.12), "panel", bevel=0.012)
    box(m, C, "Glareshield", (0, 1.41, pz - 0.02), (0.72, 0.03, 0.18), "glareshield", bevel=0.012)
    tube(m, C, "Glareshield lip", [(-0.36, 1.425, pz + 0.07), (0, 1.43, pz + 0.075), (0.36, 1.425, pz + 0.07)], 0.012, "rubber", 8)
    box(m, C, "Radio stack", (0, 0.97, pz + 0.08), (0.26, 0.26, 0.24), "panel", bevel=0.01)
    box(m, C, "Pedestal", (0, 0.9, pz + 0.3), (0.22, 0.08, 0.34), "panel", bevel=0.01)
    F = Face(m, C, (0, 0.96, pz + 0.205), (1, 0, 0), (0, 1, 0), (0, 0, 1))
    F.screen(-0.04, 0.09, 0.12, 0.026, "screen_g"); F.knob(0.075, 0.09, 0.012)
    F.screen(-0.04, 0.04, 0.12, 0.026, "screen_g"); F.knob(0.075, 0.04, 0.012)
    F.screen(-0.04, -0.01, 0.12, 0.022, "screen_a"); F.knob(0.075, -0.01, 0.01)
    for i in range(4): F.toggle(-0.09 + i * 0.05, -0.06)
    F.lamp(-0.09, -0.1, "lamp_r"); F.lamp(-0.05, -0.1, "lamp_a"); F.lamp(-0.01, -0.1, "lamp_a")
    for sx in (-1, 1):
        f = Face(m, C, (sx * 0.27, 1.24, pz + 0.002), (1, 0, 0), (0, 1, 0), (0, 0, 1))
        for i in range(3):
            for j in range(2): f.knob(sx * (i * 0.012) - sx * 0.01, -0.09 + j * 0.02, 0.005, "grip")
    # circuit breaker panel on the pedestal top
    f = Face(m, C, (0, 0.942, pz + 0.3), (1, 0, 0), (0, 0, -1), (0, 1, 0))
    for i in range(5):
        for j in range(3): f.knob(-0.07 + i * 0.035, -0.1 + j * 0.05, 0.006, "grip")
    # round instruments on the console face (the game may still draw its live panel over them)
    P = Face(m, C, (0, 1.24, pz + 0.001), (1, 0, 0), (0, 1, 0), (0, 0, 1))
    r = 0.046
    gauge(m, C, P, -0.165, 0.072, r, "airspeed", marks=21, major=5, arcs=((0.15, 0.75, "arc_g"), (0.75, 0.88, "arc_y"), (0.88, 0.91, "arc_r")))
    gauge(m, C, P, 0.0, 0.072, r, "attitude", kind="attitude")
    gauge(m, C, P, 0.165, 0.072, r, "altimeter", sweep=(90.0, -270.0), marks=50, major=5, needle_deg=90.0)
    gauge(m, C, P, -0.165, -0.066, r, "rotor rpm", arcs=((0.62, 0.8, "arc_g"), (0.8, 0.84, "arc_r")), needle_deg=-10.0)
    gauge(m, C, P, 0.0, -0.066, r, "heading", sweep=(90.0, -270.0), marks=36, major=3, needle_deg=90.0)
    gauge(m, C, P, 0.165, -0.066, r, "vertical speed", sweep=(180.0, -180.0), marks=21, major=5, needle_deg=180.0)
    ellipsoid(m, C, "Compass", (0, 1.47, pz - 0.02), (0.045, 0.035, 0.04), "panel", 14, 8)
    ellipsoid(m, C, "Compass card", (0, 1.47, pz - 0.055), (0.03, 0.02, 0.006), "compass", 12, 6, ao=False)
    cyl(m, C, "Compass stem", (0, 1.43, pz - 0.02), (0, 1.47, pz - 0.02), 0.008, 0.008, "panel", 8)

    # ---- seats with harnesses; extinguisher, map pocket, door pulls, headsets
    for sx in (-1, 1): seat(m, C, sx * 0.3, 0.86, -0.56, -0.18, 0.4, 1.8)
    cyl(m, C, "Fire extinguisher", (-0.55, 0.95, -0.2), (-0.55, 1.25, -0.2), 0.045, 0.045, "red", 18, bevel=0.006)
    cyl(m, C, "Extinguisher head", (-0.55, 1.25, -0.2), (-0.55, 1.29, -0.2), 0.02, 0.018, "metal", 12)
    box(m, C, "Map pocket", (0.55, 1.05, -0.5), (0.02, 0.18, 0.25), "floor", bevel=0.006)
    for sx in (-1, 1):
        box(m, C, "Door pull", (sx * 0.6, 1.36, -0.25), (0.03, 0.03, 0.12), "metal", bevel=0.006)
        # headset hanging on a hook on the rear bulkhead
        hx = sx * 0.3
        cyl(m, C, "Headset hook", (hx, 1.62, 0.31), (hx, 1.62, 0.26), 0.008, 0.008, "metal", 8)
        tube(m, C, "Headset band", [(hx - 0.08, 1.50, 0.27), (hx - 0.06, 1.61, 0.265), (hx, 1.64, 0.26), (hx + 0.06, 1.61, 0.265), (hx + 0.08, 1.50, 0.27)], 0.012, "headset", 8)
        for s2 in (-1, 1): cyl(m, C, "Ear cup", (hx + s2 * 0.08, 1.47, 0.28), (hx + s2 * 0.08, 1.47, 0.24), 0.05, 0.05, "headset", 20, bevel=0.01)
    # outside parts visible from the seat: skids and the front crosstube
    for x in (-0.95, 0.95):
        tube(m, C, "Skid (view)", [(x, 0.34, -2.0), (x, 0.16, -1.86), (x, 0.06, -1.6), (x, 0.05, -0.6), (x, 0.05, 0.9)], 0.042, "metal", 12)
    tube(m, C, "Crosstube (view)", [(-0.95, 0.06, -0.85), (-0.9, 0.42, -0.85), (-0.62, 0.8, -0.85), (0, 0.88, -0.85), (0.62, 0.8, -0.85), (0.9, 0.42, -0.85), (0.95, 0.06, -0.85)], 0.04, "metal", 12)



def skin_details(m, part, H, roof_z, steps, ports, wicks, whip):
    """Small things every real airframe carries, laid on the hull H: GPS domes on the roof, a VHF whip, static
    ports by the nose, kick-in steps up the side and static wicks on the tail surfaces. Positions in game axes."""
    def safe(f):
        try: f()
        except Exception: pass
    for i, z in enumerate(roof_z):          # GPS / satcom domes
        def dome(z=z, i=i):
            c = H.point(z, math.pi / 2, 0.0)
            ellipsoid(m, part, "GPS antenna", (c[0] + (0.14 if i % 2 else -0.14), c[1] + 0.012, c[2]), (0.07, 0.025, 0.1), "radarfair", 16, 6)
        safe(dome)
    def vhf():
        c = H.point(whip, math.pi / 2, 0.0)
        tube(m, part, "VHF whip", [(c[0] - 0.22, c[1] - 0.01, c[2]), (c[0] - 0.22, c[1] + 0.3, c[2] + 0.18), (c[0] - 0.22, c[1] + 0.52, c[2] + 0.42)], 0.006, "darkmetal", 6)
        cyl(m, part, "VHF whip base", (c[0] - 0.22, c[1] - 0.01, c[2]), (c[0] - 0.22, c[1] + 0.03, c[2]), 0.022, 0.016, "darkmetal", 10)
    if whip is not None: safe(vhf)
    for sd in (1, -1):
        for (z, y) in ports:                 # static ports: little flush discs with a ring
            def port(z=z, y=y, sd=sd):
                a = H.on_side(z, y, sd, 0.0); b = H.on_side(z, y, sd, 0.004)
                cyl(m, part, "Static port", a, b, 0.022, 0.022, "metal", 14)
                cyl(m, part, "Static port hole", b, H.on_side(z, y, sd, 0.0045), 0.008, 0.008, "intake", 8)
            safe(port)
        for (z, y) in steps:                 # kick-in steps: a dark recess with a lip
            def step(z=z, y=y, sd=sd):
                box(m, part, "Kick step", H.on_side(z, y, sd, 0.002), (0.02, 0.055, 0.16), "intake", bevel=0.004)
                box(m, part, "Kick step lip", H.on_side(z, y + 0.03, sd, 0.006), (0.02, 0.008, 0.17), "metal", bevel=0.002)
            safe(step)
    for (x0, x1, z0, z1, y) in wicks:        # static wicks on the trailing edges
        for k in range(3):
            u = 0.3 + 0.3 * k
            x = lerp(x0, x1, u); z = lerp(z0, z1, u)
            for s in (1, -1):
                tube(m, part, "Static wick", [(s * x, y, z - 0.01), (s * x, y + 0.004, z + 0.14)], 0.004, "darkmetal", 6)

class Face:
    """Things laid on a plane: origin o, axes ex (right), ey (up), ez (out of the face)."""
    def __init__(self, m, part, o, ex, ey, ez): self.m, self.part, self.o, self.ex, self.ey, self.ez = m, part, o, ex, ey, ez
    def at(self, x, y, z=0): return add(self.o, add(add(mul(self.ex, x), mul(self.ey, y)), mul(self.ez, z)))
    def obox(self, c, sz, mat, name="Panel part", bevel=0.0):
        box(self.m, self.part, name, self.at(*c), sz, mat, axes=(self.ex, self.ey, self.ez), bevel=bevel)
    def toggle(self, x, y, up=True, s=1.0):
        self.obox((x, y, 0.004 * s), (0.018 * s, 0.026 * s, 0.008 * s), "seatframe", "Switch base")
        cyl(self.m, self.part, "Toggle", self.at(x, y, 0.006 * s), self.at(x, y + (0.012 if up else -0.012) * s, 0.026 * s), 0.0035 * s, 0.003 * s, "chrome", 8)
    def knob(self, x, y, r=0.012, mat="panel"):
        cyl(self.m, self.part, "Knob", self.at(x, y, 0), self.at(x, y, r * 1.1), r, r * 0.9, mat, 10)
        cyl(self.m, self.part, "Knob cap", self.at(x, y, r * 1.1), self.at(x, y, r * 1.2), r * 0.35, r * 0.3, "chrome", 8)
    def lamp(self, x, y, mat, w=0.02, h=0.012): self.obox((x, y, 0.002), (w, h, 0.006), mat, "Annunciator")
    def plate(self, cx, cy, w, h, mat, depth=0.01): self.obox((cx, cy, -depth / 2), (w, h, depth), mat, "Plate")
    def button(self, x, y, label, w=0.03, h=0.02):
        self.obox((x, y, 0.005), (w, h, 0.012), "seatframe", "Button", bevel=0.002)
        self.obox((x, y + h * 0.25, 0.0115), (w * 0.7, h * 0.18, 0.001), label, "Button legend")
    def screen(self, cx, cy, w, h, glow):
        self.obox((cx, cy, -0.0125), (w + 0.03, h + 0.03, 0.025), "panel", "Screen bezel", bevel=0.003)
        self.obox((cx, cy, 0.0005), (w, h, 0.002), glow, "Screen")


def seat(m, C, x, y_floor, z_front, z_back, width, back_top, seat_mat="seat", frame_mat="seatframe", harness_mat="harness"):
    cy = y_floor + 0.32
    box(m, C, "Seat pan", (x, y_floor + 0.14, (z_front + z_back) / 2), (width * 0.8, 0.28, z_back - z_front), frame_mat, bevel=0.01)
    box(m, C, "Seat cushion", (x, cy, (z_front + z_back) / 2 - 0.01), (width, 0.1, z_back - z_front), seat_mat, bevel=0.035, segs=4)
    back_z = z_back + 0.04; lean = 0.12
    ey = norm((0, back_top - cy, lean)); ez = norm(cross((1, 0, 0), ey)); ez_out = mul(ez, -1)
    o = (x, cy + 0.05, back_z)
    def at(px, py, pz): return add(o, add(add(mul((1, 0, 0), px), mul(ey, py)), mul(ez_out, pz)))
    box(m, C, "Seat back", at(0, (back_top - cy) / 2, 0), (width, back_top - cy - 0.05, 0.1), seat_mat, axes=((1, 0, 0), ey, ez_out), bevel=0.035, segs=4)
    box(m, C, "Headrest", at(0, back_top - cy + 0.06, 0.01), (width * 0.55, 0.17, 0.09), seat_mat, axes=((1, 0, 0), ey, ez_out), bevel=0.03, segs=4)
    for sx in (-1, 1):
        box(m, C, "Shoulder strap", at(sx * width * 0.2, (back_top - cy) * 0.55, -0.058), (0.045, back_top - cy - 0.15, 0.006), harness_mat, axes=((1, 0, 0), ey, ez_out))
    box(m, C, "Lap belt", (x, cy + 0.06, z_front + 0.12), (width * 0.7, 0.008, 0.045), harness_mat)
    box(m, C, "Buckle", (x, cy + 0.066, z_front + 0.12), (0.06, 0.014, 0.05), "chrome", bevel=0.004)


# ============================================================================ controls (one part each)
def build_controls(m):
    ctl = []
    for sx in (-1, 1):
        tag = "l" if sx < 0 else "r"
        # cyclic
        p = "ctl_cyclic_" + tag; base = (sx * 0.3, 0.88, -0.74)
        cyl(m, p, "Cyclic boot", base, (sx * 0.3, 0.92, -0.74), 0.06, 0.04, "rubber", 16, bevel=0.008)
        tube(m, p, "Cyclic stick", [base, (sx * 0.3, 1.1, -0.76), (sx * 0.3, 1.22, -0.79)], 0.013, "darkmetal", 10)
        cyl(m, p, "Cyclic grip", (sx * 0.3, 1.2, -0.79), (sx * 0.3, 1.32, -0.81), 0.022, 0.024, "grip", 14, bevel=0.006)
        cyl(m, p, "Trigger", (sx * 0.3, 1.32, -0.81), (sx * 0.3, 1.34, -0.82), 0.012, 0.01, "red", 10)
        box(m, p, "Trim hat", (sx * 0.3, 1.315, -0.832), (0.012, 0.012, 0.01), "panel")
        ctl.append({"part": p, "kind": "cyclic", "pivot": base})
        # collective with twist-grip throttle
        p = "ctl_collective_" + tag; piv = (sx * 0.05 + (-0.5 if sx < 0 else 0), 0.98, -0.22); tip = (piv[0], 1.06, -0.66)
        tube(m, p, "Collective lever", [piv, (piv[0], 1.02, -0.44), tip], 0.016, "darkmetal", 10)
        cyl(m, p, "Throttle grip", tip, add(tip, (0, 0.02, -0.12)), 0.024, 0.024, "grip", 16, bevel=0.006)
        box(m, p, "Collective switch box", (piv[0], 1.08, -0.66), (0.03, 0.03, 0.04), "panel", bevel=0.004)
        cyl(m, p, "Friction knob", (piv[0], 0.99, -0.26), (piv[0] + 0.03, 0.99, -0.26), 0.012, 0.012, "chrome", 10)
        ctl.append({"part": p, "kind": "collective", "pivot": piv})
        for kind, dx in (("pedalL", -0.1), ("pedalR", 0.1)):
            p = "ctl_%s_%s" % (kind, tag); px = sx * 0.3 + dx
            tube(m, p, "Pedal arm", [(px, 0.88, -0.98), (px, 0.97, -1.02), (px, 1.04, -0.99)], 0.012, "metal", 8)
            box(m, p, "Pedal", (px, 1.0, -1.0), (0.07, 0.12, 0.015), "seatframe", bevel=0.004)
            for i in range(3): box(m, p, "Pedal tread", (px, 0.96 + i * 0.04, -1.009), (0.06, 0.008, 0.004), "rubber")
            ctl.append({"part": p, "kind": kind, "pivot": (px, 0.9, -1.0)})
    return ctl


def build_kestrel():
    m = Model(); H = Hull(CABIN)
    build_exterior(m, H); build_rotor(m); build_cockpit(m, H); ctl = build_controls(m)
    # the skids seen from the seat belong to the cockpit view only (the body has the real ones)
    for o in m.objs:
        if o.part == "cockpit" and "(view)" in o.name: o.part = "cockpit_only"
    # doors: the bubble doors either side, hinged on their front edge
    doors = []
    for side in (1, -1):
        s = "r" if side > 0 else "l"
        carve_door(m, side, DOOR_FWD + 0.002, DOOR_AFT - 0.002, lambda z: 0.98, lambda z: 1.975, "door_" + s, min_x=0.3)
        piv = H.P(DOOR_FWD, H.th_at_y(DOOR_FWD, 1.4, side), 1.0)
        doors.append({"part": "door_" + s, "group": "crew_" + s, "kind": "hinge", "pivot": list(piv), "axis": [0, 1, 0], "open": 1.25 * side})
    pw, ph, cy0, cz, tilt = 0.31, 0.124, 1.25, -1.15 + 0.04, 0.035
    meta = {
        "format": "RCM1", "model": "Kestrel K2", "refRotorDiameter": 7.7, "generator": VERSION,
        "points": {
            "mastY": 2.6, "tailPos": [-0.24, 1.63, 4.97], "eye": [0.3, 1.68, -0.42], "fuel": [0.5, 1.85, 0.8],
            "lights": {"navL": [-0.7, 1.06, 0.32], "navR": [0.7, 1.06, 0.32], "tail": [0, 1.66, 5.16], "beacon": [0, 2.31, 0.62],
                        "strobes": [[0, 2.6, 5.04]], "landing": {"pos": [0, 0.8, -1.24], "dir": [0, -0.32, -1]}},
            "panelQuad": [[-pw, cy0 - ph, cz + tilt], [pw, cy0 - ph, cz + tilt], [pw, cy0 + ph, cz - tilt], [-pw, cy0 + ph, cz - tilt]],
        },
        "controls": [{"part": c["part"], "kind": c["kind"], "pivot": list(c["pivot"])} for c in ctl],
        "tailRotor": {"r": 0.55, "blades": 2, "tip": "stripe"},
        "interior": "cockpit", "cockpitOnly": "cockpit_only", "doors": doors,
        # raised lettering wrapped onto both sides of the tail boom (added in Blender, which has the font)
        "registration": {"text": "G-KRSA", "z": 2.47, "y": 1.66, "height": 0.085, "mat": "accent", "boom": KESTREL_BOOM},
    }
    meta["points"].update({"floorY": 0.86, "seats": {"pilot": [0.3, 1.23, -0.37, "crew_r"], "crew": [-0.3, 1.23, -0.37, "crew_l"]},
                           "doorways": {"crew_r": [[0.3, -0.05], [1.25, -0.05]], "crew_l": [[-0.3, -0.05], [-1.25, -0.05]]},
                           "exhausts": [[0.33, 0.92, 1.66, 0.05, -0.25, 1]]})
    build_tail_rotor(m, meta["tailRotor"])
    return m, meta



# ============================================================================ medium twins: Northstar 175 and Guardian S9
TWIN_FUS = [
    (-4.88, 1.22, 0.04, 0.04, 0.04, 2.0), (-4.70, 1.24, 0.42, 0.40, 0.38, 2.1), (-4.30, 1.36, 0.78, 0.74, 0.60, 2.4),
    (-3.70, 1.52, 0.98, 1.00, 0.70, 3.0), (-2.90, 1.60, 1.07, 1.07, 0.75, 3.6), (-1.60, 1.62, 1.10, 1.08, 0.77, 4.2),
    (1.30, 1.62, 1.10, 1.08, 0.77, 4.2), (2.30, 1.74, 1.00, 0.98, 0.60, 3.4), (3.20, 2.02, 0.68, 0.68, 0.36, 2.6),
    (4.30, 2.24, 0.40, 0.42, 0.28, 2.2), (6.60, 2.42, 0.26, 0.30, 0.22, 2.0), (7.75, 2.48, 0.20, 0.24, 0.18, 2.0),
    (7.95, 2.48, 0.04, 0.04, 0.04, 2.0),
]
WS_Z0, WS_Z1 = -4.5, -3.15                      # windscreen
CD_Z0, CD_Z1 = -3.12, -2.33                     # cockpit door windows
CHIN_Z0, CHIN_Z1 = -4.6, -3.75                  # chin windows (SAR)
TWIN_CREW_DOOR = (-3.2, -2.22)                  # hinged cockpit doors
def twin_cabin_door(sar): return (-1.25, 0.15) if sar else (-2.05, -1.35)
def ws_low(z): return 1.62 - (z + 3.2) * 0.12
def ws_high(z): return 2.48 - (z + 3.2) * 0.42
def glaze_y1(z): return ws_low(z) if z < WS_Z1 else ws_low(WS_Z1)
def glaze_y2(z): return ws_high(z) if z < WS_Z1 else ws_high(WS_Z1)
def in_ws(z): return WS_Z0 < z < WS_Z1
def in_cd(z): return CD_Z0 < z < CD_Z1


def feature_rows(z0, z1, n, feats, ends_dense=True):
    if ends_dense: base = [lerp(z0, z1, 0.55 * (0.5 - 0.5 * math.cos(math.pi * i / n)) + 0.45 * i / n) for i in range(n + 1)]
    else: base = [lerp(z0, z1, i / n) for i in range(n + 1)]
    gap = (z1 - z0) / n * 0.3
    keep = [z for i, z in enumerate(base) if i in (0, n) or min([abs(z - f) for f in feats] + [9]) > gap]
    return sorted(set(keep + [f for f in feats if z0 < f < z1]))


def feature_loft(m, part, name, H, rows, y_funcs, segs, paint, pt=None, inward=False, **opts):
    """Loft whose rings have vertices exactly on given height lines on both sides (y_funcs, low to high), and
    rows exactly at given stations, so paint edges and glazing follow smooth lines. paint(band, z, side) -> key or None.
    segs: vertices per side in each band (len(y_funcs) + 1 bands)."""
    o = m.new(name, part, **opts)
    pt = pt or (lambda z, a: H.P(z, a))
    rings, bands = [], None
    for z in rows:
        seq = [-math.pi / 2] + [H.th_at_y(z, f(z), 1) for f in y_funcs] + [math.pi / 2]
        for i in range(1, len(seq)): seq[i] = min(math.pi / 2, max(seq[i], seq[i - 1]))
        right, rb = [], []
        for k, cnt in enumerate(segs):
            for j in range(cnt): right.append(lerp(seq[k], seq[k + 1], j / cnt)); rb.append(k)
        ang = right + [math.pi / 2] + [math.pi - a for a in reversed(right[1:])]
        rings.append([pt(z, a) for a in ang])
        if bands is None: bands = [(b, 1) for b in rb] + [(b, -1) for b in reversed(rb)]
    nv = len(rings[0]); ids = {}
    def vid(i, j):
        if (i, j) not in ids: ids[(i, j)] = o.vert(rings[i][j])
        return ids[(i, j)]
    for i in range(len(rows) - 1):
        zc = 0.5 * (rows[i] + rows[i + 1]); centre = (H.xc(zc), H.yc(zc), zc)
        for j in range(nv):
            j2 = (j + 1) % nv; band, side = bands[j]
            mat = paint(band, zc, side)
            if mat is None: continue
            ps = [rings[i][j], rings[i + 1][j], rings[i + 1][j2], rings[i][j2]]
            fn = add(cross(sub(ps[1], ps[0]), sub(ps[2], ps[0])), cross(sub(ps[2], ps[0]), sub(ps[3], ps[0])))
            if length(fn) < 1e-12: continue
            c = mul(add(add(ps[0], ps[1]), add(ps[2], ps[3])), 0.25); out = sub(c, centre)
            oriented_quad(o, (vid(i, j), vid(i + 1, j), vid(i + 1, j2), vid(i, j2)), mul(out, -1) if inward else out, mat)
    return o


def rrect_outline(cz, cy, hz, hy, r, n=36):
    """Rounded rectangle in the (z, y) plane, counter-clockwise."""
    r = min(r, hz, hy); pts = []
    corners = [(cz + hz - r, cy + hy - r, 0.0), (cz - hz + r, cy + hy - r, 0.5 * math.pi), (cz - hz + r, cy - hy + r, math.pi), (cz + hz - r, cy - hy + r, 1.5 * math.pi)]
    per = n // 4
    for (ccz, ccy, a0) in corners:
        for i in range(per + 1): a = a0 + 0.5 * math.pi * i / per; pts.append((ccz + r * math.cos(a), ccy + r * math.sin(a)))
    return pts


def decal(m, part, name, H, side, outline, mat, off, rings=3, gasket=None, **opts):
    """A flat panel laid on the hull side (a window, an intake) following the outline (z, y points), raised
    `off` metres; gasket=(width, material, off) adds a frame ring around it."""
    o = m.new(name, part, **opts)
    cz = sum(p[0] for p in outline) / len(outline); cy = sum(p[1] for p in outline) / len(outline)
    def on(z, y, d): return H.on_side(z, y, side, d)
    c_id = o.vert(on(cz, cy, off)); prev = None
    for k in range(1, rings + 1):
        t = k / rings
        ring = [o.vert(on(lerp(cz, z, t), lerp(cy, y, t), off)) for (z, y) in outline]
        n = len(ring)
        for j in range(n):
            a, b = ring[j], ring[(j + 1) % n]
            P = [o.v[c_id], o.v[a], o.v[b]]
            out = sub(P[0], (H.xc(cz), H.yc(cz), cz))
            if prev is None:
                o.face((c_id, a, b) if dot(cross(sub(P[1], P[0]), sub(P[2], P[0])), out) >= 0 else (c_id, b, a), mat)
            else:
                oriented_quad(o, (prev[j], a, b, prev[(j + 1) % n]), out, mat)
        prev = ring
    if gasket:
        w, gmat, goff = gasket
        g = m.new(name + " gasket", part, **opts)
        inner = [g.vert(on(z, y, goff)) for (z, y) in outline]
        outer = []
        for (z, y) in outline:
            dz, dy = z - cz, y - cy; l = math.hypot(dz, dy) or 1
            outer.append(g.vert(on(z + dz / l * w, y + dy / l * w, goff)))
        n = len(inner)
        for j in range(n):
            out = sub(g.v[inner[j]], (H.xc(cz), H.yc(cz), cz))
            oriented_quad(g, (inner[j], outer[j], outer[(j + 1) % n], inner[(j + 1) % n]), out, gmat)
    return o


def ribbon(m, part, name, H, side, z0, z1, ymid, half, mat, off, n=120):
    """A painted band along the hull side between ymid(z) - half(z) and ymid(z) + half(z)."""
    o = m.new(name, part)
    rows = []
    for i in range(n + 1):
        z = lerp(z0, z1, i / n)
        rows.append([o.vert(H.on_side(z, ymid(z) + f * half(z), side, off)) for f in (-1, 0, 1)])
    for i in range(n):
        z = lerp(z0, z1, (i + 0.5) / n)
        for j in range(2):
            ids = (rows[i][j], rows[i + 1][j], rows[i + 1][j + 1], rows[i][j + 1])
            out = sub(o.v[ids[0]], (H.xc(z), H.yc(z), z))
            oriented_quad(o, ids, out, mat)
    return o


def build_twin(sar):
    m = Model(); H = Hull(TWIN_FUS); E = "body"
    body_c = "paint"; belly = "belly_sar" if sar else "belly_ns"
    def body_at(z):
        if sar and z < -4.45: return "radome"
        if sar and 5.2 < z < 5.9: return "stripe"
        if z > 6.3: return "accent"
        return body_c
    def chin_top(z): return 1.35 if (sar and CHIN_Z0 < z < CHIN_Z1) else 1.07
    # ---- fuselage, nose to tail, with glazing on exact lines
    rows = feature_rows(H.z0, H.z1, 110, [WS_Z0, WS_Z1, CD_Z0, CD_Z1, -4.45, CHIN_Z0, CHIN_Z1, 2.4, 5.2, 5.9, 6.3] + list(TWIN_CREW_DOOR) + list(twin_cabin_door(sar)))
    def hull_paint(band, z, side):
        if band == 0: return "radome" if (sar and z < -4.45) else (belly if z < 2.4 else body_at(z))
        if band == 1: return "glass" if (sar and CHIN_Z0 < z < CHIN_Z1) else body_at(z)
        if band == 3 and (in_ws(z) or in_cd(z)): return "glass"
        return body_at(z)
    feature_loft(m, E, "Fuselage", H, rows, [lambda z: 1.07, chin_top, glaze_y1, glaze_y2], (8, 3, 7, 8, 7), hull_paint)

    # ---- windscreen and door frames
    fo = 0.006
    top = lambda z: H.yc(z) + H.ht(z)
    ctr = [H.point(z, math.pi / 2, fo) for z in [WS_Z0 + 0.05 + i * 0.1 for i in range(14)] if ws_high(z) >= top(z) - 0.01 and z < WS_Z1 - 0.02]
    if len(ctr) > 1: tube(m, E, "Windscreen centre post", ctr, 0.03, "frame", 8, steps=2)
    for sd in (1, -1):
        tube(m, E, "Windscreen sill", [H.on_side(z, ws_low(z), sd, fo) for z in [WS_Z0 + i * (WS_Z1 - WS_Z0) / 14 for i in range(15)]], 0.03, "frame", 8, steps=2)
        hdr = [H.on_side(z, ws_high(z), sd, fo) for z in [WS_Z0 + i * (WS_Z1 - WS_Z0) / 14 for i in range(15)] if ws_high(z) < top(z) - 0.01]
        if len(hdr) > 1: tube(m, E, "Windscreen header", hdr, 0.03, "frame", 8, steps=2)
        tube(m, E, "Windscreen aft post", [H.on_side(WS_Z1, lerp(ws_low(WS_Z1), ws_high(WS_Z1), t / 8), sd, fo) for t in range(9)], 0.035, "frame", 8, steps=2)
        y_top = min(ws_high(WS_Z0), top(WS_Z0) - 0.005)
        tube(m, E, "Windscreen front edge", [H.on_side(WS_Z0, lerp(ws_low(WS_Z0), y_top, t / 8), sd, fo) for t in range(9)], 0.025, "frame", 8, steps=2)
        # cockpit door window frame and door shut lines
        y1, y2 = glaze_y1(CD_Z0), glaze_y2(CD_Z0)
        for z in (CD_Z0, CD_Z1):
            tube(m, E, "Door window frame", [H.on_side(z, lerp(y1, y2, t / 6), sd, fo) for t in range(7)], 0.025, "frame", 8, steps=2)
        for y in (y1, y2):
            tube(m, E, "Door window frame", [H.on_side(lerp(CD_Z0, CD_Z1, t / 8), y, sd, fo) for t in range(9)], 0.025, "frame", 8, steps=2)
        for z in (-3.2, -2.22):
            tube(m, E, "Door shut line", [H.on_side(z, lerp(0.95, 2.45, t / 10), sd, 0.002) for t in range(11)], 0.009, "seam", 6, steps=2)
        box(m, E, "Cockpit door handle", H.on_side(-2.38, 1.5, sd, 0.015), (0.03, 0.03, 0.14), "chrome", bevel=0.008)
        tube(m, E, "Grab handle", [H.on_side(-3.35, 1.25, sd, 0.0), H.on_side(-3.35, 1.25, sd, 0.05), H.on_side(-3.35, 1.55, sd, 0.05), H.on_side(-3.35, 1.55, sd, 0.0)], 0.012, "chrome", 8, steps=3)

    # ---- cabin windows with gaskets, sliding cabin door and its rail
    win_z = [-1.55, -0.6, 0.45, 1.3] if sar else [-1.75, -0.95, -0.15, 0.65, 1.4]
    door_z = twin_cabin_door(sar)
    for sd in (1, -1):
        for wz in win_z:
            bubble = sar and sd > 0 and wz == win_z[1]
            hz, hy = (0.34, 0.30) if bubble else (0.27, 0.22)
            decal(m, E, "Cabin window" + (" (bubble)" if bubble else ""), H, sd, rrect_outline(wz, 1.98, hz, hy, 0.09), "glass", 0.014 if bubble else 0.009, rings=4, gasket=(0.04, "seam", 0.007))
        for z in door_z:
            tube(m, E, "Cabin door shut line", [H.on_side(z, lerp(0.9, 2.5, t / 10), sd, 0.002) for t in range(11)], 0.009, "seam", 6, steps=2)
        for y in (0.9, 2.5):
            tube(m, E, "Cabin door shut line", [H.on_side(lerp(door_z[0], door_z[1], t / 8), y, sd, 0.002) for t in range(9)], 0.009, "seam", 6, steps=2)
        tube(m, E, "Cabin door rail", [H.on_side(lerp(door_z[0] - 0.05, door_z[1] + 0.7, t / 8), 2.56, sd, 0.02) for t in range(9)], 0.018, "metal", 8, steps=2)
        box(m, E, "Cabin door handle", H.on_side(door_z[0] + 0.12, 1.55, sd, 0.015), (0.03, 0.14, 0.03), "chrome", bevel=0.008)
        # cheatline, sweeping up the boom (and a second line on the rescue type)
        line_y = lambda z: 1.2 + smooth(1.6, 4.6, z) * (2.42 - 1.2)
        line_h = lambda z: 0.11 - smooth(2.0, 5.0, z) * 0.06
        ribbon(m, E, "Cheatline", H, sd, -3.7, 7.7, line_y, line_h, "stripe", 0.005, 140)
        if sar: ribbon(m, E, "Cheatline (upper)", H, sd, -3.7, 2.0, lambda z: line_y(z) + line_h(z) + 0.07, lambda z: 0.035, "stripe", 0.005, 80)

    # ---- engine cowling with intakes and panel lines, pylon fairing, exhausts
    cowl = [(-1.55, 2.72, 0.04, 0.04, 0.04, 2.0), (-1.15, 2.78, 0.62, 0.30, 0.12, 2.6), (-0.40, 2.84, 0.80, 0.44, 0.16, 3.2),
            (1.60, 2.84, 0.80, 0.42, 0.16, 3.2), (2.60, 2.72, 0.50, 0.20, 0.12, 2.4), (3.10, 2.60, 0.08, 0.05, 0.05, 2.0)]
    Hc = Hull(cowl)
    o = m.new("Engine cowling", E)
    surface(o, 56, 48, lambda u, v: Hc.P(lerp(Hc.z0, Hc.z1, u), v * 2 * math.pi), lambda *a: "paint")
    for sd in (1, -1):
        decal(m, E, "Engine intake", Hc, sd, rrect_outline(-0.85, 2.88, 0.22, 0.14, 0.12, 44), "intake", 0.01, rings=6, gasket=(0.035, "metal", 0.012))
        for z in (0.25, 1.25):
            tube(m, E, "Cowling panel line", [Hc.on_side(z, lerp(2.72, 3.12, t / 6), sd, 0.002) for t in range(7)], 0.008, "seam", 6, steps=2)
        for i in range(3):
            box(m, E, "Cowling latch", Hc.on_side(0.6 + i * 0.25, 2.78, sd, 0.008), (0.02, 0.03, 0.08), "chrome", bevel=0.005)
    tube(m, E, "Cowling centre line", [Hc.point(lerp(-0.4, 2.5, t / 12), math.pi / 2, 0.002) for t in range(13)], 0.008, "seam", 6, steps=2)
    pylon = [(-0.95, 3.12, 0.04, 0.04, 0.04, 2.0), (-0.60, 3.15, 0.42, 0.20, 0.15, 2.4), (0.55, 3.15, 0.40, 0.20, 0.15, 2.4), (1.05, 3.08, 0.04, 0.04, 0.04, 2.0)]
    Hp = Hull(pylon); o = m.new("Rotor pylon fairing", E)
    surface(o, 30, 32, lambda u, v: Hp.P(lerp(Hp.z0, Hp.z1, u), v * 2 * math.pi), lambda *a: "paint")
    for x in (-1, 1):
        ex = tube(m, E, "Exhaust", [(x * 0.5, 2.95, 1.95), (x * 0.62, 2.98, 2.45), (x * 0.86, 3.02, 2.85)], lambda t: 0.19 + t * 0.03, "darkmetal", 20, caps=False)
        d = norm((x * 0.5, 0.05, 0.85))
        cyl(m, E, "Exhaust mouth", add((x * 0.86, 3.02, 2.85), mul(d, -0.08)), add((x * 0.86, 3.02, 2.85), mul(d, -0.07)), 0.2, 0.2, "intake", 20)
        ring_band(m, E, "Exhaust lip", (x * 0.86, 3.02, 2.85), d, 0.205, 0.235, 0.03, "exhaust", 24)
    cyl(m, E, "Mast", (0, 3.2, 0), (0, 3.5, 0), 0.15, 0.13, "metal", 20)
    cyl(m, E, "Swashplate (fixed)", (0, 3.28, 0), (0, 3.34, 0), 0.36, 0.36, "darkmetal", 32, bevel=0.01)
    for a in (0.5, 2.6, 4.7):
        d = (math.cos(a), 0, math.sin(a))
        tube(m, E, "Servo rod", [add((0, 3.14, 0), mul(d, 0.30)), add((0, 3.28, 0), mul(d, 0.30))], 0.018, "chrome", 8)

    # ---- sponsons, fuel cap, wheels, nose gear
    for x in (-1, 1):
        sp = [(-1.0, 1.0, 0.03, 0.03, 0.03, 2.0, x * 1.12), (-0.6, 0.98, 0.30, 0.26, 0.30, 2.4, x * 1.2), (0.9, 0.98, 0.36, 0.30, 0.32, 2.8, x * 1.24),
              (1.9, 1.05, 0.20, 0.18, 0.18, 2.4, x * 1.16), (2.3, 1.10, 0.03, 0.03, 0.03, 2.0, x * 1.1)]
        Hs = Hull(sp)
        feature_loft(m, E, "Sponson", Hs, feature_rows(-1.0, 2.3, 44, []), [lambda z: 0.92, lambda z: 1.07, lambda z: 1.17], (5, 4, 2, 7),
                     lambda band, z, side: (belly if band == 0 else "stripe" if band == 2 else "paint"))
        if x > 0:
            fp = Hs.on_side(0.9, 1.2, 1, 0.0); fp2 = Hs.on_side(0.9, 1.2, 1, 0.02)
            cyl(m, E, "Fuel cap", fp, fp2, 0.05, 0.045, "red", 16, bevel=0.004)
        wx = x * 1.22
        cyl(m, E, "Main wheel tyre", (wx - 0.13, 0.36, 0.55), (wx + 0.13, 0.36, 0.55), 0.36, 0.36, "rubber", 32, bevel=0.07, segs=4)
        cyl(m, E, "Main wheel hub", (wx - 0.14, 0.36, 0.55), (wx + 0.14, 0.36, 0.55), 0.2, 0.2, "metal", 24, bevel=0.015)
        for i in range(6):
            a = i / 6 * 2 * math.pi
            c = (wx + x * 0.145, 0.36 + 0.12 * math.cos(a), 0.55 + 0.12 * math.sin(a))
            cyl(m, E, "Wheel nut", c, add(c, (x * 0.015, 0, 0)), 0.014, 0.012, "chrome", 6)
        tube(m, E, "Main gear strut", [(wx, 0.36, 0.55), (wx, 0.62, 0.4), (x * 1.05, 0.85, 0.2)], 0.06, "metal", 12)
        cyl(m, E, "Main gear oleo", (wx, 0.52, 0.46), (wx, 0.70, 0.36), 0.045, 0.045, "chrome", 12)
    for s2 in (-1, 1):
        cyl(m, E, "Nose wheel tyre", (s2 * 0.2 - 0.08, 0.27, -3.65), (s2 * 0.2 + 0.08, 0.27, -3.65), 0.27, 0.27, "rubber", 28, bevel=0.05, segs=4)
        cyl(m, E, "Nose wheel hub", (s2 * 0.2 - 0.09, 0.27, -3.65), (s2 * 0.2 + 0.09, 0.27, -3.65), 0.15, 0.15, "metal", 20, bevel=0.01)
    cyl(m, E, "Nose axle", (-0.12, 0.27, -3.65), (0.12, 0.27, -3.65), 0.04, 0.04, "metal", 12)
    tube(m, E, "Nose gear strut", [(0, 0.27, -3.65), (0, 0.6, -3.55), (0, 0.95, -3.4)], 0.06, "metal", 12)
    cyl(m, E, "Nose gear oleo", (0, 0.42, -3.6), (0, 0.66, -3.53), 0.045, 0.045, "chrome", 12)
    tube(m, E, "Torque link", [(0, 0.45, -3.62), (0, 0.55, -3.68), (0, 0.64, -3.56)], 0.014, "darkmetal", 8)

    # ---- tail: swept fin, stabiliser and end plates, bumper, gearbox, drive shaft fairing
    wing(m, E, "Fin", (0, 2.55, 6.55), (0, 4.25, 7.45), 1.45, 0.72, 0.13, (0, 0, 1), (1, 0, 0), lambda c: "stripe" if c[1] > 3.95 else "accent", 10, 24, ufeats=[(3.95 - 2.55) / 1.7])
    for s2 in (-1, 1):
        wing(m, E, "Stabiliser", (s2 * 0.2, 2.5, 6.7), (s2 * 1.2, 2.56, 6.85), 0.62, 0.42, 0.12, (0, 0, 1), (0, 1, 0), lambda c: "stripe" if abs(c[0]) > 1.05 else "accent", 8, 20, ufeats=[0.85])
        wing(m, E, "Stabiliser end plate", (s2 * 1.2, 2.3, 6.8), (s2 * 1.2, 2.82, 6.88), 0.44, 0.40, 0.10, (0, 0, 1), (1, 0, 0), lambda c: "accent", 3, 16)
    tube(m, E, "Tail bumper", [(0, 2.22, 6.6), (0, 1.95, 7.1), (0, 2.18, 7.5)], 0.04, "metal", 10)
    ellipsoid(m, E, "Tail gearbox fairing", (0.12, 3.4, 7.6), (0.18, 0.2, 0.26), "paint", 20, 12)
    cyl(m, E, "Tail rotor shaft", (0.12, 3.4, 7.6), (0.36, 3.4, 7.6), 0.06, 0.05, "metal", 12)
    tube(m, E, "Drive shaft fairing", [H.point(z, math.pi / 2, 0.03) for z in (3.0, 4.0, 5.0, 6.0, 6.6)], 0.07, "paint", 12)

    # ---- lights, radar, aerials, pitots, wipers, steps
    for s2, col in ((-1, "navred"), (1, "navgreen")):
        ellipsoid(m, E, "Nav light", (s2 * 1.6, 1.05, 0.35), (0.06, 0.05, 0.1), col, 14, 8)
    ellipsoid(m, E, "Beacon", (0, 3.3, 1.7), (0.07, 0.06, 0.07), "beacon", 14, 8)
    ellipsoid(m, E, "Belly strobe", (0, 0.82, 0.4), (0.05, 0.04, 0.06), "lens", 12, 6)
    ellipsoid(m, E, "Landing light pod", (-0.4, 0.66, -3.15), (0.14, 0.09, 0.12), "frame", 16, 8)
    cyl(m, E, "Landing light lens", (-0.4, 0.62, -3.24), (-0.4, 0.60, -3.27), 0.09, 0.085, "lens", 20)
    ellipsoid(m, E, "Tail light", (0, 2.5, 8.0), (0.04, 0.04, 0.05), "lens", 12, 6)
    ellipsoid(m, E, "Fin strobe", (0, 4.28, 7.74), (0.04, 0.04, 0.05), "lens", 12, 6)
    if not sar: ellipsoid(m, E, "Weather radar fairing", (0, 0.86, -2.4), (0.32, 0.12, 0.5), "radarfair", 20, 10)
    for s2 in (-1, 1):
        tube(m, E, "Pitot tube", [(s2 * 0.35, 1.3, -4.55), (s2 * 0.37, 1.29, -4.8), (s2 * 0.38, 1.28, -4.95)], 0.022, "chrome", 8)
        tube(m, E, "Wiper", [H.on_side(-4.15, 1.82, s2, 0.02), H.on_side(-3.9, 2.25, s2, 0.02)], 0.012, "darkmetal", 6)
        if not sar: box(m, E, "Cabin step", (s2 * 1.1, 0.72, (door_z[0] + door_z[1]) / 2), (0.12, 0.04, 0.4), "darkmetal", bevel=0.008)
    for (x, z, h) in ((0.3, -2.2, 0.25), (0.0, 3.6, 0.3)):
        b0 = H.point(z, math.pi / 2, 0.0); b0 = (x, b0[1] - 0.02, z)
        wing(m, E, "Aerial", b0, (x, b0[1] + h, z + 0.15), 0.22, 0.12, 0.1, (0, 0, 1), (1, 0, 0), lambda c: "darkmetal", 2, 10)
    wing(m, E, "Belly aerial", (0, 0.88, -0.8), (0, 0.62, -0.68), 0.2, 0.12, 0.1, (0, 0, 1), (1, 0, 0), lambda c: "darkmetal", 2, 10)
    skin_details(m, E, H, roof_z=(-2.6, -2.2), steps=[(-1.25, 2.05), (-1.05, 2.4)], ports=[(-3.7, 1.55), (-3.4, 1.45)],
                 wicks=[(0.5, 1.15, 7.18, 7.27, 2.53)], whip=0.6)
    for sd in (1, -1):   # engine intake grilles: bars across each intake opening
        for dy in (-0.1, -0.04, 0.02, 0.08):
            tube(m, E, "Intake grille bar", [Hc.on_side(-0.85 + dz, 2.88 + dy, sd, 0.014) for dz in (-0.19, -0.06, 0.06, 0.19)], 0.006, "metal", 6, steps=2)
    try:   # fuel filler on the right side, behind the cabin door
        fp = H.on_side(0.95, 1.4, 1, 0.0); fq = H.on_side(0.95, 1.4, 1, 0.006)
        cyl(m, E, "Fuel filler cap", fp, fq, 0.055, 0.055, "chrome", 18)
        cyl(m, E, "Fuel filler ring", fq, H.on_side(0.95, 1.4, 1, 0.0065), 0.07, 0.07, "darkmetal", 18)
    except Exception: pass

    # ---- kit the game fits per flight (each its own part)
    box(m, "eq_hoist", "Hoist mount", (1.02, 2.66, -1.4), (0.24, 0.32, 0.5), "metal", bevel=0.02)
    tube(m, "eq_hoist", "Hoist boom", [(1.05, 2.82, -1.4), (1.35, 2.86, -1.4), (1.68, 2.82, -1.4)], 0.07, "metal", 14)
    cyl(m, "eq_hoist", "Hoist drum", (1.68, 2.82, -1.62), (1.68, 2.82, -1.18), 0.16, 0.16, "hoistyellow", 24, bevel=0.015)
    for z in (-1.64, -1.16): cyl(m, "eq_hoist", "Hoist end plate", (1.68, 2.82, z - 0.012), (1.68, 2.82, z + 0.012), 0.19, 0.19, "darkmetal", 24, bevel=0.005)
    cyl(m, "eq_hoist", "Hook guide", (1.7, 2.66, -1.4), (1.7, 2.52, -1.4), 0.05, 0.04, "darkmetal", 12)
    cyl(m, "eq_flir", "FLIR stalk", (0, 0.88, -3.95), (0, 0.72, -3.95), 0.12, 0.14, "darkmetal", 16)
    ellipsoid(m, "eq_flir", "FLIR turret", (0, 0.56, -3.95), (0.24, 0.24, 0.24), "flirgrey", 24, 14)
    cyl(m, "eq_flir", "FLIR window", (0, 0.58, -4.17), (0, 0.58, -4.2), 0.1, 0.1, "glass", 20)
    cyl(m, "eq_flir", "FLIR window (small)", (0.13, 0.5, -4.13), (0.13, 0.5, -4.16), 0.045, 0.045, "glass", 14)
    tube(m, "eq_searchlight", "Searchlight arm", [(0.5, 0.86, -3.0), (0.5, 0.56, -3.05)], 0.05, "darkmetal", 12)
    cyl(m, "eq_searchlight", "Searchlight housing", (0.5, 0.48, -2.92), (0.5, 0.38, -3.32), 0.2, 0.24, "metal", 24, bevel=0.015)
    cyl(m, "eq_searchlight", "Searchlight lens", (0.5, 0.375, -3.33), (0.5, 0.372, -3.345), 0.215, 0.21, "lens", 24)
    for x in (-1, 1):
        box(m, "eq_floats", "Float pack", (x * 1.26, 1.27, 0.6), (0.24, 0.08, 1.5), "hoistyellow" if sar else "floatpack", bevel=0.03, segs=4)
        for z in (0.1, 1.1): box(m, "eq_floats", "Float strap", (x * 1.26, 1.312, z), (0.25, 0.006, 0.04), "darkmetal")

    # ---- main rotor (part "blades", hub at the origin)
    nb = 5 if sar else 4
    for i in range(nb):
        a = i / nb * 2 * math.pi
        blade(m, a, R=7.5, rootR=0.95, chord=0.529, thick=0.11, twist_deg=-9, root_frac=0.38, tip_mat="hoistyellow" if sar else "tipwhite", fittings=False, nu=30)
        d = (math.sin(a), 0.0, math.cos(a)); sdv = norm(cross(d, (0, 1, 0)))
        cyl(m, "blades", "Blade grip", mul(d, 0.28), mul(d, 0.98), 0.11, 0.09, "metal", 18, bevel=0.01)
        cyl(m, "blades", "Lead-lag damper", add(mul(d, 0.3), mul(sdv, 0.18)), add(mul(d, 0.75), mul(sdv, 0.14)), 0.05, 0.05, "darkmetal", 12, bevel=0.006)
        horn = add(mul(d, 0.6), mul(sdv, -0.14))
        box(m, "blades", "Pitch horn", add(mul(d, 0.6), mul(sdv, -0.08)), (0.12, 0.04, 0.05), "darkmetal", axes=(sdv, (0, 1, 0), d), bevel=0.006)
        tube(m, "blades", "Pitch link", [add(horn, (0, -0.17, 0)), horn], 0.022, "chrome", 8)
        for r in (0.45, 0.85): cyl(m, "blades", "Grip bolt", add(mul(d, r), (0, 0.11, 0)), add(mul(d, r), (0, -0.11, 0)), 0.018, 0.018, "chrome", 8)
    cyl(m, "blades", "Rotor hub", (0, -0.12, 0), (0, 0.14, 0), 0.42, 0.38, "darkmetal", 30, bevel=0.02)
    ellipsoid(m, "blades", "Hub cap", (0, 0.18, 0), (0.3, 0.14, 0.3), "metal", 20, 10)
    cyl(m, "blades", "Swashplate (rotating)", (0, -0.2, 0), (0, -0.165, 0), 0.33, 0.33, "metal", 32, bevel=0.008)

    build_twin_cockpit(m, H, sar)
    ctl = build_twin_controls(m)
    build_twin_cabin(m, H, sar)
    # doors: hinged cockpit doors, sliding cabin doors (both cut from the finished skin and lining)
    doors = []
    cd = twin_cabin_door(sar)
    for side in (1, -1):
        s = "r" if side > 0 else "l"
        carve_door(m, side, TWIN_CREW_DOOR[0] + 0.002, TWIN_CREW_DOOR[1] - 0.002, lambda z: 1.07, glaze_y2, "door_crew_" + s, src_parts=("body", "cockpit"), min_x=0.5)
        piv = H.P(TWIN_CREW_DOOR[0], H.th_at_y(TWIN_CREW_DOOR[0], 1.7, side), 1.0)
        doors.append({"part": "door_crew_" + s, "group": "crew_" + s, "kind": "hinge", "pivot": list(piv), "axis": [0, 1, 0], "open": 1.3 * side})
        carve_door(m, side, cd[0] + 0.002, cd[1] - 0.002, lambda z: 1.07, glaze_y2, "door_cabin_" + s, src_parts=("body", "interior"), min_x=0.5)
        doors.append({"part": "door_cabin_" + s, "group": "cabin_" + s, "kind": "slide", "out": [side * 0.07, 0, 0], "move": [0, 0, 1.45 if sar else 0.75]})
    pz = -3.75; pw, ph, cy0, cz, tilt = 0.62, 0.248, 1.6, pz + 0.04, 0.035
    meta = {
        "format": "RCM1", "model": "Guardian S9" if sar else "Northstar 175", "refRotorDiameter": 15.0, "generator": VERSION,
        "points": {
            "mastY": 3.55, "tailPos": [0.42, 3.4, 7.6], "eye": [0.45, 2.08, -2.8], "fuel": [1.42, 1.2, 0.9],
            "lights": {"navL": [-1.62, 1.05, 0.35], "navR": [1.62, 1.05, 0.35], "tail": [0, 2.5, 8.02], "beacon": [0, 3.36, 1.7],
                        "strobes": [[0, 4.3, 7.75], [0, 0.82, 0.4]], "landing": {"pos": [-0.4, 0.52, -3.2], "dir": [0, -0.3, -1]},
                        "search": {"pos": [0.5, 0.36, -3.36], "dir": [0, -0.8, -0.6]}, "cabin": [0, 1.8, -0.5]},
            "panelQuad": [[-pw, cy0 - ph, cz + tilt], [pw, cy0 - ph, cz + tilt], [pw, cy0 + ph, cz - tilt], [-pw, cy0 + ph, cz - tilt]],
        },
        "controls": [{"part": c["part"], "kind": c["kind"], "pivot": list(c["pivot"])} for c in ctl],
        "tailRotor": {"r": 1.5 * 7.5 / 7.8, "blades": 4, "tip": [0.95, 0.75, 0.10] if sar else "stripe"},
        "equipment": {"hoist": "eq_hoist", "flir": "eq_flir", "searchlight": "eq_searchlight", "floats": "eq_floats"},
        "interior": ["cockpit", "interior"], "doors": doors,
    }
    cdw = [0.6, -0.55] if sar else [0.6, -1.7]
    meta["points"].update({
        "floorY": 0.95,
        "seats": {"pilot": [0.45, 1.31, -2.81, "crew_r"], "crew": [-0.45, 1.31, -2.81, "crew_l"],
                  "medic": [-0.5, 1.415, -1.75, "cabin_l", 1] if sar else [0.7, 1.415, -1.0, "cabin_r", -1],
                  "winchman": [0.5, 1.415, -1.75, "cabin_r", 1] if sar else [-0.7, 1.415, -1.0, "cabin_l", -1]},
        "doorways": {"crew_r": [[0.55, -2.7], [2.0, -2.7]], "crew_l": [[-0.55, -2.7], [-2.0, -2.7]],
                     "cabin_r": [cdw, [2.0, cdw[1]]], "cabin_l": [[-cdw[0], cdw[1]], [-2.0, cdw[1]]]},
        "exhausts": [[-0.86, 3.02, 2.85, -0.5, 0.05, 0.85], [0.86, 3.02, 2.85, 0.5, 0.05, 0.85]],
    })
    build_tail_rotor(m, meta["tailRotor"])
    return m, meta


def build_twin_cabin(m, H, sar):
    """The cabin behind the cockpit (part "interior"): lining, floor, and the fit for the role: passenger seats
    in the offshore type; a stretcher, crew seats and equipment racks in the rescue type."""
    I = "interior"; fy = 0.95; trim = "trim_sar" if sar else "trim_ns"
    cabin_lining(m, H, I, -2.06, 2.2, fy, trim)
    if not sar:
        for z in (-1.0, -0.15, 0.7, 1.55):
            for x in (-0.72, -0.27, 0.27, 0.72):
                nj_seat(m, I, x, z, -1, mat="seat_ns", y=fy, width=0.42, back=0.85)
        box(m, I, "Baggage net", (0, fy + 0.7, 2.15), (1.6, 1.2, 0.02), "harness")
    else:
        for x in (-0.5, 0.5): nj_seat(m, I, x, -1.75, 1, mat="seat_sar", y=fy, width=0.45, back=0.85)
        box(m, I, "Stretcher base", (-0.55, fy + 0.2, 0.9), (0.62, 0.4, 2.1), "cabinet", bevel=0.015)
        box(m, I, "Stretcher", (-0.55, fy + 0.45, 0.9), (0.56, 0.08, 1.95), "metal", bevel=0.01)
        box(m, I, "Mattress", (-0.55, fy + 0.53, 0.92), (0.52, 0.08, 1.85), "mattress", bevel=0.03, segs=3)
        box(m, I, "Equipment rack", (0.75, fy + 0.65, 1.5), (0.45, 1.3, 0.9), "panel2", bevel=0.012)
        for k in range(3):
            cyl(m, I, "Oxygen bottle", (0.62, fy + 0.05, 0.55 + k * 0.18), (0.62, fy + 0.9, 0.55 + k * 0.18), 0.075, 0.075, "o2green", 16)
        box(m, I, "Rescue bag", (0.5, fy + 0.18, -0.6), (0.4, 0.35, 0.5), "hoistyellow", bevel=0.05, segs=3)
        box(m, I, "Winch operator panel", (0.95, fy + 1.15, -0.2), (0.06, 0.3, 0.4), "panel2", bevel=0.01)


def build_twin_cockpit(m, H, sar):
    C = "cockpit"
    trim = "trim_sar" if sar else "trim_ns"
    def chin_in(z): return 1.35 if (sar and CHIN_Z0 < z < CHIN_Z1) else 0.98
    rows = feature_rows(-4.62, -2.05, 64, [CHIN_Z0, WS_Z0, CHIN_Z1, WS_Z1, CD_Z0, CD_Z1] + list(TWIN_CREW_DOOR), ends_dense=False)
    def lining_paint(band, z, side):
        if band == 0: return "floor"
        if band == 1: return None if (sar and CHIN_Z0 < z < CHIN_Z1) else trim
        if band == 3 and (in_ws(z) or in_cd(z)): return None
        return trim
    feature_loft(m, C, "Cockpit lining", H, rows, [lambda z: 0.98, chin_in, glaze_y1, glaze_y2], (6, 3, 6, 8, 7), lining_paint,
                 pt=lambda z, a: H.P(z, a, 0.985), inward=True)
    box(m, C, "Floor", (0, 0.93, -3.2), (1.9, 0.02, 2.4), "floor")
    o = m.new("Rear bulkhead", C)
    cap_fan(o, [H.P(-2.06, j / 96 * 2 * math.pi, 0.985) for j in range(96)], (0, 0, -1), trim)
    box(m, C, "Door to the cabin", (0, 1.6, -2.075), (0.62, 1.3, 0.03), "panel2", bevel=0.006)
    box(m, C, "Cabin door handle", (0.22, 1.6, -2.095), (0.03, 0.12, 0.02), "metal", bevel=0.004)
    # frames along every glazing edge (they also hide the edges of the lining)
    top = lambda z: H.yc(z) + H.ht(z)
    fr = 0.03
    ctr = [H.P(z, math.pi / 2, 0.975) for z in [WS_Z0 + 0.05 + i * 0.1 for i in range(14)] if ws_high(z) >= top(z) - 0.01 and z < WS_Z1 - 0.02]
    if len(ctr) > 1: tube(m, C, "Centre post", ctr, fr, "frame2", 8, steps=2)
    def side_pt(z, y, sd): return H.P(z, H.th_at_y(z, y, sd), 0.97)
    for sd in (1, -1):
        tube(m, C, "Windscreen sill", [side_pt(z, ws_low(z), sd) for z in [WS_Z0 + i * (WS_Z1 - WS_Z0) / 14 for i in range(15)]], fr, "frame2", 8, steps=2)
        hdr = []
        for z in [WS_Z0 + i * (WS_Z1 - WS_Z0) / 26 for i in range(27)]:
            hdr.append(H.P(z, math.pi / 2, 0.97) if ws_high(z) >= top(z) else side_pt(z, ws_high(z), sd))
        tube(m, C, "Windscreen header", hdr, 0.05, "frame2", 8, steps=2)
        for z in (WS_Z1, CD_Z0, CD_Z1):
            tube(m, C, "Door post", [side_pt(z, lerp(glaze_y1(z), glaze_y2(z), t / 8), sd) for t in range(9)], fr, "frame2", 8, steps=2)
        for y in (glaze_y1(CD_Z0), glaze_y2(CD_Z0)):
            tube(m, C, "Door window frame", [side_pt(lerp(CD_Z0, CD_Z1, t / 8), y, sd) for t in range(9)], fr, "frame2", 8, steps=2)
        if sar:
            for y in (0.98, 1.35):
                tube(m, C, "Chin window frame", [side_pt(lerp(CHIN_Z0, CHIN_Z1, t / 8), y, sd) for t in range(9)], 0.025, "frame2", 8, steps=2)
    # ---- instrument panel and glareshield
    pz = -3.75
    box(m, C, "Instrument panel", (0, 1.58, pz - 0.1), (1.86, 0.62, 0.16), "panel2", bevel=0.015)
    box(m, C, "Glareshield", (0, 1.93, pz - 0.05), (1.96, 0.05, 0.42), "glareshield", bevel=0.015)
    box(m, C, "Glareshield lip", (0, 1.9, pz + 0.16), (1.96, 0.02, 0.02), "glareshield")
    gf = Face(m, C, (0, 1.955, pz + 0.17), (1, 0, 0), (0, 0, -1), (0, 1, 0))
    for sx in (-1, 1): gf.button(sx * 0.55, 0.06, "warn_r", 0.06, 0.03); gf.button(sx * 0.47, 0.06, "lamp_a", 0.06, 0.03)
    apf = Face(m, C, (0, 1.955, pz + 0.1), (1, 0, 0), (0, 0, -1), (0, 1, 0))
    apf.plate(0, 0.06, 0.5, 0.07, "panel", 0.012)
    for i in range(8): apf.button(-0.21 + i * 0.06, 0.06, "lamp_g" if i % 3 == 0 else "btn_w", 0.045, 0.035)
    # four large landscape displays across the panel (flight, engine and nav pages: the game draws them live),
    # each with its row of soft keys above and below, and a round standby instrument in the middle
    pf = Face(m, C, (0, 1.58, pz - 0.018), (1, 0, 0), (0, 1, 0), (0, 0, 1))
    for x in (-0.66, -0.25, 0.25, 0.66):
        pf.screen(x, 0.05, 0.33, 0.26, "disp")
        for i in range(6):
            pf.button(x - 0.14 + i * 0.056, 0.215, "btn_grey", 0.034, 0.02)
            pf.button(x - 0.14 + i * 0.056, -0.115, "btn_grey", 0.034, 0.02)
        pf.knob(x - 0.15, -0.2, 0.014); pf.knob(x + 0.15, -0.2, 0.014)
    gauge(m, C, pf, 0, 0.1, 0.055, "standby attitude", kind="attitude")
    for i in range(4): pf.lamp(-0.045 + i * 0.03, 0.2, "lamp_a" if i == 0 else "lamp_g", 0.022, 0.012)
    for i in range(5): pf.toggle(-0.06 + i * 0.03, -0.2)
    # ---- centre pedestal
    box(m, C, "Pedestal", (0, 1.08, -3.3), (0.38, 0.3, 0.8), "panel", bevel=0.012)
    ped = Face(m, C, (0, 1.235, -3.3), (1, 0, 0), (0, 0, -1), (0, 1, 0))
    for (y, glow) in ((0.3, "screen_g"), (0.18, "screen_g"), (0.06, "screen_a"), (-0.06, "screen_g")):
        ped.plate(0, y, 0.34, 0.1, "panel2", 0.006); ped.screen(-0.05, y, 0.14, 0.03, glow); ped.knob(0.1, y, 0.016); ped.knob(-0.14, y, 0.01)
    for i in range(6): ped.button(-0.13 + i * 0.052, -0.18, "lamp_g" if i < 3 else "btn_grey", 0.04, 0.03)
    for i in range(4): ped.toggle(-0.12 + i * 0.08, -0.3)
    # ---- overhead console
    ohz0 = -3.4; ohY = H.yc(-2.9) + H.ht(-2.9) * 0.97 - 0.02
    box(m, C, "Overhead console", (0, ohY - 0.04, ohz0 + 0.5), (0.62, 0.08, 1.1), "panel2", bevel=0.012)
    oh = Face(m, C, (0, ohY - 0.081, ohz0 + 0.5), (1, 0, 0), (0, 0, 1), (0, -1, 0))
    for r in range(5):
        for i in range(7): oh.toggle(-0.24 + i * 0.08, -0.42 + r * 0.1, (r + i) % 3 != 0, 1.1)
    for sx in (-1, 1):
        oh.plate(sx * 0.14, 0.18, 0.16, 0.16, "panel", 0.008)
        box(m, C, "Fire handle", oh.at(sx * 0.14, 0.18, 0.03), (0.12, 0.03, 0.03), "fire_red", bevel=0.006)
        box(m, C, "Fire handle stem", oh.at(sx * 0.14, 0.18, 0.015), (0.02, 0.03, 0.02), "fire_red")
        cyl(m, C, "Engine lever", oh.at(sx * 0.08, 0.36, 0), oh.at(sx * 0.08, 0.36, 0.09), 0.008, 0.008, "metal", 8)
        box(m, C, "Engine lever knob", oh.at(sx * 0.08, 0.36, 0.1), (0.04, 0.03, 0.03), "seatframe", bevel=0.006)
    for i in range(6): oh.lamp(-0.2 + i * 0.08, 0.48, "lamp_a" if i < 2 else "lamp_g", 0.05, 0.018)
    # ---- side consoles, visors, headsets, compass, extinguisher, seats
    for sx in (-1, 1):
        box(m, C, "Side console", (sx * 0.95, 1.25, -3.05), (0.14, 0.12, 0.9), "panel2", bevel=0.01)
        sf = Face(m, C, (sx * 0.95, 1.315, -3.05), (1, 0, 0), (0, 0, -1), (0, 1, 0))
        for i in range(4): sf.knob(0, -0.3 + i * 0.15, 0.012)
        sf.toggle(0.02, 0.33)
        box(m, C, "Sun visor", (sx * 0.48, 2.5, -3.55), (0.42, 0.012, 0.22), "visor", bevel=0.004)
        hx = sx * 0.45
        tube(m, C, "Headset band", [(hx - 0.08, 2.06, -2.33), (hx - 0.06, 2.17, -2.34), (hx, 2.2, -2.34), (hx + 0.06, 2.17, -2.34), (hx + 0.08, 2.06, -2.33)], 0.013, "headset", 8)
        for s2 in (-1, 1): cyl(m, C, "Ear cup", (hx + s2 * 0.08, 2.03, -2.31), (hx + s2 * 0.08, 2.03, -2.36), 0.055, 0.055, "headset", 20, bevel=0.012)
        seat(m, C, sx * 0.45, 0.94, -3.02, -2.6, 0.5, 2.2, "seat_sar" if sar else "seat_ns", "seatframe", "harness_sar" if sar else "harness")
    ellipsoid(m, C, "Standby compass", (0, 2.32, -4.0), (0.05, 0.04, 0.045), "panel", 14, 8)
    cyl(m, C, "Compass stem", (0, 2.38, -3.95), (0, 2.5, -3.9), 0.01, 0.01, "panel", 8)
    cyl(m, C, "Fire extinguisher", (-0.85, 1.05, -2.25), (-0.85, 1.45, -2.25), 0.06, 0.06, "red", 20, bevel=0.008)


def build_twin_controls(m):
    ctl = []
    for sx in (-1, 1):
        tag = "l" if sx < 0 else "r"
        p = "ctl_cyclic_" + tag; base = (sx * 0.45, 0.95, -3.18)
        cyl(m, p, "Cyclic boot", base, (sx * 0.45, 0.99, -3.18), 0.07, 0.05, "rubber", 16, bevel=0.01)
        tube(m, p, "Cyclic stick", [base, (sx * 0.45, 1.2, -3.2), (sx * 0.45, 1.42, -3.26)], 0.016, "darkmetal", 10)
        cyl(m, p, "Cyclic grip", (sx * 0.45, 1.4, -3.26), (sx * 0.45, 1.56, -3.3), 0.028, 0.03, "grip", 16, bevel=0.008)
        for i in range(3):
            cyl(m, p, "Grip switch", (sx * 0.45 + (i - 1) * 0.015, 1.56, -3.3), (sx * 0.45 + (i - 1) * 0.015, 1.575, -3.31), 0.006, 0.005, "red" if i == 1 else "chrome", 8)
        ctl.append({"part": p, "kind": "cyclic", "pivot": base})
        p = "ctl_collective_" + tag; px = 0.22 if sx > 0 else -0.78; piv = (px, 1.05, -2.55); tip = (px, 1.18, -3.15)
        tube(m, p, "Collective lever", [piv, (px, 1.12, -2.85), tip], 0.02, "darkmetal", 10)
        box(m, p, "Collective switch head", (px, 1.22, -3.17), (0.06, 0.07, 0.09), "panel", bevel=0.008)
        for i in range(3): box(m, p, "Collective switch", (px + (i - 1) * 0.018, 1.26, -3.17), (0.012, 0.012, 0.03), "lamp_a" if i == 0 else "chrome")
        ctl.append({"part": p, "kind": "collective", "pivot": piv})
        for kind, dx in (("pedalL", -0.13), ("pedalR", 0.13)):
            p = "ctl_%s_%s" % (kind, tag); x = sx * 0.45 + dx
            tube(m, p, "Pedal arm", [(x, 0.95, -3.5), (x, 1.06, -3.56), (x, 1.14, -3.53)], 0.014, "metal", 8)
            box(m, p, "Pedal", (x, 1.09, -3.55), (0.09, 0.16, 0.018), "seatframe", bevel=0.005)
            for i in range(3): box(m, p, "Pedal tread", (x, 1.04 + i * 0.05, -3.56), (0.08, 0.01, 0.005), "rubber")
            ctl.append({"part": p, "kind": kind, "pivot": (x, 0.95, -3.55)})
    return ctl


# ============================================================================ landmarks (model "landmarks")
# One part per placed structure, named by the game's landmarkKey(). Each is built in its own frame: origin on
# the ground at the structure's centre, unrotated (the game places and rotates it), x across the width w,
# z across the depth d. The instance list below is written by tools/export_landmarks.ts.
# ---- BEGIN GENERATED LANDMARKS (tools/export_landmarks.ts) ----
LANDMARKS = [
    {"key":"hangar@55,-70","kind":"hangar","w":40,"d":26,"h":11},
    {"key":"tower@95,10","kind":"tower","w":8,"d":8,"h":16},
    {"key":"hospital@-1240,-4760","kind":"hospital","w":70,"d":40,"h":14},
    {"key":"lighthouse@880,-3600","kind":"lighthouse","w":6,"d":6,"h":24},
    {"key":"hospital@-1000,-1080","kind":"hospital","w":58,"d":40,"h":24,"pad":[-8,-6,10]},
    {"key":"hospital@-1000,-1030","kind":"hospital","w":44,"d":22,"h":10},
    {"key":"hospital@335,-7672","kind":"hospital","w":92,"d":50,"h":20},
    {"key":"hospital@392,-7600","kind":"hospital","w":34,"d":28,"h":12},
    {"key":"tower@-238,-540","kind":"tower","w":4,"d":4,"h":16},
    {"key":"distillery@-2470,360","kind":"distillery","w":22,"d":14,"h":10},
    {"key":"castle@-2120,-630","kind":"castle","w":13,"d":11,"h":19,"walls":[[0,22,34,4.5,0,0],[-17,6,32,3.2,1,0],[17,4,26,6.5,1,0],[-6,-16,22,2.8,0,0],[10,-16,6,5.5,0,0]]},
    {"key":"platform@21030,-6000","kind":"platform","w":45,"d":40,"h":38,"deck":[-30,0,62]},
    {"key":"platform@30530,3500","kind":"platform","w":45,"d":40,"h":38,"deck":[-30,0,58]},
    {"key":"platform@26530,-14500","kind":"platform","w":45,"d":40,"h":38,"deck":[-30,0,66]},
]
# ---- END GENERATED LANDMARKS ----

import random as _random


def quad(o, a, b, c, d, mat, outward):
    oriented_quad(o, [o.vert(a), o.vert(b), o.vert(c), o.vert(d)], outward, mat)


FACES = {   # face id: (outward normal, along-axis) for a box centred on the origin
    0: ((0, 0, 1), (1, 0, 0)), 1: ((1, 0, 0), (0, 0, -1)), 2: ((0, 0, -1), (-1, 0, 0)), 3: ((-1, 0, 0), (0, 0, 1)),
}


def face_rect(o, w, d, face, u, y, rw, rh, mat, off=0.03):
    """A rectangle on the face of a w x d box: centre at u along the face, height y; size rw x rh."""
    n, a = FACES[face]
    half = (d if face in (0, 2) else w) / 2
    c = add(mul(n, half + off), mul(a, u))
    p = lambda du, dy: (c[0] + a[0] * du, y + dy, c[2] + a[2] * du)
    quad(o, p(-rw / 2, -rh / 2), p(rw / 2, -rh / 2), p(rw / 2, rh / 2), p(-rw / 2, rh / 2), mat, n)


def face_len(w, d, face): return w if face in (0, 2) else d


def window_rows(o, w, d, faces, ys, ww, wh, spacing, margin=2.0, skip=None, mat="window", off=0.03):
    for f in faces:
        L = face_len(w, d, f); n = max(1, int((L - 2 * margin) // spacing))
        for y in ys:
            for i in range(n):
                u = -L / 2 + margin + (i + 0.5) * (L - 2 * margin) / n
                if skip and skip(f, u, y): continue
                face_rect(o, w, d, f, u, y, ww, wh, mat, off)


def gable_roof(m, part, name, cx, cz, eave, length_x, width_z, rise, mat, gable_mat, ridge_along_x=True, over=0.3):
    """Pitched roof with gable ends. Ridge along x by default."""
    o = m.new(name, part)
    if ridge_along_x:
        x0, x1, hz = cx - length_x / 2 - over, cx + length_x / 2 + over, width_z / 2 + over
        e0, e1, r = (x0, eave, cz - hz), (x1, eave, cz - hz), (x1, eave + rise * (1 + over / (width_z / 2)), cz)
        top = eave + rise
        quad(o, (x0, eave - over * rise / (width_z / 2), cz - hz), (x1, eave - over * rise / (width_z / 2), cz - hz), (x1, top, cz), (x0, top, cz), mat, (0, 1, -1))
        quad(o, (x0, eave - over * rise / (width_z / 2), cz + hz), (x1, eave - over * rise / (width_z / 2), cz + hz), (x1, top, cz), (x0, top, cz), mat, (0, 1, 1))
        g = m.new(name + " gable", part)
        for sx in (-1, 1):
            xg = cx + sx * length_x / 2
            ids = [g.vert((xg, eave, cz - width_z / 2)), g.vert((xg, eave, cz + width_z / 2)), g.vert((xg, top - over * rise / (width_z / 2) * 0, cz))]
            P = [g.v[i] for i in ids]
            g.face(ids if dot(cross(sub(P[1], P[0]), sub(P[2], P[0])), (sx, 0, 0)) >= 0 else ids[::-1], gable_mat)
    else:
        z0, z1, hx = cz - length_x / 2 - over, cz + length_x / 2 + over, width_z / 2 + over
        top = eave + rise; dr = over * rise / (width_z / 2)
        quad(o, (cx - hx, eave - dr, z0), (cx - hx, eave - dr, z1), (cx, top, z1), (cx, top, z0), mat, (-1, 1, 0))
        quad(o, (cx + hx, eave - dr, z0), (cx + hx, eave - dr, z1), (cx, top, z1), (cx, top, z0), mat, (1, 1, 0))
        g = m.new(name + " gable", part)
        for sz in (-1, 1):
            zg = cz + sz * length_x / 2
            ids = [g.vert((cx - width_z / 2, eave, zg)), g.vert((cx + width_z / 2, eave, zg)), g.vert((cx, top, zg))]
            P = [g.v[i] for i in ids]
            g.face(ids if dot(cross(sub(P[1], P[0]), sub(P[2], P[0])), (0, 0, sz)) >= 0 else ids[::-1], gable_mat)


def frustum(m, part, name, cx, cz, y0, h0, y1, h1, mat, top=True):
    """Square frustum (a pyramid when h1 = 0): half-size h0 at y0 to h1 at y1."""
    o = m.new(name, part)
    for (nx, nz) in ((0, 1), (1, 0), (0, -1), (-1, 0)):
        ax = (-nz, nx)   # along the face
        def P(hh, y, s): return (cx + nx * hh + ax[0] * hh * s, y, cz + nz * hh + ax[1] * hh * s)
        quad(o, P(h0, y0, -1), P(h0, y0, 1), P(h1, y1, 1), P(h1, y1, -1), mat, (nx, 0.4, nz))
    if top and h1 > 0: quad(o, (cx - h1, y1, cz - h1), (cx + h1, y1, cz - h1), (cx + h1, y1, cz + h1), (cx - h1, y1, cz + h1), mat, (0, 1, 0))
    return o


def railing(m, part, pts, h, mat, post_every=1.5, closed=True, r=0.03):
    """Posts and a top rail along a polyline."""
    pts = list(pts) + ([pts[0]] if closed else [])
    tube(m, part, "Handrail", [(p[0], p[1] + h, p[2]) for p in pts], r, mat, 6, caps=False, steps=1)
    tube(m, part, "Knee rail", [(p[0], p[1] + h * 0.5, p[2]) for p in pts], r * 0.8, mat, 6, caps=False, steps=1)
    for a, b in zip(pts, pts[1:]):
        n = max(1, int(length(sub(b, a)) // post_every))
        for i in range(n):
            p = add(a, mul(sub(b, a), i / n))
            cyl(m, part, "Rail post", p, (p[0], p[1] + h, p[2]), r, r, mat, 6, caps=False)


# ---------------------------------------------------------------------------------------- hospital
def lm_hospital(m, P, e):
    w, d, h = e["w"], e["d"], e["h"]; pad = e.get("pad")
    box(m, P, "Plinth", (0, 0.1, 0), (w + 0.8, 0.8, d + 0.8), "conc_grey")
    box(m, P, "Main block", (0, h / 2, 0), (w, h, d), "conc_white", bevel=0.06)
    floors = max(2, int((h - 1) // 4))
    for f in range(1, floors + 1):
        y = f * 4 - 0.3
        if y < h - 0.8: box(m, P, "Floor band", (0, y, 0), (w + 0.2, 0.35, d + 0.2), "conc_grey")
    win = m.new("Windows", P)
    ys = [2.2 + f * 4 for f in range(floors) if 2.2 + f * 4 + 0.9 < h - 0.5]
    window_rows(win, w, d, (0, 1, 2, 3), ys, 2.6, 1.6, 3.3, margin=2.5, skip=lambda f, u, y: f == 0 and y < 4 and abs(u) < 6)
    # glazed stair tower at one corner
    box(m, P, "Stair tower", (w / 2 - 1.5, (h + 2.5) / 2, d / 2 - 1.5), (4.0, h + 2.5, 4.0), "window")
    for (x, z) in ((w / 2 + 0.5, d / 2 + 0.5), (w / 2 - 3.5, d / 2 + 0.5), (w / 2 + 0.5, d / 2 - 3.5)):
        box(m, P, "Stair frame", (x, (h + 2.5) / 2, z), (0.25, h + 2.5, 0.25), "conc_grey")
    for y in [4.0 * k for k in range(1, int(h // 4) + 1)] + [h + 2.5]:
        box(m, P, "Stair floor", (w / 2 - 1.5, y, d / 2 - 1.5), (4.2, 0.25, 4.2), "conc_grey")
    # entrance: glazed screen, canopy on columns, name band and a hospital sign
    face_rect(win, w, d, 0, 0, 1.7, 7.0, 3.2, "window", 0.04)
    box(m, P, "Entrance canopy", (0, 3.75, d / 2 + 1.9), (10.0, 0.35, 3.8), "conc_grey", bevel=0.05)
    for x in (-4.5, 4.5): cyl(m, P, "Canopy column", (x, 0, d / 2 + 3.4), (x, 3.6, d / 2 + 3.4), 0.18, 0.18, "rail_white", 12)
    box(m, P, "Name band", (0, h - 1.2, d / 2 + 0.09), (min(18, w * 0.4), 1.2, 0.15), "sign_blue", bevel=0.02)
    box(m, P, "Sign panel", (7.5, 3.0, d / 2 + 0.08), (1.4, 1.4, 0.12), "sign_blue", bevel=0.02)
    for (x, y, sx, sy) in ((7.2, 3.0, 0.16, 0.9), (7.8, 3.0, 0.16, 0.9), (7.5, 3.0, 0.6, 0.16)):
        box(m, P, "Sign H", (x, y, d / 2 + 0.15), (sx, sy, 0.03), "sign_white")
    # ambulance bay on the -x side
    box(m, P, "Ambulance canopy", (-w / 2 - 3.2, 4.3, 0), (6.4, 0.3, 9.0), "conc_grey", bevel=0.04)
    for z in (-4, 4): cyl(m, P, "Canopy column", (-w / 2 - 6.0, 0, z), (-w / 2 - 6.0, 4.2, z), 0.16, 0.16, "rail_white", 12)
    face_rect(win, w, d, 3, 0, 1.6, 4.0, 3.0, "window", 0.04)
    for z in (-3.0, -1.0, 1.0, 3.0): cyl(m, P, "Bollard", (-w / 2 - 6.6, 0, z), (-w / 2 - 6.6, 0.9, z), 0.12, 0.12, "steel_yellow", 10)
    # roof: parapet, membrane, plant (kept well clear of a rooftop pad)
    for (cx, cz, sx, sz) in ((0, d / 2 - 0.2, w, 0.4), (0, -d / 2 + 0.2, w, 0.4), (w / 2 - 0.2, 0, 0.4, d), (-w / 2 + 0.2, 0, 0.4, d)):
        box(m, P, "Parapet", (cx, h + 0.5, cz), (sx, 1.0, sz), "conc_grey")
    box(m, P, "Roof membrane", (0, h + 0.03, 0), (w - 0.6, 0.06, d - 0.6), "felt_roof")
    def clear(x, z, r): return pad is None or math.hypot(x - pad[0], z - pad[1]) > pad[2] * 1.6 + r
    if clear(w / 2 - 9, -d / 2 + 6, 6):
        box(m, P, "Plant room", (w / 2 - 9, h + 2.0, -d / 2 + 6), (10, 4, 7), "module_grey", bevel=0.04)
        o = m.new("Louvres", P)
        for i in range(6): quad(o, (w / 2 - 13 + i * 1.4, h + 1.2, -d / 2 + 9.53), (w / 2 - 12.2 + i * 1.4, h + 1.2, -d / 2 + 9.53), (w / 2 - 12.2 + i * 1.4, h + 3.2, -d / 2 + 9.53), (w / 2 - 13 + i * 1.4, h + 3.2, -d / 2 + 9.53), "door_dark", (0, 0, 1))
    for i in range(3):
        x, z = -w / 2 + 6 + i * 3.5, -d / 2 + 4
        if clear(x, z, 2): box(m, P, "Air handler", (x, h + 0.8, z), (2.6, 1.6, 2.0), "rail_white", bevel=0.05); cyl(m, P, "Fan", (x, h + 1.6, z), (x, h + 1.65, z), 0.7, 0.7, "door_dark", 16)
    if clear(-w / 2 + 5, d / 2 - 5, 3): box(m, P, "Lift overrun", (-w / 2 + 5, h + 1.5, d / 2 - 5), (4, 3, 4), "conc_white", bevel=0.04)


# ------------------------------------------------------------------------------------------ hangar
def lm_hangar(m, P, e):
    w, d, h = e["w"], e["d"], e["h"]          # doors on the +z face
    box(m, P, "Plinth", (0, 0.0, 0), (w + 0.4, 0.6, d + 0.4), "conc_grey")
    box(m, P, "Walls", (0, h / 2, 0), (w, h, d), "clad_wall", bevel=0.05)
    box(m, P, "Kick plate", (0, 0.6, 0), (w + 0.06, 1.2, d + 0.06), "module_grey")
    # barrel roof across the span, ridge front to back
    rise, nseg = 3.0, 24
    o = m.new("Barrel roof", P); g = m.new("Arched gable", P)
    arc = [(-w / 2 - 0.4 + (w + 0.8) * i / nseg, h + rise * (1 - (2 * i / nseg - 1) ** 2)) for i in range(nseg + 1)]
    for i in range(nseg):
        (x0, y0), (x1, y1) = arc[i], arc[i + 1]
        quad(o, (x0, y0, -d / 2 - 0.4), (x1, y1, -d / 2 - 0.4), (x1, y1, d / 2 + 0.4), (x0, y0, d / 2 + 0.4), "clad_roof", (-(y1 - y0), x1 - x0, 0))
    for sz in (-1, 1):
        cen = g.vert((0, h, sz * d / 2))
        ring = [g.vert((max(-w / 2, min(w / 2, x)), y - 0.05, sz * d / 2)) for (x, y) in arc]
        for i in range(nseg):
            ids = [cen, ring[i], ring[i + 1]]; Pp = [g.v[k] for k in ids]
            g.face(ids if dot(cross(sub(Pp[1], Pp[0]), sub(Pp[2], Pp[0])), (0, 0, sz)) >= 0 else ids[::-1], "clad_wall")
    for x in (-w / 4, 0, w / 4): box(m, P, "Ridge vent", (x, h + rise * (1 - (2 * (x + w / 2 + 0.4) / (w + 0.8) - 1) ** 2) + 0.3, 0), (1.2, 0.6, 3.0), "rail_white", bevel=0.05)
    # sliding doors in four leaves, header and track
    wd, hd = w * 0.82, h * 0.84
    for i in range(4):
        x = -wd / 2 + (i + 0.5) * wd / 4
        box(m, P, "Door leaf", (x, hd / 2, d / 2 + 0.2 + (i % 2) * 0.3), (wd / 4 - 0.05, hd, 0.25), "clad_door", bevel=0.02)
    box(m, P, "Door header", (0, hd + 0.45, d / 2 + 0.35), (wd + 1.2, 0.9, 0.7), "module_grey", bevel=0.04)
    box(m, P, "Door track", (0, 0.05, d / 2 + 0.35), (wd + 1.2, 0.1, 0.7), "rail_white")
    face_rect(m.new("Wicket door", P), w, d, 0, wd / 8, 1.05, 0.9, 2.1, "door_dark", 0.36)
    box(m, P, "Hangar sign", (0, hd + 1.7, d / 2 + 0.1), (12, 1.6, 0.15), "sign_blue", bevel=0.03)
    box(m, P, "Sign stripe", (0, hd + 1.35, d / 2 + 0.19), (11.6, 0.18, 0.03), "sign_white")
    for x in (-wd / 3, 0, wd / 3):
        box(m, P, "Floodlight", (x, hd + 0.95, d / 2 + 0.9), (0.6, 0.3, 0.4), "door_dark"); box(m, P, "Floodlight lens", (x, hd + 0.8, d / 2 + 0.9), (0.5, 0.02, 0.3), "lens")
    # windows, personnel door, gutters and downpipes
    win = m.new("Windows", P)
    window_rows(win, w, d, (2,), [h - 2.0], 3.5, 1.2, 6.4, margin=2.0)
    window_rows(win, w, d, (1, 3), [h - 2.0], 3.0, 1.0, 6.0, margin=3.0)
    face_rect(win, w, d, 2, -w / 2 + 3, 1.15, 1.0, 2.2, "door_dark", 0.03)
    box(m, P, "Door canopy", (w / 2 - 3, 2.6, -d / 2 - 0.6), (1.8, 0.12, 1.2), "rail_white")
    for sx in (-1, 1):
        box(m, P, "Gutter", (sx * (w / 2 + 0.6), h - 0.05, 0), (0.25, 0.25, d + 0.8), "rail_white")
        for sz in (-1, 1): cyl(m, P, "Downpipe", (sx * (w / 2 + 0.12), 0.2, sz * (d / 2 - 0.4)), (sx * (w / 2 + 0.12), h, sz * (d / 2 - 0.4)), 0.07, 0.07, "rail_white", 8)


# ------------------------------------------------------------------------------------------- towers
def octagon_prism(m, P, name, y0, y1, r0, r1, mat, caps=True):
    return cyl(m, P, name, (0, y0, 0), (0, y1, 0), r0 / math.cos(math.pi / 8), r1 / math.cos(math.pi / 8), mat, 8, caps=caps, sharp=30.0, smooth=False)


def lm_tower(m, P, e):
    w, d, h = e["w"], e["d"], e["h"]
    if w < 6:      # fire-service drill tower: brick, open at each floor, steel ladder
        box(m, P, "Drill tower", (0, h / 2, 0), (w, h, d), "brick_red", bevel=0.03)
        o = m.new("Openings", P)
        for y in range(3, int(h) - 1, 3):
            face_rect(o, w, d, 0, 0, y + 0.1, 1.4, 2.0, "door_dark", 0.02); face_rect(o, w, d, 1, 0, y + 0.1, 1.2, 1.8, "door_dark", 0.02)
            box(m, P, "Floor band", (0, y - 1.0, 0), (w + 0.1, 0.25, d + 0.1), "conc_grey")
        box(m, P, "Parapet coping", (0, h + 0.1, 0), (w + 0.3, 0.2, d + 0.3), "conc_grey")
        for x in (-0.25, 0.25): cyl(m, P, "Ladder rail", (x, 0, -d / 2 - 0.3), (x, h, -d / 2 - 0.3), 0.03, 0.03, "rail_white", 6)
        for y in [0.3 * i for i in range(1, int(h / 0.3))]: box(m, P, "Ladder rung", (0, y, -d / 2 - 0.3), (0.5, 0.03, 0.03), "rail_white")
        return
    # control tower: operations annex, shaft, octagonal glazed cab with gallery
    box(m, P, "Annex", (0, 1.75, 0), (w, 3.5, d), "conc_white", bevel=0.05)
    box(m, P, "Annex roof", (0, 3.55, 0), (w + 0.3, 0.2, d + 0.3), "conc_grey")
    win = m.new("Windows", P)
    window_rows(win, w, d, (1, 2, 3), [1.9], 1.4, 1.2, 2.2, margin=1.0)
    face_rect(win, w, d, 0, -1.5, 1.2, 1.8, 2.4, "window", 0.03)
    box(m, P, "Shaft", (0, h / 2, 0), (4.4, h, 4.4), "conc_white", bevel=0.05)
    o = m.new("Stair glazing", P); face_rect(o, 4.4, 4.4, 0, 0, (4.0 + h - 1.5) / 2, 1.0, h - 5.5, "window", 0.03)
    box(m, P, "Cab floor", (0, h - 0.25, 0), (8.4, 0.5, 8.4), "conc_grey", bevel=0.05)
    box(m, P, "Gallery", (0, h + 0.05, 0), (9.6, 0.15, 9.6), "deck_grey")
    railing(m, P, [(-4.8, h + 0.12, -4.8), (4.8, h + 0.12, -4.8), (4.8, h + 0.12, 4.8), (-4.8, h + 0.12, 4.8)], 1.05, "rail_white")
    octagon_prism(m, P, "Cab base", h, h + 0.8, 3.5, 3.55, "module_grey")
    octagon_prism(m, P, "Cab glazing", h + 0.8, h + 3.0, 3.55, 3.95, "window", caps=False)
    for i in range(8):
        a = (i + 0.5) / 8 * 2 * math.pi; c0, c1 = 3.55 / math.cos(math.pi / 8), 3.95 / math.cos(math.pi / 8)
        tube(m, P, "Mullion", [(math.cos(a) * c0, h + 0.8, math.sin(a) * c0), (math.cos(a) * c1, h + 3.0, math.sin(a) * c1)], 0.05, "door_dark", 6, steps=1)
    octagon_prism(m, P, "Cab roof", h + 3.0, h + 3.4, 4.5, 4.5, "module_grey")
    octagon_prism(m, P, "Roof membrane", h + 3.4, h + 3.45, 4.2, 4.2, "felt_roof")
    for (x, z, hh) in ((1.5, 1.0, 3.5), (-1.2, -1.4, 2.5)): cyl(m, P, "Aerial", (x, h + 3.45, z), (x, h + 3.45 + hh, z), 0.04, 0.02, "rail_white", 6)
    ellipsoid(m, P, "Beacon", (0, h + 3.7, 0), (0.18, 0.15, 0.18), "beacon", 12, 6)


# --------------------------------------------------------------------------------------- lighthouse
def lm_lighthouse(m, P, e):
    h = e["h"]
    cyl(m, P, "Tower plinth", (0, -0.4, 0), (0, 0.8, 0), 3.5, 3.4, "stone_dark", 32)
    cyl(m, P, "Tower", (0, 0.8, 0), (0, h * 0.45, 0), 3.0 - 0.8 * (0.8 / h) * 0.0 - 0.03, 3.0 - 0.8 * 0.45, "harl_white", 32, caps=False)
    cyl(m, P, "Red band", (0, h * 0.45, 0), (0, h * 0.55, 0), 3.0 - 0.8 * 0.45 + 0.02, 3.0 - 0.8 * 0.55 + 0.02, "harl_red", 32, caps=False)
    cyl(m, P, "Tower upper", (0, h * 0.55, 0), (0, h - 0.6, 0), 3.0 - 0.8 * 0.55, 2.25, "harl_white", 32, caps=False)
    cyl(m, P, "Corbel", (0, h - 0.6, 0), (0, h, 0), 2.25, 2.95, "harl_white", 32)
    cyl(m, P, "Gallery deck", (0, h, 0), (0, h + 0.22, 0), 3.05, 3.05, "door_dark", 32)
    ring_band(m, P, "Gallery top rail", (0, h + 1.25, 0), (0, 1, 0), 2.92, 3.0, 0.06, "door_dark", 32)
    for i in range(24):
        a = i / 24 * 2 * math.pi
        cyl(m, P, "Gallery post", (math.cos(a) * 2.96, h + 0.22, math.sin(a) * 2.96), (math.cos(a) * 2.96, h + 1.25, math.sin(a) * 2.96), 0.03, 0.03, "door_dark", 6, caps=False)
    cyl(m, P, "Lantern base", (0, h + 0.22, 0), (0, h + 0.9, 0), 1.75, 1.75, "door_dark", 24)
    cyl(m, P, "Lantern", (0, h + 0.9, 0), (0, h + 2.6, 0), 1.6, 1.6, "lantern_glow", 24, caps=False, ao=False)
    for i in range(12):
        a = i / 12 * 2 * math.pi
        box(m, P, "Astragal", (math.cos(a) * 1.62, h + 1.75, math.sin(a) * 1.62), (0.06, 1.7, 0.06), "door_dark")
    cyl(m, P, "Lantern roof", (0, h + 2.6, 0), (0, h + 3.4, 0), 1.9, 0.35, "door_dark", 24)
    ellipsoid(m, P, "Vent ball", (0, h + 3.6, 0), (0.32, 0.3, 0.32), "door_dark", 14, 8)
    cyl(m, P, "Finial", (0, h + 3.85, 0), (0, h + 4.6, 0), 0.04, 0.02, "door_dark", 6)
    # door with a dressed-stone surround (facing -x) and small stair windows climbing round the tower
    box(m, P, "Door surround", (-2.98, 1.9, 0), (0.3, 2.6, 1.6), "stone_dark")
    box(m, P, "Door", (-3.1, 1.75, 0), (0.06, 2.2, 1.1), "timber_door")
    for i in range(5):
        y = 4 + i * (h - 8) / 4; a = math.pi + 0.8 + i * 1.3; r = 3.0 - 0.8 * (y / h)
        box(m, P, "Stair window", (math.cos(a) * (r + 0.01), y, math.sin(a) * (r + 0.01)), (0.12, 0.9, 0.55), "window", axes=((math.cos(a), 0, math.sin(a)), (0, 1, 0), (-math.sin(a), 0, math.cos(a))))
    # keeper's cottage to the +x side
    cx = 8.0
    box(m, P, "Cottage", (cx, 2.3, 0), (9.0, 4.6, 6.0), "harl_white", bevel=0.04)
    gable_roof(m, P, "Cottage roof", cx, 0, 4.6, 9.0, 6.0, 1.9, "slate_roof", "harl_white")
    for x in (cx - 4.3, cx + 4.3):
        box(m, P, "Chimney", (x, 6.4, 0), (0.9, 2.2, 1.6), "stone_dark")
        for z in (-0.35, 0.35): cyl(m, P, "Chimney pot", (x, 7.5, z), (x, 8.0, z), 0.13, 0.11, "pot", 10)
    win = m.new("Cottage windows", P)
    for (x, z, n) in ((cx - 2.5, 3.0, (0, 0, 1)), (cx + 2.5, 3.0, (0, 0, 1)), (cx - 2.5, -3.0, (0, 0, -1)), (cx + 2.5, -3.0, (0, 0, -1)), (cx, -3.0, (0, 0, -1))):
        zz = z + n[2] * 0.03
        quad(win, (x - 0.55, 1.1, zz), (x + 0.55, 1.1, zz), (x + 0.55, 2.5, zz), (x - 0.55, 2.5, zz), "window", n)
        box(m, P, "Window surround", (x, 1.8, z + n[2] * 0.02), (1.35, 1.65, 0.04), "sign_white")
    box(m, P, "Cottage door", (cx, 1.05, 3.04), (1.0, 2.1, 0.06), "timber_door")
    # low boundary wall
    for (x0, z0, x1, z1) in ((-5, -5, 13.5, -5), (13.5, -5, 13.5, 5), (13.5, 5, 3, 5), (-1, 5, -5, 5), (-5, 5, -5, -5)):
        L = math.hypot(x1 - x0, z1 - z0)
        box(m, P, "Boundary wall", ((x0 + x1) / 2, 0.45, (z0 + z1) / 2), (L if z0 == z1 else 0.45, 1.3, L if x0 == x1 else 0.45), "stone_dark", bevel=0.04)


# ------------------------------------------------------------------------------------------- castle
def lm_castle(m, P, e):
    w, d, h = e["w"], e["d"], e["h"]
    rng = _random.Random(7)
    box(m, P, "Batter", (0, 0.6, 0), (w + 1.0, 3.0, d + 1.0), "stone_castle", bevel=0.05)
    box(m, P, "Keep", (0, h / 2 - 1, 0), (w, h + 2, d), "stone_castle", bevel=0.04)
    box(m, P, "String course", (0, h - 2.2, 0), (w + 0.3, 0.3, d + 0.3), "stone_dark")
    # corbelled parapet with crenellations
    for f in range(4):
        L = face_len(w, d, f); n, a = FACES[f]; half = (d if f in (0, 2) else w) / 2
        for i in range(int(L // 1.0)):
            u = -L / 2 + 0.5 + i * 1.0
            c = add(mul(n, half + 0.2), mul(a, u))
            box(m, P, "Corbel", (c[0], h - 0.45, c[2]), (0.45 if f in (0, 2) else 0.5, 0.6, 0.5 if f in (0, 2) else 0.45), "stone_dark")
        c = mul(n, half + 0.2)
        box(m, P, "Parapet", (c[0], h + 0.45, c[2]), ((L + 0.9) if f in (0, 2) else 0.55, 1.1, 0.55 if f in (0, 2) else (L + 0.9)), "stone_castle")
        for i in range(int(L // 1.6)):
            u = -L / 2 + 0.8 + i * 1.6
            cc = add(mul(n, half + 0.2), mul(a, u))
            box(m, P, "Merlon", (cc[0], h + 1.4, cc[2]), (0.8 if f in (0, 2) else 0.55, 0.8, 0.55 if f in (0, 2) else 0.8), "stone_castle")
    # bartizans on two corners, with slate cones
    for (sx, sz) in ((1, 1), (-1, -1)):
        x, z = sx * (w / 2 + 0.3), sz * (d / 2 + 0.3)
        cyl(m, P, "Bartizan corbel", (x, h - 3.6, z), (x, h - 2.4, z), 0.3, 1.1, "stone_dark", 16)
        cyl(m, P, "Bartizan", (x, h - 2.4, z), (x, h + 1.3, z), 1.1, 1.1, "stone_castle", 16)
        cyl(m, P, "Bartizan roof", (x, h + 1.3, z), (x, h + 3.8, z), 1.35, 0.0, "slate_roof", 16)
        box(m, P, "Bartizan slit", (x + sx * 1.08, h - 0.6, z), (0.06, 0.9, 0.25), "door_dark")
    # caphouse and chimney on the roof
    box(m, P, "Caphouse", (-1.5, h + 1.3, 0), (5.0, 2.6, 4.0), "stone_castle", bevel=0.03)
    gable_roof(m, P, "Caphouse roof", -1.5, 0, h + 2.6, 5.0, 4.0, 1.8, "slate_roof", "stone_castle")
    box(m, P, "Chimney stack", (3.2, h + 2.2, 2.6), (1.0, 4.4, 1.0), "stone_castle")
    box(m, P, "Chimney cope", (3.2, h + 4.45, 2.6), (1.2, 0.15, 1.2), "stone_dark")
    # windows: arrow slits low down, dressed windows above, a doorway
    o = m.new("Openings", P)
    for f in range(4):
        L = face_len(w, d, f)
        for (y, ww, hh, mat) in ((4, 0.25, 1.4, "door_dark"), (9, 0.6, 1.3, "door_dark"), (14, 0.9, 1.6, "window")):
            for u in ((-L / 4, L / 4) if y > 5 else (0,)):
                face_rect(o, w, d, f, u + (0.6 if f == 1 else 0), y, ww, hh, mat, 0.02)
                if y > 5:
                    n, a = FACES[f]; half = (d if f in (0, 2) else w) / 2; c = add(mul(n, half + 0.03), mul(a, u + (0.6 if f == 1 else 0)))
                    box(m, P, "Window surround", (c[0], y, c[2]), ((ww + 0.4) if f in (0, 2) else 0.06, hh + 0.4, 0.06 if f in (0, 2) else (ww + 0.4)), "stone_dark")
    box(m, P, "Door surround", (-3.0, 1.6, d / 2 + 0.05), (1.8, 3.0, 0.12), "stone_dark")
    box(m, P, "Door", (-3.0, 1.3, d / 2 + 0.1), (1.1, 2.4, 0.06), "timber_door")
    # the ruined curtain wall: ragged tops and gaps, each run at its own ground level
    for (lx, lz, L, hh, along, goff) in e.get("walls", []):
        nseg = max(2, int(L // 2.5)); mat = "stone_castle" if rng.random() < 0.5 else "stone_dark"
        for i in range(nseg):
            if 0 < i < nseg - 1 and rng.random() < 0.06: continue            # a breach
            u = -L / 2 + (i + 0.5) * L / nseg
            top = hh * (0.8 + 0.25 * rng.random())
            x, z = (lx, lz + u) if along else (lx + u, lz)
            sx, sz = (1.3, L / nseg + 0.04) if along else (L / nseg + 0.04, 1.3)
            box(m, P, "Curtain wall", (x, goff + (top + 1.6) / 2 - 0.8, z), (sx, top + 1.6, sz), mat)


# --------------------------------------------------------------------------------------- distillery
def lm_distillery(m, P, e):
    w, d, h = e["w"], e["d"], e["h"]
    box(m, P, "Plinth", (0, 0.1, 0), (w + 0.5, 0.8, d + 0.5), "stone_dark")
    box(m, P, "Still house", (0, h / 2, 0), (w, h, d), "harl_white", bevel=0.04)
    rise = min(w, d) * 0.32
    gable_roof(m, P, "Roof", 0, 0, h, w, d, rise, "slate_roof", "harl_white")
    # pagoda-roofed kiln ventilator
    kx = w / 4
    box(m, P, "Kiln", (kx, h + 2.4, 0), (4.5, 4.8, 4.5), "harl_white", bevel=0.03)
    frustum(m, P, "Pagoda skirt", kx, 0, h + 4.8, 3.3, h + 5.5, 2.2, "slate_roof", top=False)
    frustum(m, P, "Pagoda roof", kx, 0, h + 5.5, 2.2, h + 7.5, 0.7, "slate_roof", top=False)
    box(m, P, "Ventilator", (kx, h + 7.9, 0), (1.4, 0.8, 1.4), "verdigris", bevel=0.03)
    frustum(m, P, "Ventilator cap", kx, 0, h + 8.3, 0.95, h + 8.9, 0.0, "verdigris", top=False)
    cyl(m, P, "Finial", (kx, h + 8.9, 0), (kx, h + 9.8, 0), 0.04, 0.03, "copper", 6)
    box(m, P, "Weathervane", (kx + 0.25, h + 9.6, 0), (0.6, 0.25, 0.03), "copper")
    # tall brick chimney stack in one corner
    sx, sz = -w / 2 + 1.1, -d / 2 + 1.1
    box(m, P, "Chimney stack", (sx, (h + 9) / 2, sz), (1.7, h + 9, 1.7), "brick_red", bevel=0.03)
    for y in (h + 8.6, h + 5.0): box(m, P, "Stack band", (sx, y, sz), (1.95, 0.35, 1.95), "conc_grey")
    # windows, cask doors, loading hatch and a copper still showing through the big end window
    win = m.new("Windows", P)
    def skip(f, u, y): return f == 0 and ((y < 4 and abs(u) < 3) or (y > 4 and abs(u + w / 4) < 1.8))   # cask doors, loading hatch
    window_rows(win, w, d, (0, 2), [2.4, 6.0], 1.1, 1.4, 3.0, margin=2.5, skip=skip)
    window_rows(win, w, d, (1,), [2.4, 6.0], 1.1, 1.4, 3.0, margin=2.0)
    face_rect(win, w, d, 3, 0, 4.5, 3.0, 5.0, "window", 0.03)
    for f in (0, 2):
        n, a = FACES[f]; half = d / 2
        for i in range(int((w - 5) // 3.0)):
            u = -w / 2 + 2.5 + (i + 0.5) * (w - 5) / int((w - 5) // 3.0)
            for y in (2.4, 6.0):
                if skip(f, u, y): continue
                c = add(mul(n, half + 0.08), mul(a, u))
                box(m, P, "Sill", (c[0], y - 0.78, c[2]), (1.3, 0.12, 0.16), "stone_dark")
    box(m, P, "Cask doors", (0, 1.6, d / 2 + 0.06), (2.8, 3.2, 0.1), "timber_door")
    box(m, P, "Door lintel", (0, 3.4, d / 2 + 0.08), (3.4, 0.35, 0.16), "stone_dark")
    box(m, P, "Loading hatch", (-w / 4, 6.2, d / 2 + 0.06), (1.6, 1.8, 0.1), "timber_door")
    cyl(m, P, "Hoist beam", (-w / 4, 7.6, d / 2), (-w / 4, 7.6, d / 2 + 1.2), 0.1, 0.1, "door_dark", 8)
    cyl(m, P, "Copper still", (-w / 2 + 3.0, 0.3, 0), (-w / 2 + 3.0, 4.2, 0), 1.3, 0.5, "copper", 20)
    ellipsoid(m, P, "Still pot", (-w / 2 + 3.0, 1.6, 0), (1.5, 1.4, 1.5), "copper", 20, 10)


# ----------------------------------------------------------------------------------------- platform
def lm_platform(m, P, e):
    w, d, h = e["w"], e["d"], e["h"]            # origin at the base level (-20 m); sea level at y = 20
    dx, dz, dh = e["deck"]
    top = h                                      # main deck top
    sea = 20.0
    legs = [(-16, -16), (16, -16), (16, 16), (-16, 16)]
    for (x, z) in legs:
        cyl(m, P, "Jacket leg", (x * 1.15, -20, z * 1.15), (x, top - 12, z), 1.9, 1.65, "steel_yellow", 18)
        cyl(m, P, "Splash zone", (x * 1.03, sea - 1.5, z * 1.03), (x * 1.01, sea + 2.5, z * 1.01), 1.78, 1.74, "steel_dark", 18, caps=False)
    def leg_at(i, y):
        x, z = legs[i]; t = (y + 20) / (top - 12 + 20); return (x * (1.15 - 0.15 * t), y, z * (1.15 - 0.15 * t))
    for i in range(4):
        j = (i + 1) % 4
        for (ya, yb) in ((-10, 8), (8, 26)):
            tube(m, P, "X brace", [leg_at(i, ya), leg_at(j, yb)], 0.45, "steel_yellow", 10, steps=1)
            tube(m, P, "X brace", [leg_at(j, ya), leg_at(i, yb)], 0.45, "steel_yellow", 10, steps=1)
        for y in (8, 26): tube(m, P, "Horizontal", [leg_at(i, y), leg_at(j, y)], 0.5, "steel_yellow", 10, steps=1)
    # decks and modules
    box(m, P, "Cellar deck", (0, top - 9.5, 0), (w - 3, 1.0, d - 3), "deck_grey")
    box(m, P, "Module deck", (0, top - 5, 0), (w, 8, d), "module_grey", bevel=0.05)
    box(m, P, "Main deck", (0, top - 0.5, 0), (w + 1, 1.0, d + 1), "deck_grey")
    railing(m, P, [(-w / 2 - 0.4, top, -d / 2 - 0.4), (w / 2 + 0.4, top, -d / 2 - 0.4), (w / 2 + 0.4, top, d / 2 + 0.4), (-w / 2 + 2, top, d / 2 + 0.4)], 1.1, "steel_yellow", 2.0, closed=False)
    win = m.new("Windows", P)
    for f in range(4): window_rows(win, w, d, (f,), [top - 6.5, top - 3.0], 2.0, 1.0, 5.0, margin=3.0, mat="door_dark")
    box(m, P, "Process module", (6, top + 4.5, -8), (18, 9, 14), "module_orange", bevel=0.05)
    o = m.new("Louvres", P)
    for i in range(5): quad(o, (-2 + i * 3.6, top + 2, -15.04), (-0.5 + i * 3.6, top + 2, -15.04), (-0.5 + i * 3.6, top + 7, -15.04), (-2 + i * 3.6, top + 7, -15.04), "door_dark", (0, 0, -1))
    for (x, z) in ((2, -10), (9, -6)): cyl(m, P, "Vessel", (x, top + 9, z), (x, top + 12.5, z), 1.4, 1.4, "rail_white", 18)
    box(m, P, "Accommodation", (-8, top + 3.5, 9), (14, 7, 12), "module_white", bevel=0.05)
    window_rows(win, 14, 12, (0, 1, 2, 3), [top + 2.0 - top + 0.0], 0.0, 0.0, 99)  # (no-op: keeps the call shape)
    for (y,) in ((top + 1.9,), (top + 5.0,)):
        for i in range(5):
            x = -14 + i * 2.8
            quad(win, (x - 0.8, y - 0.5, 15.04), (x + 0.8, y - 0.5, 15.04), (x + 0.8, y + 0.5, 15.04), (x - 0.8, y + 0.5, 15.04), "window", (0, 0, 1))
        for i in range(4):
            z = 4.5 + i * 2.8
            quad(win, (-15.04, y - 0.5, z - 0.8), (-15.04, y - 0.5, z + 0.8), (-15.04, y + 0.5, z + 0.8), (-15.04, y + 0.5, z - 0.8), "window", (-1, 0, 0))
    railing(m, P, [(-15, top + 7, 3), (-1, top + 7, 3), (-1, top + 7, 15), (-15, top + 7, 15)], 1.0, "rail_white", 2.0)
    # lifeboats on davits off the +z edge
    for x in (-13, -5):
        ellipsoid(m, P, "Lifeboat", (x, top - 2.5, d / 2 + 2.2), (3.4, 1.3, 1.25), "lifeboat", 20, 10)
        box(m, P, "Lifeboat canopy", (x, top - 1.6, d / 2 + 2.2), (4.6, 0.6, 1.6), "lifeboat", bevel=0.25, segs=3)
        for sx in (-1.8, 1.8): tube(m, P, "Davit", [(x + sx, top, d / 2), (x + sx, top + 1.5, d / 2 + 1.2), (x + sx, top + 0.8, d / 2 + 2.2)], 0.12, "rail_white", 8)
    # pedestal crane
    cx, cz = -18, -15
    cyl(m, P, "Crane pedestal", (cx, top, cz), (cx, top + 7, cz), 1.4, 1.2, "steel_yellow", 18)
    box(m, P, "Crane cab", (cx + 1.2, top + 8.2, cz), (3.2, 2.4, 2.4), "steel_yellow", bevel=0.08)
    bx, by, bz = cx + 18 * 0.8, top + 22, cz + 18 * 0.4
    for (ox, oz) in ((0.5, 0.5), (0.5, -0.5), (-0.5, 0.5), (-0.5, -0.5)):
        tube(m, P, "Boom chord", [(cx + ox * 1.2, top + 8.5, cz + oz * 1.2), (bx + ox * 0.4, by, bz + oz * 0.4)], 0.12, "steel_yellow", 6, steps=1)
    for t in [i / 8 for i in range(1, 8)]:
        a = (cx + (bx - cx) * t, top + 8.5 + (by - top - 8.5) * t, cz + (bz - cz) * t)
        tube(m, P, "Boom lacing", [(a[0] + 0.5, a[1] + 0.5, a[2] + 0.5), (a[0] - 0.5, a[1] - 0.5, a[2] - 0.5)], 0.06, "steel_yellow", 5, steps=1)
    tube(m, P, "Hoist rope", [(bx, by, bz), (bx, top + 6, bz)], 0.03, "door_dark", 5, steps=1)
    box(m, P, "Hook block", (bx, top + 5.6, bz), (0.5, 0.8, 0.5), "steel_yellow")
    # derrick: four legs, girts and diagonal bracing
    ox, oz = 14, 10
    box(m, P, "Drill floor", (ox, top + 1.0, oz), (8, 2, 8), "module_grey", bevel=0.05)
    def dleg(sx, sz, y): t = (y - top - 2) / 58; s = 2.5 * (1 - t) + 0.6 * t; return (ox + sx * s, y, oz + sz * s)
    corners = ((-1, -1), (1, -1), (1, 1), (-1, 1))
    for (sx, sz) in corners: tube(m, P, "Derrick leg", [dleg(sx, sz, top + 2), dleg(sx, sz, top + 60)], 0.22, "rail_white", 6, steps=1)
    for k in range(10):
        y0, y1 = top + 2 + k * 5.8, top + 2 + (k + 1) * 5.8
        for i in range(4):
            a, b = corners[i], corners[(i + 1) % 4]
            tube(m, P, "Girt", [dleg(*a, y1), dleg(*b, y1)], 0.1, "rail_white", 5, steps=1, caps=False)
            tube(m, P, "Brace", [dleg(*a, y0), dleg(*b, y1)], 0.07, "rail_white", 5, steps=1, caps=False)
    box(m, P, "Crown block", (ox, top + 60.6, oz), (1.8, 1.2, 1.8), "steel_yellow")
    box(m, P, "Monkey board", (ox + 1.3, top + 30, oz), (1.2, 0.15, 2.6), "deck_grey")
    # flare boom ending where the game draws the flame
    a0, a1 = (18, top, -18), (52, top + 30, -26)
    for (ox2, oy2) in ((0.6, 0.0), (-0.6, 0.0), (0.0, 0.9)):
        tube(m, P, "Flare chord", [(a0[0], a0[1] + oy2, a0[2] + ox2), (a1[0], a1[1] + oy2, a1[2] + ox2)], 0.14, "rail_white", 6, steps=1)
    for t in [i / 10 for i in range(11)]:
        p = (a0[0] + (a1[0] - a0[0]) * t, a0[1] + (a1[1] - a0[1]) * t, a0[2] + (a1[2] - a0[2]) * t)
        tube(m, P, "Flare lacing", [(p[0], p[1], p[2] - 0.6), (p[0], p[1] + 0.9, p[2]), (p[0], p[1], p[2] + 0.6)], 0.05, "rail_white", 5, steps=1)
    cyl(m, P, "Flare tip", (a1[0], a1[1], a1[2]), (a1[0], a1[1] + 2.5, a1[2]), 0.5, 0.45, "door_dark", 12)
    # helideck on its support frame, with a perimeter net and deck lights
    box(m, P, "Helideck", (dx, dh - 0.6, dz), (24, 1.0, 24), "deck_green")
    box(m, P, "Helideck edge", (dx, dh - 1.2, dz), (24.4, 0.5, 24.4), "steel_yellow")
    net = m.new("Safety net", P)
    for (nx, nz) in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        ax = (-nz, nx)
        def Pn(r, s, y): return (dx + nx * r + ax[0] * s, y, dz + nz * r + ax[1] * s)
        quad(net, Pn(12.1, -12.1, dh - 0.3), Pn(12.1, 12.1, dh - 0.3), Pn(13.6, 13.6, dh - 0.05), Pn(13.6, -13.6, dh - 0.05), "net", (nx, 1, nz))
    for i in range(16):
        a = i / 16 * 2 * math.pi
        box(m, P, "Deck light", (dx + math.cos(a) * 11.6, dh - 0.02, dz + math.sin(a) * 11.6), (0.35, 0.12, 0.35), "deck_light")
    for sz in (-9, 9):
        tube(m, P, "Deck support", [(dx - 10, dh - 1.4, dz + sz), (-w / 2 + 1, top - 8, dz + sz)], 0.45, "steel_yellow", 10, steps=1)
        tube(m, P, "Deck support", [(dx + 10, dh - 1.4, dz + sz), (-w / 2 + 2, top, dz + sz)], 0.45, "steel_yellow", 10, steps=1)
        cyl(m, P, "Deck column", (-w / 2 + 1.5, top, dz + sz), (-w / 2 + 1.5, dh - 1.4, dz + sz), 0.5, 0.5, "steel_yellow", 12)
    box(m, P, "Deck girder", (dx + 6, dh - 1.9, dz), (16, 1.0, 20), "steel_yellow")
    box(m, P, "Stair tower", (-w / 2 + 3, (top + dh) / 2, dz + 13), (3, dh - top, 3), "module_grey", bevel=0.05)


LANDMARK_BUILDERS = {"hospital": lm_hospital, "hangar": lm_hangar, "tower": lm_tower, "lighthouse": lm_lighthouse,
                     "castle": lm_castle, "distillery": lm_distillery, "platform": lm_platform}


def build_landmarks():
    m = Model(); layout = {}; shots = []; seen = set(); x = 0.0
    for e in LANDMARKS:
        before = len(m.objs)
        LANDMARK_BUILDERS[e["kind"]](m, e["key"], e)
        span = max(e["w"], e["d"]) + (110 if e["kind"] == "platform" else 30)
        layout[e["key"]] = (x + span / 2, 0.0, 0.0); cx = x + span / 2; x += span
        if e["kind"] not in seen:                 # one preview render per kind
            seen.add(e["kind"]); size = max(e["w"], e["d"], e["h"])
            k = 1.9 if e["kind"] != "platform" else 1.5
            shots.append((e["kind"], (cx + size * 0.9 * k, size * 0.55 * k, size * 1.25 * k), (cx, e["h"] * 0.4, 0), 35))
        assert len(m.objs) > before
    meta = {"format": "RCM1", "model": "Landmarks", "library": True, "refRotorDiameter": 1.0, "generator": VERSION,
            "points": {}, "controls": [], "layout": layout, "shots": shots, "aoDistance": 2.5,
            "instances": [e["key"] for e in LANDMARKS]}
    return m, meta


# camera views for the preview renders: (name, camera, target, lens) in game coordinates
SHOTS = {
    "kestrel": [("front", (5.2, 2.9, -7.0), (0, 1.2, -0.3), 50), ("side", (-7.8, 1.6, 1.6), (0, 1.3, 1.6), 40), ("cockpit", (0.3, 1.68, -0.42), (0.05, 1.05, -1.6), 18)],
    "nightjar": [("front", (7.0, 3.0, -9.5), (0, 1.5, 0.2), 45), ("side", (-12.5, 2.2, 1.6), (0, 1.7, 1.6), 35), ("rear", (6.0, 2.4, 9.5), (0, 1.5, 1.0), 40), ("cockpit", (0.42, 1.66, -2.18), (0.1, 1.25, -3.3), 18)],
    "twin": [("front", (9.5, 4.2, -13.0), (0, 1.8, -0.2), 50), ("side", (-17.0, 2.6, 1.6), (0, 2.0, 1.6), 35), ("cockpit", (0.45, 2.08, -2.8), (0.15, 1.6, -4.4), 18)],
}
# ============================================================================ vehicles (model "vehicles")
# One part per kind of road vehicle, named as the game names them (src/world/places.ts VehicleKind). Local
# frame: x right, y up, z forward (the front of the vehicle at +z), wheels on y = 0. Body paint uses livery
# slot 1, so each car takes the colour the game gives it. Lamp positions go into the meta for the game's
# night lights.
MATS.update({
    "v_trim":     (0.06, 0.06, 0.065, 0.25, 0, False),
    "v_tyre":     (0.03, 0.03, 0.033, 0.10, 0, False),
    "v_rim":      (0.66, 0.67, 0.69, 0.75, 0, False),
    "v_glass":    (0.035, 0.045, 0.055, 0.95, 0, False),
    "v_head":     (0.86, 0.86, 0.84, 0.85, 0, False),   # lamps are not self-lit: the game adds glows when they are on
    "v_tail":     (0.55, 0.04, 0.03, 0.7, 0, False),
    "v_amber":    (0.85, 0.45, 0.05, 0.7, 0, False),
    "v_blue":     (0.10, 0.20, 0.75, 0.7, 0, False),
    "v_plate_f":  (0.92, 0.92, 0.90, 0.40, 0, False),
    "v_plate_r":  (0.95, 0.80, 0.08, 0.40, 0, False),
    "v_white":    (0.93, 0.93, 0.92, 0.70, 0, False),
    "v_yellow":   (0.95, 0.85, 0.10, 0.60, 0, False),
    "v_green":    (0.10, 0.45, 0.20, 0.50, 0, False),
    "v_navy":     (0.08, 0.15, 0.55, 0.60, 0, False),
    "v_red":      (0.80, 0.06, 0.05, 0.60, 0, False),
    "v_steel":    (0.55, 0.56, 0.58, 0.60, 0, False),
})

# side-view top line (z, y) from the back to the front, and the rest of each body
CARS = {
    "hatch":  dict(L=4.05, W=1.78, belt=0.92, hl=0.78, wr=0.31, wb=2.5, top=[(-2.025, 0.95), (-1.98, 1.25), (-1.75, 1.44), (-1.2, 1.46), (0.15, 1.45), (0.95, 0.95), (1.4, 0.86), (2.025, 0.7)], B=-0.45),
    "saloon": dict(L=4.7, W=1.82, belt=0.95, hl=0.8, wr=0.32, wb=2.75, top=[(-2.35, 0.95), (-2.2, 1.0), (-1.4, 1.02), (-0.95, 1.42), (0.35, 1.44), (1.1, 0.98), (1.6, 0.9), (2.35, 0.72)], B=-0.25),
    "estate": dict(L=4.75, W=1.82, belt=0.95, hl=0.8, wr=0.32, wb=2.8, top=[(-2.375, 0.98), (-2.32, 1.38), (-2.1, 1.48), (0.35, 1.47), (1.1, 0.98), (1.6, 0.9), (2.375, 0.72)], B=-0.2),
    "suv":    dict(L=4.6, W=1.9, belt=1.12, hl=0.98, wr=0.37, wb=2.75, top=[(-2.3, 1.15), (-2.25, 1.6), (-2.0, 1.72), (0.45, 1.72), (1.05, 1.18), (1.5, 1.1), (2.3, 0.92)], B=-0.3),
    "police": dict(L=4.8, W=1.84, belt=0.96, hl=0.8, wr=0.33, wb=2.85, top=[(-2.4, 0.98), (-2.34, 1.4), (-2.12, 1.5), (0.35, 1.5), (1.1, 0.99), (1.6, 0.9), (2.4, 0.72)], B=-0.2, fixed="v_white", checks=(0.4, 0.9, -2.05, 2.0, "v_navy", "v_yellow")),
    "pickup": dict(L=5.3, W=1.86, belt=1.08, hl=0.95, wr=0.4, wb=3.2, top=[(-2.65, 1.08), (-0.4, 1.08), (-0.32, 1.82), (0.6, 1.83), (1.35, 1.12), (1.8, 1.06), (2.65, 0.92)], B=0.1, bed=(-2.6, -0.4)),
    "van":    dict(L=5.4, W=2.0, belt=1.15, hl=0.85, wr=0.36, wb=3.3, top=[(-2.7, 2.3), (1.0, 2.3), (1.55, 1.3), (2.0, 1.12), (2.7, 0.95)], B=1.1, cabglass=(0.85, 2.7)),
    "ambulance": dict(L=6.0, W=2.1, belt=1.15, hl=0.85, wr=0.37, wb=3.6, top=[(-3.0, 2.75), (1.25, 2.75), (1.4, 1.95), (1.85, 1.3), (2.3, 1.12), (3.0, 0.95)], B=1.4, cabglass=(1.15, 3.0), fixed="v_yellow", checks=(0.45, 1.0, -2.85, 1.1, "v_green", "v_yellow")),
    "rover":  dict(L=4.6, W=1.84, belt=1.2, hl=0.95, wr=0.4, wb=2.8, top=[(-2.3, 1.95), (0.55, 1.95), (0.75, 1.92), (1.3, 1.22), (2.3, 1.12)], B=0.0, fixed="v_white", boxy=True, band=(0.9, 1.08, -2.2, 1.4, "v_red")),
}


def car(m, kind):
    S = CARS[kind]; P = kind
    L, W = S["L"], S["W"]; hw = W / 2; belt = S["belt"]; y0 = 0.26
    ztop = [p[0] for p in S["top"]]; ytop_f = pchip(ztop, [p[1] for p in S["top"]])
    boxy = S.get("boxy", False)
    def ytop(z): return ytop_f(max(-L / 2, min(L / 2, z)))
    def halfw(z):
        e = max(0.0, abs(z) - (L / 2 - (0.35 if boxy else 0.65))) / (0.35 if boxy else 0.65)
        return hw * (0.8 + 0.2 * math.sqrt(max(0.0, 1 - e * e)))
    NS = 16   # points per half section
    def section(z):
        w = halfw(z); yt = ytop(z)
        g = smooth(belt + 0.02, belt + 0.3, yt)         # 0: no glasshouse here (bonnet, boot), 1: full height
        wg = w - (0.06 if boxy else 0.22) * g            # glasshouse narrows towards the roof
        pts = [(0.0, y0 - 0.02), (w - 0.1, y0), (w, y0 + 0.14), (w + 0.01, (y0 + belt) / 2), (w, belt - 0.08), (w - 0.02, belt)]
        topy = max(yt, belt + 0.01)
        pts += [(w - 0.05, lerp(belt + 0.01, belt + 0.03, g)), (lerp(w - 0.1, wg, g), lerp(belt + 0.015, topy - 0.12, g)),
                (lerp(w - 0.16, wg - 0.03, g), lerp(belt + 0.02, topy - 0.05, g)), (lerp(w - 0.25, wg - 0.1, g), topy - 0.01),
                (w * 0.55, topy + 0.01), (w * 0.25, topy + 0.02), (0.0, topy + 0.02)]
        while len(pts) < NS: pts.append(pts[-1])
        right = [(x, y) for (x, y) in pts]
        left = [(-x, y) for (x, y) in reversed(pts[1:-1])]
        return right + left
    # rows: a rounded end at each bumper (a superellipse in plan, so the corners stay square-ish), the body between
    nE, nM = 6, 40; nu = nM + 2 * nE; r = 0.14 if boxy else 0.2
    zb, zf = -L / 2 + r, L / 2 - r
    def row(u):
        i = u * nu
        if i < nE: th = (1 - i / nE) * math.pi / 2; return zb, zb - r * math.sin(th) ** 0.5, math.cos(th) ** 0.5
        if i > nu - nE: th = (i - (nu - nE)) / nE * math.pi / 2; return zf, zf + r * math.sin(th) ** 0.5, math.cos(th) ** 0.5
        return (lerp(zb, zf, (i - nE) / nM),) * 2 + (1.0,)
    def Pf(u, v):
        zs, z, xf = row(u)
        s = section(zs); k = min(len(s) - 1, int(round(v * len(s))) % len(s))
        return (s[k][0] * xf, s[k][1], z)
    nv = len(section(0.0))
    o = m.new(kind + " body", P, smooth=True, sharp=50.0, subsurf=1)
    fixed = S.get("fixed")
    zw = S["wb"] / 2; wr = S["wr"]
    cg = S.get("cabglass")
    def paint(c, n, u, v):
        x, y, z = c
        if y < 0.42 and abs(n[1]) < 0.9: return "v_trim"
        for wz in (zw, -zw):                                   # wheel arches
            if abs(x) > halfw(z) - 0.06 and math.hypot(y - wr, z - wz) < wr + 0.09: return "v_tyre"
        yt = ytop(z)
        if belt + 0.03 < y and (cg is None or cg[0] < z < cg[1]):
            side = abs(n[0]) > 0.75 and y < yt - 0.06
            screen = abs(n[2]) > 0.4 and n[1] < 0.88 and abs(n[0]) < 0.6 and y > belt + 0.06
            pillar = side and abs(z - S["B"]) < 0.06
            if (side or screen) and not pillar: return "v_glass"
            if pillar: return "v_trim"
        if abs(n[0]) > 0.6 and S.get("checks"):
            y0c, y1c, z0c, z1c, c1, c2 = S["checks"]
            if y0c < y < y1c and z0c < z < z1c:
                cell = (z1c - z0c) / max(2, int(round((z1c - z0c) / 0.55)))
                return c1 if (int((z - z0c) / cell) + (1 if y > (y0c + y1c) / 2 else 0)) % 2 else c2
        if abs(n[0]) > 0.6 and S.get("band") and S["band"][0] < y < S["band"][1] and S["band"][2] < z < S["band"][3]: return S["band"][4]
        # lamps and grille on the ends
        if n[2] > 0.5 and z > L / 2 - 0.25:
            if S["hl"] - 0.09 < y < S["hl"] + 0.06 and abs(x) > halfw(z) * 0.5: return "v_head"
            if 0.45 < y < S["hl"] - 0.03 and abs(x) < halfw(z) * 0.42: return "v_trim"
        if n[2] < -0.5 and z < -L / 2 + 0.25:
            if S["hl"] + 0.05 < y < S["hl"] + 0.2 and abs(x) > halfw(z) * 0.55: return "v_tail"
        return fixed or "paint"
    surface(o, nu, nv, Pf, paint)
    # wheels: tyre, rim with spokes, hub
    for sx in (-1, 1):
        for wz in (zw, -zw):
            x = sx * (hw - 0.13)
            cyl(m, P, "Tyre", (x - sx * 0.12, wr, wz), (x + sx * 0.12, wr, wz), wr, wr, "v_tyre", 24, smooth=True)
            cyl(m, P, "Rim", (x + sx * 0.115, wr, wz), (x + sx * 0.13, wr, wz), wr * 0.64, wr * 0.6, "v_rim", 20)
            for k in range(5):
                a = k / 5 * 2 * math.pi
                box(m, P, "Spoke", (x + sx * 0.132, wr + math.sin(a) * wr * 0.33, wz + math.cos(a) * wr * 0.33), (0.02, 0.07, wr * 0.5), "v_steel", axes=((1, 0, 0), (0, math.cos(a), -math.sin(a)), (0, math.sin(a), math.cos(a))))
            cyl(m, P, "Hub", (x + sx * 0.13, wr, wz), (x + sx * 0.15, wr, wz), wr * 0.16, wr * 0.13, "v_rim", 10)
    # mirrors, plates, door handles
    zm = None
    for zz in [i / 40 * L - L / 2 for i in range(41)]:
        if ytop(zz) > belt + 0.25: zm = zz
    zm = (zm if zm is not None else L / 4) - 0.15
    for sx in (-1, 1):
        box(m, P, "Mirror", (sx * (halfw(zm) + 0.12), belt + 0.08, zm), (0.2, 0.12, 0.1), fixed or "paint", bevel=0.02)
        for dz in (0.35, S["B"] - 0.4):
            box(m, P, "Handle", (sx * (halfw(dz) + 0.03), belt - 0.08, dz), (0.03, 0.03, 0.14), "v_trim")
    box(m, P, "Plate front", (0, 0.5, L / 2 + 0.03), (0.52, 0.11, 0.03), "v_plate_f")
    box(m, P, "Plate rear", (0, S["hl"] - 0.1, -L / 2 - 0.03), (0.52, 0.11, 0.03), "v_plate_r")
    info = {"len": L, "width": W, "height": max(p[1] for p in S["top"]),
            "heads": [[-hw * 0.7, S["hl"], L / 2], [hw * 0.7, S["hl"], L / 2]], "tails": [[-hw * 0.75, S["hl"] + 0.12, -L / 2], [hw * 0.75, S["hl"] + 0.12, -L / 2]], "beacons": []}
    if "bed" in S:     # pickup load bed: a dark floor and the inner sides
        z0, z1 = S["bed"]
        box(m, P, "Bed floor", (0, belt - 0.15, (z0 + z1) / 2), (W - 0.2, 0.04, z1 - z0), "v_trim")
    if kind == "police":
        light_bar(m, P, 1.52, -0.55, info)
    if kind == "ambulance":
        light_bar(m, P, 2.77, 1.15, info)
        for sx in (-1, 1):
            box(m, P, "Rear beacon", (sx * (hw - 0.15), 2.74, -L / 2 + 0.12), (0.2, 0.12, 0.12), "v_blue")
            info["beacons"].append([sx * (hw - 0.15), 2.82, -L / 2 + 0.12])
        box(m, P, "Rear doors seam", (0, 1.5, -L / 2 - 0.03), (0.03, 2.2, 0.03), "v_trim")
    if kind == "van":
        box(m, P, "Rear doors seam", (0, 1.4, -L / 2 - 0.03), (0.03, 1.9, 0.03), "v_trim")
        for sx in (-1, 1): box(m, P, "Side door rail", (sx * (hw + 0.035), 2.0, -0.4), (0.04, 0.04, 2.4), "v_trim")
    if kind == "rover":
        cyl(m, P, "Spare wheel", (0, 1.05, -L / 2 - 0.02), (0, 1.05, -L / 2 - 0.28), 0.38, 0.38, "v_tyre", 18)
        light_bar(m, P, 1.97, 0.3, info)
    return info


def light_bar(m, P, y, z, info):
    box(m, P, "Light bar", (0, y + 0.06, z), (1.1, 0.1, 0.25), "v_trim", bevel=0.03)
    for sx in (-1, 1):
        box(m, P, "Beacon", (sx * 0.3, y + 0.13, z), (0.42, 0.08, 0.2), "v_blue", bevel=0.02)
        info["beacons"].append([sx * 0.3, y + 0.17, z])


def fire_engine(m):
    P = "fireEngine"; L, W = 8.0, 2.45; hw = W / 2
    red, white = "v_red", "v_white"
    # cab (crew cab with a sloping front) and the body with roller-shutter lockers
    surface_box = lambda name, c, s, mat, bev=0.08: box(m, P, name, c, s, mat, bevel=bev)
    surface_box("Cab", (0, 1.65, 2.6), (W, 2.3, 2.6), red)
    surface_box("Windscreen", (0, 2.2, 3.92), (W - 0.25, 0.95, 0.04), "v_glass", 0.0)
    for sx in (-1, 1):
        surface_box("Cab window", (sx * (hw + 0.015), 2.2, 2.9), (0.03, 0.8, 1.6), "v_glass", 0.0)
        surface_box("Crew window", (sx * (hw + 0.015), 2.2, 1.7), (0.03, 0.8, 0.8), "v_glass", 0.0)
    surface_box("Body", (0, 1.75, -1.2), (W, 2.5, 5.2), red)
    for sx in (-1, 1):
        for i in range(3):
            surface_box("Locker", (sx * (hw + 0.02), 1.75, -3.1 + i * 1.75), (0.04, 1.9, 1.55), "v_steel", 0.0)
            for k in range(12): box(m, P, "Shutter rib", (sx * (hw + 0.055), 0.9 + k * 0.16, -3.1 + i * 1.75), (0.03, 0.025, 1.5), "v_trim")
        surface_box("Stripe", (sx * (hw + 0.02), 0.55, 0), (0.04, 0.14, 7.9), "v_yellow", 0.0)
    surface_box("Bumper", (0, 0.45, 3.95), (W, 0.3, 0.2), "v_trim", 0.04)
    # ladder on the roof
    for sx in (-1, 1): box(m, P, "Ladder rail", (sx * 0.45, 3.15, -0.8), (0.06, 0.1, 6.2), "v_steel")
    for k in range(24): box(m, P, "Rung", (0, 3.15, -3.8 + k * 0.26), (0.9, 0.04, 0.04), "v_steel")
    for sx in (-1, 1):
        for wz in (2.4, -2.2):
            x = sx * (hw - 0.2)
            cyl(m, P, "Tyre", (x - sx * 0.17, 0.5, wz), (x + sx * 0.17, 0.5, wz), 0.5, 0.5, "v_tyre", 24)
            cyl(m, P, "Rim", (x + sx * 0.165, 0.5, wz), (x + sx * 0.18, 0.5, wz), 0.3, 0.28, "v_rim", 16)
    info = {"len": L, "width": W, "height": 3.25, "heads": [[-0.9, 0.85, 3.96], [0.9, 0.85, 3.96]], "tails": [[-1.0, 1.0, -3.81], [1.0, 1.0, -3.81]], "beacons": []}
    for sx in (-1, 1): box(m, P, "Headlamp", (sx * 0.9, 0.85, 3.93), (0.3, 0.15, 0.03), "v_head")
    light_bar(m, P, 2.82, 3.4, info)
    for sx in (-1, 1): box(m, P, "Rear beacon", (sx * 1.05, 3.05, -3.75), (0.18, 0.14, 0.14), "v_blue"); info["beacons"].append([sx * 1.05, 3.14, -3.75])
    return info


VEHICLE_KINDS = list(CARS.keys()) + ["fireEngine"]


def build_vehicles():
    m = Model(); layout = {}; info = {}; shots = []
    for i, k in enumerate(VEHICLE_KINDS):
        info[k] = fire_engine(m) if k == "fireEngine" else car(m, k)
        layout[k] = (((i % 5) - 2) * 7.0, 0.0, (i // 5) * 10.0)
    shots = [("front", (14.0, 6.0, 26.0), (0.0, 1.0, 5.0), 40), ("side", (-24.0, 4.0, 4.0), (0.0, 1.0, 5.0), 45)]
    meta = {"format": "RCM1", "model": "Vehicles", "library": True, "refRotorDiameter": 1.0, "generator": VERSION,
            "points": {}, "controls": [], "layout": layout, "shots": shots, "aoDistance": 0.8, "vehicles": info}
    return m, meta


# ============================================================================ trees (model "trees")
# Two versions of each species the game plants: one part each, named <species>_<n>, origin at the foot of
# the trunk, built at a typical height (meta "trees" gives it) for the game to scale. Foliage uses livery
# slot 1, so the game can vary each tree's shade and autumn colour. The game also shades the crown
# smoothly (lit tops and outer leaves, dark undersides and centre) on top of any baked shading.
# Kept light (a few hundred triangles a tree), because the game draws thousands of them.
MATS.update({
    "bark":      (0.25, 0.20, 0.155, 0.03, 0, False),
    "bark_grey": (0.42, 0.40, 0.37, 0.03, 0, False),
    "bark_birch": (0.80, 0.78, 0.74, 0.05, 0, False),
    "bark_pine": (0.36, 0.23, 0.15, 0.03, 0, False),
    "leaf_dark":  (0.075, 0.12, 0.05, 0.03, 1, False),
    "leaf_mid":   (0.13, 0.21, 0.075, 0.03, 1, False),
    "leaf_light": (0.21, 0.30, 0.10, 0.04, 1, False),
    "leaf":       (0.15, 0.215, 0.09, 0.03, 1, False),
})

class Rng:
    def __init__(self, seed): self.s = (seed * 2654435761 + 12345) & 0xFFFFFFFF
    def __call__(self):
        self.s = (self.s * 1664525 + 1013904223) & 0xFFFFFFFF
        return self.s / 4294967296.0


def clump(m, P, c, r, rng, crown_c, crown_r, nu=7, nv=5, lumps=0.22):
    """A lumpy foliage clump: an ellipsoid with its rings pushed in and out, shaded by where it sits."""
    o = m.new("Foliage", P, smooth=True, sharp=80.0)
    ph = [rng() * 6.28 for _ in range(3)]
    def bump(th, a): return 1 + lumps * (0.5 * math.sin(3 * a + ph[0]) * math.sin(2 * th + ph[1]) + 0.5 * math.sin(5 * a + ph[2] + th * 3))
    top = o.vert((c[0], c[1] + r[1] * bump(0, 0), c[2])); bot = o.vert((c[0], c[1] - r[1] * 0.75, c[2]))
    rings = []
    for i in range(1, nv):
        th = i / nv * math.pi; sq = 0.75 if th > math.pi / 2 else 1.0     # flatter underneath
        rings.append([o.vert((c[0] + math.sin(th) * math.cos(j / nu * 6.283 + i * 0.4) * r[0] * bump(th, j / nu * 6.283), c[1] + math.cos(th) * r[1] * sq * bump(th, j * 1.7),
                              c[2] + math.sin(th) * math.sin(j / nu * 6.283 + i * 0.4) * r[2] * bump(th, j / nu * 6.283))) for j in range(nu)])
    def shade(ids):
        q = (0.0, 0.0, 0.0)
        for i in ids: q = add(q, o.v[i])
        return "leaf", mul(q, 1.0 / len(ids))
    for j in range(nu):
        j2 = (j + 1) % nu
        for (pole, ring, up) in ((top, rings[0], True), (bot, rings[-1], False)):
            ids = (pole, ring[j], ring[j2]); mat, q = shade(ids)
            P3 = [o.v[i] for i in ids]
            if dot(cross(sub(P3[1], P3[0]), sub(P3[2], P3[0])), sub(q, c)) >= 0: o.face(ids, mat)
            else: o.face((pole, ring[j2], ring[j]), mat)
        for i in range(len(rings) - 1):
            ids = (rings[i][j], rings[i + 1][j], rings[i + 1][j2], rings[i][j2]); mat, q = shade(ids)
            oriented_quad(o, ids, sub(q, c), mat)


def broadleaf(m, P, rng, H, trunk_f, crown_w, crown_h, n_clumps, limbs, bark, clump_r, droop=0.0, lumps=0.22):
    """A broadleaf tree: trunk, limbs reaching into the crown, and foliage clumps spread over the crown's shell."""
    yb = H * trunk_f                                   # where the trunk divides
    lean = ((rng() - 0.5) * 0.3, (rng() - 0.5) * 0.3)
    top = (lean[0], yb, lean[1])
    tube(m, P, "Trunk", [(0, -0.4, 0), (lean[0] * 0.3, yb * 0.5, lean[1] * 0.3), top], lambda t: H * (0.034 - 0.012 * t), bark, seg=6, steps=2, caps=False)
    cc = (lean[0], yb + crown_h * 0.5, lean[1]); cr = (crown_w / 2, crown_h / 2, crown_w / 2)
    tips = []
    for k in range(limbs):
        a = k / limbs * 6.283 + rng() * 0.8; up = 0.55 + rng() * 0.35
        out = crown_w * (0.22 + rng() * 0.12)
        mid = (top[0] + math.cos(a) * out * 0.5, yb + crown_h * 0.25 * up, top[2] + math.sin(a) * out * 0.5)
        tip = (top[0] + math.cos(a) * out, yb + crown_h * (0.35 + 0.3 * up), top[2] + math.sin(a) * out)
        tube(m, P, "Limb", [top, mid, tip], lambda t: H * (0.018 - 0.011 * t), bark, seg=4, steps=1, caps=False)
        tips.append(tip)
    # clumps: one on each limb tip, the rest spread over the crown shell (golden-angle spiral), one on top
    pts = list(tips)
    for i in range(n_clumps - len(tips) - 1):
        t = (i + 0.5) / max(1, n_clumps - len(tips) - 1); y = 0.85 - 1.3 * t       # upper shell mostly
        a = i * 2.39996 + rng() * 0.6; rr = math.sqrt(max(0.05, 1 - y * y)) * 0.72
        pts.append((cc[0] + math.cos(a) * rr * cr[0], cc[1] + y * cr[1] * 0.75 - droop * crown_h * (1 - y) * 0.3, cc[2] + math.sin(a) * rr * cr[2]))
    pts.append((cc[0], cc[1] + cr[1] * 0.45, cc[2]))
    for p in pts:
        s = clump_r * (0.8 + rng() * 0.45)
        clump(m, P, p, (s * crown_w, s * crown_h * 0.85, s * crown_w), rng, cc, cr, lumps=lumps)
    return yb + crown_h


def conifer_tiers(m, P, rng, H, n, bark, base_f, width, rim_drop, jag=0.25, seg=9):
    """Spruce and larch: a straight trunk and stacked drooping tiers with ragged rims."""
    tube(m, P, "Trunk", [(0, -0.4, 0), (0, H * 0.5, 0), (0, H * 0.97, 0)], lambda t: H * (0.024 - 0.02 * t), bark, seg=6, steps=1, caps=False)
    for i in range(n):
        f = i / n; y0 = H * (base_f + (1 - base_f) * f * 0.92); r = width / 2 * (1 - f * 0.85) * (0.9 + rng() * 0.2)
        h = (H * (1 - base_f) / n) * 1.9
        o = m.new("Tier", P, smooth=True, sharp=70.0)
        apex = o.vert((0, y0 + h, 0))
        rim, under = [], []
        for j in range(seg):
            a = j / seg * 6.283 + i * 0.7; rr = r * (1 + jag * (0.5 if j % 2 else -0.5) * (0.6 + rng() * 0.8))
            rim.append(o.vert((math.cos(a) * rr, y0 - rim_drop * rr, math.sin(a) * rr)))
            under.append(o.vert((math.cos(a) * rr * 0.35, y0 + h * 0.18, math.sin(a) * rr * 0.35)))
        for j in range(seg):
            j2 = (j + 1) % seg
            q = mul(add(add(o.v[apex], o.v[rim[j]]), o.v[rim[j2]]), 1 / 3.0)
            ids = (apex, rim[j], rim[j2]); mat = "leaf_light" if i >= n - 2 or (j % 3 == 0) else "leaf_mid"
            P3 = [o.v[k] for k in ids]
            if dot(cross(sub(P3[1], P3[0]), sub(P3[2], P3[0])), (q[0], 0.4, q[2])) >= 0: o.face(ids, mat)
            else: o.face((apex, rim[j2], rim[j]), mat)
            oriented_quad(o, (rim[j], under[j], under[j2], rim[j2]), (0, -1, 0), "leaf_dark")


def scots_pine(m, P, rng, H):
    """Scots pine: a tall bare orange trunk with a kink, and a flat, broken crown of a few clumps at the top."""
    k = ((rng() - 0.5) * 0.8, (rng() - 0.5) * 0.8)
    tube(m, P, "Trunk", [(0, -0.4, 0), (k[0] * 0.3, H * 0.4, k[1] * 0.3), (k[0], H * 0.72, k[1]), (k[0] * 0.8, H * 0.9, k[1] * 0.8)], lambda t: H * (0.03 - 0.02 * t), "bark_pine", seg=6, steps=2, caps=False)
    cc = (k[0], H * 0.84, k[1]); cr = (H * 0.2, H * 0.1, H * 0.2)
    for i in range(10):
        a = i * 2.4 + rng(); d = H * (0.05 + 0.13 * rng()) if i else 0.0
        p = (cc[0] + math.cos(a) * d, H * (0.7 + 0.2 * rng()) if i else H * 0.9, cc[2] + math.sin(a) * d)
        if i: tube(m, P, "Branch", [(k[0], p[1] - H * 0.06, k[1]), p], H * 0.007, "bark_pine", seg=4, steps=1, caps=False)
        s = H * (0.07 + 0.035 * rng())
        clump(m, P, p, (s * 1.3, s * 0.45, s * 1.3), rng, cc, cr, nu=6, nv=4, lumps=0.32)


TREE_SPECIES = {
    #            height, builder
    "oak":      (14.0, lambda m, P, r, H: broadleaf(m, P, r, H, 0.3, H * 1.05, H * 0.68, 14, 5, "bark", 0.23, lumps=0.3)),
    "ash":      (16.0, lambda m, P, r, H: broadleaf(m, P, r, H, 0.38, H * 0.7, H * 0.6, 11, 4, "bark_grey", 0.21, lumps=0.34)),
    "beech":    (17.0, lambda m, P, r, H: broadleaf(m, P, r, H, 0.28, H * 0.8, H * 0.72, 13, 4, "bark_grey", 0.25, droop=0.4, lumps=0.22)),
    "sycamore": (15.0, lambda m, P, r, H: broadleaf(m, P, r, H, 0.3, H * 0.85, H * 0.7, 13, 4, "bark", 0.25, lumps=0.26)),
    "birch":    (13.0, lambda m, P, r, H: broadleaf(m, P, r, H, 0.32, H * 0.42, H * 0.66, 10, 3, "bark_birch", 0.23, droop=0.7, lumps=0.34)),
    "hawthorn": (6.0,  lambda m, P, r, H: broadleaf(m, P, r, H, 0.2, H * 1.0, H * 0.8, 9, 3, "bark", 0.27, lumps=0.38)),
    "pine":     (18.0, scots_pine),
    "spruce":   (20.0, lambda m, P, r, H: conifer_tiers(m, P, r, H, 8, "bark", 0.12, H * 0.42, 0.28)),
    "larch":    (18.0, lambda m, P, r, H: conifer_tiers(m, P, r, H, 7, "bark", 0.15, H * 0.36, 0.18, jag=0.45, seg=8)),
}
TREE_VARIANTS = 2


def build_trees():
    m = Model(); layout = {}; info = {}
    for si, (sp, (H, fn)) in enumerate(TREE_SPECIES.items()):
        for v in range(TREE_VARIANTS):
            name = "%s_%d" % (sp, v)
            fn(m, name, Rng(si * 31 + v * 7 + 3), H * (0.95 + 0.1 * v))
            info[name] = {"species": sp, "height": H * (0.95 + 0.1 * v)}
            layout[name] = ((si - 4) * 14.0, 0.0, v * 16.0)
    shots = [("front", (0.0, 18.0, 110.0), (0.0, 8.0, 8.0), 50), ("side", (-90.0, 30.0, 60.0), (0.0, 8.0, 8.0), 50)]
    meta = {"format": "RCM1", "model": "Trees", "library": True, "refRotorDiameter": 1.0, "generator": VERSION,
            "points": {}, "controls": [], "layout": layout, "shots": shots, "aoDistance": 2.0, "trees": info}
    return m, meta


# ============================================================================ Nightjar N4 (model "nightjar")
# An original light twin in the class of today's twin-engine air ambulance and police helicopters: a deep
# cabin with sliding side doors and rear clamshell doors under a raised boom, two hinged crew doors, a
# shrouded tail fan, skids and a five-blade rotor. Doors are separate parts that the game opens (meta
# "doors"); the cabin is furnished front and back (part "interior"), and the medical and police fits are
# kit parts. Livery: slot 1 body, slot 2 and 3 the two colours of the high-visibility check band (slot 3
# also the tail), so one model wears every skin.
MATS.update({
    "chk_a":     (1.0, 1.0, 1.0, 0.36, 2, False),
    "chk_b":     (1.0, 1.0, 1.0, 0.36, 3, False),
    "nj_belly":  (0.30, 0.31, 0.33, 0.35, 0, False),
    "seal":      (0.03, 0.03, 0.035, 0.15, 0, False),
    "lining":    (0.62, 0.63, 0.62, 0.15, 0, False),
    "lining_dk": (0.22, 0.23, 0.25, 0.15, 0, False),
    "cab_floor": (0.10, 0.11, 0.12, 0.10, 0, False),
    "mattress":  (0.08, 0.10, 0.14, 0.20, 0, False),
    "sheet":     (0.85, 0.87, 0.90, 0.10, 0, False),
    "o2green":   (0.10, 0.45, 0.20, 0.50, 0, False),
    "medbag_r":  (0.70, 0.08, 0.06, 0.25, 0, False),
    "medbag_g":  (0.10, 0.45, 0.25, 0.25, 0, False),
    "defib":     (0.92, 0.75, 0.08, 0.40, 0, False),
    "cabinet":   (0.80, 0.81, 0.82, 0.30, 0, False),
    "mfd":       (0.05, 0.10, 0.18, 0.0, 0, True),
    "mfd_map":   (0.10, 0.22, 0.12, 0.0, 0, True),
    "mfd_cam":   (0.20, 0.22, 0.24, 0.0, 0, True),
    "fan":       (0.20, 0.21, 0.22, 0.50, 0, False),
    "rail":      (0.55, 0.57, 0.60, 0.65, 0, False),
})

NJ_FUS = [  # z, yc, half-width, upper and lower half-heights, superellipse exponent
    (-3.95, 1.18, 0.03, 0.03, 0.03, 2.0), (-3.82, 1.20, 0.36, 0.40, 0.34, 2.1), (-3.50, 1.28, 0.64, 0.66, 0.58, 2.4),
    (-3.00, 1.34, 0.80, 0.76, 0.70, 2.9), (-2.40, 1.36, 0.87, 0.78, 0.74, 3.4), (-1.60, 1.37, 0.88, 0.77, 0.75, 3.7),
    (0.80, 1.37, 0.88, 0.77, 0.75, 3.7), (1.45, 1.40, 0.86, 0.76, 0.70, 3.5), (2.00, 1.47, 0.81, 0.69, 0.60, 3.1),
]
NJ_REAR = 2.00
NJ_YF = [0.70, 0.80, 0.92, 1.05, 1.18, 1.28, 1.38, 1.48, 1.60, 1.72, 1.85, 1.92, 1.98, 2.04]
NJ_BELLY, NJ_WS = 0.70, 1.48
NJ_CHK = (0.92, 1.05, 1.18)
# door and window rectangles on each side: (z0, z1, y0, y1)
NJ_CREW_DOOR, NJ_CREW_WIN = (-2.45, -1.32, 0.80, 2.04), (-2.38, -1.40, 1.38, 1.92)
NJ_CABIN_DOOR, NJ_CABIN_WIN = (-1.22, 0.30, 0.70, 2.04), (-1.12, 0.20, 1.28, 1.85)
NJ_REAR_WIN = (0.55, 1.40, 1.28, 1.85)
NJ_CHIN = (-3.55, -2.75, 0.80, 1.18)
NJ_SLIDE = 1.48                 # how far the cabin doors slide back
NJ_FLOOR = 0.74


def nj_rows():
    feats = [NJ_CHIN[0], NJ_CHIN[1], NJ_CREW_DOOR[0], NJ_CREW_DOOR[1], NJ_CREW_WIN[0], NJ_CREW_WIN[1], NJ_CABIN_DOOR[0], NJ_CABIN_DOOR[1],
             NJ_CABIN_WIN[0], NJ_CABIN_WIN[1], NJ_REAR_WIN[0], NJ_REAR_WIN[1], -2.55]
    feats += [-2.45 + 0.28 * i for i in range(16)]                    # check band cells
    base = [-3.95 + 0.005 * i * i for i in range(12)]                 # dense at the nose
    z = base[-1]
    while z < NJ_REAR: base.append(z); z += 0.07
    base.append(NJ_REAR)
    keep = [z for z in base if min(abs(z - f) for f in feats) > 0.02 or z in (-3.95, NJ_REAR)]
    return sorted(set([round(z, 4) for z in keep + feats if -3.95 <= z <= NJ_REAR]))


def nj_ring(H, z):
    """Angles round the section: 6 across the belly, one vertex on every feature height each side, 10 over the roof."""
    seq = [H.th_at_y(z, y, 1) for y in NJ_YF]
    seq = [-math.pi / 2 + 1e-4] + seq
    for i in range(1, len(seq)): seq[i] = max(seq[i], seq[i - 1] + 1e-5)
    seq = [min(s, math.pi / 2 - 1e-3) for s in seq]
    right = [lerp(-math.pi / 2, seq[1], j / 6) for j in range(6)] + seq[1:]
    top = [lerp(seq[-1], math.pi - seq[-1], j / 10) for j in range(1, 10)]
    left = [math.pi - a for a in reversed(right[1:])]
    return right + top + left                     # closes back to -pi/2 (index 0) after the last


def in_rect(z, y, r, e=0.0): return r[0] + e < z < r[1] - e and r[2] + e < y < r[3] - e


def nj_paint(x, y, z):
    """Material of the skin at a point (outer surface)."""
    if y < NJ_BELLY: return "nj_belly"
    if z < -2.55 and y > NJ_WS: return "glass"                                    # windscreen
    if in_rect(z, y, NJ_CHIN) and abs(x) > 0.2: return "glass"                    # chin windows
    if in_rect(z, y, NJ_CREW_WIN) or in_rect(z, y, NJ_CABIN_WIN) or in_rect(z, y, NJ_REAR_WIN): return "glass"
    if NJ_CHK[0] < y < NJ_CHK[2] and -2.45 < z < NJ_REAR:                        # check band
        cell = int(math.floor((z + 2.45) / 0.28)); row = 1 if y > NJ_CHK[1] else 0
        return "chk_a" if (cell + row) % 2 == 0 else "chk_b"
    if y > 2.04 and z > -2.55: return "paint"
    return "paint"


def nj_region(x, y, z):
    """Which object a skin face belongs to: the body, or one of the doors."""
    side = "r" if x > 0 else "l"
    if abs(x) < 0.3: return "body"
    if in_rect(z, y, NJ_CREW_DOOR): return "door_crew_" + side
    if in_rect(z, y, NJ_CABIN_DOOR): return "door_cabin_" + side
    return "body"


def nj_grid(o, pts, mats, inward=False, centre=None):
    """Quads over a grid of points pts[i][j] (closed in j if the last column wraps), with a material (or None) per quad."""
    ids = {}
    def vid(i, j):
        if (i, j) not in ids: ids[(i, j)] = o.vert(pts[i][j])
        return ids[(i, j)]
    for (i, j, j2), mat in mats.items():
        if mat is None: continue
        ps = [pts[i][j], pts[i + 1][j], pts[i + 1][j2], pts[i][j2]]
        fn = add(cross(sub(ps[1], ps[0]), sub(ps[2], ps[0])), cross(sub(ps[2], ps[0]), sub(ps[3], ps[0])))
        if length(fn) < 1e-12: continue
        c = mul(add(add(ps[0], ps[1]), add(ps[2], ps[3])), 0.25)
        out = sub(c, centre(c) if centre else (0.0, c[1], c[2]))
        if inward: out = mul(out, -1)
        oriented_quad(o, (vid(i, j), vid(i + 1, j), vid(i + 1, j2), vid(i, j2)), out, mat)


def nj_skin(m, H):
    """The fuselage pod: outer skin (body, minus the door openings), the cabin lining inside it, and jambs
    round every door opening so an open door shows a finished edge."""
    rows = nj_rows()
    rings = [nj_ring(H, z) for z in rows]
    nv = len(rings[0])
    outer = [[H.P(z, a, 1.0) for a in ang] for z, ang in zip(rows, rings)]
    inner = [[H.P(z, a, 0.965) for a in ang] for z, ang in zip(rows, rings)]
    body_q, lin_q = {}, {}
    for i in range(len(rows) - 1):
        for j in range(nv):
            j2 = (j + 1) % nv
            ps = [outer[i][j], outer[i + 1][j], outer[i + 1][j2], outer[i][j2]]
            c = mul(add(add(ps[0], ps[1]), add(ps[2], ps[3])), 0.25)
            reg = nj_region(*c); mat = nj_paint(*c)
            body_q[(i, j, j2)] = mat if reg == "body" else None
            # lining: not behind glass (so you see out), not in the door openings (the doors carry their own),
            # floor below the floor line, dark lower walls, light upper walls and roof
            lin = None
            if reg == "body" and mat != "glass" and c[2] > -3.75:
                lin = "cab_floor" if c[1] < NJ_FLOOR + 0.02 else ("lining_dk" if c[1] < 1.25 else "lining")
            lin_q[(i, j, j2)] = lin
    centre = lambda c: (0.0, H.yc(c[2]), c[2])
    nj_grid(m.new("Fuselage skin", "body"), outer, body_q, centre=centre)
    nj_grid(m.new("Cabin lining", "interior", ao=True), inner, lin_q, inward=True, centre=centre)
    # floor panel
    box(m, "interior", "Cabin floor", (0, NJ_FLOOR - 0.01, -0.85), (1.5, 0.02, 5.4), "cab_floor")
    # jambs round the four side openings
    for side in (1, -1):
        for r in (NJ_CREW_DOOR, NJ_CABIN_DOOR):
            nj_jamb(m, H, side, r)
    # rear: the hull ends at NJ_REAR. Clamshell opening below the boom; a fixed panel above it.
    zr = NJ_REAR
    ring = [H.P(zr, a, 1.0) for a in nj_ring(H, zr)]
    ringi = [H.P(zr, a, 0.965) for a in nj_ring(H, zr)]
    o = m.new("Rear jamb", "body")
    n = len(ring)
    for j in range(n):
        j2 = (j + 1) % n
        if (ring[j][1] + ring[j2][1]) / 2 > NJ_REAR_TOP: continue
        cc = mul(add(ring[j], ring[j2]), 0.5)
        oriented_quad(o, (o.vert(ring[j]), o.vert(ring[j2]), o.vert(ringi[j2]), o.vert(ringi[j])), sub((0, H.yc(zr), zr), cc), "seal")
    top_o = [p for p in ring if p[1] >= NJ_REAR_TOP - 1e-6]
    top_i = [p for p in ringi if p[1] >= NJ_REAR_TOP - 1e-6]
    if len(top_o) > 2:
        cap_fan(m.new("Rear upper panel", "body"), top_o, (0, 0, 1), "paint")
        cap_fan(m.new("Rear upper panel inside", "interior"), [(p[0], p[1], p[2] - 0.02) for p in top_i], (0, 0, -1), "lining")
    # header across the top of the opening
    hw = H.w(zr) * 0.93
    box(m, "body", "Rear door header", (0, NJ_REAR_TOP + 0.01, zr - 0.02), (hw * 2, 0.03, 0.06), "seal")


NJ_REAR_TOP = 1.85


def nj_jamb(m, H, side, r, inset=0.965):
    """Edge strip round a door opening on the skin, from the outer skin to the lining."""
    z0, z1, y0, y1 = r
    path = [(z, y0) for z in [lerp(z0, z1, t / 12) for t in range(13)]] + [(z1, y) for y in [lerp(y0, y1, t / 8) for t in range(1, 9)]]
    path += [(z, y1) for z in [lerp(z1, z0, t / 12) for t in range(1, 13)]] + [(z0, y) for y in [lerp(y1, y0, t / 8) for t in range(1, 8)]]
    o = m.new("Door jamb", "body")
    cz, cy = (z0 + z1) / 2, (y0 + y1) / 2
    for k in range(len(path)):
        (za, ya), (zb, yb) = path[k], path[(k + 1) % len(path)]
        ta, tb = H.th_at_y(za, ya, side), H.th_at_y(zb, yb, side)
        a0, b0 = H.P(za, ta, 1.0), H.P(zb, tb, 1.0)
        a1, b1 = H.P(za, ta, inset), H.P(zb, tb, inset)
        mid = H.P(cz, H.th_at_y(cz, cy, side), 0.98)
        cc = mul(add(a0, b1), 0.5)
        oriented_quad(o, (o.vert(a0), o.vert(b0), o.vert(b1), o.vert(a1)), sub(mid, cc), "seal")


def nj_door(m, H, part, side, r, win, handle_z):
    """A door panel cut to its opening (a hair smaller), skin outside and lining inside, with its window."""
    e = 0.012
    z0, z1, y0, y1 = r[0] + e, r[1] - e, r[2] + e, r[3] - e
    zs = sorted(set([z0, z1] + [z for z in nj_rows() if z0 < z < z1] + [win[0], win[1]]))
    ys = sorted(set([y0, y1] + [y for y in NJ_YF if y0 < y < y1] + [win[2], win[3]]))
    def grid(inset): return [[H.P(z, H.th_at_y(z, y, side), inset) for y in ys] for z in zs]
    out, inn = grid(1.0), grid(0.965)
    qo, qi = {}, {}
    for i in range(len(zs) - 1):
        for j in range(len(ys) - 1):
            zc, yc = (zs[i] + zs[i + 1]) / 2, (ys[j] + ys[j + 1]) / 2
            mat = nj_paint(side * 1.0, yc, zc)
            qo[(i, j, j + 1)] = mat
            qi[(i, j, j + 1)] = None if mat == "glass" else ("lining_dk" if yc < 1.25 else "lining")
    centre = lambda c: (0.0, H.yc(c[2]), c[2])
    nj_grid(m.new("Door skin", part), out, qo, centre=centre)
    nj_grid(m.new("Door lining", part), inn, qi, inward=True, centre=centre)
    # door edge (thickness)
    o = m.new("Door edge", part)
    loop = [(i, 0) for i in range(len(zs))] + [(len(zs) - 1, j) for j in range(1, len(ys))] + [(i, len(ys) - 1) for i in range(len(zs) - 2, -1, -1)] + [(0, j) for j in range(len(ys) - 2, 0, -1)]
    mid = H.P((z0 + z1) / 2, H.th_at_y((z0 + z1) / 2, (y0 + y1) / 2, side), 0.98)
    for k in range(len(loop)):
        (ia, ja), (ib, jb) = loop[k], loop[(k + 1) % len(loop)]
        a0, b0, a1, b1 = out[ia][ja], out[ib][jb], inn[ia][ja], inn[ib][jb]
        cc = mul(add(a0, b1), 0.5)
        oriented_quad(o, (o.vert(a0), o.vert(b0), o.vert(b1), o.vert(a1)), sub(cc, mid), "seal")
    # window seal round the glass
    wz0, wz1, wy0, wy1 = win
    pts = [(wz0, wy0), (wz1, wy0), (wz1, wy1), (wz0, wy1), (wz0, wy0)]
    seal = []
    for k in range(4):
        (za, ya), (zb, yb) = pts[k], pts[k + 1]
        for t in range(6): z, y = lerp(za, zb, t / 6), lerp(ya, yb, t / 6); seal.append(H.point(z, H.th_at_y(z, y, side), 0.008))
    seal.append(seal[0])
    tube(m, part, "Window seal", seal, 0.012, "seal", 6, caps=False, steps=1)
    # handle
    hp = H.point(handle_z, H.th_at_y(handle_z, 1.22, side), 0.02)
    box(m, part, "Door handle", hp, (0.03, 0.03, 0.16), "chrome", bevel=0.008)
    return (z0, z1, y0, y1)


def nj_rear_doors(m, H):
    """Clamshell doors closing the back of the pod under the boom, each hinged on its outer edge."""
    zr = NJ_REAR
    ring = [H.P(zr, a, 1.0) for a in nj_ring(H, zr)]
    doors = []
    for side in (1, -1):
        part = "door_rear_" + ("r" if side > 0 else "l")
        pts = sorted([p for p in ring if p[0] * side > 0.004 and p[1] <= NJ_REAR_TOP + 1e-6], key=lambda p: p[1])
        ybot = min(p[1] for p in ring)
        outline = [(side * 0.006, ybot + 0.02)] + [(p[0] - side * 0.008, p[1] - (0.01 if p[1] > NJ_REAR_TOP - 1e-3 else 0)) for p in pts] + [(side * 0.006, NJ_REAR_TOP - 0.01)]
        for zz, nn, mat in ((zr + 0.012, 1, "paint"), (zr - 0.025, -1, "lining")):
            o = m.new("Rear door " + ("skin" if nn > 0 else "lining"), part)
            cap_fan(o, [(x, y, zz) for x, y in outline], (0, 0, nn), mat)
        o = m.new("Rear door edge", part)
        for k in range(len(outline)):
            (xa, ya), (xb, yb) = outline[k], outline[(k + 1) % len(outline)]
            cx = sum(q[0] for q in outline) / len(outline); cy = sum(q[1] for q in outline) / len(outline)
            oriented_quad(o, (o.vert((xa, ya, zr + 0.012)), o.vert((xb, yb, zr + 0.012)), o.vert((xb, yb, zr - 0.025)), o.vert((xa, ya, zr - 0.025))), ((xa + xb) / 2 - cx, (ya + yb) / 2 - cy, 0), "seal")
        # window, handle, hinges
        wx = side * 0.42
        box(m, part, "Rear door window", (wx, 1.55, zr + 0.016), (0.42, 0.34, 0.01), "glass")
        box(m, part, "Rear door window seal", (wx, 1.55, zr + 0.014), (0.47, 0.39, 0.008), "seal")
        box(m, part, "Rear door handle", (side * 0.1, 1.2, zr + 0.03), (0.03, 0.16, 0.03), "chrome", bevel=0.008)
        hx = max(abs(q[0]) for q in outline) * side
        for y in (1.15, 1.65):
            cyl(m, part, "Hinge", (hx, y - 0.05, zr + 0.01), (hx, y + 0.05, zr + 0.01), 0.018, 0.018, "darkmetal", 10)
        doors.append({"part": part, "group": "rear", "kind": "hinge", "pivot": [hx, 1.4, zr], "axis": [0, 1, 0], "open": 1.9 * side})
    return doors


def nj_shroud(m):
    """The tail: a shrouded fan (duct, stators, hub) blending up into the fin."""
    P = "body"; zc, yc = 6.62, 2.02
    RI = 0.52
    ctrl = [(0, 0.66), (35, 0.71), (60, 0.98), (76, 1.08), (92, 0.88), (125, 0.70), (180, 0.68), (235, 0.62), (300, 0.63), (360, 0.66)]
    radius = pchip([c[0] for c in ctrl], [c[1] for c in ctrl])
    N = 48
    def out(a, k=1.0): r = radius(a / math.pi * 180 % 360) * k; return (zc + math.cos(a) * r, yc + math.sin(a) * r)
    def inn(a): return (zc + math.cos(a) * RI, yc + math.sin(a) * RI)
    T = 0.17                                                     # half thickness at the duct
    o = m.new("Shroud", P, smooth=True, sharp=60.0)
    for side in (1, -1):
        for j in range(N):
            a, b = j / N * 2 * math.pi, (j + 1) / N * 2 * math.pi
            zo, yo = out(a, 0.94); zo2, yo2 = out(b, 0.94); zi, yi = inn(a); zi2, yi2 = inn(b)
            th = lambda z, y: T * (1 - 0.55 * smooth(0.8, 1.2, math.hypot(z - zc, y - yc)))
            quad = [(side * th(zi, yi), yi, zi), (side * th(zo, yo), yo, zo), (side * th(zo2, yo2), yo2, zo2), (side * th(zi2, yi2), yi2, zi2)]
            ids = [o.vert(q) for q in quad]
            mid = ((zi + zo) / 2, (yi + yo) / 2)
            oriented_quad(o, ids, (side, 0, 0), "accent" if mid[1] > yc + 0.6 else "paint")
    # rounded rim joining the two faces round the outside
    for j in range(N):
        a, b = j / N * 2 * math.pi, (j + 1) / N * 2 * math.pi
        rows = []
        for (k, t) in ((0.94, 1), (0.985, 0.6), (1.0, 0.0), (0.985, -0.6), (0.94, -1)):
            za, ya = out(a, k); zb, yb = out(b, k)
            tha = T * (1 - 0.55 * smooth(0.8, 1.2, math.hypot(za - zc, ya - yc))); thb = T * (1 - 0.55 * smooth(0.8, 1.2, math.hypot(zb - zc, yb - yc)))
            rows.append(((t * tha, ya, za), (t * thb, yb, zb)))
        for q in range(4):
            ids = [o.vert(rows[q][0]), o.vert(rows[q + 1][0]), o.vert(rows[q + 1][1]), o.vert(rows[q][1])]
            mz, my = out((a + b) / 2, 1.0)
            oriented_quad(o, ids, (0, my - yc, mz - zc), "accent" if my > yc + 0.6 else "paint")
    # duct: the inner wall of the fan tunnel, facing in
    o = m.new("Duct", P)
    for j in range(N):
        a, b = j / N * 2 * math.pi, (j + 1) / N * 2 * math.pi
        za, ya = inn(a); zb, yb = inn(b)
        ids = [o.vert((T, ya, za)), o.vert((T, yb, zb)), o.vert((-T, yb, zb)), o.vert((-T, ya, za))]
        oriented_quad(o, ids, (0, yc - (ya + yb) / 2, zc - (za + zb) / 2), "darkmetal")
    # stators (behind the fan) and the hub fairing carrying the gearbox
    for k in range(10):
        a = k / 10 * 2 * math.pi + 0.15
        d = (0, math.sin(a), math.cos(a))
        box(m, P, "Stator", (-0.09, yc + d[1] * 0.33, zc + d[2] * 0.33), (0.025, 0.04, 0.38), "fan", axes=((1, 0, 0), (0, math.cos(a), -math.sin(a)), d))
    cyl(m, P, "Fan hub fairing", (-0.16, yc, zc), (0.02, yc, zc), 0.15, 0.17, "fan", 20)
    ellipsoid(m, P, "Hub cap", (-0.16, yc, zc), (0.06, 0.15, 0.15), "fan", 16, 8)
    # tail bumper under the shroud
    tube(m, P, "Tail bumper", [(0, 1.35, 6.2), (0, 1.22, 6.45), (0, 1.27, 6.75)], 0.025, "darkmetal", 8)


def nj_exterior(m, H):
    P = "body"
    nj_skin(m, H)
    # windscreen frames: centre post and the edge of the glass, door posts
    tube(m, P, "Windscreen centre post", [H.point(z, math.pi / 2 if z > -3.4 else math.pi / 2, 0.006) for z in (-3.62, -3.3, -3.0, -2.75, -2.56)], 0.022, "frame", 6, steps=2)
    for side in (1, -1):
        edge = [H.point(z, H.th_at_y(z, NJ_WS, side), 0.006) for z in [-3.62 + 0.08 * i for i in range(14)]]
        tube(m, P, "Windscreen sill", edge, 0.018, "frame", 6, steps=1)
        if side > 0:
            tR = H.th_at_y(-2.56, NJ_WS, 1)
            tube(m, P, "Windscreen arch", [H.point(-2.56, lerp(tR, math.pi - tR, t / 12), 0.006) for t in range(13)], 0.022, "frame", 6, steps=1)
        # chin window seals and fixed rear window seal
        for (z0, z1, y0, y1) in (NJ_CHIN, NJ_REAR_WIN):
            pts = [(z0, y0), (z1, y0), (z1, y1), (z0, y1), (z0, y0)]
            seal = []
            for k in range(4):
                (za, ya), (zb, yb) = pts[k], pts[k + 1]
                for t in range(6): z, y = lerp(za, zb, t / 6), lerp(ya, yb, t / 6); seal.append(H.point(z, H.th_at_y(z, y, side), 0.006))
            seal.append(seal[0])
            tube(m, P, "Window seal", seal, 0.012, "seal", 6, caps=False, steps=1)
        # sliding door rails above and below, running back past the rear window
        for y in (2.05, 0.72):
            rail = [H.point(z, H.th_at_y(z, y, side), 0.02) for z in [-1.2 + 0.15 * i for i in range(21)]]
            tube(m, P, "Door rail", rail, 0.016, "rail", 6, steps=1)
        # steps under the doors, each on two struts down to the skid
        for name, zs in (("Crew step", -1.9), ("Cabin step", -0.45)):
            box(m, P, name, (side * 0.98, 0.34, zs), (0.3, 0.025, 0.16), "frame", bevel=0.006)
            for dz in (-0.06, 0.06):
                tube(m, P, "Step strut", [(side * 0.9, 0.33, zs + dz), (side * 1.06, 0.18, zs + dz), (side * 1.08, 0.07, zs + dz)], 0.014, "metal", 6, steps=2)
    # ---- engine cowling with intakes, exhausts and the mast fairing
    cowl = [(-1.80, 2.06, 0.06, 0.04, 0.04, 2.0), (-1.45, 2.20, 0.52, 0.26, 0.18, 2.6), (-0.70, 2.33, 0.62, 0.42, 0.25, 3.0),
            (0.90, 2.33, 0.62, 0.42, 0.25, 3.0), (1.60, 2.26, 0.52, 0.34, 0.22, 2.7), (2.15, 2.12, 0.36, 0.22, 0.18, 2.3)]
    Hc = Hull(cowl)
    o = m.new("Engine cowling", P)
    def cowl_paint(c, n, u, v):
        x, y, z = c
        if abs(x) > 0.42 and -1.25 < z < -0.55 and 2.28 < y < 2.62: return "intake" if int((z + 1.25) / 0.05) % 2 == 0 else "darkmetal"
        if near(z, 0.25, 0.012) or near(z, 1.15, 0.012): return "seam"
        if abs(x) < 0.02 and y > 2.6: return "seam"
        return "paint"
    surface(o, 46, 40, lambda u, v: Hc.P(lerp(-1.8, 2.15, u), v * 2 * math.pi), cowl_paint)
    for side in (1, -1):
        ex = [(side * 0.38, 2.42, 1.35), (side * 0.47, 2.46, 1.75), (side * 0.56, 2.5, 2.05)]
        tube(m, P, "Exhaust", ex, lambda t: 0.11 + 0.03 * t, "exhaust", 14, caps=False)
        ring_band(m, P, "Exhaust lip", ex[-1], norm(sub(ex[-1], ex[-2])), 0.13, 0.15, 0.02, "darkmetal", 16)
    cyl(m, P, "Mast fairing", (0, 2.70, 0.0), (0, 2.95, 0.0), 0.32, 0.2, "paint", 28)
    cyl(m, P, "Mast", (0, 2.95, 0.0), (0, 3.30, 0.0), 0.08, 0.075, "metal", 20)
    cyl(m, P, "Swashplate (fixed)", (0, 3.06, 0.0), (0, 3.1, 0.0), 0.24, 0.24, "darkmetal", 32)
    for a in (0.5, 2.6, 4.7):
        d = (math.cos(a), 0, math.sin(a))
        tube(m, P, "Servo rod", [add((0, 2.92, 0), mul(d, 0.26)), add((0, 3.06, 0), mul(d, 0.22))], 0.012, "metal", 8)
    # ---- tail boom, stabiliser with end plates, shroud and fin
    boom = [(1.20, 2.06, 0.46, 0.34, 0.26, 2.4), (2.10, 2.06, 0.38, 0.29, 0.21, 2.3), (3.60, 2.05, 0.28, 0.24, 0.20, 2.2),
            (5.40, 2.03, 0.20, 0.19, 0.18, 2.1), (6.0, 2.02, 0.16, 0.16, 0.16, 2.0)]
    Hb = Hull(boom)
    o = m.new("Tail boom", P)
    def boom_paint(c, n, u, v):
        x, y, z = c
        if 2.3 < z < 5.0 and abs(y - 2.05) < 0.05 and abs(x) > 0.1: return "chk_a"
        return "paint"
    surface(o, 56, 28, lambda u, v: Hb.P(lerp(1.2, 6.0, u), v * 2 * math.pi), boom_paint)
    for s in (-1, 1):
        wing(m, P, "Stabiliser", (s * 0.12, 2.0, 5.15), (s * 1.02, 2.02, 5.32), 0.48, 0.36, 0.12, (0, 0, 1), (0, 1, 0), lambda c: "paint", 6, 20)
        wing(m, P, "End plate", (s * 1.04, 1.78, 5.24), (s * 1.06, 2.36, 5.48), 0.46, 0.32, 0.1, (0, 0, 1), (1, 0, 0), lambda c: "accent", 6, 18)
    nj_shroud(m)
    # ---- skids
    for x in (-1.08, 1.08):
        tube(m, P, "Skid", [(x, 0.36, -3.05), (x, 0.16, -2.88), (x, 0.06, -2.55), (x, 0.05, -1.6), (x, 0.05, 1.2), (x, 0.09, 1.65)], 0.045, "metal", 14)
        for z in (-1.55, 0.85):
            ring_band(m, P, "Skid saddle", (x, 0.06, z), (0, 0, 1), 0.045, 0.058, 0.09, "darkmetal", 20)
        box(m, P, "Wear shoe", (x, 0.012, -0.4), (0.055, 0.024, 0.9), "darkmetal", bevel=0.006)
    for z in (-1.55, 0.85):
        tube(m, P, "Crosstube", [(-1.08, 0.06, z), (-1.02, 0.36, z), (-0.8, 0.6, z), (-0.45, 0.66, z), (0, 0.66, z), (0.45, 0.66, z), (0.8, 0.6, z), (1.02, 0.36, z), (1.08, 0.06, z)], 0.045, "metal", 14)
    # ---- lights, aerials, probes
    ellipsoid(m, P, "Beacon (fin)", (0, 3.08, 6.87), (0.05, 0.04, 0.05), "beacon", 14, 8)
    ellipsoid(m, P, "Beacon (belly)", (0, 0.6, -0.4), (0.06, 0.04, 0.06), "beacon", 14, 8)
    for s, col in ((-1, "navred"), (1, "navgreen")):
        ellipsoid(m, P, "Nav light", (s * 1.07, 2.4, 5.48), (0.02, 0.035, 0.05), col, 12, 6)
    ellipsoid(m, P, "Tail light", (0, 2.02, 7.3), (0.025, 0.03, 0.03), "lens", 12, 6)
    cyl(m, P, "Landing light", (0, 0.72, -3.15), (0, 0.66, -3.25), 0.09, 0.085, "frame", 20)
    cyl(m, P, "Landing light lens", (0, 0.66, -3.25), (0, 0.655, -3.26), 0.08, 0.075, "lens", 20)
    for sx in (1, -1):   # pitot probes out of the nose below the windscreen (they start inside the skin)
        tube(m, P, "Pitot", [(sx * 0.22, 1.12, -3.12), (sx * 0.25, 1.14, -3.38), (sx * 0.26, 1.15, -3.56)], 0.011, "chrome", 8)
    box(m, P, "Roof aerial", (0.2, 2.2, -2.2), (0.012, 0.14, 0.12), "frame", bevel=0.004)
    box(m, P, "Belly aerial", (0.25, 0.6, 0.8), (0.012, 0.12, 0.12), "frame", bevel=0.004)
    tube(m, P, "Wire cutter (upper)", [(0, 2.13, -2.6), (0, 2.32, -2.7), (0, 2.42, -2.65)], 0.015, "darkmetal", 6)
    tube(m, P, "Wire cutter (lower)", [(0, 0.62, -2.9), (0, 0.45, -3.0), (0, 0.4, -2.85)], 0.015, "darkmetal", 6)
    skin_details(m, P, H, roof_z=(-1.1, -0.7), steps=[(-0.9, 1.95)], ports=[(-2.75, 1.35)],
                 wicks=[(0.35, 0.95, 5.55, 5.66, 2.02)], whip=0.3)


def nj_rotor(m):
    R = 5.5
    for i in range(5): blade(m, i * 2 * math.pi / 5, R=R, rootR=0.42, chord=0.3, thick=0.1, twist_deg=-9, root_frac=0.75, fittings=False)
    for i in range(5):
        a = i * 2 * math.pi / 5; d = (math.sin(a), 0, math.cos(a))
        box(m, "blades", "Flexbeam", mul(d, 0.3), (0.12, 0.05, 0.5), "darkmetal", axes=(norm(cross((0, 1, 0), d)), (0, 1, 0), d), bevel=0.01)
        box(m, "blades", "Pitch cuff", mul(d, 0.38), (0.2, 0.1, 0.25), "blade", axes=(norm(cross((0, 1, 0), d)), (0, 1, 0), d), bevel=0.02)
        tube(m, "blades", "Pitch link", [add(mul(d, 0.24), (0, -0.28, 0)), add(mul(d, 0.3), (0, -0.02, 0))], 0.012, "chrome", 8)
    ellipsoid(m, "blades", "Hub cap", (0, 0.06, 0), (0.3, 0.16, 0.3), "darkmetal", 24, 10)
    cyl(m, "blades", "Swashplate (rotating)", (0, -0.31, 0), (0, -0.28, 0), 0.22, 0.22, "metal", 32)


def nj_seat(m, part, x, z, facing, mat="seat_sar", y=NJ_FLOOR, width=0.46, back=1.0):
    """Crashworthy seat: pan, cushion, back and headrest; facing -1 forward (towards -z), +1 backwards."""
    f = facing
    zc = z
    box(m, part, "Seat frame", (x, y + 0.2, zc), (width * 0.85, 0.36, 0.42), "seatframe", bevel=0.01)
    box(m, part, "Seat cushion", (x, y + 0.42, zc + f * 0.02), (width, 0.09, 0.46), mat, bevel=0.03, segs=3)
    lean = 0.12
    bz = zc - f * 0.22
    ey = norm((0, 1, -f * lean)); ez = (0, 0, f)
    box(m, part, "Seat back", (x, y + 0.42 + back / 2, bz - f * lean * back / 2), (width, back, 0.1), mat, axes=((1, 0, 0), ey, norm(cross((1, 0, 0), ey))), bevel=0.03, segs=3)
    box(m, part, "Headrest", (x, y + 0.42 + back + 0.08, bz - f * lean * (back + 0.08)), (width * 0.55, 0.16, 0.1), mat, axes=((1, 0, 0), ey, norm(cross((1, 0, 0), ey))), bevel=0.03, segs=3)
    for sx in (-1, 1):
        box(m, part, "Harness", (x + sx * width * 0.2, y + 0.42 + back * 0.55, bz - f * (lean * back * 0.55 - 0.06)), (0.045, back * 0.8, 0.008), "harness_sar", axes=((1, 0, 0), ey, norm(cross((1, 0, 0), ey))))


def nj_interior(m):
    """Cockpit and cabin furniture common to every fit (part "interior", seen through open doors and from the seat)."""
    I = "interior"
    # ---- cockpit: instrument panel with four large displays, glareshield, pedestal, overhead console
    pz = -3.02
    box(m, I, "Instrument panel", (0, 1.16, pz), (1.36, 0.42, 0.14), "panel", bevel=0.015)
    box(m, I, "Glareshield", (0, 1.4, pz + 0.04), (1.42, 0.05, 0.3), "glareshield", bevel=0.015)
    F = Face(m, I, (0, 1.16, pz + 0.071), (1, 0, 0), (0, 1, 0), (0, 0, 1))
    for i, x in enumerate((-0.48, -0.17, 0.17, 0.48)):
        F.screen(x, 0.0, 0.22, 0.27, "mfd_map" if i in (1, 2) else "mfd")
        for k in range(5): F.button(x - 0.13, -0.11 + k * 0.055, "btn_w", 0.02, 0.016); F.button(x + 0.13, -0.11 + k * 0.055, "btn_w", 0.02, 0.016)
    box(m, I, "Pedestal", (0, 0.92, pz + 0.38), (0.32, 0.36, 0.62), "panel", bevel=0.015)
    Fp = Face(m, I, (0, 1.105, pz + 0.38), (1, 0, 0), (0, 0, -1), (0, 1, 0))
    Fp.screen(0, 0.16, 0.2, 0.1, "mfd")
    for i in range(3):
        for j in range(2): Fp.knob(-0.09 + i * 0.09, -0.02 - j * 0.09, 0.014)
    box(m, I, "Overhead console", (0, 2.02, -2.62), (0.46, 0.08, 0.6), "panel", bevel=0.012)
    Fo = Face(m, I, (0, 1.979, -2.62), (1, 0, 0), (0, 0, -1), (0, -1, 0))
    for i in range(6):
        for j in range(4): Fo.toggle(-0.17 + i * 0.068, -0.2 + j * 0.13)
    for sx in (-1, 1):
        nj_seat(m, I, sx * 0.42, -1.95, -1, back=0.66)   # the cabin roof is low over the pilots
        cyl(m, I, "Collective housing", (sx * 0.42 - 0.3, NJ_FLOOR, -1.85), (sx * 0.42 - 0.3, NJ_FLOOR + 0.12, -1.85), 0.06, 0.05, "panel", 12)
    # ---- cabin: rear-facing seats behind the pilots, roof rails and lights, rear bulkhead fittings
    for sx in (-1, 1): nj_seat(m, I, sx * 0.45, -1.15, 1, mat="seat_ns", back=0.75)
    for sx in (-1, 1):
        box(m, I, "Roof rail", (sx * 0.35, 2.06, -0.2), (0.04, 0.03, 3.6), "rail")
        box(m, I, "Cabin light", (sx * 0.2, 2.07, 0.4), (0.08, 0.02, 0.5), "lamp_w")
        box(m, I, "Grab handle", (sx * 0.78, 1.85, -0.4), (0.03, 0.03, 0.3), "darkmetal", bevel=0.008)
    for sx in (-1, 1):
        box(m, I, "Stowage locker", (sx * 0.62, NJ_FLOOR + 0.3, 1.7), (0.3, 0.6, 0.45), "cabinet", bevel=0.01)


def nj_fit_hems(m):
    """Medical fit: stretcher on its loading platform, equipment wall with monitor, oxygen, defibrillator and bags."""
    E = "fit_hems"
    box(m, E, "Stretcher platform", (-0.32, NJ_FLOOR + 0.18, 0.55), (0.62, 0.36, 2.4), "cabinet", bevel=0.015)
    box(m, E, "Stretcher", (-0.32, NJ_FLOOR + 0.42, 0.55), (0.56, 0.08, 2.0), "metal", bevel=0.01)
    box(m, E, "Mattress", (-0.32, NJ_FLOOR + 0.5, 0.58), (0.52, 0.08, 1.85), "mattress", bevel=0.03, segs=3)
    box(m, E, "Sheet", (-0.32, NJ_FLOOR + 0.545, 0.75), (0.53, 0.012, 1.3), "sheet")
    box(m, E, "Pillow", (-0.32, NJ_FLOOR + 0.58, -0.25), (0.42, 0.08, 0.3), "sheet", bevel=0.03, segs=3)
    for sx in (-1, 1):
        tube(m, E, "Stretcher rail", [(-0.32 + sx * 0.28, NJ_FLOOR + 0.6, -0.2), (-0.32 + sx * 0.28, NJ_FLOOR + 0.6, 1.3)], 0.012, "chrome", 8)
    # equipment wall on the right
    box(m, E, "Equipment cabinet", (0.6, NJ_FLOOR + 0.55, 1.1), (0.34, 1.1, 1.0), "cabinet", bevel=0.012)
    Fe = Face(m, E, (0.429, NJ_FLOOR + 0.8, 1.1), (0, 0, 1), (0, 1, 0), (-1, 0, 0))
    Fe.screen(-0.18, 0.2, 0.26, 0.2, "mfd_map")
    Fe.screen(0.18, 0.2, 0.2, 0.2, "mfd")
    for i in range(4): Fe.button(-0.3 + i * 0.07, -0.02, "btn_w", 0.03, 0.02)
    box(m, E, "Defibrillator", (0.55, NJ_FLOOR + 1.22, 1.35), (0.26, 0.22, 0.3), "defib", bevel=0.02)
    for k in range(3):
        cyl(m, E, "Oxygen bottle", (0.72, NJ_FLOOR + 0.05, 0.25 + k * 0.17), (0.72, NJ_FLOOR + 0.85, 0.25 + k * 0.17), 0.07, 0.07, "o2green", 16)
        cyl(m, E, "Bottle valve", (0.72, NJ_FLOOR + 0.85, 0.25 + k * 0.17), (0.72, NJ_FLOOR + 0.93, 0.25 + k * 0.17), 0.03, 0.025, "chrome", 10)
    box(m, E, "Response bag", (0.55, NJ_FLOOR + 0.18, -0.55), (0.32, 0.34, 0.5), "medbag_r", bevel=0.05, segs=3)
    box(m, E, "Airway bag", (0.18, NJ_FLOOR + 0.15, -0.6), (0.26, 0.28, 0.4), "medbag_g", bevel=0.05, segs=3)
    tube(m, E, "Drip pole", [(-0.05, NJ_FLOOR + 0.6, 0.1), (-0.05, 2.0, 0.1)], 0.012, "chrome", 8)
    box(m, E, "Drip bag", (-0.05, 1.82, 0.16), (0.1, 0.16, 0.03), "sheet", bevel=0.01)
    nj_seat(m, E, 0.45, 0.3, -1, mat="seat_ns", back=0.75)


def nj_fit_police(m, H):
    """Police fit (with the camera): the nose camera ball, and a tactical flight officer's workstation."""
    E = "eq_flir"
    # camera turret under the nose
    cyl(m, E, "Turret mount", (0, 0.62, -3.05), (0, 0.5, -3.05), 0.12, 0.1, "darkmetal", 16)
    ellipsoid(m, E, "Camera ball", (0, 0.32, -3.05), (0.2, 0.2, 0.2), "flirgrey", 20, 12)
    box(m, E, "Camera window", (0, 0.32, -3.24), (0.18, 0.12, 0.02), "glass")
    box(m, E, "Laser window", (0.09, 0.22, -3.22), (0.06, 0.06, 0.02), "glass")
    # workstation in the cabin, facing the left sliding door
    box(m, E, "Console", (-0.55, NJ_FLOOR + 0.45, 0.4), (0.3, 0.9, 0.9), "panel", bevel=0.015)
    Fc = Face(m, E, (-0.399, NJ_FLOOR + 0.92, 0.4), (0, 0, -1), (0, 1, 0), (1, 0, 0))
    Fc.screen(-0.2, 0.1, 0.32, 0.24, "mfd_cam")
    Fc.screen(0.2, 0.1, 0.32, 0.24, "mfd_map")
    for i in range(6): Fc.button(-0.3 + i * 0.12, -0.12, "btn_w", 0.04, 0.025)
    box(m, E, "Hand controller", (-0.25, NJ_FLOOR + 0.62, 0.4), (0.2, 0.06, 0.24), "panel", bevel=0.02)
    cyl(m, E, "Joystick", (-0.25, NJ_FLOOR + 0.65, 0.4), (-0.25, NJ_FLOOR + 0.78, 0.4), 0.02, 0.025, "grip", 10)
    nj_seat(m, E, 0.05, 0.45, 1, mat="seat_ns", back=0.8)
    box(m, E, "Equipment rack", (0.55, NJ_FLOOR + 0.5, 1.3), (0.4, 1.0, 0.6), "panel", bevel=0.012)


def nj_kit(m, H):
    # searchlight on the right side under the cabin
    S = "eq_searchlight"
    box(m, S, "Searchlight mount", (0.72, 0.56, -2.2), (0.2, 0.12, 0.2), "darkmetal", bevel=0.01)
    cyl(m, S, "Searchlight body", (0.72, 0.38, -2.12), (0.72, 0.38, -2.42), 0.17, 0.17, "flirgrey", 20)
    cyl(m, S, "Searchlight lens", (0.72, 0.38, -2.42), (0.72, 0.38, -2.44), 0.15, 0.15, "lens", 20)
    # rescue hoist above the right cabin door
    Ho = "eq_hoist"
    box(m, Ho, "Hoist boom", (0.95, 2.18, -0.5), (0.5, 0.1, 0.12), "hoistyellow", bevel=0.02)
    cyl(m, Ho, "Hoist", (1.18, 2.12, -0.5), (1.18, 2.12, -0.82), 0.12, 0.12, "hoistyellow", 18)
    tube(m, Ho, "Hoist cable", [(1.18, 2.0, -0.6), (1.18, 1.7, -0.6)], 0.006, "darkmetal", 6)
    box(m, Ho, "Hook", (1.18, 1.66, -0.6), (0.05, 0.08, 0.03), "darkmetal")
    # emergency floats packed on the skids
    Fl = "eq_floats"
    for x in (-1.08, 1.08):
        cyl(m, Fl, "Float pack", (x, 0.13, -2.4), (x, 0.13, 1.1), 0.09, 0.09, "floatpack", 14)


def nj_controls(m):
    ctl = []
    for sx in (-1, 1):
        tag = "l" if sx < 0 else "r"; x = sx * 0.42
        p = "ctl_cyclic_" + tag; base = (x, NJ_FLOOR + 0.06, -2.4)
        cyl(m, p, "Cyclic boot", base, (x, NJ_FLOOR + 0.12, -2.4), 0.07, 0.05, "rubber", 16)
        tube(m, p, "Cyclic stick", [base, (x, NJ_FLOOR + 0.35, -2.43), (x, NJ_FLOOR + 0.58, -2.5)], 0.016, "darkmetal", 10)
        cyl(m, p, "Cyclic grip", (x, NJ_FLOOR + 0.56, -2.5), (x, NJ_FLOOR + 0.72, -2.54), 0.028, 0.03, "grip", 16)
        for i in range(3): cyl(m, p, "Grip switch", (x + (i - 1) * 0.015, NJ_FLOOR + 0.72, -2.54), (x + (i - 1) * 0.015, NJ_FLOOR + 0.735, -2.55), 0.006, 0.005, "red" if i == 1 else "chrome", 8)
        ctl.append({"part": p, "kind": "cyclic", "pivot": base})
        p = "ctl_collective_" + tag; px = x - 0.3; piv = (px, NJ_FLOOR + 0.12, -1.85); tip = (px, NJ_FLOOR + 0.3, -2.4)
        tube(m, p, "Collective lever", [piv, (px, NJ_FLOOR + 0.2, -2.1), tip], 0.02, "darkmetal", 10)
        box(m, p, "Collective head", (px, NJ_FLOOR + 0.33, -2.42), (0.06, 0.07, 0.1), "panel", bevel=0.008)
        ctl.append({"part": p, "kind": "collective", "pivot": piv})
        for kind, dx in (("pedalL", -0.12), ("pedalR", 0.12)):
            p = "ctl_%s_%s" % (kind, tag); px = x + dx
            tube(m, p, "Pedal arm", [(px, NJ_FLOOR, -2.92), (px, NJ_FLOOR + 0.12, -2.98), (px, NJ_FLOOR + 0.2, -2.95)], 0.014, "metal", 8)
            box(m, p, "Pedal", (px, NJ_FLOOR + 0.15, -2.97), (0.09, 0.16, 0.018), "seatframe", bevel=0.005)
            ctl.append({"part": p, "kind": kind, "pivot": (px, NJ_FLOOR, -2.95)})
    return ctl


def build_nightjar():
    m = Model(); H = Hull(NJ_FUS)
    nj_exterior(m, H); nj_rotor(m); nj_interior(m); nj_fit_hems(m); nj_fit_police(m, H); nj_kit(m, H)
    ctl = nj_controls(m)
    doors = []
    for side in (1, -1):
        s = "r" if side > 0 else "l"
        nj_door(m, H, "door_crew_" + s, side, NJ_CREW_DOOR, NJ_CREW_WIN, -1.48)
        piv = H.P(NJ_CREW_DOOR[0], H.th_at_y(NJ_CREW_DOOR[0], 1.4, side), 1.0)
        doors.append({"part": "door_crew_" + s, "group": "crew_" + s, "kind": "hinge", "pivot": list(piv), "axis": [0, 1, 0], "open": 1.25 * side})
        nj_door(m, H, "door_cabin_" + s, side, NJ_CABIN_DOOR, NJ_CABIN_WIN, 0.15)
        doors.append({"part": "door_cabin_" + s, "group": "cabin_" + s, "kind": "slide", "out": [side * 0.075, 0, 0], "move": [0, 0, NJ_SLIDE]})
    doors += nj_rear_doors(m, H)
    # a little cockpit-only detail: the inside of the windscreen frame
    tube(m, "cockpit", "Windscreen centre post (inside)", [H.P(z, math.pi / 2, 0.96) for z in (-3.6, -3.2, -2.8, -2.56)], 0.02, "frame", 6, steps=2)
    pw, ph, cy0, cz, tilt = 0.62, 0.17, 1.16, -2.94, 0.0
    meta = {
        "format": "RCM1", "model": "Nightjar N4", "refRotorDiameter": 11.0, "generator": VERSION,
        "points": {
            "mastY": 3.42, "tailPos": [0.0, 2.02, 6.62], "eye": [0.42, 1.78, -1.92], "fuel": [0.95, 1.05, 1.2],
            "lights": {"navL": [-1.07, 2.4, 5.48], "navR": [1.07, 2.4, 5.48], "tail": [0, 2.02, 7.33], "beacon": [0, 3.1, 6.87],
                       "strobes": [[0, 3.1, 6.9], [0, 0.56, -0.4]], "landing": {"pos": [0, 0.64, -3.26], "dir": [0, -0.3, -1]},
                       "search": {"pos": [0.72, 0.38, -2.46], "dir": [0, -0.7, -0.7]}, "cabin": [0, 1.6, -0.2]},
            "panelQuad": [[-pw, cy0 - ph, cz + tilt], [pw, cy0 - ph, cz + tilt], [pw, cy0 + ph, cz - tilt], [-pw, cy0 + ph, cz - tilt]],
            "exhausts": [[-0.56, 2.5, 2.05, -0.3, 0.1, 1], [0.56, 2.5, 2.05, 0.3, 0.1, 1]],
            "floorY": NJ_FLOOR,
            # where the crew sit (x, y of the seat cushion, z) and which door each uses
            "seats": {"pilot": [0.42, NJ_FLOOR + 0.465, -1.95, "crew_r", -1], "crew": [-0.42, NJ_FLOOR + 0.465, -1.95, "crew_l", -1],
                      "medic": [0.45, NJ_FLOOR + 0.465, 0.3, "cabin_r", -1], "winchman": [-0.45, NJ_FLOOR + 0.465, -1.15, "cabin_l", 1]},
            # for each door: a point just inside and one just outside on the ground
            "doorways": {"crew_r": [[0.45, -1.9], [1.45, -1.9]], "crew_l": [[-0.45, -1.9], [-1.45, -1.9]],
                         "cabin_r": [[0.35, -0.45], [1.5, -0.45]], "cabin_l": [[-0.35, -0.45], [-1.5, -0.45]], "rear": [[0.0, 1.6], [0.0, 2.9]]},
        },
        "controls": [{"part": c["part"], "kind": c["kind"], "pivot": list(c["pivot"])} for c in ctl],
        "tailRotor": {"r": 0.5, "blades": 10, "tip": [0.2, 0.21, 0.22], "col": [0.16, 0.17, 0.18]},
        "equipment": {"medical": "fit_hems", "flir": "eq_flir", "searchlight": "eq_searchlight", "hoist": "eq_hoist", "floats": "eq_floats"},
        "interior": "interior",
        "doors": doors,
    }
    build_tail_rotor(m, meta["tailRotor"])
    return m, meta



MODELS = {
    "kestrel": {"file": "kestrel_k2", "build": lambda: build_kestrel(), "shots": "kestrel"},
    "northstar": {"file": "northstar_175", "build": lambda: build_twin(False), "shots": "twin"},
    "guardian": {"file": "guardian_s9", "build": lambda: build_twin(True), "shots": "twin"},
    "landmarks": {"file": "landmarks", "build": lambda: build_landmarks(), "shots": "twin"},
    "vehicles": {"file": "vehicles", "build": lambda: build_vehicles(), "shots": "twin"},
    "trees": {"file": "trees", "build": lambda: build_trees(), "shots": "twin"},
    "nightjar": {"file": "nightjar_n4", "build": lambda: build_nightjar(), "shots": "nightjar"},
}


# ============================================================================ the game file (.rcm)
def write_rcm(path, parts, meta):
    """parts: list of (name, pos float32[n,3] game coords, nrm float32[n,3], rgb float[n,3], alpha float[n],
    ao float[n], slot uint8[n], idx uint32[m])."""
    import numpy as np
    parts = [p for p in parts if p[0] not in GLTF_ONLY_PARTS]
    allp = np.concatenate([p[1] for p in parts], axis=0)
    lo, hi = allp.min(axis=0) - 1e-4, allp.max(axis=0) + 1e-4
    meta = dict(meta); meta["bounds"] = {"min": [float(x) for x in lo], "max": [float(x) for x in hi]}
    meta["parts"] = []
    blobs = []
    def pad4(b): return b + b"\0" * ((4 - len(b) % 4) % 4)
    for prt in parts:
        name, pos, nrm, rgb, alpha, ao, slot, idx = prt[:8]
        layer = prt[8] if len(prt) > 8 else None
        textured = layer is not None and bool(np.any(layer))
        n = len(pos); i32 = n > 65535
        q = np.clip(np.round((pos - lo) / (hi - lo) * 65535), 0, 65535).astype("<u2")
        nn = np.clip(np.round(nrm * 127), -127, 127).astype("i1")
        nn4 = np.zeros((n, 4), "i1"); nn4[:, :3] = nn
        col = np.zeros((n, 4), "u1")
        col[:, :3] = np.clip(np.round(rgb * 255), 0, 255); col[:, 3] = np.clip(np.round(alpha * 255), 0, 255)
        ex = np.zeros((n, 4), "u1"); ex[:, 0] = np.clip(np.round(ao * 255), 0, 255); ex[:, 1] = slot
        uvb = b""
        if textured:
            ex[:, 2] = layer
            uvb = box_uv(pos.astype(np.float64), nrm.astype(np.float64), layer.astype(int)).astype("<f4").tobytes()
        ib = idx.astype("<u4" if i32 else "<u2")
        blob = pad4(q.tobytes()) + nn4.tobytes() + col.tobytes() + ex.tobytes() + uvb + pad4(ib.tobytes())
        blobs.append(blob)
        pm = {"name": name, "vc": int(n), "ic": int(len(idx)), "i32": bool(i32)}
        if textured: pm["uv"] = True
        meta["parts"].append(pm)
    js = pad4(json.dumps(meta, separators=(",", ":")).encode("utf-8"))
    with open(path, "wb") as f:
        f.write(b"RCM1"); f.write(struct.pack("<I", len(js))); f.write(js)
        for b in blobs: f.write(b)
    return sum(len(b) for b in blobs) + len(js) + 8


def parts_from_model_plain(m):
    """No Blender: triangles straight from the generated geometry, smooth normals with hard edges, no shading bake."""
    import numpy as np
    groups = {}
    for o in m.objs:
        if not o.f: continue
        g = groups.setdefault(o.part, {"pos": [], "nrm": [], "mat": [], "idx": []})
        V = np.array(o.v, dtype=float)
        tris, tmat = [], []
        for f, mk in zip(o.f, o.m):
            for k in range(1, len(f) - 1): tris.append((f[0], f[k], f[k + 1])); tmat.append(mk)
        T = np.array(tris); fn = np.cross(V[T[:, 1]] - V[T[:, 0]], V[T[:, 2]] - V[T[:, 0]])
        fl = np.linalg.norm(fn, axis=1); ok = fl > 1e-14; T, fn, fl = T[ok], fn[ok], fl[ok]; tmat = [t for t, k in zip(tmat, ok) if k]
        fu = fn / fl[:, None]
        vn = np.zeros_like(V)
        for c in range(3): np.add.at(vn, T[:, c], fn)
        vn /= np.maximum(np.linalg.norm(vn, axis=1), 1e-12)[:, None]
        cos_sharp = math.cos(math.radians(o.o["sharp"]))
        base = len(g["pos"])
        for ti, t in enumerate(T):
            for c in range(3):
                n = vn[t[c]] if (o.o["smooth"] and np.dot(vn[t[c]], fu[ti]) > cos_sharp) else fu[ti]
                g["pos"].append(V[t[c]]); g["nrm"].append(n); g["mat"].append(tmat[ti])
            g["idx"].extend([base, base + 1, base + 2]); base += 3
    parts = []
    for name, g in groups.items():
        pos = np.array(g["pos"]); nrm = np.array(g["nrm"])
        rgb = np.array([MATS[k][:3] for k in g["mat"]]); alpha = np.array([mat_alpha(k) for k in g["mat"]])
        slot = np.array([MATS[k][4] for k in g["mat"]], dtype="u1")
        # weld identical corners to keep the file small
        key = np.concatenate([np.round(pos * 1e4), np.round(nrm * 100), np.array([[hash(k) % 100000] for k in g["mat"]])], axis=1)
        _, first, inv = np.unique(key, axis=0, return_index=True, return_inverse=True)
        inv = inv.reshape(-1)
        layer = np.array([tex_layer(k) for k in g["mat"]], dtype="u1")
        parts.append((name, pos[first].astype("f4"), nrm[first].astype("f4"), rgb[first], alpha[first], np.ones(len(first)), slot[first], inv[np.array(g["idx"])].astype("u4"), layer[first]))
    return parts


# ============================================================================ Blender
def run_blender():
    import bpy, bmesh
    import numpy as np
    from mathutils import Vector, Matrix
    from mathutils.bvhtree import BVHTree

    log_lines = []
    def log(*a):
        s = " ".join(str(x) for x in a); print(s); log_lines.append(s)

    def out_dir():
        cands = [r"F:\Rotorcraft Blender"]
        try:
            here = os.path.dirname(os.path.abspath(__file__))
            if os.path.isdir(here): cands.insert(0 if not os.path.isdir(cands[0]) else 1, here)
        except Exception: pass
        for t in bpy.data.texts:
            if t.filepath and t.filepath.lower().endswith("rotorcraft_models.py"):
                d = os.path.dirname(bpy.path.abspath(t.filepath))
                if os.path.isdir(d): cands.insert(1, d)
        cands.append(os.path.expanduser("~"))
        for c in cands:
            if c and os.path.isdir(c): return c
        return os.getcwd()

    OUT = out_dir()
    t_start = time.time()
    log(VERSION, "| Blender", bpy.app.version_string, "| output folder:", OUT)
    if bpy.app.version < (4, 1, 0):
        log("This script needs Blender 4.1 or newer. Please update Blender (blender.org) and run it again.")
        raise RuntimeError("Blender 4.1 or newer needed")

    def to_bl(p): return (p[0], -p[2], p[1])                      # game (x, y up, z aft) -> Blender (x, y fwd... nose +Y, z up)

    def finish_log():
        try:
            with open(os.path.join(OUT, "rotorcraft_build_log.txt"), "w", encoding="utf-8") as f: f.write("\n".join(log_lines) + "\n")
        except Exception as e: print("could not write the log:", e)

    def build_one(key):
        spec = MODELS[key]
        log("\n==== " + spec["file"])
        # ---- clear the scene
        for ob in list(bpy.data.objects): bpy.data.objects.remove(ob, do_unlink=True)
        for coll in list(bpy.data.collections): bpy.data.collections.remove(coll)
        for me in list(bpy.data.meshes): bpy.data.meshes.remove(me)
        scene = bpy.context.scene
        scene.unit_settings.system = "METRIC"

        # ---- materials (preview colours read the baked 'Preview' colour attribute)
        mats = {}
        for key in MATS:
            mt = bpy.data.materials.get("RC " + key) or bpy.data.materials.new("RC " + key)
            mt["rc_key"] = key
            mt.use_nodes = True
            nt = mt.node_tree; bsdf = nt.nodes.get("Principled BSDF")
            r, g, b, gloss, slot, emis = MATS[key]
            if bsdf:
                bsdf.inputs["Roughness"].default_value = max(0.08, 1.0 - gloss * 0.85)
                bsdf.inputs["Metallic"].default_value = 0.6 if key in ("metal", "chrome", "bladele") else 0.0
                try:
                    attr = nt.nodes.new("ShaderNodeVertexColor"); attr.layer_name = "Preview"
                    nt.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])
                    if emis:
                        nt.links.new(attr.outputs["Color"], bsdf.inputs["Emission Color"]); bsdf.inputs["Emission Strength"].default_value = 2.0
                    if key == "glass":
                        bsdf.inputs["Roughness"].default_value = 0.05
                except Exception as e: log("material note:", key, e)
            pr = mat_preview_rgb(key); mt.diffuse_color = (pr[0], pr[1], pr[2], 1.0)
            mats[key] = mt

        # ---- collections
        root = bpy.data.collections.new(spec["file"]); scene.collection.children.link(root)
        src_coll = bpy.data.collections.new("Source (editable, with modifiers)"); root.children.link(src_coll)
        fin_coll = bpy.data.collections.new("Game export (baked)"); root.children.link(fin_coll)
        part_colls = {}

        # ---- build geometry
        t0 = time.time()
        t_model = time.time()
        model, meta = spec["build"]()
        log("geometry: %d objects built in %.1fs" % (len(model.objs), time.time() - t0))
        sources = []
        for o in model.objs:
            keys = sorted(set(o.m)); kidx = {k: i for i, k in enumerate(keys)}
            me = bpy.data.meshes.new(o.name)
            me.from_pydata([to_bl(p) for p in o.v], [], [list(f) for f in o.f])
            for k in keys: me.materials.append(mats[k])
            me.polygons.foreach_set("material_index", [kidx[k] for k in o.m])
            me.polygons.foreach_set("use_smooth", [o.o["smooth"]] * len(o.f))
            me.validate(clean_customdata=False); me.update()
            # hard edges by angle
            bm = bmesh.new(); bm.from_mesh(me)
            bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
            lim = math.radians(o.o["sharp"])
            for e in bm.edges:
                if len(e.link_faces) == 2 and e.calc_face_angle(0) > lim: e.smooth = False
            bm.to_mesh(me); bm.free()
            ob = bpy.data.objects.new(o.name, me)
            pc = part_colls.get(o.part)
            if pc is None:
                pc = bpy.data.collections.new("src " + o.part); src_coll.children.link(pc); part_colls[o.part] = pc
            pc.objects.link(ob)
            ob["rc_part"] = o.part; ob["rc_ao"] = bool(o.o["ao"])
            if o.o["bevel"] > 0:
                bv = ob.modifiers.new("Bevel", "BEVEL"); bv.width = o.o["bevel"]; bv.segments = o.o["segs"]
                bv.limit_method = "ANGLE"; bv.angle_limit = math.radians(30)
                try: bv.harden_normals = True
                except Exception: pass
                wn = ob.modifiers.new("WeightedNormal", "WEIGHTED_NORMAL"); wn.keep_sharp = True
            if o.o["subsurf"] > 0:
                ss = ob.modifiers.new("Subdivision", "SUBSURF"); ss.levels = ss.render_levels = o.o["subsurf"]
            sources.append(ob)

        # ---- apply modifiers into the export copies
        dg = bpy.context.evaluated_depsgraph_get()
        finals = {}
        fin_part_colls = {}
        for ob in sources:
            ev = ob.evaluated_get(dg)
            me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
            me.name = ob.name + " (game)"
            nob = bpy.data.objects.new(me.name, me); nob["rc_part"] = ob["rc_part"]; nob["rc_ao"] = ob["rc_ao"]
            pc = fin_part_colls.get(ob["rc_part"])
            if pc is None:
                pc = bpy.data.collections.new(ob["rc_part"]); fin_coll.children.link(pc); fin_part_colls[ob["rc_part"]] = pc
            pc.objects.link(nob)
            if ob["rc_part"] == "blades": nob.location = (0, 0, meta["points"]["mastY"])
            if ob["rc_part"] == "tail_rotor": nob.location = to_bl(meta["points"]["tailPos"])
            if meta.get("layout") and ob["rc_part"] in meta["layout"]: nob.location = to_bl(meta["layout"][ob["rc_part"]])   # spread out for viewing
            finals.setdefault(ob["rc_part"], []).append(nob)
        # ---- registration: Blender's font, extruded 0.8 mm and wrapped round the (round) tail boom, both sides.
        # Read from either side it runs towards the nose on the right and towards the tail on the left, as painted
        # registrations do; it joins the body part, so it is baked and written to the .rcm like everything else.
        reg = meta.get("registration")
        if reg:
            Hb = Hull(reg["boom"])
            for side in (1, -1):
                cu = bpy.data.curves.new("Registration", "FONT")
                cu.body = reg["text"]; cu.size = reg["height"] / 0.72; cu.extrude = 0.0004
                cu.align_x = "CENTER"; cu.align_y = "CENTER"
                tob = bpy.data.objects.new("Registration text", cu); scene.collection.objects.link(tob)
                me = bpy.data.meshes.new_from_object(tob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
                bpy.data.objects.remove(tob); bpy.data.curves.remove(cu)
                co = np.zeros(len(me.vertices) * 3); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
                z = reg["z"] - side * co[:, 0]
                y = reg["y"] + co[:, 1]
                rad = np.array([Hb.w(v) for v in z]); yc = np.array([Hb.yc(v) for v in z])
                x = side * (np.sqrt(np.maximum(rad * rad - (y - yc) ** 2, 1e-8)) + 0.0006 + (co[:, 2] + 0.0004))
                me.vertices.foreach_set("co", np.stack([x, -z, y], axis=1).astype(np.float32).ravel())   # game -> Blender axes
                me.materials.clear(); me.materials.append(mats[reg["mat"]])
                me.polygons.foreach_set("material_index", [0] * len(me.polygons))
                me.update()
                nob = bpy.data.objects.new("Registration (game)", me); nob["rc_part"] = "body"; nob["rc_ao"] = True
                fin_part_colls["body"].objects.link(nob); finals["body"].append(nob)
        src_coll.hide_viewport = True; src_coll.hide_render = True
        try:
            bpy.context.view_layer.layer_collection.children[root.name].children[src_coll.name].exclude = True
        except Exception: pass
        log("modifiers applied: %d export meshes" % sum(len(v) for v in finals.values()))

        # ---- nothing inside pokes through the skin: every vertex of the cockpit, cabin and controls that lies
        # outside the outer skin (hull, glazing, closed doors) or within a few millimetres of it is pulled back just
        # inside it. Parts meant to be seen outside from the seat ("(view)") are left alone.
        def fix_clipping():
            if meta.get("library"): return
            inner_parts = [p for p in finals if p in ("cockpit", "interior", "cockpit_only") or p.startswith("ctl_")]
            ref = []
            for p in ["body"] + [q for q in finals if q.startswith("door")]:
                for ob in finals.get(p, []):
                    me = ob.data
                    if len(me.polygons) < 40: continue
                    a = sum(f.area for f in me.polygons)
                    if a < (1.2 if p == "body" else 0.3): continue
                    if any(s in ob.name.lower() for s in ("skid", "tube", "strut", "step", "rail", "post", "frame", "line", "seam", "rivet", "fairing", "boom", "fin", "stabil", "cowl", "tank", "engine", "sponson", "pylon", "lining", "console", "panel", "tyre", "wheel", "exhaust", "seat")): continue
                    ref.append(ob)
            log("clipping reference skin: " + ", ".join(o.name for o in ref))
            if not ref or not inner_parts: return
            bm = bmesh.new(); spans = []
            for ob in ref:
                n0 = len(bm.faces)
                tmp = ob.data.copy(); tmp.transform(Matrix.Translation(ob.location)); bm.from_mesh(tmp); bpy.data.meshes.remove(tmp)
                bm.faces.ensure_lookup_table()
                # which way do this mesh's faces point: away from its middle (outward) or towards it?
                fs = bm.faces[n0:]
                c = sum((f.calc_center_median() for f in fs), Vector()) / max(1, len(fs))
                out = sum(1 for f in fs if f.normal.dot(f.calc_center_median() - c) > 0) / max(1, len(fs))
                spans.append((n0, len(bm.faces), 1.0 if out >= 0.5 else -1.0))
            bm.faces.ensure_lookup_table(); bm.normal_update()
            tree = BVHTree.FromBMesh(bm)
            sign = np.ones(len(bm.faces))
            for a, b, s in spans: sign[a:b] = s
            bm.free()
            moved = {}
            for p in inner_parts:
                for ob in finals[p]:
                    if "(view)" in ob.name: continue
                    me = ob.data; off = Vector(ob.location); out = []
                    for v in me.vertices:
                        w = v.co + off
                        hit = tree.find_nearest(w, 0.35)
                        if hit[0] is None: continue
                        loc, nrm, idx, dist = hit
                        nrm = nrm * sign[idx]
                        depth = (w - loc).dot(nrm)
                        if depth > -0.006: out.append((v, loc, nrm, depth))
                    if not out: continue
                    lo = Vector((min(v.co.x for v in me.vertices), min(v.co.y for v in me.vertices), min(v.co.z for v in me.vertices)))
                    hi = Vector((max(v.co.x for v in me.vertices), max(v.co.y for v in me.vertices), max(v.co.z for v in me.vertices)))
                    if (hi - lo).length < 0.8 and len(out) > 0.25 * len(me.vertices):
                        # a small fitting (headrest, locker, handle) that pokes through: move it in whole, unchanged
                        nav = sum((o[2] for o in out), Vector()).normalized()
                        push = max(o[3] for o in out) + 0.008
                        for v in me.vertices: v.co -= nav * push
                        moved[ob.name + " (moved %.0f mm)" % (push * 1000)] = len(out)
                    else:
                        for v, loc, nrm, depth in out: v.co = loc - nrm * 0.008 - off
                        moved[ob.name] = len(out)
                    me.update()
            if moved:
                top = sorted(moved.items(), key=lambda kv: -kv[1])[:8]
                log("clipping fixed: %d vertices in %d objects (%s)" % (sum(moved.values()), len(moved), ", ".join("%s %d" % kv for kv in top)))
        try: fix_clipping()
        except Exception as e:
            import traceback; log("clipping check failed:", repr(e)); log(traceback.format_exc())

        # ---- bake ambient occlusion per vertex with ray casts
        def smooth_over_edges(me, ao, iters):
            """Average each vertex with its neighbours a few times, so the bake reads as soft shading, not speckle."""
            ne = len(me.edges)
            if ne == 0: return ao
            ev = np.zeros(ne * 2, dtype=np.int64); me.edges.foreach_get("vertices", ev); ev = ev.reshape(-1, 2)
            deg = np.bincount(ev.ravel(), minlength=len(ao)).astype(float)
            for _ in range(iters):
                acc = np.zeros(len(ao)); np.add.at(acc, ev[:, 0], ao[ev[:, 1]]); np.add.at(acc, ev[:, 1], ao[ev[:, 0]])
                ao = 0.5 * ao + 0.5 * np.where(deg > 0, acc / np.maximum(deg, 1), ao)
            return ao

        def bvh_for(objs, ground=False):
            bm = bmesh.new()
            for ob in objs: bm.from_mesh(ob.data)
            if ground:   # the ground the building stands on, so walls darken where they meet it
                vs = [bm.verts.new(c) for c in ((-400, -400, -0.03), (400, -400, -0.03), (400, 400, -0.03), (-400, 400, -0.03))]
                bm.faces.new(vs)
            t = BVHTree.FromBMesh(bm); bm.free(); return t
        # cosine-weighted hemisphere directions (Fibonacci), in a local frame with +Z as the normal
        NR = 96; dirs = []
        for i in range(NR):
            u = (i + 0.5) / NR; r = math.sqrt(u); a = i * 2.399963229728653
            dirs.append((r * math.cos(a), r * math.sin(a), math.sqrt(max(0.0, 1 - u))))
        dirs = [tuple(float(x) for x in d) for d in dirs]
        groups = {"body": ["body"], "blades": ["blades"], "cockpit": [p for p in finals if p == "cockpit" or p.startswith("ctl_")]}
        doors_parts = [p for p in finals if p.startswith("door")]
        groups["body"] = ["body"] + doors_parts                      # doors and the skin round them shade alike
        for p in finals:
            if p.startswith("eq_"): groups[p] = [p, "body"]
            if p.startswith("door"): groups[p] = [p, "body"]
        ao_vals = {}
        for part, objs in finals.items():
            exterior = part == "body" or part.startswith("door") or part.startswith("eq_")   # painted outside: a gentler bake
            occ_parts = groups.get(part, ["cockpit"] + [p for p in finals if p.startswith("ctl_")] if part.startswith("ctl_") else [part])
            occ = bvh_for([o for p in occ_parts for o in finals.get(p, [])], ground=bool(meta.get("library")) and not part.startswith("platform"))
            maxd = meta.get("aoDistance") or (0.5 if part == "body" else 0.35 if part == "blades" else 0.45)
            t0 = time.time(); nv_total = 0; done_v = 0; est_v = sum(len(o.data.vertices) for o in objs)
            for ob in objs:
                me = ob.data; n = len(me.vertices); nv_total += n
                co = np.zeros(n * 3); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
                vn = np.zeros(n * 3); me.vertices.foreach_get("normal", vn); vn = vn.reshape(-1, 3)
                ao = np.ones(n)
                if ob["rc_ao"]:
                    for i in range(n):
                        if (done_v + i) % 4000 == 0:
                            print("  baking shading: %s %d / ~%d vertices" % (part, done_v + i, est_v)); sys.stdout.flush()
                        nn = Vector((float(vn[i][0]), float(vn[i][1]), float(vn[i][2])))
                        if nn.length < 1e-6: continue
                        nn.normalize(); t1 = nn.orthogonal().normalized(); t2 = nn.cross(t1)
                        p = Vector((float(co[i][0]), float(co[i][1]), float(co[i][2]))) + nn * 0.003
                        acc = 0.0
                        for d in dirs:
                            w = t1 * d[0] + t2 * d[1] + nn * d[2]
                            hit = occ.ray_cast(p, w, maxd)
                            if hit[0] is not None: acc += 1.0 - hit[3] / maxd
                        ao[i] = max(0.4, 1.0 - 0.9 * acc / NR) if not exterior else max(0.62, 1.0 - 0.6 * acc / NR)
                    ao = smooth_over_edges(me, ao, 3)
                ao_vals[ob.name] = ao; done_v += n
            log("shading baked for %-18s %6d vertices in %.1fs" % (part, nv_total, time.time() - t0))

        # ---- preview colour attribute (material colour x baked shading), so Blender shows what the game will
        for part, objs in finals.items():
            for ob in objs:
                me = ob.data; ao = ao_vals[ob.name]
                attr = me.color_attributes.get("Preview") or me.color_attributes.new("Preview", "FLOAT_COLOR", "CORNER")
                nl = len(me.loops); lv = np.zeros(nl, dtype=np.int64); me.loops.foreach_get("vertex_index", lv)
                lm = np.zeros(nl, dtype=np.int64)
                pm = np.zeros(len(me.polygons), dtype=np.int64); me.polygons.foreach_get("material_index", pm)
                ls = np.zeros(len(me.polygons), dtype=np.int64); me.polygons.foreach_get("loop_start", ls)
                lt = np.zeros(len(me.polygons), dtype=np.int64); me.polygons.foreach_get("loop_total", lt)
                lm = np.repeat(pm, lt)
                keys = [ms["rc_key"] if ms else "paint" for ms in me.materials]
                prev = np.array([mat_preview_rgb(k) for k in keys])[lm] * ao[lv][:, None]
                emis = np.array([MATS[k][5] for k in keys], dtype=bool)[lm]
                prev[emis] = np.array([mat_preview_rgb(k) for k in keys])[lm][emis]
                rgba = np.concatenate([prev, np.ones((nl, 1))], axis=1)
                attr.data.foreach_set("color", rgba.astype(np.float32).ravel())
                try: me.color_attributes.active_color = attr; me.color_attributes.render_color_index = me.color_attributes.find("Preview")
                except Exception: pass

        # ---- write the game file
        parts = []
        first = ["body", "blades", "cockpit"]
        part_order = sorted(finals) if meta.get("library") else [p for p in first if p in finals] + sorted(p for p in finals if p not in first)
        for part in part_order:
            if part in GLTF_ONLY_PARTS: continue
            objs = finals.get(part, [])
            P, N, RGB, A, AO, S, I, LY = [], [], [], [], [], [], [], []; base = 0
            for ob in objs:
                me = ob.data; me.calc_loop_triangles()
                nt = len(me.loop_triangles)
                if nt == 0: continue
                tl = np.zeros(nt * 3, dtype=np.int64); me.loop_triangles.foreach_get("loops", tl)
                tm = np.zeros(nt, dtype=np.int64); me.loop_triangles.foreach_get("material_index", tm)
                nl = len(me.loops); lv = np.zeros(nl, dtype=np.int64); me.loops.foreach_get("vertex_index", lv)
                cn = np.zeros(nl * 3); me.corner_normals.foreach_get("vector", cn); cn = cn.reshape(-1, 3)
                co = np.zeros(len(me.vertices) * 3); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
                keys = [ms["rc_key"] if ms else "paint" for ms in me.materials]
                cm = np.repeat(tm, 3)                                  # material per triangle corner
                cv = lv[tl]; cnn = cn[tl]
                key = np.concatenate([cv[:, None].astype(float), np.round(cnn * 120), cm[:, None].astype(float)], axis=1)
                _, first, inv = np.unique(key, axis=0, return_index=True, return_inverse=True); inv = inv.reshape(-1)
                vb = co[cv[first]]; nb = cnn[first]
                P.append(np.stack([vb[:, 0], vb[:, 2], -vb[:, 1]], axis=1))            # Blender -> game axes
                nb = nb / np.maximum(np.linalg.norm(nb, axis=1), 1e-9)[:, None]
                N.append(np.stack([nb[:, 0], nb[:, 2], -nb[:, 1]], axis=1))
                km = cm[first]
                RGB.append(np.array([MATS[k][:3] for k in keys])[km]); A.append(np.array([mat_alpha(k) for k in keys])[km])
                S.append(np.array([MATS[k][4] for k in keys], dtype=np.uint8)[km])
                aov = ao_vals[ob.name][cv[first]].copy(); emis = np.array([MATS[k][5] for k in keys], dtype=bool)[km]; aov[emis] = 1.0
                AO.append(aov); I.append(inv + base); base += len(first)
                LY.append(np.array([tex_layer(k) for k in keys], dtype=np.uint8)[km])
            if not P: continue
            parts.append((part, np.concatenate(P).astype(np.float32), np.concatenate(N).astype(np.float32), np.concatenate(RGB), np.concatenate(A), np.concatenate(AO), np.concatenate(S), np.concatenate(I).astype(np.uint32), np.concatenate(LY)))
            log("part %-18s %6d vertices %6d triangles" % (part, len(parts[-1][1]), len(parts[-1][7]) // 3))
        meta["blender"] = bpy.app.version_string; meta["ao"] = True
        rcm = os.path.join(OUT, spec["file"] + ".rcm")
        size = write_rcm(rcm, parts, meta)
        log("wrote %s (%.0f KB)" % (rcm, size / 1024))

        # ---- viewport: show the baked colours
        try:
            for win in bpy.context.window_manager.windows:
                for area in win.screen.areas:
                    if area.type == "VIEW_3D":
                        for sp in area.spaces:
                            if sp.type == "VIEW_3D":
                                sp.shading.type = "SOLID"; sp.shading.color_type = "VERTEX"; sp.shading.light = "STUDIO"
        except Exception as e: log("viewport note:", e)

        # ---- preview renders (Workbench, quick)
        try:
            cam_data = bpy.data.cameras.new("Preview camera"); cam = bpy.data.objects.new("Preview camera", cam_data)
            root.objects.link(cam); scene.camera = cam; cam_data.lens = 50
            scene.render.engine = "BLENDER_WORKBENCH"
            sh = scene.display.shading
            sh.light = "STUDIO"; sh.color_type = "VERTEX"; sh.show_cavity = True; sh.show_shadows = True
            scene.render.resolution_x, scene.render.resolution_y = 1600, 900
            scene.render.film_transparent = False
            world = scene.world or bpy.data.worlds.new("World"); scene.world = world; world.color = (0.30, 0.36, 0.42)
            shots = [(n, to_bl(c), to_bl(t), lens) for (n, c, t, lens) in (meta.get("shots") or SHOTS[spec["shots"]])]
            hidden_cockpit = list(fin_part_colls.items())
            for name, loc, target, lens in shots:
                for p, c in hidden_cockpit:
                    c.hide_render = (name != "cockpit" and p == "cockpit") or (name == "cockpit" and (p == "body" or p.startswith("eq_")))
                cam.location = Vector(loc); cam_data.lens = lens
                cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
                scene.render.filepath = os.path.join(OUT, "%s_%s.png" % (spec["file"], name))
                bpy.ops.render.render(write_still=True)
                log("rendered", scene.render.filepath)
            for p, c in hidden_cockpit: c.hide_render = False
        except Exception as e: log("preview renders skipped:", repr(e))

        # ---- physically based materials for the glTF. The previews above read the 'Preview' colour (material
        # colour x baked shading); the .glb instead gets each material's own colour, metalness, roughness, clear
        # coat and transparency, with the baked shading alone in an 'AO' colour layer (COLOR_0, which glTF
        # viewers and Unity multiply into the base colour). The .rcm was written earlier and is unaffected.
        def set_input(bsdf, name, value):
            try: bsdf.inputs[name].default_value = value
            except (KeyError, TypeError): pass

        def gltf_material(mt, key):
            nt = mt.node_tree; bsdf = nt.nodes.get("Principled BSDF")
            if bsdf is None: return
            for l in list(nt.links):
                if l.to_node == bsdf and l.to_socket.name in ("Base Color", "Emission Color"): nt.links.remove(l)
            r, g, b, gloss, slot, emis = MATS[key]
            base = mat_preview_rgb(key)
            if key in GLTF_GLASS:
                tr, tg, tb, alpha, rough = GLTF_GLASS[key]
                set_input(bsdf, "Base Color", (tr, tg, tb, 1.0)); set_input(bsdf, "Metallic", 0.0); set_input(bsdf, "Roughness", rough)
                set_input(bsdf, "Alpha", alpha); set_input(bsdf, "IOR", 1.5)
                try: mt.surface_render_method = "BLENDED"
                except Exception: mt.blend_method = "BLEND"
                mt.use_backface_culling = False                     # seen from inside the cockpit as well
                return
            met, rough, coat = GLTF_PBR.get(key, (0.0, max(0.15, min(0.95, 0.88 - 0.8 * gloss)), 0.0))
            set_input(bsdf, "Base Color", (base[0], base[1], base[2], 1.0)); set_input(bsdf, "Metallic", met); set_input(bsdf, "Roughness", rough)
            set_input(bsdf, "Alpha", 1.0)
            mt.use_backface_culling = True
            set_input(bsdf, "Coat Weight", coat); set_input(bsdf, "Coat Roughness", 0.04)
            if emis:
                set_input(bsdf, "Emission Color", (base[0], base[1], base[2], 1.0)); set_input(bsdf, "Emission Strength", 2.5)
            else:
                set_input(bsdf, "Emission Strength", 0.0)

        used = set()
        for objs in finals.values():
            for ob in objs:
                for ms in ob.data.materials:
                    if ms is not None and "rc_key" in ms: used.add(ms["rc_key"])
        for key in used: gltf_material(mats[key], key)
        for objs in finals.values():
            for ob in objs:
                me = ob.data; ao = ao_vals[ob.name]
                nl = len(me.loops); lv = np.zeros(nl, dtype=np.int64); me.loops.foreach_get("vertex_index", lv)
                pm = np.zeros(len(me.polygons), dtype=np.int64); me.polygons.foreach_get("material_index", pm)
                lt = np.zeros(len(me.polygons), dtype=np.int64); me.polygons.foreach_get("loop_total", lt)
                keys = [ms["rc_key"] if ms else "paint" for ms in me.materials] or ["paint"]
                bright = np.array([MATS[k][5] or k in GLTF_GLASS for k in keys], dtype=bool)[np.repeat(pm, lt)]   # glowing and clear surfaces are not shaded
                g = np.where(bright, 1.0, ao[lv])
                # light weathering: faint mottling in the finish, and road dust and exhaust grime low on the airframe
                co = np.zeros(len(me.vertices) * 3); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)[lv] + np.array(ob.location)
                bx, by, bz = co[:, 0], co[:, 1], co[:, 2]          # Blender axes: z is up
                mottle = 0.5 + 0.25 * (np.sin(bx * 6.1 + by * 2.3) * np.sin(by * 4.7 - bz * 3.9) + np.sin(bz * 7.3 + bx * 3.1) * np.sin(by * 1.9 + 1.7))
                low = np.clip((1.0 - bz) / 0.5, 0, 1) * (bz > 0.02)
                g = np.where(bright, g, g * (1 - 0.025 * mottle) * (1 - 0.07 * low))
                prev = me.color_attributes.get("Preview")              # the previews are rendered: COLOR_0 must be the shading
                if prev is not None: me.color_attributes.remove(prev)
                attr = me.color_attributes.get("AO") or me.color_attributes.new("AO", "FLOAT_COLOR", "CORNER")
                attr.data.foreach_set("color", np.stack([g, g, g, np.ones(nl)], axis=1).astype(np.float32).ravel())
                try: me.color_attributes.active_color = attr; me.color_attributes.render_color_index = me.color_attributes.find("AO")
                except Exception: pass
        log("glTF materials: %d physically based (%d transparent)" % (len(used), len([k for k in used if k in GLTF_GLASS])))

        # ---- texture bake (Cycles on the GPU): the outside of the aircraft gets real textures instead of flat
        # material colours. Each exterior part (body, doors, rotors, fitted kit) is joined into one mesh, unwrapped,
        # and baked: base colour, roughness and metalness, ambient occlusion and the world position of every texel.
        # The bakes are combined here into a weathered finish (dirt in the recesses, mottling in the paint, rain
        # streaks, exhaust soot, dust low down) and one material per part. Glass, lamps and the cockpit stay as
        # they are (the live instruments need their named meshes). Set RC_BAKE=0 to skip.
        def bake_exterior():
            if os.environ.get("RC_BAKE", "1") == "0" or meta.get("library"): return
            t_bake = time.time()
            scene.render.engine = "CYCLES"
            try:
                prefs = bpy.context.preferences.addons["cycles"].preferences
                for kind in ("OPTIX", "CUDA", "HIP", "ONEAPI", "METAL"):
                    try:
                        prefs.compute_device_type = kind; prefs.get_devices()
                        if any(d.type == kind for d in prefs.devices): break
                    except Exception: continue
                for d in prefs.devices: d.use = True
                scene.cycles.device = "GPU"
            except Exception as e: log("bake: GPU not available, using the CPU:", e)
            scene.cycles.use_denoising = False
            world = scene.world or bpy.data.worlds.new("World"); scene.world = world
            world.light_settings.distance = 0.55                        # ambient occlusion reach (m)
            ex_pts = []
            for objs in finals.values():
                for ob in objs:
                    if "xhaust" in ob.name and len(ob.data.vertices):
                        co = np.zeros(len(ob.data.vertices) * 3); ob.data.vertices.foreach_get("co", co); co = co.reshape(-1, 3) + np.array(ob.location)
                        ex_pts.append(co.mean(axis=0))

            def plain(ob):
                keys = [ms["rc_key"] if ms and "rc_key" in ms else "paint" for ms in ob.data.materials]
                return all(k not in GLTF_GLASS and not MATS[k][5] for k in keys)

            def with_output(mt, make):
                """Temporarily drive the material output from an emission shader (make sets its colour); returns an undo."""
                nt = mt.node_tree; out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
                old = [l.from_socket for l in nt.links if l.to_node == out and l.to_socket.name == "Surface"]
                em = nt.nodes.new("ShaderNodeEmission"); em.inputs["Strength"].default_value = 1.0
                extra = make(nt, em) or []
                nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
                def undo():
                    for n in extra: nt.nodes.remove(n)
                    nt.nodes.remove(em)
                    if old: nt.links.new(old[0], out.inputs["Surface"])
                return undo

            def value_noise(p, scale, seed):
                """Smooth lattice noise in 0..1 at world positions p (n x 3)."""
                q = p * scale; i = np.floor(q).astype(np.int64); f = q - i; f = f * f * (3 - 2 * f)
                def h(ix, iy, iz):
                    v = (ix * 73856093) ^ (iy * 19349663) ^ (iz * 83492791) ^ (seed * 2654435761)
                    v = (v ^ (v >> 13)) * 1274126177
                    return ((v ^ (v >> 16)) & 0xFFFF) / 65535.0
                r = 0.0
                for dx in (0, 1):
                    for dy in (0, 1):
                        for dz in (0, 1):
                            w = (f[:, 0] if dx else 1 - f[:, 0]) * (f[:, 1] if dy else 1 - f[:, 1]) * (f[:, 2] if dz else 1 - f[:, 2])
                            r = r + w * h(i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz)
                return r

            def to_srgb(c): return np.where(c <= 0.0031308, 12.92 * c, 1.055 * np.power(np.clip(c, 0, None), 1 / 2.4) - 0.055)

            tex_dir = os.path.join(OUT, spec["file"] + "_textures"); os.makedirs(tex_dir, exist_ok=True)
            for part in list(finals):
                if not (part == "body" or part.startswith("door") or part in ("blades", "tail_rotor") or part.startswith("eq_")): continue
                # small fittings (bolts, rivets, hinges) keep their plain colours: the texture is for real surfaces
                objs = [ob for ob in finals[part] if plain(ob) and len(ob.data.polygons) and sum(p.area for p in ob.data.polygons) > 0.03]
                if len(objs) == 0: continue
                # join (keeps every material slot), then unwrap
                rest = [o for o in finals[part] if o not in objs]
                bpy.ops.object.select_all(action="DESELECT")
                for ob in objs: ob.hide_set(False); ob.hide_render = False; ob.select_set(True)
                bpy.context.view_layer.objects.active = objs[0]
                if len(objs) > 1: bpy.ops.object.join()
                J = bpy.context.view_layer.objects.active; J.name = part + " skin (game)"; J.data.name = J.name
                finals[part] = rest + [J]
                area = sum(p.area for p in J.data.polygons)
                size = 4096 if part == "body" else 2048 if area > 12 else 1024 if area > 3 else 512
                for uv in list(J.data.uv_layers): J.data.uv_layers.remove(uv)
                J.data.uv_layers.new(name="UVMap")
                bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
                bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.002, area_weight=0.0, scale_to_bounds=False)
                try: bpy.ops.uv.pack_islands(rotate=True, margin=0.0015)
                except Exception as e: log("uv pack:", e)
                bpy.ops.object.mode_set(mode="OBJECT")
                bpy.ops.object.select_all(action="DESELECT"); J.select_set(True); bpy.context.view_layer.objects.active = J
                slots = [ms for ms in J.data.materials if ms is not None]

                def bake(kind, make, samples=1):
                    img = bpy.data.images.new("%s %s" % (part, kind), size, size, alpha=False, float_buffer=True)
                    img.colorspace_settings.name = "Non-Color"
                    nodes, undos = [], []
                    for mt in slots:
                        tn = mt.node_tree.nodes.new("ShaderNodeTexImage"); tn.image = img; mt.node_tree.nodes.active = tn; nodes.append((mt, tn))
                        if make: undos.append(with_output(mt, lambda nt, em, mt=mt: make(mt, nt, em)))
                    scene.cycles.samples = samples
                    bpy.ops.object.bake(type="EMIT" if make else kind, margin=6, use_clear=True)
                    for u in undos: u()
                    for mt, tn in nodes: mt.node_tree.nodes.remove(tn)
                    px = np.empty(size * size * 4, dtype=np.float32); img.pixels.foreach_get(px)
                    bpy.data.images.remove(img)
                    return px.reshape(-1, 4)

                def base_of(mt, nt, em):
                    c = mat_preview_rgb(mt.get("rc_key", "paint")); em.inputs["Color"].default_value = (c[0], c[1], c[2], 1)
                def mr_of(mt, nt, em):
                    k = mt.get("rc_key", "paint"); r, g, b, gloss, slot, emis = MATS[k]
                    met, rough, coat = GLTF_PBR.get(k, (0.0, max(0.15, min(0.95, 0.88 - 0.8 * gloss)), 0.0))
                    em.inputs["Color"].default_value = (coat, rough, met, 1)
                def pos_of(mt, nt, em):
                    g = nt.nodes.new("ShaderNodeNewGeometry"); nt.links.new(g.outputs["Position"], em.inputs["Color"]); return [g]
                def nrm_of(mt, nt, em):
                    g = nt.nodes.new("ShaderNodeNewGeometry"); nt.links.new(g.outputs["Normal"], em.inputs["Color"]); return [g]
                def tan_of(mt, nt, em):
                    g = nt.nodes.new("ShaderNodeTangent"); g.direction_type = "UV_MAP"; g.uv_map = "UVMap"
                    nt.links.new(g.outputs["Tangent"], em.inputs["Color"]); return [g]

                base = bake("base", base_of)[:, :3]
                mr = bake("mr", mr_of)[:, :3]
                pos = bake("pos", pos_of)[:, :3]
                ao = bake("AO", None, samples=48)[:, 0]
                covered = np.abs(pos).sum(axis=1) > 1e-6

                # weathering
                n1 = value_noise(pos, 1.6, 1); n2 = value_noise(pos, 0.35, 2)
                streak = value_noise(pos * np.array([1.0, 1.0, 0.08]), 9.0, 3)
                met = mr[:, 2]; rough = mr[:, 1]; coat = mr[:, 0]
                painted = ((met < 0.5) & (coat > 0.2)).astype(np.float32)
                grime = np.clip(1 - ao, 0, 1)
                col = base * (0.6 + 0.4 * np.power(np.clip(ao, 0, 1), 1.3))[:, None]
                col = col * (1 - 0.35 * grime[:, None] * np.array([0.0, 0.05, 0.14]))           # warm brown dirt in the corners
                col = col * (1 + 0.05 * (n1 - 0.5) + 0.05 * (n2 - 0.5))[:, None]
                col = col * (1 - 0.07 * np.clip((streak - 0.62) / 0.25, 0, 1) * painted)[:, None]
                col = col * (1 - 0.09 * np.clip((1.1 - pos[:, 2]) / 0.9, 0, 1))[:, None]           # road dust low down
                soot = np.zeros(len(pos))
                for e in ex_pts:
                    t = np.clip(e[1] - pos[:, 1], 0, None)                                        # downstream: Blender -y is aft
                    r = np.hypot(pos[:, 0] - e[0], pos[:, 2] - (e[2] - 0.12 * t))
                    soot = np.maximum(soot, np.exp(-t / 1.1) * np.exp(-(r / (0.12 + 0.1 * t)) ** 2) * (t > 0.05))   # a short, narrow plume
                col = col * (1 - 0.45 * soot)[:, None]
                rough = np.clip(rough + 0.18 * grime + 0.08 * (n1 - 0.5) + 0.25 * soot, 0.04, 1)
                col = np.clip(col, 0, 1)

                # ---- surface detail (skin parts only): panel joints at the frames and along the sides, rows of
                # rivets beside them and a few inset access panels, as a height field in metres, turned into a
                # tangent-space normal map (and the joints darkened a little in the colour)
                normal_img = None
                if part == "body" or part.startswith("door"):
                    nw = bake("nrm", nrm_of)[:, :3]; tw = bake("tan", tan_of)[:, :3]
                    nw = nw / np.maximum(np.linalg.norm(nw, axis=1), 1e-6)[:, None]
                    tw = tw - nw * np.sum(tw * nw, axis=1)[:, None]; tw = tw / np.maximum(np.linalg.norm(tw, axis=1), 1e-6)[:, None]
                    bw = np.cross(nw, tw)
                    skin = painted > 0.5
                    top = np.abs(nw[:, 2]) > 0.72                      # roof and belly: joints run fore and aft instead
                    FR, ST, RIV = 0.56, 0.42, 0.045                     # frame pitch, stringer pitch, rivet pitch (m)

                    def hcell(a, b, c):
                        v = (a * 73856093) ^ (b * 19349663) ^ (c * 83492791)
                        v = (v ^ (v >> 13)) * 1274126177
                        return ((v ^ (v >> 16)) & 0xFFFF) / 65535.0

                    def height(q):
                        x, y, z = q[:, 0], q[:, 1], q[:, 2]
                        along = np.where(top, x, z)                     # the coordinate a frame joint runs along
                        across = y                                       # frames are planes of constant y (fore-aft)
                        def dist(v, pitch): return np.abs((v / pitch + 0.5) % 1.0 - 0.5) * pitch
                        df = dist(across, FR); ds = dist(along, ST)
                        h = -0.0010 * np.exp(-(df / 0.0034) ** 2) - 0.0008 * np.exp(-(ds / 0.0032) ** 2)
                        # rivet rows 18 mm either side of each frame joint, and along the stringer joints
                        dr = np.abs(df - 0.018)                            # df is a distance: both sides of the joint
                        da = dist(along, RIV)
                        h += 0.0005 * np.exp(-(dr ** 2 + da ** 2) / 0.0036 ** 2)
                        drs = np.abs(ds - 0.016); dal = dist(across, RIV)
                        h += 0.00045 * np.exp(-(drs ** 2 + dal ** 2) / 0.0034 ** 2)
                        # access panels: some cells get an inset rectangle 6 cm inside the joints
                        ci = np.floor(across / FR).astype(np.int64); cj = np.floor(along / ST).astype(np.int64)
                        pick = hcell(ci, cj, (x > 0).astype(np.int64) + 7) < 0.16
                        fx = (across / FR) % 1.0 * FR; fy = (along / ST) % 1.0 * ST
                        inset = 0.06
                        edge = np.minimum(np.minimum(np.abs(fx - inset), np.abs(FR - inset - fx)), np.minimum(np.abs(fy - inset), np.abs(ST - inset - fy)))
                        inside = (fx > inset - 0.003) & (fx < FR - inset + 0.003) & (fy > inset - 0.003) & (fy < ST - inset + 0.003)
                        h += np.where(pick & inside, -0.0008 * np.exp(-(edge / 0.003) ** 2), 0.0)
                        # fasteners at the access panel corners and along its edges
                        cx = np.minimum(np.abs(fx - inset - 0.012), np.abs(FR - inset - 0.012 - fx)); cy = dist(fy - inset, 0.09)
                        h += np.where(pick & inside, 0.0003 * np.exp(-(cx ** 2 + cy ** 2) / 0.003 ** 2), 0.0)
                        return np.where(skin, h, 0.0)

                    e = 0.0011
                    h0 = height(pos)
                    dht = (height(pos + tw * e) - height(pos - tw * e)) / (2 * e)
                    dhb = (height(pos + bw * e) - height(pos - bw * e)) / (2 * e)
                    nts = np.stack([-dht, -dhb, np.ones(len(pos))], axis=1)
                    nts = nts / np.linalg.norm(nts, axis=1)[:, None]
                    nts[~covered] = (0, 0, 1)
                    col = col * (1 - np.clip(-h0 / 0.0008, 0, 1) * 0.4)[:, None]       # dirt in the joints shows them from a distance
                    rough = np.clip(rough + np.clip(-h0 / 0.0008, 0, 1) * 0.2, 0.04, 1)
                    normal_img = bpy.data.images.new(part + " normal", size, size, alpha=False)
                    normal_img.colorspace_settings.name = "Non-Color"
                    normal_img.pixels.foreach_set(np.concatenate([nts * 0.5 + 0.5, np.ones((len(nts), 1))], axis=1).astype(np.float32).ravel())
                    normal_img.filepath_raw = os.path.join(tex_dir, "%s_%s_normal.png" % (spec["file"], part)); normal_img.file_format = "PNG"; normal_img.save()

                img_c = bpy.data.images.new(part + " base colour", size, size, alpha=False)
                img_c.colorspace_settings.name = "sRGB"
                img_c.pixels.foreach_set(np.concatenate([to_srgb(col), np.ones((len(col), 1))], axis=1).astype(np.float32).ravel())
                img_c.filepath_raw = os.path.join(tex_dir, "%s_%s_basecolor.png" % (spec["file"], part)); img_c.file_format = "PNG"; img_c.save()
                img_m = bpy.data.images.new(part + " metal rough", size, size, alpha=False)
                img_m.colorspace_settings.name = "Non-Color"
                img_m.pixels.foreach_set(np.stack([np.ones(len(col)), rough, met, np.ones(len(col))], axis=1).astype(np.float32).ravel())
                img_m.filepath_raw = os.path.join(tex_dir, "%s_%s_metalrough.png" % (spec["file"], part)); img_m.file_format = "PNG"; img_m.save()

                # one material for the part, the baked textures in it
                mt = bpy.data.materials.new(part + " skin"); mt.use_nodes = True; mt.use_backface_culling = True
                nt = mt.node_tree; bsdf = nt.nodes.get("Principled BSDF")
                tc = nt.nodes.new("ShaderNodeTexImage"); tc.image = img_c
                tm = nt.nodes.new("ShaderNodeTexImage"); tm.image = img_m
                sep = nt.nodes.new("ShaderNodeSeparateColor")
                nt.links.new(tc.outputs["Color"], bsdf.inputs["Base Color"])
                nt.links.new(tm.outputs["Color"], sep.inputs["Color"])
                nt.links.new(sep.outputs["Green"], bsdf.inputs["Roughness"]); nt.links.new(sep.outputs["Blue"], bsdf.inputs["Metallic"])
                if normal_img is not None:
                    tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = normal_img
                    nm = nt.nodes.new("ShaderNodeNormalMap"); nm.space = "TANGENT"; nm.uv_map = "UVMap"
                    nt.links.new(tn.outputs["Color"], nm.inputs["Color"]); nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
                J.data.materials.clear(); J.data.materials.append(mt)
                for p in J.data.polygons: p.material_index = 0
                # the shading is in the texture now: drop the per-vertex shading from this mesh
                for name in ("AO", "Preview"):
                    a = J.data.color_attributes.get(name)
                    if a is not None: J.data.color_attributes.remove(a)
                log("baked %-18s %d objects -> 1, %d px, %.0f%% of the texture used" % (part, len(objs), size, 100 * covered.mean()))
            scene.render.engine = "BLENDER_WORKBENCH"
            log("texture bake: %.0fs" % (time.time() - t_bake))
        try: bake_exterior()
        except Exception as e:
            import traceback; log("texture bake failed:", repr(e)); log(traceback.format_exc())

        # ---- rig for the glTF: each part under an empty at the point it turns about (rotor hub, door hinge,
        # control pivot), so Unity and other programs can spin the rotor and swing the doors and controls. The
        # rest of the game's metadata goes along as glTF extras (rc_*), in game axes, which are the glTF axes too.
        def empty(name, loc, parent, display="PLAIN_AXES", size=0.15):
            e = bpy.data.objects.new(name, None); e.empty_display_type = display; e.empty_display_size = size
            fin_coll.objects.link(e); e.parent = parent; e.location = loc; return e

        def build_rig():
            rig = empty(meta["model"], (0, 0, 0), None, "ARROWS", 0.5)
            rig["rc_meta"] = json.dumps(meta, separators=(",", ":"))
            info = {}                                              # part -> (pivot in game coords or None, extras)
            if "blades" in finals and "points" in meta:
                info["blades"] = ([0, meta["points"]["mastY"], 0], {"rc_kind": "rotor", "rc_axis": [0, 1, 0]})
            tr = meta.get("tailRotor")
            if "tail_rotor" in finals and tr:
                props = {"rc_kind": "tail_rotor", "rc_axis": [1, 0, 0], "rc_radius": tr["r"], "rc_blades": tr["blades"]}
                for k in ("tip", "col"):
                    if k in tr: props["rc_" + k] = tr[k]
                info["tail_rotor"] = (meta["points"]["tailPos"], props)
            for c in meta.get("controls", []): info[c["part"]] = (c["pivot"], {"rc_kind": "control", "rc_control": c["kind"]})
            for d in meta.get("doors", []):
                props = {"rc_kind": "door_" + d["kind"], "rc_group": d["group"]}
                for k in ("axis", "open", "out", "move"):
                    if k in d: props["rc_" + k] = d[k]
                info[d["part"]] = (d.get("pivot"), props)
            roles = {p: "equipment_" + k for k, p in (meta.get("equipment") or {}).items()}
            inner = meta.get("interior") or []
            for p in [inner] if isinstance(inner, str) else inner: roles[p] = "interior"
            if meta.get("cockpitOnly"): roles[meta["cockpitOnly"]] = "cockpit_only"
            for part, objs in finals.items():
                piv, props = info.get(part, (None, {"rc_kind": "static"}))
                at = Vector(to_bl(piv)) if piv else objs[0].location.copy()   # library parts sit at their layout spot
                pe = empty(part, at, rig); pe["rc_part"] = part          # Blender adds .001 if the name is taken
                for k, v in props.items(): pe[k] = v
                if part in roles: pe["rc_role"] = roles[part]
                for ob in objs:
                    loc = ob.location - at; ob.parent = pe; ob.location = loc   # same place in the world
            pts = meta.get("points") or {}
            tr = meta.get("tailRotor")
            if tr and "tailPos" in pts and "tail_rotor" not in finals:   # no modelled tail rotor: just a mount point
                e = empty("tail_rotor", to_bl(pts["tailPos"]), rig, "SPHERE", tr["r"])   # unturned, so rc_axis holds
                e["rc_kind"] = "tail_rotor"; e["rc_axis"] = [1, 0, 0]; e["rc_radius"] = tr["r"]; e["rc_blades"] = tr["blades"]
                for k in ("tip", "col"):
                    if k in tr: e["rc_" + k] = tr[k]

            def point(name, p, **props):
                e = empty(name, to_bl(p[:3]), rig, "SPHERE", 0.05); e["rc_kind"] = "point"
                for k, v in props.items(): e["rc_" + k] = v
            if "eye" in pts: point("eye", pts["eye"])
            for k, v in (pts.get("lights") or {}).items():
                if isinstance(v, dict): point("light_" + k, v["pos"], dir=v["dir"])
                elif v and isinstance(v[0], (list, tuple)):
                    for i, p in enumerate(v): point("light_%s_%d" % (k, i + 1), p)
                else: point("light_" + k, v)
            for k, v in (pts.get("seats") or {}).items(): point("seat_" + k, v, door=v[3])
            return rig

        # ---- glTF for other programs, and the .blend
        try:
            rig = build_rig()
            log("rig: %d parts under pivots" % len(finals))
            bpy.ops.object.select_all(action="DESELECT")
            for ob in [rig] + list(rig.children_recursive): ob.select_set(True)
            # aircraft: .gltf with its textures as separate PNGs (Unity imports and compresses them as textures);
            # the libraries (buildings, vehicles, trees) stay single .glb files
            separate = not meta.get("library")
            glb = os.path.join(OUT, spec["file"] + (".gltf" if separate else ".glb"))
            fmt = dict(export_format="GLTF_SEPARATE", export_texture_dir=spec["file"] + "_textures") if separate else dict(export_format="GLB")
            try:   # Blender 4.2+: export the active colour layer (the baked shading) as COLOR_0
                bpy.ops.export_scene.gltf(filepath=glb, use_selection=True, export_extras=True, export_vertex_color="ACTIVE", **fmt)
            except TypeError:
                bpy.ops.export_scene.gltf(filepath=glb, use_selection=True, export_extras=True, **fmt)
            log("wrote", os.path.basename(glb))
        except Exception as e: log("glTF export skipped:", repr(e))
        # ---- lit previews of the finished, textured aircraft (EEVEE, a sun and a sky), for checking the look
        if not meta.get("library") and os.environ.get("RC_BEAUTY", "1") != "0":
            try:
                for eng in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
                    try: scene.render.engine = eng; break
                    except TypeError: continue
                sun_d = bpy.data.lights.new("Sun", "SUN"); sun_d.energy = 4.0; sun_d.angle = math.radians(2)
                sun = bpy.data.objects.new("Sun", sun_d); root.objects.link(sun); sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(35))
                w = scene.world; w.use_nodes = True
                bg = w.node_tree.nodes.get("Background"); bg.inputs["Color"].default_value = (0.42, 0.55, 0.72, 1); bg.inputs["Strength"].default_value = 0.9
                try: scene.view_settings.view_transform = "AgX"
                except Exception: pass
                for p_, c in fin_part_colls.items(): c.hide_render = p_ == "cockpit_only"
                for name, loc, target, lens in shots:
                    if name == "cockpit": continue
                    cam.location = Vector(loc); cam_data.lens = lens
                    cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
                    scene.render.filepath = os.path.join(OUT, "%s_%s_lit.png" % (spec["file"], name))
                    bpy.ops.render.render(write_still=True)
                for p_, c in fin_part_colls.items(): c.hide_render = False
                log("lit previews rendered")
            except Exception as e: log("lit previews skipped:", repr(e))
        try:
            bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, spec["file"] + ".blend"), copy=True)
            log("wrote", spec["file"] + ".blend")
        except Exception as e: log(".blend save skipped:", repr(e))
        log("%s done in %.0f s." % (meta["model"], time.time() - t_model))
    failed = []
    try:
        for key in BUILD:
            if key not in MODELS: log("unknown model in BUILD:", key); continue
            try: build_one(key)
            except Exception:
                import traceback
                log("FAILED (%s):\n" % key + traceback.format_exc()); failed.append(key)
        log("\nALL DONE in %.0f s." % (time.time() - t_start) + (" Problems with: " + ", ".join(failed) if failed else "") +
            " Attach the .rcm files and rotorcraft_build_log.txt in the chat.")
        if failed: raise RuntimeError("some models failed: " + ", ".join(failed))
    except Exception:
        import traceback
        log("FAILED:\n" + traceback.format_exc())
        log("Please attach rotorcraft_build_log.txt in the chat.")
        raise
    finally:
        finish_log()


if __name__ == "__main__":
    try:
        import bpy  # noqa: F401
        IN_BLENDER = True
    except ImportError:
        IN_BLENDER = False
    if IN_BLENDER:
        run_blender()
    else:
        key = sys.argv[1] if len(sys.argv) > 1 else "kestrel"
        out = sys.argv[2] if len(sys.argv) > 2 else MODELS[key]["file"] + "_plain.rcm"
        t0 = time.time(); m, meta = MODELS[key]["build"]()
        parts = parts_from_model_plain(m); meta["ao"] = False; meta["blender"] = None
        n = write_rcm(out, parts, meta)
        print("objects:", len(m.objs), "| parts:", ", ".join("%s %d/%d" % (p[0], len(p[1]), len(p[7]) // 3) for p in parts))
        print("wrote", out, "%.0f KB" % (n / 1024), "in %.1fs" % (time.time() - t0))
