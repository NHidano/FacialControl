using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Sampling;
using Hidano.FacialControl.Editor.Tools;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Tools
{
    /// <summary>
    /// ExpressionCreatorWindow の AnimationClip ベイク経路テスト。
    /// ベイクロジックは <see cref="ExpressionClipBakery"/> static helper に抽出済み。
    /// </summary>
    [TestFixture]
    public class ExpressionCreatorWindowTests
    {
        private readonly List<UnityEngine.Object> _trackedObjects = new List<UnityEngine.Object>();
        private readonly List<string> _trackedFiles = new List<string>();
        private readonly List<string> _trackedAssetPaths = new List<string>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _trackedObjects.Count; i++)
            {
                if (_trackedObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_trackedObjects[i]);
                }
            }
            _trackedObjects.Clear();

            for (int i = 0; i < _trackedFiles.Count; i++)
            {
                if (File.Exists(_trackedFiles[i]))
                    File.Delete(_trackedFiles[i]);
            }
            _trackedFiles.Clear();

            for (int i = 0; i < _trackedAssetPaths.Count; i++)
            {
                AssetDatabase.DeleteAsset(_trackedAssetPaths[i]);
            }
            _trackedAssetPaths.Clear();
        }

        private AnimationClip CreateTrackedClip()
        {
            var clip = new AnimationClip();
            _trackedObjects.Add(clip);
            return clip;
        }

        [Test]
        public void CreateGUI_DoesNotShowTransitionMetadataFoldout()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);

            var createGui = typeof(ExpressionCreatorWindow).GetMethod(
                "CreateGUI",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(createGui);

            createGui.Invoke(window, null);

            var foldouts = window.rootVisualElement.Query<Foldout>().ToList();
            Assert.AreEqual(0, foldouts.Count);
        }

        [Test]
        public void BakeButton_HasFlexShrinkZeroAndMinWidth()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);

            var createGui = typeof(ExpressionCreatorWindow).GetMethod(
                "CreateGUI",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(createGui);

            createGui.Invoke(window, null);

            var bakeButton = window.rootVisualElement.Q<Button>("expression-creator-bake-button");
            Assert.IsNotNull(bakeButton);
            Assert.AreEqual(0f, bakeButton.style.flexShrink.value, 1e-5f);
            Assert.AreEqual(140f, bakeButton.style.minWidth.value.value, 1e-5f);
        }

        [Test]
        public void BakeButton_HasDoubledHeight()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);

            InvokeCreateGUI(window);

            var bakeButton = window.rootVisualElement.Q<Button>("expression-creator-bake-button");
            Assert.IsNotNull(bakeButton);
            // 通常ボタン(約20px)の2倍
            Assert.AreEqual(40f, bakeButton.style.height.value.value, 1e-5f);
        }

        [Test]
        public void CreateGUI_AddsSavePreviewPngButtonToLeftPanel()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);

            InvokeCreateGUI(window);

            var leftPanel = window.rootVisualElement.Q<VisualElement>("expression-creator-left-panel");
            Assert.IsNotNull(leftPanel);

            var saveButton = leftPanel.Q<Button>("expression-creator-save-preview-png-button");
            Assert.IsNotNull(saveButton);
            Assert.AreEqual("プレビューを PNG として保存", saveButton.text);
            Assert.IsNotNull(saveButton.clickable);
        }

        [Test]
        public void CreateGUI_AddsTwoClipModeTabs()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);

            InvokeCreateGUI(window);

            var tabView = window.rootVisualElement.Q<TabView>("expression-creator-clip-tab-view");
            Assert.IsNotNull(tabView);

            var registeredTab = window.rootVisualElement.Q<Tab>("expression-creator-registered-tab");
            Assert.IsNotNull(registeredTab);
            Assert.AreEqual("登録済み Expression から編集", registeredTab.label);

            var clipEditTab = window.rootVisualElement.Q<Tab>("expression-creator-clip-edit-tab");
            Assert.IsNotNull(clipEditTab);
            Assert.AreEqual("AnimationClip を作成・編集", clipEditTab.label);

            // 「AnimationClip を作成・編集」タブ内に既存 Clip 編集 / 新規 Clip 作成のボタンを持つ
            var projectButton = clipEditTab.Q<Button>("expression-creator-edit-project-clip-button");
            Assert.IsNotNull(projectButton);
            Assert.AreEqual("既存 Clip を編集", projectButton.text);

            var createButton = clipEditTab.Q<Button>("expression-creator-create-new-clip-button");
            Assert.IsNotNull(createButton);
            Assert.AreEqual("新規 Clip を作成", createButton.text);

            // 登録済みタブ内にドロップダウンを持つ
            var dropdown = registeredTab.Q<DropdownField>("expression-creator-registered-clip-dropdown");
            Assert.IsNotNull(dropdown);
        }

        [Test]
        public void CreateGUI_ClipRowHiddenUntilModeSelected()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);

            InvokeCreateGUI(window);

            var clipRow = window.rootVisualElement.Q<VisualElement>("expression-creator-clip-row");
            Assert.IsNotNull(clipRow);
            Assert.AreEqual(DisplayStyle.None, clipRow.style.display.value);

            // 登録済み Clip が 0 件のためドロップダウンは非表示、案内 HelpBox が表示される
            var dropdown = window.rootVisualElement.Q<DropdownField>("expression-creator-registered-clip-dropdown");
            Assert.IsNotNull(dropdown);
            Assert.AreEqual(DisplayStyle.None, dropdown.style.display.value);

            var helpBox = window.rootVisualElement.Q<HelpBox>("expression-creator-registered-clip-help");
            Assert.IsNotNull(helpBox);
            Assert.AreEqual(DisplayStyle.Flex, helpBox.style.display.value);
        }

        [Test]
        public void EditProjectClipButton_Click_ShowsClipRow()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);

            InvokeCreateGUI(window);

            InvokePrivateMethod(window, "OnEditProjectClipClicked");

            var clipRow = window.rootVisualElement.Q<VisualElement>("expression-creator-clip-row");
            Assert.AreEqual(DisplayStyle.Flex, clipRow.style.display.value);
        }

        [Test]
        public void EditRegisteredClip_WithCharacterSO_PopulatesDropdownAndAssignsClip()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);
            InvokeCreateGUI(window);

            var clipA = CreateTrackedClip();
            clipA.name = "SmileClip";
            var model = CreateControllerModel(("笑顔", clipA), ("Clip なし", null));

            InvokePrivateMethod(window, "ApplyModelChange", model);

            var dropdown = window.rootVisualElement.Q<DropdownField>("expression-creator-registered-clip-dropdown");
            Assert.AreEqual(DisplayStyle.Flex, dropdown.style.display.value);
            // Clip 未設定の Expression は選択肢に含まれない
            Assert.AreEqual(1, dropdown.choices.Count);
            StringAssert.Contains("笑顔", dropdown.choices[0]);

            // 登録済み Clip があるため案内 HelpBox は非表示
            var helpBox = window.rootVisualElement.Q<HelpBox>("expression-creator-registered-clip-help");
            Assert.AreEqual(DisplayStyle.None, helpBox.style.display.value);

            InvokePrivateMethod(window, "ApplyRegisteredClipSelection", 0);

            var clipField = (ObjectField)GetPrivateField(window, "_clipField");
            Assert.AreSame(clipA, clipField.value);
            Assert.AreSame(clipA, GetPrivateField(window, "_targetClip"));

            var clipRow = window.rootVisualElement.Q<VisualElement>("expression-creator-clip-row");
            Assert.AreEqual(DisplayStyle.Flex, clipRow.style.display.value);
        }

        [Test]
        public void CreateGUI_AddsTrackTargetFieldBelowModelField()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);

            InvokeCreateGUI(window);

            var leftPanel = window.rootVisualElement.Q<VisualElement>("expression-creator-left-panel");
            var trackField = leftPanel.Q<ObjectField>("expression-creator-track-target-field");
            Assert.IsNotNull(trackField);
            Assert.AreEqual(typeof(Transform), trackField.objectType);

            var modelField = (ObjectField)GetPrivateField(window, "_modelField");
            // モデルスロットの直下に配置される
            Assert.AreEqual(leftPanel.IndexOf(modelField) + 1, leftPanel.IndexOf(trackField));
        }

        [Test]
        public void ApplyModelChange_GenericModel_AutoResolvesNeckJointAsTrackTarget()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);
            InvokeCreateGUI(window);

            var model = new GameObject("Model");
            _trackedObjects.Add(model);
            var armature = new GameObject("Armature");
            armature.transform.SetParent(model.transform);
            var neck = new GameObject("neck_01");
            neck.transform.SetParent(armature.transform);

            InvokePrivateMethod(window, "ApplyModelChange", model);

            var trackField = (ObjectField)GetPrivateField(window, "_trackTargetField");
            Assert.AreSame(neck.transform, trackField.value);
        }

        [Test]
        public void ExportAllButton_WithoutController_DisabledWithReasonTooltip()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);

            InvokeCreateGUI(window);

            var exportButton = window.rootVisualElement.Q<Button>("expression-creator-export-all-button");
            Assert.IsNotNull(exportButton);
            Assert.IsFalse(exportButton.enabledSelf);
            Assert.IsNotEmpty(exportButton.tooltip);

            var container = window.rootVisualElement.Q<VisualElement>("expression-creator-export-all-container");
            Assert.IsNotNull(container);
            Assert.IsNotEmpty(container.tooltip);
        }

        [Test]
        public void ExportAllButton_WithControllerAndRegisteredClips_Enabled()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);
            InvokeCreateGUI(window);

            var clip = CreateTrackedClip();
            var model = CreateControllerModel(("笑顔", clip));

            InvokePrivateMethod(window, "ApplyModelChange", model);

            var exportButton = window.rootVisualElement.Q<Button>("expression-creator-export-all-button");
            Assert.IsTrue(exportButton.enabledSelf);
        }

        [Test]
        public void ExportAllHandler_WithControllerAndSO_WritesPngPerExpression()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);
            InvokeCreateGUI(window);

            var clipA = CreateTrackedClip();
            var clipB = CreateTrackedClip();
            var model = CreateControllerModel(("笑顔", clipA), ("怒り", clipB), ("Clip なし", null));

            InvokePrivateMethod(window, "ApplyModelChange", model);

            var exportFolder = Path.Combine(Path.GetTempPath(), $"expression-export-{Guid.NewGuid():N}");
            var writtenPaths = new List<string>();
            var capturedSizes = new List<(int width, int height)>();

            SetPrivateField(window, "_exportFolderProvider", (Func<string>)(() => exportFolder));
            SetPrivateField(window, "_previewTextureCapture", (Func<int, int, Texture2D>)((width, height) =>
            {
                capturedSizes.Add((width, height));
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.Apply();
                return texture;
            }));
            SetPrivateField(window, "_pngFileWriter", (Action<string, byte[]>)((path, bytes) => writtenPaths.Add(path)));

            // Window 展開直後の「Expression 未選択」状態でも一括書き出しは成立すること
            Assert.IsNull(GetPrivateField(window, "_targetClip"));

            InvokePrivateMethod(window, "OnExportAllExpressionPreviewsClicked");

            // Clip 付き Expression の数だけ PNG が書き出される
            Assert.AreEqual(2, writtenPaths.Count);

            // 書き出し画像サイズは 512x512
            foreach (var size in capturedSizes)
            {
                Assert.AreEqual(512, size.width);
                Assert.AreEqual(512, size.height);
            }

            // ファイル名は {モデル名}_{Expression 名}_{yyyyMMdd-HHmm}.png
            StringAssert.IsMatch(@"ControllerModel_笑顔_\d{8}-\d{4}\.png$", writtenPaths[0]);
            StringAssert.IsMatch(@"ControllerModel_怒り_\d{8}-\d{4}\.png$", writtenPaths[1]);
        }

        [Test]
        public void RestoreSliderValues_ClipContainsMissingBlendShape_ShowsYellowWarning()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);
            InvokeCreateGUI(window);

            var model = CreateModelWithBlendShapes(("Face", new[] { "Smile" }));
            InvokePrivateMethod(window, "ApplyModelChange", model);

            var clip = CreateTrackedClip();
            ExpressionClipBakery.Bake(clip, new List<ExpressionClipBakery.BlendShapeBakeEntry>
            {
                new ExpressionClipBakery.BlendShapeBakeEntry("Face", "Smile", 0.5f),
                new ExpressionClipBakery.BlendShapeBakeEntry("Face", "Unknown", 1.0f),
            }, 0.25f, TransitionCurvePreset.Linear);

            SetPrivateField(window, "_targetClip", clip);
            InvokePrivateMethod(window, "RestoreSliderValuesFromTargetClip");

            var warning = window.rootVisualElement.Q<Label>("expression-creator-missing-blendshape-warning");
            Assert.IsNotNull(warning);
            Assert.AreEqual(DisplayStyle.Flex, warning.style.display.value);
            StringAssert.Contains("Unknown", warning.text);
            // 黄色系の文字色
            var color = warning.style.color.value;
            Assert.Greater(color.r, 0.9f);
            Assert.Greater(color.g, 0.7f);
            Assert.Less(color.b, 0.5f);

            // 一括削除ボタンも表示される
            var deleteButton = window.rootVisualElement.Q<Button>(
                "expression-creator-delete-missing-blendshape-button");
            Assert.IsNotNull(deleteButton);
            Assert.AreEqual(DisplayStyle.Flex, deleteButton.style.display.value);
        }

        [Test]
        public void DeleteMissingBlendShapes_RemovesOnlyMissingCurves_AndHidesWarning()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);
            InvokeCreateGUI(window);

            var model = CreateModelWithBlendShapes(("Face", new[] { "Smile" }));
            InvokePrivateMethod(window, "ApplyModelChange", model);

            var clip = CreateTrackedClip();
            ExpressionClipBakery.Bake(clip, new List<ExpressionClipBakery.BlendShapeBakeEntry>
            {
                new ExpressionClipBakery.BlendShapeBakeEntry("Face", "Smile", 0.5f),
                new ExpressionClipBakery.BlendShapeBakeEntry("Face", "Unknown", 1.0f),
                new ExpressionClipBakery.BlendShapeBakeEntry("Ghost", "Vanished", 0.75f),
            }, 0.25f, TransitionCurvePreset.Linear);

            SetPrivateField(window, "_targetClip", clip);
            InvokePrivateMethod(window, "RestoreSliderValuesFromTargetClip");

            InvokePrivateMethod(window, "OnDeleteMissingBlendShapesClicked");

            // 存在しない BlendShape のカーブのみ削除され、既存カーブは残る
            var bindings = AnimationUtility.GetCurveBindings(clip);
            Assert.AreEqual(1, bindings.Length);
            Assert.AreEqual("Face", bindings[0].path);
            Assert.AreEqual("blendShape.Smile", bindings[0].propertyName);

            // 警告と一括削除ボタンは非表示に戻る
            var warning = window.rootVisualElement.Q<Label>("expression-creator-missing-blendshape-warning");
            Assert.AreEqual(DisplayStyle.None, warning.style.display.value);

            var deleteButton = window.rootVisualElement.Q<Button>(
                "expression-creator-delete-missing-blendshape-button");
            Assert.AreEqual(DisplayStyle.None, deleteButton.style.display.value);
        }

        [Test]
        public void DeleteMissingBlendShapes_WithoutTargetClip_DoesNothing()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);
            InvokeCreateGUI(window);

            // _targetClip 未設定でも例外なく無視される
            Assert.DoesNotThrow(() => InvokePrivateMethod(window, "OnDeleteMissingBlendShapesClicked"));
        }

        [Test]
        public void RestoreSliderValues_AllBlendShapesExist_HidesWarning()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);
            InvokeCreateGUI(window);

            var model = CreateModelWithBlendShapes(("Face", new[] { "Smile" }));
            InvokePrivateMethod(window, "ApplyModelChange", model);

            var clip = CreateTrackedClip();
            ExpressionClipBakery.Bake(clip, new List<ExpressionClipBakery.BlendShapeBakeEntry>
            {
                new ExpressionClipBakery.BlendShapeBakeEntry("Face", "Smile", 0.5f),
            }, 0.25f, TransitionCurvePreset.Linear);

            SetPrivateField(window, "_targetClip", clip);
            InvokePrivateMethod(window, "RestoreSliderValuesFromTargetClip");

            var warning = window.rootVisualElement.Q<Label>("expression-creator-missing-blendshape-warning");
            Assert.AreEqual(DisplayStyle.None, warning.style.display.value);

            var deleteButton = window.rootVisualElement.Q<Button>(
                "expression-creator-delete-missing-blendshape-button");
            Assert.AreEqual(DisplayStyle.None, deleteButton.style.display.value);
        }

        [Test]
        public void BlendShapeSliderRow_UsesZeroToHundredRange()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);
            InvokeCreateGUI(window);

            var model = CreateModelWithBlendShapes(("Face", new[] { "Smile" }));
            InvokePrivateMethod(window, "ApplyModelChange", model);

            var clip = CreateTrackedClip();
            ExpressionClipBakery.Bake(clip, new List<ExpressionClipBakery.BlendShapeBakeEntry>
            {
                new ExpressionClipBakery.BlendShapeBakeEntry("Face", "Smile", 0.5f),
            }, 0.25f, TransitionCurvePreset.Linear);
            SetPrivateField(window, "_targetClip", clip);
            InvokePrivateMethod(window, "RestoreSliderValuesFromTargetClip");

            // ScrollView の Scroller 内部にも Slider が存在するため contentContainer 配下から取得する
            var listView = (ScrollView)GetPrivateField(window, "_blendShapeListView");
            var slider = listView.contentContainer.Q<Slider>();
            Assert.IsNotNull(slider);
            Assert.AreEqual(0f, slider.lowValue, 1e-5f);
            Assert.AreEqual(100f, slider.highValue, 1e-5f);
            // 内部正規化値 0.5 は表示値 50 に対応する
            Assert.AreEqual(50f, slider.value, 1e-3f);

            var valueField = listView.contentContainer.Q<FloatField>();
            Assert.AreEqual(50f, valueField.value, 1e-3f);
        }

        [Test]
        public void RendererFilterDropdown_ListsRenderersAndFilters()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);
            InvokeCreateGUI(window);

            var model = CreateModelWithBlendShapes(
                ("Face", new[] { "Smile", "Anger" }),
                ("Body", new[] { "Fat" }));
            InvokePrivateMethod(window, "ApplyModelChange", model);

            var dropdown = window.rootVisualElement.Q<DropdownField>("expression-creator-renderer-filter-dropdown");
            Assert.IsNotNull(dropdown);
            Assert.AreEqual(3, dropdown.choices.Count);
            Assert.AreEqual("すべて", dropdown.choices[0]);
            CollectionAssert.Contains(dropdown.choices, "Face");
            CollectionAssert.Contains(dropdown.choices, "Body");

            // ScrollView の Scroller 内部にも Slider が存在するため contentContainer 配下に限定して数える
            var listView = (ScrollView)GetPrivateField(window, "_blendShapeListView");
            Assert.AreEqual(3, listView.contentContainer.Query<Slider>().ToList().Count);

            // "Face" レンダラーのみに絞り込む
            int faceIndex = dropdown.choices.IndexOf("Face");
            InvokePrivateMethod(window, "ApplyRendererFilter", faceIndex);

            Assert.AreEqual(2, listView.contentContainer.Query<Slider>().ToList().Count);

            // 「すべて」に戻す
            InvokePrivateMethod(window, "ApplyRendererFilter", 0);
            Assert.AreEqual(3, listView.contentContainer.Query<Slider>().ToList().Count);
        }

        [Test]
        public void MarkUnsavedChanges_ThenBake_ClearsHasUnsavedChanges()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);
            InvokeCreateGUI(window);

            var model = CreateModelWithBlendShapes(("Face", new[] { "Smile" }));
            InvokePrivateMethod(window, "ApplyModelChange", model);

            Assert.IsFalse(window.hasUnsavedChanges);

            InvokePrivateMethod(window, "MarkUnsavedChanges");
            Assert.IsTrue(window.hasUnsavedChanges);

            var assetPath = $"Assets/expression-creator-bake-{Guid.NewGuid():N}.anim";
            _trackedAssetPaths.Add(assetPath);
            var clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, assetPath);
            SetPrivateField(window, "_targetClip", clip);

            InvokePrivateMethod(window, "OnBakeClicked");

            Assert.IsFalse(window.hasUnsavedChanges);
        }

        [Test]
        public void SaveChanges_NoTargetClipAndDialogCancelled_Throws()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);
            InvokeCreateGUI(window);

            var model = CreateModelWithBlendShapes(("Face", new[] { "Smile" }));
            InvokePrivateMethod(window, "ApplyModelChange", model);
            InvokePrivateMethod(window, "MarkUnsavedChanges");

            SetPrivateField(window, "_createClipPathProvider", (Func<string>)(() => string.Empty));

            Assert.Throws<InvalidOperationException>(() => window.SaveChanges());
            Assert.IsTrue(window.hasUnsavedChanges);
        }

        [Test]
        public void SaveChanges_WithTargetClip_BakesAndClearsHasUnsavedChanges()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);
            InvokeCreateGUI(window);

            var model = CreateModelWithBlendShapes(("Face", new[] { "Smile" }));
            InvokePrivateMethod(window, "ApplyModelChange", model);
            InvokePrivateMethod(window, "MarkUnsavedChanges");

            var assetPath = $"Assets/expression-creator-save-{Guid.NewGuid():N}.anim";
            _trackedAssetPaths.Add(assetPath);
            var clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, assetPath);
            SetPrivateField(window, "_targetClip", clip);

            window.SaveChanges();

            Assert.IsFalse(window.hasUnsavedChanges);
        }

        [Test]
        public void SavePreviewPngHandler_WithSpecifiedPath_WritesPngFile()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);
            InvokeCreateGUI(window);

            // Expression 未選択時は警告して保存しない仕様のため、Clip を設定しておく
            SetPrivateField(window, "_targetClip", CreateTrackedClip());

            var outputPath = Path.Combine(Path.GetTempPath(), $"expression-preview-{Guid.NewGuid():N}.png");
            _trackedFiles.Add(outputPath);
            var capturedWidth = 0;
            var capturedHeight = 0;

            SetPrivateField(window, "_savePreviewPathProvider", (Func<string, string>)(_ => outputPath));
            SetPrivateField(window, "_previewTextureCapture", (Func<int, int, Texture2D>)((width, height) =>
            {
                capturedWidth = width;
                capturedHeight = height;
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white });
                texture.Apply();
                return texture;
            }));
            SetPrivateField(window, "_pngFileWriter", (Action<string, byte[]>)File.WriteAllBytes);

            var handler = typeof(ExpressionCreatorWindow).GetMethod(
                "OnSavePreviewClicked",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(handler);

            handler.Invoke(window, null);

            // 書き出し画像サイズは画面上のプレビュー(256)とは独立に 512x512
            Assert.AreEqual(512, capturedWidth);
            Assert.AreEqual(512, capturedHeight);
            Assert.IsTrue(File.Exists(outputPath));

            var bytes = File.ReadAllBytes(outputPath);
            Assert.Greater(bytes.Length, 8);
            Assert.AreEqual((byte)0x89, bytes[0]);
            Assert.AreEqual((byte)'P', bytes[1]);
            Assert.AreEqual((byte)'N', bytes[2]);
            Assert.AreEqual((byte)'G', bytes[3]);
        }

        [Test]
        public void SavePreviewPngHandler_RegisteredExpressionClip_DefaultFileNameHasModelExpressionTimestamp()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);
            InvokeCreateGUI(window);

            var clip = CreateTrackedClip();
            clip.name = "SmileClip";
            var model = CreateControllerModel(("笑顔", clip));
            InvokePrivateMethod(window, "ApplyModelChange", model);
            SetPrivateField(window, "_targetClip", clip);

            string receivedDefaultFileName = null;
            SetPrivateField(window, "_savePreviewPathProvider", (Func<string, string>)(defaultFileName =>
            {
                receivedDefaultFileName = defaultFileName;
                // キャンセル扱いで保存はさせない
                return string.Empty;
            }));

            InvokePrivateMethod(window, "OnSavePreviewClicked");

            // {モデル名}_{Expression 名}_{yyyyMMdd-HHmm}.png
            Assert.IsNotNull(receivedDefaultFileName);
            StringAssert.IsMatch(@"^ControllerModel_笑顔_\d{8}-\d{4}\.png$", receivedDefaultFileName);
        }

        [Test]
        public void SavePreviewPngHandler_UnregisteredClip_DefaultFileNameUsesClipName()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);
            InvokeCreateGUI(window);

            var clip = CreateTrackedClip();
            clip.name = "MyClip";
            SetPrivateField(window, "_targetClip", clip);

            string receivedDefaultFileName = null;
            SetPrivateField(window, "_savePreviewPathProvider", (Func<string, string>)(defaultFileName =>
            {
                receivedDefaultFileName = defaultFileName;
                return string.Empty;
            }));

            InvokePrivateMethod(window, "OnSavePreviewClicked");

            // モデル未設定のため Expression 名（Clip 名）と日時のみ
            Assert.IsNotNull(receivedDefaultFileName);
            StringAssert.IsMatch(@"^MyClip_\d{8}-\d{4}\.png$", receivedDefaultFileName);
        }

        [Test]
        public void SavePreviewPngHandler_WithoutExpressionSelected_LogsWarningAndDoesNotSave()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);
            InvokeCreateGUI(window);

            var pathProviderCalled = false;
            SetPrivateField(window, "_savePreviewPathProvider", (Func<string, string>)(_ =>
            {
                pathProviderCalled = true;
                return string.Empty;
            }));

            LogAssert.Expect(LogType.Warning, new Regex("Expression が選択されていない"));
            InvokePrivateMethod(window, "OnSavePreviewClicked");

            Assert.IsFalse(pathProviderCalled, "Expression 未選択時は保存ダイアログを開かないこと");
        }

        [Test]
        public void LastExportFolder_RoundTrip_ReturnsSavedFolderOnlyWhileDirectoryExists()
        {
            const string prefsKey = "Hidano.FacialControl.ExpressionCreatorWindow.LastExportFolder";
            var hadKey = EditorPrefs.HasKey(prefsKey);
            var originalValue = EditorPrefs.GetString(prefsKey, string.Empty);
            var tempDir = Path.Combine(Path.GetTempPath(), $"expression-export-prefs-{Guid.NewGuid():N}");

            try
            {
                Directory.CreateDirectory(tempDir);

                InvokeStaticMethod("SaveLastExportFolder", tempDir);
                Assert.AreEqual(tempDir, InvokeStaticMethod("LoadLastExportFolder"));

                // 記憶先ディレクトリが消えていたら既定位置（空文字）へフォールバック
                Directory.Delete(tempDir, true);
                Assert.AreEqual(string.Empty, InvokeStaticMethod("LoadLastExportFolder"));

                // ダイアログキャンセル（空文字）は既存の記憶を上書きしない
                Directory.CreateDirectory(tempDir);
                InvokeStaticMethod("SaveLastExportFolder", string.Empty);
                Assert.AreEqual(tempDir, InvokeStaticMethod("LoadLastExportFolder"));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);

                if (hadKey)
                    EditorPrefs.SetString(prefsKey, originalValue);
                else
                    EditorPrefs.DeleteKey(prefsKey);
            }
        }

        [Test]
        public void CreateNewClipHandler_WithSpecifiedPath_CreatesAndAssignsClip()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);
            InvokeCreateGUI(window);

            var assetPath = $"Assets/expression-creator-created-{Guid.NewGuid():N}.anim";
            _trackedAssetPaths.Add(assetPath);
            SetPrivateField(window, "_createClipPathProvider", (Func<string>)(() => assetPath));

            InvokePrivateMethod(window, "OnCreateNewClipClicked");

            var createdClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            Assert.IsNotNull(createdClip);

            var clipField = (ObjectField)GetPrivateField(window, "_clipField");
            Assert.AreSame(createdClip, clipField.value);
            Assert.AreSame(createdClip, GetPrivateField(window, "_targetClip"));
        }

        [Test]
        public void CreateNewClipHandler_DialogCancelled_DoesNotCreateOrChangeClip()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);
            InvokeCreateGUI(window);

            var existingClip = CreateTrackedClip();
            var clipField = (ObjectField)GetPrivateField(window, "_clipField");
            clipField.value = existingClip;
            SetPrivateField(window, "_targetClip", existingClip);

            var createAssetCalled = false;
            SetPrivateField(window, "_createClipPathProvider", (Func<string>)(() => string.Empty));
            SetPrivateField(window, "_clipAssetCreator", (Action<AnimationClip, string>)((_, _) => createAssetCalled = true));

            InvokePrivateMethod(window, "OnCreateNewClipClicked");

            Assert.IsFalse(createAssetCalled);
            Assert.AreSame(existingClip, clipField.value);
            Assert.AreSame(existingClip, GetPrivateField(window, "_targetClip"));
        }

        [Test]
        public void Bake_BlendShapeSliders_WritesEditorCurves()
        {
            var clip = CreateTrackedClip();
            var entries = new List<ExpressionClipBakery.BlendShapeBakeEntry>
            {
                new ExpressionClipBakery.BlendShapeBakeEntry("Body/Face", "Smile", 0.5f),
                new ExpressionClipBakery.BlendShapeBakeEntry("Body/Face", "Anger", 0.25f),
                new ExpressionClipBakery.BlendShapeBakeEntry("Body/Head", "Surprise", 1.0f),
            };

            ExpressionClipBakery.Bake(clip, entries, 0.25f, TransitionCurvePreset.Linear);

            var bindings = AnimationUtility.GetCurveBindings(clip);
            // BlendShape 3 本のみ（メタデータは AnimationEvent 側で運搬）
            Assert.AreEqual(3, bindings.Length);

            var byKey = new Dictionary<string, float>();
            for (int i = 0; i < bindings.Length; i++)
            {
                var b = bindings[i];
                Assert.AreEqual(typeof(SkinnedMeshRenderer), b.type);
                Assert.IsTrue(b.propertyName.StartsWith("blendShape."),
                    $"Unexpected propertyName: {b.propertyName}");
                var curve = AnimationUtility.GetEditorCurve(clip, b);
                byKey[$"{b.path}|{b.propertyName}"] = curve.Evaluate(0f);
            }

            // 入力エントリは正規化 0..1。AnimationClip カーブは Unity 標準 0..100 スケールで書き込まれる。
            Assert.AreEqual(50f, byKey["Body/Face|blendShape.Smile"], 1e-5f);
            Assert.AreEqual(25f, byKey["Body/Face|blendShape.Anger"], 1e-5f);
            Assert.AreEqual(100f, byKey["Body/Head|blendShape.Surprise"], 1e-5f);
        }

        [Test]
        public void Bake_DoesNotWriteAnimationEvents()
        {
            var clip = CreateTrackedClip();
            var entries = new List<ExpressionClipBakery.BlendShapeBakeEntry>
            {
                new ExpressionClipBakery.BlendShapeBakeEntry("Body/Face", "Smile", 0.5f),
            };

            ExpressionClipBakery.Bake(clip, entries, 0.7f, TransitionCurvePreset.EaseInOut);

            var events = AnimationUtility.GetAnimationEvents(clip);
            Assert.IsNotNull(events);
            Assert.AreEqual(0, events.Length);
        }

        [Test]
        public void LoadExistingClip_RestoresSliderValues()
        {
            var clip = CreateTrackedClip();
            var entries = new List<ExpressionClipBakery.BlendShapeBakeEntry>
            {
                new ExpressionClipBakery.BlendShapeBakeEntry("Body/Face", "Smile", 0.5f),
                new ExpressionClipBakery.BlendShapeBakeEntry("Body/Face", "Anger", 0.25f),
                new ExpressionClipBakery.BlendShapeBakeEntry("Body/Head", "Surprise", 1.0f),
            };
            ExpressionClipBakery.Bake(clip, entries, 0.25f, TransitionCurvePreset.Linear);

            var sampler = new AnimationClipExpressionSampler();
            var loaded = ExpressionClipBakery.LoadBlendShapeValues(clip, sampler);

            Assert.AreEqual(3, loaded.Count);
            Assert.AreEqual(0.5f, loaded[("Body/Face", "Smile")], 1e-5f);
            Assert.AreEqual(0.25f, loaded[("Body/Face", "Anger")], 1e-5f);
            Assert.AreEqual(1.0f, loaded[("Body/Head", "Surprise")], 1e-5f);
        }

        [Test]
        public void Bake_NullClip_Throws()
        {
            var entries = new List<ExpressionClipBakery.BlendShapeBakeEntry>();
            Assert.Throws<ArgumentNullException>(() =>
                ExpressionClipBakery.Bake(null, entries, 0.25f, TransitionCurvePreset.Linear));
        }

        [Test]
        public void Bake_NullEntries_Throws()
        {
            var clip = CreateTrackedClip();
            Assert.Throws<ArgumentNullException>(() =>
                ExpressionClipBakery.Bake(clip, null, 0.25f, TransitionCurvePreset.Linear));
        }

        [Test]
        public void Bake_RebakeOverwritesExistingCurves()
        {
            var clip = CreateTrackedClip();
            // 1 回目のベイク: Smile + Anger
            var first = new List<ExpressionClipBakery.BlendShapeBakeEntry>
            {
                new ExpressionClipBakery.BlendShapeBakeEntry("Body/Face", "Smile", 0.4f),
                new ExpressionClipBakery.BlendShapeBakeEntry("Body/Face", "Anger", 0.8f),
            };
            ExpressionClipBakery.Bake(clip, first, 0.25f, TransitionCurvePreset.Linear);

            // 2 回目のベイク: Surprise のみ。旧 Smile / Anger は削除されるべき
            var second = new List<ExpressionClipBakery.BlendShapeBakeEntry>
            {
                new ExpressionClipBakery.BlendShapeBakeEntry("Body/Head", "Surprise", 1.0f),
            };
            ExpressionClipBakery.Bake(clip, second, 0.25f, TransitionCurvePreset.Linear);

            var bindings = AnimationUtility.GetCurveBindings(clip);
            Assert.AreEqual(1, bindings.Length, "再ベイクで旧 BlendShape カーブが削除されること");
            Assert.AreEqual("blendShape.Surprise", bindings[0].propertyName);
        }

        private static void InvokeCreateGUI(ExpressionCreatorWindow window)
        {
            var createGui = typeof(ExpressionCreatorWindow).GetMethod(
                "CreateGUI",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(createGui);

            createGui.Invoke(window, null);
        }

        private static void SetPrivateField(ExpressionCreatorWindow window, string fieldName, object value)
        {
            var field = typeof(ExpressionCreatorWindow).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);

            field.SetValue(window, value);
        }

        private static object GetPrivateField(ExpressionCreatorWindow window, string fieldName)
        {
            var field = typeof(ExpressionCreatorWindow).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);

            return field.GetValue(window);
        }

        private static object InvokeStaticMethod(string methodName, params object[] args)
        {
            var method = typeof(ExpressionCreatorWindow).GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method);

            return method.Invoke(null, args);
        }

        private static void InvokePrivateMethod(ExpressionCreatorWindow window, string methodName, params object[] args)
        {
            var method = typeof(ExpressionCreatorWindow).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);

            method.Invoke(window, args);
        }

        /// <summary>
        /// FacialController + FacialCharacterProfileSO 付きのモデルを生成する。
        /// 各タプルは (Expression 名, AnimationClip)。clip は null 許容（Clip 未設定 Expression）。
        /// </summary>
        private GameObject CreateControllerModel(params (string name, AnimationClip clip)[] expressions)
        {
            var model = new GameObject("ControllerModel");
            _trackedObjects.Add(model);

            var so = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            _trackedObjects.Add(so);

            for (int i = 0; i < expressions.Length; i++)
            {
                so.Expressions.Add(new ExpressionSerializable
                {
                    id = $"expr-{i}",
                    name = expressions[i].name,
                    animationClip = expressions[i].clip,
                });
            }

            var controller = model.AddComponent<FacialController>();
            controller.CharacterSO = so;
            return model;
        }

        /// <summary>
        /// BlendShape 付き SkinnedMeshRenderer を子に持つモデルを生成する。
        /// 各タプルは (レンダラー名, BlendShape 名配列)。
        /// </summary>
        private GameObject CreateModelWithBlendShapes(params (string rendererName, string[] shapes)[] renderers)
        {
            var model = new GameObject("BlendShapeModel");
            _trackedObjects.Add(model);

            for (int r = 0; r < renderers.Length; r++)
            {
                var child = new GameObject(renderers[r].rendererName);
                child.transform.SetParent(model.transform);

                var mesh = new Mesh();
                _trackedObjects.Add(mesh);
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                mesh.triangles = new[] { 0, 1, 2 };

                var deltas = new[] { Vector3.up, Vector3.up, Vector3.up };
                for (int s = 0; s < renderers[r].shapes.Length; s++)
                {
                    mesh.AddBlendShapeFrame(renderers[r].shapes[s], 100f, deltas, null, null);
                }

                var smr = child.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = mesh;
            }

            return model;
        }
    }
}
