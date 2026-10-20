using NUnit.Framework;
using UnityEngine;
using InfinityTech.Component;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    // DecalComponent angle-fade parameters as Shaders/ShaderLibrary/DecalProjection.hlsl evaluates them.
    public class DecalProjectionTests
    {
        static float Fade(Vector4 p, float angleDegrees) =>
            p.z <= 0 ? 1 : Mathf.Clamp01(Mathf.Cos(angleDegrees * Mathf.Deg2Rad) * p.x + p.y);

        [Test]
        [Category("Unverified_0_5")]
        public void AngleFade_DisabledAt180_RampsBetweenStartAndEnd()
        {
            Assert.AreEqual(Vector4.zero, DecalComponent.AngleFadeParameters(180, 180));
            Vector4 p = DecalComponent.AngleFadeParameters(30, 60);
            Assert.AreEqual(1.0f, Fade(p, 0), 1e-4f);
            Assert.AreEqual(1.0f, Fade(p, 30), 1e-4f);
            Assert.AreEqual(0.0f, Fade(p, 60), 1e-4f);
            Assert.AreEqual(0.0f, Fade(p, 90), 1e-4f);
            Assert.That(Fade(p, 45), Is.InRange(0.01f, 0.99f));
            Vector4 inverted = DecalComponent.AngleFadeParameters(60, 30);
            Assert.AreEqual(0.0f, Fade(inverted, 61), 1e-3f, "An end before the start fades at the start angle.");
        }
    }
}
