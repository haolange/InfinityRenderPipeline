using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using InfinityTech.Rendering;
using InfinityTech.Rendering.AnalyticShadow;

namespace InfinityTech.Component.Editor
{
    // Explicit command that keeps only the four light channels of every mask in the open scenes, with Undo and without saving.
    internal static class LightChannelClampCommand
    {
        [MenuItem("Window/Infinity/Lighting/Clamp Light Channels In Open Scenes")]
        static void ClampOpenScenes()
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Clamp Light Channels");
            int clamped = 0;
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                {
                    clamped += ClampScene(scene);
                }
            }
            Undo.CollapseUndoOperations(group);
            Debug.Log("[InfinityRP] Clamped " + clamped + " light-channel masks to four channels in the open scenes. Save the scenes to keep the change.");
        }

        // Clamps every light-channel mask of one loaded scene through serialized properties (recorded for Undo) and returns
        // how many masks changed.
        internal static int ClampScene(Scene scene)
        {
            int clamped = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    clamped += ClampUnsigned(renderer, "m_RenderingLayerMask");
                }
                foreach (Light light in root.GetComponentsInChildren<Light>(true))
                {
                    clamped += ClampUnsigned(light, "m_RenderingLayerMask");
                }
                foreach (InfinityAdditionalLightData data in root.GetComponentsInChildren<InfinityAdditionalLightData>(true))
                {
                    clamped += ClampEnum(data, "lightLayer");
                }
                foreach (MeshComponent mesh in root.GetComponentsInChildren<MeshComponent>(true))
                {
                    clamped += ClampEnum(mesh, "renderingLayer");
                }
                foreach (DecalComponent decal in root.GetComponentsInChildren<DecalComponent>(true))
                {
                    clamped += ClampEnum(decal, "renderingLayerMask");
                }
                foreach (InfinityAnalyticOccluder occluder in root.GetComponentsInChildren<InfinityAnalyticOccluder>(true))
                {
                    clamped += ClampEnum(occluder, "m_RenderingLayerMask");
                }
            }
            return clamped;
        }

        static int ClampUnsigned(Object owner, string propertyName)
        {
            var serializedOwner = new SerializedObject(owner);
            SerializedProperty property = serializedOwner.FindProperty(propertyName);
            uint mask = property.uintValue;
            if ((mask & ~RenderingLayerUtility.ValidMask) == 0)
            {
                return 0;
            }
            property.uintValue = Clamp(mask, owner);
            serializedOwner.ApplyModifiedProperties();
            return 1;
        }

        static int ClampEnum(Object owner, string propertyName)
        {
            var serializedOwner = new SerializedObject(owner);
            SerializedProperty property = serializedOwner.FindProperty(propertyName);
            uint mask = (uint)property.intValue;
            if ((mask & ~RenderingLayerUtility.ValidMask) == 0)
            {
                return 0;
            }
            property.intValue = (int)Clamp(mask, owner);
            serializedOwner.ApplyModifiedProperties();
            return 1;
        }

        // A mask that loses every channel would leave its object unlit or uncast; it is reported for an explicit correction.
        static uint Clamp(uint mask, Object owner)
        {
            uint channels = mask & RenderingLayerUtility.ValidMask;
            if (channels == 0)
            {
                Debug.LogWarning("[InfinityRP] " + owner.name + " used only channels above the four light channels; its mask is now empty and needs a channel.", owner);
            }
            return channels;
        }
    }
}
