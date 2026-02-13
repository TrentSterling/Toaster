Shader "Toaster/DebugHeatmap"
{
    Properties
    {
        _VolumeTex ("Volume Texture", 3D) = "" {}
        _Slice ("Z Slice (0-1)", Range(0, 1)) = 0.5
        [KeywordEnum(Density, Luminance, Red, Green, Blue, Alpha)] _Channel ("Channel", Float) = 0
        _Boost ("Brightness Boost", Range(1, 20)) = 5.0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "DebugHeatmap"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _CHANNEL_DENSITY _CHANNEL_LUMINANCE _CHANNEL_RED _CHANNEL_GREEN _CHANNEL_BLUE _CHANNEL_ALPHA

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE3D(_VolumeTex);
            SAMPLER(sampler_VolumeTex);

            CBUFFER_START(UnityPerMaterial)
                float _Slice;
                float _Boost;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            // Attempt heatmap: black -> blue -> cyan -> green -> yellow -> red -> white
            half3 Heatmap(float t)
            {
                t = saturate(t);
                half3 c;
                if (t < 0.2)
                    c = lerp(half3(0, 0, 0), half3(0, 0, 1), t / 0.2);
                else if (t < 0.4)
                    c = lerp(half3(0, 0, 1), half3(0, 1, 1), (t - 0.2) / 0.2);
                else if (t < 0.6)
                    c = lerp(half3(0, 1, 1), half3(0, 1, 0), (t - 0.4) / 0.2);
                else if (t < 0.8)
                    c = lerp(half3(0, 1, 0), half3(1, 1, 0), (t - 0.6) / 0.2);
                else
                    c = lerp(half3(1, 1, 0), half3(1, 0, 0), (t - 0.8) / 0.2);
                return c;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 uvw = float3(input.uv, _Slice);
                float4 voxel = SAMPLE_TEXTURE3D(_VolumeTex, sampler_VolumeTex, uvw);

                float value = 0;
                #if _CHANNEL_DENSITY
                    value = (voxel.r + voxel.g + voxel.b) * 0.333;
                #elif _CHANNEL_LUMINANCE
                    value = dot(voxel.rgb, half3(0.2126, 0.7152, 0.0722));
                #elif _CHANNEL_RED
                    value = voxel.r;
                #elif _CHANNEL_GREEN
                    value = voxel.g;
                #elif _CHANNEL_BLUE
                    value = voxel.b;
                #elif _CHANNEL_ALPHA
                    value = voxel.a;
                #endif

                value *= _Boost;
                half3 color = Heatmap(value);
                float alpha = saturate(value * 5.0); // fade in from nothing

                // Grid overlay — subtle lines at voxel boundaries
                float2 gridUV = input.uv * 48.0; // approximate grid resolution
                float2 grid = abs(frac(gridUV - 0.5) - 0.5) / fwidth(gridUV);
                float gridLine = 1.0 - saturate(min(grid.x, grid.y));
                color = lerp(color, half3(0.3, 0.3, 0.3), gridLine * 0.15);

                return half4(color, max(alpha, gridLine * 0.1));
            }
            ENDHLSL
        }
    }
}
