using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// Streams 250 m tiles from StreamingAssets/tiles around a focus point: nearest first, a few downloads in flight,
/// at most one mesh build per frame. Tiles far away are destroyed.
public class TileStreamer : MonoBehaviour
{
    public Material faceMat, lineMat, hullMat;
    public float loadRadius = 420f, unloadRadius = 620f;
    public Vector3 focus;
    public bool active;

    class Tile { public int x, y; public UnityWebRequest req; public GameObject go; public bool done, failed; public byte[] bytes; public List<Object> owned = new List<Object>(); }
    readonly Dictionary<int, Tile> tiles = new Dictionary<int, Tile>();
    readonly List<Tile> pendingBuild = new List<Tile>();
    int inFlight;
    public int LoadedCount { get { int n = 0; foreach (var t in tiles.Values) if (t.done) n++; return n; } }
    public int BytesLoaded;

    string Url(int x, int y)
    {
        string p = Application.streamingAssetsPath + $"/tiles/t_{x}_{y}.bin";
        return p.Contains("://") ? p : "file://" + p;
    }

    public bool ReadyAround(Vector3 p, float r)
    {
        float ts = PGWorld.TileSize;
        int x0 = Mathf.FloorToInt((p.x - r) / ts), x1 = Mathf.FloorToInt((p.x + r) / ts);
        int y0 = Mathf.FloorToInt((p.z - r) / ts), y1 = Mathf.FloorToInt((p.z + r) / ts);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int k = PGWorld.Key(x, y);
                if (!PGWorld.Tiles.Contains(k)) continue;
                if (!tiles.TryGetValue(k, out var t) || !(t.done || t.failed)) return false;
            }
        return true;
    }

    void Update()
    {
        if (!active) return;
        float ts = PGWorld.TileSize;
        // unload
        var drop = new List<int>();
        foreach (var kv in tiles)
        {
            var t = kv.Value; var c = new Vector3((t.x + 0.5f) * ts, 0, (t.y + 0.5f) * ts);
            if (Flat(c - focus) > unloadRadius + ts * 0.71f && (t.done || t.failed)) drop.Add(kv.Key);
        }
        foreach (var k in drop) { Free(tiles[k]); tiles.Remove(k); }
        // request nearest missing
        if (inFlight < 4)
        {
            int x0 = Mathf.FloorToInt((focus.x - loadRadius) / ts), x1 = Mathf.FloorToInt((focus.x + loadRadius) / ts);
            int y0 = Mathf.FloorToInt((focus.z - loadRadius) / ts), y1 = Mathf.FloorToInt((focus.z + loadRadius) / ts);
            float best = float.MaxValue; int bx = 0, by = 0; bool found = false;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int k = PGWorld.Key(x, y);
                    if (tiles.ContainsKey(k) || !PGWorld.Tiles.Contains(k)) continue;
                    float d = DistToTile(x, y);
                    if (d > loadRadius) continue;
                    if (d < best) { best = d; bx = x; by = y; found = true; }
                }
            if (found)
            {
                var t = new Tile { x = bx, y = by, req = UnityWebRequest.Get(Url(bx, by)) };
                t.req.SendWebRequest(); inFlight++; tiles[PGWorld.Key(bx, by)] = t;
            }
        }
        // collect finished downloads
        foreach (var t in tiles.Values)
        {
            if (t.req == null || !t.req.isDone) continue;
            inFlight--;
            if (t.req.result == UnityWebRequest.Result.Success) { t.bytes = t.req.downloadHandler.data; pendingBuild.Add(t); }
            else { t.failed = true; Debug.LogWarning("[PG] tile failed " + t.x + "," + t.y + " " + t.req.error); }
            t.req.Dispose(); t.req = null;
        }
        // build one per frame (nearest first)
        if (pendingBuild.Count > 0)
        {
            pendingBuild.Sort((a, b) => DistToTile(a.x, a.y).CompareTo(DistToTile(b.x, b.y)));
            var t = pendingBuild[0]; pendingBuild.RemoveAt(0);
            if (tiles.ContainsKey(PGWorld.Key(t.x, t.y))) BuildTile(t);
        }
    }

    float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
    float DistToTile(int x, int y)
    {
        float ts = PGWorld.TileSize;
        float dx = Mathf.Max(0, Mathf.Max(x * ts - focus.x, focus.x - (x + 1) * ts));
        float dy = Mathf.Max(0, Mathf.Max(y * ts - focus.z, focus.z - (y + 1) * ts));
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    void BuildTile(Tile t)
    {
        byte[] data = t.bytes; t.bytes = null;
        if (data == null) { t.failed = true; return; }
        BytesLoaded += data.Length;
        TileBuilder.Result r;
        try { r = new TileBuilder().Build(data); }
        catch (System.Exception e) { Debug.LogError("[PG] tile build " + t.x + "," + t.y + ": " + e); t.failed = true; return; }
        var go = new GameObject($"tile_{t.x}_{t.y}"); go.transform.SetParent(transform, false); go.transform.localPosition = r.origin;
        Part(go, "faces", r.faces, faceMat, t); Part(go, "lines", r.lines, lineMat, t); if (r.hull) Part(go, "hull", r.hull, hullMat, t);
        var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = r.collider; t.owned.Add(r.collider);
        t.go = go; t.done = true;
    }

    void Part(GameObject parent, string n, Mesh m, Material mat, Tile t)
    {
        var g = new GameObject(n); g.transform.SetParent(parent.transform, false);
        g.AddComponent<MeshFilter>().sharedMesh = m;
        var mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        t.owned.Add(m);
    }

    void Free(Tile t)
    {
        if (t.req != null) { t.req.Abort(); t.req.Dispose(); inFlight--; }
        pendingBuild.Remove(t);
        if (t.go) Destroy(t.go);
        foreach (var o in t.owned) if (o) Destroy(o);
    }
}
