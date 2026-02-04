using System;
using NUnit.Framework;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Editor
{
    /// <summary>
    /// P19-T01: RebuildProfileFromEdits 相当ロジックで BlendShape Weight 編集値が正しく反映されるか検証。
    /// Weight 編集前後のラウンドトリップ、複数 BlendShape の同時編集、範囲外値のクランプを確認する。
    /// </summary>
    [TestFixture]
    public class ProfileRebuildBlendShapeWeightTests
    {
        private SystemTextJsonParser _parser;

        [SetUp]
        public void SetUp()
        {
            _parser = new SystemTextJsonParser();
        }

        // --- ヘルパー ---

        /// <summary>
        /// テスト用のデフォルトプロファイルを生成する
        /// </summary>
        private FacialProfile CreateTestProfile()
        {
            var layers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("lipsync", 1, ExclusionMode.Blend)
            };

            var expressions = new[]
            {
                new Expression(
                    "expr-001",
                    "smile",
                    "emotion",
                    0.25f,
                    TransitionCurve.Linear,
                    new[]
                    {
                        new BlendShapeMapping("mouth_smile", 0.8f),
                        new BlendShapeMapping("eye_squint", 0.3f),
                        new BlendShapeMapping("cheek_puff", 0.5f)
                    }),
                new Expression(
                    "expr-002",
                    "blink",
                    "emotion",
                    0.1f,
                    TransitionCurve.Linear,
                    new[]
                    {
                        new BlendShapeMapping("eye_close_L", 1.0f),
                        new BlendShapeMapping("eye_close_R", 1.0f)
                    })
            };

            return new FacialProfile("1.0", layers, expressions);
        }

        /// <summary>
        /// BlendShape Weight を編集してプロファイルを再構築する
        /// （RebuildProfileFromEdits の Weight 編集ロジックを再現）
        /// </summary>
        private FacialProfile RebuildWithWeightEdits(
            FacialProfile originalProfile,
            int expressionIndex,
            float[] weightEdits)
        {
            var expressions = originalProfile.Expressions.ToArray();
            var orig = expressions[expressionIndex];
            var blendShapes = orig.BlendShapeValues.ToArray();

            for (int j = 0; j < blendShapes.Length && j < weightEdits.Length; j++)
            {
                blendShapes[j] = new BlendShapeMapping(
                    blendShapes[j].Name,
                    weightEdits[j],
                    blendShapes[j].Renderer);
            }

            expressions[expressionIndex] = new Expression(
                orig.Id,
                orig.Name,
                orig.Layer,
                orig.TransitionDuration,
                orig.TransitionCurve,
                blendShapes,
                orig.LayerSlots.ToArray());

            return new FacialProfile(
                originalProfile.SchemaVersion,
                originalProfile.Layers.ToArray(),
                expressions);
        }

        /// <summary>
        /// シリアライズ → 再パースのラウンドトリップを行う
        /// </summary>
        private FacialProfile RoundTrip(FacialProfile profile)
        {
            var json = _parser.SerializeProfile(profile);
            return _parser.ParseProfile(json);
        }

        // --- Weight 編集のラウンドトリップテスト ---

        [Test]
        public void EditWeight_SingleValue_RoundTrip_ValueUpdated()
        {
            var original = CreateTestProfile();
            var edited = RebuildWithWeightEdits(original, 0, new[] { 0.5f, 0.3f, 0.5f });
            var parsed = RoundTrip(edited);

            var bsSpan = parsed.Expressions.Span[0].BlendShapeValues.Span;
            Assert.AreEqual(0.5f, bsSpan[0].Value, 0.001f);
        }

        [Test]
        public void EditWeight_MultipleValues_RoundTrip_AllValuesUpdated()
        {
            var original = CreateTestProfile();
            var edited = RebuildWithWeightEdits(original, 0, new[] { 0.1f, 0.9f, 0.0f });
            var parsed = RoundTrip(edited);

            var bsSpan = parsed.Expressions.Span[0].BlendShapeValues.Span;
            Assert.AreEqual(3, bsSpan.Length);
            Assert.AreEqual(0.1f, bsSpan[0].Value, 0.001f);
            Assert.AreEqual(0.9f, bsSpan[1].Value, 0.001f);
            Assert.AreEqual(0.0f, bsSpan[2].Value, 0.001f);
        }

        [Test]
        public void EditWeight_NamesPreserved()
        {
            var original = CreateTestProfile();
            var edited = RebuildWithWeightEdits(original, 0, new[] { 0.1f, 0.2f, 0.3f });
            var parsed = RoundTrip(edited);

            var bsSpan = parsed.Expressions.Span[0].BlendShapeValues.Span;
            Assert.AreEqual("mouth_smile", bsSpan[0].Name);
            Assert.AreEqual("eye_squint", bsSpan[1].Name);
            Assert.AreEqual("cheek_puff", bsSpan[2].Name);
        }

        [Test]
        public void EditWeight_NegativeValue_ClampedToZero()
        {
            var original = CreateTestProfile();
            var edited = RebuildWithWeightEdits(original, 0, new[] { -0.5f, 0.3f, 0.5f });
            var parsed = RoundTrip(edited);

            var bsSpan = parsed.Expressions.Span[0].BlendShapeValues.Span;
            Assert.AreEqual(0.0f, bsSpan[0].Value, 0.001f);
        }

        [Test]
        public void EditWeight_ValueOverOne_ClampedToOne()
        {
            var original = CreateTestProfile();
            var edited = RebuildWithWeightEdits(original, 0, new[] { 1.5f, 0.3f, 0.5f });
            var parsed = RoundTrip(edited);

            var bsSpan = parsed.Expressions.Span[0].BlendShapeValues.Span;
            Assert.AreEqual(1.0f, bsSpan[0].Value, 0.001f);
        }

        [Test]
        public void EditWeight_OtherExpressionUnchanged()
        {
            var original = CreateTestProfile();
            var edited = RebuildWithWeightEdits(original, 0, new[] { 0.1f, 0.2f, 0.3f });
            var parsed = RoundTrip(edited);

            // Expression 1 は元のまま
            var expr1 = parsed.Expressions.Span[1];
            Assert.AreEqual("expr-002", expr1.Id);
            Assert.AreEqual("blink", expr1.Name);
            var bsSpan1 = expr1.BlendShapeValues.Span;
            Assert.AreEqual(1.0f, bsSpan1[0].Value, 0.001f);
            Assert.AreEqual(1.0f, bsSpan1[1].Value, 0.001f);
        }

        [Test]
        public void EditWeight_ExpressionMetadataPreserved()
        {
            var original = CreateTestProfile();
            var edited = RebuildWithWeightEdits(original, 0, new[] { 0.1f, 0.2f, 0.3f });
            var parsed = RoundTrip(edited);

            var expr = parsed.Expressions.Span[0];
            Assert.AreEqual("expr-001", expr.Id);
            Assert.AreEqual("smile", expr.Name);
            Assert.AreEqual("emotion", expr.Layer);
            Assert.AreEqual(0.25f, expr.TransitionDuration, 0.001f);
        }

        [Test]
        public void EditWeight_SecondExpression_RoundTrip_ValueUpdated()
        {
            var original = CreateTestProfile();
            var edited = RebuildWithWeightEdits(original, 1, new[] { 0.5f, 0.7f });
            var parsed = RoundTrip(edited);

            var bsSpan = parsed.Expressions.Span[1].BlendShapeValues.Span;
            Assert.AreEqual(0.5f, bsSpan[0].Value, 0.001f);
            Assert.AreEqual(0.7f, bsSpan[1].Value, 0.001f);
        }

        [Test]
        public void EditWeight_BoundaryZero_RoundTrip_ValueIsZero()
        {
            var original = CreateTestProfile();
            var edited = RebuildWithWeightEdits(original, 0, new[] { 0.0f, 0.0f, 0.0f });
            var parsed = RoundTrip(edited);

            var bsSpan = parsed.Expressions.Span[0].BlendShapeValues.Span;
            for (int i = 0; i < bsSpan.Length; i++)
            {
                Assert.AreEqual(0.0f, bsSpan[i].Value, 0.001f);
            }
        }

        [Test]
        public void EditWeight_BoundaryOne_RoundTrip_ValueIsOne()
        {
            var original = CreateTestProfile();
            var edited = RebuildWithWeightEdits(original, 0, new[] { 1.0f, 1.0f, 1.0f });
            var parsed = RoundTrip(edited);

            var bsSpan = parsed.Expressions.Span[0].BlendShapeValues.Span;
            for (int i = 0; i < bsSpan.Length; i++)
            {
                Assert.AreEqual(1.0f, bsSpan[i].Value, 0.001f);
            }
        }
    }
}
