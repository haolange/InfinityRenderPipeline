#ifndef INFINITY_TEXTURE_VIEW_INCLUDED
#define INFINITY_TEXTURE_VIEW_INCLUDED
#include "TextureViews.generated.hlsl"
float2 InfinityTextureUV(float2 uv, float4 view, float mip)
{
    float2 result = uv;
    if (view.x > 0 && any(view.xy != view.zw))
    {
        float2 logical = max(1, floor(view.xy / exp2(mip)));
        float2 physical = max(1, floor(view.zw / exp2(mip)));
        result = clamp(uv * logical, .5, logical - .5) / physical;
    }
    return result;
}
float3 InfinityTextureUV(float3 uv, float4 view, float mip)
{ return float3(InfinityTextureUV(uv.xy, view, mip), uv.z); }
float2 InfinityTextureGradient(float2 gradient, float4 view)
{ return view.x > 0 ? gradient * view.xy / view.zw : gradient; }
float3 InfinityTextureGradient(float3 gradient, float4 view)
{ return float3(InfinityTextureGradient(gradient.xy, view), gradient.z); }
uint InfinityLogicalExtent(float4 view, uint physical, uint axis, uint mip)
{ return view.x > 0 ? (uint)max(1, floor(view[axis] / exp2(mip))) : physical; }
uint InfinityLogicalMipCount(float4 view, uint physical)
{ return view.x > 0 ? 1 + (uint)floor(log2(max(view.x, view.y))) : physical; }
#define InfinityGetDimensions(tex,w,h) { tex.GetDimensions(w,h); w=InfinityLogicalExtent(tex##_InfinityView,w,0,0); h=InfinityLogicalExtent(tex##_InfinityView,h,1,0); }
#define InfinityGetMipDimensions(tex,mip,w,h,levels) { tex.GetDimensions(mip,w,h,levels); w=InfinityLogicalExtent(tex##_InfinityView,w,0,mip); h=InfinityLogicalExtent(tex##_InfinityView,h,1,mip); levels=InfinityLogicalMipCount(tex##_InfinityView,levels); }
#define InfinityGetVolumeDimensions(tex,w,h,d) { tex.GetDimensions(w,h,d); w=InfinityLogicalExtent(tex##_InfinityView,w,0,0); h=InfinityLogicalExtent(tex##_InfinityView,h,1,0); }
#define InfinityGetVolumeMipDimensions(tex,mip,w,h,d,levels) { tex.GetDimensions(mip,w,h,d,levels); w=InfinityLogicalExtent(tex##_InfinityView,w,0,mip); h=InfinityLogicalExtent(tex##_InfinityView,h,1,mip); }
float InfinityViewLOD(float4 view, float lod)
{ return view.x > 0 && any(view.xy != view.zw) ? clamp(lod,0,InfinityLogicalMipCount(view,1)-1) : lod; }
#define INFINITY_SAMPLE_VIEW_LEVEL(tex,samp,uv,level) tex.SampleLevel(samp,InfinityTextureUV(uv,tex##_InfinityView,level),level)
#define INFINITY_SAMPLE_VIEW_LOD(tex,samp,uv,lod) lerp(INFINITY_SAMPLE_VIEW_LEVEL(tex,samp,uv,floor(lod)),INFINITY_SAMPLE_VIEW_LEVEL(tex,samp,uv,ceil(lod)),frac(lod))
#define INFINITY_VIEW_PADDED(tex) (tex##_InfinityView.x > 0 && any(tex##_InfinityView.xy != tex##_InfinityView.zw))
#define InfinitySampleLevel(tex,samp,uv,lod) (INFINITY_VIEW_PADDED(tex) ? INFINITY_SAMPLE_VIEW_LEVEL(tex,samp,uv,floor(InfinityViewLOD(tex##_InfinityView,lod)+.5)) : tex.SampleLevel(samp,uv,lod))
#define InfinitySampleLevelTrilinear(tex,samp,uv,lod) (INFINITY_VIEW_PADDED(tex) ? INFINITY_SAMPLE_VIEW_LOD(tex,samp,uv,InfinityViewLOD(tex##_InfinityView,lod)) : tex.SampleLevel(samp,uv,lod))
#define InfinitySample(tex,samp,uv) InfinitySampleGrad(tex,samp,uv,ddx(uv),ddy(uv))
float InfinityTextureLOD(float4 view, float2 dx, float2 dy)
{
    float2 x = dx * view.xy, y = dy * view.xy;
    return clamp(.5 * log2(max(max(dot(x,x),dot(y,y)),1e-12)),0,InfinityLogicalMipCount(view,1)-1);
}
#define InfinitySampleGrad(tex,samp,uv,dx,dy) ((tex##_InfinityView.x > 0 && any(tex##_InfinityView.xy != tex##_InfinityView.zw)) ? InfinitySampleLevelTrilinear(tex,samp,uv,InfinityTextureLOD(tex##_InfinityView,dx,dy)) : tex.SampleGrad(samp,uv,dx,dy))
#endif
