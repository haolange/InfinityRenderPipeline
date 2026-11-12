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
        RenderTargetIdentifier Identify(in RGTextureHandle texture) => resources.ResolveTexture(texture).resource;

        // Resolves a graph texture once. Returns true with the logical view to write when the property has a registered view.
        internal bool ResolveTexture(int nameID, in RGTextureHandle texture, int mip, out RenderTargetIdentifier identifier, out int viewID, out Vector4 view)
        {
            RGTexture resource = resources.ResolveTexture(texture);
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

        internal void SetGlobalTexture(int nameID, in RGTextureHandle texture, RenderTextureSubElement element)
        {
            if (ResolveTexture(nameID, texture, 0, out RenderTargetIdentifier identifier, out int viewID, out Vector4 view))
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

        internal void SetComputeTexture(ComputeShader shader, int kernel, int nameID, in RGTextureHandle texture, int mip, RenderTextureSubElement element)
        {
            if (ResolveTexture(nameID, texture, mip, out RenderTargetIdentifier identifier, out int viewID, out Vector4 view))
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

        internal void SetRayTracingTexture(RayTracingShader shader, int nameID, in RGTextureHandle texture)
        {
            if (ResolveTexture(nameID, texture, 0, out RenderTargetIdentifier identifier, out int viewID, out Vector4 view))
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

        internal void SetUnifiedTexture(IRayTracingShader shader, int nameID, in RGTextureHandle texture)
        {
            if (ResolveTexture(nameID, texture, 0, out RenderTargetIdentifier identifier, out int viewID, out Vector4 view))
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

        internal void SetGlobalBuffer(int nameID, in RGBufferHandle buffer)
        {
            RGBuffer resource = resources.ResolveBuffer(buffer);
            if (resource.graphicsResource != null)
            {
                commands.SetGlobalBuffer(nameID, resource.graphicsResource);
            }
            else
            {
                commands.SetGlobalBuffer(nameID, resource.resource);
            }
        }

        internal void SetComputeBuffer(ComputeShader shader, int kernel, int nameID, in RGBufferHandle buffer)
        {
            RGBuffer resource = resources.ResolveBuffer(buffer);
            if (resource.graphicsResource != null)
            {
                commands.SetComputeBufferParam(shader, kernel, nameID, resource.graphicsResource);
            }
            else
            {
                commands.SetComputeBufferParam(shader, kernel, nameID, resource.resource);
            }
        }

        internal void DispatchComputeIndirect(ComputeShader shader, int kernel, in RGBufferHandle argsBuffer, uint argsOffset)
        {
            RGBuffer resource = resources.ResolveBuffer(argsBuffer);
            if (resource.graphicsResource != null)
            {
                if ((resource.graphicsTarget & GraphicsBuffer.Target.IndirectArguments) == 0)
                    throw new InvalidOperationException($"RG buffer {resource.GetName()} is not an IndirectArguments buffer.");
                commands.DispatchCompute(shader, kernel, resource.graphicsResource, argsOffset);
            }
            else
            {
                if ((resource.descriptor.type & ComputeBufferType.IndirectArguments) == 0)
                    throw new InvalidOperationException($"RG buffer {resource.GetName()} is not an IndirectArguments buffer.");
                commands.DispatchCompute(shader, kernel, resource.resource, argsOffset);
            }
        }

        internal void CopyTexture(in RGTextureHandle src, in RGTextureHandle dst)
            => commands.CopyTexture(Identify(src), Identify(dst));

        internal void CopyTexture(in RGTextureHandle src, int srcElement, in RGTextureHandle dst, int dstElement)
            => commands.CopyTexture(Identify(src), srcElement, Identify(dst), dstElement);

        internal void CopyTexture(in RGTextureHandle src, int srcElement, int srcMip, in RGTextureHandle dst, int dstElement, int dstMip)
            => commands.CopyTexture(Identify(src), srcElement, srcMip, Identify(dst), dstElement, dstMip);

        internal void CopyTexture(in RGTextureHandle src, int srcElement, int srcMip, int srcX, int srcY, int srcWidth, int srcHeight, in RGTextureHandle dst, int dstElement, int dstMip, int dstX, int dstY)
            => commands.CopyTexture(Identify(src), srcElement, srcMip, srcX, srcY, srcWidth, srcHeight, Identify(dst), dstElement, dstMip, dstX, dstY);
        #endregion Graph resources

        #region Global parameters
        internal void SetKeyword(GlobalKeyword keyword, bool value) => commands.SetKeyword(keyword, value);
        internal void SetKeyword(ComputeShader computeShader, in LocalKeyword keyword, bool value) => commands.SetKeyword(computeShader, keyword, value);
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
        internal void SetGlobalBuffer(int nameID, ComputeBuffer value) => commands.SetGlobalBuffer(nameID, value);
        internal void SetGlobalBuffer(int nameID, GraphicsBuffer value) => commands.SetGlobalBuffer(nameID, value);
        internal void SetGlobalConstantBuffer(ComputeBuffer buffer, int nameID, int offset, int size) => commands.SetGlobalConstantBuffer(buffer, nameID, offset, size);
        internal void SetGlobalConstantBuffer(GraphicsBuffer buffer, int nameID, int offset, int size) => commands.SetGlobalConstantBuffer(buffer, nameID, offset, size);
        #endregion Global parameters

        internal void SetBufferData(ComputeBuffer buffer, Array data, int sourceIndex, int destinationIndex, int count) => commands.SetBufferData(buffer, data, sourceIndex, destinationIndex, count);
        internal void BeginSample(ProfilingSampler sampler) => sampler?.Begin(commands);
        internal void EndSample(ProfilingSampler sampler) => sampler?.End(commands);
    }
}
