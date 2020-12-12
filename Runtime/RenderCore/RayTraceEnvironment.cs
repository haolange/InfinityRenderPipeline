using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Runtime.Rendering.Pipeline;

namespace InfinityTech.Runtime.Rendering.Core
{
    [ExecuteAlways]
    public class RayTraceEnvironment : MonoBehaviour
    {
        public static  RayTracingAccelerationStructure TracingAccelerationStructure;

        public void Awake() {

        }

        public void OnEnable() {
            InitRTMannager();
        }

        public void Start() {

        }

        public void OnPreRender() {
  
        }

        public void OnDisable() {
            ReleaseRTMannager();
        }

        private void InitRTMannager() {
            InfinityRenderPipelineAsset PipelineAsset = (InfinityRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;

            if (TracingAccelerationStructure == null && PipelineAsset.EnableRayTracing == true)
            {
                RayTracingAccelerationStructure.RASSettings TracingAccelerationStructureSetting = new RayTracingAccelerationStructure.RASSettings(RayTracingAccelerationStructure.ManagementMode.Automatic, RayTracingAccelerationStructure.RayTracingModeMask.Everything, -1 ^ (1 << 9));
                TracingAccelerationStructure = new RayTracingAccelerationStructure(TracingAccelerationStructureSetting);
                TracingAccelerationStructure.Build();//
            }
        }

        private void ReleaseRTMannager() {
            if (TracingAccelerationStructure != null) {
                TracingAccelerationStructure.Release();
                TracingAccelerationStructure.Dispose();
                TracingAccelerationStructure = null;
            }
        }
    }

    public static class RayTraceMannager
    {
        
    }
}
