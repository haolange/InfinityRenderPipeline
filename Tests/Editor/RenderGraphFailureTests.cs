using System;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class RenderGraphFailureTests
    {
        sealed class Observation : IRGPassQueueObserver
        {
            public int queued;
            public void OnQueued() => queued++;
            public void OnQueueFailed() { }
            public Exception error;
            public RenderTexture allocated;
        }
        struct FailurePassData
        {
            public Observation observation;
            public RGTextureRef texture;
        }
        struct ConsumerPassData { public RGTextureRef texture; }

        [TestCase(RenderBufferLoadAction.Load, true)]
        [TestCase(RenderBufferLoadAction.Clear, false)]
        [TestCase(RenderBufferLoadAction.DontCare, false)]
        public void AttachmentLoad_DeclaresPreviousContentsAsInput(RenderBufferLoadAction load, bool readsPrevious)
        {
            var pass = new RGRasterPass<ConsumerPassData>();
            var color = new RGTextureRef(0);
            var depth = new RGTextureRef(1);
            pass.SetColorAttachment(color, 0, load, RenderBufferStoreAction.Store);
            pass.SetDepthStencilAttachment(depth, load, RenderBufferStoreAction.Store, EDepthAccess.Write);
            Assert.AreEqual(readsPrevious, pass.resourceReadLists[(int)ERGResourceType.Texture].Contains(color.handle));
            Assert.AreEqual(readsPrevious, pass.resourceReadLists[(int)ERGResourceType.Texture].Contains(depth.handle));
            CollectionAssert.AreEquivalent(new[] { color.handle, depth.handle }, pass.resourceWriteLists[(int)ERGResourceType.Texture]);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void InjectFailure(Exception error) => throw error;

        [TestCase(false)]
        [TestCase(true)]
        public void FailedPass_PreservesOriginalExceptionAndRetiresAllocatedResources(bool asyncCompute)
        {
            var graph = new RGBuilder("FailureContract");
            var resources = new ResourcePool();
            var frameCommands = new CommandBuffer();
            var observed = new Observation { error = new InvalidOperationException("Injected original graph failure") };
            try
            {
                var descriptor = new TextureDescriptor(17, 13)
                {
                    name = "FailureContractTexture", dimension = TextureDimension.Tex2D,
                    colorFormat = GraphicsFormat.R16G16B16A16_SFloat, enableRandomWrite = true
                };
                RGTextureRef texture = graph.CreateTexture(descriptor);
                using (RGComputePassRef pass = graph.AddComputePass<FailurePassData>(new ProfilingSampler("FailureContractProducer")))
                {
                    pass.EnablePassCulling(false);
                    pass.SetQueueObserver(observed);
                    pass.EnableAsyncCompute(asyncCompute);
                    ref FailurePassData data = ref pass.GetPassData<FailurePassData>();
                    data.observation = observed;
                    data.texture = pass.WriteTexture(texture);
                    pass.SetExecuteFunc((in FailurePassData parameters, in RGComputeEncoder commands, RGObjectPool pool) =>
                    {
                        parameters.observation.allocated = parameters.texture;
                        InjectFailure(parameters.observation.error);
                    });
                }
                using (RGTransferPassRef pass = graph.AddTransferPass<ConsumerPassData>(new ProfilingSampler("FailureContractConsumer")))
                {
                    pass.EnablePassCulling(false);
                    pass.GetPassData<ConsumerPassData>().texture = pass.ReadTexture(texture);
                    pass.SetExecuteFunc((in ConsumerPassData data, in RGTransferEncoder commands, RGObjectPool pool) => { });
                }
                Exception actual = Assert.Throws<InvalidOperationException>(() => graph.Execute(default, resources, frameCommands));
                Assert.AreSame(observed.error, actual);
                Assert.AreEqual(0, observed.queued, "A failed recording must not publish production or capture ownership.");
                StringAssert.Contains(nameof(InjectFailure), actual.StackTrace);
                Assert.AreEqual(0, frameCommands.sizeInBytes, "Failed pass commands must not contaminate the enclosing frame buffer.");
                Assert.IsNull(RGResourceFactory.current);
                Assert.IsNotNull(observed.allocated);
                Assert.IsTrue(observed.allocated.IsCreated());
                Assert.AreEqual(1, graph.RetiredResourceCount);
                graph.ClearRecordedGraph();
                Assert.AreEqual(1, graph.RetiredResourceCount, "Repeated logical cleanup must not lose retirement ownership.");
                // This fixture throws before submitting any GPU commands; explicit drain is safe here.
                graph.FlushRetiredResources();
                Assert.AreEqual(0, graph.RetiredResourceCount);
            }
            finally
            {
                graph.Dispose(); resources.Dispose(); frameCommands.Dispose();
            }
        }
    }
}
