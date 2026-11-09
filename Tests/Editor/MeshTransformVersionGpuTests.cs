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
using InfinityTech.Rendering.MeshPipeline.Tests;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class MeshTransformVersionGpuTests
    {
        static IEnumerator Produce(MeshWorld world, ResourcePool pool, ComputeShader shader)
        {
            var graph = new RGBuilder("TransformVersionContract", pool);
            // The render context owns its own mesh world; keep its allocations out of the pool under test.
            var contextPool = new ResourcePool();
            world.transformVersions.Record(graph, world.transformVersions.current, shader, new HashSet<MeshTransformVersion>(), externallyObserved: true);
            bool executed = false;
            Exception failure = null;
            Action<ScriptableRenderContext, List<Camera>> render = (context, cameras) =>
            {
                if (executed) return;
                executed = true;
                try
                {
                    using (var state = new RenderContext(contextPool, shader) { scriptableRenderContext = context })
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
                var request = AsyncGPUReadback.Request(world.transformVersions.current.matrices.buffer);
                while (!request.done) yield return null;
                Assert.IsFalse(request.hasError);
                // All transform production completed before this readback. No pool fence is needed in this fixture.
                world.transformVersions.AfterSubmit(default);
            }
            finally
            {
                RenderPipelineManager.beginContextRendering -= render; graph.Dispose(); contextPool.Dispose();
                if (previousFocus != null) previousFocus.Focus();
            }
        }
        static void Move(MeshWorld world, MeshEntityId id, float x)
            => world.WriteEntityPose(id, float4x4.Translate(new float3(x, 0, 0)), MeshTestUtility.Bound(new float3(x, 0, 0)));
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
                shader.SetBuffer(kernel, "PreviousEntityTransforms", previous.matrices.buffer);
                shader.SetBuffer(kernel, "PreviousEntityGenerations", previous.generations.buffer);
                shader.SetBuffer(kernel, "EntityGenerations", binding.current.generations.buffer);
                shader.SetInt("PreviousEntityValid", binding.previous != null ? 1 : 0);
                shader.SetInt("PreviousEntityCount", binding.previous != null ? binding.previous.count : 0);
                shader.SetInt("TestEntityIndex", index); shader.SetBuffer(kernel, "Result", output); shader.Dispatch(kernel, 1, 1, 1);
                var request = AsyncGPUReadback.Request(output); request.WaitForCompletion(); Assert.IsFalse(request.hasError);
                return request.GetData<Vector4>()[0];
            }
        }
        [UnityTest]
        public IEnumerator ImmutableVersions_ShareUpload_PreserveSparseViews_RejectReusedSlots()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback) Assert.Ignore("GPU fixture unavailable.");
            var build = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.infinity.render-pipeline/Shaders/Mesh/Compute_MeshDrawPipeline.compute");
            var sample = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.infinity.render-pipeline/Tests/Editor/MeshTransformVersionTests.compute");
            if (build == null) Assert.Ignore("Compute_MeshDrawPipeline is not available.");
            Assert.IsNotNull(sample);
            var pool = new ResourcePool(); var world = new MeshWorld(pool, build);
            var a = new MeshViewMotionHistory(); var b = new MeshViewMotionHistory();
            try
            {
                MeshEntityId id, last = default;
                using (var update = world.BeginUpdate())
                {
                    id = MeshTestUtility.CreateEntity(update, float3.zero);
                    for (int i = 1; i <= 128; ++i) last = MeshTestUtility.CreateEntity(update, new float3(i, 0, 0));
                    update.Commit();
                }
                world.UpdateResidency(); var first = world.transformVersions.current;
                Assert.AreEqual(Vector4.zero, Previous(sample, a.Prepare(first), (int)id.Index)); a.Rollback();
                yield return Produce(world, pool, build);
                a.Prepare(first); a.Commit(); b.Prepare(first); b.Commit();
                Assert.AreSame(a.accepted, b.accepted);
                long uploaded = world.transformVersions.uploadedBytes;
                world.UpdateResidency(); Assert.AreSame(first, world.transformVersions.current);
                Assert.AreEqual(uploaded, world.transformVersions.uploadedBytes);
                Assert.AreEqual(1, world.transformVersions.productionCount);
                Move(world, id, 2); Move(world, last, 130); world.UpdateResidency(); var second = world.transformVersions.current;
                Assert.AreEqual(65, second.changeCount, "Only the two changed pages should be uploaded.");
                yield return Produce(world, pool, build);
                Assert.AreEqual(0, ReadMatrix(first, (int)id.Index).c3.x);
                Assert.AreEqual(2, ReadMatrix(second, (int)id.Index).c3.x);
                Assert.AreEqual(64, ReadMatrix(second, 64).c3.x, "Unchanged middle page must survive GPU version copy.");
                Assert.AreEqual(130, ReadMatrix(second, (int)last.Index).c3.x);
                Assert.AreEqual(new Vector4(2, 0, 0, 1), Previous(sample, new MeshMotionBinding(second, second), (int)id.Index));
                Assert.AreEqual(new Vector4(0, 0, 0, 1), Previous(sample, a.Prepare(second), (int)id.Index));
                a.Rollback(); Assert.AreSame(first, a.accepted);
                b.Prepare(second); b.Commit(); Assert.AreSame(first, a.accepted); Assert.AreSame(second, b.accepted);
                using (var update = world.BeginUpdate()) { update.RemoveEntity(id); update.Commit(); }
                MeshEntityId reused;
                using (var update = world.BeginUpdate()) { reused = MeshTestUtility.CreateEntity(update, new float3(100, 0, 0)); update.Commit(); }
                Assert.AreEqual(id.Index, reused.Index); Assert.AreNotEqual(id.Generation, reused.Generation);
                world.UpdateResidency(); var third = world.transformVersions.current;
                yield return Produce(world, pool, build);
                Assert.AreEqual(100, ReadMatrix(third, (int)reused.Index).c3.x);
                Assert.AreEqual(Vector4.zero, Previous(sample, a.Prepare(third), (int)reused.Index));
                Assert.AreEqual(Vector4.zero, Previous(sample, a.Prepare(third), third.count));
                a.Rollback();
            }
            finally { a.Clear(); b.Clear(); world.Dispose(); pool.Dispose(); }
        }
    }
}
