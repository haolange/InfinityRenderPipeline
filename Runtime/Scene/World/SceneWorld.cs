using System;
using System.Collections.Generic;
using UnityEngine;
using InfinityTech.Component;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.TerrainPipeline;

namespace InfinityTech.Rendering.SceneModules
{
    /// <summary>
    /// The pipeline's derived view of the scene: one module per scene type, each a persistent layer derived from that
    /// type's component registry. Component registries are the authority for what exists; they live as long as the
    /// process, like the Unity objects they mirror, and hold no work aimed at a particular world. A new scene world
    /// therefore rebuilds every module from them, which is what a replaced pipeline needs.
    ///
    /// <see cref="Update"/> is one transaction across the modules: every table stages its change, the mesh world applies
    /// the components' structural edits in its own transaction, and only then do the tables publish (P3). A failure
    /// rolls every module back and keeps the queued work for the next frame. Poses follow the structure:
    /// <see cref="ApplyStaticPoses"/>, then <see cref="MeshWorld.UpdateDynamicPoses"/>.
    /// </summary>
    internal sealed class SceneWorld : IDisposable
    {
        public readonly MeshWorld meshes;
        public readonly LightModule lights;
        public readonly ProbeModule probes;
        public readonly DecalModule decals;
        public readonly TerrainModule terrains;

        readonly List<MeshEntityId> m_PendingRemovals = new List<MeshEntityId>(64);

        /// <param name="meshPasses">The pipeline's mesh pass table; the mesh world builds one pass cache per entry.</param>
        /// <param name="shadowCasterPass">The entry of that table whose members cast shadows.</param>
        public SceneWorld(ResourcePool resourcePool, ComputeShader meshDrawShader, MeshPassDesc[] meshPasses, MeshPassId shadowCasterPass)
        {
            meshes = new MeshWorld(resourcePool, meshDrawShader, meshPasses);
            lights = new LightModule(resourcePool, shadowCasterPass);
            probes = new ProbeModule(resourcePool);
            decals = new DecalModule();
            terrains = new TerrainModule();
            // A new world knows none of the registered mesh components: every one syncs again from scratch. The tables
            // rebuild from their registries at the first Update because they start at no committed version.
            MeshComponent.ResyncAll();
        }

        /// <summary>The frame's scene transaction. <paramref name="refreshStaticPoses"/> asks every static mesh for its pose.</summary>
        public void Update(bool refreshStaticPoses)
        {
            ProcessMeshRegistrations();
            if (refreshStaticPoses)
            {
                MeshComponent.NotifyAllStaticPoses();
            }
            try
            {
                lights.Prepare();
                probes.Prepare();
                decals.Prepare();
                terrains.Prepare();
                CommitMeshEdits();
            }
            catch
            {
                lights.Rollback();
                probes.Rollback();
                decals.Rollback();
                terrains.Rollback();
                throw;
            }
            lights.Commit();
            probes.Commit();
            decals.Commit();
            terrains.Commit();
        }

        // An enable queued before a later disable must not resurrect the component: CompleteRegistration checks the epoch.
        // A removal names the world that holds the entity; a removal for an earlier world is not this world's to apply.
        void ProcessMeshRegistrations()
        {
            foreach ((MeshComponent component, uint epoch) in MeshComponent.s_PendingRegistrations)
            {
                if (component)
                {
                    component.CompleteRegistration(epoch);
                }
            }
            MeshComponent.s_PendingRegistrations.Clear();
            foreach ((MeshEntityId entity, int worldId) in MeshComponent.s_PendingRemovals)
            {
                if (entity.IsValid && worldId == meshes.worldId)
                {
                    m_PendingRemovals.Add(entity);
                }
            }
            MeshComponent.s_PendingRemovals.Clear();
        }

        // One structural transaction: pending removals, then every dirty component. Components publish their staged state
        // only after the commit; on failure the world rolls back, the components abandon their staged state, and the
        // queues stay for the next frame while the original exception propagates.
        void CommitMeshEdits()
        {
            List<MeshComponent> syncs = MeshComponent.s_PendingSyncs;
            if (syncs.Count == 0 && m_PendingRemovals.Count == 0)
            {
                return;
            }
            bool committed = false;
            try
            {
                using (MeshWorldUpdate update = meshes.BeginUpdate())
                {
                    foreach (MeshEntityId entity in m_PendingRemovals)
                    {
                        if (meshes.IsAlive(entity))
                        {
                            update.RemoveEntity(entity);
                        }
                    }
                    foreach (MeshComponent component in syncs)
                    {
                        if (component)
                        {
                            component.Sync(meshes, update);
                        }
                    }
                    update.Commit();
                    committed = true;
                }
            }
            finally
            {
                foreach (MeshComponent component in syncs)
                {
                    if (!component)
                    {
                        continue;
                    }
                    if (committed)
                    {
                        component.PublishSync();
                    }
                    else
                    {
                        component.AbandonSync();
                    }
                }
                if (committed)
                {
                    m_PendingRemovals.Clear();
                    syncs.Clear();
                }
            }
        }

        /// <summary>Explicit static pose notifications, written after the structural transaction.</summary>
        public void ApplyStaticPoses()
        {
            List<MeshComponent> poses = MeshComponent.s_PendingPoses;
            foreach (MeshComponent component in poses)
            {
                if (component)
                {
                    component.ApplyStaticPose(meshes);
                }
            }
            poses.Clear();
        }

        public void Dispose()
        {
            foreach (MeshComponent component in MeshComponent.s_PendingPoses)
            {
                if (component)
                {
                    component.transformPending = false;
                }
            }
            MeshComponent.s_PendingPoses.Clear();
            MeshComponent.s_PendingSyncs.Clear();
            MeshComponent.s_PendingRegistrations.Clear();
            MeshComponent.s_PendingRemovals.Clear();
            m_PendingRemovals.Clear();

            var failures = new List<Exception>();
            void Release(Action action)
            {
                try { action(); }
                catch (Exception error) { failures.Add(error); }
            }
            Release(probes.Dispose);
            Release(lights.Dispose);
            Release(meshes.Dispose);
            if (failures.Count == 1)
            {
                throw failures[0];
            }
            if (failures.Count > 1)
            {
                throw new AggregateException(failures);
            }
        }
    }
}
