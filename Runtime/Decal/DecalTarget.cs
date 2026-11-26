using System;
using UnityEngine;
using InfinityTech.Rendering.MaterialPipeline;

namespace InfinityTech.Rendering.Decal
{
    public enum EDecalTarget
    {
        DBuffer = 0,
        GBuffer = 1,
    }

    // The material property _DecalTarget is the authority for which decal pass draws. Clustered decals are not a pass.
    public static class DecalTargetUtility
    {
        public const string DBufferPass = "RasterDBufferPass";
        public const string GBufferPass = "RasterDirectDecalPass";
        public const string NormalPass = "RasterDirectDecalNormalPass";

        public static void ApplyPassState(Material material)
        {
            if (material == null)
            {
                throw new ArgumentNullException(nameof(material));
            }
            if (!material.HasProperty("_DecalTarget"))
            {
                throw new InvalidOperationException($"Material '{material.name}' must declare _DecalTarget.");
            }
            float raw = material.GetFloat("_DecalTarget");
            if (raw != 0.0f && raw != 1.0f)
            {
                throw new InvalidOperationException($"Material '{material.name}' _DecalTarget must be 0 (DBuffer) or 1 (GBuffer).");
            }
            bool gBuffer = raw == 1.0f;
            bool replaceNormal = gBuffer && material.HasProperty("_DecalReplaceNormal") && material.GetFloat("_DecalReplaceNormal") != 0.0f;
            MaterialRouteUtility.SetNamedPass(material, DBufferPass, !gBuffer);
            MaterialRouteUtility.SetNamedPass(material, GBufferPass, gBuffer);
            MaterialRouteUtility.SetNamedPass(material, NormalPass, replaceNormal);
        }

        public static EDecalTarget Read(Material material)
        {
            if (material == null)
            {
                throw new ArgumentNullException(nameof(material));
            }
            if (!material.HasProperty("_DecalTarget"))
            {
                throw new InvalidOperationException($"Material '{material.name}' must declare _DecalTarget.");
            }
            float raw = material.GetFloat("_DecalTarget");
            if (raw == 0.0f)
            {
                return EDecalTarget.DBuffer;
            }
            if (raw == 1.0f)
            {
                return EDecalTarget.GBuffer;
            }
            throw new InvalidOperationException($"Material '{material.name}' _DecalTarget must be 0 (DBuffer) or 1 (GBuffer).");
        }

    }
}
