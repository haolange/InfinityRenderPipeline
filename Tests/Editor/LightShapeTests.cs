using NUnit.Framework;
using UnityEngine;
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
        [TestCase(EInfinityLightType.Disk, 0.5f, 0.0f, LightType.Disc)]
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
