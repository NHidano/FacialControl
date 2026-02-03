using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Hidano.FacialControl.Adapters.FileSystem;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Common;

namespace Hidano.FacialControl.Editor.Windows
{
    /// <summary>
    /// プロファイル管理ウィンドウ。
    /// EditorWindow でプロファイル内の Expression リスト管理と CRUD 操作を提供する。
    /// </summary>
    public class ProfileManagerWindow : EditorWindow
    {
        private const string WindowTitle = "プロファイル管理";
        private const float MinWindowWidth = 450f;
        private const float MinWindowHeight = 400f;

        private FacialProfileSO _profileSO;
        private FacialProfile _currentProfile;
        private string _currentJsonPath;
        private string _searchText = "";
        private List<Expression> _filteredExpressions = new List<Expression>();

        // UI 要素
        private ObjectField _profileSOField;
        private TextField _searchField;
        private ScrollView _expressionListView;
        private Label _statusLabel;
        private Label _profileInfoLabel;
        private Button _addButton;

        private IJsonParser _parser;
        private IProfileRepository _repository;
        private FacialProfileMapper _mapper;

        [MenuItem("FacialControl/プロファイル管理", false, 10)]
        public static void ShowWindow()
        {
            var window = GetWindow<ProfileManagerWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(MinWindowWidth, MinWindowHeight);
        }

        private void OnEnable()
        {
            _parser = new SystemTextJsonParser();
            _repository = new FileProfileRepository(_parser);
            _mapper = new FacialProfileMapper(_repository);
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;

            var styleSheet = FacialControlStyles.Load();
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            // ========================================
            // プロファイル SO 選択セクション
            // ========================================
            var profileSection = new VisualElement();
            profileSection.style.marginBottom = 4;
            profileSection.style.paddingLeft = 4;
            profileSection.style.paddingRight = 4;
            profileSection.style.paddingTop = 4;

            _profileSOField = new ObjectField("プロファイル SO")
            {
                objectType = typeof(FacialProfileSO),
                allowSceneObjects = false
            };
            _profileSOField.RegisterValueChangedCallback(OnProfileSOChanged);
            profileSection.Add(_profileSOField);

            _profileInfoLabel = new Label("プロファイルが選択されていません。");
            _profileInfoLabel.AddToClassList(FacialControlStyles.InfoLabel);
            profileSection.Add(_profileInfoLabel);

            root.Add(profileSection);

            // ========================================
            // 検索 + 追加ボタンセクション
            // ========================================
            var toolbarSection = new VisualElement();
            toolbarSection.style.flexDirection = FlexDirection.Row;
            toolbarSection.style.paddingLeft = 4;
            toolbarSection.style.paddingRight = 4;
            toolbarSection.style.marginBottom = 4;

            _searchField = new TextField("検索");
            _searchField.style.flexGrow = 1;
            _searchField.RegisterValueChangedCallback(OnSearchChanged);
            toolbarSection.Add(_searchField);

            _addButton = new Button(OnAddExpressionClicked) { text = "追加" };
            _addButton.AddToClassList(FacialControlStyles.ActionButton);
            _addButton.SetEnabled(false);
            toolbarSection.Add(_addButton);

            root.Add(toolbarSection);

            // ========================================
            // Expression リスト表示セクション
            // ========================================
            _expressionListView = new ScrollView(ScrollViewMode.Vertical);
            _expressionListView.style.flexGrow = 1;
            _expressionListView.style.paddingLeft = 4;
            _expressionListView.style.paddingRight = 4;
            root.Add(_expressionListView);

            // ========================================
            // ステータスラベル
            // ========================================
            _statusLabel = new Label();
            _statusLabel.AddToClassList(FacialControlStyles.StatusLabel);
            _statusLabel.style.paddingLeft = 4;
            _statusLabel.style.paddingBottom = 4;
            root.Add(_statusLabel);
        }

        /// <summary>
        /// プロファイル SO 変更時の処理
        /// </summary>
        private void OnProfileSOChanged(ChangeEvent<UnityEngine.Object> evt)
        {
            _profileSO = evt.newValue as FacialProfileSO;
            LoadProfile();
        }

        /// <summary>
        /// プロファイルを JSON から読み込む
        /// </summary>
        private void LoadProfile()
        {
            if (_profileSO == null)
            {
                _currentProfile = default;
                _currentJsonPath = null;
                _filteredExpressions.Clear();
                UpdateUI();
                return;
            }

            if (string.IsNullOrWhiteSpace(_profileSO.JsonFilePath))
            {
                ShowStatus("JSON ファイルパスが設定されていません。", isError: true);
                _currentProfile = default;
                _currentJsonPath = null;
                _filteredExpressions.Clear();
                UpdateUI();
                return;
            }

            var fullPath = Path.Combine(UnityEngine.Application.streamingAssetsPath, _profileSO.JsonFilePath);
            if (!File.Exists(fullPath))
            {
                ShowStatus($"ファイルが見つかりません: {fullPath}", isError: true);
                _currentProfile = default;
                _currentJsonPath = null;
                _filteredExpressions.Clear();
                UpdateUI();
                return;
            }

            try
            {
                var json = File.ReadAllText(fullPath, System.Text.Encoding.UTF8);
                _currentProfile = _parser.ParseProfile(json);
                _currentJsonPath = fullPath;
                FilterExpressions();
                UpdateUI();
                ShowStatus($"プロファイルを読み込みました。Expression 数: {_currentProfile.Expressions.Length}", isError: false);
            }
            catch (Exception ex)
            {
                ShowStatus($"読み込みエラー: {ex.Message}", isError: true);
                Debug.LogError($"[ProfileManagerWindow] JSON 読み込みエラー: {ex}");
                _currentProfile = default;
                _currentJsonPath = null;
                _filteredExpressions.Clear();
                UpdateUI();
            }
        }

        /// <summary>
        /// 検索テキスト変更時の処理
        /// </summary>
        private void OnSearchChanged(ChangeEvent<string> evt)
        {
            _searchText = evt.newValue ?? "";
            FilterExpressions();
            RebuildExpressionList();
        }

        /// <summary>
        /// 検索条件に基づいて Expression をフィルタリングする
        /// </summary>
        private void FilterExpressions()
        {
            _filteredExpressions.Clear();

            if (string.IsNullOrEmpty(_currentJsonPath))
                return;

            var span = _currentProfile.Expressions.Span;
            for (int i = 0; i < span.Length; i++)
            {
                if (string.IsNullOrEmpty(_searchText)
                    || span[i].Name.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _filteredExpressions.Add(span[i]);
                }
            }
        }

        /// <summary>
        /// UI 全体を更新する
        /// </summary>
        private void UpdateUI()
        {
            bool hasProfile = !string.IsNullOrEmpty(_currentJsonPath);
            _addButton?.SetEnabled(hasProfile);

            if (_profileInfoLabel != null)
            {
                if (hasProfile)
                {
                    _profileInfoLabel.text =
                        $"バージョン: {_currentProfile.SchemaVersion}  |  " +
                        $"レイヤー: {_currentProfile.Layers.Length}  |  " +
                        $"Expression: {_currentProfile.Expressions.Length}";
                }
                else
                {
                    _profileInfoLabel.text = _profileSO != null
                        ? "プロファイルを読み込めませんでした。"
                        : "プロファイルが選択されていません。";
                }
            }

            RebuildExpressionList();
        }

        /// <summary>
        /// Expression リスト UI を再構築する
        /// </summary>
        private void RebuildExpressionList()
        {
            if (_expressionListView == null)
                return;

            _expressionListView.Clear();

            if (_filteredExpressions.Count == 0)
            {
                var emptyLabel = new Label(
                    string.IsNullOrEmpty(_currentJsonPath)
                        ? "プロファイルを選択してください。"
                        : "Expression が見つかりません。");
                emptyLabel.AddToClassList(FacialControlStyles.InfoLabel);
                emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                emptyLabel.style.marginTop = 20;
                _expressionListView.Add(emptyLabel);
                return;
            }

            for (int i = 0; i < _filteredExpressions.Count; i++)
            {
                var expr = _filteredExpressions[i];
                var item = CreateExpressionItem(expr);
                _expressionListView.Add(item);
            }
        }

        /// <summary>
        /// 個々の Expression 表示アイテムを生成する
        /// </summary>
        private VisualElement CreateExpressionItem(Expression expression)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.marginBottom = 2;
            container.style.paddingTop = 4;
            container.style.paddingBottom = 4;
            container.style.paddingLeft = 4;
            container.style.paddingRight = 4;
            container.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f, 1f);
            container.style.borderBottomLeftRadius = 3;
            container.style.borderBottomRightRadius = 3;
            container.style.borderTopLeftRadius = 3;
            container.style.borderTopRightRadius = 3;

            // 情報部分
            var infoSection = new VisualElement();
            infoSection.style.flexGrow = 1;

            var nameLabel = new Label(expression.Name);
            nameLabel.style.fontSize = 13;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            infoSection.Add(nameLabel);

            var detailLabel = new Label(
                $"レイヤー: {expression.Layer}  |  " +
                $"遷移: {expression.TransitionDuration:F2}s  |  " +
                $"BS: {expression.BlendShapeValues.Length}");
            detailLabel.AddToClassList(FacialControlStyles.InfoLabel);
            detailLabel.style.fontSize = 11;
            infoSection.Add(detailLabel);

            container.Add(infoSection);

            // ボタン部分
            var buttonSection = new VisualElement();
            buttonSection.style.flexDirection = FlexDirection.Row;
            buttonSection.style.alignItems = Align.Center;

            var exprId = expression.Id;

            var editButton = new Button(() => OnEditExpressionClicked(exprId)) { text = "編集" };
            editButton.style.width = 40;
            editButton.style.height = 22;
            buttonSection.Add(editButton);

            var deleteButton = new Button(() => OnDeleteExpressionClicked(exprId)) { text = "削除" };
            deleteButton.style.width = 40;
            deleteButton.style.height = 22;
            deleteButton.style.marginLeft = 2;
            buttonSection.Add(deleteButton);

            container.Add(buttonSection);

            return container;
        }

        /// <summary>
        /// Expression 追加ボタン押下時の処理
        /// </summary>
        private void OnAddExpressionClicked()
        {
            if (string.IsNullOrEmpty(_currentJsonPath))
                return;

            var layerSpan = _currentProfile.Layers.Span;
            string defaultLayer = layerSpan.Length > 0 ? layerSpan[0].Name : "emotion";

            var newExpression = new Expression(
                id: Guid.NewGuid().ToString(),
                name: "New Expression",
                layer: defaultLayer);

            var expressions = new List<Expression>();
            var exprSpan = _currentProfile.Expressions.Span;
            for (int i = 0; i < exprSpan.Length; i++)
                expressions.Add(exprSpan[i]);
            expressions.Add(newExpression);

            var layers = ToArray(_currentProfile.Layers);

            Undo.RecordObject(_profileSO, "Expression 追加");

            _currentProfile = new FacialProfile(
                _currentProfile.SchemaVersion,
                layers,
                expressions.ToArray());

            SaveProfile();
            FilterExpressions();
            UpdateUI();
            ShowStatus($"Expression を追加しました: {newExpression.Name}", isError: false);
        }

        /// <summary>
        /// Expression 編集ボタン押下時の処理
        /// </summary>
        private void OnEditExpressionClicked(string expressionId)
        {
            var expr = _currentProfile.FindExpressionById(expressionId);
            if (expr == null)
                return;

            var editWindow = ExpressionEditDialog.ShowDialog(expr.Value, _currentProfile);
            editWindow.OnSaved += editedExpression =>
            {
                Undo.RecordObject(_profileSO, "Expression 編集");

                var expressions = new List<Expression>();
                var exprSpan = _currentProfile.Expressions.Span;
                for (int i = 0; i < exprSpan.Length; i++)
                {
                    if (exprSpan[i].Id == editedExpression.Id)
                        expressions.Add(editedExpression);
                    else
                        expressions.Add(exprSpan[i]);
                }

                var layers = ToArray(_currentProfile.Layers);

                _currentProfile = new FacialProfile(
                    _currentProfile.SchemaVersion,
                    layers,
                    expressions.ToArray());

                SaveProfile();
                FilterExpressions();
                UpdateUI();
                ShowStatus($"Expression を更新しました: {editedExpression.Name}", isError: false);
            };
        }

        /// <summary>
        /// Expression 削除ボタン押下時の処理
        /// </summary>
        private void OnDeleteExpressionClicked(string expressionId)
        {
            var expr = _currentProfile.FindExpressionById(expressionId);
            if (expr == null)
                return;

            if (!EditorUtility.DisplayDialog(
                "Expression の削除",
                $"Expression \"{expr.Value.Name}\" を削除しますか？",
                "削除",
                "キャンセル"))
                return;

            Undo.RecordObject(_profileSO, "Expression 削除");

            var expressions = new List<Expression>();
            var exprSpan = _currentProfile.Expressions.Span;
            for (int i = 0; i < exprSpan.Length; i++)
            {
                if (exprSpan[i].Id != expressionId)
                    expressions.Add(exprSpan[i]);
            }

            var layers = ToArray(_currentProfile.Layers);

            _currentProfile = new FacialProfile(
                _currentProfile.SchemaVersion,
                layers,
                expressions.ToArray());

            SaveProfile();
            FilterExpressions();
            UpdateUI();
            ShowStatus($"Expression を削除しました: {expr.Value.Name}", isError: false);
        }

        /// <summary>
        /// 現在のプロファイルを JSON に保存する
        /// </summary>
        private void SaveProfile()
        {
            if (string.IsNullOrEmpty(_currentJsonPath) || _profileSO == null)
                return;

            try
            {
                var json = _parser.SerializeProfile(_currentProfile);
                File.WriteAllText(_currentJsonPath, json, System.Text.Encoding.UTF8);
                _mapper.UpdateSO(_profileSO, _currentProfile);
                EditorUtility.SetDirty(_profileSO);
            }
            catch (Exception ex)
            {
                ShowStatus($"保存エラー: {ex.Message}", isError: true);
                Debug.LogError($"[ProfileManagerWindow] JSON 保存エラー: {ex}");
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

        /// <summary>
        /// ReadOnlyMemory を配列に変換する
        /// </summary>
        private static T[] ToArray<T>(ReadOnlyMemory<T> memory)
        {
            var span = memory.Span;
            var result = new T[span.Length];
            for (int i = 0; i < span.Length; i++)
                result[i] = span[i];
            return result;
        }
    }

    /// <summary>
    /// Expression 編集ダイアログ。
    /// 名前、レイヤー、遷移時間を編集する小型 EditorWindow。
    /// </summary>
    public class ExpressionEditDialog : EditorWindow
    {
        /// <summary>
        /// 編集完了時に呼び出されるイベント
        /// </summary>
        public event Action<Expression> OnSaved;

        private Expression _original;
        private FacialProfile _profile;

        private TextField _nameField;
        private DropdownField _layerDropdown;
        private FloatField _transitionDurationField;
        private Label _statusLabel;

        /// <summary>
        /// 編集ダイアログを表示する
        /// </summary>
        public static ExpressionEditDialog ShowDialog(Expression expression, FacialProfile profile)
        {
            var window = CreateInstance<ExpressionEditDialog>();
            window.titleContent = new GUIContent("Expression 編集");
            window._original = expression;
            window._profile = profile;
            window.ShowUtility();
            window.minSize = new Vector2(350, 200);
            window.maxSize = new Vector2(500, 250);
            return window;
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

            // 名前
            _nameField = new TextField("名前") { value = _original.Name };
            root.Add(_nameField);

            // レイヤー選択
            var layerNames = new List<string>();
            var layerSpan = _profile.Layers.Span;
            for (int i = 0; i < layerSpan.Length; i++)
                layerNames.Add(layerSpan[i].Name);

            if (layerNames.Count > 0)
            {
                int currentIndex = layerNames.IndexOf(_original.Layer);
                _layerDropdown = new DropdownField("レイヤー", layerNames, currentIndex >= 0 ? currentIndex : 0);
            }
            else
            {
                _layerDropdown = new DropdownField("レイヤー", new List<string> { _original.Layer }, 0);
            }
            root.Add(_layerDropdown);

            // 遷移時間
            _transitionDurationField = new FloatField("遷移時間 (秒)")
            {
                value = _original.TransitionDuration
            };
            _transitionDurationField.tooltip = "0〜1 秒";
            root.Add(_transitionDurationField);

            // ID（読み取り専用）
            var idLabel = new Label($"ID: {_original.Id}");
            idLabel.AddToClassList(FacialControlStyles.InfoLabel);
            idLabel.style.marginTop = 8;
            root.Add(idLabel);

            // ボタン
            var buttonSection = new VisualElement();
            buttonSection.style.flexDirection = FlexDirection.Row;
            buttonSection.style.justifyContent = Justify.FlexEnd;
            buttonSection.style.marginTop = 8;

            var cancelButton = new Button(Close) { text = "キャンセル" };
            buttonSection.Add(cancelButton);

            var saveButton = new Button(OnSaveClicked) { text = "保存" };
            saveButton.AddToClassList(FacialControlStyles.ActionButton);
            saveButton.style.marginLeft = 4;
            buttonSection.Add(saveButton);

            root.Add(buttonSection);

            // ステータス
            _statusLabel = new Label();
            _statusLabel.AddToClassList(FacialControlStyles.StatusLabel);
            root.Add(_statusLabel);
        }

        /// <summary>
        /// 保存ボタン押下時の処理
        /// </summary>
        private void OnSaveClicked()
        {
            var name = _nameField.value;
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowDialogStatus("名前を入力してください。", isError: true);
                return;
            }

            var layer = _layerDropdown.value;
            if (string.IsNullOrWhiteSpace(layer))
            {
                ShowDialogStatus("レイヤーを選択してください。", isError: true);
                return;
            }

            var duration = _transitionDurationField.value;

            // 元の BlendShapeValues と LayerSlots を保持
            var bsArray = new BlendShapeMapping[_original.BlendShapeValues.Length];
            var bsSpan = _original.BlendShapeValues.Span;
            for (int i = 0; i < bsSpan.Length; i++)
                bsArray[i] = bsSpan[i];

            var slotsArray = new LayerSlot[_original.LayerSlots.Length];
            var slotsSpan = _original.LayerSlots.Span;
            for (int i = 0; i < slotsSpan.Length; i++)
                slotsArray[i] = slotsSpan[i];

            var edited = new Expression(
                _original.Id,
                name,
                layer,
                duration,
                _original.TransitionCurve,
                bsArray,
                slotsArray);

            OnSaved?.Invoke(edited);
            Close();
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
