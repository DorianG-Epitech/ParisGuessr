#!/usr/bin/env python3
"""Guess map of Paris rendered from IGN BD TOPO (Etalab 2.0): buildings, roads, water, parks, railways, arrondissements.
Output: Assets/ParisGuessr/Resources/paris_map.png covering exactly the world bbox (north up)."""
import os, json, math
from PIL import Image, ImageDraw, ImageFont
import shapely.geometry as sg
D = os.environ.get("PG_DATA", "/workspace/parisguessr/data")
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
X0, Y0, X1, Y1 = 642800.0, 6857200.0, 661400.0, 6867400.0
W = 3072; H = round(W * (Y1 - Y0) / (X1 - X0)); SS = 2
S = (X1 - X0) / (W * SS)
def P(c): return ((c[0] - X0) / S, (Y1 - c[1]) / S)
def load(n): return json.load(open(os.path.join(D, "vector", n + ".geojson")))["features"]
img = Image.new("RGB", (W * SS, H * SS), (232, 230, 225)); d = ImageDraw.Draw(img)
def polys(g):
    g = sg.shape(g) if isinstance(g, dict) else g
    return list(g.geoms) if g.geom_type.startswith("Multi") else [g]
def fill(g, col):
    for p in polys(g):
        if p.geom_type != "Polygon": continue
        d.polygon([P(c) for c in p.exterior.coords], fill=col)
        for r in p.interiors: d.polygon([P(c) for c in r.coords], fill=None)
paris = sg.shape(load("commune")[0]["geometry"])
fill(paris, (246, 243, 236))
for f in load("zone_de_vegetation") + load("cimetiere") + load("terrain_de_sport"):
    if f["geometry"]: fill(f["geometry"], (205, 228, 190))
for f in load("surface_hydrographique"):
    if f["geometry"]: fill(f["geometry"], (166, 203, 230))
print("buildings", flush=True)
for f in load("batiment"):
    if f["geometry"]:
        for p in f["geometry"]["coordinates"]:
            d.polygon([P(c) for c in p[0]], fill=(221, 215, 205))
for f in load("troncon_de_voie_ferree"):
    if f["geometry"] and f["properties"].get("position_par_rapport_au_sol") in ("0", "1"):
        for l in polys(f["geometry"]): d.line([P(c) for c in l.coords], fill=(170, 170, 175), width=2 * SS)
roads = [f for f in load("troncon_de_route") if f["geometry"] and f["properties"]["etat_de_l_objet"] == "En service"
         and int(f["properties"]["position_par_rapport_au_sol"] or 0) >= 0 and f["properties"]["nature"] not in ("Escalier",)]
def rw(f):
    p = f["properties"]; imp = int(p["importance"] or 6); n = p["nature"]
    if n in ("Sentier", "Chemin"): return 1, (255, 255, 255), None
    if n == "Type autoroutier" or imp <= 2: return 6, (247, 213, 139), (205, 160, 90)
    if imp == 3: return 5, (255, 236, 180), (210, 185, 130)
    if imp == 4: return 4, (255, 255, 255), (185, 180, 172)
    return 3, (255, 255, 255), (195, 190, 182)
order = sorted(roads, key=lambda f: -rw(f)[0])
for pas in (0, 1):
    for f in sorted(roads, key=lambda f: rw(f)[0]):
        w, col, case = rw(f)
        if pas == 0 and case is None: continue
        for l in polys(f["geometry"]):
            pts = [P(c) for c in l.coords]
            if pas == 0: d.line(pts, fill=case, width=(w + 2) * SS // 1, joint="curve")
            else: d.line(pts, fill=col, width=w * SS, joint="curve")
# arrondissements
arrs = load("arrondissement_municipal")
for f in arrs:
    for p in polys(f["geometry"]):
        pts = [P(c) for c in p.exterior.coords]
        # dashed
        for i in range(0, len(pts) - 1):
            a, b = pts[i], pts[i + 1]; L = math.dist(a, b); n = max(1, int(L // (14 * SS)))
            for j in range(n):
                if j % 2: continue
                t0, t1 = j / n, min(1, (j + 1) / n)
                d.line([(a[0] + (b[0] - a[0]) * t0, a[1] + (b[1] - a[1]) * t0), (a[0] + (b[0] - a[0]) * t1, a[1] + (b[1] - a[1]) * t1)], fill=(140, 110, 190), width=2 * SS)
for p in polys(paris):
    d.line([P(c) for c in p.exterior.coords], fill=(90, 80, 110), width=3 * SS)
img = img.resize((W, H), Image.LANCZOS); d = ImageDraw.Draw(img, "RGBA")
fb = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
font = ImageFont.truetype(fb, 34); small = ImageFont.truetype(fb.replace("-Bold", "-BoldOblique") if os.path.exists(fb.replace("-Bold", "-BoldOblique")) else fb, 22)
for f in arrs:
    n = int(f["properties"]["code_insee"]) - 75100
    c = sg.shape(f["geometry"]).representative_point(); x, y = (c.x - X0) / (S * SS), (Y1 - c.y) / (S * SS)
    t = "1er" if n == 1 else f"{n}e"
    d.text((x, y), t, font=font, fill=(110, 80, 170, 200), anchor="mm", stroke_width=3, stroke_fill=(255, 255, 255, 200))
from pyproj import Transformer
tr = Transformer.from_crs("EPSG:4326", "EPSG:2154", always_xy=True)
for name, lon, lat in [("Bois de Boulogne", 2.2490, 48.8620), ("Bois de Vincennes", 2.4370, 48.8340), ("Seine", 2.2850, 48.8510), ("Seine", 2.3720, 48.8420)]:
    x, y = tr.transform(lon, lat); d.text(((x - X0) / (S * SS), (Y1 - y) / (S * SS)), name, font=small, fill=(60, 110, 60, 230) if "Bois" in name else (40, 90, 150, 230), anchor="mm", stroke_width=2, stroke_fill=(255, 255, 255, 200))
d.text((W - 10, H - 8), "Carte : IGN BD TOPO (Licence Ouverte Etalab 2.0)", font=ImageFont.truetype(fb, 16), fill=(90, 90, 90, 255), anchor="rd")
os.makedirs(os.path.join(ROOT, "Assets", "ParisGuessr", "Resources"), exist_ok=True)
img.save(os.path.join(ROOT, "Assets", "ParisGuessr", "Resources", "paris_map.png"), optimize=True)
print("map", W, H)
