using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.GlobalIllumination;
using UnityEngine.Rendering;
using InfinityTech.Component;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.Pipeline.Editor;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class LightIntensityUnitTests
    {
        [Test]
        public void CanonicalAndSupportedUnits_MatchShapeSemantics()
        {
            Assert.AreEqual(LightUnit.Lux, LightIntensityUtility.GetCanonicalUnit(LightSourceShape.DefaultDirectional()));
            CollectionAssert.AreEqual(new[] { LightUnit.Lux },
                LightIntensityUtility.GetSupportedUnits(LightSourceShape.DefaultDirectional()).ToArray());

            var box = LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.ParallelBox);
            Assert.AreEqual(LightUnit.Lux, LightIntensityUtility.GetCanonicalUnit(box));
            CollectionAssert.AreEqual(new[] { LightUnit.Lux },
                LightIntensityUtility.GetSupportedUnits(box).ToArray());

            var point = LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional);
            Assert.AreEqual(LightUnit.Candela, LightIntensityUtility.GetCanonicalUnit(point));
            CollectionAssert.AreEqual(new[] { LightUnit.Lumen, LightUnit.Candela, LightUnit.Lux },
                LightIntensityUtility.GetSupportedUnits(point).ToArray());

            var sphere = point;
            sphere.sourceRadius = 0.25f;
            Assert.AreEqual(LightUnit.Candela, LightIntensityUtility.GetCanonicalUnit(sphere));
            CollectionAssert.AreEqual(new[] { LightUnit.Lumen, LightUnit.Candela, LightUnit.Nits, LightUnit.Lux },
                LightIntensityUtility.GetSupportedUnits(sphere).ToArray());

            var line = point;
            line.sourceLength = 1.5f;
            Assert.AreEqual(LightUnit.Candela, LightIntensityUtility.GetCanonicalUnit(line));
            CollectionAssert.AreEqual(new[] { LightUnit.Lumen, LightUnit.Candela, LightUnit.Lux },
                LightIntensityUtility.GetSupportedUnits(line).ToArray());

            var tube = line;
            tube.sourceRadius = 0.1f;
            Assert.AreEqual(LightUnit.Candela, LightIntensityUtility.GetCanonicalUnit(tube));
            CollectionAssert.AreEqual(new[] { LightUnit.Lumen, LightUnit.Candela, LightUnit.Nits, LightUnit.Lux },
                LightIntensityUtility.GetSupportedUnits(tube).ToArray());

            var cone = LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Cone);
            Assert.AreEqual(LightUnit.Candela, LightIntensityUtility.GetCanonicalUnit(cone));
            CollectionAssert.AreEqual(new[] { LightUnit.Lumen, LightUnit.Candela, LightUnit.Lux },
                LightIntensityUtility.GetSupportedUnits(cone).ToArray());

            var pyramid = LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Pyramid);
            Assert.AreEqual(LightUnit.Candela, LightIntensityUtility.GetCanonicalUnit(pyramid));
            CollectionAssert.AreEqual(new[] { LightUnit.Lumen, LightUnit.Candela, LightUnit.Lux },
                LightIntensityUtility.GetSupportedUnits(pyramid).ToArray());

            var rect = LightSourceShape.DefaultLocal(ELocalLightEmitter.Rectangle, ELocalLightDistribution.Hemisphere);
            Assert.AreEqual(LightUnit.Nits, LightIntensityUtility.GetCanonicalUnit(rect));
            CollectionAssert.AreEqual(new[] { LightUnit.Lumen, LightUnit.Nits },
                LightIntensityUtility.GetSupportedUnits(rect).ToArray());

            var disk = LightSourceShape.DefaultLocal(ELocalLightEmitter.Disk, ELocalLightDistribution.Hemisphere);
            Assert.AreEqual(LightUnit.Nits, LightIntensityUtility.GetCanonicalUnit(disk));
            CollectionAssert.AreEqual(new[] { LightUnit.Lumen, LightUnit.Nits },
                LightIntensityUtility.GetSupportedUnits(disk).ToArray());

            var diskCone = LightSourceShape.DefaultLocal(ELocalLightEmitter.Disk, ELocalLightDistribution.Cone);
            Assert.AreEqual(LightUnit.Nits, LightIntensityUtility.GetCanonicalUnit(diskCone));
            CollectionAssert.AreEqual(new[] { LightUnit.Lumen, LightUnit.Nits },
                LightIntensityUtility.GetSupportedUnits(diskCone).ToArray());
        }

        [Test]
        public void PhotometricConversions_RoundTripAndRespectLimits()
        {
            var rect = LightSourceShape.DefaultLocal(ELocalLightEmitter.Rectangle, ELocalLightDistribution.Hemisphere);
            rect.sourceWidth = 2.0f;
            rect.sourceHeight = 0.5f;
            float rectNits = 100.0f;
            float rectLumens = LightIntensityUtility.ToDisplayIntensity(rectNits, rect, LightUnit.Lumen, 0f, 60f, 1f);
            Assert.AreEqual(Mathf.PI * rectNits, rectLumens, 1e-4f);
            Assert.AreEqual(rectNits, LightIntensityUtility.ToCanonicalIntensity(rectLumens, rect, LightUnit.Lumen, 0f, 60f, 1f), 1e-4f);

            var disk = LightSourceShape.DefaultLocal(ELocalLightEmitter.Disk, ELocalLightDistribution.Hemisphere);
            disk.sourceRadius = 0.5f;
            float diskNits = 80.0f;
            float diskLumens = LightIntensityUtility.ToDisplayIntensity(diskNits, disk, LightUnit.Lumen, 0f, 60f, 1f);
            Assert.AreEqual(Mathf.PI * Mathf.PI * 0.25f * diskNits, diskLumens, 1e-4f);

            // Disk + Cone approaches Disk flux when inner = outer = 180 degrees.
            var diskCone = LightSourceShape.DefaultLocal(ELocalLightEmitter.Disk, ELocalLightDistribution.Cone);
            diskCone.sourceRadius = 0.5f;
            float wideDiskConeLumens = LightIntensityUtility.ToDisplayIntensity(diskNits, diskCone, LightUnit.Lumen, 180f, 180f, 1f);
            Assert.AreEqual(diskLumens, wideDiskConeLumens, 1e-3f);

            // Tighter inner angle reduces cone lumen output for the same on-axis candela.
            var cone = LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Cone);
            float hardConeLumens = LightIntensityUtility.ToDisplayIntensity(100f, cone, LightUnit.Lumen, 60f, 60f, 1f);
            float softConeLumens = LightIntensityUtility.ToDisplayIntensity(100f, cone, LightUnit.Lumen, 20f, 60f, 1f);
            Assert.AreEqual(100f * 2f * Mathf.PI * (1f - Mathf.Cos(30f * Mathf.Deg2Rad)), hardConeLumens, 1e-3f);
            Assert.Less(softConeLumens, hardConeLumens);

            // Pyramid exact spherical solid angle matches cube face (2*pi/3) at 90 degrees, aspect 1,
            // and stays invariant under reciprocal aspect ratios (2 vs 0.5).
            var pyramid = LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Pyramid);
            pyramid.pyramidAspect = 1.0f;
            float pyramidLumens = LightIntensityUtility.ToDisplayIntensity(100f, pyramid, LightUnit.Lumen, 0f, 90f, 1f);
            Assert.AreEqual(100f * (2.0f * Mathf.PI / 3.0f), pyramidLumens, 1e-3f);
            var widePyramid = pyramid; widePyramid.pyramidAspect = 2.0f;
            var tallPyramid = pyramid; tallPyramid.pyramidAspect = 0.5f;
            Assert.AreEqual(
                LightIntensityUtility.ToDisplayIntensity(100f, widePyramid, LightUnit.Lumen, 0f, 90f, 1f),
                LightIntensityUtility.ToDisplayIntensity(100f, tallPyramid, LightUnit.Lumen, 0f, 90f, 1f),
                1e-4f);
            Assert.Less(LightIntensityUtility.ToDisplayIntensity(100f, widePyramid, LightUnit.Lumen, 0f, 90f, 1f), pyramidLumens);

            // Intermediate inner/outer angle Disk + Cone sits strictly between hard inner cone and hard outer cone.
            var midDiskCone = LightSourceShape.DefaultLocal(ELocalLightEmitter.Disk, ELocalLightDistribution.Cone);
            midDiskCone.sourceRadius = 0.5f;
            float innerOnly = LightIntensityUtility.ToDisplayIntensity(diskNits, midDiskCone, LightUnit.Lumen, 30f, 30f, 1f);
            float softEdge = LightIntensityUtility.ToDisplayIntensity(diskNits, midDiskCone, LightUnit.Lumen, 30f, 60f, 1f);
            float outerOnly = LightIntensityUtility.ToDisplayIntensity(diskNits, midDiskCone, LightUnit.Lumen, 60f, 60f, 1f);
            Assert.Greater(softEdge, innerOnly);
            Assert.Less(softEdge, outerOnly);

            // Small-factor emitters (< 1e-6f) round-trip Lumen <-> canonical without asymmetry loss.
            var tinyDiskCone = midDiskCone;
            tinyDiskCone.sourceRadius = 0.02f;
            float tinyFlux = LightIntensityUtility.ToDisplayIntensity(150f, tinyDiskCone, LightUnit.Lumen, 1f, 1f, 1f);
            Assert.AreEqual(150f, LightIntensityUtility.ToCanonicalIntensity(tinyFlux, tinyDiskCone, LightUnit.Lumen, 1f, 1f, 1f), 1e-3f);

            // Unsupported unit falls back without scaling; cross-unit Candela/Lux -> Nits on Rect converts via area.
            Assert.AreEqual(100f, LightIntensityUtility.ToDisplayIntensity(100f, rect, LightUnit.Candela, 0f, 60f, 1f), 1e-5f);
            Assert.AreEqual(100f, LightIntensityUtility.ToCanonicalIntensity(100f, rect, LightUnit.Candela, 0f, 60f, 1f), 1e-5f);
            Assert.AreEqual(40f / (2f * 2f) * (2f * 2f) / 1f, LightIntensityUtility.ToCanonicalIntensity(10f, rect, LightUnit.Lux, 0f, 60f, 2f), 1e-4f);

            // Tube projected area converges to Sphere as length -> 0.
            var sphere = LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional);
            sphere.sourceRadius = 0.3f;
            var shortTube = sphere;
            shortTube.sourceLength = 1e-5f;
            Assert.AreEqual(
                LightIntensityUtility.ToDisplayIntensity(50f, sphere, LightUnit.Nits, 0f, 60f, 1f),
                LightIntensityUtility.ToDisplayIntensity(50f, shortTube, LightUnit.Nits, 0f, 60f, 1f),
                1e-2f);

            // Lux distance scaling on Point.
            float luxAt3m = LightIntensityUtility.ToDisplayIntensity(90f, sphere, LightUnit.Lux, 0f, 60f, 3f);
            Assert.AreEqual(10f, luxAt3m, 1e-5f);
            Assert.AreEqual(90f, LightIntensityUtility.ToCanonicalIntensity(10f, sphere, LightUnit.Lux, 0f, 60f, 3f), 1e-5f);
        }

        [Test]
        public void ResolveDisplayUnit_MapsUnsupportedDirectionalUnitWithoutMutatingLight()
        {
            var go = new GameObject("DirectionalDefault");
            try
            {
                Light light = go.AddComponent<Light>();
                light.type = UnityEngine.LightType.Directional;
                light.lightUnit = LightUnit.Candela;
                light.intensity = 3.14f;
                string before = EditorJsonUtility.ToJson(light);

                LightSourceShape shape = LightShapeUtility.FromUnityLight(light);
                LightUnit resolved = LightIntensityUtility.ResolveDisplayUnit(light, shape);

                Assert.AreEqual(LightUnit.Lux, resolved);
                Assert.AreEqual(LightUnit.Candela, light.lightUnit, "Resolving display unit must not mutate the Light.");
                Assert.AreEqual(before, EditorJsonUtility.ToJson(light));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SwitchingDisplayUnit_PreservesCanonicalRadianceInRealtimeAndLightmapper()
        {
            var go = new GameObject("UnitSwitchLight");
            try
            {
                Light light = go.AddComponent<Light>();
                light.color = Color.white;
                light.intensity = 120f;
                light.range = 10f;
                var data = go.AddComponent<InfinityAdditionalLightData>();
                var rect = LightSourceShape.DefaultLocal(ELocalLightEmitter.Rectangle, ELocalLightDistribution.Hemisphere);
                rect.sourceWidth = 2f;
                rect.sourceHeight = 0.5f;
                data.SetShape(rect);

                light.lightUnit = LightUnit.Nits;
                Assert.IsTrue(LightRecordPack.TryPack(VisibleLightTestUtility.FromLight(light), light, data, data.shape, 0, Vector3.zero, out LightRecord recordNits));
                using var giNits = new Unity.Collections.NativeArray<LightDataGI>(1, Unity.Collections.Allocator.Temp);
                InfinityLightmapperDelegate.Delegate(new[] { light }, giNits);

                light.lightUnit = LightUnit.Lumen;
                Assert.AreEqual(120f, light.intensity, 1e-5f);
                Assert.IsTrue(LightRecordPack.TryPack(VisibleLightTestUtility.FromLight(light), light, data, data.shape, 0, Vector3.zero, out LightRecord recordLumens));
                using var giLumens = new Unity.Collections.NativeArray<LightDataGI>(1, Unity.Collections.Allocator.Temp);
                InfinityLightmapperDelegate.Delegate(new[] { light }, giLumens);

                Assert.AreEqual(recordNits.radiance, recordLumens.radiance);
                Assert.AreEqual(giNits[0].color.intensity, giLumens[0].color.intensity, 1e-5f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void IntensityEditModes_ScaleCanonicalIntensityPredictably()
        {
            var smallRect = LightSourceShape.DefaultLocal(ELocalLightEmitter.Rectangle, ELocalLightDistribution.Hemisphere);
            smallRect.sourceWidth = 1f;
            smallRect.sourceHeight = 1f;
            var wideRect = smallRect;
            wideRect.sourceWidth = 2f;

            // PhysicallyBased preserves Rect Nits across resize; PreserveFlux halves Nits when area doubles; Artistic leaves raw value.
            Assert.AreEqual(100f, LightIntensityUtility.AdjustIntensityForShapeEdit(
                100f, smallRect, wideRect, 0f, 60f, 0f, 60f, ELightIntensityEditMode.PhysicallyBased), 1e-4f);
            Assert.AreEqual(50f, LightIntensityUtility.AdjustIntensityForShapeEdit(
                100f, smallRect, wideRect, 0f, 60f, 0f, 60f, ELightIntensityEditMode.PreserveFlux), 1e-4f);
            Assert.AreEqual(100f, LightIntensityUtility.AdjustIntensityForShapeEdit(
                100f, smallRect, wideRect, 0f, 60f, 0f, 60f, ELightIntensityEditMode.Artistic), 1e-4f);

            // Sphere radius doubling under PhysicallyBased quadruples candela to preserve surface Nits; under PreserveFlux keeps 4*pi*I constant.
            var smallSphere = LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional);
            smallSphere.sourceRadius = 0.25f;
            var largeSphere = smallSphere;
            largeSphere.sourceRadius = 0.5f;
            Assert.AreEqual(400f, LightIntensityUtility.AdjustIntensityForShapeEdit(
                100f, smallSphere, largeSphere, 0f, 60f, 0f, 60f, ELightIntensityEditMode.PhysicallyBased), 1e-3f);
            Assert.AreEqual(100f, LightIntensityUtility.AdjustIntensityForShapeEdit(
                100f, smallSphere, largeSphere, 0f, 60f, 0f, 60f, ELightIntensityEditMode.PreserveFlux), 1e-4f);

            // Spot cone angle widening under PhysicallyBased preserves on-axis Candela; PreserveFlux reduces Candela to preserve Lumens.
            var cone = LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Cone);
            Assert.AreEqual(100f, LightIntensityUtility.AdjustIntensityForShapeEdit(
                100f, cone, cone, 60f, 60f, 90f, 90f, ELightIntensityEditMode.PhysicallyBased), 1e-4f);
            float expectedNarrowToWide = 100f * LightIntensityUtility.GetSpotConeSolidAngle(60f, 60f)
                / LightIntensityUtility.GetSpotConeSolidAngle(90f, 90f);
            Assert.AreEqual(expectedNarrowToWide, LightIntensityUtility.AdjustIntensityForShapeEdit(
                100f, cone, cone, 60f, 60f, 90f, 90f, ELightIntensityEditMode.PreserveFlux), 1e-3f);

            // Transitioning between zero-area Point and finite-area Sphere/Line/Tube never produces NaN/0/Inf.
            var point = LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional);
            var line = point; line.sourceLength = 2f;
            var tube = line; tube.sourceRadius = 0.25f;
            Assert.AreEqual(100f, LightIntensityUtility.AdjustIntensityForShapeEdit(
                100f, point, smallSphere, 0f, 60f, 0f, 60f, ELightIntensityEditMode.PhysicallyBased), 1e-4f);
            Assert.AreEqual(100f, LightIntensityUtility.AdjustIntensityForShapeEdit(
                100f, smallSphere, point, 0f, 60f, 0f, 60f, ELightIntensityEditMode.PhysicallyBased), 1e-4f);
            Assert.AreEqual(100f, LightIntensityUtility.AdjustIntensityForShapeEdit(
                100f, line, tube, 0f, 60f, 0f, 60f, ELightIntensityEditMode.PhysicallyBased), 1e-4f);

            // Crossing Point (Candela) -> Rect (Nits) preserves luminous flux under PhysicallyBased.
            float pointToRect = LightIntensityUtility.AdjustIntensityForShapeEdit(
                100f, point, smallRect, 0f, 60f, 0f, 60f, ELightIntensityEditMode.PhysicallyBased);
            Assert.AreEqual(400f, pointToRect, 1e-3f);

            // Transient zero rect or extreme small ratio never divides by zero or overflows to Infinity.
            var zeroRect = smallRect; zeroRect.sourceWidth = 0f;
            Assert.AreEqual(100f, LightIntensityUtility.AdjustIntensityForShapeEdit(
                100f, smallRect, zeroRect, 0f, 60f, 0f, 60f, ELightIntensityEditMode.PreserveFlux), 1e-4f);
            var subnormalRect = smallRect; subnormalRect.sourceWidth = 1e-19f; subnormalRect.sourceHeight = 1e-19f;
            float guarded = LightIntensityUtility.AdjustIntensityForShapeEdit(
                1e30f, smallRect, subnormalRect, 0f, 60f, 0f, 60f, ELightIntensityEditMode.PreserveFlux);
            Assert.IsTrue(float.IsFinite(guarded));
            Assert.IsTrue(float.IsNaN(LightIntensityUtility.AdjustIntensityForShapeEdit(
                float.NaN, smallRect, wideRect, 0f, 60f, 0f, 60f, ELightIntensityEditMode.PreserveFlux)));

            // Directional & ParallelBox remain invariant across PhysicallyBased/PreserveFlux edits.
            var sun = LightSourceShape.DefaultDirectional();
            var box = LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.ParallelBox);
            Assert.AreEqual(3.14f, LightIntensityUtility.AdjustIntensityForShapeEdit(
                3.14f, sun, sun, 0f, 60f, 0f, 60f, ELightIntensityEditMode.PreserveFlux), 1e-5f);
            Assert.AreEqual(15f, LightIntensityUtility.AdjustIntensityForShapeEdit(
                15f, box, box, 0f, 60f, 0f, 60f, ELightIntensityEditMode.PhysicallyBased), 1e-5f);
        }

        [Test]
        public void BakeableShapes_MatchLightmapperAndRealtimeRadiance()
        {
            var go = new GameObject("BakeParity");
            try
            {
                Light light = go.AddComponent<Light>();
                light.color = new Color(0.8f, 0.5f, 0.2f, 1f);
                light.intensity = 25f;
                light.range = 12f;
                light.spotAngle = 70f;
                light.innerSpotAngle = 25f;
                var data = go.AddComponent<InfinityAdditionalLightData>();

                LightSourceShape[] shapes =
                {
                    LightSourceShape.DefaultDirectional(),
                    LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional),
                    LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Cone),
                    LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Pyramid),
                    LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.ParallelBox),
                    LightSourceShape.DefaultLocal(ELocalLightEmitter.Rectangle, ELocalLightDistribution.Hemisphere),
                    LightSourceShape.DefaultLocal(ELocalLightEmitter.Disk, ELocalLightDistribution.Hemisphere),
                };

                using var output = new Unity.Collections.NativeArray<LightDataGI>(1, Unity.Collections.Allocator.Temp);
                foreach (LightSourceShape shape in shapes)
                {
                    data.SetShape(shape);
                    Assert.IsTrue(LightRecordPack.TryPack(VisibleLightTestUtility.FromLight(light), light, data, shape, 0, Vector3.zero, out LightRecord record));
                    InfinityLightmapperDelegate.Delegate(new[] { light }, output);
                    LinearColor gi = output[0].color;
                    Assert.AreEqual(gi.red * gi.intensity, record.radiance.x, 1e-4f, shape.emitter + " " + shape.distribution + " R");
                    Assert.AreEqual(gi.green * gi.intensity, record.radiance.y, 1e-4f, shape.emitter + " " + shape.distribution + " G");
                    Assert.AreEqual(gi.blue * gi.intensity, record.radiance.z, 1e-4f, shape.emitter + " " + shape.distribution + " B");
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
