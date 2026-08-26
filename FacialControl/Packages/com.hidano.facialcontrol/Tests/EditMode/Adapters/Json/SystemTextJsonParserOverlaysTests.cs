using System;
using NUnit.Framework;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.Json
{
    [TestFixture]
    public class SystemTextJsonParserOverlaysTests
    {
        private SystemTextJsonParser _parser;

        [SetUp]
        public void SetUp()
        {
            _parser = new SystemTextJsonParser();
        }

        [Test]
        public void Parse_LegacyExpressionIdField_ThrowsFormatException()
        {
            var ex = Assert.Throws<FormatException>(() =>
                _parser.ParseProfile(LegacyExpressionIdOverlayJson));

            StringAssert.Contains("expressionId", ex.Message);
            StringAssert.Contains("defaultOverlays[0].expressionId", ex.Message);
        }

        [Test]
        public void Parse_GazeConfigsExpressionId_DoesNotThrow()
        {
            var dto = _parser.ParseProfileSnapshotV2(GazeConfigsExpressionIdJson);

            Assert.AreEqual(1, dto.gazeConfigs.Count);
            Assert.AreEqual("look_left", dto.gazeConfigs[0].expressionId);
            Assert.AreEqual(false, dto.gazeConfigs[0].useDistinctLeftRight);
        }

        [Test]
        public void Parse_SuppressWithSnapshot_ThrowsFormatException()
        {
            var ex = Assert.Throws<FormatException>(() =>
                _parser.ParseProfile(SuppressWithSnapshotOverlayJson));

            StringAssert.Contains("blink", ex.Message);
            StringAssert.Contains("suppress=true", ex.Message);
            StringAssert.Contains("snapshot", ex.Message);
        }

        [Test]
        public void RoundTrip_ThreeOverlayStates_PreservesEquivalentProfile()
        {
            var profile = BuildThreeStateProfile();

            var json = _parser.SerializeProfile(profile);
            StringAssert.Contains(@"""slots""", json);
            StringAssert.Contains(@"""suppress""", json);
            StringAssert.Contains(@"""snapshot""", json);
            Assert.IsFalse(json.Contains("expressionId"));

            var parsed = _parser.ParseProfile(json);

            AssertSlots(parsed, "blink", "blush", "sparkle", "default_blink");

            var anger = parsed.FindExpressionById("anger");
            Assert.IsTrue(anger.HasValue);
            Assert.AreEqual(3, anger.Value.Overlays.Length);

            var overlays = anger.Value.Overlays.Span;
            AssertDefaultFallback(overlays[0], "blink");
            AssertSuppress(overlays[1], "blush");
            AssertSnapshotOverride(overlays[2], "sparkle", "SparkleOverlay", "Face", 0.75f);

            Assert.AreEqual(1, parsed.DefaultOverlays.Length);
            AssertSnapshotOverride(
                parsed.DefaultOverlays.Span[0],
                "default_blink",
                "DefaultBlink",
                "Face",
                1f);
        }

        [Test]
        public void Parse_ProfileWithoutSlots_NormalizesSlotsToEmpty()
        {
            var dto = _parser.ParseProfileSnapshotV2(ProfileWithoutSlotsJson);
            Assert.IsNotNull(dto.slots);
            Assert.AreEqual(0, dto.slots.Count);

            var profile = _parser.ParseProfile(ProfileWithoutSlotsJson);
            Assert.AreEqual(0, profile.Slots.Length);
        }

        [Test]
        public void Parse_SampleProfileJson_RoundTripsEquivalentOverlaySchema()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);

            AssertSlots(profile, "blink");

            var smile = profile.FindExpressionById("smile");
            Assert.IsTrue(smile.HasValue);
            Assert.AreEqual(1, smile.Value.Overlays.Length);
            AssertSnapshotOverride(
                smile.Value.Overlays.Span[0],
                "blink",
                "Fcl_EYE_Close_L",
                "Face",
                1f);

            var smileClosedEye = profile.FindExpressionById("smile_closed_eye");
            Assert.IsTrue(smileClosedEye.HasValue);
            Assert.AreEqual(1, smileClosedEye.Value.Overlays.Length);
            AssertSuppress(smileClosedEye.Value.Overlays.Span[0], "blink");

            Assert.AreEqual(1, profile.DefaultOverlays.Length);
            AssertDefaultFallback(profile.DefaultOverlays.Span[0], "blink");

            var serialized = _parser.SerializeProfile(profile);
            var reparsed = _parser.ParseProfile(serialized);

            AssertSlots(reparsed, "blink");
            AssertDefaultFallback(reparsed.DefaultOverlays.Span[0], "blink");
            AssertSuppress(
                reparsed.FindExpressionById("smile_closed_eye").Value.Overlays.Span[0],
                "blink");
            AssertSnapshotOverride(
                reparsed.FindExpressionById("smile").Value.Overlays.Span[0],
                "blink",
                "Fcl_EYE_Close_L",
                "Face",
                1f);
        }

        private static FacialProfile BuildThreeStateProfile()
        {
            var layers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
            };
            var layerInputSources = new[]
            {
                new[] { new InputSourceDeclaration("input", 1f, null) },
            };
            var expressionOverlays = new[]
            {
                new OverlaySlotBinding("blink", suppress: false, snapshot: null),
                new OverlaySlotBinding("blush", suppress: true, snapshot: null),
                new OverlaySlotBinding(
                    "sparkle",
                    suppress: false,
                    snapshot: CreateSnapshot("sparkle-override", "SparkleOverlay", 0.75f)),
            };
            var expressions = new[]
            {
                new Expression(
                    "anger",
                    "Anger",
                    "emotion",
                    Expression.DefaultTransitionDuration,
                    default,
                    new[] { new BlendShapeMapping("Anger", 1f, "Face") },
                    expressionOverlays),
            };
            var defaultOverlays = new[]
            {
                new OverlaySlotBinding(
                    "default_blink",
                    suppress: false,
                    snapshot: CreateSnapshot("default-blink", "DefaultBlink", 1f)),
            };

            return new FacialProfile(
                SystemTextJsonParser.SchemaVersionV2,
                layers,
                expressions,
                rendererPaths: new[] { "Face" },
                layerInputSources: layerInputSources,
                defaultOverlays: defaultOverlays,
                slots: new[] { "blink", "blush", "sparkle", "default_blink" });
        }

        private static ExpressionSnapshot CreateSnapshot(string id, string blendShapeName, float value)
        {
            return new ExpressionSnapshot(
                id,
                transitionDuration: 0.08f,
                transitionCurvePreset: TransitionCurvePreset.Linear,
                blendShapes: new[]
                {
                    new BlendShapeSnapshot("Face", blendShapeName, value),
                },
                bones: null,
                rendererPaths: new[] { "Face" });
        }

        private static void AssertSlots(FacialProfile profile, params string[] expected)
        {
            Assert.AreEqual(expected.Length, profile.Slots.Length);
            var slots = profile.Slots.Span;
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], slots[i]);
            }
        }

        private static void AssertDefaultFallback(OverlaySlotBinding binding, string slot)
        {
            Assert.AreEqual(slot, binding.Slot);
            Assert.IsFalse(binding.Suppress);
            Assert.IsFalse(binding.Snapshot.HasValue);
            Assert.IsTrue(binding.IsDefaultFallback);
        }

        private static void AssertSuppress(OverlaySlotBinding binding, string slot)
        {
            Assert.AreEqual(slot, binding.Slot);
            Assert.IsTrue(binding.Suppress);
            Assert.IsFalse(binding.Snapshot.HasValue);
            Assert.IsFalse(binding.IsDefaultFallback);
        }

        private static void AssertSnapshotOverride(
            OverlaySlotBinding binding,
            string slot,
            string expectedBlendShapeName,
            string expectedRendererPath,
            float expectedValue)
        {
            Assert.AreEqual(slot, binding.Slot);
            Assert.IsFalse(binding.Suppress);
            Assert.IsTrue(binding.Snapshot.HasValue);
            Assert.IsFalse(binding.IsDefaultFallback);

            var snapshot = binding.Snapshot.Value;
            Assert.AreEqual(1, snapshot.BlendShapes.Length);
            Assert.AreEqual(1, snapshot.RendererPaths.Length);
            Assert.AreEqual(expectedRendererPath, snapshot.RendererPaths.Span[0]);

            var blendShape = snapshot.BlendShapes.Span[0];
            Assert.AreEqual(expectedRendererPath, blendShape.RendererPath);
            Assert.AreEqual(expectedBlendShapeName, blendShape.Name);
            Assert.AreEqual(expectedValue, blendShape.Value, 0.0001f);
        }

        private const string LegacyExpressionIdOverlayJson = @"{
    ""schemaVersion"": ""1.0"",
    ""slots"": [""blink""],
    ""layers"": [
        {""name"": ""emotion"", ""priority"": 0, ""exclusionMode"": ""lastWins"", ""inputSources"": [
            {""id"": ""input"", ""weight"": 1.0}
        ]}
    ],
    ""expressions"": [],
    ""rendererPaths"": [],
    ""defaultOverlays"": [
        {""slot"": ""blink"", ""expressionId"": ""blink_overlay""}
    ]
}";

        private const string GazeConfigsExpressionIdJson = @"{
    ""schemaVersion"": ""1.0"",
    ""layers"": [],
    ""expressions"": [],
    ""rendererPaths"": [],
    ""gaze_configs"": [
        {
            ""expressionId"": ""look_left"",
            ""leftEyeBonePath"": ""Head/LeftEye"",
            ""leftEyeInitialRotation"": {""x"":0,""y"":0,""z"":0},
            ""leftEyeYawAxisLocal"": {""x"":0,""y"":1,""z"":0},
            ""leftEyePitchAxisLocal"": {""x"":1,""y"":0,""z"":0},
            ""rightEyeBonePath"": ""Head/RightEye"",
            ""rightEyeInitialRotation"": {""x"":0,""y"":0,""z"":0},
            ""rightEyeYawAxisLocal"": {""x"":0,""y"":1,""z"":0},
            ""rightEyePitchAxisLocal"": {""x"":1,""y"":0,""z"":0},
            ""lookUpAngle"": 15,
            ""lookDownAngle"": 9,
            ""outerYawAngle"": 15,
            ""innerYawAngle"": 18
        }
    ]
}";

        private const string SuppressWithSnapshotOverlayJson = @"{
    ""schemaVersion"": ""1.0"",
    ""slots"": [""blink""],
    ""layers"": [
        {""name"": ""emotion"", ""priority"": 0, ""exclusionMode"": ""lastWins"", ""inputSources"": [
            {""id"": ""input"", ""weight"": 1.0}
        ]}
    ],
    ""expressions"": [],
    ""rendererPaths"": [""Face""],
    ""defaultOverlays"": [
        {""slot"": ""blink"", ""suppress"": true, ""snapshot"": {
            ""transitionDuration"": 0.08,
            ""transitionCurvePreset"": ""Linear"",
            ""blendShapes"": [
                {""rendererPath"": ""Face"", ""name"": ""Blink"", ""value"": 1.0}
            ],
            ""bones"": [],
            ""rendererPaths"": [""Face""],
            ""overlays"": []
        }}
    ]
}";

        private const string ProfileWithoutSlotsJson = @"{
    ""schemaVersion"": ""1.0"",
    ""layers"": [],
    ""expressions"": [],
    ""rendererPaths"": [],
    ""defaultOverlays"": []
}";
    }
}
