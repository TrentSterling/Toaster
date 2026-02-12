Shader "Toaster/DebugSlice"
{
    Properties
    {
        _VolumeTex ("Volume Texture", 3D) = "" {}
        _Slice ("Z Slice (0-1)", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "DebugSlice"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE3D(_VolumeTex);
            SAMPLER(sampler_VolumeTex);

            CBUFFER_START(UnityPerMaterial)
                float _Slice;
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
                float4 col = SAMPLE_TEXTURE3D(_VolumeTex, sampler_VolumeTex, uvw);

                // Show alpha as brightness if RGB is empty
                float brightness = max(col.r, max(col.g, col.b));
                if (brightness < 0.001)
                    col.rgb = col.aaa;

                col.a = max(col.a, brightness);
                return col;
            }
            ENDHLSL
        }
    }
}
