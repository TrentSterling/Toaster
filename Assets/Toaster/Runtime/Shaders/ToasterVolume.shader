Shader "Toaster/Volume"
{
    Properties
    {
        _VolumeTex ("Volume Texture (Albedo)", 3D) = "" {}
        [Toggle(_USE_LIGHTING)] _UseLighting ("Use Lighting Grid", Float) = 0
        _LightingTex ("Lighting Grid", 3D) = "" {}
        _Intensity ("Intensity", Range(0, 10)) = 1.0
        _StepCount ("Step Count", Range(8, 256)) = 64
        _Density ("Density Multiplier", Range(0, 5)) = 1.0
        [Toggle(_ADDITIVE_MODE)] _AdditiveMode ("Additive Blend (Emissive Glow)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }

        Pass
        {
            Name "VolumeRaymarch"
            Blend One OneMinusSrcAlpha
            Cull Front
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _USE_LIGHTING
            #pragma shader_feature_local _ADDITIVE_MODE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE3D(_VolumeTex);
            SAMPLER(sampler_VolumeTex);
            TEXTURE3D(_LightingTex);
            SAMPLER(sampler_LightingTex);

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
                float _StepCount;
                float _Density;
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

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                return output;
            }

            // Ray-box intersection (slab method)
            // Returns (tNear, tFar) — if tNear > tFar, no intersection
            float2 RayBoxIntersect(float3 rayOrigin, float3 rayDir, float3 boxMin, float3 boxMax)
            {
                float3 invDir = 1.0 / rayDir;
                float3 t0 = (boxMin - rayOrigin) * invDir;
                float3 t1 = (boxMax - rayOrigin) * invDir;
                float3 tmin = min(t0, t1);
                float3 tmax = max(t0, t1);
                float tNear = max(max(tmin.x, tmin.y), tmin.z);
                float tFar = min(min(tmax.x, tmax.y), tmax.z);
                return float2(tNear, tFar);
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Box bounds in world space: object-space unit cube [-0.5, 0.5] transformed
                float3 boxMin = TransformObjectToWorld(float3(-0.5, -0.5, -0.5));
                float3 boxMax = TransformObjectToWorld(float3(0.5, 0.5, 0.5));

                // Ensure min < max (handles negative scales)
                float3 boundsMin = min(boxMin, boxMax);
                float3 boundsMax = max(boxMin, boxMax);

                float3 rayOrigin = GetCameraPositionWS();
                float3 rayDir = normalize(input.positionWS - rayOrigin);

                float2 tHit = RayBoxIntersect(rayOrigin, rayDir, boundsMin, boundsMax);

                if (tHit.x > tHit.y)
                    return half4(0, 0, 0, 0);

                // Clamp entry to camera (don't ray march behind camera)
                tHit.x = max(tHit.x, 0.0);

                float totalDist = tHit.y - tHit.x;
                float stepSize = totalDist / _StepCount;

                // Front-to-back compositing
                float3 accumulatedColor = 0;
                float accumulatedAlpha = 0;

                float3 boundsSize = boundsMax - boundsMin;

                for (int i = 0; i < (int)_StepCount; i++)
                {
                    if (accumulatedAlpha > 0.99)
                        break;

                    float t = tHit.x + (i + 0.5) * stepSize;
                    float3 samplePos = rayOrigin + rayDir * t;

                    // Convert world position to UVW [0,1]
                    float3 uvw = (samplePos - boundsMin) / boundsSize;

                    float4 voxelData = SAMPLE_TEXTURE3D_LOD(_VolumeTex, sampler_VolumeTex, uvw, 0);

                    #ifdef _USE_LIGHTING
                    // Blend albedo with traced lighting
                    float4 lightData = SAMPLE_TEXTURE3D_LOD(_LightingTex, sampler_LightingTex, uvw, 0);
                    float3 color = lightData.rgb * _Intensity;
                    #else
                    float3 color = voxelData.rgb * _Intensity;
                    #endif

                    float density = voxelData.a * _Density * stepSize;

                    // Front-to-back blending
                    float transmittance = 1.0 - accumulatedAlpha;
                    accumulatedColor += color * density * transmittance;
                    accumulatedAlpha += density * transmittance;
                }

                accumulatedAlpha = saturate(accumulatedAlpha);
                return half4(accumulatedColor, accumulatedAlpha);
            }
            ENDHLSL
        }
    }
}
