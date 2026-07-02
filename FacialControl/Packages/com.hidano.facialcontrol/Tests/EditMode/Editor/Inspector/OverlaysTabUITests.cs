using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Hidano.FacialControl.Adapters.Json.Dto;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Editor.Inspector;
using Hidano.FacialControl.Editor.Windows.Routing;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Inspector
{
    [TestFixture]
    public class OverlaysTabUITests
    {
        private const string EmotionLayerName = "Emotion";
        private const string SecondaryLayerName = "Detail";
        private const string BlinkSlotName = "blink";
        private const string WinkSlotName = "wink";

        private FacialCharacterProfileSO _so;
        private UnityEditor.Editor _editor;
        private readonly List<AnimationClip> _clips = new List<AnimationClip>();

        [TearDown]
        public void TearDown()
        {
            if (_editor != null)
            {
                // 各テストの overlay 操作で予約された自動保存 delayCall を解除し、
                // TearDown 後に破棄済み Editor / StreamingAssets へ書き込まれるのを防ぐ。
                CancelPendingAutoSave(_editor);
                Object.DestroyImmediate(_editor);
                _editor = null;
            }

            RoutingEditorWindow[] windows = Resources.FindObjectsOfTypeAll<RoutingEditorWindow>();
            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i] != null)
                {
                    Object.DestroyImmediate(windows[i]);
                }
            }

            for (int i = 0; i < _clips.Count; i++)
            {
                if (_clips[i] != null)
                {
                    Object.DestroyImmediate(_clips[i]);
                }
            }
            _clips.Clear();

            if (_so != null)
            {
                Object.DestroyImmediate(_so);
                _so = null;
            }

            // OnDisable フラッシュ検証テストが StreamingAssets へ書き出した profile.json を掃除する。
            DeleteStreamingAssetsExport("OverlaysTabUITestProfile");
        }

        private static void DeleteStreamingAssetsExport(string profileName)
        {
            string exportDir = Path.Combine(
                UnityEngine.Application.streamingAssetsPath,
                FacialCharacterProfileSO.StreamingAssetsRootFolder,
                profileName);
            if (Directory.Exists(exportDir))
            {
                Directory.Delete(exportDir, recursive: true);
            }

            string metaPath = exportDir + ".meta";
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }
        }

        [Test]
        public void CreateInspectorGUI_ExpressionLibrarySection_RendersAllExpressionsFromAllLayers()
        {
            _so = CreateProfileWithSlots(BlinkSlotName);
            _so.Layers.Add(new LayerDefinitionSerializable
            {
                name = SecondaryLayerName,
                priority = 1,
            });
            _so.Expressions.Add(CreateExpression(
                new OverlaySlotBindingSerializable { slot = BlinkSlotName },
                id: "smile",
                name: "Smile",
                layer: EmotionLayerName));
            _so.Expressions.Add(CreateExpression(
                new OverlaySlotBindingSerializable { slot = BlinkSlotName },
                id: "wink",
                name: "Wink",
                layer: SecondaryLayerName));

            var root = BuildInspectorRoot();
            var expressionLibrary = root.Q<VisualElement>(FacialCharacterProfileSOInspector.ExpressionLibraryFoldoutName);

            Assert.That(expressionLibrary, Is.Not.Null, "表情ライブラリタブに Expression リストセクションが見つかりません。");

            var nameFields = new List<TextField>();
            var layerDropdowns = new List<DropdownField>();
            expressionLibrary.Query<TextField>(FacialCharacterProfileSOInspector.ExpressionRowNameFieldName)
                .ForEach(nameFields.Add);
            expressionLibrary.Query<DropdownField>(FacialCharacterProfileSOInspector.ExpressionRowLayerDropdownName)
                .ForEach(layerDropdowns.Add);

            Assert.That(nameFields, Has.Count.EqualTo(2), "表情ライブラリタブは全レイヤーの Expression をフラットに表示する必要があります。");
            Assert.That(layerDropdowns, Has.Count.EqualTo(2), "各 Expression row には Layer DropdownField が必要です。");
            Assert.That(nameFields[0].value, Is.EqualTo("Smile"));
            Assert.That(layerDropdowns[0].value, Is.EqualTo(EmotionLayerName));
            Assert.That(nameFields[1].value, Is.EqualTo("Wink"));
            Assert.That(layerDropdowns[1].value, Is.EqualTo(SecondaryLayerName));
        }

        [Test]
        public void CreateInspectorGUI_OverlaysStateDropdownInitialValue_MatchesSerializableGetState()
        {
            _so = CreateProfileWithSlots(BlinkSlotName);
            var binding = new OverlaySlotBindingSerializable
            {
                slot = BlinkSlotName,
                suppress = true,
            };
            _so.Expressions.Add(CreateExpression(binding));

            var root = BuildInspectorRoot();
            AssertSixTabAndBuilderContracts(root);

            var dropdown = root.Q<DropdownField>(FacialCharacterProfileSOInspector.ExpressionOverlayStateDropdownName);
            Assert.That(dropdown, Is.Not.Null, "Expression row の Overlays 3 状態 dropdown が見つかりません。");
            Assert.That(dropdown.choices, Is.EqualTo(new[] { "Default", "Suppress", "Override" }));
            Assert.That(dropdown.value, Is.EqualTo(ToStateChoice(binding.GetState())));
        }

        [Test]
        public void CreateInspectorGUI_ExpressionRowLayerDropdown_UsesLayerChoicesAndUpdatesExpressionLayer()
        {
            _so = CreateProfileWithSlots(BlinkSlotName);
            _so.Layers.Add(new LayerDefinitionSerializable
            {
                name = SecondaryLayerName,
                priority = 1,
            });
            _so.Expressions.Add(CreateExpression(new OverlaySlotBindingSerializable { slot = BlinkSlotName }));

            var root = BuildInspectorRoot();
            var dropdown = root.Q<DropdownField>(FacialCharacterProfileSOInspector.ExpressionRowLayerDropdownName);

            Assert.That(dropdown, Is.Not.Null, "Expression row の Layer DropdownField が見つかりません。");
            Assert.That(dropdown.choices, Is.EqualTo(new[] { EmotionLayerName, SecondaryLayerName }));
            Assert.That(dropdown.value, Is.EqualTo(EmotionLayerName));

            dropdown.value = SecondaryLayerName;

            Assert.That(_so.Expressions[0].layer, Is.EqualTo(SecondaryLayerName));
        }

        [Test]
        public void CreateInspectorGUI_LayersTab_DoesNotRenderExpressionRows()
        {
            _so = CreateProfileWithSlots(BlinkSlotName);
            _so.Expressions.Add(CreateExpression(new OverlaySlotBindingSerializable { slot = BlinkSlotName }));

            var root = BuildInspectorRoot();
            var layersTab = root.Q<VisualElement>(FacialCharacterProfileSOInspector.TabLayersName);

            Assert.That(layersTab, Is.Not.Null, "Layers tab was not found.");
            Assert.That(
                layersTab.Q<TextField>(FacialCharacterProfileSOInspector.ExpressionRowNameFieldName),
                Is.Null,
                "Layers tab must not render Expression row name fields.");
            Assert.That(
                layersTab.Q<DropdownField>(FacialCharacterProfileSOInspector.ExpressionRowLayerDropdownName),
                Is.Null,
                "Layers tab must not render Expression row layer dropdowns.");
            Assert.That(
                layersTab.Q<DropdownField>(FacialCharacterProfileSOInspector.ExpressionOverlayStateDropdownName),
                Is.Null,
                "Layers tab must not render Expression overlay controls.");
        }

        [Test]
        public void Click_RoutingEditorOpenButton_OpensRoutingEditorWindow()
        {
            _so = CreateProfileWithSlots(BlinkSlotName);

            var root = BuildInspectorRoot();
            var saveStatusBar = root.Q<VisualElement>(FacialCharacterProfileSOInspector.SaveStatusBarName);
            Assert.That(saveStatusBar, Is.Not.Null, "Save status bar was not found.");

            var button = saveStatusBar.Q<Button>(FacialCharacterProfileSOInspector.RoutingEditorOpenButtonName);
            Assert.That(button, Is.Not.Null, "Routing Editor launch button was not found in the save status bar.");
            Assert.That(button.text, Is.EqualTo("ルーティングを編集"));

            ClickButton(button);

            RoutingEditorWindow[] windows = Resources.FindObjectsOfTypeAll<RoutingEditorWindow>();
            Assert.That(windows, Has.Length.EqualTo(1));
        }

        [Test]
        public void CreateInspectorGUI_OverrideSelected_ShowsAnimationClipFieldOnlyForOverride()
        {
            _so = CreateProfileWithSlots(BlinkSlotName);
            _so.Expressions.Add(CreateExpression(new OverlaySlotBindingSerializable { slot = BlinkSlotName }));

            var root = BuildInspectorRoot();
            var dropdown = root.Q<DropdownField>(FacialCharacterProfileSOInspector.ExpressionOverlayStateDropdownName);
            var clipField = root.Q<ObjectField>(FacialCharacterProfileSOInspector.ExpressionOverlayAnimationClipFieldName);
            Assert.That(dropdown, Is.Not.Null);
            Assert.That(clipField, Is.Not.Null);
            Assert.That(clipField.objectType, Is.EqualTo(typeof(AnimationClip)));

            dropdown.value = ToStateChoice(OverlaySlotBindingState.DefaultFallback);
            Assert.That(clipField.style.display.value, Is.EqualTo(DisplayStyle.None));

            dropdown.value = ToStateChoice(OverlaySlotBindingState.Override);
            Assert.That(clipField.style.display.value, Is.EqualTo(DisplayStyle.Flex));

            dropdown.value = ToStateChoice(OverlaySlotBindingState.Suppress);
            Assert.That(clipField.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void OverlayStateDropdownSelection_UpdatesSerializableFieldsAndClearsOverrideData()
        {
            _so = CreateProfileWithSlots(BlinkSlotName);
            var clip = CreateClip("OverlayStateRadioSelection_OverrideClip");
            var binding = new OverlaySlotBindingSerializable
            {
                slot = BlinkSlotName,
                animationClip = clip,
                cachedSnapshot = CreateCachedSnapshot(),
            };
            _so.Expressions.Add(CreateExpression(binding));

            var root = BuildInspectorRoot();
            var dropdown = root.Q<DropdownField>(FacialCharacterProfileSOInspector.ExpressionOverlayStateDropdownName);
            Assert.That(dropdown, Is.Not.Null);

            dropdown.value = ToStateChoice(OverlaySlotBindingState.Suppress);
            Assert.That(binding.suppress, Is.True);
            Assert.That(binding.animationClip, Is.Null);
            Assert.That(IsEmptySnapshot(binding.cachedSnapshot), Is.True);

            binding.animationClip = clip;
            binding.cachedSnapshot = CreateCachedSnapshot();

            dropdown.value = ToStateChoice(OverlaySlotBindingState.DefaultFallback);
            Assert.That(binding.suppress, Is.False);
            Assert.That(binding.animationClip, Is.Null);
            Assert.That(IsEmptySnapshot(binding.cachedSnapshot), Is.True);

            dropdown.value = ToStateChoice(OverlaySlotBindingState.Override);
            Assert.That(binding.suppress, Is.False);
        }

        [Test]
        public void OverlayStateDropdownSelection_SchedulesAutoSave()
        {
            // 回帰テスト: Overlay の Suppress/Override 切替は SerializedProperty を経由せず
            // managed モデルを直接書き換えるため、TrackSerializedObjectValue による自動保存監視が
            // 発火しない。ハンドラが明示的に自動保存を予約しないと、設定が profile.json / アセットへ
            // 確実に保存されない（「保存されない気がする」不具合）。panel 未接続のテスト環境では
            // 監視ポーリングが走らないため、_autoSavePending が true になるのは
            // ハンドラが ScheduleAutoSave() を呼んだ場合のみ。
            _so = CreateProfileWithSlots(BlinkSlotName);
            _so.Expressions.Add(CreateExpression(new OverlaySlotBindingSerializable { slot = BlinkSlotName }));

            var root = BuildInspectorRoot();
            var dropdown = root.Q<DropdownField>(FacialCharacterProfileSOInspector.ExpressionOverlayStateDropdownName);
            Assert.That(dropdown, Is.Not.Null);
            Assert.That(GetAutoSavePending(_editor), Is.False, "前提: 初期状態では自動保存は予約されていません。");

            dropdown.value = ToStateChoice(OverlaySlotBindingState.Suppress);

            Assert.That(
                GetAutoSavePending(_editor),
                Is.True,
                "Overlay の Suppress 切替後に自動保存が予約されていません。"
                + "managed モデルの直接書き換えでも保存を予約する必要があります。");
        }

        [Test]
        public void ExpressionOverlayClipSelection_SchedulesAutoSave()
        {
            _so = CreateProfileWithSlots(BlinkSlotName);
            _so.Expressions.Add(CreateExpression(new OverlaySlotBindingSerializable { slot = BlinkSlotName }));

            var root = BuildInspectorRoot();
            var dropdown = root.Q<DropdownField>(FacialCharacterProfileSOInspector.ExpressionOverlayStateDropdownName);
            var clipField = root.Q<ObjectField>(FacialCharacterProfileSOInspector.ExpressionOverlayAnimationClipFieldName);
            Assert.That(dropdown, Is.Not.Null);
            Assert.That(clipField, Is.Not.Null);

            dropdown.value = ToStateChoice(OverlaySlotBindingState.Override);
            ResetAutoSavePending(_editor);

            clipField.value = CreateClip("ExpressionOverlayClipSelection_OverrideClip");

            Assert.That(
                GetAutoSavePending(_editor),
                Is.True,
                "Overlay の Override clip 割当後に自動保存が予約されていません。");
        }

        [Test]
        public void OverlayStateDropdownSelection_Suppress_SurvivesSerializedObjectRoundTrip()
        {
            // 回帰テスト（Suppress 設定が Play 突入で .asset 上 1→0 に戻る不具合）。
            // bound な ListView / ObjectField は SerializedObject の内部キャッシュを保持し、
            // Domain Reload 直前に ApplyModifiedProperties で書き戻す。Suppress 切替が
            // SerializedProperty 経由で確定されていないと、その書き戻しで suppress が
            // 旧値(false) に巻き戻る。ここでは radio で Suppress にしたあと、SerializedObject の
            // ラウンドトリップ（ApplyModifiedProperties → 新規 SerializedObject で再読込）を
            // 経ても suppress が保持されることを検証する。
            _so = CreateProfileWithSlots(BlinkSlotName);
            _so.Expressions.Add(CreateExpression(new OverlaySlotBindingSerializable { slot = BlinkSlotName }));

            var root = BuildInspectorRoot();
            var dropdown = root.Q<DropdownField>(FacialCharacterProfileSOInspector.ExpressionOverlayStateDropdownName);
            Assert.That(dropdown, Is.Not.Null);

            dropdown.value = ToStateChoice(OverlaySlotBindingState.Suppress);

            // managed モデル即時反映を確認。
            Assert.That(_so.Expressions[0].overlays[0].suppress, Is.True,
                "Suppress 切替直後の managed モデルで suppress=true である必要があります。");

            // bound UI が保持する SerializedObject の書き戻しを模す。
            // SerializedProperty 経由で確定していれば、Inspector の serializedObject を
            // ApplyModifiedProperties しても managed の suppress=true を上書きしない。
            var inspectorSerialized = GetSerializedObject(_editor);
            inspectorSerialized.ApplyModifiedProperties();
            Assert.That(_so.Expressions[0].overlays[0].suppress, Is.True,
                "Inspector serializedObject の ApplyModifiedProperties 後に suppress が巻き戻りました。");

            // .asset へ書かれる値（新規 SerializedObject 読み出し）でも保持されること。
            var fresh = new SerializedObject(_so);
            var suppressProp = fresh
                .FindProperty("_expressions")
                .GetArrayElementAtIndex(0)
                .FindPropertyRelative("overlays")
                .GetArrayElementAtIndex(0)
                .FindPropertyRelative("suppress");
            Assert.That(suppressProp, Is.Not.Null);
            Assert.That(suppressProp.boolValue, Is.True,
                "永続化対象の SerializedObject で suppress=true が保持されていません。");
        }

        [Test]
        public void OverlayStateDropdownSelection_DefaultFallbackToSuppress_AddsBindingAndSurvivesRoundTrip()
        {
            // DefaultFallback（overlays に該当 slot の binding が無い）から Suppress へ切替える際は、
            // overlays 配列へ新規要素を Add する。配列構造変更が SerializedObject 経由で確定されていないと
            // 新要素ごと巻き戻る。新規 Add ケースでもラウンドトリップで suppress が保持されることを検証する。
            _so = CreateProfileWithSlots(BlinkSlotName, WinkSlotName);
            // overlays は blink のみ宣言。wink slot の binding は未作成（DefaultFallback）。
            _so.Expressions.Add(CreateExpression(new OverlaySlotBindingSerializable { slot = BlinkSlotName }));

            var root = BuildInspectorRoot();
            var dropdowns = new List<DropdownField>();
            root.Query<DropdownField>(FacialCharacterProfileSOInspector.ExpressionOverlayStateDropdownName)
                .ForEach(dropdowns.Add);
            // blink / wink の 2 行が描画される（wink は CollectOverlaySlotsForExpression が宣言 slot を補完）。
            Assert.That(dropdowns, Has.Count.GreaterThanOrEqualTo(2),
                "宣言済み slot ごとに overlay 行と状態 dropdown が描画される必要があります。");

            int initialOverlayCount = _so.Expressions[0].overlays.Count;

            // 2 行目（wink, DefaultFallback）を Suppress に切替える → 新規 binding を Add。
            dropdowns[1].value = ToStateChoice(OverlaySlotBindingState.Suppress);

            Assert.That(_so.Expressions[0].overlays.Count, Is.GreaterThan(initialOverlayCount),
                "DefaultFallback→Suppress で overlays に新規 binding が Add される必要があります。");

            var added = _so.Expressions[0].overlays.Find(b => b != null && b.slot == WinkSlotName);
            Assert.That(added, Is.Not.Null, "wink slot の binding が Add されていません。");
            Assert.That(added.suppress, Is.True, "Add された binding の suppress が true ではありません。");

            // ラウンドトリップで新規要素・suppress が保持されること。
            var inspectorSerialized = GetSerializedObject(_editor);
            inspectorSerialized.ApplyModifiedProperties();

            var fresh = new SerializedObject(_so);
            var overlaysProp = fresh
                .FindProperty("_expressions")
                .GetArrayElementAtIndex(0)
                .FindPropertyRelative("overlays");
            int foundIndex = -1;
            for (int i = 0; i < overlaysProp.arraySize; i++)
            {
                var slotProp = overlaysProp.GetArrayElementAtIndex(i).FindPropertyRelative("slot");
                if (slotProp != null && slotProp.stringValue == WinkSlotName)
                {
                    foundIndex = i;
                    break;
                }
            }
            Assert.That(foundIndex, Is.GreaterThanOrEqualTo(0),
                "ラウンドトリップ後の SerializedObject に wink binding が存在しません。");
            var freshSuppress = overlaysProp.GetArrayElementAtIndex(foundIndex).FindPropertyRelative("suppress");
            Assert.That(freshSuppress.boolValue, Is.True,
                "ラウンドトリップ後に Add された binding の suppress が巻き戻りました。");
        }

        [Test]
        public void CreateInspectorGUI_UndeclaredSlotReference_RendersWarningHelpBoxOnOverlayRow()
        {
            _so = CreateProfileWithSlots();
            _so.Expressions.Add(CreateExpression(new OverlaySlotBindingSerializable { slot = BlinkSlotName }));

            var root = BuildInspectorRoot();
            var help = root.Q<HelpBox>(FacialCharacterProfileSOInspector.ExpressionOverlayUndeclaredSlotHelpName);

            Assert.That(help, Is.Not.Null, "未宣言 slot を参照する overlay row の警告 HelpBox が見つかりません。");
            Assert.That(help.messageType, Is.EqualTo(HelpBoxMessageType.Warning));
            StringAssert.Contains(BlinkSlotName, help.text);
        }

        [Test]
        public void SlotsPropertyChanged_RebuildsDefaultOverlayDropdownChoices()
        {
            _so = CreateProfileWithSlots(BlinkSlotName);
            _so.DefaultOverlays.Add(new OverlaySlotBindingSerializable { slot = BlinkSlotName });

            var root = BuildInspectorRoot();
            var dropdown = root.Q<DropdownField>(FacialCharacterProfileSOInspector.DefaultOverlaySlotDropdownName);
            Assert.That(dropdown, Is.Not.Null, "Default Overlays の slot DropdownField が見つかりません。");
            Assert.That(dropdown.choices, Is.EqualTo(new[] { BlinkSlotName }));

            SetSlots(_so, BlinkSlotName, WinkSlotName);
            root = RebuildInspectorRoot();
            dropdown = root.Q<DropdownField>(FacialCharacterProfileSOInspector.DefaultOverlaySlotDropdownName);

            Assert.That(dropdown, Is.Not.Null);
            Assert.That(dropdown.choices, Is.EqualTo(new[] { BlinkSlotName, WinkSlotName }));
        }

        [Test]
        public void CreateInspectorGUI_DefaultOverlayRow_RendersSlotAndAlwaysVisibleClipFieldWithoutStateDropdown()
        {
            _so = CreateProfileWithSlots(BlinkSlotName);
            var binding = new OverlaySlotBindingSerializable
            {
                slot = BlinkSlotName,
                suppress = true,
                cachedSnapshot = CreateCachedSnapshot(),
            };
            _so.DefaultOverlays.Add(binding);

            var root = BuildInspectorRoot();
            var foldout = root.Q<VisualElement>(FacialCharacterProfileSOInspector.DefaultOverlaysFoldoutName);
            var dropdown = foldout.Q<DropdownField>(FacialCharacterProfileSOInspector.DefaultOverlaySlotDropdownName);
            var stateDropdown = foldout.Q<DropdownField>(FacialCharacterProfileSOInspector.ExpressionOverlayStateDropdownName);
            var clipField = foldout.Q<ObjectField>(FacialCharacterProfileSOInspector.DefaultOverlayAnimationClipFieldName);

            Assert.That(dropdown, Is.Not.Null, "Default Overlays row の slot DropdownField が見つかりません。");
            Assert.That(stateDropdown, Is.Null, "Default Overlays row に 3 状態 dropdown を表示してはいけません。");
            Assert.That(clipField, Is.Not.Null, "Default Overlays row の AnimationClip フィールドが見つかりません。");
            Assert.That(clipField.objectType, Is.EqualTo(typeof(AnimationClip)));
            Assert.That(clipField.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(binding.suppress, Is.False);
            Assert.That(IsEmptySnapshot(binding.cachedSnapshot), Is.True);
        }

        [Test]
        public void DefaultOverlayAnimationClipFieldSelection_NormalizesSuppressAndClearsSnapshotWhenUnset()
        {
            _so = CreateProfileWithSlots(BlinkSlotName);
            var initialClip = CreateClip("DefaultOverlayAnimationClipFieldSelection_InitialClip");
            var binding = new OverlaySlotBindingSerializable
            {
                slot = BlinkSlotName,
                suppress = true,
                animationClip = initialClip,
                cachedSnapshot = CreateCachedSnapshot(),
            };
            _so.DefaultOverlays.Add(binding);

            var root = BuildInspectorRoot();
            var clipField = root.Q<ObjectField>(FacialCharacterProfileSOInspector.DefaultOverlayAnimationClipFieldName);
            Assert.That(clipField, Is.Not.Null);
            Assert.That(clipField.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(binding.suppress, Is.False);
            Assert.That(binding.animationClip, Is.SameAs(initialClip));

            var replacementClip = CreateClip("DefaultOverlayAnimationClipFieldSelection_ReplacementClip");
            clipField.value = replacementClip;
            Assert.That(binding.suppress, Is.False);
            Assert.That(binding.animationClip, Is.SameAs(replacementClip));
            Assert.That(clipField.style.display.value, Is.EqualTo(DisplayStyle.Flex));

            binding.cachedSnapshot = CreateCachedSnapshot();
            clipField.value = null;
            Assert.That(binding.suppress, Is.False);
            Assert.That(binding.animationClip, Is.Null);
            Assert.That(IsEmptySnapshot(binding.cachedSnapshot), Is.True);
            Assert.That(clipField.style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        [Test]
        public void CreateInspectorGUI_DefaultOverlayUndeclaredSlot_RendersWarningHelpBoxOnRow()
        {
            _so = CreateProfileWithSlots();
            _so.DefaultOverlays.Add(new OverlaySlotBindingSerializable { slot = BlinkSlotName });

            var root = BuildInspectorRoot();
            var help = root.Q<HelpBox>(FacialCharacterProfileSOInspector.DefaultOverlayUndeclaredSlotHelpName);

            Assert.That(help, Is.Not.Null, "未宣言 slot を参照する Default Overlay row の警告 HelpBox が見つかりません。");
            Assert.That(help.messageType, Is.EqualTo(HelpBoxMessageType.Warning));
            StringAssert.Contains(BlinkSlotName, help.text);
        }

        [Test]
        public void DefaultOverlayClipSelection_SurvivesSerializedObjectRoundTrip()
        {
            // 回帰テスト（Default Overlays の clip が Play 突入で .asset 上から外れる不具合）。
            // clip 割当が SerializedProperty 経由で確定されていれば、Inspector serializedObject の
            // ApplyModifiedProperties（Play 突入直前の最終確定相当）や新規 SerializedObject 読み出しでも
            // animationClip が保持される。
            _so = CreateProfileWithSlots(BlinkSlotName);
            _so.DefaultOverlays.Add(new OverlaySlotBindingSerializable { slot = BlinkSlotName });

            var root = BuildInspectorRoot();
            var clipField = root.Q<ObjectField>(FacialCharacterProfileSOInspector.DefaultOverlayAnimationClipFieldName);
            Assert.That(clipField, Is.Not.Null);

            var clip = CreateClip("DefaultOverlayClipSelection_RoundTripClip");
            clipField.value = clip;

            Assert.That(_so.DefaultOverlays[0].animationClip, Is.SameAs(clip),
                "clip 割当直後の managed モデルで animationClip が保持される必要があります。");

            var inspectorSerialized = GetSerializedObject(_editor);
            inspectorSerialized.ApplyModifiedProperties();
            Assert.That(_so.DefaultOverlays[0].animationClip, Is.SameAs(clip),
                "Inspector serializedObject の ApplyModifiedProperties 後に animationClip が巻き戻りました。");

            var fresh = new SerializedObject(_so);
            var clipProp = fresh
                .FindProperty("_defaultOverlays")
                .GetArrayElementAtIndex(0)
                .FindPropertyRelative("animationClip");
            Assert.That(clipProp, Is.Not.Null);
            Assert.That(clipProp.objectReferenceValue, Is.SameAs(clip),
                "永続化対象の SerializedObject で animationClip が保持されていません。");
        }

        [Test]
        public void ExitingEditMode_FlushesPendingOverlayEdits()
        {
            // 回帰テスト（アサイン直後に Play すると Default Overlays の clip が外れる不具合）。
            // panel attach 時の overlay 編集は schedule.Execute で次ティックへ遅延される。遅延ティックが
            // 来る前に Play 突入するとドメインリロードで panel が破棄され編集が失われるため、
            // ExitingEditMode で保留中の編集を確定する必要がある。
            _so = CreateProfileWithSlots(BlinkSlotName);
            _so.DefaultOverlays.Add(new OverlaySlotBindingSerializable { slot = BlinkSlotName });
            BuildInspectorRoot();

            int executedCount = 0;
            AddPendingOverlayEdit(_editor, () => executedCount++);
            Assert.That(GetPendingOverlayEditCount(_editor), Is.EqualTo(1),
                "前提: 保留中の overlay 編集が 1 件積まれている必要があります。");

            InvokePlayModeFlush(_editor, PlayModeStateChange.ExitingEditMode);

            Assert.That(executedCount, Is.EqualTo(1),
                "ExitingEditMode で保留中の overlay 編集がフラッシュ（確定）されていません。");
            Assert.That(GetPendingOverlayEditCount(_editor), Is.EqualTo(0),
                "フラッシュ後に保留リストが空になっていません。");
        }

        [Test]
        public void DestroyEditor_AutoSavePending_FlushesAutoSaveOnDisable()
        {
            // 回帰テスト（編集直後に別オブジェクトを選択すると保存が失われる不具合）。
            // ScheduleAutoSave は EditorApplication.delayCall へ保存を予約するが、発火前に
            // Inspector (Editor) が破棄されると delayCall 側の FlushAutoSave は target == null で
            // 何もせず、.asset / profile.json が未保存のまま残る。破棄（OnDisable）時点で
            // 保留中の自動保存を同期確定する必要がある。
            _so = CreateProfileWithSlots(BlinkSlotName);
            _so.Expressions.Add(CreateExpression(new OverlaySlotBindingSerializable { slot = BlinkSlotName }));

            var root = BuildInspectorRoot();
            var dropdown = root.Q<DropdownField>(FacialCharacterProfileSOInspector.ExpressionOverlayStateDropdownName);
            Assert.That(dropdown, Is.Not.Null);

            dropdown.value = ToStateChoice(OverlaySlotBindingState.Suppress);
            Assert.That(GetAutoSavePending(_editor), Is.True,
                "前提: Suppress 切替で自動保存が予約されている必要があります。");

            Object.DestroyImmediate(_editor);
            _editor = null;

            string jsonPath = FacialCharacterProfileSO.GetStreamingAssetsProfilePath(_so.name);
            Assert.That(File.Exists(jsonPath), Is.True,
                "Inspector 破棄時に保留中の自動保存が確定されていません（profile.json 未出力）。"
                + "破棄後の delayCall では target が null となり保存できないため、"
                + "OnDisable で FlushAutoSave を実行する必要があります。");
        }

        [Test]
        public void NonExitingEditModeChange_DoesNotFlushPendingOverlayEdits()
        {
            // ExitingEditMode 以外（例: EnteredPlayMode）では保留編集をフラッシュしてはならない。
            _so = CreateProfileWithSlots(BlinkSlotName);
            _so.DefaultOverlays.Add(new OverlaySlotBindingSerializable { slot = BlinkSlotName });
            BuildInspectorRoot();

            int executedCount = 0;
            AddPendingOverlayEdit(_editor, () => executedCount++);

            InvokePlayModeFlush(_editor, PlayModeStateChange.EnteredPlayMode);

            Assert.That(executedCount, Is.EqualTo(0),
                "ExitingEditMode 以外で overlay 編集がフラッシュされてはいけません。");
            Assert.That(GetPendingOverlayEditCount(_editor), Is.EqualTo(1),
                "ExitingEditMode 以外で保留リストが変化してはいけません。");
        }

        [Test]
        public void BuildSaveStatusBar_DoesNotRenderManualExportButton()
        {
            // 書き出しはパラメータ変更時（ScheduleAutoSave）と Play 突入時（AutoExporter）に
            // 自動実行されるため、手動の「今すぐ書き出し」ボタンは表示しない。
            _so = CreateProfileWithSlots(BlinkSlotName);

            var root = BuildInspectorRoot();
            var saveStatusBar = root.Q<VisualElement>(FacialCharacterProfileSOInspector.SaveStatusBarName);
            Assert.That(saveStatusBar, Is.Not.Null, "Save status bar was not found.");

            var buttons = new List<Button>();
            saveStatusBar.Query<Button>().ForEach(buttons.Add);
            Assert.That(buttons.FindAll(b => b.text == "今すぐ書き出し"), Is.Empty,
                "手動の「今すぐ書き出し」ボタンを表示してはいけません。");
        }

        [Test]
        public void TabSelection_PersistsAcrossInspectorRebuild()
        {
            // domain reload / asset 再読み込みで Inspector が再構築されても、
            // 直前に選択していたタブを SessionState から復元する。
            _so = CreateProfileWithSlots(BlinkSlotName);

            var root = BuildInspectorRoot();
            var tabView = root.Q<TabView>(FacialCharacterProfileSOInspector.TabViewName);
            var layersTab = root.Q<Tab>(FacialCharacterProfileSOInspector.TabLayersName);
            Assert.That(tabView, Is.Not.Null);
            Assert.That(layersTab, Is.Not.Null);

            tabView.activeTab = layersTab;

            root = RebuildInspectorRoot();
            tabView = root.Q<TabView>(FacialCharacterProfileSOInspector.TabViewName);

            Assert.That(tabView.activeTab, Is.Not.Null);
            Assert.That(tabView.activeTab.name, Is.EqualTo(FacialCharacterProfileSOInspector.TabLayersName),
                "Inspector 再構築後も直前に選択していたタブが復元される必要があります。");
        }

        [Test]
        public void SectionFoldoutCollapse_PersistsAcrossInspectorRebuild()
        {
            // Foldout の折りたたみ状態も SessionState から復元する。
            _so = CreateProfileWithSlots(BlinkSlotName);

            var root = BuildInspectorRoot();
            var foldout = root.Q<Foldout>(FacialCharacterProfileSOInspector.DefaultOverlaysFoldoutName);
            Assert.That(foldout, Is.Not.Null);
            Assert.That(foldout.value, Is.True, "前提: Default Overlays セクションは既定で展開されています。");

            foldout.value = false;

            root = RebuildInspectorRoot();
            foldout = root.Q<Foldout>(FacialCharacterProfileSOInspector.DefaultOverlaysFoldoutName);

            Assert.That(foldout.value, Is.False,
                "Inspector 再構築後も直前の Foldout 折りたたみ状態が復元される必要があります。");
        }

        private VisualElement BuildInspectorRoot()
        {
            _editor = UnityEditor.Editor.CreateEditor(_so, typeof(FacialCharacterProfileSOInspector));
            Assert.That(_editor, Is.Not.Null);
            return _editor.CreateInspectorGUI();
        }

        private VisualElement RebuildInspectorRoot()
        {
            if (_editor != null)
            {
                Object.DestroyImmediate(_editor);
                _editor = null;
            }

            return BuildInspectorRoot();
        }

        private static void AssertSixTabAndBuilderContracts(VisualElement root)
        {
            Assert.That(root.Q<VisualElement>(FacialCharacterProfileSOInspector.TabExpressionLibraryName), Is.Not.Null);
            Assert.That(root.Q<VisualElement>(FacialCharacterProfileSOInspector.TabLayersName), Is.Not.Null);
            Assert.That(root.Q<VisualElement>(FacialCharacterProfileSOInspector.TabBaseExpressionName), Is.Not.Null);
            Assert.That(root.Q<VisualElement>(FacialCharacterProfileSOInspector.TabGazeName), Is.Not.Null);
            Assert.That(root.Q<VisualElement>(FacialCharacterProfileSOInspector.TabAdapterBindingsName), Is.Not.Null);
            Assert.That(root.Q<VisualElement>(FacialCharacterProfileSOInspector.TabDebugName), Is.Not.Null);

            AssertPrivateBuilderExists("BuildSlotsDeclarationSection", typeof(VisualElement));
            AssertPrivateBuilderExists("BuildDefaultOverlaysSection", typeof(VisualElement));
            AssertPrivateBuilderExists("BuildExpressionLibrarySection", typeof(VisualElement));
            AssertPrivateBuilderExists("BuildOverlaysSectionForExpression", typeof(SerializedProperty), typeof(int));

            Assert.That(
                root.Q<VisualElement>(FacialCharacterProfileSOInspector.SlotsDeclarationFoldoutName),
                Is.Not.Null,
                "表情ライブラリタブに Slots 宣言セクションが必要です。");
            Assert.That(
                root.Q<VisualElement>(FacialCharacterProfileSOInspector.DefaultOverlaysFoldoutName),
                Is.Not.Null,
                "表情ライブラリタブに Default Overlays セクションが必要です。");
            Assert.That(
                root.Q<VisualElement>(FacialCharacterProfileSOInspector.ExpressionLibraryFoldoutName),
                Is.Not.Null,
                "表情ライブラリタブに Expression リストセクションが必要です。");
            Assert.That(
                root.Q<VisualElement>(FacialCharacterProfileSOInspector.ExpressionOverlaysSectionName),
                Is.Not.Null,
                "Expression row に Overlays セクションが必要です。");
        }

        private static void AssertPrivateBuilderExists(string methodName, params System.Type[] parameterTypes)
        {
            var method = typeof(FacialCharacterProfileSOInspector).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: parameterTypes,
                modifiers: null);

            Assert.That(method, Is.Not.Null, $"{methodName} が FacialCharacterProfileSOInspector に未実装です。");
        }

        private static void ClickButton(Button button)
        {
            Assert.That(button, Is.Not.Null);
            var invoke = button.clickable.GetType().GetMethod(
                "Invoke",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(EventBase) },
                modifiers: null);
            Assert.That(invoke, Is.Not.Null);
            invoke.Invoke(button.clickable, new object[] { null });
        }

        private static FacialCharacterProfileSO CreateProfileWithSlots(params string[] slots)
        {
            var so = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            so.name = "OverlaysTabUITestProfile";
            so.Layers.Add(new LayerDefinitionSerializable
            {
                name = EmotionLayerName,
                priority = 0,
            });
            SetSlots(so, slots);
            return so;
        }

        private static ExpressionSerializable CreateExpression(
            OverlaySlotBindingSerializable binding,
            string id = "smile",
            string name = "Smile",
            string layer = EmotionLayerName)
        {
            return new ExpressionSerializable
            {
                id = id,
                name = name,
                layer = layer,
                isGaze = false,
                overlays = new List<OverlaySlotBindingSerializable> { binding },
            };
        }

        private AnimationClip CreateClip(string name)
        {
            var clip = new AnimationClip { name = name };
            _clips.Add(clip);
            return clip;
        }

        private static OverlaySnapshotDto CreateCachedSnapshot()
        {
            return new OverlaySnapshotDto
            {
                blendShapes = new List<BlendShapeSnapshotDto>
                {
                    new BlendShapeSnapshotDto
                    {
                        rendererPath = "Body",
                        name = "Blink_L",
                        value = 1f,
                    },
                },
                bones = new List<BoneSnapshotDto>(),
                rendererPaths = new List<string> { "Body" },
            };
        }

        private static bool IsEmptySnapshot(OverlaySnapshotDto snapshot)
        {
            if (snapshot == null) return true;
            bool hasBlendShapes = snapshot.blendShapes != null && snapshot.blendShapes.Count > 0;
            bool hasBones = snapshot.bones != null && snapshot.bones.Count > 0;
            bool hasRendererPaths = snapshot.rendererPaths != null && snapshot.rendererPaths.Count > 0;
            return !hasBlendShapes && !hasBones && !hasRendererPaths;
        }

        private static string ToStateChoice(OverlaySlotBindingState state)
        {
            switch (state)
            {
                case OverlaySlotBindingState.DefaultFallback:
                    return "Default";
                case OverlaySlotBindingState.Suppress:
                    return "Suppress";
                case OverlaySlotBindingState.Override:
                    return "Override";
                default:
                    Assert.Fail($"未知の OverlaySlotBindingState です: {state}");
                    return null;
            }
        }

        private const BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;

        private static SerializedObject GetSerializedObject(UnityEditor.Editor editor)
        {
            Assert.That(editor, Is.Not.Null);
            return editor.serializedObject;
        }

        private static bool GetAutoSavePending(UnityEditor.Editor editor)
        {
            var field = typeof(FacialCharacterProfileSOInspector)
                .GetField("_autoSavePending", InstanceNonPublic);
            Assert.That(field, Is.Not.Null, "_autoSavePending フィールドが見つかりません。");
            return (bool)field.GetValue(editor);
        }

        private static void ResetAutoSavePending(UnityEditor.Editor editor)
        {
            var field = typeof(FacialCharacterProfileSOInspector)
                .GetField("_autoSavePending", InstanceNonPublic);
            field?.SetValue(editor, false);
        }

        private static void CancelPendingAutoSave(UnityEditor.Editor editor)
        {
            if (editor == null) return;

            var flush = typeof(FacialCharacterProfileSOInspector)
                .GetMethod("FlushAutoSave", InstanceNonPublic);
            if (flush != null)
            {
                // delayCall に登録された FlushAutoSave と同一の (target, method) デリゲートを作り、
                // -= で取り除く。method group での += と等価なデリゲートは互いに等しいため除去できる。
                var del = (EditorApplication.CallbackFunction)Delegate.CreateDelegate(
                    typeof(EditorApplication.CallbackFunction), editor, flush);
                EditorApplication.delayCall -= del;
            }

            ResetAutoSavePending(editor);
        }

        private static void AddPendingOverlayEdit(UnityEditor.Editor editor, Action edit)
        {
            var field = typeof(FacialCharacterProfileSOInspector)
                .GetField("_pendingOverlayEdits", InstanceNonPublic);
            Assert.That(field, Is.Not.Null, "_pendingOverlayEdits フィールドが見つかりません。");
            var list = (List<Action>)field.GetValue(editor);
            Assert.That(list, Is.Not.Null);
            list.Add(edit);
        }

        private static int GetPendingOverlayEditCount(UnityEditor.Editor editor)
        {
            var field = typeof(FacialCharacterProfileSOInspector)
                .GetField("_pendingOverlayEdits", InstanceNonPublic);
            Assert.That(field, Is.Not.Null, "_pendingOverlayEdits フィールドが見つかりません。");
            var list = (List<Action>)field.GetValue(editor);
            return list?.Count ?? 0;
        }

        private static void InvokePlayModeFlush(UnityEditor.Editor editor, PlayModeStateChange change)
        {
            var method = typeof(FacialCharacterProfileSOInspector)
                .GetMethod("OnPlayModeStateChangedFlushOverlayEdits", InstanceNonPublic);
            Assert.That(method, Is.Not.Null, "OnPlayModeStateChangedFlushOverlayEdits メソッドが見つかりません。");
            method.Invoke(editor, new object[] { change });
        }

        private static void SetSlots(FacialCharacterProfileSO so, params string[] slots)
        {
            var serialized = new SerializedObject(so);
            serialized.Update();
            var slotsProperty = serialized.FindProperty("_slots");
            Assert.That(slotsProperty, Is.Not.Null, "_slots SerializedProperty が見つかりません。");
            slotsProperty.ClearArray();

            for (int i = 0; i < slots.Length; i++)
            {
                slotsProperty.InsertArrayElementAtIndex(i);
                slotsProperty.GetArrayElementAtIndex(i).stringValue = slots[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
