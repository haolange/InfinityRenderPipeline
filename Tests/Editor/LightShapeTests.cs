using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using InfinityTech.Component;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class LightShapeTests
    {
        static Light NewLight(LightType type, out GameObject go)
        {
            go = new GameObject("LightShapeTest");
            Light light = go.AddComponent<Light>();
            light.type = type;
            return light;
        }

        [TestCase(EInfinityLightType.Point, 0.0f, 0.0f, LightType.Point)]
        [TestCase(EInfinityLightType.Point, 0.2f, 0.0f, LightType.Point)]
        [TestCase(EInfinityLightType.Point, 0.0f, 1.5f, LightType.Tube)]
        [TestCase(EInfinityLightType.Point, 0.1f, 1.5f, LightType.Tube)]
        [TestCase(EInfinityLightType.Rect, 0.0f, 0.0f, LightType.Rectangle)]
        [TestCase(EInfinityLightType.Disk, 0.5f, 0.0f, LightType.Rectangle)]
        [TestCase(EInfinityLightType.Directional, 0.0f, 0.0f, LightType.Directional)]
        [Category("Unverified_0_5")]
        public void ShapeParameters_DeriveTheNativeType(EInfinityLightType type, float radius, float length, LightType expected)
        {
            FLightShape shape = FLightShape.Default(type);
            shape.sourceRadius = radius;
            shape.sourceLength = length;
            Assert.AreEqual(expected, LightShapeUtility.NativeType(shape));
            Assert.AreEqual(length <= 0, LightShapeUtility.IsBakeable(shape), "Tubes and lines are realtime only.");
        }

        [TestCase(ESpotShape.Cone, LightType.Spot)]
        [TestCase(ESpotShape.Pyramid, LightType.Pyramid)]
        [TestCase(ESpotShape.Box, LightType.Box)]
        [Category("Unverified_0_5")]
        public void SpotShapes_DeriveTheNativeType(ESpotShape spotShape, LightType expected)
        {
            FLightShape shape = FLightShape.Default(EInfinityLightType.Spot);
            shape.spotShape = spotShape;
            Assert.AreEqual(expected, LightShapeUtility.NativeType(shape));
        }

        [Test]
        [Category("Unverified_0_5")]
        public void ApplyThenRead_RoundTripsEveryShape()
        {
            Light light = NewLight(LightType.Point, out GameObject go);
            try
            {
                var shapes = new[]
                {
                    new FLightShape { type = EInfinityLightType.Point, sourceRadius = 0.15f, sourceLength = 2.0f, sourceWidth = 1, sourceHeight = 1, pyramidAspect = 1, boxSize = Vector2.one },
                    new FLightShape { type = EInfinityLightType.Spot, spotShape = ESpotShape.Pyramid, sourceRadius = 0.05f, pyramidAspect = 2.0f, sourceWidth = 1, sourceHeight = 1, boxSize = Vector2.one },
                    new FLightShape { type = EInfinityLightType.Spot, spotShape = ESpotShape.Box, boxSize = new Vector2(3, 2), sourceWidth = 1, sourceHeight = 1, pyramidAspect = 1 },
                    new FLightShape { type = EInfinityLightType.Rect, sourceWidth = 2.0f, sourceHeight = 0.5f, pyramidAspect = 1, boxSize = Vector2.one },
                    new FLightShape { type = EInfinityLightType.Disk, sourceRadius = 0.75f, sourceWidth = 1, sourceHeight = 1, pyramidAspect = 1, boxSize = Vector2.one },
                    new FLightShape { type = EInfinityLightType.Directional, sourceAngle = 0.5357f, sourceWidth = 1, sourceHeight = 1, pyramidAspect = 1, boxSize = Vector2.one },
                };
                foreach (FLightShape shape in shapes)
                {
                    LightShapeUtility.ApplyToUnityLight(light, shape);
                    FLightShape read = LightShapeUtility.FromUnityLight(light);
                    if (shape.type == EInfinityLightType.Disk)
                    {
                        // The carrier does not store Disk identity; authored shape lives on Additional data.
                        Assert.AreEqual(EInfinityLightType.Rect, read.type);
                        Assert.AreEqual(Mathf.Sqrt(Mathf.PI) * shape.sourceRadius, read.sourceWidth, 1e-5f);
                        Assert.AreEqual(read.sourceWidth, read.sourceHeight, 1e-5f);
                        Assert.IsFalse(LightShapeUtility.ApplyToUnityLight(light, shape), "Second apply is a no-op.");
                        continue;
                    }
                    Assert.AreEqual(shape.type, read.type);
                    Assert.AreEqual(shape.sourceRadius, read.sourceRadius, 1e-5f, shape.type + " radius");
                    Assert.AreEqual(shape.sourceLength, read.sourceLength, 1e-5f, shape.type + " length");
                    if (shape.type == EInfinityLightType.Spot) Assert.AreEqual(shape.spotShape, read.spotShape);
                    if (shape.type == EInfinityLightType.Rect)
                    {
                        Assert.AreEqual(shape.sourceWidth, read.sourceWidth, 1e-5f);
                        Assert.AreEqual(shape.sourceHeight, read.sourceHeight, 1e-5f);
                    }
                    if (shape.spotShape == ESpotShape.Pyramid) Assert.AreEqual(shape.pyramidAspect, read.pyramidAspect, 1e-5f);
                    if (shape.spotShape == ESpotShape.Box) Assert.AreEqual(shape.boxSize, read.boxSize);
                    if (shape.type == EInfinityLightType.Directional) Assert.AreEqual(shape.sourceAngle, read.sourceAngle, 1e-5f);
                    Assert.IsFalse(LightShapeUtility.ApplyToUnityLight(light, shape), "A second apply changes nothing.");
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        [Category("Unverified_0_5")]
        public void AddingTheComponent_AdoptsTheNativeShape()
        {
            Light light = NewLight(LightType.Rectangle, out GameObject go);
            try
            {
                light.areaSize = new Vector2(3, 1);
                var data = go.AddComponent<InfinityAdditionalLightData>();
                FLightShape shape = data.shape;
                Assert.AreEqual(EInfinityLightType.Rect, shape.type);
                Assert.AreEqual(3.0f, shape.sourceWidth, 1e-5f);
                Assert.AreEqual(LightType.Rectangle, light.type, "Adding the component never changes the light.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [TestCase(LightmapBakeType.Realtime)]
        [TestCase(LightmapBakeType.Mixed)]
        [TestCase(LightmapBakeType.Baked)]
        [Category("Unverified_0_5")]
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
                var disk = FLightShape.Default(EInfinityLightType.Disk);
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
                data.SetShape(FLightShape.Default(EInfinityLightType.Rect));
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
        [Category("Unverified_0_5")]
        public void AuthoredDisk_KeepsDiscBakeShapeOnNativeCarrier(LightmapBakeType mode)
        {
            Light light = NewLight(LightType.Point, out GameObject go);
            try
            {
                var data = go.AddComponent<InfinityAdditionalLightData>();
                light.lightmapBakeType = mode;
                var disk = FLightShape.Default(EInfinityLightType.Disk);
                disk.sourceRadius = 0.85f;
                data.SetShape(disk);
                Assert.AreEqual(LightType.Rectangle, light.type);
                Assert.AreEqual(mode, light.lightmapBakeType);
                Assert.AreEqual(EInfinityLightType.Disk, FLightRecordPack.ResolveShape(light, data).type);
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

        [TestCase(LightmapBakeType.Realtime)]
        [TestCase(LightmapBakeType.Mixed)]
        [Category("Unverified_0_5")]
        public void LegacyDisk_ModeOnlyEditUsesCarrierInOneUndo(LightmapBakeType mode)
        {
            Light light = NewLight(LightType.Disc, out GameObject go);
            try
            {
                light.lightmapBakeType = LightmapBakeType.Baked;
                light.areaSize = new Vector2(0.85f, 1);
                var data = go.AddComponent<InfinityAdditionalLightData>();
                string native = EditorJsonUtility.ToJson(light);
                Assert.AreEqual(EInfinityLightType.Disk, data.shape.type);
                Assert.AreEqual(LightType.Disc, light.type, "Adoption must not migrate native fields.");
                Undo.IncrementCurrentGroup();
                var apply = typeof(InfinityTech.Component.Editor.InfinityLightEditor).GetMethod("ApplyBakeMode",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                apply.Invoke(null, new object[] { light, data, mode });
                Undo.FlushUndoRecordObjects();
                Assert.AreEqual(LightType.Rectangle, light.type);
                Assert.AreEqual(mode, light.lightmapBakeType);
                Undo.PerformUndo();
                Assert.AreEqual(native, EditorJsonUtility.ToJson(light));
                Undo.PerformRedo();
                Assert.AreEqual(LightType.Rectangle, light.type);
                Assert.AreEqual(mode, light.lightmapBakeType);
                Assert.AreEqual(EInfinityLightType.Disk, data.shape.type);
                Undo.ClearUndo(light);
                Undo.ClearUndo(data);
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

        [Test]
        [Category("Unverified_0_5")]
        public void InspectorAdoption_UndoRedoRestoresLegacyShapeWithoutChangingNativeLight()
        {
            var originalScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            bool originalDirty = originalScene.isDirty;
            var ownedScene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var go = EditorUtility.CreateGameObjectWithHideFlags("LightAdoptionTest", HideFlags.DontSave, typeof(Light));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, ownedScene);
            Light light = go.GetComponent<Light>();
            light.type = LightType.Rectangle;
            var marker = EditorUtility.CreateGameObjectWithHideFlags("LightAdoptionUndoSentinel", HideFlags.DontSave);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(marker, ownedScene);
            UnityEditor.Editor inspector = null;
            InfinityAdditionalLightData data = null;
            try
            {
                light.areaSize = new Vector2(3, 1);
                data = go.AddComponent<InfinityAdditionalLightData>();
                var serialized = new SerializedObject(data);
                serialized.FindProperty("m_ShapeAuthored").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                string before = EditorJsonUtility.ToJson(data);
                string native = EditorJsonUtility.ToJson(light);
                Undo.IncrementCurrentGroup();
                Undo.RecordObject(marker.transform, "Light adoption sentinel");
                marker.transform.position = Vector3.one;
                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
                inspector = UnityEditor.Editor.CreateEditor(light, typeof(InfinityTech.Component.Editor.InfinityLightEditor));
                Undo.FlushUndoRecordObjects();
                string adopted = EditorJsonUtility.ToJson(data);
                Assert.IsTrue(data.shapeAuthored);
                Assert.AreNotEqual(before, adopted);
                Assert.AreEqual(native, EditorJsonUtility.ToJson(light));
                Undo.PerformUndo();
                Assert.AreEqual(before, EditorJsonUtility.ToJson(data));
                Assert.AreEqual(Vector3.one, marker.transform.position, "Adoption must own the Undo entry.");
                Assert.AreEqual(native, EditorJsonUtility.ToJson(light));
                Undo.PerformRedo();
                Assert.AreEqual(adopted, EditorJsonUtility.ToJson(data));
                Assert.AreEqual(native, EditorJsonUtility.ToJson(light));
            }
            finally
            {
                if (inspector) Object.DestroyImmediate(inspector);
                if (data) Undo.ClearUndo(data);
                Undo.ClearUndo(light);
                Undo.ClearUndo(marker.transform);
                Object.DestroyImmediate(marker);
                Object.DestroyImmediate(go);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(ownedScene);
                Assert.AreEqual(originalDirty, originalScene.isDirty, "The test must not edit the framework scene.");
            }
        }

        [Test]
        [Category("Unverified_0_5")]
        public void InvalidRect_IsRejected()
        {
            FLightShape shape = FLightShape.Default(EInfinityLightType.Rect);
            shape.sourceWidth = 0;
            Assert.IsFalse(LightShapeUtility.IsValid(shape));
            Assert.IsTrue(LightShapeUtility.IsValid(FLightShape.Default(EInfinityLightType.Point)));
        }

        [Test]
        [Category("Unverified_0_5")]
        public void PyramidTangents_SpotAngleSpansTheLongerSide()
        {
            Vector2 wide = LightShapeUtility.PyramidTangents(90, 2);
            Assert.AreEqual(1.0f, wide.x, 1e-4f);
            Assert.AreEqual(0.5f, wide.y, 1e-4f);
            Vector2 tall = LightShapeUtility.PyramidTangents(90, 0.5f);
            Assert.AreEqual(0.5f, tall.x, 1e-4f);
            Assert.AreEqual(1.0f, tall.y, 1e-4f);
        }
    }
}
