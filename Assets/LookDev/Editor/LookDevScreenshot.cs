using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

// 一键截图：按 F12（或菜单 LookDev/截图）。编辑模式、运行模式都能用。
// 截的是 LookDev 相机的画面（带 ACES、Bloom、SSAO 等后处理），固定 1920×1080，存到 工程根目录/Screenshots/，
// 同时复制到剪贴板，可以直接 Ctrl+V 贴进 PureRef。
// 编辑模式下 LookDev 相机跟着 Scene 视图走，所以截到的就是你在 Scene 视图里看的角度。
// 文件名 = 序号_标签_git提交号.png；MyAssets 里有没提交的改动时，提交号后面会加 "-未提交"。
// 标签在菜单 LookDev/截图设置 里填，比如 "坑03_背光面死黑"。
public class LookDevScreenshot : EditorWindow
{
    const string LabelKey = "LookDev.Screenshot.Label";
    const string RevealKey = "LookDev.Screenshot.Reveal";
    const string ClipboardKey = "LookDev.Screenshot.Clipboard";
    const string Folder = "Screenshots";
    const int Width = 1920, Height = 1080;

    static string _last = "（还没截过）";

    [MenuItem("LookDev/截图 _F12", priority = 0)]
    public static void Capture() { CaptureWithLabel(EditorPrefs.GetString(LabelKey, "")); }

    [MenuItem("LookDev/截图设置", priority = 1)]
    static void OpenWindow()
    {
        var w = GetWindow<LookDevScreenshot>(true, "LookDev 截图");
        w.minSize = new Vector2(380, 190);
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("标签会写进文件名，比如：坑03_背光面死黑。留空就只有序号。", EditorStyles.wordWrappedMiniLabel);
        string label = EditorGUILayout.TextField("标签", EditorPrefs.GetString(LabelKey, ""));
        EditorPrefs.SetString(LabelKey, label);
        bool clipboard = EditorGUILayout.Toggle("同时复制到剪贴板", EditorPrefs.GetBool(ClipboardKey, true));
        EditorPrefs.SetBool(ClipboardKey, clipboard);
        LookDevSceneViewFollow.Enabled = EditorGUILayout.Toggle("Game 视图跟随 Scene 视图", LookDevSceneViewFollow.Enabled);
        bool reveal = EditorGUILayout.Toggle("截完打开文件夹", EditorPrefs.GetBool(RevealKey, false));
        EditorPrefs.SetBool(RevealKey, reveal);
        GUILayout.Space(8);
        if (GUILayout.Button("截图（F12）", GUILayout.Height(30))) CaptureWithLabel(label);
        EditorGUILayout.LabelField("最近一张：" + _last, EditorStyles.wordWrappedMiniLabel);
    }

    public static string CaptureWithLabel(string label)
    {
        var cam = FindLookDevCamera();
        if (cam == null) { Debug.LogWarning("[LookDev] 场景里找不到相机，没截图。"); return null; }
        LookDevSceneViewFollow.SyncNow();

        Directory.CreateDirectory(Folder);
        string file = $"{NextIndex():000}{LabelPart(label)}_{GitTag()}.png";
        string path = Path.Combine(Folder, file);

        // URP 会直接用目标贴图的格式当相机的颜色缓冲（UniversalRenderPipelineCore.cs CreateRenderTextureDescriptor）。
        // 给 8 位的 ARGB32，亮度在渲染时就被截到 1，Bloom 和 ACES 都拿不到高光，所以先渲染到 HDR 贴图，
        // 后处理做完以后再拷到 sRGB 贴图里（写入时 GPU 自动做线性 → sRGB 转换），跟 Game 视图上屏一致。
        var hdrRT = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear);
        var prevTarget = cam.targetTexture;
        cam.targetTexture = hdrRT;
        cam.Render();
        cam.targetTexture = prevTarget;

        var ldrRT = RenderTexture.GetTemporary(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(hdrRT, ldrRT);

        var prevActive = RenderTexture.active;
        RenderTexture.active = ldrRT;
        var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        tex.Apply();
        RenderTexture.active = prevActive;
        RenderTexture.ReleaseTemporary(hdrRT);
        RenderTexture.ReleaseTemporary(ldrRT);

        File.WriteAllBytes(path, tex.EncodeToPNG());
        DestroyImmediate(tex);

        _last = file;
        bool clipboard = EditorPrefs.GetBool(ClipboardKey, true);
        if (clipboard) CopyImageToClipboard(Path.GetFullPath(path));
        Debug.Log("[LookDev] 已截图" + (clipboard ? "并复制到剪贴板" : "") + "：" + Path.GetFullPath(path));
        if (EditorPrefs.GetBool(RevealKey, false)) EditorUtility.RevealInFinder(path);
        return path;
    }

    // Unity 自己没有"把图片放进剪贴板"的接口，借 Windows 自带的 PowerShell 做；后台跑，不卡编辑器
    static void CopyImageToClipboard(string fullPath)
    {
        string p = fullPath.Replace("'", "''");
        string cmd = "Add-Type -AssemblyName System.Windows.Forms; Add-Type -AssemblyName System.Drawing; " +
                     $"$img = [System.Drawing.Image]::FromFile('{p}'); [System.Windows.Forms.Clipboard]::SetImage($img); $img.Dispose()";
        try
        {
            var psi = new ProcessStartInfo("powershell", "-NoProfile -STA -WindowStyle Hidden -Command \"" + cmd + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Process.Start(psi);
        }
        catch (System.Exception e) { Debug.LogWarning("[LookDev] 复制到剪贴板失败：" + e.Message); }
    }

    public static Camera FindLookDevCamera()
    {
        var ctrl = FindObjectOfType<LookDevController>();
        if (ctrl != null && ctrl.viewCamera != null) return ctrl.viewCamera;
        return Camera.main;
    }

    static int NextIndex()
    {
        int max = 0;
        foreach (var f in Directory.GetFiles(Folder, "*.png"))
        {
            var m = Regex.Match(Path.GetFileName(f), @"^(\d{3})");
            if (m.Success) max = Mathf.Max(max, int.Parse(m.Groups[1].Value));
        }
        return max + 1;
    }

    static string LabelPart(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "";
        foreach (char c in Path.GetInvalidFileNameChars()) label = label.Replace(c, '_');
        return "_" + label.Trim().Replace(' ', '_');
    }

    // 当前 git 提交号；MyAssets（你的 shader 和材质）里有没提交的改动就加 "-未提交"
    // 只看 MyAssets：切 HDRI、动场景这些环境改动不影响标记
    static string GitTag()
    {
        string hash = RunGit("rev-parse --short HEAD");
        if (string.IsNullOrEmpty(hash)) return "无提交";
        string dirty = RunGit("status --porcelain -- Assets/LookDev/MyAssets");
        return string.IsNullOrEmpty(dirty) ? hash : hash + "-未提交";
    }

    static string RunGit(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory = Directory.GetCurrentDirectory(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            using (var p = Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                return p.ExitCode == 0 ? output.Trim() : null;
            }
        }
        catch { return null; }
    }
}

// 编辑模式下让 LookDev 相机（Game 视图）跟着 Scene 视图的位置、角度、FOV 走。
// Scene 视图本身不带 ACES、Bloom 这些后处理，所以截图还是用 LookDev 相机，只是位置跟着你。
// 运行模式下不跟，交给 LookDevOrbitCamera。菜单 LookDev/Game 视图跟随 Scene 视图 可以开关。
[InitializeOnLoad]
public static class LookDevSceneViewFollow
{
    const string Key = "LookDev.FollowSceneView";
    const string MenuPath = "LookDev/Game 视图跟随 Scene 视图";
    static Camera _cam;

    static LookDevSceneViewFollow() { EditorApplication.update += Update; }

    public static bool Enabled
    {
        get => EditorPrefs.GetBool(Key, true);
        set => EditorPrefs.SetBool(Key, value);
    }

    [MenuItem(MenuPath, priority = 2)]
    static void Toggle() { Enabled = !Enabled; }

    [MenuItem(MenuPath, true)]
    static bool ToggleValidate() { Menu.SetChecked(MenuPath, Enabled); return true; }

    static void Update()
    {
        if (Enabled) SyncNow();
    }

    public static void SyncNow()
    {
        if (!Enabled || EditorApplication.isPlayingOrWillChangePlaymode) return;
        var view = SceneView.lastActiveSceneView;
        if (view == null || view.camera == null) return;
        if (_cam == null) _cam = LookDevScreenshot.FindLookDevCamera();
        if (_cam == null) return;

        var src = view.camera;
        var t = _cam.transform;
        // 只在有变化时才写，避免每帧都改场景
        if (t.position == src.transform.position && t.rotation == src.transform.rotation &&
            Mathf.Approximately(_cam.fieldOfView, src.fieldOfView) && _cam.orthographic == src.orthographic) return;

        t.SetPositionAndRotation(src.transform.position, src.transform.rotation);
        _cam.fieldOfView = src.fieldOfView;
        _cam.orthographic = src.orthographic;
        _cam.orthographicSize = src.orthographicSize;
    }
}
