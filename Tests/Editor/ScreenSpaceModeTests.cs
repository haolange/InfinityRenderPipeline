using NUnit.Framework;
using UnityEngine;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.PostProcess;
using static InfinityTech.Rendering.Pipeline.Tests.VolumeTestUtility;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class ScreenSpaceModeTests
    {
        [Test]
        public void IsActive_RequiresEnable()
        {
            ScreenSpaceReflection ssr = ScriptableObject.CreateInstance<ScreenSpaceReflection>();
            try
            {
                ssr.active = true;
                Assert.IsFalse(VolumeComponentActive(ssr));

                ssr.enable.value = true;
                Assert.IsTrue(VolumeComponentActive(ssr));

                ssr.active = false;
                Assert.IsFalse(VolumeComponentActive(ssr));
            }
            finally
            {
                Object.DestroyImmediate(ssr);
            }
        }

        [Test]
        public void Resolve_None_WhenBothInactive()
        {
            ScreenSpaceReflection ssr = ScriptableObject.CreateInstance<ScreenSpaceReflection>();
            ScreenSpaceIndirectDiffuse ssgi = ScriptableObject.CreateInstance<ScreenSpaceIndirectDiffuse>();
            try
            {
                ssr.active = false;
                ssgi.active = false;
                Assert.AreEqual(EScreenSpaceMode.None, ScreenSpaceModeUtility.Resolve(ssr, ssgi));
            }
            finally
            {
                Object.DestroyImmediate(ssr);
                Object.DestroyImmediate(ssgi);
            }
        }

        [Test]
        public void Resolve_SSR_WhenOnlyReflectionOverridden()
        {
            ScreenSpaceReflection ssr = ScriptableObject.CreateInstance<ScreenSpaceReflection>();
            ScreenSpaceIndirectDiffuse ssgi = ScriptableObject.CreateInstance<ScreenSpaceIndirectDiffuse>();
            try
            {
                ssr.active = true;
                ssr.enable.value = true;
                ssgi.active = true;
                Assert.AreEqual(EScreenSpaceMode.SSR, ScreenSpaceModeUtility.Resolve(ssr, ssgi));
            }
            finally
            {
                Object.DestroyImmediate(ssr);
                Object.DestroyImmediate(ssgi);
            }
        }

        [Test]
        public void Resolve_SSGI_WhenOnlyIndirectOverridden()
        {
            ScreenSpaceReflection ssr = ScriptableObject.CreateInstance<ScreenSpaceReflection>();
            ScreenSpaceIndirectDiffuse ssgi = ScriptableObject.CreateInstance<ScreenSpaceIndirectDiffuse>();
            try
            {
                ssr.active = true;
                ssgi.active = true;
                ssgi.enable.value = true;
                Assert.AreEqual(EScreenSpaceMode.SSGI, ScreenSpaceModeUtility.Resolve(ssr, ssgi));
            }
            finally
            {
                Object.DestroyImmediate(ssr);
                Object.DestroyImmediate(ssgi);
            }
        }

        [Test]
        public void Resolve_Both_WhenBothOverridden()
        {
            ScreenSpaceReflection ssr = ScriptableObject.CreateInstance<ScreenSpaceReflection>();
            ScreenSpaceIndirectDiffuse ssgi = ScriptableObject.CreateInstance<ScreenSpaceIndirectDiffuse>();
            try
            {
                ssr.active = true;
                ssr.enable.value = true;
                ssgi.active = true;
                ssgi.enable.value = true;
                Assert.AreEqual(EScreenSpaceMode.Both, ScreenSpaceModeUtility.Resolve(ssr, ssgi));
            }
            finally
            {
                Object.DestroyImmediate(ssr);
                Object.DestroyImmediate(ssgi);
            }
        }
    }
}
