using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using System.Collections;
using System.Collections.Generic;

namespace InfinityTech.Rendering.Feature
{
    public class RayTracingIndirect
    {
        private RayTracingShader m_Shader;

        public RayTracingIndirect(RayTracingShader shader)
        {
            this.m_Shader = shader;
        }
    }
}
