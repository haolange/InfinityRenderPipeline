using System;
using Unity.Collections;
using Unity.Mathematics;

namespace InfinityTech.Rendering.LightPipeline
{
    // Screen-space traced (contact) shadows for local lights. The trace writes one R32_UInt per pixel: bits 0..23 say
    // which local light is occluded, bits 24..31 hold the occlusion strength (intensity x distance fade), the HDRP
    // layout. Each contact-enabled local light gets a bit, stored in record.misc.z (-1 without one); when more than
    // MaxLights want one, the lights covering the most screen tiles win. The sun is traced separately (R8).
    public static class ContactShadowAllocation
    {
        public const int MaxLights = 24;
        public const uint LightMask = (1u << MaxLights) - 1u;

        // records: directional records first, then localCount local records in tileRects order. Returns the number of
        // bits assigned.
        public static int Assign(NativeList<FLightRecord> records, int directionalCount, NativeList<int4> tileRects, int localCount)
        {
            for (int i = 0; i < records.Length; ++i)
            {
                FLightRecord record = records[i];
                record.misc.z = -1;
                records[i] = record;
            }

            int candidateCount = 0;
            var candidates = new NativeArray<int>(math.max(1, localCount), Allocator.Temp);
            var areas = new NativeArray<int>(math.max(1, localCount), Allocator.Temp);
            for (int local = 0; local < localCount; ++local)
            {
                FLightRecord record = records[directionalCount + local];
                if ((record.flags & FLightRecordFlags.EnableContactShadow) == 0)
                {
                    continue;
                }
                int4 rect = tileRects[local];
                areas[candidateCount] = math.max(0, rect.z - rect.x + 1) * math.max(0, rect.w - rect.y + 1);
                candidates[candidateCount] = local;
                candidateCount++;
            }

            // Largest screen coverage first; lower index (nearer) breaks ties. Insertion sort: candidates are few.
            for (int i = 1; i < candidateCount; ++i)
            {
                int local = candidates[i];
                int area = areas[i];
                int j = i - 1;
                while (j >= 0 && (areas[j] < area || (areas[j] == area && candidates[j] > local)))
                {
                    candidates[j + 1] = candidates[j];
                    areas[j + 1] = areas[j];
                    --j;
                }
                candidates[j + 1] = local;
                areas[j + 1] = area;
            }

            int assigned = math.min(candidateCount, MaxLights);
            for (int bit = 0; bit < assigned; ++bit)
            {
                int index = directionalCount + candidates[bit];
                FLightRecord record = records[index];
                record.misc.z = bit;
                records[index] = record;
            }
            candidates.Dispose();
            areas.Dispose();
            return assigned;
        }
    }
}
