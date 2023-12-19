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
        struct AtmospherePassData
        {
            public RDGTextureRef skyTarget;
            public RDGTextureRef volumeLUT;
            public RDGTextureRef scatteringLUT;
            public RDGTextureRef transmittionLUT;
        }

        void RenderSkyAtmosphere(Camera RenderCamera)
        {
            //Add SkyAtmospherePass
            using (RDGPassRef passRef = m_GraphBuilder.AddPass<AtmospherePassData>("SkyAtmospherePass", ProfilingSampler.Get(CustomSamplerId.RenderAtmosphere)))
            {
                //Setup Phase
                ref AtmospherePassData passData = ref passRef.GetPassData<AtmospherePassData>();

                //Execute Phase
                passRef.SetExecuteFunc((in AtmospherePassData passData, in RDGContext graphContext) =>
                {

                });
            }
        }
    }
}
