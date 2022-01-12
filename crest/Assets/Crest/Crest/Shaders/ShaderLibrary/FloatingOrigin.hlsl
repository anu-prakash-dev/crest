// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

#ifndef CREST_FLOATING_ORIGIN_INCLUDED
#define CREST_FLOATING_ORIGIN_INCLUDED

#if CREST_FLOATING_ORIGIN
#define CREST_WITH_FLOATING_ORIGIN_LOD_OFFSET(position) (position - (_CrestFloatingOriginOffset.xz % CREST_LOD_SIZE))
#define CREST_WITH_FLOATING_ORIGIN_MODULUS(position) (position % CREST_LOD_SIZE)
#else
#define CREST_WITH_FLOATING_ORIGIN_LOD_OFFSET(position) position
#define CREST_WITH_FLOATING_ORIGIN_MODULUS(position) position
#endif

namespace WaveHarmonic
{
	namespace Crest
	{
		float2 TiledFloatingOriginDivisor(const half i_scale, const float i_textureSize)
		{
			// Safely assumes a square texture.
			return i_scale * i_textureSize;
		}
	}
}

#endif // CREST_FLOATING_ORIGIN_INCLUDED
