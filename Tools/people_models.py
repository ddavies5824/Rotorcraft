# People and rescue props for the game: figures in the poses the scenes need (standing, waving in two frames,
# lying, kneeling, on the phone, hanging on the winch wire, sitting, swimming), a stretcher, a pallet of freight,
# a liferaft and a small fishing boat. Bodies are built from a joint skeleton with the skin modifier and smoothed;
# clothing is split into material slots (Top, Legs, Skin, Hair, Shoes, Helmet) so the game can dress each figure
# (hi-vis, medic, police, fire...) by tinting materials. Figures face -Y (forward in the game after export).
#   blender -b --factory-startup --python people_models.py -- <out.glb>
import bpy, bmesh, math, sys
from mathutils import Vector, Matrix, Euler

OUT = sys.argv[-1]
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

MATS = {}
def mat(name, rgb, rough=0.8, metal=0.0, emit=None):
    if name in MATS: return MATS[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    if emit:
        b.inputs["Emission Color"].default_value = (*emit, 1); b.inputs["Emission Strength"].default_value = 1.0
    MATS[name] = m
    return m

TOP = mat("Top", (0.18, 0.25, 0.42), 0.85)
LEGS = mat("Legs", (0.12, 0.13, 0.16), 0.85)
SKIN = mat("Skin", (0.80, 0.60, 0.49), 0.55)
HAIR = mat("Hair", (0.16, 0.11, 0.07), 0.7)
SHOES = mat("Shoes", (0.06, 0.06, 0.06), 0.6)
HELMET = mat("Helmet", (0.92, 0.92, 0.9), 0.35)

def standing():
    j = {
        "pelvis": (0, 0, 0.96), "spine": (0, 0.01, 1.16), "chest": (0, 0.0, 1.36), "neck": (0, 0.0, 1.50), "head": (0, -0.01, 1.63),
    }
    for s, x in (("L", 1), ("R", -1)):
        j["hip" + s] = (0.095 * x, 0, 0.93); j["knee" + s] = (0.10 * x, -0.02, 0.52); j["ankle" + s] = (0.10 * x, 0.02, 0.09); j["toe" + s] = (0.10 * x, -0.13, 0.04)
        j["shoulder" + s] = (0.195 * x, 0.01, 1.43); j["elbow" + s] = (0.235 * x, 0.03, 1.15); j["wrist" + s] = (0.25 * x, 0.0, 0.90); j["hand" + s] = (0.255 * x, -0.01, 0.82)
    return j

def rot_about(j, keys, pivot, euler):
    m = Euler(euler).to_matrix()
    p = Vector(j[pivot])
    for k in keys: j[k] = tuple(p + m @ (Vector(j[k]) - p))

ARM = lambda s: ["elbow" + s, "wrist" + s, "hand" + s]
FORE = lambda s: ["wrist" + s, "hand" + s]
LEG = lambda s: ["knee" + s, "ankle" + s, "toe" + s]
SHIN = lambda s: ["ankle" + s, "toe" + s]

def pose(name):
    j = standing()
    if name == "stand":
        rot_about(j, ARM("L"), "shoulderL", (0, 0.08, 0)); rot_about(j, ARM("R"), "shoulderR", (0, -0.08, 0))
    elif name in ("wave", "wave2"):
        # right arm raised high, waving side to side between frames
        rot_about(j, ARM("R"), "shoulderR", (0, -2.7 if name == "wave" else -2.25, 0))
        rot_about(j, FORE("R"), "elbowR", (0, -0.35 if name == "wave" else 0.45, 0))
        rot_about(j, ARM("L"), "shoulderL", (0, 0.1, 0))
    elif name == "phone":
        rot_about(j, ARM("R"), "shoulderR", (0.5, -0.25, 0)); rot_about(j, FORE("R"), "elbowR", (2.3, 0, 0))
        rot_about(j, ARM("L"), "shoulderL", (0, 0.12, 0))
    elif name == "kneel":
        # right knee down, left foot forward: drop the body
        for k in list(j): j[k] = (j[k][0], j[k][1], j[k][2] - 0.42)
        j["kneeR"] = (-0.10, 0.05, 0.06); j["ankleR"] = (-0.10, 0.42, 0.07); j["toeR"] = (-0.10, 0.50, 0.03)
        j["hipR"] = (-0.095, 0.0, 0.51); j["hipL"] = (0.095, 0.0, 0.51)
        j["kneeL"] = (0.10, -0.40, 0.50); j["ankleL"] = (0.10, -0.38, 0.09); j["toeL"] = (0.10, -0.50, 0.04)
        rot_about(j, ["spine", "chest", "neck", "head"] + [k + s for k in ("shoulder", "elbow", "wrist", "hand") for s in "LR"], "pelvis", (-0.35, 0, 0))
        rot_about(j, ARM("L"), "shoulderL", (-0.9, 0, 0)); rot_about(j, ARM("R"), "shoulderR", (-0.9, 0, 0))
    elif name == "hang":
        # on the wire: arms up to the hook, legs a little bent
        rot_about(j, ARM("L"), "shoulderL", (-0.4, 2.55, 0)); rot_about(j, ARM("R"), "shoulderR", (-0.4, -2.55, 0))
        rot_about(j, SHIN("L"), "kneeL", (-0.5, 0, 0)); rot_about(j, SHIN("R"), "kneeR", (-0.35, 0, 0))
        rot_about(j, LEG("L"), "hipL", (0.25, 0, 0)); rot_about(j, LEG("R"), "hipR", (0.15, 0, 0))
    elif name == "sit":
        for s in "LR":
            rot_about(j, LEG(s), "hip" + s, (1.5, 0, 0)); rot_about(j, SHIN(s), "knee" + s, (-1.45, 0, 0))
            rot_about(j, ARM(s), "shoulder" + s, (-0.5, 0, 0))
        for k in list(j): j[k] = (j[k][0], j[k][1], j[k][2] - 0.5)
    elif name == "lie":
        rot_about(j, ARM("L"), "shoulderL", (0, 0.3, 0)); rot_about(j, ARM("R"), "shoulderR", (0, -0.2, 0))
        rot_about(j, LEG("L"), "hipL", (0, 0.08, 0)); rot_about(j, SHIN("R"), "kneeR", (-0.4, 0, 0))
        for k in list(j): j[k] = tuple(Euler((-math.pi / 2, 0, 0)).to_matrix() @ Vector(j[k]))
        for k in list(j): j[k] = (j[k][0], j[k][1], j[k][2] + 0.12)
    elif name == "swim":
        # head and shoulders out of the water, arms out: the rest is below the surface (origin = water line)
        rot_about(j, ARM("L"), "shoulderL", (0, 1.3, 0)); rot_about(j, ARM("R"), "shoulderR", (0, -1.3, 0))
        for k in list(j): j[k] = (j[k][0], j[k][1], j[k][2] - 1.38)
    return j

def skin_part(name, chain_edges, j, radii, material):
    verts = []; idx = {}
    for a, b in chain_edges:
        for k in (a, b):
            if k not in idx: idx[k] = len(verts); verts.append(j[k])
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [(idx[a], idx[b]) for a, b in chain_edges], [])
    ob = bpy.data.objects.new(name, me); scene.collection.objects.link(ob)
    sk = ob.modifiers.new("skin", "SKIN"); sk.use_smooth_shade = True
    for k, i in idx.items():
        r = radii.get(k, 0.05)
        me.skin_vertices[0].data[i].radius = r if isinstance(r, tuple) else (r, r)
    me.skin_vertices[0].data[0].use_root = True
    sub = ob.modifiers.new("sub", "SUBSURF"); sub.levels = 1; sub.render_levels = 1
    ob.data.materials.append(material)
    return ob

def head_part(j, helmet):
    c = Vector(j["head"]); n = Vector(j["neck"])
    up = (c - n).normalized()
    bpy.ops.mesh.primitive_uv_sphere_add(segments=20, ring_count=12, radius=0.105, location=c)
    h = bpy.context.object; h.scale = (0.9, 1.0, 1.12)
    h.rotation_euler = up.to_track_quat("Z", "Y").to_euler()
    h.data.materials.append(SKIN)
    bpy.ops.object.shade_smooth()
    # hair or helmet cap: the top and back half of a slightly larger sphere
    bpy.ops.mesh.primitive_uv_sphere_add(segments=20, ring_count=12, radius=0.118 if helmet else 0.11, location=c + up * 0.02)
    cap = bpy.context.object; cap.scale = (0.94, 1.04, 1.0 if helmet else 1.05)
    cap.rotation_euler = h.rotation_euler
    bm = bmesh.new(); bm.from_mesh(cap.data)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < (-0.01 if helmet else 0.0) and v.co.y < 0.02 or v.co.z < -0.06], context="VERTS")
    bm.to_mesh(cap.data); bm.free()
    cap.data.materials.append(HELMET if helmet else HAIR)
    bpy.ops.object.shade_smooth()
    return [h, cap]

def figure(name, posename, helmet=False):
    j = pose(posename)
    parts = []
    parts.append(skin_part(name + "_torso", [("pelvis", "spine"), ("spine", "chest"), ("chest", "neck")] +
                           [("chest", "shoulder" + s) for s in "LR"] + [("shoulder" + s, "elbow" + s) for s in "LR"] + [("elbow" + s, "wrist" + s) for s in "LR"],
                           j, {"pelvis": (0.15, 0.11), "spine": (0.145, 0.10), "chest": (0.165, 0.11), "neck": 0.055, "shoulderL": 0.062, "shoulderR": 0.062,
                               "elbowL": 0.047, "elbowR": 0.047, "wristL": 0.038, "wristR": 0.038}, TOP))
    parts.append(skin_part(name + "_legs", [("pelvis", "hipL"), ("pelvis", "hipR"), ("hipL", "kneeL"), ("hipR", "kneeR"), ("kneeL", "ankleL"), ("kneeR", "ankleR")],
                           j, {"pelvis": (0.15, 0.105), "hipL": 0.085, "hipR": 0.085, "kneeL": 0.058, "kneeR": 0.058, "ankleL": 0.044, "ankleR": 0.044}, LEGS))
    parts.append(skin_part(name + "_feet", [("ankleL", "toeL"), ("ankleR", "toeR")], j, {"ankleL": 0.05, "ankleR": 0.05, "toeL": (0.045, 0.035), "toeR": (0.045, 0.035)}, SHOES))
    parts.append(skin_part(name + "_hands", [("wristL", "handL"), ("wristR", "handR")], j, {"wristL": 0.033, "wristR": 0.033, "handL": (0.042, 0.022), "handR": (0.042, 0.022)}, SKIN))
    parts.append(skin_part(name + "_neck", [("neck", "head")], j, {"neck": 0.05, "head": 0.045}, SKIN))
    for p in parts:
        bpy.context.view_layer.objects.active = p
        for m in list(p.modifiers): bpy.ops.object.modifier_apply(modifier=m.name)
    parts += head_part(j, helmet)
    return join(name, parts)

def join(name, parts):
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts: p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    ob = bpy.context.object; ob.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    # smooth normals with sharp creases kept
    return ob

def box(name, size, loc, material, bevel=0.0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    o = bpy.context.object; o.name = name; o.scale = size
    bpy.ops.object.transform_apply(scale=True)
    if bevel > 0:
        b = o.modifiers.new("b", "BEVEL"); b.width = bevel; b.segments = 2
        bpy.ops.object.modifier_apply(modifier="b")
    o.data.materials.append(material)
    return o

def cyl(name, r, depth, loc, rot, material, verts=16):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=loc, rotation=rot)
    o = bpy.context.object; o.name = name; o.data.materials.append(material)
    return o

root = bpy.data.objects.new("People", None); scene.collection.objects.link(root)
def keep(o, name):
    o.name = name; o.parent = root
    return o

for p in ("stand", "wave", "wave2", "phone", "kneel", "hang", "sit", "lie", "swim"):
    keep(figure("pose_" + p, p), "pose_" + p)
for p in ("stand", "hang", "lie"):
    keep(figure("helmet_" + p, p, helmet=True), "helmet_" + p)

# stretcher with a casualty under a blanket
STEEL = mat("Steel", (0.62, 0.64, 0.66), 0.35, 1.0)
CANVAS = mat("Canvas", (0.85, 0.42, 0.12), 0.8)
BLANKET = mat("Blanket", (0.16, 0.30, 0.55), 0.95)
parts = [box("bed", (0.56, 2.0, 0.04), (0, 0, 0.30), CANVAS, 0.01)]
for x in (-0.28, 0.28): parts.append(cyl("rail", 0.016, 2.2, (x, 0, 0.32), (math.pi / 2, 0, 0), STEEL))
for x in (-0.24, 0.24):
    for y in (-0.8, 0.8): parts.append(cyl("leg", 0.014, 0.28, (x, y, 0.15), (0, 0, 0), STEEL))
casualty = figure("stretcher_body", "lie")
casualty.location = (0, 0, 0.21); casualty.rotation_euler = (0, 0, 0)
bpy.ops.object.select_all(action="DESELECT"); casualty.select_set(True); bpy.context.view_layer.objects.active = casualty
bpy.ops.object.transform_apply(location=True)
parts.append(casualty)
parts.append(box("blanket", (0.5, 1.25, 0.12), (0, 0.32, 0.43), BLANKET, 0.05))
keep(join("stretcher", parts), "stretcher")

# freight pallet: wooden pallet, shrink-wrapped boxes, straps
WOOD = mat("Wood", (0.55, 0.42, 0.27), 0.9)
CARD = mat("Card", (0.66, 0.52, 0.34), 0.9)
WRAP = mat("Wrap", (0.85, 0.88, 0.9), 0.2)
STRAP = mat("Strap", (0.9, 0.55, 0.08), 0.6)
parts = []
for y in (-0.5, 0, 0.5): parts.append(box("block", (1.2, 0.1, 0.1), (0, y, 0.05), WOOD))
for x in (-0.55, -0.27, 0, 0.27, 0.55): parts.append(box("deck", (0.12, 1.1, 0.025), (x, 0, 0.11), WOOD))
parts.append(box("load", (1.12, 1.02, 0.8), (0, 0, 0.53), WRAP, 0.03))
for x in (-0.3, 0.3): parts.append(box("strap", (0.05, 1.05, 0.84), (x, 0, 0.53), STRAP))
keep(join("pallet", parts), "pallet")

# liferaft: an orange double tube ring with a canopy arch and a grey floor
RUBBER = mat("Raft", (0.95, 0.38, 0.06), 0.55)
FLOOR = mat("RaftFloor", (0.2, 0.2, 0.22), 0.8)
parts = []
for i, z in enumerate((0.16, 0.42)):
    bpy.ops.mesh.primitive_torus_add(major_radius=1.05, minor_radius=0.15, major_segments=32, minor_segments=12, location=(0, 0, z))
    t = bpy.context.object; t.data.materials.append(RUBBER); bpy.ops.object.shade_smooth(); parts.append(t)
bpy.ops.mesh.primitive_cylinder_add(vertices=32, radius=1.0, depth=0.05, location=(0, 0, 0.05)); f = bpy.context.object; f.data.materials.append(FLOOR); parts.append(f)
bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, radius=1.02, location=(0, 0, 0.45))
c = bpy.context.object; c.scale = (1, 1, 0.75)
bm = bmesh.new(); bm.from_mesh(c.data)
bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < 0 or (v.co.y < -0.35 and v.co.z < 0.75)], context="VERTS")
bm.to_mesh(c.data); bm.free()
c.data.materials.append(RUBBER); bpy.ops.object.shade_smooth(); parts.append(c)
keep(join("liferaft", parts), "liferaft")

# small fishing boat (about 9 m): hull, wheelhouse, mast
HULL = mat("Hull", (0.12, 0.28, 0.42), 0.5)
DECK = mat("Deck", (0.55, 0.5, 0.42), 0.85)
WHITE = mat("Paint", (0.9, 0.9, 0.88), 0.45)
GLASS = mat("Glass", (0.08, 0.12, 0.15), 0.05, 0.3)
bm = bmesh.new()
sections = [(-4.5, 0.9, 1.0), (-3.0, 1.5, 1.05), (0, 1.6, 1.1), (2.6, 1.3, 1.3), (4.4, 0.05, 1.6)]
rings = []
for y, half, h in sections:
    ring = [bm.verts.new((-half, -y, h)), bm.verts.new((-half * 0.7, -y, 0.15)), bm.verts.new((0, -y, -0.3)), bm.verts.new((half * 0.7, -y, 0.15)), bm.verts.new((half, -y, h))]
    rings.append(ring)
for a, b in zip(rings, rings[1:]):
    for i in range(4): bm.faces.new((a[i], a[i + 1], b[i + 1], b[i]))
bm.faces.new(list(reversed(rings[0])))
me = bpy.data.meshes.new("hull"); bm.to_mesh(me); bm.free()
hull = bpy.data.objects.new("hull", me); scene.collection.objects.link(hull); me.materials.append(HULL)
parts = [hull, box("deck", (2.9, 7.5, 0.06), (0, 0.3, 1.0), DECK), box("house", (1.6, 1.8, 1.5), (0, -0.4, 1.8), WHITE, 0.05),
         box("windows", (1.62, 0.6, 0.45), (0, -1.0, 2.15), GLASS), box("roof", (1.8, 2.0, 0.08), (0, -0.4, 2.6), WHITE),
         cyl("mast", 0.06, 3.5, (0, 1.4, 2.7), (0, 0, 0), WHITE, 10), cyl("boom", 0.04, 2.6, (0, 2.6, 2.4), (math.radians(65), 0, 0), WHITE, 8)]
keep(join("boat", parts), "boat")

for o in root.children:
    for p in o.data.polygons: p.use_smooth = True

bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.gltf(filepath=OUT, export_format="GLB", use_selection=True, export_apply=True, export_yup=True)
print("PEOPLE DONE", [o.name for o in root.children])
