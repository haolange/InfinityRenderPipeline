using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Core;
using InfinityTech.Core.Geometry;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// Resident GPU CandidateTable per MeshPassId. Uploaded only when PassBin / CommandTable rebuilds.
    /// CpuDirect never builds this table.
    /// </summary>
    internal sealed class MeshCandidateTableStore : IDisposable
    {
        internal struct PassTable
        {
            public ComputeBuffer candidateTable;
            public MeshDrawCommand[] commands;
            public uint[] commandMeta;
            public uint[] candidateOffsets;
            public uint[] candidateCounts;
            public uint2[] candidates;
            public int commandCount;
            public int candidateCount;
            public int builtEpoch;
            public int capacityCandidates;
        }

        private readonly MeshScene m_Scene;
        private readonly PassBinStore m_Bins;
        private readonly PassTable[] m_Tables = new PassTable[PassRegistry.Count];
        private bool m_Disposed;

        public MeshCandidateTableStore(MeshScene scene, PassBinStore bins)
        {
            m_Scene = scene ?? throw new ArgumentNullException(nameof(scene));
            m_Bins = bins ?? throw new ArgumentNullException(nameof(bins));
            for (int i = 0; i < m_Tables.Length; ++i)
            {
                m_Tables[i].builtEpoch = int.MinValue;
            }
        }

        public ref PassTable Ensure(MeshPassId passId)
        {
            int index = (int)passId;
            m_Bins.EnsureRebuilt(passId);
            int epoch = m_Bins.GetBuiltEpoch(passId);
            if (m_Tables[index].builtEpoch == epoch && m_Tables[index].commands != null)
            {
                return ref m_Tables[index];
            }

            Rebuild(passId, ref m_Tables[index], epoch);
            return ref m_Tables[index];
        }

        public PassView BuildPassView(MeshPassId passId)
        {
            ref PassTable table = ref Ensure(passId);
            if (table.commandCount <= 0)
            {
                return PassView.Invalid;
            }

            return new PassView
            {
                isValid = true,
                commandCount = table.commandCount,
                instanceCount = table.candidateCount
            };
        }

        public MeshDrawGpuStaging CreateStaging(MeshPassId passId, in MeshView view, int boundsInstanceCount)
        {
            MeshDrawGpuStaging staging = CreateStagingCore(passId, boundsInstanceCount);
            if (staging == null)
            {
                return null;
            }

            view.CopyFrustumPlanes(staging.frustumPlanes);
            return staging;
        }

        public MeshDrawGpuStaging CreateStaging(MeshPassId passId, in MeshViewCullingResult culling, int boundsInstanceCount)
        {
            MeshDrawGpuStaging staging = CreateStagingCore(passId, boundsInstanceCount);
            if (staging == null)
            {
                return null;
            }

            if (culling.isValid && culling.frustum.IsCreated)
            {
                int planeCount = math.min(6, culling.frustum.Length);
                for (int i = 0; i < planeCount; ++i)
                {
                    FPlane plane = culling.frustum[i];
                    staging.frustumPlanes[i] = new Vector4(plane.normalDist.x, plane.normalDist.y, plane.normalDist.z, plane.normalDist.w);
                }
            }

            return staging;
        }

        MeshDrawGpuStaging CreateStagingCore(MeshPassId passId, int boundsInstanceCount)
        {
            ref PassTable table = ref Ensure(passId);
            if (table.commandCount <= 0)
            {
                return null;
            }

            return new MeshDrawGpuStaging
            {
                commandCount = table.commandCount,
                candidateCount = table.candidateCount,
                boundsInstanceCount = math.max(1, boundsInstanceCount),
                commandMeta = table.commandMeta,
                candidateOffsets = table.candidateOffsets,
                candidateCounts = table.candidateCounts,
                drawCommands = table.commands,
                frustumPlanes = new Vector4[6],
                isValid = true
            };
        }

        public ComputeBuffer GetCandidateBuffer(MeshPassId passId)
        {
            return Ensure(passId).candidateTable;
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }

            m_Disposed = true;
            for (int i = 0; i < m_Tables.Length; ++i)
            {
                m_Tables[i].candidateTable?.Release();
                m_Tables[i].candidateTable = null;
            }
        }

        void Rebuild(MeshPassId passId, ref PassTable table, int epoch)
        {
            NativeArray<MeshPassCommand> binCommands = m_Bins.GetCommands(passId);
            NativeArray<int> members = m_Bins.GetMemberDrawIndices(passId);
            NativeArray<MeshDraw> draws = m_Scene.GetDraws();

            int rawCount = binCommands.Length;
            var commands = new System.Collections.Generic.List<MeshDrawCommand>(rawCount);
            var metas = new System.Collections.Generic.List<uint>(rawCount * 4);
            var offsets = new System.Collections.Generic.List<uint>(rawCount);
            var counts = new System.Collections.Generic.List<uint>(rawCount);
            var candidates = new System.Collections.Generic.List<uint2>(math.max(1, members.Length));

            for (int commandIndex = 0; commandIndex < rawCount; ++commandIndex)
            {
                MeshPassCommand passCommand = binCommands[commandIndex];
                int remaining = passCommand.memberCount;
                int memberCursor = passCommand.memberBegin;
                while (remaining > 0)
                {
                    int span = math.min(remaining, MeshDrawGPUBackend.MaxInstances);
                    int emitted = 0;
                    int baseOffset = candidates.Count;
                    for (int i = 0; i < span; ++i)
                    {
                        int drawIndex = members[memberCursor + i];
                        if (drawIndex < 0 || drawIndex >= draws.Length)
                        {
                            continue;
                        }

                        MeshDraw draw = draws[drawIndex];
                        if (!draw.instance.IsValid)
                        {
                            continue;
                        }

                        candidates.Add(new uint2(draw.instance.Index, (uint)commands.Count));
                        emitted++;
                    }

                    uint indexCount = 0;
                    uint startIndex = 0;
                    uint baseVertex = 0;
                    Mesh mesh = UnityEntityId.ToObject<Mesh>(passCommand.key.meshUnityId);
                    if (mesh != null && passCommand.key.sectionIndex >= 0 && passCommand.key.sectionIndex < mesh.subMeshCount)
                    {
                        SubMeshDescriptor subMesh = mesh.GetSubMesh(passCommand.key.sectionIndex);
                        indexCount = (uint)math.max(0, subMesh.indexCount);
                        startIndex = (uint)math.max(0, subMesh.indexStart);
                        baseVertex = (uint)math.max(0, subMesh.baseVertex);
                    }

                    commands.Add(new MeshDrawCommand(
                        passCommand.key.meshUnityId,
                        passCommand.key.sectionIndex,
                        passCommand.key.materialUnityId,
                        new int2(emitted, baseOffset),
                        passCommand.key.bakedTextureSet));
                    metas.Add(indexCount);
                    metas.Add(startIndex);
                    metas.Add(baseVertex);
                    metas.Add((uint)baseOffset);
                    offsets.Add((uint)baseOffset);
                    counts.Add((uint)emitted);

                    remaining -= span;
                    memberCursor += span;
                }
            }

            table.commands = commands.ToArray();
            table.commandMeta = metas.ToArray();
            table.candidateOffsets = offsets.ToArray();
            table.candidateCounts = counts.ToArray();
            table.candidates = candidates.ToArray();
            table.commandCount = table.commands.Length;
            table.candidateCount = table.candidates.Length;
            table.builtEpoch = epoch;

            int needed = math.max(1, table.candidateCount);
            if (table.candidateTable == null || table.capacityCandidates < needed)
            {
                table.candidateTable?.Release();
                table.capacityCandidates = math.max(needed, 256);
                table.candidateTable = new ComputeBuffer(table.capacityCandidates, sizeof(uint) * 2, ComputeBufferType.Structured);
            }

            if (table.candidateCount > 0)
            {
                table.candidateTable.SetData(table.candidates, 0, 0, table.candidateCount);
                MeshPipelineDiagnostics.CandidateUploadsBytes += table.candidateCount * sizeof(uint) * 2;
            }
        }
    }
}
