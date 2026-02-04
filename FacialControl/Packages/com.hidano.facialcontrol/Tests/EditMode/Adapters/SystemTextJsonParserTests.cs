using System;
using NUnit.Framework;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    /// <summary>
    /// P05-T01: SystemTextJsonParser の単体テスト。
    /// 正常パース、シリアライズ往復、不正 JSON 例外、バージョンチェックを検証する。
    /// </summary>
    [TestFixture]
    public class SystemTextJsonParserTests
    {
        private SystemTextJsonParser _parser;

        [SetUp]
        public void SetUp()
        {
            _parser = new SystemTextJsonParser();
        }

        // ================================================================
        // ParseProfile — 正常パース
        // ================================================================

        [Test]
        public void ParseProfile_MinimalValidJson_ReturnsProfile()
        {
            var json = @"{""schemaVersion"":""1.0"",""layers"":[],""expressions"":[]}";

            var profile = _parser.ParseProfile(json);

            Assert.AreEqual("1.0", profile.SchemaVersion);
            Assert.AreEqual(0, profile.Layers.Length);
            Assert.AreEqual(0, profile.Expressions.Length);
        }

        [Test]
        public void ParseProfile_WithLayers_ParsesCorrectly()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins""},
                    {""name"":""lipsync"",""priority"":1,""exclusionMode"":""blend""}
                ],
                ""expressions"":[]
            }";

            var profile = _parser.ParseProfile(json);

            Assert.AreEqual(2, profile.Layers.Length);
            Assert.AreEqual("emotion", profile.Layers.Span[0].Name);
            Assert.AreEqual(0, profile.Layers.Span[0].Priority);
            Assert.AreEqual(ExclusionMode.LastWins, profile.Layers.Span[0].ExclusionMode);
            Assert.AreEqual("lipsync", profile.Layers.Span[1].Name);
            Assert.AreEqual(1, profile.Layers.Span[1].Priority);
            Assert.AreEqual(ExclusionMode.Blend, profile.Layers.Span[1].ExclusionMode);
        }

        [Test]
        public void ParseProfile_WithExpression_ParsesCorrectly()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[{""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins""}],
                ""expressions"":[{
                    ""id"":""test-id-001"",
                    ""name"":""笑顔"",
                    ""layer"":""emotion"",
                    ""transitionDuration"":0.3,
                    ""transitionCurve"":{""type"":""easeInOut""},
                    ""blendShapeValues"":[
                        {""name"":""Fcl_ALL_Joy"",""value"":1.0},
                        {""name"":""Fcl_EYE_Joy"",""value"":0.8,""renderer"":""Face""}
                    ],
                    ""layerSlots"":[]
                }]
            }";

            var profile = _parser.ParseProfile(json);

            Assert.AreEqual(1, profile.Expressions.Length);
            var expr = profile.Expressions.Span[0];
            Assert.AreEqual("test-id-001", expr.Id);
            Assert.AreEqual("笑顔", expr.Name);
            Assert.AreEqual("emotion", expr.Layer);
            Assert.AreEqual(0.3f, expr.TransitionDuration, 0.001f);
            Assert.AreEqual(TransitionCurveType.EaseInOut, expr.TransitionCurve.Type);
            Assert.AreEqual(2, expr.BlendShapeValues.Length);
        }

        [Test]
        public void ParseProfile_BlendShapeWithRenderer_ParsesCorrectly()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[{""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins""}],
                ""expressions"":[{
                    ""id"":""test-id-002"",
                    ""name"":""test"",
                    ""layer"":""emotion"",
                    ""transitionDuration"":0.25,
                    ""transitionCurve"":{""type"":""linear""},
                    ""blendShapeValues"":[
                        {""name"":""Blink"",""value"":0.6,""renderer"":""Body""}
                    ],
                    ""layerSlots"":[]
                }]
            }";

            var profile = _parser.ParseProfile(json);
            var bs = profile.Expressions.Span[0].BlendShapeValues.Span[0];

            Assert.AreEqual("Blink", bs.Name);
            Assert.AreEqual(0.6f, bs.Value, 0.001f);
            Assert.AreEqual("Body", bs.Renderer);
        }

        [Test]
        public void ParseProfile_BlendShapeWithoutRenderer_RendererIsNull()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[{""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins""}],
                ""expressions"":[{
                    ""id"":""test-id-003"",
                    ""name"":""test"",
                    ""layer"":""emotion"",
                    ""transitionDuration"":0.25,
                    ""transitionCurve"":{""type"":""linear""},
                    ""blendShapeValues"":[
                        {""name"":""Blink"",""value"":0.5}
                    ],
                    ""layerSlots"":[]
                }]
            }";

            var profile = _parser.ParseProfile(json);
            var bs = profile.Expressions.Span[0].BlendShapeValues.Span[0];

            Assert.IsNull(bs.Renderer);
        }

        [Test]
        public void ParseProfile_WithLayerSlots_ParsesCorrectly()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins""},
                    {""name"":""lipsync"",""priority"":1,""exclusionMode"":""blend""}
                ],
                ""expressions"":[{
                    ""id"":""test-id-004"",
                    ""name"":""test"",
                    ""layer"":""emotion"",
                    ""transitionDuration"":0.25,
                    ""transitionCurve"":{""type"":""linear""},
                    ""blendShapeValues"":[],
                    ""layerSlots"":[{
                        ""layer"":""lipsync"",
                        ""blendShapeValues"":[
                            {""name"":""Fcl_MTH_A"",""value"":0.5}
                        ]
                    }]
                }]
            }";

            var profile = _parser.ParseProfile(json);
            var slots = profile.Expressions.Span[0].LayerSlots;

            Assert.AreEqual(1, slots.Length);
            Assert.AreEqual("lipsync", slots.Span[0].Layer);
            Assert.AreEqual(1, slots.Span[0].BlendShapeValues.Length);
            Assert.AreEqual("Fcl_MTH_A", slots.Span[0].BlendShapeValues.Span[0].Name);
            Assert.AreEqual(0.5f, slots.Span[0].BlendShapeValues.Span[0].Value, 0.001f);
        }

        [Test]
        public void ParseProfile_TransitionCurveNull_DefaultsToLinear()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[{""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins""}],
                ""expressions"":[{
                    ""id"":""test-id-005"",
                    ""name"":""test"",
                    ""layer"":""emotion"",
                    ""transitionDuration"":0.25,
                    ""blendShapeValues"":[],
                    ""layerSlots"":[]
                }]
            }";

            var profile = _parser.ParseProfile(json);

            Assert.AreEqual(TransitionCurveType.Linear, profile.Expressions.Span[0].TransitionCurve.Type);
        }

        [Test]
        public void ParseProfile_AllTransitionCurveTypes_ParseCorrectly()
        {
            var curveTypes = new[] { "linear", "easeIn", "easeOut", "easeInOut", "custom" };
            var expectedTypes = new[]
            {
                TransitionCurveType.Linear,
                TransitionCurveType.EaseIn,
                TransitionCurveType.EaseOut,
                TransitionCurveType.EaseInOut,
                TransitionCurveType.Custom
            };

            for (int i = 0; i < curveTypes.Length; i++)
            {
                var json = @"{
                    ""schemaVersion"":""1.0"",
                    ""layers"":[{""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins""}],
                    ""expressions"":[{
                        ""id"":""test-id-curve-" + i + @""",
                        ""name"":""test"",
                        ""layer"":""emotion"",
                        ""transitionDuration"":0.25,
                        ""transitionCurve"":{""type"":""" + curveTypes[i] + @"""},
                        ""blendShapeValues"":[],
                        ""layerSlots"":[]
                    }]
                }";

                var profile = _parser.ParseProfile(json);
                Assert.AreEqual(expectedTypes[i], profile.Expressions.Span[0].TransitionCurve.Type,
                    $"カーブタイプ '{curveTypes[i]}' のパースに失敗");
            }
        }

        [Test]
        public void ParseProfile_CustomCurveWithKeys_ParsesKeyFrames()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[{""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins""}],
                ""expressions"":[{
                    ""id"":""test-id-custom"",
                    ""name"":""test"",
                    ""layer"":""emotion"",
                    ""transitionDuration"":0.25,
                    ""transitionCurve"":{
                        ""type"":""custom"",
                        ""keys"":[
                            {""time"":0.0,""value"":0.0,""inTangent"":0.0,""outTangent"":1.0,""inWeight"":0.0,""outWeight"":0.33,""weightedMode"":0},
                            {""time"":1.0,""value"":1.0,""inTangent"":1.0,""outTangent"":0.0,""inWeight"":0.33,""outWeight"":0.0,""weightedMode"":0}
                        ]
                    },
                    ""blendShapeValues"":[],
                    ""layerSlots"":[]
                }]
            }";

            var profile = _parser.ParseProfile(json);
            var curve = profile.Expressions.Span[0].TransitionCurve;

            Assert.AreEqual(TransitionCurveType.Custom, curve.Type);
            Assert.AreEqual(2, curve.Keys.Length);
            Assert.AreEqual(0f, curve.Keys.Span[0].Time, 0.001f);
            Assert.AreEqual(0f, curve.Keys.Span[0].Value, 0.001f);
            Assert.AreEqual(1f, curve.Keys.Span[1].Time, 0.001f);
            Assert.AreEqual(1f, curve.Keys.Span[1].Value, 0.001f);
        }

        [Test]
        public void ParseProfile_JapaneseExpressionName_ParsesCorrectly()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[{""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins""}],
                ""expressions"":[{
                    ""id"":""test-id-jp"",
                    ""name"":""怒り顔"",
                    ""layer"":""emotion"",
                    ""transitionDuration"":0.25,
                    ""transitionCurve"":{""type"":""linear""},
                    ""blendShapeValues"":[{""name"":""まばたき左"",""value"":0.8}],
                    ""layerSlots"":[]
                }]
            }";

            var profile = _parser.ParseProfile(json);
            var expr = profile.Expressions.Span[0];

            Assert.AreEqual("怒り顔", expr.Name);
            Assert.AreEqual("まばたき左", expr.BlendShapeValues.Span[0].Name);
        }

        // ================================================================
        // ParseProfile — 不正 JSON 例外
        // ================================================================

        [Test]
        public void ParseProfile_NullJson_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                _parser.ParseProfile(null));
        }

        [Test]
        public void ParseProfile_EmptyString_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                _parser.ParseProfile(""));
        }

        [Test]
        public void ParseProfile_WhitespaceOnly_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                _parser.ParseProfile("   "));
        }

        [Test]
        public void ParseProfile_MissingSchemaVersion_ThrowsFormatException()
        {
            var json = @"{""layers"":[],""expressions"":[]}";

            Assert.Throws<FormatException>(() =>
                _parser.ParseProfile(json));
        }

        [Test]
        public void ParseProfile_EmptySchemaVersion_ThrowsFormatException()
        {
            var json = @"{""schemaVersion"":"""",""layers"":[],""expressions"":[]}";

            Assert.Throws<FormatException>(() =>
                _parser.ParseProfile(json));
        }

        [Test]
        public void ParseProfile_InvalidExclusionMode_ThrowsFormatException()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[{""name"":""emotion"",""priority"":0,""exclusionMode"":""invalid""}],
                ""expressions"":[]
            }";

            Assert.Throws<FormatException>(() =>
                _parser.ParseProfile(json));
        }

        [Test]
        public void ParseProfile_InvalidTransitionCurveType_ThrowsFormatException()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[{""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins""}],
                ""expressions"":[{
                    ""id"":""test-id-invalid"",
                    ""name"":""test"",
                    ""layer"":""emotion"",
                    ""transitionDuration"":0.25,
                    ""transitionCurve"":{""type"":""invalid_type""},
                    ""blendShapeValues"":[],
                    ""layerSlots"":[]
                }]
            }";

            Assert.Throws<FormatException>(() =>
                _parser.ParseProfile(json));
        }

        // ================================================================
        // ParseProfile — バージョンチェック
        // ================================================================

        [Test]
        public void ParseProfile_SupportedVersion_DoesNotThrow()
        {
            var json = @"{""schemaVersion"":""1.0"",""layers"":[],""expressions"":[]}";

            Assert.DoesNotThrow(() =>
                _parser.ParseProfile(json));
        }

        [Test]
        public void ParseProfile_UnsupportedVersion_ThrowsFormatException()
        {
            var json = @"{""schemaVersion"":""2.0"",""layers"":[],""expressions"":[]}";

            var ex = Assert.Throws<FormatException>(() =>
                _parser.ParseProfile(json));
            Assert.IsTrue(ex.Message.Contains("2.0"));
        }

        [Test]
        public void ParseProfile_UnsupportedVersion099_ThrowsFormatException()
        {
            var json = @"{""schemaVersion"":""0.99"",""layers"":[],""expressions"":[]}";

            Assert.Throws<FormatException>(() =>
                _parser.ParseProfile(json));
        }

        // ================================================================
        // SerializeProfile — シリアライズ往復
        // ================================================================

        [Test]
        public void SerializeProfile_MinimalProfile_RoundTrip_PreservesSchemaVersion()
        {
            var profile = new FacialProfile("1.0");

            var json = _parser.SerializeProfile(profile);
            var reparsed = _parser.ParseProfile(json);

            Assert.AreEqual("1.0", reparsed.SchemaVersion);
        }

        [Test]
        public void SerializeProfile_WithLayers_RoundTrip_PreservesLayers()
        {
            var layers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("lipsync", 1, ExclusionMode.Blend),
                new LayerDefinition("eye", 2, ExclusionMode.LastWins)
            };
            var profile = new FacialProfile("1.0", layers);

            var json = _parser.SerializeProfile(profile);
            var reparsed = _parser.ParseProfile(json);

            Assert.AreEqual(3, reparsed.Layers.Length);
            for (int i = 0; i < layers.Length; i++)
            {
                Assert.AreEqual(layers[i].Name, reparsed.Layers.Span[i].Name);
                Assert.AreEqual(layers[i].Priority, reparsed.Layers.Span[i].Priority);
                Assert.AreEqual(layers[i].ExclusionMode, reparsed.Layers.Span[i].ExclusionMode);
            }
        }

        [Test]
        public void SerializeProfile_WithExpressions_RoundTrip_PreservesExpressions()
        {
            var layers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins)
            };
            var blendShapes = new[]
            {
                new BlendShapeMapping("Fcl_ALL_Joy", 1.0f),
                new BlendShapeMapping("Fcl_EYE_Joy", 0.8f, "Face")
            };
            var expressions = new[]
            {
                new Expression(
                    "expr-001", "笑顔", "emotion", 0.3f,
                    new TransitionCurve(TransitionCurveType.EaseInOut),
                    blendShapes)
            };
            var profile = new FacialProfile("1.0", layers, expressions);

            var json = _parser.SerializeProfile(profile);
            var reparsed = _parser.ParseProfile(json);

            Assert.AreEqual(1, reparsed.Expressions.Length);
            var expr = reparsed.Expressions.Span[0];
            Assert.AreEqual("expr-001", expr.Id);
            Assert.AreEqual("笑顔", expr.Name);
            Assert.AreEqual("emotion", expr.Layer);
            Assert.AreEqual(0.3f, expr.TransitionDuration, 0.001f);
            Assert.AreEqual(TransitionCurveType.EaseInOut, expr.TransitionCurve.Type);
            Assert.AreEqual(2, expr.BlendShapeValues.Length);
        }

        [Test]
        public void SerializeProfile_RendererField_RoundTrip_PreservesRenderer()
        {
            var layers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins)
            };
            var blendShapes = new[]
            {
                new BlendShapeMapping("Blink", 0.5f, "Body"),
                new BlendShapeMapping("Smile", 1.0f)
            };
            var expressions = new[]
            {
                new Expression("expr-002", "test", "emotion", 0.25f,
                    TransitionCurve.Linear, blendShapes)
            };
            var profile = new FacialProfile("1.0", layers, expressions);

            var json = _parser.SerializeProfile(profile);
            var reparsed = _parser.ParseProfile(json);

            var bs = reparsed.Expressions.Span[0].BlendShapeValues;
            Assert.AreEqual("Body", bs.Span[0].Renderer);
            Assert.IsNull(bs.Span[1].Renderer);
        }

        [Test]
        public void SerializeProfile_WithLayerSlots_RoundTrip_PreservesSlots()
        {
            var layers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("lipsync", 1, ExclusionMode.Blend)
            };
            var layerSlots = new[]
            {
                new LayerSlot("lipsync", new[]
                {
                    new BlendShapeMapping("Fcl_MTH_A", 0.5f)
                })
            };
            var expressions = new[]
            {
                new Expression("expr-003", "test", "emotion", 0.25f,
                    TransitionCurve.Linear, null, layerSlots)
            };
            var profile = new FacialProfile("1.0", layers, expressions);

            var json = _parser.SerializeProfile(profile);
            var reparsed = _parser.ParseProfile(json);

            var slots = reparsed.Expressions.Span[0].LayerSlots;
            Assert.AreEqual(1, slots.Length);
            Assert.AreEqual("lipsync", slots.Span[0].Layer);
            Assert.AreEqual(1, slots.Span[0].BlendShapeValues.Length);
            Assert.AreEqual("Fcl_MTH_A", slots.Span[0].BlendShapeValues.Span[0].Name);
            Assert.AreEqual(0.5f, slots.Span[0].BlendShapeValues.Span[0].Value, 0.001f);
        }

        [Test]
        public void SerializeProfile_ReturnsValidJsonString()
        {
            var profile = new FacialProfile("1.0");

            var json = _parser.SerializeProfile(profile);

            Assert.IsNotNull(json);
            Assert.IsNotEmpty(json);
            Assert.IsTrue(json.Contains("\"schemaVersion\""));
        }

        // ================================================================
        // ParseConfig — 正常パース
        // ================================================================

        [Test]
        public void ParseConfig_MinimalValidJson_ReturnsConfig()
        {
            var json = @"{""schemaVersion"":""1.0"",""osc"":{""sendPort"":9000,""receivePort"":9001,""preset"":""vrchat"",""mapping"":[]},""cache"":{""animationClipLruSize"":16}}";

            var config = _parser.ParseConfig(json);

            Assert.AreEqual("1.0", config.SchemaVersion);
            Assert.AreEqual(9000, config.Osc.SendPort);
            Assert.AreEqual(9001, config.Osc.ReceivePort);
            Assert.AreEqual("vrchat", config.Osc.Preset);
            Assert.AreEqual(16, config.Cache.AnimationClipLruSize);
        }

        [Test]
        public void ParseConfig_WithOscMapping_ParsesCorrectly()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""osc"":{
                    ""sendPort"":9000,
                    ""receivePort"":9001,
                    ""preset"":""vrchat"",
                    ""mapping"":[
                        {""oscAddress"":""/avatar/parameters/Joy"",""blendShapeName"":""Fcl_ALL_Joy"",""layer"":""emotion""}
                    ]
                },
                ""cache"":{""animationClipLruSize"":16}
            }";

            var config = _parser.ParseConfig(json);
            var mapping = config.Osc.Mapping;

            Assert.AreEqual(1, mapping.Length);
            Assert.AreEqual("/avatar/parameters/Joy", mapping.Span[0].OscAddress);
            Assert.AreEqual("Fcl_ALL_Joy", mapping.Span[0].BlendShapeName);
            Assert.AreEqual("emotion", mapping.Span[0].Layer);
        }

        [Test]
        public void ParseConfig_CustomPorts_ParsesCorrectly()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""osc"":{""sendPort"":8000,""receivePort"":8001,""preset"":""arkit"",""mapping"":[]},
                ""cache"":{""animationClipLruSize"":32}
            }";

            var config = _parser.ParseConfig(json);

            Assert.AreEqual(8000, config.Osc.SendPort);
            Assert.AreEqual(8001, config.Osc.ReceivePort);
            Assert.AreEqual("arkit", config.Osc.Preset);
            Assert.AreEqual(32, config.Cache.AnimationClipLruSize);
        }

        // ================================================================
        // ParseConfig — 不正 JSON 例外
        // ================================================================

        [Test]
        public void ParseConfig_NullJson_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                _parser.ParseConfig(null));
        }

        [Test]
        public void ParseConfig_EmptyString_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                _parser.ParseConfig(""));
        }

        [Test]
        public void ParseConfig_WhitespaceOnly_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                _parser.ParseConfig("   "));
        }

        [Test]
        public void ParseConfig_MissingSchemaVersion_ThrowsFormatException()
        {
            var json = @"{""osc"":{""sendPort"":9000,""receivePort"":9001,""preset"":""vrchat"",""mapping"":[]},""cache"":{""animationClipLruSize"":16}}";

            Assert.Throws<FormatException>(() =>
                _parser.ParseConfig(json));
        }

        // ================================================================
        // ParseConfig — バージョンチェック
        // ================================================================

        [Test]
        public void ParseConfig_SupportedVersion_DoesNotThrow()
        {
            var json = @"{""schemaVersion"":""1.0"",""osc"":{""sendPort"":9000,""receivePort"":9001,""preset"":""vrchat"",""mapping"":[]},""cache"":{""animationClipLruSize"":16}}";

            Assert.DoesNotThrow(() =>
                _parser.ParseConfig(json));
        }

        [Test]
        public void ParseConfig_UnsupportedVersion_ThrowsFormatException()
        {
            var json = @"{""schemaVersion"":""2.0"",""osc"":{""sendPort"":9000,""receivePort"":9001,""preset"":""vrchat"",""mapping"":[]},""cache"":{""animationClipLruSize"":16}}";

            var ex = Assert.Throws<FormatException>(() =>
                _parser.ParseConfig(json));
            Assert.IsTrue(ex.Message.Contains("2.0"));
        }

        // ================================================================
        // SerializeConfig — シリアライズ往復
        // ================================================================

        [Test]
        public void SerializeConfig_RoundTrip_PreservesAllFields()
        {
            var mapping = new[]
            {
                new OscMapping("/avatar/parameters/Joy", "Fcl_ALL_Joy", "emotion"),
                new OscMapping("/avatar/parameters/Angry", "Fcl_ALL_Angry", "emotion")
            };
            var osc = new OscConfiguration(9000, 9001, "vrchat", mapping);
            var cache = new CacheConfiguration(32);
            var config = new FacialControlConfig("1.0", osc, cache);

            var json = _parser.SerializeConfig(config);
            var reparsed = _parser.ParseConfig(json);

            Assert.AreEqual("1.0", reparsed.SchemaVersion);
            Assert.AreEqual(9000, reparsed.Osc.SendPort);
            Assert.AreEqual(9001, reparsed.Osc.ReceivePort);
            Assert.AreEqual("vrchat", reparsed.Osc.Preset);
            Assert.AreEqual(2, reparsed.Osc.Mapping.Length);
            Assert.AreEqual(32, reparsed.Cache.AnimationClipLruSize);
        }

        [Test]
        public void SerializeConfig_RoundTrip_PreservesOscMapping()
        {
            var mapping = new[]
            {
                new OscMapping("/avatar/parameters/Joy", "Fcl_ALL_Joy", "emotion")
            };
            var osc = new OscConfiguration(9000, 9001, "vrchat", mapping);
            var cache = new CacheConfiguration(16);
            var config = new FacialControlConfig("1.0", osc, cache);

            var json = _parser.SerializeConfig(config);
            var reparsed = _parser.ParseConfig(json);

            Assert.AreEqual("/avatar/parameters/Joy", reparsed.Osc.Mapping.Span[0].OscAddress);
            Assert.AreEqual("Fcl_ALL_Joy", reparsed.Osc.Mapping.Span[0].BlendShapeName);
            Assert.AreEqual("emotion", reparsed.Osc.Mapping.Span[0].Layer);
        }

        [Test]
        public void SerializeConfig_ReturnsValidJsonString()
        {
            var config = new FacialControlConfig("1.0");

            var json = _parser.SerializeConfig(config);

            Assert.IsNotNull(json);
            Assert.IsNotEmpty(json);
            Assert.IsTrue(json.Contains("\"schemaVersion\""));
        }

        // ================================================================
        // ExclusionMode パース
        // ================================================================

        [Test]
        public void ParseProfile_ExclusionModeLastWins_ParsesCorrectly()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[{""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins""}],
                ""expressions"":[]
            }";

            var profile = _parser.ParseProfile(json);

            Assert.AreEqual(ExclusionMode.LastWins, profile.Layers.Span[0].ExclusionMode);
        }

        [Test]
        public void ParseProfile_ExclusionModeBlend_ParsesCorrectly()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[{""name"":""lipsync"",""priority"":1,""exclusionMode"":""blend""}],
                ""expressions"":[]
            }";

            var profile = _parser.ParseProfile(json);

            Assert.AreEqual(ExclusionMode.Blend, profile.Layers.Span[0].ExclusionMode);
        }

        // ================================================================
        // IJsonParser インターフェース準拠
        // ================================================================

        [Test]
        public void SystemTextJsonParser_ImplementsIJsonParser()
        {
            Assert.IsInstanceOf<Hidano.FacialControl.Domain.Interfaces.IJsonParser>(_parser);
        }

        // ================================================================
        // P17-T04: rendererPaths の parse / serialize
        // ================================================================

        [Test]
        public void ParseProfile_WithRendererPaths_ParsesCorrectly()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""rendererPaths"":[""Armature/Body"",""Armature/Head""],
                ""layers"":[],
                ""expressions"":[]
            }";

            var profile = _parser.ParseProfile(json);

            Assert.AreEqual(2, profile.RendererPaths.Length);
            Assert.AreEqual("Armature/Body", profile.RendererPaths.Span[0]);
            Assert.AreEqual("Armature/Head", profile.RendererPaths.Span[1]);
        }

        [Test]
        public void ParseProfile_WithoutRendererPaths_DefaultsToEmptyArray()
        {
            var json = @"{""schemaVersion"":""1.0"",""layers"":[],""expressions"":[]}";

            var profile = _parser.ParseProfile(json);

            Assert.AreEqual(0, profile.RendererPaths.Length);
        }

        [Test]
        public void ParseProfile_EmptyRendererPaths_ReturnsEmptyArray()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""rendererPaths"":[],
                ""layers"":[],
                ""expressions"":[]
            }";

            var profile = _parser.ParseProfile(json);

            Assert.AreEqual(0, profile.RendererPaths.Length);
        }

        [Test]
        public void SerializeProfile_WithRendererPaths_RoundTrip_PreservesPaths()
        {
            var rendererPaths = new[] { "Armature/Body", "Armature/Face" };
            var profile = new FacialProfile("1.0", null, null, rendererPaths);

            var json = _parser.SerializeProfile(profile);
            var reparsed = _parser.ParseProfile(json);

            Assert.AreEqual(2, reparsed.RendererPaths.Length);
            Assert.AreEqual("Armature/Body", reparsed.RendererPaths.Span[0]);
            Assert.AreEqual("Armature/Face", reparsed.RendererPaths.Span[1]);
        }

        [Test]
        public void SerializeProfile_WithoutRendererPaths_RoundTrip_ReturnsEmptyArray()
        {
            var profile = new FacialProfile("1.0");

            var json = _parser.SerializeProfile(profile);
            var reparsed = _parser.ParseProfile(json);

            Assert.AreEqual(0, reparsed.RendererPaths.Length);
        }

        [Test]
        public void SerializeProfile_RendererPathsInJson_ContainsField()
        {
            var rendererPaths = new[] { "Armature/Body" };
            var profile = new FacialProfile("1.0", null, null, rendererPaths);

            var json = _parser.SerializeProfile(profile);

            Assert.IsTrue(json.Contains("\"rendererPaths\""));
            Assert.IsTrue(json.Contains("Armature/Body"));
        }

        [Test]
        public void ParseProfile_SampleProfileJson_RendererPaths_ParsesCorrectly()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);

            Assert.AreEqual(1, profile.RendererPaths.Length);
            Assert.AreEqual("Armature/Body", profile.RendererPaths.Span[0]);
        }
    }
}
