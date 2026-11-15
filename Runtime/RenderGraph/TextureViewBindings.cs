using UnityEngine;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.RenderGraph
{
    // Registry of the shader properties that sample a graph texture through its logical view. The registry only answers
    // "is this property a texture-view property"; the value always comes from the bound graph texture's descriptor.
    internal static partial class TextureViewBindings
    {
        internal static bool TryGetViewID(int nameID, out int viewID) => s_ViewIDs.TryGetValue(nameID, out viewID);

        // The logical view seen through one mip: size and capacity halve per level and never drop below one texel.
        internal static Vector4 MipView(in TextureView view, int mip)
        {
            Vector4 value = view.shaderValue;
            if (mip <= 0)
            {
                return value;
            }
            float scale = Mathf.Pow(2, mip);
            return new Vector4(Mathf.Max(1, Mathf.Floor(value.x / scale)), Mathf.Max(1, Mathf.Floor(value.y / scale)), Mathf.Max(1, Mathf.Floor(value.z / scale)), Mathf.Max(1, Mathf.Floor(value.w / scale)));
        }
    }
}
