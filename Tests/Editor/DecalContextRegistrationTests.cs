using NUnit.Framework;
using System.Collections.Generic;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline.Tests;
using InfinityTech.Rendering.Scene;
using UnityEngine;
using InfinityTech.Component;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    // The decal registry is the authority for which decals exist; a scene world's decal table derives from it and publishes
    // only inside its transaction.
    public class DecalContextRegistrationTests
    {
        readonly List<SceneWorld> m_Worlds = new List<SceneWorld>();
        ComputeShader m_MeshShader;
        ResourcePool m_Pool;

        SceneWorld OwnWorld()
        {
            var world = new SceneWorld(m_Pool, m_MeshShader);
            m_Worlds.Add(world);
            return world;
        }

        static int Decals(SceneWorld world)
        {
            SceneWorldTestUtility.UpdateOwn(world);
            return world.decals.table.count;
        }

        [SetUp]
        public void CreatePool()
        {
            m_MeshShader = MeshTestUtility.LoadMeshDrawShader();
            m_Pool = new ResourcePool();
        }

        [TearDown]
        public void DisposeOwnedWorlds()
        {
            try { foreach (var world in m_Worlds) SceneWorldTestUtility.Dispose(world); }
            finally
            {
                m_Worlds.Clear();
                m_Pool?.Dispose();
                m_Pool = null;
            }
        }

        [Test]
        public void ReplacementWorld_RebuildsFromTheRegistry()
        {
            var previous = OwnWorld();
            int baseline = Decals(previous);
            GameObject go = null;
            try
            {
                go = new GameObject("Decal world lifetime test");
                var decal = go.AddComponent<DecalComponent>();
                Assert.AreEqual(baseline + 1, Decals(previous));
                var replacement = OwnWorld();
                Assert.AreEqual(baseline + 1, Decals(replacement), "A new world derives every live decal from the registry.");
                decal.enabled = false;
                Assert.AreEqual(baseline, Decals(replacement));
                decal.enabled = true;
                Assert.AreEqual(baseline + 1, Decals(replacement));
                Object.DestroyImmediate(go);
                go = null;
                Assert.AreEqual(baseline, Decals(replacement));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Update_PublishesTheFinalActiveState_AfterToggles(bool finalEnabled)
        {
            var world = OwnWorld();
            int baseline = Decals(world);
            int revision = world.decals.table.revision;
            GameObject go = null;
            try
            {
                go = new GameObject("Decal toggle test");
                var decal = go.AddComponent<DecalComponent>();
                decal.enabled = false;
                decal.enabled = true;
                decal.enabled = finalEnabled;
                Assert.AreEqual(baseline, world.decals.table.count, "The table publishes only inside the scene transaction.");
                Assert.AreEqual(baseline + (finalEnabled ? 1 : 0), Decals(world));
                Assert.AreEqual(revision + 1, world.decals.table.revision, "Registry changes since the published table republish it once.");
                Decals(world);
                Assert.AreEqual(revision + 1, world.decals.table.revision, "An unchanged registry publishes nothing.");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Assert.AreEqual(baseline, Decals(world));
            }
        }

        [Test]
        public void SceneUnload_RemovesItsDecals()
        {
            var world = OwnWorld();
            int baseline = Decals(world);
            Scene original = SceneManager.GetActiveScene();
            Scene owned = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = new GameObject("Decal unload test");
                SceneManager.MoveGameObjectToScene(go, owned);
                go.AddComponent<DecalComponent>();
                Assert.AreEqual(baseline + 1, Decals(world));
                EditorSceneManager.ClosePreviewScene(owned);
                Assert.AreEqual(baseline, Decals(world));
            }
            finally
            {
                if (owned.IsValid() && owned.isLoaded) EditorSceneManager.ClosePreviewScene(owned);
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            }
        }
    }
}
