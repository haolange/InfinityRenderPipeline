using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Core;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>Command identity inside one pass cache: the world's section and material slots.</summary>
    internal struct MeshDrawCommandKey : IEquatable<MeshDrawCommandKey>
    {
        public int section;
        public int material;

        public MeshDrawCommandKey(int section, int material) { this.section = section; this.material = material; }
        public bool Equals(MeshDrawCommandKey other) => section == other.section && material == other.material;
        public override bool Equals(object obj) => obj is MeshDrawCommandKey other && Equals(other);
        public override int GetHashCode() => unchecked(section * 397 ^ material);
    }

    /// <summary>A cached draw command of one pass: one mesh section with one material. A slot with refCount 0 is free.</summary>
    internal struct MeshDrawCommand
    {
        public MeshDrawCommandKey key;
        public int refCount;
        /// <summary>Members whose entity's LOD chain has a cross-fade band; only such commands can draw a fading range.</summary>
        public int fadingMemberCount;
        /// <summary>Shader-state rank, material slot and section slot, each saturated into its segment.</summary>
        public ulong stateKey;
    }

    /// <summary>Managed objects and submesh range a command draws with, resolved when the command is created.</summary>
    internal struct MeshDrawCommandBinding
    {
        public Mesh mesh;
        public Material material;
        public int passIndex;
        public int submesh;
        public uint indexCount, indexStart, baseVertex;
        public bool reportedMissing;
    }

    internal struct ShaderState : IEquatable<ShaderState>
    {
        public ulong shader;
        public int pass;
        public bool Equals(ShaderState other) => shader == other.shader && pass == other.pass;
        public override bool Equals(object obj) => obj is ShaderState other && Equals(other);
        public override int GetHashCode() => unchecked(shader.GetHashCode() * 397 ^ pass);
    }

    /// <summary>
    /// The cached commands of one <see cref="MeshPassDesc"/>: which elements belong to the pass and which command each one
    /// draws with. Maintained incrementally from the world's committed dirty elements; lives as long as its world.
    /// </summary>
    internal sealed class MeshPassCache : IDisposable
    {
        public const uint NoCommand = 0xFFFFFFFFu;
        /// <summary>Candidate <c>y</c> packs the command slot (low 24 bits) and the element's LOD level (high 8 bits).</summary>
        public const uint CommandMask = 0x00FFFFFFu;
        public const int LodShift = 24;
        /// <summary>uints per command in <see cref="GetCommandMeta"/>: index count, start index, base vertex, stable base, fading base.</summary>
        public const int MetaStride = 5;

        public readonly MeshPassDesc desc;
        readonly uint m_RouteMask;
        NativeList<uint2> m_Candidates;
        NativeList<byte> m_CanFade;
        NativeList<MeshDrawCommand> m_Commands;
        readonly List<MeshDrawCommandBinding> m_Bindings = new List<MeshDrawCommandBinding>(64);
        NativeParallelHashMap<MeshDrawCommandKey, int> m_Lookup;
        NativeList<int> m_FreeCommands;
        NativeList<int> m_EntityMemberCount;
        NativeList<int> m_Pending;
        NativeArray<uint> m_CommandMeta;
        NativeList<int> m_CommandOrder;
        uint m_CommandMetaRevision = uint.MaxValue;
        readonly Dictionary<ShaderState, int> m_StateRanks = new Dictionary<ShaderState, int>(16);
        readonly Dictionary<MeshDrawCommandKey, MeshDrawCommandBinding> m_Prepared = new Dictionary<MeshDrawCommandKey, MeshDrawCommandBinding>(16);
        int m_ShaderEpoch;
        BufferHandle m_CandidateBuffer;
        int m_CandidateCapacity;
        uint m_UploadedRevision = uint.MaxValue;
        bool m_Disposed;

        public int memberCount { get; private set; }
        public int commandCount { get; private set; }
        public int commandHighWater => m_Commands.Length;
        /// <summary>Advances whenever membership changes; GPU tables and command metadata follow it.</summary>
        public uint revision { get; private set; }

        /// <summary>(entity slot, command slot | LOD level &lt;&lt; 24, or <see cref="NoCommand"/>) per element slot.</summary>
        internal NativeArray<uint2> candidates => m_Candidates.AsArray();
        internal NativeArray<MeshDrawCommand> commands => m_Commands.AsArray();
        internal NativeArray<int> entityMemberCount => m_EntityMemberCount.AsArray();
        internal BufferHandle candidateBuffer => m_CandidateBuffer;

        internal MeshPassCache(in MeshPassDesc desc)
        {
            if (!MeshPassTable.IsMeshLightMode(desc.lightMode))
                throw new ArgumentException($"'{desc.lightMode}' is not an Infinity mesh LightMode.", nameof(desc));
            if (desc.queueMin > desc.queueMax) throw new ArgumentException("queueMin exceeds queueMax.", nameof(desc));
            this.desc = desc;
            for (int route = 0; route <= 1; ++route)
                for (int stage = 0; stage <= 3; ++stage)
                    if (MaterialRouteUtility.RouteEnablesPass(route, stage, desc.lightMode)) m_RouteMask |= 1u << (route * 4 + stage);
            m_Candidates = new NativeList<uint2>(1024, Allocator.Persistent);
            m_CanFade = new NativeList<byte>(1024, Allocator.Persistent);
            m_Commands = new NativeList<MeshDrawCommand>(64, Allocator.Persistent);
            m_Lookup = new NativeParallelHashMap<MeshDrawCommandKey, int>(64, Allocator.Persistent);
            m_FreeCommands = new NativeList<int>(16, Allocator.Persistent);
            m_EntityMemberCount = new NativeList<int>(1024, Allocator.Persistent);
            m_Pending = new NativeList<int>(64, Allocator.Persistent);
            m_CommandOrder = new NativeList<int>(64, Allocator.Persistent);
            m_ShaderEpoch = MeshPassShaderUtility.shaderEpoch;
        }

        public MeshDrawCommandBinding GetBinding(int command) => m_Bindings[command];

        internal void ReportMissing(int command)
        {
            MeshDrawCommandBinding binding = m_Bindings[command];
            if (binding.reportedMissing) return;
            binding.reportedMissing = true;
            m_Bindings[command] = binding;
            Debug.LogError($"Mesh pass '{desc.lightMode}' command {command} skips its draws: its mesh or material was destroyed without a MeshComponent update.");
        }

        /// <summary>Builds the cache from every live element of the world.</summary>
        internal void Build(MeshWorld world)
        {
            var all = new NativeList<int>(math.max(1, world.elementHighWater), Allocator.Temp);
            for (int i = 0; i < world.elementHighWater; ++i)
                if (world.IsElementSlotLive(i)) all.Add(i);
            try
            {
                Prepare(world, all.AsArray());
                Apply(world);
            }
            finally { all.Dispose(); }
        }

        bool Qualifies(MeshWorld world, int elementIndex, out MeshDrawCommandKey key, out int entity)
        {
            key = default;
            entity = -1;
            if (!world.IsElementSlotLive(elementIndex)) return false;
            MeshElement element = world.elements[elementIndex];
            if (!element.material.IsValid) return false;
            MeshMaterialState state = world.materials[(int)element.material.Index].state;
            if ((m_RouteMask & (1u << (state.route * 4 + state.stage))) == 0) return false;
            if (state.renderQueue < desc.queueMin || state.renderQueue > desc.queueMax) return false;
            entity = (int)element.entity.Index;
            if ((world.entities[entity].desc.flags & desc.requireEntityFlags) != desc.requireEntityFlags) return false;
            key = new MeshDrawCommandKey((int)element.section.Index, (int)element.material.Index);
            return true;
        }

        /// <summary>
        /// Resolves the binding of every command the dirty elements would draw with, without changing the cache. Throws
        /// when an element's route enables this pass but its mesh, submesh or shader pass cannot be drawn.
        /// </summary>
        internal void Prepare(MeshWorld world, NativeArray<int> dirtyElements)
        {
            m_Prepared.Clear();
            m_Pending.Clear();
            int newCommands = 0;
            foreach (int element in dirtyElements)
            {
                m_Pending.Add(element);
                if (Qualifies(world, element, out MeshDrawCommandKey key, out _) && !m_Prepared.ContainsKey(key))
                {
                    m_Prepared.Add(key, Resolve(world, key));
                    if (!m_Lookup.ContainsKey(key)) newCommands++;
                }
            }
            // Free slots are reused before the table grows, so no command index exceeds the live count after Apply.
            if ((long)commandCount + newCommands > CommandMask)
                throw new InvalidOperationException($"Mesh pass '{desc.lightMode}' would hold more than {CommandMask} commands.");
        }

        internal void DiscardPrepared()
        {
            m_Prepared.Clear();
            m_Pending.Clear();
        }

        /// <summary>Publishes the prepared change: membership of every pending element is removed, then re-evaluated.</summary>
        internal void Apply(MeshWorld world)
        {
            bool changed = false;
            while (m_Candidates.Length < world.elementHighWater) { m_Candidates.Add(new uint2(0u, NoCommand)); m_CanFade.Add(0); }
            while (m_EntityMemberCount.Length < world.entityHighWater) m_EntityMemberCount.Add(0);

            foreach (int element in m_Pending)
            {
                uint2 candidate = m_Candidates[element];
                if (candidate.y == NoCommand) continue;
                int command = (int)(candidate.y & CommandMask);
                MeshDrawCommand record = m_Commands[command];
                record.refCount--;
                if (m_CanFade[element] != 0) record.fadingMemberCount--;
                m_CanFade[element] = 0;
                if (record.refCount == 0)
                {
                    m_Lookup.Remove(record.key);
                    m_FreeCommands.Add(command);
                    m_Bindings[command] = default;
                    commandCount--;
                }
                m_Commands[command] = record;
                m_EntityMemberCount[(int)candidate.x]--;
                memberCount--;
                m_Candidates[element] = new uint2(0u, NoCommand);
                changed = true;
            }

            foreach (int element in m_Pending)
            {
                if (!Qualifies(world, element, out MeshDrawCommandKey key, out int entity)) continue;
                MeshDrawCommandBinding binding = m_Prepared[key];
                if (m_Lookup.TryGetValue(key, out int command))
                {
                    m_Bindings[command] = binding;
                }
                else
                {
                    command = AllocateCommand(key, binding);
                }
                MeshDrawCommand record = m_Commands[command];
                record.refCount++;
                record.stateKey = StateKey(binding, key);
                bool canFade = world.entities[entity].lod.hasFadeBand;
                if (canFade) record.fadingMemberCount++;
                m_CanFade[element] = (byte)(canFade ? 1 : 0);
                m_Commands[command] = record;
                int lodIndex = world.elements[element].lodIndex;
                m_Candidates[element] = new uint2((uint)entity, (uint)command | ((uint)lodIndex << LodShift));
                m_EntityMemberCount[entity]++;
                memberCount++;
                changed = true;
            }

            m_Prepared.Clear();
            m_Pending.Clear();
            if (changed) unchecked { revision++; }
        }

        int AllocateCommand(in MeshDrawCommandKey key, in MeshDrawCommandBinding binding)
        {
            int command;
            if (m_FreeCommands.Length > 0)
            {
                command = m_FreeCommands[m_FreeCommands.Length - 1];
                m_FreeCommands.RemoveAt(m_FreeCommands.Length - 1);
                m_Commands[command] = new MeshDrawCommand { key = key };
                m_Bindings[command] = binding;
            }
            else
            {
                command = m_Commands.Length;
                m_Commands.Add(new MeshDrawCommand { key = key });
                m_Bindings.Add(binding);
            }
            m_Lookup.Add(key, command);
            commandCount++;
            return command;
        }

        MeshDrawCommandBinding Resolve(MeshWorld world, in MeshDrawCommandKey key)
        {
            MeshSection section = world.sections[key.section];
            MeshMaterial record = world.materials[key.material];
            Mesh mesh = UnityEntityId.ToObject<Mesh>(section.meshId);
            Material material = UnityEntityId.ToObject<Material>(record.materialId);
            if (mesh == null) throw new InvalidOperationException($"Mesh pass '{desc.lightMode}': mesh {section.meshId} no longer exists.");
            if (material == null) throw new InvalidOperationException($"Mesh pass '{desc.lightMode}': material {record.materialId} no longer exists.");
            if (section.submesh >= mesh.subMeshCount)
                throw new InvalidOperationException($"Mesh '{mesh.name}' has {mesh.subMeshCount} submeshes; an element draws submesh {section.submesh}.");
            int passIndex = MeshPassShaderUtility.FindPassIndex(material, desc.lightMode);
            if (passIndex < 0)
                throw new InvalidOperationException($"Material '{material.name}' routes to '{desc.lightMode}', but shader '{material.shader.name}' has no such pass.");
            SubMeshDescriptor subMesh = mesh.GetSubMesh(section.submesh);
            return new MeshDrawCommandBinding
            {
                mesh = mesh,
                material = material,
                passIndex = passIndex,
                submesh = section.submesh,
                indexCount = (uint)math.max(0, subMesh.indexCount),
                indexStart = (uint)math.max(0, subMesh.indexStart),
                baseVertex = (uint)math.max(0, subMesh.baseVertex)
            };
        }

        ulong StateKey(in MeshDrawCommandBinding binding, in MeshDrawCommandKey key)
        {
            var state = new ShaderState { shader = UnityEntityId.ToUInt64(binding.material.shader), pass = binding.passIndex };
            if (!m_StateRanks.TryGetValue(state, out int rank))
            {
                rank = m_StateRanks.Count;
                m_StateRanks.Add(state, rank);
            }
            return SortKeyPacking.Pack(SortKeyPacking.SaturateUnsigned(rank, 16), 48)
                | SortKeyPacking.Pack(SortKeyPacking.SaturateUnsigned(key.material, 24), 24)
                | SortKeyPacking.Pack(SortKeyPacking.SaturateUnsigned(key.section, 24), 0);
        }

        /// <summary>After a shader import, pass indices are resolved again before the next draw list records.</summary>
        internal void EnsureShaderBindings()
        {
            if (m_ShaderEpoch == MeshPassShaderUtility.shaderEpoch) return;
            m_StateRanks.Clear();
            for (int command = 0; command < m_Commands.Length; ++command)
            {
                MeshDrawCommand record = m_Commands[command];
                if (record.refCount == 0) continue;
                MeshDrawCommandBinding binding = m_Bindings[command];
                if (binding.material == null) continue;
                binding.passIndex = MeshPassShaderUtility.FindPassIndex(binding.material, desc.lightMode);
                if (binding.passIndex < 0)
                    throw new InvalidOperationException($"Material '{binding.material.name}' routes to '{desc.lightMode}', but shader '{binding.material.shader.name}' no longer has that pass.");
                m_Bindings[command] = binding;
                record.stateKey = StateKey(binding, record.key);
                m_Commands[command] = record;
            }
            m_ShaderEpoch = MeshPassShaderUtility.shaderEpoch;
            m_CommandMetaRevision = uint.MaxValue;
        }

        /// <summary>
        /// Per command slot (<see cref="MetaStride"/> uints): index count, start index, base vertex, and the first instance of
        /// its stable and its fading range in a draw list's instance buffers. Stable ranges fill [0, memberCount), fading
        /// ranges [memberCount, 2 * memberCount). Free slots have zero indices and empty ranges.
        /// </summary>
        internal NativeArray<uint> GetCommandMeta()
        {
            RefreshCommandTables();
            return m_CommandMeta;
        }

        /// <summary>Live command slots in state-key order: the submission order of the GPU backend.</summary>
        internal NativeArray<int> GetCommandOrder()
        {
            RefreshCommandTables();
            return m_CommandOrder.AsArray();
        }

        struct StateKeyOrder : IComparer<int>
        {
            public NativeArray<MeshDrawCommand> commands;
            public int Compare(int a, int b)
            {
                int c = commands[a].stateKey.CompareTo(commands[b].stateKey);
                return c != 0 ? c : a.CompareTo(b);
            }
        }

        void RefreshCommandTables()
        {
            if (m_CommandMetaRevision == revision && m_CommandMeta.IsCreated && m_CommandMeta.Length >= m_Commands.Length * MetaStride)
                return;
            int needed = math.max(MetaStride, m_Commands.Length * MetaStride);
            if (!m_CommandMeta.IsCreated || m_CommandMeta.Length < needed)
            {
                if (m_CommandMeta.IsCreated) m_CommandMeta.Dispose();
                m_CommandMeta = new NativeArray<uint>(math.ceilpow2(needed), Allocator.Persistent);
            }
            uint instanceBase = 0;
            for (int command = 0; command < m_Commands.Length; ++command)
            {
                MeshDrawCommand record = m_Commands[command];
                MeshDrawCommandBinding binding = m_Bindings[command];
                bool live = record.refCount > 0;
                m_CommandMeta[command * MetaStride + 0] = live ? binding.indexCount : 0u;
                m_CommandMeta[command * MetaStride + 1] = live ? binding.indexStart : 0u;
                m_CommandMeta[command * MetaStride + 2] = live ? binding.baseVertex : 0u;
                m_CommandMeta[command * MetaStride + 3] = instanceBase;
                m_CommandMeta[command * MetaStride + 4] = (uint)memberCount + instanceBase;
                if (live) instanceBase += (uint)record.refCount;
            }
            m_CommandOrder.Clear();
            for (int command = 0; command < m_Commands.Length; ++command)
                if (m_Commands[command].refCount > 0) m_CommandOrder.Add(command);
            m_CommandOrder.AsArray().Sort(new StateKeyOrder { commands = m_Commands.AsArray() });
            m_CommandMetaRevision = revision;
        }

        /// <summary>Uploads the candidate table when membership changed. Called from residency, outside graph recording.</summary>
        internal void UploadCandidates(ResourcePool pool, MeshWorldStats stats)
        {
            int needed = math.max(64, m_Candidates.Length);
            bool stale = m_UploadedRevision != revision || m_CandidateBuffer.buffer == null || m_CandidateCapacity < m_Candidates.Length;
            if (!stale) return;
            if (m_CandidateBuffer.buffer == null || m_CandidateCapacity < needed || !pool.CanWriteBuffer(m_CandidateBuffer))
            {
                BufferHandle previous = m_CandidateBuffer;
                int capacity = math.max(needed, m_CandidateCapacity);
                m_CandidateBuffer = pool.GetBuffer(new BufferDescriptor(capacity, sizeof(uint) * 2) { name = "MeshPassCandidates" });
                m_CandidateCapacity = capacity;
                if (previous.buffer != null) pool.ReleaseBuffer(previous);
            }
            if (m_Candidates.Length > 0) m_CandidateBuffer.buffer.SetData(m_Candidates.AsArray(), 0, 0, m_Candidates.Length);
            stats.uploadedBytes += (long)m_Candidates.Length * sizeof(uint) * 2;
            m_UploadedRevision = revision;
        }

        internal void ReleaseGPU(ResourcePool pool)
        {
            if (m_CandidateBuffer.buffer != null) pool.ReleaseBuffer(m_CandidateBuffer);
            m_CandidateBuffer = default;
            m_CandidateCapacity = 0;
            m_UploadedRevision = uint.MaxValue;
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            m_Candidates.Dispose();
            m_CanFade.Dispose();
            m_Commands.Dispose();
            m_Lookup.Dispose();
            m_FreeCommands.Dispose();
            m_EntityMemberCount.Dispose();
            m_Pending.Dispose();
            if (m_CommandMeta.IsCreated) m_CommandMeta.Dispose();
            m_CommandOrder.Dispose();
            m_Bindings.Clear();
        }
    }
}
