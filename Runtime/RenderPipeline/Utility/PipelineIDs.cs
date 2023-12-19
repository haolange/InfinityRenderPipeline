using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Profiling;

namespace InfinityTech.Rendering.Pipeline
{
    public enum CustomSamplerId
    {
        RenderDepth,
        RenderGBuffer,
        RenderMotionObject,
        CopyMotionDepth,
        RenderMotionCamera,
        RenderForward,
        RenderSkyBox,
        RenderAtmosphere,
        ComputeAntiAliasing,
        RenderWireOverlay,
        RenderGizmos,
        RenderPresent,
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

                for (int i = 0; i < (int)CustomSamplerId.Max; ++i)
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
        public static int MotionDepthBuffer = Shader.PropertyToID("_MotionDepthTexture");
        public static int LightingBuffer = Shader.PropertyToID("_LightingTexture");
        public static int AntiAliasingBuffer = Shader.PropertyToID("_AntiAliasingBuffer");
        public static int MainTexture = Shader.PropertyToID("_MainTex");
        public static int ScaleBias = Shader.PropertyToID("_ScaleBais");
        public static int MeshBatchOffset = Shader.PropertyToID("meshBatchOffset");
        public static int MeshBatchIndexs = Shader.PropertyToID("meshBatchIndexs");
        public static int MeshBatchBuffer = Shader.PropertyToID("meshBatchBuffer");
    }
    
    public static class InfinityPassIDs 
    {
        public static ShaderTagId DepthPass = new ShaderTagId("DepthPass");
        public static ShaderTagId GBufferPass = new ShaderTagId("GBufferPass");
        public static ShaderTagId MotionPass = new ShaderTagId("MotionPass");
        public static ShaderTagId ForwardPass = new ShaderTagId("ForwardPass");
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
