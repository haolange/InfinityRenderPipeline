using System;
using System.Collections.Generic;
using System.Text;

namespace InfinityTech.Rendering.RenderGraph
{
    // One pass of a compiled graph as the RenderGraph Viewer shows it. Resource lists hold RGGraphSnapshot.resources
    // indices.
    internal struct RGPassSnapshot
    {
        public string name;
        public EPassType type;
        public bool asyncCompute;
        public bool culled;
        public bool allowCulling;
        // Pass index this pass waits for (-1 none) and the first pass waiting for it (-1 none).
        public int waitsForPass;
        public int signalsToPass;
        public bool graphicsFence;
        public int segment;
        public int mergeGroup;
        public int subpass;
        public string cullReason;
        public int[] reads;
        public int[] writes;
        public int[] creates;
        public int[] releases;
    }

    internal struct RGResourceSnapshot
    {
        public ERGResourceType type;
        public int index;
        public string name;
        public bool imported;
        public string description;
        // First and last live (not culled) pass that reads or writes it; -1 when unused.
        public int firstUse;
        public int lastUse;
        // Pass that allocates it from the pool and pass after which it returns there (-1: imported or never live).
        public int createPass;
        public int releasePass;
        public int aliasSlot;
        public int aliasGroup;
        public long estimatedBytes;
        public bool memoryless;
    }

    // A compiled graph: passes in record order, then buffers and textures.
    internal sealed class RGGraphSnapshot
    {
        public string label;
        public int frame;
        public int segmentCount;
        public long peakAliasBytes;
        public readonly List<RGPassSnapshot> passes = new List<RGPassSnapshot>();
        public readonly List<RGResourceSnapshot> resources = new List<RGResourceSnapshot>();

        public int CountCulled()
        {
            int count = 0;
            foreach (RGPassSnapshot pass in passes) if (pass.culled) count++;
            return count;
        }

        public int CountAsync()
        {
            int count = 0;
            foreach (RGPassSnapshot pass in passes) if (pass.asyncCompute && !pass.culled) count++;
            return count;
        }

        public string ToText()
        {
            var text = new StringBuilder();
            text.Append("graph ").Append(label).Append(" frame ").Append(frame).AppendLine();
            for (int i = 0; i < passes.Count; i++)
            {
                RGPassSnapshot pass = passes[i];
                text.Append(i).Append(' ').Append(pass.name).Append(" [").Append(pass.type).Append(pass.asyncCompute ? ", async" : "")
                    .Append(pass.culled ? ", culled" : "").Append(" segment ").Append(pass.segment);
                if (pass.mergeGroup >= 0) text.Append(" merge ").Append(pass.mergeGroup).Append('.').Append(pass.subpass);
                text.Append(']');
                if (pass.culled && !string.IsNullOrEmpty(pass.cullReason)) text.Append(" (").Append(pass.cullReason).Append(')');
                if (pass.waitsForPass >= 0) text.Append(" waits ").Append(pass.waitsForPass);
                text.AppendLine();
                AppendResources(text, "  reads", pass.reads);
                AppendResources(text, "  writes", pass.writes);
                AppendResources(text, "  creates", pass.creates);
                AppendResources(text, "  releases", pass.releases);
            }
            return text.ToString();
        }

        void AppendResources(StringBuilder text, string label, int[] list)
        {
            if (list == null || list.Length == 0) return;
            text.Append(label).Append(':');
            foreach (int resource in list) text.Append(' ').Append(resources[resource].name);
            text.AppendLine();
        }
    }

    /// <summary>
    /// Hook of the RenderGraph Viewer (Editor/RenderGraph/InfinityRenderGraphViewer.cs). While a viewer is open every
    /// executed graph publishes a snapshot right after compilation, keyed by its label (the camera); nothing is
    /// recorded or allocated otherwise.
    /// </summary>
    internal static class RGDebugSession
    {
        static readonly Dictionary<string, RGGraphSnapshot> s_Latest = new Dictionary<string, RGGraphSnapshot>();

        public static bool captureEnabled;
        // Keeps the current snapshots while the viewer inspects them.
        public static bool paused;
        public static event Action changed;

        public static IReadOnlyDictionary<string, RGGraphSnapshot> latest => s_Latest;

        internal static void Publish(RGGraphSnapshot snapshot)
        {
            if (paused || snapshot == null) return;
            s_Latest[snapshot.label ?? string.Empty] = snapshot;
            changed?.Invoke();
        }

        public static void Clear()
        {
            s_Latest.Clear();
            changed?.Invoke();
        }
    }
}
