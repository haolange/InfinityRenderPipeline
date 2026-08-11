using System;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// Counters for Mesh Drawing Pipeline invariants, alloc pressure, template cache, and GPU backend.
    /// </summary>
    public static class MeshPipelineDiagnostics
    {
        public static int RegisteredInstances;
        public static int RegisteredDraws;
        public static int TransformRecords;
        public static float MatrixDuplicateRatio;
        public static int TempAllocCount;
        public static int CulledPassSkippedBuilds;
        public static int TemplateCacheHits;
        public static int TemplateCacheMisses;
        public static int GpuOverflowCount;
        public static int LocalShadowBudgetDropped;

        public struct SnapshotData
        {
            public int RegisteredInstances;
            public int RegisteredDraws;
            public int TransformRecords;
            public float MatrixDuplicateRatio;
            public int TempAllocCount;
            public int CulledPassSkippedBuilds;
            public int TemplateCacheHits;
            public int TemplateCacheMisses;
            public int GpuOverflowCount;
            public int LocalShadowBudgetDropped;
        }

        public static SnapshotData Snapshot()
        {
            return new SnapshotData
            {
                RegisteredInstances = RegisteredInstances,
                RegisteredDraws = RegisteredDraws,
                TransformRecords = TransformRecords,
                MatrixDuplicateRatio = MatrixDuplicateRatio,
                TempAllocCount = TempAllocCount,
                CulledPassSkippedBuilds = CulledPassSkippedBuilds,
                TemplateCacheHits = TemplateCacheHits,
                TemplateCacheMisses = TemplateCacheMisses,
                GpuOverflowCount = GpuOverflowCount,
                LocalShadowBudgetDropped = LocalShadowBudgetDropped
            };
        }

        public static void Reset()
        {
            RegisteredInstances = 0;
            RegisteredDraws = 0;
            TransformRecords = 0;
            MatrixDuplicateRatio = 0.0f;
            TempAllocCount = 0;
            CulledPassSkippedBuilds = 0;
            TemplateCacheHits = 0;
            TemplateCacheMisses = 0;
            GpuOverflowCount = 0;
            LocalShadowBudgetDropped = 0;
        }

        public static void PublishFromScene(MeshScene scene)
        {
            if (scene == null)
            {
                return;
            }

            RegisteredInstances = scene.LogicalInstanceCount;
            RegisteredDraws = scene.DrawCount;
            TransformRecords = scene.TransformCount;
            MatrixDuplicateRatio = scene.MatrixDuplicateRatio;
        }
    }
}
