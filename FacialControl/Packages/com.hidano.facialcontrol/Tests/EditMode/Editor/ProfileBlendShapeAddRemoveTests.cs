using System;
using NUnit.Framework;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Editor
{
    /// <summary>
    /// P19-T02: Expression 内 BlendShape の追加・削除後にプロファイルが正しく再構築されるか検証。
    /// BlendShape 追加後の Length 増加、削除後の Length 減少、名前と Weight の保持、
    /// 削除後の順序維持、JSON ラウンドトリップを確認する。
    /// </summary>
    [TestFixture]
    public class ProfileBlendShapeAddRemoveTests
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
        /// 指定 Expression に BlendShape を追加してプロファイルを再構築する
        /// </summary>
        private FacialProfile AddBlendShape(
            FacialProfile originalProfile,
            int expressionIndex,
            string name,
            float weight)
        {
            var expressions = originalProfile.Expressions.ToArray();
            var expr = expressions[expressionIndex];
            var existingBs = expr.BlendShapeValues.ToArray();
            var newBs = new BlendShapeMapping[existingBs.Length + 1];
            Array.Copy(existingBs, newBs, existingBs.Length);
            newBs[existingBs.Length] = new BlendShapeMapping(name, weight);

            expressions[expressionIndex] = new Expression(
                expr.Id, expr.Name, expr.Layer, expr.TransitionDuration,
                expr.TransitionCurve, newBs, expr.LayerSlots.ToArray());

            return new FacialProfile(
                originalProfile.SchemaVersion,
                originalProfile.Layers.ToArray(),
                expressions,
                originalProfile.RendererPaths.ToArray());
        }

        /// <summary>
        /// 指定 Expression から指定インデックスの BlendShape を削除してプロファイルを再構築する
        /// </summary>
        private FacialProfile RemoveBlendShape(
            FacialProfile originalProfile,
            int expressionIndex,
            int blendShapeIndex)
        {
            var expressions = originalProfile.Expressions.ToArray();
            var expr = expressions[expressionIndex];
            var existingBs = expr.BlendShapeValues.ToArray();
            var newBs = new BlendShapeMapping[existingBs.Length - 1];
            int destIdx = 0;
            for (int i = 0; i < existingBs.Length; i++)
            {
                if (i != blendShapeIndex)
                {
                    newBs[destIdx] = existingBs[i];
                    destIdx++;
                }
            }

            expressions[expressionIndex] = new Expression(
                expr.Id, expr.Name, expr.Layer, expr.TransitionDuration,
                expr.TransitionCurve, newBs, expr.LayerSlots.ToArray());

            return new FacialProfile(
                originalProfile.SchemaVersion,
                originalProfile.Layers.ToArray(),
                expressions,
                originalProfile.RendererPaths.ToArray());
        }

        /// <summary>
        /// シリアライズ → 再パースのラウンドトリップを行う
        /// </summary>
        private FacialProfile RoundTrip(FacialProfile profile)
        {
            var json = _parser.SerializeProfile(profile);
            return _parser.ParseProfile(json);
        }

        // --- BlendShape 追加テスト ---

        [Test]
        public void AddBlendShape_LengthIncreased()
        {
            var original = CreateTestProfile();
            var modified = AddBlendShape(original, 0, "brow_raise", 0.6f);

            Assert.AreEqual(4, modified.Expressions.Span[0].BlendShapeValues.Length);
        }

        [Test]
        public void AddBlendShape_NameAndWeightPreserved()
        {
            var original = CreateTestProfile();
            var modified = AddBlendShape(original, 0, "brow_raise", 0.6f);

            var bsSpan = modified.Expressions.Span[0].BlendShapeValues.Span;
            Assert.AreEqual("brow_raise", bsSpan[3].Name);
            Assert.AreEqual(0.6f, bsSpan[3].Value, 0.001f);
        }

        [Test]
        public void AddBlendShape_ExistingBlendShapesPreserved()
        {
            var original = CreateTestProfile();
            var modified = AddBlendShape(original, 0, "brow_raise", 0.6f);

            var bsSpan = modified.Expressions.Span[0].BlendShapeValues.Span;
            Assert.AreEqual("mouth_smile", bsSpan[0].Name);
            Assert.AreEqual(0.8f, bsSpan[0].Value, 0.001f);
            Assert.AreEqual("eye_squint", bsSpan[1].Name);
            Assert.AreEqual(0.3f, bsSpan[1].Value, 0.001f);
            Assert.AreEqual("cheek_puff", bsSpan[2].Name);
            Assert.AreEqual(0.5f, bsSpan[2].Value, 0.001f);
        }

        [Test]
        public void AddBlendShape_RoundTrip_ValuesPersisted()
        {
            var original = CreateTestProfile();
            var modified = AddBlendShape(original, 0, "brow_raise", 0.6f);
            var parsed = RoundTrip(modified);

            var bsSpan = parsed.Expressions.Span[0].BlendShapeValues.Span;
            Assert.AreEqual(4, bsSpan.Length);
            Assert.AreEqual("brow_raise", bsSpan[3].Name);
            Assert.AreEqual(0.6f, bsSpan[3].Value, 0.001f);
        }

        [Test]
        public void AddBlendShape_OtherExpressionUnchanged()
        {
            var original = CreateTestProfile();
            var modified = AddBlendShape(original, 0, "brow_raise", 0.6f);

            var expr1 = modified.Expressions.Span[1];
            Assert.AreEqual(2, expr1.BlendShapeValues.Length);
            Assert.AreEqual("eye_close_L", expr1.BlendShapeValues.Span[0].Name);
            Assert.AreEqual("eye_close_R", expr1.BlendShapeValues.Span[1].Name);
        }

        [Test]
        public void AddBlendShape_WeightZero_Preserved()
        {
            var original = CreateTestProfile();
            var modified = AddBlendShape(original, 0, "nose_wrinkle", 0f);

            var bsSpan = modified.Expressions.Span[0].BlendShapeValues.Span;
            Assert.AreEqual("nose_wrinkle", bsSpan[3].Name);
            Assert.AreEqual(0f, bsSpan[3].Value, 0.001f);
        }

        // --- BlendShape 削除テスト ---

        [Test]
        public void RemoveBlendShape_LengthDecreased()
        {
            var original = CreateTestProfile();
            var modified = RemoveBlendShape(original, 0, 1);

            Assert.AreEqual(2, modified.Expressions.Span[0].BlendShapeValues.Length);
        }

        [Test]
        public void RemoveBlendShape_RemainingOrderAndValuesPreserved()
        {
            var original = CreateTestProfile();
            // 中間（index=1: eye_squint）を削除
            var modified = RemoveBlendShape(original, 0, 1);

            var bsSpan = modified.Expressions.Span[0].BlendShapeValues.Span;
            Assert.AreEqual("mouth_smile", bsSpan[0].Name);
            Assert.AreEqual(0.8f, bsSpan[0].Value, 0.001f);
            Assert.AreEqual("cheek_puff", bsSpan[1].Name);
            Assert.AreEqual(0.5f, bsSpan[1].Value, 0.001f);
        }

        [Test]
        public void RemoveBlendShape_FirstElement_OrderPreserved()
        {
            var original = CreateTestProfile();
            var modified = RemoveBlendShape(original, 0, 0);

            var bsSpan = modified.Expressions.Span[0].BlendShapeValues.Span;
            Assert.AreEqual(2, bsSpan.Length);
            Assert.AreEqual("eye_squint", bsSpan[0].Name);
            Assert.AreEqual("cheek_puff", bsSpan[1].Name);
        }

        [Test]
        public void RemoveBlendShape_LastElement_OrderPreserved()
        {
            var original = CreateTestProfile();
            var modified = RemoveBlendShape(original, 0, 2);

            var bsSpan = modified.Expressions.Span[0].BlendShapeValues.Span;
            Assert.AreEqual(2, bsSpan.Length);
            Assert.AreEqual("mouth_smile", bsSpan[0].Name);
            Assert.AreEqual("eye_squint", bsSpan[1].Name);
        }

        [Test]
        public void RemoveBlendShape_RoundTrip_ValuesPersisted()
        {
            var original = CreateTestProfile();
            var modified = RemoveBlendShape(original, 0, 1);
            var parsed = RoundTrip(modified);

            var bsSpan = parsed.Expressions.Span[0].BlendShapeValues.Span;
            Assert.AreEqual(2, bsSpan.Length);
            Assert.AreEqual("mouth_smile", bsSpan[0].Name);
            Assert.AreEqual(0.8f, bsSpan[0].Value, 0.001f);
            Assert.AreEqual("cheek_puff", bsSpan[1].Name);
            Assert.AreEqual(0.5f, bsSpan[1].Value, 0.001f);
        }

        [Test]
        public void RemoveBlendShape_OtherExpressionUnchanged()
        {
            var original = CreateTestProfile();
            var modified = RemoveBlendShape(original, 0, 0);

            var expr1 = modified.Expressions.Span[1];
            Assert.AreEqual(2, expr1.BlendShapeValues.Length);
            Assert.AreEqual(1.0f, expr1.BlendShapeValues.Span[0].Value, 0.001f);
            Assert.AreEqual(1.0f, expr1.BlendShapeValues.Span[1].Value, 0.001f);
        }

        // --- 追加 + 削除の複合テスト ---

        [Test]
        public void AddThenRemove_RestoredToOriginalLength()
        {
            var original = CreateTestProfile();
            var afterAdd = AddBlendShape(original, 0, "temp_bs", 0.5f);
            // 追加した BlendShape（末尾: index=3）を削除
            var afterRemove = RemoveBlendShape(afterAdd, 0, 3);

            Assert.AreEqual(3, afterRemove.Expressions.Span[0].BlendShapeValues.Length);
        }

        [Test]
        public void RemoveThenAdd_NewBlendShapeAtEnd()
        {
            var original = CreateTestProfile();
            var afterRemove = RemoveBlendShape(original, 0, 0);
            var afterAdd = AddBlendShape(afterRemove, 0, "new_bs", 0.7f);

            var bsSpan = afterAdd.Expressions.Span[0].BlendShapeValues.Span;
            Assert.AreEqual(3, bsSpan.Length);
            Assert.AreEqual("eye_squint", bsSpan[0].Name);
            Assert.AreEqual("cheek_puff", bsSpan[1].Name);
            Assert.AreEqual("new_bs", bsSpan[2].Name);
            Assert.AreEqual(0.7f, bsSpan[2].Value, 0.001f);
        }

        [Test]
        public void AddThenRemove_RoundTrip_Consistent()
        {
            var original = CreateTestProfile();
            var afterAdd = AddBlendShape(original, 0, "temp_bs", 0.5f);
            var afterRemove = RemoveBlendShape(afterAdd, 0, 1);
            var parsed = RoundTrip(afterRemove);

            var bsSpan = parsed.Expressions.Span[0].BlendShapeValues.Span;
            Assert.AreEqual(3, bsSpan.Length);
            Assert.AreEqual("mouth_smile", bsSpan[0].Name);
            Assert.AreEqual("cheek_puff", bsSpan[1].Name);
            Assert.AreEqual("temp_bs", bsSpan[2].Name);
        }

        // --- Expression メタデータ保持テスト ---

        [Test]
        public void AddBlendShape_ExpressionMetadataPreserved()
        {
            var original = CreateTestProfile();
            var modified = AddBlendShape(original, 0, "brow_raise", 0.6f);

            var expr = modified.Expressions.Span[0];
            Assert.AreEqual("expr-001", expr.Id);
            Assert.AreEqual("smile", expr.Name);
            Assert.AreEqual("emotion", expr.Layer);
            Assert.AreEqual(0.25f, expr.TransitionDuration, 0.001f);
        }

        [Test]
        public void RemoveBlendShape_ExpressionMetadataPreserved()
        {
            var original = CreateTestProfile();
            var modified = RemoveBlendShape(original, 0, 0);

            var expr = modified.Expressions.Span[0];
            Assert.AreEqual("expr-001", expr.Id);
            Assert.AreEqual("smile", expr.Name);
            Assert.AreEqual("emotion", expr.Layer);
            Assert.AreEqual(0.25f, expr.TransitionDuration, 0.001f);
        }
    }
}
