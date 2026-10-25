using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class SurfaceMetadataTests
    {
        [Test]
        public void ProductionCodec_PreservesDepthAgeAndNormalAccuracy()
        {
            const int count = 4096;
            var source = new Texture2D(count, 1, GraphicsFormat.R32G32B32A32_SFloat, UnityEngine.Experimental.Rendering.TextureCreationFlags.None);
            RenderTexture packed = null;
            try
            {
                var pixels = new Color[count]; var normals = new Vector3[count];
                for (int i = 0; i < count; ++i)
                {
                    float z = 1f - 2f * (i + .5f) / count, a = i * 2.39996323f;
                    var normal = new Vector3(Mathf.Sqrt(1 - z * z) * Mathf.Cos(a), Mathf.Sqrt(1 - z * z) * Mathf.Sin(a), z);
                    normals[i] = normal;
                    var oct = new Vector2(normal.x, normal.y) / (Mathf.Abs(normal.x) + Mathf.Abs(normal.y) + Mathf.Abs(normal.z));
                    if (normal.z <= 0) oct = new Vector2((1 - Mathf.Abs(oct.y)) * (oct.x >= 0 ? 1 : -1), (1 - Mathf.Abs(oct.x)) * (oct.y >= 0 ? 1 : -1));
                    pixels[i] = new Color(oct.x, oct.y, BitConverter.Int32BitsToSingle(0x3a000000 + i * 937), i % 65);
                }
                source.SetPixels(pixels); source.Apply(); packed = SurfaceMetadataTestUtility.Pack(source);
                var values = SurfaceMetadataTestUtility.Read(packed);
                double maxAngle = 0;
                for (int i = 0; i < count; ++i)
                {
                    Assert.AreEqual(BitConverter.SingleToInt32Bits(pixels[i].b), BitConverter.SingleToInt32Bits(values[i * 4 + 2]));
                    Assert.AreEqual(i % 65, values[i * 4 + 3]);
                    var n = new Vector3(values[i * 4], values[i * 4 + 1], 1 - Mathf.Abs(values[i * 4]) - Mathf.Abs(values[i * 4 + 1]));
                    if (n.z < 0) { float x = n.x; n.x = (1 - Mathf.Abs(n.y)) * (x >= 0 ? 1 : -1); n.y = (1 - Mathf.Abs(x)) * (n.y >= 0 ? 1 : -1); }
                    n.Normalize();
                    double dot = (double)n.x * normals[i].x + (double)n.y * normals[i].y + (double)n.z * normals[i].z;
                    double lengths = Math.Sqrt(((double)n.x*n.x+(double)n.y*n.y+(double)n.z*n.z)*((double)normals[i].x*normals[i].x+(double)normals[i].y*normals[i].y+(double)normals[i].z*normals[i].z));
                    maxAngle = Math.Max(maxAngle, Math.Acos(Math.Clamp(dot / lengths, -1, 1)) * 180 / Math.PI);
                }
                Assert.LessOrEqual(maxAngle, .1);
            }
            finally { if (packed != null) { packed.Release(); UnityEngine.Object.DestroyImmediate(packed); } UnityEngine.Object.DestroyImmediate(source); }
        }
    }
}
