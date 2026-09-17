using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.MeshPipeline.Tests
{
    public class BackendPolicyTests
    {
        [Test]
        public void CandidateTable_MatchesVisibleExtractSlots()
        {
            using (var scene = new MeshScene(16))
            using (var bins = new PassBinStore(scene, new PassRegistry(), 0))
            using (var tables = new MeshCandidateTableStore(scene, bins))
            {
                MeshSceneUpdate update = scene.BeginUpdate();
                MeshInstanceId visible = Create(update, flags: EMeshInstanceFlags.Visible);
                Create(update, flags: 0);
                update.Commit();

                NativeArray<MeshPassCommand> commands = bins.GetCommands(MeshPassId.GBuffer);
                NativeArray<int> members = bins.GetMemberDrawIndices(MeshPassId.GBuffer);
                Assert.AreEqual(2, members.Length);

                ref var table = ref tables.Ensure(MeshPassId.GBuffer);
                Assert.AreEqual(2, table.candidateCount);
                var visibleSlots = new HashSet<int> { (int)visible.Index };
                int visibleCandidates = 0;
                for (int i = 0; i < table.candidateCount; ++i)
                {
                    if (visibleSlots.Contains((int)table.candidates[i].x))
                    {
                        visibleCandidates++;
                    }
                }

                Assert.AreEqual(1, visibleCandidates);
                Assert.AreEqual(commands.Length, table.commandCount);
            }
        }

        static MeshInstanceId Create(MeshSceneUpdate update, EMeshInstanceFlags flags)
        {
            TransformId transform = update.CreateTransform(float4x4.identity);
            MeshInstanceId instance = update.CreateInstance(
                transform,
                new FBound(float3.zero, new float3(1, 1, 1)),
                layerMask: ~0,
                renderingLayerMask: 1,
                flags: flags,
                motionType: EMotionType.Object,
                castShadow: ECastShadowMethod.Off);
            update.CreateDraw(instance, 10, 0, 20, EPassEligibility.GBuffer, 2000, 0);
            return instance;
        }
    }
}
