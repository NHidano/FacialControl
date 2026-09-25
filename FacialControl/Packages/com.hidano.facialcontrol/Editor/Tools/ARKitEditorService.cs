using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Editor.Tools
{
    /// <summary>
    /// Editor 向け ARKit 検出 API。
    /// Domain 層の ARKitDetector をラップし、SkinnedMeshRenderer からの BlendShape 名取得と
    /// 検出結果の JSON 保存機能を提供する。
    /// </summary>
    public sealed class ARKitEditorService
    {
        private readonly ARKitUseCase _useCase;
        private readonly IJsonParser _parser;

        public ARKitEditorService()
        {
            _useCase = new ARKitUseCase();
            _parser = new SystemTextJsonParser();
        }

        public ARKitEditorService(ARKitUseCase useCase, IJsonParser parser)
        {
            _useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
            _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        }

        /// <summary>
        /// SkinnedMeshRenderer から全 BlendShape 名を取得する。
        /// </summary>
        /// <param name="renderer">対象の SkinnedMeshRenderer</param>
        /// <returns>BlendShape 名の配列。BlendShape が存在しない場合は空配列</returns>
        public string[] GetBlendShapeNames(SkinnedMeshRenderer renderer)
        {
            if (renderer == null)
                throw new ArgumentNullException(nameof(renderer));

            var mesh = renderer.sharedMesh;
            if (mesh == null)
                return Array.Empty<string>();

            int count = mesh.blendShapeCount;
            if (count == 0)
                return Array.Empty<string>();

            var names = new string[count];
            for (int i = 0; i < count; i++)
            {
                names[i] = mesh.GetBlendShapeName(i);
            }
            return names;
        }

        /// <summary>
        /// SkinnedMeshRenderer の BlendShape に対して ARKit 52 / PerfectSync 検出と Expression 自動生成を実行する。
        /// </summary>
        /// <param name="renderer">対象の SkinnedMeshRenderer</param>
        /// <returns>検出結果と生成された Expression</returns>
        public ARKitUseCase.DetectResult DetectFromRenderer(SkinnedMeshRenderer renderer)
        {
            var blendShapeNames = GetBlendShapeNames(renderer);
            return _useCase.DetectAndGenerate(blendShapeNames);
        }

        /// <summary>
        /// 検出された BlendShape 名から OSC マッピングを生成する。
        /// </summary>
        /// <param name="detectedNames">検出済みパラメータ名配列</param>
        /// <returns>生成された OscMapping 配列</returns>
        public OscMapping[] GenerateOscMapping(string[] detectedNames)
        {
            return _useCase.GenerateOscMapping(detectedNames);
        }

        /// <summary>
        /// 検出された Expression をプロファイル JSON として保存する。
        /// レイヤー定義は Expression のレイヤー参照から自動生成される。
        /// </summary>
        /// <param name="expressions">保存対象の Expression 配列</param>
        /// <param name="path">保存先ファイルパス</param>
        public void SaveExpressionsAsProfileJson(Expression[] expressions, string path)
        {
            if (expressions == null)
                throw new ArgumentNullException(nameof(expressions));
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("保存先パスを空にすることはできません。", nameof(path));

            // Expression のレイヤー参照からレイヤー定義を構築
            var layerNames = new HashSet<string>();
            for (int i = 0; i < expressions.Length; i++)
            {
                layerNames.Add(expressions[i].Layer);
            }

            var layers = new List<LayerDefinition>();
            int priority = 0;
            foreach (var name in layerNames)
            {
                layers.Add(new LayerDefinition(name, priority, ExclusionMode.LastWins));
                priority++;
            }

            var profile = new FacialProfile("1.0", layers.ToArray(), expressions);
            var json = _parser.SerializeProfile(profile);

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, json, System.Text.Encoding.UTF8);
        }

        /// <summary>
        /// OSC マッピングを config.json として保存する。
        /// </summary>
        /// <param name="mappings">保存対象の OscMapping 配列</param>
        /// <param name="path">保存先ファイルパス</param>
        public void SaveOscMappingAsConfigJson(OscMapping[] mappings, string path)
        {
            if (mappings == null)
                throw new ArgumentNullException(nameof(mappings));
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("保存先パスを空にすることはできません。", nameof(path));

            var oscConfig = new OscConfiguration(mapping: mappings);
            var config = new FacialControlConfig("1.0", oscConfig);
            var json = _parser.SerializeConfig(config);

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, json, System.Text.Encoding.UTF8);
        }

        /// <summary>
        /// 新規に生成した Expression / OSC マッピングを、既存の <see cref="FacialCharacterProfileSO"/>
        /// が参照する JSON にマージする。
        /// </summary>
        /// <param name="targetProfile">マージ先の統合 SO。<c>StreamingAssets/FacialControl/{SO 名}/profile.json</c> を書き換える</param>
        /// <param name="newExpressions">追記する Expression 配列。null または空配列の場合は Expression 追加をスキップする</param>
        /// <param name="newMappings">追記する OSC マッピング配列。null または空配列の場合は OSC 追加をスキップする</param>
        /// <remarks>
        /// ID 衝突時は <see cref="System.Guid.NewGuid"/> で新 UUID を再発行する。
        /// 名前衝突時は <c>{originalName}_2</c>, <c>_3</c>... のサフィックスを付与して一意化する。
        /// OSC マッピングは <see cref="OscMapping.OscAddress"/> が既存と重複する場合はスキップする。
        /// 処理前に <see cref="Undo.RecordObject"/> を呼び出すため Undo 可能。
        /// </remarks>
        public void MergeIntoExistingProfile(
            FacialCharacterProfileSO targetProfile,
            Expression[] newExpressions,
            OscMapping[] newMappings = null)
        {
            if (targetProfile == null)
                throw new ArgumentNullException(nameof(targetProfile));

            var fullPath = FacialCharacterProfileSO.GetStreamingAssetsProfilePath(targetProfile.CharacterAssetName);
            if (string.IsNullOrEmpty(fullPath))
                throw new InvalidOperationException(
                    "マージ先 SO の CharacterAssetName が未設定です。");

            // Undo 登録（SO 自体の変更は発生しなくても、ユーザーに操作取り消しの機会を提供する）
            Undo.RecordObject(targetProfile, "Merge ARKit Profile");

            if (!File.Exists(fullPath))
                throw new FileNotFoundException(
                    $"マージ先 JSON ファイルが見つかりません: {fullPath}", fullPath);

            var existingJson = File.ReadAllText(fullPath, System.Text.Encoding.UTF8);
            var existingProfile = _parser.ParseProfile(existingJson);

            // 既存 Expression の ID / 名前セット（衝突検出用）
            var existingIds = new HashSet<string>(StringComparer.Ordinal);
            var existingNames = new HashSet<string>(StringComparer.Ordinal);
            var existingExpressions = existingProfile.Expressions.ToArray();
            for (int i = 0; i < existingExpressions.Length; i++)
            {
                existingIds.Add(existingExpressions[i].Id);
                existingNames.Add(existingExpressions[i].Name);
            }

            // 新規 Expression をマージ（ID / 名前の衝突を解決しつつ追加）
            var mergedExpressions = new List<Expression>(existingExpressions);
            if (newExpressions != null)
            {
                for (int i = 0; i < newExpressions.Length; i++)
                {
                    var source = newExpressions[i];

                    // ID 衝突: 新 UUID を再発行（既存集合と新規追加分の両方に対してユニーク化）
                    var resolvedId = source.Id;
                    while (existingIds.Contains(resolvedId))
                    {
                        resolvedId = Guid.NewGuid().ToString();
                    }

                    // 名前衝突: {originalName}_2, _3, ... のサフィックスを付与
                    var resolvedName = source.Name;
                    int suffix = 2;
                    while (existingNames.Contains(resolvedName))
                    {
                        resolvedName = $"{source.Name}_{suffix}";
                        suffix++;
                    }

                    // 防御的に新しい Expression を組み立てる
                    var merged = new Expression(
                        id: resolvedId,
                        name: resolvedName,
                        layer: source.Layer,
                        transitionDuration: source.TransitionDuration,
                        transitionCurve: source.TransitionCurve,
                        blendShapeValues: source.BlendShapeValues.ToArray());

                    mergedExpressions.Add(merged);
                    existingIds.Add(resolvedId);
                    existingNames.Add(resolvedName);
                }
            }

            // 新規 Expression が参照するレイヤーのうち、既存 Layers に未定義のものを追記する
            var mergedLayers = new List<LayerDefinition>(existingProfile.Layers.ToArray());
            var existingLayerNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < mergedLayers.Count; i++)
            {
                existingLayerNames.Add(mergedLayers[i].Name);
            }
            if (newExpressions != null)
            {
                int nextPriority = mergedLayers.Count;
                for (int i = 0; i < newExpressions.Length; i++)
                {
                    var layerName = newExpressions[i].Layer;
                    if (string.IsNullOrEmpty(layerName))
                        continue;
                    if (existingLayerNames.Add(layerName))
                    {
                        mergedLayers.Add(new LayerDefinition(layerName, nextPriority, ExclusionMode.LastWins));
                        nextPriority++;
                    }
                }
            }

            // RendererPaths と LayerInputSources は round-trip のため既存値を維持する
            var mergedProfile = new FacialProfile(
                schemaVersion: string.IsNullOrEmpty(existingProfile.SchemaVersion) ? Hidano.FacialControl.Adapters.Json.SystemTextJsonParser.SchemaVersionV2 : existingProfile.SchemaVersion,
                layers: mergedLayers.ToArray(),
                expressions: mergedExpressions.ToArray(),
                rendererPaths: existingProfile.RendererPaths.ToArray(),
                layerInputSources: existingProfile.LayerInputSources.ToArray());

            var mergedJson = _parser.SerializeProfile(mergedProfile);

            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(fullPath, mergedJson, System.Text.Encoding.UTF8);

            // OSC マッピングは同じ JSON 階層の config.json に追記する。
            // config.json は StreamingAssets 配下のプロファイル JSON と同ディレクトリに配置する慣習。
            if (newMappings != null && newMappings.Length > 0)
            {
                MergeOscMappingsIntoSiblingConfig(fullPath, newMappings);
            }

            // Inspector / Project ビュー反映のため AssetDatabase を更新
            EditorUtility.SetDirty(targetProfile);
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// プロファイル JSON と同ディレクトリに配置された config.json に、新規 OSC マッピングを追記する。
        /// config.json が存在しない場合は新規作成する。OscAddress の重複はスキップする。
        /// </summary>
        /// <param name="profileJsonFullPath">プロファイル JSON のフルパス</param>
        /// <param name="newMappings">追記する OSC マッピング配列</param>
        private void MergeOscMappingsIntoSiblingConfig(string profileJsonFullPath, OscMapping[] newMappings)
        {
            var configDir = Path.GetDirectoryName(profileJsonFullPath);
            if (string.IsNullOrEmpty(configDir))
                return;

            var configPath = Path.Combine(configDir, "config.json");

            OscConfiguration existingOsc = default;
            string existingSchemaVersion = "1.0";
            List<OscMapping> mergedMappings;
            HashSet<string> existingAddresses = new HashSet<string>(StringComparer.Ordinal);

            if (File.Exists(configPath))
            {
                var configJson = File.ReadAllText(configPath, System.Text.Encoding.UTF8);
                var existingConfig = _parser.ParseConfig(configJson);
                existingOsc = existingConfig.Osc;
                if (!string.IsNullOrEmpty(existingConfig.SchemaVersion))
                    existingSchemaVersion = existingConfig.SchemaVersion;

                var existingMappingArr = existingOsc.Mapping.ToArray();
                mergedMappings = new List<OscMapping>(existingMappingArr);
                for (int i = 0; i < existingMappingArr.Length; i++)
                {
                    existingAddresses.Add(existingMappingArr[i].OscAddress);
                }
            }
            else
            {
                mergedMappings = new List<OscMapping>();
            }

            // 重複 OSC アドレスはスキップ
            for (int i = 0; i < newMappings.Length; i++)
            {
                if (existingAddresses.Add(newMappings[i].OscAddress))
                {
                    mergedMappings.Add(newMappings[i]);
                }
            }

            // 既存ポート・プリセットを維持してマッピングのみ更新
            var updatedOsc = new OscConfiguration(
                sendPort: File.Exists(configPath) ? existingOsc.SendPort : OscConfiguration.DefaultSendPort,
                receivePort: File.Exists(configPath) ? existingOsc.ReceivePort : OscConfiguration.DefaultReceivePort,
                preset: File.Exists(configPath) ? existingOsc.Preset : OscConfiguration.DefaultPreset,
                mapping: mergedMappings.ToArray());

            var updatedConfig = new FacialControlConfig(existingSchemaVersion, updatedOsc);
            var serialized = _parser.SerializeConfig(updatedConfig);

            File.WriteAllText(configPath, serialized, System.Text.Encoding.UTF8);
        }
    }
}
