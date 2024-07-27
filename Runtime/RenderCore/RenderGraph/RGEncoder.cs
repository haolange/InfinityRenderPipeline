using System;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.RenderGraph
{
    public struct RGTransferEncoder
    {
        internal CommandBuffer m_CommandBuffer;

        internal RGTransferEncoder(CommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
        }
    }

    public struct RGComputeEncoder
    {
        internal CommandBuffer m_CommandBuffer;

        internal RGComputeEncoder(CommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
        }
    }

    public struct RGRaytracingEncoder
    {
        internal CommandBuffer m_CommandBuffer;

        internal RGRaytracingEncoder(CommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
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

        public void EnableScissorRect(in  Rect scissor) 
        { 
            m_CommandBuffer.EnableScissorRect(scissor); 
        }

        public void DisableScissorRect() 
        { 
            m_CommandBuffer.DisableScissorRect(); 
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

        public void DrawMultipleMeshes(in Matrix4x4[] matrices, Mesh[] meshes, in int[] subsetIndices, in int count, Material material, in int shaderPass, MaterialPropertyBlock properties)
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

        }

        public void DrawRendererList(in RendererList rendererList)
        {

        }

        public void DrawProcedural(in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, in int vertexCount, in int instanceCount, MaterialPropertyBlock properties)
        {

        }

        public void DrawProcedural(in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, in int vertexCount, in int instanceCount)
        {

        }

        public void DrawProcedural(in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, in int vertexCount)
        {

        }

        public void DrawProcedural(GraphicsBuffer indexBuffer, in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, in int indexCount, in int instanceCount, MaterialPropertyBlock properties)
        {

        }

        public void DrawProcedural(GraphicsBuffer indexBuffer, in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, in int indexCount, in int instanceCount)
        {

        }

        public void DrawProcedural(GraphicsBuffer indexBuffer, in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, in int indexCount)
        {

        }

        public void DrawProceduralIndirect(in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, ComputeBuffer bufferWithArgs, in int argsOffset, MaterialPropertyBlock properties)
        {

        }

        public void DrawProceduralIndirect(in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, ComputeBuffer bufferWithArgs, in int argsOffset)
        {

        }

        public void DrawProceduralIndirect(in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, ComputeBuffer bufferWithArgs)
        {

        }

        public void DrawProceduralIndirect(GraphicsBuffer indexBuffer, in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, ComputeBuffer bufferWithArgs, in int argsOffset, MaterialPropertyBlock properties)
        {

        }

        public void DrawProceduralIndirect(GraphicsBuffer indexBuffer, in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, ComputeBuffer bufferWithArgs, in int argsOffset)
        {

        }

        public void DrawProceduralIndirect(GraphicsBuffer indexBuffer, in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, ComputeBuffer bufferWithArgs)
        {

        }

        public void DrawProceduralIndirect(in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, GraphicsBuffer bufferWithArgs, in int argsOffset, MaterialPropertyBlock properties)
        {

        }

        public void DrawProceduralIndirect(in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, GraphicsBuffer bufferWithArgs, in int argsOffset)
        {

        }

        public void DrawProceduralIndirect(in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, GraphicsBuffer bufferWithArgs)
        {

        }

        public void DrawProceduralIndirect(GraphicsBuffer indexBuffer, in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, GraphicsBuffer bufferWithArgs, in int argsOffset, MaterialPropertyBlock properties)
        {

        }

        public void DrawProceduralIndirect(GraphicsBuffer indexBuffer, in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, GraphicsBuffer bufferWithArgs, in int argsOffset)
        {

        }

        public void DrawProceduralIndirect(GraphicsBuffer indexBuffer, in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, GraphicsBuffer bufferWithArgs)
        {

        }

        public void DrawMeshInstanced(Mesh mesh, in int submeshIndex, Material material, in int shaderPass, in Matrix4x4[] matrices, in int count, MaterialPropertyBlock properties)
        {

        }

        public void DrawMeshInstanced(Mesh mesh, in int submeshIndex, Material material, in int shaderPass, in Matrix4x4[] matrices, in int count)
        {

        }

        public void DrawMeshInstanced(Mesh mesh, in int submeshIndex, Material material, in int shaderPass, in Matrix4x4[] matrices)
        {

        }

        public void DrawMeshInstancedProcedural(Mesh mesh, in int submeshIndex, Material material, in int shaderPass, in int count, MaterialPropertyBlock properties)
        {

        }

        public void DrawMeshInstancedIndirect(Mesh mesh, in int submeshIndex, Material material, in int shaderPass, ComputeBuffer bufferWithArgs, in int argsOffset, MaterialPropertyBlock properties)
        {

        }

        public void DrawMeshInstancedIndirect(Mesh mesh, in int submeshIndex, Material material, in int shaderPass, ComputeBuffer bufferWithArgs, in int argsOffset)
        {

        }

        public void DrawMeshInstancedIndirect(Mesh mesh, in int submeshIndex, Material material, in int shaderPass, ComputeBuffer bufferWithArgs)
        {

        }

        public void DrawMeshInstancedIndirect(Mesh mesh, in int submeshIndex, Material material, in int shaderPass, GraphicsBuffer bufferWithArgs, in int argsOffset, MaterialPropertyBlock properties)
        {

        }

        public void DrawMeshInstancedIndirect(Mesh mesh, in int submeshIndex, Material material, in int shaderPass, GraphicsBuffer bufferWithArgs, in int argsOffset)
        {

        }

        public void DrawMeshInstancedIndirect(Mesh mesh, in int submeshIndex, Material material, in int shaderPass, GraphicsBuffer bufferWithArgs)
        {

        }

        public void DrawOcclusionMesh(in RectInt normalizedCamViewport)
        {

        }
    }
}