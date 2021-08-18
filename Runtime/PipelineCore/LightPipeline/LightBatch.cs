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

    public struct FLightBatch /*: IComparable<FLightBatch>, IEquatable<FLightBatch>*/
    {
        public ELightState LightState;
        public ELightType LightType;
        public ELightLayer LightLayer;

        ///Emission Property
        public Color LightColor;
        public float LightIntensity;
        public float Temperature;
        public float LightRange;
        public float LightDiffuse;
        public float LightSpecular;
        public float SourceRadius;
        public float SourceLength;
        public float SourceInnerAngle;
        public float SourceOuterAngle;
        public float SourceWidth;
        public float SourceHeight;

        ///Globalillumination Property
        public int EnableGlobalIllumination;
        public float GlobalIlluminationIntensity;

        ///IES and Cookie Property
        public int IESTextureIndex;
        public int CookieTextureIndex;

        ///Shadow Property
        public int EnableShadow;
        public EShadowType ShadowType;
        public ELightLayer ShadowLayer;
        public EShadowResolution Resolution;
        public float NearPlane;
        public float MinSoftness;
        public float MaxSoftness;

        ///Contact Shadow Property
        public int EnableContactShadow;
        public float ContactShadowLength;

        ///VolumetricFog Property
        public int EnableVolumetric;
        public float VolumetricScatterIntensity;
        public float VolumetricScatterOcclusion;

        ///Performance Property
        public float MaxDrawDistance;
        public float MaxDrawDistanceFade;


        /*public bool Equals(FLightBatch Target)
        {
            return SubmeshIndex.Equals(Target.SubmeshIndex) && Mesh.Equals(Target.Mesh) && Material.Equals(Target.Material);
        }

        public override bool Equals(object obj)
        {
            return Equals((FLightBatch)obj);
        }

        public int CompareTo(FLightBatch MeshBatch)
        {
            return Priority.CompareTo(MeshBatch.Priority);
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
