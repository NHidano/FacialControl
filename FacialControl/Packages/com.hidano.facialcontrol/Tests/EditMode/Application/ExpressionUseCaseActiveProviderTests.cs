using NUnit.Framework;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Application
{
    /// <summary>
    /// <see cref="ExpressionUseCase"/> が <see cref="IActiveExpressionProvider"/> を
    /// 正しく実装することを保証するテスト。OverlayInputSource の解決ロジックは
    /// 本 provider に依存するため、レイヤー別 top の取得が確実に動くことを保証する。
    /// </summary>
    [TestFixture]
    public class ExpressionUseCaseActiveProviderTests
    {
        private const string EmotionLayer = "emotion";
        private const string EyeLayer = "eye";
        private const string BlinkSlot = "blink";
        private const string BlinkBlendShapeName = "bs_eye_blink";
        private const string MouthBlendShapeName = "bs_mouth";

        private static FacialProfile CreateLastWinsProfile()
        {
            var layers = new[]
            {
                new LayerDefinition(EmotionLayer, 0, ExclusionMode.LastWins),
                new LayerDefinition(EyeLayer, 1, ExclusionMode.LastWins),
            };
            return new FacialProfile("1.0", layers);
        }

        private static FacialProfile CreateBlendProfile()
        {
            var layers = new[]
            {
                new LayerDefinition(EmotionLayer, 0, ExclusionMode.Blend),
            };
            return new FacialProfile("1.0", layers);
        }

        private static (FacialProfile profile, string[] blendShapeNames) CreateOverlayProfile()
        {
            var blendShapeNames = new[] { BlinkBlendShapeName, MouthBlendShapeName };
            var layers = new[]
            {
                new LayerDefinition(EmotionLayer, 0, ExclusionMode.LastWins),
            };

            var smileBlinkSnapshot = CreateSnapshot(
                "smile_blink_snapshot",
                new BlendShapeSnapshot(string.Empty, BlinkBlendShapeName, 1f));
            var defaultBlinkSnapshot = CreateSnapshot(
                "default_blink_snapshot",
                new BlendShapeSnapshot(string.Empty, BlinkBlendShapeName, 0.25f));

            var smile = new Expression(
                id: "smile",
                name: "Smile",
                layer: EmotionLayer,
                transitionDuration: 0f,
                transitionCurve: TransitionCurve.Linear,
                blendShapeValues: new[]
                {
                    new BlendShapeMapping(MouthBlendShapeName, 0.5f),
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
                    new BlendShapeMapping(MouthBlendShapeName, 0.5f),
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
                    new BlendShapeMapping(MouthBlendShapeName, 0.25f),
                },
                overlays: new[]
                {
                    new OverlaySlotBinding(BlinkSlot, suppress: false, snapshot: null),
                });

            var profile = new FacialProfile(
                schemaVersion: "1.0",
                layers: layers,
                expressions: new[] { smile, smileClosedEye, neutral },
                rendererPaths: null,
                layerInputSources: null,
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

        private static OverlayInputSource CreateOverlaySource(
            FacialProfile profile,
            string[] blendShapeNames,
            IActiveExpressionProvider activeProvider)
        {
            return new OverlayInputSource(
                id: InputSourceId.Parse("overlay:blink"),
                slot: BlinkSlot,
                blendShapeCount: blendShapeNames.Length,
                blendShapeNames: blendShapeNames,
                profile: profile,
                activeProvider: activeProvider,
                emotionLayerName: EmotionLayer);
        }

        private static Expression GetExpression(FacialProfile profile, string id)
        {
            var expression = profile.FindExpressionById(id);
            Assert.IsTrue(expression.HasValue, $"Test profile must contain '{id}'.");
            return expression.Value;
        }

        [Test]
        public void TryGetTop_NoActive_ReturnsNull()
        {
            var sut = new ExpressionUseCase(CreateLastWinsProfile());
            Assert.IsNull(((IActiveExpressionProvider)sut).TryGetTopActiveExpression("emotion"));
        }

        [Test]
        public void TryGetTop_AfterActivate_ReturnsThatExpression()
        {
            var sut = new ExpressionUseCase(CreateLastWinsProfile());
            var smile = new Expression("smile", "Smile", "emotion");
            sut.Activate(smile);

            var top = ((IActiveExpressionProvider)sut).TryGetTopActiveExpression("emotion");
            Assert.IsNotNull(top);
            Assert.AreEqual("smile", top.Value.Id);
        }

        [Test]
        public void TryGetTop_LastWins_ReplacementReplacesTop()
        {
            var sut = new ExpressionUseCase(CreateLastWinsProfile());
            sut.Activate(new Expression("smile", "Smile", "emotion"));
            sut.Activate(new Expression("anger", "Anger", "emotion"));

            var top = ((IActiveExpressionProvider)sut).TryGetTopActiveExpression("emotion");
            Assert.IsNotNull(top);
            Assert.AreEqual("anger", top.Value.Id, "LastWins では最後 Activate が top");
        }

        [Test]
        public void TryGetTop_Blend_ReturnsLastAddedAsTop()
        {
            var sut = new ExpressionUseCase(CreateBlendProfile());
            sut.Activate(new Expression("smile", "Smile", "emotion"));
            sut.Activate(new Expression("anger", "Anger", "emotion"));

            var top = ((IActiveExpressionProvider)sut).TryGetTopActiveExpression("emotion");
            Assert.IsNotNull(top);
            Assert.AreEqual("anger", top.Value.Id, "Blend モードでも最後 Activate を top として返す");
        }

        [Test]
        public void TryGetTop_AfterDeactivate_ReturnsNullWhenLayerEmpties()
        {
            var sut = new ExpressionUseCase(CreateLastWinsProfile());
            var smile = new Expression("smile", "Smile", "emotion");
            sut.Activate(smile);
            sut.Deactivate(smile);

            Assert.IsNull(((IActiveExpressionProvider)sut).TryGetTopActiveExpression("emotion"));
        }

        [Test]
        public void TryGetTop_DifferentLayer_IndependentlyTracked()
        {
            var sut = new ExpressionUseCase(CreateLastWinsProfile());
            sut.Activate(new Expression("smile", "Smile", "emotion"));
            sut.Activate(new Expression("blink", "Blink", "eye"));

            var emotion = ((IActiveExpressionProvider)sut).TryGetTopActiveExpression("emotion");
            var eye = ((IActiveExpressionProvider)sut).TryGetTopActiveExpression("eye");
            Assert.AreEqual("smile", emotion?.Id);
            Assert.AreEqual("blink", eye?.Id);
        }

        [TestCase(null)]
        [TestCase("")]
        public void TryGetTop_NullOrEmptyLayer_ReturnsNull(string layerName)
        {
            var sut = new ExpressionUseCase(CreateLastWinsProfile());
            sut.Activate(new Expression("smile", "Smile", "emotion"));
            Assert.IsNull(((IActiveExpressionProvider)sut).TryGetTopActiveExpression(layerName));
        }

        [Test]
        public void TryGetTop_UnknownLayer_ReturnsNull()
        {
            var sut = new ExpressionUseCase(CreateLastWinsProfile());
            sut.Activate(new Expression("smile", "Smile", "emotion"));
            Assert.IsNull(((IActiveExpressionProvider)sut).TryGetTopActiveExpression("does_not_exist"));
        }

        [Test]
        public void TryGetTop_AfterSetProfile_IsCleared()
        {
            var sut = new ExpressionUseCase(CreateLastWinsProfile());
            sut.Activate(new Expression("smile", "Smile", "emotion"));

            sut.SetProfile(CreateLastWinsProfile());

            Assert.IsNull(((IActiveExpressionProvider)sut).TryGetTopActiveExpression("emotion"));
        }

        [Test]
        public void TryGetTop_OverlayInputSourceResolvesNewSchemaForActiveProviderSwitch()
        {
            var (profile, blendShapeNames) = CreateOverlayProfile();
            Assert.IsEmpty(profile.ValidateSlotReferences());
            Assert.IsFalse(profile.FindExpressionById("blink_overlay").HasValue);

            var sut = new ExpressionUseCase(profile);
            var overlaySource = CreateOverlaySource(profile, blendShapeNames, sut);
            var output = new float[blendShapeNames.Length];

            Assert.IsTrue(overlaySource.TryWriteValues(output));
            Assert.AreEqual(0.25f, output[0], 1e-6f);
            Assert.IsTrue(overlaySource.ContributeMask[0]);
            Assert.IsFalse(overlaySource.ContributeMask[1]);

            sut.Activate(GetExpression(profile, "smile"));
            output[0] = -1f;
            output[1] = -1f;
            Assert.IsTrue(overlaySource.TryWriteValues(output));
            Assert.AreEqual(1f, output[0], 1e-6f);
            Assert.AreEqual(0f, output[1], 1e-6f);
            Assert.IsTrue(overlaySource.ContributeMask[0]);
            Assert.IsFalse(overlaySource.ContributeMask[1]);

            sut.Activate(GetExpression(profile, "smile_closed_eye"));
            output[0] = -1f;
            output[1] = -1f;
            Assert.IsFalse(overlaySource.TryWriteValues(output));
            Assert.AreEqual(-1f, output[0], 1e-6f);
            Assert.AreEqual(-1f, output[1], 1e-6f);
            Assert.IsFalse(overlaySource.ContributeMask[0]);
            Assert.IsFalse(overlaySource.ContributeMask[1]);

            sut.Activate(GetExpression(profile, "neutral"));
            output[0] = -1f;
            output[1] = -1f;
            Assert.IsTrue(overlaySource.TryWriteValues(output));
            Assert.AreEqual(0.25f, output[0], 1e-6f);
            Assert.AreEqual(0f, output[1], 1e-6f);
            Assert.IsTrue(overlaySource.ContributeMask[0]);
            Assert.IsFalse(overlaySource.ContributeMask[1]);
        }
    }
}
