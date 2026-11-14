#!/usr/bin/env python3
"""ParisGuessr data fetch: IGN Géoplateforme (Licence Ouverte Etalab 2.0) for the commune of Paris (75056).
Outputs in data/: vector/<layer>.geojson (BD TOPO V3, EPSG:2154), dem/dem_4m.f32 (RGE ALTI, 4 m grid, row0 = south) + dem.json"""
import os, sys, json, time, requests, numpy as np
from concurrent.futures import ThreadPoolExecutor
D = os.environ.get("PG_DATA", "/workspace/parisguessr/data")
X0, Y0, X1, Y1 = 642800.0, 6857200.0, 661400.0, 6867400.0   # Paris bbox (L93) + margin
UA = {"User-Agent": "GrokBot-ParisGuessr/1.0"}
WMS = "https://data.geopf.fr/wms-r"; WFS = "https://data.geopf.fr/wfs/ows"

def get(url, params, tries=6):
    for t in range(tries):
        try:
            r = requests.get(url, params=params, headers=UA, timeout=180)
            if r.status_code == 200 and not r.headers.get("content-type", "").startswith(("text/xml", "application/xml")):
                return r
            print("  retry", r.status_code, r.text[:150], flush=True)
        except Exception as e:
            print("  retry", e, flush=True)
        time.sleep(3 + 4 * t)
    raise RuntimeError("failed " + str(params))

LAYERS = ["batiment", "troncon_de_route", "surface_hydrographique", "arrondissement_municipal", "zone_de_vegetation",
          "troncon_de_voie_ferree", "cimetiere", "terrain_de_sport"]

def fetch_box(L, bb):
    feats, start = [], 0
    while True:
        r = get(WFS, dict(SERVICE="WFS", VERSION="2.0.0", REQUEST="GetFeature", TYPENAMES="BDTOPO_V3:" + L,
                          OUTPUTFORMAT="application/json", SRSNAME="EPSG:2154", COUNT=5000, STARTINDEX=start,
                          BBOX=f"{bb[0]},{bb[1]},{bb[2]},{bb[3]},urn:ogc:def:crs:EPSG::2154"))
        f = r.json().get("features", []); feats += f; start += len(f)
        if len(f) < 5000: return feats

def vectors():
    nx, ny = 6, 4
    boxes = [(X0 + (X1 - X0) * i / nx, Y0 + (Y1 - Y0) * j / ny, X0 + (X1 - X0) * (i + 1) / nx, Y0 + (Y1 - Y0) * (j + 1) / ny)
             for i in range(nx) for j in range(ny)]
    for L in LAYERS:
        out = os.path.join(D, "vector", L + ".geojson")
        if os.path.exists(out): continue
        seen, feats = set(), []
        with ThreadPoolExecutor(6) as ex:
            for fs in ex.map(lambda b: fetch_box(L, b), boxes):
                for f in fs:
                    k = f.get("id") or f["properties"].get("cleabs")
                    if k in seen: continue
                    seen.add(k); feats.append(f)
        json.dump({"type": "FeatureCollection", "features": feats}, open(out, "w"))
        print("vector", L, len(feats), flush=True)

def dem():
    out = os.path.join(D, "dem", "dem_4m.f32")
    if os.path.exists(out): print("dem exists"); return
    step, T = 4.0, 1000
    W, H = int((X1 - X0) / step) + 1, int((Y1 - Y0) / step) + 1
    A = np.zeros((H, W), np.float32)
    jobs = [(ty, tx) for ty in range(0, H, T) for tx in range(0, W, T)]
    def one(job):
        ty, tx = job; w = min(T, W - tx); h = min(T, H - ty)
        bx = X0 + tx * step - step / 2; by = Y0 + ty * step - step / 2
        b = get(WMS, dict(SERVICE="WMS", VERSION="1.3.0", REQUEST="GetMap", LAYERS="ELEVATION.ELEVATIONGRIDCOVERAGE.HIGHRES",
                          STYLES="", CRS="EPSG:2154", BBOX=f"{bx},{by},{bx + w * step},{by + h * step}", WIDTH=w, HEIGHT=h,
                          FORMAT="image/x-bil;bits=32")).content
        a = np.frombuffer(b, "<f4").reshape(h, w)[::-1]
        A[ty:ty + h, tx:tx + w] = a; print("dem", ty, tx, float(a.min()), float(a.max()), flush=True)
    with ThreadPoolExecutor(4) as ex: list(ex.map(one, jobs))
    bad = (A < -50) | (A > 500)
    if bad.any(): print("nodata", int(bad.sum())); A[bad] = np.median(A[~bad])
    A.tofile(out)
    json.dump(dict(x0=X0, y0=Y0, step=step, w=W, h=H), open(os.path.join(D, "dem", "dem.json"), "w"))

if __name__ == "__main__":
    for w in (sys.argv[1:] or ["dem", "vectors"]): globals()[w]()
