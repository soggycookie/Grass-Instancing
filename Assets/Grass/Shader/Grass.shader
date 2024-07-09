Shader "Unlit/Grass"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _TipColor("Tip Color", Color )= (1,1,1,1)
        _RootColor("Root Color", Color )= (1,1,1,1)
        _Droop("Droop", Range(0.0,10.0) ) = 1.0
        [NoScaleOffset] _HeightMap("Height Map", 2D) = "gray"{}
        _HeightStrength("Height Strength", Range(0, 10)) = 1 
        _WindSpeed("Wind Speed", Range(0, 5) ) = 1
        _WindAmplitude("Wind Amplitude", Range(0, 1) ) = 1
        _FogColor ("Fog Color", Color) = (1, 1, 1)
        _FogDensity ("Fog Density", Range(0.0, 1.0)) = 0.0
        _FogOffset ("Fog Offset", Range(0.0, 10.0)) = 0.0
    }
    SubShader
    {
        Tags {             
            "RenderType" = "Transparent"
            "Queue" = "Transparent" }
        LOD 100

        Cull Off
        Zwrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            //#pragma multi_compile_fog
            #pragma target 4.5
            #pragma shader_feature IS_FOG

            #include "Random.cginc"
            #include "UnityCG.cginc"

            struct GrassData{
                float3 position;
                float2 worldUV;
            };


            #if SHADER_TARGET >= 45
                StructuredBuffer<GrassData> grassDataBuffer;
            #endif 

            
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _TipColor, _RootColor, _FogColor;
            float _Droop, _HeightStrength, _WindSpeed, _WindAmplitude, _FogDensity, _FogOffset;
            sampler2D _HeightMap;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };


            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float2 wUV: TEXCOORD1;
                float3 worldPos : TEXCOORD2;
            };


            float4 RotateAroundYInDegrees (float4 vertex, float degrees) {
                float alpha = degrees * UNITY_PI / 180.0;
                float sina, cosa;
                sincos(alpha, sina, cosa);
                float2x2 m = float2x2(cosa, -sina, sina, cosa);
                return float4(mul(m, vertex.xz), vertex.yw).xzyw;
            }

            v2f vert (appdata v, uint id : SV_INSTANCEID)
            {
                v2f o;

                #if SHADER_TARGET >= 45
                    float3 data = grassDataBuffer[id].position ;
                    float2 worldUV = grassDataBuffer[id].worldUV;
                #else
                    float3 data = 0;
                    float2 worldUV = 0;
                #endif


                float idHash = randValue(abs(data.x * 10000 + data.y * 100 + data.z * 0.05f + 2));
                idHash = randValue(idHash * 450);

                float4 animationDirection = float4(0.0f, 0.0f, 1.0f, 0.0f);
                animationDirection = normalize(RotateAroundYInDegrees(animationDirection,  idHash * 360.0f));
                
                v.vertex = RotateAroundYInDegrees(v.vertex, idHash * 180);
                v.vertex.xz += lerp(0.5f, 1, idHash) * _Droop * lerp(0.5f, 1.0f, idHash) * (v.uv.y * v.uv.y) * animationDirection.xz;
                v.vertex.y +=  v.uv.y *(1 - tex2Dlod(_HeightMap, float4(worldUV,0,0)) + lerp(0, 0.2f, idHash))* _HeightStrength ;

                //wind animation
                //float displacement = sin((worldUV.x + worldUV.y + _Time.y * _WindSpeed) * 8) * _WindAmplitude ;
                //v.vertex.y -= v.uv.y * v.uv.y * displacement * 0.3f;
                //displacement+= sin((worldUV.x + worldUV.y + _Time.y * lerp(1, 3, idHash ) * _WindSpeed)* 10) * lerp(0.1f, 0.5f, idHash) * _WindAmplitude;
                //v.vertex.xz += (v.uv.y * v.uv.y ) * displacement / 2;

                float4 worldPos = float4(data+ v.vertex.xyz, 1);
                
                o.vertex = mul(UNITY_MATRIX_VP, worldPos);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.wUV = worldUV;
                o.worldPos = worldPos;

                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                // sample the texture
                float4 col = tex2D(_MainTex, i.uv);
                float4 color = lerp(_RootColor,_TipColor, i.uv.y);

                //noob occlusion
                color *= pow( i.uv.y, 0.9f);
                
                /* Fog */
                #if IS_FOG
                    float viewDistance = length(_WorldSpaceCameraPos - i.worldPos);
                    float fogFactor = (_FogDensity / sqrt(log(2))) * (max(0.0f, viewDistance - _FogOffset));
                    fogFactor = exp2(-fogFactor * fogFactor);
                    color = lerp(_FogColor, col * color, fogFactor);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
