using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    static class VisibleLightTestUtility
    {
        // Builds the snapshot culling would capture for the light.
        public static VisibleLight FromLight(Light light)
        {
            Color tint = light.useColorTemperature ? Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature) : Color.white;
            return new VisibleLight
            {
                lightType = light.type,
                localToWorldMatrix = light.transform.localToWorldMatrix,
                range = light.range,
                spotAngle = light.spotAngle,
                finalColor = light.color.linear * tint * light.intensity,
            };
        }
    }
}
