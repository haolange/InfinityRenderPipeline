using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    public partial class InfinityRenderPipeline
    {
        struct FAtmospherePassData
        {
            public FRDGTextureRef skyTarget;
            public FRDGTextureRef volumeLUT;
            public FRDGTextureRef scatteringLUT;
            public FRDGTextureRef transmittionLUT;
        }

        void RenderSkyAtmosphere(Camera RenderCamera)
        {
            //Add SkyAtmospherePass
            using (FRDGPassRef passRef = m_GraphBuilder.AddPass<FAtmospherePassData>("SkyAtmosphere", ProfilingSampler.Get(CustomSamplerId.RenderAtmosphere)))
            {
                //Setup Phase
                ref FAtmospherePassData passData = ref passRef.GetPassData<FAtmospherePassData>();

                //Execute Phase
                passRef.SetExecuteFunc((in FAtmospherePassData passData, in FRDGContext graphContext) =>
                {

                });
            }
        }
    }
}
