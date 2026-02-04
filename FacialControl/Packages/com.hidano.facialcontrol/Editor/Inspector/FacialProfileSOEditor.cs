using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Hidano.FacialControl.Adapters.FileSystem;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Common;
using Hidano.FacialControl.Editor.Windows;

namespace Hidano.FacialControl.Editor.Inspector
{
    /// <summary>
    /// FacialProfileSO のカスタム Inspector。
    /// UI Toolkit で実装し、JSON ファイルパス表示、JSON 読み込みボタン、
    /// 簡易プロファイル情報、レイヤー詳細一覧、Expression 詳細一覧を表示する。
    /// P17-03: レイヤー・Expression のインライン編集と JSON 上書き保存機能を提供する。
    /// </summary>
    [CustomEditor(typeof(FacialProfileSO))]
    public class FacialProfileSOEditor : UnityEditor.Editor
    {
        private const string JsonPathSectionLabel = "JSON ファイル";
        private const string ProfileInfoSectionLabel = "プロファイル情報";
        private const string LayerDetailSectionLabel = "レイヤー一覧";
        private const string ExpressionDetailSectionLabel = "Expression 一覧";
        private const string ReferenceModelSectionLabel = "参照モデル";

        private Label _schemaVersionLabel;
        private Label _layerCountLabel;
        private Label _expressionCountLabel;
        private Label _statusLabel;

        private Foldout _layerDetailFoldout;
        private Foldout _expressionDetailFoldout;

        /// <summary>
        /// JSON ファイルパス表示用の読み取り専用 Label
        /// </summary>
        private Label _jsonPathLabel;

        /// <summary>
        /// 参照モデルセクションの RendererPaths 一覧表示コンテナ
        /// </summary>
        private VisualElement _rendererPathsContainer;

        /// <summary>
        /// Expression リスト操作セクション（+ / - ボタン）のコンテナ
        /// </summary>
        private VisualElement _expressionListButtonContainer;

        /// <summary>
        /// JSON 読み込み成功時にキャッシュされた FacialProfile
        /// </summary>
        private FacialProfile? _cachedProfile;

        /// <summary>
        /// レイヤー編集データ（UI フィールドから収集する用）
        /// </summary>
        private readonly List<LayerEditData> _layerEdits = new List<LayerEditData>();

        /// <summary>
        /// Expression 編集データ（UI フィールドから収集する用）
        /// </summary>
        private readonly List<ExpressionEditData> _expressionEdits = new List<ExpressionEditData>();

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();

            var styleSheet = FacialControlStyles.Load();
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            // ========================================
            // 新規プロファイル作成ボタン
            // ========================================
            var createProfileButton = new Button(OnCreateNewProfileClicked) { text = "新規プロファイル作成" };
            createProfileButton.AddToClassList(FacialControlStyles.ActionButton);
            createProfileButton.style.marginBottom = 8;
            root.Add(createProfileButton);

            // ========================================
            // JSON ファイルパスセクション
            // ========================================
            var jsonFoldout = new Foldout { text = JsonPathSectionLabel, value = true };

            var so0 = target as FacialProfileSO;
            _jsonPathLabel = new Label($"JSON ファイルパス: {(so0 != null && !string.IsNullOrEmpty(so0.JsonFilePath) ? so0.JsonFilePath : "---")}");
            _jsonPathLabel.tooltip = "StreamingAssets からの相対パス";
            _jsonPathLabel.AddToClassList(FacialControlStyles.InfoLabel);
            jsonFoldout.Add(_jsonPathLabel);

            var jsonPathHelpBox = new HelpBox(
                "パスは StreamingAssets/ からの相対パスです（例: FacialControl/default_profile.json）",
                HelpBoxMessageType.Info);
            jsonFoldout.Add(jsonPathHelpBox);

            var loadButton = new Button(OnLoadJsonClicked) { text = "JSON 読み込み" };
            loadButton.AddToClassList(FacialControlStyles.ActionButton);
            jsonFoldout.Add(loadButton);

            // インポート・エクスポートボタン
            var importExportContainer = new VisualElement();
            importExportContainer.style.flexDirection = FlexDirection.Row;
            importExportContainer.style.marginTop = 4;

            var importButton = new Button(OnImportClicked) { text = "インポート" };
            importButton.AddToClassList(FacialControlStyles.ActionButton);
            importButton.style.flexGrow = 1;
            importExportContainer.Add(importButton);

            var exportButton = new Button(OnExportClicked) { text = "エクスポート" };
            exportButton.AddToClassList(FacialControlStyles.ActionButton);
            exportButton.style.flexGrow = 1;
            importExportContainer.Add(exportButton);

            jsonFoldout.Add(importExportContainer);

            _statusLabel = new Label();
            _statusLabel.AddToClassList(FacialControlStyles.StatusLabel);
            jsonFoldout.Add(_statusLabel);

            root.Add(jsonFoldout);

            // ========================================
            // プロファイル情報セクション（読み取り専用）
            // ========================================
            var infoFoldout = new Foldout { text = ProfileInfoSectionLabel, value = true };

            _schemaVersionLabel = new Label("スキーマバージョン: ---");
            _schemaVersionLabel.AddToClassList(FacialControlStyles.InfoLabel);
            infoFoldout.Add(_schemaVersionLabel);

            _layerCountLabel = new Label("レイヤー数: ---");
            _layerCountLabel.AddToClassList(FacialControlStyles.InfoLabel);
            infoFoldout.Add(_layerCountLabel);

            _expressionCountLabel = new Label("Expression 数: ---");
            _expressionCountLabel.AddToClassList(FacialControlStyles.InfoLabel);
            infoFoldout.Add(_expressionCountLabel);

            root.Add(infoFoldout);

            // ========================================
            // レイヤー詳細セクション
            // ========================================
            _layerDetailFoldout = new Foldout { text = LayerDetailSectionLabel, value = false };
            root.Add(_layerDetailFoldout);

            // ========================================
            // Expression 詳細セクション
            // ========================================
            _expressionDetailFoldout = new Foldout { text = ExpressionDetailSectionLabel, value = false };
            root.Add(_expressionDetailFoldout);

            // + / - ボタン（Unity 標準 List UI パターン）
            _expressionListButtonContainer = new VisualElement();
            _expressionListButtonContainer.style.flexDirection = FlexDirection.Row;
            _expressionListButtonContainer.style.justifyContent = Justify.FlexEnd;
            _expressionListButtonContainer.style.marginTop = 2;
            _expressionListButtonContainer.style.marginBottom = 4;

            var addButton = new Button(OnAddExpressionClicked) { text = "+" };
            addButton.style.width = 24;
            addButton.style.height = 20;
            _expressionListButtonContainer.Add(addButton);

            var removeButton = new Button(OnRemoveLastExpressionClicked) { text = "-" };
            removeButton.style.width = 24;
            removeButton.style.height = 20;
            _expressionListButtonContainer.Add(removeButton);

            root.Add(_expressionListButtonContainer);

            // ========================================
            // 参照モデルセクション
            // ========================================
            var refModelFoldout = new Foldout { text = ReferenceModelSectionLabel, value = true };

            var refModelField = new ObjectField("参照モデル")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = false
            };
            refModelField.BindProperty(serializedObject.FindProperty("_referenceModel"));
            refModelFoldout.Add(refModelField);

            var detectButton = new Button(OnDetectRendererPathsClicked) { text = "RendererPaths 自動検出" };
            detectButton.AddToClassList(FacialControlStyles.ActionButton);
            refModelFoldout.Add(detectButton);

            _rendererPathsContainer = new VisualElement();
            _rendererPathsContainer.style.marginLeft = 8;
            refModelFoldout.Add(_rendererPathsContainer);

            root.Add(refModelFoldout);

            // ========================================
            // JSON に保存ボタン
            // ========================================
            var saveButton = new Button(OnSaveJsonClicked) { text = "JSON に保存" };
            saveButton.AddToClassList(FacialControlStyles.ActionButton);
            root.Add(saveButton);

            // 初回更新
            root.schedule.Execute(() =>
            {
                UpdateProfileInfo();
                TryLoadCachedProfile();
                UpdateRendererPathsDisplay();
            });

            return root;
        }

        /// <summary>
        /// JSON 読み込みボタン押下時の処理。
        /// JSON ファイルを読み込み、SO の表示用フィールドを更新する。
        /// </summary>
        private void OnLoadJsonClicked()
        {
            var so = target as FacialProfileSO;
            if (so == null)
                return;

            if (string.IsNullOrWhiteSpace(so.JsonFilePath))
            {
                ShowStatus("JSON ファイルパスが設定されていません。", isError: true);
                return;
            }

            var fullPath = System.IO.Path.Combine(UnityEngine.Application.streamingAssetsPath, so.JsonFilePath);
            if (!System.IO.File.Exists(fullPath))
            {
                ShowStatus($"ファイルが見つかりません: {fullPath}", isError: true);
                return;
            }

            try
            {
                var json = System.IO.File.ReadAllText(fullPath, System.Text.Encoding.UTF8);
                var parser = new SystemTextJsonParser();
                var profile = parser.ParseProfile(json);

                var mapper = new FacialProfileMapper(
                    new FileProfileRepository(parser));

                Undo.RecordObject(so, "JSON 読み込み");
                mapper.UpdateSO(so, profile);
                EditorUtility.SetDirty(so);
                serializedObject.Update();

                _cachedProfile = profile;

                UpdateProfileInfo();
                RebuildDetailUI();
                ShowStatus("JSON 読み込みに成功しました。", isError: false);
            }
            catch (Exception ex)
            {
                _cachedProfile = null;
                ClearDetailUI();
                ShowStatus($"読み込みエラー: {ex.Message}", isError: true);
                Debug.LogError($"[FacialProfileSOEditor] JSON 読み込みエラー: {ex}");
            }
        }

        /// <summary>
        /// インポートボタン押下時の処理。
        /// 外部 JSON ファイルを選択し、パースして SO の JSON パスへ保存、UI を更新する。
        /// </summary>
        private void OnImportClicked()
        {
            var so = target as FacialProfileSO;
            if (so == null)
                return;

            if (string.IsNullOrWhiteSpace(so.JsonFilePath))
            {
                ShowStatus("JSON ファイルパスが設定されていません。インポート先を指定してください。", isError: true);
                return;
            }

            var importPath = EditorUtility.OpenFilePanel("プロファイル JSON のインポート", "", "json");
            if (string.IsNullOrEmpty(importPath))
                return;

            try
            {
                var json = System.IO.File.ReadAllText(importPath, System.Text.Encoding.UTF8);
                var parser = new SystemTextJsonParser();
                var profile = parser.ParseProfile(json);

                Undo.RecordObject(so, "プロファイルインポート");

                // SO の JSON パスへ保存
                var fullPath = System.IO.Path.Combine(UnityEngine.Application.streamingAssetsPath, so.JsonFilePath);
                var directory = System.IO.Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }
                System.IO.File.WriteAllText(fullPath, json, System.Text.Encoding.UTF8);

                // SO の表示用フィールドを同期更新
                var mapper = new FacialProfileMapper(
                    new FileProfileRepository(parser));
                mapper.UpdateSO(so, profile);
                EditorUtility.SetDirty(so);
                serializedObject.Update();

                _cachedProfile = profile;

                UpdateProfileInfo();
                RebuildDetailUI();
                ShowStatus($"インポートしました: {System.IO.Path.GetFileName(importPath)}", isError: false);
            }
            catch (Exception ex)
            {
                ShowStatus($"インポートエラー: {ex.Message}", isError: true);
                Debug.LogError($"[FacialProfileSOEditor] インポートエラー: {ex}");
            }
        }

        /// <summary>
        /// エクスポートボタン押下時の処理。
        /// 現在のプロファイルをシリアライズして外部 JSON ファイルに書き出す。
        /// </summary>
        private void OnExportClicked()
        {
            if (_cachedProfile == null)
            {
                ShowStatus("プロファイルが読み込まれていません。先に JSON を読み込んでください。", isError: true);
                return;
            }

            var exportPath = EditorUtility.SaveFilePanel("プロファイル JSON のエクスポート", "", "profile.json", "json");
            if (string.IsNullOrEmpty(exportPath))
                return;

            try
            {
                var parser = new SystemTextJsonParser();
                var json = parser.SerializeProfile(_cachedProfile.Value);
                System.IO.File.WriteAllText(exportPath, json, System.Text.Encoding.UTF8);

                ShowStatus($"エクスポートしました: {System.IO.Path.GetFileName(exportPath)}", isError: false);
            }
            catch (Exception ex)
            {
                ShowStatus($"エクスポートエラー: {ex.Message}", isError: true);
                Debug.LogError($"[FacialProfileSOEditor] エクスポートエラー: {ex}");
            }
        }

        /// <summary>
        /// 新規プロファイル作成ボタン押下時の処理。
        /// ProfileCreationDialog を表示し、作成完了後に Inspector を自動更新する。
        /// </summary>
        private void OnCreateNewProfileClicked()
        {
            var dialog = ProfileCreationDialog.ShowDialog();
            dialog.OnCreated += OnProfileCreated;
        }

        /// <summary>
        /// ProfileCreationDialog でプロファイル作成完了時のコールバック。
        /// Inspector を自動更新する。
        /// </summary>
        private void OnProfileCreated(FacialProfileSO createdSo)
        {
            // 現在の Inspector のターゲット SO の JSON を再読み込みして更新
            var so = target as FacialProfileSO;
            if (so == null)
                return;

            // JSON パスが設定されていれば再読み込み
            if (!string.IsNullOrWhiteSpace(so.JsonFilePath))
            {
                serializedObject.Update();
                UpdateProfileInfo();
                TryLoadCachedProfile();
                UpdateRendererPathsDisplay();
            }

            ShowStatus("新規プロファイルが作成されました。", isError: false);
        }

        /// <summary>
        /// JSON に保存ボタン押下時の処理。
        /// 編集済み値で FacialProfile を再構築し、JSON ファイルに上書き保存する。
        /// </summary>
        private void OnSaveJsonClicked()
        {
            var so = target as FacialProfileSO;
            if (so == null)
                return;

            if (_cachedProfile == null)
            {
                ShowStatus("プロファイルが読み込まれていません。先に JSON を読み込んでください。", isError: true);
                return;
            }

            if (string.IsNullOrWhiteSpace(so.JsonFilePath))
            {
                ShowStatus("JSON ファイルパスが設定されていません。", isError: true);
                return;
            }

            var fullPath = System.IO.Path.Combine(UnityEngine.Application.streamingAssetsPath, so.JsonFilePath);

            try
            {
                Undo.RecordObject(so, "JSON に保存");

                var profile = RebuildProfileFromEdits();

                var parser = new SystemTextJsonParser();
                var json = parser.SerializeProfile(profile);
                System.IO.File.WriteAllText(fullPath, json, System.Text.Encoding.UTF8);

                _cachedProfile = profile;

                // SO の表示用フィールドを同期更新
                so.SchemaVersion = profile.SchemaVersion;
                so.LayerCount = profile.Layers.Length;
                so.ExpressionCount = profile.Expressions.Length;
                EditorUtility.SetDirty(so);
                serializedObject.Update();

                UpdateProfileInfo();
                ShowStatus("JSON に保存しました。", isError: false);
            }
            catch (Exception ex)
            {
                ShowStatus($"保存エラー: {ex.Message}", isError: true);
                Debug.LogError($"[FacialProfileSOEditor] JSON 保存エラー: {ex}");
            }
        }

        /// <summary>
        /// Expression 追加ボタン押下時の処理。
        /// デフォルト値で新規 Expression を作成し、JSON 自動保存 → UI 再構築を行う。
        /// </summary>
        private void OnAddExpressionClicked()
        {
            var so = target as FacialProfileSO;
            if (so == null)
                return;

            if (_cachedProfile == null)
            {
                ShowStatus("プロファイルが読み込まれていません。先に JSON を読み込んでください。", isError: true);
                return;
            }

            if (string.IsNullOrWhiteSpace(so.JsonFilePath))
            {
                ShowStatus("JSON ファイルパスが設定されていません。", isError: true);
                return;
            }

            var fullPath = System.IO.Path.Combine(UnityEngine.Application.streamingAssetsPath, so.JsonFilePath);
            var originalProfile = _cachedProfile.Value;

            // 先頭レイヤー名を取得（レイヤーがない場合は "emotion"）
            var layerSpan = originalProfile.Layers.Span;
            string defaultLayer = layerSpan.Length > 0 ? layerSpan[0].Name : "emotion";

            // 新規 Expression を作成
            var newExpression = new Expression(
                Guid.NewGuid().ToString(),
                "New Expression",
                defaultLayer);

            // 既存 Expression 配列に追加
            var existingExpressions = originalProfile.Expressions.ToArray();
            var newExpressions = new Expression[existingExpressions.Length + 1];
            Array.Copy(existingExpressions, newExpressions, existingExpressions.Length);
            newExpressions[existingExpressions.Length] = newExpression;

            // 新しいプロファイルを構築
            var newProfile = new FacialProfile(
                originalProfile.SchemaVersion,
                originalProfile.Layers.ToArray(),
                newExpressions,
                originalProfile.RendererPaths.ToArray());

            try
            {
                Undo.RecordObject(so, "Expression 追加");

                // JSON 自動保存
                var parser = new SystemTextJsonParser();
                var json = parser.SerializeProfile(newProfile);
                System.IO.File.WriteAllText(fullPath, json, System.Text.Encoding.UTF8);

                // キャッシュ更新
                _cachedProfile = newProfile;

                // SO の表示用フィールドを同期更新
                so.SchemaVersion = newProfile.SchemaVersion;
                so.LayerCount = newProfile.Layers.Length;
                so.ExpressionCount = newProfile.Expressions.Length;
                EditorUtility.SetDirty(so);
                serializedObject.Update();

                UpdateProfileInfo();
                RebuildDetailUI();
                ShowStatus("Expression を追加しました。", isError: false);
            }
            catch (Exception ex)
            {
                ShowStatus($"Expression 追加エラー: {ex.Message}", isError: true);
                Debug.LogError($"[FacialProfileSOEditor] Expression 追加エラー: {ex}");
            }
        }

        /// <summary>
        /// Expression 削除ボタン押下時の処理。
        /// 確認ダイアログ表示後に削除し、JSON 自動保存 → UI 再構築を行う。
        /// </summary>
        private void OnDeleteExpressionClicked(int index)
        {
            var so = target as FacialProfileSO;
            if (so == null)
                return;

            if (_cachedProfile == null)
            {
                ShowStatus("プロファイルが読み込まれていません。先に JSON を読み込んでください。", isError: true);
                return;
            }

            if (string.IsNullOrWhiteSpace(so.JsonFilePath))
            {
                ShowStatus("JSON ファイルパスが設定されていません。", isError: true);
                return;
            }

            var originalProfile = _cachedProfile.Value;
            var existingExpressions = originalProfile.Expressions.ToArray();

            if (index < 0 || index >= existingExpressions.Length)
            {
                ShowStatus("無効な Expression インデックスです。", isError: true);
                return;
            }

            var expressionName = existingExpressions[index].Name;

            // 確認ダイアログ
            if (!EditorUtility.DisplayDialog(
                "Expression 削除",
                $"Expression「{expressionName}」を削除しますか？",
                "削除",
                "キャンセル"))
            {
                return;
            }

            var fullPath = System.IO.Path.Combine(UnityEngine.Application.streamingAssetsPath, so.JsonFilePath);

            // 削除後の Expression 配列を構築
            var newExpressions = new Expression[existingExpressions.Length - 1];
            int destIndex = 0;
            for (int i = 0; i < existingExpressions.Length; i++)
            {
                if (i != index)
                {
                    newExpressions[destIndex] = existingExpressions[i];
                    destIndex++;
                }
            }

            // 新しいプロファイルを構築
            var newProfile = new FacialProfile(
                originalProfile.SchemaVersion,
                originalProfile.Layers.ToArray(),
                newExpressions,
                originalProfile.RendererPaths.ToArray());

            try
            {
                Undo.RecordObject(so, "Expression 削除");

                // JSON 自動保存
                var parser = new SystemTextJsonParser();
                var json = parser.SerializeProfile(newProfile);
                System.IO.File.WriteAllText(fullPath, json, System.Text.Encoding.UTF8);

                // キャッシュ更新
                _cachedProfile = newProfile;

                // SO の表示用フィールドを同期更新
                so.SchemaVersion = newProfile.SchemaVersion;
                so.LayerCount = newProfile.Layers.Length;
                so.ExpressionCount = newProfile.Expressions.Length;
                EditorUtility.SetDirty(so);
                serializedObject.Update();

                UpdateProfileInfo();
                RebuildDetailUI();
                ShowStatus($"Expression「{expressionName}」を削除しました。", isError: false);
            }
            catch (Exception ex)
            {
                ShowStatus($"Expression 削除エラー: {ex.Message}", isError: true);
                Debug.LogError($"[FacialProfileSOEditor] Expression 削除エラー: {ex}");
            }
        }

        /// <summary>
        /// `-` ボタン押下時の処理。
        /// 末尾の Expression を削除し、JSON 自動保存 → UI 再構築を行う。
        /// </summary>
        private void OnRemoveLastExpressionClicked()
        {
            if (_cachedProfile == null)
            {
                ShowStatus("プロファイルが読み込まれていません。先に JSON を読み込んでください。", isError: true);
                return;
            }

            var originalProfile = _cachedProfile.Value;
            var existingExpressions = originalProfile.Expressions.ToArray();

            if (existingExpressions.Length == 0)
            {
                ShowStatus("削除する Expression がありません。", isError: true);
                return;
            }

            // 末尾の Expression を削除
            OnDeleteExpressionClicked(existingExpressions.Length - 1);
        }

        /// <summary>
        /// 編集データから FacialProfile を再構築する
        /// </summary>
        private FacialProfile RebuildProfileFromEdits()
        {
            var originalProfile = _cachedProfile.Value;

            // レイヤー再構築
            var layers = new LayerDefinition[_layerEdits.Count];
            for (int i = 0; i < _layerEdits.Count; i++)
            {
                var edit = _layerEdits[i];
                layers[i] = new LayerDefinition(edit.Name, edit.Priority, edit.ExclusionMode);
            }

            // Expression 再構築
            // OriginalIndex を使って編集済みデータと元データを正しくマージする。
            var originalExpressions = originalProfile.Expressions.Span;

            // 編集データを OriginalIndex でルックアップするマップを構築
            var editMap = new Dictionary<int, ExpressionEditData>();
            foreach (var edit in _expressionEdits)
            {
                editMap[edit.OriginalIndex] = edit;
            }

            var expressions = new Expression[originalExpressions.Length];
            for (int i = 0; i < originalExpressions.Length; i++)
            {
                var orig = originalExpressions[i];
                var blendShapes = orig.BlendShapeValues.ToArray();
                var layerSlots = orig.LayerSlots.ToArray();
                var transitionCurve = orig.TransitionCurve;

                if (editMap.TryGetValue(i, out var editData))
                {
                    // 編集データがある場合は反映
                    // BlendShape 名の編集が行われている場合は反映
                    if (editData.BlendShapeNameEdits != null && blendShapes != null)
                    {
                        for (int j = 0; j < blendShapes.Length && j < editData.BlendShapeNameEdits.Length; j++)
                        {
                            if (blendShapes[j].Name != editData.BlendShapeNameEdits[j])
                            {
                                blendShapes[j] = new BlendShapeMapping(
                                    editData.BlendShapeNameEdits[j],
                                    blendShapes[j].Value,
                                    blendShapes[j].Renderer);
                            }
                        }
                    }

                    expressions[i] = new Expression(
                        editData.Id,
                        editData.Name,
                        editData.Layer,
                        editData.TransitionDuration,
                        transitionCurve,
                        blendShapes,
                        layerSlots);
                }
                else
                {
                    // 編集データがない場合は元データを維持
                    expressions[i] = new Expression(
                        orig.Id,
                        orig.Name,
                        orig.Layer,
                        orig.TransitionDuration,
                        transitionCurve,
                        blendShapes,
                        layerSlots);
                }
            }

            return new FacialProfile(originalProfile.SchemaVersion, layers, expressions, originalProfile.RendererPaths.ToArray());
        }

        /// <summary>
        /// Inspector 初回表示時に JSON からプロファイルをキャッシュする
        /// </summary>
        private void TryLoadCachedProfile()
        {
            var so = target as FacialProfileSO;
            if (so == null)
                return;

            if (string.IsNullOrWhiteSpace(so.JsonFilePath))
                return;

            var fullPath = System.IO.Path.Combine(UnityEngine.Application.streamingAssetsPath, so.JsonFilePath);
            if (!System.IO.File.Exists(fullPath))
                return;

            try
            {
                var json = System.IO.File.ReadAllText(fullPath, System.Text.Encoding.UTF8);
                var parser = new SystemTextJsonParser();
                _cachedProfile = parser.ParseProfile(json);
                RebuildDetailUI();
            }
            catch (Exception)
            {
                _cachedProfile = null;
            }
        }

        /// <summary>
        /// SO のフィールドからプロファイル情報表示を更新する
        /// </summary>
        private void UpdateProfileInfo()
        {
            var so = target as FacialProfileSO;
            if (so == null)
                return;

            // JSON ファイルパス表示を更新
            if (_jsonPathLabel != null)
            {
                string pathText = !string.IsNullOrEmpty(so.JsonFilePath) ? so.JsonFilePath : "---";
                _jsonPathLabel.text = $"JSON ファイルパス: {pathText}";
            }

            string version = !string.IsNullOrEmpty(so.SchemaVersion)
                ? so.SchemaVersion
                : "---";
            int layers = so.LayerCount;
            int expressions = so.ExpressionCount;

            if (_schemaVersionLabel != null)
                _schemaVersionLabel.text = $"スキーマバージョン: {version}";
            if (_layerCountLabel != null)
                _layerCountLabel.text = $"レイヤー数: {layers}";
            if (_expressionCountLabel != null)
                _expressionCountLabel.text = $"Expression 数: {expressions}";
        }

        /// <summary>
        /// キャッシュされた FacialProfile からレイヤー詳細・Expression 詳細 UI を再構築する
        /// </summary>
        private void RebuildDetailUI()
        {
            ClearDetailUI();

            if (_cachedProfile == null)
                return;

            var profile = _cachedProfile.Value;
            BuildLayerDetailUI(profile);
            BuildExpressionDetailUI(profile);
        }

        /// <summary>
        /// レイヤー詳細・Expression 詳細 UI をクリアする
        /// </summary>
        private void ClearDetailUI()
        {
            _layerDetailFoldout?.Clear();
            _expressionDetailFoldout?.Clear();
            _layerEdits.Clear();
            _expressionEdits.Clear();
        }

        /// <summary>
        /// レイヤー詳細 UI を編集可能フィールドで構築する
        /// </summary>
        private void BuildLayerDetailUI(FacialProfile profile)
        {
            if (_layerDetailFoldout == null)
                return;

            _layerEdits.Clear();

            var layerSpan = profile.Layers.Span;
            if (layerSpan.Length == 0)
            {
                var emptyLabel = new Label("レイヤーが定義されていません。");
                emptyLabel.AddToClassList(FacialControlStyles.InfoLabel);
                _layerDetailFoldout.Add(emptyLabel);
                return;
            }

            for (int i = 0; i < layerSpan.Length; i++)
            {
                var layer = layerSpan[i];
                var editData = new LayerEditData(layer.Name, layer.Priority, layer.ExclusionMode);
                _layerEdits.Add(editData);

                var container = new VisualElement();
                container.style.marginLeft = 8;
                container.style.marginBottom = 4;

                var capturedIndex = i;

                // 名前: TextField
                var nameField = new TextField("名前") { value = layer.Name };
                nameField.RegisterValueChangedCallback(evt =>
                {
                    if (capturedIndex < _layerEdits.Count)
                        _layerEdits[capturedIndex].Name = evt.newValue;
                });
                container.Add(nameField);

                // 優先度: IntegerField
                var priorityField = new IntegerField("優先度") { value = layer.Priority };
                priorityField.RegisterValueChangedCallback(evt =>
                {
                    if (capturedIndex < _layerEdits.Count)
                        _layerEdits[capturedIndex].Priority = evt.newValue;
                });
                container.Add(priorityField);

                // 排他モード: EnumField
                var modeField = new EnumField("排他モード", layer.ExclusionMode);
                modeField.RegisterValueChangedCallback(evt =>
                {
                    if (capturedIndex < _layerEdits.Count && evt.newValue is ExclusionMode mode)
                        _layerEdits[capturedIndex].ExclusionMode = mode;
                });
                container.Add(modeField);

                _layerDetailFoldout.Add(container);
            }
        }

        /// <summary>
        /// Expression 詳細 UI を編集可能フィールドで構築する
        /// </summary>
        private void BuildExpressionDetailUI(FacialProfile profile)
        {
            if (_expressionDetailFoldout == null)
                return;

            _expressionEdits.Clear();

            var exprSpan = profile.Expressions.Span;
            if (exprSpan.Length == 0)
            {
                var emptyLabel = new Label("Expression が定義されていません。");
                emptyLabel.AddToClassList(FacialControlStyles.InfoLabel);
                _expressionDetailFoldout.Add(emptyLabel);
                return;
            }

            // レイヤー名リストを構築（ドロップダウン用）
            var layerNames = new List<string>();
            var layerSpan = profile.Layers.Span;
            for (int i = 0; i < layerSpan.Length; i++)
            {
                layerNames.Add(layerSpan[i].Name);
            }

            for (int i = 0; i < exprSpan.Length; i++)
            {
                var expr = exprSpan[i];

                var editData = new ExpressionEditData(
                    expr.Id, expr.Name, expr.Layer, expr.TransitionDuration, i);
                _expressionEdits.Add(editData);
                var editListIndex = _expressionEdits.Count - 1;

                var bsCount = expr.BlendShapeValues.Length;
                var exprFoldout = new Foldout
                {
                    text = $"{expr.Name}  [レイヤー: {expr.Layer} / 遷移: {expr.TransitionDuration:F2}s / BlendShape: {bsCount}]",
                    value = false
                };
                exprFoldout.style.marginLeft = 4;

                var capturedIndex = editListIndex;
                var capturedFoldout = exprFoldout;

                // 名前: TextField
                var nameField = new TextField("名前") { value = expr.Name };
                nameField.RegisterValueChangedCallback(evt =>
                {
                    if (capturedIndex < _expressionEdits.Count)
                    {
                        _expressionEdits[capturedIndex].Name = evt.newValue;
                        UpdateExpressionFoldoutText(capturedFoldout, capturedIndex);
                    }
                });
                exprFoldout.Add(nameField);

                // レイヤー: DropdownField
                var layerField = new DropdownField("レイヤー", layerNames, GetLayerIndex(layerNames, expr.Layer));
                layerField.RegisterValueChangedCallback(evt =>
                {
                    if (capturedIndex < _expressionEdits.Count)
                    {
                        _expressionEdits[capturedIndex].Layer = evt.newValue;
                        UpdateExpressionFoldoutText(capturedFoldout, capturedIndex);
                    }
                });
                exprFoldout.Add(layerField);

                // 遷移時間: FloatField
                var durationField = new FloatField("遷移時間 (秒)") { value = expr.TransitionDuration };
                durationField.RegisterValueChangedCallback(evt =>
                {
                    if (capturedIndex < _expressionEdits.Count)
                    {
                        _expressionEdits[capturedIndex].TransitionDuration = evt.newValue;
                        UpdateExpressionFoldoutText(capturedFoldout, capturedIndex);
                    }
                });
                exprFoldout.Add(durationField);

                // BlendShape 値の一覧
                var bsSpan = expr.BlendShapeValues.Span;
                if (bsSpan.Length > 0)
                {
                    var bsFoldout = new Foldout
                    {
                        text = $"BlendShape 値 ({bsSpan.Length})",
                        value = false
                    };

                    // 参照モデルが設定されている場合は BlendShape 名をドロップダウンで選択可能にする
                    var so = target as FacialProfileSO;
                    var allBlendShapeNames = (so != null) ? CollectBlendShapeNames(so.ReferenceModel) : null;
                    var hasReferenceModel = allBlendShapeNames != null && allBlendShapeNames.Count > 0;

                    if (hasReferenceModel)
                    {
                        // BlendShape 名編集データを初期化
                        var nameEdits = new string[bsSpan.Length];
                        for (int j = 0; j < bsSpan.Length; j++)
                        {
                            nameEdits[j] = bsSpan[j].Name;
                        }
                        editData.BlendShapeNameEdits = nameEdits;
                    }

                    for (int j = 0; j < bsSpan.Length; j++)
                    {
                        var bs = bsSpan[j];
                        var rendererText = bs.Renderer != null ? $" ({bs.Renderer})" : "";

                        if (hasReferenceModel)
                        {
                            // ドロップダウンで BlendShape 名を選択可能
                            var bsContainer = new VisualElement();
                            bsContainer.style.flexDirection = FlexDirection.Row;
                            bsContainer.style.alignItems = Align.Center;
                            bsContainer.style.marginLeft = 8;

                            var capturedExprIndex = capturedIndex;
                            var capturedBsIndex = j;

                            var dropdownChoices = new List<string>(allBlendShapeNames);
                            var initialIndex = dropdownChoices.IndexOf(bs.Name);
                            if (initialIndex < 0)
                            {
                                // 参照モデルに存在しない BlendShape 名は選択肢に追加
                                dropdownChoices.Insert(0, bs.Name);
                                initialIndex = 0;
                            }
                            var dropdown = new DropdownField(
                                dropdownChoices,
                                initialIndex);
                            dropdown.style.flexGrow = 1;
                            dropdown.RegisterValueChangedCallback(evt =>
                            {
                                if (capturedExprIndex < _expressionEdits.Count)
                                {
                                    var edits = _expressionEdits[capturedExprIndex].BlendShapeNameEdits;
                                    if (edits != null && capturedBsIndex < edits.Length)
                                    {
                                        edits[capturedBsIndex] = evt.newValue;
                                    }
                                }
                            });
                            bsContainer.Add(dropdown);

                            var valueLabel = new Label($": {bs.Value:F3}{rendererText}");
                            valueLabel.AddToClassList(FacialControlStyles.InfoLabel);
                            bsContainer.Add(valueLabel);

                            bsFoldout.Add(bsContainer);
                        }
                        else
                        {
                            // 従来通り Label で読み取り専用表示
                            var bsLabel = new Label($"  {bs.Name}: {bs.Value:F3}{rendererText}");
                            bsLabel.AddToClassList(FacialControlStyles.InfoLabel);
                            bsFoldout.Add(bsLabel);
                        }
                    }

                    exprFoldout.Add(bsFoldout);
                }
                else
                {
                    var noBsLabel = new Label("  BlendShape 値なし");
                    noBsLabel.AddToClassList(FacialControlStyles.InfoLabel);
                    exprFoldout.Add(noBsLabel);
                }

                _expressionDetailFoldout.Add(exprFoldout);
            }
        }

        /// <summary>
        /// Expression Foldout のヘッダーテキストを更新する
        /// </summary>
        private void UpdateExpressionFoldoutText(Foldout foldout, int index)
        {
            if (index >= _expressionEdits.Count)
                return;

            var edit = _expressionEdits[index];
            var originalProfile = _cachedProfile.Value;
            var exprSpan = originalProfile.Expressions.Span;
            int bsCount = edit.OriginalIndex < exprSpan.Length ? exprSpan[edit.OriginalIndex].BlendShapeValues.Length : 0;

            foldout.text = $"{edit.Name}  [レイヤー: {edit.Layer} / 遷移: {edit.TransitionDuration:F2}s / BlendShape: {bsCount}]";
        }

        /// <summary>
        /// レイヤー名リストからインデックスを取得する。見つからない場合は 0。
        /// </summary>
        private static int GetLayerIndex(List<string> layerNames, string layer)
        {
            if (layerNames.Count == 0)
                return -1;

            for (int i = 0; i < layerNames.Count; i++)
            {
                if (layerNames[i] == layer)
                    return i;
            }

            return 0;
        }

        /// <summary>
        /// 参照モデルから RendererPaths を自動検出する。
        /// 全 SkinnedMeshRenderer のモデルルートからの相対パスを算出し、SO に設定する。
        /// </summary>
        private void OnDetectRendererPathsClicked()
        {
            var so = target as FacialProfileSO;
            if (so == null)
                return;

            if (so.ReferenceModel == null)
            {
                ShowStatus("参照モデルが設定されていません。", isError: true);
                return;
            }

            var renderers = so.ReferenceModel.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 0)
            {
                ShowStatus("参照モデルに SkinnedMeshRenderer が見つかりません。", isError: true);
                return;
            }

            var rootTransform = so.ReferenceModel.transform;
            var paths = new string[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                paths[i] = GetRelativePath(rootTransform, renderers[i].transform);
            }

            Undo.RecordObject(so, "RendererPaths 自動検出");
            so.RendererPaths = paths;
            EditorUtility.SetDirty(so);

            UpdateRendererPathsDisplay();
            ShowStatus($"RendererPaths を検出しました: {paths.Length} 件", isError: false);
        }

        /// <summary>
        /// ルート Transform からターゲット Transform への相対パスを算出する
        /// </summary>
        private static string GetRelativePath(Transform root, Transform target)
        {
            if (target == root)
                return "";

            var parts = new System.Collections.Generic.List<string>();
            var current = target;
            while (current != null && current != root)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>
        /// RendererPaths 一覧表示を更新する
        /// </summary>
        private void UpdateRendererPathsDisplay()
        {
            if (_rendererPathsContainer == null)
                return;

            _rendererPathsContainer.Clear();

            var so = target as FacialProfileSO;
            if (so == null)
                return;

            var paths = so.RendererPaths;
            if (paths == null || paths.Length == 0)
            {
                var emptyLabel = new Label("RendererPaths が設定されていません。");
                emptyLabel.AddToClassList(FacialControlStyles.InfoLabel);
                _rendererPathsContainer.Add(emptyLabel);
                return;
            }

            var headerLabel = new Label($"RendererPaths ({paths.Length} 件)");
            headerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            headerLabel.style.marginBottom = 4;
            _rendererPathsContainer.Add(headerLabel);

            // 参照モデルがある場合は BlendShape 数も表示
            GameObject refModel = so.ReferenceModel;

            for (int i = 0; i < paths.Length; i++)
            {
                int blendShapeCount = 0;
                if (refModel != null)
                {
                    var rendererTransform = refModel.transform.Find(paths[i]);
                    if (rendererTransform != null)
                    {
                        var smr = rendererTransform.GetComponent<SkinnedMeshRenderer>();
                        if (smr != null && smr.sharedMesh != null)
                        {
                            blendShapeCount = smr.sharedMesh.blendShapeCount;
                        }
                    }
                }

                string displayText = refModel != null
                    ? $"  {paths[i]}  (BlendShape: {blendShapeCount})"
                    : $"  {paths[i]}";
                var pathLabel = new Label(displayText);
                pathLabel.AddToClassList(FacialControlStyles.InfoLabel);
                _rendererPathsContainer.Add(pathLabel);
            }
        }

        /// <summary>
        /// 参照モデルの全 SkinnedMeshRenderer から BlendShape 名を収集する（重複排除、ソート済み）。
        /// 参照モデルが null の場合は null を返す。
        /// </summary>
        private static List<string> CollectBlendShapeNames(GameObject referenceModel)
        {
            if (referenceModel == null)
                return null;

            var renderers = referenceModel.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 0)
                return null;

            var nameSet = new HashSet<string>();
            foreach (var smr in renderers)
            {
                if (smr.sharedMesh == null)
                    continue;

                var mesh = smr.sharedMesh;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    nameSet.Add(mesh.GetBlendShapeName(i));
                }
            }

            if (nameSet.Count == 0)
                return null;

            var sorted = new List<string>(nameSet);
            sorted.Sort(StringComparer.Ordinal);
            return sorted;
        }

        /// <summary>
        /// ステータスメッセージを表示する
        /// </summary>
        private void ShowStatus(string message, bool isError)
        {
            if (_statusLabel == null)
                return;

            _statusLabel.text = message;

            _statusLabel.RemoveFromClassList(FacialControlStyles.StatusError);
            _statusLabel.RemoveFromClassList(FacialControlStyles.StatusSuccess);
            _statusLabel.AddToClassList(isError
                ? FacialControlStyles.StatusError
                : FacialControlStyles.StatusSuccess);

            _statusLabel.style.display = DisplayStyle.Flex;
        }

        /// <summary>
        /// レイヤー編集データ
        /// </summary>
        private class LayerEditData
        {
            public string Name;
            public int Priority;
            public ExclusionMode ExclusionMode;

            public LayerEditData(string name, int priority, ExclusionMode exclusionMode)
            {
                Name = name;
                Priority = priority;
                ExclusionMode = exclusionMode;
            }
        }

        /// <summary>
        /// Expression 編集データ
        /// </summary>
        private class ExpressionEditData
        {
            public string Id;
            public string Name;
            public string Layer;
            public float TransitionDuration;

            /// <summary>
            /// オリジナルの Expression 配列内でのインデックス
            /// </summary>
            public int OriginalIndex;

            /// <summary>
            /// BlendShape 名の編集データ。null の場合は元データを維持。
            /// </summary>
            public string[] BlendShapeNameEdits;

            public ExpressionEditData(string id, string name, string layer, float transitionDuration, int originalIndex)
            {
                Id = id;
                Name = name;
                Layer = layer;
                TransitionDuration = transitionDuration;
                OriginalIndex = originalIndex;
            }
        }
    }
}
