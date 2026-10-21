#ifndef LOOKDEV_PROBE_INCLUDED
#define LOOKDEV_PROBE_INCLUDED

// 调试探针：在片元着色器任意位置写一行，就把那个变量当颜色输出，不用改下拉菜单。
//
// 接入（三行）：
//   Properties:  [Toggle(_LD_PROBE_ON)] _LDProbe("调试探针", Float) = 0
//   Pass:        #pragma shader_feature_local_fragment _ _LD_PROBE_ON
//                #include "Assets/LookDev/Shaders/LookDevProbe.hlsl"   （放在 Lighting.hlsl 后面）
//
// 用法（材质上勾选"调试探针"才生效，不勾就是空的，不影响正常结果）：
//   LD_PROBE(x);         数值、颜色：float 显示成灰度，float2 显示成红绿，float3/float4 直接显示
//   LD_PROBE_DIR(x);     方向向量：从 -1~1 挪到 0~1 再显示
//   LD_PROBE_SIGNED(x);  看正负：正数显示绿色，负数显示红色（只看 x 的第一个分量）
//
// 宏参数不要起名叫 x：宏里的 .x 分量也会被一起替换掉。
// 注意：探针会直接 return，同一个函数里只有第一个被执行到的探针起作用。
// 调试时记得关掉 Look-Dev 总控里的 ACES，否则颜色会被色调映射改掉。

float3 LDProbeColor(float v)  { return v.xxx; }
float3 LDProbeColor(float2 v) { return float3(v, 0); }
float3 LDProbeColor(float3 v) { return v; }
float3 LDProbeColor(float4 v) { return v.rgb; }

float3 LDProbeSigned(float v) { return float3(max(-v, 0), max(v, 0), 0); }

#if defined(_LD_PROBE_ON)
    #define LD_PROBE(value)        return float4(LDProbeColor(value), 1)
    #define LD_PROBE_DIR(value)    return float4(LDProbeColor(value) * 0.5 + 0.5, 1)
    #define LD_PROBE_SIGNED(value) return float4(LDProbeSigned((value).x), 1)
#else
    #define LD_PROBE(value)
    #define LD_PROBE_DIR(value)
    #define LD_PROBE_SIGNED(value)
#endif

#endif
