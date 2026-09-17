using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using InfinityTech.Core;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// Per-pass structural bins and merged CommandTables. Rebuilt on BinEpoch / material invalidation.
    /// Layer masks are not bin members; they stay on Visibility.
    /// </summary>
    public sealed class PassBinStore : IDisposable
    {
        public const int MaxCommandInstances = 65536;

        private struct PassState
        {
            public NativeList<int> drawIndices;
            public NativeList<MeshPassCommand> commands;
            public NativeList<int> memberDrawIndices;
            public int builtEpoch;
            public bool dirty;
        }

        private readonly MeshScene m_Scene;
        private readonly PassRegistry m_Registry;
        private readonly uint m_PlatformFeatureKey;
        private readonly PassState[] m_Passes = new PassState[PassRegistry.Count];
        private int m_BuiltStructuralRevision = int.MinValue;
        private bool m_Disposed;

        public static event Action<ulong, uint> MaterialRevisionInvalidated;
        public static event Action MembershipInvalidated;

        public static void NotifyMaterialRevision(ulong materialUnityId, uint oldRevision)
        {
            MaterialRevisionInvalidated?.Invoke(materialUnityId, oldRevision);
        }

        public static void NotifyMembershipInvalidated()
        {
            MembershipInvalidated?.Invoke();
        }

        public PassBinStore(MeshScene scene, PassRegistry registry, uint platformFeatureKey)
        {
            m_Scene = scene ?? throw new ArgumentNullException(nameof(scene));
            m_Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            m_PlatformFeatureKey = platformFeatureKey;
            for (int i = 0; i < m_Passes.Length; ++i)
            {
                m_Passes[i] = new PassState
                {
                    drawIndices = new NativeList<int>(64, Allocator.Persistent),
                    commands = new NativeList<MeshPassCommand>(32, Allocator.Persistent),
                    memberDrawIndices = new NativeList<int>(64, Allocator.Persistent),
                    builtEpoch = -1,
                    dirty = true
                };
            }

            MaterialRevisionInvalidated += OnMaterialRevisionInvalidated;
            MembershipInvalidated += OnMembershipInvalidated;
        }

        public NativeArray<int> GetDrawIndices(MeshPassId passId)
        {
            EnsureRebuilt(passId);
            return m_Passes[(int)passId].drawIndices.AsArray();
        }

        public NativeArray<MeshPassCommand> GetCommands(MeshPassId passId)
        {
            EnsureRebuilt(passId);
            return m_Passes[(int)passId].commands.AsArray();
        }

        public NativeArray<int> GetMemberDrawIndices(MeshPassId passId)
        {
            EnsureRebuilt(passId);
            return m_Passes[(int)passId].memberDrawIndices.AsArray();
        }

        public int GetBuiltEpoch(MeshPassId passId)
        {
            EnsureRebuilt(passId);
            return m_Passes[(int)passId].builtEpoch;
        }

        public void EnsureRebuilt(MeshPassId passId)
        {
            int index = (int)passId;
            if (index < 0 || index >= m_Passes.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(passId));
            }

            PassState state = m_Passes[index];
            if (m_BuiltStructuralRevision != m_Scene.StructuralRevision)
            {
                for (int i = 0; i < m_Passes.Length; ++i)
                {
                    PassState other = m_Passes[i];
                    other.dirty = true;
                    m_Passes[i] = other;
                }

                m_BuiltStructuralRevision = m_Scene.StructuralRevision;
                state = m_Passes[index];
            }

            if (!state.dirty && state.builtEpoch == m_Scene.BinEpoch)
            {
                return;
            }

            Rebuild(passId, ref state);
            MeshPipelineDiagnostics.PassBinRebuilds++;
            m_Passes[index] = state;
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }

            m_Disposed = true;
            MaterialRevisionInvalidated -= OnMaterialRevisionInvalidated;
            MembershipInvalidated -= OnMembershipInvalidated;
            for (int i = 0; i < m_Passes.Length; ++i)
            {
                if (m_Passes[i].drawIndices.IsCreated) m_Passes[i].drawIndices.Dispose();
                if (m_Passes[i].commands.IsCreated) m_Passes[i].commands.Dispose();
                if (m_Passes[i].memberDrawIndices.IsCreated) m_Passes[i].memberDrawIndices.Dispose();
            }
        }

        void OnMembershipInvalidated()
        {
            for (int i = 0; i < m_Passes.Length; ++i)
            {
                PassState state = m_Passes[i];
                state.dirty = true;
                m_Passes[i] = state;
            }
        }

        void OnMaterialRevisionInvalidated(ulong materialUnityId, uint oldRevision)
        {
            for (int i = 0; i < m_Passes.Length; ++i)
            {
                PassState state = m_Passes[i];
                bool hit = false;
                for (int c = 0; c < state.commands.Length; ++c)
                {
                    MeshDrawCommandKey key = state.commands[c].key;
                    if (key.materialUnityId == materialUnityId && key.materialRevision == oldRevision)
                    {
                        hit = true;
                        break;
                    }
                }

                if (hit || state.dirty)
                {
                    state.dirty = true;
                    m_Passes[i] = state;
                }
            }
        }

        void Rebuild(MeshPassId passId, ref PassState state)
        {
            state.drawIndices.Clear();
            state.commands.Clear();
            state.memberDrawIndices.Clear();

            MeshPassDefinition definition = m_Registry.Get(passId);
            NativeArray<MeshDraw> draws = m_Scene.GetDraws();
            NativeArray<MeshInstanceRecord> instances = m_Scene.GetInstances();
            int drawHighWater = m_Scene.DrawHighWater;
            var members = new List<int>(math.max(16, drawHighWater));

            for (int drawIndex = 0; drawIndex < drawHighWater; ++drawIndex)
            {
                if (!m_Scene.IsDrawSlotLive(drawIndex))
                {
                    continue;
                }

                MeshDraw draw = draws[drawIndex];
                if ((draw.eligibility & definition.requiredEligibility) != definition.requiredEligibility)
                {
                    continue;
                }

                if (draw.renderQueue < definition.renderQueueMin || draw.renderQueue > definition.renderQueueMax)
                {
                    continue;
                }

                if (!draw.instance.IsValid || !m_Scene.IsInstanceAlive(draw.instance))
                {
                    continue;
                }

                MeshInstanceRecord instance = instances[(int)draw.instance.Index];
                if (definition.excludeCameraMotionOnly && instance.motionType == EMotionType.Camera)
                {
                    continue;
                }

                members.Add(drawIndex);
            }

            if (definition.sortPolicy == EMeshSortPolicy.StructuralMaterialOrder)
            {
                members.Sort((a, b) => CompareStructural(draws[a], draws[b], a, b));
            }

            state.drawIndices.AddRange(members.ToArray());

            var firstSeen = new List<MeshDrawCommandKey>(members.Count);
            var groups = new Dictionary<MeshDrawCommandKey, List<int>>(members.Count);
            for (int i = 0; i < members.Count; ++i)
            {
                int drawIndex = members[i];
                MeshDrawCommandKey key = BuildKey(draws[drawIndex], definition);
                if (!groups.TryGetValue(key, out List<int> list))
                {
                    list = new List<int>(4);
                    groups[key] = list;
                    firstSeen.Add(key);
                }

                list.Add(drawIndex);
            }

            for (int i = 0; i < firstSeen.Count; ++i)
            {
                MeshDrawCommandKey key = firstSeen[i];
                List<int> list = groups[key];
                int begin = state.memberDrawIndices.Length;
                for (int m = 0; m < list.Count; ++m)
                {
                    state.memberDrawIndices.Add(list[m]);
                }

                state.commands.Add(new MeshPassCommand
                {
                    key = key,
                    memberBegin = begin,
                    memberCount = list.Count
                });
            }

            state.builtEpoch = m_Scene.BinEpoch;
            state.dirty = false;
        }

        static int CompareStructural(in MeshDraw a, in MeshDraw b, int indexA, int indexB)
        {
            int c = a.renderQueue.CompareTo(b.renderQueue);
            if (c != 0) return c;
            c = a.materialUnityId.CompareTo(b.materialUnityId);
            if (c != 0) return c;
            c = a.meshUnityId.CompareTo(b.meshUnityId);
            if (c != 0) return c;
            c = a.sectionIndex.CompareTo(b.sectionIndex);
            return c != 0 ? c : indexA.CompareTo(indexB);
        }

        MeshDrawCommandKey BuildKey(in MeshDraw draw, in MeshPassDefinition definition)
        {
            uint materialRevision = 0;
            if (draw.material.IsValid)
            {
                NativeArray<MaterialDataRecord> materials = m_Scene.GetMaterials();
                if (draw.material.Index < (uint)materials.Length)
                {
                    materialRevision = materials[(int)draw.material.Index].revision;
                }
            }

            uint sectionRevision = 0;
            if (draw.section.IsValid)
            {
                NativeArray<MeshSectionRecord> sections = m_Scene.GetSections();
                if (draw.section.Index < (uint)sections.Length)
                {
                    sectionRevision = sections[(int)draw.section.Index].revision;
                }
            }

            Material material = UnityEntityId.ToObject<Material>(draw.materialUnityId);
            ulong shaderUnityId = material != null ? UnityEntityId.ToUInt64(material.shader) : 0;
            uint materialRoute = 0;
            if (material != null && material.HasProperty("_SurfaceRoute") && material.HasProperty("_TranslucentStage"))
            {
                MaterialRouteUtility.Read(material, out int route, out int stage);
                materialRoute = (uint)((route << 2) | stage);
            }

            int bakedTextureSet = 0;
            if (draw.instance.IsValid && m_Scene.TryGetInstance(draw.instance, out MeshInstanceRecord instance))
            {
                bakedTextureSet = instance.bakedLighting.TextureSet;
            }

            return new MeshDrawCommandKey
            {
                shaderUnityId = shaderUnityId,
                materialRoute = materialRoute,
                shaderPassIndex = definition.shaderPassIndex,
                meshUnityId = draw.meshUnityId,
                sectionIndex = draw.sectionIndex,
                materialUnityId = draw.materialUnityId,
                materialRevision = materialRevision,
                sectionRevision = sectionRevision,
                platformFeatureKey = m_PlatformFeatureKey,
                staticFlags = draw.staticFlags,
                bakedTextureSet = bakedTextureSet
            };
        }
    }
}
