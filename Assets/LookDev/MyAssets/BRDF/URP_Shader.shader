Shader "Unlit/URP_Shader"
{
    Properties
    {
        _MRO("MRO贴图", 2D) = "white" {}
        _Normal("法线贴图", 2D) = "bump" {}
        _BaseColor("基础色贴图", 2D) = "white" {}
        _Height("高度贴图", 2D) = "white" {}

        //下面添加一个debug菜单
        [KeywordEnum(None, Normal, ViewDir, NoL, Metallic, Roughness, AO, DiffuseColor,mainLightBRDF,additionalLightBRDF,DirectColor, IndirectColor)]
        _DebugView("调试显示", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 100

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ///
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            ///上面这全是阴影的内容 必须要加
                                                                            
            //下面来声明Debug关键字
            #pragma shader_feature_local_fragment _DEBUGVIEW_NONE _DEBUGVIEW_NORMAL _DEBUGVIEW_VIEWDIR _DEBUGVIEW_NOL _DEBUGVIEW_METALLIC _DEBUGVIEW_ROUGHNESS _DEBUGVIEW_AO _DEBUGVIEW_DIFFUSECOLOR _DEBUGVIEW_MAINLIGHTBRDF _DEBUGVIEW_ADDITIONALLIGHTBRDF _DEBUGVIEW_DIRECTCOLOR _DEBUGVIEW_INDIRECTCOLOR

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"                        
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 tangentOS  : TANGENT;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
                float3 tangentWS   : TEXCOORD3;
                float3 bitangentWS : TEXCOORD4;
            };

            // 设定走太阳散射需要的步长
            CBUFFER_START(UnityPerMaterial)
            CBUFFER_END

            TEXTURE2D(_MRO);
            SAMPLER(sampler_MRO);
            TEXTURE2D(_Normal);
            SAMPLER(sampler_Normal);
            TEXTURE2D(_BaseColor);
            SAMPLER(sampler_BaseColor);
            TEXTURE2D(_Height);
            SAMPLER(sampler_Height);

            Varyings Vert(Attributes input)
            {
                Varyings output;
                //下面是切线空间
                VertexNormalInputs norm = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS);//直接拿切线空间和模型坐标

                output.tangentWS = norm.tangentWS;
                output.normalWS = norm.normalWS;
                output.bitangentWS = norm.bitangentWS;
                output.positionWS = pos.positionWS;
                output.positionCS = pos.positionCS;
                output.uv = input.uv;
                return output;
            }

            //Trowbridge-Reitz GGX法线分布函数
            float MyDistributionGGX(float roughness, float NoH)
            {    float a = roughness *roughness;
                float a2 = a * a;
                float NoH2 = NoH * NoH;
                return a2 /( PI * pow(NoH2 * (a2-1) + 1, 2)); 
            }

            //Schlick-GGX 几何遮蔽函数
            float MyGeometrySchlickGGX(float k, float NoV)//这里的k不分 ibl 和 直射光的粗糙度，直接传入就行。
            {
                return NoV / (NoV * (1 - k) + k);
            }

            //史密斯的方法  几何遮蔽函数
            float MyGeometrySmith(float NoV, float NoL, float k)
            {
                float ggx1 = MyGeometrySchlickGGX(k, NoV);
                float ggx2 = MyGeometrySchlickGGX(k,saturate(NoL));
                return ggx1 * ggx2;
            }

            //菲涅尔效应    
            float3 MyFresnelSchlick(float3 F0, float VoH)
            {
                float cosTheta = VoH;
                return F0 + (1.0 - F0) * pow(1 - cosTheta, 5.0);
            }

            //为了测试的时候方便传入传出数值 直接用结构体打包需要的数据，配合方法直接算
            struct MyLightContext//这里面要装很多计算用的数据，把要提前计算的东西封装好
            {
                float NoL;
                float NoV; 
                float NoH;
                float VoH;
                float VoL;
                float3 radiance;
            };

            struct MyBRDFContext
            {
                float D;
                float G;
                float3 F;
                float3 specularColor;
                float3 diffuseColor;
            };

            void MyInitLightContext(inout MyLightContext lightContext, float3 normal, float3 viewDir, Light light)
            {
                float3 h = (light.direction + viewDir) / length(light.direction + viewDir);
                lightContext.NoL = dot(normal, light.direction);
                lightContext.NoV = saturate(abs(dot(normal,viewDir)));  //这个就不省点乘了 直接用就是
                lightContext.NoH = saturate(dot(normal, h));
                lightContext.VoH = saturate(dot(viewDir, h));
                lightContext.VoL = dot(viewDir, light.direction);
                lightContext.radiance = light.color * light.shadowAttenuation * light.distanceAttenuation;
            }

            void MyInitBRDFContext(inout MyBRDFContext brdfContext, MyLightContext lightContext, float roughness, float k, float3 F0, float3 diffuseColor)
            {
                brdfContext.D = MyDistributionGGX(roughness, lightContext.NoH);
                brdfContext.G = MyGeometrySmith(lightContext.NoV, lightContext.NoL, k);
                brdfContext.F = MyFresnelSchlick(F0, lightContext.VoH);
                brdfContext.specularColor = brdfContext.D * brdfContext.F * brdfContext.G / max(4*saturate(lightContext.NoL)*lightContext.NoV,0.0001);
                brdfContext.diffuseColor = diffuseColor / PI;
            }

            float3 MyDirectBRDF(MyLightContext lightContext,inout MyBRDFContext brdfContext)//用这个函数之前需要先packeg两个结构体
            {  
                return (brdfContext.specularColor +brdfContext.diffuseColor) * lightContext.radiance *saturate(lightContext.NoL);
            }

            //下面是间接光的公式

            float2 EnvBRDFApproxLazarov(float Roughness, float NoV)
            {
                // [ Lazarov 2013, "Getting More Physical in Call of Duty: Black Ops II" ]
                // Adaptation to fit our G term.
                const float4 c0 = { -1, -0.0275, -0.572, 0.022 };
                const float4 c1 = { 1, 0.0425, 1.04, -0.04 };
                float4 r = Roughness * c0 + c1;
                float a004 = min(r.x * r.x, exp2(-9.28 * NoV)) * r.x + r.y;
                float2 AB = float2(-1.04, 1.04) * a004 + r.zw;
                return AB;
            }

            float3 EnvBRDFApprox(float3 F0, float Roughness, float NoV)
            {
                float2 AB = EnvBRDFApproxLazarov(Roughness, NoV);

                // Anything less than 2% is physically impossible and is instead considered to be shadowing
                // Note: this is needed for the 'specular' show flag to work, since it uses a SpecularColor of 0
                float F90 = saturate(50.0 * F0.g);

                return F0 * AB.x + F90 * AB.y;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 mro = SAMPLE_TEXTURE2D(_MRO, sampler_MRO, input.uv);//MRO贴图
                float3 baseColor = SAMPLE_TEXTURE2D(_BaseColor, sampler_BaseColor, input.uv);//基础色贴图
                float3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_Normal, sampler_Normal, input.uv));//法线贴图
                float3x3 TBN = float3x3(input.tangentWS, input.bitangentWS, input.normalWS);//TBN
                float3 normal = normalize(TransformTangentToWorld(normalTS, TBN));//这个法线才能拿来用

                float3 cameraPosWS = GetCameraPositionWS();//摄像机位置。
                float3 viewDir = normalize(cameraPosWS - input.positionWS);//视觉方向。

                //下面要整合一大堆接下来需要用到的数值
                float3 F0 = lerp(0.04, baseColor, mro.r); //基础反射率 这个是拿去给菲涅尔效应用的
                float k = pow(mro.g + 1, 2) / 8;//这里用的是直接光照的重映射
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);//获得光照mainLight
                float3 diffuseColor = baseColor * (1 - mro.r);//这是拿来用的漫反射

                //下面来打包几个数据结构体
                //为了测试的时候方便传入传出数值 直接用结构体打包需要的数据，配合方法直接算
                MyLightContext mainLightContext;//主光源的数据
                MyBRDFContext brdfContext;

                MyInitLightContext(mainLightContext, normal, viewDir, mainLight);
                MyInitBRDFContext(brdfContext, mainLightContext, mro.g, k, F0, diffuseColor);

                float3 mainLightBRDF = MyDirectBRDF(mainLightContext, brdfContext);//这都是平行光的brdf结果
                float3 additionalLightBRDF = 0;

                //下面来搞其他的直射光BRDF
                int count = GetAdditionalLightsCount();
                for (int i = 0; i < count; i++)
                {
                    Light light = GetAdditionalLight(i, input.positionWS, half4(1, 1, 1, 1));
                    MyLightContext additionalLightContext;
                    MyInitLightContext(additionalLightContext, normal, viewDir, light);
                    additionalLightBRDF += MyDirectBRDF(additionalLightContext, brdfContext);
                }

                float3 directColor = additionalLightBRDF + mainLightBRDF;
                //现在的Lambert就是集合完毕所有直射光照的东西了

                //接下来处理间接光
                float3 indirectDiffuse = SampleSH(normal) * diffuseColor *mro.b ;
                float3 R = reflect(-viewDir, normal);
                float3 envColor = GlossyEnvironmentReflection(R, input.positionWS, mro.g, mro.b);

                float3 indirectSpecular = envColor * EnvBRDFApprox(F0, mro.g, mainLightContext.NoV);
                float3 indirectColor = (indirectDiffuse + indirectSpecular) ;

                //下面是debug return
                #if defined(_DEBUGVIEW_NORMAL)
                    return float4(normal * 0.5 + 0.5, 1);  // 方向向量从 -1~1 挪到 0~1，才看得出颜色
                #elif defined(_DEBUGVIEW_NOL)
                    return float4(mainLightContext.NoL.xxx, 1);  // 一个数复制到 RGB 三个通道，显示成灰度
                #elif defined(_DEBUGVIEW_VIEWDIR)
                    return float4(viewDir * 0.5 + 0.5, 1);
                #elif defined(_DEBUGVIEW_METALLIC)
                    return float4(mro.xxx, 1);
                #elif defined(_DEBUGVIEW_ROUGHNESS)
                    return float4(mro.yyy, 1);
                #elif defined(_DEBUGVIEW_AO)
                    return float4(mro.zzz, 1);
                #elif defined(_DEBUGVIEW_DIFFUSECOLOR)
                    return float4(diffuseColor.xyz, 1);
                #elif defined(_DEBUGVIEW_MAINLIGHTBRDF)
                    return float4(mainLightBRDF,1);
                #elif defined(_DEBUGVIEW_ADDITIONALLIGHTBRDF)
                    return float4(additionalLightBRDF,1); 
                #elif defined(_DEBUGVIEW_DIRECTCOLOR) 
                    return float4(directColor.xyz, 1);
                #elif defined(_DEBUGVIEW_INDIRECTCOLOR)
                    return float4(indirectColor.xyz, 1);
                #endif
                return float4(directColor + indirectColor, 1);
            }

            ENDHLSL
        }
    }
}
