Shader "Toaster/DebugPointCloud"
{
    Properties
    {
        _VolumeTex ("Volume Texture", 3D) = "" {}
        _PointSize ("Point Size", Range(0.01, 0.5)) = 0.1
        _Threshold ("Visibility Threshold", Range(0.001, 0.5)) = 0.01
        _ColorBoost ("Color Boost", Range(1, 10)) = 3.0
        _BoundsMin ("Bounds Min", Vector) = (-6, -4, -6, 0)
        _BoundsMax ("Bounds Max", Vector) = (6, 4, 6, 0)
        _GridRes ("Grid Resolution", Vector) = (48, 32, 48, 0)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }

        Pass
        {
            Name "PointCloud"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE3D(_VolumeTex);
            SAMPLER(sampler_VolumeTex);

            CBUFFER_START(UnityPerMaterial)
                float _PointSize;
                float _Threshold;
                float _ColorBoost;
                float4 _BoundsMin;
                float4 _BoundsMax;
                float4 _GridRes;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 color : TEXCOORD0;
                float alpha : TEXCOORD1;
            };

            Varyings vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                Varyings output;

                int3 res = int3(_GridRes.xyz);
                int totalVoxels = res.x * res.y * res.z;

                // Each instance = 1 voxel, 6 vertices per quad (2 triangles)
                int voxelIdx = instanceID;
                int quadVert = vertexID;

                // Convert linear index to 3D grid coords
                int z = voxelIdx / (res.x * res.y);
                int rem = voxelIdx - z * res.x * res.y;
                int y = rem / res.x;
                int x = rem - y * res.x;

                // Sample voxel data
                float3 uvw = (float3(x, y, z) + 0.5) / float3(res);
                float4 voxel = SAMPLE_TEXTURE3D_LOD(_VolumeTex, sampler_VolumeTex, uvw, 0);

                float brightness = max(voxel.r, max(voxel.g, voxel.b));

                // If below threshold, collapse to degenerate (hidden)
                if (brightness < _Threshold)
                {
                    output.positionCS = float4(0, 0, 0, 0);
                    output.color = 0;
                    output.alpha = 0;
                    return output;
                }

                // World position of voxel center
                float3 boundsSize = _BoundsMax.xyz - _BoundsMin.xyz;
                float3 worldPos = _BoundsMin.xyz + (float3(x, y, z) + 0.5) / float3(res) * boundsSize;

                // Billboard quad offsets
                float3 camRight = UNITY_MATRIX_V[0].xyz;
                float3 camUp = UNITY_MATRIX_V[1].xyz;

                // 6 verts: 2 triangles forming a quad
                // 0-1-2, 2-1-3 -> but we use 6 verts: 0,1,2, 2,3,0
                float2 offsets[6] = {
                    float2(-1, -1), float2(1, -1), float2(1, 1),
                    float2(1, 1), float2(-1, 1), float2(-1, -1)
                };
                float2 off = offsets[quadVert] * _PointSize * 0.5;

                float3 billboardPos = worldPos + camRight * off.x + camUp * off.y;

                output.positionCS = TransformWorldToHClip(billboardPos);
                output.color = voxel.rgb * _ColorBoost;
                output.alpha = saturate(brightness * 5.0);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                if (input.alpha < 0.001) discard;
                return half4(input.color, input.alpha);
            }
            ENDHLSL
        }
    }
}
