// LookDev 参考 shader：贴图接口和你手写的一样（BaseColor / Normal / MRO），
// 光照完全交给 URP 自带的 UniversalFragmentPBR，用来当"标准答案"和你的 BRDF 对比。
// MRO 通道约定：R = Metallic，G = Roughness，B = AO（MRO 贴图导入时要关掉 sRGB）。
// 注意：URP 的 BRDF 是简化版 GGX，和虚幻的 Smith-joint 高光形状会有细微差别。
Shader "LookDev/Reference PBR (URP, MRO)"
{
    Properties
    {
        _BaseMap ("基础色贴图 (sRGB)", 2D) = "white" {}
        _BaseTint ("基础色乘色", Color) = (1,1,1,1)
        [Normal] _NormalMap ("法线贴图", 2D) = "bump" {}
        _NormalStrength ("法线强度", Range(0, 2)) = 1
        _MROMap ("MRO 贴图 (R金属 G粗糙 B AO, 线性)", 2D) = "white" {}
        _Metallic ("金属度倍数", Range(0, 1)) = 1
        _Roughness ("粗糙度倍数", Range(0, 1)) = 1
        _AOStrength ("AO 强度", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 tangentWS  : TEXCOORD3; // w = 副切线符号
                float  fogCoord   : TEXCOORD4;
            };

            TEXTURE2D(_BaseMap);   SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D(_MROMap);    SAMPLER(sampler_MROMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseTint;
                half _NormalStrength;
                half _Metallic;
                half _Roughness;
                half _AOStrength;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = nrm.normalWS;
                output.tangentWS = float4(nrm.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogCoord = ComputeFogFactor(pos.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseTint;
                half3 mro = SAMPLE_TEXTURE2D(_MROMap, sampler_MROMap, input.uv).rgb;
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv), _NormalStrength);

                float3 normalWS = normalize(input.normalWS);
                float3 tangentWS = normalize(input.tangentWS.xyz);
                float3 bitangentWS = input.tangentWS.w * cross(normalWS, tangentWS);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = baseColor.rgb;
                surface.alpha = 1;
                surface.metallic = mro.r * _Metallic;
                surface.smoothness = 1 - saturate(mro.g * _Roughness);
                surface.occlusion = lerp(1, mro.b, _AOStrength);
                surface.normalTS = normalTS;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, half3x3(tangentWS, bitangentWS, normalWS)));
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogCoord;
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
            ENDHLSL
        }

        // 阴影、深度、法线（SSAO 需要）直接借用 URP Lit 的 pass
        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
        UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
