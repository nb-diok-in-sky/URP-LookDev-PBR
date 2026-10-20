# Look-Dev（URP 资产 / BRDF 检验场景）

Unity 2022.3.62f3c1 + URP 14.0.12，渲染设置沿用 BRDF_FunctionTest（线性色彩空间，URP-HighFidelity）。

## 打开
Unity Hub → Add → 选本工程文件夹 → 打开 `Assets/LookDev/Scenes/LookDev.unity`。

## 场景里有什么
| 物体 | 用途 |
|---|---|
| `== LookDev 总控 ==` | 切 HDRI、转天空、太阳方向/强度、曝光、ACES 开关、Bloom 开关、背景开关 |
| `== 对比区（贴图在这里填） ==` | 填一次贴图，同时塞给左边的参考材质和右边你的材质 |
| `ReferenceSlot_URP自带PBR` | 左：`LookDev/Reference PBR (URP, MRO)`，光照用 URP 官方 BRDF，当标准答案 |
| `TestSlot_我的Shader` | 右：`Assets/LookDev/MyAssets/Mat_MyShader.mat`，把 Shader 换成你自己的 |
| `== 参考球 ==` | 铬球（看反射）/ 18% 灰（看曝光）/ 80% 白 |
| `== 粗糙度阶梯 ==` | 上排金属、下排非金属，粗糙度 0→1，拿来和你的 BRDF 对比高光形状 |
| `== ColorChecker 色卡 ==` | X-Rite 24 色，检查色彩 / 色调映射 |

后处理按虚幻默认风格配置：ACES 色调映射、Bloom（阈值 1）、轻微暗角、SSAO、4096 软阴影、MSAA 4x 加 SMAA、HDR 64 位缓冲。
HDRI 来自 Poly Haven（CC0 协议）：studio_small_09、kloofendal_48d_partly_cloudy_puresky、hotel_room，都是 4K。

## 检验流程
1. 把你的 shader 和贴图拷进 `Assets/LookDev/MyAssets/`（重建场景不会动这个文件夹）。
2. 贴图导入设置：基础色勾 sRGB；MRO 关 sRGB；法线的 Texture Type 选 Normal map。
3. 选中 `Mat_MyShader`，把 Shader 换成你的。
4. 选中 `== 对比区 ==`，把贴图拖进去；如果你 shader 的属性名不是 `_BaseColor / _Normal / _MRO / _Height`，在下面改。
5. 换你自己的模型：拖到两个 Slot 下面，把默认球体关掉。

MRO 通道约定：R = 金属度，G = 粗糙度，B = AO。

## 操作
- 编辑模式：直接改总控 Inspector。换 HDRI 或旋转天空后，停手约 0.3 秒会自动重烘一次反射。
- 运行模式（Play）：左键拖动 = 绕物体转；中键 = 平移；滚轮 = 推拉；Shift + 左键 = 转 HDRI；F = 回到初始机位；1/2/3 = 切 HDRI；B = 背景开关；L = 太阳开关；T = 转台。
- 菜单 `LookDev/重建场景`：重新生成整个场景，`Generated/` 和场景文件会被覆盖。

## 你的 shader 要接入阴影 / SSAO
只写一个 ForwardLit pass 的话，物体不投影，SSAO 也拿不到它的法线。在 SubShader 末尾加上：
```
UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
```
另外要接收环境光，就在片元里加 `SampleSH(normalWS)`（漫反射）和 `GlossyEnvironmentReflection(...)`（镜面反射），否则只有太阳光。

## 已知差异
URP 的 BRDF 是简化版 GGX，和虚幻的 Smith-joint 高光形状不完全一样。另外 URP 没有自动曝光，曝光要在总控里手动调。

## 在 VS Code 里跳转到 URP 源码（F12）
1. 装插件 HLSL Tools（TimGJones.hlsltools）。
2. 在工程根目录建 include 链接（Unity 的 include 写的是 `Packages/包名/...`，实际文件在 `Library/PackageCache/包名@版本`）：
   ```powershell
   New-Item -ItemType Junction -Path .shaderinclude\Packages\com.unity.render-pipelines.core -Target Library\PackageCache\com.unity.render-pipelines.core@14.0.12
   New-Item -ItemType Junction -Path .shaderinclude\Packages\com.unity.render-pipelines.universal -Target Library\PackageCache\com.unity.render-pipelines.universal@14.0.12
   ```
   升级 URP 后版本号会变，要删掉重建。
3. 用 VS Code 打开工程根目录。`shadertoolsconfig.json` 已配好 include 路径和 `SHADER_API_D3D11`。
4. 在函数、结构体、宏上按 F12 跳到定义，Alt+F12 原地预览。shader 里有编译错误时，错误后面的代码可能跳不了，先修错误。
