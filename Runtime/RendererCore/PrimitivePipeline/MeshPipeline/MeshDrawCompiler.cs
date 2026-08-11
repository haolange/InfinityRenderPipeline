using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// Burst-friendly MeshSortPlan → 64-bit lexicographic key (high bits = field0).
    /// Each field packs into a 16-bit segment. StableDrawId uses EncodeUnsigned(drawIndex) only as a
    /// coarse 16-bit bucket; true total order past 65535 draws comes from VisibleMeshDraw.CompareTo
    /// drawIndex tie-break after equal sortKey.
    /// </summary>
    [BurstCompile]
    public static class MeshSortKey
    {
        private const int k_SegmentBits = 16;
        private const uint k_SegmentMask = 0xFFFFu;
        private const float k_DefaultDistanceQuantizeScale = 100f; // centimeters

        /// <summary>
        /// Packs up to four MeshSortField segments into a 64-bit key.
        /// StableDrawId segments are coarse (16-bit); uniqueness beyond that is CompareTo(drawIndex).
        /// </summary>
        public static ulong PackSortKey(
            in MeshSortPlan plan,
            in MeshDrawRecord draw,
            in MeshInstanceRecord instance,
            float3 viewPos,
            int drawIndex)
        {
            ulong key = 0;
            int count = plan.count;
            if (count < 0)
            {
                count = 0;
            }

            if (count > 4)
            {
                count = 4;
            }

            for (int i = 0; i < count; ++i)
            {
                MeshSortField field = plan.GetField(i);
                uint segment = EncodeSemantic(field, draw, instance, viewPos, drawIndex) & k_SegmentMask;
                if (field.direction == ESortDirection.Descending)
                {
                    segment = (~segment) & k_SegmentMask;
                }

                key = (key << k_SegmentBits) | segment;
            }

            // Keep field0 in the most significant bits when fewer than 4 fields are used.
            if (count > 0 && count < 4)
            {
                key <<= k_SegmentBits * (4 - count);
            }

            return key;
        }

        private static uint EncodeSemantic(
            in MeshSortField field,
            in MeshDrawRecord draw,
            in MeshInstanceRecord instance,
            float3 viewPos,
            int drawIndex)
        {
            switch (field.semantic)
            {
                case EMeshSortSemantic.RenderQueue:
                    return EncodeUnsigned(draw.renderQueue);
                case EMeshSortSemantic.PassPriority:
                    return EncodeSigned(draw.priority);
                case EMeshSortSemantic.Material:
                    return HashInt(draw.materialUnityId);
                case EMeshSortSemantic.Mesh:
                    return HashInt(draw.meshUnityId);
                case EMeshSortSemantic.Section:
                    return EncodeUnsigned(draw.sectionIndex);
                case EMeshSortSemantic.Distance:
                    return EncodeDistance(instance.worldBounds.center, viewPos, field.quantizeScale);
                case EMeshSortSemantic.StableDrawId:
                    // Coarse 16-bit bucket only; VisibleMeshDraw.CompareTo uses drawIndex for true stability.
                    return EncodeUnsigned(drawIndex);
                case EMeshSortSemantic.InstanceGroup:
                    return HashInt((int)instance.transform.Index);
                case EMeshSortSemantic.PipelineState:
                case EMeshSortSemantic.Shader:
                case EMeshSortSemantic.GeometryLayout:
                default:
                    return EncodeUnsigned(draw.priority);
            }
        }

        private static uint EncodeSigned(int value)
        {
            // 16-bit biased encoding: clamp to int16 range, then +32768 → [0, 65535].
            return (uint)(math.clamp(value, -32768, 32767) + 32768);
        }

        private static uint EncodeUnsigned(int value)
        {
            if (value < 0)
            {
                return 0;
            }

            if (value > 65535)
            {
                return 65535u;
            }

            return (uint)value;
        }

        private static uint EncodeDistance(float3 center, float3 viewPos, float quantizeScale)
        {
            float dist = math.length(center - viewPos);
            float scale = quantizeScale > 0f ? quantizeScale : k_DefaultDistanceQuantizeScale;
            float scaled = dist * scale;
            if (scaled <= 0.0f)
            {
                return 0u;
            }

            if (scaled >= 65535.0f)
            {
                return 65535u;
            }

            return (uint)scaled;
        }

        private static uint HashInt(int value)
        {
            unchecked
            {
                uint x = (uint)value;
                x ^= x >> 16;
                x *= 0x7feb352du;
                x ^= x >> 15;
                x *= 0x846ca68bu;
                x ^= x >> 16;
                return x;
            }
        }
    }

    [BurstCompile]
    public struct MeshPassFilterJob : IJob
    {
        [ReadOnly] public NativeArray<byte> instanceVisibility;
        [ReadOnly] public NativeArray<MeshInstanceRecord> instances;
        [ReadOnly] public NativeArray<uint> instanceGenerations;
        [ReadOnly] public NativeArray<uint> transformGenerations;
        [ReadOnly] public NativeArray<MeshDrawRecord> draws;
        [ReadOnly] public NativeArray<MeshPassDrawId> passDrawIds;
        [ReadOnly] public MeshFilterProgram filter;
        [ReadOnly] public MeshSortPlan sort;
        public float3 viewPosition;
        public int shaderPassIndex;
        public int drawHighWater;

        public NativeList<VisibleMeshDraw> visibleDraws;

        public void Execute()
        {
            for (int drawIndex = 0; drawIndex < drawHighWater; ++drawIndex)
            {
                MeshDrawRecord draw = draws[drawIndex];
                MeshInstanceId instanceId = draw.instance;
                if (!instanceId.IsValid || instanceId.Index >= (uint)instanceGenerations.Length)
                {
                    continue;
                }

                int instanceIndex = (int)instanceId.Index;
                if (instanceGenerations[instanceIndex] != instanceId.Generation)
                {
                    continue;
                }

                if (instanceIndex >= instanceVisibility.Length || instanceVisibility[instanceIndex] == 0)
                {
                    continue;
                }

                MeshInstanceRecord instance = instances[instanceIndex];

                TransformId transformId = instance.transform;
                if (!transformId.IsValid
                    || transformId.Index >= (uint)transformGenerations.Length
                    || transformGenerations[(int)transformId.Index] != transformId.Generation)
                {
                    continue;
                }

                if ((draw.eligibility & filter.requiredEligibility) != filter.requiredEligibility)
                {
                    continue;
                }

                if (draw.renderQueue < filter.renderQueueMin || draw.renderQueue > filter.renderQueueMax)
                {
                    continue;
                }

                if ((instance.layerMask & filter.layerMask) == 0)
                {
                    continue;
                }

                if ((instance.renderingLayerMask & filter.renderingLayerMask) == 0)
                {
                    continue;
                }

                if (filter.excludeCameraMotionOnly && instance.motionType == EMotionType.Camera)
                {
                    continue;
                }

                var grouping = new MeshGroupingKey(draw.meshUnityId, draw.sectionIndex, draw.materialUnityId, shaderPassIndex);
                visibleDraws.Add(new VisibleMeshDraw
                {
                    grouping = grouping,
                    passDrawId = passDrawIds.IsCreated ? passDrawIds[drawIndex] : MeshPassDrawId.Invalid,
                    instance = instanceId,
                    sortKey = MeshSortKey.PackSortKey(sort, draw, instance, viewPosition, drawIndex),
                    drawIndex = drawIndex,
                    transformIndex = (int)transformId.Index
                });
            }
        }
    }

    [BurstCompile]
    public struct MeshPassSortJob : IJob
    {
        public NativeList<VisibleMeshDraw> visibleDraws;

        public void Execute()
        {
            if (visibleDraws.Length > 1)
            {
                visibleDraws.Sort();
            }
        }
    }

    [BurstCompile]
    public struct MeshPassBuildJob : IJob
    {
        [ReadOnly] public NativeList<VisibleMeshDraw> visibleDraws;
        [ReadOnly] public NativeArray<MeshDrawRecord> draws;

        public NativeList<MeshDrawCommand> drawCommands;
        public NativeArray<int> instanceIndices;
        public NativeArray<int> instanceSlotIndices;

        public void Execute()
        {
            if (visibleDraws.Length == 0)
            {
                return;
            }

            MeshPassDrawId lastPassDrawId = MeshPassDrawId.Invalid;
            MeshGroupingKey lastGrouping = default;
            bool hasLast = false;
            bool lastUsedPassDrawId = false;

            for (int i = 0; i < visibleDraws.Length; ++i)
            {
                VisibleMeshDraw visible = visibleDraws[i];
                // CPU Submit index buffer: TransformId.Index for shared matrix lookup.
                instanceIndices[i] = visible.transformIndex;
                // GPU candidate stream: MeshInstanceId.Index for instance-indexed cull/bounds.
                instanceSlotIndices[i] = visible.instance.IsValid ? (int)visible.instance.Index : -1;

                MeshDrawRecord draw = draws[visible.drawIndex];
                bool newGroup;
                if (visible.passDrawId.IsValid)
                {
                    newGroup = !hasLast || !lastUsedPassDrawId || !visible.passDrawId.Equals(lastPassDrawId);
                    lastPassDrawId = visible.passDrawId;
                    lastUsedPassDrawId = true;
                }
                else
                {
                    newGroup = !hasLast || lastUsedPassDrawId || !visible.grouping.Equals(lastGrouping);
                    lastGrouping = visible.grouping;
                    lastUsedPassDrawId = false;
                }

                if (newGroup)
                {
                    hasLast = true;
                    drawCommands.Add(new MeshDrawCommand(
                        draw.meshUnityId,
                        draw.sectionIndex,
                        draw.materialUnityId,
                        new int2(0, i)));
                }

                MeshDrawCommand command = drawCommands[drawCommands.Length - 1];
                command.countOffset.x += 1;
                drawCommands[drawCommands.Length - 1] = command;
            }
        }
    }
}
