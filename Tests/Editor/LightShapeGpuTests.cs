using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class LightShapeGpuTests
    {
        const string ShaderPath = "Packages/com.infinity.render-pipeline/Tests/Editor/Shaders/LightShapeContract.compute";
        static readonly string[] Names = { "punctual", "sphere", "line", "tube", "rect", "disk" };

        static Vector4[] RunKernel(string kernelName, int count, float distance = 0.0f)
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shaders are required.");
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath));
            try
            {
                using (var output = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 16))
                {
                    int kernel = shader.FindKernel(kernelName);
                    shader.SetVector("ShapeDistance", new Vector4(distance, 0, 0, 0));
                    shader.SetBuffer(kernel, "Results", output);
                    shader.Dispatch(kernel, count, 1, 1);
                    var results = new Vector4[count];
                    output.GetData(results);
                    return results;
                }
            }
            finally
            {
                Object.DestroyImmediate(shader);
            }
        }

        [TestCase(5.0f, 0.02f)]
        [TestCase(50.0f, 0.001f)]
        public void EveryShape_ConvergesToThePunctualIrradiance(float distance, float tolerance)
        {
            Vector4[] results = RunKernel("ShapeIrradiance", Names.Length, distance);
            for (int i = 0; i < Names.Length; ++i)
            {
                Assert.IsFalse(float.IsNaN(results[i].x) || float.IsInfinity(results[i].x), Names[i]);
                Assert.AreEqual(1.0f, results[i].x / results[i].y, tolerance, $"{Names[i]}: E / unit against far-field ref");
                Assert.That(results[i].w, Is.InRange(0.0f, 1.0f), $"{Names[i]}: energy normalization");
            }
        }

        [Test]
        public void LargerSources_LowerSpecularPeakNormalization()
        {
            Vector4[] near = RunKernel("ShapeIrradiance", Names.Length, 2.0f);
            Assert.AreEqual(1.0f, near[0].w, 1e-6f, "Punctual lights are not renormalized.");
            Assert.Less(near[1].w, 1.0f, "Sphere");
            Assert.Less(near[4].w, 1.0f, "Rect");
        }

        [Test]
        public void Quadrature_HorizonClippedRect_TiltedDisk_And_ElongatedVsSquareRect()
        {
            Vector4[] r = RunKernel("ShapeQuadrature", 17);
            // Anti-FXC-constant-folding sanity check on GPU quadrature integrators.
            Assert.AreEqual(0.176893f, r[1].w, 0.002f, "Elongated Rect GPU quadrature baseline mismatch");
            Assert.AreEqual(0.317927f, r[2].w, 0.002f, "Square Rect GPU quadrature baseline mismatch");
            // 0: Horizon-clipped rect diffuse <= 2%, specular vs GGX quadrature <= 30%.
            Assert.AreEqual(1.0f, r[0].x / r[0].y, 0.02f, "Horizon-clipped Rect diffuse vs 2D quadrature");
            Assert.AreEqual(1.0f, r[0].z / r[0].w, 0.30f, "Horizon-clipped Rect GGX specular vs 2D quadrature");

            // 1 vs 2: Same-area Elongated (3.2x0.3) vs Square (0.98x0.98) Rect must have distinct specularEnergy and bounded GGX error.
            Assert.AreNotEqual(r[1].x, r[2].x, "Elongated vs Square same-area Rect must not collapse to identical specularEnergy");
            Assert.Greater(Mathf.Abs(r[1].x - r[2].x), 0.02f, "Anisotropic specularEnergy difference between 3.2x0.3 and 0.98x0.98");
            Assert.AreEqual(1.0f, r[1].z / r[1].w, 0.30f, "Elongated Rect GGX specular vs 2D quadrature");
            Assert.AreEqual(1.0f, r[2].z / r[2].w, 0.30f, "Square Rect GGX specular vs 2D quadrature");

            // 3 & 4: Tilted Disk (45 deg & 70 deg) unclipped diffuse <= 2%, specular vs GGX quadrature <= 30%.
            Assert.AreEqual(1.0f, r[3].x / r[3].y, 0.02f, "Tilted 45 deg Disk diffuse vs 2D quadrature");
            Assert.AreEqual(1.0f, r[3].z / r[3].w, 0.30f, "Tilted 45 deg Disk GGX specular vs 2D quadrature");
            Assert.AreEqual(1.0f, r[4].x / r[4].y, 0.02f, "Tilted 70 deg Disk diffuse vs 2D quadrature");
            Assert.AreEqual(1.0f, r[4].z / r[4].w, 0.30f, "Tilted 70 deg Disk GGX specular vs 2D quadrature");

            // 5: Horizon-crossing Disk remains finite, positive, and tracks clipped integral.
            Assert.Greater(r[5].x, 0.0f);
            Assert.AreEqual(1.0f, r[5].x / r[5].y, 0.15f, "Horizon-crossing Disk diffuse vs 2D quadrature");

            // 15: DiskCone shifted-apex beam clipping (on-axis == 1, rim-cone > 0, outside == 0, behind == 0).
            Assert.AreEqual(1.0f, r[15].x, 1e-5f, "DiskCone on-axis window");
            Assert.Greater(r[15].y, 0.0f, "DiskCone rim-cone window");
            Assert.AreEqual(0.0f, r[15].z, 1e-6f, "DiskCone outside cone window");
            Assert.AreEqual(0.0f, r[15].w, 1e-6f, "DiskCone behind emitter window");

            // 16: Off-lobe 45-degree tilted Disk diffuse <= 2% and bounded finite Karis MRP specular.
            Assert.AreEqual(1.0f, r[16].x / r[16].y, 0.02f, "Off-lobe 45 deg Disk diffuse vs 2D quadrature");
            Assert.IsFalse(float.IsNaN(r[16].z) || float.IsInfinity(r[16].z));
            Assert.That(r[16].z, Is.InRange(0.0f, r[16].w * 2.0f), "Off-lobe 45 deg Disk bounded Karis MRP specular");
        }

        [Test]
        public void Quadrature_TubeCapsule_And_ZeroSizeTransitions()
        {
            Vector4[] r = RunKernel("ShapeQuadrature", 17);
            // 6..8: Broadside Tube at r = 0.005 (thin ribbon), 0.08, 0.25 vs 3D capsule quadrature.
            Assert.AreEqual(1.0f, r[6].x / r[6].y, 0.05f, "Tube r=0.005 thin-ribbon diffuse vs 3D capsule");
            Assert.AreEqual(1.0f, r[7].x / r[7].y, 0.06f, "Tube r=0.08 diffuse vs 3D capsule");
            Assert.AreEqual(1.0f, r[8].x / r[8].y, 0.10f, "Tube r=0.25 diffuse vs 3D capsule");
            Assert.AreEqual(1.0f, r[6].z / r[6].w, 0.30f, "Tube r=0.005 GGX specular vs 3D capsule");
            Assert.AreEqual(1.0f, r[7].z / r[7].w, 0.30f, "Tube r=0.08 GGX specular vs 3D capsule");
            Assert.AreEqual(1.0f, r[8].z / r[8].w, 0.30f, "Tube r=0.25 GGX specular vs 3D capsule");

            // 9 vs 10: Continuity across r / d = 0.02 thin-ribbon switch.
            Assert.AreEqual(1.0f, r[9].x / r[9].y, 0.05f, "Tube r/d=0.0195 diffuse vs capsule");
            Assert.AreEqual(1.0f, r[10].x / r[10].y, 0.05f, "Tube r/d=0.0205 diffuse vs capsule");
            Assert.AreEqual(r[9].x, r[10].x, r[10].x * 0.06f, "Thin/thick ribbon transition must be continuous across r/d=0.02");

            // 11: End-on Tube non-zero diffuse and bounded against 3D capsule (~0.72 of single-cap due to 2-cap average).
            Assert.Greater(r[11].x, 0.04f, "End-on Tube diffuse must be positive");
            Assert.AreEqual(1.0f, r[11].x / r[11].y, 0.35f, "End-on Tube diffuse vs 3D capsule");
            Assert.IsFalse(float.IsNaN(r[11].z));

            // 12..14: Zero-size limits (Sphere->Point, Line->Point, Tube->Sphere), non-vacuous diffuse and energy parity.
            Assert.Greater(r[12].y, 0.1f, "Sphere->Point diffuse must not be vacuous zero");
            Assert.Greater(r[13].y, 0.1f, "Line->Point diffuse must not be vacuous zero");
            Assert.Greater(r[14].y, 0.1f, "Tube->Sphere diffuse must not be vacuous zero");
            Assert.AreEqual(r[12].y, r[12].x, r[12].y * 0.002f, "Sphere -> Point");
            Assert.AreEqual(r[13].y, r[13].x, r[13].y * 0.002f, "Line -> Point");
            Assert.AreEqual(r[14].y, r[14].x, r[14].y * 0.002f, "Tube -> Sphere");
            Assert.AreEqual(r[12].w, r[12].z, 0.002f, "Sphere -> Point energy");
            Assert.AreEqual(r[13].w, r[13].z, 0.002f, "Line -> Point energy");
            Assert.AreEqual(r[14].w, r[14].z, 1e-4f, "Tube -> Sphere energy");
        }

        [Test]
        public void PlanarRepresentativePoint_ContinuousAcrossGrazingPlane()
        {
            Vector4[] r = RunKernel("PlanarRepresentativeSweep", 42);
            for (int baseIdx = 0; baseIdx < 42; baseIdx += 21)
            {
                for (int i = 0; i < 20; ++i)
                {
                    Vector3 a = r[baseIdx + i];
                    Vector3 b = r[baseIdx + i + 1];
                    float delta = Vector3.Distance(a, b);
                    Assert.IsFalse(float.IsNaN(delta) || float.IsInfinity(delta));
                    Assert.Less(delta, 0.15f, $"Planar representative jumped at step {i} (group {baseIdx})");
                }
            }
        }

        [Test]
        public void EvaluateVolumeLocalLight_MatchesPhysicalUnitsAndShapes()
        {
            Vector4[] r = RunKernel("VolumeLocalLightContract", 5);
            // 0 vs 1: Planar Rect Nits in front vs zero behind.
            Assert.AreEqual(2.0f / (16.0f + 2.0f / Mathf.PI), r[0].w, 0.01f, "Volumetric Rect Nits attenuation");
            Assert.AreEqual(0.0f, r[1].w, 1e-6f, "Volumetric Rect behind-plane must be zero");

            // 2 vs 3: SpotBox Lux constant with distance on plateau.
            Assert.AreEqual(new Vector3(0, 0, -1), new Vector3(r[2].x, r[2].y, r[2].z));
            Assert.AreEqual(r[2].w, r[3].w, 0.002f, "SpotBox volumetric Lux must stay uniform across short distance change");

            // 4: Line/Tube L points toward closest point on segment (0.4, 0, 0) from (0.4, 2, 0), so L == (0, -1, 0).
            Assert.AreEqual(0.0f, r[4].x, 1e-5f, "Volumetric Tube L.x should aim at closest point");
            Assert.AreEqual(-1.0f, r[4].y, 1e-5f, "Volumetric Tube L.y should point along -Y");
            Assert.Greater(r[4].w, 0.0f);
        }
    }
}
