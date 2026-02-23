using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class AtmosphericSkyFogPassUtilityData
    {
        internal static int SRV_TransmittanceLUTID = Shader.PropertyToID("SRV_TransmittanceLUT");
        internal static int SRV_MultiScatteringLUTID = Shader.PropertyToID("SRV_MultiScatteringLUT");
        internal static int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static int AtmoSky_SunDirectionID = Shader.PropertyToID("AtmoSky_SunDirection");
        internal static int AtmoSky_SunColorID = Shader.PropertyToID("AtmoSky_SunColor");
    }

    public partial class InfinityRenderPipeline
    {
        struct AtmosphericSkyFogPassData
        {
            public Vector4 sunDirection;
            public Vector4 sunColor;
            public Material skyFogMaterial;
            public RGTextureRef lightingTexture;
            public RGTextureRef depthTexture;
            public RGTextureRef transmittanceLUT;
            public RGTextureRef multiScatteringLUT;
        }

        void RenderAtmosphericSkyAndFog(RenderContext renderContext, Camera camera)
        {
            RGTextureRef lightingTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.LightingBuffer);
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureRef transmittanceLUT = m_RGScoper.QueryTexture(InfinityShaderIDs.AtmosphereTransmittanceLUT);
            RGTextureRef multiScatteringLUT = m_RGScoper.QueryTexture(InfinityShaderIDs.AtmosphereMultiScatteringLUT);

            // Find main directional light as sun
            Vector4 sunDirection = new Vector4(0, 1, 0, 0);
            Vector4 sunColor = new Vector4(1, 1, 1, 1);
            Light sunLight = RenderSettings.sun;
            if (sunLight != null)
            {
                sunDirection = -sunLight.transform.forward;
                sunColor = (Vector4)(sunLight.color * sunLight.intensity);
            }

            //Add AtmosphericSkyFogPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<AtmosphericSkyFogPassData>(ProfilingSampler.Get(CustomSamplerId.RenderAtmosphericSkyAndFog)))
            {
                //Setup Phase
                passRef.EnablePassCulling(false);
                passRef.SetColorAttachment(lightingTexture, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);
                passRef.ReadTexture(transmittanceLUT);
                passRef.ReadTexture(multiScatteringLUT);

                ref AtmosphericSkyFogPassData passData = ref passRef.GetPassData<AtmosphericSkyFogPassData>();
                {
                    passData.sunDirection = sunDirection;
                    passData.sunColor = sunColor;
                    passData.skyFogMaterial = GraphicsUtility.BlitMaterial;
                    passData.lightingTexture = lightingTexture;
                    passData.depthTexture = depthTexture;
                    passData.transmittanceLUT = transmittanceLUT;
                    passData.multiScatteringLUT = multiScatteringLUT;
                }

                //Execute Phase
                passRef.SetExecuteFunc((in AtmosphericSkyFogPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    // Set atmosphere sky parameters for the sky rendering shader
                    cmdEncoder.SetGlobalVector(AtmosphericSkyFogPassUtilityData.AtmoSky_SunDirectionID, passData.sunDirection);
                    cmdEncoder.SetGlobalVector(AtmosphericSkyFogPassUtilityData.AtmoSky_SunColorID, passData.sunColor);
                    cmdEncoder.SetGlobalTexture(AtmosphericSkyFogPassUtilityData.SRV_TransmittanceLUTID, passData.transmittanceLUT);
                    cmdEncoder.SetGlobalTexture(AtmosphericSkyFogPassUtilityData.SRV_MultiScatteringLUTID, passData.multiScatteringLUT);
                    cmdEncoder.SetGlobalTexture(AtmosphericSkyFogPassUtilityData.SRV_DepthTextureID, passData.depthTexture);

                    // Draw skybox - the shader will sample atmosphere LUTs
                    if (passData.skyFogMaterial != null)
                    {
                        cmdEncoder.DrawMesh(GraphicsUtility.FullScreenMesh, Matrix4x4.identity, passData.skyFogMaterial, 0, 2);
                    }
                });
            }
        }
    }
}
