using System.Collections.Generic;
using System.Reflection;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Adapters.Json.Dto;
using NUnit.Framework;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.Json
{
    [TestFixture]
    public class SystemTextJsonParserBaseExpressionTests
    {
        private SystemTextJsonParser _parser;

        [SetUp]
        public void SetUp()
        {
            _parser = new SystemTextJsonParser();
        }

        [Test]
        public void ParseProfileSnapshotV2_BaseExpressionField_PopulatesSnapshotBlendShapes()
        {
            var dto = _parser.ParseProfileSnapshotV2(BuildProfileJsonWithBaseExpression());

            var baseExpression = GetBaseExpression(dto);

            Assert.That(baseExpression.blendShapes, Is.Not.Null);
            Assert.That(baseExpression.blendShapes, Has.Count.EqualTo(2));
            AssertBlendShape(baseExpression.blendShapes[0], "Body", "Brow_Angry", 64.5f);
            AssertBlendShape(baseExpression.blendShapes[1], "Face", "Eye_Narrow", 28.25f);
        }

        [Test]
        public void ParseProfileSnapshotV2_BaseExpressionMissing_CreatesEmptySnapshot()
        {
            var dto = _parser.ParseProfileSnapshotV2(@"{
                ""schemaVersion"": ""1.0"",
                ""layers"": [],
                ""expressions"": [],
                ""rendererPaths"": []
            }");

            var baseExpression = GetBaseExpression(dto);

            Assert.That(baseExpression.blendShapes, Is.Not.Null);
            Assert.That(baseExpression.blendShapes, Is.Empty);
        }

        [Test]
        public void SerializeProfileSnapshot_BaseExpression_EmitsBaseExpressionSchema()
        {
            var dto = CreateProfileSnapshotDto();
            SetBaseExpression(dto, CreateBaseExpressionSnapshot());

            string json = _parser.SerializeProfileSnapshot(dto);

            StringAssert.Contains(@"""baseExpression""", json);
            StringAssert.Contains(@"""blendShapes""", json);
            StringAssert.Contains(@"""Brow_Angry""", json);

            var parsed = _parser.ParseProfileSnapshotV2(json);
            var baseExpression = GetBaseExpression(parsed);

            Assert.That(baseExpression.blendShapes, Has.Count.EqualTo(2));
            AssertBlendShape(baseExpression.blendShapes[0], "Body", "Brow_Angry", 64.5f);
            AssertBlendShape(baseExpression.blendShapes[1], "Face", "Eye_Narrow", 28.25f);
        }

        [Test]
        public void SerializeProfileSnapshot_BaseExpression_DoesNotEmitAnimationClipPath()
        {
            var dto = CreateProfileSnapshotDto();
            SetBaseExpression(dto, CreateBaseExpressionSnapshot());

            string json = _parser.SerializeProfileSnapshot(dto);

            StringAssert.Contains(@"""baseExpression""", json);
            StringAssert.DoesNotContain("animationClip", json);
            StringAssert.DoesNotContain("BaseExpression_RoundTripClip.anim", json);
            StringAssert.DoesNotContain("Assets/BaseExpression_RoundTripClip.anim", json);
        }

        [Test]
        public void ParseProfile_BaseExpressionField_PopulatesProfileBaseExpression()
        {
            var profile = _parser.ParseProfile(BuildProfileJsonWithNormalizedBaseExpression());

            Assert.That(profile.BaseExpression.Length, Is.EqualTo(2),
                "profile.json の baseExpression は FacialProfile まで運ばれる必要がある。");
            Assert.That(profile.BaseExpression.Span[0].RendererPath, Is.EqualTo("Body"));
            Assert.That(profile.BaseExpression.Span[0].Name, Is.EqualTo("Brow_Angry"));
            Assert.That(profile.BaseExpression.Span[0].Value, Is.EqualTo(0.645f).Within(1e-6f));
            Assert.That(profile.BaseExpression.Span[1].Name, Is.EqualTo("Eye_Narrow"));
            Assert.That(profile.BaseExpression.Span[1].Value, Is.EqualTo(0.2825f).Within(1e-6f));
        }

        [Test]
        public void ParseProfile_BaseExpressionMissing_ProfileBaseExpressionIsEmpty()
        {
            var profile = _parser.ParseProfile(@"{
                ""schemaVersion"": ""1.0"",
                ""layers"": [],
                ""expressions"": [],
                ""rendererPaths"": []
            }");

            Assert.That(profile.BaseExpression.Length, Is.EqualTo(0),
                "baseExpression 欠如の既存 profile.json は空 base として読み込まれる（forward compat）。");
        }

        [Test]
        public void SerializeProfile_ProfileWithBaseExpression_RoundTripsBlendShapes()
        {
            var source = _parser.ParseProfile(BuildProfileJsonWithNormalizedBaseExpression());

            string json = _parser.SerializeProfile(source);

            StringAssert.Contains(@"""baseExpression""", json);
            StringAssert.Contains(@"""Brow_Angry""", json);

            var roundTripped = _parser.ParseProfile(json);
            Assert.That(roundTripped.BaseExpression.Length, Is.EqualTo(2));
            Assert.That(roundTripped.BaseExpression.Span[0].Name, Is.EqualTo("Brow_Angry"));
            Assert.That(roundTripped.BaseExpression.Span[0].Value, Is.EqualTo(0.645f).Within(1e-6f));
            Assert.That(roundTripped.BaseExpression.Span[1].Name, Is.EqualTo("Eye_Narrow"));
            Assert.That(roundTripped.BaseExpression.Span[1].Value, Is.EqualTo(0.2825f).Within(1e-6f));
        }

        /// <summary>
        /// ドメイン / JSON の正規化スケール (0..1) でベース表情を含むプロファイル JSON。
        /// </summary>
        private static string BuildProfileJsonWithNormalizedBaseExpression()
        {
            return @"{
                ""schemaVersion"": ""1.0"",
                ""layers"": [],
                ""expressions"": [],
                ""rendererPaths"": [""Body"", ""Face""],
                ""baseExpression"": {
                    ""blendShapes"": [
                        {""rendererPath"": ""Body"", ""name"": ""Brow_Angry"", ""value"": 0.645},
                        {""rendererPath"": ""Face"", ""name"": ""Eye_Narrow"", ""value"": 0.2825}
                    ]
                }
            }";
        }

        private static string BuildProfileJsonWithBaseExpression()
        {
            return @"{
                ""schemaVersion"": ""1.0"",
                ""layers"": [],
                ""expressions"": [],
                ""rendererPaths"": [""Body"", ""Face""],
                ""baseExpression"": {
                    ""blendShapes"": [
                        {""rendererPath"": ""Body"", ""name"": ""Brow_Angry"", ""value"": 64.5},
                        {""rendererPath"": ""Face"", ""name"": ""Eye_Narrow"", ""value"": 28.25}
                    ]
                }
            }";
        }

        private static ProfileSnapshotDto CreateProfileSnapshotDto()
        {
            return new ProfileSnapshotDto
            {
                schemaVersion = SystemTextJsonParser.SchemaVersionV2,
                layers = new List<LayerDefinitionDto>(),
                expressions = new List<ExpressionDto>(),
                rendererPaths = new List<string> { "Body", "Face" },
                gazeConfigs = new List<GazeBindingConfigDto>(),
            };
        }

        private static ExpressionSnapshotDto CreateBaseExpressionSnapshot()
        {
            return new ExpressionSnapshotDto
            {
                blendShapes = new List<BlendShapeSnapshotDto>
                {
                    new BlendShapeSnapshotDto
                    {
                        rendererPath = "Body",
                        name = "Brow_Angry",
                        value = 64.5f,
                    },
                    new BlendShapeSnapshotDto
                    {
                        rendererPath = "Face",
                        name = "Eye_Narrow",
                        value = 28.25f,
                    },
                },
                bones = new List<BoneSnapshotDto>(),
                rendererPaths = new List<string> { "Body", "Face" },
            };
        }

        private static ExpressionSnapshotDto GetBaseExpression(ProfileSnapshotDto dto)
        {
            var value = GetBaseExpressionField().GetValue(dto) as ExpressionSnapshotDto;
            Assert.That(value, Is.Not.Null,
                "ProfileSnapshotDto.baseExpression must be normalized to an empty ExpressionSnapshotDto when the JSON field is missing.");
            return value;
        }

        private static void SetBaseExpression(ProfileSnapshotDto dto, ExpressionSnapshotDto snapshot)
        {
            GetBaseExpressionField().SetValue(dto, snapshot);
        }

        private static FieldInfo GetBaseExpressionField()
        {
            var field = typeof(ProfileSnapshotDto).GetField(
                "baseExpression",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null,
                "ProfileSnapshotDto must expose root baseExpression for profile.json schema round-trip.");
            Assert.That(field.IsPublic, Is.True,
                "ProfileSnapshotDto.baseExpression must be public so JsonUtility can serialize the schema field.");
            Assert.That(field.FieldType, Is.EqualTo(typeof(ExpressionSnapshotDto)),
                "ProfileSnapshotDto.baseExpression must reuse ExpressionSnapshotDto.");
            return field;
        }

        private static void AssertBlendShape(
            BlendShapeSnapshotDto actual,
            string expectedRendererPath,
            string expectedName,
            float expectedValue)
        {
            Assert.That(actual.rendererPath, Is.EqualTo(expectedRendererPath));
            Assert.That(actual.name, Is.EqualTo(expectedName));
            Assert.That(actual.value, Is.EqualTo(expectedValue).Within(1e-6f));
        }
    }
}
