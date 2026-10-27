using System.Collections.Generic;
using System.Threading;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;

namespace InfinityTech.Rendering.RenderGraph
{
    /// <summary>
    /// Logical Unity RendererList handle. Not an ERGResourceType; lives in an independent registry.
    /// Record stores a descriptor; ScriptableRenderContext.Create happens after pass culling.
    /// </summary>
    public readonly struct RGRendererListHandle
    {
        internal readonly int contextId;
        internal readonly int index;
        internal readonly int generation;

        internal RGRendererListHandle(int contextId, int index, int generation)
        {
            this.contextId = contextId;
            this.index = index;
            this.generation = generation;
        }

        public bool IsValid => contextId > 0;

        public static RGRendererListHandle Invalid => new RGRendererListHandle(-1, 0, 0);
    }

    internal enum ERGRendererListKind : byte
    {
        Renderers = 0,
        Shadow = 1
    }

    internal enum ERGRendererListCompileState : byte
    {
        Declared = 0,
        Live = 1,
        Created = 2,
        Released = 3
    }

    internal struct RGRendererListRecord
    {
        public ERGRendererListKind kind;
        public RendererListDesc desc;
        public ShadowDrawingSettings shadowSettings;
        public List<int> consumerPassIndices;
        public ERGRendererListCompileState state;
        public RendererList created;
    }

    internal delegate RendererList RGRendererListCreateFn(RGRendererListRecord record);

    /// <summary>
    /// Per-graph RendererList registry: Create(desc) at record, Create on SRC after cull.
    /// </summary>
    internal sealed class RGRendererListContext
    {
        private static int s_NextContextId;

        private readonly int m_ContextId;
        private readonly List<RGRendererListRecord> m_Records = new List<RGRendererListRecord>(16);
        private int m_GraphGeneration = 1;

        readonly Stack<List<int>> m_Consumers = new Stack<List<int>>();
        List<int> RentConsumers() => m_Consumers.Count > 0 ? m_Consumers.Pop() : new List<int>(4);

        public RGRendererListContext()
        {
            m_ContextId = Interlocked.Increment(ref s_NextContextId);
        }

        public int Count => m_Records.Count;
        public int GraphGeneration => m_GraphGeneration;
        public int ContextId => m_ContextId;
        public int CreateCallCount { get; private set; }
        public int LiveCount { get; private set; }

        public bool IsLive(in RGRendererListHandle list)
        {
            return list.contextId == m_ContextId
                && list.generation == m_GraphGeneration
                && list.index >= 0
                && list.index < m_Records.Count;
        }

        public RGRendererListHandle Declare(in RendererListDesc desc)
        {
            int index = m_Records.Count;
            m_Records.Add(new RGRendererListRecord
            {
                kind = ERGRendererListKind.Renderers,
                desc = desc,
                consumerPassIndices = RentConsumers(),
                state = ERGRendererListCompileState.Declared,
                created = RendererList.nullRendererList
            });
            return new RGRendererListHandle(m_ContextId, index, m_GraphGeneration);
        }

        public RGRendererListHandle DeclareShadow(in ShadowDrawingSettings settings)
        {
            int index = m_Records.Count;
            m_Records.Add(new RGRendererListRecord
            {
                kind = ERGRendererListKind.Shadow,
                shadowSettings = settings,
                consumerPassIndices = RentConsumers(),
                state = ERGRendererListCompileState.Declared,
                created = RendererList.nullRendererList
            });
            return new RGRendererListHandle(m_ContextId, index, m_GraphGeneration);
        }

        public ERGRendererListCompileState GetState(in RGRendererListHandle list)
        {
            if (!IsLive(list))
            {
                return ERGRendererListCompileState.Released;
            }

            return m_Records[list.index].state;
        }

        public void ClearConsumers()
        {
            for (int i = 0; i < m_Records.Count; ++i)
            {
                RGRendererListRecord record = m_Records[i];
                record.consumerPassIndices.Clear();
                if (record.state != ERGRendererListCompileState.Released)
                {
                    record.state = ERGRendererListCompileState.Declared;
                    record.created = RendererList.nullRendererList;
                }
                m_Records[i] = record;
            }
        }

        public void MarkLiveConsumer(int listIndex, int passIndex)
        {
            RGRendererListRecord record = m_Records[listIndex];
            record.consumerPassIndices.Add(passIndex);
            if (record.state == ERGRendererListCompileState.Declared)
            {
                record.state = ERGRendererListCompileState.Live;
            }
            m_Records[listIndex] = record;
        }

        public void ReleaseUnused()
        {
            LiveCount = 0;
            for (int i = 0; i < m_Records.Count; ++i)
            {
                RGRendererListRecord record = m_Records[i];
                if (record.state == ERGRendererListCompileState.Declared)
                {
                    record.state = ERGRendererListCompileState.Released;
                    m_Records[i] = record;
                    continue;
                }

                if (record.state == ERGRendererListCompileState.Live)
                {
                    LiveCount++;
                }
            }
        }

        public void CreateLive(ScriptableRenderContext context)
        {
            for (int i = 0; i < m_Records.Count; ++i)
            {
                var record = m_Records[i];
                if (record.state != ERGRendererListCompileState.Live) continue;
                CreateCallCount++;
                if (record.kind == ERGRendererListKind.Shadow)
                {
                    var settings = record.shadowSettings;
                    record.created = context.CreateShadowRendererList(ref settings);
                }
                else record.created = context.CreateRendererList(record.desc);
                record.state = ERGRendererListCompileState.Created;
                m_Records[i] = record;
            }
        }

        public void CreateLiveWith(RGRendererListCreateFn create)
        {
            if (create == null)
            {
                throw new System.ArgumentNullException(nameof(create));
            }

            for (int i = 0; i < m_Records.Count; ++i)
            {
                RGRendererListRecord record = m_Records[i];
                if (record.state != ERGRendererListCompileState.Live)
                {
                    continue;
                }

                CreateCallCount++;
                record.created = create(record);
                record.state = ERGRendererListCompileState.Created;
                m_Records[i] = record;
            }
        }

        public bool TryGetCreated(in RGRendererListHandle list, out RendererList created)
        {
            if (!IsLive(list))
            {
                created = RendererList.nullRendererList;
                return false;
            }

            RGRendererListRecord record = m_Records[list.index];
            if (record.state != ERGRendererListCompileState.Created)
            {
                created = RendererList.nullRendererList;
                return false;
            }

            created = record.created;
            return created.isValid;
        }

        public void Submit(CommandBuffer cmdBuffer, in RGRendererListHandle list)
        {
            if (cmdBuffer == null || !TryGetCreated(list, out RendererList created))
            {
                return;
            }

            cmdBuffer.DrawRendererList(created);
        }

        public void ReleaseAll()
        {
            for (int i = 0; i < m_Records.Count; ++i)
            {
                RGRendererListRecord record = m_Records[i];
                record.consumerPassIndices.Clear(); m_Consumers.Push(record.consumerPassIndices);
                record.created = RendererList.nullRendererList;
                record.state = ERGRendererListCompileState.Released;
                m_Records[i] = record;
            }

            m_Records.Clear();
            CreateCallCount = 0;
            LiveCount = 0;
            unchecked { ++m_GraphGeneration; }
        }
    }
}
