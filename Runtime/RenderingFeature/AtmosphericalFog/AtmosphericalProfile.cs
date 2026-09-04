using UnityEngine;

namespace InfinityTech.Rendering.Feature
{
    [ExecuteInEditMode]
    [CreateAssetMenu(menuName = "InfinityRenderPipeline/AtmosphericalProfile", order = 359)]
    public sealed class AtmosphericalProfile : ScriptableObject
    {
        [Header("Planet")]
        [Min(10000)]
        public float radius = 6360000.0f;

        [Min(100)]
        public float thickness = 60000.0f;

        [Header("Scatter (Hillaire km^-1; bind converts to m^-1)")]
        [Range(0.01f, 100f)]
        public float brightness = 1.0f;

        public bool drawGround = false;

        public Color groundAlbedo = new Color(0.3f, 0.3f, 0.3f);

        public Color rayleighScatter = new Color(0.00580f, 0.01356f, 0.03310f);

        [Min(0)]
        public float rayleighStrength = 1.0f;

        [Min(1)]
        public float rayleighHeight = 8000.0f;

        [Min(0)]
        public float mieStrength = 0.003996f;

        [Min(0)]
        public float mieAbsorption = 0.000444f;

        [Min(1)]
        public float mieHeight = 1200.0f;

        [Range(-1f, 1f)]
        public float mieAnisotropy = 0.8f;

        public Color ozoneAbsorption = new Color(0.000650f, 0.001881f, 0.000085f);

        [Min(0)]
        public float ozoneStrength = 1.0f;

        [Min(1)]
        public float ozoneLayerCenter = 25000.0f;

        [Min(1)]
        public float ozoneLayerWidth = 15000.0f;

        [Min(0)]
        public float multiScatterStrength = 1.0f;

        [Range(0.0001f, 0.03f)]
        public float sunAngle = (0.5f / 180.0f * Mathf.PI);

        [Header("Quality")]
        [Range(64, 512)]
        public int transmittanceLUTWidth = 256;

        [Range(16, 128)]
        public int transmittanceLUTHeight = 64;

        [Range(16, 64)]
        public int multiScatteringLUTSize = 32;

        [Range(64, 512)]
        public int skyViewLUTWidth = 192;

        [Range(32, 256)]
        public int skyViewLUTHeight = 108;

        [Range(8, 64)]
        public int aerialPerspectiveSize = 32;

        [Min(100)]
        public float aerialPerspectiveDistance = 32000.0f;

        [Range(16, 256)]
        public int cubemapSize = 128;

        public bool UpgradeZerosToDefaults()
        {
            bool changed = false;
            changed |= Upgrade(ref radius, 6360000.0f);
            changed |= Upgrade(ref thickness, 60000.0f);
            changed |= Upgrade(ref brightness, 1.0f);
            changed |= UpgradeColor(ref groundAlbedo, new Color(0.3f, 0.3f, 0.3f));
            changed |= UpgradeColor(ref rayleighScatter, new Color(0.00580f, 0.01356f, 0.03310f));
            changed |= Upgrade(ref rayleighStrength, 1.0f);
            changed |= Upgrade(ref rayleighHeight, 8000.0f);
            changed |= Upgrade(ref mieStrength, 0.003996f);
            changed |= Upgrade(ref mieAbsorption, 0.000444f);
            changed |= Upgrade(ref mieHeight, 1200.0f);
            changed |= Upgrade(ref mieAnisotropy, 0.8f, allowZero: true);
            changed |= UpgradeColor(ref ozoneAbsorption, new Color(0.000650f, 0.001881f, 0.000085f));
            changed |= Upgrade(ref ozoneStrength, 1.0f);
            changed |= Upgrade(ref ozoneLayerCenter, 25000.0f);
            changed |= Upgrade(ref ozoneLayerWidth, 15000.0f);
            changed |= Upgrade(ref multiScatterStrength, 1.0f);
            changed |= Upgrade(ref sunAngle, 0.5f / 180.0f * Mathf.PI);
            changed |= Upgrade(ref transmittanceLUTWidth, 256);
            changed |= Upgrade(ref transmittanceLUTHeight, 64);
            changed |= Upgrade(ref multiScatteringLUTSize, 32);
            changed |= Upgrade(ref skyViewLUTWidth, 192);
            changed |= Upgrade(ref skyViewLUTHeight, 108);
            changed |= Upgrade(ref aerialPerspectiveSize, 32);
            changed |= Upgrade(ref aerialPerspectiveDistance, 32000.0f);
            changed |= Upgrade(ref cubemapSize, 128);
            return changed;
        }

        static bool Upgrade(ref float field, float fallback, bool allowZero = false)
        {
            if (allowZero)
            {
                return false;
            }

            if (field <= 0.0f)
            {
                field = fallback;
                return true;
            }

            return false;
        }

        static bool Upgrade(ref int field, int fallback)
        {
            if (field <= 0)
            {
                field = fallback;
                return true;
            }

            return false;
        }

        static bool UpgradeColor(ref Color field, Color fallback)
        {
            if (field.r == 0.0f && field.g == 0.0f && field.b == 0.0f)
            {
                field = fallback;
                return true;
            }

            return false;
        }
    }
}
