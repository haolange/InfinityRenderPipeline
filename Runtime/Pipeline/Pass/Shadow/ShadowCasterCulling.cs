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

        struct ShadowCasterSplitOrder : IComparer<ShadowCasterSplitRecord>
        {
            public int Compare(ShadowCasterSplitRecord a, ShadowCasterSplitRecord b)
            {
                int cmp = a.lightIndex.CompareTo(b.lightIndex);
                return cmp != 0 ? cmp : a.splitIndex.CompareTo(b.splitIndex);
            }
        }

        // Unity 6 CreateShadowRendererList is empty until CullShadowCasters runs with this camera's allocated splits.
        void CullShadowCasterSplits(in CameraRecordContext context)
        {
            ShadowAllocator allocator = renderContext.lightContext.ShadowAllocator;
            CullingResults cullingResults = context.cullingResults;
            var records = new NativeList<ShadowCasterSplitRecord>(allocator.ActiveCascadeCount + allocator.LocalSliceCount, Allocator.Temp);
            int cascadeLight = allocator.CascadeVisibleLightIndex;
            if (cascadeLight >= 0 && cullingResults.GetShadowCasterBounds(cascadeLight, out _))
            {
                for (int cascade = 0; cascade < allocator.ActiveCascadeCount; ++cascade)
                {
                    CascadeShadowSlice slice = allocator.CascadeSlices[cascade];
                    if (slice.valid)
                        records.Add(new ShadowCasterSplitRecord { lightIndex = cascadeLight, splitIndex = slice.cullingSplitIndex,
                            splitData = slice.splitData, projectionType = BatchCullingProjectionType.Orthographic });
                }
            }
            for (int sliceIndex = 0; sliceIndex < allocator.LocalSliceCount; ++sliceIndex)
            {
                LocalShadowSlice local = allocator.LocalSlices[sliceIndex];
                if (!local.cachedSampling && local.valid && cullingResults.GetShadowCasterBounds(local.visibleLightIndex, out _))
                    records.Add(new ShadowCasterSplitRecord { lightIndex = local.visibleLightIndex, splitIndex = local.face,
                        splitData = local.splitData, projectionType = local.projectionType });
            }
            int recordCount = records.Length;
            if (recordCount == 0)
            {
                records.Dispose();
                return;
            }

            records.Sort(new ShadowCasterSplitOrder());
            int visibleLightCount = cullingResults.visibleLights.Length;
            var splitBuffer = new NativeArray<ShadowSplitData>(recordCount, Allocator.Temp);
            var perLightInfos = new NativeArray<LightShadowCasterCullingInfo>(visibleLightCount, Allocator.Temp);
            int recordIndex = 0;
            while (recordIndex < recordCount)
            {
                ShadowCasterSplitRecord first = records[recordIndex];
                int rangeStart = recordIndex;
                while (recordIndex < recordCount && records[recordIndex].lightIndex == first.lightIndex)
                {
                    splitBuffer[recordIndex] = records[recordIndex].splitData;
                    recordIndex++;
                }
                perLightInfos[first.lightIndex] = new LightShadowCasterCullingInfo
                {
                    splitRange = new RangeInt(rangeStart, recordIndex - rangeStart),
                    projectionType = first.projectionType
                };
            }

            ShadowCastersCullingInfos infos = default;
            infos.splitBuffer = splitBuffer;
            infos.perLightInfos = perLightInfos;
            renderContext.scriptableRenderContext.CullShadowCasters(cullingResults, infos);
            splitBuffer.Dispose();
            perLightInfos.Dispose();
            records.Dispose();
        }
    }
}
