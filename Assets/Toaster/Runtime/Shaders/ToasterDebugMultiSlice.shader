Shader "Toaster/DebugMultiSlice"
{
    Properties
    {
        _VolumeTex ("Volume Texture", 3D) = "" {}
        _SliceCount ("Slice Count", Range(4, 64)) = 16
        _Opacity ("Slice Opacity", Range(0.01, 1.0)) = 0.3
        _ColorBoost ("Color Boost", Range(1, 10)) = 3.0
        _AnimSpeed ("Animation Speed", Range(0, 2)) = 0.0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }

        Pass
        {
            Name "MultiSlice"
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Front
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE3D(_VolumeTex);
            SAMPLER(sampler_VolumeTex);

            CBUFFER_START(UnityPerMaterial)
                float _SliceCount;
                float _Opacity;
                float _ColorBoost;
                float _AnimSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            float2 RayBoxIntersect(float3 ro, float3 rd, float3 bmin, float3 bmax)
            {
                float3 invDir = 1.0 / rd;
                float3 t0 = (bmin - ro) * invDir;
                float3 t1 = (bmax - ro) * invDir;
                float3 tmin = min(t0, t1);
                float3 tmax = max(t0, t1);
                float tN = max(max(tmin.x, tmin.y), tmin.z);
                float tF = min(min(tmax.x, tmax.y), tmax.z);
                return float2(tN, tF);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 boxMin = TransformObjectToWorld(float3(-0.5, -0.5, -0.5));
                float3 boxMax = TransformObjectToWorld(float3(0.5, 0.5, 0.5));
                float3 boundsMin = min(boxMin, boxMax);
                float3 boundsMax = max(boxMin, boxMax);
                float3 boundsSize = boundsMax - boundsMin;

                float3 rayOrigin = GetCameraPositionWS();
                float3 rayDir = normalize(input.positionWS - rayOrigin);

                float2 tHit = RayBoxIntersect(rayOrigin, rayDir, boundsMin, boundsMax);
                if (tHit.x > tHit.y) return half4(0, 0, 0, 0);
                tHit.x = max(tHit.x, 0.0);

                float animOffset = _AnimSpeed > 0 ? frac(_Time.y * _AnimSpeed) / _SliceCount : 0;

                // Front-to-back composite discrete slices
                float3 accColor = 0;
                float accAlpha = 0;

                int sliceCount = (int)_SliceCount;
                for (int i = 0; i < sliceCount; i++)
                {
                    if (accAlpha > 0.95) break;

                    // Each slice is a Y-plane at a fixed world height
                    float sliceT = (float(i) + 0.5) / float(sliceCount) + animOffset;
                    sliceT = frac(sliceT); // wrap for animation
                    float sliceY = boundsMin.y + sliceT * boundsSize.y;

                    // Find ray intersection with this Y-plane
                    float t = (sliceY - rayOrigin.y) / rayDir.y;
                    if (t < tHit.x || t > tHit.y) continue;

                    float3 hitPos = rayOrigin + rayDir * t;
                    float3 uvw = (hitPos - boundsMin) / boundsSize;

                    // Check bounds
                    if (any(uvw < 0) || any(uvw > 1)) continue;

                    float4 voxel = SAMPLE_TEXTURE3D_LOD(_VolumeTex, sampler_VolumeTex, uvw, 0);
                    float3 color = voxel.rgb * _ColorBoost;
                    float brightness = max(color.r, max(color.g, color.b));

                    if (brightness < 0.001) continue;

                    float sliceAlpha = saturate(brightness) * _Opacity;

                    // Subtle slice edge highlight
                    float2 edgeDist = min(uvw.xz, 1.0 - uvw.xz);
                    float edge = 1.0 - saturate(min(edgeDist.x, edgeDist.y) * 20.0);
                    color += half3(0.1, 0.1, 0.15) * edge * 0.3;

                    // Front-to-back blend
                    float transmittance = 1.0 - accAlpha;
                    accColor += color * sliceAlpha * transmittance;
                    accAlpha += sliceAlpha * transmittance;
                }

                return half4(accColor, accAlpha);
            }
            ENDHLSL
        }
    }
}
