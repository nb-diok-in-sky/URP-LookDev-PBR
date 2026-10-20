using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// LookDev 总控：切 HDRI、转天空、调太阳、调曝光。编辑模式下改 Inspector 就会实时生效。
// 运行模式快捷键：1~9 切 HDRI，B 显示/隐藏背景，L 开关太阳。
[ExecuteAlways]
public class LookDevController : MonoBehaviour
{
    [Header("HDRI 环境")]
    public Cubemap[] hdris;
    public int hdriIndex;
    [Range(0, 360)] public float hdriRotation;
    [Range(0, 8)] public float hdriExposure = 1f;
    [Tooltip("关掉后背景变成纯灰，但光照和反射仍来自 HDRI")]
    public bool showBackground = true;
    public Color backgroundColor = new Color(0.18f, 0.18f, 0.18f);

    [Header("太阳光")]
    public Light sun;
    public bool sunEnabled = true;
    [Range(0, 10)] public float sunIntensity = 2f;
    public Color sunColor = Color.white;
    [Range(0, 360)] public float sunYaw = 30f;
    [Range(0, 90)] public float sunPitch = 45f;
    [Tooltip("打开后转 HDRI 时太阳跟着转")]
    public bool sunFollowsHdri = true;

    [Header("后处理")]
    public Volume volume;
    [Range(-5, 5)] public float exposureEV;
    [Tooltip("关掉 = 不做色调映射，看线性原始输出")]
    public bool tonemapACES = true;
    public bool bloom = true;

    [Header("引用（场景生成时自动填好）")]
    public Material skyboxMaterial;
    public ReflectionProbe reflectionProbe;
    public Camera viewCamera;

    int _lastHash;

    void OnEnable() { _lastHash = 0; }

#if UNITY_EDITOR
    void OnValidate() { UnityEditor.EditorApplication.QueuePlayerLoopUpdate(); }

    // 编辑模式下实时探针不可靠，改为停手 0.3 秒后同步烘焙一张反射图（带粗糙度卷积）
    const string BakedReflectionPath = "Assets/LookDev/Generated/SkyReflection.exr";
    double _bakeAt = -1;

    void OnDisable() { UnityEditor.EditorApplication.update -= EditorTick; }

    void ScheduleBake()
    {
        _bakeAt = UnityEditor.EditorApplication.timeSinceStartup + 0.3;
        UnityEditor.EditorApplication.update -= EditorTick;
        UnityEditor.EditorApplication.update += EditorTick;
    }

    void EditorTick()
    {
        if (_bakeAt < 0 || UnityEditor.EditorApplication.timeSinceStartup < _bakeAt) return;
        _bakeAt = -1;
        UnityEditor.EditorApplication.update -= EditorTick;
        if (this != null && !Application.isPlaying) BakeReflectionNow();
    }

    public void BakeReflectionNow()
    {
        if (reflectionProbe == null) return;
        reflectionProbe.mode = ReflectionProbeMode.Custom;
        if (UnityEditor.Lightmapping.BakeReflectionProbe(reflectionProbe, BakedReflectionPath))
            reflectionProbe.customBakedTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture>(BakedReflectionPath);
    }
#endif

    void Update()
    {
        if (Application.isPlaying) HandleHotkeys();

        int hash = StateHash();
        if (hash == _lastHash) return;
        _lastHash = hash;
        Apply();
    }

    void HandleHotkeys()
    {
        for (int i = 0; i < 9; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i) && hdris != null && i < hdris.Length) hdriIndex = i;
        if (Input.GetKeyDown(KeyCode.B)) showBackground = !showBackground;
        if (Input.GetKeyDown(KeyCode.L)) sunEnabled = !sunEnabled;
    }

    int StateHash()
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + hdriIndex;
            h = h * 31 + (hdris != null && hdris.Length > 0 ? hdris[Mathf.Clamp(hdriIndex, 0, hdris.Length - 1)]?.GetHashCode() ?? 0 : 0);
            h = h * 31 + hdriRotation.GetHashCode();
            h = h * 31 + hdriExposure.GetHashCode();
            h = h * 31 + showBackground.GetHashCode();
            h = h * 31 + backgroundColor.GetHashCode();
            h = h * 31 + sunEnabled.GetHashCode();
            h = h * 31 + sunIntensity.GetHashCode();
            h = h * 31 + sunColor.GetHashCode();
            h = h * 31 + sunYaw.GetHashCode();
            h = h * 31 + sunPitch.GetHashCode();
            h = h * 31 + sunFollowsHdri.GetHashCode();
            h = h * 31 + exposureEV.GetHashCode();
            h = h * 31 + tonemapACES.GetHashCode();
            h = h * 31 + bloom.GetHashCode();
            return h;
        }
    }

    public void Apply()
    {
        if (skyboxMaterial != null)
        {
            if (hdris != null && hdris.Length > 0)
            {
                hdriIndex = Mathf.Clamp(hdriIndex, 0, hdris.Length - 1);
                skyboxMaterial.SetTexture("_Tex", hdris[hdriIndex]);
            }
            skyboxMaterial.SetFloat("_Rotation", hdriRotation);
            skyboxMaterial.SetFloat("_Exposure", hdriExposure);
            RenderSettings.skybox = skyboxMaterial;
        }

        if (viewCamera != null)
        {
            viewCamera.clearFlags = showBackground ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            viewCamera.backgroundColor = backgroundColor;
        }

        if (sun != null)
        {
            sun.enabled = sunEnabled;
            sun.intensity = sunIntensity;
            sun.color = sunColor;
            // Skybox/Cubemap 的 _Rotation 是让天空绕 Y 轴反向转，所以太阳用减号跟上
            float yaw = sunYaw - (sunFollowsHdri ? hdriRotation : 0f);
            sun.transform.rotation = Quaternion.Euler(sunPitch, yaw, 0f);
        }

        if (volume != null && volume.sharedProfile != null)
        {
            var profile = volume.sharedProfile;
            if (profile.TryGet(out ColorAdjustments ca)) { ca.active = true; ca.postExposure.overrideState = true; ca.postExposure.value = exposureEV; }
            if (profile.TryGet(out Tonemapping tm)) { tm.active = true; tm.mode.overrideState = true; tm.mode.value = tonemapACES ? TonemappingMode.ACES : TonemappingMode.None; }
            if (profile.TryGet(out Bloom bl)) bl.active = bloom;
        }

        // 环境光（SH）和反射都从当前天空重新算
        DynamicGI.UpdateEnvironment();
        if (reflectionProbe != null)
        {
            if (Application.isPlaying)
            {
                reflectionProbe.mode = ReflectionProbeMode.Realtime;
                reflectionProbe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                reflectionProbe.RenderProbe();
            }
#if UNITY_EDITOR
            else ScheduleBake();
#endif
        }
    }
}
