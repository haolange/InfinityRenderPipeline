using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.RenderGraph
{
    public interface ITransferCommands
    {
        void CopyBuffer(GraphicsBuffer src, GraphicsBuffer dst);
        void CopyTexture(in RenderTargetIdentifier src, in RenderTargetIdentifier dst);
        void CopyTexture(in RenderTargetIdentifier src, in int srcElement, in RenderTargetIdentifier dst, in int dstElement);
        void CopyTexture(in RenderTargetIdentifier src, in int srcElement, in int srcMip, in RenderTargetIdentifier dst, in int dstElement, in int dstMip);
    }

    public interface IComputeCommands
    {
        void SetComputeFloatParam(ComputeShader computeShader, int nameID, float val);
        void SetComputeIntParam(ComputeShader computeShader, int nameID, int val);
        void SetComputeVectorParam(ComputeShader computeShader, int nameID, Vector4 val);
        void SetComputeMatrixParam(ComputeShader computeShader, int nameID, Matrix4x4 val);
        void SetComputeTextureParam(ComputeShader computeShader, int kernelIndex, int nameID, RenderTargetIdentifier rt);
        void SetComputeTextureParam(ComputeShader computeShader, int kernelIndex, int nameID, RenderTargetIdentifier rt, int mipLevel);
        void SetComputeBufferParam(ComputeShader computeShader, int kernelIndex, int nameID, ComputeBuffer buffer);
        void SetComputeConstantBufferParam(ComputeShader computeShader, int nameID, ComputeBuffer buffer, int offset, int size);
        void DispatchCompute(ComputeShader computeShader, in int kernelIndex, in int threadGroupsX, in int threadGroupsY, in int threadGroupsZ);
        void CopyTexture(in RenderTargetIdentifier src, in RenderTargetIdentifier dst);
        void BeginSample(string name);
        void EndSample(string name);
        void BeginSample(ProfilingSampler sampler);
        void EndSample(ProfilingSampler sampler);
    }

    public interface IRaytracingCommands
    {
        void SetRayTracingShaderPass(RayTracingShader rayTracingShader, string passName);
        void SetRayTracingAccelerationStructure(RayTracingShader rayTracingShader, string name, RayTracingAccelerationStructure rayTracingAccelerationStructure);
        void SetRayTracingIntParam(RayTracingShader rayTracingShader, int nameID, int val);
        void SetRayTracingFloatParam(RayTracingShader rayTracingShader, int nameID, float val);
        void SetRayTracingVectorParam(RayTracingShader rayTracingShader, int nameID, Vector4 val);
        void SetRayTracingMatrixParam(RayTracingShader rayTracingShader, int nameID, Matrix4x4 val);
        void SetRayTracingTextureParam(RayTracingShader rayTracingShader, int nameID, RenderTargetIdentifier rt);
        void DispatchRays(RayTracingShader rayTracingShader, string rayGenName, in uint width, in uint height, in uint depth, Camera camera);
    }

    public interface IRasterCommands
    {
        void SetViewport(in Rect pixelRect);
        void SetGlobalInt(int nameID, int value);
        void SetGlobalVector(int nameID, Vector4 value);
        void SetGlobalTexture(int nameID, RenderTargetIdentifier value);
        void DrawMesh(Mesh mesh, in Matrix4x4 matrix, Material material, in int submeshIndex, in int shaderPass);
        void DrawRendererList(in RendererList rendererList);
    }

    public readonly struct CommandBufferCommands : ITransferCommands, IComputeCommands, IRaytracingCommands, IRasterCommands
    {
        readonly CommandBuffer m_CommandBuffer;

        public CommandBufferCommands(CommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
        }

        public void CopyBuffer(GraphicsBuffer src, GraphicsBuffer dst)
        {
            m_CommandBuffer.CopyBuffer(src, dst);
        }

        public void CopyTexture(in RenderTargetIdentifier src, in RenderTargetIdentifier dst)
        {
            m_CommandBuffer.CopyTexture(src, dst);
        }

        public void CopyTexture(in RenderTargetIdentifier src, in int srcElement, in RenderTargetIdentifier dst, in int dstElement)
        {
            m_CommandBuffer.CopyTexture(src, srcElement, dst, dstElement);
        }

        public void CopyTexture(in RenderTargetIdentifier src, in int srcElement, in int srcMip, in RenderTargetIdentifier dst, in int dstElement, in int dstMip)
        {
            m_CommandBuffer.CopyTexture(src, srcElement, srcMip, dst, dstElement, dstMip);
        }

        public void SetComputeFloatParam(ComputeShader computeShader, int nameID, float val)
        {
            m_CommandBuffer.SetComputeFloatParam(computeShader, nameID, val);
        }

        public void SetComputeIntParam(ComputeShader computeShader, int nameID, int val)
        {
            m_CommandBuffer.SetComputeIntParam(computeShader, nameID, val);
        }

        public void SetComputeVectorParam(ComputeShader computeShader, int nameID, Vector4 val)
        {
            m_CommandBuffer.SetComputeVectorParam(computeShader, nameID, val);
        }

        public void SetComputeMatrixParam(ComputeShader computeShader, int nameID, Matrix4x4 val)
        {
            m_CommandBuffer.SetComputeMatrixParam(computeShader, nameID, val);
        }

        public void SetComputeTextureParam(ComputeShader computeShader, int kernelIndex, int nameID, RenderTargetIdentifier rt)
        {
            m_CommandBuffer.SetComputeTextureParam(computeShader, kernelIndex, nameID, rt);
        }

        public void SetComputeTextureParam(ComputeShader computeShader, int kernelIndex, int nameID, RenderTargetIdentifier rt, int mipLevel)
        {
            m_CommandBuffer.SetComputeTextureParam(computeShader, kernelIndex, nameID, rt, mipLevel);
        }

        public void SetComputeBufferParam(ComputeShader computeShader, int kernelIndex, int nameID, ComputeBuffer buffer)
        {
            m_CommandBuffer.SetComputeBufferParam(computeShader, kernelIndex, nameID, buffer);
        }

        public void SetComputeConstantBufferParam(ComputeShader computeShader, int nameID, ComputeBuffer buffer, int offset, int size)
        {
            m_CommandBuffer.SetComputeConstantBufferParam(computeShader, nameID, buffer, offset, size);
        }

        public void DispatchCompute(ComputeShader computeShader, in int kernelIndex, in int threadGroupsX, in int threadGroupsY, in int threadGroupsZ)
        {
            m_CommandBuffer.DispatchCompute(computeShader, kernelIndex, threadGroupsX, threadGroupsY, threadGroupsZ);
        }

        public void BeginSample(string name)
        {
            m_CommandBuffer.BeginSample(name);
        }

        public void EndSample(string name)
        {
            m_CommandBuffer.EndSample(name);
        }

        public void BeginSample(ProfilingSampler sampler)
        {
            sampler?.Begin(m_CommandBuffer);
        }

        public void EndSample(ProfilingSampler sampler)
        {
            sampler?.End(m_CommandBuffer);
        }

        public void SetRayTracingShaderPass(RayTracingShader rayTracingShader, string passName)
        {
            m_CommandBuffer.SetRayTracingShaderPass(rayTracingShader, passName);
        }

        public void SetRayTracingAccelerationStructure(RayTracingShader rayTracingShader, string name, RayTracingAccelerationStructure rayTracingAccelerationStructure)
        {
            m_CommandBuffer.SetRayTracingAccelerationStructure(rayTracingShader, name, rayTracingAccelerationStructure);
        }

        public void SetRayTracingIntParam(RayTracingShader rayTracingShader, int nameID, int val)
        {
            m_CommandBuffer.SetRayTracingIntParam(rayTracingShader, nameID, val);
        }

        public void SetRayTracingFloatParam(RayTracingShader rayTracingShader, int nameID, float val)
        {
            m_CommandBuffer.SetRayTracingFloatParam(rayTracingShader, nameID, val);
        }

        public void SetRayTracingVectorParam(RayTracingShader rayTracingShader, int nameID, Vector4 val)
        {
            m_CommandBuffer.SetRayTracingVectorParam(rayTracingShader, nameID, val);
        }

        public void SetRayTracingMatrixParam(RayTracingShader rayTracingShader, int nameID, Matrix4x4 val)
        {
            m_CommandBuffer.SetRayTracingMatrixParam(rayTracingShader, nameID, val);
        }

        public void SetRayTracingTextureParam(RayTracingShader rayTracingShader, int nameID, RenderTargetIdentifier rt)
        {
            m_CommandBuffer.SetRayTracingTextureParam(rayTracingShader, nameID, rt);
        }

        public void DispatchRays(RayTracingShader rayTracingShader, string rayGenName, in uint width, in uint height, in uint depth, Camera camera)
        {
            m_CommandBuffer.DispatchRays(rayTracingShader, rayGenName, width, height, depth, camera);
        }

        public void SetViewport(in Rect pixelRect)
        {
            m_CommandBuffer.SetViewport(pixelRect);
        }

        public void SetGlobalInt(int nameID, int value)
        {
            m_CommandBuffer.SetGlobalInt(nameID, value);
        }

        public void SetGlobalVector(int nameID, Vector4 value)
        {
            m_CommandBuffer.SetGlobalVector(nameID, value);
        }

        public void SetGlobalTexture(int nameID, RenderTargetIdentifier value)
        {
            m_CommandBuffer.SetGlobalTexture(nameID, value);
        }

        public void DrawMesh(Mesh mesh, in Matrix4x4 matrix, Material material, in int submeshIndex, in int shaderPass)
        {
            m_CommandBuffer.DrawMesh(mesh, matrix, material, submeshIndex, shaderPass);
        }

        public void DrawRendererList(in RendererList rendererList)
        {
            m_CommandBuffer.DrawRendererList(rendererList);
        }
    }
}
