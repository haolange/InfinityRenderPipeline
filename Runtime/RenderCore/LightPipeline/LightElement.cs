using System;
using Unity.Burst;
using UnityEngine;
using Unity.Mathematics;
using InfinityTech.Core;
using InfinityTech.Core.Geometry;
using System.Runtime.CompilerServices;

namespace InfinityTech.Rendering.LightPipeline
{
    public enum ELightType
    {
        Directional = 0,
        Point = 1,
        Spot = 2,
        Rect = 3
    }

    public enum ELightState
    {
        Static = 0,
        Mixed = 1,
        Dynamic = 2
    }

    public enum ELightUnit
    {
        Lumen,      // lm = total power/flux emitted by the light
        Candela,    // lm/sr = flux per steradian
        Lux,        // lm/m² = flux per unit area
        Luminance,  // lm/m²/sr = flux per unit area and per steradian
        Ev100,      // ISO 100 Exposure Value (https://en.wikipedia.org/wiki/Exposure_value)
    }

    public enum ELightLayer
    {
        Nothing = 0,   // Custom name for "Nothing" option
        LightLayerDefault = 1 << 0,
        LightLayer1 = 1 << 1,
        LightLayer2 = 1 << 2,
        LightLayer3 = 1 << 3,
        LightLayer4 = 1 << 4,
        LightLayer5 = 1 << 5,
        LightLayer6 = 1 << 6,
        LightLayer7 = 1 << 7,
        Everything = 0xFF, // Custom name for "Everything" option
    }

    public enum EShadowResolution
    {
        X512 = 0,
        X1024 = 1,
        X2048 = 2,
        X4096 = 3,
        X8192 = 4
    }

    public enum EShadowType
    {
        Hard = 0,
        PCF = 1,
        PCSS = 2
    }

    public enum EShadowCascade
    {
        One = 0,
        Two = 1,
        Three = 2,
        Four = 3
    }

    public struct LightElement /*: IComparable<LightElement>, IEquatable<LightElement>*/
    {
        public ELightState state;
        public ELightType lightType;
        public ELightLayer lightLayer;

        // Emission Property
        public Color color;
        public float intensity;
        public float temperature;
        public float range;
        public float diffuse;
        public float specular;
        public float radius;
        public float length;
        public float innerAngle;
        public float outerAngle;
        public float width;
        public float height;

        // Indirect Property
        public int enableIndirect;
        public float indirectIntensity;

        // IES and Cookie Property
        public int IESIndex;
        public int cookieIndex;

        // Shadow Property
        public int enableShadow;
        public EShadowType shadowType;
        public ELightLayer shadowLayer;
        public EShadowResolution resolution;
        public float nearPlane;
        public float minSoftness;
        public float maxSoftness;

        // Contact Shadow Property
        public int enableContactShadow;
        public float contactShadowLength;

        // VolumetricFog Property
        public int enableVolumetric;
        public float volumetricIntensity;
        public float volumetricOcclusion;

        // Performance Property
        public float maxDrawDistance;
        public float maxDrawDistanceFade;


        /*public bool Equals(LightElement target)
        {
            return SubmeshIndex.Equals(target.SubmeshIndex) && Mesh.Equals(target.Mesh) && Material.Equals(target.Material);
        }

        public override bool Equals(object target)
        {
            return Equals((LightElement)target);
        }

        public int CompareTo(LightElement target)
        {
            return Priority.CompareTo(target.Priority);
        }

        public override int GetHashCode()
        {
            int hashCode = MatchForDynamicInstance(ref this);
            hashCode += CastShadow.GetHashCode();
            hashCode += MotionType.GetHashCode();
            hashCode += Visible.GetHashCode();
            hashCode += Priority.GetHashCode();
            hashCode += RenderLayer.GetHashCode();
            hashCode += BoundBox.GetHashCode();
            hashCode += Matrix_LocalToWorld.GetHashCode();

            return hashCode;
        }*/
    }
}
