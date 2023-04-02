using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;


namespace InfinityTech.Component
{
    [ExecuteAlways]
    public class RayTraceEnvironment : MonoBehaviour
    {
        public RayTracingAccelerationStructure m_AccelerationStructure;

        public void Awake() 
        {

        }

        public void OnEnable() 
        {
            InitRTMannager();
        }

        public void Start() 
        {

        }

        public void OnPreRender() 
        {
  
        }

        public void OnDisable() 
        {
            ReleaseRTMannager();
        }

        private void InitRTMannager() 
        {
            InfinityRenderPipelineAsset PipelineAsset = (InfinityRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;

            if (m_AccelerationStructure == null && PipelineAsset.enableRayTrace == true)
            {
                RayTracingAccelerationStructure.Settings TracingAccelerationStructureSetting = new RayTracingAccelerationStructure.Settings(RayTracingAccelerationStructure.ManagementMode.Automatic, RayTracingAccelerationStructure.RayTracingModeMask.Everything, -1 ^ (1 << 9));
                m_AccelerationStructure = new RayTracingAccelerationStructure(TracingAccelerationStructureSetting);
                m_AccelerationStructure.Build();//
            }
        }

        private void ReleaseRTMannager() 
        {
            if (m_AccelerationStructure != null) 
            {
                m_AccelerationStructure.Release();
                m_AccelerationStructure.Dispose();
                m_AccelerationStructure = null;
            }
        }
    }

    public static class RayTraceMannager
    {
        
    }
}
