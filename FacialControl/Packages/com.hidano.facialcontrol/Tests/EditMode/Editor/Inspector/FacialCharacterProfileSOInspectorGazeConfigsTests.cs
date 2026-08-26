using System.Collections.Generic;
using System.Reflection;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Inspector;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Inspector
{
    [TestFixture]
    public class FacialCharacterProfileSOInspectorGazeConfigsTests
    {
        private sealed class TestCharacterSO : FacialCharacterProfileSO
        {
            public List<GazeBindingConfig> WritableGazeConfigs => _gazeConfigs;
        }

        private TestCharacterSO _so;
        private UnityEditor.Editor _editor;
        private readonly List<GameObject> _createdGameObjects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();

            if (_editor != null)
            {
                Object.DestroyImmediate(_editor);
                _editor = null;
            }

            if (_so != null)
            {
                Object.DestroyImmediate(_so);
                _so = null;
            }

            for (int i = 0; i < _createdGameObjects.Count; i++)
            {
                if (_createdGameObjects[i] != null)
                {
                    Object.DestroyImmediate(_createdGameObjects[i]);
                }
            }
            _createdGameObjects.Clear();
        }

        [Test]
        public void GazeConfigsAddDropdown_ListsOnlyAnalogExpressionsWithoutExistingConfig()
        {
            _so = CreateProfile();
            _so.Expressions.Add(CreateExpression("analog-one", "Analog One", true));
            _so.Expressions.Add(CreateExpression("analog-two", "Analog Two", true));
            _so.Expressions.Add(CreateExpression("digital-one", "Digital One", false));
            _so.WritableGazeConfigs.Add(new GazeBindingConfig { expressionId = "analog-two" });

            var root = BuildInspectorRoot();

            var dropdown = root.Q<DropdownField>(FacialCharacterProfileSOInspector.GazeConfigAddDropdownName);
            Assert.That(dropdown, Is.Not.Null);
            Assert.That(dropdown.choices, Is.EqualTo(new[] { "Analog One [analog-one]" }));
        }

        [Test]
        public void GazeConfigsAddDropdown_SelectionAppendsConfigForExpressionId()
        {
            _so = CreateProfile();
            _so.Expressions.Add(CreateExpression("analog-one", "Analog One", true));

            var root = BuildInspectorRoot();
            var dropdown = root.Q<DropdownField>(FacialCharacterProfileSOInspector.GazeConfigAddDropdownName);

            Assert.That(_so.GazeConfigs, Is.Empty);
            Assert.That(dropdown.choices, Does.Contain("Analog One [analog-one]"));
            InvokeAddGazeConfig("analog-one");

            Assert.That(_so.GazeConfigs, Has.Count.EqualTo(1));
            Assert.That(_so.GazeConfigs[0].expressionId, Is.EqualTo("analog-one"));
        }

        [Test]
        public void GazeConfigsAddDropdown_WhenNoCandidatesRemain_IsDisabledWithLabel()
        {
            _so = CreateProfile();
            _so.Expressions.Add(CreateExpression("analog-one", "Analog One", true));
            _so.WritableGazeConfigs.Add(new GazeBindingConfig { expressionId = "analog-one" });

            var root = BuildInspectorRoot();

            var dropdown = root.Q<DropdownField>(FacialCharacterProfileSOInspector.GazeConfigAddDropdownName);
            Assert.That(dropdown, Is.Not.Null);
            Assert.That(dropdown.enabledSelf, Is.False);
            Assert.That(dropdown.value, Is.EqualTo(FacialCharacterProfileSOInspector.GazeConfigNoCandidatesLabel));
        }

        [Test]
        public void GazeConfigsRow_RendersRequiredSingleRowControls()
        {
            _so = CreateProfile();
            _so.Expressions.Add(CreateExpression("analog-one", "Analog One", true));
            _so.WritableGazeConfigs.Add(new GazeBindingConfig { expressionId = "analog-one" });

            var root = BuildInspectorRoot();
            var row = root.Q<VisualElement>(FacialCharacterProfileSOInspector.GazeConfigRowName);

            Assert.That(row, Is.Not.Null);
            Assert.That(row.Q<Label>(FacialCharacterProfileSOInspector.GazeConfigExpressionNameLabelName)?.text, Is.EqualTo("Expression 名: Analog One"));
            Assert.That(row.Q<TextField>(FacialCharacterProfileSOInspector.GazeConfigLeftBonePathFieldName), Is.Not.Null);
            Assert.That(row.Q<TextField>(FacialCharacterProfileSOInspector.GazeConfigRightBonePathFieldName), Is.Not.Null);
            Assert.That(row.Q<FloatField>(FacialCharacterProfileSOInspector.GazeConfigLookUpAngleFieldName), Is.Not.Null);
            Assert.That(row.Q<FloatField>(FacialCharacterProfileSOInspector.GazeConfigLookDownAngleFieldName), Is.Not.Null);
            Assert.That(row.Q<FloatField>(FacialCharacterProfileSOInspector.GazeConfigOuterYawAngleFieldName), Is.Not.Null);
            Assert.That(row.Q<FloatField>(FacialCharacterProfileSOInspector.GazeConfigInnerYawAngleFieldName), Is.Not.Null);
            Assert.That(row.Q<ObjectField>(FacialCharacterProfileSOInspector.GazeConfigLookLeftClipFieldName), Is.Not.Null);
            Assert.That(row.Q<ObjectField>(FacialCharacterProfileSOInspector.GazeConfigLookRightClipFieldName), Is.Not.Null);
            Assert.That(row.Q<ObjectField>(FacialCharacterProfileSOInspector.GazeConfigLookUpClipFieldName), Is.Not.Null);
            Assert.That(row.Q<ObjectField>(FacialCharacterProfileSOInspector.GazeConfigLookDownClipFieldName), Is.Not.Null);
            Assert.That(row.Q<Button>(FacialCharacterProfileSOInspector.GazeConfigAutoAssignButtonName), Is.Not.Null);
            Assert.That(row.Q<Button>(FacialCharacterProfileSOInspector.GazeConfigRemoveButtonName), Is.Not.Null);
        }

        [Test]
        public void RebuildGazeConfigsUI_Contains_BulkRegenerateButton()
        {
            _so = CreateProfile();
            _so.Expressions.Add(CreateExpression("analog-one", "Analog One", true));

            var root = BuildInspectorRoot();

            var button = root.Q<Button>(FacialCharacterProfileSOInspector.GazeConfigBulkRegenerateButtonName);
            Assert.That(button, Is.Not.Null);
            Assert.That(button.text, Is.EqualTo("GazeConfig を一括再生成"));
        }

        [Test]
        public void GazeConfigResolveButtons_WhenReferenceModelMissing_AreDisabled()
        {
            _so = CreateProfile();
            _so.Expressions.Add(CreateExpression("analog-one", "Analog One", true));
            _so.WritableGazeConfigs.Add(new GazeBindingConfig { expressionId = "analog-one" });

            var root = BuildInspectorRoot();

            var rowButton = root.Q<Button>(FacialCharacterProfileSOInspector.GazeConfigAutoAssignButtonName);
            Assert.That(rowButton, Is.Not.Null);
            Assert.That(rowButton.enabledSelf, Is.False);
        }

        [Test]
        public void GazeConfigResolveButtons_WhenReferenceModelAssigned_AreEnabled()
        {
            _so = CreateProfile();
            _so.ReferenceModel = CreateReferenceModel();
            _so.Expressions.Add(CreateExpression("analog-one", "Analog One", true));
            _so.WritableGazeConfigs.Add(new GazeBindingConfig { expressionId = "analog-one" });

            var root = BuildInspectorRoot();

            var rowButton = root.Q<Button>(FacialCharacterProfileSOInspector.GazeConfigAutoAssignButtonName);
            Assert.That(rowButton, Is.Not.Null);
            Assert.That(rowButton.enabledSelf, Is.True);
        }

        [Test]
        public void BulkRegenerateGazeConfigs_AddsMissingConfigsForGazeExpressions()
        {
            _so = CreateProfile();
            _so.Expressions.Add(CreateExpression("analog-existing", "Analog Existing", true));
            _so.Expressions.Add(CreateExpression("analog-missing", "Analog Missing", true));
            _so.Expressions.Add(CreateExpression("digital-one", "Digital One", false));
            _so.WritableGazeConfigs.Add(new GazeBindingConfig { expressionId = "analog-existing" });
            BuildInspectorRoot();

            InvokeBulkRegenerateGazeConfigs();

            Assert.That(_so.GazeConfigs, Has.Count.EqualTo(2));
            Assert.That(FindGazeConfig("analog-existing"), Is.Not.Null);
            Assert.That(FindGazeConfig("analog-missing"), Is.Not.Null);
            Assert.That(FindGazeConfig("digital-one"), Is.Null);
        }

        [Test]
        public void BulkRegenerateGazeConfigs_DoesNotOverwriteExistingConfigs()
        {
            _so = CreateProfile();
            _so.Expressions.Add(CreateExpression("analog-existing", "Analog Existing", true));
            _so.Expressions.Add(CreateExpression("analog-missing", "Analog Missing", true));
            _so.WritableGazeConfigs.Add(new GazeBindingConfig
            {
                expressionId = "analog-existing",
                leftEyeBonePath = "ManualLeft",
                rightEyeBonePath = "ManualRight",
                lookUpAngle = 42f,
                lookDownAngle = 43f,
                outerYawAngle = 44f,
                innerYawAngle = 45f,
            });
            BuildInspectorRoot();

            InvokeBulkRegenerateGazeConfigs();

            Assert.That(_so.GazeConfigs, Has.Count.EqualTo(2));
            var existing = FindGazeConfig("analog-existing");
            Assert.That(existing, Is.Not.Null);
            Assert.That(existing.leftEyeBonePath, Is.EqualTo("ManualLeft"));
            Assert.That(existing.rightEyeBonePath, Is.EqualTo("ManualRight"));
            Assert.That(existing.lookUpAngle, Is.EqualTo(42f));
            Assert.That(existing.lookDownAngle, Is.EqualTo(43f));
            Assert.That(existing.outerYawAngle, Is.EqualTo(44f));
            Assert.That(existing.innerYawAngle, Is.EqualTo(45f));
            Assert.That(FindGazeConfig("analog-missing"), Is.Not.Null);
        }

        [Test]
        public void ResolveGazeConfigFromReferenceModel_OverwritesBonesButKeepsManualAngleRanges()
        {
            // ボーン名・初期角度は参照モデルで上書きするが、
            // 数値入力済みの可動範囲はユーザーの調整値なので初期値へ戻さない。
            _so = CreateProfile();
            _so.ReferenceModel = CreateReferenceModel();
            _so.Expressions.Add(CreateExpression("analog-one", "Analog One", true));
            _so.WritableGazeConfigs.Add(new GazeBindingConfig
            {
                expressionId = "analog-one",
                leftEyeBonePath = "ManualLeft",
                rightEyeBonePath = "ManualRight",
                lookUpAngle = 42f,
                lookDownAngle = 43f,
                outerYawAngle = 44f,
                innerYawAngle = 45f,
            });
            BuildInspectorRoot();

            InvokeResolveGazeConfig(0);

            Assert.That(_so.GazeConfigs[0].leftEyeBonePath, Is.EqualTo("LeftEye"));
            Assert.That(_so.GazeConfigs[0].rightEyeBonePath, Is.EqualTo("RightEye"));
            Assert.That(_so.GazeConfigs[0].lookUpAngle, Is.EqualTo(42f));
            Assert.That(_so.GazeConfigs[0].lookDownAngle, Is.EqualTo(43f));
            Assert.That(_so.GazeConfigs[0].outerYawAngle, Is.EqualTo(44f));
            Assert.That(_so.GazeConfigs[0].innerYawAngle, Is.EqualTo(45f));
        }

        [Test]
        public void AutoAssignGazeConfigForExpression_ExistingConfig_KeepsManualAngleRanges()
        {
            _so = CreateProfile();
            _so.ReferenceModel = CreateReferenceModel();
            _so.Expressions.Add(CreateExpression("analog-one", "Analog One", true));
            _so.WritableGazeConfigs.Add(new GazeBindingConfig
            {
                expressionId = "analog-one",
                leftEyeBonePath = "ManualLeft",
                rightEyeBonePath = "ManualRight",
                lookUpAngle = 42f,
                lookDownAngle = 43f,
                outerYawAngle = 44f,
                innerYawAngle = 45f,
            });
            BuildInspectorRoot();

            InvokeAutoAssignGazeConfigForExpression(0);

            var config = FindGazeConfig("analog-one");
            Assert.That(config, Is.Not.Null);
            Assert.That(config.leftEyeBonePath, Is.EqualTo("LeftEye"));
            Assert.That(config.rightEyeBonePath, Is.EqualTo("RightEye"));
            Assert.That(config.lookUpAngle, Is.EqualTo(42f));
            Assert.That(config.lookDownAngle, Is.EqualTo(43f));
            Assert.That(config.outerYawAngle, Is.EqualTo(44f));
            Assert.That(config.innerYawAngle, Is.EqualTo(45f));
        }

        [Test]
        public void ReferenceModelChanged_DoesNotAutoFillBonePathsAndMarksGazeTabWithAttention()
        {
            // 参照モデル変更時に GazeConfig を自動入力しない。
            // 代わりに目線タブにアスタリスクを付けてユーザーに「ボーン設定を確認しろ」と促す。
            // 前回の値が残ると未設定との区別がつかなくなる事象への対応。
            _so = CreateProfile();
            _so.Expressions.Add(CreateExpression("analog-one", "Analog One", true));
            _so.WritableGazeConfigs.Add(new GazeBindingConfig
            {
                expressionId = "analog-one",
                lookUpAngle = 42f,
            });
            BuildInspectorRoot();

            AssignReferenceModel(CreateReferenceModel());
            InvokeReferenceModelChanged();

            // GazeConfig は自動入力されない (空欄のまま)。
            Assert.That(_so.GazeConfigs[0].leftEyeBonePath, Is.Null.Or.Empty);
            Assert.That(_so.GazeConfigs[0].rightEyeBonePath, Is.Null.Or.Empty);
            Assert.That(_so.GazeConfigs[0].lookUpAngle, Is.EqualTo(42f));
        }

        [Test]
        public void RemoveExpression_RemovesMatchingGazeConfigAndUndoRestoresBothInOneStep()
        {
            Undo.ClearAll();
            _so = CreateProfile();
            _so.Expressions.Add(CreateExpression("analog-one", "Analog One", true));
            _so.WritableGazeConfigs.Add(new GazeBindingConfig { expressionId = "analog-one" });
            BuildInspectorRoot();

            InvokeRemoveExpression(0);

            Assert.That(_so.Expressions, Is.Empty);
            Assert.That(_so.GazeConfigs, Is.Empty);

            Undo.PerformUndo();

            Assert.That(_so.Expressions, Has.Count.EqualTo(1));
            Assert.That(_so.Expressions[0].id, Is.EqualTo("analog-one"));
            Assert.That(_so.GazeConfigs, Has.Count.EqualTo(1));
            Assert.That(_so.GazeConfigs[0].expressionId, Is.EqualTo("analog-one"));
        }

        [Test]
        public void ChangeExpressionIsGaze_GazeToNonGaze_RemovesMatchingGazeConfigAndUndoRestoresBothInOneStep()
        {
            Undo.ClearAll();
            _so = CreateProfile();
            _so.Expressions.Add(CreateExpression("analog-one", "Analog One", true));
            _so.WritableGazeConfigs.Add(new GazeBindingConfig { expressionId = "analog-one" });
            BuildInspectorRoot();

            InvokeChangeExpressionIsGaze(0, false);

            Assert.That(_so.Expressions[0].isGaze, Is.EqualTo(false));
            Assert.That(_so.GazeConfigs, Is.Empty);

            Undo.PerformUndo();

            Assert.That(_so.Expressions[0].isGaze, Is.EqualTo(true));
            Assert.That(_so.GazeConfigs, Has.Count.EqualTo(1));
            Assert.That(_so.GazeConfigs[0].expressionId, Is.EqualTo("analog-one"));
        }

        [Test]
        public void ChangeExpressionIsGaze_NonGazeToNonGaze_DoesNotRemoveGazeConfig()
        {
            _so = CreateProfile();
            _so.Expressions.Add(CreateExpression("digital-one", "Digital One", false));
            _so.WritableGazeConfigs.Add(new GazeBindingConfig { expressionId = "digital-one" });
            BuildInspectorRoot();

            InvokeChangeExpressionIsGaze(0, false);

            Assert.That(_so.Expressions[0].isGaze, Is.EqualTo(false));
            Assert.That(_so.GazeConfigs, Has.Count.EqualTo(1));
            Assert.That(_so.GazeConfigs[0].expressionId, Is.EqualTo("digital-one"));
        }

        [Test]
        public void RemoveGazeConfigAt_ExplicitUserRemoval_DoesNotRemoveExpressionAndUndoRestoresConfig()
        {
            Undo.ClearAll();
            _so = CreateProfile();
            _so.Expressions.Add(CreateExpression("analog-one", "Analog One", true));
            _so.WritableGazeConfigs.Add(new GazeBindingConfig { expressionId = "analog-one" });
            BuildInspectorRoot();

            InvokeRemoveGazeConfigAt(0);

            Assert.That(_so.Expressions, Has.Count.EqualTo(1));
            Assert.That(_so.Expressions[0].id, Is.EqualTo("analog-one"));
            Assert.That(_so.GazeConfigs, Is.Empty);

            Undo.PerformUndo();

            Assert.That(_so.Expressions, Has.Count.EqualTo(1));
            Assert.That(_so.GazeConfigs, Has.Count.EqualTo(1));
            Assert.That(_so.GazeConfigs[0].expressionId, Is.EqualTo("analog-one"));
        }

        [Test]
        public void RemoveGazeConfigAt_ExplicitUserRemoval_UndoRestoresDeletedConfigContents()
        {
            Undo.ClearAll();
            _so = CreateProfile();
            _so.Expressions.Add(CreateExpression("analog-one", "Analog One", true));
            _so.WritableGazeConfigs.Add(new GazeBindingConfig
            {
                expressionId = "analog-one",
                leftEyeBonePath = "Armature/Head/LeftEye",
                rightEyeBonePath = "Armature/Head/RightEye",
                lookUpAngle = 21f,
                lookDownAngle = 11f,
                outerYawAngle = 31f,
                innerYawAngle = 13f,
            });
            BuildInspectorRoot();

            InvokeRemoveGazeConfigAt(0);

            Assert.That(_so.GazeConfigs, Is.Empty);

            Undo.PerformUndo();

            Assert.That(_so.GazeConfigs, Has.Count.EqualTo(1));
            Assert.That(_so.GazeConfigs[0].expressionId, Is.EqualTo("analog-one"));
            Assert.That(_so.GazeConfigs[0].leftEyeBonePath, Is.EqualTo("Armature/Head/LeftEye"));
            Assert.That(_so.GazeConfigs[0].rightEyeBonePath, Is.EqualTo("Armature/Head/RightEye"));
            Assert.That(_so.GazeConfigs[0].lookUpAngle, Is.EqualTo(21f));
            Assert.That(_so.GazeConfigs[0].lookDownAngle, Is.EqualTo(11f));
            Assert.That(_so.GazeConfigs[0].outerYawAngle, Is.EqualTo(31f));
            Assert.That(_so.GazeConfigs[0].innerYawAngle, Is.EqualTo(13f));
        }

        [Test]
        public void ExpressionRows_DoNotRenderIdTextOrGazeConfigControls()
        {
            _so = CreateProfile();
            _so.Layers.Add(CreateLayer("base"));
            _so.Expressions.Add(CreateExpression("analog-one", "Analog One", true));

            var root = BuildInspectorRoot();
            var layersSection = root.Q<VisualElement>(FacialCharacterProfileSOInspector.LayersFoldoutName);
            Assert.That(layersSection, Is.Not.Null);

            var layerLabels = new List<string>();
            layersSection.Query<Label>().ForEach(label => layerLabels.Add(label.text));
            Assert.That(layerLabels, Does.Not.Contain("analog-one"));
            Assert.That(layersSection.Q<VisualElement>(FacialCharacterProfileSOInspector.GazeConfigRowName), Is.Null);
            Assert.That(layersSection.Q<TextField>(FacialCharacterProfileSOInspector.GazeConfigLeftBonePathFieldName), Is.Null);
            Assert.That(layersSection.Q<TextField>(FacialCharacterProfileSOInspector.GazeConfigRightBonePathFieldName), Is.Null);
            Assert.That(layersSection.Q<Button>(FacialCharacterProfileSOInspector.GazeConfigAutoAssignButtonName), Is.Null);
            Assert.That(layersSection.Q<ObjectField>(FacialCharacterProfileSOInspector.GazeConfigLookLeftClipFieldName), Is.Null);
            Assert.That(layersSection.Q<ObjectField>(FacialCharacterProfileSOInspector.GazeConfigLookRightClipFieldName), Is.Null);
            Assert.That(layersSection.Q<ObjectField>(FacialCharacterProfileSOInspector.GazeConfigLookUpClipFieldName), Is.Null);
            Assert.That(layersSection.Q<ObjectField>(FacialCharacterProfileSOInspector.GazeConfigLookDownClipFieldName), Is.Null);
        }

        [Test]
        public void InspectorRoot_QueryDoesNotFindLegacyDeadElementNames()
        {
            _so = CreateProfile();
            _so.Layers.Add(CreateLayer("base"));
            _so.Expressions.Add(CreateExpression("analog-one", "Analog One", true));

            var root = BuildInspectorRoot();
            // expression-row-gaze-auto-assign-button は 2026-05 のレイヤー/表情タブ改修で
            // 「目線設定が未作成 / 参照モデル変更時に行内から再解決する」用途のボタンとして
            // 再導入されているため legacy 一覧から除外している (UpdateRowValidation 経由で表示)。
            var legacyNames = new[]
            {
                "expression-row-id-label",
                "expression-row-gaze-left-bone-path",
                "expression-row-gaze-left-init-rot",
                "expression-row-gaze-right-bone-path",
                "expression-row-gaze-right-init-rot",
                "expression-row-gaze-look-left-clip",
                "expression-row-gaze-look-right-clip",
                "expression-row-gaze-look-up-clip",
                "expression-row-gaze-look-down-clip",
            };

            foreach (var legacyName in legacyNames)
            {
                Assert.That(root.Q<VisualElement>(legacyName), Is.Null, legacyName);
            }
        }

        [Test]
        public void DebugExpressionIdMapping_RendersNameExpressionIdKindAndLayerForEveryExpression()
        {
            _so = CreateProfile();
            _so.Layers.Add(CreateLayer("base"));
            _so.Layers.Add(CreateLayer("eye"));
            _so.Expressions.Add(CreateExpression("smile", "Smile", false, "base"));
            _so.Expressions.Add(CreateExpression("look", "Look", true, "eye"));

            var root = BuildInspectorRoot();

            Assert.That(
                root.Q<Label>(FacialCharacterProfileSOInspector.DebugExpressionIdMappingTitleName)?.text,
                Is.EqualTo("Expression ID マッピング"));
            Assert.That(CollectLabelTexts(root, FacialCharacterProfileSOInspector.DebugExpressionIdMappingNameCellName),
                Is.EqualTo(new[] { "Smile", "Look" }));
            Assert.That(CollectLabelTexts(root, FacialCharacterProfileSOInspector.DebugExpressionIdMappingExpressionIdCellName),
                Is.EqualTo(new[] { "smile", "look" }));
            Assert.That(CollectLabelTexts(root, FacialCharacterProfileSOInspector.DebugExpressionIdMappingKindCellName),
                Is.EqualTo(new[] { "通常", "目線" }));
            Assert.That(CollectLabelTexts(root, FacialCharacterProfileSOInspector.DebugExpressionIdMappingLayerCellName),
                Is.EqualTo(new[] { "base", "eye" }));
        }

        [Test]
        public void DebugExpressionIdMapping_RebuildReflectsExpressionEdits()
        {
            _so = CreateProfile();
            _so.Layers.Add(CreateLayer("base"));
            _so.Layers.Add(CreateLayer("eye"));
            _so.Expressions.Add(CreateExpression("smile", "Smile", false, "base"));

            var root = BuildInspectorRoot();

            _so.Expressions[0].name = "Smile Renamed";
            _so.Expressions[0].isGaze = true;
            _so.Expressions[0].layer = "eye";
            _so.Expressions.Add(CreateExpression("blink", "Blink", false, "base"));
            InvokeRebuildExpressionIdMapping();

            Assert.That(CollectLabelTexts(root, FacialCharacterProfileSOInspector.DebugExpressionIdMappingNameCellName),
                Is.EqualTo(new[] { "Smile Renamed", "Blink" }));
            Assert.That(CollectLabelTexts(root, FacialCharacterProfileSOInspector.DebugExpressionIdMappingKindCellName),
                Is.EqualTo(new[] { "目線", "通常" }));
            Assert.That(CollectLabelTexts(root, FacialCharacterProfileSOInspector.DebugExpressionIdMappingLayerCellName),
                Is.EqualTo(new[] { "eye", "base" }));

            _so.Expressions.RemoveAt(0);
            InvokeRebuildExpressionIdMapping();

            Assert.That(CollectLabelTexts(root, FacialCharacterProfileSOInspector.DebugExpressionIdMappingExpressionIdCellName),
                Is.EqualTo(new[] { "blink" }));
        }

        private VisualElement BuildInspectorRoot()
        {
            _editor = UnityEditor.Editor.CreateEditor(_so, typeof(FacialCharacterProfileSOInspector));
            Assert.That(_editor, Is.Not.Null);
            return _editor.CreateInspectorGUI();
        }

        private void InvokeAddGazeConfig(string expressionId)
        {
            var method = typeof(FacialCharacterProfileSOInspector).GetMethod(
                "AddGazeConfigFromCandidate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(_editor, new object[] { expressionId });
        }

        private void InvokeResolveGazeConfig(int index)
        {
            var method = typeof(FacialCharacterProfileSOInspector).GetMethod(
                "ResolveGazeConfigFromReferenceModel",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(_editor, new object[] { index });
        }

        private void InvokeAutoAssignGazeConfigForExpression(int exprIndex)
        {
            var method = typeof(FacialCharacterProfileSOInspector).GetMethod(
                "AutoAssignGazeConfigForExpression",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(_editor, new object[] { exprIndex });
        }

        private void InvokeBulkRegenerateGazeConfigs()
        {
            var method = typeof(FacialCharacterProfileSOInspector).GetMethod(
                "BulkRegenerateGazeConfigs",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(_editor, null);
        }

        private void InvokeRemoveExpression(int index)
        {
            var method = typeof(FacialCharacterProfileSOInspector).GetMethod(
                "RemoveExpression",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(_editor, new object[] { index });
        }

        private void InvokeChangeExpressionIsGaze(int index, bool isGaze)
        {
            var method = typeof(FacialCharacterProfileSOInspector).GetMethod(
                "ChangeExpressionIsGaze",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(_editor, new object[] { index, isGaze });
        }

        private void InvokeRemoveGazeConfigAt(int index)
        {
            var method = typeof(FacialCharacterProfileSOInspector).GetMethod(
                "RemoveGazeConfigAt",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(_editor, new object[] { index });
        }

        private void InvokeReferenceModelChanged()
        {
            var method = typeof(FacialCharacterProfileSOInspector).GetMethod(
                "OnReferenceModelPropertyChanged",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(_editor, null);
        }

        private void InvokeRebuildExpressionIdMapping()
        {
            _editor.serializedObject.Update();
            var method = typeof(FacialCharacterProfileSOInspector).GetMethod(
                "RebuildExpressionIdMapping",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(_editor, null);
        }

        private void AssignReferenceModel(GameObject referenceModel)
        {
            var so = _editor.serializedObject;
            so.Update();
            var prop = so.FindProperty("_referenceModel");
            Assert.That(prop, Is.Not.Null);
            prop.objectReferenceValue = referenceModel;
            so.ApplyModifiedProperties();
        }

        private GameObject CreateReferenceModel()
        {
            var root = new GameObject("ReferenceModel");
            _createdGameObjects.Add(root);

            var leftEye = new GameObject("LeftEye");
            leftEye.transform.SetParent(root.transform, false);
            leftEye.transform.localEulerAngles = new Vector3(1f, 2f, 3f);

            var rightEye = new GameObject("RightEye");
            rightEye.transform.SetParent(root.transform, false);
            rightEye.transform.localEulerAngles = new Vector3(4f, 5f, 6f);

            return root;
        }

        private static TestCharacterSO CreateProfile()
        {
            var so = ScriptableObject.CreateInstance<TestCharacterSO>();
            so.name = "GazeConfigInspectorTest";
            return so;
        }

        private static LayerDefinitionSerializable CreateLayer(string name)
        {
            return new LayerDefinitionSerializable
            {
                name = name,
                priority = 0,
                exclusionMode = ExclusionMode.LastWins,
            };
        }

        private static ExpressionSerializable CreateExpression(
            string id,
            string name,
            bool isGaze,
            string layer = "base")
        {
            return new ExpressionSerializable
            {
                id = id,
                name = name,
                layer = layer,
                isGaze = isGaze,
            };
        }

        private GazeBindingConfig FindGazeConfig(string expressionId)
        {
            for (int i = 0; i < _so.GazeConfigs.Count; i++)
            {
                var config = _so.GazeConfigs[i];
                if (config != null && config.expressionId == expressionId)
                {
                    return config;
                }
            }

            return null;
        }

        private static List<string> CollectLabelTexts(VisualElement root, string name)
        {
            var result = new List<string>();
            root.Query<Label>(name).ForEach(label => result.Add(label.text));
            return result;
        }
    }
}
