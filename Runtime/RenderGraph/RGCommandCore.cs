using System;
using System.Collections.Generic;
using UnityEngine;
using InfinityTech.Rendering.GPUResource;
using UnityEngine.Rendering;
using UnityEngine.Rendering.UnifiedRayTracing;

namespace InfinityTech.Rendering.RenderGraph
{
    // The one implementation of every command more than one pass-type encoder offers. A public encoder restricts which
    // commands its pass type may record and forwards each of them here; graph handles resolve only through the graph's own
    // factory, so a handle from another graph is rejected where it is used.
    internal readonly struct RGCommandCore
    {
        internal readonly CommandBuffer commands;
        readonly RGResourceFactory resources;

        internal RGCommandCore(CommandBuffer commands, RGResourceFactory resources)
        {
            this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
            this.resources = resources ?? throw new ArgumentNullException(nameof(resources));
        }

        #region Graph resources
        RenderTargetIdentifier Identify(in RGTextureHandle textureHandle) => resources.ResolveTexture(textureHandle).resource;

        // Resolves a graph texture once. Returns true with the logical view to write when the property has a registered view.
        internal bool ResolveTexture(int nameID, in RGTextureHandle textureHandle, int mip, out RenderTargetIdentifier identifier, out int viewID, out Vector4 view)
        {
            RGTexture resource = resources.ResolveTexture(textureHandle);
            identifier = resource.resource;
            if (!TextureViewBindings.TryGetViewID(nameID, out viewID))
            {
                view = default;
                return false;
            }
            if (resource.descriptor.width <= 0)
            {
                throw new InvalidOperationException($"Graph texture '{resource.GetName()}' has no logical size, so it cannot be bound to texture-view property {RGResourceKey.NameOf(nameID)}.");
            }
            view = TextureViewBindings.MipView(resource.descriptor.view, mip);
            return true;
        }

        // A texture outside the graph has no logical view to publish, so it cannot feed a texture-view property.
        static void RejectViewProperty(int nameID)
        {
            if (TextureViewBindings.TryGetViewID(nameID, out _))
            {
                throw new InvalidOperationException($"Shader property {RGResourceKey.NameOf(nameID)} is a texture-view property; bind a graph texture.");
            }
        }

        internal void SetGlobalTexture(int nameID, in RGTextureHandle textureHandle, RenderTextureSubElement element)
        {
            if (ResolveTexture(nameID, textureHandle, 0, out RenderTargetIdentifier identifier, out int viewID, out Vector4 view))
            {
                commands.SetGlobalVector(viewID, view);
            }
            commands.SetGlobalTexture(nameID, identifier, element);
        }

        internal void SetGlobalTexture(int nameID, in RenderTargetIdentifier texture, RenderTextureSubElement element)
        {
            RejectViewProperty(nameID);
            commands.SetGlobalTexture(nameID, texture, element);
        }

        // The caller states the logical view of a texture outside the graph; it is written only when the property has a view companion.
        internal void SetGlobalTexture(int nameID, in RenderTargetIdentifier texture, in TextureView view)
        {
            if (TextureViewBindings.TryGetViewID(nameID, out int viewID))
            {
                commands.SetGlobalVector(viewID, view.shaderValue);
            }
            commands.SetGlobalTexture(nameID, texture);
        }

        internal void SetComputeTexture(ComputeShader shader, int kernel, int nameID, in RenderTargetIdentifier texture, in TextureView view)
        {
            if (TextureViewBindings.TryGetViewID(nameID, out int viewID))
            {
                commands.SetComputeVectorParam(shader, viewID, view.shaderValue);
            }
            commands.SetComputeTextureParam(shader, kernel, nameID, texture);
        }

        internal void SetComputeTexture(ComputeShader shader, int kernel, int nameID, in RGTextureHandle textureHandle, int mip, RenderTextureSubElement element)
        {
            if (ResolveTexture(nameID, textureHandle, mip, out RenderTargetIdentifier identifier, out int viewID, out Vector4 view))
            {
                commands.SetComputeVectorParam(shader, viewID, view);
            }
            commands.SetComputeTextureParam(shader, kernel, nameID, identifier, mip, element);
        }

        internal void SetComputeTexture(ComputeShader shader, int kernel, int nameID, in RenderTargetIdentifier texture, int mip, RenderTextureSubElement element)
        {
            RejectViewProperty(nameID);
            commands.SetComputeTextureParam(shader, kernel, nameID, texture, mip, element);
        }

        internal void SetRayTracingTexture(RayTracingShader shader, int nameID, in RGTextureHandle textureHandle)
        {
            if (ResolveTexture(nameID, textureHandle, 0, out RenderTargetIdentifier identifier, out int viewID, out Vector4 view))
            {
                commands.SetRayTracingVectorParam(shader, viewID, view);
            }
            commands.SetRayTracingTextureParam(shader, nameID, identifier);
        }

        internal void SetRayTracingTexture(RayTracingShader shader, int nameID, in RenderTargetIdentifier texture)
        {
            RejectViewProperty(nameID);
            commands.SetRayTracingTextureParam(shader, nameID, texture);
        }

        internal void SetUnifiedTexture(IRayTracingShader shader, int nameID, in RGTextureHandle textureHandle)
        {
            if (ResolveTexture(nameID, textureHandle, 0, out RenderTargetIdentifier identifier, out int viewID, out Vector4 view))
            {
                shader.SetVectorParam(commands, viewID, view);
            }
            shader.SetTextureParam(commands, nameID, identifier);
        }

        internal void SetUnifiedTexture(IRayTracingShader shader, int nameID, in RenderTargetIdentifier texture)
        {
            RejectViewProperty(nameID);
            shader.SetTextureParam(commands, nameID, texture);
        }

        internal void SetGlobalBuffer(int nameID, in RGBufferHandle bufferHandle)
            => commands.SetGlobalBuffer(nameID, resources.ResolveBuffer(bufferHandle).resource);

        internal void SetComputeBufferParam(ComputeShader shader, int kernel, int nameID, in RGBufferHandle bufferHandle)
            => commands.SetComputeBufferParam(shader, kernel, nameID, resources.ResolveBuffer(bufferHandle).resource);

        internal void SetComputeConstantBufferParam(ComputeShader shader, int nameID, in RGConstantsHandle constantsHandle)
            => commands.SetComputeConstantBufferParam(shader, nameID, resources.ResolveBuffer(constantsHandle.bufferHandle).resource, 0, constantsHandle.size);

        internal void DispatchComputeIndirect(ComputeShader shader, int kernel, in RGBufferHandle argsBufferHandle, uint argsOffset)
        {
            RGBuffer resource = resources.ResolveBuffer(argsBufferHandle);
            if ((resource.descriptor.target & GraphicsBuffer.Target.IndirectArguments) == 0)
                throw new InvalidOperationException($"RG buffer {resource.GetName()} is not an IndirectArguments buffer.");
            commands.DispatchCompute(shader, kernel, resource.resource, argsOffset);
        }

        internal void CopyTexture(in RGTextureHandle srcHandle, in RGTextureHandle dstHandle)
            => commands.CopyTexture(Identify(srcHandle), Identify(dstHandle));

        internal void CopyTexture(in RGTextureHandle srcHandle, int srcElement, in RGTextureHandle dstHandle, int dstElement)
            => commands.CopyTexture(Identify(srcHandle), srcElement, Identify(dstHandle), dstElement);

        internal void CopyTexture(in RGTextureHandle srcHandle, int srcElement, int srcMip, in RGTextureHandle dstHandle, int dstElement, int dstMip)
            => commands.CopyTexture(Identify(srcHandle), srcElement, srcMip, Identify(dstHandle), dstElement, dstMip);

        internal void CopyTexture(in RGTextureHandle srcHandle, int srcElement, int srcMip, int srcX, int srcY, int srcWidth, int srcHeight, in RGTextureHandle dstHandle, int dstElement, int dstMip, int dstX, int dstY)
            => commands.CopyTexture(Identify(srcHandle), srcElement, srcMip, srcX, srcY, srcWidth, srcHeight, Identify(dstHandle), dstElement, dstMip, dstX, dstY);
        #endregion Graph resources

        #region Global parameters
        internal void SetKeyword(GlobalKeyword keyword, bool value) => commands.SetKeyword(keyword, value);
        internal void SetKeyword(ComputeShader computeShader, in LocalKeyword keyword, bool value) => commands.SetKeyword(computeShader, keyword, value);
        internal void SetKeyword(Material material, in LocalKeyword keyword, bool value) => commands.SetKeyword(material, keyword, value);
        internal void SetGlobalFloat(int nameID, float value) => commands.SetGlobalFloat(nameID, value);
        internal void SetGlobalInt(int nameID, int value) => commands.SetGlobalInt(nameID, value);
        internal void SetGlobalInteger(int nameID, int value) => commands.SetGlobalInteger(nameID, value);
        internal void SetGlobalVector(int nameID, Vector4 value) => commands.SetGlobalVector(nameID, value);
        internal void SetGlobalColor(int nameID, Color value) => commands.SetGlobalColor(nameID, value);
        internal void SetGlobalMatrix(int nameID, Matrix4x4 value) => commands.SetGlobalMatrix(nameID, value);
        internal void SetGlobalFloatArray(int nameID, List<float> values) => commands.SetGlobalFloatArray(nameID, values);
        internal void SetGlobalFloatArray(int nameID, float[] values) => commands.SetGlobalFloatArray(nameID, values);
        internal void SetGlobalVectorArray(int nameID, List<Vector4> values) => commands.SetGlobalVectorArray(nameID, values);
        internal void SetGlobalVectorArray(int nameID, Vector4[] values) => commands.SetGlobalVectorArray(nameID, values);
        internal void SetGlobalMatrixArray(int nameID, List<Matrix4x4> values) => commands.SetGlobalMatrixArray(nameID, values);
        internal void SetGlobalMatrixArray(int nameID, Matrix4x4[] values) => commands.SetGlobalMatrixArray(nameID, values);
        internal void SetGlobalBuffer(int nameID, GraphicsBuffer value) => commands.SetGlobalBuffer(nameID, value);
        internal void SetGlobalConstantBuffer(GraphicsBuffer buffer, int nameID, int offset, int size) => commands.SetGlobalConstantBuffer(buffer, nameID, offset, size);
        #endregion Global parameters

        internal void SetBufferData(GraphicsBuffer buffer, Array data, int sourceIndex, int destinationIndex, int count) => commands.SetBufferData(buffer, data, sourceIndex, destinationIndex, count);
        internal void BeginSample(ProfilingSampler sampler) => sampler?.Begin(commands);
        internal void EndSample(ProfilingSampler sampler) => sampler?.End(commands);
    }
}
