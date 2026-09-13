using System.Collections;
using UnityEngine.TestTools;
using UnityEngine.Rendering;
using UnityEditor;
using InfinityTech.Component;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class SceneViewOutputTests
    {
        [UnityTest]
        public IEnumerator Present_GrayCard_EncodesSdrOnce_AndPreservesLinearTargets()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.infinity.render-pipeline/Shaders/Utility/DrawFullScreen.shader");
            Assert.IsNotNull(shader);
            var material = new Material(shader);
            int presentPass = material.FindPass("Present");
            Assert.GreaterOrEqual(presentPass, 0);
            var source = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true);
            try
            {
                source.SetPixels(new[] { new Color(.18f,.18f,.18f,1), new Color(.18f,.18f,.18f,1), new Color(.18f,.18f,.18f,1), new Color(.18f,.18f,.18f,1) });
                source.Apply();
                material.SetTexture("_MainTex", source);
                material.SetVector("_ScaleBais", new Vector4(1,1,0,0));
                var formats = new[] { GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.R8G8B8A8_SRGB };
                var policies = new[] { EOutputEncodePolicy.LinearTarget, EOutputEncodePolicy.ShaderLinearToSRGB, EOutputEncodePolicy.HardwareSRGB };
                for (int i = 0; i < formats.Length; ++i)
                {
                    var target = new RenderTexture(new RenderTextureDescriptor(2, 2) { graphicsFormat = formats[i], depthStencilFormat = GraphicsFormat.None, msaaSamples = 1 });
                    var commands = new CommandBuffer();
                    bool deferredCleanup = false;
                    var ownedMaterial = material;
                    var ownedSource = source;
                    try
                    {
                        Assert.IsTrue(target.Create());
                        Assert.AreEqual(formats[i], target.graphicsFormat);
                        material.SetVector("_InfinityOutputTransfer", new Vector4((int)policies[i],0,100,0));
                        commands.SetRenderTarget(target);
                        commands.SetViewport(new Rect(0,0,2,2));
                        commands.ClearRenderTarget(false,true,Color.magenta);
                        commands.DrawMesh(GraphicsUtility.FullScreenMesh, Matrix4x4.identity, material, 0, presentPass);
                        Graphics.ExecuteCommandBuffer(commands);
                        var readback = AsyncGPUReadback.Request(target, 0, _ =>
                        {
                            if (!deferredCleanup) return;
                            Object.DestroyImmediate(target);
                            Object.DestroyImmediate(ownedMaterial);
                            Object.DestroyImmediate(ownedSource);
                        });
                        double deadline = Time.realtimeSinceStartupAsDouble + 10;
                        while (!readback.done && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                        if (!readback.done)
                        {
                            deferredCleanup = true;
                            material = null;
                            source = null;
                            Assert.Fail("Present readback timed out; resources retire when this request completes.");
                        }
                        Assert.IsFalse(readback.hasError, formats[i].ToString());
                        for (int pixel = 0; pixel < 4; ++pixel)
                            for (int channel = 0; channel < 3; ++channel)
                            {
                                float value = i == 0 ? (float)new half { value = readback.GetData<ushort>()[pixel*4+channel] }
                                    : readback.GetData<byte>()[pixel*4+channel] / 255f;
                                if (i == 0) Assert.That(value, Is.EqualTo(.18f).Within(1f/1024f));
                                else Assert.That(value, Is.InRange(.44f,.48f), policies[i].ToString());
                            }
                    }
                    finally { commands.Dispose(); if (!deferredCleanup) Object.DestroyImmediate(target); }
                }
            }
            finally { Object.DestroyImmediate(material); Object.DestroyImmediate(source); }
        }

        [Test]
        public void ExplicitTargetImport_PreservesDescriptor_AndDoesNotReleaseUnityTexture()
        {
            var texture = new RenderTexture(37, 29, 0, RenderTextureFormat.ARGBHalf);
            var graph = new InfinityTech.Rendering.RenderGraph.RGBuilder("ExternalTargetOwnership");
            try
            {
                texture.Create();
                var descriptor = new InfinityTech.Rendering.GPUResource.TextureDescriptor(37, 29)
                {
                    name = "ExternalSceneTarget", colorFormat = texture.graphicsFormat,
                    dimension = UnityEngine.Rendering.TextureDimension.Tex2D
                };
                var imported = graph.ImportBackbuffer(new UnityEngine.Rendering.RenderTargetIdentifier(texture), descriptor);
                Assert.AreEqual(descriptor, graph.GetTextureDescriptor(imported));
                graph.Dispose();
                graph = null;
                Assert.IsTrue(texture.IsCreated(), "The graph owns the identifier wrapper, not Unity's target texture.");
            }
            finally
            {
                graph?.Dispose();
                Object.DestroyImmediate(texture);
            }
        }

        [TestCase(0.5f, 321, 240)]
        [TestCase(0.75f, 481, 360)]
        [TestCase(1f, 641, 479)]
        public void Dimensions_PreserveOutputViewport_AndSeparateEnabledFromDownscaled(float scale, int width, int height)
        {
            var viewport = new Rect(13, 27, 641, 479);
            var dimensions = new CameraDimensionDescriptor(new int2(641, 479), scale, true, viewport);
            Assert.AreEqual(viewport, dimensions.outputViewport);
            Assert.AreEqual(new int2(width, height), dimensions.internalSize);
            Assert.AreEqual(new int2(641, 479), dimensions.displaySize);
            Assert.IsTrue(dimensions.superResolution);
            Assert.AreEqual(scale < 1, dimensions.downscaled);
        }

        [TestCase(CameraType.SceneView)]
        [TestCase(CameraType.Preview)]
        public void EditorCameras_IgnoreGameScale(CameraType type)
        {
            var go = new GameObject("DimensionCamera");
            var target = new RenderTexture(641, 479, 0);
            var asset = ScriptableObject.CreateInstance<InfinityRenderPipelineAsset>();
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.enabled = false;
                camera.targetTexture = target;
                camera.cameraType = type;
                asset.enableSuperResolution = true;
                asset.renderScale = .5f;
                var dimensions = CameraDimensionDescriptor.FromCamera(camera, asset, null);
                Assert.IsFalse(dimensions.superResolution);
                Assert.AreEqual(dimensions.displaySize, dimensions.internalSize);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void CameraOff_DisablesSRWithoutChangingOutputViewport()
        {
            var go = new GameObject("DimensionCamera");
            var target = new RenderTexture(641, 479, 0);
            var asset = ScriptableObject.CreateInstance<InfinityRenderPipelineAsset>();
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.enabled = false;
                camera.targetTexture = target;
                var additional = go.AddComponent<InfinityAdditionalCameraData>();
                additional.superResolutionOverride = ESuperResolutionOverride.Off;
                asset.enableSuperResolution = true;
                asset.renderScale = .5f;
                var dimensions = CameraDimensionDescriptor.FromCamera(camera, asset, additional);
                Assert.IsFalse(dimensions.superResolution);
                Assert.AreEqual(dimensions.displaySize, dimensions.internalSize);
                Assert.AreEqual(camera.pixelRect, dimensions.outputViewport);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(asset);
            }
        }

        [TestCase(GraphicsFormat.R16G16B16A16_SFloat, EOutputEncodePolicy.LinearTarget)]
        [TestCase(GraphicsFormat.R8G8B8A8_SRGB, EOutputEncodePolicy.HardwareSRGB)]
        public void TextureTargets_ApplyOnlyTheirStorageTransfer(GraphicsFormat format, EOutputEncodePolicy expected)
        {
            var go = new GameObject("OutputCamera");
            var target = new RenderTexture(new RenderTextureDescriptor(641, 479) { graphicsFormat = format });
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.enabled = false;
                camera.targetTexture = target;
                var decision = OutputTransformUtility.ResolveFromHardware(EOutputMode.SDR, EHDREncoding.PQ_Rec2020, camera);
                Assert.AreEqual(expected, decision.policy);
                Assert.AreEqual(GraphicsFormat.R16G16B16A16_SFloat, decision.displayFormat);
                Assert.AreEqual(OutputTransformUtility.OutputGamutSRGB, decision.outputGamut);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(target);
            }
        }
    }
}
