using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Component;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class NormalizationContractTests
    {
        [Test]
        public void OptionalVolumes_GateOnEnableNotOverrideState()
        {
            ScreenSpaceReflection ssr = ScriptableObject.CreateInstance<ScreenSpaceReflection>();
            ScreenSpaceAmbientOcclusion ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
            try
            {
                ssr.active = true;
                ssr.NumRays.overrideState = true;
                Assert.IsFalse(GraphicsUtility.VolumeComponentActive(ssr));
                ssr.enable.value = true;
                Assert.IsTrue(GraphicsUtility.VolumeComponentActive(ssr));

                ssao.active = true;
                ssao.Intensity.overrideState = true;
                Assert.IsFalse(ScreenSpaceModeUtility.ShouldRequestGTAO(ssao));
                ssao.enable.value = true;
                Assert.IsTrue(ScreenSpaceModeUtility.ShouldRequestGTAO(ssao));
            }
            finally
            {
                Object.DestroyImmediate(ssr);
                Object.DestroyImmediate(ssao);
            }
        }

        [Test]
        public void BloomVignetteGrain_GateOnIntensity()
        {
            Bloom bloom = ScriptableObject.CreateInstance<Bloom>();
            Vignette vignette = ScriptableObject.CreateInstance<Vignette>();
            FilmGrain grain = ScriptableObject.CreateInstance<FilmGrain>();
            try
            {
                bloom.active = true;
                vignette.active = true;
                grain.active = true;
                Assert.IsFalse(GraphicsUtility.VolumeComponentActive(bloom));
                Assert.IsFalse(GraphicsUtility.VolumeComponentActive(vignette));
                Assert.IsFalse(GraphicsUtility.VolumeComponentActive(grain));
                bloom.intensity.value = 0.2f;
                vignette.intensity.value = 0.2f;
                grain.intensity.value = 0.2f;
                Assert.IsTrue(GraphicsUtility.VolumeComponentActive(bloom));
                Assert.IsTrue(GraphicsUtility.VolumeComponentActive(vignette));
                Assert.IsTrue(GraphicsUtility.VolumeComponentActive(grain));
            }
            finally
            {
                Object.DestroyImmediate(bloom);
                Object.DestroyImmediate(vignette);
                Object.DestroyImmediate(grain);
            }
        }

        [Test]
        public void FilmTonemap_NoneIsInactive()
        {
            FilmTonemap film = ScriptableObject.CreateInstance<FilmTonemap>();
            try
            {
                film.active = true;
                film.mode.value = EFilmTonemapMode.None;
                Assert.IsFalse(film.IsActive());
                film.mode.value = EFilmTonemapMode.Film;
                Assert.IsTrue(film.IsActive());
            }
            finally
            {
                Object.DestroyImmediate(film);
            }
        }

        [Test]
        public void RenderScale_ClampsAndGameOnlySuperResolution()
        {
            var asset = ScriptableObject.CreateInstance<InfinityRenderPipelineAsset>();
            try
            {
                asset.renderScale = 0.1f;
                Assert.AreEqual(0.5f, asset.renderScale);
                asset.renderScale = 2.0f;
                Assert.AreEqual(1.0f, asset.renderScale);
                asset.enableSuperResolution = true;
                asset.renderScale = 0.7f;
                var cameraObject = new GameObject("ScaleCam");
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.cameraType = CameraType.Game;
                CameraDimensionDescriptor game = CameraDimensionDescriptor.FromCamera(camera, asset, null);
                Assert.IsTrue(game.superResolution);
                camera.cameraType = CameraType.SceneView;
                CameraDimensionDescriptor scene = CameraDimensionDescriptor.FromCamera(camera, asset, null);
                Assert.IsFalse(scene.superResolution);
                Object.DestroyImmediate(cameraObject);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void AdditionalCameraData_GetOrCreateIsIdempotent()
        {
            var cameraObject = new GameObject("AddCam");
            Camera camera = cameraObject.AddComponent<Camera>();
            try
            {
                InfinityAdditionalCameraData first = InfinityAdditionalCameraData.GetOrCreate(camera, false);
                InfinityAdditionalCameraData second = InfinityAdditionalCameraData.GetOrCreate(camera, false);
                Assert.AreSame(first, second);
                Assert.AreEqual(1, cameraObject.GetComponents<InfinityAdditionalCameraData>().Length);
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void RayTracingEnvironment_ReportsBackendWithoutThrowing()
        {
            Assert.DoesNotThrow(() => InfinityRayTracingEnvironment.ResolveBackend());
        }

        [Test]
        public void GlobalSettings_Uses17_6SettingsThenPipelineTypeArguments()
        {
            Type generic = typeof(InfinityRenderPipelineGlobalSettings).BaseType;
            Assert.IsNotNull(generic);
            Assert.IsTrue(generic.IsGenericType);
            Type[] args = generic.GetGenericArguments();
            Assert.AreEqual(2, args.Length);
            Assert.AreEqual(typeof(InfinityRenderPipelineGlobalSettings), args[0]);
            Assert.AreEqual(typeof(InfinityRenderPipeline), args[1]);
        }

        [Test]
        public void GlobalSettings_OwnsGraphicsSettingsContainer()
        {
            FieldInfo container = typeof(InfinityRenderPipelineGlobalSettings).GetField("m_Settings", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(container);
            Assert.AreEqual(typeof(RenderPipelineGraphicsSettingsContainer), container.FieldType);
        }
    }
}
