Shader "Custom/UnderwaterSurfaceShader"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _FogMultiplier ("Fog Multiplier", Range(0, 1)) = 1.0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType"="Transparent" }
        LOD 200

        CGPROGRAM
        #pragma surface Surface Standard vertex:Vertex finalcolor:FinalColor alpha:blend
        #pragma target 3.0
        #pragma require 2darray
        #pragma multi_compile_fog

        // #pragma enable_d3d11_debug_symbols

        #pragma multi_compile _ CREST_WATER_VOLUME_2D CREST_WATER_VOLUME_HAS_BACKFACE
        #pragma multi_compile _ CREST_SUBSURFACESCATTERING_ON
        #pragma multi_compile _ CREST_SHADOWS_ON

        #include "UnityCG.cginc"

        #include "/Assets/Crest/Crest/Shaders/Underwater/UnderwaterEffectIncludes.hlsl"

        struct Input
        {
            float3 worldPos;
            float4 screenPos;
            float1 fogCoord;
        };

        half _Glossiness;
        half _Metallic;
        fixed4 _Color;
        half _FogMultiplier;

        void Vertex (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            UNITY_TRANSFER_FOG(o, UnityObjectToClipPos(v.vertex));
        }

        void FinalColor(Input IN, SurfaceOutputStandard o, inout fixed4 color)
        {
            float2 positionNDC = IN.screenPos.xy / IN.screenPos.w;
            float deviceDepth = IN.screenPos.z / IN.screenPos.w;

            if (!CrestApplyUnderwaterFog(positionNDC, IN.worldPos, deviceDepth, _FogMultiplier, color.rgb))
            {
                UNITY_APPLY_FOG(IN.fogCoord, color);
            }
        }

        void Surface(Input IN, inout SurfaceOutputStandard o)
        {
            o.Albedo = _Color.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = _Color.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
