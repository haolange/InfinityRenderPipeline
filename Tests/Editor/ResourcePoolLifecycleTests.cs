using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class ResourcePoolLifecycleTests
    {
        [Test]
        public void BufferReturnWithoutFence_PreservesUnsubmittedRecordingAcrossCameras()
        {
            var pool = new ResourcePool();
            var first = new RGBuilder("FirstCamera", pool);
            var second = new RGBuilder("SecondCamera", pool);
            var descriptor = new BufferDescriptor(16, 4);
            var original = pool.GetBuffer(descriptor);
            first.ImportBuffer(original);
            second.ImportBuffer(original);
            pool.ReleaseBuffer(original);
            var replacement = pool.GetBuffer(descriptor);
            try
            {
                Assert.AreNotSame(original.buffer, replacement.buffer);
                first.ClearRecordedGraph();
                Assert.IsFalse(pool.BufferUsage(original.buffer).CanWriteCpu, "The second camera still owns a recording reference.");
                second.ClearRecordedGraph();
                var reused = pool.GetBuffer(descriptor);
                Assert.AreSame(original.buffer, reused.buffer, "Cancellation of both recordings must release the unused allocation.");
                pool.ReleaseBuffer(reused);
            }
            finally { first.Dispose(); second.Dispose(); pool.ReleaseBuffer(replacement); pool.Dispose(); }
        }

        [Test]
        public void TextureReturnWithoutFence_PreservesRecordedPhysicalAllocation()
        {
            var pool = new ResourcePool();
            var graph = new RGBuilder("TextureRecording", pool);
            var second = new RGBuilder("SecondTextureCamera", pool);
            var descriptor = new TextureDescriptor(16, 16) { colorFormat = GraphicsFormat.R32_SFloat, dimension = TextureDimension.Tex2D };
            var original = pool.GetTexture(descriptor);
            graph.ImportTexture(original);
            second.ImportTexture(original);
            pool.ReleaseTexture(original);
            var replacement = pool.GetTexture(descriptor);
            try
            {
                Assert.AreNotSame(original.texture, replacement.texture);
                graph.ClearRecordedGraph();
                Assert.IsFalse(pool.TextureUsage(original.texture).CanWriteCpu,
                    "Cancelling the first camera must not cancel the second camera's texture reference.");
                second.ClearRecordedGraph();
                var reused = pool.GetTexture(descriptor);
                Assert.AreSame(original.texture, reused.texture);
                pool.ReleaseTexture(reused);
            }
            finally { graph.Dispose(); second.Dispose(); pool.ReleaseTexture(replacement); pool.Dispose(); }
        }

        [Test]
        public void PoolDisposal_DoesNotDestroyAnUnsubmittedRecordedBuffer()
        {
            var pool = new ResourcePool();
            var graph = new RGBuilder("DisposeBeforeSubmission", pool);
            var resource = pool.GetBuffer(new BufferDescriptor(4, 4));
            graph.ImportBuffer(resource);
            pool.ReleaseBuffer(resource);
            pool.Dispose();
            Assert.IsTrue(resource.buffer.IsValid(), "Dispose cannot invalidate a still-recorded resource.");
            graph.ClearRecordedGraph();
            graph.Dispose();
            pool.Dispose();
            Assert.IsFalse(resource.buffer.IsValid(), "Cancelling the final recording makes destruction safe.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void QueuedUse_IsNotCpuWritable_AndOnlySameQueueReusesWithoutFence(bool asyncCompute)
        {
            var usage = new ResourceUsage();
            usage.QueueAttempt(7, asyncCompute, default);
            Assert.IsFalse(usage.CanWriteCpu);
            Assert.IsTrue(usage.CanAcquireCommands(asyncCompute, 7, out _));
            Assert.IsFalse(usage.CanAcquireCommands(!asyncCompute, 7, out _));
            Assert.IsFalse(usage.CanAcquireCommands(asyncCompute, 8, out _), "Advancing a CPU frame cannot accept a previous submission.");
            usage.Submitted(7, default, false, default);
            Assert.IsFalse(usage.CanWriteCpu, "Submit without a completion proof is insufficient for a CPU upload.");
            Assert.IsTrue(usage.CanAcquireCommands(asyncCompute, 8, out _));
        }

        [Test]
        public void PayloadReuseAndGrowth_DoNotReleaseARecordedOldBuffer()
        {
            var pool = new ResourcePool();
            var graph = new RGBuilder("RecordedPayload", pool);
            var payload = MeshDrawGPUBackend.RentPayload();
            MeshDrawGpuPayload next = null;
            try
            {
                payload.EnsureCapacity(pool, 1, 1);
                var original = payload.argsBuffer;
                var descriptor = new BufferDescriptor(original.count, 4, ComputeBufferType.IndirectArguments);
                graph.ImportBuffer(new BufferHandle(-1, descriptor, original));
                MeshDrawGPUBackend.RetirePayload(payload); payload = null;
                MeshDrawGPUBackend.FlushRetiredPayloads();
                next = MeshDrawGPUBackend.RentPayload();
                next.EnsureCapacity(pool, 128, 1024);
                Assert.IsTrue(original.IsValid(), "Payload growth must not physically release commands recorded before Submit.");
                Assert.AreNotSame(original, next.argsBuffer);
                graph.ClearRecordedGraph();
                var reused = pool.GetBuffer(descriptor);
                Assert.AreSame(original, reused.buffer, "Cancelled recording releases the original allocation for safe reuse.");
                pool.ReleaseBuffer(reused);
            }
            finally
            {
                graph.Dispose();
                if (payload != null) MeshDrawGPUBackend.ReturnPayload(payload);
                if (next != null) MeshDrawGPUBackend.ReturnPayload(next);
                pool.Dispose();
            }
        }

        [Test]
        public void CameraFailure_DoesNotStartANewSubmissionBatchInsideTheSameFrame()
        {
            var pool = new ResourcePool();
            pool.BeginFrame();
            int epoch = pool.recordingEpoch;
            pool.SubmissionUncertain();
            pool.EnsureRecording();
            Assert.AreEqual(epoch, pool.recordingEpoch);
            pool.BeginFrame();
            Assert.AreNotEqual(epoch, pool.recordingEpoch);
            pool.Dispose();
        }

        [Test]
        public void KnownNativeSubmission_WithMissingFenceWaitsForRecoveryAcknowledgement()
        {
            var pool = new ResourcePool();
            var usage = new ResourceUsage();
            pool.QueueAttempt(usage, false, default);
            pool.SubmissionNeedsJoin();
            pool.BeginFrame();
            // State-only fixture: no native allocation, and no default fence is queried.
            pool.AfterSubmit(default);
            Assert.IsTrue(usage.pending, "A recovery fence cannot be assumed queued before the recovery join succeeds.");
            pool.QueueDrainAccepted();
            pool.AfterSubmit(default);
            Assert.IsFalse(usage.pending);
            pool.Dispose();
        }

        [Test]
        public void NewFrameSubmission_DoesNotAcknowledgeFailedUnknownOldSubmission()
        {
            var pool = new ResourcePool();
            var usage = new ResourceUsage();
            pool.QueueAttempt(usage, false, default);
            pool.SubmissionUncertain(true);
            pool.BeginFrame();
            // No native resource is attached to this state fixture. Only batch membership
            // is checked; a default fence is never queried as completion evidence.
            pool.AfterSubmit(default);
            Assert.IsTrue(usage.pending);
            Assert.IsFalse(usage.CanWriteCpu);
            pool.Dispose();
        }

        [Test]
        public void CompletingNewBatch_DoesNotAcknowledgeAnOlderUnsubmittedBatch()
        {
            var usage = new ResourceUsage();
            usage.QueueAttempt(10, false, default);
            usage.QueueAttempt(11, false, default);
            usage.Submitted(11, default, false, default);
            Assert.IsTrue(usage.pending);
            Assert.IsFalse(usage.CanWriteCpu);
            Assert.IsFalse(usage.CanAcquireCommands(false, 12, out _));
            usage.Submitted(10, default, false, default);
            Assert.IsFalse(usage.pending);
            Assert.IsFalse(usage.CanWriteCpu, "All batches submitted still does not prove CPU completion.");
        }

        [Test]
        public void CrossQueueWait_DoesNotAuthorizeCpuUpload()
        {
            var usage = new ResourceUsage();
            usage.QueueAttempt(4, true, new ResourceReuseFence { hasCompute = true });
            Assert.IsTrue(usage.CanAcquireCommands(false, 4, out var wait));
            Assert.IsTrue(wait.hasCompute);
            Assert.IsFalse(usage.CanWriteCpu, "A wait that will execute on the GPU cannot order an immediate CPU SetData.");
        }

        [Test]
        public void FailureAndRecordingCancellation_DoNotEraseAlreadyQueuedUse()
        {
            var usage = new ResourceUsage();
            usage.RetainRecording();
            usage.QueueAttempt(2, false, default);
            usage.MarkUncertain();
            usage.ReleaseRecording();
            Assert.IsFalse(usage.CanWriteCpu);
            Assert.IsFalse(usage.CanAcquireCommands(false, 2, out _));
            Assert.IsFalse(usage.CanAcquireCommands(false, 3, out _));
            usage.Submitted(2, default, false, new ResourceReuseFence { hasGraphics = true });
            Assert.IsFalse(usage.CanWriteCpu);
            Assert.IsTrue(usage.CanAcquireCommands(false, 3, out _), "A successfully drained/submitted queue restores ordered GPU reuse only.");
        }
    }
}
