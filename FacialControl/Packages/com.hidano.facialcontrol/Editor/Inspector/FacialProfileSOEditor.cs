using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Hidano.FacialControl.Adapters.FileSystem;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Adapters.ScriptableObject;

namespace Hidano.FacialControl.Editor.Inspector
{
    /// <summary>
    /// FacialProfileSO のカスタム Inspector。
    /// UI Toolkit で実装し、JSON ファイルパス表示、JSON 読み込みボタン、
    /// 簡易プロファイル情報を表示する。
    /// </summary>
    [CustomEditor(typeof(FacialProfileSO))]
    public class FacialProfileSOEditor : UnityEditor.Editor
    {
        private const string JsonPathSectionLabel = "JSON ファイル";
        private const string ProfileInfoSectionLabel = "プロファイル情報";

        private Label _schemaVersionLabel;
        private Label _layerCountLabel;
        private Label _expressionCountLabel;
        private Label _statusLabel;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();

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
            loadButton.style.marginTop = 4;
            jsonFoldout.Add(loadButton);

            _statusLabel = new Label();
            _statusLabel.style.marginLeft = 4;
            _statusLabel.style.marginTop = 2;
            _statusLabel.style.display = DisplayStyle.None;
            jsonFoldout.Add(_statusLabel);

            root.Add(jsonFoldout);

            // ========================================
            // プロファイル情報セクション（読み取り専用）
            // ========================================
            var infoFoldout = new Foldout { text = ProfileInfoSectionLabel, value = true };

            _schemaVersionLabel = new Label("スキーマバージョン: ---");
            _schemaVersionLabel.style.marginLeft = 4;
            infoFoldout.Add(_schemaVersionLabel);

            _layerCountLabel = new Label("レイヤー数: ---");
            _layerCountLabel.style.marginLeft = 4;
            infoFoldout.Add(_layerCountLabel);

            _expressionCountLabel = new Label("Expression 数: ---");
            _expressionCountLabel.style.marginLeft = 4;
            infoFoldout.Add(_expressionCountLabel);

            root.Add(infoFoldout);

            // 初回更新
            root.schedule.Execute(UpdateProfileInfo);

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

                UpdateProfileInfo();
                ShowStatus("JSON 読み込みに成功しました。", isError: false);
            }
            catch (System.Exception ex)
            {
                ShowStatus($"読み込みエラー: {ex.Message}", isError: true);
                Debug.LogError($"[FacialProfileSOEditor] JSON 読み込みエラー: {ex}");
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
        /// ステータスメッセージを表示する
        /// </summary>
        private void ShowStatus(string message, bool isError)
        {
            if (_statusLabel == null)
                return;

            _statusLabel.text = message;
            _statusLabel.style.color = isError
                ? new Color(1f, 0.3f, 0.3f)
                : new Color(0.3f, 0.8f, 0.3f);
            _statusLabel.style.display = DisplayStyle.Flex;
        }
    }
}
