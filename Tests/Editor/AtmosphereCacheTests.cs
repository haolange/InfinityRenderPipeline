using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class AtmosphereCacheTests
    {
        AtmosphereSharedCache m_Shared;
        AtmosphereViewCache m_View;

        [SetUp]
        public void SetUp()
        {
            RTHandles.Initialize(64, 64);
            m_Shared = new AtmosphereSharedCache();
            m_View = new AtmosphereViewCache();
        }

        [TearDown]
        public void TearDown()
        {
            m_Shared?.Dispose();
            m_View?.Dispose();
            m_Shared = null;
            m_View = null;
        }

        [Test]
        public void SharedCache_SameKey_SecondResolveIsHit()
        {
            AtmosphereParameter key = ValidParameter();
            TextureDescriptor trans = MakeLut(16, 8, "Trans");
            TextureDescriptor multi = MakeLut(8, 8, "Multi");

            m_Shared.BeginFrame();
            m_Shared.ResolveShared(key, trans, multi, out _, out _, out bool firstHit);
            Assert.IsFalse(firstHit);
            m_Shared.MarkSharedProduced();

            m_Shared.ResolveShared(key, trans, multi, out _, out _, out bool sameFrameHit);
            Assert.IsTrue(sameFrameHit);

            m_Shared.CommitFrame();
            m_Shared.BeginFrame();
            m_Shared.ResolveShared(key, trans, multi, out _, out _, out bool committedHit);
            Assert.IsTrue(committedHit);
        }

        [Test]
        public void SharedCache_KeyChange_IsMiss()
        {
            AtmosphereParameter key = ValidParameter();
            TextureDescriptor trans = MakeLut(16, 8, "Trans");
            TextureDescriptor multi = MakeLut(8, 8, "Multi");

            m_Shared.BeginFrame();
            m_Shared.ResolveShared(key, trans, multi, out _, out _, out _);
            m_Shared.MarkSharedProduced();
            m_Shared.CommitFrame();

            key.brightness = 2.0f;
            m_Shared.BeginFrame();
            m_Shared.ResolveShared(key, trans, multi, out _, out _, out bool hit);
            Assert.IsFalse(hit);
        }

        [Test]
        public void SharedCache_ThicknessChange_IsMiss()
        {
            AtmosphereParameter key = ValidParameter();
            TextureDescriptor trans = MakeLut(16, 8, "Trans");
            TextureDescriptor multi = MakeLut(8, 8, "Multi");

            m_Shared.BeginFrame();
            m_Shared.ResolveShared(key, trans, multi, out _, out _, out _);
            m_Shared.MarkSharedProduced();
            m_Shared.CommitFrame();

            key.atmosphereHeight = 80000.0f;
            m_Shared.BeginFrame();
            m_Shared.ResolveShared(key, trans, multi, out _, out _, out bool hit);
            Assert.IsFalse(hit);
        }

        [Test]
        public void SharedCache_RayleighChange_IsMiss()
        {
            AtmosphereParameter key = ValidParameter();
            TextureDescriptor trans = MakeLut(16, 8, "Trans");
            TextureDescriptor multi = MakeLut(8, 8, "Multi");

            m_Shared.BeginFrame();
            m_Shared.ResolveShared(key, trans, multi, out _, out _, out _);
            m_Shared.MarkSharedProduced();
            m_Shared.CommitFrame();

            key.rayleighScattering = new Color(0.01f, 0.02f, 0.04f, 1.0f);
            m_Shared.BeginFrame();
            m_Shared.ResolveShared(key, trans, multi, out _, out _, out bool hit);
            Assert.IsFalse(hit);
        }

        [Test]
        public void ViewCache_SameQuantizedPose_IsHit()
        {
            AtmosphereParameter parameter = ValidParameter();
            AtmosphereViewKey keyA = AtmosphereViewKey.Create(parameter, Vector3.up, new Vector3(1.2f, 2.4f, 3.6f));
            AtmosphereViewKey keyB = AtmosphereViewKey.Create(parameter, Vector3.up, new Vector3(1.9f, 2.1f, 3.2f));
            TextureDescriptor sky = MakeLut(16, 8, "Sky");
            TextureDescriptor aerial = MakeAerial(8);
            BufferDescriptor sun = new BufferDescriptor(1, 16, ComputeBufferType.Structured);

            m_View.BeginFrame();
            m_View.Resolve(keyA, sky, aerial, sun, out _, out _, out _, out bool firstHit);
            Assert.IsFalse(firstHit);
            m_View.MarkProduced();
            m_View.CommitFrame();

            m_View.BeginFrame();
            m_View.Resolve(keyB, sky, aerial, sun, out _, out _, out _, out bool hit);
            Assert.IsTrue(hit);
        }

        [Test]
        public void ViewCache_ThicknessOrRayleighChange_IsMiss()
        {
            AtmosphereParameter parameter = ValidParameter();
            Vector3 sun = Vector3.up;
            Vector3 camera = new Vector3(1.2f, 2.4f, 3.6f);
            AtmosphereViewKey key = AtmosphereViewKey.Create(parameter, sun, camera);
            TextureDescriptor sky = MakeLut(16, 8, "Sky");
            TextureDescriptor aerial = MakeAerial(8);
            BufferDescriptor sunBuffer = new BufferDescriptor(1, 16, ComputeBufferType.Structured);

            m_View.BeginFrame();
            m_View.Resolve(key, sky, aerial, sunBuffer, out _, out _, out _, out _);
            m_View.MarkProduced();
            m_View.CommitFrame();

            parameter.atmosphereHeight = 80000.0f;
            AtmosphereViewKey thicknessKey = AtmosphereViewKey.Create(parameter, sun, camera);
            m_View.BeginFrame();
            m_View.Resolve(thicknessKey, sky, aerial, sunBuffer, out _, out _, out _, out bool thicknessHit);
            Assert.IsFalse(thicknessHit);
            m_View.MarkProduced();
            m_View.CommitFrame();

            parameter.rayleighScattering = new Color(0.01f, 0.02f, 0.04f, 1.0f);
            AtmosphereViewKey rayleighKey = AtmosphereViewKey.Create(parameter, sun, camera);
            m_View.BeginFrame();
            m_View.Resolve(rayleighKey, sky, aerial, sunBuffer, out _, out _, out _, out bool rayleighHit);
            Assert.IsFalse(rayleighHit);
        }

        [Test]
        public void IBLCache_ThicknessOrRayleighChange_IsMiss()
        {
            AtmosphereParameter parameter = ValidParameter();
            AtmosphereIBLKey key = AtmosphereIBLKey.Create(parameter, Vector3.up);
            TextureDescriptor cubemap = MakeCubemap(16, "Cube", false);
            TextureDescriptor prefilter = MakeCubemap(16, "Prefilter", true);
            BufferDescriptor sh = new BufferDescriptor(9, 16, ComputeBufferType.Structured);

            m_Shared.BeginFrame();
            m_Shared.ResolveIBL(key, cubemap, prefilter, sh, out _, out _, out _, out bool firstHit);
            Assert.IsFalse(firstHit);
            m_Shared.MarkIBLProduced();
            m_Shared.CommitFrame();

            parameter.atmosphereHeight = 80000.0f;
            AtmosphereIBLKey thicknessKey = AtmosphereIBLKey.Create(parameter, Vector3.up);
            m_Shared.BeginFrame();
            m_Shared.ResolveIBL(thicknessKey, cubemap, prefilter, sh, out _, out _, out _, out bool thicknessHit);
            Assert.IsFalse(thicknessHit);
            m_Shared.MarkIBLProduced();
            m_Shared.CommitFrame();

            parameter.rayleighScattering = new Color(0.01f, 0.02f, 0.04f, 1.0f);
            AtmosphereIBLKey rayleighKey = AtmosphereIBLKey.Create(parameter, Vector3.up);
            m_Shared.BeginFrame();
            m_Shared.ResolveIBL(rayleighKey, cubemap, prefilter, sh, out _, out _, out _, out bool rayleighHit);
            Assert.IsFalse(rayleighHit);
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
                transmittanceLUTWidth = 16,
                transmittanceLUTHeight = 8,
                multiScatteringLUTSize = 8,
                skyViewLUTWidth = 16,
                skyViewLUTHeight = 8,
                aerialPerspectiveSize = 8,
                aerialPerspectiveDistance = 32000.0f,
                cubemapSize = 16
            };
        }

        static TextureDescriptor MakeLut(int width, int height, string name)
        {
            return new TextureDescriptor(width, height)
            {
                name = name,
                dimension = TextureDimension.Tex2D,
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                depthBufferBits = EDepthBits.None,
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        static TextureDescriptor MakeAerial(int size)
        {
            return new TextureDescriptor(size, size, size)
            {
                name = "Aerial",
                dimension = TextureDimension.Tex3D,
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                depthBufferBits = EDepthBits.None,
                enableRandomWrite = true,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        static TextureDescriptor MakeCubemap(int size, string name, bool useMips)
        {
            return new TextureDescriptor(size, size, 6)
            {
                name = name,
                dimension = TextureDimension.Tex2DArray,
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                depthBufferBits = EDepthBits.None,
                enableRandomWrite = true,
                useMipMap = useMips,
                autoGenerateMips = false,
                filterMode = useMips ? FilterMode.Trilinear : FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
        }
    }
}
