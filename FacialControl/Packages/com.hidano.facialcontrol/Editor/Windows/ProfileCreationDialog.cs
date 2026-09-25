using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
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
        /// JSON 出力先のフルパスを引数として渡す。
        /// </summary>
        public event Action<string> OnCreated;

        private TextField _profileNameField;
        private ScrollView _layerListView;
        private Button _addLayerButton;
        private Button _createButton;
        private Label _statusLabel;
        private EnumField _namingConventionField;
        private Toggle _includeSampleExpressionsToggle;

        private List<ProfileCreationData.LayerEntry> _layers;
        private List<VisualElement> _layerElements;

        private IJsonParser _parser;

        /// <summary>
        /// メニューからダイアログを起動する
        /// </summary>
        [MenuItem("Tools/FacialControl/新規プロファイル作成", false, 10)]
        public static void OpenFromMenu()
        {
            ShowDialog();
        }

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
            // 雛形 Expression 設定セクション
            // ========================================
            var sampleHeader = new Label("雛形 Expression");
            sampleHeader.style.marginTop = 8;
            sampleHeader.style.marginBottom = 4;
            root.Add(sampleHeader);

            _namingConventionField = new EnumField(
                "命名規則プリセット",
                ProfileCreationData.NamingConvention.VRM);
            _namingConventionField.tooltip =
                "雛形 Expression の BlendShape 名に使用する命名規則を選択します。None を選ぶと雛形を生成しません。";
            root.Add(_namingConventionField);

            _includeSampleExpressionsToggle = new Toggle("雛形 Expression を追加")
            {
                value = true
            };
            _includeSampleExpressionsToggle.tooltip =
                "smile / angry / blink の 3 つの雛形 Expression をプロファイルに追加します。";
            root.Add(_includeSampleExpressionsToggle);

            var sampleHelpLabel = new Label(
                "BlendShape 名はモデル依存のため、必要に応じて Expression 作成ウィンドウで調整してください");
            sampleHelpLabel.style.whiteSpace = WhiteSpace.Normal;
            sampleHelpLabel.style.marginTop = 2;
            sampleHelpLabel.style.marginBottom = 4;
            sampleHelpLabel.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            sampleHelpLabel.style.fontSize = 11;
            root.Add(sampleHelpLabel);

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

            // 雛形設定の解決（UI 未初期化時のフォールバックあり）
            var naming = _namingConventionField != null
                ? (ProfileCreationData.NamingConvention)_namingConventionField.value
                : ProfileCreationData.NamingConvention.VRM;
            var includeSamples = _includeSampleExpressionsToggle != null
                ? _includeSampleExpressionsToggle.value
                : true;

            // 雛形は emotion / eye レイヤーを参照するため、欠落している場合は警告のみ表示し続行
            if (includeSamples && naming != ProfileCreationData.NamingConvention.None)
            {
                bool hasEmotion = false;
                bool hasEye = false;
                for (int i = 0; i < _layers.Count; i++)
                {
                    if (_layers[i].Name == "emotion") hasEmotion = true;
                    if (_layers[i].Name == "eye") hasEye = true;
                }

                if (!hasEmotion || !hasEye)
                {
                    var missing = new List<string>();
                    if (!hasEmotion) missing.Add("emotion");
                    if (!hasEye) missing.Add("eye");
                    Debug.LogWarning(
                        $"[ProfileCreationDialog] 雛形 Expression が参照するレイヤーが不足しています: {string.Join(", ", missing)}。"
                        + " 該当レイヤーを追加するか、Expression のレイヤーを調整してください。");
                }
            }

            try
            {
                var data = new ProfileCreationData(profileName, _layers.ToArray())
                {
                    IncludeSampleExpressions = includeSamples,
                    Naming = naming
                };
                var profile = data.BuildProfile();

                // 新統合 SO は規約パス
                // (StreamingAssets/FacialControl/{SO 名}/profile.json) で profile.json を読み込むため、
                // ダイアログでは JSON テンプレートのみを生成し、SO 自体はユーザーが
                // Project ウィンドウの Create メニューから手動で作成する設計とする。
                var streamingAssetsPath = UnityEngine.Application.streamingAssetsPath;
                var jsonDir = Path.Combine(streamingAssetsPath, "FacialControl", profileName);
                if (!Directory.Exists(jsonDir))
                    Directory.CreateDirectory(jsonDir);

                var fullJsonPath = Path.Combine(jsonDir, FacialCharacterProfileSO.ProfileJsonFileName);
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
                AssetDatabase.Refresh();

                OnCreated?.Invoke(fullJsonPath);
                ShowDialogStatus(
                    $"JSON テンプレートを生成しました: {fullJsonPath}\n"
                    + "Project ウィンドウから 'FacialControl/Facial Character' を作成し、SO 名を "
                    + $"\"{profileName}\" にすると自動で読み込まれます。",
                    isError: false);

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
