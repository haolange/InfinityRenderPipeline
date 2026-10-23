using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using InfinityTech.Component;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class LightAngularGpuTests
    {
        const string k_Shader = "Packages/com.infinity.render-pipeline/Tests/Editor/Shaders/LightAngularContract.compute";

        static Vector2[] Evaluate(FLightRecord record, Vector4[] positions)
        {
            if (!SystemInfo.supportsComputeShaders || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("A compute graphics device is required for the angular contract.");
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(k_Shader);
            Assert.IsNotNull(shader, k_Shader);
            using (var records = new ComputeBuffer(1, Marshal.SizeOf<FLightRecord>()))
            using (var points = new ComputeBuffer(positions.Length, 16))
            using (var results = new ComputeBuffer(positions.Length, 8))
            {
                records.SetData(new[] { record });
                points.SetData(positions);
                int kernel = shader.FindKernel("LightAngularContract");
                shader.SetBuffer(kernel, "Records", records);
                shader.SetBuffer(kernel, "Positions", points);
                shader.SetBuffer(kernel, "Results", results);
                shader.Dispatch(kernel, positions.Length, 1, 1);
                var output = new Vector2[positions.Length];
                results.GetData(output);
                return output;
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PackedCone_UsesSmoothAngularWindowOnGpu(bool transformed)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject go = null;
            try
            {
                go = EditorUtility.CreateGameObjectWithHideFlags("AngularContract", HideFlags.DontSave, typeof(Light));
                SceneManager.MoveGameObjectToScene(go, preview);
                Light light = go.GetComponent<Light>();
                var data = go.GetComponent<InfinityAdditionalLightData>() ?? go.AddComponent<InfinityAdditionalLightData>();
                data.SetShape(FLightShape.Default(EInfinityLightType.Spot));
                light.spotAngle = 60;
                light.innerSpotAngle = 20;
                light.range = 30;
                data.maxDrawDistance = 0;
                if (transformed)
                    go.transform.SetPositionAndRotation(new Vector3(3, -2, 5), Quaternion.Euler(23, 61, -17));
                Assert.IsTrue(FLightRecordPack.TryPack(light, data, data.shape, 0, Vector3.zero, out FLightRecord record));

                // Include transition samples with known window values, not only the saturated cone interior.
                float outer = Mathf.Cos(30 * Mathf.Deg2Rad), inner = Mathf.Cos(10 * Mathf.Deg2Rad);
                float[] windows = { 1, 1, 0.75f, 0.5f, 0.25f, 0, 0 };
                float[] cosines = { 1, inner, Mathf.Lerp(outer, inner, 0.75f), Mathf.Lerp(outer, inner, 0.5f),
                    Mathf.Lerp(outer, inner, 0.25f), outer, Mathf.Cos(31 * Mathf.Deg2Rad) };
                var positions = new Vector4[cosines.Length];
                for (int i = 0; i < positions.Length; ++i)
                {
                    float c = cosines[i];
                    positions[i] = go.transform.TransformPoint(new Vector3(Mathf.Sqrt(1 - c * c), 0, c) * 4);
                }
                Vector2[] actual = Evaluate(record, positions);
                for (int i = 0; i < actual.Length; ++i)
                {
                    Assert.AreEqual(windows[i] * windows[i], actual[i].y, 2e-5f, "CoreRP reference " + i);
                    Assert.AreEqual(actual[i].y, actual[i].x, 2e-5f, "Production angular window " + i);
                }
            }
            finally
            {
                if (go != null)
                {
                    Undo.FlushUndoRecordObjects();
                    foreach (UnityEngine.Component component in go.GetComponents<UnityEngine.Component>()) Undo.ClearUndo(component);
                    Undo.ClearUndo(go);
                    Object.DestroyImmediate(go);
                }
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        [Test]
        public void PointPyramidAndBox_RetainTheirAngularSupportOnGpu()
        {
            Vector4[] positions = { new Vector4(0.5f, 0.5f, 4, 0), new Vector4(3, 0, 4, 0),
                new Vector4(0, 3, 4, 0), new Vector4(0, 0, -1, 0) };
            var record = new FLightRecord { positionRange = new Vector4(0, 0, 0, 30),
                forward = new Vector4(0, 0, 1, 0), right = new Vector4(1, 0, 0, 0) };
            foreach (Vector2 result in Evaluate(record, positions)) Assert.AreEqual(1, result.x);
            record.flags = FLightRecordFlags.SpotPyramid;
            record.area = new Vector4(Mathf.Tan(30 * Mathf.Deg2Rad), Mathf.Tan(30 * Mathf.Deg2Rad), 0, 0);
            Vector2[] pyramid = Evaluate(record, positions);
            record.flags = FLightRecordFlags.SpotBox;
            record.area = new Vector4(2, 4, 0, 0);
            Vector2[] box = Evaluate(record, positions);
            for (int i = 0; i < positions.Length; ++i)
            {
                Assert.AreEqual(i == 0 ? 1 : 0, pyramid[i].x, "Pyramid " + i);
                Assert.AreEqual(i == 0 ? 1 : 0, box[i].x, "Box " + i);
            }
        }
    }
}
