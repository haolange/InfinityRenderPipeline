using NUnit.Framework;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.MeshPipeline.Tests
{
    public class RGDrawListLifetimeTests
    {
        [Test]
        public void Payload_RetireStaysOutOfPoolUntilFlush()
        {
            MeshDrawGPUBackend.FlushRetiredPayloads();
            MeshDrawGpuPayload payload = MeshDrawGPUBackend.RentPayload();
            MeshDrawGPUBackend.RetirePayload(payload);

            MeshDrawGpuPayload other = MeshDrawGPUBackend.RentPayload();
            Assert.AreNotSame(payload, other);

            MeshDrawGPUBackend.ReturnPayload(other);
            MeshDrawGPUBackend.FlushRetiredPayloads();

            MeshDrawGpuPayload again = MeshDrawGPUBackend.RentPayload();
            Assert.AreSame(payload, again);
            MeshDrawGPUBackend.ReturnPayload(again);
        }
    }
}
