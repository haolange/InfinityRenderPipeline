using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    public partial class InfinityRenderPipeline
    {
        struct ShadowCasterSplitRecord
        {
            public int lightIndex;
            public int splitIndex;
            public ShadowSplitData splitData;
            public BatchCullingProjectionType projectionType;
        }

        readonly List<ShadowCasterSplitRecord> m_ShadowCasterSplits = new List<ShadowCasterSplitRecord>(32);

        void RecordShadowCasterSplit(int lightIndex, int splitIndex, in ShadowSplitData split, BatchCullingProjectionType projectionType)
        {
            m_ShadowCasterSplits.Add(new ShadowCasterSplitRecord
            {
                lightIndex = lightIndex,
                splitIndex = splitIndex,
                splitData = split,
                projectionType = projectionType
            });
        }

        void RecordAllocatedShadowCasterSplits(RenderContext renderContext)
        {
            if (renderContext == null || renderContext.lightContext == null)
            {
                return;
            }

            ShadowAllocator allocator = renderContext.lightContext.ShadowAllocator;
            if (allocator == null)
            {
                return;
            }

            int cascadeLight = allocator.CascadeVisibleLightIndex;
            if (cascadeLight >= 0)
            {
                for (int cascade = 0; cascade < allocator.CascadeAllocatedCount; ++cascade)
                {
                    FCascadeShadowSlice slice = allocator.CascadeSlices[cascade];
                    if (!slice.valid)
                    {
                        continue;
                    }

                    RecordShadowCasterSplit(cascadeLight, cascade, slice.splitData, BatchCullingProjectionType.Orthographic);
                }
            }

            for (int sliceIndex = 0; sliceIndex < allocator.LocalSliceCount; ++sliceIndex)
            {
                FLocalShadowSlice local = allocator.LocalSlices[sliceIndex];
                if (!local.valid)
                {
                    continue;
                }

                RecordShadowCasterSplit(local.visibleLightIndex, local.face, local.splitData, BatchCullingProjectionType.Perspective);
            }
        }

        void FlushShadowCasterCulling(ScriptableRenderContext context, in CullingResults cullingResults)
        {
            int recordCount = m_ShadowCasterSplits.Count;
            if (recordCount == 0)
            {
                return;
            }

            m_ShadowCasterSplits.Sort(CompareShadowCasterSplits);

            int visibleLightCount = cullingResults.visibleLights.Length;
            NativeArray<ShadowSplitData> splitBuffer = new NativeArray<ShadowSplitData>(recordCount, Allocator.Temp);
            NativeArray<LightShadowCasterCullingInfo> perLightInfos = new NativeArray<LightShadowCasterCullingInfo>(visibleLightCount, Allocator.Temp);

            int bufferIndex = 0;
            int recordIndex = 0;
            while (recordIndex < recordCount)
            {
                ShadowCasterSplitRecord first = m_ShadowCasterSplits[recordIndex];
                int rangeStart = bufferIndex;
                while (recordIndex < recordCount && m_ShadowCasterSplits[recordIndex].lightIndex == first.lightIndex)
                {
                    splitBuffer[bufferIndex++] = m_ShadowCasterSplits[recordIndex].splitData;
                    recordIndex++;
                }

                if ((uint)first.lightIndex < (uint)visibleLightCount)
                {
                    perLightInfos[first.lightIndex] = new LightShadowCasterCullingInfo
                    {
                        splitRange = new RangeInt(rangeStart, bufferIndex - rangeStart),
                        projectionType = first.projectionType
                    };
                }
            }

            ShadowCastersCullingInfos infos = default;
            infos.splitBuffer = splitBuffer;
            infos.perLightInfos = perLightInfos;
            context.CullShadowCasters(cullingResults, infos);

            splitBuffer.Dispose();
            perLightInfos.Dispose();
            m_ShadowCasterSplits.Clear();
        }

        static int CompareShadowCasterSplits(ShadowCasterSplitRecord a, ShadowCasterSplitRecord b)
        {
            int cmp = a.lightIndex.CompareTo(b.lightIndex);
            return cmp != 0 ? cmp : a.splitIndex.CompareTo(b.splitIndex);
        }
    }
}
