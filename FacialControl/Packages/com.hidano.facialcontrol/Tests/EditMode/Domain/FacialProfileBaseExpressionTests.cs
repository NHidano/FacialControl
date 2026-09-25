using System;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    /// <summary>
    /// <see cref="FacialProfile.BaseExpression"/> の保持契約を検証する。
    /// ベース表情は「どのレイヤーも contribute しない BlendShape index に残す初期値」であり、
    /// JSON / SO からランタイム合成パイプラインへ運ぶ担体を Domain 層が持つ必要がある。
    /// </summary>
    [TestFixture]
    public class FacialProfileBaseExpressionTests
    {
        private static LayerDefinition[] CreateLayers()
        {
            return new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
            };
        }

        [Test]
        public void Constructor_BaseExpressionSnapshots_ExposesBaseExpression()
        {
            var baseExpression = new[]
            {
                new BlendShapeSnapshot("Body", "Brow_Angry", 0.645f),
                new BlendShapeSnapshot("Face", "Eye_Narrow", 0.2825f),
            };

            var profile = new FacialProfile(
                "1.0",
                CreateLayers(),
                baseExpression: baseExpression);

            Assert.That(profile.BaseExpression.Length, Is.EqualTo(2));
            Assert.That(profile.BaseExpression.Span[0].Name, Is.EqualTo("Brow_Angry"));
            Assert.That(profile.BaseExpression.Span[0].Value, Is.EqualTo(0.645f).Within(1e-6f));
            Assert.That(profile.BaseExpression.Span[1].Name, Is.EqualTo("Eye_Narrow"));
            Assert.That(profile.BaseExpression.Span[1].Value, Is.EqualTo(0.2825f).Within(1e-6f));
        }

        [Test]
        public void Constructor_NullBaseExpression_ExposesEmptyBaseExpression()
        {
            var profile = new FacialProfile("1.0", CreateLayers());

            Assert.That(profile.BaseExpression.Length, Is.EqualTo(0));
        }

        [Test]
        public void Constructor_BaseExpression_CopiesDefensively()
        {
            var baseExpression = new[]
            {
                new BlendShapeSnapshot("Body", "Brow_Angry", 0.5f),
            };

            var profile = new FacialProfile(
                "1.0",
                CreateLayers(),
                baseExpression: baseExpression);

            // 呼出側の配列を書き換えてもプロファイル側は不変であること。
            baseExpression[0] = new BlendShapeSnapshot("Body", "Mutated", 1f);

            Assert.That(profile.BaseExpression.Span[0].Name, Is.EqualTo("Brow_Angry"));
            Assert.That(profile.BaseExpression.Span[0].Value, Is.EqualTo(0.5f).Within(1e-6f));
        }

        [Test]
        public void Constructor_EmptyBaseExpression_ExposesEmptyBaseExpression()
        {
            var profile = new FacialProfile(
                "1.0",
                CreateLayers(),
                baseExpression: Array.Empty<BlendShapeSnapshot>());

            Assert.That(profile.BaseExpression.Length, Is.EqualTo(0));
        }
    }
}
