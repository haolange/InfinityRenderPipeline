using NUnit.Framework;
using UnityEngine;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    // Local shadow frustums are built from the record's shape flags; every shape must put a receiver in front of the
    // emitter inside its tile's clip volume.
    public class LocalShadowFrustumTests
    {
        static FLightRecord Record(int flags, Vector4 area, float radius = 0)
        {
            return new FLightRecord
            {
                positionRange = new Vector4(1, 2, 3, 10),
                forward = new Vector4(0, 0, 1, 0.5f),
                right = new Vector4(1, 0, 0, 0),
                shape = new Vector4(radius, 0, 0, 0),
                area = area,
                flags = flags | FLightRecordFlags.EnableShadow,
                visibleLightIndex = 0,
            };
        }

        static bool InsideClip(ShadowAllocator.FLocalShadowFrustum frustum, Vector3 positionWS)
        {
            Vector4 clip = frustum.projection * (frustum.view * new Vector4(positionWS.x, positionWS.y, positionWS.z, 1));
            if (clip.w <= 0) return false;
            Vector3 ndc = new Vector3(clip.x, clip.y, clip.z) / clip.w;
            return Mathf.Abs(ndc.x) <= 1 && Mathf.Abs(ndc.y) <= 1 && Mathf.Abs(ndc.z) <= 1;
        }

        [Test]
        [Category("Unverified_0_5")]
        public void PointFaces_EachContainsItsAxis()
        {
            FLightRecord record = Record(0, Vector4.zero, 0.2f);
            Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            Vector3 center = new Vector3(1, 2, 3);
            for (int face = 0; face < 6; ++face)
            {
                var frustum = ShadowAllocator.LocalShadowFrustum(record, null, face, 512, 3);
                Assert.IsTrue(frustum.valid);
                Assert.IsTrue(InsideClip(frustum, center + axes[face] * 5), $"face {face} misses its own axis");
                Assert.IsFalse(InsideClip(frustum, center - axes[face] * 5), $"face {face} sees the opposite axis");
            }
        }

        [Test]
        [Category("Unverified_0_5")]
        public void SingleTileShapes_ContainForwardPoint()
        {
            Vector3 ahead = new Vector3(1, 2, 3) + new Vector3(0, 0, 5);
            Vector3 behind = new Vector3(1, 2, 3) - new Vector3(0, 0, 5);
            int[] flags = { FLightRecordFlags.SpotCone, FLightRecordFlags.SpotPyramid, FLightRecordFlags.SpotBox, FLightRecordFlags.Planar };
            Vector4[] areas = { Vector4.zero, new Vector4(0.5f, 0.3f, 0, 0), new Vector4(2, 1, 0, 0), new Vector4(1, 0.5f, 0, 0) };
            for (int i = 0; i < flags.Length; ++i)
            {
                var frustum = ShadowAllocator.LocalShadowFrustum(Record(flags[i], areas[i]), null, 0, 512, 3);
                Assert.IsTrue(frustum.valid);
                Assert.IsTrue(InsideClip(frustum, ahead), $"shape {flags[i]} misses the forward point");
                Assert.IsFalse(InsideClip(frustum, behind), $"shape {flags[i]} sees behind the light");
            }
        }

        [Test]
        [Category("Unverified_0_5")]
        public void BoxSpot_IsOrthographic()
        {
            var frustum = ShadowAllocator.LocalShadowFrustum(Record(FLightRecordFlags.SpotBox, new Vector4(2, 1, 0, 0)), null, 0, 512, 3);
            Assert.IsTrue(frustum.orthographic);
            Assert.AreEqual(0.0f, frustum.near);
        }
    }
}
