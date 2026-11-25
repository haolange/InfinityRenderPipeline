using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using InfinityTech.Component;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Component.Editor;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class LightShapeTests
    {
        [TestCase(0f, 1000f)]
        [TestCase(0.25f, 3750f)]
        [TestCase(0.5f, 6500f)]
        [TestCase(0.75f, 13250f)]
        [TestCase(1f, 20000f)]
        [Category("LightInspector")]
        public void ColorTemperatureSlider_RoundTripsItsGradientStops(float position, float kelvin)
        {
            Assert.AreEqual(kelvin, InfinityLightInspectorGUI.KelvinFromPosition(position), 0.01f);
            Assert.AreEqual(position, InfinityLightInspectorGUI.PositionFromKelvin(kelvin), 1e-5f);
        }

        static Light NewLight(LightType type, out GameObject go)
        {
            go = new GameObject("LightShapeTest");
            Light light = go.AddComponent<Light>();
            light.type = type;
            return light;
        }

        [TestCase(ELightKind.Local, ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional, 0.0f, 0.0f, LightType.Point, true)]
        [TestCase(ELightKind.Local, ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional, 0.2f, 0.0f, LightType.Point, true)]
        [TestCase(ELightKind.Local, ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional, 0.0f, 1.5f, LightType.Tube, false)]
        [TestCase(ELightKind.Local, ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional, 0.1f, 1.5f, LightType.Tube, false)]
        [TestCase(ELightKind.Local, ELocalLightEmitter.Rectangle, ELocalLightDistribution.Hemisphere, 0.0f, 0.0f, LightType.Rectangle, true)]
        [TestCase(ELightKind.Local, ELocalLightEmitter.Disk, ELocalLightDistribution.Hemisphere, 0.5f, 0.0f, LightType.Rectangle, true)]
        [TestCase(ELightKind.Local, ELocalLightEmitter.Disk, ELocalLightDistribution.Cone, 0.5f, 0.0f, LightType.Rectangle, false)]
        [TestCase(ELightKind.Directional, ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional, 0.0f, 0.0f, LightType.Directional, true)]
        public void ShapeParameters_DeriveTheNativeType(
            ELightKind kind,
            ELocalLightEmitter emitter,
            ELocalLightDistribution distribution,
            float radius,
            float length,
            LightType expected,
            bool expectedBakeable)
        {
            LightSourceShape shape = kind == ELightKind.Directional
                ? LightSourceShape.DefaultDirectional()
                : LightSourceShape.DefaultLocal(emitter, distribution);
            shape.sourceRadius = radius;
            shape.sourceLength = length;
            Assert.AreEqual(expected, LightShapeUtility.NativeType(shape));
            Assert.AreEqual(expectedBakeable, LightShapeUtility.IsBakeable(shape));
        }

        [TestCase(ELocalLightDistribution.Cone, LightType.Spot)]
        [TestCase(ELocalLightDistribution.Pyramid, LightType.Pyramid)]
        [TestCase(ELocalLightDistribution.ParallelBox, LightType.Box)]
        public void SpotDistributions_DeriveTheNativeType(ELocalLightDistribution distribution, LightType expected)
        {
            LightSourceShape shape = LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, distribution);
            Assert.AreEqual(expected, LightShapeUtility.NativeType(shape));
        }

        [Test]
        public void ApplyThenRead_RoundTripsEveryShape()
        {
            Light light = NewLight(LightType.Point, out GameObject go);
            try
            {
                var shapes = new[]
                {
                    new LightSourceShape { kind = ELightKind.Local, emitter = ELocalLightEmitter.Round, distribution = ELocalLightDistribution.Omnidirectional, sourceRadius = 0.15f, sourceLength = 2.0f, sourceWidth = 1, sourceHeight = 1, pyramidAspect = 1, boxSize = Vector2.one },
                    new LightSourceShape { kind = ELightKind.Local, emitter = ELocalLightEmitter.Round, distribution = ELocalLightDistribution.Pyramid, sourceRadius = 0.05f, pyramidAspect = 2.0f, sourceWidth = 1, sourceHeight = 1, boxSize = Vector2.one },
                    new LightSourceShape { kind = ELightKind.Local, emitter = ELocalLightEmitter.Round, distribution = ELocalLightDistribution.ParallelBox, boxSize = new Vector2(3, 2), sourceWidth = 1, sourceHeight = 1, pyramidAspect = 1 },
                    new LightSourceShape { kind = ELightKind.Local, emitter = ELocalLightEmitter.Rectangle, distribution = ELocalLightDistribution.Hemisphere, sourceWidth = 2.0f, sourceHeight = 0.5f, pyramidAspect = 1, boxSize = Vector2.one },
                    new LightSourceShape { kind = ELightKind.Local, emitter = ELocalLightEmitter.Disk, distribution = ELocalLightDistribution.Hemisphere, sourceRadius = 0.75f, sourceWidth = 1, sourceHeight = 1, pyramidAspect = 1, boxSize = Vector2.one },
                    new LightSourceShape { kind = ELightKind.Directional, emitter = ELocalLightEmitter.Round, distribution = ELocalLightDistribution.Omnidirectional, sourceAngle = 0.5357f, sourceWidth = 1, sourceHeight = 1, pyramidAspect = 1, boxSize = Vector2.one },
                };
                foreach (LightSourceShape shape in shapes)
                {
                    LightShapeUtility.ApplyToUnityLight(light, shape);
                    LightSourceShape read = LightShapeUtility.FromUnityLight(light);
                    if (shape.emitter == ELocalLightEmitter.Disk)
                    {
                        // The carrier does not store Disk identity; authored shape lives on Additional data.
                        Assert.AreEqual(ELocalLightEmitter.Rectangle, read.emitter);
                        Assert.AreEqual(ELocalLightDistribution.Hemisphere, read.distribution);
                        Assert.AreEqual(Mathf.Sqrt(Mathf.PI) * shape.sourceRadius, read.sourceWidth, 1e-5f);
                        Assert.AreEqual(read.sourceWidth, read.sourceHeight, 1e-5f);
                        Assert.IsFalse(LightShapeUtility.ApplyToUnityLight(light, shape), "Second apply is a no-op.");
                        continue;
                    }
                    Assert.AreEqual(shape.kind, read.kind);
                    Assert.AreEqual(shape.emitter, read.emitter);
                    Assert.AreEqual(shape.distribution, read.distribution);
                    Assert.AreEqual(shape.sourceRadius, read.sourceRadius, 1e-5f, shape.emitter + " radius");
                    Assert.AreEqual(shape.sourceLength, read.sourceLength, 1e-5f, shape.emitter + " length");
                    if (shape.emitter == ELocalLightEmitter.Rectangle)
                    {
                        Assert.AreEqual(shape.sourceWidth, read.sourceWidth, 1e-5f);
                        Assert.AreEqual(shape.sourceHeight, read.sourceHeight, 1e-5f);
                    }
                    if (shape.distribution == ELocalLightDistribution.Pyramid) Assert.AreEqual(shape.pyramidAspect, read.pyramidAspect, 1e-5f);
                    if (shape.distribution == ELocalLightDistribution.ParallelBox) Assert.AreEqual(shape.boxSize, read.boxSize);
                    if (shape.kind == ELightKind.Directional) Assert.AreEqual(shape.sourceAngle, read.sourceAngle, 1e-5f);
                    Assert.IsFalse(LightShapeUtility.ApplyToUnityLight(light, shape), "A second apply changes nothing.");
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void AddingTheComponent_AdoptsTheNativeShape()
        {
            Light light = NewLight(LightType.Rectangle, out GameObject go);
            try
            {
                light.areaSize = new Vector2(3, 1);
                var data = go.AddComponent<InfinityAdditionalLightData>();
                LightSourceShape shape = data.shape;
                Assert.AreEqual(EResolvedLightShape.Rect, LightShapeUtility.Resolve(shape));
                Assert.AreEqual(3.0f, shape.sourceWidth, 1e-5f);
                Assert.AreEqual(LightType.Rectangle, light.type, "Adding the component never changes the light.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void PlayerBuild_FreezesTheShapeOfEverySceneLight()
        {
            Light bare = NewLight(LightType.Rectangle, out GameObject bareObject);
            Light sun = NewLight(LightType.Directional, out GameObject sunObject);
            try
            {
                bare.areaSize = new Vector2(3, 1);
                bare.shadows = LightShadows.Hard;
                sun.shadowAngle = 2.5f;
                var sunData = sunObject.AddComponent<InfinityAdditionalLightData>();
                var serialized = new SerializedObject(sunData);
                serialized.FindProperty("m_ShapeAuthored").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                sunData.shadowFilter = EShadowFilter.PCSS;
                Assert.IsFalse(sunData.shapeAuthored);

                LightShapeBuildProcessor.Freeze(new[] { bareObject, sunObject });

                var bareData = bareObject.GetComponent<InfinityAdditionalLightData>();
                Assert.IsNotNull(bareData, "A light without the component gets one in the built scene.");
                Assert.IsTrue(bareData.shapeAuthored);
                Assert.AreEqual(3.0f, bareData.shape.sourceWidth, 1e-5f);
                Assert.AreEqual(1.0f, bareData.shape.sourceHeight, 1e-5f);
                Assert.AreEqual(0.0f, bareData.maxDrawDistance, "No distance fade, like a light without the component.");
                Assert.AreEqual(EShadowFilter.Hard, bareData.shadowFilter);
                Assert.IsTrue(sunData.shapeAuthored);
                Assert.AreEqual(2.5f, sunData.shape.sourceAngle, 1e-5f);
                Assert.AreEqual(EShadowFilter.PCSS, sunData.shadowFilter, "Freezing keeps the light's shadow filter.");
                Assert.AreEqual(LightType.Rectangle, bare.type, "Freezing never writes the native light.");
            }
            finally
            {
                Object.DestroyImmediate(bareObject);
                Object.DestroyImmediate(sunObject);
            }
        }

        [TestCase(LightmapBakeType.Realtime)]
        [TestCase(LightmapBakeType.Mixed)]
        [TestCase(LightmapBakeType.Baked)]
        public void ShapeEdits_PreserveNativeBakeModeThroughUndoRedo(LightmapBakeType mode)
        {
            Light light = NewLight(LightType.Point, out GameObject go);
            try
            {
                var data = go.AddComponent<InfinityAdditionalLightData>();
                light.lightmapBakeType = mode;
                string beforeNative = EditorJsonUtility.ToJson(light);
                string beforeShape = EditorJsonUtility.ToJson(data);
                Undo.IncrementCurrentGroup();
                Undo.RecordObjects(new Object[] { light, data }, "Change light shape");
                var disk = LightSourceShape.DefaultLocal(ELocalLightEmitter.Disk, ELocalLightDistribution.Hemisphere);
                disk.sourceRadius = 0.85f;
                data.SetShape(disk);
                Undo.FlushUndoRecordObjects();
                Assert.AreEqual(LightType.Rectangle, light.type);
                Assert.AreEqual(mode, light.lightmapBakeType);
                string afterNative = EditorJsonUtility.ToJson(light);
                string afterShape = EditorJsonUtility.ToJson(data);
                Undo.PerformUndo();
                Assert.AreEqual(beforeNative, EditorJsonUtility.ToJson(light));
                Assert.AreEqual(beforeShape, EditorJsonUtility.ToJson(data));
                Assert.AreEqual(LightType.Point, light.type);
                Assert.AreEqual(mode, light.lightmapBakeType);
                Undo.PerformRedo();
                Assert.AreEqual(afterNative, EditorJsonUtility.ToJson(light));
                Assert.AreEqual(afterShape, EditorJsonUtility.ToJson(data));
                Assert.AreEqual(LightType.Rectangle, light.type);
                Assert.AreEqual(mode, light.lightmapBakeType);
                data.SetShape(LightSourceShape.DefaultLocal(ELocalLightEmitter.Rectangle, ELocalLightDistribution.Hemisphere));
                Assert.AreEqual(mode, light.lightmapBakeType);
                Undo.ClearUndo(data);
                Undo.ClearUndo(light);
            }
            finally
            {
                Undo.FlushUndoRecordObjects();
                var data = go.GetComponent<InfinityAdditionalLightData>();
                if (data) Undo.ClearUndo(data);
                Undo.ClearUndo(light);
                Undo.ClearUndo(go);
                Object.DestroyImmediate(go);
            }
        }

        [TestCase(LightmapBakeType.Realtime)]
        [TestCase(LightmapBakeType.Mixed)]
        [TestCase(LightmapBakeType.Baked)]
        public void AuthoredDisk_KeepsDiscBakeShapeOnNativeCarrier(LightmapBakeType mode)
        {
            Light light = NewLight(LightType.Point, out GameObject go);
            try
            {
                var data = go.AddComponent<InfinityAdditionalLightData>();
                light.lightmapBakeType = mode;
                var disk = LightSourceShape.DefaultLocal(ELocalLightEmitter.Disk, ELocalLightDistribution.Hemisphere);
                disk.sourceRadius = 0.85f;
                data.SetShape(disk);
                Assert.AreEqual(LightType.Rectangle, light.type);
                Assert.AreEqual(mode, light.lightmapBakeType);
                Assert.AreEqual(EResolvedLightShape.Disk, LightShapeUtility.Resolve(LightRecordPack.ResolveShape(light, data)));
                using (var output = new Unity.Collections.NativeArray<UnityEngine.Experimental.GlobalIllumination.LightDataGI>(1, Unity.Collections.Allocator.Temp))
                {
                    InfinityLightmapperDelegate.Delegate(new[] { light }, output);
                    Assert.AreEqual(UnityEngine.Experimental.GlobalIllumination.LightType.Disc, output[0].type);
                    Assert.AreEqual(disk.sourceRadius, output[0].shape0, 1e-6f);
                    Assert.AreEqual(mode.ToString(), output[0].mode.ToString());
                    Assert.AreEqual(light.GetEntityId(), output[0].entityId);
                }
            }
            finally
            {
                Undo.FlushUndoRecordObjects();
                var data = go.GetComponent<InfinityAdditionalLightData>();
                if (data) Undo.ClearUndo(data);
                Undo.ClearUndo(light);
                Undo.ClearUndo(go);
                Object.DestroyImmediate(go);
            }
        }

        [Category("LightInspector")]
        [TestCase(LightmapBakeType.Realtime)]
        [TestCase(LightmapBakeType.Mixed)]
        public void NativeDisk_ModeOnlyEditUsesCarrierInOneUndo(LightmapBakeType mode)
        {
            using (var fixture = new LightInspectorFixture())
            {
                Light light = fixture.NewLight(LightType.Disc);
                light.lightmapBakeType = LightmapBakeType.Baked;
                light.areaSize = new Vector2(0.85f, 1);
                var data = light.gameObject.AddComponent<InfinityAdditionalLightData>();
                string native = EditorJsonUtility.ToJson(light);
                Assert.AreEqual(EResolvedLightShape.Disk, LightShapeUtility.Resolve(data.shape));
                Assert.AreEqual(LightType.Disc, light.type, "Adoption must not migrate native fields.");

                InfinityLightEditorTransactions.ApplyBakeMode(new[] { light }, mode);

                Assert.AreEqual(LightType.Rectangle, light.type);
                Assert.AreEqual(mode, light.lightmapBakeType);
                Undo.PerformUndo();
                Assert.AreEqual(native, EditorJsonUtility.ToJson(light));
                Undo.PerformRedo();
                Assert.AreEqual(LightType.Rectangle, light.type);
                Assert.AreEqual(mode, light.lightmapBakeType);
                Assert.AreEqual(EResolvedLightShape.Disk, LightShapeUtility.Resolve(data.shape));
            }
        }

        [Category("LightInspector")]
        [TestCase(false)]
        [TestCase(true)]
        public void LightInspectorCreation_DoesNotModifyTargetsOrAddUndo(bool withAdditional)
        {
            using (var fixture = new LightInspectorFixture())
            {
                Light light = fixture.NewLight(LightType.Rectangle);
                light.areaSize = new Vector2(3, 1);
                InfinityAdditionalLightData data = null;
                if (withAdditional)
                {
                    data = light.gameObject.AddComponent<InfinityAdditionalLightData>();
                    SetUnauthored(data);
                    data.shadowFilter = EShadowFilter.PCSS;
                }
                string native = EditorJsonUtility.ToJson(light);
                string additional = data ? EditorJsonUtility.ToJson(data) : null;
                Light marker = fixture.NewLight(LightType.Point);
                Undo.IncrementCurrentGroup();
                Undo.RecordObject(marker.transform, "Inspector sentinel");
                marker.transform.position = Vector3.one;
                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
                bool sceneDirty = fixture.scene.isDirty;

                var inspector = UnityEditor.Editor.CreateEditor(light, typeof(InfinityLightEditor));
                try
                {
                    Assert.IsNotNull(inspector);
                    Assert.AreEqual(native, EditorJsonUtility.ToJson(light));
                    Assert.AreSame(data, light.GetComponent<InfinityAdditionalLightData>());
                    if (data)
                    {
                        Assert.AreEqual(additional, EditorJsonUtility.ToJson(data));
                        Assert.IsFalse(data.shapeAuthored);
                        Assert.AreEqual(EShadowFilter.PCSS, data.shadowFilter);
                    }
                    Assert.AreEqual(sceneDirty, fixture.scene.isDirty);
                }
                finally
                {
                    if (inspector) Object.DestroyImmediate(inspector);
                }

                Undo.PerformUndo();
                Assert.AreEqual(Vector3.zero, marker.transform.position, "Opening the inspector must not create an Undo entry.");
                Assert.AreEqual(native, EditorJsonUtility.ToJson(light));
                Assert.AreSame(data, light.GetComponent<InfinityAdditionalLightData>());
            }
        }

        [Test, Category("LightInspector")]
        public void AdditionalDataInspector_UsesTheEmptyEditorWithoutModifyingData()
        {
            using (var fixture = new LightInspectorFixture())
            {
                Light light = fixture.NewLight(LightType.Point);
                var data = light.gameObject.AddComponent<InfinityAdditionalLightData>();
                data.diffuse = 0.4f;
                string native = EditorJsonUtility.ToJson(light);
                string additional = EditorJsonUtility.ToJson(data);
                bool sceneDirty = fixture.scene.isDirty;
                var inspector = UnityEditor.Editor.CreateEditor(data);
                try
                {
                    Assert.IsInstanceOf<InfinityAdditionalLightDataEditor>(inspector);
                    Assert.AreEqual(additional, EditorJsonUtility.ToJson(data));
                    Assert.AreEqual(native, EditorJsonUtility.ToJson(light));
                    Assert.AreEqual(sceneDirty, fixture.scene.isDirty);
                }
                finally
                {
                    if (inspector) Object.DestroyImmediate(inspector);
                }
            }
        }

        [Test, Category("LightInspector")]
        public void LightInspector_BareLightSnapshotFieldsRemainEditable()
        {
            using (var fixture = new LightInspectorFixture())
            {
                Light light = fixture.NewLight(LightType.Point);
                var inspector = UnityEditor.Editor.CreateEditor(light, typeof(InfinityLightEditor));
                try
                {
                    var field = typeof(InfinityLightEditor).GetField("m_After", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    var snapshots = (InfinityLightInspectorSnapshot[])field.GetValue(inspector);
                    using var serialized = new SerializedObject(snapshots[0]);
                    Assert.IsTrue(serialized.FindProperty("m_Shape.kind").editable);
                    Assert.IsTrue(serialized.FindProperty("m_Shape.emitter").editable);
                    Assert.IsTrue(serialized.FindProperty("m_Shape.distribution").editable);
                    Assert.IsTrue(serialized.FindProperty("m_Shape.sourceRadius").editable);
                    Assert.IsTrue(serialized.FindProperty("maxDrawDistance").editable);
                    Assert.IsNull(light.GetComponent<InfinityAdditionalLightData>());
                }
                finally
                {
                    Object.DestroyImmediate(inspector);
                }
            }
        }

        [Test, Category("LightInspector")]
        public void FirstAdditionalEdit_PreservesBareLightAndUndoRedo()
        {
            using (var fixture = new LightInspectorFixture())
            {
                Light light = fixture.NewLight(LightType.Point);
                light.shadows = LightShadows.Hard;
                string native = EditorJsonUtility.ToJson(light);
                var before = new SerializedObject(fixture.Snapshot(light));
                var after = new SerializedObject(fixture.Snapshot(light));
                after.FindProperty("diffuse").floatValue = 0.4f;
                after.ApplyModifiedPropertiesWithoutUndo();

                InfinityLightEditorTransactions.ApplyAdditional(new[] { light }, new[] { before }, new[] { after }, "diffuse");

                var data = light.GetComponent<InfinityAdditionalLightData>();
                Assert.IsNotNull(data);
                Assert.AreEqual(0.4f, data.diffuse, 1e-6f);
                Assert.AreEqual(0, data.maxDrawDistance);
                Assert.AreEqual(EShadowFilter.Hard, data.shadowFilter);
                Assert.IsFalse(data.shapeAuthored);
                Assert.AreEqual(native, EditorJsonUtility.ToJson(light));
                Undo.PerformUndo();
                Assert.IsNull(light.GetComponent<InfinityAdditionalLightData>());
                Assert.AreEqual(native, EditorJsonUtility.ToJson(light));
                Undo.PerformRedo();
                data = light.GetComponent<InfinityAdditionalLightData>();
                Assert.IsNotNull(data);
                Assert.AreEqual(0.4f, data.diffuse, 1e-6f);
                Assert.AreEqual(0, data.maxDrawDistance);
                Assert.AreEqual(EShadowFilter.Hard, data.shadowFilter);
                Assert.IsFalse(data.shapeAuthored);
                Assert.AreEqual(native, EditorJsonUtility.ToJson(light));
            }
        }

        [Category("LightInspector")]
        [TestCase(LightShadows.None)]
        [TestCase(LightShadows.Hard)]
        [TestCase(LightShadows.Soft)]
        public void BareSnapshot_AndFirstEditMatchRealtimeDefaults(LightShadows shadows)
        {
            using (var fixture = new LightInspectorFixture())
            {
                Light light = fixture.NewLight(LightType.Point);
                light.shadows = shadows;
                var beforeSnapshot = fixture.Snapshot(light);
                var afterSnapshot = fixture.Snapshot(light);
                using var before = new SerializedObject(beforeSnapshot);
                using var after = new SerializedObject(afterSnapshot);
                var visible = VisibleLightTestUtility.FromLight(light);
                LightSourceShape shape = LightShapeUtility.FromUnityLight(light);
                Assert.IsTrue(LightRecordPack.TryPack(visible, light, null, shape, 0, Vector3.one * 1000, out LightRecord bare));
                Assert.AreEqual(bare.weights, new Vector4(beforeSnapshot.diffuse, beforeSnapshot.specular,
                    beforeSnapshot.volumetricIntensity, beforeSnapshot.volumetricOcclusion));
                Assert.AreEqual((bare.flags & LightRecordFlags.EnableContactShadow) != 0, beforeSnapshot.enableContactShadow);
                Assert.AreEqual((bare.flags & LightRecordFlags.EnableVolumetric) != 0, beforeSnapshot.enableVolumetric);
                Assert.AreEqual(bare.lightLayer, (int)beforeSnapshot.lightLayer);
                Assert.AreEqual(bare.shadowFilter, before.FindProperty("m_ShadowFilter").enumValueIndex);
                Assert.AreEqual(0f, beforeSnapshot.maxDrawDistance);

                after.FindProperty("maxDrawDistanceFade").floatValue = 2;
                after.ApplyModifiedPropertiesWithoutUndo();
                InfinityLightEditorTransactions.ApplyAdditional(new[] { light }, new[] { before }, new[] { after }, "maxDrawDistanceFade");

                var data = light.GetComponent<InfinityAdditionalLightData>();
                Assert.IsNotNull(data);
                Assert.IsTrue(LightRecordPack.TryPack(visible, light, data, data.shape, 0, Vector3.one * 1000, out LightRecord prepared));
                Assert.AreEqual(bare.weights, prepared.weights);
                Assert.AreEqual(bare.flags, prepared.flags);
                Assert.AreEqual(bare.lightLayer, prepared.lightLayer);
                Assert.AreEqual(bare.shadowFilter, prepared.shadowFilter);
                Assert.AreEqual(bare.radiance, prepared.radiance);
            }
        }

        [Category("LightInspector")]
        [TestCase("maxDrawDistance")]
        [TestCase("maxDrawDistanceFade")]
        [TestCase("iesProfile")]
        public void BareSnapshot_UsesAdditionalDataTooltips(string propertyPath)
        {
            using (var fixture = new LightInspectorFixture())
            {
                Light light = fixture.NewLight(LightType.Point);
                var snapshot = fixture.Snapshot(light);
                var data = light.gameObject.AddComponent<InfinityAdditionalLightData>();
                using var snapshotObject = new SerializedObject(snapshot);
                using var dataObject = new SerializedObject(data);
                Assert.AreEqual(dataObject.FindProperty(propertyPath).tooltip, snapshotObject.FindProperty(propertyPath).tooltip);
                Assert.IsNotEmpty(snapshotObject.FindProperty(propertyPath).tooltip);
            }
        }

        [Test, Category("LightInspector")]
        public void UnchangedAdditionalEdit_DoesNotCreateComponents()
        {
            using (var fixture = new LightInspectorFixture())
            {
                Light first = fixture.NewLight(LightType.Point);
                Light second = fixture.NewLight(LightType.Spot);
                var before = new[] { new SerializedObject(fixture.Snapshot(first)), new SerializedObject(fixture.Snapshot(second)) };
                var after = new[] { new SerializedObject(fixture.Snapshot(first)), new SerializedObject(fixture.Snapshot(second)) };

                InfinityLightEditorTransactions.ApplyAdditional(new[] { first, second }, before, after, "diffuse");

                Assert.IsNull(first.GetComponent<InfinityAdditionalLightData>());
                Assert.IsNull(second.GetComponent<InfinityAdditionalLightData>());
            }
        }

        [Test, Category("LightInspector")]
        public void AdditionalEdit_DoesNotAdoptUnauthoredShapeOrReplaceFilter()
        {
            using (var fixture = new LightInspectorFixture())
            {
                Light light = fixture.NewLight(LightType.Rectangle);
                light.areaSize = new Vector2(3, 1);
                var data = light.gameObject.AddComponent<InfinityAdditionalLightData>();
                SetUnauthored(data);
                data.shadowFilter = EShadowFilter.PCSS;
                string native = EditorJsonUtility.ToJson(light);
                var before = new SerializedObject(fixture.Snapshot(light));
                var after = new SerializedObject(fixture.Snapshot(light));
                after.FindProperty("maxDrawDistance").floatValue = 42;
                after.ApplyModifiedPropertiesWithoutUndo();

                InfinityLightEditorTransactions.ApplyAdditional(new[] { light }, new[] { before }, new[] { after }, "maxDrawDistance");

                Assert.AreEqual(42, data.maxDrawDistance);
                Assert.IsFalse(data.shapeAuthored);
                Assert.AreEqual(EShadowFilter.PCSS, data.shadowFilter);
                Assert.AreEqual(native, EditorJsonUtility.ToJson(light));
                Undo.PerformUndo();
                Assert.AreEqual(128, data.maxDrawDistance);
                Assert.IsFalse(data.shapeAuthored);
                Assert.AreEqual(EShadowFilter.PCSS, data.shadowFilter);
                Undo.PerformRedo();
                Assert.AreEqual(42, data.maxDrawDistance);
                Assert.IsFalse(data.shapeAuthored);
                Assert.AreEqual(EShadowFilter.PCSS, data.shadowFilter);
            }
        }

        [Test, Category("LightInspector")]
        public void ShapeEdit_OnlyChangesAffectedTargetsAndPreservesTheirOtherValues()
        {
            using (var fixture = new LightInspectorFixture())
            {
                Light first = fixture.NewLight(LightType.Rectangle);
                Light second = fixture.NewLight(LightType.Rectangle);
                first.areaSize = new Vector2(2, 1);
                second.areaSize = new Vector2(4, 3);
                string firstNative = EditorJsonUtility.ToJson(first);
                string secondNative = EditorJsonUtility.ToJson(second);
                var before = new[] { fixture.Snapshot(first), fixture.Snapshot(second) };
                var after = new[] { fixture.Snapshot(first), fixture.Snapshot(second) };
                using var selected = new SerializedObject(new Object[] { after[0], after[1] });
                using var firstSnapshot = new SerializedObject(after[0]);
                using var secondSnapshot = new SerializedObject(after[1]);
                SerializedProperty width = selected.FindProperty("m_Shape.sourceWidth");
                width.floatValue = 2;
                InfinityLightEditorTransactions.StageProperty(width, new[] { firstSnapshot, secondSnapshot });

                InfinityLightEditorTransactions.ApplyShape(new[] { first, second }, before, after);

                Assert.IsNull(first.GetComponent<InfinityAdditionalLightData>());
                Assert.AreEqual(firstNative, EditorJsonUtility.ToJson(first));
                var data = second.GetComponent<InfinityAdditionalLightData>();
                Assert.IsNotNull(data);
                Assert.IsTrue(data.shapeAuthored);
                Assert.AreEqual(2, data.shape.sourceWidth);
                Assert.AreEqual(3, data.shape.sourceHeight);
                Assert.AreEqual(new Vector2(2, 3), second.areaSize);
                Undo.PerformUndo();
                Assert.IsNull(first.GetComponent<InfinityAdditionalLightData>());
                Assert.IsNull(second.GetComponent<InfinityAdditionalLightData>());
                Assert.AreEqual(firstNative, EditorJsonUtility.ToJson(first));
                Assert.AreEqual(secondNative, EditorJsonUtility.ToJson(second));
                Undo.PerformRedo();
                data = second.GetComponent<InfinityAdditionalLightData>();
                Assert.IsNotNull(data);
                Assert.AreEqual(2, data.shape.sourceWidth);
                Assert.AreEqual(3, data.shape.sourceHeight);
                Assert.AreEqual(new Vector2(2, 3), second.areaSize);
                Assert.IsNull(first.GetComponent<InfinityAdditionalLightData>());
            }
        }

        [Test, Category("LightInspector")]
        public void ShapeEdit_StagesActualMultiSelectionThroughSnapshots()
        {
            using (var fixture = new LightInspectorFixture())
            {
                Light first = fixture.NewLight(LightType.Point);
                Light second = fixture.NewLight(LightType.Point);
                var firstData = first.gameObject.AddComponent<InfinityAdditionalLightData>();
                var secondData = second.gameObject.AddComponent<InfinityAdditionalLightData>();
                var firstShape = LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional);
                firstShape.sourceRadius = 0.1f;
                firstShape.sourceLength = 0.2f;
                firstData.SetShape(firstShape);
                var secondShape = LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional);
                secondShape.sourceRadius = 0.3f;
                secondShape.sourceLength = 0.4f;
                secondData.SetShape(secondShape);
                var before = new[] { fixture.Snapshot(first), fixture.Snapshot(second) };
                var after = new[] { fixture.Snapshot(first), fixture.Snapshot(second) };
                using var actual = new SerializedObject(new Object[] { firstData, secondData });
                using var firstSnapshot = new SerializedObject(after[0]);
                using var secondSnapshot = new SerializedObject(after[1]);
                SerializedProperty radius = actual.FindProperty("m_Shape.sourceRadius");
                Assert.IsTrue(radius.hasMultipleDifferentValues);
                radius.floatValue = 0.5f;
                InfinityLightEditorTransactions.StageProperty(radius, new[] { firstSnapshot, secondSnapshot });

                InfinityLightEditorTransactions.ApplyShape(new[] { first, second }, before, after);

                Assert.AreEqual(0.5f, firstData.shape.sourceRadius, 1e-6f);
                Assert.AreEqual(0.5f, secondData.shape.sourceRadius, 1e-6f);
                Assert.AreEqual(0.2f, firstData.shape.sourceLength, 1e-6f);
                Assert.AreEqual(0.4f, secondData.shape.sourceLength, 1e-6f);
                Assert.AreEqual(0.5f, first.shapeRadius, 1e-6f);
                Assert.AreEqual(0.5f, second.shapeRadius, 1e-6f);
                Undo.PerformUndo();
                Assert.AreEqual(0.1f, firstData.shape.sourceRadius, 1e-6f);
                Assert.AreEqual(0.3f, secondData.shape.sourceRadius, 1e-6f);
                Assert.AreEqual(0.1f, first.shapeRadius, 1e-6f);
                Assert.AreEqual(0.3f, second.shapeRadius, 1e-6f);
            }
        }

        [Test, Category("LightInspector")]
        public void ShapeEdits_KeepInterveningUndoSeparate()
        {
            using (var fixture = new LightInspectorFixture())
            {
                Light light = fixture.NewLight(LightType.Point);
                Light marker = fixture.NewLight(LightType.Point);
                float original = light.shapeRadius;
                var before = new[] { fixture.Snapshot(light) };
                var after = new[] { fixture.Snapshot(light) };
                using (var edited = new SerializedObject(after[0]))
                {
                    edited.FindProperty("m_Shape.sourceRadius").floatValue = 0.5f;
                    edited.ApplyModifiedPropertiesWithoutUndo();
                }
                InfinityLightEditorTransactions.ApplyShape(new[] { light }, before, after);

                Undo.IncrementCurrentGroup();
                Undo.RecordObject(marker.transform, "Intervening edit");
                marker.transform.position = Vector3.one;
                Undo.FlushUndoRecordObjects();

                before = new[] { fixture.Snapshot(light) };
                after = new[] { fixture.Snapshot(light) };
                using (var edited = new SerializedObject(after[0]))
                {
                    edited.FindProperty("m_Shape.sourceRadius").floatValue = 1;
                    edited.ApplyModifiedPropertiesWithoutUndo();
                }
                InfinityLightEditorTransactions.ApplyShape(new[] { light }, before, after);

                Undo.PerformUndo();
                Assert.AreEqual(0.5f, light.shapeRadius, 1e-6f);
                Assert.AreEqual(Vector3.one, marker.transform.position);
                Undo.PerformUndo();
                Assert.AreEqual(Vector3.zero, marker.transform.position);
                Assert.AreEqual(0.5f, light.shapeRadius, 1e-6f);
                Undo.PerformUndo();
                Assert.IsNull(light.GetComponent<InfinityAdditionalLightData>());
                Assert.AreEqual(original, light.shapeRadius, 1e-6f);
                Undo.PerformRedo();
                Undo.PerformRedo();
                Undo.PerformRedo();
                var data = light.GetComponent<InfinityAdditionalLightData>();
                Assert.IsNotNull(data);
                Assert.AreEqual(1, data.shape.sourceRadius, 1e-6f);
                Assert.AreEqual(1, light.shapeRadius, 1e-6f);
                Assert.AreEqual(Vector3.one, marker.transform.position);
            }
        }

        [Test, Category("LightInspector")]
        public void ShapeEdit_FailureRollsBackEveryTarget()
        {
            using (var fixture = new LightInspectorFixture())
            {
                Light first = fixture.NewLight(LightType.Point);
                Light second = fixture.NewLight(LightType.Point);
                string firstNative = EditorJsonUtility.ToJson(first);
                string secondNative = EditorJsonUtility.ToJson(second);
                var before = new[] { fixture.Snapshot(first), fixture.Snapshot(second) };
                var after = new[] { fixture.Snapshot(first), fixture.Snapshot(second) };
                using var selected = new SerializedObject(new Object[] { after[0], after[1] });
                using var firstSnapshot = new SerializedObject(after[0]);
                using var secondSnapshot = new SerializedObject(after[1]);
                SerializedProperty radius = selected.FindProperty("m_Shape.sourceRadius");
                radius.floatValue = 0.25f;
                InfinityLightEditorTransactions.StageProperty(radius, new[] { firstSnapshot, secondSnapshot });
                var invalid = after[1].shape;
                invalid.kind = (ELightKind)999;
                typeof(InfinityLightInspectorSnapshot).GetField("m_Shape", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(after[1], invalid);

                Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                    InfinityLightEditorTransactions.ApplyShape(new[] { first, second }, before, after));

                Assert.IsNull(first.GetComponent<InfinityAdditionalLightData>());
                Assert.IsNull(second.GetComponent<InfinityAdditionalLightData>());
                Assert.IsFalse(InfinityAdditionalLightData.s_Registry.Contains(InfinityTech.Core.UnityEntityId.ToUInt64(first)));
                Assert.IsFalse(InfinityAdditionalLightData.s_Registry.Contains(InfinityTech.Core.UnityEntityId.ToUInt64(second)));
                Assert.AreEqual(firstNative, EditorJsonUtility.ToJson(first));
                Assert.AreEqual(secondNative, EditorJsonUtility.ToJson(second));
            }
        }

        [Test, Category("LightInspector")]
        public void NativeDisk_BakeModeEditCreatesCarrierOnlyForTheEdit()
        {
            using (var fixture = new LightInspectorFixture())
            {
                Light light = fixture.NewLight(LightType.Disc);
                light.lightmapBakeType = LightmapBakeType.Baked;
                light.areaSize = new Vector2(0.75f, 1);
                string native = EditorJsonUtility.ToJson(light);

                InfinityLightEditorTransactions.ApplyBakeMode(new[] { light }, LightmapBakeType.Realtime);

                var data = light.GetComponent<InfinityAdditionalLightData>();
                Assert.IsNotNull(data);
                Assert.AreEqual(EResolvedLightShape.Disk, LightShapeUtility.Resolve(data.shape));
                Assert.AreEqual(0, data.maxDrawDistance);
                Assert.AreEqual(LightType.Rectangle, light.type);
                Assert.AreEqual(LightmapBakeType.Realtime, light.lightmapBakeType);
                Undo.PerformUndo();
                Assert.IsNull(light.GetComponent<InfinityAdditionalLightData>());
                Assert.AreEqual(native, EditorJsonUtility.ToJson(light));
                Undo.PerformRedo();
                data = light.GetComponent<InfinityAdditionalLightData>();
                Assert.IsNotNull(data);
                Assert.AreEqual(EResolvedLightShape.Disk, LightShapeUtility.Resolve(data.shape));
                Assert.AreEqual(0, data.maxDrawDistance);
                Assert.AreEqual(LightType.Rectangle, light.type);
                Assert.AreEqual(LightmapBakeType.Realtime, light.lightmapBakeType);
            }
        }

        [Category("LightInspector")]
        [TestCase(false)]
        [TestCase(true)]
        public void SceneRangeEdit_DoesNotCreateOrAuthorAdditionalData(bool withAdditional)
        {
            using (var fixture = new LightInspectorFixture())
            {
                Light light = fixture.NewLight(LightType.Point);
                InfinityAdditionalLightData data = null;
                if (withAdditional)
                {
                    data = light.gameObject.AddComponent<InfinityAdditionalLightData>();
                    SetUnauthored(data);
                    data.shadowFilter = EShadowFilter.PCSS;
                }
                LightSourceShape shape = data ? data.shape : LightShapeUtility.FromUnityLight(light);
                float originalRange = light.range;

                InfinityLightEditorTransactions.ApplySceneEdit(light, shape, shape, originalRange + 3, light.spotAngle);

                Assert.AreSame(data, light.GetComponent<InfinityAdditionalLightData>());
                if (data) Assert.IsFalse(data.shapeAuthored);
                Assert.AreEqual(originalRange + 3, light.range);
                Undo.PerformUndo();
                Assert.AreSame(data, light.GetComponent<InfinityAdditionalLightData>());
                if (data) Assert.IsFalse(data.shapeAuthored);
                Assert.AreEqual(originalRange, light.range);
                Undo.PerformRedo();
                Assert.AreSame(data, light.GetComponent<InfinityAdditionalLightData>());
                if (data)
                {
                    Assert.IsFalse(data.shapeAuthored);
                    Assert.AreEqual(EShadowFilter.PCSS, data.shadowFilter);
                }
                Assert.AreEqual(originalRange + 3, light.range);
            }
        }

        [Test, Category("LightInspector")]
        public void ShapeEdit_PrefabInstanceTracksBothOverridesAndUndo()
        {
            using (var fixture = new LightInspectorFixture())
            {
                Light light = fixture.InstantiatePrefab();
                var data = light.GetComponent<InfinityAdditionalLightData>();
                Assert.IsNotNull(data);
                Assert.AreEqual(0.1f, data.shape.sourceRadius, 1e-6f);
                Assert.AreEqual(0.1f, light.shapeRadius, 1e-6f);
                var before = new[] { fixture.Snapshot(light) };
                var after = new[] { fixture.Snapshot(light) };
                using (var candidate = new SerializedObject(after[0]))
                {
                    candidate.FindProperty("m_Shape.sourceRadius").floatValue = 0.5f;
                    candidate.ApplyModifiedPropertiesWithoutUndo();
                }

                InfinityLightEditorTransactions.ApplyShape(new[] { light }, before, after);

                using (var extension = new SerializedObject(data))
                using (var native = new SerializedObject(light))
                {
                    Assert.IsTrue(extension.FindProperty("m_Shape.sourceRadius").prefabOverride);
                    Assert.IsTrue(native.FindProperty("m_ShapeRadius").prefabOverride);
                }
                Assert.AreEqual(0.5f, data.shape.sourceRadius, 1e-6f);
                Assert.AreEqual(0.5f, light.shapeRadius, 1e-6f);
                Undo.PerformUndo();
                Assert.AreEqual(0.1f, data.shape.sourceRadius, 1e-6f);
                Assert.AreEqual(0.1f, light.shapeRadius, 1e-6f);
                Undo.PerformRedo();
                Assert.AreEqual(0.5f, data.shape.sourceRadius, 1e-6f);
                Assert.AreEqual(0.5f, light.shapeRadius, 1e-6f);
            }
        }

        static void SetUnauthored(InfinityAdditionalLightData data)
        {
            var serialized = new SerializedObject(data);
            serialized.FindProperty("m_ShapeAuthored").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        sealed class LightInspectorFixture : System.IDisposable
        {
            readonly UnityEngine.SceneManagement.Scene m_OriginalScene;
            readonly bool m_OriginalDirty;
            readonly List<GameObject> m_GameObjects = new List<GameObject>();
            readonly List<InfinityLightInspectorSnapshot> m_Snapshots = new List<InfinityLightInspectorSnapshot>();

            internal UnityEngine.SceneManagement.Scene scene { get; }

            internal LightInspectorFixture()
            {
                m_OriginalScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                m_OriginalDirty = m_OriginalScene.isDirty;
                scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
                var sentinel = EditorUtility.CreateGameObjectWithHideFlags("LightInspectorUndoSentinel", HideFlags.DontSave);
                m_GameObjects.Add(sentinel);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(sentinel, scene);
                Undo.IncrementCurrentGroup();
                Undo.RecordObject(sentinel.transform, "Light inspector fixture sentinel");
                sentinel.transform.position = Vector3.one;
                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
            }

            internal Light NewLight(LightType type)
            {
                var go = EditorUtility.CreateGameObjectWithHideFlags("LightInspectorTest", HideFlags.DontSave, typeof(Light));
                m_GameObjects.Add(go);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
                Light light = go.GetComponent<Light>();
                light.type = type;
                return light;
            }

            internal Light InstantiatePrefab()
            {
                const string path = "Packages/com.infinity.render-pipeline/Tests/Editor/Fixtures/LightInspector.prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                m_GameObjects.Add(go);
                return go.GetComponent<Light>();
            }

            internal InfinityLightInspectorSnapshot Snapshot(Light light)
            {
                var snapshot = ScriptableObject.CreateInstance<InfinityLightInspectorSnapshot>();
                snapshot.hideFlags = HideFlags.DontSave;
                snapshot.Read(light);
                m_Snapshots.Add(snapshot);
                return snapshot;
            }

            public void Dispose()
            {
                Undo.FlushUndoRecordObjects();
                foreach (GameObject go in m_GameObjects)
                {
                    if (!go) continue;
                    var data = go.GetComponent<InfinityAdditionalLightData>();
                    if (data) Undo.ClearUndo(data);
                    var light = go.GetComponent<Light>();
                    if (light) Undo.ClearUndo(light);
                    Undo.ClearUndo(go.transform);
                    Undo.ClearUndo(go);
                    Object.DestroyImmediate(go);
                }
                foreach (var snapshot in m_Snapshots)
                    if (snapshot) Object.DestroyImmediate(snapshot);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
                Assert.AreEqual(m_OriginalDirty, m_OriginalScene.isDirty, "The test must not edit the framework scene.");
            }
        }

        [Test]
        public void InvalidRect_IsRejected()
        {
            LightSourceShape shape = LightSourceShape.DefaultLocal(ELocalLightEmitter.Rectangle, ELocalLightDistribution.Hemisphere);
            shape.sourceWidth = 0;
            Assert.IsFalse(LightShapeUtility.IsValid(shape));
            Assert.IsTrue(LightShapeUtility.IsValid(LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional)));
        }

        [Test]
        public void ShapeCombinations_CapabilitiesAndNormalization_AreConsistent()
        {
            Assert.IsFalse(LightShapeUtility.IsValidCombination(ELightKind.Local, ELocalLightEmitter.Round, ELocalLightDistribution.Hemisphere));
            Assert.IsFalse(LightShapeUtility.IsValidCombination(ELightKind.Local, ELocalLightEmitter.Rectangle, ELocalLightDistribution.Cone));
            Assert.IsFalse(LightShapeUtility.IsValidCombination(ELightKind.Local, ELocalLightEmitter.Disk, ELocalLightDistribution.Pyramid));
            Assert.IsTrue(LightShapeUtility.IsValidCombination(ELightKind.Local, ELocalLightEmitter.Disk, ELocalLightDistribution.Cone));

            var invalidShape = new LightSourceShape
            {
                kind = ELightKind.Local,
                emitter = ELocalLightEmitter.Rectangle,
                distribution = ELocalLightDistribution.Omnidirectional,
                sourceWidth = 1f,
                sourceHeight = 1f,
            };
            Assert.IsFalse(LightShapeUtility.IsValid(invalidShape));
            Assert.IsFalse(LightShapeUtility.GetCapabilities(invalidShape).validCombination);

            var diskCone = LightSourceShape.DefaultLocal(ELocalLightEmitter.Disk, ELocalLightDistribution.Cone);
            LightCapabilities diskConeCaps = LightShapeUtility.GetCapabilities(diskCone);
            Assert.IsTrue(diskConeCaps.validCombination);
            Assert.IsTrue(diskConeCaps.realtimeShading);
            Assert.IsTrue(diskConeCaps.realtimeShadow);
            Assert.IsFalse(diskConeCaps.baking);
            Assert.IsFalse(string.IsNullOrEmpty(diskConeCaps.bakeDisabledReason));
            Assert.IsTrue(diskConeCaps.supportsPlanarCookie);
            Assert.IsTrue(diskConeCaps.supports2DIes);

            var tube = LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional);
            tube.sourceLength = 1.5f;
            LightCapabilities tubeCaps = LightShapeUtility.GetCapabilities(tube);
            Assert.IsFalse(tubeCaps.baking);
            Assert.IsFalse(string.IsNullOrEmpty(tubeCaps.bakeDisabledReason));

            Assert.AreEqual((ELocalLightEmitter.Rectangle, ELocalLightDistribution.Hemisphere),
                LightShapeUtility.NormalizeCombination(ELocalLightEmitter.Rectangle, ELocalLightDistribution.Cone, keepEmitter: true));
            Assert.AreEqual((ELocalLightEmitter.Disk, ELocalLightDistribution.Cone),
                LightShapeUtility.NormalizeCombination(ELocalLightEmitter.Disk, ELocalLightDistribution.Cone, keepEmitter: false));
        }

        [Test]
        public void DiskCone_PacksShiftedApexAndSetsNoBakeWithWarning()
        {
            var root = new GameObject("Rig");
            var child = new GameObject("DiskConeSpot");
            child.transform.SetParent(root.transform, false);
            Light light = child.AddComponent<Light>();
            try
            {
                light.lightmapBakeType = LightmapBakeType.Mixed;
                light.spotAngle = 90f;
                light.innerSpotAngle = 30f;
                light.range = 12f;
                var data = child.AddComponent<InfinityAdditionalLightData>();
                var diskCone = LightSourceShape.DefaultLocal(ELocalLightEmitter.Disk, ELocalLightDistribution.Cone);
                diskCone.sourceRadius = 0.75f;
                data.SetShape(diskCone);

                Assert.AreEqual(LightType.Rectangle, light.type);
                Assert.IsTrue(LightRecordPack.TryPack(VisibleLightTestUtility.FromLight(light), light, data, data.shape, 0, Vector3.zero, out LightRecord record));
                Assert.AreEqual(LightRecordFlags.Planar | LightRecordFlags.SpotCone, record.flags & (LightRecordFlags.Planar | LightRecordFlags.SpotCone));
                Assert.AreEqual(0.75f, record.shape.y, 1e-4f, "90-degree cone from a 0.75m disk has apex 0.75m behind the disk.");
                Assert.AreEqual(Mathf.Cos(45f * Mathf.Deg2Rad), record.forward.w, 1e-5f);

                UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"Rig/DiskConeSpot.*NoBake"));
                var spotGo = new GameObject("BakeableSpot");
                try
                {
                    Light spotLight = spotGo.AddComponent<Light>();
                    spotLight.type = LightType.Spot;
                    spotLight.lightmapBakeType = LightmapBakeType.Baked;
                    spotLight.color = Color.red;
                    spotLight.intensity = 42f;
                    using var output = new Unity.Collections.NativeArray<UnityEngine.Experimental.GlobalIllumination.LightDataGI>(2, Unity.Collections.Allocator.Temp);
                    InfinityLightmapperDelegate.Delegate(new[] { spotLight, light }, output);
                    Assert.AreEqual(UnityEngine.Experimental.GlobalIllumination.LightMode.Baked, output[0].mode);
                    Assert.AreEqual(UnityEngine.Experimental.GlobalIllumination.LightMode.Unknown, output[1].mode);
                    Assert.AreEqual(0f, output[1].range, "InitNoBake slot must not leak preceding light fields.");
                }
                finally
                {
                    Object.DestroyImmediate(spotGo);
                }
            }
            finally
            {
                Undo.FlushUndoRecordObjects();
                Undo.ClearAll();
                Object.DestroyImmediate(root);
            }
        }

        [Test, Category("LightInspector")]
        public void ShapeEdit_MixedOrBakedToRealtimeOnly_PromptsAndAppliesAtomically()
        {
            using (var fixture = new LightInspectorFixture())
            {
                Light light = fixture.NewLight(LightType.Point);
                light.lightmapBakeType = LightmapBakeType.Mixed;
                var before = new[] { fixture.Snapshot(light) };
                var after = new[] { fixture.Snapshot(light) };
                using (var serializedAfter = new SerializedObject(after[0]))
                {
                    serializedAfter.FindProperty("m_Shape.sourceLength").floatValue = 1.5f;
                    serializedAfter.ApplyModifiedPropertiesWithoutUndo();
                }

                bool declined = InfinityLightEditorTransactions.ApplyShape(new[] { light }, before, after, _ => false);
                Assert.IsFalse(declined);
                Assert.IsNull(light.GetComponent<InfinityAdditionalLightData>());
                Assert.AreEqual(LightmapBakeType.Mixed, light.lightmapBakeType);
                Assert.AreEqual(LightType.Point, light.type);

                bool accepted = InfinityLightEditorTransactions.ApplyShape(new[] { light }, before, after, _ => true);
                Assert.IsTrue(accepted);
                Assert.AreEqual(LightmapBakeType.Realtime, light.lightmapBakeType);
                Assert.AreEqual(LightType.Tube, light.type);
                Assert.AreEqual(1.5f, light.GetComponent<InfinityAdditionalLightData>().shape.sourceLength, 1e-5f);

                Undo.PerformUndo();
                Assert.IsNull(light.GetComponent<InfinityAdditionalLightData>());
                Assert.AreEqual(LightmapBakeType.Mixed, light.lightmapBakeType);
                Assert.AreEqual(LightType.Point, light.type);
            }
        }

        [Test]
        public void PyramidTangents_SpotAngleSpansTheLongerSide()
        {
            Vector2 wide = LightShapeUtility.PyramidTangents(90, 2);
            Assert.AreEqual(1.0f, wide.x, 1e-4f);
            Assert.AreEqual(0.5f, wide.y, 1e-4f);
            Vector2 tall = LightShapeUtility.PyramidTangents(90, 0.5f);
            Assert.AreEqual(0.5f, tall.x, 1e-4f);
            Assert.AreEqual(1.0f, tall.y, 1e-4f);
        }

        [Test, Category("LightInspector")]
        public void DisplayUnitSwitch_PreservesCanonicalIntensityAndSupportsUndo()
        {
            using (var fixture = new LightInspectorFixture())
            {
                Light sphere = fixture.NewLight(LightType.Point);
                var sphereShape = LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional);
                sphereShape.sourceRadius = 0.5f;
                sphere.gameObject.AddComponent<InfinityAdditionalLightData>().SetShape(sphereShape);
                sphere.intensity = 120f;
                sphere.lightUnit = LightUnit.Candela;

                Light rect = fixture.NewLight(LightType.Rectangle);
                var rectShape = LightSourceShape.DefaultLocal(ELocalLightEmitter.Rectangle, ELocalLightDistribution.Hemisphere);
                rectShape.sourceWidth = 2f;
                rectShape.sourceHeight = 1f;
                rect.gameObject.AddComponent<InfinityAdditionalLightData>().SetShape(rectShape);
                rect.intensity = 50f;
                rect.lightUnit = LightUnit.Nits;

                InfinityLightEditorTransactions.ApplyDisplayUnit(
                    new[] { sphere, rect },
                    new[] { sphereShape, rectShape },
                    LightUnit.Lumen);

                Assert.AreEqual(LightUnit.Lumen, sphere.lightUnit);
                Assert.AreEqual(LightUnit.Lumen, rect.lightUnit);
                Assert.AreEqual(120f, sphere.intensity, 1e-6f);
                Assert.AreEqual(50f, rect.intensity, 1e-6f);

                Undo.PerformUndo();
                Assert.AreEqual(LightUnit.Candela, sphere.lightUnit);
                Assert.AreEqual(LightUnit.Nits, rect.lightUnit);
                Assert.AreEqual(120f, sphere.intensity, 1e-6f);
                Assert.AreEqual(50f, rect.intensity, 1e-6f);

                Undo.PerformRedo();
                Assert.AreEqual(LightUnit.Lumen, sphere.lightUnit);
                Assert.AreEqual(LightUnit.Lumen, rect.lightUnit);
            }
        }

        [Test, Category("LightInspector")]
        public void DisplayIntensityEdit_ConvertsPhotometricallyAcrossUnitsAndSupportsUndo()
        {
            using (var fixture = new LightInspectorFixture())
            {
                Light rect = fixture.NewLight(LightType.Rectangle);
                var rectShape = LightSourceShape.DefaultLocal(ELocalLightEmitter.Rectangle, ELocalLightDistribution.Hemisphere);
                rectShape.sourceWidth = 2f;
                rectShape.sourceHeight = 1.5f;
                rect.gameObject.AddComponent<InfinityAdditionalLightData>().SetShape(rectShape);
                rect.intensity = 10f;
                rect.lightUnit = LightUnit.Lumen;

                Light sphere = fixture.NewLight(LightType.Point);
                var sphereShape = LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional);
                sphereShape.sourceRadius = 0.4f;
                sphere.gameObject.AddComponent<InfinityAdditionalLightData>().SetShape(sphereShape);
                sphere.intensity = 10f;
                sphere.lightUnit = LightUnit.Lumen;

                InfinityLightEditorTransactions.ApplyDisplayIntensity(
                    new[] { rect, sphere },
                    new[] { rectShape, sphereShape },
                    100f * Mathf.PI,
                    LightUnit.Lumen);

                float expectedRectNits = (100f * Mathf.PI) / (3f * Mathf.PI);
                float expectedSphereCandela = (100f * Mathf.PI) / (4f * Mathf.PI);
                Assert.AreEqual(expectedRectNits, rect.intensity, 1e-3f);
                Assert.AreEqual(expectedSphereCandela, sphere.intensity, 1e-3f);

                Undo.PerformUndo();
                Assert.AreEqual(10f, rect.intensity, 1e-6f);
                Assert.AreEqual(10f, sphere.intensity, 1e-6f);

                Undo.PerformRedo();
                Assert.AreEqual(expectedRectNits, rect.intensity, 1e-3f);
                Assert.AreEqual(expectedSphereCandela, sphere.intensity, 1e-3f);
            }
        }

        [Test, Category("LightInspector")]
        public void ShapeAndSpotAngleEdits_RespectIntensityEditModeAndUndo()
        {
            using (var fixture = new LightInspectorFixture())
            {
                Light cone = fixture.NewLight(LightType.Spot);
                var coneData = cone.gameObject.AddComponent<InfinityAdditionalLightData>();
                coneData.intensityEditMode = ELightIntensityEditMode.PreserveFlux;
                cone.intensity = 100f;
                cone.spotAngle = 60f;
                cone.innerSpotAngle = 20f;
                float initialFlux = LightIntensityUtility.ToDisplayIntensity(cone, coneData.shape, LightUnit.Lumen);

                var snapshots = new[] { fixture.Snapshot(cone) };
                InfinityLightEditorTransactions.ApplySpotAngles(new[] { cone }, snapshots, 90f, 30f);

                float editedFlux = LightIntensityUtility.ToDisplayIntensity(cone, coneData.shape, LightUnit.Lumen);
                Assert.AreEqual(90f, cone.spotAngle, 1e-5f);
                Assert.AreEqual(30f, cone.innerSpotAngle, 1e-5f);
                Assert.AreEqual(initialFlux, editedFlux, initialFlux * 1e-4f);

                Undo.PerformUndo();
                Assert.AreEqual(60f, cone.spotAngle, 1e-5f);
                Assert.AreEqual(20f, cone.innerSpotAngle, 1e-5f);
                Assert.AreEqual(100f, cone.intensity, 1e-4f);

                // Scene edit radius change in PreserveFlux adjusts canonical candela intensity and reverts cleanly.
                Light sphere = fixture.NewLight(LightType.Point);
                var sphereData = sphere.gameObject.AddComponent<InfinityAdditionalLightData>();
                var s1 = LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional);
                s1.sourceRadius = 0.2f;
                sphereData.SetShape(s1);
                sphereData.intensityEditMode = ELightIntensityEditMode.PhysicallyBased;
                sphere.intensity = 40f;
                var s2 = s1;
                s2.sourceRadius = 0.4f;

                Assert.IsTrue(InfinityLightEditorTransactions.ApplySceneEdit(sphere, s1, s2, sphere.range, sphere.spotAngle));
                Assert.AreEqual(160f, sphere.intensity, 1e-3f);

                Undo.PerformUndo();
                Assert.AreEqual(0.2f, sphereData.shape.sourceRadius, 1e-5f);
                Assert.AreEqual(40f, sphere.intensity, 1e-4f);
            }
        }

        [Test, Category("LightInspector")]
        public void SceneEdit_MixedToRealtimeOnly_PromptsAndSwitchesAtomically()
        {
            using (var fixture = new LightInspectorFixture())
            {
                Light light = fixture.NewLight(LightType.Point);
                light.lightmapBakeType = LightmapBakeType.Baked;
                LightSourceShape prev = LightShapeUtility.FromUnityLight(light);
                LightSourceShape next = prev;
                next.sourceLength = 2.0f;

                bool declined = InfinityLightEditorTransactions.ApplySceneEdit(light, prev, next, light.range, light.spotAngle, _ => false);
                Assert.IsFalse(declined);
                Assert.IsNull(light.GetComponent<InfinityAdditionalLightData>());
                Assert.AreEqual(LightmapBakeType.Baked, light.lightmapBakeType);

                bool accepted = InfinityLightEditorTransactions.ApplySceneEdit(light, prev, next, light.range + 2f, light.spotAngle, _ => true);
                Assert.IsTrue(accepted);
                Assert.AreEqual(LightmapBakeType.Realtime, light.lightmapBakeType);
                Assert.AreEqual(LightType.Tube, light.type);
                Assert.AreEqual(2.0f, light.GetComponent<InfinityAdditionalLightData>().shape.sourceLength, 1e-5f);

                Undo.PerformUndo();
                Assert.IsNull(light.GetComponent<InfinityAdditionalLightData>());
                Assert.AreEqual(LightmapBakeType.Baked, light.lightmapBakeType);
            }
        }

        [Test, Category("LightInspector")]
        public void AdditionalLightDataEditor_LeavesExpandedBodyEmpty()
        {
            using (var fixture = new LightInspectorFixture())
            {
                Light light = fixture.NewLight(LightType.Point);
                InfinityAdditionalLightData data = light.gameObject.AddComponent<InfinityAdditionalLightData>();
                var editor = UnityEditor.Editor.CreateEditor(data);
                try
                {
                    Assert.AreEqual("InfinityAdditionalLightDataEditor", editor.GetType().Name);
                    Assert.DoesNotThrow(() => editor.OnInspectorGUI());
                }
                finally
                {
                    Object.DestroyImmediate(editor);
                }
            }
        }

        [Test, Category("LightInspector")]
        public void InfinityLightEditor_RendersAllShapesAndMultiSelectionWithoutLazyCreation()
        {
            using (var fixture = new LightInspectorFixture())
            {
                Light plainSpot = fixture.NewLight(LightType.Spot);
                Light diskCone = fixture.NewLight(LightType.Rectangle);
                var diskConeData = diskCone.gameObject.AddComponent<InfinityAdditionalLightData>();
                var diskConeShape = LightSourceShape.DefaultLocal(ELocalLightEmitter.Disk, ELocalLightDistribution.Cone);
                diskConeShape.sourceRadius = 0.4f;
                diskConeData.SetShape(diskConeShape);

                Light boxLight = fixture.NewLight(LightType.Box);
                var boxData = boxLight.gameObject.AddComponent<InfinityAdditionalLightData>();
                var boxShape = LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.ParallelBox);
                boxShape.boxSize = new Vector2(1.5f, 0.8f);
                boxData.SetShape(boxShape);

                Light tubeLight = fixture.NewLight(LightType.Tube);
                var tubeData = tubeLight.gameObject.AddComponent<InfinityAdditionalLightData>();
                var tubeShape = LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional);
                tubeShape.sourceRadius = 0.1f;
                tubeShape.sourceLength = 1.2f;
                tubeData.SetShape(tubeShape);

                Light directional = fixture.NewLight(LightType.Directional);

                Object[] selections = { plainSpot, diskCone, boxLight, tubeLight, directional };
                foreach (Object single in selections)
                    DrawEditorHostPass(new[] { single }, 420f);
                foreach (Object single in selections)
                    DrawEditorHostPass(new[] { single }, 260f);

                DrawEditorHostPass(new Object[] { plainSpot, diskCone }, 420f);
                DrawEditorHostPass(new Object[] { tubeLight, boxLight }, 260f);

                Assert.IsNull(plainSpot.GetComponent<InfinityAdditionalLightData>(),
                    "Inspecting a plain Unity Light must never lazily add InfinityAdditionalLightData.");
            }
        }

        [Test, Category("LightInspector")]
        public void DragCoalescing_MergesSameControlAndSplitsAfterReleaseOrInterveningEdit()
        {
            using (var fixture = new LightInspectorFixture())
            {
                Light light = fixture.NewLight(LightType.Spot);
                light.intensity = 100f;
                light.spotAngle = 40f;
                light.innerSpotAngle = 20f;
                var before = new[] { fixture.Snapshot(light) };
                var shapes = new[] { before[0].shape };

                int previousHotControl = GUIUtility.hotControl;
                try
                {
                    Undo.IncrementCurrentGroup();
                    // First drag on control 4242: two consecutive steps must coalesce into one Undo group.
                    GUIUtility.hotControl = 4242;
                    InfinityLightEditorTransactions.SyncDragState();
                    InfinityLightEditorTransactions.ApplyDisplayIntensity(new[] { light }, shapes, 150f, UnityEngine.Rendering.LightUnit.Candela, mergeDrag: true);
                    int dragGroup = Undo.GetCurrentGroup();
                    InfinityLightEditorTransactions.ApplyDisplayIntensity(new[] { light }, shapes, 220f, UnityEngine.Rendering.LightUnit.Candela, mergeDrag: true);
                    Assert.AreEqual(dragGroup, Undo.GetCurrentGroup(), "Consecutive drag steps on the same hotControl must share one Undo group.");
                    Assert.AreEqual(220f, light.intensity, 1e-4f);

                    // Releasing the mouse clears hotControl; next drag on the exact same control ID must get a new group.
                    GUIUtility.hotControl = 0;
                    InfinityLightEditorTransactions.SyncDragState();
                    GUIUtility.hotControl = 4242;
                    InfinityLightEditorTransactions.ApplyDisplayIntensity(new[] { light }, shapes, 310f, UnityEngine.Rendering.LightUnit.Candela, mergeDrag: true);
                    int secondDragGroup = Undo.GetCurrentGroup();
                    Assert.Greater(secondDragGroup, dragGroup, "A second drag on the same control ID after release must start a new Undo group.");

                    // An intervening Undo group (e.g., native Inspector edit) also splits subsequent drag updates.
                    Undo.IncrementCurrentGroup();
                    InfinityLightEditorTransactions.ApplyDisplayIntensity(new[] { light }, shapes, 450f, UnityEngine.Rendering.LightUnit.Candela, mergeDrag: true);
                    Assert.Greater(Undo.GetCurrentGroup(), secondDragGroup, "An intervening Undo group must split drag coalescing.");

                    // End the active drag (hotControl == 0) before invoking Unity's Undo stack.
                    GUIUtility.hotControl = 0;
                    InfinityLightEditorTransactions.SyncDragState();
                    Undo.PerformUndo();
                    Assert.AreEqual(310f, light.intensity, 1e-4f);
                    Undo.PerformUndo();
                    Assert.AreEqual(220f, light.intensity, 1e-4f);
                    Undo.PerformUndo();
                    Assert.AreEqual(100f, light.intensity, 1e-4f);
                }
                finally
                {
                    GUIUtility.hotControl = previousHotControl;
                    InfinityLightEditorTransactions.SyncDragState();
                }
            }
        }

        static void DrawEditorHostPass(Object[] targets, float width)
        {
            var host = ScriptableObject.CreateInstance<InspectorLayoutTestWindow>();
            var editor = UnityEditor.Editor.CreateEditor(targets);
            try
            {
                Assert.IsInstanceOf<InfinityLightEditor>(editor);
                host.editor = editor;
                host.minSize = new Vector2(width, 400f);
                host.maxSize = new Vector2(width, 400f);
                host.position = new Rect(120f, 120f, width, 400f);
                host.ShowUtility();
                host.SendEvent(new Event { type = EventType.Layout });
                host.SendEvent(new Event { type = EventType.Repaint });
                Assert.Greater(host.drawCount, 0, $"Inspector OnGUI did not execute at width {width}");
                Assert.IsNull(host.caughtException, $"Inspector GUI threw at width {width}: {host.caughtException}");
            }
            finally
            {
                if (editor) Object.DestroyImmediate(editor);
                if (host) host.Close();
            }
        }

        sealed class InspectorLayoutTestWindow : EditorWindow
        {
            [System.NonSerialized] public UnityEditor.Editor editor;
            [System.NonSerialized] public System.Exception caughtException;
            [System.NonSerialized] public int drawCount;

            void OnGUI()
            {
                drawCount++;
                try
                {
                    editor?.OnInspectorGUI();
                }
                catch (System.Exception ex)
                {
                    if (!(ex is ExitGUIException))
                        caughtException = ex;
                }
            }
        }
    }
}
