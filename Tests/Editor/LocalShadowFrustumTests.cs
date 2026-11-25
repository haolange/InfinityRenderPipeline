using NUnit.Framework;
using UnityEngine;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    // Local shadow frustums are built from the record's shape flags; every shape must put a receiver in front of the
    // emitter inside its tile's clip volume.
    public class LocalShadowFrustumTests
    {
        static LightRecord Record(int flags, Vector4 area, float radius = 0)
        {
            return new LightRecord
            {
                positionRange = new Vector4(1, 2, 3, 10),
                forward = new Vector4(0, 0, 1, 0.5f),
                right = new Vector4(1, 0, 0, 0),
                shape = new Vector4(radius, 0, 0, 0),
                area = area,
                flags = flags | LightRecordFlags.EnableShadow,
                visibleLightIndex = 0,
            };
        }

        static bool InsideClip(ShadowAllocator.LocalShadowViewProjection frustum, Vector3 positionWS)
        {
            Vector4 clip = frustum.projection * (frustum.view * new Vector4(positionWS.x, positionWS.y, positionWS.z, 1));
            if (clip.w <= 0) return false;
            Vector3 ndc = new Vector3(clip.x, clip.y, clip.z) / clip.w;
            return Mathf.Abs(ndc.x) <= 1 && Mathf.Abs(ndc.y) <= 1 && Mathf.Abs(ndc.z) <= 1;
        }

        [Test]
        public void PointFaces_EachContainsItsAxis()
        {
            LightRecord record = Record(0, Vector4.zero, 0.2f);
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
        public void SingleTileShapes_ContainForwardPoint()
        {
            Vector3 ahead = new Vector3(1, 2, 3) + new Vector3(0, 0, 5);
            Vector3 behind = new Vector3(1, 2, 3) - new Vector3(0, 0, 5);
            int[] flags = { LightRecordFlags.SpotCone, LightRecordFlags.SpotPyramid, LightRecordFlags.SpotBox, LightRecordFlags.Planar };
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
        public void BoxSpot_IsOrthographic()
        {
            var frustum = ShadowAllocator.LocalShadowFrustum(Record(LightRecordFlags.SpotBox, new Vector4(2, 1, 0, 0)), null, 0, 512, 3);
            Assert.IsTrue(frustum.orthographic);
            Assert.AreEqual(0.0f, frustum.near);
        }

        [Test]
        public void DiskAndDiskCone_UseShiftedApexAndEncloseEmitterPlane()
        {
            Vector3 origin = new Vector3(1, 2, 3);
            LightRecord disk = Record(LightRecordFlags.Planar, Vector4.zero, 0.75f);
            var diskFrustum = ShadowAllocator.LocalShadowFrustum(disk, null, 0, 512, 3);
            Assert.IsTrue(diskFrustum.valid);
            Assert.IsTrue(InsideClip(diskFrustum, origin + new Vector3(0.75f, 0, 0.05f)), "Disk frustum must cover rim near emitter.");
            Assert.IsFalse(InsideClip(diskFrustum, origin - new Vector3(0, 0, 0.05f)), "Disk frustum near plane starts at emitter.");

            float cosOuter = Mathf.Cos(45f * Mathf.Deg2Rad);
            LightRecord diskCone = Record(LightRecordFlags.Planar | LightRecordFlags.SpotCone, Vector4.zero, 0.75f);
            diskCone.forward.w = cosOuter;
            diskCone.shape.y = LightShapeUtility.DiskConeApexOffset(0.75f, 90f);
            var diskConeFrustum = ShadowAllocator.LocalShadowFrustum(diskCone, null, 0, 512, 3);
            Assert.IsTrue(diskConeFrustum.valid);
            Assert.AreEqual(0.75f, diskConeFrustum.near, 1e-4f);
            Assert.AreEqual(10.75f, diskConeFrustum.far, 1e-4f);
            Assert.IsTrue(InsideClip(diskConeFrustum, origin + new Vector3(0.75f, 0, 0.05f)), "DiskCone frustum must enclose disk rim.");
            Assert.IsTrue(InsideClip(diskConeFrustum, origin + new Vector3(2.75f, 0, 2.0f)), "DiskCone frustum covers rim-origin beam (r + z*tan45).");
            Assert.IsFalse(InsideClip(diskConeFrustum, origin - new Vector3(0, 0, 0.1f)), "DiskCone frustum clips behind disk.");
        }

        [Test]
        public void PackEmitterShadowShape_EncodesRectAspectLineTubeAndPyramidScale()
        {
            LightRecord rect = Record(LightRecordFlags.Planar, new Vector4(3.2f, 0.4f, 0, 0));
            var rectFrustum = ShadowAllocator.LocalShadowFrustum(rect, null, 0, 512, 3);
            Assert.Greater(rectFrustum.apexOffset, 0.5f);
            ShadowAllocator.PackEmitterShadowShape(rect, rectFrustum, out Vector4 rectOrigin, out Vector4 rectU, out Vector4 rectV);
            Assert.AreEqual(ShadowPCSSShapeMode.OrientedRect, rectU.w);
            Assert.AreEqual(1.6f, rectU.z, 1e-4f);
            Assert.AreEqual(0.2f, rectV.z, 1e-4f);
            Assert.AreEqual(1.6f, rectOrigin.w, 1e-4f);

            // Tube along world X: perpendicular +Z face sees full halfLength + radius; head-on +X face sees radius only.
            LightRecord line = Record(0, Vector4.zero, 0.0f);
            line.shape.z = 2.0f;
            var facePosZ = ShadowAllocator.LocalShadowFrustum(line, null, 4, 256, 3);
            ShadowAllocator.PackEmitterShadowShape(line, facePosZ, out Vector4 lineSizeZ, out Vector4 lineUZ, out Vector4 lineVZ);
            Assert.AreEqual(ShadowPCSSShapeMode.OrientedCapsule, lineUZ.w);
            Assert.AreEqual(1.0f, lineUZ.z, 1e-4f);
            Assert.AreEqual(0.0f, lineVZ.z, 1e-4f);
            Assert.AreEqual(1.0f, lineSizeZ.w, 1e-4f, "Line must receive nonzero PCSS emitter size.");

            var facePosX = ShadowAllocator.LocalShadowFrustum(line, null, 0, 256, 3);
            ShadowAllocator.PackEmitterShadowShape(line, facePosX, out _, out Vector4 lineUX, out _);
            Assert.AreEqual(0.0f, lineUX.z, 1e-4f, "Head-on face sees zero projected line length.");

            // Tube adds transverse radius to line length.
            LightRecord tube = Record(0, Vector4.zero, 0.25f);
            tube.shape.z = 2.0f;
            ShadowAllocator.PackEmitterShadowShape(tube, facePosZ, out _, out Vector4 tubeU, out Vector4 tubeV);
            Assert.AreEqual(1.0f, tubeU.z, 1e-4f);
            Assert.AreEqual(0.25f, tubeV.z, 1e-4f);

            // SpotPyramid with non-unit aspect reports m11/m00 texel scale.
            LightRecord pyramid = Record(LightRecordFlags.SpotPyramid, new Vector4(0.8f, 0.4f, 0, 0), 0.1f);
            var pyramidFrustum = ShadowAllocator.LocalShadowFrustum(pyramid, null, 0, 256, 3);
            ShadowAllocator.PackEmitterShadowShape(pyramid, pyramidFrustum, out _, out _, out Vector4 pyrV);
            Assert.AreEqual(2.0f, pyrV.w, 1e-3f);

            // Diagonal 45-deg Line projects diagonal UV axes onto +Z cube face.
            LightRecord rotatedLine = Record(0, Vector4.zero, 0.15f);
            rotatedLine.shape.z = 2.0f;
            rotatedLine.right = new Vector4(0.70710677f, 0.70710677f, 0, 0);
            ShadowAllocator.PackEmitterShadowShape(rotatedLine, facePosZ, out _, out Vector4 rotLineU, out Vector4 rotLineV);
            Assert.AreEqual(0.7071f, rotLineU.x, 1e-3f);
            Assert.AreEqual(0.7071f, rotLineU.y, 1e-3f);
            Assert.AreEqual(-0.7071f, rotLineV.x, 1e-3f);
            Assert.AreEqual(0.7071f, rotLineV.y, 1e-3f);
            Assert.AreEqual(1.0f, rotLineU.z, 1e-3f);
        }

        [Test]
        public void QuantifySingleViewpoint_NearEmitterPenumbraAndOffAxisCoverage()
        {
            // Document single-viewpoint virtual-apex error for a 2.0 x 0.5 Rect light (halfExtent = 1.0308m, apexOffset = 0.595m).
            LightRecord rect = Record(LightRecordFlags.Planar, new Vector4(2.0f, 0.5f, 0, 0));
            var frustum = ShadowAllocator.LocalShadowFrustum(rect, null, 0, 512, 3);
            float dBlocker = 0.15f;
            float dReceiver = 1.50f;
            float halfW = 1.0f;

            float analyticPenumbra = 2.0f * halfW * (dReceiver - dBlocker) / dBlocker;
            float virtualApexDepthErrorRatio = ((dReceiver - dBlocker) / (frustum.apexOffset + dBlocker)) / ((dReceiver - dBlocker) / dBlocker);
            float emitterBaselineRatio = ((dReceiver - dBlocker) / ((frustum.apexOffset + dBlocker) - frustum.apexOffset)) / ((dReceiver - dBlocker) / dBlocker);

            Assert.Less(virtualApexDepthErrorRatio, 0.22f, "Virtual-apex depth under-estimates near-emitter penumbra by ~5x.");
            Assert.AreEqual(1.0f, emitterBaselineRatio, 1e-4f, "Subtracting apexOffset restores exact emitter-plane penumbra ratio.");
            Assert.Greater(analyticPenumbra, 15.0f);

            // Coverage limit outside 120-deg planar cone (theta > 60 deg): sin^2(60 deg) = 0.75 of hemisphere Lambertian flux stays inside cone;
            // up to 25% outer grazing flux sits beyond +/- 60 deg (captured by emitter edge rays up to ~120 deg from far corners).
            float cos60 = Mathf.Cos(ShadowAllocator.PlanarShadowFieldOfView * 0.5f * Mathf.Deg2Rad);
            float inConeLambertianFraction = 1.0f - cos60 * cos60;
            Assert.AreEqual(0.75f, inConeLambertianFraction, 1e-4f);
        }
    }
}
