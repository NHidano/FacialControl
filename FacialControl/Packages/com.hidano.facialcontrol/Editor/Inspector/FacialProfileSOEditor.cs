using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Hidano.FacialControl.Adapters.FileSystem;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Common;

namespace Hidano.FacialControl.Editor.Inspector
{
    /// <summary>
    /// FacialProfileSO のカスタム Inspector。
    /// UI Toolkit で実装し、JSON ファイルパス表示、JSON 読み込みボタン、
    /// 簡易プロファイル情報、レイヤー詳細一覧、Expression 詳細一覧を表示する。
    /// </summary>
    [CustomEditor(typeof(FacialProfileSO))]
    public class FacialProfileSOEditor : UnityEditor.Editor
    {
        private const string JsonPathSectionLabel = "JSON ファイル";
        private const string ProfileInfoSectionLabel = "プロファイル情報";
        private const string LayerDetailSectionLabel = "レイヤー一覧";
        private const string ExpressionDetailSectionLabel = "Expression 一覧";

        private Label _schemaVersionLabel;
        private Label _layerCountLabel;
        private Label _expressionCountLabel;
        private Label _statusLabel;

        private Foldout _layerDetailFoldout;
        private Foldout _expressionDetailFoldout;

        /// <summary>
        /// JSON 読み込み成功時にキャッシュされた FacialProfile
        /// </summary>
        private FacialProfile? _cachedProfile;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();

            var styleSheet = FacialControlStyles.Load();
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            // ========================================
            // JSON ファイルパスセクション
            // ========================================
            var jsonFoldout = new Foldout { text = JsonPathSectionLabel, value = true };

            var jsonPathField = new PropertyField(
                serializedObject.FindProperty("_jsonFilePath"),
                "JSON ファイルパス");
            jsonPathField.tooltip = "StreamingAssets からの相対パス";
            jsonFoldout.Add(jsonPathField);

            var loadButton = new Button(OnLoadJsonClicked) { text = "JSON 読み込み" };
            loadButton.AddToClassList(FacialControlStyles.ActionButton);
            jsonFoldout.Add(loadButton);

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

            // 初回更新
            root.schedule.Execute(() =>
            {
                UpdateProfileInfo();
                TryLoadCachedProfile();
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
            catch (System.Exception ex)
            {
                _cachedProfile = null;
                ClearDetailUI();
                ShowStatus($"読み込みエラー: {ex.Message}", isError: true);
                Debug.LogError($"[FacialProfileSOEditor] JSON 読み込みエラー: {ex}");
            }
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
            catch (System.Exception)
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
        }

        /// <summary>
        /// レイヤー詳細 UI を構築する
        /// </summary>
        private void BuildLayerDetailUI(FacialProfile profile)
        {
            if (_layerDetailFoldout == null)
                return;

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
                var container = new VisualElement();
                container.style.marginLeft = 8;
                container.style.marginBottom = 4;

                var nameLabel = new Label($"名前: {layer.Name}");
                nameLabel.AddToClassList(FacialControlStyles.InfoLabel);
                container.Add(nameLabel);

                var priorityLabel = new Label($"  優先度: {layer.Priority}");
                priorityLabel.AddToClassList(FacialControlStyles.InfoLabel);
                container.Add(priorityLabel);

                var modeLabel = new Label($"  排他モード: {layer.ExclusionMode}");
                modeLabel.AddToClassList(FacialControlStyles.InfoLabel);
                container.Add(modeLabel);

                _layerDetailFoldout.Add(container);
            }
        }

        /// <summary>
        /// Expression 詳細 UI を構築する
        /// </summary>
        private void BuildExpressionDetailUI(FacialProfile profile)
        {
            if (_expressionDetailFoldout == null)
                return;

            var exprSpan = profile.Expressions.Span;
            if (exprSpan.Length == 0)
            {
                var emptyLabel = new Label("Expression が定義されていません。");
                emptyLabel.AddToClassList(FacialControlStyles.InfoLabel);
                _expressionDetailFoldout.Add(emptyLabel);
                return;
            }

            for (int i = 0; i < exprSpan.Length; i++)
            {
                var expr = exprSpan[i];
                var bsCount = expr.BlendShapeValues.Length;
                var exprFoldout = new Foldout
                {
                    text = $"{expr.Name}  [レイヤー: {expr.Layer} / 遷移: {expr.TransitionDuration:F2}s / BlendShape: {bsCount}]",
                    value = false
                };
                exprFoldout.style.marginLeft = 4;

                // BlendShape 値の一覧を子 Foldout として構築
                var bsSpan = expr.BlendShapeValues.Span;
                if (bsSpan.Length > 0)
                {
                    for (int j = 0; j < bsSpan.Length; j++)
                    {
                        var bs = bsSpan[j];
                        var rendererText = bs.Renderer != null ? $" ({bs.Renderer})" : "";
                        var bsLabel = new Label($"  {bs.Name}: {bs.Value:F3}{rendererText}");
                        bsLabel.AddToClassList(FacialControlStyles.InfoLabel);
                        exprFoldout.Add(bsLabel);
                    }
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
    }
}
