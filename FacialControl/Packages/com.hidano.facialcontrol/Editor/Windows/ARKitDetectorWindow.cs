using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Editor.Common;
using Hidano.FacialControl.Editor.Tools;

namespace Hidano.FacialControl.Editor.Windows
{
    /// <summary>
    /// ARKit 52 / PerfectSync 自動検出ウィンドウ。
    /// SkinnedMeshRenderer の BlendShape をスキャンし、
    /// ARKit / PerfectSync パラメータを検出して Expression・OSC マッピングを自動生成する。
    /// </summary>
    public class ARKitDetectorWindow : EditorWindow
    {
        private const string WindowTitle = "ARKit 検出ツール";
        private const float MinWindowWidth = 500f;
        private const float MinWindowHeight = 450f;

        // 対象モデル
        private SkinnedMeshRenderer _targetRenderer;

        // 検出結果
        private ARKitUseCase.DetectResult _detectResult;
        private OscMapping[] _oscMappings;
        private Dictionary<string, string[]> _groupedResults;
        private bool _hasDetected;

        // UI 要素
        private ObjectField _rendererField;
        private Button _detectButton;
        private Label _summaryLabel;
        private ScrollView _resultListView;
        private Button _generateExpressionsButton;
        private Button _generateOscButton;
        private Label _statusLabel;

        // 依存
        private ARKitEditorService _editorService;

        [MenuItem("FacialControl/ARKit 検出ツール", false, 30)]
        public static void ShowWindow()
        {
            var window = GetWindow<ARKitDetectorWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(MinWindowWidth, MinWindowHeight);
        }

        private void OnEnable()
        {
            _editorService = new ARKitEditorService();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;

            var styleSheet = FacialControlStyles.Load();
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            // ========================================
            // 対象モデル選択セクション
            // ========================================
            var targetSection = new VisualElement();
            targetSection.style.marginBottom = 4;
            targetSection.style.paddingLeft = 4;
            targetSection.style.paddingRight = 4;
            targetSection.style.paddingTop = 4;

            _rendererField = new ObjectField("SkinnedMeshRenderer")
            {
                objectType = typeof(SkinnedMeshRenderer),
                allowSceneObjects = true
            };
            _rendererField.RegisterValueChangedCallback(OnRendererChanged);
            targetSection.Add(_rendererField);

            root.Add(targetSection);

            // ========================================
            // 検出実行ボタンセクション
            // ========================================
            var actionSection = new VisualElement();
            actionSection.style.flexDirection = FlexDirection.Row;
            actionSection.style.paddingLeft = 4;
            actionSection.style.paddingRight = 4;
            actionSection.style.marginBottom = 4;

            _detectButton = new Button(OnDetectClicked) { text = "検出実行" };
            _detectButton.AddToClassList(FacialControlStyles.ActionButton);
            _detectButton.SetEnabled(false);
            actionSection.Add(_detectButton);

            root.Add(actionSection);

            // ========================================
            // 検出サマリーセクション
            // ========================================
            _summaryLabel = new Label("SkinnedMeshRenderer を選択してください。");
            _summaryLabel.AddToClassList(FacialControlStyles.InfoLabel);
            _summaryLabel.style.paddingLeft = 4;
            _summaryLabel.style.paddingRight = 4;
            _summaryLabel.style.marginBottom = 4;
            root.Add(_summaryLabel);

            // ========================================
            // 検出結果リスト表示セクション
            // ========================================
            _resultListView = new ScrollView(ScrollViewMode.Vertical);
            _resultListView.style.flexGrow = 1;
            _resultListView.style.paddingLeft = 4;
            _resultListView.style.paddingRight = 4;
            root.Add(_resultListView);

            // ========================================
            // 生成ボタンセクション
            // ========================================
            var generateSection = new VisualElement();
            generateSection.style.flexDirection = FlexDirection.Row;
            generateSection.style.paddingLeft = 4;
            generateSection.style.paddingRight = 4;
            generateSection.style.marginTop = 4;
            generateSection.style.marginBottom = 4;

            _generateExpressionsButton = new Button(OnGenerateExpressionsClicked) { text = "Expression 生成" };
            _generateExpressionsButton.AddToClassList(FacialControlStyles.ActionButton);
            _generateExpressionsButton.SetEnabled(false);
            generateSection.Add(_generateExpressionsButton);

            _generateOscButton = new Button(OnGenerateOscClicked) { text = "OSC マッピング生成" };
            _generateOscButton.AddToClassList(FacialControlStyles.ActionButton);
            _generateOscButton.style.marginLeft = 4;
            _generateOscButton.SetEnabled(false);
            generateSection.Add(_generateOscButton);

            root.Add(generateSection);

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
        /// SkinnedMeshRenderer 変更時の処理
        /// </summary>
        private void OnRendererChanged(ChangeEvent<UnityEngine.Object> evt)
        {
            _targetRenderer = evt.newValue as SkinnedMeshRenderer;
            _hasDetected = false;
            _detectResult = default;
            _oscMappings = null;
            _groupedResults = null;

            _detectButton?.SetEnabled(_targetRenderer != null);
            _generateExpressionsButton?.SetEnabled(false);
            _generateOscButton?.SetEnabled(false);

            if (_targetRenderer != null)
            {
                var blendShapeNames = _editorService.GetBlendShapeNames(_targetRenderer);
                _summaryLabel.text = $"BlendShape 数: {blendShapeNames.Length}  |  検出を実行してください。";
            }
            else
            {
                _summaryLabel.text = "SkinnedMeshRenderer を選択してください。";
            }

            _resultListView?.Clear();
        }

        /// <summary>
        /// 検出実行ボタン押下時の処理
        /// </summary>
        private void OnDetectClicked()
        {
            if (_targetRenderer == null || _targetRenderer.sharedMesh == null)
            {
                ShowStatus("有効な SkinnedMeshRenderer を選択してください。", isError: true);
                return;
            }

            var blendShapeNames = _editorService.GetBlendShapeNames(_targetRenderer);

            if (blendShapeNames.Length == 0)
            {
                ShowStatus("BlendShape が見つかりません。", isError: true);
                _hasDetected = false;
                UpdateGenerateButtons();
                _resultListView?.Clear();
                _summaryLabel.text = "BlendShape が見つかりません。";
                return;
            }

            // 検出実行
            _detectResult = _editorService.DetectFromRenderer(_targetRenderer);
            _oscMappings = _editorService.GenerateOscMapping(_detectResult.DetectedNames);
            _groupedResults = ARKitDetector.GroupByLayer(_detectResult.DetectedNames);
            _hasDetected = true;

            // UI 更新
            UpdateSummary(blendShapeNames.Length);
            RebuildResultList();
            UpdateGenerateButtons();

            if (_detectResult.DetectedNames.Length > 0)
            {
                ShowStatus(
                    $"検出完了: {_detectResult.DetectedNames.Length} パラメータが見つかりました。",
                    isError: false);
            }
            else
            {
                ShowStatus("ARKit / PerfectSync パラメータが見つかりませんでした。", isError: false);
            }
        }

        /// <summary>
        /// サマリーラベルを更新する
        /// </summary>
        private void UpdateSummary(int totalBlendShapes)
        {
            int arkitCount = ARKitDetector.DetectARKit(_detectResult.DetectedNames).Length;
            int psCount = ARKitDetector.DetectPerfectSync(_detectResult.DetectedNames).Length;
            int arkitTotal = ARKitDetector.ARKit52Names.Length;
            int psTotal = ARKitDetector.PerfectSyncNames.Length;

            // PerfectSync 対応 = ARKit 52 + 拡張 13 の全 65 パラメータを持つモデル
            bool isPerfectSyncCompatible = arkitCount == arkitTotal && psCount == psTotal;
            string compatStatus = isPerfectSyncCompatible
                ? "PerfectSync 対応"
                : arkitCount == arkitTotal
                    ? "ARKit 52 対応"
                    : arkitCount > 0
                        ? "ARKit 部分対応"
                        : "未対応";

            _summaryLabel.text =
                $"総 BlendShape: {totalBlendShapes}  |  " +
                $"ARKit: {arkitCount}/{arkitTotal}  |  " +
                $"PerfectSync 拡張: {psCount}/{psTotal}  |  " +
                $"合計検出: {_detectResult.DetectedNames.Length}  |  " +
                $"判定: {compatStatus}";
        }

        /// <summary>
        /// 生成ボタンの有効/無効を更新する
        /// </summary>
        private void UpdateGenerateButtons()
        {
            bool canGenerate = _hasDetected && _detectResult.DetectedNames.Length > 0;
            _generateExpressionsButton?.SetEnabled(canGenerate);
            _generateOscButton?.SetEnabled(canGenerate);
        }

        /// <summary>
        /// 検出結果リスト UI を再構築する
        /// </summary>
        private void RebuildResultList()
        {
            if (_resultListView == null)
                return;

            _resultListView.Clear();

            if (!_hasDetected || _detectResult.DetectedNames.Length == 0)
            {
                var emptyLabel = new Label("検出されたパラメータはありません。");
                emptyLabel.AddToClassList(FacialControlStyles.InfoLabel);
                emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                emptyLabel.style.marginTop = 20;
                _resultListView.Add(emptyLabel);
                return;
            }

            // レイヤーグループ別に表示
            foreach (var kvp in _groupedResults)
            {
                var groupContainer = CreateGroupSection(kvp.Key, kvp.Value);
                _resultListView.Add(groupContainer);
            }
        }

        /// <summary>
        /// レイヤーグループセクションを生成する
        /// </summary>
        private VisualElement CreateGroupSection(string groupName, string[] parameterNames)
        {
            var container = new VisualElement();
            container.style.marginBottom = 8;

            // グループヘッダー
            var foldout = new Foldout { text = $"{groupName} ({parameterNames.Length})", value = true };
            foldout.style.marginBottom = 2;

            for (int i = 0; i < parameterNames.Length; i++)
            {
                var item = CreateParameterItem(parameterNames[i]);
                foldout.Add(item);
            }

            container.Add(foldout);
            return container;
        }

        /// <summary>
        /// 個々のパラメータ表示アイテムを生成する
        /// </summary>
        private VisualElement CreateParameterItem(string parameterName)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.paddingTop = 2;
            container.style.paddingBottom = 2;
            container.style.paddingLeft = 8;

            // ARKit か PerfectSync か判定
            bool isARKit = Array.IndexOf(ARKitDetector.ARKit52Names, parameterName) >= 0;
            string typeTag = isARKit ? "[ARKit]" : "[PerfectSync]";

            var typeLabel = new Label(typeTag);
            typeLabel.style.width = 100;
            typeLabel.style.color = isARKit
                ? new Color(0.4f, 0.8f, 1f)
                : new Color(1f, 0.7f, 0.4f);
            typeLabel.style.fontSize = 11;
            container.Add(typeLabel);

            var nameLabel = new Label(parameterName);
            nameLabel.style.flexGrow = 1;
            nameLabel.style.fontSize = 12;
            container.Add(nameLabel);

            return container;
        }

        /// <summary>
        /// Expression 生成ボタン押下時の処理。
        /// 確認ダイアログを表示後、JSON ファイルとして保存する。
        /// </summary>
        private void OnGenerateExpressionsClicked()
        {
            if (!_hasDetected || _detectResult.GeneratedExpressions.Length == 0)
            {
                ShowStatus("生成する Expression がありません。", isError: true);
                return;
            }

            var expressions = _detectResult.GeneratedExpressions;
            string message =
                $"{expressions.Length} 個の Expression を生成します:\n\n";

            for (int i = 0; i < expressions.Length; i++)
            {
                message += $"  - {expressions[i].Name} (レイヤー: {expressions[i].Layer}, " +
                           $"BlendShape: {expressions[i].BlendShapeValues.Length})\n";
            }

            message += "\nJSON ファイルとして保存しますか？";

            if (!EditorUtility.DisplayDialog("Expression 自動生成", message, "保存", "キャンセル"))
                return;

            var path = EditorUtility.SaveFilePanel(
                "Expression JSON の保存先",
                "",
                "arkit_expressions",
                "json");

            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                _editorService.SaveExpressionsAsProfileJson(expressions, path);
                ShowStatus($"Expression を保存しました: {System.IO.Path.GetFileName(path)}", isError: false);
            }
            catch (Exception ex)
            {
                ShowStatus($"保存エラー: {ex.Message}", isError: true);
                Debug.LogError($"[ARKitDetectorWindow] Expression 保存エラー: {ex}");
            }
        }

        /// <summary>
        /// OSC マッピング生成ボタン押下時の処理。
        /// 確認ダイアログを表示後、config.json として保存する。
        /// </summary>
        private void OnGenerateOscClicked()
        {
            if (_oscMappings == null || _oscMappings.Length == 0)
            {
                ShowStatus("生成する OSC マッピングがありません。", isError: true);
                return;
            }

            string message =
                $"{_oscMappings.Length} 個の OSC マッピングを生成します:\n\n";

            int previewCount = Math.Min(_oscMappings.Length, 5);
            for (int i = 0; i < previewCount; i++)
            {
                message += $"  {_oscMappings[i].OscAddress} → {_oscMappings[i].BlendShapeName}\n";
            }

            if (_oscMappings.Length > previewCount)
            {
                message += $"  ... 他 {_oscMappings.Length - previewCount} 件\n";
            }

            message += "\nconfig.json として保存しますか？";

            if (!EditorUtility.DisplayDialog("OSC マッピング自動生成", message, "保存", "キャンセル"))
                return;

            var path = EditorUtility.SaveFilePanel(
                "config.json の保存先",
                "",
                "config",
                "json");

            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                _editorService.SaveOscMappingAsConfigJson(_oscMappings, path);
                ShowStatus($"OSC マッピングを保存しました: {System.IO.Path.GetFileName(path)}", isError: false);
            }
            catch (Exception ex)
            {
                ShowStatus($"保存エラー: {ex.Message}", isError: true);
                Debug.LogError($"[ARKitDetectorWindow] OSC マッピング保存エラー: {ex}");
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
