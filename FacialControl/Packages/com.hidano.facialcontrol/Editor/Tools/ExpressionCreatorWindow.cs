using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Adapters.FileSystem;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Common;

namespace Hidano.FacialControl.Editor.Tools
{
    /// <summary>
    /// Expression 作成支援ツール。
    /// BlendShape スライダーを操作してリアルタイムプレビューしながら Expression を作成する。
    /// </summary>
    public class ExpressionCreatorWindow : EditorWindow
    {
        private const string WindowTitle = "Expression 作成";
        private const float MinWindowWidth = 700f;
        private const float MinWindowHeight = 500f;
        private const int PreviewSize = 256;

        // モデル参照
        private GameObject _targetObject;
        private SkinnedMeshRenderer[] _skinnedMeshRenderers;

        // プレビュー
        private PreviewRenderUtility _previewRenderUtility;
        private GameObject _previewInstance;
        private RenderTexture _previewTexture;
        private IMGUIContainer _previewContainer;
        private Vector2 _previewRotation = new Vector2(0f, 0f);
        private float _previewZoom = 1.5f;
        private Vector2 _lastMousePos;
        private bool _isDragging;

        // BlendShape 管理
        private List<BlendShapeEntry> _blendShapeEntries = new List<BlendShapeEntry>();
        private ScrollView _blendShapeListView;
        private TextField _blendShapeSearchField;
        private string _blendShapeSearchText = "";

        // Expression 設定
        private TextField _expressionNameField;
        private DropdownField _layerDropdown;
        private FloatField _transitionDurationField;
        private DropdownField _curveTypeDropdown;

        // プロファイル
        private FacialProfileSO _profileSO;
        private FacialProfile _currentProfile;
        private string _currentJsonPath;

        // ステータス
        private Label _statusLabel;

        // 依存
        private IJsonParser _parser;
        private IProfileRepository _repository;
        private FacialProfileMapper _mapper;

        [MenuItem("FacialControl/Expression 作成", false, 20)]
        public static void ShowWindow()
        {
            var window = GetWindow<ExpressionCreatorWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(MinWindowWidth, MinWindowHeight);
        }

        private void OnEnable()
        {
            _parser = new SystemTextJsonParser();
            _repository = new FileProfileRepository(_parser);
            _mapper = new FacialProfileMapper(_repository);
        }

        private void OnDisable()
        {
            CleanupPreview();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;

            var styleSheet = FacialControlStyles.Load();
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            // メインレイアウト: 左右分割
            var mainContainer = new VisualElement();
            mainContainer.style.flexDirection = FlexDirection.Row;
            mainContainer.style.flexGrow = 1;
            root.Add(mainContainer);

            // ========================================
            // 左パネル: プレビュー + モデル選択
            // ========================================
            var leftPanel = new VisualElement();
            leftPanel.style.width = PreviewSize + 16;
            leftPanel.style.minWidth = PreviewSize + 16;
            leftPanel.style.paddingLeft = 4;
            leftPanel.style.paddingRight = 4;
            leftPanel.style.paddingTop = 4;
            mainContainer.Add(leftPanel);

            // モデル選択
            var modelField = new ObjectField("モデル")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true
            };
            modelField.RegisterValueChangedCallback(OnModelChanged);
            leftPanel.Add(modelField);

            // プレビュー領域（IMGUI で PreviewRenderUtility を描画）
            _previewContainer = new IMGUIContainer(OnPreviewGUI);
            _previewContainer.style.width = PreviewSize;
            _previewContainer.style.height = PreviewSize;
            _previewContainer.style.marginTop = 4;
            _previewContainer.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
            leftPanel.Add(_previewContainer);

            // リセットボタン
            var resetButton = new Button(OnResetBlendShapes) { text = "全スライダーリセット" };
            resetButton.AddToClassList(FacialControlStyles.ActionButton);
            resetButton.style.marginTop = 4;
            leftPanel.Add(resetButton);

            // ========================================
            // 右パネル: Expression 設定 + BlendShape スライダー
            // ========================================
            var rightPanel = new VisualElement();
            rightPanel.style.flexGrow = 1;
            rightPanel.style.paddingLeft = 4;
            rightPanel.style.paddingRight = 4;
            rightPanel.style.paddingTop = 4;
            mainContainer.Add(rightPanel);

            // プロファイル SO 選択
            var profileField = new ObjectField("プロファイル SO")
            {
                objectType = typeof(FacialProfileSO),
                allowSceneObjects = false
            };
            profileField.RegisterValueChangedCallback(OnProfileSOChanged);
            rightPanel.Add(profileField);

            // Expression 名
            _expressionNameField = new TextField("Expression 名")
            {
                value = "New Expression"
            };
            rightPanel.Add(_expressionNameField);

            // レイヤー選択
            _layerDropdown = new DropdownField("レイヤー", new List<string> { "emotion" }, 0);
            rightPanel.Add(_layerDropdown);

            // 遷移時間
            _transitionDurationField = new FloatField("遷移時間 (秒)")
            {
                value = 0.25f
            };
            _transitionDurationField.tooltip = "0〜1 秒";
            rightPanel.Add(_transitionDurationField);

            // カーブ種類
            var curveTypes = new List<string> { "Linear", "EaseIn", "EaseOut", "EaseInOut", "Custom" };
            _curveTypeDropdown = new DropdownField("遷移カーブ", curveTypes, 0);
            rightPanel.Add(_curveTypeDropdown);

            // BlendShape 検索
            _blendShapeSearchField = new TextField("BlendShape 検索");
            _blendShapeSearchField.RegisterValueChangedCallback(OnBlendShapeSearchChanged);
            _blendShapeSearchField.style.marginTop = 8;
            rightPanel.Add(_blendShapeSearchField);

            // BlendShape スライダーリスト
            _blendShapeListView = new ScrollView(ScrollViewMode.Vertical);
            _blendShapeListView.style.flexGrow = 1;
            _blendShapeListView.style.marginTop = 4;
            rightPanel.Add(_blendShapeListView);

            // ========================================
            // 下部: 保存ボタン + ステータス
            // ========================================
            var bottomSection = new VisualElement();
            bottomSection.style.flexDirection = FlexDirection.Row;
            bottomSection.style.paddingLeft = 4;
            bottomSection.style.paddingRight = 4;
            bottomSection.style.paddingBottom = 4;
            bottomSection.style.paddingTop = 4;
            bottomSection.style.justifyContent = Justify.FlexEnd;

            var saveButton = new Button(OnSaveExpressionClicked) { text = "Expression を保存" };
            saveButton.AddToClassList(FacialControlStyles.ActionButton);
            bottomSection.Add(saveButton);

            root.Add(bottomSection);

            // ステータスラベル
            _statusLabel = new Label();
            _statusLabel.AddToClassList(FacialControlStyles.StatusLabel);
            _statusLabel.style.paddingLeft = 4;
            _statusLabel.style.paddingBottom = 4;
            root.Add(_statusLabel);
        }

        // ========================================
        // モデル選択
        // ========================================

        private void OnModelChanged(ChangeEvent<UnityEngine.Object> evt)
        {
            _targetObject = evt.newValue as GameObject;
            CollectBlendShapes();
            RebuildBlendShapeList();
            SetupPreview();
        }

        /// <summary>
        /// 対象モデルから全 SkinnedMeshRenderer の BlendShape を収集する
        /// </summary>
        private void CollectBlendShapes()
        {
            _blendShapeEntries.Clear();

            if (_targetObject == null)
            {
                _skinnedMeshRenderers = null;
                return;
            }

            _skinnedMeshRenderers = _targetObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            for (int r = 0; r < _skinnedMeshRenderers.Length; r++)
            {
                var smr = _skinnedMeshRenderers[r];
                if (smr.sharedMesh == null)
                    continue;

                int count = smr.sharedMesh.blendShapeCount;
                for (int i = 0; i < count; i++)
                {
                    var shapeName = smr.sharedMesh.GetBlendShapeName(i);
                    _blendShapeEntries.Add(new BlendShapeEntry
                    {
                        RendererName = smr.name,
                        RendererIndex = r,
                        BlendShapeName = shapeName,
                        BlendShapeIndex = i,
                        Value = 0f
                    });
                }
            }
        }

        // ========================================
        // BlendShape スライダー UI
        // ========================================

        private void OnBlendShapeSearchChanged(ChangeEvent<string> evt)
        {
            _blendShapeSearchText = evt.newValue ?? "";
            RebuildBlendShapeList();
        }

        /// <summary>
        /// BlendShape スライダーリスト UI を再構築する
        /// </summary>
        private void RebuildBlendShapeList()
        {
            if (_blendShapeListView == null)
                return;

            _blendShapeListView.Clear();

            if (_blendShapeEntries.Count == 0)
            {
                var emptyLabel = new Label("モデルを選択してください。");
                emptyLabel.AddToClassList(FacialControlStyles.InfoLabel);
                emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                emptyLabel.style.marginTop = 20;
                _blendShapeListView.Add(emptyLabel);
                return;
            }

            string currentRenderer = null;

            for (int i = 0; i < _blendShapeEntries.Count; i++)
            {
                var entry = _blendShapeEntries[i];

                // 検索フィルタ
                if (!string.IsNullOrEmpty(_blendShapeSearchText)
                    && entry.BlendShapeName.IndexOf(_blendShapeSearchText, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                // Renderer ヘッダー
                if (currentRenderer != entry.RendererName)
                {
                    currentRenderer = entry.RendererName;
                    var header = new Label(entry.RendererName);
                    header.style.unityFontStyleAndWeight = FontStyle.Bold;
                    header.style.marginTop = 8;
                    header.style.marginBottom = 2;
                    _blendShapeListView.Add(header);
                }

                // スライダー行
                var row = CreateBlendShapeSliderRow(i);
                _blendShapeListView.Add(row);
            }
        }

        /// <summary>
        /// BlendShape スライダー行を生成する
        /// </summary>
        private VisualElement CreateBlendShapeSliderRow(int entryIndex)
        {
            var entry = _blendShapeEntries[entryIndex];

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 1;

            // BlendShape 名ラベル
            var nameLabel = new Label(entry.BlendShapeName);
            nameLabel.style.width = 160;
            nameLabel.style.minWidth = 100;
            nameLabel.style.overflow = Overflow.Hidden;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(nameLabel);

            // スライダー
            var slider = new Slider(0f, 1f);
            slider.value = entry.Value;
            slider.style.flexGrow = 1;
            slider.style.minWidth = 80;

            // 値フィールド
            var valueField = new FloatField();
            valueField.value = entry.Value;
            valueField.style.width = 55;
            valueField.style.marginLeft = 4;

            // キャプチャ用ローカル変数
            int capturedIndex = entryIndex;

            slider.RegisterValueChangedCallback(evt =>
            {
                _blendShapeEntries[capturedIndex].Value = evt.newValue;
                valueField.SetValueWithoutNotify(evt.newValue);
                ApplyBlendShapeToPreview(capturedIndex);
            });

            valueField.RegisterValueChangedCallback(evt =>
            {
                float clamped = Mathf.Clamp01(evt.newValue);
                _blendShapeEntries[capturedIndex].Value = clamped;
                slider.SetValueWithoutNotify(clamped);
                valueField.SetValueWithoutNotify(clamped);
                ApplyBlendShapeToPreview(capturedIndex);
            });

            row.Add(slider);
            row.Add(valueField);

            return row;
        }

        /// <summary>
        /// 全 BlendShape スライダーをリセットする
        /// </summary>
        private void OnResetBlendShapes()
        {
            for (int i = 0; i < _blendShapeEntries.Count; i++)
            {
                _blendShapeEntries[i].Value = 0f;
            }

            RebuildBlendShapeList();
            ApplyAllBlendShapesToPreview();
        }

        // ========================================
        // プレビュー
        // ========================================

        /// <summary>
        /// PreviewRenderUtility を初期化してプレビューインスタンスを作成する
        /// </summary>
        private void SetupPreview()
        {
            CleanupPreview();

            if (_targetObject == null)
                return;

            _previewRenderUtility = new PreviewRenderUtility();
            _previewRenderUtility.camera.fieldOfView = 30f;
            _previewRenderUtility.camera.nearClipPlane = 0.01f;
            _previewRenderUtility.camera.farClipPlane = 100f;
            _previewRenderUtility.camera.clearFlags = CameraClearFlags.SolidColor;
            _previewRenderUtility.camera.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);

            // ライティング設定
            _previewRenderUtility.lights[0].intensity = 1.2f;
            _previewRenderUtility.lights[0].transform.rotation = Quaternion.Euler(30f, -30f, 0f);

            // プレビューインスタンス生成
            _previewInstance = Instantiate(_targetObject);
            _previewInstance.hideFlags = HideFlags.HideAndDontSave;

            // 位置リセット
            _previewInstance.transform.position = Vector3.zero;
            _previewInstance.transform.rotation = Quaternion.identity;

            _previewRenderUtility.AddSingleGO(_previewInstance);

            // プレビュー用 SkinnedMeshRenderer を更新
            _skinnedMeshRenderers = _previewInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            ApplyAllBlendShapesToPreview();
        }

        /// <summary>
        /// プレビューリソースを解放する
        /// </summary>
        private void CleanupPreview()
        {
            if (_previewInstance != null)
            {
                DestroyImmediate(_previewInstance);
                _previewInstance = null;
            }

            if (_previewRenderUtility != null)
            {
                _previewRenderUtility.Cleanup();
                _previewRenderUtility = null;
            }
        }

        /// <summary>
        /// 特定の BlendShape 値をプレビューに反映する
        /// </summary>
        private void ApplyBlendShapeToPreview(int entryIndex)
        {
            if (_previewInstance == null || _skinnedMeshRenderers == null)
                return;

            var entry = _blendShapeEntries[entryIndex];

            if (entry.RendererIndex >= 0 && entry.RendererIndex < _skinnedMeshRenderers.Length)
            {
                var smr = _skinnedMeshRenderers[entry.RendererIndex];
                if (smr != null && smr.sharedMesh != null)
                {
                    smr.SetBlendShapeWeight(entry.BlendShapeIndex, entry.Value * 100f);
                }
            }

            _previewContainer?.MarkDirtyRepaint();
        }

        /// <summary>
        /// 全 BlendShape 値をプレビューに反映する
        /// </summary>
        private void ApplyAllBlendShapesToPreview()
        {
            if (_previewInstance == null || _skinnedMeshRenderers == null)
                return;

            for (int i = 0; i < _blendShapeEntries.Count; i++)
            {
                var entry = _blendShapeEntries[i];
                if (entry.RendererIndex >= 0 && entry.RendererIndex < _skinnedMeshRenderers.Length)
                {
                    var smr = _skinnedMeshRenderers[entry.RendererIndex];
                    if (smr != null && smr.sharedMesh != null)
                    {
                        smr.SetBlendShapeWeight(entry.BlendShapeIndex, entry.Value * 100f);
                    }
                }
            }

            _previewContainer?.MarkDirtyRepaint();
        }

        /// <summary>
        /// IMGUI ベースのプレビュー描画
        /// </summary>
        private void OnPreviewGUI()
        {
            if (_previewRenderUtility == null || _previewInstance == null)
            {
                GUILayout.Label("モデルを選択してください。", EditorStyles.centeredGreyMiniLabel,
                    GUILayout.Width(PreviewSize), GUILayout.Height(PreviewSize));
                return;
            }

            var rect = GUILayoutUtility.GetRect(PreviewSize, PreviewSize);

            // マウス操作（回転・ズーム）
            HandlePreviewInput(rect);

            // カメラ位置の計算
            var bounds = CalculateBounds(_previewInstance);
            float distance = bounds.extents.magnitude * _previewZoom;
            var center = bounds.center;

            var rotation = Quaternion.Euler(_previewRotation.y, _previewRotation.x, 0f);
            var camPos = center + rotation * new Vector3(0f, 0f, -distance);

            _previewRenderUtility.camera.transform.position = camPos;
            _previewRenderUtility.camera.transform.LookAt(center);

            _previewRenderUtility.BeginPreview(rect, GUIStyle.none);
            _previewRenderUtility.Render(true, true);
            var texture = _previewRenderUtility.EndPreview();

            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
        }

        /// <summary>
        /// プレビューのマウス入力処理（回転・ズーム）
        /// </summary>
        private void HandlePreviewInput(Rect rect)
        {
            var evt = Event.current;

            if (!rect.Contains(evt.mousePosition))
                return;

            switch (evt.type)
            {
                case EventType.MouseDown when evt.button == 0:
                    _isDragging = true;
                    _lastMousePos = evt.mousePosition;
                    evt.Use();
                    break;

                case EventType.MouseUp when evt.button == 0:
                    _isDragging = false;
                    evt.Use();
                    break;

                case EventType.MouseDrag when _isDragging:
                    var delta = evt.mousePosition - _lastMousePos;
                    _previewRotation.x += delta.x * 0.5f;
                    _previewRotation.y -= delta.y * 0.5f;
                    _previewRotation.y = Mathf.Clamp(_previewRotation.y, -89f, 89f);
                    _lastMousePos = evt.mousePosition;
                    evt.Use();
                    Repaint();
                    break;

                case EventType.ScrollWheel:
                    _previewZoom += evt.delta.y * 0.05f;
                    _previewZoom = Mathf.Clamp(_previewZoom, 0.5f, 5f);
                    evt.Use();
                    Repaint();
                    break;
            }
        }

        /// <summary>
        /// GameObject の全 Renderer を含むバウンディングボックスを計算する
        /// </summary>
        private static Bounds CalculateBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return new Bounds(go.transform.position, Vector3.one);

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        // ========================================
        // プロファイル管理
        // ========================================

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
                UpdateLayerDropdown();
                return;
            }

            if (string.IsNullOrWhiteSpace(_profileSO.JsonFilePath))
            {
                ShowStatus("JSON ファイルパスが設定されていません。", isError: true);
                _currentProfile = default;
                _currentJsonPath = null;
                UpdateLayerDropdown();
                return;
            }

            var fullPath = Path.Combine(UnityEngine.Application.streamingAssetsPath, _profileSO.JsonFilePath);
            if (!File.Exists(fullPath))
            {
                ShowStatus($"ファイルが見つかりません: {fullPath}", isError: true);
                _currentProfile = default;
                _currentJsonPath = null;
                UpdateLayerDropdown();
                return;
            }

            try
            {
                var json = File.ReadAllText(fullPath, System.Text.Encoding.UTF8);
                _currentProfile = _parser.ParseProfile(json);
                _currentJsonPath = fullPath;
                UpdateLayerDropdown();
                ShowStatus($"プロファイルを読み込みました。", isError: false);
            }
            catch (Exception ex)
            {
                ShowStatus($"読み込みエラー: {ex.Message}", isError: true);
                Debug.LogError($"[ExpressionCreatorWindow] JSON 読み込みエラー: {ex}");
                _currentProfile = default;
                _currentJsonPath = null;
                UpdateLayerDropdown();
            }
        }

        /// <summary>
        /// プロファイルのレイヤー情報でドロップダウンを更新する
        /// </summary>
        private void UpdateLayerDropdown()
        {
            if (_layerDropdown == null)
                return;

            var layerNames = new List<string>();

            if (!string.IsNullOrEmpty(_currentJsonPath))
            {
                var layerSpan = _currentProfile.Layers.Span;
                for (int i = 0; i < layerSpan.Length; i++)
                    layerNames.Add(layerSpan[i].Name);
            }

            if (layerNames.Count == 0)
                layerNames.Add("emotion");

            _layerDropdown.choices = layerNames;
            _layerDropdown.index = 0;
        }

        // ========================================
        // 保存
        // ========================================

        /// <summary>
        /// 現在のスライダー値から Expression を作成してプロファイルに保存する
        /// </summary>
        private void OnSaveExpressionClicked()
        {
            // バリデーション
            var name = _expressionNameField?.value;
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowStatus("Expression 名を入力してください。", isError: true);
                return;
            }

            if (string.IsNullOrEmpty(_currentJsonPath) || _profileSO == null)
            {
                ShowStatus("プロファイル SO を選択してください。", isError: true);
                return;
            }

            var layer = _layerDropdown?.value;
            if (string.IsNullOrWhiteSpace(layer))
            {
                ShowStatus("レイヤーを選択してください。", isError: true);
                return;
            }

            // BlendShape 値を収集（値が 0 以外のもの）
            var mappings = new List<BlendShapeMapping>();
            for (int i = 0; i < _blendShapeEntries.Count; i++)
            {
                var entry = _blendShapeEntries[i];
                if (entry.Value > 0f)
                {
                    mappings.Add(new BlendShapeMapping(
                        entry.BlendShapeName,
                        entry.Value,
                        entry.RendererName));
                }
            }

            // 遷移カーブの作成
            var curveType = ParseCurveType(_curveTypeDropdown?.value ?? "Linear");
            var transitionCurve = new TransitionCurve(curveType);

            // Expression 生成
            var expression = new Expression(
                id: Guid.NewGuid().ToString(),
                name: name,
                layer: layer,
                transitionDuration: _transitionDurationField?.value ?? 0.25f,
                transitionCurve: transitionCurve,
                blendShapeValues: mappings.ToArray());

            // プロファイルに追加
            var expressions = new List<Expression>();
            var exprSpan = _currentProfile.Expressions.Span;
            for (int i = 0; i < exprSpan.Length; i++)
                expressions.Add(exprSpan[i]);
            expressions.Add(expression);

            var layers = ToArray(_currentProfile.Layers);

            Undo.RecordObject(_profileSO, "Expression 作成");

            _currentProfile = new FacialProfile(
                _currentProfile.SchemaVersion,
                layers,
                expressions.ToArray());

            // JSON 保存
            try
            {
                var json = _parser.SerializeProfile(_currentProfile);
                File.WriteAllText(_currentJsonPath, json, System.Text.Encoding.UTF8);
                _mapper.UpdateSO(_profileSO, _currentProfile);
                EditorUtility.SetDirty(_profileSO);
                ShowStatus($"Expression を保存しました: {name}", isError: false);
            }
            catch (Exception ex)
            {
                ShowStatus($"保存エラー: {ex.Message}", isError: true);
                Debug.LogError($"[ExpressionCreatorWindow] 保存エラー: {ex}");
            }
        }

        // ========================================
        // ヘルパー
        // ========================================

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

        private static TransitionCurveType ParseCurveType(string value)
        {
            return value switch
            {
                "EaseIn" => TransitionCurveType.EaseIn,
                "EaseOut" => TransitionCurveType.EaseOut,
                "EaseInOut" => TransitionCurveType.EaseInOut,
                "Custom" => TransitionCurveType.Custom,
                _ => TransitionCurveType.Linear
            };
        }

        private static T[] ToArray<T>(ReadOnlyMemory<T> memory)
        {
            var span = memory.Span;
            var result = new T[span.Length];
            for (int i = 0; i < span.Length; i++)
                result[i] = span[i];
            return result;
        }

        /// <summary>
        /// BlendShape エントリ。スライダーの状態を保持する。
        /// </summary>
        private class BlendShapeEntry
        {
            public string RendererName;
            public int RendererIndex;
            public string BlendShapeName;
            public int BlendShapeIndex;
            public float Value;
        }
    }
}
