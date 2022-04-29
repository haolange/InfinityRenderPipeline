using System;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Component;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace InfinityTech.Rendering.LightPipeline
{
    internal struct FDrawShadowRequest
    {

    }

    internal struct FLocalLightElement
    {

    }

    internal struct FDirectionalLightElement
    {
        public Color color;
        public float4 directional;
        public float diffuse;
        public float specular;
        public float radius;
        public ELightLayer lightLayer;

        public int enableIndirect;
        public float indirectIntensity;
        public EShadowType shadowType;
        public float minSoftness;

        public float maxSoftness;
        public int enableContactShadow;
        public float contactShadowLength;
        public int enableVolumetric;

        public float volumetricIntensity;
        public float volumetricOcclusion;
        public float maxDrawDistance;
        public float maxDrawDistanceFade;
    }

    internal static class LightShaderIDs
    {
        public static int DirectionalLightCount = Shader.PropertyToID("g_DirectionalLightCount");
        public static int DirectionalLightBuffer = Shader.PropertyToID("g_DirectionalLightBuffer");
    }

    internal class LightContext : IDisposable
    {
        int m_DirectionalLightCount;
        int m_DirectionalLightByteSize;
        GraphicsBuffer m_DirectionalLightBuffer;
        NativeList<FDirectionalLightElement> m_DirectionalLightElements;

        internal LightContext()
        {
            m_DirectionalLightCount = 2;
            m_DirectionalLightByteSize = Marshal.SizeOf(typeof(FDirectionalLightElement));
            m_DirectionalLightBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, m_DirectionalLightByteSize);
            m_DirectionalLightElements = new NativeList<FDirectionalLightElement>(2, Allocator.Persistent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Clear()
        {
            m_DirectionalLightElements.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetDirectionalLightData(CommandBuffer cmdBuffer)
        {
            cmdBuffer.SetGlobalInt(LightShaderIDs.DirectionalLightCount, m_DirectionalLightElements.Length);
            if (m_DirectionalLightElements.Length == 0) return;

            if(m_DirectionalLightCount < m_DirectionalLightElements.Length)
            {
                m_DirectionalLightBuffer.Release();
                m_DirectionalLightCount = m_DirectionalLightElements.Length;
                m_DirectionalLightBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_DirectionalLightCount, m_DirectionalLightByteSize);
            }

            cmdBuffer.SetBufferData(m_DirectionalLightBuffer, m_DirectionalLightElements.AsArray());
            cmdBuffer.SetGlobalBuffer(LightShaderIDs.DirectionalLightBuffer, m_DirectionalLightBuffer);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void AddDirectionalLight(in int index, LightComponent light)
        {
            FDirectionalLightElement lightElement;
            lightElement.directional = -Vector4.Normalize(light.transform.forward);
            lightElement.lightLayer = light.lightLayer;
            lightElement.color = light.color * light.intensity;
            lightElement.diffuse = light.diffuse;
            lightElement.specular = light.specular;
            lightElement.radius = light.radius;
            lightElement.enableIndirect = light.enableIndirect ? 1 : 0;
            lightElement.indirectIntensity = light.indirectIntensity;
            lightElement.shadowType = light.shadowType;
            lightElement.minSoftness = light.minSoftness;
            lightElement.maxSoftness = light.maxSoftness;
            lightElement.enableContactShadow = light.enableContactShadow ? 1 : 0;
            lightElement.contactShadowLength = light.contactShadowLength;
            lightElement.enableVolumetric = light.enableVolumetric ? 1 : 0;
            lightElement.volumetricIntensity = light.volumetricIntensity;
            lightElement.volumetricOcclusion = light.volumetricOcclusion;
            lightElement.maxDrawDistance = light.maxDrawDistance;
            lightElement.maxDrawDistanceFade = light.maxDrawDistanceFade;
            m_DirectionalLightElements.Add(lightElement);
        }

        public void Dispose()
        {
            m_DirectionalLightBuffer.Release();
            m_DirectionalLightElements.Dispose();
        }
    }
}
