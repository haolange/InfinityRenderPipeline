using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class SurfaceMetadataTests
    {
        // The screen-space history limit: SCREEN_SPACE_MAX_HISTORY_AGE in ScreenSpaceDenoise/TemporalAccumulation.hlsl.
        const int k_MaxHistoryAge = 64;

        [Test]
        public void ProductionCodec_RoundTripsNormalAgeAndValidity()
        {
            const int count = 4096;
            var source = new Texture2D(count, 1, GraphicsFormat.R32G32B32A32_SFloat, UnityEngine.Experimental.Rendering.TextureCreationFlags.None);
            RenderTexture packed = null;
            try
            {
                // Samples

                var pixels = new Color[count];
                var normals = new Vector3[count];
                for (int i = 0; i < count; ++i)
                {
                    // A Fibonacci sphere covers every octahedral face of both hemispheres.
                    float z = 1f - 2f * (i + .5f) / count;
                    float angle = i * 2.39996323f;
                    float radius = Mathf.Sqrt(1 - z * z);
                    var normal = new Vector3(radius * Mathf.Cos(angle), radius * Mathf.Sin(angle), z);
                    normals[i] = normal;
                    Vector2 octahedron = Octahedron(normal);
                    pixels[i] = new Color(octahedron.x, octahedron.y, Age(i), Valid(i) ? 1 : 0);
                }
                source.SetPixels(pixels);
                source.Apply();

                // Round trip

                packed = SurfaceMetadataTestUtility.Pack(source);
                float[] values = SurfaceMetadataTestUtility.Read(packed);
                double maxAngle = 0;
                for (int i = 0; i < count; ++i)
                {
                    // The age saturates at the history limit, and an invalid sample reads as age 0 whatever it stored.
                    float expectedAge = Valid(i) ? Mathf.Min(Age(i), k_MaxHistoryAge) : 0;
                    Assert.AreEqual(expectedAge, values[i * 4 + 3], $"age of sample {i} (stored {Age(i)}, valid {Valid(i)})");
                    double x = values[i * 4];
                    double y = values[i * 4 + 1];
                    double z = values[i * 4 + 2];
                    Vector3 expected = normals[i];
                    double crossX = y * expected.z - z * expected.y;
                    double crossY = z * expected.x - x * expected.z;
                    double crossZ = x * expected.y - y * expected.x;
                    double dot = x * expected.x + y * expected.y + z * expected.z;
                    double angleDegrees = Math.Atan2(Math.Sqrt(crossX * crossX + crossY * crossY + crossZ * crossZ), dot) * 180 / Math.PI;
                    maxAngle = Math.Max(maxAngle, angleDegrees);
                }
                // Twelve bits per octahedral axis keep the normal well inside 0.1 degree, independent of age and validity.
                Assert.LessOrEqual(maxAngle, .1);
            }
            finally
            {
                if (packed != null)
                {
                    packed.Release();
                    UnityEngine.Object.DestroyImmediate(packed);
                }
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        // Every age the seven-bit field can hold, stored once valid and once invalid in each run of 256 samples.
        static int Age(int sample) => sample % 128;

        static bool Valid(int sample) => (sample / 128) % 2 == 0;

        static Vector2 Octahedron(Vector3 normal)
        {
            float sum = Mathf.Abs(normal.x) + Mathf.Abs(normal.y) + Mathf.Abs(normal.z);
            var octahedron = new Vector2(normal.x / sum, normal.y / sum);
            if (normal.z <= 0)
            {
                octahedron = new Vector2((1 - Mathf.Abs(octahedron.y)) * (octahedron.x >= 0 ? 1 : -1), (1 - Mathf.Abs(octahedron.x)) * (octahedron.y >= 0 ? 1 : -1));
            }
            return octahedron;
        }
    }
}
