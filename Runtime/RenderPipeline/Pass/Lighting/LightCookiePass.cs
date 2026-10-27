using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class LightCookiePassUtilityData
    {
        internal const string KernelCopy2D = "CookieCopy2D";
        internal const string KernelCubeToOctahedral = "CookieCubeToOctahedral";
        internal const string KernelDownsample = "CookieDownsample";
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
            public ComputeShader shader;
            public FCookieUpload[] uploads;
            public RGTextureHandle atlas;
        }

        sealed class CookieProduction : IRGPassQueueObserver
        {
            public LightCookieAtlas atlas;
            public FCookieKey[] keys;
            public void OnQueueFailed() { }
            public void OnQueued() => atlas.MarkProduced(keys, keys.Length);
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

            List<FCookieUpload> pending = m_LightCookieAtlas.Uploads;
            if (pending.Count > 0)
            {
                FCookieUpload[] uploads = pending.ToArray();
                pending.Clear();
                var keys = new FCookieKey[uploads.Length];
                for (int i = 0; i < uploads.Length; ++i) keys[i] = uploads[i].key;

                using (RGComputePassHandle passRef = m_RGBuilder.AddComputePass<LightCookiePassData>(ProfilingSampler.Get(CustomSamplerId.ComputeLightCookies)))
                {
                    ref LightCookiePassData passData = ref passRef.GetPassData<LightCookiePassData>();
                    passData.shader = shaders.lightCookieShader;
                    passData.uploads = uploads;
                    passData.atlas = passRef.WriteTexture(atlas);
                    passRef.EnablePassCulling(false);
                    passRef.SetQueueObserver(new CookieProduction { atlas = m_LightCookieAtlas, keys = keys });
                    passRef.SetExecuteFunc((in LightCookiePassData passData, in RGComputeEncoder cmdEncoder) =>
                    {
                        ComputeShader shader = passData.shader;
                        int copy2D = shader.FindKernel(LightCookiePassUtilityData.KernelCopy2D);
                        int cubeToOcta = shader.FindKernel(LightCookiePassUtilityData.KernelCubeToOctahedral);
                        int downsample = shader.FindKernel(LightCookiePassUtilityData.KernelDownsample);
                        foreach (FCookieUpload upload in passData.uploads)
                        {
                            if (upload.source == null) continue;
                            bool cube = upload.layout == ECookieLayout.Octahedral;
                            int kernel = cube ? cubeToOcta : copy2D;
                            int sourceSize = cube ? 2 * upload.source.width : Mathf.Max(upload.source.width, upload.source.height);
                            float lodBias = Mathf.Log(Mathf.Max(1, sourceSize) / (float)upload.size, 2);
                            cmdEncoder.SetComputeVectorParam(shader, LightCookiePassUtilityData.Cookie_BlockID, new Vector4(upload.position.x, upload.position.y, upload.size, lodBias));
                            cmdEncoder.SetComputeIntParam(shader, LightCookiePassUtilityData.Cookie_ChannelsID, (int)upload.channels);
                            cmdEncoder.SetComputeTextureParam(shader, kernel, cube ? LightCookiePassUtilityData.SRV_CookieCubeID : LightCookiePassUtilityData.SRV_CookieSourceID, upload.source);
                            cmdEncoder.SetComputeTextureParam(shader, kernel, LightCookiePassUtilityData.UAV_CookieAtlasID, passData.atlas, 0);
                            cmdEncoder.DispatchCompute(shader, kernel, (upload.size + 7) / 8, (upload.size + 7) / 8, 1);

                            // The block's mips: mip m covers (position >> m, size >> m) down to one texel.
                            for (int mip = 1; (upload.size >> mip) >= 1; ++mip)
                            {
                                int size = upload.size >> mip;
                                cmdEncoder.SetComputeVectorParam(shader, LightCookiePassUtilityData.Cookie_BlockID, new Vector4(upload.position.x >> mip, upload.position.y >> mip, size, 0));
                                cmdEncoder.SetComputeTextureParam(shader, downsample, LightCookiePassUtilityData.UAV_CookieAtlasSourceID, passData.atlas, mip - 1);
                                cmdEncoder.SetComputeTextureParam(shader, downsample, LightCookiePassUtilityData.UAV_CookieAtlasID, passData.atlas, mip);
                                cmdEncoder.DispatchCompute(shader, downsample, (size + 7) / 8, (size + 7) / 8, 1);
                            }
                        }
                    });
                }
            }

            MarkFeatureProduced(EFrameFeature.LightCookies);
        }
    }
}
