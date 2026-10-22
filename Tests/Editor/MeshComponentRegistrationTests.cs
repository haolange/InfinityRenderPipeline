using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using InfinityTech.Component;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class MeshComponentRegistrationTests
    {
        // Run only the delegates appended by this test. Existing Editor tasks remain untouched.
        static FGraphicsTask[] TakeOwnTasks(int start)
        {
            int count = FGraphics.GraphicsTasks.Count - start;
            Assert.GreaterOrEqual(count, 0);
            var tasks = FGraphics.GraphicsTasks.GetRange(start, count).ToArray();
            FGraphics.GraphicsTasks.RemoveRange(start, count);
            return tasks;
        }

        static void FlushOwnFrame(int start, RenderContext context, MeshComponent component)
        {
            foreach (var task in TakeOwnTasks(start)) task?.Invoke(context);
            // Registration now joins the world first; the real pipeline batches probes before dirty sync.
            // Drive those phases without draining another Editor object's queued work.
            context.InterpolateMeshProbes();
            var dirty = (System.Collections.Generic.List<MeshComponent>)typeof(RenderContext).GetField(
                "s_DirtyMeshList", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
            if (dirty.RemoveAll(value => ReferenceEquals(value, component)) > 0)
                component.SyncFromSnapshot(context);
        }

        [TestCase(false, false, false, 0)]
        [TestCase(true, false, false, 0)]
        [TestCase(true, true, false, 0)]
        [TestCase(true, true, true, 0)]
        [TestCase(true, true, false, 1)]
        [TestCase(true, true, false, -1)]
        public void DeferredRegistration_RespectsFinalActiveState(bool deactivateObject, bool reenable, bool initiallyRegistered, int mobilityChange)
        {
            GameObject go = null;
            Material material = null;
            var context = new RenderContext();
            MeshComponent component = null;
            int start = FGraphics.GraphicsTasks.Count;
            var prefix = FGraphics.GraphicsTasks.ToArray();
            try
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.SetActive(false);
                var mesh = go.GetComponent<MeshFilter>().sharedMesh;
                material = new Material(Shader.Find("InfinityPipeline/InfinityLit-Instanced")) { enableInstancing = true };
                go.GetComponent<Renderer>().sharedMaterial = material;
                component = go.AddComponent<MeshComponent>();
                component.meshAsset = mesh;
                component.materials = new[] { material };
                component.movebility = mobilityChange == 1 ? EStateType.Static : EStateType.Dynamic;
                component.castShadow = ECastShadowMethod.Dynamic;
                go.SetActive(true);
                if (initiallyRegistered)
                    FlushOwnFrame(start, context, component);
                if (deactivateObject) go.SetActive(false);
                else component.enabled = false;
                if (mobilityChange != 0) component.movebility = mobilityChange == 1 ? EStateType.Dynamic : EStateType.Static;
                if (reenable) go.SetActive(true);
                FlushOwnFrame(start, context, component);
                var scene = context.GetMeshScene();
                Assert.AreEqual(reenable ? 1 : 0, scene.LogicalInstanceCount,
                    "An enable queued before disable cannot resurrect an inactive renderer.");
                Assert.AreEqual(reenable ? 1 : 0, scene.TransformCount);
                Assert.AreEqual(reenable ? 1 : 0, scene.DrawCount);
                Assert.AreEqual(reenable && component.movebility == EStateType.Dynamic ? 1 : 0, context.GetWorldDynamicPrimitive().Count);
                Assert.AreEqual(reenable && component.movebility == EStateType.Static ? 1 : 0, context.GetWorldStaticMesh().Count);
                CollectionAssert.AreEqual(prefix, FGraphics.GraphicsTasks);
                Assert.AreEqual(!reenable, !go.GetComponent<Renderer>().forceRenderingOff);
            }
            finally
            {
                try
                {
                    UnityEngine.Object.DestroyImmediate(go);
                    foreach (var task in TakeOwnTasks(start)) task?.Invoke(context);
                    CollectionAssert.AreEqual(prefix, FGraphics.GraphicsTasks);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(material);
                    // RenderContext teardown clears process queues. Preserve external work, including its order,
                    // and remove only this destroyed component from the project-owned dirty list.
                    var dirty = (System.Collections.Generic.List<MeshComponent>)typeof(RenderContext).GetField(
                        "s_DirtyMeshList", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
                    var remainingTasks = FGraphics.GraphicsTasks.ToArray();
                    var remainingDirty = dirty.Where(value => !ReferenceEquals(value, component)).ToArray();
                    try { context.Dispose(); }
                    finally
                    {
                        FGraphics.GraphicsTasks.InsertRange(0, remainingTasks);
                        dirty.InsertRange(0, remainingDirty);
                    }
                    CollectionAssert.AreEqual(prefix, FGraphics.GraphicsTasks.Take(start));
                }
            }
        }
    }
}
