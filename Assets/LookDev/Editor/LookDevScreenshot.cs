using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

// 一键截图：按 F12（或菜单 LookDev/截图）。编辑模式、运行模式都能用。
// 截的是 LookDev 相机的画面，固定 1920×1080，存到 工程根目录/Screenshots/。
// 文件名 = 序号_标签_git提交号.png；Assets 里有没提交的改动时，提交号后面会加 "-未提交"。
// 标签在菜单 LookDev/截图设置 里填，比如 "坑03_背光面死黑"。
public class LookDevScreenshot : EditorWindow
{
    const string LabelKey = "LookDev.Screenshot.Label";
    const string RevealKey = "LookDev.Screenshot.Reveal";
    const string Folder = "Screenshots";
    const int Width = 1920, Height = 1080;

    static string _last = "（还没截过）";

    [MenuItem("LookDev/截图 _F12", priority = 0)]
    public static void Capture() { CaptureWithLabel(EditorPrefs.GetString(LabelKey, "")); }

    [MenuItem("LookDev/截图设置", priority = 1)]
    static void OpenWindow()
    {
        var w = GetWindow<LookDevScreenshot>(true, "LookDev 截图");
        w.minSize = new Vector2(380, 150);
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("标签会写进文件名，比如：坑03_背光面死黑。留空就只有序号。", EditorStyles.wordWrappedMiniLabel);
        string label = EditorGUILayout.TextField("标签", EditorPrefs.GetString(LabelKey, ""));
        EditorPrefs.SetString(LabelKey, label);
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

        Directory.CreateDirectory(Folder);
        string file = $"{NextIndex():000}{LabelPart(label)}_{GitTag()}.png";
        string path = Path.Combine(Folder, file);

        var rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
        var prevTarget = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prevTarget;

        var prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        tex.Apply();
        RenderTexture.active = prevActive;
        RenderTexture.ReleaseTemporary(rt);

        File.WriteAllBytes(path, tex.EncodeToPNG());
        DestroyImmediate(tex);

        _last = file;
        Debug.Log("[LookDev] 已截图：" + Path.GetFullPath(path));
        if (EditorPrefs.GetBool(RevealKey, false)) EditorUtility.RevealInFinder(path);
        return path;
    }

    static Camera FindLookDevCamera()
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

    // 当前 git 提交号；Assets 里有没提交的改动就加 "-未提交"
    static string GitTag()
    {
        string hash = RunGit("rev-parse --short HEAD");
        if (string.IsNullOrEmpty(hash)) return "无提交";
        string dirty = RunGit("status --porcelain -- Assets");
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
