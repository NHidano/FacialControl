using System.Collections.Generic;
using NUnit.Framework;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Application
{
    /// <summary>
    /// Regression tests for the emotion + overlay two-layer pipeline with
    /// the slot/snapshot overlay schema.
    /// </summary>
    [TestFixture]
    public class LayerUseCaseOverlayLayerTests
    {
        private const string BlinkSlot = "blink";
        private const string BrowName = "bs_brow";
        private const string EyeMakeupName = "bs_eye_lift";
        private const string EyeBlinkName = "bs_eye_blink";
        private const string MouthName = "bs_mouth";

        private static (FacialProfile profile, string[] bsNames) BuildProfile()
        {
            var bsNames = new[] { BrowName, EyeMakeupName, EyeBlinkName, MouthName };
            var layers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("overlay", 1, ExclusionMode.LastWins),
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
                layer: "emotion",
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
                layer: "emotion",
                transitionDuration: 0f,
                transitionCurve: TransitionCurve.Linear,
                blendShapeValues: new[]
                {
                    new BlendShapeMapping(MouthName, 0.5f),
                    new BlendShapeMapping(EyeMakeupName, 0.75f),
                    new BlendShapeMapping(EyeBlinkName, 1.0f),
                },
                overlays: new[]
                {
                    new OverlaySlotBinding(BlinkSlot, suppress: true, snapshot: null),
                });

            var neutral = new Expression(
                id: "neutral",
                name: "Neutral",
                layer: "emotion",
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

            return (profile, bsNames);
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

        private static (LayerUseCase useCase, ExpressionUseCase exprUseCase) BuildPipeline(
            FacialProfile profile, string[] bsNames)
        {
            var exprUseCase = new ExpressionUseCase(profile);

            var overlayInputSource = new OverlayInputSource(
                id: InputSourceId.Parse("overlay:blink"),
                slot: BlinkSlot,
                blendShapeCount: bsNames.Length,
                blendShapeNames: bsNames,
                profile: profile,
                activeProvider: exprUseCase,
                emotionLayerName: "emotion");

            var additional = new List<(int layerIdx, IInputSource source, float weight)>
            {
                (1, overlayInputSource, 1f),
            };
            var useCase = new LayerUseCase(profile, exprUseCase, bsNames, additional);
            return (useCase, exprUseCase);
        }

        [Test]
        public void BuildProfile_UsesInlineOverlaySchema()
        {
            var (profile, _) = BuildProfile();

            Assert.AreEqual(1, profile.Slots.Length);
            Assert.AreEqual(BlinkSlot, profile.Slots.Span[0]);
            Assert.AreEqual(3, profile.Expressions.Length);

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
        public void SmileHold_FullTrigger_InlineOverlayReplacesEyeBlendShapes()
        {
            var (profile, bsNames) = BuildProfile();
            var (useCase, exprUseCase) = BuildPipeline(profile, bsNames);
            using (useCase)
            {
                exprUseCase.Activate(profile.FindExpressionById("smile").Value);
                useCase.SetLayerWeight("overlay", 1f);
                useCase.UpdateWeights(1f);

                var output = useCase.GetBlendedOutput();

                Assert.AreEqual(1.0f, output[0], 1e-3f);
                Assert.AreEqual(0.0f, output[1], 1e-3f);
                Assert.AreEqual(1.0f, output[2], 1e-3f);
                Assert.AreEqual(0.5f, output[3], 1e-3f);
            }
        }

        [Test]
        public void SmileHold_HalfTrigger_InlineOverlayInterpolatesLinearly()
        {
            var (profile, bsNames) = BuildProfile();
            var (useCase, exprUseCase) = BuildPipeline(profile, bsNames);
            using (useCase)
            {
                exprUseCase.Activate(profile.FindExpressionById("smile").Value);
                useCase.SetLayerWeight("overlay", 0.5f);
                useCase.UpdateWeights(1f);

                var output = useCase.GetBlendedOutput();

                Assert.AreEqual(1.0f, output[0], 1e-3f);
                Assert.AreEqual(0.5f, output[1], 1e-3f);
                Assert.AreEqual(0.5f, output[2], 1e-3f);
                Assert.AreEqual(0.5f, output[3], 1e-3f);
            }
        }

        [Test]
        public void SmileClosedEyeHold_FullTrigger_OverlaySuppressed()
        {
            var (profile, bsNames) = BuildProfile();
            var (useCase, exprUseCase) = BuildPipeline(profile, bsNames);
            using (useCase)
            {
                exprUseCase.Activate(profile.FindExpressionById("smile_closed_eye").Value);
                useCase.SetLayerWeight("overlay", 1f);
                useCase.UpdateWeights(1f);

                var output = useCase.GetBlendedOutput();

                Assert.AreEqual(0.75f, output[1], 1e-3f);
                Assert.AreEqual(1.0f, output[2], 1e-3f);
                Assert.AreEqual(0.5f, output[3], 1e-3f);
            }
        }

        [Test]
        public void NoActiveExpression_FullTrigger_DefaultBlinkFires()
        {
            var (profile, bsNames) = BuildProfile();
            var (useCase, _) = BuildPipeline(profile, bsNames);
            using (useCase)
            {
                useCase.SetLayerWeight("overlay", 1f);
                useCase.UpdateWeights(1f);

                var output = useCase.GetBlendedOutput();

                Assert.AreEqual(0.0f, output[0], 1e-3f);
                Assert.AreEqual(0.0f, output[1], 1e-3f);
                Assert.AreEqual(1.0f, output[2], 1e-3f);
                Assert.AreEqual(0.0f, output[3], 1e-3f);
            }
        }

        [Test]
        public void NeutralHold_FullTrigger_FallsBackToDefaultBlink()
        {
            var (profile, bsNames) = BuildProfile();
            var (useCase, exprUseCase) = BuildPipeline(profile, bsNames);
            using (useCase)
            {
                exprUseCase.Activate(profile.FindExpressionById("neutral").Value);
                useCase.SetLayerWeight("overlay", 1f);
                useCase.UpdateWeights(1f);

                var output = useCase.GetBlendedOutput();

                Assert.AreEqual(0.0f, output[1], 1e-3f);
                Assert.AreEqual(1.0f, output[2], 1e-3f);
                Assert.AreEqual(0.25f, output[3], 1e-3f);
            }
        }
    }
}
