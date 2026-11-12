using UnityEngine;
using InfinityTech.Core;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class LightCookiePassUtilityData
    {
        internal static readonly int Cookie_BlockID = Shader.PropertyToID("Cookie_Block");
        internal static readonly int Cookie_ChannelsID = Shader.PropertyToID("Cookie_Channels");
        internal static readonly int SRV_CookieSourceID = Shader.PropertyToID("SRV_CookieSource");
        internal static readonly int SRV_CookieCubeID = Shader.PropertyToID("SRV_CookieCube");
        internal static readonly int UAV_CookieAtlasID = Shader.PropertyToID("UAV_CookieAtlas");
        internal static readonly int UAV_CookieAtlasSourceID = Shader.PropertyToID("UAV_CookieAtlasSource");
    }

    public partial class InfinityRenderPipeline
    {
        struct LightCookiePassData
        {
            public ComputeKernel copy2D, cubeToOctahedral, downsample;
            public CookieUpload[] uploads;
            public int uploadCount;
            public RGTextureHandle atlas;
        }

        sealed class CookieProduction : IRGPassQueueObserver
        {
            public LightCookieAtlas atlas;
            public void OnQueueFailed() { }
            public void OnQueued() => atlas.MarkRecordedProduced();
        }

        // Imports the cookie atlas when this camera's records reference it and uploads the textures that are new or
        // changed. Uploads queued by an earlier camera of the frame are not repeated.
        void ComputeLightCookies(RenderContext renderContext)
        {
            if (!ShouldRecordFeature(EFrameFeature.LightCookies))
            {
                return;
            }
            ActiveFeatures.ThrowIfCannotProduce(EFrameFeature.LightCookies);

            RGTextureHandle atlas = m_RGBuilder.ImportTexture(m_LightCookieAtlas.GetAtlas());
            m_RGScoper.RegisterTexture(InfinityShaderIDs.LightCookieAtlas, atlas);

            if (m_LightCookieAtlas.TakeUploads(out CookieUpload[] uploads, out int uploadCount))
            {
                using (RGComputePassHandle passRef = m_RGBuilder.AddComputePass<LightCookiePassData>(ProfilingSampler.Get(CustomSamplerId.ComputeLightCookies)))
                {
                    ref LightCookiePassData passData = ref passRef.GetPassData<LightCookiePassData>();
                    passData.copy2D = kernels.cookieCopy2D;
                    passData.cubeToOctahedral = kernels.cookieCubeToOctahedral;
                    passData.downsample = kernels.cookieDownsample;
                    passData.uploads = uploads;
                    passData.uploadCount = uploadCount;
                    passData.atlas = passRef.WriteTexture(atlas);
                    passRef.EnablePassCulling(false);
                    passRef.SetQueueObserver(m_CookieProduction);
                    passRef.SetExecuteFunc((in LightCookiePassData passData, in RGComputeEncoder cmdEncoder) =>
                    {
                        ComputeShader shader = passData.copy2D.shader;
                        ComputeKernel downsample = passData.downsample;
                        int count = passData.uploadCount;
                        CookieUpload[] recorded = passData.uploads;
                        for (int i = 0; i < count; ++i)
                        {
                            CookieUpload upload = recorded[i];
                            if (upload.source == null)
                            {
                                continue;
                            }
                            bool cube = upload.layout == ECookieLayout.Octahedral;
                            ComputeKernel kernel = cube ? passData.cubeToOctahedral : passData.copy2D;
                            int sourceSize = cube ? 2 * upload.source.width : Mathf.Max(upload.source.width, upload.source.height);
                            float lodBias = Mathf.Log(Mathf.Max(1, sourceSize) / (float)upload.size, 2);
                            cmdEncoder.SetComputeVectorParam(shader, LightCookiePassUtilityData.Cookie_BlockID, new Vector4(upload.position.x, upload.position.y, upload.size, lodBias));
                            cmdEncoder.SetComputeIntParam(shader, LightCookiePassUtilityData.Cookie_ChannelsID, (int)upload.channels);
                            cmdEncoder.SetComputeTextureParam(shader, kernel.index, cube ? LightCookiePassUtilityData.SRV_CookieCubeID : LightCookiePassUtilityData.SRV_CookieSourceID, upload.source, TextureView.Whole(upload.source));
                            cmdEncoder.SetComputeTextureParam(shader, kernel.index, LightCookiePassUtilityData.UAV_CookieAtlasID, passData.atlas, 0);
                            cmdEncoder.DispatchCompute(shader, kernel.index, kernel.GroupsX(upload.size), kernel.GroupsY(upload.size), 1);

                            // The block's mips: mip m covers (position >> m, size >> m) down to one texel.
                            for (int mip = 1; (upload.size >> mip) >= 1; ++mip)
                            {
                                int size = upload.size >> mip;
                                cmdEncoder.SetComputeVectorParam(shader, LightCookiePassUtilityData.Cookie_BlockID, new Vector4(upload.position.x >> mip, upload.position.y >> mip, size, 0));
                                cmdEncoder.SetComputeTextureParam(shader, downsample.index, LightCookiePassUtilityData.UAV_CookieAtlasSourceID, passData.atlas, mip - 1);
                                cmdEncoder.SetComputeTextureParam(shader, downsample.index, LightCookiePassUtilityData.UAV_CookieAtlasID, passData.atlas, mip);
                                cmdEncoder.DispatchCompute(shader, downsample.index, downsample.GroupsX(size), downsample.GroupsY(size), 1);
                            }
                        }
                    });
                }
            }

            MarkFeatureProduced(EFrameFeature.LightCookies);
        }
    }
}
