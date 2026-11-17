using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Component;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    // Every camera's cross-frame state and profiling sampler, keyed by camera id. A state lives until its camera stops
    // rendering (CameraFrameState.ShouldRecycle) or the render context is disposed.
    internal sealed class CameraStateTable : IDisposable
    {
        readonly ResourcePool m_ResourcePool;
        readonly Dictionary<int, CameraFrameState> m_States = new Dictionary<int, CameraFrameState>(8);
        readonly Dictionary<int, ProfilingSampler> m_Samplers = new Dictionary<int, ProfilingSampler>(8);
        // Recycle scratch: its last reader is Recycle's removal loop, bounded by the camera count.
        readonly List<int> m_RecycleIds = new List<int>(8);

        internal CameraStateTable(ResourcePool resourcePool)
        {
            m_ResourcePool = resourcePool;
        }

        internal Dictionary<int, CameraFrameState> states => m_States;

        // Unity draws a material icon (always 64 px high) and the material preview (never below 70 px) with one preview
        // camera; the height separates their state.
        internal static int CameraId(Camera camera)
        {
            int cameraId = camera.GetHashCode();
            if (camera.cameraType == CameraType.Preview && camera.pixelHeight == 64)
            {
                cameraId += 1;
            }
            return cameraId;
        }

        internal CameraFrameState GetOrCreate(Camera camera, out bool created)
        {
            int cameraId = CameraId(camera);
            created = !m_States.TryGetValue(cameraId, out CameraFrameState state);
            if (created)
            {
                state = new CameraFrameState(cameraId, m_ResourcePool);
                m_States.Add(cameraId, state);
            }
            return state;
        }

        // The camera component's own sampler, else one named after the camera, created once per camera id.
        internal ProfilingSampler GetSampler(Camera camera, InfinityAdditionalCameraData cameraData)
        {
            if (cameraData != null && cameraData.viewProfiler != null)
            {
                return cameraData.viewProfiler;
            }
            int cameraId = CameraId(camera);
            if (!m_Samplers.TryGetValue(cameraId, out ProfilingSampler sampler))
            {
                sampler = new ProfilingSampler(camera.name);
                m_Samplers.Add(cameraId, sampler);
            }
            return sampler;
        }

        internal bool TryGetSampler(Camera camera, out ProfilingSampler sampler) => m_Samplers.TryGetValue(CameraId(camera), out sampler);

        // Disposes the states of cameras that stopped rendering; fence covers their last submitted frame.
        internal void Recycle(int frame, ResourceReuseFence fence)
        {
            m_RecycleIds.Clear();
            foreach (KeyValuePair<int, CameraFrameState> pair in m_States)
            {
                if (pair.Value.camera == null || CameraFrameState.ShouldRecycle(pair.Value.lastSeenFrame, frame, pair.Value.cameraType))
                {
                    m_RecycleIds.Add(pair.Key);
                }
            }
            for (int i = 0; i < m_RecycleIds.Count; ++i)
            {
                int cameraId = m_RecycleIds[i];
                m_States[cameraId].Dispose(fence);
                m_States.Remove(cameraId);
            }
        }

        public void Dispose()
        {
            var failures = new List<Exception>();
            foreach (CameraFrameState state in m_States.Values)
            {
                try { state.Dispose(); }
                catch (Exception error) { failures.Add(error); }
            }
            m_States.Clear();
            m_Samplers.Clear();
            if (failures.Count == 1)
            {
                throw failures[0];
            }
            if (failures.Count > 1)
            {
                throw new AggregateException(failures);
            }
        }
    }
}
