using InfinityTech.Component;
using NUnit.Framework;
using UnityEngine;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class LivenessMarkerPlacementTests
    {
        [TestCase(false, 60f, 1.333333f, 0.3f, 100f)]
        [TestCase(false, 90f, 0.25f, 5f, 100f)]
        [TestCase(false, 30f, 4f, 0.01f, 0.2f)]
        [TestCase(false, 170f, 1f, 0.3f, 100f)]
        [TestCase(true, 5f, 1.333333f, 0.3f, 100f)]
        [TestCase(true, 10f, 0.2f, 10f, 10.2f)]
        public void PlacedCube_RotatingCornersRemainInCornerAndClipRange(bool orthographic, float lens,
            float aspect, float near, float far)
        {
            var cameraObject = new GameObject("Marker placement camera") { hideFlags = HideFlags.HideAndDontSave };
            var marker = new GameObject("Marker placement cube") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = orthographic;
                if (orthographic) camera.orthographicSize = lens;
                else camera.fieldOfView = lens;
                camera.aspect = aspect;
                camera.nearClipPlane = near;
                camera.farClipPlane = far;
                camera.transform.SetPositionAndRotation(new Vector3(2, 3, -4), Quaternion.Euler(10, 30, 0));
                marker.transform.localScale = Vector3.one * 0.25f;
                LivenessMarkerUtility.PlaceInCameraNearCorner(marker.transform, camera);

                foreach (float angle in new[] { 0f, 45f, 89f, 135f })
                {
                    marker.transform.rotation = Quaternion.Euler(angle * 0.5f, angle, angle * 0.3f);
                    for (int corner = 0; corner < 8; corner++)
                    {
                        var local = new Vector3((corner & 1) == 0 ? -0.5f : 0.5f,
                            (corner & 2) == 0 ? -0.5f : 0.5f, (corner & 4) == 0 ? -0.5f : 0.5f);
                        Vector3 viewport = camera.WorldToViewportPoint(marker.transform.TransformPoint(local));
                        Assert.That(viewport.x, Is.InRange(0.8f, 1f), $"corner {corner}, angle {angle}");
                        Assert.That(viewport.y, Is.InRange(0f, 0.2f), $"corner {corner}, angle {angle}");
                        Assert.That(viewport.z, Is.GreaterThan(near).And.LessThan(far));
                    }
                }
                Assert.That(marker.transform.lossyScale.x, Is.GreaterThan(0f));
            }
            finally
            {
                Object.DestroyImmediate(marker);
                Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
