using System;
using NUnit.Framework;
using UnityEngine;
using InfinityTech.Component;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class TerrainAuthoringContractTests
    {
        static void Lifecycle(TerrainComponent component, string name) => typeof(EntityComponent).GetMethod(name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(component, null);

        [Test]
        public void ExplicitImportUsesTerrainDataWithoutColliderAndSurvivesReenable()
        {
            int start = FGraphics.GraphicsTasks.Count;
            var data = new TerrainData { heightmapResolution = 65, size = new Vector3(64, 10, 64) };
            var go = new GameObject("OwnedTerrainAuthoring");
            go.SetActive(false);
            try
            {
                go.AddComponent<Terrain>().terrainData = data;
                var component = go.AddComponent<TerrainComponent>();
                component.Serialize();
                Assert.IsNull(go.GetComponent<TerrainCollider>());
                Assert.AreSame(data, component.terrainData);
                var sector = component.terrainSector;
                Assert.AreEqual(1, sector.sections.Length);
                go.SetActive(true);
                // TerrainComponent runs in Play mode. Exercise its native lifecycle messages in this EditMode fixture.
                Lifecycle(component, "OnEnable");
                Assert.IsTrue(sector.m_Sections.IsCreated);
                Lifecycle(component, "OnDisable");
                go.SetActive(false);
                Assert.IsFalse(sector.m_Sections.IsCreated);
                Assert.AreSame(sector, component.terrainSector, "Disabling releases runtime storage, not authored sector data.");
                go.SetActive(true);
                Lifecycle(component, "OnEnable");
                Assert.IsTrue(sector.m_Sections.IsCreated);
                Assert.AreSame(sector, component.terrainSector);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(data);
                FGraphics.GraphicsTasks.RemoveRange(start, FGraphics.GraphicsTasks.Count - start);
            }
        }

        [Test]
        public void InvalidImportDoesNotReplaceAuthoredSector()
        {
            var data = new TerrainData { heightmapResolution = 65, size = new Vector3(64, 10, 64) };
            var go = new GameObject("OwnedTerrainInvalidImport");
            go.SetActive(false);
            try
            {
                var native = go.AddComponent<Terrain>(); native.terrainData = data;
                var component = go.AddComponent<TerrainComponent>(); component.Serialize();
                var sector = component.terrainSector;
                native.terrainData = null;
                Assert.Throws<InvalidOperationException>(() => component.Serialize());
                Assert.AreSame(sector, component.terrainSector);
                Assert.AreSame(data, component.terrainData);
                sector.Dispose(); sector.Dispose();
            }
            finally { UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(data); }
        }
    }
}
