using System;
using NUnit.Framework;
using UnityEngine;
using InfinityTech.Core;
using InfinityTech.Component;
using InfinityTech.Rendering.Scene;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline.Tests;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    // Component registries are the authority; a scene world derives its tables from them and publishes inside one
    // transaction across the modules.
    public class SceneWorldTransactionTests
    {
        ComputeShader m_MeshShader;
        ResourcePool m_Pool;

        [SetUp]
        public void CreatePool()
        {
            m_MeshShader = MeshTestUtility.LoadMeshDrawShader();
            m_Pool = new ResourcePool();
        }

        [TearDown]
        public void DisposePool()
        {
            m_Pool?.Dispose();
            m_Pool = null;
        }

        static GameObject CreateLight(string name, out ulong lightId)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            Light light = go.AddComponent<Light>();
            go.AddComponent<InfinityAdditionalLightData>();
            lightId = UnityEntityId.ToUInt64(light);
            return go;
        }

        [Test]
        public void ReplacementWorld_RebuildsTheLightTableFromTheRegistry()
        {
            GameObject go = null;
            SceneWorld previous = new SceneWorld(m_Pool, m_MeshShader), replacement = null;
            try
            {
                go = CreateLight("SceneWorld.Light", out ulong lightId);
                SceneWorldTestUtility.UpdateOwn(previous);
                Assert.IsTrue(previous.lights.table.TryGet(lightId, out InfinityAdditionalLightData data));
                Assert.AreSame(go.GetComponent<InfinityAdditionalLightData>(), data);
                SceneWorldTestUtility.Dispose(previous);
                previous = null;

                replacement = new SceneWorld(m_Pool, m_MeshShader);
                SceneWorldTestUtility.UpdateOwn(replacement);
                Assert.IsTrue(replacement.lights.table.TryGet(lightId, out _),
                    "A replaced pipeline's world still knows a light enabled before it existed.");
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                if (previous != null) SceneWorldTestUtility.Dispose(previous);
                if (replacement != null) SceneWorldTestUtility.Dispose(replacement);
            }
        }

        [Test]
        public void Tables_PublishOnlyAtUpdate_AndAdvanceTheirRevisionOnce()
        {
            GameObject go = null;
            var world = new SceneWorld(m_Pool, m_MeshShader);
            try
            {
                SceneWorldTestUtility.UpdateOwn(world);
                int revision = world.lights.table.revision;
                go = CreateLight("SceneWorld.Publish", out ulong lightId);
                Assert.IsFalse(world.lights.table.TryGet(lightId, out _), "Readers see the published table until the transaction commits.");
                SceneWorldTestUtility.UpdateOwn(world);
                Assert.IsTrue(world.lights.table.TryGet(lightId, out _));
                Assert.AreEqual(revision + 1, world.lights.table.revision);
                SceneWorldTestUtility.UpdateOwn(world);
                Assert.AreEqual(revision + 1, world.lights.table.revision, "An unchanged registry publishes nothing.");
                UnityEngine.Object.DestroyImmediate(go);
                go = null;
                SceneWorldTestUtility.UpdateOwn(world);
                Assert.IsFalse(world.lights.table.TryGet(lightId, out _));
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                SceneWorldTestUtility.Dispose(world);
            }
        }

        [Test]
        public void FailedMeshTransaction_RollsBackEveryTable()
        {
            GameObject light = null, mesh = null;
            Material material = null;
            MeshAsset asset = null;
            var world = new SceneWorld(m_Pool, m_MeshShader);
            try
            {
                SceneWorldTestUtility.UpdateOwn(world);
                light = CreateLight("SceneWorld.Rollback", out ulong lightId);
                mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mesh.hideFlags = HideFlags.DontSave;
                material = MeshTestUtility.CreateRoutedMaterial(0, 0, "SceneWorld.Rollback");
                asset = ScriptableObject.CreateInstance<MeshAsset>();
                asset.hideFlags = HideFlags.DontSave;
                asset.InitializeSingleLevel(mesh.GetComponent<MeshFilter>().sharedMesh, default);
                var component = mesh.AddComponent<MeshComponent>();
                component.MeshAsset = asset;
                component.SetMaterials(new[] { material });

                // A transaction left open makes the mesh commit fail after every table staged its change.
                MeshWorldUpdate blocking = world.meshes.BeginUpdate();
                Assert.Throws<InvalidOperationException>(() => SceneWorldTestUtility.UpdateOwn(world, component));
                Assert.IsFalse(world.lights.table.TryGet(lightId, out _), "No module publishes when another module's commit fails.");
                blocking.Dispose();

                SceneWorldTestUtility.UpdateOwn(world, component);
                Assert.IsTrue(world.lights.table.TryGet(lightId, out _));
                Assert.IsTrue(world.meshes.IsAlive(component.entityId), "The work kept by the failed frame applies in the next one.");
            }
            finally
            {
                if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
                if (light != null) UnityEngine.Object.DestroyImmediate(light);
                SceneWorldTestUtility.UpdateOwn(world);
                SceneWorldTestUtility.Dispose(world);
                if (material != null) UnityEngine.Object.DestroyImmediate(material);
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }
        }
    }
}
