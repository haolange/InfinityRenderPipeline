using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Component;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class LightRecordTests
    {
        [Test]
        public void PackRadiance_AppliesIntensityOnce_AlphaIsOne()
        {
            Vector4 radiance = FLightRecordPack.Radiance(new Color(0.5f, 0.25f, 0.1f, 1.0f), 4.0f);
            Assert.AreEqual(2.0f, radiance.x, 1e-4f);
            Assert.AreEqual(1.0f, radiance.y, 1e-4f);
            Assert.AreEqual(0.4f, radiance.z, 1e-4f);
            Assert.AreEqual(1.0f, radiance.w, 1e-4f);
        }

        [Test]
        public void PackFromUnityLight_UsesLinearUnityColorTimesIntensityOnce()
        {
            GameObject go = new GameObject("LightRecordTest");
            try
            {
                Light light = go.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = new Color(0.5f, 0.25f, 0.1f, 1.0f);
                light.intensity = 4.0f;
                light.shadowStrength = 0.35f;
                InfinityAdditionalLightData ext = go.AddComponent<InfinityAdditionalLightData>();

                FLightShape shape = FLightRecordPack.ResolveShape(light, ext);
                Assert.AreEqual(EInfinityLightType.Directional, shape.type);
                Assert.IsTrue(FLightRecordPack.TryPack(light, ext, shape, 0, Vector3.zero, out FLightRecord record));
                Assert.AreEqual(light.color.linear.r * light.intensity, record.radiance.x, 1e-4f);
                Assert.AreEqual(1.0f, record.radiance.w, 1e-4f);
                Assert.IsTrue(record.IsDirectional);
                Assert.AreEqual(0.35f, record.misc.x, 1e-5f);
                Assert.AreEqual(-1, record.cookieIndex);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("Unverified_0_5")]
        public void RealtimeRadiance_MatchesUnityLightmapperExtraction(bool useTemperature)
        {
            var go = new GameObject("LightRadianceOracle");
            bool previousLinear = GraphicsSettings.lightsUseLinearIntensity;
            bool previousTemperature = GraphicsSettings.lightsUseColorTemperature;
            try
            {
                GraphicsSettings.lightsUseLinearIntensity = true;
                GraphicsSettings.lightsUseColorTemperature = true;
                Light light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.intensity = 4;
                light.useColorTemperature = useTemperature;
                light.colorTemperature = 3500;
                foreach (Color color in new[] { Color.white, new Color(0.5f, 0.5f, 0.5f), new Color(0.2f, 0.5f, 0.8f) })
                {
                    light.color = color;
                    var extracted = new UnityEngine.Experimental.GlobalIllumination.PointLight();
                    UnityEngine.Experimental.GlobalIllumination.LightmapperUtils.Extract(light, ref extracted);
                    var expected = extracted.color;
                    Assert.IsTrue(FLightRecordPack.TryPack(light, null, LightShapeUtility.FromUnityLight(light), 0, Vector3.zero, out FLightRecord record));
                    Assert.AreEqual(expected.red * expected.intensity, record.radiance.x, 1e-5f, "Red: " + color);
                    Assert.AreEqual(expected.green * expected.intensity, record.radiance.y, 1e-5f, "Green: " + color);
                    Assert.AreEqual(expected.blue * expected.intensity, record.radiance.z, 1e-5f, "Blue: " + color);
                }
            }
            finally
            {
                GraphicsSettings.lightsUseLinearIntensity = previousLinear;
                GraphicsSettings.lightsUseColorTemperature = previousTemperature;
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        [Category("Unverified_0_5")]
        public void ShadowSliceCounts_Point6_SingleTileShapes1_Directional0()
        {
            Assert.AreEqual(6, FLightRecordPack.LocalShadowSliceCount(new FLightRecord()));
            Assert.AreEqual(1, FLightRecordPack.LocalShadowSliceCount(new FLightRecord { flags = FLightRecordFlags.SpotCone }));
            Assert.AreEqual(1, FLightRecordPack.LocalShadowSliceCount(new FLightRecord { flags = FLightRecordFlags.SpotPyramid }));
            Assert.AreEqual(1, FLightRecordPack.LocalShadowSliceCount(new FLightRecord { flags = FLightRecordFlags.SpotBox }));
            Assert.AreEqual(1, FLightRecordPack.LocalShadowSliceCount(new FLightRecord { flags = FLightRecordFlags.Planar }));
            Assert.AreEqual(0, FLightRecordPack.LocalShadowSliceCount(new FLightRecord { flags = FLightRecordFlags.Directional }));
        }

        [Test]
        [Category("Unverified_0_5")]
        public void DistanceFade_FadesOverFadeLengthBeforeMaximum()
        {
            Assert.AreEqual(1.0f, FLightRecordPack.DistanceFade(10, 128, 1), 1e-6f);
            Assert.AreEqual(0.5f, FLightRecordPack.DistanceFade(127.5f, 128, 1), 1e-6f);
            Assert.AreEqual(0.0f, FLightRecordPack.DistanceFade(129, 128, 1), 1e-6f);
            Assert.AreEqual(1.0f, FLightRecordPack.DistanceFade(1000, 0, 1), 1e-6f, "Maximum 0 disables distance culling.");
        }

        [Test]
        [Category("Unverified_0_5")]
        public void LocalLight_BeyondMaximumDistance_IsNotPacked()
        {
            GameObject go = new GameObject("LightFadeTest");
            try
            {
                Light light = go.AddComponent<Light>();
                light.type = LightType.Point;
                InfinityAdditionalLightData ext = go.AddComponent<InfinityAdditionalLightData>();
                ext.maxDrawDistance = 10;
                ext.maxDrawDistanceFade = 2;
                FLightShape shape = FLightRecordPack.ResolveShape(light, ext);
                Assert.IsFalse(FLightRecordPack.TryPack(light, ext, shape, 0, new Vector3(0, 0, 20), out _));
                Assert.IsTrue(FLightRecordPack.TryPack(light, ext, shape, 0, new Vector3(0, 0, 9), out FLightRecord record));
                Assert.AreEqual(light.intensity * 0.5f, record.radiance.x, 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void HasLocalLightList_TrueWhenLocalCountPositive()
        {
            Assert.IsFalse(LightContext.HasLocalLightList(0));
            Assert.IsTrue(LightContext.HasLocalLightList(1));
            Assert.IsTrue(LightContext.HasLocalLightList(3));
        }

        [Test]
        public void InfinityAdditionalLightData_DoesNotOwnColorAsAuthority()
        {
            Assert.IsNull(typeof(InfinityAdditionalLightData).GetField("color", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
            Assert.IsNull(typeof(InfinityAdditionalLightData).GetField("intensity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
            Assert.IsNull(typeof(InfinityAdditionalLightData).GetField("lightType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
            Assert.IsNull(typeof(InfinityAdditionalLightData).GetMethod("OnGUIChange", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
            Assert.IsNull(typeof(InfinityAdditionalLightData).GetMethod("GetLightElement", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
            Assert.IsNull(typeof(InfinityAdditionalLightData).GetField("enableShadow", BindingFlags.Instance | BindingFlags.Public));
            Assert.IsNotNull(typeof(InfinityAdditionalLightData).GetField("diffuse", BindingFlags.Instance | BindingFlags.Public));
        }
    }
}
