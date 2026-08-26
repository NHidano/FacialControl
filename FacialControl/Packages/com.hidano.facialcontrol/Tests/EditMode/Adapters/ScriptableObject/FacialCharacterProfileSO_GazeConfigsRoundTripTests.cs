using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Adapters.Json.Dto;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Editor.AutoExport;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests
{
    /// <summary>
    /// SO ルート <c>_gazeConfigs</c> を Exporter → JSON → Parser → Converter で
    /// ラウンドトリップさせ、各 entry が value-equal で復元されることを保証する。
    /// 旧 v2.0 JSON が parser で strict 拒否され続ける（自動 migration が存在しない）ことも併せて確認する。
    /// </summary>
    [TestFixture]
    public class FacialCharacterProfileSO_GazeConfigsRoundTripTests
    {
        private sealed class TestCharacterSO : FacialCharacterProfileSO
        {
            public List<GazeBindingConfig> WritableGazeConfigs => _gazeConfigs;
        }

        [Test]
        public void RoundTrip_MultipleGazeConfigs_PreservesValueEqualEntriesAtSORoot()
        {
            // ラウンドトリップ手順:
            //   (a) SO ルートに GazeBindingConfig を 2 件詰める。
            //   (b) FacialCharacterProfileExporter で StreamingAssets profile.json に書き出す（SO → JSON）。
            //   (c) SystemTextJsonParser.ParseProfileSnapshotV2 で v1.0 strict パース。
            //   (d) FacialCharacterProfileConverter.ToSORootGazeConfigs で SO ルート相当に復元（JSON → SO）。
            //   (e) 復元結果が元 SO の _gazeConfigs と value-equal であることを assert する。
            //   look-clip / lookXxxSamples は JSON 出力対象外（SO YAML 側 source-of-truth）の運用のため、
            //   ラウンドトリップ後は default (null / empty) になる点も明示的に検証する。

            var so = UnityEngine.ScriptableObject.CreateInstance<TestCharacterSO>();
            string assetName = "GazeConfigsRoundTrip_" + Guid.NewGuid().ToString("N");
            string profilePath = FacialCharacterProfileSO.GetStreamingAssetsProfilePath(assetName);
            string profileDirectory = Path.GetDirectoryName(profilePath);

            try
            {
                so.name = assetName;
                var sourceConfigs = BuildSourceGazeConfigs();
                so.WritableGazeConfigs.AddRange(sourceConfigs);

                bool exported = FacialCharacterProfileExporter.ExportProfileJson(so);

                Assert.That(exported, Is.True, "Exporter は profile.json を生成するはず");
                Assert.That(File.Exists(profilePath), Is.True, "profile.json は disk に書き出されるはず");

                string json = File.ReadAllText(profilePath);
                StringAssert.Contains("\"gaze_configs\"", json, "JSON root 直下の \"gaze_configs\" キーが必要");
                StringAssert.Contains("\"useDistinctLeftRight\"", json, "Gaze JSON must carry the distinct left/right mode flag.");
                StringAssert.Contains("\"sourceIdLeft\"", json, "Gaze JSON must carry the left source id field.");
                StringAssert.Contains("\"sourceIdRight\"", json, "Gaze JSON must carry the right source id field.");
                StringAssert.DoesNotContain("\"_gazeConfigs\"", json, "Unity SerializedField 接頭辞は JSON に出力されないはず");

                var parser = new SystemTextJsonParser();
                ProfileSnapshotDto parsed = parser.ParseProfileSnapshotV2(json);

                Assert.That(parsed, Is.Not.Null);
                Assert.That(parsed.schemaVersion, Is.EqualTo(SystemTextJsonParser.SchemaVersionV2));
                Assert.That(parsed.gazeConfigs, Is.Not.Null);
                Assert.That(parsed.gazeConfigs, Has.Count.EqualTo(sourceConfigs.Count));

                List<GazeBindingConfig> restored = FacialCharacterProfileConverter.ToSORootGazeConfigs(parsed);

                Assert.That(restored, Is.Not.Null);
                Assert.That(restored, Has.Count.EqualTo(sourceConfigs.Count));
                for (int i = 0; i < sourceConfigs.Count; i++)
                {
                    AssertGazeConfigsValueEqual(sourceConfigs[i], restored[i], "index " + i);
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(profileDirectory) && Directory.Exists(profileDirectory))
                {
                    Directory.Delete(profileDirectory, true);
                }
                Object.DestroyImmediate(so);
            }
        }

        [Test]
        public void RoundTrip_EmptyGazeConfigs_RestoresEmptyListWithoutNull()
        {
            // SO ルートが空 list でも JSON ↔ SO ラウンドトリップが破綻しないことを保証する。
            // Converter は空 list を null 化せず new List<GazeBindingConfig>() を返す。

            var so = UnityEngine.ScriptableObject.CreateInstance<TestCharacterSO>();
            string assetName = "GazeConfigsRoundTripEmpty_" + Guid.NewGuid().ToString("N");
            string profilePath = FacialCharacterProfileSO.GetStreamingAssetsProfilePath(assetName);
            string profileDirectory = Path.GetDirectoryName(profilePath);

            try
            {
                so.name = assetName;

                bool exported = FacialCharacterProfileExporter.ExportProfileJson(so);
                Assert.That(exported, Is.True);
                Assert.That(File.Exists(profilePath), Is.True);

                string json = File.ReadAllText(profilePath);
                var parser = new SystemTextJsonParser();
                ProfileSnapshotDto parsed = parser.ParseProfileSnapshotV2(json);

                List<GazeBindingConfig> restored = FacialCharacterProfileConverter.ToSORootGazeConfigs(parsed);

                Assert.That(restored, Is.Not.Null);
                Assert.That(restored, Is.Empty);
            }
            finally
            {
                if (!string.IsNullOrEmpty(profileDirectory) && Directory.Exists(profileDirectory))
                {
                    Directory.Delete(profileDirectory, true);
                }
                Object.DestroyImmediate(so);
            }
        }

        [Test]
        public void Parse_UnsupportedSchemaVersion_IsRejectedToKeepCIFailingOnUnsupportedJson()
        {
            // "1.0" 以外（未サポート）の JSON が誤って silent migrate されないこと。
            // パーサが schemaVersion="2.0" のような未サポート値を NotSupportedException で拒否し続けることで、
            // 自動 migration コードを書かないという方針が CI で守られる（CI が落ちる挙動を維持）。
            var unsupportedJson =
                "{\"schemaVersion\":\"2.0\"," +
                "\"layers\":[]," +
                "\"expressions\":[]," +
                "\"rendererPaths\":[]}";

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("schema v1.0 の strict チェックに失敗"));

            var parser = new SystemTextJsonParser();
            var ex = Assert.Throws<NotSupportedException>(() => parser.ParseProfileSnapshotV2(unsupportedJson));
            StringAssert.Contains("'2.0'", ex.Message);
            StringAssert.Contains("'1.0'", ex.Message);
        }

        private static List<GazeBindingConfig> BuildSourceGazeConfigs()
        {
            return new List<GazeBindingConfig>
            {
                new GazeBindingConfig
                {
                    expressionId = "eye_look",
                    useDistinctLeftRight = false,
                    sourceIdLeft = string.Empty,
                    sourceIdRight = string.Empty,
                    leftEyeBonePath = "Armature/Hips/Head/LeftEye",
                    leftEyeInitialRotation = new Vector3(1f, 2f, 3f),
                    leftEyeYawAxisLocal = new Vector3(0f, 1f, 0f),
                    leftEyePitchAxisLocal = new Vector3(1f, 0f, 0f),
                    rightEyeBonePath = "Armature/Hips/Head/RightEye",
                    rightEyeInitialRotation = new Vector3(4f, 5f, 6f),
                    rightEyeYawAxisLocal = new Vector3(0f, 0.75f, 0.25f),
                    rightEyePitchAxisLocal = new Vector3(0.5f, 0f, 0.5f),
                    lookUpAngle = 16f,
                    lookDownAngle = 8f,
                    outerYawAngle = 17f,
                    innerYawAngle = 12f,
                },
                new GazeBindingConfig
                {
                    expressionId = "secondary_look",
                    useDistinctLeftRight = true,
                    sourceIdLeft = "input:secondary_look.left",
                    sourceIdRight = "osc:secondary_look.right",
                    leftEyeBonePath = "Armature/Spine/Head/L_Eye",
                    leftEyeInitialRotation = new Vector3(-2f, 0f, 4f),
                    leftEyeYawAxisLocal = new Vector3(0f, 1f, 0.1f),
                    leftEyePitchAxisLocal = new Vector3(1f, 0f, -0.1f),
                    rightEyeBonePath = "Armature/Spine/Head/R_Eye",
                    rightEyeInitialRotation = new Vector3(-2f, 0f, -4f),
                    rightEyeYawAxisLocal = new Vector3(0f, 0.95f, -0.1f),
                    rightEyePitchAxisLocal = new Vector3(1f, 0.1f, 0f),
                    lookUpAngle = 9f,
                    lookDownAngle = 6f,
                    outerYawAngle = 22f,
                    innerYawAngle = 14f,
                },
            };
        }

        private static void AssertGazeConfigsValueEqual(GazeBindingConfig expected, GazeBindingConfig actual, string label)
        {
            Assert.That(actual, Is.Not.Null, "[" + label + "] restored entry must not be null");
            Assert.That(actual.expressionId, Is.EqualTo(expected.expressionId), "[" + label + "] expressionId");
            Assert.That(actual.useDistinctLeftRight, Is.EqualTo(expected.useDistinctLeftRight), "[" + label + "] useDistinctLeftRight");
            Assert.That(actual.sourceIdLeft, Is.EqualTo(expected.sourceIdLeft), "[" + label + "] sourceIdLeft");
            Assert.That(actual.sourceIdRight, Is.EqualTo(expected.sourceIdRight), "[" + label + "] sourceIdRight");
            Assert.That(actual.leftEyeBonePath, Is.EqualTo(expected.leftEyeBonePath), "[" + label + "] leftEyeBonePath");
            Assert.That(actual.leftEyeInitialRotation, Is.EqualTo(expected.leftEyeInitialRotation), "[" + label + "] leftEyeInitialRotation");
            Assert.That(actual.leftEyeYawAxisLocal, Is.EqualTo(expected.leftEyeYawAxisLocal), "[" + label + "] leftEyeYawAxisLocal");
            Assert.That(actual.leftEyePitchAxisLocal, Is.EqualTo(expected.leftEyePitchAxisLocal), "[" + label + "] leftEyePitchAxisLocal");
            Assert.That(actual.rightEyeBonePath, Is.EqualTo(expected.rightEyeBonePath), "[" + label + "] rightEyeBonePath");
            Assert.That(actual.rightEyeInitialRotation, Is.EqualTo(expected.rightEyeInitialRotation), "[" + label + "] rightEyeInitialRotation");
            Assert.That(actual.rightEyeYawAxisLocal, Is.EqualTo(expected.rightEyeYawAxisLocal), "[" + label + "] rightEyeYawAxisLocal");
            Assert.That(actual.rightEyePitchAxisLocal, Is.EqualTo(expected.rightEyePitchAxisLocal), "[" + label + "] rightEyePitchAxisLocal");
            Assert.That(actual.lookUpAngle, Is.EqualTo(expected.lookUpAngle).Within(1e-6f), "[" + label + "] lookUpAngle");
            Assert.That(actual.lookDownAngle, Is.EqualTo(expected.lookDownAngle).Within(1e-6f), "[" + label + "] lookDownAngle");
            Assert.That(actual.outerYawAngle, Is.EqualTo(expected.outerYawAngle).Within(1e-6f), "[" + label + "] outerYawAngle");
            Assert.That(actual.innerYawAngle, Is.EqualTo(expected.innerYawAngle).Within(1e-6f), "[" + label + "] innerYawAngle");

            // look-clip / lookXxxSamples は JSON 出力対象外。SO YAML 側の source-of-truth として
            // ラウンドトリップ後は default (null / empty) になる。
            Assert.That(actual.lookLeftClip, Is.Null, "[" + label + "] lookLeftClip should be null after JSON roundtrip");
            Assert.That(actual.lookRightClip, Is.Null, "[" + label + "] lookRightClip should be null after JSON roundtrip");
            Assert.That(actual.lookUpClip, Is.Null, "[" + label + "] lookUpClip should be null after JSON roundtrip");
            Assert.That(actual.lookDownClip, Is.Null, "[" + label + "] lookDownClip should be null after JSON roundtrip");
            Assert.That(actual.lookLeftSamples, Is.Not.Null);
            Assert.That(actual.lookLeftSamples, Is.Empty, "[" + label + "] lookLeftSamples should be empty after JSON roundtrip");
            Assert.That(actual.lookRightSamples, Is.Not.Null);
            Assert.That(actual.lookRightSamples, Is.Empty, "[" + label + "] lookRightSamples should be empty after JSON roundtrip");
            Assert.That(actual.lookUpSamples, Is.Not.Null);
            Assert.That(actual.lookUpSamples, Is.Empty, "[" + label + "] lookUpSamples should be empty after JSON roundtrip");
            Assert.That(actual.lookDownSamples, Is.Not.Null);
            Assert.That(actual.lookDownSamples, Is.Empty, "[" + label + "] lookDownSamples should be empty after JSON roundtrip");
        }
    }
}
