// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

#ifndef BUILTIN_PIPELINE_LIGHTING_INCLUDED
#define BUILTIN_PIPELINE_LIGHTING_INCLUDED

#if CREST_SCENE_CAMERA_LIGHT_FIX
half3 _CrestWorldSpaceLightPos0;
half3 _CrestLightColor0;
#endif

// Abstraction over Light shading data.
struct Light
{
    half3   direction;
    half3   color;
    // half    distanceAttenuation;
    // half    shadowAttenuation;
};

Light GetMainLight()
{
    Light light;
#if CREST_SCENE_CAMERA_LIGHT_FIX
    // Unity is not setting the sun correctly both in scene view and before transparent pass.
    light.direction = _CrestWorldSpaceLightPos0;
    light.color = _CrestLightColor0;
#else
    light.direction = half3(_WorldSpaceLightPos0.xyz);
    light.color = _LightColor0.rgb;
#endif
    return light;
}

#endif // BUILTIN_PIPELINE_LIGHTING_INCLUDED
