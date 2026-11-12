using UnityEngine;
using InfinityTech.Rendering.GPUResource;
using UnityEngine.Rendering;
using UnityEngine.Rendering.UnifiedRayTracing;

namespace InfinityTech.Rendering.RenderGraph
{
    public interface ITransferCommands
    {
        void SetBufferData<T>(GraphicsBuffer buffer, Unity.Collections.NativeArray<T> data) where T : struct;
        void SetBufferData(GraphicsBuffer buffer, System.Array data, int sourceIndex, int destinationIndex, int count);
        void CopyBuffer(GraphicsBuffer src, GraphicsBuffer dst);
        void CopyTexture(RGTextureHandle src, RGTextureHandle dst);
        void CopyTexture(RGTextureHandle src, int srcElement, RGTextureHandle dst, int dstElement);
        void CopyTexture(RGTextureHandle src, int srcElement, int srcMip, RGTextureHandle dst, int dstElement, int dstMip);
        void RequestAsyncReadback(GraphicsBuffer source, System.Action<AsyncGPUReadbackRequest> completion);
        void RequestAsyncReadback(Texture source, System.Action<AsyncGPUReadbackRequest> completion);
    }

    public interface IComputeCommands
    {
        void SetKeyword(GlobalKeyword keyword, bool value);
        void SetKeyword(ComputeShader computeShader, in LocalKeyword keyword, bool value);
        void SetComputeFloatParam(ComputeShader computeShader, int nameID, float val);
        void SetComputeIntParam(ComputeShader computeShader, int nameID, int val);
        void SetComputeVectorParam(ComputeShader computeShader, int nameID, Vector4 val);
        void SetComputeVectorArrayParam(ComputeShader computeShader, int nameID, Vector4[] values);
        void SetComputeMatrixParam(ComputeShader computeShader, int nameID, Matrix4x4 val);
        void SetComputeMatrixArrayParam(ComputeShader computeShader, int nameID, Matrix4x4[] values);
        // A graph texture binds its logical view to the property's view companion; a texture outside the graph cannot bind to a texture-view property.
        void SetComputeTextureParam(ComputeShader computeShader, int kernelIndex, int nameID, RGTextureHandle texture);
        void SetComputeTextureParam(ComputeShader computeShader, int kernelIndex, int nameID, RGTextureHandle texture, int mipLevel);
        void SetComputeTextureParam(ComputeShader computeShader, int kernelIndex, int nameID, RenderTargetIdentifier rt);
        void SetComputeTextureParam(ComputeShader computeShader, int kernelIndex, int nameID, RenderTargetIdentifier rt, int mipLevel);
        // A texture outside the graph states its own logical view for a texture-view property.
        void SetComputeTextureParam(ComputeShader computeShader, int kernelIndex, int nameID, RenderTargetIdentifier rt, in TextureView view);
        void SetComputeBufferParam(ComputeShader computeShader, int kernelIndex, int nameID, ComputeBuffer buffer);
        void SetComputeBufferParam(ComputeShader computeShader, int kernelIndex, int nameID, RGBufferHandle buffer);
        void SetComputeConstantBufferParam(ComputeShader computeShader, int nameID, ComputeBuffer buffer, int offset, int size);
        void DispatchCompute(ComputeShader computeShader, in int kernelIndex, in int threadGroupsX, in int threadGroupsY, in int threadGroupsZ);
        // Thread-group counts come from three uints at argsOffset (bytes) in a graph buffer created with
        // ComputeBufferType.IndirectArguments. The pass must declare the buffer with ReadBuffer.
        void DispatchComputeIndirect(ComputeShader computeShader, in int kernelIndex, RGBufferHandle argsBuffer, in uint argsOffset);
        void CopyTexture(RGTextureHandle src, RGTextureHandle dst);
        void BeginSample(ProfilingSampler sampler);
        void EndSample(ProfilingSampler sampler);
    }

    public interface IRaytracingCommands
    {
        void SetRayTracingShaderPass(RayTracingShader rayTracingShader, string passName);
        void SetRayTracingAccelerationStructure(RayTracingShader rayTracingShader, int nameID, RayTracingAccelerationStructure rayTracingAccelerationStructure);
        void SetRayTracingIntParam(RayTracingShader rayTracingShader, int nameID, int val);
        void SetRayTracingFloatParam(RayTracingShader rayTracingShader, int nameID, float val);
        void SetRayTracingVectorParam(RayTracingShader rayTracingShader, int nameID, Vector4 val);
        void SetRayTracingMatrixParam(RayTracingShader rayTracingShader, int nameID, Matrix4x4 val);
        void SetRayTracingTextureParam(RayTracingShader rayTracingShader, int nameID, RGTextureHandle texture);
        void SetRayTracingTextureParam(RayTracingShader rayTracingShader, int nameID, RenderTargetIdentifier rt);
        void DispatchRays(RayTracingShader rayTracingShader, string rayGenName, in uint width, in uint height, in uint depth, Camera camera);
        void BuildUnifiedAccel(IRayTracingAccelStruct accel, GraphicsBuffer scratch);
        // Unity's unified ray tracing shader has no int variant of the acceleration-structure binding.
        void SetUnifiedAccelerationStructure(IRayTracingShader shader, string name, IRayTracingAccelStruct accel);
        void SetUnifiedInt(IRayTracingShader shader, int nameID, int val);
        void SetUnifiedFloat(IRayTracingShader shader, int nameID, float val);
        void SetUnifiedVector(IRayTracingShader shader, int nameID, Vector4 val);
        void SetUnifiedMatrix(IRayTracingShader shader, int nameID, Matrix4x4 val);
        void SetUnifiedTexture(IRayTracingShader shader, int nameID, RGTextureHandle texture);
        void SetUnifiedTexture(IRayTracingShader shader, int nameID, RenderTargetIdentifier rt);
        void DispatchUnified(IRayTracingShader shader, GraphicsBuffer scratch, uint width, uint height, uint depth);
    }

    public interface IRasterCommands
    {
        void SetKeyword(GlobalKeyword keyword, bool value);
        void IssuePluginEventAndData(System.IntPtr callback, int eventId, System.IntPtr data);
        void SetViewport(in Rect pixelRect);
        void SetGlobalFloat(int nameID, float value);
        void SetGlobalBuffer(int nameID, ComputeBuffer value);
        void SetGlobalBuffer(int nameID, RGBufferHandle value);
        void SetGlobalBuffer(int nameID, GraphicsBuffer value);
        void SetGlobalInt(int nameID, int value);
        void SetGlobalVector(int nameID, Vector4 value);
        void SetGlobalVectorArray(int nameID, Vector4[] values);
        void SetGlobalMatrixArray(int nameID, Matrix4x4[] values);
        // A graph texture binds its logical view to the property's view companion; a texture outside the graph cannot bind to a texture-view property.
        void SetGlobalTexture(int nameID, RGTextureHandle texture);
        void SetGlobalTexture(int nameID, RenderTargetIdentifier value);
        // A texture outside the graph states its own logical view for a texture-view property.
        void SetGlobalTexture(int nameID, RenderTargetIdentifier value, in TextureView view);
        void DrawMesh(Mesh mesh, in Matrix4x4 matrix, Material material, in int submeshIndex, in int shaderPass);
        void DrawRendererList(in RendererList rendererList);
        void BeginSample(ProfilingSampler sampler);
        void EndSample(ProfilingSampler sampler);
    }
}
