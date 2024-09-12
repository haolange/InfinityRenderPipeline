using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.RenderGraph
{
    public struct RGTransferEncoder
    {
        internal CommandBuffer m_CommandBuffer;

        internal RGTransferEncoder(CommandBuffer commandBuffer)
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

        public void CopyTexture(in RenderTargetIdentifier src, in int srcElement, in int srcMip, in int srcX, in int srcY, in int srcWidth, in int srcHeight, in RenderTargetIdentifier dst, in int dstElement, in int dstMip, in int dstX, in int dstY)
        {
            m_CommandBuffer.CopyTexture(src, srcElement, srcMip, srcX, srcY, srcWidth, srcHeight, dst, dstElement, dstMip, dstX, dstY);
        }
    }

    public struct RGComputeEncoder
    {
        internal CommandBuffer m_CommandBuffer;

        internal RGComputeEncoder(CommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
        }

        public void DispatchCompute(ComputeShader computeShader, in int kernelIndex, in int threadGroupsX, in int threadGroupsY, in int threadGroupsZ)
        {
            m_CommandBuffer.DispatchCompute(computeShader, kernelIndex, threadGroupsX, threadGroupsY, threadGroupsZ);
        }

        public void DispatchCompute(ComputeShader computeShader, in int kernelIndex, ComputeBuffer indirectBuffer, in uint argsOffset)
        {
            m_CommandBuffer.DispatchCompute(computeShader, kernelIndex, indirectBuffer, argsOffset);
        }

        public void DispatchCompute(ComputeShader computeShader, in int kernelIndex, GraphicsBuffer indirectBuffer, in uint argsOffset)
        {
            m_CommandBuffer.DispatchCompute(computeShader, kernelIndex, indirectBuffer, argsOffset);
        }
    }

    public struct RGRaytracingEncoder
    {
        internal CommandBuffer m_CommandBuffer;

        internal RGRaytracingEncoder(CommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
        }

        public void DispatchRays(RayTracingShader rayTracingShader, string rayGenName, in uint width, in uint height, in uint depth, Camera camera)
        {
            m_CommandBuffer.DispatchRays(rayTracingShader, rayGenName, width, height, depth, camera);
        }

        public void DispatchRays(RayTracingShader rayTracingShader, string rayGenName, GraphicsBuffer argsBuffer, in uint argsOffset, Camera camera)
        {
            m_CommandBuffer.DispatchRays(rayTracingShader, rayGenName, argsBuffer, argsOffset, camera);
        }
    }

    public struct RGRasterEncoder
    {
        internal CommandBuffer m_CommandBuffer;

        internal RGRasterEncoder(CommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
        }

        public void SetViewport(in Rect pixelRect)
        {
            m_CommandBuffer.SetViewport(pixelRect);
        }

        public void SetGlobalDepthBias(in float bias, in float slopeBias)
        {
            m_CommandBuffer.SetGlobalDepthBias(bias, slopeBias);
        }

        public void SetInvertCulling(in bool invertCulling)
        {
            m_CommandBuffer.SetInvertCulling(invertCulling);
        }

        public void EnableScissorRect(in  Rect scissor) 
        { 
            m_CommandBuffer.EnableScissorRect(scissor); 
        }

        public void DisableScissorRect() 
        { 
            m_CommandBuffer.DisableScissorRect(); 
        }

        public void NextSubPass()
        {
            m_CommandBuffer.NextSubPass();
        }

        public void DrawMesh(Mesh mesh, in Matrix4x4 matrix, Material material, in int submeshIndex, in int shaderPass, MaterialPropertyBlock properties)
        {
            m_CommandBuffer.DrawMesh(mesh, matrix, material, submeshIndex, shaderPass, properties);
        }

        public void DrawMesh(Mesh mesh, in Matrix4x4 matrix, Material material, in int submeshIndex, in int shaderPass)
        {
            m_CommandBuffer.DrawMesh(mesh, matrix, material, submeshIndex, shaderPass);
        }

        public void DrawMesh(Mesh mesh, in Matrix4x4 matrix, Material material, in int submeshIndex)
        {
            m_CommandBuffer.DrawMesh(mesh, matrix, material, submeshIndex);
        }

        public void DrawMesh(Mesh mesh, in Matrix4x4 matrix, Material material)
        {
            m_CommandBuffer.DrawMesh(mesh, matrix, material);
        }

        public void DrawMultipleMeshes(Matrix4x4[] matrices, Mesh[] meshes, int[] subsetIndices, in int count, Material material, in int shaderPass, MaterialPropertyBlock properties)
        {
            m_CommandBuffer.DrawMultipleMeshes(matrices, meshes, subsetIndices, count, material, shaderPass, properties);
        }

        public void DrawRenderer(Renderer renderer, Material material, in int submeshIndex, in int shaderPass)
        {
            m_CommandBuffer.DrawRenderer(renderer, material, submeshIndex, shaderPass);
        }

        public void DrawRenderer(Renderer renderer, Material material, in int submeshIndex)
        {
            m_CommandBuffer.DrawRenderer(renderer, material, submeshIndex);
        }

        public void DrawRenderer(Renderer renderer, Material material)
        {
            m_CommandBuffer.DrawRenderer(renderer, material);
        }

        public void DrawRendererList(in RendererList rendererList)
        {
            m_CommandBuffer.DrawRendererList(rendererList);
        }

        public void DrawProcedural(in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, in int vertexCount, in int instanceCount, MaterialPropertyBlock properties)
        {
            m_CommandBuffer.DrawProcedural(matrix, material, shaderPass, topology, vertexCount, instanceCount, properties);
        }

        public void DrawProcedural(in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, in int vertexCount, in int instanceCount)
        {
            m_CommandBuffer.DrawProcedural(matrix, material, shaderPass, topology, vertexCount, instanceCount);
        }

        public void DrawProcedural(in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, in int vertexCount)
        {
            m_CommandBuffer.DrawProcedural(matrix, material, shaderPass, topology, vertexCount);
        }

        public void DrawProcedural(GraphicsBuffer indexBuffer, in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, in int indexCount, in int instanceCount, MaterialPropertyBlock properties)
        {
            m_CommandBuffer.DrawProcedural(indexBuffer, matrix, material, shaderPass, topology, indexCount, instanceCount, properties);
        }

        public void DrawProcedural(GraphicsBuffer indexBuffer, in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, in int indexCount, in int instanceCount)
        {
            m_CommandBuffer.DrawProcedural(indexBuffer, matrix, material, shaderPass, topology, indexCount, instanceCount);
        }

        public void DrawProcedural(GraphicsBuffer indexBuffer, in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, in int indexCount)
        {
            m_CommandBuffer.DrawProcedural(indexBuffer, matrix, material, shaderPass, topology, indexCount);
        }

        public void DrawProceduralIndirect(in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, ComputeBuffer bufferWithArgs, in int argsOffset, MaterialPropertyBlock properties)
        {
            m_CommandBuffer.DrawProceduralIndirect(matrix, material,shaderPass,topology, bufferWithArgs,argsOffset, properties);
        }

        public void DrawProceduralIndirect(in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, ComputeBuffer bufferWithArgs, in int argsOffset)
        {
            m_CommandBuffer.DrawProceduralIndirect(matrix, material, shaderPass, topology, bufferWithArgs, argsOffset);
        }

        public void DrawProceduralIndirect(in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, ComputeBuffer bufferWithArgs)
        {
            m_CommandBuffer.DrawProceduralIndirect(matrix, material, shaderPass, topology, bufferWithArgs);
        }

        public void DrawProceduralIndirect(GraphicsBuffer indexBuffer, in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, ComputeBuffer bufferWithArgs, in int argsOffset, MaterialPropertyBlock properties)
        {
            m_CommandBuffer.DrawProceduralIndirect(indexBuffer,matrix, material, shaderPass, topology, bufferWithArgs, argsOffset, properties);
        }

        public void DrawProceduralIndirect(GraphicsBuffer indexBuffer, in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, ComputeBuffer bufferWithArgs, in int argsOffset)
        {
            m_CommandBuffer.DrawProceduralIndirect(indexBuffer, matrix, material, shaderPass, topology, bufferWithArgs, argsOffset);
        }

        public void DrawProceduralIndirect(GraphicsBuffer indexBuffer, in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, ComputeBuffer bufferWithArgs)
        {
            m_CommandBuffer.DrawProceduralIndirect(indexBuffer, matrix, material, shaderPass, topology, bufferWithArgs);
        }

        public void DrawProceduralIndirect(in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, GraphicsBuffer bufferWithArgs, in int argsOffset, MaterialPropertyBlock properties)
        {
            m_CommandBuffer.DrawProceduralIndirect(matrix, material, shaderPass, topology, bufferWithArgs, argsOffset, properties);
        }

        public void DrawProceduralIndirect(in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, GraphicsBuffer bufferWithArgs, in int argsOffset)
        {
            m_CommandBuffer.DrawProceduralIndirect(matrix, material, shaderPass, topology, bufferWithArgs, argsOffset);
        }

        public void DrawProceduralIndirect(in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, GraphicsBuffer bufferWithArgs)
        {
            m_CommandBuffer.DrawProceduralIndirect(matrix, material, shaderPass, topology, bufferWithArgs);
        }

        public void DrawProceduralIndirect(GraphicsBuffer indexBuffer, in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, GraphicsBuffer bufferWithArgs, in int argsOffset, MaterialPropertyBlock properties)
        {
            m_CommandBuffer.DrawProceduralIndirect(indexBuffer, matrix, material, shaderPass, topology, bufferWithArgs, argsOffset, properties);
        }

        public void DrawProceduralIndirect(GraphicsBuffer indexBuffer, in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, GraphicsBuffer bufferWithArgs, in int argsOffset)
        {
            m_CommandBuffer.DrawProceduralIndirect(indexBuffer, matrix, material, shaderPass, topology, bufferWithArgs, argsOffset);
        }

        public void DrawProceduralIndirect(GraphicsBuffer indexBuffer, in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, GraphicsBuffer bufferWithArgs)
        {
            m_CommandBuffer.DrawProceduralIndirect(indexBuffer, matrix, material, shaderPass, topology, bufferWithArgs);
        }

        public void DrawMeshInstanced(Mesh mesh, in int submeshIndex, Material material, in int shaderPass, Matrix4x4[] matrices, in int count, MaterialPropertyBlock properties)
        {
            m_CommandBuffer.DrawMeshInstanced(mesh, submeshIndex, material, shaderPass, matrices, count, properties);
        }

        public void DrawMeshInstanced(Mesh mesh, in int submeshIndex, Material material, in int shaderPass, Matrix4x4[] matrices, in int count)
        {
            m_CommandBuffer.DrawMeshInstanced(mesh, submeshIndex, material, shaderPass, matrices, count);
        }

        public void DrawMeshInstanced(Mesh mesh, in int submeshIndex, Material material, in int shaderPass, Matrix4x4[] matrices)
        {
            m_CommandBuffer.DrawMeshInstanced(mesh, submeshIndex, material, shaderPass, matrices);
        }

        public void DrawMeshInstancedProcedural(Mesh mesh, in int submeshIndex, Material material, in int shaderPass, in int count, MaterialPropertyBlock properties)
        {
            m_CommandBuffer.DrawMeshInstancedProcedural(mesh, submeshIndex, material, shaderPass, count, properties);
        }

        public void DrawMeshInstancedIndirect(Mesh mesh, in int submeshIndex, Material material, in int shaderPass, ComputeBuffer bufferWithArgs, in int argsOffset, MaterialPropertyBlock properties)
        {
            m_CommandBuffer.DrawMeshInstancedIndirect(mesh, submeshIndex, material, shaderPass, bufferWithArgs, argsOffset, properties);
        }

        public void DrawMeshInstancedIndirect(Mesh mesh, in int submeshIndex, Material material, in int shaderPass, ComputeBuffer bufferWithArgs, in int argsOffset)
        {
            m_CommandBuffer.DrawMeshInstancedIndirect(mesh, submeshIndex, material, shaderPass, bufferWithArgs, argsOffset);
        }

        public void DrawMeshInstancedIndirect(Mesh mesh, in int submeshIndex, Material material, in int shaderPass, ComputeBuffer bufferWithArgs)
        {
            m_CommandBuffer.DrawMeshInstancedIndirect(mesh, submeshIndex, material, shaderPass, bufferWithArgs);
        }

        public void DrawMeshInstancedIndirect(Mesh mesh, in int submeshIndex, Material material, in int shaderPass, GraphicsBuffer bufferWithArgs, in int argsOffset, MaterialPropertyBlock properties)
        {
            m_CommandBuffer.DrawMeshInstancedIndirect(mesh, submeshIndex, material, shaderPass, bufferWithArgs, argsOffset, properties);
        }

        public void DrawMeshInstancedIndirect(Mesh mesh, in int submeshIndex, Material material, in int shaderPass, GraphicsBuffer bufferWithArgs, in int argsOffset)
        {
            m_CommandBuffer.DrawMeshInstancedIndirect(mesh, submeshIndex, material, shaderPass, bufferWithArgs, argsOffset);
        }

        public void DrawMeshInstancedIndirect(Mesh mesh, in int submeshIndex, Material material, in int shaderPass, GraphicsBuffer bufferWithArgs)
        {
            m_CommandBuffer.DrawMeshInstancedIndirect(mesh, submeshIndex, material, shaderPass, bufferWithArgs);
        }

        public void DrawOcclusionMesh(in RectInt normalizedCamViewport)
        {
            m_CommandBuffer.DrawOcclusionMesh(normalizedCamViewport);
        }
    }
}