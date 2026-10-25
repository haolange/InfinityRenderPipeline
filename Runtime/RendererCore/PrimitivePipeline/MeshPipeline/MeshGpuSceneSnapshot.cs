using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.MeshPipeline
{
    // A DrawList binds the resident tables selected at declaration, not mutable facade fields.
    internal readonly struct MeshGpuSceneSnapshot
    {
        internal readonly FBufferRef TransformBuffer, RenderingLayerBuffer, InstanceTransformIndexBuffer;
        internal readonly MeshTransformVersion version;
        internal bool isValid => TransformBuffer.buffer != null && RenderingLayerBuffer.buffer != null
            && InstanceTransformIndexBuffer.buffer != null;
        internal MeshGpuSceneSnapshot(GpuScene scene)
        {
            TransformBuffer = scene.TransformBuffer; RenderingLayerBuffer = scene.RenderingLayerBuffer;
            InstanceTransformIndexBuffer = scene.InstanceTransformIndexBuffer;
            version = scene.transformVersions.current;
        }
    }
}
