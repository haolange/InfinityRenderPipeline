using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Profiling;

namespace InfinityTech.Runtime.Rendering.Pipeline
{
    public enum CustomSamplerId
    {
        OpaqueDepth,
        OpaqueGBuffer,
        OpaqueMotion,
        SkyAtmosphere,
        RenderGizmos,
        PresentView,
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
        public static int RT_DepthBuffer = Shader.PropertyToID("_GBufferDepth");
        public static int RT_ThinGBufferA = Shader.PropertyToID("_ThinGBufferA");
        public static int RT_ThinGBufferB = Shader.PropertyToID("_ThinGBufferB");
        public static int RT_MotionBuffer = Shader.PropertyToID("_MotionBuffer");

        public static RenderTargetIdentifier[] ID_GBuffers = { RT_ThinGBufferA, RT_ThinGBufferB };

        public static int RT_MainTexture = Shader.PropertyToID("_MainTex");
        public static int BlitScaleBias = Shader.PropertyToID("_ScaleBais");
    }
    
    public static class InfinityPassIDs {
        public static ShaderTagId OpaqueDepth = new ShaderTagId("OpaqueDepth");
        public static ShaderTagId OpaqueGBuffer = new ShaderTagId("OpaqueGBuffer");
        public static ShaderTagId OpaqueMotion = new ShaderTagId("OpaqueMotion");
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
