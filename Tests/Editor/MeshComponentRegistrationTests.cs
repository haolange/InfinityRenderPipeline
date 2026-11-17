using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using InfinityTech.Component;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.MeshPipeline.Tests;
using InfinityTech.Rendering.Scene;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class MeshComponentRegistrationTests
    {
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
            MeshAsset asset = null;
            var pool = new ResourcePool();
            var sceneWorld = new SceneWorld(pool, meshShader);
            MeshComponent component = null;
            int geometryStates = MeshComponent.geometryStateCount;
            try
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.SetActive(false);
                var mesh = go.GetComponent<MeshFilter>().sharedMesh;
                material = MeshTestUtility.CreateRoutedMaterial(0, 0, "MeshComponentRegistration");
                go.GetComponent<Renderer>().sharedMaterial = material;
                asset = ScriptableObject.CreateInstance<MeshAsset>();
                asset.hideFlags = HideFlags.DontSave;
                asset.InitializeSingleLevel(mesh, default);
                component = go.AddComponent<MeshComponent>();
                component.MeshAsset = asset;
                component.SetMaterials(new[] { material });
                component.Mobility = mobilityChange == 1 ? EMeshMobility.Static : EMeshMobility.Dynamic;
                component.CastShadow = ECastShadowMethod.Dynamic;
                go.SetActive(true);
                if (initiallyRegistered)
                    SceneWorldTestUtility.UpdateOwn(sceneWorld, component);
                if (deactivateObject) go.SetActive(false);
                else component.enabled = false;
                if (mobilityChange != 0) component.Mobility = mobilityChange == 1 ? EMeshMobility.Dynamic : EMeshMobility.Static;
                if (reenable) go.SetActive(true);
                SceneWorldTestUtility.UpdateOwn(sceneWorld, component);
                MeshWorld world = sceneWorld.meshes;
                Assert.AreEqual(reenable ? 1 : 0, world.entityCount,
                    "An enable queued before disable cannot resurrect an inactive renderer.");
                Assert.AreEqual(reenable ? mesh.subMeshCount : 0, world.elementCount);
                Assert.AreEqual(reenable, world.IsAlive(component.entityId));
                Assert.AreEqual(reenable && component.Mobility == EMeshMobility.Dynamic ? 1 : 0, world.dynamicEntityCount);
                Assert.AreEqual(reenable && component.Mobility == EMeshMobility.Dynamic, world.IsDynamic(component.entityId));
                if (reenable) Assert.Greater(world.GetPassCache(MeshPassTable.Shadow).memberCount, 0, "A shadow-casting component joins the Shadow pass.");
                Assert.IsFalse(go.GetComponent<Renderer>().forceRenderingOff, "Custom registration must not mutate native rendering state.");
                UnityEngine.Object.DestroyImmediate(go);
                go = null;
                Assert.AreEqual(geometryStates, MeshComponent.geometryStateCount,
                    "Only a registered component holds a geometry reference; edits and syncs while disabled take none.");
            }
            finally
            {
                try
                {
                    if (go != null) UnityEngine.Object.DestroyImmediate(go);
                    SceneWorldTestUtility.UpdateOwn(sceneWorld, component);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(material);
                    UnityEngine.Object.DestroyImmediate(asset);
                    try { SceneWorldTestUtility.Dispose(sceneWorld); }
                    finally { pool.Dispose(); }
                }
            }
        }

        [Test]
        public void MultiLevelAsset_RegistersEveryLevelWithItsSlots_AndAnInvalidAssetIsNotDrawn()
        {
            var meshShader = MeshTestUtility.LoadMeshDrawShader();
            var objects = new System.Collections.Generic.List<UnityEngine.Object>();
            var pool = new ResourcePool();
            var sceneWorld = new SceneWorld(pool, meshShader);
            MeshComponent component = null;
            int geometryStates = MeshComponent.geometryStateCount;
            try
            {
                T Own<T>(T value) where T : UnityEngine.Object { objects.Add(value); return value; }
                Mesh near = Own(MeshTestUtility.CreateMesh(2, "MultiLevel.Near")), far = Own(MeshTestUtility.CreateMesh(1, "MultiLevel.Far"));
                Material first = Own(MeshTestUtility.CreateRoutedMaterial(0, 0, "MultiLevel.First"));
                Material second = Own(MeshTestUtility.CreateRoutedMaterial(0, 0, "MultiLevel.Second"));
                MeshAsset asset = Own(ScriptableObject.CreateInstance<MeshAsset>());
                asset.hideFlags = HideFlags.DontSave;
                // LOD 0 submesh 0 uses slot 1 and submesh 1 slot 0; LOD 1 has one submesh on slot 1.
                asset.Initialize(new[] { near, far }, new[] { first, second },
                    new[] { new MeshLODInfo { screenSize = 0.5f, materialSlot = new[] { 1, 0 } }, new MeshLODInfo { screenSize = 0.1f, materialSlot = new[] { 1 } } }, 0.25f);
                var go = Own(new GameObject("MultiLevel"));
                go.SetActive(false);
                component = go.AddComponent<MeshComponent>();
                component.MeshAsset = asset;
                component.SetMaterials(new[] { first, second });
                go.SetActive(true);
                SceneWorldTestUtility.UpdateOwn(sceneWorld, component);

                MeshWorld world = sceneWorld.meshes;
                Assert.IsTrue(world.IsAlive(component.entityId));
                MeshEntity entity = world.entities[(int)component.entityId.Index];
                Assert.AreEqual(asset.BuildLodChain(), entity.lod, "The entity carries the asset's LOD chain.");
                var drawn = new System.Collections.Generic.List<(int lod, ulong mesh, int submesh, ulong material)>();
                for (int element = entity.firstElement; element >= 0; element = world.elements[element].nextElement)
                {
                    MeshElement value = world.elements[element];
                    MeshSection section = world.sections[(int)value.section.Index];
                    drawn.Add((value.lodIndex, section.meshId, section.submesh, world.materials[(int)value.material.Index].materialId));
                }
                CollectionAssert.AreEquivalent(new[]
                {
                    (0, MeshTestUtility.Id(near), 0, MeshTestUtility.Id(second)),
                    (0, MeshTestUtility.Id(near), 1, MeshTestUtility.Id(first)),
                    (1, MeshTestUtility.Id(far), 0, MeshTestUtility.Id(second))
                }, drawn, "One element per (level, submesh), drawing the component material at the asset's slot.");

                // A second component shares the asset's geometry state; an in-place edit of one LOD mesh rebuilds both.
                var other = Own(new GameObject("MultiLevel.Other"));
                other.SetActive(false);
                MeshComponent sharing = other.AddComponent<MeshComponent>();
                sharing.MeshAsset = asset;
                sharing.SetMaterials(new[] { first, second });
                other.SetActive(true);
                SceneWorldTestUtility.UpdateOwn(sceneWorld, component, sharing);
                Assert.AreEqual(geometryStates + 1, MeshComponent.geometryStateCount, "Components of one asset share one geometry state.");
                MeshEntityId before = component.entityId, sharedBefore = sharing.entityId;
                MeshComponent.NotifyGeometryChanged(far);
                SceneWorldTestUtility.UpdateOwn(sceneWorld, component, sharing);
                Assert.AreNotEqual(before, component.entityId, "A LOD mesh edit rebuilds the entity.");
                Assert.AreNotEqual(sharedBefore, sharing.entityId);
                Assert.AreEqual(2, world.entityCount);
                UnityEngine.Object.DestroyImmediate(other);
                SceneWorldTestUtility.UpdateOwn(sceneWorld, component);
                Assert.AreEqual(1, world.entityCount);

                // Screen sizes that do not decrease make the asset invalid: the component is reported and removed.
                asset.Initialize(new[] { near, far }, new[] { first, second },
                    new[] { new MeshLODInfo { screenSize = 0.1f, materialSlot = new[] { 1, 0 } }, new MeshLODInfo { screenSize = 0.5f, materialSlot = new[] { 1 } } }, 0.25f);
                // In the same transaction a valid component still registers: one invalid asset does not roll back the frame.
                MeshAsset valid = Own(ScriptableObject.CreateInstance<MeshAsset>());
                valid.hideFlags = HideFlags.DontSave;
                valid.InitializeSingleLevel(far, default);
                var third = Own(new GameObject("MultiLevel.Valid"));
                third.SetActive(false);
                MeshComponent independent = third.AddComponent<MeshComponent>();
                independent.MeshAsset = valid;
                independent.SetMaterials(new[] { first });
                third.SetActive(true);
                UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("MeshComponent 'MultiLevel' is not drawn: .*LOD 1"));
                SceneWorldTestUtility.UpdateOwn(sceneWorld, component, independent);
                Assert.IsFalse(world.IsAlive(component.entityId), "An invalid asset is not drawn.");
                Assert.IsTrue(world.IsAlive(independent.entityId), "The valid component of the same transaction is drawn.");
                Assert.AreEqual(1, world.entityCount);
            }
            finally
            {
                try
                {
                    foreach (UnityEngine.Object value in objects) if (value is GameObject) UnityEngine.Object.DestroyImmediate(value);
                    SceneWorldTestUtility.UpdateOwn(sceneWorld, component);
                }
                finally
                {
                    foreach (UnityEngine.Object value in objects) if (value != null) UnityEngine.Object.DestroyImmediate(value);
                    try { SceneWorldTestUtility.Dispose(sceneWorld); }
                    finally { pool.Dispose(); }
                    Assert.AreEqual(geometryStates, MeshComponent.geometryStateCount, "Destroyed components release their geometry states.");
                }
            }
        }
    }
}
