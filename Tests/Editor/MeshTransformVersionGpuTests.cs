using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Unity.Mathematics;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class MeshTransformVersionGpuTests
    {
        static IEnumerator Produce(GpuScene gpu, ResourcePool pool, ComputeShader shader)
        {
            var graph = new RGBuilder("TransformVersionContract", pool);
            gpu.transformVersions.Record(graph, gpu.transformVersions.current, shader, new HashSet<MeshTransformVersion>(), externallyObserved: true);
            bool executed = false;
            Exception failure = null;
            Action<ScriptableRenderContext, List<Camera>> render = (context, cameras) =>
            {
                if (executed) return;
                executed = true;
                try
                {
                    using (var state = new RenderContext { scriptableRenderContext = context })
                    using (var commands = new CommandBuffer()) graph.Execute(state, commands);
                    using (var completionCommands = new CommandBuffer())
                    {
                        var tail = new ResourceReuseFence { hasGraphics = true };
                        tail.graphics = completionCommands.CreateAsyncGraphicsFence();
                        var completion = completionCommands.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.AllGPUOperations);
                        context.ExecuteCommandBuffer(completionCommands);
                        context.Submit();
                        pool.AfterSubmit(completion, tail);
                    }
                }
                catch (Exception error) { failure = error; }
            };
            RenderPipelineManager.beginContextRendering += render;
            var previousFocus = EditorWindow.focusedWindow;
            var gameViews = Resources.FindObjectsOfTypeAll<EditorWindow>();
            EditorWindow gameView = null;
            foreach (var window in gameViews)
                if (window.GetType().FullName == "UnityEditor.GameView") { gameView = window; break; }
            gameView?.Focus();
            try
            {
                double deadline = EditorApplication.timeSinceStartup + 15;
                while (!executed && EditorApplication.timeSinceStartup < deadline)
                {
                    gameView?.Repaint(); SceneView.RepaintAll(); EditorApplication.QueuePlayerLoopUpdate(); yield return null;
                }
                Assert.IsTrue(executed, "Transform producer did not receive a live rendering context.");
                if (failure != null) throw failure;
                var request = AsyncGPUReadback.Request(gpu.TransformBuffer.buffer);
                while (!request.done) yield return null;
                Assert.IsFalse(request.hasError);
                // All transform production completed before this readback. No pool fence is needed in this fixture.
                gpu.transformVersions.AfterSubmit(default);
            }
            finally
            {
                RenderPipelineManager.beginContextRendering -= render; graph.Dispose();
                if (previousFocus != null) previousFocus.Focus();
            }
        }
        static void Move(MeshScene scene, TransformId id, float x)
        {
            using (var update = scene.BeginUpdate()) { update.SetTransform(id, float4x4.Translate(new float3(x, 0, 0))); update.Commit(); }
        }
        static float4x4 ReadMatrix(MeshTransformVersion version, int index)
        {
            var result = new float4x4[1]; version.matrices.buffer.GetData(result, 0, index, 1); return result[0];
        }
        static Vector4 Previous(ComputeShader shader, MeshMotionBinding binding, int index)
        {
            using (var output = new ComputeBuffer(1, 16))
            {
                int kernel = shader.FindKernel("ReadPrevious");
                var previous = binding.previous ?? binding.current;
                shader.SetBuffer(kernel, "previousTransformBuffer", previous.matrices.buffer);
                shader.SetBuffer(kernel, "previousTransformGenerationBuffer", previous.generations.buffer);
                shader.SetBuffer(kernel, "transformGenerationBuffer", binding.current.generations.buffer);
                shader.SetInt("meshPreviousTransformValid", binding.previous != null ? 1 : 0);
                shader.SetInt("meshPreviousTransformCount", binding.previous != null ? binding.previous.count : 0);
                shader.SetInt("TestTransformIndex", index); shader.SetBuffer(kernel, "Result", output); shader.Dispatch(kernel, 1, 1, 1);
                var request = AsyncGPUReadback.Request(output); request.WaitForCompletion(); Assert.IsFalse(request.hasError);
                return request.GetData<Vector4>()[0];
            }
        }
        [UnityTest]
        public IEnumerator ImmutableVersions_ShareUpload_PreserveSparseViews_RejectReusedSlots()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback) Assert.Ignore("GPU fixture unavailable.");
            var build = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/MeshDrawPipeline/Compute_MeshDrawPipeline.compute");
            var sample = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.infinity.render-pipeline/Tests/Editor/MeshTransformVersionTests.compute");
            Assert.IsNotNull(build); Assert.IsNotNull(sample);
            using (var scene = new MeshScene(128))
            {
                var pool = new ResourcePool(); var gpu = new GpuScene(pool, scene);
                var a = new MeshViewMotionHistory(); var b = new MeshViewMotionHistory();
                try
                {
                    TransformId id, last = default;
                    using (var update = scene.BeginUpdate())
                    {
                        id = update.CreateTransform(float4x4.identity);
                        for (int i = 1; i <= 128; ++i) last = update.CreateTransform(float4x4.Translate(new float3(i, 0, 0)));
                        update.Commit();
                    }
                    gpu.Update(); var first = gpu.transformVersions.current;
                    Assert.AreEqual(Vector4.zero, Previous(sample, a.Prepare(first), (int)id.Index)); a.Rollback();
                    yield return Produce(gpu, pool, build);
                    a.Prepare(first); a.Commit(); b.Prepare(first); b.Commit();
                    Assert.AreSame(a.accepted, b.accepted);
                    long uploaded = gpu.transformVersions.uploadedBytes;
                    gpu.Update(); Assert.AreSame(first, gpu.transformVersions.current);
                    Assert.AreEqual(uploaded, gpu.transformVersions.uploadedBytes);
                    Assert.AreEqual(1, gpu.transformVersions.productionCount);
                    Move(scene, id, 2); Move(scene, last, 130); gpu.Update(); var second = gpu.transformVersions.current;
                    Assert.AreEqual(65, second.changeCount, "Only the two changed pages should be uploaded.");
                    yield return Produce(gpu, pool, build);
                    Assert.AreEqual(0, ReadMatrix(first, (int)id.Index).c3.x);
                    Assert.AreEqual(2, ReadMatrix(second, (int)id.Index).c3.x);
                    Assert.AreEqual(64, ReadMatrix(second, 64).c3.x, "Unchanged middle page must survive GPU version copy.");
                    Assert.AreEqual(130, ReadMatrix(second, (int)last.Index).c3.x);
                    Assert.AreEqual(new Vector4(2, 0, 0, 1), Previous(sample, new MeshMotionBinding(second, second), (int)id.Index));
                    Assert.AreEqual(new Vector4(0, 0, 0, 1), Previous(sample, a.Prepare(second), (int)id.Index));
                    a.Rollback(); Assert.AreSame(first, a.accepted);
                    b.Prepare(second); b.Commit(); Assert.AreSame(first, a.accepted); Assert.AreSame(second, b.accepted);
                    scene.FreeTransform(id);
                    TransformId reused;
                    using (var update = scene.BeginUpdate()) { reused = update.CreateTransform(float4x4.Translate(new float3(100, 0, 0))); update.Commit(); }
                    Assert.AreEqual(id.Index, reused.Index); Assert.AreNotEqual(id.Generation, reused.Generation);
                    gpu.Update(); var third = gpu.transformVersions.current;
                    yield return Produce(gpu, pool, build);
                    Assert.AreEqual(100, ReadMatrix(third, (int)reused.Index).c3.x);
                    Assert.AreEqual(Vector4.zero, Previous(sample, a.Prepare(third), (int)reused.Index));
                    Assert.AreEqual(Vector4.zero, Previous(sample, a.Prepare(third), third.count));
                    a.Rollback();
                }
                finally { a.Clear(); b.Clear(); gpu.Dispose(); pool.Dispose(); }
            }
        }
    }
}
