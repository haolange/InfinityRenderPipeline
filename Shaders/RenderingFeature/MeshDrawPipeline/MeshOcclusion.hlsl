#ifndef INFINITY_MESH_OCCLUSION
#define INFINITY_MESH_OCCLUSION
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "../../ShaderLibrary/HiZ.hlsl"

Texture2D<float> _MeshCullFurthest;
float4x4 _MeshCullProjection;
float4 _MeshCullFullExtent;
uint _MeshCullMipCount, _MeshCullCurrentCount, _MeshCullPreviousCount;
StructuredBuffer<float4x4> _MeshCullCurrentTransforms, _MeshCullPreviousTransforms;
StructuredBuffer<uint> _MeshCullCurrentGenerations, _MeshCullPreviousGenerations;
RWStructuredBuffer<uint> _DeferredVisibility, _FinalVisibility, _RecoveredVisibility;
StructuredBuffer<uint> _OcclusionDeferred;

bool MeshHasStableHistory(uint instance)
{
    uint transform = _InstanceToTransform[instance];
    bool stable = false;
    if (transform < _MeshCullCurrentCount && transform < _MeshCullPreviousCount)
    {
        uint generation = _MeshCullCurrentGenerations[transform];
        if (generation != 0 && generation == _MeshCullPreviousGenerations[transform])
        {
            float4x4 current = _MeshCullCurrentTransforms[transform];
            float4x4 previous = _MeshCullPreviousTransforms[transform];
            stable = all(current[0] == previous[0]) && all(current[1] == previous[1])
                && all(current[2] == previous[2]) && all(current[3] == previous[3]);
        }
    }
    return stable;
}

bool MeshOccluded(float3 center, float3 extent)
{
    bool reliable = _MeshCullMipCount != 0 && all(isfinite(_MeshCullFullExtent.xy)) && all(_MeshCullFullExtent.xy >= 1);
    bool occluded = false;
    float2 lower = 1, upper = 0;
#if UNITY_REVERSED_Z
    float nearest = 0;
#else
    float nearest = 1;
#endif
    const float machineEpsilon = 1.192092896e-7;
    float3 conservativeExtent = extent + 8 * machineEpsilon * (abs(center) + abs(extent));
    const float dotError = (4 * machineEpsilon) / (1 - 4 * machineEpsilon);
    [loop]
    for (uint corner = 0; corner < 8 && reliable; ++corner)
    {
        float3 cornerSign = float3((corner & 1) != 0 ? 1 : -1, (corner & 2) != 0 ? 1 : -1, (corner & 4) != 0 ? 1 : -1);
        float4 world = float4(center + conservativeExtent * cornerSign, 1);
        float4 clip = mul(_MeshCullProjection, world);
        // An unreliable projection cannot supply an occlusion proof. Keep reciprocal
        // evaluation and texture reads inside their corresponding validity branches.
        reliable = all(isfinite(clip)) && clip.w > 1.175494351e-38 && clip.z >= 0 && clip.z <= clip.w;
        if (reliable)
        {
            float3 projected = clip.xyz / clip.w;
            float4 magnitude = abs(world);
            float4 clipError = dotError * float4(dot(abs(_MeshCullProjection[0]), magnitude),
                dot(abs(_MeshCullProjection[1]), magnitude), dot(abs(_MeshCullProjection[2]), magnitude), dot(abs(_MeshCullProjection[3]), magnitude));
            float3 projectionError = (clipError.xyz + abs(projected) * clipError.w) / clip.w + 2 * machineEpsilon * abs(projected);
            reliable = all(isfinite(projected)) && all(isfinite(projectionError));
            if (reliable)
            {
                float2 uvLower = (projected.xy - projectionError.xy) * .5 + .5;
                float2 uvUpper = (projected.xy + projectionError.xy) * .5 + .5;
                reliable = all(uvLower >= 0) && all(uvUpper < 1);
                lower = min(lower, uvLower); upper = max(upper, uvUpper);
#if UNITY_REVERSED_Z
                nearest = max(nearest, min(1, projected.z + projectionError.z));
#else
                nearest = min(nearest, max(0, projected.z - projectionError.z));
#endif
            }
        }
    }
    if (reliable)
    {
        uint2 fullExtent = (uint2)_MeshCullFullExtent.xy;
        float footprint = max((upper.x - lower.x) * _MeshCullFullExtent.x, (upper.y - lower.y) * _MeshCullFullExtent.y);
        uint level = min(_MeshCullMipCount, max(1u, (uint)ceil(log2(max(footprint, 1)))));
        uint2 begin = HiZCell(lower, fullExtent, level);
        uint2 end = HiZCell(upper, fullExtent, level);
        occluded = true;
        [loop]
        for (uint y = begin.y; y <= end.y && occluded; ++y)
            [loop]
            for (uint x = begin.x; x <= end.x && occluded; ++x)
            {
                float farthest = _MeshCullFurthest.Load(int3(x, y, level - 1));
                occluded = isfinite(farthest);
                if (occluded)
                {
                    // Normalized arithmetic error only; no world-scale/artistic bias.
                    float error = 8 * machineEpsilon * (abs(farthest) + abs(nearest) + 1);
#if UNITY_REVERSED_Z
                    occluded = farthest > 0 && nearest + error < farthest;
#else
                    occluded = farthest < 1 && nearest - error > farthest;
#endif
                }
            }
    }
    return occluded;
}

void MeshWriteFirst(uint instance, bool visible, bool deferred)
{
    _Visibility[instance] = visible && !deferred ? 1 : 0;
    _FinalVisibility[instance] = visible && !deferred ? 1 : 0;
    _DeferredVisibility[instance] = deferred ? 1 : 0;
}
[numthreads(64, 1, 1)]
void CullFirstNoHistory(uint3 id : SV_DispatchThreadID)
{
    if (id.x < _InstanceCount) MeshWriteFirst(id.x, MeshFrustumVisible(id.x), false);
}
[numthreads(64, 1, 1)]
void CullFirstWithHistory(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= _InstanceCount) return;
    bool visible = MeshFrustumVisible(id.x);
    bool deferred = false;
    if (visible && MeshHasStableHistory(id.x))
        deferred = MeshOccluded(_InstanceBoundsCenter[id.x].xyz, _InstanceBoundsExtent[id.x].xyz);
    MeshWriteFirst(id.x, visible, deferred);
}
[numthreads(64, 1, 1)]
void CullOcclusionRecovery(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= _InstanceCount) return;
    bool deferred = _OcclusionDeferred[id.x] != 0;
    bool visible = false;
    if (deferred) visible = !MeshOccluded(_InstanceBoundsCenter[id.x].xyz, _InstanceBoundsExtent[id.x].xyz);
    _RecoveredVisibility[id.x] = visible ? 1 : 0;
    if (deferred) _FinalVisibility[id.x] = visible ? 1 : 0;
}
#endif
