using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using InfinityTech.Component;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.MeshPipeline.Tests;

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

        static System.Collections.Generic.List<MeshComponent> DirtyList() => (System.Collections.Generic.List<MeshComponent>)typeof(RenderContext).GetField(
            "s_DirtyMeshList", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);

        static void FlushOwnFrame(int start, int meshStart, RenderContext context, MeshComponent component)
        {
            foreach (var task in TakeOwnTasks(start)) task?.Invoke(context);
            FlushOwnMeshTasks(meshStart, context);
            // Registration joins the world before dirty synchronization. Drain this context's removals and only this
            // component's dirty entry through the frame transaction; another Editor object's queued work is restored.
            var dirty = DirtyList();
            var others = dirty.Where(value => !ReferenceEquals(value, component)).ToArray();
            bool own = others.Length != dirty.Count;
            dirty.Clear();
            if (own) dirty.Add(component);
            try { context.DrainDirtyMeshes(); }
            finally
            {
                dirty.Clear();
                dirty.AddRange(others);
            }
        }

        [TestCase(false, false, false, 0)]
        [TestCase(true, false, false, 0)]
        [TestCase(true, true, false, 0)]
        [TestCase(true, true, true, 0)]
        [TestCase(true, true, false, 1)]
        [TestCase(true, true, false, -1)]
        public void DeferredRegistration_RespectsFinalActiveState(bool deactivateObject, bool reenable, bool initiallyRegistered, int mobilityChange)
        {
            var meshShader = MeshTestUtility.LoadMeshDrawShader();
            GameObject go = null;
            Material material = null;
            var pool = new ResourcePool();
            var context = new RenderContext(pool, meshShader);
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
                material = MeshTestUtility.CreateRoutedMaterial(0, 0, "MeshComponentRegistration");
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
                MeshWorld world = context.meshWorld;
                Assert.AreEqual(reenable ? 1 : 0, world.entityCount,
                    "An enable queued before disable cannot resurrect an inactive renderer.");
                Assert.AreEqual(reenable ? mesh.subMeshCount : 0, world.elementCount);
                Assert.AreEqual(reenable, world.IsAlive(component.entityId));
                Assert.AreEqual(reenable && component.Mobility == EStateType.Dynamic ? 1 : 0, world.dynamicEntityCount);
                Assert.AreEqual(reenable && component.Mobility == EStateType.Dynamic, world.IsDynamic(component.entityId));
                if (reenable) Assert.Greater(world.GetPassCache(MeshPassPresets.Shadow).memberCount, 0, "A shadow-casting component joins the Shadow pass.");
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
                    var dirty = DirtyList();
                    var remainingTasks = FGraphics.GraphicsTasks.ToArray();
                    var remainingMeshTasks = FGraphics.MeshTasks.ToArray();
                    var remainingDirty = dirty.Where(value => !ReferenceEquals(value, component)).ToArray();
                    try { context.Dispose(); }
                    finally
                    {
                        pool.Dispose();
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
