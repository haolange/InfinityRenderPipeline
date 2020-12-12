using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using System.Collections;
using System.Collections.Generic;

namespace InfinityTech.Runtime.Rendering.Feature
{
    public class RayTrace_GlobalIllumination
    {
        public CommandBuffer CmdBuffer;
        public RayTracingShader TraceGlobalIlluminationShader;
        public RayTracingAccelerationStructure TracingAccelerationStructure;
        public RayTracingAccelerationStructure.RASSettings TracingAccelerationStructureSetting;


        public void OnEnable() {

        }

        public void OnPreRender() {

        }

        public void OnDisable() {

        }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        public void InitRatTraceGlobalIllumination() {
        
        }
    }
}
