using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    public static class GraphicsUtility
    {
        internal static bool VolumeComponentActive(VolumeComponent component)
        {
            if (component == null)
            {
                return false;
            }

            if (component is InfinityTech.Rendering.Volume.IInfinityVolumeActivity activity)
            {
                return activity.IsActive();
            }

            return component.active;
        }

        internal static Material s_BlitMaterial;

        internal static Material BlitMaterial
        {
            get
            {
                return s_BlitMaterial;
            }
        }

    }
}
