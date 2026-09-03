using System;
using System.Runtime.CompilerServices;
using InfinityTech.Rendering;

namespace InfinityTech.Rendering.MeshPipeline
{
    public struct MeshFilterProgram
    {
        public int renderQueueMin;
        public int renderQueueMax;
        public int layerMask;
        public uint renderingLayerMask;
        public EPassEligibility requiredEligibility;
        public bool excludeCameraMotionOnly;

        public MeshFilterProgram(
            int renderQueueMin,
            int renderQueueMax,
            EPassEligibility requiredEligibility,
            int layerMask = ~0,
            bool excludeCameraMotionOnly = false,
            uint renderingLayerMask = (uint)ERenderingLayer.Everything)
        {
            this.renderQueueMin = renderQueueMin;
            this.renderQueueMax = renderQueueMax;
            this.requiredEligibility = requiredEligibility;
            this.layerMask = layerMask;
            this.excludeCameraMotionOnly = excludeCameraMotionOnly;
            this.renderingLayerMask = renderingLayerMask;
        }
    }

    public struct MeshSortField
    {
        public EMeshSortSemantic semantic;
        public ESortDirection direction;
        /// <summary>
        /// Distance quantization scale (units per meter). 0 = use MeshSortKey default (100 = cm).
        /// </summary>
        public float quantizeScale;

        public MeshSortField(
            EMeshSortSemantic semantic,
            ESortDirection direction = ESortDirection.Ascending,
            float quantizeScale = 0f)
        {
            this.semantic = semantic;
            this.direction = direction;
            this.quantizeScale = quantizeScale;
        }
    }

    public struct MeshSortPlan
    {
        public MeshSortField field0;
        public MeshSortField field1;
        public MeshSortField field2;
        public MeshSortField field3;
        public int count;

        public static MeshSortPlan Create(params MeshSortField[] fields)
        {
            MeshSortPlan plan = default;
            plan.count = fields != null ? Math.Min(4, fields.Length) : 0;
            if (plan.count > 0) plan.field0 = fields[0];
            if (plan.count > 1) plan.field1 = fields[1];
            if (plan.count > 2) plan.field2 = fields[2];
            if (plan.count > 3) plan.field3 = fields[3];
            return plan;
        }

        public MeshSortField GetField(int index)
        {
            switch (index)
            {
                case 0: return field0;
                case 1: return field1;
                case 2: return field2;
                case 3: return field3;
                default: return default;
            }
        }
    }

    public struct MeshGroupingKey : IEquatable<MeshGroupingKey>, IComparable<MeshGroupingKey>
    {
        public int meshUnityId;
        public int sectionIndex;
        public int materialUnityId;
        public int pipelinePassIndex;

        public MeshGroupingKey(int meshUnityId, int sectionIndex, int materialUnityId, int pipelinePassIndex)
        {
            this.meshUnityId = meshUnityId;
            this.sectionIndex = sectionIndex;
            this.materialUnityId = materialUnityId;
            this.pipelinePassIndex = pipelinePassIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(MeshGroupingKey other)
        {
            return meshUnityId == other.meshUnityId
                && sectionIndex == other.sectionIndex
                && materialUnityId == other.materialUnityId
                && pipelinePassIndex == other.pipelinePassIndex;
        }

        public override bool Equals(object obj) => obj is MeshGroupingKey other && Equals(other);

        /// <summary>
        /// Hash is acceleration only; collisions must be resolved with Equals.
        /// </summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = meshUnityId;
                hash = (hash * 397) ^ sectionIndex;
                hash = (hash * 397) ^ materialUnityId;
                hash = (hash * 397) ^ pipelinePassIndex;
                return hash;
            }
        }

        public int CompareTo(MeshGroupingKey other)
        {
            int c = meshUnityId.CompareTo(other.meshUnityId);
            if (c != 0) return c;
            c = sectionIndex.CompareTo(other.sectionIndex);
            if (c != 0) return c;
            c = materialUnityId.CompareTo(other.materialUnityId);
            if (c != 0) return c;
            return pipelinePassIndex.CompareTo(other.pipelinePassIndex);
        }
    }

    public struct MeshPassDefinition
    {
        public string name;
        public int shaderPassIndex;
        public string lightModeTag;
        public MeshFilterProgram defaultFilter;
        public MeshSortPlan defaultSort;
        public EPassEligibility eligibility;
    }

    public static class BuiltinMeshesPasses
    {
        public static readonly MeshPassDefinition Depth = new MeshPassDefinition
        {
            name = "Depth",
            shaderPassIndex = 1,
            lightModeTag = "DepthPass",
            eligibility = EPassEligibility.Depth,
            defaultFilter = new MeshFilterProgram(0, 2999, EPassEligibility.Depth),
            // Distance: decimeter scale (10) — camera-range resolution without early 16-bit saturation.
            defaultSort = MeshSortPlan.Create(
                new MeshSortField(EMeshSortSemantic.Distance, ESortDirection.Ascending, quantizeScale: 10f),
                new MeshSortField(EMeshSortSemantic.Material),
                new MeshSortField(EMeshSortSemantic.Mesh),
                new MeshSortField(EMeshSortSemantic.StableDrawId))
        };

        public static readonly MeshPassDefinition GBuffer = new MeshPassDefinition
        {
            name = "GBuffer",
            shaderPassIndex = 2,
            lightModeTag = "GBufferPass",
            eligibility = EPassEligibility.GBuffer,
            defaultFilter = new MeshFilterProgram(0, 2999, EPassEligibility.GBuffer),
            defaultSort = MeshSortPlan.Create(
                new MeshSortField(EMeshSortSemantic.RenderQueue),
                new MeshSortField(EMeshSortSemantic.Material),
                new MeshSortField(EMeshSortSemantic.Mesh),
                new MeshSortField(EMeshSortSemantic.Section))
        };

        public static readonly MeshPassDefinition Forward = new MeshPassDefinition
        {
            name = "Forward",
            shaderPassIndex = 3,
            lightModeTag = "ForwardPass",
            eligibility = EPassEligibility.Forward,
            defaultFilter = new MeshFilterProgram(0, 2999, EPassEligibility.Forward),
            defaultSort = MeshSortPlan.Create(
                new MeshSortField(EMeshSortSemantic.RenderQueue),
                new MeshSortField(EMeshSortSemantic.Material),
                new MeshSortField(EMeshSortSemantic.Mesh),
                new MeshSortField(EMeshSortSemantic.StableDrawId))
        };

        public static readonly MeshPassDefinition Motion = new MeshPassDefinition
        {
            name = "Motion",
            shaderPassIndex = 4,
            lightModeTag = "MotionPass",
            eligibility = EPassEligibility.Motion,
            defaultFilter = new MeshFilterProgram(0, 2999, EPassEligibility.Motion, ~0, excludeCameraMotionOnly: true),
            // Distance: decimeter scale (10) — matches Depth camera-range quantization.
            defaultSort = MeshSortPlan.Create(
                new MeshSortField(EMeshSortSemantic.Distance, ESortDirection.Ascending, quantizeScale: 10f),
                new MeshSortField(EMeshSortSemantic.Material),
                new MeshSortField(EMeshSortSemantic.Mesh),
                new MeshSortField(EMeshSortSemantic.StableDrawId))
        };

        public static readonly MeshPassDefinition Shadow = new MeshPassDefinition
        {
            name = "Shadow",
            shaderPassIndex = 0,
            lightModeTag = "ShadowPass",
            eligibility = EPassEligibility.Shadow,
            defaultFilter = new MeshFilterProgram(0, 2999, EPassEligibility.Shadow),
            // Distance: meter scale (1) — coarser bins for cascade / large-world ranges.
            defaultSort = MeshSortPlan.Create(
                new MeshSortField(EMeshSortSemantic.Distance, ESortDirection.Ascending, quantizeScale: 1f),
                new MeshSortField(EMeshSortSemantic.Material),
                new MeshSortField(EMeshSortSemantic.Mesh),
                new MeshSortField(EMeshSortSemantic.StableDrawId))
        };
    }
}
