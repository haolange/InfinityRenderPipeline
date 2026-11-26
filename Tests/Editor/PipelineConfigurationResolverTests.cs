using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class PipelineConfigurationResolverTests
    {
        static InfinityRenderPipelineAsset CreateAsset(EDepthStencilFormat depthStencil = EDepthStencilFormat.D32FS8, bool renderingLayers = true, int profileCount = 0)
        {
            var asset = ScriptableObject.CreateInstance<InfinityRenderPipelineAsset>();
            using (var serialized = new SerializedObject(asset))
            {
                serialized.FindProperty("m_DepthStencilFormat").intValue = (int)depthStencil;
                serialized.FindProperty("m_EnableRenderingLayers").boolValue = renderingLayers;
                SerializedProperty profiles = serialized.FindProperty("diffusionProfiles");
                profiles.arraySize = profileCount;
                for (int i = 0; i < profileCount; ++i)
                {
                    profiles.GetArrayElementAtIndex(i).objectReferenceValue = ScriptableObject.CreateInstance<DiffusionProfile>();
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            return asset;
        }

        static void DestroyAsset(InfinityRenderPipelineAsset asset)
        {
            DiffusionProfile[] profiles = asset.ReadDiffusionProfiles();
            if (profiles != null)
            {
                foreach (var profile in profiles)
                {
                    if (profile != null)
                    {
                        Object.DestroyImmediate(profile);
                    }
                }
            }
            Object.DestroyImmediate(asset);
        }

        static DeviceCapabilitySnapshot Capabilities(bool d24s8 = true, bool d32fs8 = true, bool rgba8Srgb = true, bool rgba8LoadStore = true, bool r11g11b10LoadStore = true, bool rg16f = true)
        {
            return new DeviceCapabilitySnapshot(d24s8, d32fs8, rgba8Srgb, rgba8LoadStore, r11g11b10LoadStore, rg16f, supportsAsyncCompute: true, supportsRayTracing: true);
        }

        [Test]
        public void DefaultConfiguration_FixedColorPath_NoConflicts()
        {
            var asset = CreateAsset();
            try
            {
                var settings = asset.ResolveSettings();
                var config = PipelineConfigurationResolver.Evaluate(in settings, DeviceCapabilitySnapshot.AllSupported);
                Assert.IsFalse(config.hasBlockingConflict);
                Assert.AreEqual(EConfigStatus.Supported, config.colorPathState.status);
                Assert.AreEqual(GraphicsFormat.D32_SFloat_S8_UInt, config.depthStencilFormat);
                // GBuffer A, GBuffer B, Scene Color and the sideband: four 32-bit targets.
                Assert.AreEqual(128, config.geometryColorBitsPerPixel);
                Assert.AreEqual(GraphicsFormat.R8G8B8A8_SRGB, EffectivePipelineConfiguration.GBufferAFormat);
                Assert.AreEqual(GraphicsFormat.R8G8B8A8_UNorm, EffectivePipelineConfiguration.GBufferBFormat);
                Assert.AreEqual(GraphicsFormat.B10G11R11_UFloatPack32, EffectivePipelineConfiguration.SceneColorFormat);
                Assert.AreEqual(GraphicsFormat.R8G8B8A8_UNorm, EffectivePipelineConfiguration.SidebandFormat);
                Assert.AreEqual(GraphicsFormat.R16G16_SFloat, EffectivePipelineConfiguration.MotionFormat);
            }
            finally
            {
                DestroyAsset(asset);
            }
        }

        [TestCase(EDepthStencilFormat.D24S8, GraphicsFormat.D24_UNorm_S8_UInt)]
        [TestCase(EDepthStencilFormat.D32FS8, GraphicsFormat.D32_SFloat_S8_UInt)]
        public void AuthoredDepth_SupportedFormat_IsUsed(EDepthStencilFormat authored, GraphicsFormat expected)
        {
            var asset = CreateAsset(depthStencil: authored);
            try
            {
                var settings = asset.ResolveSettings();
                var config = PipelineConfigurationResolver.Evaluate(in settings, DeviceCapabilitySnapshot.AllSupported);
                Assert.AreEqual(expected, config.depthStencilFormat);
                Assert.AreEqual(EConfigStatus.Supported, config.depthStencilState.status);
            }
            finally
            {
                DestroyAsset(asset);
            }
        }

        [Test]
        public void UnsupportedD24S8_FallsBackToD32FS8WithPlatformOverride()
        {
            var asset = CreateAsset(depthStencil: EDepthStencilFormat.D24S8);
            try
            {
                var settings = asset.ResolveSettings();
                var config = PipelineConfigurationResolver.Evaluate(in settings, Capabilities(d24s8: false));
                Assert.IsFalse(config.hasBlockingConflict);
                Assert.AreEqual(GraphicsFormat.D32_SFloat_S8_UInt, config.depthStencilFormat);
                Assert.AreEqual(EConfigStatus.PlatformOverride, config.depthStencilState.status);
            }
            finally
            {
                DestroyAsset(asset);
            }
        }

        [Test]
        public void NoStencilDepthFormat_IsBlocking()
        {
            var asset = CreateAsset();
            try
            {
                var settings = asset.ResolveSettings();
                var config = PipelineConfigurationResolver.Evaluate(in settings, Capabilities(d24s8: false, d32fs8: false));
                Assert.IsTrue(config.hasBlockingConflict);
                Assert.AreEqual(EConfigStatus.Unsupported, config.depthStencilState.status);
            }
            finally
            {
                DestroyAsset(asset);
            }
        }

        // Each format of the fixed color path is required: there is no alternative layout to fall back to.
        [TestCase(false, true, true, true)]
        [TestCase(true, false, true, true)]
        [TestCase(true, true, false, true)]
        [TestCase(true, true, true, false)]
        public void MissingColorPathFormat_IsBlocking(bool rgba8Srgb, bool rgba8LoadStore, bool r11g11b10LoadStore, bool rg16f)
        {
            var asset = CreateAsset();
            try
            {
                var settings = asset.ResolveSettings();
                var caps = Capabilities(rgba8Srgb: rgba8Srgb, rgba8LoadStore: rgba8LoadStore, r11g11b10LoadStore: r11g11b10LoadStore, rg16f: rg16f);
                var config = PipelineConfigurationResolver.Evaluate(in settings, in caps);
                Assert.IsTrue(config.hasBlockingConflict);
                Assert.AreEqual(EConfigStatus.Unsupported, config.colorPathState.status);
                Assert.AreEqual(config.colorPathState.reason, config.blockingConflict);
            }
            finally
            {
                DestroyAsset(asset);
            }
        }

        [TestCase(0, EConfigStatus.Inactive, false)]
        [TestCase(1, EConfigStatus.Supported, false)]
        [TestCase(15, EConfigStatus.Supported, false)]
        [TestCase(16, EConfigStatus.Conflict, true)]
        public void DiffusionProfiles_FitTheSidebandNibble(int profileCount, EConfigStatus expected, bool blocking)
        {
            var asset = CreateAsset(profileCount: profileCount);
            try
            {
                var settings = asset.ResolveSettings();
                var config = PipelineConfigurationResolver.Evaluate(in settings, DeviceCapabilitySnapshot.AllSupported);
                Assert.AreEqual(expected, config.subsurfaceState.status);
                Assert.AreEqual(blocking, config.hasBlockingConflict);
            }
            finally
            {
                DestroyAsset(asset);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RenderingLayers_PassThroughUnchanged(bool renderingLayers)
        {
            var asset = CreateAsset(renderingLayers: renderingLayers);
            try
            {
                var settings = asset.ResolveSettings();
                var config = PipelineConfigurationResolver.Evaluate(in settings, DeviceCapabilitySnapshot.AllSupported);
                Assert.AreEqual(renderingLayers, config.enableRenderingLayers);
                Assert.AreEqual(128, config.geometryColorBitsPerPixel);
            }
            finally
            {
                DestroyAsset(asset);
            }
        }
    }
}
