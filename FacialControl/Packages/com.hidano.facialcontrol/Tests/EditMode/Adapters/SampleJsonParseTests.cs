using NUnit.Framework;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    /// <summary>
    /// P05-T03: 技術仕様書 §13.7 / §13.8 のサンプル JSON を使ったパーステスト。
    /// SystemTextJsonParser が仕様書のサンプル JSON を正しくパース・シリアライズできることを検証する。
    /// </summary>
    [TestFixture]
    public class SampleJsonParseTests
    {
        private SystemTextJsonParser _parser;

        [SetUp]
        public void SetUp()
        {
            _parser = new SystemTextJsonParser();
        }

        // ================================================================
        // プロファイル JSON パーステスト（§13.7）
        // ================================================================

        [Test]
        public void ParseProfile_SampleJson_ReturnsValidProfile()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);

            Assert.IsNotNull(profile);
            Assert.AreEqual("1.0", profile.SchemaVersion);
        }

        [Test]
        public void ParseProfile_SampleJson_HasThreeLayers()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);

            Assert.AreEqual(3, profile.Layers.Length);
        }

        [Test]
        public void ParseProfile_SampleJson_EmotionLayerCorrect()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);
            var layer = profile.Layers.Span[0];

            Assert.AreEqual("emotion", layer.Name);
            Assert.AreEqual(0, layer.Priority);
            Assert.AreEqual(ExclusionMode.LastWins, layer.ExclusionMode);
        }

        [Test]
        public void ParseProfile_SampleJson_LipsyncLayerCorrect()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);
            var layer = profile.Layers.Span[1];

            Assert.AreEqual("lipsync", layer.Name);
            Assert.AreEqual(1, layer.Priority);
            Assert.AreEqual(ExclusionMode.Blend, layer.ExclusionMode);
        }

        [Test]
        public void ParseProfile_SampleJson_EyeLayerCorrect()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);
            var layer = profile.Layers.Span[2];

            Assert.AreEqual("eye", layer.Name);
            Assert.AreEqual(2, layer.Priority);
            Assert.AreEqual(ExclusionMode.LastWins, layer.ExclusionMode);
        }

        [Test]
        public void ParseProfile_SampleJson_HasThreeExpressions()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);

            Assert.AreEqual(3, profile.Expressions.Length);
        }

        [Test]
        public void ParseProfile_SampleJson_SmileExpressionCorrect()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);
            var expr = profile.Expressions.Span[0];

            Assert.AreEqual("550e8400-e29b-41d4-a716-446655440000", expr.Id);
            Assert.AreEqual("笑顔", expr.Name);
            Assert.AreEqual("emotion", expr.Layer);
            Assert.AreEqual(0.25f, expr.TransitionDuration, 0.001f);
            Assert.AreEqual(TransitionCurveType.EaseInOut, expr.TransitionCurve.Type);
        }

        [Test]
        public void ParseProfile_SampleJson_SmileBlendShapesCorrect()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);
            var expr = profile.Expressions.Span[0];
            var bs = expr.BlendShapeValues;

            Assert.AreEqual(3, bs.Length);

            Assert.AreEqual("Fcl_ALL_Joy", bs.Span[0].Name);
            Assert.AreEqual(1.0f, bs.Span[0].Value, 0.001f);
            Assert.IsNull(bs.Span[0].Renderer);

            Assert.AreEqual("Fcl_EYE_Joy", bs.Span[1].Name);
            Assert.AreEqual(0.8f, bs.Span[1].Value, 0.001f);
            Assert.IsNull(bs.Span[1].Renderer);

            Assert.AreEqual("Fcl_EYE_Joy_R", bs.Span[2].Name);
            Assert.AreEqual(0.6f, bs.Span[2].Value, 0.001f);
            Assert.AreEqual("Face", bs.Span[2].Renderer);
        }

        [Test]
        public void ParseProfile_SampleJson_SmileLayerSlotsCorrect()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);
            var expr = profile.Expressions.Span[0];
            var slots = expr.LayerSlots;

            Assert.AreEqual(1, slots.Length);
            Assert.AreEqual("lipsync", slots.Span[0].Layer);

            var slotBs = slots.Span[0].BlendShapeValues;
            Assert.AreEqual(1, slotBs.Length);
            Assert.AreEqual("Fcl_MTH_A", slotBs.Span[0].Name);
            Assert.AreEqual(0.5f, slotBs.Span[0].Value, 0.001f);
        }

        [Test]
        public void ParseProfile_SampleJson_AngryExpressionCorrect()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);
            var expr = profile.Expressions.Span[1];

            Assert.AreEqual("661f9511-f30c-52e5-b827-557766551111", expr.Id);
            Assert.AreEqual("怒り", expr.Name);
            Assert.AreEqual("emotion", expr.Layer);
            Assert.AreEqual(0.15f, expr.TransitionDuration, 0.001f);
            Assert.AreEqual(TransitionCurveType.Linear, expr.TransitionCurve.Type);
        }

        [Test]
        public void ParseProfile_SampleJson_AngryBlendShapesCorrect()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);
            var expr = profile.Expressions.Span[1];
            var bs = expr.BlendShapeValues;

            Assert.AreEqual(2, bs.Length);
            Assert.AreEqual("Fcl_ALL_Angry", bs.Span[0].Name);
            Assert.AreEqual(1.0f, bs.Span[0].Value, 0.001f);
            Assert.AreEqual("Fcl_BRW_Angry", bs.Span[1].Name);
            Assert.AreEqual(0.9f, bs.Span[1].Value, 0.001f);
        }

        [Test]
        public void ParseProfile_SampleJson_AngryHasEmptyLayerSlots()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);
            var expr = profile.Expressions.Span[1];

            Assert.AreEqual(0, expr.LayerSlots.Length);
        }

        [Test]
        public void ParseProfile_SampleJson_BlinkExpressionCorrect()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);
            var expr = profile.Expressions.Span[2];

            Assert.AreEqual("772a0622-a41d-63f6-c938-668877662222", expr.Id);
            Assert.AreEqual("まばたき", expr.Name);
            Assert.AreEqual("eye", expr.Layer);
            Assert.AreEqual(0.08f, expr.TransitionDuration, 0.001f);
            Assert.AreEqual(TransitionCurveType.Linear, expr.TransitionCurve.Type);
        }

        [Test]
        public void ParseProfile_SampleJson_BlinkBlendShapesCorrect()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);
            var expr = profile.Expressions.Span[2];
            var bs = expr.BlendShapeValues;

            Assert.AreEqual(1, bs.Length);
            Assert.AreEqual("Fcl_EYE_Close", bs.Span[0].Name);
            Assert.AreEqual(1.0f, bs.Span[0].Value, 0.001f);
        }

        [Test]
        public void ParseProfile_SampleJson_LayerReferencesValid()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);
            var invalidRefs = profile.ValidateLayerReferences();

            Assert.AreEqual(0, invalidRefs.Count);
        }

        [Test]
        public void ParseProfile_SampleJson_FindExpressionById()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);

            var smile = profile.FindExpressionById("550e8400-e29b-41d4-a716-446655440000");
            Assert.IsNotNull(smile);
            Assert.AreEqual("笑顔", smile.Value.Name);

            var angry = profile.FindExpressionById("661f9511-f30c-52e5-b827-557766551111");
            Assert.IsNotNull(angry);
            Assert.AreEqual("怒り", angry.Value.Name);

            var blink = profile.FindExpressionById("772a0622-a41d-63f6-c938-668877662222");
            Assert.IsNotNull(blink);
            Assert.AreEqual("まばたき", blink.Value.Name);
        }

        [Test]
        public void ParseProfile_SampleJson_GetExpressionsByLayer()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);

            var emotionExprs = profile.GetExpressionsByLayer("emotion");
            Assert.AreEqual(2, emotionExprs.Length);

            var eyeExprs = profile.GetExpressionsByLayer("eye");
            Assert.AreEqual(1, eyeExprs.Length);

            var lipsyncExprs = profile.GetExpressionsByLayer("lipsync");
            Assert.AreEqual(0, lipsyncExprs.Length);
        }

        // ================================================================
        // プロファイル JSON シリアライズ往復テスト
        // ================================================================

        [Test]
        public void SerializeProfile_SampleJson_RoundTrip_PreservesData()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);
            var serialized = _parser.SerializeProfile(profile);
            var reparsed = _parser.ParseProfile(serialized);

            Assert.AreEqual(profile.SchemaVersion, reparsed.SchemaVersion);
            Assert.AreEqual(profile.Layers.Length, reparsed.Layers.Length);
            Assert.AreEqual(profile.Expressions.Length, reparsed.Expressions.Length);
        }

        [Test]
        public void SerializeProfile_SampleJson_RoundTrip_PreservesLayers()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);
            var serialized = _parser.SerializeProfile(profile);
            var reparsed = _parser.ParseProfile(serialized);

            for (int i = 0; i < profile.Layers.Length; i++)
            {
                Assert.AreEqual(profile.Layers.Span[i].Name, reparsed.Layers.Span[i].Name);
                Assert.AreEqual(profile.Layers.Span[i].Priority, reparsed.Layers.Span[i].Priority);
                Assert.AreEqual(profile.Layers.Span[i].ExclusionMode, reparsed.Layers.Span[i].ExclusionMode);
            }
        }

        [Test]
        public void SerializeProfile_SampleJson_RoundTrip_PreservesExpressions()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);
            var serialized = _parser.SerializeProfile(profile);
            var reparsed = _parser.ParseProfile(serialized);

            for (int i = 0; i < profile.Expressions.Length; i++)
            {
                var original = profile.Expressions.Span[i];
                var restored = reparsed.Expressions.Span[i];

                Assert.AreEqual(original.Id, restored.Id);
                Assert.AreEqual(original.Name, restored.Name);
                Assert.AreEqual(original.Layer, restored.Layer);
                Assert.AreEqual(original.TransitionDuration, restored.TransitionDuration, 0.001f);
                Assert.AreEqual(original.TransitionCurve.Type, restored.TransitionCurve.Type);
                Assert.AreEqual(original.BlendShapeValues.Length, restored.BlendShapeValues.Length);
                Assert.AreEqual(original.LayerSlots.Length, restored.LayerSlots.Length);
            }
        }

        [Test]
        public void SerializeProfile_SampleJson_RoundTrip_PreservesBlendShapeValues()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);
            var serialized = _parser.SerializeProfile(profile);
            var reparsed = _parser.ParseProfile(serialized);

            // 笑顔の BlendShapeValues を詳細検証
            var originalBs = profile.Expressions.Span[0].BlendShapeValues;
            var restoredBs = reparsed.Expressions.Span[0].BlendShapeValues;

            for (int i = 0; i < originalBs.Length; i++)
            {
                Assert.AreEqual(originalBs.Span[i].Name, restoredBs.Span[i].Name);
                Assert.AreEqual(originalBs.Span[i].Value, restoredBs.Span[i].Value, 0.001f);
            }
        }

        [Test]
        public void SerializeProfile_SampleJson_RoundTrip_PreservesRendererField()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);
            var serialized = _parser.SerializeProfile(profile);
            var reparsed = _parser.ParseProfile(serialized);

            // renderer フィールドが "Face" の BlendShapeMapping を検証
            var restoredBs = reparsed.Expressions.Span[0].BlendShapeValues;
            Assert.AreEqual("Face", restoredBs.Span[2].Renderer);
        }

        [Test]
        public void SerializeProfile_SampleJson_RoundTrip_PreservesLayerSlots()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);
            var serialized = _parser.SerializeProfile(profile);
            var reparsed = _parser.ParseProfile(serialized);

            var originalSlots = profile.Expressions.Span[0].LayerSlots;
            var restoredSlots = reparsed.Expressions.Span[0].LayerSlots;

            Assert.AreEqual(originalSlots.Length, restoredSlots.Length);
            Assert.AreEqual(originalSlots.Span[0].Layer, restoredSlots.Span[0].Layer);

            var originalSlotBs = originalSlots.Span[0].BlendShapeValues;
            var restoredSlotBs = restoredSlots.Span[0].BlendShapeValues;
            Assert.AreEqual(originalSlotBs.Length, restoredSlotBs.Length);
            Assert.AreEqual(originalSlotBs.Span[0].Name, restoredSlotBs.Span[0].Name);
            Assert.AreEqual(originalSlotBs.Span[0].Value, restoredSlotBs.Span[0].Value, 0.001f);
        }

        // ================================================================
        // 設定 JSON パーステスト（§13.8）
        // ================================================================

        [Test]
        public void ParseConfig_SampleJson_ReturnsValidConfig()
        {
            var config = _parser.ParseConfig(JsonSchemaDefinition.SampleConfigJson);

            Assert.IsNotNull(config);
            Assert.AreEqual("1.0", config.SchemaVersion);
        }

        [Test]
        public void ParseConfig_SampleJson_OscPortsCorrect()
        {
            var config = _parser.ParseConfig(JsonSchemaDefinition.SampleConfigJson);

            Assert.AreEqual(9000, config.Osc.SendPort);
            Assert.AreEqual(9001, config.Osc.ReceivePort);
        }

        [Test]
        public void ParseConfig_SampleJson_OscPresetCorrect()
        {
            var config = _parser.ParseConfig(JsonSchemaDefinition.SampleConfigJson);

            Assert.AreEqual("vrchat", config.Osc.Preset);
        }

        [Test]
        public void ParseConfig_SampleJson_OscMappingCorrect()
        {
            var config = _parser.ParseConfig(JsonSchemaDefinition.SampleConfigJson);
            var mapping = config.Osc.Mapping;

            Assert.AreEqual(2, mapping.Length);

            Assert.AreEqual("/avatar/parameters/Fcl_ALL_Joy", mapping.Span[0].OscAddress);
            Assert.AreEqual("Fcl_ALL_Joy", mapping.Span[0].BlendShapeName);
            Assert.AreEqual("emotion", mapping.Span[0].Layer);

            Assert.AreEqual("/avatar/parameters/Fcl_MTH_A", mapping.Span[1].OscAddress);
            Assert.AreEqual("Fcl_MTH_A", mapping.Span[1].BlendShapeName);
            Assert.AreEqual("lipsync", mapping.Span[1].Layer);
        }

        [Test]
        public void ParseConfig_SampleJson_CacheCorrect()
        {
            var config = _parser.ParseConfig(JsonSchemaDefinition.SampleConfigJson);

            Assert.AreEqual(16, config.Cache.AnimationClipLruSize);
        }

        // ================================================================
        // 設定 JSON シリアライズ往復テスト
        // ================================================================

        [Test]
        public void SerializeConfig_SampleJson_RoundTrip_PreservesData()
        {
            var config = _parser.ParseConfig(JsonSchemaDefinition.SampleConfigJson);
            var serialized = _parser.SerializeConfig(config);
            var reparsed = _parser.ParseConfig(serialized);

            Assert.AreEqual(config.SchemaVersion, reparsed.SchemaVersion);
            Assert.AreEqual(config.Osc.SendPort, reparsed.Osc.SendPort);
            Assert.AreEqual(config.Osc.ReceivePort, reparsed.Osc.ReceivePort);
            Assert.AreEqual(config.Osc.Preset, reparsed.Osc.Preset);
            Assert.AreEqual(config.Osc.Mapping.Length, reparsed.Osc.Mapping.Length);
            Assert.AreEqual(config.Cache.AnimationClipLruSize, reparsed.Cache.AnimationClipLruSize);
        }

        [Test]
        public void SerializeConfig_SampleJson_RoundTrip_PreservesOscMapping()
        {
            var config = _parser.ParseConfig(JsonSchemaDefinition.SampleConfigJson);
            var serialized = _parser.SerializeConfig(config);
            var reparsed = _parser.ParseConfig(serialized);

            for (int i = 0; i < config.Osc.Mapping.Length; i++)
            {
                Assert.AreEqual(
                    config.Osc.Mapping.Span[i].OscAddress,
                    reparsed.Osc.Mapping.Span[i].OscAddress);
                Assert.AreEqual(
                    config.Osc.Mapping.Span[i].BlendShapeName,
                    reparsed.Osc.Mapping.Span[i].BlendShapeName);
                Assert.AreEqual(
                    config.Osc.Mapping.Span[i].Layer,
                    reparsed.Osc.Mapping.Span[i].Layer);
            }
        }

        // ================================================================
        // スキーマ定数テスト
        // ================================================================

        [Test]
        public void SchemaDefinition_CurrentVersion_MatchesExpected()
        {
            Assert.AreEqual("1.0", JsonSchemaDefinition.CurrentSchemaVersion);
        }

        [Test]
        public void SchemaDefinition_ProfileFieldNames_Correct()
        {
            Assert.AreEqual("schemaVersion", JsonSchemaDefinition.Profile.SchemaVersion);
            Assert.AreEqual("layers", JsonSchemaDefinition.Profile.Layers);
            Assert.AreEqual("expressions", JsonSchemaDefinition.Profile.Expressions);
        }

        [Test]
        public void SchemaDefinition_ConfigFieldNames_Correct()
        {
            Assert.AreEqual("schemaVersion", JsonSchemaDefinition.Config.SchemaVersion);
            Assert.AreEqual("osc", JsonSchemaDefinition.Config.Osc);
            Assert.AreEqual("cache", JsonSchemaDefinition.Config.Cache);
        }

        [Test]
        public void SchemaDefinition_ExclusionModeValues_Correct()
        {
            Assert.AreEqual("lastWins", JsonSchemaDefinition.Profile.ExclusionModeValues.LastWins);
            Assert.AreEqual("blend", JsonSchemaDefinition.Profile.ExclusionModeValues.Blend);
        }

        [Test]
        public void SchemaDefinition_TransitionCurveTypeValues_Correct()
        {
            Assert.AreEqual("linear", JsonSchemaDefinition.Profile.TransitionCurveTypeValues.Linear);
            Assert.AreEqual("easeIn", JsonSchemaDefinition.Profile.TransitionCurveTypeValues.EaseIn);
            Assert.AreEqual("easeOut", JsonSchemaDefinition.Profile.TransitionCurveTypeValues.EaseOut);
            Assert.AreEqual("easeInOut", JsonSchemaDefinition.Profile.TransitionCurveTypeValues.EaseInOut);
            Assert.AreEqual("custom", JsonSchemaDefinition.Profile.TransitionCurveTypeValues.Custom);
        }
    }
}
