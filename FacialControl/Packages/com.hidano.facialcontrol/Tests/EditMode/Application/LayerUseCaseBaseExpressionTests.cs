using System;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;

namespace Hidano.FacialControl.Tests.EditMode.Application
{
    /// <summary>
    /// ベース表情 (<see cref="FacialProfile.BaseExpression"/>) がライブ合成経路
    /// (<see cref="LayerUseCase.UpdateWeights"/>) の出力初期値として適用されることを検証する。
    /// <para>
    /// 期待挙動: どのレイヤーも contribute しない BlendShape index にはベース表情の値が残り、
    /// contribute する index はレイヤー出力で上書きされる。
    /// </para>
    /// </summary>
    [TestFixture]
    public class LayerUseCaseBaseExpressionTests
    {
        private static readonly string[] BlendShapeNames = { "bs_a", "bs_b" };

        private static FacialProfile CreateProfile(
            BlendShapeSnapshot[] baseExpression,
            params Expression[] expressions)
        {
            var layers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
            };

            return new FacialProfile(
                "1.0",
                layers,
                expressions ?? Array.Empty<Expression>(),
                baseExpression: baseExpression);
        }

        private static Expression CreateSmile()
        {
            return new Expression(
                "smile", "smile", "emotion", 0.1f, TransitionCurve.Linear,
                new[] { new BlendShapeMapping("bs_a", 1f, null) });
        }

        private static BlendShapeSnapshot[] CreateBaseExpression()
        {
            return new[]
            {
                new BlendShapeSnapshot(string.Empty, "bs_a", 0.25f),
                new BlendShapeSnapshot(string.Empty, "bs_b", 0.75f),
            };
        }

        [Test]
        public void UpdateWeights_NoActiveExpression_KeepsBaseExpressionValues()
        {
            var profile = CreateProfile(CreateBaseExpression());
            var expressionUseCase = new ExpressionUseCase(profile);
            var useCase = new LayerUseCase(profile, expressionUseCase, BlendShapeNames);

            useCase.UpdateWeights(1f);
            var output = useCase.GetBlendedOutput();

            Assert.That(output[0], Is.EqualTo(0.25f).Within(1e-5f), "表情非活性時は base 値が残る");
            Assert.That(output[1], Is.EqualTo(0.75f).Within(1e-5f), "表情非活性時は base 値が残る");
        }

        [Test]
        public void UpdateWeights_LayerContributesSubset_KeepsBaseOnNonContributingIndex()
        {
            var smile = CreateSmile();
            var profile = CreateProfile(CreateBaseExpression(), smile);
            var expressionUseCase = new ExpressionUseCase(profile);
            var useCase = new LayerUseCase(profile, expressionUseCase, BlendShapeNames);

            expressionUseCase.Activate(smile);
            useCase.UpdateWeights(1f);
            var output = useCase.GetBlendedOutput();

            Assert.That(output[0], Is.EqualTo(1f).Within(1e-5f), "contribute する index は表情値で上書きされる");
            Assert.That(output[1], Is.EqualTo(0.75f).Within(1e-5f), "contribute しない index は base 値が残る");
        }

        [Test]
        public void UpdateWeights_NoBaseExpression_InitializesOutputToZero()
        {
            var smile = CreateSmile();
            var profile = CreateProfile(baseExpression: null, expressions: smile);
            var expressionUseCase = new ExpressionUseCase(profile);
            var useCase = new LayerUseCase(profile, expressionUseCase, BlendShapeNames);

            expressionUseCase.Activate(smile);
            useCase.UpdateWeights(1f);
            var output = useCase.GetBlendedOutput();

            Assert.That(output[0], Is.EqualTo(1f).Within(1e-5f));
            Assert.That(output[1], Is.EqualTo(0f).Within(1e-5f), "base 未設定時は全 0 初期化（現状互換）");
        }

        [Test]
        public void UpdateWeights_BaseExpressionUnknownBlendShapeName_IsIgnored()
        {
            var baseExpression = new[]
            {
                new BlendShapeSnapshot(string.Empty, "bs_not_on_this_model", 1f),
                new BlendShapeSnapshot(string.Empty, "bs_b", 0.4f),
            };
            var profile = CreateProfile(baseExpression);
            var expressionUseCase = new ExpressionUseCase(profile);
            var useCase = new LayerUseCase(profile, expressionUseCase, BlendShapeNames);

            Assert.DoesNotThrow(() => useCase.UpdateWeights(1f));
            var output = useCase.GetBlendedOutput();

            Assert.That(output[0], Is.EqualTo(0f).Within(1e-5f), "モデルに無い BlendShape 名は無視される");
            Assert.That(output[1], Is.EqualTo(0.4f).Within(1e-5f));
        }

        [Test]
        public void UpdateWeights_BaseExpressionValueOutOfRange_IsClampedTo01()
        {
            var baseExpression = new[]
            {
                new BlendShapeSnapshot(string.Empty, "bs_a", 1.5f),
                new BlendShapeSnapshot(string.Empty, "bs_b", -0.5f),
            };
            var profile = CreateProfile(baseExpression);
            var expressionUseCase = new ExpressionUseCase(profile);
            var useCase = new LayerUseCase(profile, expressionUseCase, BlendShapeNames);

            useCase.UpdateWeights(1f);
            var output = useCase.GetBlendedOutput();

            Assert.That(output[0], Is.EqualTo(1f).Within(1e-5f));
            Assert.That(output[1], Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void UpdateWeights_CalledRepeatedly_ReappliesBaseExpressionEachFrame()
        {
            var smile = CreateSmile();
            var profile = CreateProfile(CreateBaseExpression(), smile);
            var expressionUseCase = new ExpressionUseCase(profile);
            var useCase = new LayerUseCase(profile, expressionUseCase, BlendShapeNames);

            expressionUseCase.Activate(smile);
            useCase.UpdateWeights(1f);
            expressionUseCase.Deactivate(smile);
            useCase.UpdateWeights(1f);
            useCase.UpdateWeights(1f);

            var output = useCase.GetBlendedOutput();

            Assert.That(output[0], Is.EqualTo(0.25f).Within(1e-5f),
                "表情が rest に戻った index は base 値へ戻る（前フレーム値の残留も base の二重適用も起きない）");
            Assert.That(output[1], Is.EqualTo(0.75f).Within(1e-5f));
        }

        [Test]
        public void SetProfile_NewBaseExpression_ReplacesBaseValues()
        {
            var profile = CreateProfile(CreateBaseExpression());
            var expressionUseCase = new ExpressionUseCase(profile);
            var useCase = new LayerUseCase(profile, expressionUseCase, BlendShapeNames);

            useCase.UpdateWeights(1f);

            var newProfile = CreateProfile(new[]
            {
                new BlendShapeSnapshot(string.Empty, "bs_a", 0.1f),
            });
            useCase.SetProfile(newProfile, BlendShapeNames);
            useCase.UpdateWeights(1f);
            var output = useCase.GetBlendedOutput();

            Assert.That(output[0], Is.EqualTo(0.1f).Within(1e-5f));
            Assert.That(output[1], Is.EqualTo(0f).Within(1e-5f), "新プロファイルに無い index は 0 に戻る");
        }
    }
}
