Shader "Unlit/Grass"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _TipColor("Tip Color", Color )= (1,1,1,1)
        _HighGrassTipColor("High Grass Tip Color", Color) = (1,1,1,1)
        _RootColor("Root Color", Color )= (1,1,1,1)
        _Droop("Droop", Range(0.0,2.0) ) = 1.0
        [NoScaleOffset] _WindNoise("Wind Noise", 2D) = "white"{}
        _WindSpeed("Wind Speed", Range(0, 5) ) = 1
        _WindAmplitude("Wind Amplitude", Range(0, 1) ) = 1
        _AmbientOcclusion("Ambient Occlusion", Range(0, 1) ) = 0
        _ScaleYAxis("Scale Height", Range(0.5, 5) ) = 1
        _ScaleXAxis("Scale Width", Range(0.5, 5) ) = 1
        _ScaleXBaseOnY("Scale X Base on height map", Vector) = (0,0,0,0)
        [NoScaleOffset] _GrassHeightMap("Grass Height Texture", 2D) = "gray"{}
        _HeightStrength("Height Strength", Range(0, 10)) = 1 

        _FogColor ("Fog Color", Color) = (1, 1, 1)
        _FogOffset ("Fog Offset", Range(0.0, 10.0)) = 0.0
        _FogDensity ("Fog Density", Range(0.0, 1.0)) = 0.0
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

            
            sampler2D _MainTex, _WindNoise, _GrassHeightMap;
            float4 _MainTex_ST, _GrassHeightMap_ST;
            float4 _TipColor, _RootColor, _FogColor, _HighGrassTipColor;
            float _Droop, _HeightStrength, _WindSpeed, _WindAmplitude, _FogDensity, _FogOffset;
            float _Scale;
            uint _NumInstanceDimension;
            float _ChunkSize;
            float _AmbientOcclusion , _ScaleYAxis, _ScaleXAxis;
            float4 _ScaleXBaseOnY;

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
                float4 worldPos : TEXCOORD2;
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
                idHash = min(1, max(0, idHash));
                
                //scale
                v.vertex.y *=  _ScaleYAxis;
                v.vertex.xz *= _ScaleXAxis / 2;

                //create cluster of grass
                float heightValue = tex2Dlod(_GrassHeightMap, float4(worldUV,0,0)).r ;

                v.vertex = RotateAroundYInDegrees(v.vertex, idHash * 360.0f);
                v.vertex.y +=  v.uv.y * _HeightStrength * heightValue * lerp(1, 1.1f, idHash);
                v.vertex.xz *= _ScaleXBaseOnY.x == 0 || _ScaleXBaseOnY.y == 0 ? 1 : pow( _ScaleXBaseOnY.y / _ScaleXBaseOnY.x, heightValue)  ;
                v.vertex.xz += _ScaleYAxis * _HeightStrength * heightValue * _Droop * lerp(0.3f, 1.0f, idHash) * (v.uv.y * v.uv.y) * animationDirection.xz;

                //sway effect
                v.vertex.xz += sin((_Time.y * max(heightValue,idHash) * 1.5f )) * v.uv.y * v.uv.y * 0.5f * animationDirection.xz;
                v.vertex.y -= sin(_Time.y * 0.1f ) * v.uv.y * v.uv.y;

                //wind animation
                //float displacement = sin((worldUV.x + worldUV.y + _Time.y * _WindSpeed) * 8) * _WindAmplitude ;
                //v.vertex.y -= v.uv.y * v.uv.y * displacement * 0.3f;
                //displacement+= sin((worldUV.x + worldUV.y + _Time.y * lerp(1, 3, idHash ) * _WindSpeed)* 10) * lerp(0.1f, 0.5f, idHash) * _WindAmplitude;
                //v.vertex.xz += (v.uv.y * v.uv.y ) * displacement / 2;
                

                float4 worldPos = float4(data+ v.vertex.xyz, 1);
                
                o.vertex = mul(UNITY_MATRIX_VP, worldPos);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.wUV = worldUV;
                o.worldPos.xyz = worldPos;
                o.worldPos.w = heightValue ;

                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                // sample the texture
                float4 col = tex2D(_MainTex, i.uv);

                float4 color = lerp(_RootColor,_TipColor, i.uv.y);
                color = i.worldPos.w > 0.5f ? lerp(color, _HighGrassTipColor, pow(i.uv.y,5) ) : color; 


                //ambient occlusion base on density
                float ambientFactor = (float) _ChunkSize / _NumInstanceDimension;
                ambientFactor = 1-  min(1 - _AmbientOcclusion, ambientFactor);

                color *=  pow(i.uv.y , ambientFactor);
                
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
