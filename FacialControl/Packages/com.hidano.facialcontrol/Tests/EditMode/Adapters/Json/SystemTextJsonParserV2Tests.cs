using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.Json
{
    /// <summary>
    /// <see cref="SystemTextJsonParser"/> がプロファイル JSON 専用として、
    /// <c>schemaVersion: "1.0"</c> 以外を <see cref="Debug.LogError(object)"/> +
    /// <see cref="NotSupportedException"/> で拒否することの契約テスト。
    /// </summary>
    [TestFixture]
    public class SystemTextJsonParserV2Tests
    {
        private SystemTextJsonParser _parser;

        [SetUp]
        public void SetUp()
        {
            _parser = new SystemTextJsonParser();
        }

        // ================================================================
        // Parse_SchemaV2_ReturnsExpectedProfile
        // ================================================================

        [Test]
        public void Parse_SchemaV2_ReturnsExpectedProfile()
        {
            var json = @"{
                ""schemaVersion"": ""1.0"",
                ""rendererPaths"": [""Body""],
                ""layers"": [
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""input"",""weight"":1.0}
                    ]}
                ],
                ""expressions"": [
                    {
                        ""id"": ""550e8400-e29b-41d4-a716-446655440000"",
                        ""name"": ""Smile"",
                        ""layer"": ""emotion"",
                        ""layerOverrideMask"": [""emotion""],
                        ""snapshot"": {
                            ""transitionDuration"": 0.3,
                            ""transitionCurvePreset"": ""EaseInOut"",
                            ""blendShapes"": [
                                {""rendererPath"":""Body"",""name"":""Smile"",""value"":1.0}
                            ],
                            ""bones"": [],
                            ""rendererPaths"": [""Body""]
                        }
                    }
                ]
            }";

            var profile = _parser.ParseProfile(json);

            Assert.AreEqual(SystemTextJsonParser.SchemaVersionV2, profile.SchemaVersion);
            Assert.AreEqual(1, profile.Layers.Length);
            Assert.AreEqual("emotion", profile.Layers.Span[0].Name);

            Assert.AreEqual(1, profile.Expressions.Length);
            var expr = profile.Expressions.Span[0];
            Assert.AreEqual("550e8400-e29b-41d4-a716-446655440000", expr.Id);
            Assert.AreEqual("Smile", expr.Name);
            Assert.AreEqual("emotion", expr.Layer);
            Assert.AreEqual(0.3f, expr.TransitionDuration);
            Assert.AreEqual(TransitionCurveType.EaseInOut, expr.TransitionCurve.Type);

            Assert.AreEqual(1, expr.BlendShapeValues.Length);
            Assert.AreEqual("Smile", expr.BlendShapeValues.Span[0].Name);
            Assert.AreEqual(1.0f, expr.BlendShapeValues.Span[0].Value);
            Assert.AreEqual("Body", expr.BlendShapeValues.Span[0].Renderer);

            Assert.AreEqual(1, profile.RendererPaths.Length);
            Assert.AreEqual("Body", profile.RendererPaths.Span[0]);
        }

        // ================================================================
        // Parse_LayerOverrideMask_MapsToOverrideMaskBits
        // ================================================================

        [Test]
        public void Parse_LayerOverrideMask_MapsToOverrideMaskBits()
        {
            // layers 宣言順: emotion(bit0), overlay(bit1)。
            // smile.layerOverrideMask=["overlay"] が OverrideMask=Bit1 に変換されることを検証する。
            var json = @"{
                ""schemaVersion"": ""1.0"",
                ""rendererPaths"": [""Body""],
                ""layers"": [
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[{""id"":""input"",""weight"":1.0}]},
                    {""name"":""overlay"",""priority"":1,""exclusionMode"":""lastWins"",""inputSources"":[{""id"":""input:overlay:blink"",""weight"":1.0}]}
                ],
                ""expressions"": [
                    {
                        ""id"": ""smile"",
                        ""name"": ""Smile"",
                        ""layer"": ""emotion"",
                        ""layerOverrideMask"": [""overlay""],
                        ""snapshot"": {
                            ""transitionDuration"": 0.1,
                            ""transitionCurvePreset"": ""Linear"",
                            ""blendShapes"": [],
                            ""bones"": [],
                            ""rendererPaths"": []
                        }
                    }
                ]
            }";

            var profile = _parser.ParseProfile(json);
            var expr = profile.Expressions.Span[0];

            Assert.AreEqual(LayerOverrideMask.Bit1, expr.OverrideMask,
                "overlay は layers index1 なので Bit1 が立つべき。");
        }

        // ================================================================
        // Parse_UnsupportedSchemaVersion_ThrowsNotSupportedException
        // ================================================================

        [Test]
        public void Parse_UnsupportedSchemaVersion_ThrowsNotSupportedException()
        {
            var json = @"{
                ""schemaVersion"": ""2.0"",
                ""layers"": [
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""input"",""weight"":1.0}
                    ]}
                ],
                ""expressions"": [],
                ""rendererPaths"": [],
                ""gazeConfigs"": []
            }";

            LogAssert.Expect(LogType.Error, new Regex("schema v1.0 の strict チェックに失敗"));

            var ex = Assert.Throws<NotSupportedException>(() => _parser.ParseProfile(json));
            StringAssert.Contains("'2.0'", ex.Message);
            StringAssert.Contains("'1.0'", ex.Message);
        }

        [Test]
        public void ParseProfileSnapshotV2_RootGazeConfigs_PreservesValues()
        {
            var json = @"{
                ""schemaVersion"": ""1.0"",
                ""layers"": [],
                ""expressions"": [],
                ""rendererPaths"": [],
                ""gaze_configs"": [
                    {
                        ""expressionId"": ""eye_look"",
                        ""useDistinctLeftRight"": true,
                        ""sourceIdLeft"": ""input:eye_look.left"",
                        ""sourceIdRight"": ""osc:eye_look.right"",
                        ""leftEyeBonePath"": ""Head/LeftEye"",
                        ""leftEyeInitialRotation"": {""x"":0,""y"":1,""z"":2},
                        ""leftEyeYawAxisLocal"": {""x"":0,""y"":1,""z"":0},
                        ""leftEyePitchAxisLocal"": {""x"":1,""y"":0,""z"":0},
                        ""rightEyeBonePath"": ""Head/RightEye"",
                        ""rightEyeInitialRotation"": {""x"":3,""y"":4,""z"":5},
                        ""rightEyeYawAxisLocal"": {""x"":0,""y"":1,""z"":0},
                        ""rightEyePitchAxisLocal"": {""x"":1,""y"":0,""z"":0},
                        ""lookUpAngle"": 16,
                        ""lookDownAngle"": 8,
                        ""outerYawAngle"": 17,
                        ""innerYawAngle"": 7
                    }
                ]
            }";

            var dto = _parser.ParseProfileSnapshotV2(json);

            Assert.AreEqual(1, dto.gazeConfigs.Count);
            var cfg = dto.gazeConfigs[0];
            Assert.AreEqual("eye_look", cfg.expressionId);
            Assert.AreEqual(true, cfg.useDistinctLeftRight);
            Assert.AreEqual("input:eye_look.left", cfg.sourceIdLeft);
            Assert.AreEqual("osc:eye_look.right", cfg.sourceIdRight);
            Assert.AreEqual("Head/LeftEye", cfg.leftEyeBonePath);
            Assert.AreEqual("Head/RightEye", cfg.rightEyeBonePath);
            Assert.AreEqual(1f, cfg.leftEyeInitialRotation.y);
            Assert.AreEqual(4f, cfg.rightEyeInitialRotation.y);
            Assert.AreEqual(16f, cfg.lookUpAngle);
            Assert.AreEqual(8f, cfg.lookDownAngle);
            Assert.AreEqual(17f, cfg.outerYawAngle);
            Assert.AreEqual(7f, cfg.innerYawAngle);
        }

        // ================================================================
        // Parse_MissingSchemaVersion_ThrowsAndLogsError
        // ================================================================

        [Test]
        public void Parse_MissingSchemaVersion_ThrowsAndLogsError()
        {
            var json = @"{
                ""layers"": [
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""input"",""weight"":1.0}
                    ]}
                ],
                ""expressions"": []
            }";

            LogAssert.Expect(LogType.Error, new Regex("schema v1.0 の strict チェックに失敗"));

            var ex = Assert.Throws<NotSupportedException>(() => _parser.ParseProfile(json));
            StringAssert.Contains("<missing>", ex.Message);
            StringAssert.Contains("'1.0'", ex.Message);
        }
    }
}
