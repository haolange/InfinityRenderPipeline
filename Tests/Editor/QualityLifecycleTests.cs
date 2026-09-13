using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class QualityLifecycleTests
    {
        [Test]
        public void CameraDimensionDescriptor_EqualsByFields()
        {
            var a = new CameraDimensionDescriptor(new Unity.Mathematics.int2(1920, 1080), 0.75f, true, new Rect(0, 0, 1920, 1080));
            var b = new CameraDimensionDescriptor(new Unity.Mathematics.int2(1920, 1080), 0.75f, true, new Rect(0, 0, 1920, 1080));
            Assert.AreEqual(a.internalSize, b.internalSize);
            Assert.IsTrue(a.superResolution);
            Assert.AreEqual(1440, a.internalSize.x);
            Assert.AreEqual(810, a.internalSize.y);
        }

        [Test]
        public void GlobalSettings_ResolveDefaultProfileOrThrow()
        {
#if UNITY_EDITOR
            InfinityRenderPipelineGlobalSettings.Require();
#endif
            VolumeProfile profile = InfinityRenderPipelineGlobalSettings.ResolveDefaultVolumeProfile();
            Assert.IsNotNull(profile);
            Assert.IsTrue(DefaultVolumeProfileFactory.HasRequiredDefaultComponents(profile));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void Pipeline_RestoresOnlyOwnedGraphicsState(bool replacementOwnsState, bool editResourceBindings)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(InfinityRenderPipeline);
            var create = typeof(System.Runtime.CompilerServices.RuntimeHelpers).GetMethod("GetUninitializedObject");
            Assert.IsNotNull(create);
            // Exercise the actual production global-state methods without constructing a second renderer,
            // initializing VolumeManager or touching shared mesh/static draw ownership.
            var pipeline = (InfinityRenderPipeline)create.Invoke(null, new object[] { type });
            var testResources = new InfinityRenderPipelineResources();
            Material originalResourceBlit = testResources.materials.blitMaterial;
            Texture2D originalResourceBestFit = testResources.textures.bestFitNormalTexture;
            type.GetField("resources", flags).SetValue(pipeline, testResources);
            type.GetField("m_PipelineAsset", flags).SetValue(pipeline, GraphicsSettings.currentRenderPipeline);
            var originalBlit = GraphicsUtility.m_BlitMaterial;
            var originalBestFit = Shader.GetGlobalTexture("g_BestFitNormal_LUT");
            int originalAA = QualitySettings.antiAliasing;
            GraphicsUtility.m_BlitMaterial = null;
            Shader.SetGlobalTexture("g_BestFitNormal_LUT", Texture2D.grayTexture);
            var supported = SupportedRenderingFeatures.active;
            var shader = Shader.globalRenderPipeline;
            bool linear = GraphicsSettings.lightsUseLinearIntensity;
            bool temperature = GraphicsSettings.lightsUseColorTemperature;
            bool batching = GraphicsSettings.useScriptableRenderPipelineBatching;
            type.GetMethod("CaptureGraphicsState", flags).Invoke(pipeline, null);
            try
            {
                type.GetMethod("SetGraphicsSetting", flags).Invoke(pipeline, null);
                Assert.AreEqual("InfinityRenderPipeline", Shader.globalRenderPipeline);
                var replacement = new SupportedRenderingFeatures();
                if (editResourceBindings)
                {
                    testResources.materials.blitMaterial = null;
                    testResources.textures.bestFitNormalTexture = null;
                }
                if (replacementOwnsState)
                {
                    SupportedRenderingFeatures.active = replacement;
                    Shader.globalRenderPipeline = "ReplacementPipeline";
                    GraphicsSettings.lightsUseLinearIntensity = false;
                }
                type.GetMethod("RestoreGraphicsState", flags).Invoke(pipeline, null);
                Assert.AreSame(replacementOwnsState ? replacement : supported, SupportedRenderingFeatures.active);
                Assert.AreEqual(replacementOwnsState ? "ReplacementPipeline" : shader, Shader.globalRenderPipeline);
                Assert.AreEqual(replacementOwnsState ? false : linear, GraphicsSettings.lightsUseLinearIntensity);
                if (!replacementOwnsState)
                {
                    Assert.IsNull(GraphicsUtility.m_BlitMaterial);
                    Assert.AreSame(Texture2D.grayTexture, Shader.GetGlobalTexture("g_BestFitNormal_LUT"));
                    Assert.AreEqual(temperature, GraphicsSettings.lightsUseColorTemperature);
                    Assert.AreEqual(batching, GraphicsSettings.useScriptableRenderPipelineBatching);
                }
                Assert.DoesNotThrow(() => type.GetMethod("ReleaseOwnedResources", flags).Invoke(pipeline, null));
                Assert.DoesNotThrow(() => type.GetMethod("ReleaseOwnedResources", flags).Invoke(pipeline, null));
            }
            finally
            {
                type.GetMethod("RestoreGraphicsState", flags).Invoke(pipeline, null);
                SupportedRenderingFeatures.active = supported;
                Shader.globalRenderPipeline = shader;
                GraphicsSettings.lightsUseLinearIntensity = linear;
                GraphicsSettings.lightsUseColorTemperature = temperature;
                GraphicsSettings.useScriptableRenderPipelineBatching = batching;
                testResources.materials.blitMaterial = originalResourceBlit;
                testResources.textures.bestFitNormalTexture = originalResourceBestFit;
                GraphicsUtility.m_BlitMaterial = originalBlit;
                Shader.SetGlobalTexture("g_BestFitNormal_LUT", originalBestFit);
                QualitySettings.antiAliasing = originalAA;
            }
        }

        [Test]
        public void MissingRequiredTexture_FailsBeforePipelineChangesGlobalState()
        {
            var asset = (InfinityRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
            GraphicsSettings.TryGetRenderPipelineSettings(out InfinityRenderPipelineRuntimeTextures textures);
            Texture2D original = textures.bestFitNormalTexture;
            var features = SupportedRenderingFeatures.active;
            string shader = Shader.globalRenderPipeline;
            try
            {
                textures.bestFitNormalTexture = null;
                Assert.Throws<System.InvalidOperationException>(() => new InfinityRenderPipeline(asset));
                Assert.IsNull(textures.bestFitNormalTexture, "Read/validation must not refill a missing resource.");
                Assert.AreSame(features, SupportedRenderingFeatures.active);
                Assert.AreEqual(shader, Shader.globalRenderPipeline);
            }
            finally { textures.bestFitNormalTexture = original; }
        }
    }
}
