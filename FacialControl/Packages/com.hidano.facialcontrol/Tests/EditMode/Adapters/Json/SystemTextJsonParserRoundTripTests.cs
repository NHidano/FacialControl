using NUnit.Framework;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.Json
{
    /// <summary>
    /// <see cref="SystemTextJsonParser"/> のプロファイル JSON round-trip 契約テスト。
    /// <para>
    /// 観測完了条件:
    /// <list type="bullet">
    ///     <item><c>SerializeProfile → ParseProfile → SerializeProfile</c> が同一文字列を返す</item>
    ///     <item><c>inputSources</c> の宣言順が round-trip で保持される</item>
    ///     <item>schemaVersion は <c>"1.0"</c> 固定で出力される</item>
    /// </list>
    /// </para>
    /// </summary>
    [TestFixture]
    public class SystemTextJsonParserRoundTripTests
    {
        private SystemTextJsonParser _parser;

        [SetUp]
        public void SetUp()
        {
            _parser = new SystemTextJsonParser();
        }

        // ================================================================
        // SerializeProfile → ParseProfile → SerializeProfile 文字列等価
        // ================================================================

        [Test]
        public void SerializeParseSerialize_SampleJson_ProducesIdenticalString()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);

            var s1 = _parser.SerializeProfile(profile);
            var p2 = _parser.ParseProfile(s1);
            var s2 = _parser.SerializeProfile(p2);

            Assert.AreEqual(s1, s2, "round-trip 後の JSON 文字列が一致すること");
        }

        [Test]
        public void SerializeParseSerialize_MinimalProfile_ProducesIdenticalString()
        {
            // in-memory 構築プロファイル（inputSources 未設定）。Serialize 側が placeholder を emit し、
            // Parse で LayerInputSources に取り込まれ、再 Serialize で同一出力になる。
            var layers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("lipsync", 1, ExclusionMode.Blend)
            };
            var profile = new FacialProfile(SystemTextJsonParser.SchemaVersionV2, layers);

            var s1 = _parser.SerializeProfile(profile);
            var s2 = _parser.SerializeProfile(_parser.ParseProfile(s1));

            Assert.AreEqual(s1, s2);
        }

        [Test]
        public void SerializeParseSerialize_MultipleInputSourcesPerLayer_PreservesOrderAndString()
        {
            var json = @"{
    ""schemaVersion"": ""1.0"",
    ""layers"": [
        {""name"": ""emotion"", ""priority"": 0, ""exclusionMode"": ""lastWins"", ""inputSources"": [
            {""id"": ""input"", ""weight"": 0.5},
            {""id"": ""osc"", ""weight"": 0.5}
        ]}
    ],
    ""expressions"": [],
    ""rendererPaths"": []
}";

            var p1 = _parser.ParseProfile(json);
            var s1 = _parser.SerializeProfile(p1);
            var p2 = _parser.ParseProfile(s1);
            var s2 = _parser.SerializeProfile(p2);

            Assert.AreEqual(s1, s2);

            // 宣言順の保持 : [input, osc] が保たれる。
            Assert.AreEqual(2, p2.LayerInputSources.Span[0].Length);
            Assert.AreEqual("input", p2.LayerInputSources.Span[0][0].Id);
            Assert.AreEqual("osc", p2.LayerInputSources.Span[0][1].Id);
        }

        [Test]
        public void SerializeParseSerialize_WithOscOptions_PreservesOptionsAndString()
        {
            var json = @"{
    ""schemaVersion"": ""1.0"",
    ""layers"": [
        {""name"": ""emotion"", ""priority"": 0, ""exclusionMode"": ""lastWins"", ""inputSources"": [
            {""id"": ""osc"", ""weight"": 1.0, ""options"": {""stalenessSeconds"": 2.5}}
        ]}
    ],
    ""expressions"": [],
    ""rendererPaths"": []
}";

            var p1 = _parser.ParseProfile(json);
            var s1 = _parser.SerializeProfile(p1);
            var p2 = _parser.ParseProfile(s1);
            var s2 = _parser.SerializeProfile(p2);

            Assert.AreEqual(s1, s2);

            // options が round-trip で optionsJson として保持されていること。
            Assert.AreEqual(1, p2.LayerInputSources.Span[0].Length);
            var decl = p2.LayerInputSources.Span[0][0];
            Assert.AreEqual("osc", decl.Id);
            Assert.IsNotNull(decl.OptionsJson);
            StringAssert.Contains("stalenessSeconds", decl.OptionsJson);
            StringAssert.Contains("2.5", decl.OptionsJson);
        }

        // ================================================================
        // 宣言順と既定値の扱い 
        // ================================================================

        [Test]
        public void ParseProfile_PreservesInputSourceDeclarationOrder()
        {
            var json = @"{
    ""schemaVersion"": ""1.0"",
    ""layers"": [
        {""name"": ""emotion"", ""priority"": 0, ""exclusionMode"": ""lastWins"", ""inputSources"": [
            {""id"": ""osc"", ""weight"": 0.3},
            {""id"": ""input"", ""weight"": 0.3},
            {""id"": ""analog-blendshape"", ""weight"": 0.4}
        ]}
    ],
    ""expressions"": [],
    ""rendererPaths"": []
}";

            var profile = _parser.ParseProfile(json);

            var span = profile.LayerInputSources.Span[0];
            Assert.AreEqual(3, span.Length);
            Assert.AreEqual("osc", span[0].Id);
            Assert.AreEqual("input", span[1].Id);
            Assert.AreEqual("analog-blendshape", span[2].Id);
        }

        [Test]
        public void SerializeProfile_EmitsInputSourcesInDeclarationOrder()
        {
            var profile = _parser.ParseProfile(@"{
    ""schemaVersion"": ""1.0"",
    ""layers"": [
        {""name"": ""emotion"", ""priority"": 0, ""exclusionMode"": ""lastWins"", ""inputSources"": [
            {""id"": ""osc"", ""weight"": 0.3},
            {""id"": ""input"", ""weight"": 0.7}
        ]}
    ],
    ""expressions"": [],
    ""rendererPaths"": []
}");

            var serialized = _parser.SerializeProfile(profile);

            // 出力中の "osc" が "input" より先に現れること。
            int oscIdx = serialized.IndexOf("\"osc\"", System.StringComparison.Ordinal);
            int ctrlIdx = serialized.IndexOf("\"input\"", System.StringComparison.Ordinal);
            Assert.Greater(oscIdx, 0);
            Assert.Greater(ctrlIdx, 0);
            Assert.Less(oscIdx, ctrlIdx, "宣言順が維持されて出力されること");
        }

        [Test]
        public void SerializeProfile_WeightIsAlwaysEmittedConsistently()
        {
            var layers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins)
            };
            var profile = new FacialProfile(SystemTextJsonParser.SchemaVersionV2, layers);

            var serialized = _parser.SerializeProfile(profile);

            StringAssert.Contains("\"weight\":", serialized);
            StringAssert.Contains("1", serialized);
        }

        [Test]
        public void SerializeProfile_EmitsSchemaVersionV2()
        {
            var profile = new FacialProfile(SystemTextJsonParser.SchemaVersionV2);
            var serialized = _parser.SerializeProfile(profile);
            StringAssert.Contains("\"schemaVersion\": \"1.0\"", serialized);
        }

        // ================================================================
        // LayerInputSources の FacialProfile 担体機能
        // ================================================================

        [Test]
        public void ParseProfile_PopulatesLayerInputSourcesAlignedWithLayers()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);

            Assert.AreEqual(profile.Layers.Length, profile.LayerInputSources.Length,
                "LayerInputSources の外側インデックスは Layers と揃う");

            Assert.AreEqual("input", profile.LayerInputSources.Span[0][0].Id);
            Assert.AreEqual("osc", profile.LayerInputSources.Span[0][1].Id);
            Assert.AreEqual("lipsync", profile.LayerInputSources.Span[1][0].Id);
            Assert.AreEqual("input", profile.LayerInputSources.Span[2][0].Id);
        }

        [Test]
        public void FacialProfile_WithExplicitLayerInputSources_RoundTripsThroughParser()
        {
            var layers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins)
            };
            var layerInputSources = new[]
            {
                new[]
                {
                    new InputSourceDeclaration("input", 0.5f, null),
                    new InputSourceDeclaration("osc", 0.5f, "{\"stalenessSeconds\":1.0}")
                }
            };
            var profile = new FacialProfile(SystemTextJsonParser.SchemaVersionV2, layers, null, null, layerInputSources);

            var s1 = _parser.SerializeProfile(profile);
            var p2 = _parser.ParseProfile(s1);
            var s2 = _parser.SerializeProfile(p2);

            Assert.AreEqual(s1, s2);

            Assert.AreEqual(2, p2.LayerInputSources.Span[0].Length);
            Assert.AreEqual("input", p2.LayerInputSources.Span[0][0].Id);
            Assert.AreEqual(0.5f, p2.LayerInputSources.Span[0][0].Weight);
            Assert.AreEqual("osc", p2.LayerInputSources.Span[0][1].Id);
            Assert.AreEqual(0.5f, p2.LayerInputSources.Span[0][1].Weight);
            StringAssert.Contains("stalenessSeconds", p2.LayerInputSources.Span[0][1].OptionsJson);
        }
    }
}
