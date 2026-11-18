using System.Linq;
using InfinityTech.Component;
using InfinityTech.Rendering.SceneModules;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    // A test world shares the process-wide mesh component queues with the live pipeline's scene world. It consumes only
    // the work of the components it owns and the removals of its own world; every other object's work stays queued, in order.
    static class SceneWorldTestUtility
    {
        public static void UpdateOwn(SceneWorld world, params MeshComponent[] components)
        {
            var registrations = MeshComponent.s_PendingRegistrations;
            var removals = MeshComponent.s_PendingRemovals;
            var syncs = MeshComponent.s_PendingSyncs;
            var poses = MeshComponent.s_PendingPoses;
            int worldId = world.meshes.worldId;
            var otherRegistrations = registrations.Where(value => !components.Contains(value.component)).ToArray();
            var otherRemovals = removals.Where(value => value.worldId != worldId).ToArray();
            var otherSyncs = syncs.Where(value => !components.Contains(value)).ToArray();
            var otherPoses = poses.Where(value => !components.Contains(value)).ToArray();
            registrations.RemoveAll(value => !components.Contains(value.component));
            removals.RemoveAll(value => value.worldId != worldId);
            syncs.RemoveAll(value => !components.Contains(value));
            poses.RemoveAll(value => !components.Contains(value));
            try
            {
                world.Update(false);
                world.ApplyStaticPoses();
            }
            finally
            {
                registrations.InsertRange(0, otherRegistrations);
                removals.InsertRange(0, otherRemovals);
                syncs.InsertRange(0, otherSyncs.Where(value => value != null));
                poses.InsertRange(0, otherPoses.Where(value => value != null));
            }
        }

        // Scene world disposal clears the process queues. Every other object's queued work survives it, in order.
        public static void Dispose(SceneWorld world)
        {
            var registrations = MeshComponent.s_PendingRegistrations.ToArray();
            var removals = MeshComponent.s_PendingRemovals.Where(value => value.worldId != world.meshes.worldId).ToArray();
            var syncs = MeshComponent.s_PendingSyncs.Where(value => value != null).ToArray();
            var poses = MeshComponent.s_PendingPoses.Where(value => value != null).ToArray();
            bool[] posePending = poses.Select(value => value.transformPending).ToArray();
            try
            {
                world.Dispose();
            }
            finally
            {
                MeshComponent.s_PendingRegistrations.InsertRange(0, registrations);
                MeshComponent.s_PendingRemovals.InsertRange(0, removals);
                MeshComponent.s_PendingSyncs.InsertRange(0, syncs);
                MeshComponent.s_PendingPoses.InsertRange(0, poses);
                for (int i = 0; i < poses.Length; ++i)
                {
                    poses[i].transformPending = posePending[i];
                }
            }
        }
    }
}
