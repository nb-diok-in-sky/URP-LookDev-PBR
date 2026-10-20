Shader "Unlit/URP_Shader"
{
    Properties
    {
        
        _MRO("MRO贴图",2D) = "white"{}
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
                float4 tangentOS:TANGENT;
                float3 normalOS:NORMAL;



            };

            struct Varyings
            {    

                   
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS :TEXCOORD2;
                float3 tangentWS :TEXCOORD3;
                float3 bitangentWS : TEXCOORD4;



                  
            };

                      // 设定走太阳散射需要的步长
           
            CBUFFER_START(UnityPerMaterial)
            
            
            CBUFFER_END

             
            TEXTURE2D(_MRO); 
            SAMPLER(sampler_MRO); 
            TEXTURE2D(_Normal);
            SAMPLER(sampler_Normal);  
            TEXTURE2D (_BaseColor);
            SAMPLER(sampler_BaseColor); 
            TEXTURE2D(_Height); 
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
               float T_D_GGX(float Roughness,float NoH){
               float a2 = Roughness * Roughness; 
               float NoH2 = NoH * NoH;
               return a2/ PI * pow(NoH2*(a2-1)+1,2);

               }  

               //Schlick-GGX 几何遮蔽函数 
               float S_GGX(float k,float NoV){//这里的k不分 ibl 和 直射光的粗糙度，直接传入就行。
                return NoV / (NoV * (1-k) +k);

               }
                 
               //史密斯的方法  几何遮蔽函数
               float Smith_Method(float NoV ,float3 viewDir,float3 lightDirection,float k ){  
               float ggx1 = S_GGX(k,NoV);
               float ggx2 = S_GGX(saturate(dot(lightDirection,viewDir)),k);
                return ggx1 *ggx2;
               }
 
               //菲涅尔效应
               float fresnelSchlick(float3 F0,float3 lightDirection,float Normal){  
               float cosTheta = dot(lightDirection,Normal);
               return F0 +(1.0-F0) * pow(1-cosTheta,5.0);

               }
 

               float DirectBRDF(float3 Normal,Light light,float Roughness,float NoH, float3 viewDir, float k ,float NoV,float F0,float3 diffuseColor){
               float NoL = saturate(dot(Normal,light.direction));
               float D = T_D_GGX(Roughness,NoH); 
               float G = Smith_Method(NoV,viewDir,light.direction,k); 
               float F = fresnelSchlick(F0,light.direction,Normal);
               //直射光BRDF 
               float Specular  = D*F*G/(4*saturate(dot(Normal,light.direction))*NoV); 

               float3 Diffuse = diffuseColor/PI;
               float3 radiance = light.color * light.shadowAttenuation *light.distanceAttenuation;

               return (Diffuse + D*G*F/(4*dot(Normal,light.direction)* NoV + 0.0001) )*radiance * Normal;//加个0.00001 防止偶尔可能出现的除数为0的bug

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

float3 EnvBRDFApprox( float3 SpecularColor, float Roughness,float NoV )
{
	float2 AB = EnvBRDFApproxLazarov(Roughness, NoV);

	// Anything less than 2% is physically impossible and is instead considered to be shadowing
	// Note: this is needed for the 'specular' show flag to work, since it uses a SpecularColor of 0
	float F90 = saturate( 50.0 * SpecularColor.g );

	return SpecularColor * AB.x + F90 * AB.y;
}




            float4 Frag (Varyings input) : SV_Target
            {       

               float3 MRO = SAMPLE_TEXTURE2D(_MRO,sampler_MRO,input.uv);//MRO贴图
               float3 BaseColor = SAMPLE_TEXTURE2D(_BaseColor,sampler_BaseColor,input.uv);//基础色贴图
               float3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_Normal,sampler_Normal,input.uv ) );//法线贴图
               float3x3 TBN = float3x3(input.tangentWS,input.bitangentWS,input.normalWS );//TBN
               float normal = normalize(TransformTangentToWorld(normalTS,TBN));//这个法线才能拿来用

  
               float3 CameraPosWS = GetCameraPositionWS();//摄像机位置。
               float3 viewDir = CameraPosWS - input.positionCS;//视觉方向。
          
               //下面要整合一大堆接下来需要用到的数值
               float3 F0 =lerp(0.04,BaseColor,MRO.r); //基础反射率 这个是拿去给菲涅尔效应用的
               float k = pow(MRO.g+1,2)/8;//这里用的是直接光照的重映射
               float4 shadowCoord = TransformWorldToShadowCoord(input.positionCS); 
               Light mainLight = GetMainLight(shadowCoord);//获得光照mainLight
               float3 h_main = (mainLight.direction + viewDir)/abs(mainLight.direction + viewDir);  //h半程向量
 
               float NoV = saturate(dot(normal,viewDir));
               float NoH_main = saturate(dot(normal,h_main));

               float3 diffuseColor = BaseColor *( 1- MRO.r);//这是拿来用的漫反射


 
               //平行光BRDF
               float3 MainLight_BRDF = DirectBRDF(normal ,mainLight,MRO.g,NoH_main,viewDir,k,NoV,F0,diffuseColor);
               float3 additionalLight_BRDF = 0; 
               //下面来搞其他的直射光BRDF  
               int count = GetAdditionalLightsCount();
               for(int i = 0; i<count;i++){
               Light light = GetAdditionalLight(i,input.positionWS,half4(1,1,1,1));
               float3 h_addition = (light.direction+viewDir)/abs(light.direction+viewDir);  //非平行光的半程向量
               float NoH_addition = saturate(dot(normal,h_addition));
               additionalLight_BRDF += DirectBRDF(normal,light,MRO.g,NoH_addition,viewDir,k,NoV,F0,diffuseColor );
               }
 
             float3  DirectColor = additionalLight_BRDF + MainLight_BRDF;
              //现在的Lambert就是集合完毕所有直射光照的东西了

               //接下来处理间接光 
               float3 indirectDiffuse = SampleSH(normal) * diffuseColor;
               float3 R =reflect(-viewDir,normal);
               float3 envColor = GlossyEnvironmentReflection(R,input.positionWS,MRO.g,MRO.b);

               float3 indirectSpecular = envColor * EnvBRDFApprox(DirectColor ,MRO.g,NoV);
               float3 inDirectColor = (indirectDiffuse + indirectSpecular) ;
                               
               return float4(DirectColor + inDirectColor ,1);

            }  
 
                   
 
            ENDHLSL
        }


    }
}
