using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Component;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Editor
{
    // Window/Rendering/Light Explorer under Infinity: Unity's tabs plus the Infinity light and probe data and the Volumes.
    // The light shape is shown read-only: editing it here would bypass the native Light sync the Light inspector does.
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public sealed class InfinityLightingExplorerExtension : DefaultLightingExplorerExtension
    {
        static readonly GUIContent s_Enabled = new GUIContent("Enabled");
        static readonly GUIContent s_Name = new GUIContent("Name");

        public override LightingExplorerTab[] GetContentTabs()
        {
            return new[]
            {
                new LightingExplorerTab("Lights", GetLights, GetLightColumns, true),
                new LightingExplorerTab("Infinity Lights", GetInfinityLights, GetInfinityLightColumns, true),
                new LightingExplorerTab("Reflection Probes", GetReflectionProbes, GetReflectionProbeColumns, true),
                new LightingExplorerTab("Infinity Probes", GetInfinityProbes, GetInfinityProbeColumns, true),
                new LightingExplorerTab("Light Probes", GetLightProbes, GetLightProbeColumns, true),
                new LightingExplorerTab("Volumes", GetVolumes, GetVolumeColumns, true),
                new LightingExplorerTab("Static Emissives", GetEmissives, GetEmissivesColumns, false),
            };
        }

        static Object[] GetInfinityLights() => GetObjectsForLightingExplorer<InfinityAdditionalLightData>().Cast<Object>().ToArray();
        static Object[] GetInfinityProbes() => GetObjectsForLightingExplorer<InfinityAdditionalReflectionProbeData>().Cast<Object>().ToArray();
        static Object[] GetVolumes() => GetObjectsForLightingExplorer<Volume>().Cast<Object>().ToArray();

        static LightingExplorerTableColumn Column(LightingExplorerTableColumn.DataType type, string label, string property, int width) =>
            new LightingExplorerTableColumn(type, new GUIContent(label), property, width);

        static LightingExplorerTableColumn[] GetInfinityLightColumns()
        {
            return new[]
            {
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Checkbox, s_Enabled, "m_Enabled", 50),
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Name, s_Name, null, 200),
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Custom, new GUIContent("Shape"), "m_Shape.type", 110, (r, prop, dep) =>
                {
                    var data = prop.serializedObject.targetObject as InfinityAdditionalLightData;
                    EditorGUI.LabelField(r, data != null && data.attachedLight != null ? DescribeShape(data.shape) : "-");
                }),
                Column(LightingExplorerTableColumn.DataType.Enum, "Shadow Filter", "m_ShadowFilter", 90),
                Column(LightingExplorerTableColumn.DataType.Enum, "Shadow Update", "m_ShadowUpdateMode", 100),
                Column(LightingExplorerTableColumn.DataType.Checkbox, "Contact Shadows", "enableContactShadow", 60),
                Column(LightingExplorerTableColumn.DataType.Checkbox, "Volumetric", "enableVolumetric", 60),
                Column(LightingExplorerTableColumn.DataType.Float, "Max Distance", "maxDrawDistance", 80),
                Column(LightingExplorerTableColumn.DataType.Float, "Diffuse", "diffuse", 60),
                Column(LightingExplorerTableColumn.DataType.Float, "Specular", "specular", 60),
                Column(LightingExplorerTableColumn.DataType.Enum, "Light Layers", "lightLayer", 110),
            };
        }

        static string DescribeShape(in FLightShape shape)
        {
            switch (shape.type)
            {
                case EInfinityLightType.Point:
                    if (shape.sourceLength > 0) return shape.sourceRadius > 0 ? "Tube" : "Line";
                    return shape.sourceRadius > 0 ? "Sphere" : "Point";
                case EInfinityLightType.Spot:
                    return shape.spotShape + " Spot";
                default:
                    return shape.type.ToString();
            }
        }

        static LightingExplorerTableColumn[] GetInfinityProbeColumns()
        {
            return new[]
            {
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Checkbox, s_Enabled, "m_Enabled", 50),
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Name, s_Name, null, 200),
                Column(LightingExplorerTableColumn.DataType.Enum, "Influence", "influence", 80),
                Column(LightingExplorerTableColumn.DataType.Float, "Sphere Radius", "sphereRadius", 90),
            };
        }

        static LightingExplorerTableColumn[] GetVolumeColumns()
        {
            return new[]
            {
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Checkbox, s_Enabled, "m_Enabled", 50),
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Name, s_Name, null, 200),
                Column(LightingExplorerTableColumn.DataType.Checkbox, "Global", "isGlobal", 50),
                Column(LightingExplorerTableColumn.DataType.Float, "Priority", "priority", 60),
                Column(LightingExplorerTableColumn.DataType.Float, "Weight", "weight", 60),
                Column(LightingExplorerTableColumn.DataType.Float, "Blend Distance", "blendDistance", 90),
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Custom, new GUIContent("Profile"), "sharedProfile", 160, (r, prop, dep) =>
                {
                    EditorGUI.PropertyField(r, prop, GUIContent.none);
                }),
            };
        }
    }
}
