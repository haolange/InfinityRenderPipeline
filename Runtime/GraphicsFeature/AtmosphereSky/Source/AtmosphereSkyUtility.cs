using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Feature
{
    public static class AtmosphereSkyUtility
    {
        public static bool GenerateLut(this AtmosphereSkyAsset atmosphereSkyAsset, CommandBuffer CmdBuffer, RenderTexture T, RenderTexture MS, Color sunLum, Vector3 sunDir, bool forceRegenerate = false)
        {
            CmdBuffer.SetGlobalVector("_SunLuminance", sunLum);
            CmdBuffer.SetGlobalVector("_SunDir", sunDir);

            CmdBuffer.SetGlobalFloat("_PlanetRadius", atmosphereSkyAsset.Radius);
            CmdBuffer.SetGlobalFloat("_AtmosphereThickness", atmosphereSkyAsset.Thickness);
            CmdBuffer.SetGlobalVector("_GroundAlbedo", atmosphereSkyAsset.GroundAlbedo);
            CmdBuffer.SetGlobalVector("_RayleighScatter", atmosphereSkyAsset.RayleighScatter * atmosphereSkyAsset.RayleighStrength);
            CmdBuffer.SetGlobalFloat("_MeiScatter", atmosphereSkyAsset.MieStrength);
            CmdBuffer.SetGlobalFloat("_OZone", atmosphereSkyAsset.OzoneStrength);
            CmdBuffer.SetGlobalFloat("_SunAngle", atmosphereSkyAsset.SunSolidAngle);

            CmdBuffer.SetGlobalFloat("_MultiScatterStrength", atmosphereSkyAsset.MultiScatterStrength);

            CmdBuffer.SetGlobalVector("_TLutResolution", new Vector4(T.width, T.height));

            CmdBuffer.SetGlobalFloat("_Multiplier", atmosphereSkyAsset.Brightness);

            bool regenerated = false;

            if (forceRegenerate)
            {
                regenerated = true;
                CmdBuffer.Blit(null, T, atmosphereSkyAsset.LUTMaterial, 0);
                CmdBuffer.SetGlobalTexture("T_table", T);
                CmdBuffer.Blit(null, MS, atmosphereSkyAsset.LUTMaterial, 1);
            } else {
                CmdBuffer.SetGlobalTexture("T_table", T);
                CmdBuffer.SetGlobalTexture("MS_table", MS);
            }

            return regenerated;
        }

        public static void GenerateVolumeSkyTexture(this AtmosphereSkyAsset AtmoSky, CommandBuffer CmdBuffer, RenderTexture volume, RenderTexture sky, float maxDepth, int frameIndex = -1)
        {
            CmdBuffer.SetGlobalFloat("_RenderGround", AtmoSky.DrawGround ? 1 : 0);
            CmdBuffer.SetGlobalVector("_SLutResolution", new Vector4(sky.width, sky.height));
            CmdBuffer.SetGlobalFloat("_MaxDepth", maxDepth);
            CmdBuffer.SetGlobalFloat("_AtmoSlice", frameIndex % 4);
            CmdBuffer.Blit(null, sky, AtmoSky.LUTMaterial, 2);
            CmdBuffer.SetComputeTextureParam(AtmoSky.LUTCompute, 0, "_Result", volume);
            Vector3Int size = new Vector3Int(volume.width, volume.height, volume.volumeDepth);
            CmdBuffer.SetComputeVectorParam(AtmoSky.LUTCompute, "_Size", new Vector4(size.x, size.y, size.z));
            size.x = size.x / 4 + (size.x % 4 != 0 ? 1 : 0);
            size.y = size.y / 4 + (size.y % 4 != 0 ? 1 : 0);
            size.z = size.z / 4 + (size.z % 4 != 0 ? 1 : 0);
            CmdBuffer.DispatchCompute(AtmoSky.LUTCompute, 0, size.x, size.y, size.z);
            CmdBuffer.SetGlobalTexture("Volume_table", volume);
            CmdBuffer.SetGlobalTexture("S_table", sky);
        }

        public static void GenerateSunBuffer(this AtmosphereSkyAsset AtmoSky, CommandBuffer CmdBuffer, ComputeBuffer sunBuffer, Color sunColor)
        {
            CmdBuffer.SetGlobalVector("_SunColor", sunColor);
            CmdBuffer.SetComputeBufferParam(AtmoSky.LUTCompute, 1, "_Sun_", sunBuffer);
            CmdBuffer.DispatchCompute(AtmoSky.LUTCompute, 1, 1, 1, 1);
        }

        public static void RenderAtmoToRT(this AtmosphereSkyAsset AtmoSky, CommandBuffer CmdBuffer, RenderTargetIdentifier sceneColor, int depth, RenderTargetIdentifier target)
        {
            CmdBuffer.SetGlobalTexture("_DepthTex", depth);
            CmdBuffer.Blit(sceneColor, target, AtmoSky.LUTMaterial, 3);
        }

        public static void RenderAtmoToCubeMap(this AtmosphereSkyAsset AtmoSky, CommandBuffer CmdBuffer, int target)
        {
            CmdBuffer.SetRenderTarget(target, 0, CubemapFace.PositiveX);
            CmdBuffer.SetGlobalInt("_Slice", 0);
            CmdBuffer.Blit(null, BuiltinRenderTextureType.CurrentActive, AtmoSky.LUTMaterial, 4);

            CmdBuffer.SetRenderTarget(target, 0, CubemapFace.NegativeX);
            CmdBuffer.SetGlobalInt("_Slice", 1);
            CmdBuffer.Blit(null, BuiltinRenderTextureType.CurrentActive, AtmoSky.LUTMaterial, 4);

            CmdBuffer.SetRenderTarget(target, 0, CubemapFace.PositiveY);
            CmdBuffer.SetGlobalInt("_Slice", 2);
            CmdBuffer.Blit(null, BuiltinRenderTextureType.CurrentActive, AtmoSky.LUTMaterial, 4);

            CmdBuffer.SetRenderTarget(target, 0, CubemapFace.NegativeY);
            CmdBuffer.SetGlobalInt("_Slice", 3);
            CmdBuffer.Blit(null, BuiltinRenderTextureType.CurrentActive, AtmoSky.LUTMaterial, 4);

            CmdBuffer.SetRenderTarget(target, 0, CubemapFace.PositiveZ);
            CmdBuffer.SetGlobalInt("_Slice", 4);
            CmdBuffer.Blit(null, BuiltinRenderTextureType.CurrentActive, AtmoSky.LUTMaterial, 4);

            CmdBuffer.SetRenderTarget(target, 0, CubemapFace.NegativeZ);
            CmdBuffer.SetGlobalInt("_Slice", 5);
            CmdBuffer.Blit(null, BuiltinRenderTextureType.CurrentActive, AtmoSky.LUTMaterial, 4);
        }
    }
}
