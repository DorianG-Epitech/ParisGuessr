using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

/// Turns a compact tile (Tools/build_tiles.py, "PGT1") into meshes: faces (flat shaded, vertex coloured), outline
/// segments (PG/Lines quads), tree hulls (PG/Hull) and a collider mesh (terrain + buildings + bridge decks).
public class TileBuilder
{
    static readonly Color32 cPave = new Color32(229, 226, 219, 255), cGreen = new Color32(203, 225, 186, 255), cWater = new Color32(178, 207, 231, 255);
    static readonly Color32 cRoad = new Color32(201, 201, 199, 0), cWall = new Color32(246, 243, 236, 255), cRoof = new Color32(214, 209, 201, 255);
    static readonly Color32 cAttic = new Color32(226, 221, 212, 255), cAtticRoof = new Color32(190, 186, 182, 255), cDeck = new Color32(212, 209, 203, 255);
    static readonly Color32 cTrunk = new Color32(160, 145, 130, 255), cCrown = new Color32(196, 222, 176, 255);

    public class Result { public Mesh faces, lines, hull, collider; public Vector3 origin; }

    // faces
    readonly List<Vector3> fv = new List<Vector3>(); readonly List<Vector3> fn = new List<Vector3>(); readonly List<Color32> fc = new List<Color32>(); readonly List<int> ft = new List<int>();
    // collider
    readonly List<Vector3> cv = new List<Vector3>(); readonly List<int> ct = new List<int>();
    // lines
    readonly List<Vector3> lv = new List<Vector3>(); readonly List<Vector3> ln = new List<Vector3>(); readonly List<Vector2> lu = new List<Vector2>(); readonly List<int> lt = new List<int>();
    // hull
    readonly List<Vector3> hv = new List<Vector3>(); readonly List<Vector3> hn = new List<Vector3>(); readonly List<int> ht = new List<int>();

    int gn; float gsp; float[] gh;

    float Ground(float x, float z)
    {
        float fx = Mathf.Clamp(x / gsp, 0, gn - 1.001f), fz = Mathf.Clamp(z / gsp, 0, gn - 1.001f);
        int ix = (int)fx, iz = (int)fz; float tx = fx - ix, tz = fz - iz;
        float a = gh[iz * gn + ix], b = gh[iz * gn + ix + 1], c = gh[(iz + 1) * gn + ix], d = gh[(iz + 1) * gn + ix + 1];
        // match the terrain triangulation (diagonal ix,iz -> ix+1,iz+1)
        return tx > tz ? a + (b - a) * tx + (d - b) * tz : a + (c - a) * tz + (d - c) * tx;
    }

    void Line(Vector3 a, Vector3 b, float faint = 0)
    {
        int i = lv.Count;
        lv.Add(a); ln.Add(b); lu.Add(new Vector2(-1, faint));
        lv.Add(a); ln.Add(b); lu.Add(new Vector2(1, faint));
        lv.Add(b); ln.Add(a); lu.Add(new Vector2(1, faint));
        lv.Add(b); ln.Add(a); lu.Add(new Vector2(-1, faint));
        lt.Add(i); lt.Add(i + 1); lt.Add(i + 2); lt.Add(i); lt.Add(i + 2); lt.Add(i + 3);
    }

    void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Color32 col, bool collide)
    {
        int i = fv.Count;
        fv.Add(a); fv.Add(b); fv.Add(c); fv.Add(d);
        for (int k = 0; k < 4; k++) { fn.Add(n); fc.Add(col); }
        ft.Add(i); ft.Add(i + 1); ft.Add(i + 2); ft.Add(i); ft.Add(i + 2); ft.Add(i + 3);
        if (collide)
        {
            int j = cv.Count; cv.Add(a); cv.Add(b); cv.Add(c); cv.Add(d);
            ct.Add(j); ct.Add(j + 1); ct.Add(j + 2); ct.Add(j); ct.Add(j + 2); ct.Add(j + 3);
        }
    }

    public Result Build(byte[] data)
    {
        var r = new BinaryReader(new MemoryStream(data));
        r.ReadBytes(4);
        var origin = new Vector3(r.ReadSingle(), 0, r.ReadSingle());
        // ---- terrain
        gn = r.ReadByte(); gsp = r.ReadSingle(); gh = new float[gn * gn];
        for (int i = 0; i < gn * gn; i++) gh[i] = r.ReadUInt16() / 100f;
        var cls = r.ReadBytes(gn * gn);
        int t0 = fv.Count;
        for (int j = 0; j < gn; j++)
            for (int i = 0; i < gn; i++)
            {
                int k = j * gn + i;
                fv.Add(new Vector3(i * gsp, gh[k], j * gsp));
                float hx = gh[j * gn + Mathf.Min(i + 1, gn - 1)] - gh[j * gn + Mathf.Max(i - 1, 0)];
                float hz = gh[Mathf.Min(j + 1, gn - 1) * gn + i] - gh[Mathf.Max(j - 1, 0) * gn + i];
                fn.Add(new Vector3(-hx, 2 * gsp, -hz).normalized);
                fc.Add(cls[k] == 2 ? cWater : cls[k] == 1 ? cGreen : cPave);
            }
        int c0 = cv.Count;
        for (int k = 0; k < gn * gn; k++) cv.Add(fv[t0 + k]);
        for (int j = 0; j < gn - 1; j++)
            for (int i = 0; i < gn - 1; i++)
            {
                int a = j * gn + i, b = a + 1, c = a + gn, d = c + 1;
                ft.Add(t0 + a); ft.Add(t0 + c); ft.Add(t0 + d); ft.Add(t0 + a); ft.Add(t0 + d); ft.Add(t0 + b);
                ct.Add(c0 + a); ct.Add(c0 + c); ct.Add(c0 + d); ct.Add(c0 + a); ct.Add(c0 + d); ct.Add(c0 + b);
            }
        // ---- prisms (buildings + attic storeys)
        int np = r.ReadInt32();
        var ring = new List<Vector2>(); var all = new List<Vector2>();
        for (int p = 0; p < np; p++)
        {
            int kind = r.ReadByte(); float z0 = r.ReadUInt16() / 100f, z1 = r.ReadUInt16() / 100f; int nr = r.ReadByte();
            all.Clear();
            Color32 wc = kind == 0 ? cWall : cAttic, rc = kind == 0 ? cRoof : cAtticRoof;
            for (int q = 0; q < nr; q++)
            {
                int n = r.ReadUInt16(); ring.Clear();
                for (int i = 0; i < n; i++) ring.Add(new Vector2(r.ReadInt16() / 10f, r.ReadInt16() / 10f));
                all.AddRange(ring);
                for (int i = 0; i < n; i++)
                {
                    Vector2 a = ring[i], b = ring[(i + 1) % n], pr = ring[(i + n - 1) % n];
                    Vector2 d = b - a; if (d.sqrMagnitude < 1e-4f) continue;
                    var nrm = new Vector3(d.y, 0, -d.x).normalized;
                    Quad(new Vector3(a.x, z0, a.y), new Vector3(b.x, z0, b.y), new Vector3(b.x, z1, b.y), new Vector3(a.x, z1, a.y), nrm, wc, true);
                    Line(new Vector3(a.x, z1, a.y), new Vector3(b.x, z1, b.y));
                    if (kind == 1) Line(new Vector3(a.x, z0, a.y), new Vector3(b.x, z0, b.y));
                    else if (d.sqrMagnitude > 9f)
                    {
                        // faint storey lines (ground floor ~4 m, then ~3 m) to give the facades a Parisian scale
                        float g0 = Mathf.Max(Ground(a.x, a.y), Ground(b.x, b.y));
                        for (float fz = g0 + 4.0f; fz < z1 - 1.2f; fz += 3.0f)
                            Line(new Vector3(a.x, fz, a.y), new Vector3(b.x, fz, b.y), 1);
                    }
                    else
                    {
                        // base line drawn at ground level (z0 is sunk below the ground)
                        float ga = Ground(a.x, a.y), gb = Ground(b.x, b.y);
                        Line(new Vector3(a.x, Mathf.Max(z0, ga), a.y), new Vector3(b.x, Mathf.Max(z0, gb), b.y));
                    }
                    Vector2 e = a - pr;
                    if (e.sqrMagnitude > 1e-4f && Vector2.Dot(e.normalized, d.normalized) < 0.87f)
                        Line(new Vector3(a.x, kind == 0 ? Mathf.Max(z0, Ground(a.x, a.y)) : z0, a.y), new Vector3(a.x, z1, a.y));
                }
            }
            int nt = r.ReadUInt16();
            int baseV = fv.Count, baseC = cv.Count;
            foreach (var v in all) { var w = new Vector3(v.x, z1, v.y); fv.Add(w); fn.Add(Vector3.up); fc.Add(rc); cv.Add(w); }
            for (int i = 0; i < nt * 3; i++) { int idx = r.ReadUInt16(); ft.Add(baseV + idx); ct.Add(baseC + idx); }
        }
        // ---- roads (ribbons draped on terrain; bridges carry their own deck altitude)
        int nroads = r.ReadInt32();
        var pts = new List<Vector2>(); var zs = new List<float>();
        for (int k = 0; k < nroads; k++)
        {
            bool bridge = r.ReadByte() != 0; float w = r.ReadUInt16() / 10f; int n = r.ReadUInt16();
            pts.Clear(); zs.Clear();
            for (int i = 0; i < n; i++) pts.Add(new Vector2(r.ReadInt16() / 10f, r.ReadInt16() / 10f));
            if (bridge) for (int i = 0; i < n; i++) zs.Add(r.ReadUInt16() / 100f);
            if (n < 2) continue;
            Vector3 pl = default, pr = default;
            int lat = bridge ? 1 : Mathf.Clamp(Mathf.CeilToInt(w / 2.5f), 1, 10);
            var prevRow = new Vector3[lat + 1]; var row = new Vector3[lat + 1];
            for (int i = 0; i < n; i++)
            {
                Vector2 t = (i == 0 ? pts[1] - pts[0] : i == n - 1 ? pts[i] - pts[i - 1] : (pts[i + 1] - pts[i - 1]));
                if (t.sqrMagnitude < 1e-6f) t = Vector2.right;
                t.Normalize(); var side = new Vector2(-t.y, t.x) * (w * 0.5f);
                Vector2 L = pts[i] + side, R = pts[i] - side;
                Vector3 l3, r3;
                if (bridge) { l3 = new Vector3(L.x, zs[i], L.y); r3 = new Vector3(R.x, zs[i], R.y); }
                else
                {
                    for (int j = 0; j <= lat; j++)
                    {
                        var q = Vector2.Lerp(L, R, j / (float)lat);
                        row[j] = new Vector3(q.x, Ground(q.x, q.y) + 0.03f, q.y);
                    }
                    l3 = row[0]; r3 = row[lat];
                }
                if (i > 0)
                {
                    if (bridge)
                    {
                        Quad(pl, l3, r3, pr, Vector3.up, cDeck, true);
                        var dn = Vector3.down * 1.2f;
                        Quad(pl, l3, l3 + dn, pl + dn, Vector3.Cross(l3 - pl, Vector3.up).normalized * -1f, cDeck, false);
                        Quad(pr, r3, r3 + dn, pr + dn, Vector3.Cross(r3 - pr, Vector3.up).normalized, cDeck, false);
                        Line(pl, l3); Line(pr, r3); Line(pl + dn, l3 + dn); Line(pr + dn, r3 + dn);
                        // parapets
                        var up = Vector3.up * 1.0f;
                        Quad(pl, l3, l3 + up, pl + up, Vector3.up, cDeck, true); Quad(pr, r3, r3 + up, pr + up, Vector3.up, cDeck, true);
                        Line(pl + up, l3 + up); Line(pr + up, r3 + up);
                    }
                    else
                        for (int j = 0; j < lat; j++) Quad(prevRow[j], row[j], row[j + 1], prevRow[j + 1], Vector3.up, cRoad, false);
                }
                pl = l3; pr = r3;
                var tmp = prevRow; prevRow = row; row = tmp;
            }
        }
        // ---- curbs (outline of the road surface), draped
        int nc = r.ReadInt32();
        for (int k = 0; k < nc; k++)
        {
            int n = r.ReadUInt16(); Vector3 prev = default;
            for (int i = 0; i < n; i++)
            {
                float x = r.ReadInt16() / 10f, z = r.ReadInt16() / 10f;
                var p3 = new Vector3(x, Ground(x, z) + 0.05f, z);
                if (i > 0) Line(prev, p3, -1);
                prev = p3;
            }
        }
        // ---- trees
        int ntr = r.ReadInt32();
        for (int k = 0; k < ntr; k++)
        {
            float x = r.ReadInt16() / 10f, z = r.ReadInt16() / 10f, h = r.ReadByte() / 4f;
            float g = Ground(x, z);
            uint hs = (uint)(x * 73856093) ^ (uint)(z * 19349663);
            float rnd = (hs % 1000) / 1000f;
            Tree(new Vector3(x, g, z), h, rnd);
        }
        var res = new Result { origin = origin };
        res.faces = Make("faces", fv, fn, fc, null, ft);
        res.lines = Make("lines", lv, ln, null, lu, lt);
        res.hull = hv.Count > 0 ? Make("hull", hv, hn, null, null, ht) : null;
        var cm = new Mesh { name = "col", indexFormat = IndexFormat.UInt32 }; cm.SetVertices(cv); cm.SetTriangles(ct, 0); res.collider = cm;
        return res;
    }

    static Mesh Make(string name, List<Vector3> v, List<Vector3> n, List<Color32> c, List<Vector2> uv, List<int> t)
    {
        var m = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        m.SetVertices(v); if (n != null) m.SetNormals(n); if (c != null) m.SetColors(c); if (uv != null) m.SetUVs(0, uv);
        m.SetTriangles(t, 0, true);
        if (name == "lines") m.bounds = new Bounds(m.bounds.center, m.bounds.size + Vector3.one * 2);
        m.UploadMeshData(true);
        return m;
    }

    // low-poly tree: square trunk + icosahedron crown, crown hull for the outline
    static Vector3[] ico; static int[] icoT;
    static void InitIco()
    {
        if (ico != null) return;
        float t = (1 + Mathf.Sqrt(5)) / 2;
        ico = new[] { new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0), new Vector3(0, -1, t), new Vector3(0, 1, t),
            new Vector3(0, -1, -t), new Vector3(0, 1, -t), new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1) };
        for (int i = 0; i < 12; i++) ico[i] = ico[i].normalized;
        icoT = new[] { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
    }
    void Tree(Vector3 p, float h, float rnd)
    {
        InitIco();
        float tr = Mathf.Clamp(h * 0.012f, 0.1f, 0.25f), th = h * 0.5f;
        var c = new[] { new Vector3(-tr, 0, -tr), new Vector3(tr, 0, -tr), new Vector3(tr, 0, tr), new Vector3(-tr, 0, tr) };
        for (int i = 0; i < 4; i++)
        {
            Vector3 a = p + c[i], b = p + c[(i + 1) % 4];
            Quad(a, b, b + Vector3.up * th, a + Vector3.up * th, ((a + b) * 0.5f - p).normalized, cTrunk, false);
        }
        float cr = Mathf.Min(h * (0.19f + 0.06f * rnd), 4.5f);
        var center = p + Vector3.up * (h - cr * 1.2f);
        var sc = new Vector3(cr, cr * 1.25f, cr);
        float rot = rnd * 6.28f; var q = Quaternion.Euler(0, rot * Mathf.Rad2Deg, 0);
        for (int f = 0; f < 20; f++)
        {
            int i = fv.Count;
            Vector3 a = center + q * Vector3.Scale(ico[icoT[f * 3]], sc), b = center + q * Vector3.Scale(ico[icoT[f * 3 + 1]], sc), d = center + q * Vector3.Scale(ico[icoT[f * 3 + 2]], sc);
            var n = Vector3.Cross(b - a, d - a).normalized; if (Vector3.Dot(n, a - center) < 0) n = -n;
            fv.Add(a); fv.Add(b); fv.Add(d); fn.Add(n); fn.Add(n); fn.Add(n); fc.Add(cCrown); fc.Add(cCrown); fc.Add(cCrown);
            ft.Add(i); ft.Add(i + 1); ft.Add(i + 2);
        }
        int h0 = hv.Count;
        for (int i = 0; i < 12; i++) { var d = q * Vector3.Scale(ico[i], sc); hv.Add(center + d); hn.Add(d.normalized); }
        for (int f = 0; f < 20; f++)
        {
            int a = h0 + icoT[f * 3], b = h0 + icoT[f * 3 + 1], d = h0 + icoT[f * 3 + 2];
            // front face (Unity: Cross(b-a, c-a) toward viewer) must point outward so Cull Front keeps the inner shell
            if (Vector3.Dot(Vector3.Cross(hv[b] - hv[a], hv[d] - hv[a]), hv[a] - center) < 0) { int tmp = b; b = d; d = tmp; }
            ht.Add(a); ht.Add(b); ht.Add(d);
        }
        // trunk hull (4 side verts top/bottom)
        int t0 = hv.Count;
        for (int i = 0; i < 4; i++) { hv.Add(p + c[i]); hn.Add(c[i].normalized); hv.Add(p + c[i] + Vector3.up * th); hn.Add(c[i].normalized); }
        for (int i = 0; i < 4; i++)
        {
            int a = t0 + i * 2, b = t0 + ((i + 1) % 4) * 2;
            Vector3 outw = (c[i] + c[(i + 1) % 4]).normalized;
            if (Vector3.Dot(Vector3.Cross(hv[b] - hv[a], hv[b + 1] - hv[a]), outw) >= 0) { ht.Add(a); ht.Add(b); ht.Add(b + 1); ht.Add(a); ht.Add(b + 1); ht.Add(a + 1); }
            else { ht.Add(a); ht.Add(b + 1); ht.Add(b); ht.Add(a); ht.Add(a + 1); ht.Add(b + 1); }
        }
    }
}
