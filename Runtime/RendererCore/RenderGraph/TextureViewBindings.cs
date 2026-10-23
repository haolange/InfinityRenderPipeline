using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.RenderGraph
{
    internal static partial class TextureViewBindings
    {
        static Vector4 Resolve(RenderTargetIdentifier texture)
            => RGResourceFactory.current != null && RGResourceFactory.current.TryGetTextureView(texture, out var view) ? view.shaderValue : Vector4.zero;
        internal static void Global(CommandBuffer commands, int name, RenderTargetIdentifier texture)
        {
            if (s_ViewIDs.TryGetValue(name, out int viewID)) commands.SetGlobalVector(viewID, Resolve(texture));
        }
        internal static void Compute(CommandBuffer commands, ComputeShader shader, int name, RenderTargetIdentifier texture, int mip = 0)
        {
            if (s_ViewIDs.TryGetValue(name, out int viewID))
            {
                Vector4 view = Resolve(texture);
                if (view.x > 0 && mip > 0)
                {
                    float scale = Mathf.Pow(2, mip);
                    view = new Vector4(Mathf.Max(1, Mathf.Floor(view.x / scale)), Mathf.Max(1, Mathf.Floor(view.y / scale)), Mathf.Max(1, Mathf.Floor(view.z / scale)), Mathf.Max(1, Mathf.Floor(view.w / scale)));
                }
                commands.SetComputeVectorParam(shader, viewID, view);
            }
        }
    }
}
