using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.LightPipeline
{
    internal struct FShadowAllocatorSettings
    {
        public int cascadeMapResolution;
        public int localMapResolution;
        public float shadowDistance;
        public Vector3 cascadeRatios;
        public int maxLocalLights;
    }

    internal struct FLocalShadowSlice
    {
        public int visibleLightIndex;
        public int recordIndex;
        public int face;
        public int matrixIndex;
        public bool isSpot;
        public Vector4 atlasPixelRect;
        public Vector4 atlasUVRect;
        public Matrix4x4 shadowMatrix;
        public ShadowSplitData splitData;
        public bool valid;
    }

    internal struct FCascadeShadowSlice
    {
        public Matrix4x4 shadowMatrix;
        public Vector4 atlasPixelRect;
        public Vector4 atlasUVRect;
        public ShadowSplitData splitData;
        public bool valid;
    }

    /// <summary>
    /// Cascade: 4 tiles in a 2x2 atlas for the first directional that Unity culls as a shadow caster.
    /// Local atlas: Spot = 1 tile, Point = 6 faces, Rect = 0 (no shadow).
    /// Writes atlas UV / matrix index back into FLightRecord. Runs before GPU light upload.
    /// </summary>
    internal sealed class ShadowAllocator
    {
        public const int CascadeCount = 4;

        public int CascadeVisibleLightIndex { get; private set; } = -1;
        public int CascadeRecordIndex { get; private set; } = -1;
        public int CascadeAllocatedCount { get; private set; }
        public Vector4 CascadeSplitDistances { get; private set; }
        public readonly FCascadeShadowSlice[] CascadeSlices = new FCascadeShadowSlice[CascadeCount];
        public readonly Matrix4x4[] CascadeMatrices = new Matrix4x4[CascadeCount];

        public int LocalSliceCount { get; private set; }
        public FLocalShadowSlice[] LocalSlices = Array.Empty<FLocalShadowSlice>();
        public Matrix4x4[] LocalMatrices = Array.Empty<Matrix4x4>();
        public Vector4[] LocalUVRects = Array.Empty<Vector4>();

        public static int TileCountForType(ELightType type)
        {
            switch (type)
            {
                case ELightType.Spot:
                    return 1;
                case ELightType.Point:
                    return 6;
                default:
                    return 0;
            }
        }

        public void Reset()
        {
            CascadeVisibleLightIndex = -1;
            CascadeRecordIndex = -1;
            CascadeAllocatedCount = 0;
            CascadeSplitDistances = Vector4.zero;
            LocalSliceCount = 0;
            for (int i = 0; i < CascadeCount; ++i)
            {
                CascadeSlices[i] = default;
                CascadeMatrices[i] = Matrix4x4.identity;
            }
        }

        public void Allocate(
            NativeList<FLightRecord> records,
            in CullingResults cullingResults,
            Camera camera,
            in FShadowAllocatorSettings settings)
        {
            Reset();

            int cascadeRes = math.max(1, settings.cascadeMapResolution);
            int localRes = math.max(1, settings.localMapResolution);
            int tileResolution = math.max(1, localRes / 4);
            int tilesPerRow = math.max(1, localRes / tileResolution);
            int tileBudget = tilesPerRow * tilesPerRow;
            int maxLocalLights = math.max(1, settings.maxLocalLights);
            Vector3 cameraPosition = camera != null ? camera.transform.position : Vector3.zero;

            AllocateCascade(records, cullingResults, settings, cascadeRes);
            AllocateLocal(records, cullingResults, cameraPosition, settings, localRes, tileResolution, tilesPerRow, tileBudget, maxLocalLights);
        }

        void AllocateCascade(
            NativeList<FLightRecord> records,
            in CullingResults cullingResults,
            in FShadowAllocatorSettings settings,
            int cascadeRes)
        {
            int halfRes = cascadeRes;
            Vector3 ratios = settings.cascadeRatios;
            if (ratios.x <= 0.0f)
            {
                ratios = new Vector3(0.067f, 0.2f, 0.467f);
            }

            for (int i = 0; i < records.Length; ++i)
            {
                FLightRecord record = records[i];
                if (record.lightType != (int)ELightType.Directional || record.visibleLightIndex < 0)
                {
                    continue;
                }

                VisibleLight visible = cullingResults.visibleLights[record.visibleLightIndex];
                Light light = visible.light;
                if (record.unused0 == 0 || light == null || light.shadows == LightShadows.None)
                {
                    continue;
                }

                if (!cullingResults.GetShadowCasterBounds(record.visibleLightIndex, out _))
                {
                    continue;
                }

                CascadeVisibleLightIndex = record.visibleLightIndex;
                CascadeRecordIndex = i;
                Vector4 splits = Vector4.zero;
                int allocated = 0;
                for (int cascade = 0; cascade < CascadeCount; ++cascade)
                {
                    int col = cascade % 2;
                    int row = cascade / 2;
                    FCascadeShadowSlice slice;
                    slice.atlasPixelRect = new Vector4(col * halfRes, row * halfRes, halfRes, halfRes);
                    slice.atlasUVRect = new Vector4(col * 0.5f, row * 0.5f, 0.5f, 0.5f);
                    slice.shadowMatrix = Matrix4x4.identity;
                    slice.splitData = default;
                    slice.valid = cullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(
                        record.visibleLightIndex,
                        cascade,
                        CascadeCount,
                        ratios,
                        halfRes,
                        light.shadowNearPlane,
                        out Matrix4x4 viewMatrix,
                        out Matrix4x4 projMatrix,
                        out slice.splitData);
                    if (slice.valid)
                    {
                        slice.shadowMatrix = MakeShadowMatrix(projMatrix, viewMatrix, invertPointView: false);
                    }

                    CascadeSlices[cascade] = slice;
                    CascadeMatrices[cascade] = slice.shadowMatrix;
                    splits[cascade] = CascadeSplit(cascade, settings.shadowDistance);
                    allocated++;
                }

                CascadeAllocatedCount = allocated;
                CascadeSplitDistances = splits;
                record.shadowAtlasRect = CascadeSlices[0].atlasUVRect;
                record.shadowMatrixIndex = 0;
                record.shadowSliceCount = CascadeCount;
                record.flags |= FLightRecordFlags.EnableShadow;
                records[i] = record;
                return;
            }
        }

        static float CascadeSplit(int cascade, float shadowDistance)
        {
            float[] ratios = { 0.067f, 0.2f, 0.467f, 1.0f };
            return ratios[cascade] * shadowDistance;
        }

        void AllocateLocal(
            NativeList<FLightRecord> records,
            in CullingResults cullingResults,
            Vector3 cameraPosition,
            in FShadowAllocatorSettings settings,
            int localRes,
            int tileResolution,
            int tilesPerRow,
            int tileBudget,
            int maxLocalLights)
        {
            int candidateCount = 0;
            for (int i = 0; i < records.Length; ++i)
            {
                FLightRecord record = records[i];
                ELightType type = (ELightType)record.lightType;
                if (TileCountForType(type) <= 0 || record.visibleLightIndex < 0)
                {
                    continue;
                }

                VisibleLight visible = cullingResults.visibleLights[record.visibleLightIndex];
                if (record.unused0 == 0 || visible.light == null || visible.light.shadows == LightShadows.None || !cullingResults.GetShadowCasterBounds(record.visibleLightIndex, out _))
                {
                    continue;
                }

                candidateCount++;
            }

            var scores = new NativeArray<float>(math.max(1, candidateCount), Allocator.Temp);
            var recordIndices = new NativeArray<int>(math.max(1, candidateCount), Allocator.Temp);
            int write = 0;
            for (int i = 0; i < records.Length; ++i)
            {
                FLightRecord record = records[i];
                ELightType type = (ELightType)record.lightType;
                if (TileCountForType(type) <= 0 || record.visibleLightIndex < 0)
                {
                    continue;
                }

                VisibleLight visible = cullingResults.visibleLights[record.visibleLightIndex];
                if (record.unused0 == 0 || visible.light == null || visible.light.shadows == LightShadows.None || !cullingResults.GetShadowCasterBounds(record.visibleLightIndex, out _))
                {
                    continue;
                }

                Vector3 lightPosition = visible.localToWorldMatrix.GetColumn(3);
                float distance = math.max(0.01f, Vector3.Distance(cameraPosition, lightPosition));
                float score = visible.light.shadowStrength / distance;
                if (type == ELightType.Spot)
                {
                    score += 1e-4f;
                }

                scores[write] = score;
                recordIndices[write] = i;
                write++;
            }

            var order = new int[write];
            for (int i = 0; i < write; ++i)
            {
                order[i] = i;
            }

            Array.Sort(order, (a, b) =>
            {
                int cmp = scores[b].CompareTo(scores[a]);
                if (cmp != 0)
                {
                    return cmp;
                }

                return recordIndices[a].CompareTo(recordIndices[b]);
            });

            int tilesUsed = 0;
            int accepted = 0;
            var acceptedRecords = new int[math.min(maxLocalLights, write)];
            for (int o = 0; o < write; ++o)
            {
                int recordIndex = recordIndices[order[o]];
                FLightRecord record = records[recordIndex];
                int faces = TileCountForType((ELightType)record.lightType);
                if (accepted >= maxLocalLights || tilesUsed + faces > tileBudget)
                {
                    MeshPipelineDiagnostics.LocalShadowBudgetDropped++;
                    continue;
                }

                acceptedRecords[accepted++] = recordIndex;
                tilesUsed += faces;
            }

            EnsureLocalCapacity(math.max(1, tilesUsed));
            LocalSliceCount = 0;
            int nextSlice = 0;
            for (int a = 0; a < accepted; ++a)
            {
                int recordIndex = acceptedRecords[a];
                FLightRecord record = records[recordIndex];
                ELightType type = (ELightType)record.lightType;
                int faces = TileCountForType(type);
                int visibleIndex = record.visibleLightIndex;
                VisibleLight visible = cullingResults.visibleLights[visibleIndex];
                bool isSpot = type == ELightType.Spot;
                int firstSlice = nextSlice;

                for (int face = 0; face < faces; ++face)
                {
                    int slice = nextSlice + face;
                    int col = slice % tilesPerRow;
                    int row = slice / tilesPerRow;
                    float x = col * tileResolution;
                    float y = row * tileResolution;
                    FLocalShadowSlice local;
                    local.visibleLightIndex = visibleIndex;
                    local.recordIndex = recordIndex;
                    local.face = face;
                    local.matrixIndex = slice;
                    local.isSpot = isSpot;
                    local.atlasPixelRect = new Vector4(x, y, tileResolution, tileResolution);
                    local.atlasUVRect = new Vector4(x / localRes, y / localRes, (float)tileResolution / localRes, (float)tileResolution / localRes);
                    local.shadowMatrix = Matrix4x4.identity;
                    local.splitData = default;
                    local.valid = false;

                    if (isSpot)
                    {
                        local.valid = cullingResults.ComputeSpotShadowMatricesAndCullingPrimitives(
                            visibleIndex, out Matrix4x4 viewMatrix, out Matrix4x4 projMatrix, out local.splitData);
                        if (local.valid)
                        {
                            local.shadowMatrix = MakeShadowMatrix(projMatrix, viewMatrix, invertPointView: false);
                        }
                    }
                    else
                    {
                        local.valid = cullingResults.ComputePointShadowMatricesAndCullingPrimitives(
                            visibleIndex, (CubemapFace)face, visible.light.shadowNearPlane,
                            out Matrix4x4 viewMatrix, out Matrix4x4 projMatrix, out local.splitData);
                        if (local.valid)
                        {
                            local.shadowMatrix = MakeShadowMatrix(projMatrix, viewMatrix, invertPointView: false);
                        }
                    }

                    LocalSlices[slice] = local;
                    LocalMatrices[slice] = local.shadowMatrix;
                    LocalUVRects[slice] = local.atlasUVRect;
                }

                record.shadowAtlasRect = LocalSlices[firstSlice].atlasUVRect;
                record.shadowMatrixIndex = firstSlice;
                record.shadowSliceCount = faces;
                record.flags |= FLightRecordFlags.EnableShadow;
                records[recordIndex] = record;
                nextSlice += faces;
            }

            LocalSliceCount = nextSlice;
            scores.Dispose();
            recordIndices.Dispose();
        }

        static Matrix4x4 MakeShadowMatrix(Matrix4x4 projMatrix, Matrix4x4 viewMatrix, bool invertPointView)
        {
            if (invertPointView)
            {
                viewMatrix.m10 = -viewMatrix.m10;
                viewMatrix.m11 = -viewMatrix.m11;
                viewMatrix.m12 = -viewMatrix.m12;
                viewMatrix.m13 = -viewMatrix.m13;
            }

            return GL.GetGPUProjectionMatrix(projMatrix, true) * viewMatrix;
        }

        void EnsureLocalCapacity(int sliceCount)
        {
            if (LocalSlices.Length < sliceCount)
            {
                LocalSlices = new FLocalShadowSlice[sliceCount];
                LocalMatrices = new Matrix4x4[sliceCount];
                LocalUVRects = new Vector4[sliceCount];
            }
        }
    }
}
