using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// Batch entry points:
///   PGSetup.BuildScene  -> materials, map import settings, Assets/Scenes/Paris.unity
///   PGSetup.Preview     -> renders a few street views to /workspace/parisguessr/preview_*.png (edit mode, no play)
///   PGSetup.BuildWeb    -> WebGL build in Builds/WebGL (gzip + decompression fallback for GitHub Pages)
public static class PGSetup
{
    const string Dir = "Assets/ParisGuessr";
    const string ScenePath = "Assets/Scenes/Paris.unity";

    static Material Mat(string name, string shader)
    {
        Directory.CreateDirectory(Dir + "/Materials");
        string p = $"{Dir}/Materials/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, p); }
        else m.shader = Shader.Find(shader);
        m.enableInstancing = false; EditorUtility.SetDirty(m);
        return m;
    }

    static Texture2D MapTex()
    {
        string p = Dir + "/Resources/paris_map.png";
        var ti = (TextureImporter)AssetImporter.GetAtPath(p);
        ti.textureType = TextureImporterType.Default; ti.npotScale = TextureImporterNPOTScale.None; ti.mipmapEnabled = true;
        ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 2; ti.maxTextureSize = 4096;
        ti.textureCompression = TextureImporterCompression.Uncompressed; ti.sRGBTexture = true; ti.alphaSource = TextureImporterAlphaSource.None;
        var ps = ti.GetPlatformTextureSettings("WebGL"); ps.overridden = true; ps.maxTextureSize = 4096; ps.format = TextureImporterFormat.RGB24;
        ti.SetPlatformTextureSettings(ps);
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
    }

    static void Settings()
    {
        PlayerSettings.colorSpace = ColorSpace.Gamma;
        PlayerSettings.productName = "Paris Guessr"; PlayerSettings.companyName = "Dorian Gomes";
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.shadows = UnityEngine.ShadowQuality.Disable; QualitySettings.antiAliasing = 2;
            QualitySettings.vSyncCount = 0; QualitySettings.pixelLightCount = 0; QualitySettings.realtimeReflectionProbes = false;
        }
        QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, false);
    }

    [MenuItem("ParisGuessr/Build Scene")]
    public static void BuildScene()
    {
        Settings();
        AssetDatabase.Refresh();
        var faces = Mat("PG_Faces", "PG/Faces"); var lines = Mat("PG_Lines", "PG/Lines"); var hull = Mat("PG_Hull", "PG/Hull");
        var map = MapTex();
        AssetDatabase.SaveAssets();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.fog = false; RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.white;
        RenderSettings.skybox = null;
        var pl = new GameObject("Player");
        var cc = pl.AddComponent<CharacterController>(); cc.height = 1.75f; cc.radius = 0.3f; cc.center = new Vector3(0, 0.9f, 0); cc.stepOffset = 0.45f; cc.slopeLimit = 55; cc.skinWidth = 0.04f;
        var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera"; camGo.transform.SetParent(pl.transform, false); camGo.transform.localPosition = new Vector3(0, 1.62f, 0);
        var cam = camGo.AddComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color32(247, 247, 244, 255);
        cam.nearClipPlane = 0.15f; cam.farClipPlane = 360f; cam.fieldOfView = 70; cam.allowHDR = false; cam.allowMSAA = true;
        var player = pl.AddComponent<PGPlayer>(); player.cam = cam;
        var st = new GameObject("Tiles").AddComponent<TileStreamer>(); st.faceMat = faces; st.lineMat = lines; st.hullMat = hull;
        var g = new GameObject("Game").AddComponent<PGGame>(); g.player = player; g.streamer = st; g.mapTex = map;
        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("[PG] scene built");
    }

    /// Edit-mode preview: build the tiles around a few spawns and render the camera to PNG.
    public static void Preview()
    {
        PGWorld.Load();
        var faces = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/Materials/PG_Faces.mat");
        var lines = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/Materials/PG_Lines.mat");
        var hull = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/Materials/PG_Hull.mat");
        var sky = new Color32(247, 247, 244, 255);
        Shader.SetGlobalColor("_PGFogColor", sky); Shader.SetGlobalVector("_PGFog", new Vector4(70, 330, 0, 0));
        Shader.SetGlobalColor("_PGLineColor", new Color32(34, 34, 38, 255)); Shader.SetGlobalFloat("_PGLineWidth", 2.2f);
        int[] picks = { 0, 1, 2, 3, 4, 5 };
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-picks") picks = System.Array.ConvertAll(args[i + 1].Split(','), int.Parse);
        foreach (int pi in picks)
        {
            var s = PGWorld.Spawns[pi];
            var root = new GameObject("preview");
            int tx = Mathf.FloorToInt(s.pos.x / PGWorld.TileSize), ty = Mathf.FloorToInt(s.pos.y / PGWorld.TileSize);
            for (int y = ty - 1; y <= ty + 1; y++)
                for (int x = tx - 1; x <= tx + 1; x++)
                {
                    string f = $"Assets/StreamingAssets/tiles/t_{x}_{y}.bin";
                    if (!File.Exists(f)) continue;
                    var r = new TileBuilder().Build(File.ReadAllBytes(f));
                    var go = new GameObject("t"); go.transform.SetParent(root.transform); go.transform.position = r.origin;
                    Add(go, r.faces, faces); Add(go, r.lines, lines); if (r.hull) Add(go, r.hull, hull);
                    go.AddComponent<MeshCollider>().sharedMesh = r.collider;
                }
            Physics.SyncTransforms();
            float gy = 40;
            if (Physics.Raycast(new Vector3(s.pos.x, 800, s.pos.y), Vector3.down, out var hit, 1000)) gy = hit.point.y;
            var camGo = new GameObject("cam"); var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = sky; cam.fieldOfView = 70; cam.nearClipPlane = 0.15f; cam.farClipPlane = 360;
            camGo.transform.position = new Vector3(s.pos.x, gy + 1.62f, s.pos.y); camGo.transform.rotation = Quaternion.Euler(0, s.heading, 0);
            var rt = new RenderTexture(1280, 720, 24) { antiAliasing = 4 }; cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt; var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            File.WriteAllBytes($"/workspace/parisguessr/preview_{pi}.png", tex.EncodeToPNG());
            Debug.Log($"[PG] preview {pi} {s.street} arr {s.arr} ground {gy:F1} hit {(hit.collider != null)}");
            RenderTexture.active = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(camGo); Object.DestroyImmediate(root);
        }
    }
    static void Add(GameObject parent, Mesh m, Material mat)
    {
        var g = new GameObject(m.name); g.transform.SetParent(parent.transform, false);
        g.AddComponent<MeshFilter>().sharedMesh = m; g.AddComponent<MeshRenderer>().sharedMaterial = mat;
    }

    public static void BuildWeb()
    {
        Settings();
        PlayerSettings.WebGL.template = "PROJECT:ParisGuessr";
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
        PlayerSettings.WebGL.nameFilesAsHashes = false;
        PlayerSettings.WebGL.linkerTarget = WebGLLinkerTarget.Wasm;
        PlayerSettings.WebGL.initialMemorySize = 256;
        PlayerSettings.WebGL.maximumMemorySize = 2048;
        PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;
        PlayerSettings.WebGL.showDiagnostics = false;
        PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { GraphicsDeviceType.OpenGLES3 });
        PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.WebGL, ManagedStrippingLevel.Low);
        PlayerSettings.runInBackground = false;
        PlayerSettings.SplashScreen.show = false;
        var rep = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = "Builds/WebGL", target = BuildTarget.WebGL });
        Debug.Log($"[PG] web build {rep.summary.result}, {rep.summary.totalSize / 1048576f:F1} MB, errors {rep.summary.totalErrors}, {rep.summary.totalTime}");
        if (rep.summary.result != BuildResult.Succeeded) EditorApplication.Exit(2);
    }
}
