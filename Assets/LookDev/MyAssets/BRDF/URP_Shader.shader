Shader "Unlit/URP_Shader"
{
    Properties
    {
        
        _ORM("ORM贴图",2D) = "white"{}
        _Normal("法线贴图",2D) = "white"{}
        _BaseColor("基础色贴图",2D) = "white"{}
        _Height("高度贴图",2D) = "white"{}

    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"




            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 tangentOS:TANGENT;
                float3 normalOS:NORMAL;



            };

            struct Varyings
            {   

                   
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 positionWS : TEXCOORD1;
                float3 normlaWS :TEXCOORD2;
                float3 tangentWS :TEXCOORD3;
                float3 bitangentWS : TEXCOORD4;




            };

                      // 设定走太阳散射需要的步长
          
            CBUFFER_START(UnityPerMaterial)
            
            static const float PI = 3.14159265;
            
            CBUFFER_END


            TEXTURE2D _ORM;
            SAMPLER(sampler_ORM); 
            TEXTURE2D _Normal;
            SAMPLER(sampler_Normal); 
            TEXTURE2D _BaseColor;
            SAMPLER(sampler_BaseColor); 
            TEXTURE2D _Height;
            SAMPLER(sampler_Height); 



            Varyings Vert (Attributes input)
            {

                Varyings output;
                //下面是切线空间
                VertexNormalInputs norm = GetVertexNormalInputs(input.normalOS,input.tangentOS);
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
               float3 D_GGX(float Roughness,float NoH){
               float a2 = Roughness * Roughness;
               float3 NoH2 = NoH * NoH;
               return a/ PI * pow(NoH2*(a-1)+1,2);

               }

               //Schlick-GGX 几何遮蔽函数
               float S_GGX(float k,float NoV){//这里的k不分 ibl 和 直射光的粗糙度，直接传入就行。
                return NoV / (NoV * (1-k) +k);

               }
               
               //史密斯的方法  几何遮蔽函数
               float Smith_Method(float NoV ,float3 viewDir,float3 lightDirection,float k ){  
               float ggx1 = S_GGX(k,NoV);
               float ggx2 = S_GGX(lightDirection,viewDir,k);
                
               }

               //菲涅尔效应
               float fresnelSchlick(float3 F0,float3 lightDirection,float Normal){  
               float cosTheta = dot(lightDirection,Normal);
               return F0 +(1.0-F0) * pow(1-cosTheta,5.0);

               }


               float DirectBRDF(float3 Normal,Light light,float Roughness,float NoH, float k ,float NoV,float F0,float3 diffuseColor){
               float NoL = saturate(dot(Normal,light.direction));
               float D = D_GGX(Roughness, NoH);
               float G = Smith_Method(k,NoV);
               float F = fresnelSchlick(F0,light.direction,Normal);
               //直射光BRDF
               float Specular  = D*F*G/(4*ot(N,light.direction)*dot(N,viewDir));

               float3 Diffuse = diffuseColor/PI;
               float3 radiance = light.color * light,shadowAttenuation *light.distanceAttenuation;

               return (Diffuse + D*G*F/(4*dot(Normal,light.direction)* NoV + 0.0001)*radiance * Normal;//加个0.00001 防止偶尔可能出现的除数为0的bug

               }



            float4 Frag (Varyings input) : SV_Target
            {

               float3 ORM = SAMPLE_TEXTURE2D(_ORM,sampler_ORM,input.uv);//ORM
               float3 BaseColor = SAMPLE_TEXTURE2D(_BaseColor,sampler_BaseColor,input.uv);//基础色贴图
               float3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_Normal,sampler_Normal,input.uv ) );//法线贴图
               float3x3 TBN = float3x3(input.tangentWS,input.bitangentWS,input.normalWS );//TBN
               float normal = normalize(TransfomTangentToWorld(normalTS.TBN));//这个法线才能拿来用


               float3 CameraPosWS = GetCameraPosWS();//摄像机位置。
               float3 viewDir = CameraPosWS - input.positionCS;//视觉方向。
        
               //下面要整合一大堆点乘和接下来需要用到的数值
               float NoV = saturate(dot(normal,viewDir));
               float NoH = saturate(dot(noraml,h)
               
               float3 F0 =lerp(0.04,BaseColor,ORM.b); //基础反射率 这个是拿去给菲涅尔效应用的

               float k = pow(Roughness+1,2)/8;//这里用的是直接光照的重映射
             
               float3 h = (mainLight.direction + viewDir)/abs(mainLight.direction + viewDir);  //h半程向量

               float3 diffuseColor = BaseColor *( 1- ORM.b);//这是拿来用的漫反射

               float3 shadowCoord = TransfomWorldToShadowCoord(input.positionCS);
               Light mainLight = GetMainLight(shadowCoord);//获得光照mainLight


               //平行光BRDF
               float3 MainLight_BRDF = DirectBRDF(normal ,mainLight,Roughness,NoH,k,NoV,F0,diffuseColor);
               float3 additionalLight_BRDF = 0;
               //下面来搞其他的直射光BRDF
               int count = GetAdditionalLightsCount();
               for(int i = 0; i<count;i++){
               Light light = GetAdditionalLight(i,input.positionWS,half(1,1,1,1));
                
               additionalLight_BRDF += DirectBRDF(normal,light,Roughness,NoH,l.NoV,F0,diffuseColor );
               }

             float3  DirectColor = additionalLight_BRDF + MainLight_BRDF;
              //现在的Lambert就是集合完毕所有直射光照的东西了

               //接下来处理间接光
               float3 indirectDiffuse = SampleSH(N) * diffuseColor;
               float3 R =reflect(-viewDir,N);
               float3 envColor = GlossyEnvironmentReflection(R,input.positionWS,Roughness,ORM.r);
               float3 indirectSpecular = envColor * EnvBRDFApprox(F0,Roughness,NoV);
               float3 inDirectColor = (indirectDiffuse + indirectSpecular) ;
                               
               return DirectColor + inDirectColor;

            }

                

            ENDHLSL
        }


    }
}
