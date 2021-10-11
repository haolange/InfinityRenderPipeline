using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Feature
{
    public static class AtmosphereUtility
    {
        public static bool GenerateLut(this AtmosphericalProfile atmosphereProfile, CommandBuffer cmdBuffer, RenderTexture T, RenderTexture MS, Color sunLum, in Vector3 sunDir, in bool forceRegenerate = false)
        {
            cmdBuffer.SetGlobalVector("_SunLuminance", sunLum);
            cmdBuffer.SetGlobalVector("_SunDir", sunDir);

            cmdBuffer.SetGlobalFloat("_PlanetRadius", atmosphereProfile.Radius);
            cmdBuffer.SetGlobalFloat("_AtmosphereThickness", atmosphereProfile.Thickness);
            cmdBuffer.SetGlobalVector("_GroundAlbedo", atmosphereProfile.GroundAlbedo);
            cmdBuffer.SetGlobalVector("_RayleighScatter", atmosphereProfile.RayleighScatter * atmosphereProfile.RayleighStrength);
            cmdBuffer.SetGlobalFloat("_MeiScatter", atmosphereProfile.MieStrength);
            cmdBuffer.SetGlobalFloat("_OZone", atmosphereProfile.OzoneStrength);
            cmdBuffer.SetGlobalFloat("_SunAngle", atmosphereProfile.SunSolidAngle);

            cmdBuffer.SetGlobalFloat("_MultiScatterStrength", atmosphereProfile.MultiScatterStrength);

            cmdBuffer.SetGlobalVector("_TLutResolution", new Vector4(T.width, T.height));

            cmdBuffer.SetGlobalFloat("_Multiplier", atmosphereProfile.Brightness);

            bool regenerated = false;

            if (forceRegenerate)
            {
                regenerated = true;
                cmdBuffer.Blit(null, T, atmosphereProfile.LUTMaterial, 0);
                cmdBuffer.SetGlobalTexture("T_table", T);
                cmdBuffer.Blit(null, MS, atmosphereProfile.LUTMaterial, 1);
            } else {
                cmdBuffer.SetGlobalTexture("T_table", T);
                cmdBuffer.SetGlobalTexture("MS_table", MS);
            }

            return regenerated;
        }

        public static void GenerateVolumeSkyTexture(this AtmosphericalProfile atmosphereProfile, CommandBuffer cmdBuffer, RenderTexture volume, RenderTexture sky, in float maxDepth, in int frameIndex = -1)
        {
            cmdBuffer.SetGlobalFloat("_RenderGround", atmosphereProfile.DrawGround ? 1 : 0);
            cmdBuffer.SetGlobalVector("_SLutResolution", new Vector4(sky.width, sky.height));
            cmdBuffer.SetGlobalFloat("_MaxDepth", maxDepth);
            cmdBuffer.SetGlobalFloat("_AtmoSlice", frameIndex % 4);
            cmdBuffer.Blit(null, sky, atmosphereProfile.LUTMaterial, 2);
            cmdBuffer.SetComputeTextureParam(atmosphereProfile.LUTCompute, 0, "_Result", volume);
            Vector3Int size = new Vector3Int(volume.width, volume.height, volume.volumeDepth);
            cmdBuffer.SetComputeVectorParam(atmosphereProfile.LUTCompute, "_Size", new Vector4(size.x, size.y, size.z));
            size.x = size.x / 4 + (size.x % 4 != 0 ? 1 : 0);
            size.y = size.y / 4 + (size.y % 4 != 0 ? 1 : 0);
            size.z = size.z / 4 + (size.z % 4 != 0 ? 1 : 0);
            cmdBuffer.DispatchCompute(atmosphereProfile.LUTCompute, 0, size.x, size.y, size.z);
            cmdBuffer.SetGlobalTexture("Volume_table", volume);
            cmdBuffer.SetGlobalTexture("S_table", sky);
        }

        public static void GenerateSunBuffer(this AtmosphericalProfile atmosphereProfile, CommandBuffer cmdBuffer, ComputeBuffer sunBuffer, in Color sunColor)
        {
            cmdBuffer.SetGlobalVector("_SunColor", sunColor);
            cmdBuffer.SetComputeBufferParam(atmosphereProfile.LUTCompute, 1, "_Sun_", sunBuffer);
            cmdBuffer.DispatchCompute(atmosphereProfile.LUTCompute, 1, 1, 1, 1);
        }

        public static void RenderAtmoToRT(this AtmosphericalProfile atmosphereProfile, CommandBuffer cmdBuffer, RenderTargetIdentifier sceneColor, in int depth, RenderTargetIdentifier target)
        {
            cmdBuffer.SetGlobalTexture("_DepthTex", depth);
            cmdBuffer.Blit(sceneColor, target, atmosphereProfile.LUTMaterial, 3);
        }

        public static void RenderAtmoToCubeMap(this AtmosphericalProfile atmosphereProfile, CommandBuffer cmdBuffer, in int target)
        {
            cmdBuffer.SetRenderTarget(target, 0, CubemapFace.PositiveX);
            cmdBuffer.SetGlobalInt("_Slice", 0);
            cmdBuffer.Blit(null, BuiltinRenderTextureType.CurrentActive, atmosphereProfile.LUTMaterial, 4);

            cmdBuffer.SetRenderTarget(target, 0, CubemapFace.NegativeX);
            cmdBuffer.SetGlobalInt("_Slice", 1);
            cmdBuffer.Blit(null, BuiltinRenderTextureType.CurrentActive, atmosphereProfile.LUTMaterial, 4);

            cmdBuffer.SetRenderTarget(target, 0, CubemapFace.PositiveY);
            cmdBuffer.SetGlobalInt("_Slice", 2);
            cmdBuffer.Blit(null, BuiltinRenderTextureType.CurrentActive, atmosphereProfile.LUTMaterial, 4);

            cmdBuffer.SetRenderTarget(target, 0, CubemapFace.NegativeY);
            cmdBuffer.SetGlobalInt("_Slice", 3);
            cmdBuffer.Blit(null, BuiltinRenderTextureType.CurrentActive, atmosphereProfile.LUTMaterial, 4);

            cmdBuffer.SetRenderTarget(target, 0, CubemapFace.PositiveZ);
            cmdBuffer.SetGlobalInt("_Slice", 4);
            cmdBuffer.Blit(null, BuiltinRenderTextureType.CurrentActive, atmosphereProfile.LUTMaterial, 4);

            cmdBuffer.SetRenderTarget(target, 0, CubemapFace.NegativeZ);
            cmdBuffer.SetGlobalInt("_Slice", 5);
            cmdBuffer.Blit(null, BuiltinRenderTextureType.CurrentActive, atmosphereProfile.LUTMaterial, 4);
        }
    }
}
