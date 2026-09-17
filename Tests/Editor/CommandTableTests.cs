using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.MeshPipeline.Tests
{
    public class CommandTableTests
    {
        [Test]
        public void BakedTextureSet_SplitsCommands()
        {
            using (var scene = new MeshScene(16))
            using (var bins = new PassBinStore(scene, new PassRegistry(), 0))
            {
                MeshSceneUpdate update = scene.BeginUpdate();
                MeshInstanceId a = Create(update, 1);
                MeshInstanceId b = Create(update, 2);
                MeshInstanceId c = Create(update, 2);
                update.Commit();

                NativeArray<MeshPassCommand> commands = bins.GetCommands(MeshPassId.GBuffer);
                Assert.AreEqual(2, commands.Length);
                Assert.AreEqual(1, commands[0].key.bakedTextureSet);
                Assert.AreEqual(1, commands[0].memberCount);
                Assert.AreEqual(2, commands[1].key.bakedTextureSet);
                Assert.AreEqual(2, commands[1].memberCount);
            }
        }

        [Test]
        public void CommandKey_EqualsIsAuthoritativeUnderHashCollision()
        {
            Assert.IsTrue(TryFindCollision(out MeshDrawCommandKey keyA, out MeshDrawCommandKey keyB));
            Assert.IsFalse(keyA.Equals(keyB));
            Assert.AreEqual(keyA.GetHashCode(), keyB.GetHashCode());

            var dict = new Dictionary<MeshDrawCommandKey, int>(2);
            dict[keyA] = 11;
            dict[keyB] = 22;
            Assert.AreEqual(2, dict.Count);
            Assert.AreEqual(11, dict[keyA]);
            Assert.AreEqual(22, dict[keyB]);
        }

        [Test]
        public void MaterialChange_RebuildsOnlyMatchingPass()
        {
            using (var scene = new MeshScene(16))
            using (var bins = new PassBinStore(scene, new PassRegistry(), 0))
            {
                MeshSceneUpdate update = scene.BeginUpdate();
                Create(update, 1);
                update.CreateDraw(
                    CreateInstance(update, 0),
                    11, 0, 21, EPassEligibility.Shadow, 2000, 0);
                update.Commit();

                MeshPipelineDiagnostics.Reset();
                bins.EnsureRebuilt(MeshPassId.GBuffer);
                bins.EnsureRebuilt(MeshPassId.Shadow);
                Assert.AreEqual(2, MeshPipelineDiagnostics.PassBinRebuilds);

                using (MeshSceneUpdate change = scene.BeginUpdate())
                {
                    change.SetMaterial(
                        FindDraw(scene, EPassEligibility.GBuffer),
                        20,
                        2100);
                    change.Commit();
                }

                int afterCommit = MeshPipelineDiagnostics.PassBinRebuilds;
                bins.EnsureRebuilt(MeshPassId.Shadow);
                Assert.AreEqual(afterCommit, MeshPipelineDiagnostics.PassBinRebuilds);
                bins.EnsureRebuilt(MeshPassId.GBuffer);
                Assert.Greater(MeshPipelineDiagnostics.PassBinRebuilds, afterCommit);
            }
        }

        static MeshInstanceId Create(MeshSceneUpdate update, int textureSet)
        {
            MeshInstanceId instance = CreateInstance(update, textureSet);
            update.CreateDraw(instance, 10, 0, 20, EPassEligibility.GBuffer, 2000, 0);
            return instance;
        }

        static MeshInstanceId CreateInstance(MeshSceneUpdate update, int textureSet)
        {
            TransformId transform = update.CreateTransform(float4x4.identity);
            MeshInstanceId instance = update.CreateInstance(
                transform,
                new FBound(float3.zero, new float3(1, 1, 1)),
                layerMask: ~0,
                renderingLayerMask: 1,
                flags: EMeshInstanceFlags.Visible,
                motionType: EMotionType.Object,
                castShadow: ECastShadowMethod.Off);
            if (textureSet != 0)
            {
                update.SetInstanceBakedLighting(instance, new FMeshBakedLighting
                {
                    metadata = new float4(textureSet, 0, 0, 0)
                });
            }

            return instance;
        }

        static MeshDrawId FindDraw(MeshScene scene, EPassEligibility eligibility)
        {
            NativeArray<MeshDraw> draws = scene.GetDraws();
            for (int i = 0; i < scene.DrawHighWater; ++i)
            {
                if (!scene.IsDrawSlotLive(i))
                {
                    continue;
                }

                if ((draws[i].eligibility & eligibility) == eligibility)
                {
                    return new MeshDrawId((uint)i, scene.GetDrawGenerations()[i]);
                }
            }

            return MeshDrawId.Invalid;
        }

        static bool TryFindCollision(out MeshDrawCommandKey keyA, out MeshDrawCommandKey keyB)
        {
            keyA = Key(6, 78, 25, 815, 42, 56, 50, 0, 0);
            keyB = Key(3, 21, 20, 582, 5, 38, 25, 0, 0);
            if (!keyA.Equals(keyB) && keyA.GetHashCode() == keyB.GetHashCode())
            {
                return true;
            }

            var rnd = new System.Random(1);
            var seen = new Dictionary<int, MeshDrawCommandKey>(65536);
            for (int i = 0; i < 4000000; ++i)
            {
                var key = Key(
                    rnd.Next(8), (ulong)rnd.Next(1024), rnd.Next(64), (ulong)rnd.Next(1024),
                    (uint)rnd.Next(64), (uint)rnd.Next(64), (uint)rnd.Next(64), (uint)rnd.Next(64), rnd.Next(8));
                int hash = key.GetHashCode();
                if (seen.TryGetValue(hash, out MeshDrawCommandKey prior) && !prior.Equals(key))
                {
                    keyA = prior;
                    keyB = key;
                    return true;
                }

                seen[hash] = key;
            }

            return false;
        }

        static MeshDrawCommandKey Key(
            int shaderPass, ulong mesh, int section, ulong material,
            uint matRev, uint secRev, uint platform, uint flags, int baked)
        {
            return new MeshDrawCommandKey
            {
                shaderPassIndex = shaderPass,
                meshUnityId = mesh,
                sectionIndex = section,
                materialUnityId = material,
                materialRevision = matRev,
                sectionRevision = secRev,
                platformFeatureKey = platform,
                staticFlags = flags,
                bakedTextureSet = baked
            };
        }
    }
}
