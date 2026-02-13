Shader "Toaster/DebugIsosurface"
{
    Properties
    {
        _VolumeTex ("Volume Texture", 3D) = "" {}
        _Threshold ("Surface Threshold", Range(0.001, 1.0)) = 0.01
        _StepCount ("Ray Steps", Range(32, 512)) = 128
        _ColorBoost ("Color Boost", Range(1, 10)) = 3.0
        _AmbientLight ("Ambient Light", Range(0, 1)) = 0.2
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }

        Pass
        {
            Name "Isosurface"
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
                float _Threshold;
                float _StepCount;
                float _ColorBoost;
                float _AmbientLight;
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

            // Estimate normal from voxel density gradient (central differences)
            float3 EstimateNormal(float3 uvw, float texelSize)
            {
                float dx = SAMPLE_TEXTURE3D_LOD(_VolumeTex, sampler_VolumeTex, uvw + float3(texelSize, 0, 0), 0).a
                         - SAMPLE_TEXTURE3D_LOD(_VolumeTex, sampler_VolumeTex, uvw - float3(texelSize, 0, 0), 0).a;
                float dy = SAMPLE_TEXTURE3D_LOD(_VolumeTex, sampler_VolumeTex, uvw + float3(0, texelSize, 0), 0).a
                         - SAMPLE_TEXTURE3D_LOD(_VolumeTex, sampler_VolumeTex, uvw - float3(0, texelSize, 0), 0).a;
                float dz = SAMPLE_TEXTURE3D_LOD(_VolumeTex, sampler_VolumeTex, uvw + float3(0, 0, texelSize), 0).a
                         - SAMPLE_TEXTURE3D_LOD(_VolumeTex, sampler_VolumeTex, uvw - float3(0, 0, texelSize), 0).a;
                return normalize(float3(dx, dy, dz));
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

                float totalDist = tHit.y - tHit.x;
                float stepSize = totalDist / _StepCount;
                float texelSize = 1.0 / 48.0; // approximate voxel grid resolution

                // Light direction (hardcoded sun direction for debug viz)
                float3 lightDir = normalize(float3(0.5, 1.0, 0.3));

                for (int i = 0; i < (int)_StepCount; i++)
                {
                    float t = tHit.x + (i + 0.5) * stepSize;
                    float3 pos = rayOrigin + rayDir * t;
                    float3 uvw = (pos - boundsMin) / boundsSize;

                    float4 voxel = SAMPLE_TEXTURE3D_LOD(_VolumeTex, sampler_VolumeTex, uvw, 0);
                    float density = max(voxel.r, max(voxel.g, voxel.b));

                    if (density > _Threshold)
                    {
                        // Found surface — shade it
                        float3 normal = EstimateNormal(uvw, texelSize);
                        float ndl = saturate(dot(normal, lightDir));
                        float lighting = _AmbientLight + (1.0 - _AmbientLight) * ndl;

                        float3 color = voxel.rgb * _ColorBoost * lighting;
                        return half4(color, 1.0);
                    }
                }

                return half4(0, 0, 0, 0);
            }
            ENDHLSL
        }
    }
}
