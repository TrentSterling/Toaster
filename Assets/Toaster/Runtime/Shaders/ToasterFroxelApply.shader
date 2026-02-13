Shader "Toaster/FroxelApply"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" }

        Pass
        {
            Name "FroxelApply"

            // fog.rgb + scene.rgb * fog.transmittance
            // SrcFactor = One (fog contribution), DstFactor = SrcAlpha (scene * transmittance)
            Blend One SrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // Froxel integrated texture (accLight.rgb, transmittance)
            Texture3D<float4> _FroxelTex;
            SamplerState sampler_FroxelTex;

            // Depth distribution params (must match compute)
            float _FroxelNear;
            float _FroxelFar;
            float _DepthUniformity;
            int _FroxelResZ;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            // Fullscreen triangle from SV_VertexID — no mesh needed
            Varyings vert(uint vertexID : SV_VertexID)
            {
                Varyings o;

                // Triangle that covers the entire screen:
                // id=0: (-1,-1), id=1: (-1,3), id=2: (3,-1)
                float2 uv = float2((vertexID << 1) & 2, vertexID & 2);
                o.positionCS = float4(uv * 2.0 - 1.0, 0.0, 1.0);

                // Flip Y for UV (DX convention)
                #if UNITY_UV_STARTS_AT_TOP
                o.uv = float2(uv.x, 1.0 - uv.y);
                #else
                o.uv = uv;
                #endif

                return o;
            }

            // Depth to slice — must match ToasterFroxelCommon.hlsl
            float DepthToSliceFrag(float depth)
            {
                float t_linear = (depth - _FroxelNear) / (_FroxelFar - _FroxelNear);
                float t_log = log(depth / _FroxelNear) / log(_FroxelFar / _FroxelNear);
                float t = lerp(t_log, t_linear, _DepthUniformity);
                return saturate(t) * (float)_FroxelResZ;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;

                // Sample scene depth
                float rawDepth = SampleSceneDepth(uv);
                float linearDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

                // Clamp to froxel range
                linearDepth = clamp(linearDepth, _FroxelNear, _FroxelFar);

                // Map depth to slice
                float slice = DepthToSliceFrag(linearDepth);

                // Sample froxel grid — UVW coordinates
                float3 uvw = float3(uv.x, uv.y, slice / (float)_FroxelResZ);
                float4 fog = _FroxelTex.SampleLevel(sampler_FroxelTex, uvw, 0);

                // fog.rgb = accumulated in-scattered light
                // fog.a = remaining transmittance
                // Blend: dst = fog.rgb + scene.rgb * fog.a
                return half4(fog.rgb, fog.a);
            }
            ENDHLSL
        }
    }
}
