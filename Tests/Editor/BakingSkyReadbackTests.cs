using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using InfinityTech.Rendering.Pipeline.Editor;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class BakingSkyReadbackTests
    {
        Texture2DArray m_Source, m_Destination;

        [SetUp]
        public void SetUp()
        {
            m_Source = new Texture2DArray(2, 2, 6, TextureFormat.RGBAHalf, false, true);
            m_Destination = new Texture2DArray(2, 2, 6, TextureFormat.RGBAHalf, false, true);
            for (int face = 0; face < 6; ++face)
            {
                m_Source.SetPixels(new[]
                {
                    new Color(face + 1, 0.25f, 0.5f, 1), new Color(face + 1, 0.5f, 0.25f, 1),
                    new Color(face + 1, 0.75f, 0.125f, 1), new Color(face + 1, 1, 0.0625f, 1)
                }, face);
            }
            m_Source.Apply(false, false);
        }

        [TearDown]
        public void TearDown()
        {
            if (m_Source != null) UnityEngine.Object.DestroyImmediate(m_Source);
            if (m_Destination != null) UnityEngine.Object.DestroyImmediate(m_Destination);
        }

        [Test]
        public void ValidReadback_PreservesEveryFaceAndPixelOrientation()
        {
            var request = AsyncGPUReadback.Request(m_Source, 0, TextureFormat.RGBAHalf);
            InfinityBakingSky.CopyReadbackData(request, m_Destination);
            for (int face = 0; face < 6; ++face)
                CollectionAssert.AreEqual(m_Source.GetPixelData<ushort>(0, face).ToArray(),
                    m_Destination.GetPixelData<ushort>(0, face).ToArray(), "face " + face);
        }

        [TestCase(0x7C00)]
        [TestCase(0x7E00)]
        public void NonFiniteLastFace_RejectsReadbackBeforeAssetCommit(int bits)
        {
            var lastFace = m_Source.GetPixelData<ushort>(0, 5);
            lastFace[0] = (ushort)bits;
            m_Source.Apply(false, false);
            var request = AsyncGPUReadback.Request(m_Source, 0, TextureFormat.RGBAHalf);
            request.WaitForCompletion();
            Assert.IsFalse(request.hasError, "This control requires successful native readback containing invalid data.");
            var error = Assert.Throws<InvalidOperationException>(() => InfinityBakingSky.CopyReadbackData(request, m_Destination));
            StringAssert.Contains("non-finite", error.Message);
        }

        [UnityTest]
        public IEnumerator ExpiredRequest_RejectsWithoutChangingDestination()
        {
            var before = m_Destination.GetPixelData<ushort>(0, 0).ToArray();
            var request = AsyncGPUReadback.Request(m_Source, 0, TextureFormat.RGBAHalf);
            request.WaitForCompletion();
            Assert.IsFalse(request.hasError, "The request must first complete successfully.");
            for (int frame = 0; frame < 60 && !request.hasError; ++frame)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                yield return null;
            }
            Assert.IsTrue(request.hasError, "Unity must expire the actual completed request before this negative control.");
            Assert.Throws<InvalidOperationException>(() => InfinityBakingSky.CopyReadbackData(request, m_Destination));
            CollectionAssert.AreEqual(before, m_Destination.GetPixelData<ushort>(0, 0).ToArray());
        }
    }
}
