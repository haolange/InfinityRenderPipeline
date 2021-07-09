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
        struct FSkyAtmosphereData
        {
            public RDGTextureRef SkyTarget;
            public RDGTextureRef VolumeLUT;
            public RDGTextureRef ScatteringLUT;
            public RDGTextureRef TransmittionLUT;
        }

        void RenderSkyAtmosphere(Camera RenderCamera)
        {
            //Add SkyAtmospherePass
            using (RDGPassBuilder passBuilder = m_GraphBuilder.AddPass<FSkyAtmosphereData>("SkyAtmosphere", ProfilingSampler.Get(CustomSamplerId.SkyAtmosphere)))
            {
                //Setup Phase
                ref FSkyAtmosphereData passData = ref passBuilder.GetPassData<FSkyAtmosphereData>();

                //Execute Phase
                passBuilder.SetRenderFunc((ref FOpaqueDepthData passData, ref RDGGraphContext graphContext) =>
                {

                });
            }
        }
    }
}
