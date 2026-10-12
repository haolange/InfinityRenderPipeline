using System;
using System.Runtime.InteropServices;
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
        public int cascadeCount;
        public float cascadeBlendWidth;
        // Texels of filter footprint kept inside each local tile frustum, so filtering never reads past a cube face.
        public int filterGuardTexels;
    }

    internal struct FLocalShadowSlice
    {
        public int visibleLightIndex;
        public int recordIndex;
        public int face;
        public int matrixIndex;
        public bool isSpot;
        public BatchCullingProjectionType projectionType;
        public Vector4 atlasPixelRect;
        public Vector4 atlasUVRect;
        public Matrix4x4 shadowMatrix;
        public Vector4 casterBias;
        public ShadowSplitData splitData;
        public bool valid;
    }

    // Per local shadow slice data the shaders read (SRV_LocalShadowRects).
    // GPU-RECORD: FLocalShadowSlice Shaders/ShaderLibrary/ShadowSampling.hlsl
    [StructLayout(LayoutKind.Sequential)]
    internal struct FLocalShadowSliceGPU
    {
        // xy = atlas UV origin, zw = atlas UV size.
        public Vector4 atlasRect;
        // x, y = m22, m23 of the GPU projection (linear depth from stored depth); z = world texel size at unit depth
        // (perspective) or world texel size (orthographic); w = 1 for orthographic.
        public Vector4 depthParams;
    }

    internal struct FCascadeShadowSlice
    {
        public Matrix4x4 cullingMatrix;
        public Matrix4x4 shadowMatrix;
        public Vector4 atlasPixelRect;
        public Vector4 atlasUVRect;
        public Vector4 casterBias;
        public ShadowSplitData splitData;
        public bool valid;
    }

    /// <summary>
    /// Cascades: up to 4 tiles in a 2x2 atlas for the first shadowed directional light. Local atlas: point, sphere,
    /// line and tube lights take 6 cube faces; cone, pyramid and box spots, rects and disks one tile. Every local
    /// frustum is built here (not by Unity's light-type helpers) so every shape shadows the same way, and its culling
    /// planes go to CullShadowCasters. Writes atlas / matrix indices back into FLightRecord before the GPU upload.
    /// </summary>
    internal sealed class ShadowAllocator
    {
        public const int CascadeCount = 4;
        internal static readonly Vector3 DefaultCascadeRatios = new Vector3(0.067f, 0.2f, 0.467f);
        // Rect and disk shadows cover a 120 degree cone in front of the emitter.
        internal const float PlanarShadowFieldOfView = 120.0f;

        // Split ratios must be strictly increasing inside (0, 1). The RP Asset keeps them ordered in OnValidate;
        // a script that writes an invalid value fails at record time instead of rendering unordered cascades.
        internal static Vector3 ValidateCascadeRatios(Vector3 ratios)
        {
            bool valid = ratios.x > 0.0f && ratios.y > ratios.x && ratios.z > ratios.y && ratios.z < 1.0f;
            if (!valid)
                throw new ArgumentOutOfRangeException(nameof(ratios), $"InfinityRP: cascade split ratios {ratios} must be strictly increasing inside (0, 1).");
            return ratios;
        }

        public int CascadeVisibleLightIndex { get; private set; } = -1;
        public int CascadeRecordIndex { get; private set; } = -1;
        public int CascadeAllocatedCount { get; private set; }
        // Cascades requested by the asset (1-4); slices past it stay invalid.
        public int ActiveCascadeCount { get; private set; } = CascadeCount;
        public Vector4 CascadeSplitDistances { get; private set; }
        public readonly FCascadeShadowSlice[] CascadeSlices = new FCascadeShadowSlice[CascadeCount];
        public readonly Matrix4x4[] CascadeMatrices = new Matrix4x4[CascadeCount];
        public readonly Vector4[] CascadeSpheres = new Vector4[CascadeCount];
        // x = world texel size, y, z = m22, m23 of the GPU projection, w = blend width (fraction of the cascade).
        public readonly Vector4[] CascadeParams = new Vector4[CascadeCount];

        public int LocalSliceCount { get; private set; }
        public FLocalShadowSlice[] LocalSlices = Array.Empty<FLocalShadowSlice>();
        public Matrix4x4[] LocalMatrices = Array.Empty<Matrix4x4>();
        public FLocalShadowSliceGPU[] LocalGPU = Array.Empty<FLocalShadowSliceGPU>();

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
                CascadeSpheres[i] = Vector4.zero;
                CascadeParams[i] = Vector4.zero;
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
            ActiveCascadeCount = math.clamp(settings.cascadeCount, 1, CascadeCount);

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
            if (!(ratios.x > 0 && ratios.x < ratios.y && ratios.y < ratios.z && ratios.z < 1))
                throw new ArgumentOutOfRangeException(nameof(settings.cascadeRatios), "Cascade ratios must be increasing within (0, 1).");
            int count = ActiveCascadeCount;

            for (int i = 0; i < records.Length; ++i)
            {
                FLightRecord record = records[i];
                if (!record.IsDirectional || record.visibleLightIndex < 0)
                {
                    continue;
                }

                VisibleLight visible = cullingResults.visibleLights[record.visibleLightIndex];
                Light light = visible.light;
                if (light == null || light.shadows == LightShadows.None)
                {
                    continue;
                }

                CascadeVisibleLightIndex = record.visibleLightIndex;
                CascadeRecordIndex = i;
                Vector4 splits = Vector4.zero;
                int allocated = 0;
                for (int cascade = 0; cascade < count; ++cascade)
                {
                    int col = cascade % 2;
                    int row = cascade / 2;
                    FCascadeShadowSlice slice;
                    slice.atlasPixelRect = new Vector4(col * halfRes, row * halfRes, halfRes, halfRes);
                    slice.atlasUVRect = new Vector4(col * 0.5f, row * 0.5f, 0.5f, 0.5f);
                    slice.shadowMatrix = Matrix4x4.identity;
                    slice.cullingMatrix = Matrix4x4.identity;
                    slice.casterBias = Vector4.zero;
                    slice.splitData = default;
                    slice.valid = cullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(
                        record.visibleLightIndex,
                        cascade,
                        count,
                        ratios,
                        halfRes,
                        light.shadowNearPlane,
                        out Matrix4x4 viewMatrix,
                        out Matrix4x4 projMatrix,
                        out slice.splitData);
                    if (slice.valid)
                    {
                        float texelSize = 2f / (Mathf.Abs(projMatrix.m00) * halfRes);
                        slice.casterBias = new Vector4(-light.shadowBias * texelSize, -light.shadowNormalBias * texelSize, 0, 0);
                        slice.cullingMatrix = projMatrix * viewMatrix;
                        slice.shadowMatrix = MakeShadowMatrix(projMatrix, viewMatrix);
                        Matrix4x4 gpuProjection = GL.GetGPUProjectionMatrix(projMatrix, true);
                        CascadeParams[cascade] = new Vector4(texelSize, gpuProjection.m22, gpuProjection.m23, Mathf.Clamp(settings.cascadeBlendWidth, 0, 0.5f));
                        Vector4 sphere = slice.splitData.cullingSphere;
                        sphere.w *= sphere.w;
                        CascadeSpheres[cascade] = sphere;
                        allocated++;
                    }

                    CascadeSlices[cascade] = slice;
                    CascadeMatrices[cascade] = slice.shadowMatrix;
                    splits[cascade] = (cascade < count - 1 ? ratios[cascade] : 1f) * settings.shadowDistance;
                }
                for (int cascade = count; cascade < CascadeCount; ++cascade)
                {
                    splits[cascade] = settings.shadowDistance;
                }

                CascadeAllocatedCount = allocated;
                CascadeSplitDistances = splits;
                record.shadowAtlasRect = CascadeSlices[0].atlasUVRect;
                record.shadowMatrixIndex = 0;
                record.shadowSliceCount = count;
                record.flags |= FLightRecordFlags.EnableShadow;
                records[i] = record;
                return;
            }
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
                if (IsLocalShadowCandidate(records[i], cullingResults))
                {
                    candidateCount++;
                }
            }

            var scores = new NativeArray<float>(math.max(1, candidateCount), Allocator.Temp);
            var recordIndices = new NativeArray<int>(math.max(1, candidateCount), Allocator.Temp);
            int write = 0;
            for (int i = 0; i < records.Length; ++i)
            {
                FLightRecord record = records[i];
                if (!IsLocalShadowCandidate(record, cullingResults))
                {
                    continue;
                }

                VisibleLight visible = cullingResults.visibleLights[record.visibleLightIndex];
                Vector3 lightPosition = visible.localToWorldMatrix.GetColumn(3);
                float distance = math.max(0.01f, Vector3.Distance(cameraPosition, lightPosition));
                // Strength over distance; single-tile shapes win ties against six-face shapes.
                float score = visible.light.shadowStrength / distance;
                if (FLightRecordPack.LocalShadowSliceCount(record) == 1)
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
                int faces = FLightRecordPack.LocalShadowSliceCount(records[recordIndex]);
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
            float guard = math.max(0, settings.filterGuardTexels);
            for (int a = 0; a < accepted; ++a)
            {
                int recordIndex = acceptedRecords[a];
                FLightRecord record = records[recordIndex];
                int faces = FLightRecordPack.LocalShadowSliceCount(record);
                int visibleIndex = record.visibleLightIndex;
                Light light = cullingResults.visibleLights[visibleIndex].light;
                int firstSlice = nextSlice;

                for (int face = 0; face < faces; ++face)
                {
                    int slice = nextSlice + face;
                    int col = slice % tilesPerRow;
                    int row = slice / tilesPerRow;
                    float x = col * tileResolution;
                    float y = row * tileResolution;
                    FLocalShadowSlice local = default;
                    local.visibleLightIndex = visibleIndex;
                    local.recordIndex = recordIndex;
                    local.face = face;
                    local.matrixIndex = slice;
                    local.isSpot = faces == 1;
                    local.atlasPixelRect = new Vector4(x, y, tileResolution, tileResolution);
                    local.atlasUVRect = new Vector4(x / localRes, y / localRes, (float)tileResolution / localRes, (float)tileResolution / localRes);

                    FLocalShadowFrustum frustum = LocalShadowFrustum(record, light, face, tileResolution, guard);
                    local.projectionType = frustum.orthographic ? BatchCullingProjectionType.Orthographic : BatchCullingProjectionType.Perspective;
                    local.valid = frustum.valid;
                    local.shadowMatrix = Matrix4x4.identity;
                    var gpu = new FLocalShadowSliceGPU { atlasRect = local.atlasUVRect, depthParams = new Vector4(0, 1, 1, 0) };
                    if (frustum.valid)
                    {
                        Matrix4x4 gpuProjection = GL.GetGPUProjectionMatrix(frustum.projection, true);
                        local.shadowMatrix = gpuProjection * frustum.view;
                        float texelSize = 2f / (Mathf.Abs(frustum.projection.m00) * tileResolution);
                        local.casterBias = new Vector4(-light.shadowBias * texelSize, -light.shadowNormalBias * texelSize, 0, 0);
                        local.splitData = CullingSplit(frustum.projection * frustum.view, frustum.near);
                        gpu.depthParams = new Vector4(gpuProjection.m22, gpuProjection.m23, texelSize, frustum.orthographic ? 1 : 0);
                    }

                    LocalSlices[slice] = local;
                    LocalMatrices[slice] = local.shadowMatrix;
                    LocalGPU[slice] = gpu;
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

        static bool IsLocalShadowCandidate(in FLightRecord record, in CullingResults cullingResults)
        {
            if (record.IsDirectional || record.visibleLightIndex < 0 || FLightRecordPack.LocalShadowSliceCount(record) <= 0)
            {
                return false;
            }
            Light light = cullingResults.visibleLights[record.visibleLightIndex].light;
            return light != null && light.shadows != LightShadows.None && cullingResults.GetShadowCasterBounds(record.visibleLightIndex, out _);
        }

        internal struct FLocalShadowFrustum
        {
            public Matrix4x4 view;
            public Matrix4x4 projection;
            public float near;
            public bool orthographic;
            public bool valid;
        }

        // Cube faces in SelectPointShadowFace order: +X, -X, +Y, -Y, +Z, -Z.
        static readonly Vector3[] s_FaceForward = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        static readonly Vector3[] s_FaceUp = { Vector3.up, Vector3.up, Vector3.back, Vector3.forward, Vector3.up, Vector3.up };

        // The view and projection of one local shadow tile, built from the record's shape flags.
        internal static FLocalShadowFrustum LocalShadowFrustum(in FLightRecord record, Light light, int face, int tileResolution, float guardTexels)
        {
            var frustum = new FLocalShadowFrustum();
            Vector3 position = new Vector3(record.positionRange.x, record.positionRange.y, record.positionRange.z);
            float range = math.max(record.positionRange.w, 0.02f);
            float near = math.clamp(light != null ? light.shadowNearPlane : 0.1f, 0.01f, range * 0.5f);
            Vector3 forward = new Vector3(record.forward.x, record.forward.y, record.forward.z);
            Vector3 right = new Vector3(record.right.x, record.right.y, record.right.z);
            Vector3 up = Vector3.Cross(forward, right);
            // Widen each frustum by the filter footprint so tents and PCSS kernels stay inside the rendered area.
            float guardScale = (tileResolution + 2.0f * guardTexels) / math.max(1.0f, tileResolution);
            int flags = record.flags;

            if ((flags & (FLightRecordFlags.SpotCone | FLightRecordFlags.SpotPyramid | FLightRecordFlags.SpotBox | FLightRecordFlags.Planar)) == 0)
            {
                float fov = 2.0f * Mathf.Atan(guardScale) * Mathf.Rad2Deg;
                frustum.view = WorldToView(position, s_FaceForward[face], s_FaceUp[face]);
                frustum.projection = Matrix4x4.Perspective(fov, 1, near, range);
            }
            else if ((flags & FLightRecordFlags.SpotCone) != 0)
            {
                float halfTan = Mathf.Tan(Mathf.Clamp(light != null ? light.spotAngle : 60, 1, 179) * 0.5f * Mathf.Deg2Rad) * guardScale;
                frustum.view = WorldToView(position, forward, up);
                frustum.projection = Matrix4x4.Perspective(2.0f * Mathf.Atan(halfTan) * Mathf.Rad2Deg, 1, near, range);
            }
            else if ((flags & FLightRecordFlags.SpotPyramid) != 0)
            {
                float tanX = record.area.x * guardScale, tanY = record.area.y * guardScale;
                frustum.view = WorldToView(position, forward, up);
                frustum.projection = Matrix4x4.Perspective(2.0f * Mathf.Atan(tanY) * Mathf.Rad2Deg, tanX / math.max(tanY, 1e-4f), near, range);
            }
            else if ((flags & FLightRecordFlags.SpotBox) != 0)
            {
                float halfW = 0.5f * record.area.x * guardScale, halfH = 0.5f * record.area.y * guardScale;
                frustum.view = WorldToView(position, forward, up);
                frustum.projection = Matrix4x4.Ortho(-halfW, halfW, -halfH, halfH, 0.0f, range);
                frustum.orthographic = true;
                near = 0.0f;
            }
            else
            {
                // Rect or disk: a perspective frustum whose apex sits behind the emitter, so the emitter fills the near
                // plane of a PlanarShadowFieldOfView cone.
                float halfExtent = record.area.x > 0 && record.area.y > 0
                    ? 0.5f * Mathf.Sqrt(record.area.x * record.area.x + record.area.y * record.area.y)
                    : record.shape.x;
                float halfTan = Mathf.Tan(PlanarShadowFieldOfView * 0.5f * Mathf.Deg2Rad);
                float apexOffset = math.max(halfExtent / halfTan, 0.01f);
                frustum.view = WorldToView(position - forward * apexOffset, forward, up);
                near = apexOffset;
                frustum.projection = Matrix4x4.Perspective(2.0f * Mathf.Atan(halfTan * guardScale) * Mathf.Rad2Deg, 1, near, range + apexOffset);
            }

            frustum.near = near;
            frustum.valid = true;
            return frustum;
        }

        // Unity view space looks down -Z.
        static Matrix4x4 WorldToView(Vector3 position, Vector3 forward, Vector3 up)
        {
            Matrix4x4 lookAt = Matrix4x4.TRS(position, Quaternion.LookRotation(forward, up), new Vector3(1, 1, -1));
            return lookAt.inverse;
        }

        static ShadowSplitData CullingSplit(Matrix4x4 cullingMatrix, float near)
        {
            var split = new ShadowSplitData();
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cullingMatrix);
            split.cullingPlaneCount = planes.Length;
            for (int i = 0; i < planes.Length; ++i)
            {
                split.SetCullingPlane(i, planes[i]);
            }
            split.cullingMatrix = cullingMatrix;
            split.cullingNearPlane = near;
            return split;
        }

        static Matrix4x4 MakeShadowMatrix(Matrix4x4 projection, Matrix4x4 view) => GL.GetGPUProjectionMatrix(projection, true) * view;

        void EnsureLocalCapacity(int sliceCount)
        {
            if (LocalSlices.Length < sliceCount)
            {
                LocalSlices = new FLocalShadowSlice[sliceCount];
                LocalMatrices = new Matrix4x4[sliceCount];
                LocalGPU = new FLocalShadowSliceGPU[sliceCount];
            }
        }
    }
}
