using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using System.Collections;
using System.Collections.Generic;

namespace InfinityTech.Rendering.Feature
{
    public class RayTracingIndirectDiffuseGenerator
    {
        private RayTracingShader m_Shader;

        public RayTracingIndirectDiffuseGenerator(RayTracingShader shader)
        {
            this.m_Shader = shader;
        }
    }
}
