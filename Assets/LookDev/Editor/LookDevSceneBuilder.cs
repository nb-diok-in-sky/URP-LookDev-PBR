using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// 一键生成 LookDev 场景。菜单：LookDev/重建场景。
// 会覆盖 Assets/LookDev/Scenes/LookDev.unity 和 Assets/LookDev/Generated 下的材质，你自己的资产不要放这两个地方。
public static class LookDevSceneBuilder
{
    const string Root = "Assets/LookDev";
    const string HdriDir = Root + "/HDRI";
    const string GenDir = Root + "/Generated";
    const string ScenePath = Root + "/Scenes/LookDev.unity";
    const string RefShader = "LookDev/Reference PBR (URP, MRO)";

    static readonly string[] HdriOrder =
    {
        "studio_small_09_4k",                      // 1 摄影棚
        "kloofendal_48d_partly_cloudy_puresky_4k", // 2 户外晴天
        "hotel_room_4k",                           // 3 暖色室内
    };

    // X-Rite ColorChecker Classic 24 色（sRGB 0-255）
    static readonly int[,] Checker =
    {
        {115,82,68},{194,150,130},{98,122,157},{87,108,67},{133,128,177},{103,189,170},
        {214,126,44},{80,91,166},{193,90,99},{94,60,108},{157,188,64},{224,163,46},
        {56,61,150},{70,148,73},{175,54,60},{231,199,31},{187,86,149},{8,133,161},
        {243,243,242},{200,200,200},{160,160,160},{122,122,121},{85,85,85},{52,52,52},
    };

    [MenuItem("LookDev/重建场景 (Rebuild Scene)")]
    public static void Build()
    {
        Directory.CreateDirectory(GenDir + "/ColorChecker");
        Directory.CreateDirectory(GenDir + "/RoughnessRamp");
        Directory.CreateDirectory(Root + "/Scenes");
        Directory.CreateDirectory(Root + "/MyAssets");
        AssetDatabase.Refresh();

        var hdris = ImportHdris();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---------- 光照环境 ----------
        var skyMat = SaveMat(new Material(Shader.Find("Skybox/Cubemap")), GenDir + "/Sky_HDRI.mat");
        RenderSettings.skybox = skyMat;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.reflectionIntensity = 1f;
        RenderSettings.fog = false;
        var ls = new LightingSettings { name = "LookDevLighting", bakedGI = false, realtimeGI = false };
        AssetDatabase.DeleteAsset(GenDir + "/LookDevLighting.lighting");
        AssetDatabase.CreateAsset(ls, GenDir + "/LookDevLighting.lighting");
        Lightmapping.lightingSettings = ls;

        var sunGo = new GameObject("Sun");
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 1f;
        sun.shadowBias = 0.02f;
        sun.shadowNormalBias = 0.2f;
        var sunData = sunGo.TryGetComponent(out UniversalAdditionalLightData ld) ? ld : sunGo.AddComponent<UniversalAdditionalLightData>();
        sunData.softShadowQuality = SoftShadowQuality.High;
        RenderSettings.sun = sun;

        var probeGo = new GameObject("SkyReflectionProbe");
        probeGo.transform.position = new Vector3(0, 1, 0);
        var probe = probeGo.AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Custom; // 编辑模式烘焙，运行模式由总控切成实时
        probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
        probe.cullingMask = 0; // 只拍天空，不拍物体本身
        probe.clearFlags = ReflectionProbeClearFlags.Skybox;
        probe.hdr = true;
        probe.resolution = 512;
        probe.size = new Vector3(1000, 1000, 1000);

        // ---------- 后处理（仿虚幻默认：ACES + Bloom + 轻微暗角） ----------
        string profilePath = GenDir + "/LookDevVolume.asset";
        AssetDatabase.DeleteAsset(profilePath);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, profilePath);
        var tm = profile.Add<Tonemapping>(true); tm.mode.value = TonemappingMode.ACES;
        var bloom = profile.Add<Bloom>(true);
        bloom.threshold.value = 1f; bloom.intensity.value = 0.3f; bloom.scatter.value = 0.65f; bloom.highQualityFiltering.value = true;
        var ca = profile.Add<ColorAdjustments>(true);
        ca.postExposure.value = 0f; ca.contrast.value = 0f; ca.saturation.value = 0f; ca.hueShift.value = 0f; ca.colorFilter.value = Color.white;
        var vig = profile.Add<Vignette>(true); vig.intensity.value = 0.2f; vig.smoothness.value = 0.4f; vig.color.value = Color.black;
        foreach (var c in profile.components) { c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(c, profile); }
        EditorUtility.SetDirty(profile);

        var volGo = new GameObject("PostProcess_Volume");
        var vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.sharedProfile = profile;

        // ---------- 相机 ----------
        var camGo = new GameObject("LookDev_Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 35f;
        cam.nearClipPlane = 0.03f;
        cam.farClipPlane = 500f;
        cam.allowHDR = true;
        cam.allowMSAA = true;
        camGo.transform.position = new Vector3(0f, 1.3f, -4.5f);
        camGo.transform.LookAt(new Vector3(0f, 0.55f, 0.6f));
        var camData = cam.GetUniversalAdditionalCameraData();
        camData.renderPostProcessing = true;
        camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        camData.antialiasingQuality = AntialiasingQuality.High;
        camData.renderShadows = true;
        camGo.AddComponent<AudioListener>();

        // ---------- 总控 ----------
        var ctrlGo = new GameObject("== LookDev 总控 ==");
        var ctrl = ctrlGo.AddComponent<LookDevController>();
        ctrl.hdris = hdris;
        ctrl.skyboxMaterial = skyMat;
        ctrl.sun = sun;
        ctrl.volume = vol;
        ctrl.reflectionProbe = probe;
        ctrl.viewCamera = cam;
        var orbit = camGo.AddComponent<LookDevOrbitCamera>();
        orbit.controller = ctrl;
        orbit.pivot = new Vector3(0f, 0.55f, 0f);

        // ---------- 地面 ----------
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(6, 1, 6);
        floor.GetComponent<Renderer>().sharedMaterial = LitMat(GenDir + "/Floor.mat", Srgb(110, 110, 110), 0f, 0.2f);

        // ---------- 对比区：左参考 / 右我的 shader ----------
        var refMat = RefMat(GenDir + "/Mat_Reference_URP.mat", Color.white, 1f, 1f);
        var myMatPath = Root + "/MyAssets/Mat_MyShader.mat";
        var myMat = AssetDatabase.LoadAssetAtPath<Material>(myMatPath);
        if (myMat == null)
        {
            myMat = new Material(Shader.Find(RefShader));
            AssetDatabase.CreateAsset(myMat, myMatPath); // 放在 MyAssets 里，重建场景不会覆盖
        }

        var compare = new GameObject("== 对比区（贴图在这里填） ==");
        var texSet = compare.AddComponent<LookDevTextureSet>();
        texSet.referenceMaterial = refMat;
        texSet.myMaterial = myMat;

        var refSlot = Slot("ReferenceSlot_URP自带PBR", compare.transform, new Vector3(-0.75f, 0, 0));
        Sphere("Reference_Sphere", refSlot, new Vector3(0, 0.5f, 0), 0.9f, refMat);
        var mySlot = Slot("TestSlot_我的Shader（把模型拖到这下面）", compare.transform, new Vector3(0.75f, 0, 0));
        Sphere("MyShader_Sphere", mySlot, new Vector3(0, 0.5f, 0), 0.9f, myMat);

        // ---------- 参考球：铬球 / 18% 灰 / 白 ----------
        var refBalls = new GameObject("== 参考球 ==").transform;
        refBalls.position = new Vector3(2.4f, 0, 0.6f);
        Sphere("Chrome", refBalls, new Vector3(0, 0.25f, 0), 0.5f, LitMat(GenDir + "/Ref_Chrome.mat", Color.white, 1f, 1f));
        Sphere("Gray18", refBalls, new Vector3(0, 0.25f, 0.6f), 0.5f, LitMat(GenDir + "/Ref_Gray18.mat", new Color(0.18f, 0.18f, 0.18f).gamma, 0f, 0.1f));
        Sphere("White80", refBalls, new Vector3(0, 0.25f, 1.2f), 0.5f, LitMat(GenDir + "/Ref_White80.mat", new Color(0.8f, 0.8f, 0.8f).gamma, 0f, 0.1f));

        // ---------- 粗糙度阶梯：上排金属 / 下排非金属，粗糙度 0 → 1 ----------
        var ramp = new GameObject("== 粗糙度阶梯（URP 参考）==").transform;
        ramp.position = new Vector3(-1.25f, 0, 2.2f);
        for (int i = 0; i < 6; i++)
        {
            float r = i / 5f;
            Sphere($"Metal_R{r:0.0}", ramp, new Vector3(i * 0.5f, 0.68f, 0), 0.4f,
                RefMat($"{GenDir}/RoughnessRamp/Metal_R{i}.mat", Srgb(250, 220, 160), 1f, r));
            Sphere($"Dielectric_R{r:0.0}", ramp, new Vector3(i * 0.5f, 0.2f, 0), 0.4f,
                RefMat($"{GenDir}/RoughnessRamp/Dielectric_R{i}.mat", Srgb(180, 30, 30), 0f, r));
        }

        // ---------- 色卡 ----------
        var checker = new GameObject("== ColorChecker 色卡 ==").transform;
        checker.position = new Vector3(-2.6f, 0.02f, 0.6f);
        checker.rotation = Quaternion.Euler(0, -25f, 0);
        var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
        board.name = "Board";
        board.transform.SetParent(checker, false);
        board.transform.localPosition = new Vector3(0, 0.45f, 0.03f);
        board.transform.localScale = new Vector3(1.0f, 0.7f, 0.02f);
        board.GetComponent<Renderer>().sharedMaterial = LitMat(GenDir + "/ColorChecker/Board.mat", Srgb(20, 20, 20), 0f, 0.1f);
        for (int i = 0; i < 24; i++)
        {
            int col = i % 6, row = i / 6;
            var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
            p.name = $"Patch_{i + 1:00}";
            Object.DestroyImmediate(p.GetComponent<Collider>());
            p.transform.SetParent(checker, false);
            p.transform.localPosition = new Vector3((col - 2.5f) * 0.155f, 0.68f - row * 0.155f, 0.015f);
            p.transform.localScale = new Vector3(0.14f, 0.14f, 0.01f);
            p.GetComponent<Renderer>().sharedMaterial =
                LitMat($"{GenDir}/ColorChecker/Patch_{i + 1:00}.mat", Srgb(Checker[i, 0], Checker[i, 1], Checker[i, 2]), 0f, 0.1f);
        }

        // ---------- 默认状态 ----------
        ctrl.hdriIndex = 0;
        ctrl.Apply();
        ctrl.BakeReflectionNow();
        Selection.activeGameObject = compare;

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("[LookDev] 场景已生成：" + ScenePath);
    }

    // ---------- 辅助 ----------
    static Cubemap[] ImportHdris()
    {
        var list = new System.Collections.Generic.List<Cubemap>();
        foreach (var name in HdriOrder)
        {
            string path = $"{HdriDir}/{name}.hdr";
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) { Debug.LogWarning("[LookDev] 找不到 HDRI：" + path); continue; }
            imp.textureType = TextureImporterType.Default;
            imp.textureShape = TextureImporterShape.TextureCube;
            imp.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
            imp.mipmapEnabled = true;
            imp.sRGBTexture = false;
            imp.maxTextureSize = 4096;
            imp.textureCompression = TextureImporterCompression.CompressedHQ; // BC6H
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
            var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            if (cube != null) list.Add(cube);
        }
        return list.ToArray();
    }

    static Transform Slot(string name, Transform parent, Vector3 pos)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = pos;
        t.gameObject.AddComponent<LookDevTurntable>();
        return t;
    }

    static void Sphere(string name, Transform parent, Vector3 localPos, float diameter, Material m)
    {
        var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        s.name = name;
        s.transform.SetParent(parent, false);
        s.transform.localPosition = localPos;
        s.transform.localScale = Vector3.one * diameter;
        s.GetComponent<Renderer>().sharedMaterial = m;
    }

    static Color Srgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);

    static Material LitMat(string path, Color srgbColor, float metallic, float smoothness)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", srgbColor);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smoothness);
        return SaveMat(m, path);
    }

    static Material RefMat(string path, Color srgbColor, float metallic, float roughness)
    {
        var m = new Material(Shader.Find(RefShader));
        m.SetColor("_BaseTint", srgbColor);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Roughness", roughness);
        return SaveMat(m, path);
    }

    static Material SaveMat(Material m, string path)
    {
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // 截图检查效果（要开编辑器窗口跑，batchmode 下反射探针和环境光不会更新）：
    // Unity -projectPath ... -executeMethod LookDevSceneBuilder.CaptureAll
    static int _capIndex, _capWait;
    static LookDevController _capCtrl;

    public static void BuildAndCapture() { Build(); CaptureAll(); }

    public static void CaptureAll()
    {
        EditorSceneManager.OpenScene(ScenePath);
        _capCtrl = Object.FindObjectOfType<LookDevController>();
        _capIndex = 0;
        _capWait = 0;
        Directory.CreateDirectory("Screenshots");
        EditorApplication.update += CaptureStep;
    }

    static void CaptureStep()
    {
        var ctrl = _capCtrl;
        if (_capWait == 0)
        {
            ctrl.hdriIndex = _capIndex;
            ctrl.Apply();
            ctrl.BakeReflectionNow();
        }
        // 等几十帧让环境光和反射探针算完
        if (++_capWait < 60)
        {
            // 实时反射探针只在有相机真正渲染的帧里更新，所以等待期间也要渲染
            var warm = RenderTexture.GetTemporary(320, 180, 24);
            ctrl.viewCamera.targetTexture = warm;
            ctrl.viewCamera.Render();
            ctrl.viewCamera.targetTexture = null;
            RenderTexture.ReleaseTemporary(warm);
            EditorApplication.QueuePlayerLoopUpdate();
            return;
        }

        var cam = ctrl.viewCamera;
        var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        File.WriteAllBytes($"Screenshots/lookdev_{_capIndex + 1}_{ctrl.hdris[_capIndex].name}.png", tex.EncodeToPNG());
        cam.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);

        _capWait = 0;
        if (++_capIndex >= ctrl.hdris.Length)
        {
            EditorApplication.update -= CaptureStep;
            Debug.Log("[LookDev] 截图完成");
            if (Application.isBatchMode || System.Environment.CommandLine.Contains("-executeMethod")) EditorApplication.Exit(0);
        }
    }
}
