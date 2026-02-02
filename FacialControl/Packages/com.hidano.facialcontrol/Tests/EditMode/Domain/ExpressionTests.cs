using System;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    [TestFixture]
    public class ExpressionTests
    {
        // --- 正常系 ---

        [Test]
        public void Constructor_ValidParameters_CreatesInstance()
        {
            var blendShapes = new[]
            {
                new BlendShapeMapping("Fcl_MTH_A", 0.5f)
            };
            var layerSlots = new[]
            {
                new LayerSlot("lipsync", new[] { new BlendShapeMapping("Fcl_MTH_O", 0.3f) })
            };
            var curve = TransitionCurve.Linear;

            var expression = new Expression(
                "test-id",
                "smile",
                "emotion",
                0.25f,
                curve,
                blendShapes,
                layerSlots);

            Assert.AreEqual("test-id", expression.Id);
            Assert.AreEqual("smile", expression.Name);
            Assert.AreEqual("emotion", expression.Layer);
            Assert.AreEqual(0.25f, expression.TransitionDuration);
            Assert.AreEqual(TransitionCurveType.Linear, expression.TransitionCurve.Type);
            Assert.AreEqual(1, expression.BlendShapeValues.Length);
            Assert.AreEqual("Fcl_MTH_A", expression.BlendShapeValues.Span[0].Name);
            Assert.AreEqual(1, expression.LayerSlots.Length);
            Assert.AreEqual("lipsync", expression.LayerSlots.Span[0].Layer);
        }

        [Test]
        public void Constructor_DefaultTransitionDuration_Is025()
        {
            var expression = new Expression(
                "id-1",
                "neutral",
                "emotion");

            Assert.AreEqual(0.25f, expression.TransitionDuration);
        }

        [Test]
        public void Constructor_DefaultTransitionCurve_IsLinear()
        {
            var expression = new Expression(
                "id-1",
                "neutral",
                "emotion");

            Assert.AreEqual(TransitionCurveType.Linear, expression.TransitionCurve.Type);
        }

        [Test]
        public void Constructor_DefaultBlendShapeValues_IsEmpty()
        {
            var expression = new Expression(
                "id-1",
                "neutral",
                "emotion");

            Assert.AreEqual(0, expression.BlendShapeValues.Length);
        }

        [Test]
        public void Constructor_DefaultLayerSlots_IsEmpty()
        {
            var expression = new Expression(
                "id-1",
                "neutral",
                "emotion");

            Assert.AreEqual(0, expression.LayerSlots.Length);
        }

        [Test]
        public void Constructor_TransitionDurationZero_IsValid()
        {
            var expression = new Expression(
                "id-1",
                "instant",
                "emotion",
                0f);

            Assert.AreEqual(0f, expression.TransitionDuration);
        }

        [Test]
        public void Constructor_TransitionDurationOne_IsValid()
        {
            var expression = new Expression(
                "id-1",
                "slow",
                "emotion",
                1f);

            Assert.AreEqual(1f, expression.TransitionDuration);
        }

        [Test]
        public void Constructor_MultipleBlendShapeValues_CreatesInstance()
        {
            var blendShapes = new[]
            {
                new BlendShapeMapping("Fcl_EYE_Close_L", 1.0f),
                new BlendShapeMapping("Fcl_EYE_Close_R", 1.0f),
                new BlendShapeMapping("Fcl_BRW_Down", 0.5f)
            };

            var expression = new Expression(
                "id-2",
                "wink",
                "emotion",
                0.1f,
                TransitionCurve.Linear,
                blendShapes);

            Assert.AreEqual(3, expression.BlendShapeValues.Length);
            Assert.AreEqual("Fcl_EYE_Close_L", expression.BlendShapeValues.Span[0].Name);
            Assert.AreEqual("Fcl_EYE_Close_R", expression.BlendShapeValues.Span[1].Name);
            Assert.AreEqual("Fcl_BRW_Down", expression.BlendShapeValues.Span[2].Name);
        }

        [Test]
        public void Constructor_MultipleLayerSlots_CreatesInstance()
        {
            var slots = new[]
            {
                new LayerSlot("lipsync", new[] { new BlendShapeMapping("Fcl_MTH_A", 0.3f) }),
                new LayerSlot("eye", new[] { new BlendShapeMapping("Fcl_EYE_Close", 1.0f) })
            };

            var expression = new Expression(
                "id-3",
                "angry",
                "emotion",
                0.25f,
                TransitionCurve.Linear,
                null,
                slots);

            Assert.AreEqual(2, expression.LayerSlots.Length);
            Assert.AreEqual("lipsync", expression.LayerSlots.Span[0].Layer);
            Assert.AreEqual("eye", expression.LayerSlots.Span[1].Layer);
        }

        [Test]
        public void Constructor_CustomCurve_CreatesInstance()
        {
            var keys = new[]
            {
                new CurveKeyFrame(0f, 0f, 0f, 1f),
                new CurveKeyFrame(1f, 1f, 1f, 0f)
            };
            var curve = new TransitionCurve(TransitionCurveType.Custom, keys);

            var expression = new Expression(
                "id-4",
                "custom-transition",
                "emotion",
                0.5f,
                curve);

            Assert.AreEqual(TransitionCurveType.Custom, expression.TransitionCurve.Type);
            Assert.AreEqual(2, expression.TransitionCurve.Keys.Length);
        }

        [Test]
        public void Constructor_JapaneseName_CreatesInstance()
        {
            var expression = new Expression(
                "id-jp",
                "笑顔",
                "感情");

            Assert.AreEqual("笑顔", expression.Name);
            Assert.AreEqual("感情", expression.Layer);
        }

        [Test]
        public void Constructor_SpecialCharacterName_CreatesInstance()
        {
            var expression = new Expression(
                "id-spec",
                "expression-01_test",
                "layer-01_test");

            Assert.AreEqual("expression-01_test", expression.Name);
            Assert.AreEqual("layer-01_test", expression.Layer);
        }

        // --- TransitionDuration クランプ ---

        [Test]
        public void Constructor_TransitionDurationNegative_ClampsToZero()
        {
            var expression = new Expression(
                "id-clamp",
                "test",
                "emotion",
                -0.5f);

            Assert.AreEqual(0f, expression.TransitionDuration);
        }

        [Test]
        public void Constructor_TransitionDurationOverOne_ClampsToOne()
        {
            var expression = new Expression(
                "id-clamp2",
                "test",
                "emotion",
                2.0f);

            Assert.AreEqual(1f, expression.TransitionDuration);
        }

        // --- バリデーション ---

        [Test]
        public void Constructor_NullId_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new Expression(null, "smile", "emotion"));
        }

        [Test]
        public void Constructor_EmptyId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                new Expression("", "smile", "emotion"));
        }

        [Test]
        public void Constructor_WhitespaceId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                new Expression("   ", "smile", "emotion"));
        }

        [Test]
        public void Constructor_NullName_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new Expression("id-1", null, "emotion"));
        }

        [Test]
        public void Constructor_EmptyName_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                new Expression("id-1", "", "emotion"));
        }

        [Test]
        public void Constructor_WhitespaceName_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                new Expression("id-1", "   ", "emotion"));
        }

        [Test]
        public void Constructor_NullLayer_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new Expression("id-1", "smile", null));
        }

        [Test]
        public void Constructor_EmptyLayer_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                new Expression("id-1", "smile", ""));
        }

        [Test]
        public void Constructor_WhitespaceLayer_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                new Expression("id-1", "smile", "   "));
        }

        // --- 防御的コピー ---

        [Test]
        public void BlendShapeValues_IsDefensiveCopy_OriginalArrayModificationDoesNotAffect()
        {
            var values = new[]
            {
                new BlendShapeMapping("Fcl_MTH_A", 0.5f)
            };

            var expression = new Expression(
                "id-def",
                "test",
                "emotion",
                0.25f,
                TransitionCurve.Linear,
                values);

            // 元配列を変更しても Expression の値は変わらない
            values[0] = new BlendShapeMapping("modified", 1.0f);

            Assert.AreEqual("Fcl_MTH_A", expression.BlendShapeValues.Span[0].Name);
            Assert.AreEqual(0.5f, expression.BlendShapeValues.Span[0].Value);
        }

        [Test]
        public void LayerSlots_IsDefensiveCopy_OriginalArrayModificationDoesNotAffect()
        {
            var slots = new[]
            {
                new LayerSlot("lipsync", new[] { new BlendShapeMapping("test", 0.5f) })
            };

            var expression = new Expression(
                "id-def2",
                "test",
                "emotion",
                0.25f,
                TransitionCurve.Linear,
                null,
                slots);

            // 元配列を変更しても Expression の値は変わらない
            slots[0] = new LayerSlot("modified", new[] { new BlendShapeMapping("other", 1.0f) });

            Assert.AreEqual("lipsync", expression.LayerSlots.Span[0].Layer);
        }
    }
}
