using System;
using System.Collections.Generic;
using NUnit.Framework;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Application
{
    /// <summary>
    /// アナログ入力で重み付けされる overlay レイヤーが、active な smile 系 Expression の
    /// slot / snapshot binding から blink overlay を解決することを検証する。
    /// </summary>
    [TestFixture]
    public class LayerUseCaseAnalogOverlayTests
    {
        private const string BlinkSlot = "blink";
        private const string EmotionLayer = "emotion";
        private const string OverlayLayer = "overlay";
        private const string BrowName = "bs_brow";
        private const string EyeMakeupName = "bs_eye_lift";
        private const string EyeBlinkName = "bs_eye_blink";
        private const string MouthName = "bs_mouth";

        private sealed class FakeScalarSource : IAnalogInputSource
        {
            public FakeScalarSource(string id) { Id = id; }

            public string Id { get; }
            public bool IsValid => true;
            public int AxisCount => 1;
            public float Value { get; set; }

            public void Tick(float deltaTime) { }

            public bool TryReadScalar(out float value)
            {
                value = Value;
                return true;
            }

            public bool TryReadVector2(out float x, out float y)
            {
                x = Value;
                y = 0f;
                return true;
            }

            public bool TryReadAxes(Span<float> output)
            {
                if (output.Length >= 1) output[0] = Value;
                return true;
            }
        }

        private static (FacialProfile profile, string[] blendShapeNames) BuildProfile()
        {
            var blendShapeNames = new[] { BrowName, EyeMakeupName, EyeBlinkName, MouthName };
            var layers = new[]
            {
                new LayerDefinition(EmotionLayer, 0, ExclusionMode.LastWins),
                new LayerDefinition(OverlayLayer, 1, ExclusionMode.LastWins),
            };

            var smileBlinkSnapshot = CreateSnapshot(
                "smile_blink_snapshot",
                new BlendShapeSnapshot(string.Empty, EyeMakeupName, 0.0f),
                new BlendShapeSnapshot(string.Empty, EyeBlinkName, 1.0f));

            var defaultBlinkSnapshot = CreateSnapshot(
                "default_blink_snapshot",
                new BlendShapeSnapshot(string.Empty, EyeMakeupName, 0.0f),
                new BlendShapeSnapshot(string.Empty, EyeBlinkName, 1.0f));

            var smile = new Expression(
                id: "smile",
                name: "Smile",
                layer: EmotionLayer,
                transitionDuration: 0f,
                transitionCurve: TransitionCurve.Linear,
                blendShapeValues: new[]
                {
                    new BlendShapeMapping(BrowName, 1.0f),
                    new BlendShapeMapping(EyeMakeupName, 1.0f),
                    new BlendShapeMapping(MouthName, 0.5f),
                },
                overlays: new[]
                {
                    new OverlaySlotBinding(BlinkSlot, suppress: false, snapshot: smileBlinkSnapshot),
                });

            var smileClosedEye = new Expression(
                id: "smile_closed_eye",
                name: "SmileClosedEye",
                layer: EmotionLayer,
                transitionDuration: 0f,
                transitionCurve: TransitionCurve.Linear,
                blendShapeValues: new[]
                {
                    new BlendShapeMapping(EyeMakeupName, 0.75f),
                    new BlendShapeMapping(EyeBlinkName, 1.0f),
                    new BlendShapeMapping(MouthName, 0.5f),
                },
                overlays: new[]
                {
                    new OverlaySlotBinding(BlinkSlot, suppress: true, snapshot: null),
                });

            var neutral = new Expression(
                id: "neutral",
                name: "Neutral",
                layer: EmotionLayer,
                transitionDuration: 0f,
                transitionCurve: TransitionCurve.Linear,
                blendShapeValues: new[]
                {
                    new BlendShapeMapping(EyeMakeupName, 1.0f),
                    new BlendShapeMapping(MouthName, 0.25f),
                });

            var inputSources = new[]
            {
                new[] { new InputSourceDeclaration("input", 1f, null) },
                new[] { new InputSourceDeclaration("input:overlay:blink", 1f, null) },
            };

            var profile = new FacialProfile(
                schemaVersion: "1.0",
                layers: layers,
                expressions: new[] { smile, smileClosedEye, neutral },
                rendererPaths: null,
                layerInputSources: inputSources,
                defaultOverlays: new[]
                {
                    new OverlaySlotBinding(BlinkSlot, suppress: false, snapshot: defaultBlinkSnapshot),
                },
                slots: new[] { BlinkSlot });

            return (profile, blendShapeNames);
        }

        private static ExpressionSnapshot CreateSnapshot(
            string id,
            params BlendShapeSnapshot[] blendShapes)
        {
            return new ExpressionSnapshot(
                id,
                transitionDuration: Expression.DefaultTransitionDuration,
                transitionCurvePreset: TransitionCurvePreset.Linear,
                blendShapes: blendShapes,
                bones: null,
                rendererPaths: null);
        }

        private static (LayerUseCase useCase, ExpressionUseCase exprUseCase, FakeScalarSource trigger)
            BuildPipeline(FacialProfile profile, string[] blendShapeNames)
        {
            var exprUseCase = new ExpressionUseCase(profile);
            var trigger = new FakeScalarSource("trigger");

            var overlayInputSource = new OverlayInputSource(
                id: InputSourceId.Parse("overlay:blink"),
                slot: BlinkSlot,
                blendShapeCount: blendShapeNames.Length,
                blendShapeNames: blendShapeNames,
                profile: profile,
                activeProvider: exprUseCase,
                emotionLayerName: EmotionLayer);

            var additional = new List<(int layerIdx, IInputSource source, float weight)>
            {
                (1, overlayInputSource, 1f),
            };
            var useCase = new LayerUseCase(profile, exprUseCase, blendShapeNames, additional);
            return (useCase, exprUseCase, trigger);
        }

        private static void Activate(ExpressionUseCase exprUseCase, FacialProfile profile, string expressionId)
        {
            var expression = profile.FindExpressionById(expressionId);
            Assert.IsTrue(expression.HasValue, $"テスト profile に '{expressionId}' が存在すること。");
            exprUseCase.Activate(expression.Value);
        }

        private static void ApplyAnalogOverlayWeight(LayerUseCase useCase, FakeScalarSource trigger)
        {
            Assert.IsTrue(trigger.TryReadScalar(out float value));
            useCase.SetLayerWeight(OverlayLayer, value);
        }

        [Test]
        public void BuildProfile_UsesInlineOverlaySchema()
        {
            var (profile, _) = BuildProfile();

            Assert.AreEqual(1, profile.Slots.Length);
            Assert.AreEqual(BlinkSlot, profile.Slots.Span[0]);
            Assert.IsFalse(profile.FindExpressionById("blink_overlay").HasValue);

            var smile = profile.FindExpressionById("smile").Value;
            Assert.IsTrue(smile.TryGetOverlay(BlinkSlot, out var smileBinding));
            Assert.IsFalse(smileBinding.Suppress);
            Assert.IsTrue(smileBinding.Snapshot.HasValue);
            Assert.AreEqual("smile_blink_snapshot", smileBinding.Snapshot.Value.Id);

            var smileClosedEye = profile.FindExpressionById("smile_closed_eye").Value;
            Assert.IsTrue(smileClosedEye.TryGetOverlay(BlinkSlot, out var closedEyeBinding));
            Assert.IsTrue(closedEyeBinding.Suppress);
            Assert.IsFalse(closedEyeBinding.Snapshot.HasValue);
        }

        [Test]
        public void AnalogZero_SmileInlineOverlayDoesNotAffectBaseExpression()
        {
            var (profile, blendShapeNames) = BuildProfile();
            var (useCase, exprUseCase, trigger) = BuildPipeline(profile, blendShapeNames);
            using (useCase)
            {
                Activate(exprUseCase, profile, "smile");
                trigger.Value = 0f;
                ApplyAnalogOverlayWeight(useCase, trigger);

                useCase.UpdateWeights(1f);
                var output = useCase.GetBlendedOutput();

                Assert.AreEqual(1.0f, output[0], 1e-4f);
                Assert.AreEqual(1.0f, output[1], 1e-4f);
                Assert.AreEqual(0.0f, output[2], 1e-4f);
                Assert.AreEqual(0.5f, output[3], 1e-4f);
            }
        }

        [Test]
        public void AnalogHalf_SmileInlineOverlayInterpolatesBlinkSlot()
        {
            var (profile, blendShapeNames) = BuildProfile();
            var (useCase, exprUseCase, trigger) = BuildPipeline(profile, blendShapeNames);
            using (useCase)
            {
                Activate(exprUseCase, profile, "smile");
                trigger.Value = 0.5f;
                ApplyAnalogOverlayWeight(useCase, trigger);

                useCase.UpdateWeights(1f);
                var output = useCase.GetBlendedOutput();

                Assert.AreEqual(1.0f, output[0], 1e-4f);
                Assert.AreEqual(0.5f, output[1], 1e-4f);
                Assert.AreEqual(0.5f, output[2], 1e-4f);
                Assert.AreEqual(0.5f, output[3], 1e-4f);
            }
        }

        [Test]
        public void AnalogFull_SmileInlineOverlayReplacesBlinkSlot()
        {
            var (profile, blendShapeNames) = BuildProfile();
            var (useCase, exprUseCase, trigger) = BuildPipeline(profile, blendShapeNames);
            using (useCase)
            {
                Activate(exprUseCase, profile, "smile");
                trigger.Value = 1f;
                ApplyAnalogOverlayWeight(useCase, trigger);

                useCase.UpdateWeights(1f);
                var output = useCase.GetBlendedOutput();

                Assert.AreEqual(1.0f, output[0], 1e-4f);
                Assert.AreEqual(0.0f, output[1], 1e-4f);
                Assert.AreEqual(1.0f, output[2], 1e-4f);
                Assert.AreEqual(0.5f, output[3], 1e-4f);
            }
        }

        [Test]
        public void AnalogFull_SmileClosedEyeSuppressesOverlay()
        {
            var (profile, blendShapeNames) = BuildProfile();
            var (useCase, exprUseCase, trigger) = BuildPipeline(profile, blendShapeNames);
            using (useCase)
            {
                Activate(exprUseCase, profile, "smile_closed_eye");
                trigger.Value = 1f;
                ApplyAnalogOverlayWeight(useCase, trigger);

                useCase.UpdateWeights(1f);
                var output = useCase.GetBlendedOutput();

                Assert.AreEqual(0.0f, output[0], 1e-4f);
                Assert.AreEqual(0.75f, output[1], 1e-4f);
                Assert.AreEqual(1.0f, output[2], 1e-4f);
                Assert.AreEqual(0.5f, output[3], 1e-4f);
            }
        }

        [Test]
        public void AnalogSweep_SmileInlineOverlayFollowsTriggerLinearly()
        {
            var (profile, blendShapeNames) = BuildProfile();
            var (useCase, exprUseCase, trigger) = BuildPipeline(profile, blendShapeNames);
            using (useCase)
            {
                Activate(exprUseCase, profile, "smile");
                useCase.UpdateWeights(1f);

                foreach (var t in new[] { 0f, 0.25f, 0.5f, 0.75f, 1.0f })
                {
                    trigger.Value = t;
                    ApplyAnalogOverlayWeight(useCase, trigger);
                    useCase.UpdateWeights(0.016f);
                    var output = useCase.GetBlendedOutput();

                    Assert.AreEqual(1.0f, output[0], 1e-4f, $"Trigger={t} で Brow が smile の値を維持すること。");
                    Assert.AreEqual(1f - t, output[1], 1e-4f, $"Trigger={t} で EyeMakeup が線形に blend out すること。");
                    Assert.AreEqual(t, output[2], 1e-4f, $"Trigger={t} に Blink が線形追従すること。");
                    Assert.AreEqual(0.5f, output[3], 1e-4f, $"Trigger={t} で Mouth が smile の値を維持すること。");
                }
            }
        }
    }
}
