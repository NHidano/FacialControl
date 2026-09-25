using System;
using System.Collections.Generic;
using System.Reflection;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Editor.Common;
using Hidano.FacialControl.LipSync.Adapters.PhonemeEntries;
using Hidano.FacialControl.LipSync.Editor.Inspector;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.LipSync.Tests.EditMode.Editor
{
    public class PhonemeEntryListViewTests
    {
        private PhonemeEntryListViewTestAsset _asset;
        private SerializedObject _serializedObject;
        private SerializedProperty _entriesProperty;

        [SetUp]
        public void SetUp()
        {
            Undo.ClearAll();

            _asset = ScriptableObject.CreateInstance<PhonemeEntryListViewTestAsset>();
            _serializedObject = new SerializedObject(_asset);
            _entriesProperty =
                _serializedObject.FindProperty(nameof(PhonemeEntryListViewTestAsset.Entries));
        }

        [TearDown]
        public void TearDown()
        {
            _serializedObject.Dispose();
            UnityEngine.Object.DestroyImmediate(_asset);
            Undo.ClearAll();
        }

        [Test]
        public void Create_ArrayProperty_BuildsReorderableListWithStandardFooter()
        {
            var view = CreateView();
            var listView = view.Q<ListView>(PhonemeEntryListView.ListViewName);

            Assert.That(listView, Is.Not.Null);
            Assert.That(listView.reorderable, Is.True);
            Assert.That(listView.showAddRemoveFooter, Is.True);
        }

        [Test]
        public void Create_ListView_ShowsAlternatingRowBackgrounds()
        {
            var view = CreateView();
            var listView = view.Q<ListView>(PhonemeEntryListView.ListViewName);

            Assert.That(listView, Is.Not.Null);
            Assert.That(
                listView.showAlternatingRowBackgrounds,
                Is.EqualTo(AlternatingRowBackground.ContentOnly),
                "各音素エントリ行の境界を視認できるよう交互背景を表示する必要があります。");
        }

        [Test]
        public void Create_SavedCollapsedFoldoutState_IsRestored()
        {
            string key = ListViewFoldoutStatePersistence.GetSessionStateKey(_entriesProperty);
            try
            {
                SessionState.SetBool(key, false);

                var view = CreateView();
                var listView = view.Q<ListView>(PhonemeEntryListView.ListViewName);
                var foldout = listView.Q<Foldout>(className: BaseListView.foldoutHeaderUssClassName);

                Assert.That(foldout, Is.Not.Null);
                Assert.That(foldout.value, Is.False,
                    "音素エントリリストの折りたたみ状態が SessionState から復元される必要があります。");
            }
            finally
            {
                SessionState.EraseBool(key);
            }
        }

        [Test]
        public void AddEntry_BlendShapeAndAnimationClip_AppendsConcreteManagedReferences()
        {
            var view = CreateView();

            view.AddEntry(PhonemeEntryListView.EntryKind.BlendShape);
            view.AddEntry(PhonemeEntryListView.EntryKind.AnimationClip);

            _serializedObject.Update();
            Assert.That(_entriesProperty.arraySize, Is.EqualTo(2));
            Assert.That(
                _entriesProperty.GetArrayElementAtIndex(0).managedReferenceValue,
                Is.InstanceOf<BlendShapePhonemeEntry>());
            Assert.That(
                _entriesProperty.GetArrayElementAtIndex(1).managedReferenceValue,
                Is.InstanceOf<AnimationClipPhonemeEntry>());
        }

        [Test]
        public void AddEntry_Expression_InsertsExpressionPhonemeEntry()
        {
            var view = CreateView();

            view.AddEntry(PhonemeEntryListView.EntryKind.Expression);

            _serializedObject.Update();
            Assert.That(_entriesProperty.arraySize, Is.EqualTo(1));
            SerializedProperty entry = _entriesProperty.GetArrayElementAtIndex(0);
            Assert.That(entry.managedReferenceValue, Is.InstanceOf<ExpressionPhonemeEntry>());
            Assert.That(
                entry.managedReferenceFullTypename,
                Does.Contain(typeof(ExpressionPhonemeEntry).FullName));
        }

        [Test]
        public void SetEntryKind_FromBlendShapeToAnimationClip_PreservesCommonFields()
        {
            var view = CreateView();
            view.AddEntry(PhonemeEntryListView.EntryKind.BlendShape);

            _serializedObject.Update();
            SerializedProperty entry = _entriesProperty.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative(nameof(PhonemeEntryBase.PhonemeId)).stringValue = "A";
            entry.FindPropertyRelative(nameof(PhonemeEntryBase.MaxWeight)).floatValue = 80f;
            entry.FindPropertyRelative(nameof(BlendShapePhonemeEntry.BlendShapeName)).stringValue = "Mouth_A";
            _serializedObject.ApplyModifiedProperties();

            view.SetEntryKind(0, PhonemeEntryListView.EntryKind.AnimationClip);

            _serializedObject.Update();
            entry = _entriesProperty.GetArrayElementAtIndex(0);
            Assert.That(entry.managedReferenceValue, Is.InstanceOf<AnimationClipPhonemeEntry>());
            Assert.That(
                entry.FindPropertyRelative(nameof(PhonemeEntryBase.PhonemeId)).stringValue,
                Is.EqualTo("A"));
            Assert.That(
                entry.FindPropertyRelative(nameof(PhonemeEntryBase.MaxWeight)).floatValue,
                Is.EqualTo(80f).Within(1e-6f));
        }

        [TestCase(
            PhonemeEntryListView.EntryKind.BlendShape,
            PhonemeEntryListView.EntryKind.AnimationClip,
            typeof(AnimationClipPhonemeEntry))]
        [TestCase(
            PhonemeEntryListView.EntryKind.BlendShape,
            PhonemeEntryListView.EntryKind.Expression,
            typeof(ExpressionPhonemeEntry))]
        [TestCase(
            PhonemeEntryListView.EntryKind.AnimationClip,
            PhonemeEntryListView.EntryKind.BlendShape,
            typeof(BlendShapePhonemeEntry))]
        [TestCase(
            PhonemeEntryListView.EntryKind.AnimationClip,
            PhonemeEntryListView.EntryKind.Expression,
            typeof(ExpressionPhonemeEntry))]
        [TestCase(
            PhonemeEntryListView.EntryKind.Expression,
            PhonemeEntryListView.EntryKind.BlendShape,
            typeof(BlendShapePhonemeEntry))]
        [TestCase(
            PhonemeEntryListView.EntryKind.Expression,
            PhonemeEntryListView.EntryKind.AnimationClip,
            typeof(AnimationClipPhonemeEntry))]
        public void SetEntryKind_BetweenAllFormats_PreservesCommonFields(
            PhonemeEntryListView.EntryKind sourceKind,
            PhonemeEntryListView.EntryKind targetKind,
            Type expectedEntryType)
        {
            var view = CreateView();
            view.AddEntry(sourceKind);

            _serializedObject.Update();
            SerializedProperty entry = _entriesProperty.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative(nameof(PhonemeEntryBase.PhonemeId)).stringValue = "U";
            entry.FindPropertyRelative(nameof(PhonemeEntryBase.MaxWeight)).floatValue = 72.5f;
            _serializedObject.ApplyModifiedProperties();

            view.SetEntryKind(0, targetKind);

            _serializedObject.Update();
            entry = _entriesProperty.GetArrayElementAtIndex(0);
            Assert.That(entry.managedReferenceValue, Is.TypeOf(expectedEntryType));
            Assert.That(
                entry.FindPropertyRelative(nameof(PhonemeEntryBase.PhonemeId)).stringValue,
                Is.EqualTo("U"));
            Assert.That(
                entry.FindPropertyRelative(nameof(PhonemeEntryBase.MaxWeight)).floatValue,
                Is.EqualTo(72.5f).Within(1e-6f));
        }

        [Test]
        public void MoveEntry_ReordersSerializedArray()
        {
            var view = CreateView();
            view.AddEntry(PhonemeEntryListView.EntryKind.BlendShape);
            view.AddEntry(PhonemeEntryListView.EntryKind.AnimationClip);

            _serializedObject.Update();
            SetPhonemeId(0, "A");
            SetPhonemeId(1, "I");
            _serializedObject.ApplyModifiedProperties();

            view.MoveEntry(0, 1);

            _serializedObject.Update();
            Assert.That(GetPhonemeId(0), Is.EqualTo("I"));
            Assert.That(GetPhonemeId(1), Is.EqualTo("A"));
        }

        [Test]
        public void RemoveEntryAt_RemovesElement()
        {
            var view = CreateView();
            view.AddEntry(PhonemeEntryListView.EntryKind.BlendShape);
            view.AddEntry(PhonemeEntryListView.EntryKind.AnimationClip);

            view.RemoveEntryAt(0);

            _serializedObject.Update();
            Assert.That(_entriesProperty.arraySize, Is.EqualTo(1));
            Assert.That(
                _entriesProperty.GetArrayElementAtIndex(0).managedReferenceValue,
                Is.InstanceOf<AnimationClipPhonemeEntry>());
        }

        [Test]
        public void BlendShapeRow_EmptyBlendShapeName_ShowsWarningHelpBox()
        {
            _asset.Entries.Add(new BlendShapePhonemeEntry
            {
                PhonemeId = "A",
                BlendShapeName = string.Empty,
                MaxWeight = 100f,
            });
            _serializedObject.Update();
            var view = CreateView();

            VisualElement row = view.CreateBoundRowForIndex(0);
            var selector = row.Q<DropdownField>(PhonemeEntryListView.EntryTypeSelectorName);
            var warning = row.Q<HelpBox>(PhonemeEntryListView.BlendShapeWarningName);

            Assert.That(selector, Is.Not.Null);
            Assert.That(selector.value, Is.EqualTo(PhonemeEntryListView.BlendShapeLabel));
            Assert.That(warning, Is.Not.Null);
            Assert.That(warning.style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        [Test]
        public void AnimationClipRow_RendersClipFieldAndNoBlendShapeWarning()
        {
            _asset.Entries.Add(new AnimationClipPhonemeEntry
            {
                PhonemeId = "O",
                MaxWeight = 100f,
            });
            _serializedObject.Update();
            var view = CreateView();

            VisualElement row = view.CreateBoundRowForIndex(0);
            var selector = row.Q<DropdownField>(PhonemeEntryListView.EntryTypeSelectorName);

            Assert.That(selector, Is.Not.Null);
            Assert.That(selector.value, Is.EqualTo(PhonemeEntryListView.AnimationClipLabel));
            Assert.That(row.Q<ObjectField>(), Is.Not.Null);
            Assert.That(row.Q<HelpBox>(PhonemeEntryListView.BlendShapeWarningName), Is.Null);
        }

        [Test]
        public void ExpressionRow_WithProfileExpressions_RendersDropdownAndResolvesSelectionDisplayName()
        {
            var profileAsset = ScriptableObject.CreateInstance<PhonemeEntryListViewProfileAsset>();
            SerializedObject serializedProfile = null;
            try
            {
                profileAsset.Expressions.Add(new ExpressionSerializable { id = "expr-a", name = "A" });
                profileAsset.Expressions.Add(new ExpressionSerializable { id = "expr-i", name = "I" });
                profileAsset.Expressions.Add(new ExpressionSerializable { id = "expr-u", name = "U" });
                profileAsset.Entries.Add(new ExpressionPhonemeEntry
                {
                    PhonemeId = "A",
                    MaxWeight = 100f,
                });

                serializedProfile = new SerializedObject(profileAsset);
                SerializedProperty entries =
                    serializedProfile.FindProperty(nameof(PhonemeEntryListViewProfileAsset.Entries));
                var view = new PhonemeEntryListView(entries);

                VisualElement row = view.CreateBoundRowForIndex(0);
                var dropdown = row.Q<DropdownField>(PhonemeEntryListView.ExpressionDropdownName);

                Assert.That(dropdown, Is.Not.Null);
                Assert.That(dropdown.choices, Has.Count.EqualTo(4));
                Assert.That(dropdown.choices, Does.Contain(string.Empty));
                Assert.That(dropdown.choices, Does.Contain("A (expr-a)"));
                Assert.That(dropdown.choices, Does.Contain("I (expr-i)"));
                Assert.That(dropdown.choices, Does.Contain("U (expr-u)"));

                Assert.That(ResolveExpressionIdByDisplayNameForTest("I (expr-i)"), Is.EqualTo("expr-i"));
            }
            finally
            {
                serializedProfile?.Dispose();
                UnityEngine.Object.DestroyImmediate(profileAsset);
            }
        }

        [Test]
        public void ExpressionRow_WithoutProfileExpressions_RendersTextFieldFallback()
        {
            _asset.Entries.Add(new ExpressionPhonemeEntry
            {
                PhonemeId = "A",
                MaxWeight = 100f,
            });
            _serializedObject.Update();
            var view = CreateView();

            VisualElement row = null;
            Assert.DoesNotThrow(() => row = view.CreateBoundRowForIndex(0));

            Assert.That(row.Q<TextField>(PhonemeEntryListView.ExpressionTextFieldName), Is.Not.Null);
            Assert.That(row.Q<DropdownField>(PhonemeEntryListView.ExpressionDropdownName), Is.Null);
            Assert.That(row.Q<HelpBox>(PhonemeEntryListView.ExpressionWarningName), Is.Not.Null);
        }

        [Test]
        public void BindRow_ExpressionWithoutId_ShowsWarningHelpBox()
        {
            _asset.Entries.Add(new ExpressionPhonemeEntry
            {
                PhonemeId = "A",
                MaxWeight = 100f,
            });
            _serializedObject.Update();
            var view = CreateView();

            VisualElement row = view.CreateBoundRowForIndex(0);
            var warning = row.Q<HelpBox>(PhonemeEntryListView.ExpressionWarningName);

            Assert.That(warning, Is.Not.Null);
            Assert.That(warning.messageType, Is.EqualTo(HelpBoxMessageType.Warning));
            Assert.That(warning.text, Is.EqualTo("Expression 未割り当てです。リップシンクが動作しません"));
            Assert.That(warning.style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        [Test]
        public void BindRow_ExpressionWithId_HidesWarningHelpBox()
        {
            _asset.Entries.Add(new ExpressionPhonemeEntry
            {
                PhonemeId = "A",
                MaxWeight = 100f,
            });
            _serializedObject.Update();
            var view = CreateView();

            VisualElement rowBeforeAssignment = view.CreateBoundRowForIndex(0);
            var warningBeforeAssignment =
                rowBeforeAssignment.Q<HelpBox>(PhonemeEntryListView.ExpressionWarningName);
            Assert.That(warningBeforeAssignment, Is.Not.Null);
            Assert.That(warningBeforeAssignment.style.display.value, Is.EqualTo(DisplayStyle.Flex));

            _entriesProperty.GetArrayElementAtIndex(0)
                .FindPropertyRelative("_expressionId")
                .stringValue = "expr-a";
            _serializedObject.ApplyModifiedProperties();
            _serializedObject.Update();

            VisualElement rowAfterAssignment = view.CreateBoundRowForIndex(0);
            var warningAfterAssignment =
                rowAfterAssignment.Q<HelpBox>(PhonemeEntryListView.ExpressionWarningName);

            Assert.That(warningAfterAssignment, Is.Not.Null);
            Assert.That(warningAfterAssignment.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void UndoAfterSetEntryKind_DisplaysSerializedPropertyCurrentValue()
        {
            _asset.Entries.Add(new BlendShapePhonemeEntry
            {
                PhonemeId = "A",
                BlendShapeName = "Mouth_A",
                MaxWeight = 100f,
            });
            _serializedObject.Update();
            var view = CreateView();

            Undo.RecordObject(_asset, "Change Phoneme Entry Kind");
            view.SetEntryKind(0, PhonemeEntryListView.EntryKind.AnimationClip);
            Undo.FlushUndoRecordObjects();

            Assert.That(GetBoundEntryTypeSelectorValue(view), Is.EqualTo(PhonemeEntryListView.AnimationClipLabel));

            Undo.PerformUndo();
            InvokeUndoRedoPerformed(view);

            _serializedObject.Update();
            SerializedProperty entry = _entriesProperty.GetArrayElementAtIndex(0);
            Assert.That(entry.managedReferenceValue, Is.InstanceOf<BlendShapePhonemeEntry>());
            Assert.That(
                GetBoundEntryTypeSelectorValue(view),
                Is.EqualTo(PhonemeEntryListView.BlendShapeLabel));
        }

        [Test]
        public void UndoRedoPerformed_WhenFiredRepeatedly_RebuildsWithoutStaleDisplay()
        {
            _asset.Entries.Add(new BlendShapePhonemeEntry
            {
                PhonemeId = "I",
                BlendShapeName = "Mouth_I",
                MaxWeight = 90f,
            });
            _serializedObject.Update();
            var view = CreateView();

            Undo.RecordObject(_asset, "Change Phoneme Entry Kind To AnimationClip");
            view.SetEntryKind(0, PhonemeEntryListView.EntryKind.AnimationClip);
            Undo.FlushUndoRecordObjects();

            Undo.RecordObject(_asset, "Change Phoneme Entry Kind To BlendShape");
            view.SetEntryKind(0, PhonemeEntryListView.EntryKind.BlendShape);
            Undo.FlushUndoRecordObjects();

            Undo.PerformUndo();
            Assert.DoesNotThrow(() => InvokeUndoRedoPerformed(view));
            Assert.That(
                GetBoundEntryTypeSelectorValue(view),
                Is.EqualTo(PhonemeEntryListView.AnimationClipLabel));

            Undo.PerformUndo();
            Assert.DoesNotThrow(() =>
            {
                InvokeUndoRedoPerformed(view);
                InvokeUndoRedoPerformed(view);
            });
            Assert.That(
                GetBoundEntryTypeSelectorValue(view),
                Is.EqualTo(PhonemeEntryListView.BlendShapeLabel));
        }

        private PhonemeEntryListView CreateView()
        {
            _serializedObject.Update();
            _entriesProperty =
                _serializedObject.FindProperty(nameof(PhonemeEntryListViewTestAsset.Entries));
            return new PhonemeEntryListView(_entriesProperty);
        }

        private static void InvokeUndoRedoPerformed(PhonemeEntryListView view)
        {
            MethodInfo method = typeof(PhonemeEntryListView).GetMethod(
                "OnUndoRedoPerformed",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(view, null);
        }

        private static string GetBoundEntryTypeSelectorValue(PhonemeEntryListView view)
        {
            VisualElement row = view.CreateBoundRowForIndex(0);
            var selector = row.Q<DropdownField>(PhonemeEntryListView.EntryTypeSelectorName);
            Assert.That(selector, Is.Not.Null);
            return selector.value;
        }

        private void SetPhonemeId(int index, string phonemeId)
        {
            _entriesProperty.GetArrayElementAtIndex(index)
                .FindPropertyRelative(nameof(PhonemeEntryBase.PhonemeId))
                .stringValue = phonemeId;
        }

        private string GetPhonemeId(int index)
        {
            return _entriesProperty.GetArrayElementAtIndex(index)
                .FindPropertyRelative(nameof(PhonemeEntryBase.PhonemeId))
                .stringValue;
        }

        private static string ResolveExpressionIdByDisplayNameForTest(string displayName)
        {
            Type choiceType = typeof(PhonemeEntryListView).GetNestedType(
                "ExpressionChoice",
                BindingFlags.NonPublic);
            Assert.That(choiceType, Is.Not.Null);

            Type listType = typeof(List<>).MakeGenericType(choiceType);
            object choices = Activator.CreateInstance(listType);
            ConstructorInfo constructor = choiceType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(string) },
                null);
            Assert.That(constructor, Is.Not.Null);

            listType.GetMethod("Add").Invoke(
                choices,
                new[] { constructor.Invoke(new object[] { "expr-i", "I (expr-i)" }) });

            MethodInfo method = typeof(PhonemeEntryListView).GetMethod(
                "FindExpressionIdByDisplayName",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (string)method.Invoke(null, new[] { choices, displayName });
        }

        private sealed class PhonemeEntryListViewTestAsset : ScriptableObject
        {
            [SerializeReference]
            public List<PhonemeEntryBase> Entries = new List<PhonemeEntryBase>();
        }

        private sealed class PhonemeEntryListViewProfileAsset : FacialCharacterProfileSO
        {
            [SerializeReference]
            public List<PhonemeEntryBase> Entries = new List<PhonemeEntryBase>();
        }
    }
}
