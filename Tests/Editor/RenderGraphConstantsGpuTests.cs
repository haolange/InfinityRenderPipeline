using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.RenderGraph;
using Object = UnityEngine.Object;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    // GPU-RECORD: CBV_ContractConstants Tests/Editor/Shaders/RenderGraphConstantsContract.compute
    [StructLayout(LayoutKind.Sequential)]
    internal struct ContractConstants
    {
        public Vector4 vector;
        public uint count;
        public float scale;
        public uint padding0;
        public uint padding1;
    }

    public class RenderGraphConstantsGpuTests
    {
        const string ShaderPath = "Packages/com.infinity.render-pipeline/Tests/Editor/Shaders/RenderGraphConstantsContract.compute";
        static readonly int s_Constants = Shader.PropertyToID("CBV_ContractConstants");
        static readonly int s_Results = Shader.PropertyToID("ContractResults");
        static readonly Vector4 s_Vector = new Vector4(1.5f, -2.25f, 3.125f, 7);

        struct ConstantsPassData
        {
            public RGConstantsHandle constantsHandle;
            public ComputeShader shader;
            public int kernel;
            public RGBufferHandle resultsHandle;
        }

        // A separate method: an iterator cannot hold the ref local GetPassData returns.
        static void RecordCopyConstants(RGBuilder graph, ComputeShader shader, GraphicsBuffer results)
        {
            {
                RGComputePassHandle<ConstantsPassData> passHandle = graph.AddComputePass<ConstantsPassData>(new ProfilingSampler("ConstantsContract"));
                ref ConstantsPassData passData = ref passHandle.GetPassData();
                passData.shader = shader;
                passData.kernel = shader.FindKernel("CopyConstants");
                passData.constantsHandle = passHandle.WriteConstants(new ContractConstants { vector = s_Vector, count = 11, scale = 0.5f });
                passData.resultsHandle = passHandle.WriteBuffer(graph.ImportBuffer(new BufferHandle(-1, new BufferDescriptor(2, 16), results)));
                passHandle.EnablePassCulling(false);
                passHandle.SetExecuteFunc((in ConstantsPassData d, in RGComputeEncoder commands) =>
                {
                    commands.SetComputeConstantBufferParam(d.shader, s_Constants, d.constantsHandle);
                    commands.SetComputeBufferParam(d.shader, d.kernel, s_Results, d.resultsHandle);
                    commands.DispatchCompute(d.shader, d.kernel, 1, 1, 1);
                });
            }
        }

        // The record a pass writes reaches its cbuffer through the graph's pass-local constant buffer.
        [UnityTest]
        public IEnumerator WrittenConstants_ReachTheBoundCBuffer()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute shaders and GPU readback are required.");
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath));
            var pool = new ResourcePool();
            var graph = new RGBuilder("ConstantsContract", pool);
            using var results = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, 16);
            results.SetData(new Vector4[2]);
            RecordCopyConstants(graph, shader, results);
            bool executed = false;
            Exception failure = null;
            Action<ScriptableRenderContext, List<Camera>> render = (context, cameras) =>
            {
                if (executed) return;
                executed = true;
                try
                {
                    using var commands = new CommandBuffer();
                    graph.Execute(context, commands);
                    context.Submit();
                }
                catch (Exception error) { failure = error; }
            };
            EditorWindow gameView = null;
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
                if (window.GetType().FullName == "UnityEditor.GameView") { gameView = window; break; }
            RenderPipelineManager.beginContextRendering += render;
            try
            {
                double deadline = EditorApplication.timeSinceStartup + 15;
                while (!executed && EditorApplication.timeSinceStartup < deadline)
                { gameView?.Repaint(); SceneView.RepaintAll(); EditorApplication.QueuePlayerLoopUpdate(); yield return null; }
                Assert.IsTrue(executed, "No render callback executed the graph.");
                if (failure != null) throw failure;
                var readback = AsyncGPUReadback.Request(results);
                while (!readback.done) yield return null;
                Assert.IsFalse(readback.hasError);
                var values = readback.GetData<Vector4>();
                Assert.AreEqual(s_Vector, values[0]);
                Assert.AreEqual(new Vector4(11, 0.5f, 0, 0), values[1]);
            }
            finally
            {
                RenderPipelineManager.beginContextRendering -= render;
                graph.Dispose();
                pool.Dispose();
                Object.DestroyImmediate(shader);
            }
        }
    }
}
