using System.Reflection;
using NUnit.Framework;
using UnityEngine;
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
        public void PackFromUnityLight_UsesUnityColorTimesIntensityOnce()
        {
            GameObject go = new GameObject("LightRecordTest");
            try
            {
                Light light = go.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = new Color(0.5f, 0.25f, 0.1f, 1.0f);
                light.intensity = 4.0f;
                LightComponent ext = go.AddComponent<LightComponent>();

                FLightRecord record = FLightRecordPack.FromUnityLight(light, ext, ELightType.Directional, 0);
                Assert.AreEqual(2.0f, record.radiance.x, 1e-4f);
                Assert.AreEqual(1.0f, record.radiance.w, 1e-4f);
                Assert.AreEqual((int)ELightType.Directional, record.lightType);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ShadowAllocator_TileCounts_Spot1_Point6_Rect0()
        {
            Assert.AreEqual(1, ShadowAllocator.TileCountForType(ELightType.Spot));
            Assert.AreEqual(6, ShadowAllocator.TileCountForType(ELightType.Point));
            Assert.AreEqual(0, ShadowAllocator.TileCountForType(ELightType.Rect));
            Assert.AreEqual(0, ShadowAllocator.TileCountForType(ELightType.Directional));
        }

        [Test]
        public void HasZBinningLightList_TrueWhenLocalCountPositive()
        {
            Assert.IsFalse(LightContext.HasZBinningLightList(0));
            Assert.IsTrue(LightContext.HasZBinningLightList(1));
            Assert.IsTrue(LightContext.HasZBinningLightList(3));
        }

        [Test]
        public void LightComponent_DoesNotOwnColorAsAuthority()
        {
            Assert.IsNull(typeof(LightComponent).GetField("color", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
            Assert.IsNull(typeof(LightComponent).GetField("intensity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
            Assert.IsNull(typeof(LightComponent).GetField("lightType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
            Assert.IsNull(typeof(LightComponent).GetMethod("OnGUIChange", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
            Assert.IsNull(typeof(LightComponent).GetMethod("GetLightElement", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
            Assert.IsNotNull(typeof(LightComponent).GetField("enableShadow", BindingFlags.Instance | BindingFlags.Public));
            Assert.IsNotNull(typeof(LightComponent).GetField("diffuse", BindingFlags.Instance | BindingFlags.Public));
        }
    }
}
