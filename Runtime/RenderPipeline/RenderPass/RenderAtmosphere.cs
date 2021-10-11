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
            public RDGTextureRef SkyTarget;
            public RDGTextureRef VolumeLUT;
            public RDGTextureRef ScatteringLUT;
            public RDGTextureRef TransmittionLUT;
        }

        void RenderSkyAtmosphere(Camera RenderCamera)
        {
            //Add SkyAtmospherePass
            using (RDGPassRef passRef = m_GraphBuilder.AddPass<FAtmospherePassData>("SkyAtmosphere", ProfilingSampler.Get(CustomSamplerId.RenderAtmosphere)))
            {
                //Setup Phase
                ref FAtmospherePassData passData = ref passRef.GetPassData<FAtmospherePassData>();

                //Execute Phase
                passRef.SetExecuteFunc((ref FAtmospherePassData passData, ref RDGContext graphContext) =>
                {

                });
            }
        }
    }
}
