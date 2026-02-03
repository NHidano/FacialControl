using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Common;

namespace Hidano.FacialControl.Editor.Windows
{
    /// <summary>
    /// 新規プロファイル作成ダイアログ。
    /// プロファイル名とレイヤー定義を設定して新規プロファイルを作成する。
    /// </summary>
    public class ProfileCreationDialog : EditorWindow
    {
        /// <summary>
        /// プロファイル作成完了時に呼び出されるイベント。
        /// 作成された FacialProfileSO を引数として渡す。
        /// </summary>
        public event Action<FacialProfileSO> OnCreated;

        private TextField _profileNameField;
        private ScrollView _layerListView;
        private Button _addLayerButton;
        private Button _createButton;
        private Label _statusLabel;

        private List<ProfileCreationData.LayerEntry> _layers;
        private List<VisualElement> _layerElements;

        private IJsonParser _parser;

        /// <summary>
        /// ダイアログを表示する
        /// </summary>
        public static ProfileCreationDialog ShowDialog()
        {
            var window = CreateInstance<ProfileCreationDialog>();
            window.titleContent = new GUIContent("新規プロファイル作成");
            window.ShowUtility();
            window.minSize = new Vector2(400, 380);
            window.maxSize = new Vector2(550, 600);
            return window;
        }

        private void OnEnable()
        {
            _parser = new SystemTextJsonParser();
            _layers = new List<ProfileCreationData.LayerEntry>
            {
                new ProfileCreationData.LayerEntry("emotion", 0, ExclusionMode.LastWins),
                new ProfileCreationData.LayerEntry("lipsync", 1, ExclusionMode.Blend),
                new ProfileCreationData.LayerEntry("eye", 2, ExclusionMode.LastWins)
            };
            _layerElements = new List<VisualElement>();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;

            var styleSheet = FacialControlStyles.Load();
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            root.style.paddingLeft = 8;
            root.style.paddingRight = 8;
            root.style.paddingTop = 8;
            root.style.paddingBottom = 8;

            // ========================================
            // プロファイル名
            // ========================================
            _profileNameField = new TextField("プロファイル名");
            _profileNameField.tooltip = "プロファイル名は JSON ファイル名にも使用されます";
            root.Add(_profileNameField);

            // ========================================
            // レイヤー定義セクション
            // ========================================
            var layerHeader = new VisualElement();
            layerHeader.style.flexDirection = FlexDirection.Row;
            layerHeader.style.justifyContent = Justify.SpaceBetween;
            layerHeader.style.marginTop = 8;
            layerHeader.style.marginBottom = 4;

            var layerLabel = new Label("レイヤー定義");
            layerLabel.style.fontSize = 13;
            layerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            layerHeader.Add(layerLabel);

            _addLayerButton = new Button(OnAddLayerClicked) { text = "レイヤー追加" };
            _addLayerButton.AddToClassList(FacialControlStyles.ActionButton);
            layerHeader.Add(_addLayerButton);

            root.Add(layerHeader);

            _layerListView = new ScrollView(ScrollViewMode.Vertical);
            _layerListView.style.flexGrow = 1;
            _layerListView.style.maxHeight = 300;
            root.Add(_layerListView);

            RebuildLayerList();

            // ========================================
            // ボタンセクション
            // ========================================
            var buttonSection = new VisualElement();
            buttonSection.style.flexDirection = FlexDirection.Row;
            buttonSection.style.justifyContent = Justify.FlexEnd;
            buttonSection.style.marginTop = 8;

            var cancelButton = new Button(Close) { text = "キャンセル" };
            buttonSection.Add(cancelButton);

            _createButton = new Button(OnCreateClicked) { text = "作成" };
            _createButton.AddToClassList(FacialControlStyles.ActionButton);
            _createButton.style.marginLeft = 4;
            buttonSection.Add(_createButton);

            root.Add(buttonSection);

            // ========================================
            // ステータスラベル
            // ========================================
            _statusLabel = new Label();
            _statusLabel.AddToClassList(FacialControlStyles.StatusLabel);
            _statusLabel.style.marginTop = 4;
            root.Add(_statusLabel);
        }

        /// <summary>
        /// レイヤーリスト UI を再構築する
        /// </summary>
        private void RebuildLayerList()
        {
            _layerListView.Clear();
            _layerElements.Clear();

            for (int i = 0; i < _layers.Count; i++)
            {
                var layerItem = CreateLayerItem(i);
                _layerListView.Add(layerItem);
                _layerElements.Add(layerItem);
            }
        }

        /// <summary>
        /// 個々のレイヤー項目 UI を生成する
        /// </summary>
        private VisualElement CreateLayerItem(int index)
        {
            var container = new VisualElement();
            container.style.marginBottom = 4;
            container.style.paddingTop = 4;
            container.style.paddingBottom = 4;
            container.style.paddingLeft = 4;
            container.style.paddingRight = 4;
            container.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f, 1f);
            container.style.borderBottomLeftRadius = 3;
            container.style.borderBottomRightRadius = 3;
            container.style.borderTopLeftRadius = 3;
            container.style.borderTopRightRadius = 3;

            var layer = _layers[index];

            // 名前フィールド + 削除ボタン
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;

            var nameField = new TextField("名前") { value = layer.Name };
            nameField.style.flexGrow = 1;
            var capturedIndex = index;
            nameField.RegisterValueChangedCallback(evt =>
            {
                if (capturedIndex < _layers.Count)
                    _layers[capturedIndex].Name = evt.newValue;
            });
            headerRow.Add(nameField);

            var deleteButton = new Button(() => OnDeleteLayerClicked(capturedIndex)) { text = "削除" };
            deleteButton.style.width = 40;
            deleteButton.style.height = 22;
            deleteButton.style.marginLeft = 4;
            headerRow.Add(deleteButton);

            container.Add(headerRow);

            // Priority と ExclusionMode
            var detailRow = new VisualElement();
            detailRow.style.flexDirection = FlexDirection.Row;
            detailRow.style.marginTop = 2;

            var priorityField = new IntegerField("優先度") { value = layer.Priority };
            priorityField.style.flexGrow = 1;
            priorityField.RegisterValueChangedCallback(evt =>
            {
                if (capturedIndex < _layers.Count)
                    _layers[capturedIndex].Priority = Math.Max(0, evt.newValue);
            });
            detailRow.Add(priorityField);

            var exclusionField = new EnumField("排他モード", layer.ExclusionMode);
            exclusionField.style.flexGrow = 1;
            exclusionField.style.marginLeft = 4;
            exclusionField.RegisterValueChangedCallback(evt =>
            {
                if (capturedIndex < _layers.Count)
                    _layers[capturedIndex].ExclusionMode = (ExclusionMode)evt.newValue;
            });
            detailRow.Add(exclusionField);

            container.Add(detailRow);

            return container;
        }

        /// <summary>
        /// レイヤー追加ボタン押下時の処理
        /// </summary>
        private void OnAddLayerClicked()
        {
            int newPriority = _layers.Count;
            _layers.Add(new ProfileCreationData.LayerEntry(
                "new_layer", newPriority, ExclusionMode.LastWins));
            RebuildLayerList();
        }

        /// <summary>
        /// レイヤー削除ボタン押下時の処理
        /// </summary>
        private void OnDeleteLayerClicked(int index)
        {
            if (_layers.Count <= 1)
            {
                ShowDialogStatus("レイヤーは最低 1 つ必要です。", isError: true);
                return;
            }

            if (index >= 0 && index < _layers.Count)
            {
                _layers.RemoveAt(index);
                RebuildLayerList();
            }
        }

        /// <summary>
        /// 作成ボタン押下時の処理
        /// </summary>
        private void OnCreateClicked()
        {
            // バリデーション
            var profileName = _profileNameField.value;
            if (string.IsNullOrWhiteSpace(profileName))
            {
                ShowDialogStatus("プロファイル名を入力してください。", isError: true);
                return;
            }

            // レイヤー名の重複チェック
            var layerNames = new HashSet<string>();
            for (int i = 0; i < _layers.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(_layers[i].Name))
                {
                    ShowDialogStatus($"レイヤー {i + 1} の名前を入力してください。", isError: true);
                    return;
                }
                if (!layerNames.Add(_layers[i].Name))
                {
                    ShowDialogStatus($"レイヤー名が重複しています: {_layers[i].Name}", isError: true);
                    return;
                }
            }

            try
            {
                var data = new ProfileCreationData(profileName, _layers.ToArray());
                var profile = data.BuildProfile();

                // JSON を StreamingAssets に保存
                var streamingAssetsPath = UnityEngine.Application.streamingAssetsPath;
                var jsonDir = Path.Combine(streamingAssetsPath, "FacialControl");
                if (!Directory.Exists(jsonDir))
                    Directory.CreateDirectory(jsonDir);

                var fullJsonPath = Path.Combine(jsonDir, data.JsonFileName);
                if (File.Exists(fullJsonPath))
                {
                    if (!EditorUtility.DisplayDialog(
                        "ファイル上書き確認",
                        $"JSON ファイルが既に存在します:\n{fullJsonPath}\n\n上書きしますか？",
                        "上書き",
                        "キャンセル"))
                        return;
                }

                var json = _parser.SerializeProfile(profile);
                File.WriteAllText(fullJsonPath, json, System.Text.Encoding.UTF8);

                // FacialProfileSO を Assets/ に生成
                var soPath = $"Assets/{profileName}_Profile.asset";
                if (File.Exists(Path.Combine(UnityEngine.Application.dataPath, "..", soPath)))
                {
                    soPath = AssetDatabase.GenerateUniqueAssetPath(soPath);
                }

                var so = CreateInstance<FacialProfileSO>();
                so.JsonFilePath = data.JsonRelativePath;
                so.SchemaVersion = profile.SchemaVersion;
                so.LayerCount = profile.Layers.Length;
                so.ExpressionCount = profile.Expressions.Length;

                AssetDatabase.CreateAsset(so, soPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                OnCreated?.Invoke(so);
                ShowDialogStatus($"プロファイルを作成しました: {profileName}", isError: false);

                // 少し待ってから閉じる（ステータス表示のため）
                EditorApplication.delayCall += Close;
            }
            catch (Exception ex)
            {
                ShowDialogStatus($"作成エラー: {ex.Message}", isError: true);
                Debug.LogError($"[ProfileCreationDialog] プロファイル作成エラー: {ex}");
            }
        }

        /// <summary>
        /// ダイアログ内ステータスメッセージを表示する
        /// </summary>
        private void ShowDialogStatus(string message, bool isError)
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
