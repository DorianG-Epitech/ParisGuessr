using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// Static data shipped in Resources: world bbox, tile index, spawn points (random street points inside Paris).
public static class PGWorld
{
    [Serializable] class Meta { public float x0, y0, x1, y1, tile; public int nx, ny; }
    public static float X0, Y0, Width, Height, TileSize; public static int NX, NY;
    public static HashSet<int> Tiles = new HashSet<int>();
    public struct Spawn { public Vector2 pos; public float heading; public int arr; public string street; }
    public static List<Spawn> Spawns = new List<Spawn>();
    static bool loaded;

    public static int Key(int x, int y) => y * 1000 + x;

    public static void Load()
    {
        if (loaded) return; loaded = true;
        var m = JsonUtility.FromJson<Meta>(Resources.Load<TextAsset>("pg_meta").text);
        X0 = m.x0; Y0 = m.y0; Width = m.x1 - m.x0; Height = m.y1 - m.y0; TileSize = m.tile; NX = m.nx; NY = m.ny;
        using (var r = new BinaryReader(new MemoryStream(Resources.Load<TextAsset>("tiles").bytes)))
        {
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++) { int x = r.ReadUInt16(), y = r.ReadUInt16(); Tiles.Add(Key(x, y)); }
        }
        using (var r = new BinaryReader(new MemoryStream(Resources.Load<TextAsset>("spawns").bytes)))
        {
            r.ReadBytes(4);
            int nn = r.ReadInt32(); var names = new string[nn];
            for (int i = 0; i < nn; i++) { int l = r.ReadUInt16(); names[i] = System.Text.Encoding.UTF8.GetString(r.ReadBytes(l)); }
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                var s = new Spawn();
                s.pos = new Vector2(r.ReadSingle(), r.ReadSingle()); s.heading = r.ReadUInt16() / 100f; s.arr = r.ReadByte();
                int ni = r.ReadUInt16(); s.street = ni < names.Length ? Pretty(names[ni]) : "";
                Spawns.Add(s);
            }
        }
        Debug.Log($"[PG] world {Width}x{Height} m, {Tiles.Count} tiles, {Spawns.Count} spawns");
    }

    static readonly Dictionary<string, string> Abbr = new Dictionary<string, string> {
        {"R","Rue"},{"AV","Avenue"},{"BD","Boulevard"},{"PL","Place"},{"QU","Quai"},{"QUAI","Quai"},{"IMP","Impasse"},{"PAS","Passage"},
        {"SQ","Square"},{"VLA","Villa"},{"CITE","Cité"},{"CHE","Chemin"},{"RTE","Route"},{"ALL","Allée"},{"CRS","Cours"},{"PONT","Pont"},
        {"PORT","Port"},{"RPT","Rond-point"},{"SEN","Sentier"},{"HAM","Hameau"},{"CAR","Carrefour"},{"GAL","Galerie"},{"PRV","Parvis"},
        {"ESP","Esplanade"},{"PROM","Promenade"},{"PTE","Porte"},{"CHAU","Chaussée"},{"COUR","Cour"},{"ST","Saint"},{"STE","Sainte"} };
    static readonly HashSet<string> Small = new HashSet<string> { "de", "du", "des", "la", "le", "les", "et", "en", "sur", "aux", "au", "à" };
    /// "R DE LA MONTAGNE SAINTE-GENEVIEVE" -> "Rue de la Montagne Sainte-Genevieve"
    public static string Pretty(string n)
    {
        if (string.IsNullOrEmpty(n)) return n;
        if (n.ToUpperInvariant() != n) return n; // already mixed case
        var w = n.Split(' '); var sb = new System.Text.StringBuilder();
        for (int i = 0; i < w.Length; i++)
        {
            string x = w[i]; if (x.Length == 0) continue;
            string o;
            if (i == 0 && Abbr.TryGetValue(x, out var e)) o = e;
            else if (i > 0 && Small.Contains(x.ToLowerInvariant())) o = x.ToLowerInvariant();
            else if (x.StartsWith("L'") || x.StartsWith("D'")) o = char.ToLowerInvariant(x[0]) + "'" + Cap(x.Substring(2));
            else if (x == "ST" || x == "STE") o = Abbr[x];
            else o = Cap(x);
            if (sb.Length > 0) sb.Append(' '); sb.Append(o);
        }
        return sb.ToString();
    }
    static string Cap(string x)
    {
        var parts = x.Split('-');
        for (int i = 0; i < parts.Length; i++) if (parts[i].Length > 0) parts[i] = parts[i] == "ST" ? "Saint" : parts[i] == "STE" ? "Sainte" : parts[i].Substring(0, 1) + parts[i].Substring(1).ToLowerInvariant();
        return string.Join("-", parts);
    }

    public static string ArrName(int a) => a <= 0 ? "Paris" : (a == 1 ? "1er arrondissement" : a + "e arrondissement");
}
