using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class RenderGraphQueueGpuTests
    {
        const int Count = 4096;
        struct Data
        {
            internal ComputeShader shader;
            internal int kernel, value;
            internal RGBufferRef input, output;
            internal List<ComputeBuffer> physical;
        }

        static void Add(RGBuilder graph, ComputeShader shader, RGBufferRef input, RGBufferRef output,
            bool async, int value, List<ComputeBuffer> physical = null)
        {
            bool copy = input.IsValid();
            using (var pass = graph.AddComputePass<Data>(new ProfilingSampler(copy ? "HazardCopy" : "HazardWrite")))
            {
                pass.EnableAsyncCompute(async);
                pass.EnablePassCulling(false);
                ref Data data = ref pass.GetPassData<Data>();
                data.shader = shader;
                data.kernel = shader.FindKernel(copy ? "Copy" : "Write");
                data.value = value;
                data.physical = physical;
                data.input = copy ? pass.ReadBuffer(input) : default;
                data.output = pass.WriteBuffer(output);
                pass.SetExecuteFunc((in Data d, in RGComputeEncoder commands, RGObjectPool objects) =>
                {
                    d.physical?.Add(d.output);
                    commands.SetComputeIntParam(d.shader, "Count", Count);
                    commands.SetComputeIntParam(d.shader, "Value", d.value);
                    if (d.input.IsValid()) commands.SetComputeBufferParam(d.shader, d.kernel, Shader.PropertyToID("Input"), d.input);
                    commands.SetComputeBufferParam(d.shader, d.kernel, Shader.PropertyToID("Output"), d.output);
                    commands.DispatchCompute(d.shader, d.kernel, Count / 64, 1, 1);
                });
            }
        }

        [UnityTest]
        public IEnumerator GraphicsQueueHazardsAndPhysicalReuse_PreserveValues() => Run(false);

        [UnityTest]
        public IEnumerator CrossQueueHazardsAndPhysicalReuse_PreserveValues()
        {
            if (!SystemInfo.supportsAsyncCompute) Assert.Ignore("This GPU/API does not support async compute.");
            yield return Run(true);
        }

        static IEnumerator Run(bool async)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("This GPU/API cannot execute the fixture.");
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.infinity.render-pipeline/Tests/Editor/RenderGraphHazard.compute");
            Assert.IsNotNull(shader);
            var pool = new ResourcePool();
            var graph = new RGBuilder("QueueGpuContract", pool);
            var outputs = new ComputeBuffer[4];
            var physical = new List<ComputeBuffer>();
            var shared = new ComputeBuffer(Count, sizeof(uint));
            var descriptor = new BufferDescriptor(Count, sizeof(uint));
            var initial = new uint[Count];
            Array.Fill(initial, 1u);
            shared.SetData(initial);
            var imported = graph.ImportBuffer(new FBufferRef(-1, descriptor, shared));
            var result = new RGBufferRef[outputs.Length];
            for (int i = 0; i < outputs.Length; ++i)
            {
                outputs[i] = new ComputeBuffer(Count, sizeof(uint));
                result[i] = graph.ImportBuffer(new FBufferRef(-1, descriptor, outputs[i]));
            }
            Add(graph, shader, imported, result[0], async, 0);
            Add(graph, shader, default, imported, false, 7); // WAR
            Add(graph, shader, imported, result[1], async, 0); // RAW
            Add(graph, shader, default, imported, false, 11); // WAR
            Add(graph, shader, imported, result[2], false, 0);
            var a = graph.CreateBuffer(descriptor);
            Add(graph, shader, default, a, async, 3, physical);
            Add(graph, shader, a, result[3], async, 0);
            var b = graph.CreateBuffer(descriptor);
            Add(graph, shader, default, b, false, 5, physical); // Same physical allocation, different logical resource.
            bool executed = false;
            Exception failure = null;
            Action<ScriptableRenderContext, List<Camera>> render = null;
            render = (context, cameras) =>
            {
                if (executed) return;
                executed = true;
                try
                {
                    using (var state = new RenderContext { scriptableRenderContext = context })
                    using (var commands = new CommandBuffer()) graph.Execute(state, pool, commands);
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
            EditorWindow gameView = null;
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
                if (window.GetType().FullName == "UnityEditor.GameView") { gameView = window; break; }
            gameView?.Focus();
            try
            {
                double deadline = EditorApplication.timeSinceStartup + 15;
                while (!executed && EditorApplication.timeSinceStartup < deadline)
                {
                    gameView?.Repaint();
                    SceneView.RepaintAll();
                    EditorApplication.QueuePlayerLoopUpdate();
                    yield return null;
                }
                Assert.IsTrue(executed, "No real rendering context was available.");
                if (failure != null) throw failure;
                Assert.AreEqual(2, physical.Count);
                Assert.AreSame(physical[0], physical[1], "Fixture must actually exercise physical pool aliasing.");
                uint[] expected = { 1, 7, 11, 3 };
                for (int i = 0; i < outputs.Length; ++i)
                {
                    var request = AsyncGPUReadback.Request(outputs[i]);
                    while (!request.done) yield return null;
                    Assert.IsFalse(request.hasError);
                    foreach (uint value in request.GetData<uint>()) Assert.AreEqual(expected[i], value, "Output " + i);
                }
            }
            finally
            {
                RenderPipelineManager.beginContextRendering -= render;
                graph.Dispose();
                pool.Dispose();
                shared.Release();
                foreach (var output in outputs) output.Release();
                if (previousFocus != null) previousFocus.Focus();
            }
        }
    }
}
