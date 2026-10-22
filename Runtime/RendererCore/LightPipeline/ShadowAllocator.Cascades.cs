using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.LightPipeline
{
    internal sealed partial class ShadowAllocator
    {
        readonly ShadowCasterBounds m_MeshCasters = new ShadowCasterBounds();
        readonly Vector3[] m_NearCorners = new Vector3[4];
        readonly Vector3[] m_FarCorners = new Vector3[4];
        readonly Vector3[] m_ReceiverCorners = new Vector3[8];

        void AllocateCascade(NativeList<FLightRecord> records, in CullingResults cullingResults, Camera camera,
            in FShadowAllocatorSettings settings, int resolution)
        {
            Vector3 ratios = settings.cascadeRatios;
            if (!(ratios.x > 0 && ratios.x < ratios.y && ratios.y < ratios.z && ratios.z < 1))
                throw new ArgumentOutOfRangeException(nameof(settings.cascadeRatios), "Cascade ratios must be increasing within (0, 1).");
            float distance = Mathf.Min(settings.shadowDistance, camera.farClipPlane);
            if (distance <= camera.nearClipPlane) return;
            for (int i = 0; i < records.Length; ++i)
            {
                FLightRecord record = records[i];
                if (!record.IsDirectional || record.visibleLightIndex < 0) continue;
                Light light = cullingResults.visibleLights[record.visibleLightIndex].light;
                if (light == null || light.shadows == LightShadows.None) continue;
                bool hasNative = cullingResults.GetShadowCasterBounds(record.visibleLightIndex, out Bounds casterBounds);
                bool hasMesh = m_MeshCasters.TryGetBounds(light, camera, out Bounds meshBounds);
                if (!hasNative && !hasMesh) continue;
                if (hasNative && hasMesh) casterBounds.Encapsulate(meshBounds);
                else if (hasMesh) casterBounds = meshBounds;
                CascadeVisibleLightIndex = record.visibleLightIndex;
                CascadeRecordIndex = i;
                CascadeShadowFilter = record.shadowFilter;
                Vector4 distances = Vector4.one * distance;
                int allocated = 0;
                for (int cascade = 0; cascade < ActiveCascadeCount; ++cascade)
                {
                    float far = distance * (cascade == ActiveCascadeCount - 1 ? 1 : ratios[cascade]);
                    float near = cascade == 0 ? camera.nearClipPlane
                        : Mathf.Max(camera.nearClipPlane, distance * ratios[cascade - 1] * (1 - settings.cascadeBlendWidth));
                    distances[cascade] = far;
                    if (far <= near) continue;
                    FitCascade(camera, light, casterBounds, near, far, resolution, settings.filterGuardTexels,
                        out Matrix4x4 view, out Matrix4x4 projection, out ShadowSplitData split);
                    float texelSize = 2f / (Mathf.Abs(projection.m00) * resolution);
                    var slice = new FCascadeShadowSlice
                    {
                        cullingSplitIndex = allocated,
                        valid = true,
                        atlasPixelRect = new Vector4((cascade % 2) * resolution, (cascade / 2) * resolution, resolution, resolution),
                        atlasUVRect = new Vector4((cascade % 2) * .5f, (cascade / 2) * .5f, .5f, .5f),
                        shadowMatrix = MakeShadowMatrix(projection, view),
                        cullingMatrix = projection * view,
                        casterBias = new Vector4(-light.shadowBias * texelSize, -light.shadowNormalBias * texelSize, 0, 0),
                        splitData = split
                    };
                    CascadeSlices[cascade] = slice;
                    CascadeMatrices[cascade] = slice.shadowMatrix;
                    Vector4 sphere = split.cullingSphere;
                    sphere.w *= sphere.w;
                    CascadeSpheres[cascade] = sphere;
                    Matrix4x4 gpuProjection = GL.GetGPUProjectionMatrix(projection, true);
                    CascadeParams[cascade] = new Vector4(texelSize, gpuProjection.m22, gpuProjection.m23,
                        Mathf.Clamp(settings.cascadeBlendWidth, 0, .5f));
                    allocated++;
                }
                CascadeAllocatedCount = allocated;
                CascadeSplitDistances = distances;
                record.shadowAtlasRect = CascadeSlices[0].atlasUVRect;
                record.shadowMatrixIndex = 0;
                record.shadowSliceCount = ActiveCascadeCount;
                record.flags |= FLightRecordFlags.EnableShadow;
                records[i] = record;
                return;
            }
        }

        // One fit for native and MeshScene casters. A Unity-native-only fit can omit custom casters even when
        // Unity reports a valid split, so native validity is never used as a second matrix authority.
        internal void FitCascade(Camera camera, Light light, Bounds casters, float near, float far, int resolution, int guardTexels,
            out Matrix4x4 view, out Matrix4x4 projection, out ShadowSplitData split)
        {
            camera.CalculateFrustumCorners(new Rect(0, 0, 1, 1), near, Camera.MonoOrStereoscopicEye.Mono, m_NearCorners);
            camera.CalculateFrustumCorners(new Rect(0, 0, 1, 1), far, Camera.MonoOrStereoscopicEye.Mono, m_FarCorners);
            Vector3 center = Vector3.zero;
            Matrix4x4 cameraToWorld = camera.cameraToWorldMatrix;
            for (int i = 0; i < 4; ++i)
            {
                Vector3 nearCorner = m_NearCorners[i];
                Vector3 farCorner = m_FarCorners[i];
                nearCorner.z = -nearCorner.z;
                farCorner.z = -farCorner.z;
                m_ReceiverCorners[i] = cameraToWorld.MultiplyPoint3x4(nearCorner);
                m_ReceiverCorners[i + 4] = cameraToWorld.MultiplyPoint3x4(farCorner);
                center += m_ReceiverCorners[i] + m_ReceiverCorners[i + 4];
            }
            center /= 8;
            float radius = .001f;
            for (int i = 0; i < 8; ++i) radius = Mathf.Max(radius, Vector3.Distance(center, m_ReceiverCorners[i]));
            radius = Mathf.Ceil(radius * 16) / 16;
            int guard = Mathf.Clamp(guardTexels, 1, Mathf.Max(1, resolution / 4));
            radius *= (float)resolution / Mathf.Max(1, resolution - guard * 2);
            float texelSize = radius * 2 / resolution;
            Matrix4x4 rotation = Matrix4x4.Rotate(Quaternion.Inverse(light.transform.rotation));
            Vector3 centerLS = rotation.MultiplyPoint3x4(center);
            centerLS.x = Mathf.Round(centerLS.x / texelSize) * texelSize;
            centerLS.y = Mathf.Round(centerLS.y / texelSize) * texelSize;
            float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
            for (int i = 0; i < 8; ++i)
            {
                float z = rotation.MultiplyPoint3x4(m_ReceiverCorners[i]).z;
                minZ = Mathf.Min(minZ, z);
                maxZ = Mathf.Max(maxZ, z);
            }
            Vector3 casterCenter = rotation.MultiplyPoint3x4(casters.center);
            Vector3 e = casters.extents;
            float casterZExtent = Mathf.Abs(rotation.m20) * e.x + Mathf.Abs(rotation.m21) * e.y + Mathf.Abs(rotation.m22) * e.z;
            minZ = Mathf.Min(minZ, casterCenter.z - casterZExtent);
            maxZ = Mathf.Max(maxZ, casterCenter.z + casterZExtent);
            float padding = Mathf.Max(.01f, light.shadowNearPlane);
            // Unity view space looks down -Z; the light's authored forward direction is +Z.
            view = Matrix4x4.Scale(new Vector3(1, 1, -1))
                * Matrix4x4.Translate(new Vector3(-centerLS.x, -centerLS.y, -minZ + padding)) * rotation;
            projection = Matrix4x4.Ortho(-radius, radius, -radius, radius, .001f, Mathf.Max(.01f, maxZ - minZ + padding * 2));
            split = CullingSplit(projection * view, .001f);
            Vector3 snappedCenter = rotation.inverse.MultiplyPoint3x4(centerLS);
            split.cullingSphere = new Vector4(snappedCenter.x, snappedCenter.y, snappedCenter.z, radius);
            // Both native and custom draws use the six planes, including casters shared across the blend band.
            split.shadowCascadeBlendCullingFactor = 0;
        }
    }
}
