#!/usr/bin/env python3
"""ParisGuessr: IGN BD TOPO + RGE ALTI (Etalab 2.0) + Paris trees (opendata.paris.fr, ODbL) -> compact 250 m tiles.
Geometry is extruded at runtime (TileBuilder.cs), so a tile only stores footprints, heights, road centrelines, curbs, trees and a
5 m terrain grid. World frame: x = E - X0, z = N - Y0, y = altitude NGF (m)."""
import os, sys, json, struct, math, random, numpy as np
import shapely, shapely.geometry as sg, shapely.ops as so
from shapely.strtree import STRtree
import mapbox_earcut as earcut
from pyproj import Transformer

D = os.environ.get("PG_DATA", "/workspace/parisguessr/data")
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_TILES = os.path.join(ROOT, "Assets", "StreamingAssets", "tiles")
OUT_RES = os.path.join(ROOT, "Assets", "ParisGuessr", "Resources")
X0, Y0, X1, Y1 = 642800.0, 6857200.0, 661400.0, 6867400.0
TS = 250.0; NX = int(math.ceil((X1 - X0) / TS)); NY = int(math.ceil((Y1 - Y0) / TS))
GN, GSP = 51, 5.0
random.seed(44)

meta = json.load(open(os.path.join(D, "dem", "dem.json")))
DEM = np.fromfile(os.path.join(D, "dem", "dem_4m.f32"), np.float32).reshape(meta["h"], meta["w"])
def dem(x, y):
    """bilinear, L93 coords, arrays ok"""
    fx = (np.asarray(x) - meta["x0"]) / meta["step"]; fy = (np.asarray(y) - meta["y0"]) / meta["step"]
    fx = np.clip(fx, 0, meta["w"] - 1.001); fy = np.clip(fy, 0, meta["h"] - 1.001)
    ix = fx.astype(int); iy = fy.astype(int); tx = fx - ix; ty = fy - iy
    return (DEM[iy, ix] * (1 - tx) * (1 - ty) + DEM[iy, ix + 1] * tx * (1 - ty) + DEM[iy + 1, ix] * (1 - tx) * ty + DEM[iy + 1, ix + 1] * tx * ty)

def load(name): return json.load(open(os.path.join(D, "vector", name + ".geojson")))["features"]
def tile_of(x, y): return int((x - X0) // TS), int((y - Y0) // TS)
def cm(v): return int(max(0, min(65535, round(v * 100))))
def dm(v): return int(max(-32767, min(32767, round(v * 10))))

tiles = {}
def T(k):
    if k not in tiles: tiles[k] = dict(prisms=[], roads=[], curbs=[], trees=[])
    return tiles[k]

paris = sg.shape(load("commune")[0]["geometry"]).buffer(0)
arrs = [(int(f["properties"]["code_insee"]) - 75100, sg.shape(f["geometry"]).buffer(0)) for f in load("arrondissement_municipal")]

# ---------------- buildings ----------------
def prism(poly, z0, z1, kind):
    poly = sg.polygon.orient(poly, 1.0)
    rings = [poly.exterior] + list(poly.interiors)
    pts, ends, rr = [], [], []
    for r in rings:
        c = list(r.coords)[:-1]
        if len(c) < 3: continue
        rr.append(c); pts += c; ends.append(len(pts))
    if not rr or len(pts) > 60000: return None
    tri = earcut.triangulate_float64(np.array(pts, np.float64), np.array(ends, np.uint32))
    if len(tri) == 0: return None
    return dict(kind=kind, z0=z0, z1=z1, rings=rr, tri=tri)

print("buildings...", flush=True)
B = load("batiment")
bshapes = []
nb = 0
for f in B:
    p = f["properties"]
    if p.get("etat_de_l_objet") != "En service" or f["geometry"] is None: continue
    g = sg.shape(f["geometry"])
    g = shapely.force_2d(g).buffer(0)
    if g.is_empty: continue
    h = p.get("hauteur")
    zs = [c[2] for poly in f["geometry"]["coordinates"] for ring in poly for c in ring if len(c) > 2]
    sol = p.get("altitude_minimale_sol")
    for poly in (g.geoms if g.geom_type == "MultiPolygon" else [g]):
        if poly.area < 6: continue
        poly = poly.simplify(0.25, preserve_topology=True)
        if poly.is_empty or poly.geom_type != "Polygon": continue
        ex = np.array(poly.exterior.coords)
        g0 = float(dem(ex[:, 0], ex[:, 1]).min())
        base = min(g0, sol if sol is not None else g0) - 0.6
        if h is not None and h > 0: gut = (sol if sol is not None else g0) + h
        elif zs: gut = float(np.median(zs))
        else: gut = g0 + 6
        gut = max(gut, g0 + 2.5)
        bshapes.append(poly)
        c = poly.representative_point(); k = tile_of(c.x, c.y)
        if not (0 <= k[0] < NX and 0 <= k[1] < NY): continue
        pr = prism(poly, base, gut, 0)
        if pr is None: continue
        T(k)["prisms"].append(pr); nb += 1
        # attic / mansard storey (Paris roofs): real max roof altitude if known, else 2.8 m for >= 4 storeys residential
        top = p.get("altitude_maximale_toit"); ah = 0
        if top is not None and top > gut + 1.5: ah = min(top - gut, 8)
        elif (p.get("nombre_d_etages") or 0) >= 4 and (p.get("usage_1") or "").startswith("R") and poly.area > 60: ah = 2.8
        if ah > 0:
            ins = poly.buffer(-1.8, join_style=2)
            for q in (ins.geoms if ins.geom_type == "MultiPolygon" else [ins]):
                if q.is_empty or q.geom_type != "Polygon" or q.area < 25: continue
                q = q.simplify(0.25, preserve_topology=True)
                pa = prism(q, gut - 0.05, gut + ah, 1)
                if pa: T(k)["prisms"].append(pa)
del B
print("buildings", nb, flush=True)
btree = STRtree(bshapes)

# ---------------- roads ----------------
print("roads...", flush=True)
RIB = {"Route à 1 chaussée", "Route à 2 chaussées", "Rond-point", "Route empierrée", "Bretelle", "Type autoroutier"}
SPAWNABLE = {"Route à 1 chaussée", "Route à 2 chaussées", "Rond-point", "Route empierrée"}
tile_boxes = {}
def tbox(k):
    if k not in tile_boxes: tile_boxes[k] = sg.box(X0 + k[0] * TS, Y0 + k[1] * TS, X0 + (k[0] + 1) * TS, Y0 + (k[1] + 1) * TS)
    return tile_boxes[k]
def densify(coords, step=4.0):
    out = [coords[0]]
    for a, b in zip(coords[:-1], coords[1:]):
        L = math.dist(a[:2], b[:2]); n = max(1, int(L // step))
        for i in range(1, n + 1):
            t = i / n; out.append(tuple(a[j] + (b[j] - a[j]) * t for j in range(len(a))))
    return out
ground_ribbons = {}   # tile -> list of buffered polys for curbs
spawn_src = []
for f in load("troncon_de_route"):
    p = f["properties"]
    if p["nature"] not in RIB or p["etat_de_l_objet"] != "En service" or f["geometry"] is None: continue
    pos = int(p["position_par_rapport_au_sol"] or 0)
    if pos < 0: continue
    w = p.get("largeur_de_chaussee") or 0
    if not w: w = 3.2 * (p.get("nombre_de_voies") or 0) or 5.0
    w = float(min(max(w, 3.0), 30.0))
    line3 = sg.shape(f["geometry"])
    if line3.geom_type != "LineString": continue
    line = shapely.force_2d(line3)
    name = p.get("nom_collaboratif_gauche") or p.get("nom_voie_ban_gauche") or p.get("nom_collaboratif_droite") or ""
    if pos == 0 and p["nature"] in SPAWNABLE and line.length > 8: spawn_src.append((line, name))
    x0, y0, x1, y1 = line.bounds
    for kx in range(int((x0 - X0) // TS), int((x1 - X0) // TS) + 1):
        for ky in range(int((y0 - Y0) // TS), int((y1 - Y0) // TS) + 1):
            if not (0 <= kx < NX and 0 <= ky < NY): continue
            k = (kx, ky); bx = tbox(k).buffer(w)
            part = line3.intersection(bx) if pos > 0 else line.intersection(bx)
            for seg in (part.geoms if hasattr(part, "geoms") else [part]):
                if seg.is_empty or seg.geom_type != "LineString" or seg.length < 0.5: continue
                c = densify(list(seg.coords))
                T(k)["roads"].append(dict(bridge=pos > 0, w=w, pts=c))
            if pos == 0:
                ground_ribbons.setdefault(k, []).append(line.intersection(tbox(k).buffer(40)).buffer(w / 2, cap_style=1, quad_segs=3))
print("curbs...", flush=True)
for k, polys in ground_ribbons.items():
    u = so.unary_union([q for q in polys if not q.is_empty]).simplify(0.2)
    bl = u.boundary.intersection(tbox(k))
    for seg in (bl.geoms if hasattr(bl, "geoms") else [bl]):
        if seg.geom_type == "LineString" and seg.length > 0.5:
            T(k)["curbs"].append(densify(list(seg.coords), 5.0))
        elif seg.geom_type == "MultiLineString":
            for s in seg.geoms: T(k)["curbs"].append(densify(list(s.coords), 5.0))

# ---------------- trees ----------------
print("trees...", flush=True)
tr = Transformer.from_crs("EPSG:4326", "EPSG:2154", always_xy=True)
lat, lon, hh = [], [], []
for line in open(os.path.join(D, "arbres_paris.csv"), encoding="utf-8").read().splitlines()[1:]:
    try:
        gp, h, _ = line.split(";"); la, lo = gp.split(","); lat.append(float(la)); lon.append(float(lo)); hh.append(float(h or 0))
    except ValueError: continue
ex, ny_ = tr.transform(np.array(lon), np.array(lat))
pts = shapely.points(ex, ny_)
inside = np.zeros(len(pts), bool)
idx = btree.query(pts, predicate="within")
inside[idx[0]] = True
nt = 0
for x, y, h, ins in zip(ex, ny_, hh, inside):
    if ins: continue
    if not (h > 2 and h < 40): h = 8 + random.random() * 8
    k = tile_of(x, y)
    if 0 <= k[0] < NX and 0 <= k[1] < NY: T(k)["trees"].append((x, y, h)); nt += 1
print("trees", nt, flush=True)

# ---------------- terrain classes ----------------
water = so.unary_union([sg.shape(f["geometry"]).buffer(0) for f in load("surface_hydrographique") if f["geometry"]])
green_src = [sg.shape(f["geometry"]).buffer(0) for f in load("zone_de_vegetation") if f["geometry"]]
green_src += [sg.shape(f["geometry"]).buffer(0) for f in load("cimetiere") if f["geometry"]]
green = so.unary_union(green_src)
shapely.prepare(water); shapely.prepare(green)

# ---------------- write ----------------
os.makedirs(OUT_TILES, exist_ok=True); os.makedirs(OUT_RES, exist_ok=True)
for fn in os.listdir(OUT_TILES):
    if fn.endswith(".bin") or fn.endswith(".bin.meta"): os.remove(os.path.join(OUT_TILES, fn))
written = []
for ky in range(NY):
    for kx in range(NX):
        k = (kx, ky); t = tiles.get(k)
        if t is None or (not t["prisms"] and not t["roads"]): continue
        ox, oy = X0 + kx * TS, Y0 + ky * TS
        b = bytearray(b"PGT1"); b += struct.pack("<ff", ox - X0, oy - Y0)
        gx = ox + np.arange(GN) * GSP; gy = oy + np.arange(GN) * GSP
        GX, GY = np.meshgrid(gx, gy)
        H = dem(GX, GY)
        cls = np.zeros(GX.shape, np.uint8)
        gp = shapely.points(GX.ravel(), GY.ravel())
        cls.ravel()[shapely.contains(green, gp)] = 1
        cls.ravel()[shapely.contains(water, gp)] = 2
        b += struct.pack("<Bf", GN, GSP); b += np.clip(np.round(H * 100), 0, 65535).astype("<u2").tobytes(); b += cls.tobytes()
        b += struct.pack("<i", len(t["prisms"]))
        for pr in t["prisms"]:
            b += struct.pack("<BHHB", pr["kind"], cm(pr["z0"]), cm(pr["z1"]), len(pr["rings"]))
            for r in pr["rings"]:
                b += struct.pack("<H", len(r))
                b += np.array([[dm(x - ox), dm(y - oy)] for x, y in r], "<i2").tobytes()
            tri = np.asarray(pr["tri"], "<u2"); b += struct.pack("<H", len(tri) // 3); b += tri.tobytes()
        b += struct.pack("<i", len(t["roads"]))
        for r in t["roads"]:
            b += struct.pack("<BHH", 1 if r["bridge"] else 0, int(r["w"] * 10), len(r["pts"]))
            b += np.array([[dm(c[0] - ox), dm(c[1] - oy)] for c in r["pts"]], "<i2").tobytes()
            if r["bridge"]: b += np.array([cm(c[2] if len(c) > 2 else 0) for c in r["pts"]], "<u2").tobytes()
        b += struct.pack("<i", len(t["curbs"]))
        for c in t["curbs"]:
            b += struct.pack("<H", len(c)); b += np.array([[dm(x - ox), dm(y - oy)] for x, y in c], "<i2").tobytes()
        b += struct.pack("<i", len(t["trees"]))
        for x, y, h in t["trees"]: b += struct.pack("<hhB", dm(x - ox), dm(y - oy), int(min(255, h * 4)))
        open(os.path.join(OUT_TILES, f"t_{kx}_{ky}.bin"), "wb").write(b); written.append((kx, ky, len(b)))
sizes = [w[2] for w in written]
print("tiles", len(written), "total MB", sum(sizes) / 1e6, "max KB", max(sizes) / 1e3, "avg KB", np.mean(sizes) / 1e3, flush=True)

# ---------------- spawns ----------------
print("spawns...", flush=True)
L = np.array([l.length for l, _ in spawn_src]); P = L / L.sum()
names, nidx, spawns = [], {}, []
tries = 0
while len(spawns) < 6000 and tries < 60000:
    tries += 1
    i = np.random.choice(len(spawn_src), p=P); line, name = spawn_src[i]
    d = random.uniform(2, line.length - 2); pt = line.interpolate(d); pt2 = line.interpolate(min(line.length, d + 1))
    if not paris.contains(pt): continue
    near = btree.query(pt.buffer(70))
    if len(near) < 8: continue   # needs buildings around (not woods / big empty squares)
    if len(btree.query(pt.buffer(1.5), predicate="intersects")): continue
    arr = next((a for a, s in arrs if s.contains(pt)), 0)
    hd = math.degrees(math.atan2(pt2.x - pt.x, pt2.y - pt.y)) % 360
    if random.random() < 0.5: hd = (hd + 180) % 360
    if name not in nidx: nidx[name] = len(names); names.append(name)
    spawns.append((pt.x - X0, pt.y - Y0, hd, arr, nidx[name]))
b = bytearray(b"PGS1"); b += struct.pack("<i", len(names))
for n in names:
    e = n.encode("utf-8"); b += struct.pack("<H", len(e)); b += e
b += struct.pack("<i", len(spawns))
for x, y, hd, arr, ni in spawns: b += struct.pack("<ffHBH", x, y, int(hd * 100), arr, ni)
open(os.path.join(OUT_RES, "spawns.bytes"), "wb").write(b)
idxb = bytearray(struct.pack("<i", len(written)))
for kx, ky, _ in written: idxb += struct.pack("<HH", kx, ky)
open(os.path.join(OUT_RES, "tiles.bytes"), "wb").write(idxb)
json.dump(dict(x0=X0, y0=Y0, x1=X1, y1=Y1, tile=TS, nx=NX, ny=NY), open(os.path.join(OUT_RES, "pg_meta.json"), "w"))
print("spawns", len(spawns), "names", len(names), "tries", tries)
