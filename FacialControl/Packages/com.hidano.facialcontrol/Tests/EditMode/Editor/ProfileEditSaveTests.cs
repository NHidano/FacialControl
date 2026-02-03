using System;
using NUnit.Framework;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Editor
{
    /// <summary>
    /// P17-T02: レイヤー編集・Expression 編集後の JSON 上書き保存ラウンドトリップ検証。
    /// パース → 編集 → シリアライズ → 再パースで値が一致することを確認する。
    /// </summary>
    [TestFixture]
    public class ProfileEditSaveTests
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
                new LayerDefinition("lipsync", 1, ExclusionMode.Blend),
                new LayerDefinition("eye", 2, ExclusionMode.LastWins)
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
                        new BlendShapeMapping("eye_squint", 0.3f)
                    }),
                new Expression(
                    "expr-002",
                    "blink",
                    "eye",
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
        /// シリアライズ → 再パースのラウンドトリップを行う
        /// </summary>
        private FacialProfile RoundTrip(FacialProfile profile)
        {
            var json = _parser.SerializeProfile(profile);
            return _parser.ParseProfile(json);
        }

        // --- レイヤー編集テスト ---

        [Test]
        public void EditLayerName_RoundTrip_NameUpdated()
        {
            var original = CreateTestProfile();
            var layers = original.Layers.ToArray();
            layers[0] = new LayerDefinition("感情", layers[0].Priority, layers[0].ExclusionMode);

            var edited = new FacialProfile(original.SchemaVersion, layers, original.Expressions.ToArray());
            var parsed = RoundTrip(edited);

            Assert.AreEqual("感情", parsed.Layers.Span[0].Name);
        }

        [Test]
        public void EditLayerPriority_RoundTrip_PriorityUpdated()
        {
            var original = CreateTestProfile();
            var layers = original.Layers.ToArray();
            layers[1] = new LayerDefinition(layers[1].Name, 10, layers[1].ExclusionMode);

            var edited = new FacialProfile(original.SchemaVersion, layers, original.Expressions.ToArray());
            var parsed = RoundTrip(edited);

            Assert.AreEqual(10, parsed.Layers.Span[1].Priority);
        }

        [Test]
        public void EditLayerExclusionMode_RoundTrip_ModeUpdated()
        {
            var original = CreateTestProfile();
            var layers = original.Layers.ToArray();
            layers[0] = new LayerDefinition(layers[0].Name, layers[0].Priority, ExclusionMode.Blend);

            var edited = new FacialProfile(original.SchemaVersion, layers, original.Expressions.ToArray());
            var parsed = RoundTrip(edited);

            Assert.AreEqual(ExclusionMode.Blend, parsed.Layers.Span[0].ExclusionMode);
        }

        [Test]
        public void EditMultipleLayerFields_RoundTrip_AllFieldsUpdated()
        {
            var original = CreateTestProfile();
            var layers = original.Layers.ToArray();
            layers[2] = new LayerDefinition("まばたき", 5, ExclusionMode.Blend);

            var edited = new FacialProfile(original.SchemaVersion, layers, original.Expressions.ToArray());
            var parsed = RoundTrip(edited);

            var layer = parsed.Layers.Span[2];
            Assert.AreEqual("まばたき", layer.Name);
            Assert.AreEqual(5, layer.Priority);
            Assert.AreEqual(ExclusionMode.Blend, layer.ExclusionMode);
        }

        // --- Expression 編集テスト ---

        [Test]
        public void EditExpressionName_RoundTrip_NameUpdated()
        {
            var original = CreateTestProfile();
            var expressions = original.Expressions.ToArray();
            var orig = expressions[0];
            expressions[0] = new Expression(
                orig.Id, "にっこり", orig.Layer, orig.TransitionDuration,
                orig.TransitionCurve, orig.BlendShapeValues.ToArray(), orig.LayerSlots.ToArray());

            var edited = new FacialProfile(original.SchemaVersion, original.Layers.ToArray(), expressions);
            var parsed = RoundTrip(edited);

            Assert.AreEqual("にっこり", parsed.Expressions.Span[0].Name);
        }

        [Test]
        public void EditExpressionLayer_RoundTrip_LayerUpdated()
        {
            var original = CreateTestProfile();
            var expressions = original.Expressions.ToArray();
            var orig = expressions[0];
            expressions[0] = new Expression(
                orig.Id, orig.Name, "lipsync", orig.TransitionDuration,
                orig.TransitionCurve, orig.BlendShapeValues.ToArray(), orig.LayerSlots.ToArray());

            var edited = new FacialProfile(original.SchemaVersion, original.Layers.ToArray(), expressions);
            var parsed = RoundTrip(edited);

            Assert.AreEqual("lipsync", parsed.Expressions.Span[0].Layer);
        }

        [Test]
        public void EditExpressionTransitionDuration_RoundTrip_DurationUpdated()
        {
            var original = CreateTestProfile();
            var expressions = original.Expressions.ToArray();
            var orig = expressions[0];
            expressions[0] = new Expression(
                orig.Id, orig.Name, orig.Layer, 0.5f,
                orig.TransitionCurve, orig.BlendShapeValues.ToArray(), orig.LayerSlots.ToArray());

            var edited = new FacialProfile(original.SchemaVersion, original.Layers.ToArray(), expressions);
            var parsed = RoundTrip(edited);

            Assert.AreEqual(0.5f, parsed.Expressions.Span[0].TransitionDuration, 0.001f);
        }

        [Test]
        public void EditExpression_BlendShapeValuesPreserved()
        {
            var original = CreateTestProfile();
            var expressions = original.Expressions.ToArray();
            var orig = expressions[0];
            // 名前だけ変更し BlendShapeValues は維持
            expressions[0] = new Expression(
                orig.Id, "edited_name", orig.Layer, orig.TransitionDuration,
                orig.TransitionCurve, orig.BlendShapeValues.ToArray(), orig.LayerSlots.ToArray());

            var edited = new FacialProfile(original.SchemaVersion, original.Layers.ToArray(), expressions);
            var parsed = RoundTrip(edited);

            var bsSpan = parsed.Expressions.Span[0].BlendShapeValues.Span;
            Assert.AreEqual(2, bsSpan.Length);
            Assert.AreEqual("mouth_smile", bsSpan[0].Name);
            Assert.AreEqual(0.8f, bsSpan[0].Value, 0.001f);
            Assert.AreEqual("eye_squint", bsSpan[1].Name);
            Assert.AreEqual(0.3f, bsSpan[1].Value, 0.001f);
        }

        [Test]
        public void EditExpression_IdPreserved()
        {
            var original = CreateTestProfile();
            var expressions = original.Expressions.ToArray();
            var orig = expressions[0];
            expressions[0] = new Expression(
                orig.Id, "new_name", "lipsync", 0.75f,
                orig.TransitionCurve, orig.BlendShapeValues.ToArray(), orig.LayerSlots.ToArray());

            var edited = new FacialProfile(original.SchemaVersion, original.Layers.ToArray(), expressions);
            var parsed = RoundTrip(edited);

            Assert.AreEqual("expr-001", parsed.Expressions.Span[0].Id);
        }

        // --- レイヤー + Expression 同時編集テスト ---

        [Test]
        public void EditBothLayerAndExpression_RoundTrip_BothUpdated()
        {
            var original = CreateTestProfile();

            var layers = original.Layers.ToArray();
            layers[0] = new LayerDefinition("表情", 0, ExclusionMode.Blend);

            var expressions = original.Expressions.ToArray();
            var orig = expressions[0];
            expressions[0] = new Expression(
                orig.Id, "笑顔", "表情", 0.3f,
                orig.TransitionCurve, orig.BlendShapeValues.ToArray(), orig.LayerSlots.ToArray());

            var edited = new FacialProfile(original.SchemaVersion, layers, expressions);
            var parsed = RoundTrip(edited);

            Assert.AreEqual("表情", parsed.Layers.Span[0].Name);
            Assert.AreEqual(ExclusionMode.Blend, parsed.Layers.Span[0].ExclusionMode);
            Assert.AreEqual("笑顔", parsed.Expressions.Span[0].Name);
            Assert.AreEqual("表情", parsed.Expressions.Span[0].Layer);
            Assert.AreEqual(0.3f, parsed.Expressions.Span[0].TransitionDuration, 0.001f);
        }

        [Test]
        public void EditProfile_SchemaVersionPreserved()
        {
            var original = CreateTestProfile();
            var layers = original.Layers.ToArray();
            layers[0] = new LayerDefinition("modified", 0, ExclusionMode.Blend);

            var edited = new FacialProfile(original.SchemaVersion, layers, original.Expressions.ToArray());
            var parsed = RoundTrip(edited);

            Assert.AreEqual("1.0", parsed.SchemaVersion);
        }

        [Test]
        public void EditProfile_UnmodifiedLayersPreserved()
        {
            var original = CreateTestProfile();
            var layers = original.Layers.ToArray();
            // レイヤー 0 だけ変更
            layers[0] = new LayerDefinition("changed", 99, ExclusionMode.Blend);

            var edited = new FacialProfile(original.SchemaVersion, layers, original.Expressions.ToArray());
            var parsed = RoundTrip(edited);

            // レイヤー 1, 2 は元のまま
            Assert.AreEqual("lipsync", parsed.Layers.Span[1].Name);
            Assert.AreEqual(1, parsed.Layers.Span[1].Priority);
            Assert.AreEqual(ExclusionMode.Blend, parsed.Layers.Span[1].ExclusionMode);

            Assert.AreEqual("eye", parsed.Layers.Span[2].Name);
            Assert.AreEqual(2, parsed.Layers.Span[2].Priority);
            Assert.AreEqual(ExclusionMode.LastWins, parsed.Layers.Span[2].ExclusionMode);
        }

        [Test]
        public void EditProfile_UnmodifiedExpressionsPreserved()
        {
            var original = CreateTestProfile();
            var expressions = original.Expressions.ToArray();
            var orig = expressions[0];
            // Expression 0 だけ変更
            expressions[0] = new Expression(
                orig.Id, "modified", orig.Layer, 0.9f,
                orig.TransitionCurve, orig.BlendShapeValues.ToArray(), orig.LayerSlots.ToArray());

            var edited = new FacialProfile(original.SchemaVersion, original.Layers.ToArray(), expressions);
            var parsed = RoundTrip(edited);

            // Expression 1 は元のまま
            var expr1 = parsed.Expressions.Span[1];
            Assert.AreEqual("expr-002", expr1.Id);
            Assert.AreEqual("blink", expr1.Name);
            Assert.AreEqual("eye", expr1.Layer);
            Assert.AreEqual(0.1f, expr1.TransitionDuration, 0.001f);
        }

        // --- Expression 数・レイヤー数の整合性テスト ---

        [Test]
        public void EditProfile_RoundTrip_LayerCountMatches()
        {
            var original = CreateTestProfile();
            var layers = original.Layers.ToArray();
            layers[0] = new LayerDefinition("a", 0, ExclusionMode.Blend);

            var edited = new FacialProfile(original.SchemaVersion, layers, original.Expressions.ToArray());
            var parsed = RoundTrip(edited);

            Assert.AreEqual(3, parsed.Layers.Length);
        }

        [Test]
        public void EditProfile_RoundTrip_ExpressionCountMatches()
        {
            var original = CreateTestProfile();
            var expressions = original.Expressions.ToArray();
            var orig = expressions[0];
            expressions[0] = new Expression(
                orig.Id, "changed", orig.Layer, orig.TransitionDuration,
                orig.TransitionCurve, orig.BlendShapeValues.ToArray(), orig.LayerSlots.ToArray());

            var edited = new FacialProfile(original.SchemaVersion, original.Layers.ToArray(), expressions);
            var parsed = RoundTrip(edited);

            Assert.AreEqual(2, parsed.Expressions.Length);
        }
    }
}
