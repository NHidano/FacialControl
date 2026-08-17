using System;
using NUnit.Framework;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Adapters.Json.Dto;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.Json
{
    /// <summary>
    //: <see cref="SystemTextJsonParser"/> が
    /// <c>layers[].inputSources</c> を必須フィールドとして parse することの契約テスト
    /// 。
    /// <para>
    /// 観測完了条件: <c>inputSources</c> 欠落 / 空配列で <see cref="FormatException"/>、
    /// 正常 JSON は <see cref="InputSourceDto"/>[] に変換され、
    /// <c>options</c> は raw JSON 文字列として <see cref="InputSourceDto.optionsJson"/> に保持される。
    /// </para>
    /// </summary>
    [TestFixture]
    public class SystemTextJsonParserInputSourcesTests
    {
        private SystemTextJsonParser _parser;

        [SetUp]
        public void SetUp()
        {
            _parser = new SystemTextJsonParser();
        }

        // ================================================================
        // 欠落 / 空配列 → FormatException (Red)
        // ================================================================

        [Test]
        public void ParseProfile_LayerMissingInputSources_ThrowsFormatException()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins""}
                ],
                ""expressions"":[]
            }";

            var ex = Assert.Throws<FormatException>(() => _parser.ParseProfile(json));
            Assert.IsTrue(
                ex.Message.Contains("inputSources"),
                $"例外メッセージに 'inputSources' を含む必要がある。実際: {ex.Message}");
        }

        [Test]
        public void ParseProfile_LayerInputSourcesEmptyArray_ThrowsFormatException()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[]}
                ],
                ""expressions"":[]
            }";

            var ex = Assert.Throws<FormatException>(() => _parser.ParseProfile(json));
            Assert.IsTrue(
                ex.Message.Contains("inputSources"),
                $"例外メッセージに 'inputSources' を含む必要がある。実際: {ex.Message}");
        }

        [Test]
        public void ParseProfile_OneLayerMissingInputSourcesAmongMany_ThrowsFormatException()
        {
            // 1 つでも欠落していればエラーとして扱う 。
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[{""id"":""input"",""weight"":1.0}]},
                    {""name"":""lipsync"",""priority"":1,""exclusionMode"":""blend""}
                ],
                ""expressions"":[]
            }";

            Assert.Throws<FormatException>(() => _parser.ParseProfile(json));
        }

        [Test]
        public void ParseLayerInputSources_LayerMissingInputSources_ThrowsFormatException()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins""}
                ],
                ""expressions"":[]
            }";

            Assert.Throws<FormatException>(() => _parser.ParseLayerInputSources(json));
        }

        // ================================================================
        // 正常 JSON の parse (Green)
        // ================================================================

        [Test]
        public void ParseProfile_ValidInputSources_DoesNotThrow()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""input"",""weight"":1.0}
                    ]}
                ],
                ""expressions"":[]
            }";

            Assert.DoesNotThrow(() => _parser.ParseProfile(json));
        }

        [Test]
        public void ParseLayerInputSources_SingleEntry_ReturnsCorrectArray()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""input"",""weight"":0.75}
                    ]}
                ],
                ""expressions"":[]
            }";

            var result = _parser.ParseLayerInputSources(json);

            Assert.AreEqual(1, result.Length);
            Assert.AreEqual(1, result[0].Length);
            Assert.AreEqual("input", result[0][0].id);
            Assert.AreEqual(0.75f, result[0][0].weight);
        }

        [Test]
        public void ParseLayerInputSources_MultipleLayers_PreservesLayerOrder()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[{""id"":""input"",""weight"":1.0}]},
                    {""name"":""lipsync"",""priority"":1,""exclusionMode"":""blend"",""inputSources"":[{""id"":""lipsync"",""weight"":1.0}]}
                ],
                ""expressions"":[]
            }";

            var result = _parser.ParseLayerInputSources(json);

            Assert.AreEqual(2, result.Length);
            Assert.AreEqual("input", result[0][0].id);
            Assert.AreEqual("lipsync", result[1][0].id);
        }

        [Test]
        public void ParseLayerInputSources_MultipleEntriesPerLayer_PreservesDeclarationOrder()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""input"",""weight"":0.5},
                        {""id"":""osc"",""weight"":0.5}
                    ]}
                ],
                ""expressions"":[]
            }";

            var result = _parser.ParseLayerInputSources(json);

            Assert.AreEqual(2, result[0].Length);
            Assert.AreEqual("input", result[0][0].id);
            Assert.AreEqual("osc", result[0][1].id);
        }

        // ================================================================
        // options 抽出 (Critical 2)
        // ================================================================

        [Test]
        public void ParseLayerInputSources_OscOptions_CapturedAsRawJsonString()
        {
            // 観測完了条件: options は raw JSON 文字列として optionsJson に保持される。
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""osc"",""weight"":1.0,""options"":{""stalenessSeconds"":2.5}}
                    ]}
                ],
                ""expressions"":[]
            }";

            var result = _parser.ParseLayerInputSources(json);

            Assert.AreEqual(1, result.Length);
            Assert.AreEqual(1, result[0].Length);
            var entry = result[0][0];
            Assert.AreEqual("osc", entry.id);
            Assert.IsNotNull(entry.optionsJson);
            Assert.IsTrue(
                entry.optionsJson.Contains("stalenessSeconds"),
                $"optionsJson が stalenessSeconds を含むべき。実際: {entry.optionsJson}");
            Assert.IsTrue(
                entry.optionsJson.Contains("2.5"),
                $"optionsJson が値 2.5 を含むべき。実際: {entry.optionsJson}");
        }

#if FACIALCONTROL_HAS_OSC_MODULE
        [Test]
        public void ParseLayerInputSources_OptionsJson_CanBeDeserializedToTypedDto()
        {
            // Critical 2: 切り出した raw JSON が JsonUtility で DTO に逆シリアライズできること。
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""osc"",""weight"":1.0,""options"":{""stalenessSeconds"":2.5}}
                    ]}
                ],
                ""expressions"":[]
            }";

            var result = _parser.ParseLayerInputSources(json);
            var optionsJson = result[0][0].optionsJson;

            var options = UnityEngine.JsonUtility.FromJson<OscOptionsDto>(optionsJson);

            Assert.IsNotNull(options);
            Assert.AreEqual(2.5f, options.stalenessSeconds);
        }
#endif

        [Test]
        public void ParseLayerInputSources_NestedOptionsObject_CapturedIncludingNestedBraces()
        {
            // ネストしたオブジェクトを含む options も brace マッチで正しく抽出できる。
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""x-custom"",""weight"":1.0,""options"":{""outer"":1,""nested"":{""inner"":42}}}
                    ]}
                ],
                ""expressions"":[]
            }";

            var result = _parser.ParseLayerInputSources(json);

            var optionsJson = result[0][0].optionsJson;
            Assert.IsTrue(optionsJson.Contains("outer"));
            Assert.IsTrue(optionsJson.Contains("nested"));
            Assert.IsTrue(optionsJson.Contains("inner"));
            Assert.IsTrue(optionsJson.Contains("42"));
        }

        [Test]
        public void ParseLayerInputSources_OptionsAbsent_OptionsJsonIsNullOrEmpty()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""input"",""weight"":1.0}
                    ]}
                ],
                ""expressions"":[]
            }";

            var result = _parser.ParseLayerInputSources(json);

            Assert.IsTrue(string.IsNullOrEmpty(result[0][0].optionsJson));
        }

        [Test]
        public void ParseLayerInputSources_OptionsWithStringValue_PreservesJsonString()
        {
            // options の値が文字列などの JSON エスケープを含む場合も raw JSON として保持される。
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""x-sensor"",""weight"":1.0,""options"":{""label"":""テスト""}}
                    ]}
                ],
                ""expressions"":[]
            }";

            var result = _parser.ParseLayerInputSources(json);

            Assert.IsNotNull(result[0][0].optionsJson);
            Assert.IsTrue(result[0][0].optionsJson.Contains("テスト"));
        }

        // ================================================================
        // JsonSchemaDefinition 定数
        // ================================================================

        [Test]
        public void JsonSchemaDefinition_Layer_InputSourcesFieldName_IsInputSources()
        {
            Assert.AreEqual("inputSources", JsonSchemaDefinition.Profile.Layer.InputSources);
        }

        [Test]
        public void JsonSchemaDefinition_InputSource_FieldNames_Correct()
        {
            Assert.AreEqual("id", JsonSchemaDefinition.Profile.InputSource.Id);
            Assert.AreEqual("weight", JsonSchemaDefinition.Profile.InputSource.Weight);
            Assert.AreEqual("options", JsonSchemaDefinition.Profile.InputSource.Options);
        }
    }
}
