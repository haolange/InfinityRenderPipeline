using System;
using NUnit.Framework;
using UnityEngine;
using InfinityTech.Rendering.Feature;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class AtmosphereParameterTests
    {
        [Test]
        public void FromProfile_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => AtmosphereParameter.FromProfile(null));
        }

        [Test]
        public void ThrowIfInvalid_ZeroFields_Throws()
        {
            AtmosphereParameter parameter = default;
            Assert.Throws<InvalidOperationException>(() => parameter.ThrowIfInvalid());
        }

        [Test]
        public void Equals_AndGetHashCode_AreFieldLevel()
        {
            AtmosphereParameter a = ValidParameter();
            AtmosphereParameter b = a;
            Assert.IsTrue(a.Equals(b));
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());

            b.brightness = a.brightness + 0.1f;
            Assert.IsFalse(a.Equals(b));
        }

        [Test]
        public void ViewKey_QuantizesCameraTo1m()
        {
            AtmosphereParameter parameter = ValidParameter();
            Vector3 sun = new Vector3(0.0f, 1.0f, 0.0f);
            AtmosphereViewKey a = AtmosphereViewKey.Create(parameter, sun, new Vector3(10.2f, 4.9f, -3.1f));
            AtmosphereViewKey b = AtmosphereViewKey.Create(parameter, sun, new Vector3(10.8f, 4.1f, -3.9f));
            AtmosphereViewKey c = AtmosphereViewKey.Create(parameter, sun, new Vector3(11.0f, 4.9f, -3.1f));

            Assert.IsTrue(a.Equals(b));
            Assert.IsFalse(a.Equals(c));
            Assert.AreEqual(10, a.cameraX);
            Assert.AreEqual(4, a.cameraY);
            Assert.AreEqual(-4, a.cameraZ);
        }

        [Test]
        public void IBLKey_UsesParameterAndSun()
        {
            AtmosphereParameter parameter = ValidParameter();
            AtmosphereIBLKey a = AtmosphereIBLKey.Create(parameter, new Vector3(0, 1, 0));
            AtmosphereIBLKey b = AtmosphereIBLKey.Create(parameter, new Vector3(0, 1, 0));
            AtmosphereIBLKey c = AtmosphereIBLKey.Create(parameter, new Vector3(0, 0, 1));

            Assert.IsTrue(a.Equals(b));
            Assert.IsFalse(a.Equals(c));
        }

        [Test]
        public void ThrowIfInvalid_EarthValidParameter_Passes()
        {
            Assert.DoesNotThrow(() => ValidParameter().ThrowIfInvalid());
        }

        [Test]
        public void ThrowIfInvalid_Thickness8000_Throws()
        {
            AtmosphereParameter parameter = ValidParameter();
            parameter.atmosphereHeight = 8000.0f;
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => parameter.ThrowIfInvalid());
            StringAssert.Contains("atmosphereHeight", exception.Message);
        }

        [Test]
        public void ThrowIfInvalid_Rayleigh30xEarth_Throws()
        {
            AtmosphereParameter parameter = ValidParameter();
            parameter.rayleighScattering = new Color(
                AtmosphereParameter.EarthRayleighScattering.r * 30.0f,
                AtmosphereParameter.EarthRayleighScattering.g * 30.0f,
                AtmosphereParameter.EarthRayleighScattering.b * 30.0f,
                1.0f);
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => parameter.ThrowIfInvalid());
            StringAssert.Contains("rayleighScattering", exception.Message);
        }

        [Test]
        public void ThrowIfInvalid_Heights1_Throws()
        {
            AtmosphereParameter parameter = ValidParameter();
            parameter.rayleighHeight = 1.0f;
            parameter.mieHeight = 1.0f;
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => parameter.ThrowIfInvalid());
            StringAssert.Contains("Height", exception.Message);
        }

        [Test]
        public void ThrowIfInvalid_OzoneZero_Throws()
        {
            AtmosphereParameter parameter = ValidParameter();
            parameter.ozoneAbsorption = new Color(0.0f, 0.0f, 0.0f, 1.0f);
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => parameter.ThrowIfInvalid());
            StringAssert.Contains("ozoneAbsorption", exception.Message);
        }

        [Test]
        public void ThrowIfInvalid_SunAngleZero_Throws()
        {
            AtmosphereParameter parameter = ValidParameter();
            parameter.sunAngle = 0.0f;
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => parameter.ThrowIfInvalid());
            StringAssert.Contains("sunAngle", exception.Message);
        }

        [Test]
        public void UpgradeOutOfRangeToEarth_BrokenProfile_RestoresEarth()
        {
            AtmosphericalProfile profile = ScriptableObject.CreateInstance<AtmosphericalProfile>();
            try
            {
                profile.thickness = 8000.0f;
                profile.rayleighScatter = new Color(
                    AtmosphereParameter.EarthRayleighScattering.r * 30.0f,
                    AtmosphereParameter.EarthRayleighScattering.g * 30.0f,
                    AtmosphereParameter.EarthRayleighScattering.b * 30.0f);
                profile.rayleighHeight = 1.0f;
                profile.mieHeight = 1.0f;
                profile.ozoneAbsorption = Color.black;
                profile.sunAngle = 0.0f;

                Assert.IsTrue(profile.UpgradeOutOfRangeToEarth());
                Assert.AreEqual(AtmosphereParameter.EarthAtmosphereHeight, profile.thickness);
                Assert.AreEqual(AtmosphereParameter.EarthRayleighScattering.r, profile.rayleighScatter.r);
                Assert.AreEqual(AtmosphereParameter.EarthRayleighScattering.g, profile.rayleighScatter.g);
                Assert.AreEqual(AtmosphereParameter.EarthRayleighScattering.b, profile.rayleighScatter.b);
                Assert.AreEqual(AtmosphereParameter.EarthRayleighHeight, profile.rayleighHeight);
                Assert.AreEqual(AtmosphereParameter.EarthMieHeight, profile.mieHeight);
                Assert.AreEqual(AtmosphereParameter.EarthOzoneAbsorption.r, profile.ozoneAbsorption.r);
                Assert.AreEqual(AtmosphereParameter.EarthSunAngle, profile.sunAngle);

                AtmosphereParameter parameter = AtmosphereParameter.FromProfile(profile);
                Assert.DoesNotThrow(() => parameter.ThrowIfInvalid());
                Assert.IsFalse(profile.UpgradeOutOfRangeToEarth());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        static AtmosphereParameter ValidParameter()
        {
            return new AtmosphereParameter
            {
                planetRadius = 6360000.0f,
                atmosphereHeight = 60000.0f,
                rayleighScattering = new Color(0.00580f, 0.01356f, 0.03310f, 1.0f),
                rayleighHeight = 8000.0f,
                mieScattering = 0.003996f,
                mieAbsorption = 0.000444f,
                mieHeight = 1200.0f,
                mieAnisotropy = 0.8f,
                ozoneAbsorption = new Color(0.000650f, 0.001881f, 0.000085f, 1.0f),
                ozoneLayerCenter = 25000.0f,
                ozoneLayerWidth = 15000.0f,
                groundAlbedo = new Color(0.3f, 0.3f, 0.3f, 1.0f),
                brightness = 1.0f,
                multiScatterStrength = 1.0f,
                sunAngle = 0.5f / 180.0f * Mathf.PI,
                drawGround = false,
                transmittanceLUTWidth = 256,
                transmittanceLUTHeight = 64,
                multiScatteringLUTSize = 32,
                skyViewLUTWidth = 192,
                skyViewLUTHeight = 108,
                aerialPerspectiveSize = 32,
                aerialPerspectiveDistance = 32000.0f,
                cubemapSize = 128
            };
        }
    }
}
