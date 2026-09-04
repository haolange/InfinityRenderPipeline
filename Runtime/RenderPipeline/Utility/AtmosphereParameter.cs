using System;
using UnityEngine;
using InfinityTech.Rendering.Feature;

namespace InfinityTech.Rendering.Pipeline
{
    public struct AtmosphereParameter : IEquatable<AtmosphereParameter>
    {
        public float planetRadius;
        public float atmosphereHeight;
        public Color rayleighScattering;
        public float rayleighHeight;
        public float mieScattering;
        public float mieAbsorption;
        public float mieHeight;
        public float mieAnisotropy;
        public Color ozoneAbsorption;
        public float ozoneLayerCenter;
        public float ozoneLayerWidth;
        public Color groundAlbedo;
        public float brightness;
        public float multiScatterStrength;
        public float sunAngle;
        public bool drawGround;
        public int transmittanceLUTWidth;
        public int transmittanceLUTHeight;
        public int multiScatteringLUTSize;
        public int skyViewLUTWidth;
        public int skyViewLUTHeight;
        public int aerialPerspectiveSize;
        public float aerialPerspectiveDistance;
        public int cubemapSize;

        // Hillaire coefficients are stored per kilometer. Integration and Unity world units are meters.
        public const float ScatterPerKmToPerMeter = 0.001f;

        public const float EarthPlanetRadius = 6360000.0f;
        public const float EarthAtmosphereHeight = 60000.0f;
        public static readonly Color EarthRayleighScattering = new Color(0.00580f, 0.01356f, 0.03310f, 1.0f);
        public const float EarthRayleighHeight = 8000.0f;
        public const float EarthMieScattering = 0.003996f;
        public const float EarthMieAbsorption = 0.000444f;
        public const float EarthMieHeight = 1200.0f;
        public const float EarthMieAnisotropy = 0.8f;
        public static readonly Color EarthOzoneAbsorption = new Color(0.000650f, 0.001881f, 0.000085f, 1.0f);
        public const float EarthOzoneLayerCenter = 25000.0f;
        public const float EarthOzoneLayerWidth = 15000.0f;
        public static readonly Color EarthGroundAlbedo = new Color(0.3f, 0.3f, 0.3f, 1.0f);
        public const float EarthBrightness = 1.0f;
        public const float EarthMultiScatterStrength = 1.0f;
        public static readonly float EarthSunAngle = 0.5f / 180.0f * Mathf.PI;

        public const float AtmosphereHeightMin = 10000.0f;
        public const float AtmosphereHeightMax = 120000.0f;
        public const float ScatterScaleMin = 0.1f;
        public const float ScatterScaleMax = 10.0f;
        public const float DensityHeightMin = 100.0f;
        public const float DensityHeightMax = 20000.0f;
        public static readonly float SunAngleMin = 0.1f / 180.0f * Mathf.PI;
        public static readonly float SunAngleMax = 5.0f / 180.0f * Mathf.PI;

        const string InvalidRangeHint = "Open AtmosphericalProfile Inspector or run Infinity/Validation/Upgrade Atmospherical Profile";

        public Vector4 RayleighScatteringPerMeter => (Vector4)rayleighScattering * ScatterPerKmToPerMeter;
        public float MieScatteringPerMeter => mieScattering * ScatterPerKmToPerMeter;
        public float MieAbsorptionPerMeter => mieAbsorption * ScatterPerKmToPerMeter;
        public Vector4 OzoneAbsorptionPerMeter => (Vector4)ozoneAbsorption * ScatterPerKmToPerMeter;

        public static AtmosphereParameter FromProfile(AtmosphericalProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile), "InfinityRP: AtmosphericalProfile is required. Atmosphere lives only on the profile.");
            }

            return new AtmosphereParameter
            {
                planetRadius = profile.radius,
                atmosphereHeight = profile.thickness,
                rayleighScattering = profile.rayleighScatter * profile.rayleighStrength,
                rayleighHeight = profile.rayleighHeight,
                mieScattering = profile.mieStrength,
                mieAbsorption = profile.mieAbsorption,
                mieHeight = profile.mieHeight,
                mieAnisotropy = profile.mieAnisotropy,
                ozoneAbsorption = profile.ozoneAbsorption * profile.ozoneStrength,
                ozoneLayerCenter = profile.ozoneLayerCenter,
                ozoneLayerWidth = profile.ozoneLayerWidth,
                groundAlbedo = profile.groundAlbedo,
                brightness = profile.brightness,
                multiScatterStrength = profile.multiScatterStrength,
                sunAngle = profile.sunAngle,
                drawGround = profile.drawGround,
                transmittanceLUTWidth = profile.transmittanceLUTWidth,
                transmittanceLUTHeight = profile.transmittanceLUTHeight,
                multiScatteringLUTSize = profile.multiScatteringLUTSize,
                skyViewLUTWidth = profile.skyViewLUTWidth,
                skyViewLUTHeight = profile.skyViewLUTHeight,
                aerialPerspectiveSize = profile.aerialPerspectiveSize,
                aerialPerspectiveDistance = profile.aerialPerspectiveDistance,
                cubemapSize = profile.cubemapSize
            };
        }

        public void ThrowIfInvalid()
        {
            if (planetRadius <= 0.0f || atmosphereHeight <= 0.0f
                || transmittanceLUTWidth <= 0 || transmittanceLUTHeight <= 0
                || multiScatteringLUTSize <= 0
                || skyViewLUTWidth <= 0 || skyViewLUTHeight <= 0
                || aerialPerspectiveSize <= 0 || aerialPerspectiveDistance <= 0.0f
                || cubemapSize <= 0)
            {
                throw new InvalidOperationException("InfinityRP: AtmosphericalProfile deserialized invalid or zero fields. Open the profile or run Infinity/Validation/Upgrade Atmospherical Profile.");
            }

            ThrowIfOutOfRange(IsAtmosphereHeightInRange(atmosphereHeight), "atmosphereHeight", "[10000, 120000] m");
            ThrowIfOutOfRange(IsRayleighScatterInRange(rayleighScattering), "rayleighScattering", "[0.1x, 10x] of Earth Hillaire");
            ThrowIfOutOfRange(IsMieScatteringInRange(mieScattering), "mieScattering", "[0.1x, 10x] of 0.003996");
            ThrowIfOutOfRange(IsMieAbsorptionInRange(mieAbsorption), "mieAbsorption", "[0.1x, 10x] of 0.000444");
            ThrowIfOutOfRange(IsDensityHeightInRange(rayleighHeight), "rayleighHeight", "[100, 20000] m");
            ThrowIfOutOfRange(IsDensityHeightInRange(mieHeight), "mieHeight", "[100, 20000] m");
            ThrowIfOutOfRange(IsOzoneAbsorptionInRange(ozoneAbsorption), "ozoneAbsorption", "rgb > 0");
            ThrowIfOutOfRange(ozoneLayerCenter > 0.0f, "ozoneLayerCenter", "> 0");
            ThrowIfOutOfRange(ozoneLayerWidth > 0.0f, "ozoneLayerWidth", "> 0");
            ThrowIfOutOfRange(IsSunAngleInRange(sunAngle), "sunAngle", "[0.1, 5] degrees");
        }

        public static bool IsAtmosphereHeightInRange(float height)
        {
            return height >= AtmosphereHeightMin && height <= AtmosphereHeightMax;
        }

        public static bool IsRayleighScatterInRange(Color scatter)
        {
            return ChannelInEarthScale(scatter.r, EarthRayleighScattering.r)
                && ChannelInEarthScale(scatter.g, EarthRayleighScattering.g)
                && ChannelInEarthScale(scatter.b, EarthRayleighScattering.b);
        }

        public static bool IsMieScatteringInRange(float mieScattering)
        {
            return ChannelInEarthScale(mieScattering, EarthMieScattering);
        }

        public static bool IsMieAbsorptionInRange(float mieAbsorption)
        {
            return ChannelInEarthScale(mieAbsorption, EarthMieAbsorption);
        }

        public static bool IsDensityHeightInRange(float height)
        {
            return height >= DensityHeightMin && height <= DensityHeightMax;
        }

        public static bool IsOzoneAbsorptionInRange(Color absorption)
        {
            return absorption.r > 0.0f && absorption.g > 0.0f && absorption.b > 0.0f;
        }

        public static bool IsSunAngleInRange(float sunAngleRadians)
        {
            return sunAngleRadians >= SunAngleMin && sunAngleRadians <= SunAngleMax;
        }

        static bool ChannelInEarthScale(float value, float earth)
        {
            return value >= earth * ScatterScaleMin && value <= earth * ScatterScaleMax;
        }

        static void ThrowIfOutOfRange(bool valid, string fieldName, string rangeText)
        {
            if (!valid)
            {
                throw new InvalidOperationException($"InfinityRP: {fieldName} is outside {rangeText}. {InvalidRangeHint}");
            }
        }

        public bool Equals(AtmosphereParameter other)
        {
            return planetRadius == other.planetRadius
                && atmosphereHeight == other.atmosphereHeight
                && ColorEquals(rayleighScattering, other.rayleighScattering)
                && rayleighHeight == other.rayleighHeight
                && mieScattering == other.mieScattering
                && mieAbsorption == other.mieAbsorption
                && mieHeight == other.mieHeight
                && mieAnisotropy == other.mieAnisotropy
                && ColorEquals(ozoneAbsorption, other.ozoneAbsorption)
                && ozoneLayerCenter == other.ozoneLayerCenter
                && ozoneLayerWidth == other.ozoneLayerWidth
                && ColorEquals(groundAlbedo, other.groundAlbedo)
                && brightness == other.brightness
                && multiScatterStrength == other.multiScatterStrength
                && sunAngle == other.sunAngle
                && drawGround == other.drawGround
                && transmittanceLUTWidth == other.transmittanceLUTWidth
                && transmittanceLUTHeight == other.transmittanceLUTHeight
                && multiScatteringLUTSize == other.multiScatteringLUTSize
                && skyViewLUTWidth == other.skyViewLUTWidth
                && skyViewLUTHeight == other.skyViewLUTHeight
                && aerialPerspectiveSize == other.aerialPerspectiveSize
                && aerialPerspectiveDistance == other.aerialPerspectiveDistance
                && cubemapSize == other.cubemapSize;
        }

        public override bool Equals(object obj)
        {
            return obj is AtmosphereParameter other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + planetRadius.GetHashCode();
                hash = hash * 23 + atmosphereHeight.GetHashCode();
                hash = hash * 23 + ColorHash(rayleighScattering);
                hash = hash * 23 + rayleighHeight.GetHashCode();
                hash = hash * 23 + mieScattering.GetHashCode();
                hash = hash * 23 + mieAbsorption.GetHashCode();
                hash = hash * 23 + mieHeight.GetHashCode();
                hash = hash * 23 + mieAnisotropy.GetHashCode();
                hash = hash * 23 + ColorHash(ozoneAbsorption);
                hash = hash * 23 + ozoneLayerCenter.GetHashCode();
                hash = hash * 23 + ozoneLayerWidth.GetHashCode();
                hash = hash * 23 + ColorHash(groundAlbedo);
                hash = hash * 23 + brightness.GetHashCode();
                hash = hash * 23 + multiScatterStrength.GetHashCode();
                hash = hash * 23 + sunAngle.GetHashCode();
                hash = hash * 23 + (drawGround ? 1 : 0);
                hash = hash * 23 + transmittanceLUTWidth;
                hash = hash * 23 + transmittanceLUTHeight;
                hash = hash * 23 + multiScatteringLUTSize;
                hash = hash * 23 + skyViewLUTWidth;
                hash = hash * 23 + skyViewLUTHeight;
                hash = hash * 23 + aerialPerspectiveSize;
                hash = hash * 23 + aerialPerspectiveDistance.GetHashCode();
                hash = hash * 23 + cubemapSize;
                return hash;
            }
        }

        static bool ColorEquals(Color a, Color b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        }

        static int ColorHash(Color color)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + color.r.GetHashCode();
                hash = hash * 23 + color.g.GetHashCode();
                hash = hash * 23 + color.b.GetHashCode();
                hash = hash * 23 + color.a.GetHashCode();
                return hash;
            }
        }
    }

    internal struct AtmosphereViewKey : IEquatable<AtmosphereViewKey>
    {
        public AtmosphereParameter parameter;
        public Vector3 sunDirection;
        public int cameraX;
        public int cameraY;
        public int cameraZ;

        public static AtmosphereViewKey Create(in AtmosphereParameter parameter, Vector3 sunDirection, Vector3 cameraPosition)
        {
            return new AtmosphereViewKey
            {
                parameter = parameter,
                sunDirection = sunDirection,
                cameraX = Mathf.FloorToInt(cameraPosition.x),
                cameraY = Mathf.FloorToInt(cameraPosition.y),
                cameraZ = Mathf.FloorToInt(cameraPosition.z)
            };
        }

        public bool Equals(AtmosphereViewKey other)
        {
            return parameter.Equals(other.parameter)
                && sunDirection.x == other.sunDirection.x
                && sunDirection.y == other.sunDirection.y
                && sunDirection.z == other.sunDirection.z
                && cameraX == other.cameraX
                && cameraY == other.cameraY
                && cameraZ == other.cameraZ;
        }

        public override bool Equals(object obj)
        {
            return obj is AtmosphereViewKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + parameter.GetHashCode();
                hash = hash * 23 + sunDirection.x.GetHashCode();
                hash = hash * 23 + sunDirection.y.GetHashCode();
                hash = hash * 23 + sunDirection.z.GetHashCode();
                hash = hash * 23 + cameraX;
                hash = hash * 23 + cameraY;
                hash = hash * 23 + cameraZ;
                return hash;
            }
        }
    }

    internal struct AtmosphereIBLKey : IEquatable<AtmosphereIBLKey>
    {
        public AtmosphereParameter parameter;
        public Vector3 sunDirection;

        public static AtmosphereIBLKey Create(in AtmosphereParameter parameter, Vector3 sunDirection)
        {
            return new AtmosphereIBLKey
            {
                parameter = parameter,
                sunDirection = sunDirection
            };
        }

        public bool Equals(AtmosphereIBLKey other)
        {
            return parameter.Equals(other.parameter)
                && sunDirection.x == other.sunDirection.x
                && sunDirection.y == other.sunDirection.y
                && sunDirection.z == other.sunDirection.z;
        }

        public override bool Equals(object obj)
        {
            return obj is AtmosphereIBLKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + parameter.GetHashCode();
                hash = hash * 23 + sunDirection.x.GetHashCode();
                hash = hash * 23 + sunDirection.y.GetHashCode();
                hash = hash * 23 + sunDirection.z.GetHashCode();
                return hash;
            }
        }
    }
}
