using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Profiling;

namespace InfinityTech.Rendering.Pipeline
{
    public enum CustomSamplerId
    {
        MeshDrawPipeline,
        OpaqueDepth,
        OpaqueGBuffer,
        OpaqueMotion,
        OpaqueForward,
        SkyAtmosphere,
        SkyBox,
        Gizmos,
        Present,
        Max,
    }

    public static class InfinityCustomSamplerExtension
    {
        static CustomSampler[] s_Samplers;

        public static CustomSampler GetSampler(this CustomSamplerId samplerId)
        {
            // Lazy init
            if (s_Samplers == null)
            {
                s_Samplers = new CustomSampler[(int)CustomSamplerId.Max];

                for (int i = 0; i < (int)CustomSamplerId.Max; i++)
                {
                    var id = (CustomSamplerId)i;
                    s_Samplers[i] = CustomSampler.Create("C#_" + id);
                }
            }

            return s_Samplers[(int)samplerId];
        }
    }
    

    public static class InfinityShaderIDs
    {
        public static int DepthBuffer = Shader.PropertyToID("_DepthTexture");
        public static int GBufferA = Shader.PropertyToID("_GBufferTextureA");
        public static int GBufferB = Shader.PropertyToID("_GBufferTextureB");
        public static int MotionBuffer = Shader.PropertyToID("_MotionTexture");
        public static int DiffuseBuffer = Shader.PropertyToID("_DiffuseTexture");
        public static int SpecularBuffer = Shader.PropertyToID("_SpecularTexture");

        public static RenderTargetIdentifier[] GBuffer_IDs = { GBufferA, GBufferB };

        public static int RT_MainTexture = Shader.PropertyToID("_MainTex");
        public static int BlitScaleBias = Shader.PropertyToID("_ScaleBais");
    }
    
    public static class InfinityPassIDs {
        public static ShaderTagId OpaqueDepth = new ShaderTagId("OpaqueDepth");
        public static ShaderTagId OpaqueGBuffer = new ShaderTagId("OpaqueGBuffer");
        public static ShaderTagId OpaqueMotion = new ShaderTagId("OpaqueMotion");
        public static ShaderTagId ForwardPlus = new ShaderTagId("ForwardPlus");
    }

    public static class InfinityRenderQueue
    {
        public enum Priority
        {
            Background = UnityEngine.Rendering.RenderQueue.Background,
            OpaqueLast = UnityEngine.Rendering.RenderQueue.GeometryLast,
        }
        public static readonly RenderQueueRange k_RenderQueue_AllOpaque = new RenderQueueRange { lowerBound = (int)Priority.Background, upperBound = (int)Priority.OpaqueLast };
    }
}
