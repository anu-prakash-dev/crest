Shader "Unlit/UnderwaterTransparentShader"
{
	Properties
	{
		_Color ("Color", Color) = (0.5, 0.5, 0.5, 0.5)
	}
	SubShader
	{
		Tags { "RenderType"="Transparent" "Queue"="Transparent" }
		LOD 100
		Blend SrcAlpha OneMinusSrcAlpha
		ZWrite Off
		Cull Back

		Pass
		{
			Tags { "LightMode" = "ForwardBase" }
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			#pragma multi_compile_fog

			#pragma multi_compile _ CREST_WATER_VOLUME_2D CREST_WATER_VOLUME_HAS_BACKFACE
			#pragma multi_compile _ CREST_SUBSURFACESCATTERING_ON
			#pragma multi_compile _ CREST_SHADOWS_ON

			#include "UnityCG.cginc"
			#include "Lighting.cginc"

			#include "/Assets/Crest/Crest/Shaders/Underwater/UnderwaterEffectIncludes.hlsl"

			struct appdata
			{
				float4 vertex : POSITION;
			};

			struct v2f
			{
				UNITY_FOG_COORDS(1)
				float4 vertex : SV_POSITION;
				float3 worldPos : TEXCOORD2;
				half4 screenPos : TEXCOORD4;
			};

			float4 _Color;
			float _Metallic;
			float _Smoothness;

			v2f vert (appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				UNITY_TRANSFER_FOG(o, o.vertex);
				o.worldPos = mul(unity_ObjectToWorld, v.vertex);
				o.screenPos = ComputeScreenPos(o.vertex);
				return o;
			}

			fixed4 frag (v2f i) : SV_Target
			{
				fixed3 color = _Color.rgb;
				float2 positionNDC = i.screenPos.xy / i.screenPos.w;

				// Only apply underwater fog when underwater. Otherwise, apply Unity fog.
				if (!CrestApplyUnderwaterFog(positionNDC, i.worldPos, i.vertex.z, color.rgb))
				{
					UNITY_APPLY_FOG(i.fogCoord, color);
				}

				return fixed4(color, _Color.a);
			}
			ENDCG
		}
	}
}
