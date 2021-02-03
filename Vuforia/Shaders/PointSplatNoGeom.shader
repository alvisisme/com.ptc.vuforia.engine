/*===============================================================================
Copyright (c) 2019-2020 PTC Inc. All Rights Reserved.

Confidential and Proprietary - Protected under copyright and other laws.
Vuforia is a trademark of PTC Inc., registered in the United States and other 
countries.
===============================================================================*/
Shader "PointClouds/PointSplatNoGeom" 
{
    Properties {
        _PointSize("Point Size", Float) = 0.01
        _MinHeight("Min Height", Float) = -10.0
        _MaxHeight("Max Height", Float) = 10.0
        [Toggle(USE_NORMALS)] _UseNormals("Use Normals", Float) = 0.0
    }

    SubShader
    {
        Tags {"Queue" = "Geometry-11" }
        Pass
        {
            Lighting Off
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _PointSize;
            float _MinHeight;
            float _MaxHeight;
            float _UseNormals;

            struct VertexInput
            {
                float4 pos : POSITION;
                half4 color : COLOR;
                half4 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct VertexOutput
            {
                float4 pos : SV_POSITION;
                half4 color : COLOR;
                float3 worldPos : TEXCOORD0;
            };

            VertexOutput vert(VertexInput v)
            {
                VertexOutput o;
                
                // Convert quad center position from object space to camera space
                float3 quadCenter = v.pos;
                float3 quadCenterViewPos = UnityObjectToViewPos(quadCenter);

                // Get direction camera-to-point (in camera space)
                float3 eyeToQuadDir = normalize(quadCenterViewPos);

                // Set billboard forward dir (in camera space)
                float3 fwDir = eyeToQuadDir;
                if (_UseNormals > 0.5)
                {
                    // Set billboard forward
                    // according to normal (in camera space)
                    float3 normal = mul((float3x3)UNITY_MATRIX_MV, v.normal.xyz);
                    fwDir = -normal;
                }

                // Get view-up (in camera space)
                float3 viewUp = float3(0.0, 1.0, 0.0);

                // Compute quad right and up dir (in camera space)
                float3 quadRight = normalize(cross(fwDir, viewUp));
                float3 quadUp = normalize(cross(quadRight, fwDir));

                // Compute displaced vertex (in camera space)
                float3 radialVec = _PointSize * 2.0 * (quadRight * v.uv.x + quadUp * v.uv.y);
                float3 viewPos = quadCenterViewPos + radialVec;

                o.pos = mul(UNITY_MATRIX_P, float4(viewPos.x, viewPos.y, viewPos.z, 1.0));
                o.color = v.color;
                o.worldPos = mul(unity_ObjectToWorld, v.pos).xyz;
                return o;
            }

            float4 frag(VertexOutput v) : COLOR
            {
                clip(_MaxHeight - v.worldPos.y);
                clip(v.worldPos.y - _MinHeight);
                return v.color;
            }

            ENDCG
        }
    }
}