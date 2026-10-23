using NUnit.Framework;
using System.Collections.Generic;
using System.Reflection;
using InfinityTech.Rendering.MeshPipeline;
using UnityEngine;
using InfinityTech.Component;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class DecalContextRegistrationTests
    {
        readonly List<RenderContext> m_Contexts = new List<RenderContext>();
        FGraphicsTask[] m_PendingTasks;
        RenderContext OwnContext()
        {
            var context = new RenderContext();
            m_Contexts.Add(context);
            return context;
        }
        [SetUp]
        public void PreservePendingWork() => m_PendingTasks = FGraphics.GraphicsTasks.ToArray();
        [TearDown]
        public void DisposeOwnedContexts()
        {
            // Context disposal clears process-wide work. Preserve the Editor's preexisting work,
            // including dirty meshes, rather than letting test cleanup consume another scene's updates.
            var dirty = (List<MeshComponent>)typeof(RenderContext).GetField("s_DirtyMeshList",
                BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var pendingDirty = dirty.ToArray();
            try { foreach (var context in m_Contexts) context.Dispose(); }
            finally
            {
                m_Contexts.Clear();
                FGraphics.GraphicsTasks.Clear();
                FGraphics.GraphicsTasks.AddRange(m_PendingTasks);
                dirty.Clear();
                dirty.AddRange(pendingDirty);
            }
        }
        // Consume only this test's appended delegates; preserve the Editor's pending scene work.
        static void Drain(int start, RenderContext context)
        {
            var tasks = FGraphics.GraphicsTasks.GetRange(start, FGraphics.GraphicsTasks.Count - start);
            FGraphics.GraphicsTasks.RemoveRange(start, tasks.Count);
            foreach (var task in tasks) task?.Invoke(context);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReplacementContext_RecoversEnabledDecals_WithoutDoubleCountingPendingEnable(bool drainBeforeReplacement)
        {
            int start = FGraphics.GraphicsTasks.Count;
            var prefix = FGraphics.GraphicsTasks.ToArray();
            var previous = OwnContext();
            previous.InitializeWorldDecals();
            int baseline = previous.WorldDecalCount;
            GameObject go = null;
            var replacement = OwnContext();
            try
            {
                go = new GameObject("Decal context lifetime test");
                var decal = go.AddComponent<DecalComponent>();
                if (drainBeforeReplacement) Drain(start, previous);
                Drain(start, replacement);
                replacement.InitializeWorldDecals();
                Assert.AreEqual(baseline + 1, replacement.WorldDecalCount,
                    "Live decals survive context replacement whether their enable task ran on the old or new context.");
                decal.enabled = false;
                Drain(start, replacement);
                replacement.InitializeWorldDecals();
                Assert.AreEqual(baseline, replacement.WorldDecalCount, "Later disables retain incremental ownership.");
                decal.enabled = true;
                Drain(start, replacement);
                Assert.AreEqual(baseline + 1, replacement.WorldDecalCount);
                Object.DestroyImmediate(go);
                go = null;
                Drain(start, replacement);
                Assert.AreEqual(baseline, replacement.WorldDecalCount);
                CollectionAssert.AreEqual(prefix, FGraphics.GraphicsTasks);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Drain(start, replacement);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FirstDrain_UsesFinalActiveState_AfterQueuedToggles(bool finalEnabled)
        {
            int start = FGraphics.GraphicsTasks.Count;
            var context = OwnContext();
            var reference = OwnContext();
            reference.InitializeWorldDecals();
            int baseline = reference.WorldDecalCount;
            GameObject go = null;
            try
            {
                go = new GameObject("Decal queued toggle test");
                var decal = go.AddComponent<DecalComponent>();
                decal.enabled = false;
                decal.enabled = true;
                decal.enabled = finalEnabled;
                Drain(start, context);
                context.InitializeWorldDecals();
                Assert.AreEqual(baseline + (finalEnabled ? 1 : 0), context.WorldDecalCount);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Drain(start, context);
                Assert.AreEqual(baseline, context.WorldDecalCount);
            }
        }

        [Test]
        public void LoadedSceneUnload_RemovesRecoveredDecals()
        {
            int start = FGraphics.GraphicsTasks.Count;
            var context = OwnContext();
            context.InitializeWorldDecals();
            int baseline = context.WorldDecalCount;
            Scene original = SceneManager.GetActiveScene();
            Scene owned = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = new GameObject("Decal unload test");
                SceneManager.MoveGameObjectToScene(go, owned);
                go.AddComponent<DecalComponent>();
                Drain(start, context);
                {
                    var replacement = OwnContext();
                    replacement.InitializeWorldDecals();
                    Assert.AreEqual(baseline + 1, replacement.WorldDecalCount);
                    EditorSceneManager.ClosePreviewScene(owned);
                    Drain(start, replacement);
                    Assert.AreEqual(baseline, replacement.WorldDecalCount);
                }
            }
            finally
            {
                if (owned.IsValid() && owned.isLoaded) EditorSceneManager.ClosePreviewScene(owned);
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                Drain(start, context);
            }
        }
    }
}
