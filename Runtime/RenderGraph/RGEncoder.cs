using UnityEngine;
using InfinityTech.Rendering.GPUResource;
using UnityEngine.Rendering;
using UnityEngine.Rendering.UnifiedRayTracing;
using System.Collections.Generic;

namespace InfinityTech.Rendering.RenderGraph
{
    public readonly struct RGTransferEncoder : ITransferCommands
    {
        internal readonly RGCommandCore m_Core;

        internal RGTransferEncoder(CommandBuffer commandBuffer, RGResourceFactory resources)
        {
            m_Core = new RGCommandCore(commandBuffer, resources);
        }

        public void SetBufferData<T>(GraphicsBuffer buffer, Unity.Collections.NativeArray<T> data) where T : struct => m_Core.commands.SetBufferData(buffer, data);
        public void SetBufferData(GraphicsBuffer buffer, System.Array data, int sourceIndex, int destinationIndex, int count) => m_Core.commands.SetBufferData(buffer, data, sourceIndex, destinationIndex, count);
        public void CopyBuffer(GraphicsBuffer src, GraphicsBuffer dst) => m_Core.commands.CopyBuffer(src, dst);
        public void RequestAsyncReadback(GraphicsBuffer source, System.Action<AsyncGPUReadbackRequest> completion) => m_Core.commands.RequestAsyncReadback(source, completion);
        public void RequestAsyncReadback(Texture source, System.Action<AsyncGPUReadbackRequest> completion) => m_Core.commands.RequestAsyncReadback(source, 0, completion);
        public void CopyTexture(RGTextureHandle src, RGTextureHandle dst) => m_Core.CopyTexture(src, dst);
        public void CopyTexture(RGTextureHandle src, int srcElement, RGTextureHandle dst, int dstElement) => m_Core.CopyTexture(src, srcElement, dst, dstElement);
        public void CopyTexture(RGTextureHandle src, int srcElement, int srcMip, RGTextureHandle dst, int dstElement, int dstMip) => m_Core.CopyTexture(src, srcElement, srcMip, dst, dstElement, dstMip);
        public void CopyTexture(RGTextureHandle src, int srcElement, int srcMip, int srcX, int srcY, int srcWidth, int srcHeight, RGTextureHandle dst, int dstElement, int dstMip, int dstX, int dstY)
            => m_Core.CopyTexture(src, srcElement, srcMip, srcX, srcY, srcWidth, srcHeight, dst, dstElement, dstMip, dstX, dstY);
    }

    public readonly struct RGComputeEncoder : IComputeCommands
    {
        internal readonly RGCommandCore m_Core;

        internal RGComputeEncoder(CommandBuffer commandBuffer, RGResourceFactory resources)
        {
            m_Core = new RGCommandCore(commandBuffer, resources);
        }

        // Uploads data a compute pass reads in the same command stream (mesh frustum views).
        internal void SetBufferData(GraphicsBuffer buffer, System.Array data, int sourceIndex, int destinationIndex, int count)
            => m_Core.SetBufferData(buffer, data, sourceIndex, destinationIndex, count);

        public void SetKeyword(GlobalKeyword keyword, bool value) => m_Core.SetKeyword(keyword, value);
        public void SetKeyword(ComputeShader computeShader, in LocalKeyword keyword, bool value) => m_Core.SetKeyword(computeShader, keyword, value);

        #region SetGlobalParameter
        public void SetGlobalFloat(int nameID, float value) => m_Core.SetGlobalFloat(nameID, value);
        public void SetGlobalInt(int nameID, int value) => m_Core.SetGlobalInt(nameID, value);
        public void SetGlobalInteger(int nameID, int value) => m_Core.SetGlobalInteger(nameID, value);
        public void SetGlobalVector(int nameID, Vector4 value) => m_Core.SetGlobalVector(nameID, value);
        public void SetGlobalColor(int nameID, Color value) => m_Core.SetGlobalColor(nameID, value);
        public void SetGlobalMatrix(int nameID, Matrix4x4 value) => m_Core.SetGlobalMatrix(nameID, value);
        public void SetGlobalFloatArray(int nameID, List<float> values) => m_Core.SetGlobalFloatArray(nameID, values);
        public void SetGlobalFloatArray(int nameID, float[] values) => m_Core.SetGlobalFloatArray(nameID, values);
        public void SetGlobalVectorArray(int nameID, List<Vector4> values) => m_Core.SetGlobalVectorArray(nameID, values);
        public void SetGlobalVectorArray(int nameID, Vector4[] values) => m_Core.SetGlobalVectorArray(nameID, values);
        public void SetGlobalMatrixArray(int nameID, List<Matrix4x4> values) => m_Core.SetGlobalMatrixArray(nameID, values);
        public void SetGlobalMatrixArray(int nameID, Matrix4x4[] values) => m_Core.SetGlobalMatrixArray(nameID, values);
        public void SetGlobalTexture(int nameID, RGTextureHandle texture) => m_Core.SetGlobalTexture(nameID, texture, RenderTextureSubElement.Default);
        public void SetGlobalTexture(int nameID, RGTextureHandle texture, RenderTextureSubElement element) => m_Core.SetGlobalTexture(nameID, texture, element);
        public void SetGlobalTexture(int nameID, RenderTargetIdentifier value) => m_Core.SetGlobalTexture(nameID, value, RenderTextureSubElement.Default);
        public void SetGlobalTexture(int nameID, RenderTargetIdentifier value, RenderTextureSubElement element) => m_Core.SetGlobalTexture(nameID, value, element);
        public void SetGlobalTexture(int nameID, RenderTargetIdentifier value, in TextureView view) => m_Core.SetGlobalTexture(nameID, value, view);
        public void SetGlobalBuffer(int nameID, GraphicsBuffer value) => m_Core.SetGlobalBuffer(nameID, value);
        public void SetGlobalBuffer(int nameID, RGBufferHandle value) => m_Core.SetGlobalBuffer(nameID, value);
        public void SetGlobalConstantBuffer(GraphicsBuffer buffer, int nameID, int offset, int size) => m_Core.SetGlobalConstantBuffer(buffer, nameID, offset, size);
        #endregion SetGlobalParameter

        #region SetLocalParameter
        public void SetComputeFloatParam(ComputeShader computeShader, int nameID, float val) => m_Core.commands.SetComputeFloatParam(computeShader, nameID, val);
        public void SetComputeIntParam(ComputeShader computeShader, int nameID, int val) => m_Core.commands.SetComputeIntParam(computeShader, nameID, val);
        public void SetComputeVectorParam(ComputeShader computeShader, int nameID, Vector4 val) => m_Core.commands.SetComputeVectorParam(computeShader, nameID, val);
        public void SetComputeVectorArrayParam(ComputeShader computeShader, int nameID, Vector4[] values) => m_Core.commands.SetComputeVectorArrayParam(computeShader, nameID, values);
        public void SetComputeMatrixParam(ComputeShader computeShader, int nameID, Matrix4x4 val) => m_Core.commands.SetComputeMatrixParam(computeShader, nameID, val);
        public void SetComputeMatrixArrayParam(ComputeShader computeShader, int nameID, Matrix4x4[] values) => m_Core.commands.SetComputeMatrixArrayParam(computeShader, nameID, values);
        public void SetComputeFloatParams(ComputeShader computeShader, int nameID, float[] values) => m_Core.commands.SetComputeFloatParams(computeShader, nameID, values);
        public void SetComputeIntParams(ComputeShader computeShader, int nameID, int[] values) => m_Core.commands.SetComputeIntParams(computeShader, nameID, values);

        public void SetComputeTextureParam(ComputeShader computeShader, int kernelIndex, int nameID, RGTextureHandle texture) => m_Core.SetComputeTexture(computeShader, kernelIndex, nameID, texture, 0, RenderTextureSubElement.Default);
        public void SetComputeTextureParam(ComputeShader computeShader, int kernelIndex, int nameID, RGTextureHandle texture, int mipLevel) => m_Core.SetComputeTexture(computeShader, kernelIndex, nameID, texture, mipLevel, RenderTextureSubElement.Default);
        public void SetComputeTextureParam(ComputeShader computeShader, int kernelIndex, int nameID, RGTextureHandle texture, int mipLevel, RenderTextureSubElement element) => m_Core.SetComputeTexture(computeShader, kernelIndex, nameID, texture, mipLevel, element);
        public void SetComputeTextureParam(ComputeShader computeShader, int kernelIndex, int nameID, RenderTargetIdentifier rt) => m_Core.SetComputeTexture(computeShader, kernelIndex, nameID, rt, 0, RenderTextureSubElement.Default);
        public void SetComputeTextureParam(ComputeShader computeShader, int kernelIndex, int nameID, RenderTargetIdentifier rt, int mipLevel) => m_Core.SetComputeTexture(computeShader, kernelIndex, nameID, rt, mipLevel, RenderTextureSubElement.Default);
        public void SetComputeTextureParam(ComputeShader computeShader, int kernelIndex, int nameID, RenderTargetIdentifier rt, int mipLevel, RenderTextureSubElement element) => m_Core.SetComputeTexture(computeShader, kernelIndex, nameID, rt, mipLevel, element);

        public void SetComputeTextureParam(ComputeShader computeShader, int kernelIndex, int nameID, RenderTargetIdentifier rt, in TextureView view) => m_Core.SetComputeTexture(computeShader, kernelIndex, nameID, rt, view);

        public void SetComputeBufferParam(ComputeShader computeShader, int kernelIndex, int nameID, GraphicsBuffer buffer) => m_Core.commands.SetComputeBufferParam(computeShader, kernelIndex, nameID, buffer);
        public void SetComputeBufferParam(ComputeShader computeShader, int kernelIndex, int nameID, GraphicsBufferHandle bufferHandle) => m_Core.commands.SetComputeBufferParam(computeShader, kernelIndex, nameID, bufferHandle);
        public void SetComputeBufferParam(ComputeShader computeShader, int kernelIndex, int nameID, RGBufferHandle buffer) => m_Core.SetComputeBufferParam(computeShader, kernelIndex, nameID, buffer);
        public void SetComputeConstantBufferParam(ComputeShader computeShader, int nameID, GraphicsBuffer buffer, int offset, int size) => m_Core.commands.SetComputeConstantBufferParam(computeShader, nameID, buffer, offset, size);
        public void SetComputeParamsFromMaterial(ComputeShader computeShader, int kernelIndex, Material material) => m_Core.commands.SetComputeParamsFromMaterial(computeShader, kernelIndex, material);
        public void SetComputeRayTracingAccelerationStructure(ComputeShader computeShader, int kernelIndex, int nameID, RayTracingAccelerationStructure rayTracingAccelerationStructure)
            => m_Core.commands.SetRayTracingAccelerationStructure(computeShader, kernelIndex, nameID, rayTracingAccelerationStructure);
        #endregion SetLocalParameter

        public void DispatchCompute(ComputeShader computeShader, in int kernelIndex, in int threadGroupsX, in int threadGroupsY, in int threadGroupsZ) => m_Core.commands.DispatchCompute(computeShader, kernelIndex, threadGroupsX, threadGroupsY, threadGroupsZ);
        public void DispatchComputeIndirect(ComputeShader computeShader, in int kernelIndex, RGBufferHandle argsBuffer, in uint argsOffset) => m_Core.DispatchComputeIndirect(computeShader, kernelIndex, argsBuffer, argsOffset);
        public void DispatchCompute(ComputeShader computeShader, in int kernelIndex, GraphicsBuffer indirectBuffer, in uint argsOffset) => m_Core.commands.DispatchCompute(computeShader, kernelIndex, indirectBuffer, argsOffset);
        public void CopyTexture(RGTextureHandle src, RGTextureHandle dst) => m_Core.CopyTexture(src, dst);
        public void BeginSample(ProfilingSampler sampler) => m_Core.BeginSample(sampler);
        public void EndSample(ProfilingSampler sampler) => m_Core.EndSample(sampler);
    }

    public readonly struct RGRaytracingEncoder : IRaytracingCommands
    {
        internal readonly RGCommandCore m_Core;

        internal RGRaytracingEncoder(CommandBuffer commandBuffer, RGResourceFactory resources)
        {
            m_Core = new RGCommandCore(commandBuffer, resources);
        }

        public void SetRayTracingShaderPass(RayTracingShader rayTracingShader, string passName) => m_Core.commands.SetRayTracingShaderPass(rayTracingShader, passName);
        public void SetRayTracingAccelerationStructure(RayTracingShader rayTracingShader, int nameID, RayTracingAccelerationStructure rayTracingAccelerationStructure) => m_Core.commands.SetRayTracingAccelerationStructure(rayTracingShader, nameID, rayTracingAccelerationStructure);
        public void SetRayTracingBufferParam(RayTracingShader rayTracingShader, int nameID, GraphicsBuffer buffer) => m_Core.commands.SetRayTracingBufferParam(rayTracingShader, nameID, buffer);
        public void SetRayTracingBufferParam(RayTracingShader rayTracingShader, int nameID, GraphicsBufferHandle bufferHandle) => m_Core.commands.SetRayTracingBufferParam(rayTracingShader, nameID, bufferHandle);
        public void SetRayTracingConstantBufferParam(RayTracingShader rayTracingShader, int nameID, GraphicsBuffer buffer, int offset, int size) => m_Core.commands.SetRayTracingConstantBufferParam(rayTracingShader, nameID, buffer, offset, size);
        public void SetRayTracingTextureParam(RayTracingShader rayTracingShader, int nameID, RGTextureHandle texture) => m_Core.SetRayTracingTexture(rayTracingShader, nameID, texture);
        public void SetRayTracingTextureParam(RayTracingShader rayTracingShader, int nameID, RenderTargetIdentifier rt) => m_Core.SetRayTracingTexture(rayTracingShader, nameID, rt);
        public void SetRayTracingFloatParam(RayTracingShader rayTracingShader, int nameID, float val) => m_Core.commands.SetRayTracingFloatParam(rayTracingShader, nameID, val);
        public void SetRayTracingFloatParams(RayTracingShader rayTracingShader, int nameID, float[] values) => m_Core.commands.SetRayTracingFloatParams(rayTracingShader, nameID, values);
        public void SetRayTracingIntParam(RayTracingShader rayTracingShader, int nameID, int val) => m_Core.commands.SetRayTracingIntParam(rayTracingShader, nameID, val);
        public void SetRayTracingIntParams(RayTracingShader rayTracingShader, int nameID, int[] values) => m_Core.commands.SetRayTracingIntParams(rayTracingShader, nameID, values);
        public void SetRayTracingVectorParam(RayTracingShader rayTracingShader, int nameID, Vector4 val) => m_Core.commands.SetRayTracingVectorParam(rayTracingShader, nameID, val);
        public void SetRayTracingVectorArrayParam(RayTracingShader rayTracingShader, int nameID, Vector4[] values) => m_Core.commands.SetRayTracingVectorArrayParam(rayTracingShader, nameID, values);
        public void SetRayTracingMatrixParam(RayTracingShader rayTracingShader, int nameID, Matrix4x4 val) => m_Core.commands.SetRayTracingMatrixParam(rayTracingShader, nameID, val);
        public void SetRayTracingMatrixArrayParam(RayTracingShader rayTracingShader, int nameID, Matrix4x4[] values) => m_Core.commands.SetRayTracingMatrixArrayParam(rayTracingShader, nameID, values);
        public void BuildRayTracingAccelerationStructure(RayTracingAccelerationStructure accelerationStructure) => m_Core.commands.BuildRayTracingAccelerationStructure(accelerationStructure);
        public void BuildRayTracingAccelerationStructure(RayTracingAccelerationStructure accelerationStructure, Vector3 relativeOrigin) => m_Core.commands.BuildRayTracingAccelerationStructure(accelerationStructure, relativeOrigin);
        public void BuildRayTracingAccelerationStructure(RayTracingAccelerationStructure accelerationStructure, RayTracingAccelerationStructure.BuildSettings buildSettings) => m_Core.commands.BuildRayTracingAccelerationStructure(accelerationStructure, buildSettings);
        // Unity's CommandBuffer.DispatchRays has no ray-generation shader overload that takes an id.
        public void DispatchRays(RayTracingShader rayTracingShader, string rayGenName, in uint width, in uint height, in uint depth, Camera camera) => m_Core.commands.DispatchRays(rayTracingShader, rayGenName, width, height, depth, camera);
        public void DispatchRays(RayTracingShader rayTracingShader, string rayGenName, GraphicsBuffer argsBuffer, in uint argsOffset, Camera camera) => m_Core.commands.DispatchRays(rayTracingShader, rayGenName, argsBuffer, argsOffset, camera);
        public void BuildUnifiedAccel(IRayTracingAccelStruct accel, GraphicsBuffer scratch) => accel.Build(m_Core.commands, scratch);
        // Unity's unified ray tracing shader has no int variant of the acceleration-structure binding.
        public void SetUnifiedAccelerationStructure(IRayTracingShader shader, string name, IRayTracingAccelStruct accel) => shader.SetAccelerationStructure(m_Core.commands, name, accel);
        public void SetUnifiedInt(IRayTracingShader shader, int nameID, int val) => shader.SetIntParam(m_Core.commands, nameID, val);
        public void SetUnifiedFloat(IRayTracingShader shader, int nameID, float val) => shader.SetFloatParam(m_Core.commands, nameID, val);
        public void SetUnifiedVector(IRayTracingShader shader, int nameID, Vector4 val) => shader.SetVectorParam(m_Core.commands, nameID, val);
        public void SetUnifiedMatrix(IRayTracingShader shader, int nameID, Matrix4x4 val) => shader.SetMatrixParam(m_Core.commands, nameID, val);
        public void SetUnifiedTexture(IRayTracingShader shader, int nameID, RGTextureHandle texture) => m_Core.SetUnifiedTexture(shader, nameID, texture);
        public void SetUnifiedTexture(IRayTracingShader shader, int nameID, RenderTargetIdentifier rt) => m_Core.SetUnifiedTexture(shader, nameID, rt);
        public void DispatchUnified(IRayTracingShader shader, GraphicsBuffer scratch, uint width, uint height, uint depth) => shader.Dispatch(m_Core.commands, scratch, width, height, depth);
    }

    public readonly struct RGRasterEncoder : IRasterCommands
    {
        internal readonly RGCommandCore m_Core;
        internal readonly RGRendererListContext m_RendererLists;
        internal readonly IRGGraphExtension m_Extension;

        internal RGRasterEncoder(CommandBuffer commandBuffer, RGResourceFactory resources, RGRendererListContext rendererLists = null, IRGGraphExtension extension = null)
        {
            m_Core = new RGCommandCore(commandBuffer, resources);
            m_RendererLists = rendererLists;
            m_Extension = extension;
        }

        public void SetKeyword(GlobalKeyword keyword, bool value) => m_Core.SetKeyword(keyword, value);
        public void IssuePluginEventAndData(System.IntPtr callback, int eventId, System.IntPtr data) => m_Core.commands.IssuePluginEventAndData(callback, eventId, data);
        public void BeginSample(ProfilingSampler sampler) => m_Core.BeginSample(sampler);
        public void EndSample(ProfilingSampler sampler) => m_Core.EndSample(sampler);

        #region SetGlobalParameter
        public void SetGlobalFloat(int nameID, float value) => m_Core.SetGlobalFloat(nameID, value);
        public void SetGlobalInt(int nameID, int value) => m_Core.SetGlobalInt(nameID, value);
        public void SetGlobalInteger(int nameID, int value) => m_Core.SetGlobalInteger(nameID, value);
        public void SetGlobalVector(int nameID, Vector4 value) => m_Core.SetGlobalVector(nameID, value);
        public void SetGlobalColor(int nameID, Color value) => m_Core.SetGlobalColor(nameID, value);
        public void SetGlobalMatrix(int nameID, Matrix4x4 value) => m_Core.SetGlobalMatrix(nameID, value);
        public void SetGlobalFloatArray(int nameID, List<float> values) => m_Core.SetGlobalFloatArray(nameID, values);
        public void SetGlobalFloatArray(int nameID, float[] values) => m_Core.SetGlobalFloatArray(nameID, values);
        public void SetGlobalVectorArray(int nameID, List<Vector4> values) => m_Core.SetGlobalVectorArray(nameID, values);
        public void SetGlobalVectorArray(int nameID, Vector4[] values) => m_Core.SetGlobalVectorArray(nameID, values);
        public void SetGlobalMatrixArray(int nameID, List<Matrix4x4> values) => m_Core.SetGlobalMatrixArray(nameID, values);
        public void SetGlobalMatrixArray(int nameID, Matrix4x4[] values) => m_Core.SetGlobalMatrixArray(nameID, values);
        public void SetGlobalTexture(int nameID, RGTextureHandle texture) => m_Core.SetGlobalTexture(nameID, texture, RenderTextureSubElement.Default);
        public void SetGlobalTexture(int nameID, RGTextureHandle texture, RenderTextureSubElement element) => m_Core.SetGlobalTexture(nameID, texture, element);
        public void SetGlobalTexture(int nameID, RenderTargetIdentifier value) => m_Core.SetGlobalTexture(nameID, value, RenderTextureSubElement.Default);
        public void SetGlobalTexture(int nameID, RenderTargetIdentifier value, RenderTextureSubElement element) => m_Core.SetGlobalTexture(nameID, value, element);
        public void SetGlobalTexture(int nameID, RenderTargetIdentifier value, in TextureView view) => m_Core.SetGlobalTexture(nameID, value, view);
        public void SetGlobalBuffer(int nameID, GraphicsBuffer value) => m_Core.SetGlobalBuffer(nameID, value);
        public void SetGlobalBuffer(int nameID, RGBufferHandle value) => m_Core.SetGlobalBuffer(nameID, value);
        public void SetGlobalConstantBuffer(GraphicsBuffer buffer, int nameID, int offset, int size) => m_Core.SetGlobalConstantBuffer(buffer, nameID, offset, size);
        #endregion SetGlobalParameter

        public void SetViewport(in Rect pixelRect)
        {
            m_Core.commands.SetViewport(pixelRect);
        }

        public void SetGlobalDepthBias(in float bias, in float slopeBias)
        {
            m_Core.commands.SetGlobalDepthBias(bias, slopeBias);
        }

        public void SetInvertCulling(in bool invertCulling)
        {
            m_Core.commands.SetInvertCulling(invertCulling);
        }

        public void EnableScissorRect(in Rect scissor)
        {
            m_Core.commands.EnableScissorRect(scissor);
        }

        public void DisableScissorRect()
        {
            m_Core.commands.DisableScissorRect();
        }

        internal void NextSubPass()
        {
            m_Core.commands.NextSubPass();
        }

        public void DrawMesh(Mesh mesh, in Matrix4x4 matrix, Material material, in int submeshIndex, in int shaderPass, MaterialPropertyBlock properties)
        {
            m_Core.commands.DrawMesh(mesh, matrix, material, submeshIndex, shaderPass, properties);
        }

        public void DrawMesh(Mesh mesh, in Matrix4x4 matrix, Material material, in int submeshIndex, in int shaderPass)
        {
            m_Core.commands.DrawMesh(mesh, matrix, material, submeshIndex, shaderPass);
        }

        public void DrawMesh(Mesh mesh, in Matrix4x4 matrix, Material material, in int submeshIndex)
        {
            m_Core.commands.DrawMesh(mesh, matrix, material, submeshIndex);
        }

        public void DrawMesh(Mesh mesh, in Matrix4x4 matrix, Material material)
        {
            m_Core.commands.DrawMesh(mesh, matrix, material);
        }

        public void DrawMultipleMeshes(Matrix4x4[] matrices, Mesh[] meshes, int[] subsetIndices, in int count, Material material, in int shaderPass, MaterialPropertyBlock properties)
        {
            m_Core.commands.DrawMultipleMeshes(matrices, meshes, subsetIndices, count, material, shaderPass, properties);
        }

        public void DrawRenderer(Renderer renderer, Material material, in int submeshIndex, in int shaderPass)
        {
            m_Core.commands.DrawRenderer(renderer, material, submeshIndex, shaderPass);
        }

        public void DrawRenderer(Renderer renderer, Material material, in int submeshIndex)
        {
            m_Core.commands.DrawRenderer(renderer, material, submeshIndex);
        }

        public void DrawRenderer(Renderer renderer, Material material)
        {
            m_Core.commands.DrawRenderer(renderer, material);
        }

        public void DrawRendererList(in RendererList rendererList)
        {
            m_Core.commands.DrawRendererList(rendererList);
        }

        public void DrawRendererList(in RGRendererListHandle rendererList)
        {
            (m_RendererLists ?? throw new System.InvalidOperationException("This raster encoder has no renderer lists.")).Submit(m_Core.commands, rendererList);
        }

        public void DrawProcedural(in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, in int vertexCount, in int instanceCount, MaterialPropertyBlock properties)
        {
            m_Core.commands.DrawProcedural(matrix, material, shaderPass, topology, vertexCount, instanceCount, properties);
        }

        public void DrawProcedural(in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, in int vertexCount, in int instanceCount)
        {
            m_Core.commands.DrawProcedural(matrix, material, shaderPass, topology, vertexCount, instanceCount);
        }

        public void DrawProcedural(in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, in int vertexCount)
        {
            m_Core.commands.DrawProcedural(matrix, material, shaderPass, topology, vertexCount);
        }

        public void DrawProcedural(GraphicsBuffer indexBuffer, in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, in int indexCount, in int instanceCount, MaterialPropertyBlock properties)
        {
            m_Core.commands.DrawProcedural(indexBuffer, matrix, material, shaderPass, topology, indexCount, instanceCount, properties);
        }

        public void DrawProcedural(GraphicsBuffer indexBuffer, in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, in int indexCount, in int instanceCount)
        {
            m_Core.commands.DrawProcedural(indexBuffer, matrix, material, shaderPass, topology, indexCount, instanceCount);
        }

        public void DrawProcedural(GraphicsBuffer indexBuffer, in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, in int indexCount)
        {
            m_Core.commands.DrawProcedural(indexBuffer, matrix, material, shaderPass, topology, indexCount);
        }

        public void DrawProceduralIndirect(in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, GraphicsBuffer bufferWithArgs, in int argsOffset, MaterialPropertyBlock properties)
        {
            m_Core.commands.DrawProceduralIndirect(matrix, material, shaderPass, topology, bufferWithArgs, argsOffset, properties);
        }

        public void DrawProceduralIndirect(in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, GraphicsBuffer bufferWithArgs, in int argsOffset)
        {
            m_Core.commands.DrawProceduralIndirect(matrix, material, shaderPass, topology, bufferWithArgs, argsOffset);
        }

        public void DrawProceduralIndirect(in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, GraphicsBuffer bufferWithArgs)
        {
            m_Core.commands.DrawProceduralIndirect(matrix, material, shaderPass, topology, bufferWithArgs);
        }

        public void DrawProceduralIndirect(GraphicsBuffer indexBuffer, in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, GraphicsBuffer bufferWithArgs, in int argsOffset, MaterialPropertyBlock properties)
        {
            m_Core.commands.DrawProceduralIndirect(indexBuffer, matrix, material, shaderPass, topology, bufferWithArgs, argsOffset, properties);
        }

        public void DrawProceduralIndirect(GraphicsBuffer indexBuffer, in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, GraphicsBuffer bufferWithArgs, in int argsOffset)
        {
            m_Core.commands.DrawProceduralIndirect(indexBuffer, matrix, material, shaderPass, topology, bufferWithArgs, argsOffset);
        }

        public void DrawProceduralIndirect(GraphicsBuffer indexBuffer, in Matrix4x4 matrix, Material material, in int shaderPass, in MeshTopology topology, GraphicsBuffer bufferWithArgs)
        {
            m_Core.commands.DrawProceduralIndirect(indexBuffer, matrix, material, shaderPass, topology, bufferWithArgs);
        }

        public void DrawMeshInstanced(Mesh mesh, in int submeshIndex, Material material, in int shaderPass, Matrix4x4[] matrices, in int count, MaterialPropertyBlock properties)
        {
            m_Core.commands.DrawMeshInstanced(mesh, submeshIndex, material, shaderPass, matrices, count, properties);
        }

        public void DrawMeshInstanced(Mesh mesh, in int submeshIndex, Material material, in int shaderPass, Matrix4x4[] matrices, in int count)
        {
            m_Core.commands.DrawMeshInstanced(mesh, submeshIndex, material, shaderPass, matrices, count);
        }

        public void DrawMeshInstanced(Mesh mesh, in int submeshIndex, Material material, in int shaderPass, Matrix4x4[] matrices)
        {
            m_Core.commands.DrawMeshInstanced(mesh, submeshIndex, material, shaderPass, matrices);
        }

        public void DrawMeshInstancedProcedural(Mesh mesh, in int submeshIndex, Material material, in int shaderPass, in int count, MaterialPropertyBlock properties)
        {
            m_Core.commands.DrawMeshInstancedProcedural(mesh, submeshIndex, material, shaderPass, count, properties);
        }

        public void DrawMeshInstancedIndirect(Mesh mesh, in int submeshIndex, Material material, in int shaderPass, GraphicsBuffer bufferWithArgs, in int argsOffset, MaterialPropertyBlock properties)
        {
            m_Core.commands.DrawMeshInstancedIndirect(mesh, submeshIndex, material, shaderPass, bufferWithArgs, argsOffset, properties);
        }

        public void DrawMeshInstancedIndirect(Mesh mesh, in int submeshIndex, Material material, in int shaderPass, GraphicsBuffer bufferWithArgs, in int argsOffset)
        {
            m_Core.commands.DrawMeshInstancedIndirect(mesh, submeshIndex, material, shaderPass, bufferWithArgs, argsOffset);
        }

        public void DrawMeshInstancedIndirect(Mesh mesh, in int submeshIndex, Material material, in int shaderPass, GraphicsBuffer bufferWithArgs)
        {
            m_Core.commands.DrawMeshInstancedIndirect(mesh, submeshIndex, material, shaderPass, bufferWithArgs);
        }

        public void DrawOcclusionMesh(in RectInt normalizedCamViewport)
        {
            m_Core.commands.DrawOcclusionMesh(normalizedCamViewport);
        }
    }
}
