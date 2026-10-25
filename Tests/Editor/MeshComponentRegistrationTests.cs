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

        static void FlushOwnMeshTasks(int start, RenderContext context)
        {
            var tasks = FGraphics.MeshTasks.GetRange(start, FGraphics.MeshTasks.Count - start).ToArray();
            FGraphics.MeshTasks.RemoveRange(start, tasks.Length);
            foreach (var task in tasks) FGraphics.ProcessMeshTask(task, context);
        }

        static void FlushOwnFrame(int start, int meshStart, RenderContext context, MeshComponent component)
        {
            foreach (var task in TakeOwnTasks(start)) task?.Invoke(context);
            FlushOwnMeshTasks(meshStart, context);
            // Registration joins the world before dirty synchronization.
            // Drive those phases without draining another Editor object's queued work.
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
            int meshStart = FGraphics.MeshTasks.Count;
            var meshPrefix = FGraphics.MeshTasks.ToArray();
            var prefix = FGraphics.GraphicsTasks.ToArray();
            try
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.SetActive(false);
                var mesh = go.GetComponent<MeshFilter>().sharedMesh;
                material = new Material(Shader.Find("InfinityPipeline/InfinityLit-Instanced")) { enableInstancing = true };
                go.GetComponent<Renderer>().sharedMaterial = material;
                component = go.AddComponent<MeshComponent>();
                component.MeshAsset = mesh;
                component.SetMaterials(new[] { material });
                component.Mobility = mobilityChange == 1 ? EStateType.Static : EStateType.Dynamic;
                component.CastShadow = ECastShadowMethod.Dynamic;
                go.SetActive(true);
                if (initiallyRegistered)
                    FlushOwnFrame(start, meshStart, context, component);
                if (deactivateObject) go.SetActive(false);
                else component.enabled = false;
                if (mobilityChange != 0) component.Mobility = mobilityChange == 1 ? EStateType.Dynamic : EStateType.Static;
                if (reenable) go.SetActive(true);
                FlushOwnFrame(start, meshStart, context, component);
                var scene = context.GetMeshScene();
                Assert.AreEqual(reenable ? 1 : 0, scene.LogicalInstanceCount,
                    "An enable queued before disable cannot resurrect an inactive renderer.");
                Assert.AreEqual(reenable ? 1 : 0, scene.TransformCount);
                Assert.AreEqual(reenable ? 1 : 0, scene.DrawCount);
                Assert.AreEqual(reenable && component.Mobility == EStateType.Dynamic ? 1 : 0, context.GetWorldDynamicPrimitive().Count);
                Assert.AreEqual(reenable && component.Mobility == EStateType.Static ? 1 : 0, context.GetWorldStaticMesh().Count);
                CollectionAssert.AreEqual(prefix, FGraphics.GraphicsTasks);
                Assert.IsFalse(go.GetComponent<Renderer>().forceRenderingOff, "Custom registration must not mutate native rendering state.");
            }
            finally
            {
                try
                {
                    UnityEngine.Object.DestroyImmediate(go);
                    foreach (var task in TakeOwnTasks(start)) task?.Invoke(context);
                    FlushOwnMeshTasks(meshStart, context);
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
                    var remainingMeshTasks = FGraphics.MeshTasks.ToArray();
                    var remainingDirty = dirty.Where(value => !ReferenceEquals(value, component)).ToArray();
                    try { context.Dispose(); }
                    finally
                    {
                        FGraphics.GraphicsTasks.InsertRange(0, remainingTasks);
                        FGraphics.MeshTasks.InsertRange(0, remainingMeshTasks);
                        dirty.InsertRange(0, remainingDirty);
                    }
                    CollectionAssert.AreEqual(prefix, FGraphics.GraphicsTasks.Take(start));
                    CollectionAssert.AreEqual(meshPrefix, FGraphics.MeshTasks.Take(meshStart));
                }
            }
        }
    }
}
