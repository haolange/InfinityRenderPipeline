using System;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Scene;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    public class RenderContext : IDisposable
    {
        internal readonly SceneWorld sceneWorld;
        internal ScriptableRenderContext scriptableRenderContext;

        public RenderContext(ResourcePool resourcePool, ComputeShader meshDrawShader)
        {
            sceneWorld = new SceneWorld(resourcePool, meshDrawShader);
        }

        public void Dispose()
        {
            sceneWorld.Dispose();
        }
    }
}
