using System.Collections.Generic;
using System.Threading;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;

namespace InfinityTech.Rendering.RenderGraph
{
    /// <summary>
    /// Logical Unity RendererList handle. Not an ERGResourceType; lives in an independent registry.
    /// Record stores RendererListParams; ScriptableRenderContext creates the list after pass culling.
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
        public RendererListParams parameters;
        public ShadowDrawingSettings shadowSettings;
        public List<int> consumerPassIndices;
        public ERGRendererListCompileState state;
        public RendererList created;
    }

    // Creates the native list of a live record. The graph uses the ScriptableRenderContext creator; the generic loop
    // takes a struct so creation never allocates.
    internal interface IRGRendererListCreator
    {
        RendererList Create(ref RGRendererListRecord record);
    }

    internal readonly struct RGContextRendererListCreator : IRGRendererListCreator
    {
        readonly ScriptableRenderContext m_Context;

        internal RGContextRendererListCreator(ScriptableRenderContext context) => m_Context = context;

        public RendererList Create(ref RGRendererListRecord record)
        {
            if (record.kind == ERGRendererListKind.Shadow)
            {
                return m_Context.CreateShadowRendererList(ref record.shadowSettings);
            }
            return m_Context.CreateRendererList(ref record.parameters);
        }
    }

    /// <summary>
    /// Per-graph RendererList registry: Declare at record, create on the context after culling, submit only created lists.
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
        public int CreateCallCount
        {
            get;
            private set;
        }
        public int LiveCount
        {
            get;
            private set;
        }

        public bool IsLive(in RGRendererListHandle list)
        {
            return list.contextId == m_ContextId
                && list.generation == m_GraphGeneration
                && list.index >= 0
                && list.index < m_Records.Count;
        }

        public RGRendererListHandle Declare(in RendererListParams parameters)
        {
            int index = m_Records.Count;
            m_Records.Add(new RGRendererListRecord
            {
                kind = ERGRendererListKind.Renderers,
                parameters = parameters,
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

        public void CreateLive<TCreator>(in TCreator creator) where TCreator : struct, IRGRendererListCreator
        {
            for (int i = 0; i < m_Records.Count; ++i)
            {
                RGRendererListRecord record = m_Records[i];
                if (record.state != ERGRendererListCompileState.Live)
                {
                    continue;
                }
                CreateCallCount++;
                record.created = creator.Create(ref record);
                record.state = ERGRendererListCompileState.Created;
                m_Records[i] = record;
            }
        }

        // A consumer draws only a list this graph created for it; anything else is a recording defect.
        public void Submit(CommandBuffer cmdBuffer, in RGRendererListHandle list)
        {
            if (!IsLive(list))
            {
                throw new System.InvalidOperationException("Renderer list handle belongs to another graph or an earlier frame.");
            }
            RGRendererListRecord record = m_Records[list.index];
            if (record.state != ERGRendererListCompileState.Created || !record.created.isValid)
            {
                throw new System.InvalidOperationException($"Renderer list {list.index} was not created for this pass (state {record.state}).");
            }
            cmdBuffer.DrawRendererList(record.created);
        }

        public void ReleaseAll()
        {
            for (int i = 0; i < m_Records.Count; ++i)
            {
                RGRendererListRecord record = m_Records[i];
                record.consumerPassIndices.Clear();
                m_Consumers.Push(record.consumerPassIndices);
                record.created = RendererList.nullRendererList;
                record.state = ERGRendererListCompileState.Released;
                m_Records[i] = record;
            }

            m_Records.Clear();
            CreateCallCount = 0;
            LiveCount = 0;
            unchecked
            {
                ++m_GraphGeneration;
            }
        }
    }
}
