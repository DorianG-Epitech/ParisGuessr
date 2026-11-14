using System.Collections.Generic;
using UnityEngine;

/// Game flow + UI (IMGUI, pointer-driven so it works the same with touches and the mouse).
/// Menu -> Loading -> Explore (FPS) <-> Map (place a pin) -> Result -> ... 5 rounds -> Final.
[DefaultExecutionOrder(-100)]
public class PGGame : MonoBehaviour
{
    public PGPlayer player; public TileStreamer streamer; public Texture2D mapTex;
    public Color sky = new Color32(247, 247, 244, 255), lineColor = new Color32(34, 34, 38, 255);
    public const int Rounds = 5;
    enum State { Menu, Loading, Explore, Map, Result, Final }
    State state = State.Menu;
    class Round { public PGWorld.Spawn spawn; public Vector2 guess; public float dist; public int score; }
    readonly List<Round> rounds = new List<Round>();
    Round cur; bool hasPin; Vector2 pin;
    float loadT; PGWorld.Spawn menuSpawn; bool menuPlaced;
    // map view: centre (world metres) and scale (gui px per metre)
    Vector2 mc; float mk; Rect mapRect;
    Dictionary<int, Vector2> mapDown = new Dictionary<int, Vector2>(); Dictionary<int, float> mapDownT = new Dictionary<int, float>(); bool mapDragged; float pinchD;
    Texture2D white, dot;
    bool autoDemo; float stateT; State lastState = (State)(-1);

    void Start()
    {
        PGWorld.Load();
        Application.targetFrameRate = 60;
        if (Application.isMobilePlatform) PGInput.TouchMode = true;
        white = Texture2D.whiteTexture;
        dot = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            { float d = new Vector2(x - 31.5f, y - 31.5f).magnitude; dot.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(31.5f - d))); }
        dot.Apply();
        mapTex.wrapMode = TextureWrapMode.Clamp;
        Camera.main.backgroundColor = sky;
        // menu backdrop: a random street, slowly turning
        menuSpawn = PGWorld.Spawns[Random.Range(0, PGWorld.Spawns.Count)];
        streamer.focus = new Vector3(menuSpawn.pos.x, 0, menuSpawn.pos.y); streamer.active = true;
        foreach (var a in System.Environment.GetCommandLineArgs()) if (a == "-autodemo") autoDemo = true;
        if (Application.absoluteURL.Contains("autodemo")) autoDemo = true;
    }

    // ------------------------------------------------------------------ flow
    void NewGame() { rounds.Clear(); NextRound(); }

    void NextRound()
    {
        PGWorld.Spawn s = default;
        for (int tries = 0; tries < 200; tries++)
        {
            s = PGWorld.Spawns[Random.Range(0, PGWorld.Spawns.Count)];
            bool ok = true;
            foreach (var r in rounds) if ((r.spawn.pos - s.pos).magnitude < 1500 || r.spawn.arr == s.arr) ok = false;
            if (ok) break;
        }
        cur = new Round { spawn = s }; rounds.Add(cur); hasPin = false;
        streamer.focus = new Vector3(s.pos.x, 0, s.pos.y);
        player.controlsEnabled = false; state = State.Loading; loadT = Time.time;
        Debug.Log($"[PG] round {rounds.Count} spawn {s.pos} {s.street} arr {s.arr}");
    }

    bool PlaceAt(PGWorld.Spawn s)
    {
        var top = new Vector3(s.pos.x, 800, s.pos.y);
        if (!Physics.Raycast(top, Vector3.down, out var hit, 1000)) return false;
        player.Teleport(hit.point + Vector3.up * 0.05f, s.heading);
        return true;
    }

    void Validate()
    {
        if (!hasPin) return;
        cur.guess = pin; cur.dist = (pin - cur.spawn.pos).magnitude;
        cur.score = cur.dist < 25 ? 5000 : Mathf.RoundToInt(5000f * Mathf.Exp(-cur.dist / 1200f));
        Debug.Log($"[PG] guess dist {cur.dist:F0} m score {cur.score}");
        state = State.Result; player.controlsEnabled = false; Cursor.lockState = CursorLockMode.None;
        // frame both points
        Layout();
        // fit both pins in the part of the map below the info box (130 gui units)
        const float infoH = 130f;
        mc = (cur.guess + cur.spawn.pos) * 0.5f;
        var ext = new Vector2(Mathf.Abs(cur.guess.x - cur.spawn.pos.x), Mathf.Abs(cur.guess.y - cur.spawn.pos.y)) + Vector2.one * 400;
        mk = Mathf.Min(mapRect.width / ext.x, (mapRect.height - infoH) / ext.y) * 0.85f;
        mk = Mathf.Clamp(mk, FitScale() * 0.9f, 1.6f);
        mc.y += infoH * 0.5f / mk;
    }

    int Total() { int t = 0; foreach (var r in rounds) t += r.score; return t; }

    void OpenMap()
    {
        state = State.Map; player.controlsEnabled = false; Cursor.lockState = CursorLockMode.None;
        Debug.Log($"[PG] map opened, player at {player.transform.position}, yaw {player.yaw:F0}, moved {(new Vector2(player.transform.position.x, player.transform.position.z) - cur.spawn.pos).magnitude:F1} m");
        Layout();
        if (!hasPin) { mc = new Vector2(PGWorld.Width * 0.5f, PGWorld.Height * 0.52f); mk = FitScale() * 1.15f; }
    }
    void CloseMap() { state = State.Explore; player.controlsEnabled = true; }

    float FitScale() => Mathf.Min(mapRect.width / PGWorld.Width, mapRect.height / PGWorld.Height);

    // ------------------------------------------------------------------ update
    void Update()
    {
        if (Input.touchCount > 0) PGInput.TouchMode = true;
        PGInput.Scale = Mathf.Max(0.5f, Screen.height / (PGInput.TouchMode ? 430f : 720f));
        PGInput.Poll();
        // lighter view distance on phones
        float fogEnd = PGInput.TouchMode ? 270f : 330f;
        streamer.loadRadius = PGInput.TouchMode ? 340f : 420f; streamer.unloadRadius = streamer.loadRadius + 200f;
        player.cam.farClipPlane = fogEnd + 25f;
        Shader.SetGlobalColor("_PGFogColor", sky); Shader.SetGlobalVector("_PGFog", new Vector4(60, fogEnd, 0, 0));
        Shader.SetGlobalColor("_PGLineColor", lineColor); Shader.SetGlobalFloat("_PGLineWidth", Mathf.Max(1.4f, Screen.height / 720f * 1.8f));
        Layout();
        if (state != lastState) { lastState = state; stateT = Time.time; Debug.Log("[PG] state " + state); }
        float st = Time.time - stateT;
        switch (state)
        {
            case State.Menu:
                if (!menuPlaced && streamer.ReadyAround(streamer.focus, 150) && PlaceAt(menuSpawn)) menuPlaced = true;
                if (menuPlaced) player.yaw += Time.deltaTime * 6f;
                if (Button(BtnPlay) || Input.GetKeyDown(KeyCode.Return) || (autoDemo && menuPlaced && st > 4)) NewGame();
                break;
            case State.Loading:
                if (streamer.ReadyAround(streamer.focus, 260) && PlaceAt(cur.spawn))
                {
                    state = State.Explore; player.controlsEnabled = true;
                    Debug.Log($"[PG] round ready in {Time.time - loadT:F1}s, tiles {streamer.LoadedCount}, {streamer.BytesLoaded / 1024} KB so far");
                }
                break;
            case State.Explore:
                streamer.focus = player.transform.position;
                if (Button(BtnMap) || Input.GetKeyDown(KeyCode.M) || Input.GetKeyDown(KeyCode.Space) || (autoDemo && st > 8)) OpenMap();
                else if (Button(BtnBack) || Input.GetKeyDown(KeyCode.R)) PlaceAt(cur.spawn);
                break;
            case State.Map:
                MapInput(true);
                if (autoDemo && st > 3 && !hasPin)
                {
                    hasPin = true; pin = cur.spawn.pos + new Vector2(Random.Range(-900f, 900f), Random.Range(-600f, 600f));
                    mc = pin; mk = FitScale() * 3f; ClampView();
                }
                if (Button(BtnClose) || Input.GetKeyDown(KeyCode.Escape)) CloseMap();
                else if ((hasPin && Button(BtnValidate)) || (hasPin && Input.GetKeyDown(KeyCode.Return)) || (autoDemo && hasPin && st > 6)) Validate();
                break;
            case State.Result:
                MapInput(false);
                if (Button(BtnNext) || Input.GetKeyDown(KeyCode.Return) || (autoDemo && st > 6))
                {
                    if (rounds.Count >= Rounds) state = State.Final; else NextRound();
                }
                break;
            case State.Final:
                if (Button(BtnReplay) || Input.GetKeyDown(KeyCode.Return)) NewGame();
                break;
        }
    }

    // ------------------------------------------------------------------ layout (gui units)
    float W, H;
    Rect BtnPlay, BtnReplay, BtnMap, BtnBack, BtnClose, BtnValidate, BtnNext, BtnZoomIn, BtnZoomOut;
    void Layout()
    {
        W = Screen.width / PGInput.Scale; H = Screen.height / PGInput.Scale;
        BtnPlay = new Rect(W / 2 - 120, H * 0.58f, 240, 64);
        BtnReplay = new Rect(W / 2 - 120, H - 84, 240, 64);
        BtnMap = new Rect(W - 160, H - 92, 140, 72);
        BtnBack = new Rect(W - 130, 16, 114, 44);
        mapRect = new Rect(12, 12, W - 24, H - 24 - 72);
        BtnClose = new Rect(12, H - 70, 150, 58);
        BtnValidate = new Rect(W - 212, H - 70, 200, 58);
        BtnNext = new Rect(W - 252, H - 70, 240, 58);
        BtnZoomIn = new Rect(mapRect.xMax - 56, mapRect.y + 10, 46, 46);
        BtnZoomOut = new Rect(mapRect.xMax - 56, mapRect.y + 62, 46, 46);
    }
    bool Button(Rect r) => PGInput.Tapped(r);

    // ------------------------------------------------------------------ map interaction
    Vector2 ToScreen(Vector2 w) => new Vector2(mapRect.center.x + (w.x - mc.x) * mk, mapRect.center.y - (w.y - mc.y) * mk);
    Vector2 ToWorld(Vector2 s) => new Vector2(mc.x + (s.x - mapRect.center.x) / mk, mc.y - (s.y - mapRect.center.y) / mk);
    void ZoomAt(Vector2 s, float f)
    {
        var w = ToWorld(s);
        mk = Mathf.Clamp(mk * f, FitScale() * 0.9f, 1.6f);
        var w2 = ToWorld(s); mc += w - w2; ClampView();
    }
    void ClampView() { mc.x = Mathf.Clamp(mc.x, 0, PGWorld.Width); mc.y = Mathf.Clamp(mc.y, 0, PGWorld.Height); }

    void MapInput(bool allowPin)
    {
        if (Button(BtnZoomIn)) ZoomAt(mapRect.center, 1.6f);
        if (Button(BtnZoomOut)) ZoomAt(mapRect.center, 1 / 1.6f);
        float wheel = Input.mouseScrollDelta.y;
        if (wheel != 0 && !PGInput.TouchMode) ZoomAt(PGInput.ToGui(Input.mousePosition), Mathf.Pow(1.18f, wheel));
        var inMap = new List<PGInput.P>();
        foreach (var p in PGInput.Pointers) if (!PGInput.Consumed.Contains(p.id)) inMap.Add(p);
        int active = 0; foreach (var p in inMap) if (mapDown.ContainsKey(p.id) && p.phase != TouchPhase.Began) active++;
        if (inMap.Count >= 2 && PGInput.TouchMode)
        {
            var a = inMap[0].pos; var b = inMap[1].pos; float d = (a - b).magnitude;
            if (pinchD > 0 && d > 0) ZoomAt((a + b) * 0.5f, d / pinchD);
            pinchD = d; mapDragged = true;
            foreach (var p in inMap) if (!mapDown.ContainsKey(p.id)) { mapDown[p.id] = p.pos; mapDownT[p.id] = Time.time; }
            return;
        }
        pinchD = 0;
        foreach (var p in inMap)
        {
            if (p.phase == TouchPhase.Began)
            {
                if (!mapRect.Contains(p.pos)) continue;
                mapDown[p.id] = p.pos; mapDownT[p.id] = Time.time; mapDragged = false;
            }
            else if (!mapDown.ContainsKey(p.id)) continue;
            else if (p.phase == TouchPhase.Moved || p.phase == TouchPhase.Stationary)
            {
                if ((p.pos - mapDown[p.id]).magnitude > 9) mapDragged = true;
                if (mapDragged) { mc -= new Vector2(p.delta.x, -p.delta.y) / mk; ClampView(); }
            }
            else
            {
                if (!mapDragged && allowPin && Time.time - mapDownT[p.id] < 0.8f)
                {
                    var w = ToWorld(p.pos);
                    if (w.x >= 0 && w.y >= 0 && w.x <= PGWorld.Width && w.y <= PGWorld.Height) { hasPin = true; pin = w; }
                }
                mapDown.Remove(p.id); mapDownT.Remove(p.id);
            }
        }
    }

    // ------------------------------------------------------------------ drawing
    GUIStyle sTitle, sBig, sMid, sSmall, sBtn, sCenter;
    void Styles()
    {
        if (sTitle != null) return;
        sTitle = new GUIStyle(GUI.skin.label) { fontSize = 64, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        sTitle.normal.textColor = new Color(0.12f, 0.12f, 0.15f);
        sBig = new GUIStyle(sTitle) { fontSize = 34 };
        sMid = new GUIStyle(sTitle) { fontSize = 22, fontStyle = FontStyle.Normal };
        sCenter = new GUIStyle(sMid);
        sSmall = new GUIStyle(sMid) { fontSize = 14, alignment = TextAnchor.LowerLeft, wordWrap = true };
        sSmall.normal.textColor = new Color(0.25f, 0.25f, 0.3f);
        sBtn = new GUIStyle(sMid) { fontSize = 24, fontStyle = FontStyle.Bold };
        sBtn.normal.textColor = Color.white;
    }
    void Box(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = o; }
    void Frame(Rect r, Color c, float t = 2)
    {
        Box(new Rect(r.x, r.y, r.width, t), c); Box(new Rect(r.x, r.yMax - t, r.width, t), c);
        Box(new Rect(r.x, r.y, t, r.height), c); Box(new Rect(r.xMax - t, r.y, t, r.height), c);
    }
    void DrawBtn(Rect r, string text, bool enabled = true, bool primary = true)
    {
        Box(r, !enabled ? new Color(0.6f, 0.6f, 0.6f, 0.8f) : primary ? new Color(0.13f, 0.13f, 0.17f, 0.92f) : new Color(1, 1, 1, 0.92f));
        Frame(r, new Color(0.08f, 0.08f, 0.1f), 2);
        var st = new GUIStyle(sBtn); if (!primary) st.normal.textColor = new Color(0.1f, 0.1f, 0.12f);
        GUI.Label(r, text, st);
    }
    void Dot(Vector2 p, float size, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(new Rect(p.x - size / 2, p.y - size / 2, size, size), dot); GUI.color = o; }

    void OnGUI()
    {
        Styles();
        GUI.matrix = Matrix4x4.Scale(new Vector3(PGInput.Scale, PGInput.Scale, 1));
        switch (state)
        {
            case State.Menu:
                Box(new Rect(0, 0, W, H), new Color(1, 1, 1, 0.55f));
                GUI.Label(new Rect(0, H * 0.18f, W, 80), "Paris Guessr", sTitle);
                GUI.Label(new Rect(0, H * 0.18f + 80, W, 40), "Tu atterris dans une rue de Paris. Retrouve où tu es sur la carte.", sMid);
                GUI.Label(new Rect(0, H * 0.18f + 116, W, 30), $"{Rounds} manches · 5 000 points max par manche", sMid);
                DrawBtn(BtnPlay, "JOUER");
                GUI.Label(new Rect(16, H - 64, W - 32, 52), "Données : IGN BD TOPO® & RGE ALTI® (Licence Ouverte Etalab 2.0) · Arbres : Ville de Paris, opendata.paris.fr (ODbL). Aucune texture : formes 3D réelles en rendu contour.", sSmall);
                break;
            case State.Loading:
                Box(new Rect(0, 0, W, H), new Color(1, 1, 1, 0.85f));
                GUI.Label(new Rect(0, H / 2 - 40, W, 50), $"Manche {rounds.Count}/{Rounds}", sBig);
                GUI.Label(new Rect(0, H / 2 + 10, W, 40), "Atterrissage dans une rue de Paris…" + new string('.', (int)(Time.time * 3) % 4), sMid);
                break;
            case State.Explore: DrawHud(); break;
            case State.Map: DrawMap(false); break;
            case State.Result: DrawMap(true); break;
            case State.Final: DrawFinal(); break;
        }
    }

    void DrawHud()
    {
        var r = new Rect(16, 16, 230, 64);
        Box(r, new Color(1, 1, 1, 0.85f)); Frame(r, new Color(0.1f, 0.1f, 0.12f));
        var st = new GUIStyle(sMid) { alignment = TextAnchor.MiddleLeft, fontSize = 20 };
        GUI.Label(new Rect(28, 18, 220, 30), $"Manche {rounds.Count}/{Rounds}", st);
        GUI.Label(new Rect(28, 46, 220, 30), $"Score : {Fmt(Total())}", st);
        // compass
        var cr = new Rect(W / 2 - 150, 16, 300, 40);
        Box(cr, new Color(1, 1, 1, 0.8f)); Frame(cr, new Color(0.1f, 0.1f, 0.12f));
        string[] lab = { "N", "NE", "E", "SE", "S", "SO", "O", "NO" };
        var cs = new GUIStyle(sMid) { fontSize = 18, fontStyle = FontStyle.Bold };
        GUI.BeginGroup(cr);
        for (int i = 0; i < 8; i++)
        {
            float d = Mathf.DeltaAngle(player.yaw, i * 45f);
            if (Mathf.Abs(d) > 80) continue;
            float x = 150 + d / 80f * 150;
            cs.normal.textColor = i == 0 ? new Color(0.8f, 0.15f, 0.15f) : new Color(0.15f, 0.15f, 0.2f);
            GUI.Label(new Rect(x - 25, 0, 50, 40), lab[i], cs);
        }
        GUI.EndGroup();
        Box(new Rect(W / 2 - 1, 52, 2, 10), new Color(0.1f, 0.1f, 0.12f));
        DrawBtn(BtnMap, "CARTE");
        DrawBtn(BtnBack, "Départ", true, false);
        if (PGInput.TouchMode)
        {
            if (PGPlayer.JoyActive) { Dot(PGPlayer.JoyCenter, 120, new Color(0, 0, 0, 0.18f)); Dot(PGPlayer.JoyKnob, 54, new Color(0, 0, 0, 0.45f)); }
            else GUI.Label(new Rect(16, H - 60, W * 0.45f, 50), "Glisse à gauche pour marcher, à droite pour regarder", sSmall);
        }
        else GUI.Label(new Rect(16, H - 60, W - 200, 50), "Clic : contrôler la vue · ZQSD/WASD : marcher · Maj : courir · M : carte · R : retour au départ", sSmall);
        if (!PGInput.TouchMode && Cursor.lockState != CursorLockMode.Locked)
            GUI.Label(new Rect(0, H / 2 + 30, W, 30), "Clique pour regarder autour de toi", sMid);
    }

    void DrawMap(bool result)
    {
        Box(new Rect(0, 0, W, H), new Color(0.93f, 0.93f, 0.91f, 1));
        Box(mapRect, new Color32(232, 230, 225, 255));
        // visible part of the map texture
        float mx0 = mc.x - mapRect.width / 2 / mk, mx1 = mc.x + mapRect.width / 2 / mk;
        float mz0 = mc.y - mapRect.height / 2 / mk, mz1 = mc.y + mapRect.height / 2 / mk;
        float cx0 = Mathf.Max(0, mx0), cx1 = Mathf.Min(PGWorld.Width, mx1), cz0 = Mathf.Max(0, mz0), cz1 = Mathf.Min(PGWorld.Height, mz1);
        if (cx1 > cx0 && cz1 > cz0)
        {
            var a = ToScreen(new Vector2(cx0, cz1)); var b = ToScreen(new Vector2(cx1, cz0));
            GUI.DrawTextureWithTexCoords(new Rect(a.x, a.y, b.x - a.x, b.y - a.y), mapTex,
                new Rect(cx0 / PGWorld.Width, cz0 / PGWorld.Height, (cx1 - cx0) / PGWorld.Width, (cz1 - cz0) / PGWorld.Height));
        }
        Frame(mapRect, new Color(0.1f, 0.1f, 0.12f), 2);
        GUI.BeginGroup(mapRect);
        var off = mapRect.position;
        if (result)
        {
            var g = ToScreen(cur.guess) - off; var t = ToScreen(cur.spawn.pos) - off;
            float L = (t - g).magnitude; int n = Mathf.Max(1, (int)(L / 9));
            for (int i = 0; i <= n; i += 1) if (i % 2 == 0) Dot(Vector2.Lerp(g, t, i / (float)n), 6, new Color(0.15f, 0.15f, 0.2f));
            Pin(g, new Color(0.85f, 0.2f, 0.2f)); Pin(t, new Color(0.15f, 0.65f, 0.3f));
        }
        else if (hasPin) Pin(ToScreen(pin) - off, new Color(0.85f, 0.2f, 0.2f));
        GUI.EndGroup();
        DrawBtn(BtnZoomIn, "+", true, false); DrawBtn(BtnZoomOut, "-", true, false);
        if (!result)
        {
            DrawBtn(BtnClose, "Retour", true, false);
            DrawBtn(BtnValidate, "Valider", hasPin);
            GUI.Label(new Rect(170, H - 70, W - 400, 58), hasPin ? "Valide ou déplace ton repère" : (PGInput.TouchMode ? "Touche la carte pour placer ton repère · pince pour zoomer" : "Clique sur la carte pour placer ton repère · molette pour zoomer"), sMid);
        }
        else
        {
            var info = new Rect(mapRect.x + 12, mapRect.y + 12, Mathf.Min(520, mapRect.width - 90), 120);
            Box(info, new Color(1, 1, 1, 0.93f)); Frame(info, new Color(0.1f, 0.1f, 0.12f));
            var st = new GUIStyle(sMid) { alignment = TextAnchor.UpperLeft, fontSize = 20, wordWrap = true };
            string place = (string.IsNullOrEmpty(cur.spawn.street) ? "" : cur.spawn.street + ", ") + PGWorld.ArrName(cur.spawn.arr);
            GUI.Label(new Rect(info.x + 12, info.y + 8, info.width - 24, 54), "Tu étais : " + place, st);
            var big = new GUIStyle(sBig) { alignment = TextAnchor.UpperLeft, fontSize = 28 };
            GUI.Label(new Rect(info.x + 12, info.y + 64, info.width - 24, 44), $"{Dist(cur.dist)}  ·  +{Fmt(cur.score)} pts", big);
            GUI.Label(new Rect(12, H - 70, W - 290, 58), $"Manche {rounds.Count}/{Rounds} · total {Fmt(Total())} pts", new GUIStyle(sMid) { alignment = TextAnchor.MiddleLeft });
            DrawBtn(BtnNext, rounds.Count >= Rounds ? "Résultats" : "Manche suivante");
        }
    }

    void Pin(Vector2 p, Color c)
    {
        Dot(p, 26, new Color(0.08f, 0.08f, 0.1f)); Dot(p, 20, c); Dot(p, 7, Color.white);
    }

    void DrawFinal()
    {
        Box(new Rect(0, 0, W, H), new Color(1, 1, 1, 0.88f));
        GUI.Label(new Rect(0, H * 0.08f, W, 60), "Partie terminée", sBig);
        GUI.Label(new Rect(0, H * 0.08f + 56, W, 50), $"{Fmt(Total())} / {Fmt(Rounds * 5000)} points", new GUIStyle(sBig) { fontSize = 30 });
        var st = new GUIStyle(sMid) { fontSize = 18 };
        for (int i = 0; i < rounds.Count; i++)
        {
            var r = rounds[i];
            string place = (string.IsNullOrEmpty(r.spawn.street) ? "" : r.spawn.street + ", ") + PGWorld.ArrName(r.spawn.arr);
            GUI.Label(new Rect(0, H * 0.08f + 112 + i * 30, W, 30), $"{i + 1}. {place} — {Dist(r.dist)} — {Fmt(r.score)} pts", st);
        }
        DrawBtn(BtnReplay, "REJOUER");
    }

    static string Fmt(int n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", "\u202F");
    static string Dist(float m) => m < 1000 ? $"{m:0} m" : (m / 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).Replace(".", ",") + " km";
}
