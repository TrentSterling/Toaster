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

            // Scattering texture (for debug modes that need raw scatter data)
            Texture3D<float4> _FroxelScatterTex;
            SamplerState sampler_FroxelScatterTex;

            // Depth distribution params (must match compute)
            float _FroxelNear;
            float _FroxelFar;
            float _DepthUniformity;
            int _FroxelResZ;

            // Debug: 0=off, 1=scattering RGB, 2=extinction, 3=transmittance, 4=depth slices
            int _FroxelDebugMode;

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

            // Hue-based colormap for debug visualizations
            float3 DebugHeatmap(float t)
            {
                // Blue → Cyan → Green → Yellow → Red
                float3 c;
                t = saturate(t);
                if (t < 0.25)
                    c = lerp(float3(0, 0, 1), float3(0, 1, 1), t * 4.0);
                else if (t < 0.5)
                    c = lerp(float3(0, 1, 1), float3(0, 1, 0), (t - 0.25) * 4.0);
                else if (t < 0.75)
                    c = lerp(float3(0, 1, 0), float3(1, 1, 0), (t - 0.5) * 4.0);
                else
                    c = lerp(float3(1, 1, 0), float3(1, 0, 0), (t - 0.75) * 4.0);
                return c;
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

                // Debug modes — replace output with diagnostic visualization
                if (_FroxelDebugMode > 0)
                {
                    float3 debugColor = float3(0, 0, 0);

                    if (_FroxelDebugMode == 1) // Scattering RGB
                    {
                        float4 scatter = _FroxelScatterTex.SampleLevel(sampler_FroxelScatterTex, uvw, 0);
                        debugColor = scatter.rgb;
                    }
                    else if (_FroxelDebugMode == 2) // Extinction (density)
                    {
                        float4 scatter = _FroxelScatterTex.SampleLevel(sampler_FroxelScatterTex, uvw, 0);
                        debugColor = DebugHeatmap(scatter.a * 10.0); // scale up for visibility
                    }
                    else if (_FroxelDebugMode == 3) // Transmittance
                    {
                        debugColor = DebugHeatmap(1.0 - fog.a); // 0=clear, 1=opaque
                    }
                    else if (_FroxelDebugMode == 4) // Depth slices
                    {
                        float sliceNorm = slice / (float)_FroxelResZ;
                        debugColor = DebugHeatmap(sliceNorm);
                        // Grid lines at slice boundaries
                        float sliceFrac = frac(slice);
                        if (sliceFrac < 0.05 || sliceFrac > 0.95)
                            debugColor = float3(1, 1, 1);
                    }

                    // Debug replaces the scene entirely (Blend One SrcAlpha, a=0 kills scene)
                    return half4(debugColor, 0.0);
                }

                // Smooth fade at far edge — prevents hard cutoff at maxDistance
                // Fade over last 15% of range: full fog at 85%, zero at 100%
                float farFade = saturate((_FroxelFar - linearDepth) / (_FroxelFar * 0.15));
                fog.rgb *= farFade;
                fog.a = lerp(1.0, fog.a, farFade); // transmittance → 1.0 (clear) at far edge

                // fog.rgb = accumulated in-scattered light
                // fog.a = remaining transmittance
                // Blend: dst = fog.rgb + scene.rgb * fog.a
                return half4(fog.rgb, fog.a);
            }
            ENDHLSL
        }
    }
}
