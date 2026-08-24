using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Editor.Inspector;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Inspector
{
    /// <summary>
    /// <see cref="FacialControllerEditor"/> の概要表示が
    /// 新モデル (Schema / Layer / Expression / Snapshot) に置き換わっており、
    /// 旧 BonePose 概念由来の文字列が一切残っていないことを検証する。
    /// </summary>
    [TestFixture]
    public class FacialControllerEditorTests
    {
        private GameObject _host;
        private FacialController _controller;
        private FacialCharacterProfileSO _so;
        private UnityEditor.Editor _editor;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("FacialControllerEditorTestHost");
            _host.AddComponent<Animator>();
            _controller = _host.AddComponent<FacialController>();
        }

        [TearDown]
        public void TearDown()
        {
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
            if (_host != null)
            {
                Object.DestroyImmediate(_host);
                _host = null;
            }
        }

        private VisualElement BuildInspectorRoot()
        {
            _editor = UnityEditor.Editor.CreateEditor(_controller, typeof(FacialControllerEditor));
            Assert.IsNotNull(_editor, "FacialControllerEditor が生成できませんでした。");

            var root = _editor.CreateInspectorGUI();
            Assert.IsNotNull(root, "CreateInspectorGUI() は VisualElement を返すべきです。");
            return root;
        }

        [Test]
        public void CreateInspectorGUI_DoesNotIncludeBonePoseTextAnywhere()
        {
            var root = BuildInspectorRoot();

            // 全 Label の text に "BonePose" / "ボーンポーズ" 等の旧概念文字列が含まれないこと
            root.Query<Label>().ForEach(label =>
            {
                StringAssert.DoesNotContain("BonePose", label.text,
                    $"Label.text に 'BonePose' が残っています: '{label.text}'");
                StringAssert.DoesNotContain("ボーンポーズ", label.text,
                    $"Label.text に 'ボーンポーズ' が残っています: '{label.text}'");
                StringAssert.DoesNotContain("BonePoses", label.text,
                    $"Label.text に 'BonePoses' が残っています: '{label.text}'");
            });

            // Foldout の text 部分にも残っていないこと
            root.Query<Foldout>().ForEach(foldout =>
            {
                StringAssert.DoesNotContain("BonePose", foldout.text,
                    $"Foldout.text に 'BonePose' が残っています: '{foldout.text}'");
                StringAssert.DoesNotContain("ボーンポーズ", foldout.text,
                    $"Foldout.text に 'ボーンポーズ' が残っています: '{foldout.text}'");
            });
        }

        [Test]
        public void CreateInspectorGUI_ProvidesNewSummaryLabels()
        {
            var root = BuildInspectorRoot();

            var schema = root.Q<Label>(name: FacialControllerEditor.SchemaVersionLabelName);
            var layer = root.Q<Label>(name: FacialControllerEditor.LayerCountLabelName);
            var expression = root.Q<Label>(name: FacialControllerEditor.ExpressionCountLabelName);
            var snapshot = root.Q<Label>(name: FacialControllerEditor.SnapshotCountLabelName);

            Assert.IsNotNull(schema, "Schema バージョン Label が見つかりません。");
            Assert.IsNotNull(layer, "Layer 数 Label が見つかりません。");
            Assert.IsNotNull(expression, "Expression 数 Label が見つかりません。");
            Assert.IsNotNull(snapshot, "Snapshot 数 Label が見つかりません。");
        }

        [Test]
        public void SummaryLabelFormatConstants_DoNotMentionBonePose()
        {
            // Refactor: 表示文字列を const 化。
            // const 文字列レベルでも旧概念が残っていないことを検証する。
            StringAssert.DoesNotContain("BonePose", FacialControllerEditor.SchemaVersionLabelFormat);
            StringAssert.DoesNotContain("BonePose", FacialControllerEditor.LayerCountLabelFormat);
            StringAssert.DoesNotContain("BonePose", FacialControllerEditor.ExpressionCountLabelFormat);
            StringAssert.DoesNotContain("BonePose", FacialControllerEditor.SnapshotCountLabelFormat);

            StringAssert.DoesNotContain("ボーンポーズ", FacialControllerEditor.SchemaVersionLabelFormat);
            StringAssert.DoesNotContain("ボーンポーズ", FacialControllerEditor.LayerCountLabelFormat);
            StringAssert.DoesNotContain("ボーンポーズ", FacialControllerEditor.ExpressionCountLabelFormat);
            StringAssert.DoesNotContain("ボーンポーズ", FacialControllerEditor.SnapshotCountLabelFormat);
        }

        // ================================================================
        // 重複 FacialController の警告
        // ================================================================

        [Test]
        public void CreateInspectorGUI_NoDuplicateController_DoesNotShowWarning()
        {
            var root = BuildInspectorRoot();

            var helpBox = root.Q<HelpBox>(name: FacialControllerEditor.DuplicateWarningHelpBoxName);

            Assert.IsNull(helpBox, "重複が無いのに警告 HelpBox が表示されています。");
        }

        [Test]
        public void CreateInspectorGUI_DuplicateControllerInChild_ShowsWarning()
        {
            var child = new GameObject("Model");
            child.transform.SetParent(_host.transform, false);
            child.AddComponent<Animator>();
            child.AddComponent<FacialController>();

            var root = BuildInspectorRoot();

            var helpBox = root.Q<HelpBox>(name: FacialControllerEditor.DuplicateWarningHelpBoxName);

            Assert.IsNotNull(helpBox, "子孫に FacialController があるのに警告 HelpBox がありません。");
            StringAssert.Contains("Model", helpBox.text, "警告に重複相手のパスが含まれていません。");
        }

        [Test]
        public void FindHierarchyDuplicate_ControllerInParent_ReturnsParentController()
        {
            var parent = new GameObject("Root");
            parent.AddComponent<Animator>();
            var parentController = parent.AddComponent<FacialController>();
            _host.transform.SetParent(parent.transform, false);

            try
            {
                var duplicate = FacialControllerEditor.FindHierarchyDuplicate(_controller);

                Assert.That(duplicate, Is.SameAs(parentController));
            }
            finally
            {
                // TearDown が _host を破棄できるよう、親から切り離してから親を破棄する。
                _host.transform.SetParent(null, false);
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void FindHierarchyDuplicate_SiblingController_ReturnsNull()
        {
            var parent = new GameObject("Root");
            _host.transform.SetParent(parent.transform, false);

            var sibling = new GameObject("OtherCharacter");
            sibling.transform.SetParent(parent.transform, false);
            sibling.AddComponent<Animator>();
            sibling.AddComponent<FacialController>();

            try
            {
                var duplicate = FacialControllerEditor.FindHierarchyDuplicate(_controller);

                Assert.IsNull(duplicate, "兄弟関係の FacialController は重複として扱わない。");
            }
            finally
            {
                _host.transform.SetParent(null, false);
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void FindHierarchyDuplicate_Null_ReturnsNull()
        {
            Assert.IsNull(FacialControllerEditor.FindHierarchyDuplicate(null));
        }
    }
}
