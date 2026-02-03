using NUnit.Framework;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    /// <summary>
    /// P13-01: デフォルトプロファイル JSON テンプレート（Templates/default_profile.json）のパーステスト。
    /// テンプレートが SystemTextJsonParser で正しく読み込めること、
    /// 技術仕様書 §17 準拠の構成であることを検証する。
    /// </summary>
    [TestFixture]
    public class DefaultProfileTemplateTests
    {
        private SystemTextJsonParser _parser;
        private FacialProfile _profile;

        /// <summary>
        /// Templates/default_profile.json の内容をインラインで定義。
        /// テンプレートファイルと同一の内容を保持する。
        /// </summary>
        private const string DefaultProfileJson = @"{
    ""schemaVersion"": ""1.0"",
    ""layers"": [
        {""name"": ""emotion"", ""priority"": 0, ""exclusionMode"": ""lastWins""},
        {""name"": ""lipsync"", ""priority"": 1, ""exclusionMode"": ""blend""},
        {""name"": ""eye"", ""priority"": 2, ""exclusionMode"": ""lastWins""}
    ],
    ""expressions"": [
        {
            ""id"": ""00000000-0000-0000-0000-000000000001"",
            ""name"": ""default"",
            ""layer"": ""emotion"",
            ""transitionDuration"": 0.25,
            ""transitionCurve"": {
                ""type"": ""linear""
            },
            ""blendShapeValues"": [],
            ""layerSlots"": []
        },
        {
            ""id"": ""00000000-0000-0000-0000-000000000002"",
            ""name"": ""blink"",
            ""layer"": ""eye"",
            ""transitionDuration"": 0.08,
            ""transitionCurve"": {
                ""type"": ""linear""
            },
            ""blendShapeValues"": [],
            ""layerSlots"": []
        },
        {
            ""id"": ""00000000-0000-0000-0000-000000000003"",
            ""name"": ""gaze_follow"",
            ""layer"": ""eye"",
            ""transitionDuration"": 0.25,
            ""transitionCurve"": {
                ""type"": ""linear""
            },
            ""blendShapeValues"": [],
            ""layerSlots"": []
        },
        {
            ""id"": ""00000000-0000-0000-0000-000000000004"",
            ""name"": ""gaze_camera"",
            ""layer"": ""eye"",
            ""transitionDuration"": 0.25,
            ""transitionCurve"": {
                ""type"": ""linear""
            },
            ""blendShapeValues"": [],
            ""layerSlots"": []
        }
    ]
}";

        [SetUp]
        public void SetUp()
        {
            _parser = new SystemTextJsonParser();
            _profile = _parser.ParseProfile(DefaultProfileJson);
        }

        // ================================================================
        // パース成功テスト
        // ================================================================

        [Test]
        public void ParseProfile_DefaultTemplate_ReturnsValidProfile()
        {
            Assert.AreEqual("1.0", _profile.SchemaVersion);
        }

        // ================================================================
        // レイヤー構成テスト
        // ================================================================

        [Test]
        public void ParseProfile_DefaultTemplate_HasThreeLayers()
        {
            Assert.AreEqual(3, _profile.Layers.Length);
        }

        [Test]
        public void ParseProfile_DefaultTemplate_EmotionLayerCorrect()
        {
            var layer = _profile.Layers.Span[0];

            Assert.AreEqual("emotion", layer.Name);
            Assert.AreEqual(0, layer.Priority);
            Assert.AreEqual(ExclusionMode.LastWins, layer.ExclusionMode);
        }

        [Test]
        public void ParseProfile_DefaultTemplate_LipsyncLayerCorrect()
        {
            var layer = _profile.Layers.Span[1];

            Assert.AreEqual("lipsync", layer.Name);
            Assert.AreEqual(1, layer.Priority);
            Assert.AreEqual(ExclusionMode.Blend, layer.ExclusionMode);
        }

        [Test]
        public void ParseProfile_DefaultTemplate_EyeLayerCorrect()
        {
            var layer = _profile.Layers.Span[2];

            Assert.AreEqual("eye", layer.Name);
            Assert.AreEqual(2, layer.Priority);
            Assert.AreEqual(ExclusionMode.LastWins, layer.ExclusionMode);
        }

        // ================================================================
        // Expression テスト（技術仕様書 §17 準拠）
        // ================================================================

        [Test]
        public void ParseProfile_DefaultTemplate_HasFourExpressions()
        {
            Assert.AreEqual(4, _profile.Expressions.Length);
        }

        [Test]
        public void ParseProfile_DefaultTemplate_DefaultExpressionCorrect()
        {
            var expr = _profile.Expressions.Span[0];

            Assert.AreEqual("00000000-0000-0000-0000-000000000001", expr.Id);
            Assert.AreEqual("default", expr.Name);
            Assert.AreEqual("emotion", expr.Layer);
            Assert.AreEqual(0.25f, expr.TransitionDuration, 0.001f);
            Assert.AreEqual(TransitionCurveType.Linear, expr.TransitionCurve.Type);
            Assert.AreEqual(0, expr.BlendShapeValues.Length);
            Assert.AreEqual(0, expr.LayerSlots.Length);
        }

        [Test]
        public void ParseProfile_DefaultTemplate_BlinkExpressionCorrect()
        {
            var expr = _profile.Expressions.Span[1];

            Assert.AreEqual("00000000-0000-0000-0000-000000000002", expr.Id);
            Assert.AreEqual("blink", expr.Name);
            Assert.AreEqual("eye", expr.Layer);
            Assert.AreEqual(0.08f, expr.TransitionDuration, 0.001f);
            Assert.AreEqual(TransitionCurveType.Linear, expr.TransitionCurve.Type);
            Assert.AreEqual(0, expr.BlendShapeValues.Length);
            Assert.AreEqual(0, expr.LayerSlots.Length);
        }

        [Test]
        public void ParseProfile_DefaultTemplate_GazeFollowExpressionCorrect()
        {
            var expr = _profile.Expressions.Span[2];

            Assert.AreEqual("00000000-0000-0000-0000-000000000003", expr.Id);
            Assert.AreEqual("gaze_follow", expr.Name);
            Assert.AreEqual("eye", expr.Layer);
            Assert.AreEqual(0.25f, expr.TransitionDuration, 0.001f);
            Assert.AreEqual(TransitionCurveType.Linear, expr.TransitionCurve.Type);
            Assert.AreEqual(0, expr.BlendShapeValues.Length);
            Assert.AreEqual(0, expr.LayerSlots.Length);
        }

        [Test]
        public void ParseProfile_DefaultTemplate_GazeCameraExpressionCorrect()
        {
            var expr = _profile.Expressions.Span[3];

            Assert.AreEqual("00000000-0000-0000-0000-000000000004", expr.Id);
            Assert.AreEqual("gaze_camera", expr.Name);
            Assert.AreEqual("eye", expr.Layer);
            Assert.AreEqual(0.25f, expr.TransitionDuration, 0.001f);
            Assert.AreEqual(TransitionCurveType.Linear, expr.TransitionCurve.Type);
            Assert.AreEqual(0, expr.BlendShapeValues.Length);
            Assert.AreEqual(0, expr.LayerSlots.Length);
        }

        // ================================================================
        // レイヤー参照の整合性テスト
        // ================================================================

        [Test]
        public void ParseProfile_DefaultTemplate_AllLayerReferencesValid()
        {
            var invalidRefs = _profile.ValidateLayerReferences();

            Assert.AreEqual(0, invalidRefs.Count);
        }

        [Test]
        public void ParseProfile_DefaultTemplate_EmotionLayerHasOneExpression()
        {
            var exprs = _profile.GetExpressionsByLayer("emotion");

            Assert.AreEqual(1, exprs.Length);
            Assert.AreEqual("default", exprs.Span[0].Name);
        }

        [Test]
        public void ParseProfile_DefaultTemplate_EyeLayerHasThreeExpressions()
        {
            var exprs = _profile.GetExpressionsByLayer("eye");

            Assert.AreEqual(3, exprs.Length);
        }

        // ================================================================
        // シリアライズ往復テスト
        // ================================================================

        [Test]
        public void SerializeProfile_DefaultTemplate_RoundTrip_PreservesData()
        {
            var serialized = _parser.SerializeProfile(_profile);
            var reparsed = _parser.ParseProfile(serialized);

            Assert.AreEqual(_profile.SchemaVersion, reparsed.SchemaVersion);
            Assert.AreEqual(_profile.Layers.Length, reparsed.Layers.Length);
            Assert.AreEqual(_profile.Expressions.Length, reparsed.Expressions.Length);

            for (int i = 0; i < _profile.Expressions.Length; i++)
            {
                var original = _profile.Expressions.Span[i];
                var restored = reparsed.Expressions.Span[i];

                Assert.AreEqual(original.Id, restored.Id);
                Assert.AreEqual(original.Name, restored.Name);
                Assert.AreEqual(original.Layer, restored.Layer);
                Assert.AreEqual(original.TransitionDuration, restored.TransitionDuration, 0.001f);
                Assert.AreEqual(original.TransitionCurve.Type, restored.TransitionCurve.Type);
            }
        }
    }
}
