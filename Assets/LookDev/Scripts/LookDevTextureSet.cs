using UnityEngine;

// 一套贴图同时塞给"参考材质"(URP 自带 PBR) 和"我的材质"(你手写的 shader)，左右对比。
// 你 shader 里的属性名不一样就在下面改，改完立刻生效。
[ExecuteAlways]
public class LookDevTextureSet : MonoBehaviour
{
    [Header("贴图（MRO 和法线导入时都要关 sRGB）")]
    public Texture2D baseColor;
    public Texture2D normal;
    public Texture2D mro;
    public Texture2D height;

    [Header("材质")]
    public Material referenceMaterial;
    public Material myMaterial;

    [Header("我的 shader 里的属性名")]
    public string myBaseColorProperty = "_BaseColor";
    public string myNormalProperty = "_Normal";
    public string myMROProperty = "_MRO";
    public string myHeightProperty = "_Height";

    void OnValidate() { Apply(); }
    void OnEnable() { Apply(); }

    public void Apply()
    {
        if (referenceMaterial != null)
        {
            referenceMaterial.SetTexture("_BaseMap", baseColor);
            referenceMaterial.SetTexture("_NormalMap", normal);
            referenceMaterial.SetTexture("_MROMap", mro);
        }
        if (myMaterial != null)
        {
            Set(myMaterial, myBaseColorProperty, baseColor);
            Set(myMaterial, myNormalProperty, normal);
            Set(myMaterial, myMROProperty, mro);
            Set(myMaterial, myHeightProperty, height);
        }
#if UNITY_EDITOR
        CheckImport(mro, false, "MRO");
        CheckImport(normal, false, "法线");
#endif
    }

    static void Set(Material m, string prop, Texture tex)
    {
        if (!string.IsNullOrEmpty(prop) && tex != null && m.HasProperty(prop)) m.SetTexture(prop, tex);
    }

#if UNITY_EDITOR
    static void CheckImport(Texture2D tex, bool wantSRGB, string label)
    {
        if (tex == null) return;
        var imp = UnityEditor.AssetImporter.GetAtPath(UnityEditor.AssetDatabase.GetAssetPath(tex)) as UnityEditor.TextureImporter;
        if (imp == null) return;
        bool isNormalType = imp.textureType == UnityEditor.TextureImporterType.NormalMap;
        if (imp.sRGBTexture != wantSRGB && !isNormalType)
            Debug.LogWarning($"[LookDev] {label} 贴图 {tex.name} 勾着 sRGB，数值会被错误转换，请在导入设置里关掉 sRGB (Color Texture)。", tex);
    }
#endif
}
