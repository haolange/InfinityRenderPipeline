using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class PipelineConfigurationResolverTests
    {
        static InfinityRenderPipelineAsset CreateAsset(
            EGBufferLayout layout = EGBufferLayout.Compact64Bit,
            ESceneColorFormat sceneColor = ESceneColorFormat.R11G11B10F,
            EDepthStencilFormat depthStencil = EDepthStencilFormat.D32FS8,
            EMotionVectorFormat motionVector = EMotionVectorFormat.RG16F,
            EIndirectLightingFormat indirectLighting = EIndirectLightingFormat.R11G11B10F,
            ESubsurfaceLightingMode sssMode = ESubsurfaceLightingMode.SeparateBuffer,
            bool renderingLayers = true,
            bool withProfile = false)
        {
            var asset = ScriptableObject.CreateInstance<InfinityRenderPipelineAsset>();
            using (var serialized = new SerializedObject(asset))
            {
                serialized.FindProperty("m_GBufferLayout").intValue = (int)layout;
                serialized.FindProperty("m_SceneColorFormat").intValue = (int)sceneColor;
                serialized.FindProperty("m_DepthStencilFormat").intValue = (int)depthStencil;
                serialized.FindProperty("m_MotionVectorFormat").intValue = (int)motionVector;
                serialized.FindProperty("m_IndirectLightingFormat").intValue = (int)indirectLighting;
                serialized.FindProperty("m_SubsurfaceLightingMode").intValue = (int)sssMode;
                serialized.FindProperty("m_EnableRenderingLayers").boolValue = renderingLayers;
                if (withProfile)
                {
                    SerializedProperty profiles = serialized.FindProperty("diffusionProfiles");
                    profiles.arraySize = 1;
                    profiles.GetArrayElementAtIndex(0).objectReferenceValue = ScriptableObject.CreateInstance<DiffusionProfile>();
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
                    if (profile != null) Object.DestroyImmediate(profile);
                }
            }
            Object.DestroyImmediate(asset);
        }

        [Test]
        public void DefaultConfiguration_Compact64BitAndR11G11B10F_NoConflicts()
        {
            var asset = CreateAsset();
            try
            {
                var settings = asset.ResolveSettings();
                var config = PipelineConfigurationResolver.Evaluate(in settings, DeviceCapabilitySnapshot.AllSupported);
                Assert.IsFalse(config.hasBlockingConflict);
                Assert.AreEqual(EGBufferLayout.Compact64Bit, config.gBufferLayout);
                Assert.IsFalse(config.useBestFitYCoCg);
                Assert.AreEqual(GraphicsFormat.R8G8B8A8_SRGB, config.gBufferAFormat);
                Assert.AreEqual(GraphicsFormat.R8G8B8A8_UNorm, config.gBufferBFormat);
                Assert.AreEqual(GraphicsFormat.R8G8_UNorm, config.surfaceMetadataFormat);
                Assert.AreEqual(GraphicsFormat.B10G11R11_UFloatPack32, config.sceneColorFormat);
                Assert.AreEqual(GraphicsFormat.D32_SFloat_S8_UInt, config.depthStencilFormat);
                Assert.AreEqual(GraphicsFormat.B10G11R11_UFloatPack32, config.indirectLightingFormat);
                Assert.AreEqual(64, config.coreGBufferBitsPerPixel);
                Assert.AreEqual(16, config.surfaceMetadataBitsPerPixel);
                Assert.AreEqual(112, config.commonGBufferPassBitsPerPixel);
            }
            finally { DestroyAsset(asset); }
        }

        [Test]
        public void HighPrecision96Bit_UnsupportedFallback_SelectsCompact64Bit()
        {
            var asset = CreateAsset(layout: EGBufferLayout.HighPrecision96Bit);
            try
            {
                var settings = asset.ResolveSettings();
                var caps = new DeviceCapabilitySnapshot(
                    true, true, true, true, true, true,
                    supportsRGBA16UnormRT: false,
                    true, true, true, true, true, true, true);
                var config = PipelineConfigurationResolver.Evaluate(in settings, in caps);
                Assert.IsFalse(config.hasBlockingConflict);
                Assert.AreEqual(EGBufferLayout.Compact64Bit, config.gBufferLayout);
                Assert.AreEqual(EConfigStatus.Unsupported, config.gBufferLayoutState.status);
                Assert.AreEqual(64, config.coreGBufferBitsPerPixel);
            }
            finally { DestroyAsset(asset); }
        }

        [Test]
        public void SceneColorAlphaSSS_WithR11G11B10F_ReportsConflictAndForcesRGBA16F()
        {
            var asset = CreateAsset(
                sceneColor: ESceneColorFormat.R11G11B10F,
                sssMode: ESubsurfaceLightingMode.SceneColorAlpha,
                withProfile: true);
            try
            {
                var settings = asset.ResolveSettings();
                var config = PipelineConfigurationResolver.Evaluate(in settings, DeviceCapabilitySnapshot.AllSupported);
                Assert.IsTrue(config.hasBlockingConflict);
                Assert.AreEqual(EConfigStatus.Conflict, config.subsurfaceModeState.status);
                Assert.AreEqual(EConfigStatus.Required, config.sceneColorState.status);
                Assert.AreEqual(GraphicsFormat.R16G16B16A16_SFloat, config.sceneColorFormat);
            }
            finally { DestroyAsset(asset); }
        }

        [TestCase(EDepthStencilFormat.D32F)]
        [TestCase(EDepthStencilFormat.D16)]
        public void DepthWithoutStencil_ReportsBlockingConflict(EDepthStencilFormat depthFormat)
        {
            var asset = CreateAsset(depthStencil: depthFormat);
            try
            {
                var settings = asset.ResolveSettings();
                var config = PipelineConfigurationResolver.Evaluate(in settings, DeviceCapabilitySnapshot.AllSupported);
                Assert.IsTrue(config.hasBlockingConflict);
                Assert.AreEqual(EConfigStatus.Conflict, config.depthStencilState.status);
                Assert.IsTrue(config.hasStencil);
            }
            finally { DestroyAsset(asset); }
        }

        [Test]
        public void DisabledRenderingLayers_ReducesSurfaceMetadataTo8Bit()
        {
            var asset = CreateAsset(renderingLayers: false);
            try
            {
                var settings = asset.ResolveSettings();
                var config = PipelineConfigurationResolver.Evaluate(in settings, DeviceCapabilitySnapshot.AllSupported);
                Assert.AreEqual(GraphicsFormat.R8_UNorm, config.surfaceMetadataFormat);
                Assert.AreEqual(8, config.surfaceMetadataBitsPerPixel);
            }
            finally { DestroyAsset(asset); }
        }
    }
}
