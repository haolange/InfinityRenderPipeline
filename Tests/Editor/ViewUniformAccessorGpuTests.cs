using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    // The accessors in ViewUniform.hlsl (ViewPositionWS, ViewForwardWS, ViewIsOrthographic) read the camera from the
    // matrices CameraUniform uploads. The matrix comes from a real Camera, built the way CameraUniform builds it.
    public class ViewUniformAccessorGpuTests
    {
        const string ShaderPath = "Packages/com.infinity.render-pipeline/Tests/Editor/Shaders/ViewUniformContract.compute";

        [Test]
        public void Accessors_ReadTheCameraFromItsMatrices([Values(false, true)] bool orthographic)
        {
            if (!SystemInfo.supportsComputeShaders || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Compute shaders are required.");
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath));
            var cameraObject = new GameObject("ViewUniformAccessorCamera");
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = orthographic;
                cameraObject.transform.SetPositionAndRotation(new Vector3(8, -1.25f, 0.6f), Quaternion.Euler(10, 35, 0));
                Matrix4x4 viewToWorld = camera.worldToCameraMatrix.inverse;
                using var view = new UniformBinding<ViewUniform>(new ViewUniform
                {
                    matrix_WorldToView = camera.worldToCameraMatrix,
                    matrix_ViewToWorld = viewToWorld,
                    view_DepthParameters = new Vector4(camera.nearClipPlane, camera.farClipPlane, orthographic ? 1 : 0, 1)
                });
                using var results = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 5, 16);
                results.SetData(new Vector4[5]);
                int kernel = shader.FindKernel("ReadView");
                shader.SetBuffer(kernel, "ContractResults", results);
                shader.Dispatch(kernel, 1, 1, 1);
                var values = new Vector4[5];
                results.GetData(values);

                Vector3 position = cameraObject.transform.position, forward = cameraObject.transform.forward;
                AssertNear(position, values[3], "mul(Matrix_ViewToWorld, origin): the matrix itself does not hold the camera position");
                AssertNear(position, values[0], "ViewPositionWS");
                AssertNear(forward, values[4], "mul of the view axis: the matrix itself does not hold the camera forward");
                AssertNear(forward, values[1], "ViewForwardWS");
                Assert.AreEqual(orthographic ? 1f : 0f, values[2].x, "ViewIsOrthographic");
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(shader);
            }
        }

        static void AssertNear(Vector3 expected, Vector4 actual, string what)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(1e-4f), $"{what}: expected {expected}, read {actual}");
        }
    }
}
