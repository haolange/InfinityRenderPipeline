using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class GpuSceneSubmissionTests
    {
        struct CopyData { public ComputeShader shader; public int kernel; public RGBufferHandle input, output; }
        struct FailureData { }
        static void Copy(RGBuilder graph, ComputeShader shader, BufferHandle input, ComputeBuffer output)
        {
            using (var pass = graph.AddComputePass<CopyData>(new ProfilingSampler("SubmissionCopy")))
            {
                ref var data = ref pass.GetPassData<CopyData>();
                data.shader = shader; data.kernel = shader.FindKernel("Copy");
                data.input = pass.ReadBuffer(graph.ImportBuffer(input));
                data.output = pass.WriteBuffer(graph.ImportBuffer(new BufferHandle(-1, new BufferDescriptor(1, 4), output)));
                pass.SetExecuteFunc((in CopyData d, in RGComputeEncoder commands) =>
                {
                    commands.SetComputeIntParam(d.shader, "Count", 1);
                    commands.SetComputeBufferParam(d.shader, d.kernel, Shader.PropertyToID("Input"), d.input);
                    commands.SetComputeBufferParam(d.shader, d.kernel, Shader.PropertyToID("Output"), d.output);
                    commands.DispatchCompute(d.shader, d.kernel, 1, 1, 1);
                });
            }
        }
        static MeshInstanceId Create(MeshSceneUpdate update)
        {
            var transform = update.CreateTransform(float4x4.identity);
            return update.CreateInstance(transform, new FBound(float3.zero, new float3(1)), ~0, 1,
                EMeshInstanceFlags.Visible, EMotionType.Object, ECastShadowMethod.Off);
        }
        [UnityTest]
        public IEnumerator TwoCameras_DelayedSubmit_KeepCpuUploadsIndependent() => Run(false, false);
        [UnityTest]
        public IEnumerator RecordedCamera_DirtyUploadBeforeExecute_PreservesFrozenInput() => Run(false, false, true);
        [UnityTest]
        public IEnumerator FailedGraph_ThenGrowthBeforeSubmit_PreservesQueuedInputs() => Run(true, true);

        [UnityTest]
        public IEnumerator DisposalAfterSubmit_DefersPhysicalReleaseUntilCompletion()
        {
            if (!SystemInfo.supportsAsyncGPUReadback || !SystemInfo.supportsGraphicsFence)
                Assert.Ignore("GPU readback and CPU-queryable fences are required.");
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.infinity.render-pipeline/Tests/Editor/RenderGraphHazard.compute");
            var pool = new ResourcePool();
            var graph = new RGBuilder("DisposeWhileSubmitted", pool);
            var source = pool.GetBuffer(new BufferDescriptor(1, 4));
            source.buffer.SetData(new[] { 37u });
            using var result = new ComputeBuffer(1, 4);
            Copy(graph, shader, source, result);
            bool executed = false;
            Exception failure = null;
            Action<ScriptableRenderContext, List<Camera>> render = (context, cameras) =>
            {
                if (executed) return;
                executed = true;
                try
                {
                    using var state = new RenderContext { scriptableRenderContext = context };
                    using var commands = new CommandBuffer();
                    graph.Execute(state, commands);
                    pool.ReleaseBuffer(source);
                    var tail = new ResourceReuseFence { hasGraphics = true, graphics = commands.CreateAsyncGraphicsFence() };
                    var completion = commands.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.AllGPUOperations);
                    context.ExecuteCommandBuffer(commands); context.Submit();
                    pool.AfterSubmit(completion, tail);
                    bool incomplete = !completion.passed;
                    pool.Dispose();
                    TestContext.WriteLine("Disposal observed an incomplete submission: " + incomplete);
                    if (!completion.passed) Assert.IsTrue(source.buffer.IsValid(), "Disposal destroyed an in-flight allocation.");
                }
                catch (Exception error) { failure = error; }
            };
            var previousFocus = EditorWindow.focusedWindow;
            EditorWindow gameView = null;
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
                if (window.GetType().FullName == "UnityEditor.GameView") { gameView = window; break; }
            RenderPipelineManager.beginContextRendering += render;
            gameView?.Focus();
            try
            {
                double deadline = EditorApplication.timeSinceStartup + 15;
                while (!executed && EditorApplication.timeSinceStartup < deadline)
                { gameView?.Repaint(); SceneView.RepaintAll(); EditorApplication.QueuePlayerLoopUpdate(); yield return null; }
                Assert.IsTrue(executed);
                if (failure != null) throw failure;
                var readback = AsyncGPUReadback.Request(result);
                while (!readback.done) yield return null;
                Assert.IsFalse(readback.hasError); Assert.AreEqual(37u, readback.GetData<uint>()[0]);
                pool.Dispose();
                Assert.IsFalse(source.buffer.IsValid(), "Completed disposal did not release the physical allocation.");
            }
            finally
            {
                RenderPipelineManager.beginContextRendering -= render;
                graph.Dispose(); pool.Dispose();
                if (previousFocus != null) previousFocus.Focus();
            }
        }

        static IEnumerator Run(bool fail, bool grow, bool delayExecute = false)
        {
            if (!SystemInfo.supportsAsyncGPUReadback || !SystemInfo.supportsGraphicsFence)
                Assert.Ignore("GPU readback and CPU-queryable fences are required.");
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.infinity.render-pipeline/Tests/Editor/RenderGraphHazard.compute");
            Assert.IsNotNull(shader);
            var pool = new ResourcePool();
            using var scene = new MeshScene(16);
            var gpu = new GpuScene(pool, scene);
            var first = new RGBuilder("FirstCameraBeforeSubmit", pool);
            var second = new RGBuilder("SecondCameraBeforeSubmit", pool);
            using var firstResult = new ComputeBuffer(1, 4);
            using var secondResult = new ComputeBuffer(1, 4);
            MeshInstanceId instance;
            using (var update = scene.BeginUpdate()) { instance = Create(update); update.Commit(); }
            gpu.Update();
            var original = gpu.InstanceFlagsBuffer;
            Copy(first, shader, original, firstResult);
            pool.EnsureRecording();
            int firstEpoch = pool.recordingEpoch;
            Assert.IsFalse(pool.CanWriteBuffer(original), "Recording, before Execute, must already prohibit CPU writes.");
            if (fail)
                using (var pass = first.AddComputePass<FailureData>(new ProfilingSampler("InjectedFailureAfterCopy")))
                {
                    pass.EnablePassCulling(false);
                    pass.SetExecuteFunc((in FailureData data, in RGComputeEncoder commands) =>
                        throw new InvalidOperationException("Expected submission fixture failure."));
                }
            bool executed = false;
            Exception failure = null;
            Action<ScriptableRenderContext, List<Camera>> render = (context, cameras) =>
            {
                if (executed) return;
                executed = true;
                try
                {
                    using var state = new RenderContext { scriptableRenderContext = context };
                    using var commands = new CommandBuffer();
                    if (fail)
                        Assert.That(() => first.Execute(state, commands), Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("Expected submission fixture failure."));
                    else if (!delayExecute) first.Execute(state, commands);
                    Assert.IsFalse(pool.CanWriteBuffer(original), "Execute is not Submit/completion.");
                    pool.BeginFrame(); // Simulate a CPU frame transition while the context is still unsubmitted.
                    using (var update = scene.BeginUpdate())
                    {
                        update.SetInstanceFlags(instance, EMeshInstanceFlags.None);
                        if (grow) for (int i = 0; i < 64; ++i) Create(update);
                        update.Commit();
                    }
                    gpu.Update();
                    Assert.AreNotSame(original.buffer, gpu.InstanceFlagsBuffer.buffer, "Dirty or expanded tables require a separate CPU-writable version.");
                    Assert.IsFalse(pool.BufferUsage(original.buffer).CanWriteCpu, "Returning the old table without an explicit fence must preserve its queued use.");
                    var probe = pool.GetBuffer(original.descriptor);
                    Assert.AreNotSame(original.buffer, probe.buffer, "The no-fence return path must not become a CPU acquisition.");
                    probe.buffer.SetData(new uint[probe.descriptor.count]);
                    pool.ReleaseBuffer(probe);
                    Copy(second, shader, gpu.InstanceFlagsBuffer, secondResult);
                    if (delayExecute) first.Execute(state, commands);
                    second.Execute(state, commands);
                    Assert.IsFalse(pool.CanWriteBuffer(gpu.InstanceFlagsBuffer));
                    var tail = new ResourceReuseFence { hasGraphics = true };
                    tail.graphics = commands.CreateAsyncGraphicsFence();
                    var completion = commands.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.AllGPUOperations);
                    context.ExecuteCommandBuffer(commands);
                    context.Submit();
                    // This exact context Submit contains both batches. A different context's
                    // acknowledgement must not release the older batch implicitly.
                    pool.AcknowledgeSubmission(firstEpoch, completion, tail);
                    pool.AfterSubmit(completion, tail);
                    bool completedImmediately = completion.passed;
                    TestContext.WriteLine("Completion observed immediately after Submit: " + completedImmediately);
                    if (!completedImmediately) Assert.IsFalse(pool.CanWriteBuffer(gpu.InstanceFlagsBuffer));
                }
                catch (Exception error) { failure = error; }
            };
            var previousFocus = EditorWindow.focusedWindow;
            EditorWindow gameView = null;
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
                if (window.GetType().FullName == "UnityEditor.GameView") { gameView = window; break; }
            RenderPipelineManager.beginContextRendering += render;
            gameView?.Focus();
            try
            {
                double deadline = EditorApplication.timeSinceStartup + 15;
                while (!executed && EditorApplication.timeSinceStartup < deadline)
                { gameView?.Repaint(); SceneView.RepaintAll(); EditorApplication.QueuePlayerLoopUpdate(); yield return null; }
                Assert.IsTrue(executed, "No live rendering context was available.");
                if (failure != null) throw failure;
                var a = AsyncGPUReadback.Request(firstResult);
                var b = AsyncGPUReadback.Request(secondResult);
                while (!a.done || !b.done) yield return null;
                Assert.IsFalse(a.hasError || b.hasError);
                Assert.AreEqual((uint)EMeshInstanceFlags.Visible, a.GetData<uint>()[0], "The first camera's queued input was overwritten.");
                Assert.AreEqual(0u, b.GetData<uint>()[0]);
                Assert.IsTrue(pool.BufferUsage(original.buffer).CanWriteCpu, "The earlier submission is CPU-safe after its completion fence and ordered readback finish.");
            }
            finally
            {
                RenderPipelineManager.beginContextRendering -= render;
                first.Dispose(); second.Dispose(); gpu.Dispose(); pool.Dispose();
                if (previousFocus != null) previousFocus.Focus();
            }
        }
    }
}
