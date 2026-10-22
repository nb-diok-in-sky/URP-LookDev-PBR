using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 历史版本批量截图：把 Assets/LookDev/_History/ 里每个历史版本的 shader 挂到测试球和垃圾桶上，
// 用固定机位、固定光照、固定材质参数各截一张，方便文章里"这一步改完长什么样"一一对应。
//
// 运行（要开窗口，不要 -batchmode，否则反射探针和环境光不更新）：
//   Unity.exe -projectPath <Look-Dev> -executeMethod LookDevHistoryCapture.Run
// 输入：Assets/LookDev/_History/list.tsv（每行：shader 文件名 \t 提交号 \t 时间 \t 提交说明）
// 输出：Screenshots/历史版本/*.png 和 captures.tsv
// 不会保存场景；临时改过的参考材质参数会在结束时还原。
public static class LookDevHistoryCapture
{
    const string HistoryDir = "Assets/LookDev/_History";
    const string ScenePath = "Assets/LookDev/Scenes/LookDev.unity";
    const string PrivateDir = "Assets/LookDev/MyAssets/Private";
    const string RefMatPath = "Assets/LookDev/Generated/Mat_Reference_URP.mat";
    const string OutDir = "Screenshots/历史版本";
    const int Width = 1920, Height = 1080;

    struct Preset
    {
        public string name;
        public float metallic, roughness;
        public bool needSliders;    // 只有带滑条的版本才能调参数
    }

    static readonly Preset[] SpherePresets =
    {
        new Preset { name = "默认白贴图", metallic = 1, roughness = 1, needSliders = false },
        new Preset { name = "塑料粗糙度0.4", metallic = 0, roughness = 0.4f, needSliders = true },
        new Preset { name = "金属粗糙度0.3", metallic = 1, roughness = 0.3f, needSliders = true },
    };

    static double _startAt;

    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath);
        ShaderUtil.allowAsyncCompilation = false;   // 同步编译，避免截到青色占位
        var ctrl = Object.FindObjectOfType<LookDevController>();
        ctrl.hdriIndex = 0;
        ctrl.hdriRotation = 0;
        ctrl.hdriExposure = 1;
        ctrl.showBackground = true;
        ctrl.sunEnabled = true;
        ctrl.exposureEV = 0;
        ctrl.tonemapACES = true;
        ctrl.bloom = true;
        ctrl.Apply();
        ctrl.BakeReflectionNow();
        _startAt = EditorApplication.timeSinceStartup + 5;
        EditorApplication.update += WaitThenCapture;
    }

    static void WaitThenCapture()
    {
        // 前几秒让环境光、反射、阴影都稳定下来
        var ctrl = Object.FindObjectOfType<LookDevController>();
        if (EditorApplication.timeSinceStartup < _startAt)
        {
            RenderOnce(ctrl.viewCamera);
            return;
        }
        EditorApplication.update -= WaitThenCapture;
        try { CaptureAll(ctrl); }
        catch (System.Exception e) { Debug.LogException(e); }
        ShaderUtil.allowAsyncCompilation = true;
        EditorApplication.Exit(0);
    }

    static void CaptureAll(LookDevController ctrl)
    {
        Directory.CreateDirectory(OutDir);
        var entries = File.ReadAllLines(Path.Combine(HistoryDir, "list.tsv"), Encoding.UTF8)
            .Where(l => l.Trim().Length > 0)
            .Select(l => l.Split('\t'))
            .ToList();

        var mainCam = ctrl.viewCamera;
        var mySphere = GameObject.Find("MyShader_Sphere").GetComponent<Renderer>();
        var refSphere = GameObject.Find("Reference_Sphere").GetComponent<Renderer>();
        var bin = Object.FindObjectsOfType<Transform>()
            .Where(t => t.parent == null && t.name.StartsWith("Garbage_Baken_Finally"))
            .OrderBy(t => t.name).FirstOrDefault();
        var binRenderers = bin != null ? bin.GetComponentsInChildren<Renderer>() : new Renderer[0];

        // 对比机位：不依赖场景当前的摆放，把两个球临时搬到一块空地上并排放（场景不保存，不影响原来的摆放）
        // 太阳从 -Z 往 +Z 照，所以相机放在 -Z 一侧，拍到的是迎光面
        var stage = new Vector3(0f, 0f, -20f);
        refSphere.transform.SetParent(null, true);
        mySphere.transform.SetParent(null, true);
        refSphere.transform.SetPositionAndRotation(stage + new Vector3(-0.6f, 0.5f, 0f), Quaternion.identity);
        mySphere.transform.SetPositionAndRotation(stage + new Vector3(0.6f, 0.5f, 0f), Quaternion.identity);
        refSphere.transform.localScale = mySphere.transform.localScale = Vector3.one;
        var compareCam = CloneCamera(mainCam, "_对比机位");
        compareCam.fieldOfView = 30f;
        compareCam.transform.position = stage + new Vector3(0f, 1.0f, -4.2f);
        compareCam.transform.LookAt(stage + new Vector3(0f, 0.5f, 0f));
        compareCam.nearClipPlane = 0.05f;
        Camera binCam = null;
        if (binRenderers.Length > 0)
        {
            binCam = CloneCamera(mainCam, "_垃圾桶机位");
            var dir = new Vector3(0.25f, -0.2f, 1f).normalized;   // 固定方向：从 -Z 一侧（迎光面）略微俯视
            Frame(binCam, Encapsulate(binRenderers), dir, 1.05f);
        }

        // 记下参考材质原来的参数，最后还原
        var refMat = AssetDatabase.LoadAssetAtPath<Material>(RefMatPath);
        float refMetal0 = refMat.GetFloat("_Metallic"), refRough0 = refMat.GetFloat("_Roughness");

        var originalBinMats = binRenderers.Select(r => r.sharedMaterials).ToArray();
        var originalSphereMat = mySphere.sharedMaterial;

        var tex = FindAssetTextures();
        var floor = GameObject.Find("Floor").GetComponent<Renderer>();
        var allRenderers = Object.FindObjectsOfType<Renderer>();
        var wasEnabled = allRenderers.ToDictionary(r => r, r => r.enabled);
        System.Action<IEnumerable<Renderer>> showOnly = keep =>
        {
            var set = new HashSet<Renderer>(keep) { floor };
            foreach (var r in allRenderers) r.enabled = set.Contains(r);
        };
        var refTint0 = refMat.GetColor("_BaseTint");
        var log = new StringBuilder("序号\t提交号\t机位\t材质参数\t文件\t编译\n");

        for (int i = 0; i < entries.Count; i++)
        {
            string file = entries[i][0], hash = entries[i][1];
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Path.Combine(HistoryDir, file));
            if (shader == null) { Debug.LogWarning("[History] 找不到 " + file); continue; }
            bool ok = !ShaderUtil.ShaderHasError(shader);
            var mat = new Material(shader);
            bool hasSliders = mat.HasProperty("_MetallicStrength") && mat.HasProperty("_RoughnessStrength");

            // 球：几种材质参数
            mySphere.sharedMaterial = mat;
            showOnly(new[] { refSphere, mySphere });
            foreach (var p in SpherePresets)
            {
                if (p.needSliders && !hasSliders) continue;
                // 带滑条的版本：基础色用 50% 灰（sRGB 0.5），避免白球过曝看不出明暗；早期版本没有颜色参数，只能保持默认白
                var tint = p.needSliders ? new Color(0.5f, 0.5f, 0.5f, 1f) : Color.white;
                if (hasSliders) { mat.SetFloat("_MetallicStrength", p.metallic); mat.SetFloat("_RoughnessStrength", p.roughness); }
                if (mat.HasProperty("_BaseColorTint")) mat.SetColor("_BaseColorTint", tint);
                refMat.SetColor("_BaseTint", tint);
                refMat.SetFloat("_Metallic", p.metallic);
                refMat.SetFloat("_Roughness", p.roughness);
                string name = $"{i + 1:00}_{hash}_对比_{p.name}.png";
                Capture(compareCam, Path.Combine(OutDir, name));
                log.Append($"{i + 1}\t{hash}\t对比\t{p.name}\t{name}\t{(ok ? "通过" : "失败")}\n");
            }

            // 垃圾桶：挂上资产贴图
            if (binCam != null && (mat.HasProperty("_MRO") || mat.HasProperty("_ORM")))
            {
                var binMat = new Material(shader);
                SetIfHas(binMat, "_BaseColor", tex.baseColor);
                SetIfHas(binMat, "_Normal", tex.normal);
                SetIfHas(binMat, "_MRO", tex.mro);
                SetIfHas(binMat, "_ORM", tex.mro);
                foreach (var r in binRenderers) r.sharedMaterials = Enumerable.Repeat(binMat, r.sharedMaterials.Length).ToArray();
                showOnly(binRenderers);
                string name = $"{i + 1:00}_{hash}_垃圾桶.png";
                Capture(binCam, Path.Combine(OutDir, name));
                log.Append($"{i + 1}\t{hash}\t垃圾桶\t资产贴图\t{name}\t{(ok ? "通过" : "失败")}\n");
            }
        }

        // 还原
        refMat.SetFloat("_Metallic", refMetal0);
        refMat.SetFloat("_Roughness", refRough0);
        refMat.SetColor("_BaseTint", refTint0);
        AssetDatabase.SaveAssets();
        foreach (var kv in wasEnabled) kv.Key.enabled = kv.Value;
        mySphere.sharedMaterial = originalSphereMat;
        for (int k = 0; k < binRenderers.Length; k++) binRenderers[k].sharedMaterials = originalBinMats[k];
        Object.DestroyImmediate(compareCam.gameObject);
        if (binCam != null) Object.DestroyImmediate(binCam.gameObject);

        File.WriteAllText(Path.Combine(OutDir, "captures.tsv"), log.ToString(), new UTF8Encoding(false));
        Debug.Log("[History] 截图完成，共 " + entries.Count + " 个版本");
    }

    struct AssetTextures { public Texture2D baseColor, normal, mro; }

    static AssetTextures FindAssetTextures()
    {
        var t = new AssetTextures();
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { PrivateDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string n = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (n.Contains("basecolor")) t.baseColor = tex;
            else if (n.Contains("normal")) t.normal = tex;
            else if (n.Contains("mro") || n.Contains("orm")) t.mro = tex;
        }
        return t;
    }

    static void SetIfHas(Material m, string prop, Texture tex)
    {
        if (tex != null && m.HasProperty(prop)) m.SetTexture(prop, tex);
    }

    static Camera CloneCamera(Camera src, string name)
    {
        var go = Object.Instantiate(src.gameObject);
        go.name = name;
        foreach (var c in go.GetComponents<LookDevOrbitCamera>()) Object.DestroyImmediate(c);   // 只去掉轨道相机脚本，URP 的相机数据要留着
        foreach (var l in go.GetComponents<AudioListener>()) Object.DestroyImmediate(l);
        return go.GetComponent<Camera>();
    }

    static Bounds Encapsulate(IEnumerable<Renderer> renderers)
    {
        var list = renderers.Where(r => r != null).ToList();
        var b = list[0].bounds;
        foreach (var r in list) b.Encapsulate(r.bounds);
        return b;
    }

    static void Frame(Camera cam, Bounds b, Vector3 viewDir, float margin)
    {
        float radius = b.extents.magnitude * margin;
        float halfFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float dist = radius / Mathf.Sin(halfFov);
        cam.transform.position = b.center - viewDir.normalized * dist;
        cam.transform.LookAt(b.center);
        cam.nearClipPlane = Mathf.Max(0.01f, dist - radius * 2f);
        cam.farClipPlane = 500f;
    }

    static void RenderOnce(Camera cam)
    {
        var rt = RenderTexture.GetTemporary(320, 180, 24);
        var prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prev;
        RenderTexture.ReleaseTemporary(rt);
    }

    static void Capture(Camera cam, string path)
    {
        var rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
        var prevTarget = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
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
        Object.DestroyImmediate(tex);
    }
}
