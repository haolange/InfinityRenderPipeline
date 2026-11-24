using System;
using UnityEditor;
using UnityEngine;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Component.Editor
{
    internal static class InfinityLightEditorTransactions
    {
        internal static void StageProperty(SerializedProperty property, SerializedObject[] snapshots)
        {
            foreach (SerializedObject snapshot in snapshots)
            {
                snapshot.Update();
                snapshot.CopyFromSerializedProperty(property);
                snapshot.ApplyModifiedPropertiesWithoutUndo();
            }
            property.serializedObject.Update();
        }

        internal static void ApplyAdditional(Light[] lights, SerializedObject[] before, SerializedObject[] after, string propertyPath)
        {
            var changed = new bool[lights.Length];
            bool anyChanged = false;
            for (int i = 0; i < lights.Length; ++i)
            {
                before[i].Update();
                after[i].Update();
                changed[i] = !SerializedProperty.DataEquals(before[i].FindProperty(propertyPath), after[i].FindProperty(propertyPath));
                anyChanged |= changed[i];
            }
            if (!anyChanged)
                return;

            int group = Begin("Edit Infinity Light");
            try
            {
                for (int i = 0; i < lights.Length; ++i)
                {
                    if (!changed[i])
                        continue;
                    InfinityAdditionalLightData data = GetOrCreate(lights[i], false);
                    Undo.RecordObject(data, "Edit Infinity Light");
                    using var serialized = new SerializedObject(data);
                    serialized.CopyFromSerializedProperty(after[i].FindProperty(propertyPath));
                    serialized.ApplyModifiedProperties();
                    RecordPrefab(data);
                }
                Complete();
            }
            catch
            {
                Rollback(group);
                throw;
            }
        }

        internal static void ApplyShape(Light[] lights, InfinityLightInspectorSnapshot[] before, InfinityLightInspectorSnapshot[] after)
        {
            var changed = new bool[lights.Length];
            var shapes = new LightSourceShape[lights.Length];
            bool anyChanged = false;
            for (int i = 0; i < lights.Length; ++i)
            {
                LightSourceShape previous = before[i].shape;
                LightSourceShape current = after[i].shape;
                if (previous.type != EInfinityLightType.Disk && current.type == EInfinityLightType.Disk && current.sourceRadius <= 0)
                    current.sourceRadius = 0.5f;
                shapes[i] = current;
                changed[i] = !previous.Equals(current);
                anyChanged |= changed[i];
            }
            if (!anyChanged)
                return;

            int group = Begin("Edit Light Shape");
            try
            {
                for (int i = 0; i < lights.Length; ++i)
                {
                    if (!changed[i])
                        continue;
                    Light light = lights[i];
                    InfinityAdditionalLightData data = GetOrCreate(light, true);
                    Undo.RecordObjects(new UnityEngine.Object[] { light, data }, "Edit Light Shape");
                    data.SetShape(shapes[i]);
                    EditorUtility.SetDirty(data);
                    EditorUtility.SetDirty(light);
                    RecordPrefab(data);
                    RecordPrefab(light);
                }
                Complete();
            }
            catch
            {
                Rollback(group);
                throw;
            }
        }

        internal static void ApplyBakeMode(Light[] lights, LightmapBakeType mode)
        {
            bool anyChanged = false;
            foreach (Light light in lights)
                anyChanged |= light.lightmapBakeType != mode;
            if (!anyChanged)
                return;

            int group = Begin("Light Baking Mode");
            try
            {
                foreach (Light light in lights)
                {
                    if (light.lightmapBakeType == mode)
                        continue;
                    light.TryGetComponent(out InfinityAdditionalLightData data);
                    LightSourceShape shape = data ? data.shape : LightShapeUtility.FromUnityLight(light);
                    bool needsDiskCarrier = shape.type == EInfinityLightType.Disk && light.type == LightType.Disc && mode != LightmapBakeType.Baked;
                    if (needsDiskCarrier)
                    {
                        data = GetOrCreate(light, true);
                        Undo.RecordObjects(new UnityEngine.Object[] { light, data }, "Light Baking Mode");
                        data.SetShape(shape);
                        EditorUtility.SetDirty(data);
                        RecordPrefab(data);
                    }
                    else
                    {
                        Undo.RecordObject(light, "Light Baking Mode");
                    }
                    light.lightmapBakeType = mode;
                    if (light.lightmapBakeType != mode)
                        throw new InvalidOperationException("Unity rejected the requested light baking mode.");
                    EditorUtility.SetDirty(light);
                    RecordPrefab(light);
                }
                Complete();
            }
            catch
            {
                Rollback(group);
                throw;
            }
        }

        internal static void ApplySceneEdit(Light light, in LightSourceShape previous, in LightSourceShape current, float range, float spotAngle)
        {
            bool shapeChanged = !previous.Equals(current);
            float desiredRange = light.range == range ? light.range : Mathf.Max(0.01f, range);
            float desiredSpotAngle = light.spotAngle == spotAngle ? light.spotAngle : Mathf.Clamp(spotAngle, 1, 179);
            if (!shapeChanged && light.range == desiredRange && light.spotAngle == desiredSpotAngle)
                return;

            int group = Begin("Edit Light");
            try
            {
                Undo.RecordObject(light, "Edit Light");
                if (shapeChanged)
                {
                    InfinityAdditionalLightData data = GetOrCreate(light, true);
                    Undo.RecordObject(data, "Edit Light");
                    data.SetShape(current);
                    EditorUtility.SetDirty(data);
                    RecordPrefab(data);
                }
                if (light.range != desiredRange)
                    light.range = desiredRange;
                if (light.spotAngle != desiredSpotAngle)
                    light.spotAngle = desiredSpotAngle;
                EditorUtility.SetDirty(light);
                RecordPrefab(light);
                Complete();
            }
            catch
            {
                Rollback(group);
                throw;
            }
        }

        static InfinityAdditionalLightData GetOrCreate(Light light, bool authorShape)
        {
            if (light.TryGetComponent(out InfinityAdditionalLightData data))
                return data;

            data = Undo.AddComponent<InfinityAdditionalLightData>(light.gameObject);
            Undo.RecordObject(data, "Initialize Infinity Light");
            data.maxDrawDistance = InfinityLightInspectorSnapshot.UnlimitedDistance;
            data.shadowFilter = InfinityLightInspectorSnapshot.ResolveNativeShadowFilter(light.shadows);
            if (!authorShape)
            {
                using var serialized = new SerializedObject(data);
                serialized.FindProperty("m_ShapeAuthored").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            return data;
        }

        static int Begin(string name)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(name);
            return group;
        }

        static void Complete()
        {
            Undo.FlushUndoRecordObjects();
        }

        static void Rollback(int group)
        {
            Undo.FlushUndoRecordObjects();
            Undo.RevertAllDownToGroup(group);
        }

        static void RecordPrefab(UnityEngine.Object target)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(target))
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }
    }
}
