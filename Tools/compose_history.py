# 用法：python Tools/compose_history.py（需要 Pillow）
# 把同一个提交的"改之前 / 改之后效果图 + 代码改动图 + 注释"拼成一张大图，写文章时一张图讲一段
import os, re
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHOTS = os.path.join(ROOT, "Screenshots", "历史版本")
CODE = os.path.join(ROOT, "Screenshots", "代码改动")
OUT = os.path.join(ROOT, "Screenshots", "合成")
W = 1920
F_TITLE = ImageFont.truetype(r"C:\Windows\Fonts\msyhbd.ttc", 34)
F_LABEL = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 26)
F_NOTE = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 28)
BG = (24, 24, 24); TITLE_BG = (0, 122, 204); NOTE_BG = (38, 38, 42); LABEL = (200, 200, 200)

# 每个版本的注释：改了什么、为什么、画面有什么变化（从踩坑日志提炼）。第一个字段是内部备忘，不画到图上
NOTES = {
    "cd46586": ("草稿", ["手写的第一版，编译不过。Console 里报错刷了上百条（同一个错误会在多个变体里重复报，还会连带出假报错），实际改了 27 处、六十多行。",
                       "粉色 / 错误色就是 shader 编译失败时 Unity 显示的样子。"]),
    "5ee7da2": ("坑01～05 → 坑06", ["修完编译错误，第一次编译通过：PI 撞名、TEXTURE2D 没带括号、切线要 float4、half4 构造器、EnvBRDFApprox 是 UE 的函数。",
                                 "画面：右边的球全黑，看着像透明。真正原因要到第 04 版才找到。",
                                 "另外 MRO 没挂贴图 → 默认白 → 金属度 1，漫反射整块被乘 0，所以前几刀改了都看不出变化。"]),
    "acd78a1": ("坑06 进展", ["normal 从 float 改成 float3（法线被截成一维）；shadowCoord 改用世界坐标；_Normal 默认值从 white 改成 bump。",
                             "画面：从全黑变成光照反着 —— 迎光面黑、背光边缘亮。光源方向没错，问题在和它点乘的那一侧。"]),
    "7227380": ("坑06 解决", ["viewDir 从 positionCS 改成 positionWS → 球不再\"透明\"。",
                             "片元阶段的 SV_POSITION 是屏幕像素坐标，不是裁剪空间坐标。球默认是纯金属 = 一面镜子，视线算错，就照出了背后的景色。",
                             "同时加了调试下拉菜单。"]),
    "312879e": ("重构", ["点乘打包成 MyLightContext，BRDF 各项打包成 MyBRDFContext；半程向量改成除以 length（|l+v| 是长度不是绝对值）。",
                        "画面：光照又反了 —— 亮面变灰，暗面全黑、边缘发白有闪光点。"]),
    "772676f": ("坑07", ["调试菜单的 NoL 视图：NoL 在函数里算，Frag 里没有这个变量，改成读 mainLightContext.NoL。", "画面和上一版相同。"]),
    "b1722ed": ("整理格式", ["只改缩进、空格和命名，逻辑没动。画面和上一版相同。"]),
    "de21b38": ("坑09", ["MyDirectBRDF 最后乘的 NoL 加 saturate → 暗部边缘发光消失。菲涅尔改用 VoH，分母改用 max。",
                        "一个 saturate 同时盖住了三个问题：分母只剩 0.0001 放大一万倍、负数进了后处理、菲涅尔用 NoL 冲到几十。"]),
    "c325409": ("坑10 转折", ["G 项改用 NoL（原来用 VoL，参数顺序也反了）；菲涅尔返回 float3；漫反射也乘上光照。",
                             "画面：第一次出现柔和的明暗交界线。漫反射终于乘上了 saturate(NoL) —— 这就是兰伯特。"]),
    "4f9f2d0": ("坑11-1", ["D 项：α = 粗糙度²，GGX 公式里用的是 α²（= 粗糙度的四次方）。", "默认粗糙度 1 时看不出区别。"]),
    "dd5938d": ("坑11-2", ["F 字段改 float3；漫反射和高光一起乘 radiance × saturate(NoL)，不再改写结构体（原来附加光每调用一次漫反射就被多乘一次）。"]),
    "9e66d73": ("坑11-3", ["EnvBRDFApprox 的第一个参数改传 F0。原来传的是直接光结果 —— UE 里叫 SpecularColor，指的是材质的 F0，不是高光的计算结果。",
                          "画面：间接高光回来了，球明显变白。"]),
    "84ea53b": ("坑12", ["AO 只乘间接光（直接光被挡由阴影负责）；删掉最后多加的那层不受光照影响的漫反射。"]),
    "9a17664": ("D 项除法", ["D 项分母整个加括号，乘法改成真正的除法。",
                            "默认粗糙度 1 时 NoH²(α²-1)+1 恒等于 1，乘和除结果一样 —— 测试点恰好是让 bug 隐身的特例。"]),
    "7a2bab5": ("坑13", ["轮廓边缘一圈黑：NoV 防负，NoH、VoH 在 Init 里钳住。"]),
    "815de33": ("测试滑条", ["加金属度、粗糙度、AO、基础色四个滑条；附加光每盏灯单独算 BRDF。",
                            "从这一版起多一排对比：50% 灰的塑料球（粗糙度 0.4）。"]),
    "6364c23": ("AO 范围", ["AO 强度滑条从 0～3 改成 0～1：AO 物理上不超过 1，超过会把间接光放大。"]),
    "83cc76b": ("坑14", ["直接光乘回 π。RenderDoc 量出：只有直接光时我的是 URP 的 0.36 倍（≈ 1/π），间接光对得上（1.01）。",
                        "URP 的公式里省掉了 π（Unity 灯光强度里预乘了 π）。乘回后比值 1.045。"]),
    "9c0eb83": ("坑15", ["漫反射乘 (1 - F)：被菲涅尔反射走的光变成高光，剩下的才变成漫反射（LearnOpenGL：kD = 1 - kS）。",
                        "比值 1.045 → 1.003，和 URP 对齐。"]),
    "0d174db": ("坑16", ["间接漫反射也乘 (1 - F)，F 用带粗糙度的版本 fresnelSchlickRoughness（间接光没有半程向量，用 NoV）。比值 1.003。"]),
    "bd6c5cf": ("三个 pass", ["删掉高度贴图；UsePass 借 URP Lit 的 SHADOWCASTER / DEPTHONLY / DEPTHNORMALS。",
                             "画面：你的球第一次有影子（对比之前的版本，左边参考球一直有影子，右边没有），SSAO 也能用了。"]),
}


def wrap(text, font, width):
    lines, cur = [], ""
    for ch in text:
        if font.getlength(cur + ch) > width:
            lines.append(cur); cur = ch
        else:
            cur += ch
    if cur:
        lines.append(cur)
    return lines


def fit(img, width):
    if img.width == width:
        return img
    return img.resize((width, int(img.height * width / img.width)), Image.LANCZOS)


def labeled(img, text):
    pad = 40
    out = Image.new("RGB", (img.width, img.height + pad), BG)
    out.paste(img, (0, pad))
    ImageDraw.Draw(out).text((10, 4), text, font=F_LABEL, fill=LABEL)
    return out


def side_by_side(left, right, l_label, r_label):
    half = W // 2
    a = labeled(fit(left, half - 5), l_label) if left else None
    b = labeled(fit(right, half - 5), r_label)
    h = max(b.height, a.height if a else 0)
    row = Image.new("RGB", (W, h), BG)
    if a:
        row.paste(a, (0, 0))
        row.paste(b, (half + 5, 0))
    else:
        row.paste(b, ((W - b.width) // 2, 0))
    return row


def shot(idx, h, preset):
    p = os.path.join(SHOTS, f"{idx:02d}_{h}_对比_{preset}.png")
    return Image.open(p).convert("RGB") if os.path.exists(p) else None


def main():
    os.makedirs(OUT, exist_ok=True)
    rows = []
    for l in open(os.path.join(ROOT, "Screenshots", "历史版本", "captures.tsv"), encoding="utf-8").read().splitlines()[1:]:
        parts = l.split("\t")
        if len(parts) >= 2:
            rows.append((int(parts[0]), parts[1]))
    versions = sorted(set(rows))
    made = []
    for k, (idx, h) in enumerate(versions):
        prev = versions[k - 1] if k > 0 else None
        tag, notes = NOTES.get(h, ("", []))
        blocks = []

        # 标题
        title = Image.new("RGB", (W, 60), TITLE_BG)
        ImageDraw.Draw(title).text((20, 8), f"第 {idx:02d} 版   提交 {h}", font=F_TITLE, fill=(255, 255, 255))
        blocks.append(title)

        # 注释
        lines = []
        for n in notes:
            lines += wrap("· " + n, F_NOTE, W - 60)
        note = Image.new("RGB", (W, 24 + 42 * len(lines)), NOTE_BG)
        dn = ImageDraw.Draw(note)
        for i, ln in enumerate(lines):
            dn.text((30, 12 + 42 * i), ln, font=F_NOTE, fill=(235, 235, 235))
        blocks.append(note)

        # 前后对比：默认白贴图
        after = shot(idx, h, "默认白贴图")
        before = shot(prev[0], prev[1], "默认白贴图") if prev else None
        blocks.append(side_by_side(before, after,
                                   f"改之前（第 {prev[0]:02d} 版）  左 URP 参考 / 右 我的" if prev else "",
                                   f"改之后（第 {idx:02d} 版）  左 URP 参考 / 右 我的   不挂贴图：金属度 1、粗糙度 1"))
        # 有滑条的版本再加一排塑料球
        after_p = shot(idx, h, "塑料粗糙度0.4")
        if after_p:
            before_p = shot(prev[0], prev[1], "塑料粗糙度0.4") if prev else None
            blocks.append(side_by_side(before_p, after_p,
                                       f"改之前（第 {prev[0]:02d} 版）  塑料：金属度 0、粗糙度 0.4、50% 灰" if before_p else "",
                                       f"改之后（第 {idx:02d} 版）  塑料：金属度 0、粗糙度 0.4、50% 灰"))

        # 代码改动（多页的只放第一页，其余在 代码改动 文件夹里）
        code_files = sorted(f for f in os.listdir(CODE) if f.startswith(f"{idx:02d}_{h}_代码"))
        if code_files:
            img = Image.open(os.path.join(CODE, code_files[0])).convert("RGB")
            if img.width > W:
                img = fit(img, W)
            pad = Image.new("RGB", (W, img.height), (30, 30, 30))
            pad.paste(img, (0, 0))
            label = "代码改动（红 = 删掉，绿 = 加上）" + (f"  共 {len(code_files)} 张，这里是第 1 张，其余见 Screenshots/代码改动/" if len(code_files) > 1 else "")
            blocks.append(labeled(pad, label))

        total = sum(b.height for b in blocks) + 8 * (len(blocks) - 1)
        canvas = Image.new("RGB", (W, total), BG)
        y = 0
        for b in blocks:
            canvas.paste(b, (0, y)); y += b.height + 8
        name = f"{idx:02d}_{h}.png"
        canvas.save(os.path.join(OUT, name), optimize=True)
        made.append(name)
    print(len(made))


main()
