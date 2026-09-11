using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    static class VolumeTestUtility
    {
        public static bool VolumeComponentActive(VolumeComponent component)
        {
            return GraphicsUtility.VolumeComponentActive(component);
        }
    }
}
