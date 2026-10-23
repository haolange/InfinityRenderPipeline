using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class CloudShellGpuTests
    {
        const string ShaderPath = "Packages/com.infinity.render-pipeline/Tests/Editor/Shaders/CloudShellContract.compute";
        readonly struct RayCase
        {
            public readonly string name;
            public readonly Vector3 origin, direction;
            public readonly float limit;
            public RayCase(string name, float altitude, Vector3 direction, float limit = 1e8f)
            {
                this.name = name;
                origin = new Vector3(0, altitude, 0);
                this.direction = direction.normalized;
                this.limit = limit;
            }
            public RayCase(string name, Vector3 origin, Vector3 direction)
            {
                this.name = name; this.origin = origin; this.direction = direction.normalized; limit = 1e8f;
            }
        }

        static RayCase[] Cases(float radius)
        {
            // A ray from above the cloud top whose closest approach lies inside
            // the inner cloud sphere, but above the planet: two visible segments.
            float tangentY = -Mathf.Sqrt(1 - Mathf.Pow((radius + 250) / (radius + 3000), 2));
            return new[]
            {
                new RayCase("below up", 50, Vector3.up),
                new RayCase("below down planet", 50, Vector3.down),
                new RayCase("inside up", 1000, Vector3.up),
                new RayCase("inside down", 1000, Vector3.down),
                new RayCase("above down", 3000, Vector3.down),
                new RayCase("above away", 3000, Vector3.up),
                new RayCase("two segments", 3000, new Vector3(Mathf.Sqrt(1 - tangentY * tangentY), tangentY, 0)),
                new RayCase("inner tangent", 500, Vector3.right),
                new RayCase("outer tangent", 2000, Vector3.right),
                new RayCase("outside outer tangent", 2002, Vector3.right),
                new RayCase("inside outer tangent", 1998, Vector3.right),
                new RayCase("off pole", new Vector3(30000, 1000, 40000), new Vector3(0, radius + 1000, 40000)),
                new RayCase("surface down", 0, Vector3.down),
                new RayCase("surface up", 0, Vector3.up),
                new RayCase("inside planet", -10, Vector3.up),
                new RayCase("occluder before cloud", 50, Vector3.up, 200),
                new RayCase("occluder inside cloud", 50, Vector3.up, 800),
                new RayCase("occluder after cloud", 50, Vector3.up, 3000),
                new RayCase("local light within cloud", 1000, Vector3.up, 100),
                new RayCase("local light beyond cloud", 1000, Vector3.up, 5000),
                new RayCase("oblique light", 1000, new Vector3(1, .2f, 0)),
            };
        }

        // Independent double-precision oracle: split the positive ray at every
        // sphere boundary and classify each midpoint, rather than subtract intervals.
        static Vector4 Oracle(RayCase ray, double radius)
        {
            double ox = ray.origin.x, oz = ray.origin.z;
            double oy = radius + ray.origin.y;
            double originRadius = Math.Sqrt(ox * ox + oy * oy + oz * oz);
            if (originRadius < radius) return Vector4.zero;
            double dx = ray.direction.x, dy = ray.direction.y, dz = ray.direction.z;
            double norm = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            dx /= norm; dy /= norm; dz /= norm;
            double b = ox * dx + oy * dy + oz * dz, limit = ray.limit;
            var cuts = new List<double> { 0, limit };
            foreach (double r in new[] { radius, radius + 500, radius + 2000 })
            {
                double discriminant = b * b - (originRadius * originRadius - r * r);
                if (discriminant < 0) continue;
                double root = Math.Sqrt(discriminant);
                foreach (double t in new[] { -b - root, -b + root })
                {
                    if (t < 0 || t > ray.limit) continue;
                    if (r == radius && (t > 0 || dy < 0)) limit = Math.Min(limit, t);
                    cuts.Add(t);
                }
            }
            cuts.Sort();
            var endpoints = new List<double>();
            for (int i = 1; i < cuts.Count; ++i)
            {
                double a = cuts[i - 1], z = Math.Min(cuts[i], limit);
                if (z <= a) continue;
                double t = (a + z) * .5;
                double height = Math.Sqrt((ox + t * dx) * (ox + t * dx) + (oy + t * dy) * (oy + t * dy) + (oz + t * dz) * (oz + t * dz)) - radius;
                if (height <= 500 || height >= 2000) continue;
                if (endpoints.Count > 0 && endpoints[endpoints.Count - 1] == a) endpoints[endpoints.Count - 1] = z;
                else { endpoints.Add(a); endpoints.Add(z); }
            }
            Assert.LessOrEqual(endpoints.Count, 4, ray.name);
            while (endpoints.Count < 4) endpoints.Add(0);
            return new Vector4((float)endpoints[0], (float)endpoints[1], (float)endpoints[2], (float)endpoints[3]);
        }

        [TestCase(6360000f)]
        [TestCase(3200000f)]
        public void CloudSegments_MatchIndependentSphereOracle_AndRespectOccluders(float radius)
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shaders are required.");
            var shader = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath));
            Assert.IsNotNull(shader);
            try
            {
                RayCase[] cases = Cases(radius);
                var inputs = new Vector4[cases.Length * 2];
                for (int i = 0; i < cases.Length; ++i)
                {
                    inputs[i * 2] = new Vector4(cases[i].origin.x, cases[i].origin.y, cases[i].origin.z, cases[i].limit);
                    inputs[i * 2 + 1] = cases[i].direction;
                }
                using (var input = new ComputeBuffer(inputs.Length, 16))
                using (var output = new ComputeBuffer(inputs.Length, 16))
                {
                    input.SetData(inputs);
                    int kernel = shader.FindKernel("CloudShellContract");
                    shader.SetFloat("PlanetRadius", radius);
                    shader.SetInt("CaseCount", cases.Length);
                    shader.SetBuffer(kernel, "Cases", input);
                    shader.SetBuffer(kernel, "Results", output);
                    shader.Dispatch(kernel, cases.Length, 1, 1);
                    var results = new Vector4[inputs.Length];
                    output.GetData(results);
                    for (int i = 0; i < cases.Length; ++i)
                    {
                        Vector4 expected = Oracle(cases[i], radius), actual = results[i * 2], samples = results[i * 2 + 1];
                        int expectedCount = (expected.y > expected.x ? 1 : 0) + (expected.w > expected.z ? 1 : 0);
                        int actualCount = (actual.y > actual.x ? 1 : 0) + (actual.w > actual.z ? 1 : 0);
                        Assert.AreEqual(expectedCount, actualCount, cases[i].name + " topology");
                        for (int segment = 0; segment < actualCount; ++segment)
                        {
                            Assert.GreaterOrEqual(actual[segment * 2], 0, cases[i].name);
                            Assert.Greater(actual[segment * 2 + 1], actual[segment * 2], cases[i].name);
                            Assert.LessOrEqual(actual[segment * 2 + 1], cases[i].limit, cases[i].name + " opaque clip");
                        }
                        if (actualCount == 2) Assert.GreaterOrEqual(actual.z, actual.y, cases[i].name + " ordered segments");
                        TestContext.WriteLine($"R={radius} {cases[i].name}: GPU={actual.ToString("F4")} oracle={expected.ToString("F4")}");
                        for (int c = 0; c < 4; ++c)
                        {
                            Assert.IsFalse(float.IsNaN(actual[c]) || float.IsInfinity(actual[c]), cases[i].name);
                            // Planet-scale floats quantize altitude to half meters;
                            // grazing distance amplifies that error, vertical cases do not.
                            float tolerance = Mathf.Max(1, Mathf.Abs(expected[c]) * 2e-4f);
                            Assert.AreEqual(expected[c], actual[c], tolerance, cases[i].name + " endpoint " + c);
                        }
                        Vector3 origin = cases[i].origin;
                        double originRadius = Math.Sqrt((double)origin.x * origin.x + (radius + (double)origin.y) * (radius + (double)origin.y) + (double)origin.z * origin.z);
                        Assert.AreEqual((originRadius - radius - 500) / 1500, samples.x, .0005f, cases[i].name + " unclamped height");
                        if (samples.w > 0)
                        {
                            Assert.That(samples.y, Is.InRange(-.001f, 1.001f), cases[i].name + " sample minimum");
                            Assert.That(samples.z, Is.InRange(-.001f, 1.001f), cases[i].name + " sample maximum");
                        }
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(shader); }
        }

        [Test]
        public void DistantOpaqueDepth_IsNotTheSkyClearValue()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shaders are required.");
            var shader = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath));
            Assert.IsNotNull(shader);
            try
            {
                // Power-of-two near/far keep the near-clear inverse stable with or without FMA.
                var inputs = new[]
                {
                    new Vector4(0, .00390625f, 1, 150500),
                    new Vector4(9e-8f, .00390625f, 1, 0),
                    new Vector4(1, .00390625f, 0, 150500),
                    new Vector4(.99999994f, .00390625f, 0, 0),
                    new Vector4(.1f, .00390625f, 1, 0),
                    new Vector4(.1f, .00390625f, 0, 0),
                };
                using (var input = new ComputeBuffer(inputs.Length, 16))
                using (var output = new ComputeBuffer(inputs.Length, 16))
                {
                    input.SetData(inputs);
                    int kernel = shader.FindKernel("CloudDepthContract");
                    shader.SetFloat("PlanetRadius", 6360000);
                    shader.SetBuffer(kernel, "Cases", input);
                    shader.SetBuffer(kernel, "Results", output);
                    shader.Dispatch(kernel, inputs.Length, 1, 1);
                    var results = new Vector4[inputs.Length];
                    output.GetData(results);
                    for (int i = 0; i < inputs.Length; ++i)
                    {
                        double near = inputs[i].y;
                        double denominator = inputs[i].z != 0 ? near + inputs[i].x * (65536.0 - near) : 65536.0 - inputs[i].x * (65536.0 - near);
                        double distance = inputs[i].w > 0 ? double.PositiveInfinity : near * 65536 / denominator;
                        bool hit = distance > 500;
                        Assert.AreEqual(hit ? 500 : 0, results[i].x, 1, "entry " + i);
                        double expectedEnd = hit ? Math.Min(distance, 150500) : 0;
                        Assert.AreEqual(expectedEnd, results[i].y, Math.Max(1, expectedEnd * .0002), "opaque end or sky exit " + i);
                        Assert.AreEqual(Vector2.zero, new Vector2(results[i].z, results[i].w));
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(shader); }
        }
    }
}
