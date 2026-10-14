using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class ContactShadowAllocationTests
    {
        static FLightRecord Light(bool contact, bool directional = false)
        {
            int flags = contact ? FLightRecordFlags.EnableContactShadow : 0;
            if (directional) flags |= FLightRecordFlags.Directional;
            return new FLightRecord { flags = flags, misc = new UnityEngine.Vector4(1, 0, 7, 0) };
        }

        [Test]
        [Category("Unverified-0.5")]
        public void Bits_GoToContactLights_LargestScreenCoverageFirst()
        {
            var records = new NativeList<FLightRecord>(Allocator.Temp);
            var rects = new NativeList<int4>(Allocator.Temp);
            try
            {
                records.Add(Light(true, directional: true));                  // directional: never a bit
                records.Add(Light(true)); rects.Add(new int4(0, 0, 0, 0));      // 1 tile
                records.Add(Light(false)); rects.Add(new int4(0, 0, 9, 9));     // no contact
                records.Add(Light(true)); rects.Add(new int4(0, 0, 3, 3));      // 16 tiles
                int assigned = ContactShadowAllocation.Assign(records, 1, rects, 3);
                Assert.AreEqual(2, assigned);
                Assert.AreEqual(-1, records[0].misc.z);
                Assert.AreEqual(1, records[1].misc.z, "smaller light gets the second bit");
                Assert.AreEqual(-1, records[2].misc.z);
                Assert.AreEqual(0, records[3].misc.z, "largest coverage first");
                Assert.AreEqual(1, records[1].misc.x, "other misc fields are kept");
            }
            finally
            {
                records.Dispose();
                rects.Dispose();
            }
        }

        [Test]
        [Category("Unverified-0.5")]
        public void Bits_AreCappedAt24()
        {
            var records = new NativeList<FLightRecord>(Allocator.Temp);
            var rects = new NativeList<int4>(Allocator.Temp);
            try
            {
                for (int i = 0; i < 30; ++i)
                {
                    records.Add(Light(true));
                    rects.Add(new int4(0, 0, i, 0));
                }
                Assert.AreEqual(ContactShadowAllocation.MaxLights, ContactShadowAllocation.Assign(records, 0, rects, 30));
                int unassigned = 0;
                for (int i = 0; i < 30; ++i)
                {
                    if (records[i].misc.z < 0) unassigned++;
                }
                Assert.AreEqual(6, unassigned);
                Assert.AreEqual(-1, records[0].misc.z, "the smallest lights lose");
                Assert.AreEqual(0, records[29].misc.z);
            }
            finally
            {
                records.Dispose();
                rects.Dispose();
            }
        }
    }
}
