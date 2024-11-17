using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using System.Collections.Generic;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.RenderGraph
{
    public struct RGTransferEncoder
    {
        internal CommandBuffer m_CommandBuffer;

        public static implicit operator CommandBuffer(in RGTransferEncoder cmdEncoder) => cmdEncoder.m_CommandBuffer;

        internal RGTransferEncoder(CommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
        }

        public void Present(in bool bIsRenderToBackBufferTarget, in Rect viewPort, in float4 scaleBias, RenderTexture srcBuffer)
        {
            if (bIsRenderToBackBufferTarget)
            {
                m_CommandBuffer.SetViewport(viewPort);
            }

            m_CommandBuffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            m_CommandBuffer.SetGlobalVector(InfinityShaderIDs.ScaleBias, scaleBias);
            m_CommandBuffer.SetGlobalTexture(InfinityShaderIDs.MainTexture, srcBuffer);
            m_CommandBuffer.DrawMesh(GraphicsUtility.FullScreenMesh, Matrix4x4.identity, GraphicsUtility.BlitMaterial, 0, 1);
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

        public static implicit operator CommandBuffer(in RGComputeEncoder cmdEncoder) => cmdEncoder.m_CommandBuffer;

        internal RGComputeEncoder(CommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
        }

        #region SetGlobalParameter
        public void SetGlobalFloat(string name, float value)
        {
            m_CommandBuffer.SetGlobalFloat(name, value);
        }

        public void SetGlobalInt(string name, int value)
        {
            m_CommandBuffer.SetGlobalInt(name, value);
        }

        public void SetGlobalInteger(string name, int value)
        {
            m_CommandBuffer.SetGlobalInteger(name, value);
        }

        public void SetGlobalVector(string name, Vector4 value)
        {
            m_CommandBuffer.SetGlobalVector(name, value);
        }

        public void SetGlobalColor(string name, Color value)
        {
            m_CommandBuffer.SetGlobalColor(name, value);
        }

        public void SetGlobalMatrix(string name, Matrix4x4 value)
        {
            m_CommandBuffer.SetGlobalMatrix(name, value);
        }

        public void SetGlobalFloatArray(string propertyName, List<float> values)
        {
            m_CommandBuffer.SetGlobalFloatArray(propertyName, values);
        }

        public void SetGlobalFloatArray(int nameID, List<float> values)
        {
            m_CommandBuffer.SetGlobalFloatArray(nameID, values);
        }

        public void SetGlobalFloatArray(string propertyName, float[] values)
        {
            m_CommandBuffer.SetGlobalFloatArray(propertyName, values);
        }

        public void SetGlobalVectorArray(string propertyName, List<Vector4> values)
        {
            m_CommandBuffer.SetGlobalVectorArray(propertyName, values);
        }

        public void SetGlobalVectorArray(int nameID, List<Vector4> values)
        {
            m_CommandBuffer.SetGlobalVectorArray(nameID, values);
        }

        public void SetGlobalVectorArray(string propertyName, Vector4[] values)
        {
            m_CommandBuffer.SetGlobalVectorArray(propertyName, values);
        }

        public void SetGlobalMatrixArray(string propertyName, List<Matrix4x4> values)
        {
            m_CommandBuffer.SetGlobalMatrixArray(propertyName, values);
        }

        public void SetGlobalMatrixArray(int nameID, List<Matrix4x4> values)
        {
            m_CommandBuffer.SetGlobalMatrixArray(nameID, values);
        }

        public void SetGlobalMatrixArray(string propertyName, Matrix4x4[] values)
        {
            m_CommandBuffer.SetGlobalMatrixArray(propertyName, values);
        }

        public void SetGlobalTexture(string name, RenderTargetIdentifier value)
        {
            m_CommandBuffer.SetGlobalTexture(name, value);
        }

        public void SetGlobalTexture(int nameID, RenderTargetIdentifier value)
        {
            m_CommandBuffer.SetGlobalTexture(nameID, value);
        }

        public void SetGlobalTexture(string name, RenderTargetIdentifier value, RenderTextureSubElement element)
        {
            m_CommandBuffer.SetGlobalTexture(name, value, element);
        }

        public void SetGlobalTexture(int nameID, RenderTargetIdentifier value, RenderTextureSubElement element)
        {
            m_CommandBuffer.SetGlobalTexture(nameID, value, element);
        }

        public void SetGlobalBuffer(string name, ComputeBuffer value)
        {
            m_CommandBuffer.SetGlobalBuffer(name, value);
        }

        public void SetGlobalBuffer(int nameID, ComputeBuffer value)
        {
            m_CommandBuffer.SetGlobalBuffer(nameID, value);
        }

        public void SetGlobalBuffer(string name, GraphicsBuffer value)
        {
            m_CommandBuffer.SetGlobalBuffer(name, value);
        }

        public void SetGlobalBuffer(int nameID, GraphicsBuffer value)
        {
            m_CommandBuffer.SetGlobalBuffer(nameID, value);
        }

        public void SetGlobalConstantBuffer(ComputeBuffer buffer, int nameID, int offset, int size)
        {
            m_CommandBuffer.SetGlobalConstantBuffer(buffer, nameID, offset, size);
        }

        public void SetGlobalConstantBuffer(ComputeBuffer buffer, string name, int offset, int size)
        {
            m_CommandBuffer.SetGlobalConstantBuffer(buffer, name, offset, size);
        }

        public void SetGlobalConstantBuffer(GraphicsBuffer buffer, int nameID, int offset, int size)
        {
            m_CommandBuffer.SetGlobalConstantBuffer(buffer, nameID, offset, size);
        }

        public void SetGlobalConstantBuffer(GraphicsBuffer buffer, string name, int offset, int size)
        {
            m_CommandBuffer.SetGlobalConstantBuffer(buffer, name, offset, size);
        }
        #endregion SetGlobalParameter

        #region SetLocalParameter
        public void SetComputeFloatParam(ComputeShader computeShader, int nameID, float val)
        {
            m_CommandBuffer.SetComputeFloatParam(computeShader, nameID, val);
        }

        public void SetComputeFloatParam(ComputeShader computeShader, string name, float val)
        {
            m_CommandBuffer.SetComputeFloatParam(computeShader, name, val);
        }

        public void SetComputeIntParam(ComputeShader computeShader, int nameID, int val)
        {
            m_CommandBuffer.SetComputeIntParam(computeShader, nameID, val);
        }

        public void SetComputeIntParam(ComputeShader computeShader, string name, int val)
        {
            m_CommandBuffer.SetComputeIntParam(computeShader, name, val);
        }

        public void SetComputeVectorParam(ComputeShader computeShader, int nameID, Vector4 val)
        {
            m_CommandBuffer.SetComputeVectorParam(computeShader, nameID, val);
        }

        public void SetComputeVectorParam(ComputeShader computeShader, string name, Vector4 val)
        {
            m_CommandBuffer.SetComputeVectorParam(computeShader, name, val);
        }

        public void SetComputeVectorArrayParam(ComputeShader computeShader, int nameID, Vector4[] values)
        {
            m_CommandBuffer.SetComputeVectorArrayParam(computeShader, nameID, values);
        }

        public void SetComputeVectorArrayParam(ComputeShader computeShader, string name, Vector4[] values)
        {
            m_CommandBuffer.SetComputeVectorArrayParam(computeShader, name, values);
        }

        public void SetComputeMatrixParam(ComputeShader computeShader, string name, Matrix4x4 val)
        {
            m_CommandBuffer.SetComputeMatrixParam(computeShader, name, val);
        }

        public void SetComputeMatrixParam(ComputeShader computeShader, int nameID, Matrix4x4 val)
        {
            m_CommandBuffer.SetComputeMatrixParam(computeShader, nameID, val);
        }

        public void SetComputeMatrixArrayParam(ComputeShader computeShader, string name, Matrix4x4[] values)
        {
            m_CommandBuffer.SetComputeMatrixArrayParam(computeShader, name, values);
        }

        public void SetComputeMatrixArrayParam(ComputeShader computeShader, int nameID, Matrix4x4[] values)
        {
            m_CommandBuffer.SetComputeMatrixArrayParam(computeShader, nameID, values);
        }

        public void SetComputeFloatParams(ComputeShader computeShader, string name, params float[] values)
        {
            m_CommandBuffer.SetComputeFloatParams(computeShader, name, values);
        }

        public void SetComputeFloatParams(ComputeShader computeShader, int nameID, params float[] values)
        {
            m_CommandBuffer.SetComputeFloatParams(computeShader, nameID, values);
        }

        public void SetComputeIntParams(ComputeShader computeShader, string name, params int[] values)
        {
            m_CommandBuffer.SetComputeIntParams(computeShader, name, values);
        }

        public void SetComputeIntParams(ComputeShader computeShader, int nameID, params int[] values)
        {
            m_CommandBuffer.SetComputeIntParams(computeShader, nameID, values);
        }

        public void SetComputeTextureParam(ComputeShader computeShader, int kernelIndex, int nameID, RenderTargetIdentifier rt)
        {
            m_CommandBuffer.SetComputeTextureParam(computeShader, kernelIndex, nameID, rt, 0, RenderTextureSubElement.Default);
        }

        public void SetComputeTextureParam(ComputeShader computeShader, int kernelIndex, string name, RenderTargetIdentifier rt)
        {
            m_CommandBuffer.SetComputeTextureParam(computeShader, kernelIndex, name, rt, 0, RenderTextureSubElement.Default);
        }

        public void SetComputeTextureParam(ComputeShader computeShader, int kernelIndex, int nameID, RenderTargetIdentifier rt, int mipLevel)
        {
            m_CommandBuffer.SetComputeTextureParam(computeShader, kernelIndex, nameID, rt, mipLevel, RenderTextureSubElement.Default);
        }

        public void SetComputeTextureParam(ComputeShader computeShader, int kernelIndex, string name, RenderTargetIdentifier rt, int mipLevel)
        {
            m_CommandBuffer.SetComputeTextureParam(computeShader, kernelIndex, name, rt, mipLevel, RenderTextureSubElement.Default);
        }

        public void SetComputeTextureParam(ComputeShader computeShader, int kernelIndex, int nameID, RenderTargetIdentifier rt, int mipLevel, RenderTextureSubElement element)
        {
            m_CommandBuffer.SetComputeTextureParam(computeShader, kernelIndex, nameID, rt, mipLevel, element);
        }

        public void SetComputeTextureParam(ComputeShader computeShader, int kernelIndex, string name, RenderTargetIdentifier rt, int mipLevel, RenderTextureSubElement element)
        {
            m_CommandBuffer.SetComputeTextureParam(computeShader, kernelIndex, name, rt, mipLevel, element);
        }

        public void SetComputeBufferParam(ComputeShader computeShader, int kernelIndex, int nameID, ComputeBuffer buffer)
        {
            m_CommandBuffer.SetComputeBufferParam(computeShader, kernelIndex, nameID, buffer);
        }

        public void SetComputeBufferParam(ComputeShader computeShader, int kernelIndex, string name, ComputeBuffer buffer)
        {
            m_CommandBuffer.SetComputeBufferParam(computeShader, kernelIndex, name, buffer);
        }

        public void SetComputeBufferParam(ComputeShader computeShader, int kernelIndex, int nameID, GraphicsBufferHandle bufferHandle)
        {
            m_CommandBuffer.SetComputeBufferParam(computeShader, kernelIndex, nameID, bufferHandle);
        }

        public void SetComputeBufferParam(ComputeShader computeShader, int kernelIndex, string name, GraphicsBufferHandle bufferHandle)
        {
            m_CommandBuffer.SetComputeBufferParam(computeShader, kernelIndex, name, bufferHandle);
        }

        public void SetComputeBufferParam(ComputeShader computeShader, int kernelIndex, int nameID, GraphicsBuffer buffer)
        {
            m_CommandBuffer.SetComputeBufferParam(computeShader, kernelIndex, nameID, buffer);
        }

        public void SetComputeBufferParam(ComputeShader computeShader, int kernelIndex, string name, GraphicsBuffer buffer)
        {
            m_CommandBuffer.SetComputeBufferParam(computeShader, kernelIndex, name, buffer);
        }

        public void SetComputeConstantBufferParam(ComputeShader computeShader, int nameID, ComputeBuffer buffer, int offset, int size)
        {
            m_CommandBuffer.SetComputeConstantBufferParam(computeShader, nameID, buffer, offset, size);
        }

        public void SetComputeConstantBufferParam(ComputeShader computeShader, string name, ComputeBuffer buffer, int offset, int size)
        {
            m_CommandBuffer.SetComputeConstantBufferParam(computeShader, name, buffer, offset, size);
        }

        public void SetComputeConstantBufferParam(ComputeShader computeShader, int nameID, GraphicsBuffer buffer, int offset, int size)
        {
            m_CommandBuffer.SetComputeConstantBufferParam(computeShader, nameID, buffer, offset, size);
        }

        public void SetComputeConstantBufferParam(ComputeShader computeShader, string name, GraphicsBuffer buffer, int offset, int size)
        {
            m_CommandBuffer.SetComputeConstantBufferParam(computeShader, name, buffer, offset, size);
        }

        public void SetComputeParamsFromMaterial(ComputeShader computeShader, int kernelIndex, Material material)
        {
            m_CommandBuffer.SetComputeParamsFromMaterial(computeShader, kernelIndex, material);
        }

        public void SetComputeRayTracingAccelerationStructure(ComputeShader computeShader, int kernelIndex, string name, RayTracingAccelerationStructure rayTracingAccelerationStructure)
        {
            m_CommandBuffer.SetRayTracingAccelerationStructure(computeShader, kernelIndex, name, rayTracingAccelerationStructure);
        }

        public void SetComputeRayTracingAccelerationStructure(ComputeShader computeShader, int kernelIndex, int nameID, RayTracingAccelerationStructure rayTracingAccelerationStructure)
        {
            m_CommandBuffer.SetRayTracingAccelerationStructure(computeShader, kernelIndex, nameID, rayTracingAccelerationStructure);
        }

        #endregion SetLocalParameter

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

        public static implicit operator CommandBuffer(in RGRaytracingEncoder cmdEncoder) => cmdEncoder.m_CommandBuffer;

        internal RGRaytracingEncoder(CommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
        }

        public void SetRayTracingAccelerationStructure(RayTracingShader rayTracingShader, string name, RayTracingAccelerationStructure rayTracingAccelerationStructure)
        {
            m_CommandBuffer.SetRayTracingAccelerationStructure(rayTracingShader, name, rayTracingAccelerationStructure);
        }

        public void SetRayTracingAccelerationStructure(RayTracingShader rayTracingShader, int nameID, RayTracingAccelerationStructure rayTracingAccelerationStructure)
        {
            m_CommandBuffer.SetRayTracingAccelerationStructure(rayTracingShader, nameID, rayTracingAccelerationStructure);
        }

        public void SetRayTracingBufferParam(RayTracingShader rayTracingShader, string name, ComputeBuffer buffer)
        {
            m_CommandBuffer.SetRayTracingBufferParam(rayTracingShader, name, buffer);
        }

        public void SetRayTracingBufferParam(RayTracingShader rayTracingShader, int nameID, ComputeBuffer buffer)
        {
            m_CommandBuffer.SetRayTracingBufferParam(rayTracingShader, nameID, buffer);
        }

        public void SetRayTracingBufferParam(RayTracingShader rayTracingShader, string name, GraphicsBuffer buffer)
        {
            m_CommandBuffer.SetRayTracingBufferParam(rayTracingShader, name, buffer);
        }

        public void SetRayTracingBufferParam(RayTracingShader rayTracingShader, int nameID, GraphicsBuffer buffer)
        {
            m_CommandBuffer.SetRayTracingBufferParam(rayTracingShader, nameID, buffer);
        }

        public void SetRayTracingBufferParam(RayTracingShader rayTracingShader, string name, GraphicsBufferHandle bufferHandle)
        {
            m_CommandBuffer.SetRayTracingBufferParam(rayTracingShader, name, bufferHandle);
        }

        public void SetRayTracingBufferParam(RayTracingShader rayTracingShader, int nameID, GraphicsBufferHandle bufferHandle)
        {
            m_CommandBuffer.SetRayTracingBufferParam(rayTracingShader, nameID, bufferHandle);
        }

        public void SetRayTracingConstantBufferParam(RayTracingShader rayTracingShader, int nameID, ComputeBuffer buffer, int offset, int size)
        {
            m_CommandBuffer.SetRayTracingConstantBufferParam(rayTracingShader, nameID, buffer, offset, size);
        }

        public void SetRayTracingConstantBufferParam(RayTracingShader rayTracingShader, string name, ComputeBuffer buffer, int offset, int size)
        {
            m_CommandBuffer.SetRayTracingConstantBufferParam(rayTracingShader, name, buffer, offset, size);
        }

        public void SetRayTracingConstantBufferParam(RayTracingShader rayTracingShader, int nameID, GraphicsBuffer buffer, int offset, int size)
        {
            m_CommandBuffer.SetRayTracingConstantBufferParam(rayTracingShader, nameID, buffer, offset, size);
        }

        public void SetRayTracingConstantBufferParam(RayTracingShader rayTracingShader, string name, GraphicsBuffer buffer, int offset, int size)
        {
            m_CommandBuffer.SetRayTracingConstantBufferParam(rayTracingShader, name, buffer, offset, size);
        }

        public void SetRayTracingTextureParam(RayTracingShader rayTracingShader, string name, RenderTargetIdentifier rt)
        {
            m_CommandBuffer.SetRayTracingTextureParam(rayTracingShader, name, rt);
        }

        public void SetRayTracingTextureParam(RayTracingShader rayTracingShader, int nameID, RenderTargetIdentifier rt)
        {
            m_CommandBuffer.SetRayTracingTextureParam(rayTracingShader, nameID, rt);
        }

        public void SetRayTracingFloatParam(RayTracingShader rayTracingShader, string name, float val)
        {
            m_CommandBuffer.SetRayTracingFloatParam(rayTracingShader, name, val);
        }

        public void SetRayTracingFloatParam(RayTracingShader rayTracingShader, int nameID, float val)
        {
            m_CommandBuffer.SetRayTracingFloatParam(rayTracingShader, nameID, val);
        }

        public void SetRayTracingFloatParams(RayTracingShader rayTracingShader, string name, params float[] values)
        {
            m_CommandBuffer.SetRayTracingFloatParams(rayTracingShader, name, values);
        }

        public void SetRayTracingFloatParams(RayTracingShader rayTracingShader, int nameID, params float[] values)
        {
            m_CommandBuffer.SetRayTracingFloatParams(rayTracingShader, nameID, values);
        }

        public void SetRayTracingIntParam(RayTracingShader rayTracingShader, string name, int val)
        {
            m_CommandBuffer.SetRayTracingIntParam(rayTracingShader, name, val);
        }

        public void SetRayTracingIntParam(RayTracingShader rayTracingShader, int nameID, int val)
        {
            m_CommandBuffer.SetRayTracingIntParam(rayTracingShader, nameID, val);
        }

        public void SetRayTracingIntParams(RayTracingShader rayTracingShader, string name, params int[] values)
        {
            m_CommandBuffer.SetRayTracingIntParams(rayTracingShader, name, values);
        }

        public void SetRayTracingIntParams(RayTracingShader rayTracingShader, int nameID, params int[] values)
        {
            m_CommandBuffer.SetRayTracingIntParams(rayTracingShader, nameID, values);
        }

        public void SetRayTracingVectorParam(RayTracingShader rayTracingShader, string name, Vector4 val)
        {
            m_CommandBuffer.SetRayTracingVectorParam(rayTracingShader, name, val);
        }

        public void SetRayTracingVectorParam(RayTracingShader rayTracingShader, int nameID, Vector4 val)
        {
            m_CommandBuffer.SetRayTracingVectorParam(rayTracingShader, nameID, val);
        }

        public void SetRayTracingVectorArrayParam(RayTracingShader rayTracingShader, string name, params Vector4[] values)
        {
            m_CommandBuffer.SetRayTracingVectorArrayParam(rayTracingShader, name, values);
        }

        public void SetRayTracingVectorArrayParam(RayTracingShader rayTracingShader, int nameID, params Vector4[] values)
        {
            m_CommandBuffer.SetRayTracingVectorArrayParam(rayTracingShader, nameID, values);
        }

        public void SetRayTracingMatrixParam(RayTracingShader rayTracingShader, string name, Matrix4x4 val)
        {
            m_CommandBuffer.SetRayTracingMatrixParam(rayTracingShader, name, val);
        }

        public void SetRayTracingMatrixParam(RayTracingShader rayTracingShader, int nameID, Matrix4x4 val)
        {
            m_CommandBuffer.SetRayTracingMatrixParam(rayTracingShader, nameID, val);
        }

        public void SetRayTracingMatrixArrayParam(RayTracingShader rayTracingShader, string name, params Matrix4x4[] values)
        {
            m_CommandBuffer.SetRayTracingMatrixArrayParam(rayTracingShader, name, values);
        }

        public void SetRayTracingMatrixArrayParam(RayTracingShader rayTracingShader, int nameID, params Matrix4x4[] values)
        {
            m_CommandBuffer.SetRayTracingMatrixArrayParam(rayTracingShader, nameID, values);
        }

        public void BuildRayTracingAccelerationStructure(RayTracingAccelerationStructure accelerationStructure)
        {
            m_CommandBuffer.BuildRayTracingAccelerationStructure(accelerationStructure);
        }

        public void BuildRayTracingAccelerationStructure(RayTracingAccelerationStructure accelerationStructure, Vector3 relativeOrigin)
        {
            m_CommandBuffer.BuildRayTracingAccelerationStructure(accelerationStructure, relativeOrigin);
        }

        public void BuildRayTracingAccelerationStructure(RayTracingAccelerationStructure accelerationStructure, RayTracingAccelerationStructure.BuildSettings buildSettings)
        {
            m_CommandBuffer.BuildRayTracingAccelerationStructure(accelerationStructure, buildSettings);
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

        public static implicit operator CommandBuffer(in RGRasterEncoder cmdEncoder) => cmdEncoder.m_CommandBuffer;

        internal RGRasterEncoder(CommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
        }

        #region SetGlobalParameter
        public void SetGlobalFloat(string name, float value)
        {
            m_CommandBuffer.SetGlobalFloat(name, value);
        }

        public void SetGlobalInt(string name, int value)
        {
            m_CommandBuffer.SetGlobalInt(name, value);
        }

        public void SetGlobalInteger(string name, int value)
        {
            m_CommandBuffer.SetGlobalInteger(name, value);
        }

        public void SetGlobalVector(string name, Vector4 value)
        {
            m_CommandBuffer.SetGlobalVector(name, value);
        }

        public void SetGlobalColor(string name, Color value)
        {
            m_CommandBuffer.SetGlobalColor(name, value);
        }

        public void SetGlobalMatrix(string name, Matrix4x4 value)
        {
            m_CommandBuffer.SetGlobalMatrix(name, value);
        }

        public void SetGlobalFloatArray(string propertyName, List<float> values)
        {
            m_CommandBuffer.SetGlobalFloatArray(propertyName, values);
        }

        public void SetGlobalFloatArray(int nameID, List<float> values)
        {
            m_CommandBuffer.SetGlobalFloatArray(nameID, values);
        }

        public void SetGlobalFloatArray(string propertyName, float[] values)
        {
            m_CommandBuffer.SetGlobalFloatArray(propertyName, values);
        }

        public void SetGlobalVectorArray(string propertyName, List<Vector4> values)
        {
            m_CommandBuffer.SetGlobalVectorArray(propertyName, values);
        }

        public void SetGlobalVectorArray(int nameID, List<Vector4> values)
        {
            m_CommandBuffer.SetGlobalVectorArray(nameID, values);
        }

        public void SetGlobalVectorArray(string propertyName, Vector4[] values)
        {
            m_CommandBuffer.SetGlobalVectorArray(propertyName, values);
        }

        public void SetGlobalMatrixArray(string propertyName, List<Matrix4x4> values)
        {
            m_CommandBuffer.SetGlobalMatrixArray(propertyName, values);
        }

        public void SetGlobalMatrixArray(int nameID, List<Matrix4x4> values)
        {
            m_CommandBuffer.SetGlobalMatrixArray(nameID, values);
        }

        public void SetGlobalMatrixArray(string propertyName, Matrix4x4[] values)
        {
            m_CommandBuffer.SetGlobalMatrixArray(propertyName, values);
        }

        public void SetGlobalTexture(string name, RenderTargetIdentifier value)
        {
            m_CommandBuffer.SetGlobalTexture(name, value);
        }

        public void SetGlobalTexture(int nameID, RenderTargetIdentifier value)
        {
            m_CommandBuffer.SetGlobalTexture(nameID, value);
        }

        public void SetGlobalTexture(string name, RenderTargetIdentifier value, RenderTextureSubElement element)
        {
            m_CommandBuffer.SetGlobalTexture(name, value, element);
        }

        public void SetGlobalTexture(int nameID, RenderTargetIdentifier value, RenderTextureSubElement element)
        {
            m_CommandBuffer.SetGlobalTexture(nameID, value, element);
        }

        public void SetGlobalBuffer(string name, ComputeBuffer value)
        {
            m_CommandBuffer.SetGlobalBuffer(name, value);
        }

        public void SetGlobalBuffer(int nameID, ComputeBuffer value)
        {
            m_CommandBuffer.SetGlobalBuffer(nameID, value);
        }

        public void SetGlobalBuffer(string name, GraphicsBuffer value)
        {
            m_CommandBuffer.SetGlobalBuffer(name, value);
        }

        public void SetGlobalBuffer(int nameID, GraphicsBuffer value)
        {
            m_CommandBuffer.SetGlobalBuffer(nameID, value);
        }

        public void SetGlobalConstantBuffer(ComputeBuffer buffer, int nameID, int offset, int size)
        {
            m_CommandBuffer.SetGlobalConstantBuffer(buffer, nameID, offset, size);
        }

        public void SetGlobalConstantBuffer(ComputeBuffer buffer, string name, int offset, int size)
        {
            m_CommandBuffer.SetGlobalConstantBuffer(buffer, name, offset, size);
        }

        public void SetGlobalConstantBuffer(GraphicsBuffer buffer, int nameID, int offset, int size)
        {
            m_CommandBuffer.SetGlobalConstantBuffer(buffer, nameID, offset, size);
        }

        public void SetGlobalConstantBuffer(GraphicsBuffer buffer, string name, int offset, int size)
        {
            m_CommandBuffer.SetGlobalConstantBuffer(buffer, name, offset, size);
        }
        #endregion SetGlobalParameter

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