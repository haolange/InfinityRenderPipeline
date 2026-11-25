using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Component.Editor
{
    internal static class InfinityLightEditorTransactions
    {
        static int s_ActiveDragHotControl;
        static int s_ActiveDragUndoGroup = -1;

        internal static void SyncDragState()
        {
            if (GUIUtility.hotControl == 0)
            {
                s_ActiveDragHotControl = 0;
                s_ActiveDragUndoGroup = -1;
            }
        }

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

        internal static void ApplyAdditional(Light[] lights, SerializedObject[] before, SerializedObject[] after, string propertyPath, bool mergeDrag = false)
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

            int group = Begin("Edit Infinity Light", mergeDrag);
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

        internal static bool ApplyShape(
            Light[] lights,
            InfinityLightInspectorSnapshot[] before,
            InfinityLightInspectorSnapshot[] after,
            Func<string, bool> confirmRealtimeSwitch = null,
            bool mergeDrag = false)
        {
            var changed = new bool[lights.Length];
            var previousShapes = new LightSourceShape[lights.Length];
            var shapes = new LightSourceShape[lights.Length];
            var switchToRealtime = new bool[lights.Length];
            bool anyChanged = false;
            string confirmationReason = null;
            for (int i = 0; i < lights.Length; ++i)
            {
                LightSourceShape previous = before[i].shape;
                LightSourceShape current = after[i].shape;
                if (previous.emitter != ELocalLightEmitter.Disk && current.emitter == ELocalLightEmitter.Disk && current.sourceRadius <= 0)
                    current.sourceRadius = 0.5f;
                previousShapes[i] = previous;
                shapes[i] = current;
                changed[i] = !previous.Equals(current);
                anyChanged |= changed[i];
                if (!changed[i])
                    continue;

                // Ensure valid enum / shape resolution runs eagerly before prompting.
                LightShapeUtility.NativeType(current);
                LightCapabilities capabilities = LightShapeUtility.GetCapabilities(current);
                if (!capabilities.baking && lights[i].lightmapBakeType != LightmapBakeType.Realtime)
                {
                    switchToRealtime[i] = true;
                    confirmationReason ??= capabilities.bakeDisabledReason;
                }
            }
            if (!anyChanged)
                return false;

            if (confirmationReason != null)
            {
                string message = $"{confirmationReason}\n\nApply the new shape and switch the selected Mixed/Baked light mode to Realtime?";
                bool proceed = (confirmRealtimeSwitch ?? DefaultConfirmRealtimeSwitch)(message);
                if (!proceed)
                    return false;
            }

            int group = Begin("Edit Light Shape", mergeDrag);
            try
            {
                for (int i = 0; i < lights.Length; ++i)
                {
                    if (!changed[i])
                        continue;
                    Light light = lights[i];
                    ELightIntensityEditMode editMode = before[i].intensityEditMode;
                    float outerAngle = LightIntensityUtility.GetEffectiveSpotAngle(light);
                    float innerAngle = LightIntensityUtility.GetEffectiveInnerSpotAngle(light, outerAngle);
                    float adjustedIntensity = LightIntensityUtility.AdjustIntensityForShapeEdit(
                        light.intensity, previousShapes[i], shapes[i], innerAngle, outerAngle, innerAngle, outerAngle, editMode);

                    InfinityAdditionalLightData data = GetOrCreate(light, true);
                    Undo.RecordObjects(new UnityEngine.Object[] { light, data }, "Edit Light Shape");
                    if (switchToRealtime[i])
                    {
                        light.lightmapBakeType = LightmapBakeType.Realtime;
                        if (light.lightmapBakeType != LightmapBakeType.Realtime)
                            throw new InvalidOperationException("Unity rejected switching the light mode to Realtime.");
                    }
                    if (!Mathf.Approximately(light.intensity, adjustedIntensity))
                        light.intensity = adjustedIntensity;
                    data.SetShape(shapes[i]);
                    EditorUtility.SetDirty(data);
                    EditorUtility.SetDirty(light);
                    RecordPrefab(data);
                    RecordPrefab(light);
                }
                Complete();
                return true;
            }
            catch
            {
                Rollback(group);
                throw;
            }
        }

        internal static void ApplyDisplayIntensity(Light[] lights, LightSourceShape[] shapes, float displayIntensity, LightUnit? sharedUnit, bool mergeDrag = false)
        {
            if (lights == null || shapes == null || lights.Length != shapes.Length || lights.Length == 0 || !float.IsFinite(displayIntensity))
                return;

            float clampedDisplay = Mathf.Max(0f, displayIntensity);
            int group = Begin("Edit Light Intensity", mergeDrag);
            try
            {
                for (int i = 0; i < lights.Length; ++i)
                {
                    Light light = lights[i];
                    LightUnit unit = sharedUnit ?? LightIntensityUtility.ResolveDisplayUnit(light, shapes[i]);
                    float canonical = LightIntensityUtility.ToCanonicalIntensity(light, shapes[i], unit, clampedDisplay);
                    if (!float.IsFinite(canonical) || Mathf.Approximately(light.intensity, canonical))
                        continue;
                    Undo.RecordObject(light, "Edit Light Intensity");
                    light.intensity = Mathf.Max(0f, canonical);
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

        internal static void ApplyDisplayUnit(Light[] lights, LightSourceShape[] shapes, LightUnit targetUnit)
        {
            if (lights == null || shapes == null || lights.Length != shapes.Length || lights.Length == 0)
                return;

            bool anyChanged = false;
            for (int i = 0; i < lights.Length; ++i)
                anyChanged |= LightIntensityUtility.IsUnitSupported(shapes[i], targetUnit) && lights[i].lightUnit != targetUnit;
            if (!anyChanged)
                return;

            int group = Begin("Change Light Unit");
            try
            {
                for (int i = 0; i < lights.Length; ++i)
                {
                    Light light = lights[i];
                    if (!LightIntensityUtility.IsUnitSupported(shapes[i], targetUnit) || light.lightUnit == targetUnit)
                        continue;
                    Undo.RecordObject(light, "Change Light Unit");
                    light.lightUnit = targetUnit;
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

        internal static void ApplySpotAngles(Light[] lights, InfinityLightInspectorSnapshot[] snapshots, float? newOuterAngle, float? newInnerAngle, bool mergeDrag = false)
        {
            if (lights == null || snapshots == null || lights.Length != snapshots.Length || lights.Length == 0)
                return;
            if (!newOuterAngle.HasValue && !newInnerAngle.HasValue)
                return;

            int group = Begin("Edit Spot Angle", mergeDrag);
            try
            {
                for (int i = 0; i < lights.Length; ++i)
                {
                    Light light = lights[i];
                    LightSourceShape shape = snapshots[i].shape;
                    float prevOuter = LightIntensityUtility.GetEffectiveSpotAngle(light);
                    float prevInner = LightIntensityUtility.GetEffectiveInnerSpotAngle(light, prevOuter);
                    float nextOuter = newOuterAngle.HasValue ? Mathf.Clamp(newOuterAngle.Value, 1f, 179f) : prevOuter;
                    float nextInner = newInnerAngle.HasValue
                        ? Mathf.Clamp(newInnerAngle.Value, 0f, nextOuter)
                        : Mathf.Clamp(prevInner, 0f, nextOuter);
                    if (Mathf.Approximately(light.spotAngle, nextOuter) && Mathf.Approximately(light.innerSpotAngle, nextInner))
                        continue;

                    float adjustedIntensity = LightIntensityUtility.AdjustIntensityForShapeEdit(
                        light.intensity, shape, shape, prevInner, prevOuter, nextInner, nextOuter, snapshots[i].intensityEditMode);
                    Undo.RecordObject(light, "Edit Spot Angle");
                    light.spotAngle = nextOuter;
                    light.innerSpotAngle = nextInner;
                    if (!Mathf.Approximately(light.intensity, adjustedIntensity))
                        light.intensity = adjustedIntensity;
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

        static bool DefaultConfirmRealtimeSwitch(string message) =>
            EditorUtility.DisplayDialog("Switch Light Mode to Realtime", message, "Apply and Switch to Realtime", "Cancel");

        internal static void ApplyBakeMode(Light[] lights, LightmapBakeType mode)
        {
            bool anyChanged = false;
            foreach (Light light in lights)
                anyChanged |= light.lightmapBakeType != mode;
            if (!anyChanged)
                return;

            foreach (Light light in lights)
            {
                if (light.lightmapBakeType == mode)
                    continue;
                light.TryGetComponent(out InfinityAdditionalLightData data);
                LightSourceShape shape = data ? data.shape : LightShapeUtility.FromUnityLight(light);
                LightCapabilities capabilities = LightShapeUtility.GetCapabilities(shape);
                if (mode != LightmapBakeType.Realtime && !capabilities.baking)
                    throw new InvalidOperationException(capabilities.bakeDisabledReason ?? "This light shape is Realtime only.");
            }

            int group = Begin("Light Baking Mode");
            try
            {
                foreach (Light light in lights)
                {
                    if (light.lightmapBakeType == mode)
                        continue;
                    light.TryGetComponent(out InfinityAdditionalLightData data);
                    LightSourceShape shape = data ? data.shape : LightShapeUtility.FromUnityLight(light);
                    EResolvedLightShape resolved = LightShapeUtility.Resolve(shape);
                    bool needsDiskCarrier = (resolved == EResolvedLightShape.Disk || resolved == EResolvedLightShape.DiskCone)
                        && light.type == LightType.Disc && mode != LightmapBakeType.Baked;
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

        internal static bool ApplySceneEdit(
            Light light,
            in LightSourceShape previous,
            in LightSourceShape current,
            float range,
            float spotAngle,
            Func<string, bool> confirmRealtimeSwitch = null,
            bool mergeDrag = false)
        {
            bool shapeChanged = !previous.Equals(current);
            float desiredRange = light.range == range ? light.range : Mathf.Max(0.01f, range);
            float desiredSpotAngle = light.spotAngle == spotAngle ? light.spotAngle : Mathf.Clamp(spotAngle, 1f, 179f);
            bool spotAngleChanged = !Mathf.Approximately(light.spotAngle, desiredSpotAngle);
            if (!shapeChanged && light.range == desiredRange && !spotAngleChanged)
                return false;

            bool switchToRealtime = false;
            if (shapeChanged)
            {
                LightShapeUtility.NativeType(current);
                LightCapabilities capabilities = LightShapeUtility.GetCapabilities(current);
                if (!capabilities.baking && light.lightmapBakeType != LightmapBakeType.Realtime)
                {
                    switchToRealtime = true;
                    string reason = capabilities.bakeDisabledReason ?? "This light shape is Realtime only.";
                    string message = $"{reason}\n\nApply the new shape and switch the selected Mixed/Baked light mode to Realtime?";
                    if (!(confirmRealtimeSwitch ?? DefaultConfirmRealtimeSwitch)(message))
                        return false;
                }
            }

            light.TryGetComponent(out InfinityAdditionalLightData existingData);
            ELightIntensityEditMode editMode = existingData ? existingData.intensityEditMode : ELightIntensityEditMode.PhysicallyBased;
            float prevOuter = LightIntensityUtility.GetEffectiveSpotAngle(light);
            float prevInner = LightIntensityUtility.GetEffectiveInnerSpotAngle(light, prevOuter);
            float nextInner = Mathf.Clamp(prevInner, 0f, desiredSpotAngle);
            float adjustedIntensity = (shapeChanged || spotAngleChanged)
                ? LightIntensityUtility.AdjustIntensityForShapeEdit(
                    light.intensity, previous, current, prevInner, prevOuter, nextInner, desiredSpotAngle, editMode)
                : light.intensity;

            int group = Begin("Edit Light", mergeDrag);
            try
            {
                Undo.RecordObject(light, "Edit Light");
                if (switchToRealtime)
                {
                    light.lightmapBakeType = LightmapBakeType.Realtime;
                    if (light.lightmapBakeType != LightmapBakeType.Realtime)
                        throw new InvalidOperationException("Unity rejected switching the light mode to Realtime.");
                }
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
                if (spotAngleChanged)
                {
                    light.spotAngle = desiredSpotAngle;
                    if (light.innerSpotAngle > desiredSpotAngle)
                        light.innerSpotAngle = desiredSpotAngle;
                }
                if (!Mathf.Approximately(light.intensity, adjustedIntensity))
                    light.intensity = adjustedIntensity;
                EditorUtility.SetDirty(light);
                RecordPrefab(light);
                Complete();
                return true;
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

        static int Begin(string name, bool mergeDrag = false)
        {
            int hotControl = GUIUtility.hotControl;
            bool continuingDrag = mergeDrag && hotControl != 0 && hotControl == s_ActiveDragHotControl
                && Undo.GetCurrentGroup() == s_ActiveDragUndoGroup;
            if (!continuingDrag)
            {
                Undo.IncrementCurrentGroup();
                int nextGroup = Undo.GetCurrentGroup();
                s_ActiveDragHotControl = mergeDrag && hotControl != 0 ? hotControl : 0;
                s_ActiveDragUndoGroup = mergeDrag && hotControl != 0 ? nextGroup : -1;
            }
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
