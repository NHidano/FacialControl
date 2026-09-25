using System;
using NUnit.Framework;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.Json
{
    /// <summary>
    //: <c>inputSources</c> 欠落 JSON の parse が
    /// <see cref="FormatException"/> を投げ、Aggregator が暗黙に既存 Expression パイプラインへ
    /// フォールバックしないことを保証する契約テスト 。
    /// <para>
    /// 観測完了条件: <see cref="FormatException"/> が発生し、例外メッセージに
    /// 欠落フィールド名 <c>inputSources</c> が含まれる。<c>legacy</c> 予約 ID は廃止済みで
    /// <see cref="InputSourceId.TryParse"/> が受理せず、Parser も暗黙の legacy エントリを
    /// 挿入しない。
    /// </para>
    /// </summary>
    [TestFixture]
    public class SystemTextJsonParserLegacyFallbackContractTests
    {
        private SystemTextJsonParser _parser;

        [SetUp]
        public void SetUp()
        {
            _parser = new SystemTextJsonParser();
        }

        // ================================================================
        // FormatException 発生 + 例外メッセージに欠落フィールド名を含むこと
        // ================================================================

        [Test]
        public void ParseProfile_InputSourcesMissing_ThrowsFormatExceptionWithFieldNameInMessage()
        {
            // Arrange: inputSources を一切宣言しないレイヤー。preview 破壊的変更 (D-5) により
            // 暗黙の legacy フォールバックは廃止されたため、例外でなければならない。
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins""}
                ],
                ""expressions"":[]
            }";

            // Act & Assert
            var ex = Assert.Throws<FormatException>(() => _parser.ParseProfile(json));
            Assert.IsTrue(
                ex.Message.Contains("inputSources"),
                $"例外メッセージには欠落フィールド名 'inputSources' を含める必要がある (観測完了条件)。実際: {ex.Message}");
        }

        [Test]
        public void ParseProfile_InputSourcesEmptyArray_ThrowsFormatExceptionWithFieldNameInMessage()
        {
            // 空配列も「必須かつ非空」契約違反として FormatException になる (D-5)。
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
                $"例外メッセージには欠落フィールド名 'inputSources' を含める必要がある。実際: {ex.Message}");
        }

        [Test]
        public void ParseLayerInputSources_InputSourcesMissing_ThrowsFormatExceptionWithFieldNameInMessage()
        {
            // ParseLayerInputSources の経路でも同じ契約が成立する必要がある。
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""lipsync"",""priority"":1,""exclusionMode"":""blend""}
                ],
                ""expressions"":[]
            }";

            var ex = Assert.Throws<FormatException>(() => _parser.ParseLayerInputSources(json));
            Assert.IsTrue(
                ex.Message.Contains("inputSources"),
                $"例外メッセージには欠落フィールド名 'inputSources' を含める必要がある。実際: {ex.Message}");
        }

        // ================================================================
        // Aggregator への暗黙 legacy フォールバック禁止 (契約テスト)
        // ================================================================

        [Test]
        public void ParseProfile_InputSourcesMissing_DoesNotReturnProfile_NoImplicitFallback()
        {
            // 契約: inputSources 欠落時、Parser は FacialProfile を構築せず必ず例外で中断する。
            // これにより、FacialController 側で Aggregator / Registry を組み立てるための
            // LayerInputSources 情報が伝播せず、暗黙の Expression パイプラインフォールバックが成立し得ない。
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins""}
                ],
                ""expressions"":[]
            }";

            FacialProfile? parsed = null;
            try
            {
                parsed = _parser.ParseProfile(json);
            }
            catch (FormatException)
            {
                // 期待どおり。
            }

            Assert.IsNull(
                parsed,
                "inputSources 欠落時に FacialProfile が構築されてはならない (暗黙フォールバック禁止)。");
        }

        [Test]
        public void ParseProfile_InputSourcesMissingAmongMultipleLayers_ThrowsOnFirstMissingLayer()
        {
            // 他のレイヤーが正しく宣言されていても、1 つでも欠落していれば例外になる。
            // これは「他レイヤーの宣言を流用して暗黙補完する」フォールバック禁止を意味する。
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[{""id"":""input"",""weight"":1.0}]},
                    {""name"":""lipsync"",""priority"":1,""exclusionMode"":""blend""}
                ],
                ""expressions"":[]
            }";

            var ex = Assert.Throws<FormatException>(() => _parser.ParseProfile(json));
            Assert.IsTrue(
                ex.Message.Contains("inputSources"),
                $"例外メッセージには 'inputSources' を含める必要がある。実際: {ex.Message}");
            Assert.IsTrue(
                ex.Message.Contains("lipsync"),
                $"欠落したレイヤー名 'lipsync' を例外メッセージに含めることで、どのレイヤーの欠落かを診断可能にする。実際: {ex.Message}");
        }

        // ================================================================
        // legacy 予約 ID 廃止  の下位レイヤー契約
        // ================================================================

        [Test]
        public void InputSourceId_TryParse_LegacyIdentifier_IsRejected()
        {
            // 下位レイヤー契約: `legacy` 識別子は受理されない。
            // これにより JSON からも `"id": "legacy"` によるフォールバック復活が防がれる。
            bool accepted = InputSourceId.TryParse("legacy", out _);

            Assert.IsFalse(
                accepted,
                "InputSourceId.TryParse は 'legacy' を拒否する必要がある (D-5: legacy フォールバック廃止)。");
        }

        [Test]
        public void ParseProfile_InputSourcesDeclaredAsLegacy_SkippedNotAcceptedAsFallback()
        {
            // 仮にユーザーが `"id": "legacy"` を宣言しても、parser は InputSourceId の検証で弾き
            // 警告 + skip とする 。これにより legacy 実体は生成されず、
            // Aggregator へ暗黙 Expression フォールバックが挿入されない。
            // 宣言自体は存在するため schema チェックは通り FormatException にはならないが、
            // 有効な input source として扱われることは無い。
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""legacy"",""weight"":1.0}
                    ]}
                ],
                ""expressions"":[]
            }";

            UnityEngine.TestTools.LogAssert.Expect(
                UnityEngine.LogType.Warning,
                new System.Text.RegularExpressions.Regex("legacy"));

            var result = _parser.ParseLayerInputSources(json);

            Assert.AreEqual(1, result.Length, "レイヤー数は保たれる。");
            Assert.AreEqual(
                0,
                result[0].Length,
                "`legacy` エントリは無効識別子として skip され、有効 input source としては 0 件となる (暗黙フォールバックなし)。");
        }
    }
}
