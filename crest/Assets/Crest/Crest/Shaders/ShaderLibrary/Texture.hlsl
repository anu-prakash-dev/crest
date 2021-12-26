// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

#ifndef CREST_TEXTURE_INCLUDED
#define CREST_TEXTURE_INCLUDED

#include "../OceanGlobals.hlsl"
#include "../OceanInputsDriven.hlsl"
#include "../ShaderLibrary/FloatingOrigin.hlsl"

namespace WaveHarmonic
{
	namespace Crest
	{
		struct TiledTexture
		{
			Texture2D _texture;
			SamplerState _sampler;
			half _size;
			half _scale;
			float _texel;

			static TiledTexture Make
			(
				in const Texture2D i_texture,
				in const SamplerState i_sampler,
				in const float4 i_size,
				in const half i_scale
			)
			{
				TiledTexture tiledTexture;
				tiledTexture._texture = i_texture;
				tiledTexture._sampler = i_sampler;
				tiledTexture._scale = i_scale;
				// Safely assume a square texture.
				tiledTexture._size = i_size.z;
				tiledTexture._texel = i_size.x;
				return tiledTexture;
			}

			half4 Sample(float2 uv)
			{
				return _texture.Sample(_sampler, uv);
			}

			half4 SampleLevel(float2 uv, float lod)
			{
				return _texture.SampleLevel(_sampler, uv, lod);
			}

			float2 FloatingOriginOffset()
			{
				return _CrestFloatingOriginOffset.xz % TiledFloatingOriginDivisor(_scale, _size);
			}

			float2 ScaledFloatingOriginOffset(const CascadeParams i_cascadeData)
			{
				return _CrestFloatingOriginOffset.xz % TiledFloatingOriginDivisor(_scale * i_cascadeData._texelWidth, _size);
			}
		};
	}
}

#endif // CREST_TEXTURE_INCLUDED