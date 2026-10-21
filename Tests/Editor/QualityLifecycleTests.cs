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

        [TestCase(false, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        [TestCase(true, false, true)]
        public void Pipeline_RestoresOnlyOwnedGraphicsState(bool replacementOwnsState, bool editResourceBindings, bool replacementOwnsLightmapper)
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
            var originalLightmapper = UnityEngine.Experimental.GlobalIllumination.Lightmapping.GetDelegate();
            UnityEngine.Experimental.GlobalIllumination.Lightmapping.RequestLightsDelegate previousLightmapper = (lights, output) => { };
            UnityEngine.Experimental.GlobalIllumination.Lightmapping.RequestLightsDelegate replacementLightmapper = (lights, output) => { };
            UnityEngine.Experimental.GlobalIllumination.Lightmapping.SetDelegate(previousLightmapper);
            type.GetMethod("CaptureGraphicsState", flags).Invoke(pipeline, null);
            try
            {
                type.GetMethod("SetGraphicsSetting", flags).Invoke(pipeline, null);
                Assert.AreEqual("InfinityRenderPipeline", Shader.globalRenderPipeline);
                Assert.AreEqual(InfinityLightmapperDelegate.Delegate, UnityEngine.Experimental.GlobalIllumination.Lightmapping.GetDelegate());
                if (replacementOwnsLightmapper)
                    UnityEngine.Experimental.GlobalIllumination.Lightmapping.SetDelegate(replacementLightmapper);
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
                Assert.AreEqual(replacementOwnsLightmapper ? replacementLightmapper : previousLightmapper,
                    UnityEngine.Experimental.GlobalIllumination.Lightmapping.GetDelegate(), "Restore the prior delegate independently of feature flags, without overwriting a replacement.");
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
                UnityEngine.Experimental.GlobalIllumination.Lightmapping.SetDelegate(originalLightmapper);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void Lightmapper_OutOfOrderReleasePreservesLiveOwnerAndExternalDelegates(int replacementPoint)
        {
            var type = typeof(InfinityRenderPipeline);
            var instanceFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var ownerField = type.GetField("s_LightmapperStateOwner", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var create = typeof(System.Runtime.CompilerServices.RuntimeHelpers).GetMethod("GetUninitializedObject");
            var capture = type.GetMethod("CaptureGraphicsState", instanceFlags);
            var install = type.GetMethod("InstallLightmapperState", instanceFlags);
            var release = type.GetMethod("RestoreLightmapperState", instanceFlags);
            var originalOwner = ownerField.GetValue(null);
            var originalDelegate = UnityEngine.Experimental.GlobalIllumination.Lightmapping.GetDelegate();
            UnityEngine.Experimental.GlobalIllumination.Lightmapping.RequestLightsDelegate f0 = (lights, output) => { };
            UnityEngine.Experimental.GlobalIllumination.Lightmapping.RequestLightsDelegate f1 = (lights, output) => { };
            UnityEngine.Experimental.GlobalIllumination.Lightmapping.RequestLightsDelegate f2 = (lights, output) => { };
            var o = (InfinityRenderPipeline)create.Invoke(null, new object[] { type });
            var a = (InfinityRenderPipeline)create.Invoke(null, new object[] { type });
            var b = (InfinityRenderPipeline)create.Invoke(null, new object[] { type });
            try
            {
                UnityEngine.Experimental.GlobalIllumination.Lightmapping.SetDelegate(f0);
                capture.Invoke(o, null); install.Invoke(o, null);
                capture.Invoke(a, null); install.Invoke(a, null);
                if (replacementPoint == 1) UnityEngine.Experimental.GlobalIllumination.Lightmapping.SetDelegate(f1);
                capture.Invoke(b, null); install.Invoke(b, null);
                if (replacementPoint == 2) UnityEngine.Experimental.GlobalIllumination.Lightmapping.SetDelegate(f2);
                release.Invoke(a, null);
                Assert.AreEqual(replacementPoint == 2 ? f2 : InfinityLightmapperDelegate.Delegate,
                    UnityEngine.Experimental.GlobalIllumination.Lightmapping.GetDelegate(), "Releasing an older owner cannot change the live delegate.");
                release.Invoke(b, null);
                Assert.AreEqual(replacementPoint == 2 ? f2 : (replacementPoint == 1 ? f1 : InfinityLightmapperDelegate.Delegate),
                    UnityEngine.Experimental.GlobalIllumination.Lightmapping.GetDelegate());
                release.Invoke(o, null);
                var expected = replacementPoint == 2 ? f2 : (replacementPoint == 1 ? f1 : f0);
                Assert.AreEqual(expected, UnityEngine.Experimental.GlobalIllumination.Lightmapping.GetDelegate());
                release.Invoke(b, null); release.Invoke(a, null); release.Invoke(o, null);
                Assert.AreEqual(expected, UnityEngine.Experimental.GlobalIllumination.Lightmapping.GetDelegate(), "Repeated release is a no-op.");
            }
            finally
            {
                release.Invoke(b, null); release.Invoke(a, null); release.Invoke(o, null);
                ownerField.SetValue(null, originalOwner);
                UnityEngine.Experimental.GlobalIllumination.Lightmapping.SetDelegate(originalDelegate);
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
