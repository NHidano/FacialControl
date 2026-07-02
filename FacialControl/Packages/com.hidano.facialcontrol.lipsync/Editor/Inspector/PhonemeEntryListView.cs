using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Editor.Common;
using Hidano.FacialControl.LipSync.Adapters.PhonemeEntries;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.LipSync.Editor.Inspector
{
    public sealed class PhonemeEntryListView : VisualElement
    {
        public enum EntryKind
        {
            BlendShape,
            AnimationClip,
            Expression,
        }

        public const string RootClassName = "facial-control-lipsync-phoneme-entry-list-view";
        public const string ListViewName = "ulipsync-phoneme-entry-list";
        public const string RowClassName = "ulipsync-phoneme-entry-row";
        public const string EntryTypeSelectorName = "ulipsync-phoneme-entry-type-selector";
        public const string BlendShapeWarningName = "ulipsync-phoneme-entry-blend-shape-warning";
        public const string AnimationClipWarningName = "ulipsync-phoneme-entry-animation-clip-warning";
        public const string ExpressionDropdownName = "ulipsync-phoneme-entry-expression-dropdown";
        public const string ExpressionTextFieldName = "ulipsync-phoneme-entry-expression-text-field";
        public const string ExpressionWarningName = "ulipsync-phoneme-entry-expression-warning";
        public const string BlendShapeLabel = "BlendShape 形式";
        public const string AnimationClipLabel = "AnimationClip 形式";

        public const string ExpressionLabel = "Expression 形式";

        private const string PhonemeEntriesFieldName = "_phonemeEntries";
        private const string PhonemeIdFieldName = nameof(PhonemeEntryBase.PhonemeId);
        private const string MaxWeightFieldName = nameof(PhonemeEntryBase.MaxWeight);
        private const string BlendShapeNameFieldName = nameof(BlendShapePhonemeEntry.BlendShapeName);
        private const string ClipFieldName = nameof(AnimationClipPhonemeEntry.Clip);
        private const string ExpressionIdFieldName = "_expressionId";
        private const string ExpressionUnassignedMessage =
            "\u0045\u0078\u0070\u0072\u0065\u0073\u0073\u0069\u006f\u006e \u672a\u5272\u308a\u5f53\u3066\u3067\u3059\u3002\u30ea\u30c3\u30d7\u30b7\u30f3\u30af\u304c\u52d5\u4f5c\u3057\u307e\u305b\u3093";

        private static readonly List<string> EntryTypeChoices = new List<string>
        {
            BlendShapeLabel,
            AnimationClipLabel,
            ExpressionLabel,
        };

        private static readonly List<string> PhonemeIdChoices = new List<string>
        {
            "A",
            "I",
            "U",
            "E",
            "O",
        };

        private const string PhonemeIdTooltip =
            "uLipSync が出力する音素 ID。AIUEO の 5 候補から選択します。";

        private const string MaxWeightTooltip =
            "音素の最大ウェイト (0〜100)。BlendShape 値の最大値に対する % スケールとして適用され、"
            + "100 で frame 0 のフル値、50 で半量となります。";

        private readonly SerializedProperty _listProperty;
        private readonly List<int> _indexProxy = new List<int>();
        private readonly ListView _listView;
        private bool _undoRedoSubscribed;

        public PhonemeEntryListView(SerializedProperty listProperty)
        {
            _listProperty = listProperty ?? throw new ArgumentNullException(nameof(listProperty));
            if (!_listProperty.isArray)
            {
                throw new ArgumentException(
                    "listProperty must be an array property bound to _phonemeEntries.",
                    nameof(listProperty));
            }

            AddToClassList(RootClassName);

            RebuildIndexProxy(_indexProxy, GetListProperty());

            // NOTE: itemsSource は SetViewController の "後" に設定する。
            // BaseVerticalCollectionView.SetViewController は旧 controller を Dispose して
            // 新 controller を SetView する実装で、ListView.itemsSource ゲッターは
            // viewController.itemsSource を直接返すため、初期化子で itemsSource を渡すと
            // 差し替え後に null 化されてしまい、Add ボタンで EnsureItemSourceCanBeResized が
            // "source is not defined" 例外を投げる。InputSystemAdapterBindingDrawer と同型の修正。
            _listView = new ListView
            {
                name = ListViewName,
                // 行内はエントリ形式（BlendShape/AnimationClip/Expression）でフィールド数が変わるため、
                // 固定高だと短い形式で下に空白が残る。DynamicHeight で実高に追従させる。
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                showAddRemoveFooter = true,
                showBorder = true,
                showFoldoutHeader = true,
                headerTitle = "音素エントリ",
                reorderable = true,
                reorderMode = ListViewReorderMode.Animated,
                selectionType = SelectionType.Single,
                makeItem = MakeRow,
                bindItem = BindRow,
                unbindItem = (element, _) => element.Clear(),
            };
            // minHeight を指定すると折りたたみ時にもその高さ分の空白が残るため設定しない。
            _listView.style.marginTop = 4;
            _listView.SetViewController(new SafeListViewController());
            _listView.itemsSource = _indexProxy;
            _listView.overridingAddButtonBehavior = (_, button) => OpenAddMenu(button);
            _listView.itemsAdded += OnItemsAdded;
            _listView.itemsRemoved += OnItemsRemoved;
            _listView.itemIndexChanged += OnItemIndexChanged;
            Add(_listView);

            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        public void AddEntry(EntryKind kind)
        {
            AddEntry(kind, GetListProperty().arraySize);
        }

        public void RemoveEntryAt(int index)
        {
            SerializedProperty list = GetListProperty();
            if (index < 0 || index >= list.arraySize)
            {
                return;
            }

            SerializedObject serializedObject = list.serializedObject;
            serializedObject.Update();
            list = GetListProperty();
            list.DeleteArrayElementAtIndex(index);
            ApplyModifiedPropertiesAndRefresh(serializedObject);
        }

        public void MoveEntry(int fromIndex, int toIndex)
        {
            SerializedProperty list = GetListProperty();
            if (fromIndex < 0 || fromIndex >= list.arraySize)
            {
                return;
            }

            if (toIndex < 0)
            {
                toIndex = 0;
            }
            if (toIndex >= list.arraySize)
            {
                toIndex = list.arraySize - 1;
            }
            if (fromIndex == toIndex)
            {
                return;
            }

            SerializedObject serializedObject = list.serializedObject;
            serializedObject.Update();
            list = GetListProperty();
            list.MoveArrayElement(fromIndex, toIndex);
            ApplyModifiedPropertiesAndRefresh(serializedObject);
        }

        public void SetEntryKind(int index, EntryKind kind)
        {
            SerializedProperty list = GetListProperty();
            if (index < 0 || index >= list.arraySize)
            {
                return;
            }

            SerializedObject serializedObject = list.serializedObject;
            serializedObject.Update();
            list = GetListProperty();

            SerializedProperty entryProperty = list.GetArrayElementAtIndex(index);
            EntryKind currentKind = ResolveEntryKind(entryProperty);
            if (entryProperty.managedReferenceValue != null && currentKind == kind)
            {
                return;
            }

            var previous = entryProperty.managedReferenceValue as PhonemeEntryBase;
            PhonemeEntryBase replacement = CreateEntry(kind);
            CopyCommonFields(previous, replacement);

            entryProperty.managedReferenceValue = replacement;
            ApplyModifiedPropertiesAndRefresh(serializedObject);
            Undo.IncrementCurrentGroup();
        }

        public VisualElement CreateBoundRowForIndex(int index)
        {
            var row = MakeRow();
            BindRow(row, index);
            return row;
        }

        private void AddEntry(EntryKind kind, int index)
        {
            SerializedProperty list = GetListProperty();
            if (index < 0 || index > list.arraySize)
            {
                index = list.arraySize;
            }

            SerializedObject serializedObject = list.serializedObject;
            serializedObject.Update();
            list = GetListProperty();

            list.InsertArrayElementAtIndex(index);
            SerializedProperty entryProperty = list.GetArrayElementAtIndex(index);
            entryProperty.managedReferenceValue = CreateEntry(kind);
            ApplyModifiedPropertiesAndRefresh(serializedObject);
        }

        private void OpenAddMenu(Button anchor)
        {
            var menu = new GenericMenu();
            menu.AddItem(
                new GUIContent(BlendShapeLabel),
                false,
                () => AddEntry(EntryKind.BlendShape));
            menu.AddItem(
                new GUIContent(AnimationClipLabel),
                false,
                () => AddEntry(EntryKind.AnimationClip));
            menu.AddItem(
                new GUIContent(ExpressionLabel),
                false,
                () => AddEntry(EntryKind.Expression));
            menu.DropDown(anchor != null ? anchor.worldBound : _listView.worldBound);
        }

        private static VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList(RowClassName);
            row.style.flexDirection = FlexDirection.Column;
            row.style.marginBottom = 4;
            return row;
        }

        private void BindRow(VisualElement row, int visualIndex)
        {
            row.Clear();

            SerializedProperty list = GetListProperty();
            int propertyIndex = ResolvePropertyIndex(visualIndex);
            if (propertyIndex < 0 || propertyIndex >= list.arraySize)
            {
                row.Add(new Label("<missing phoneme entry>"));
                return;
            }

            SerializedProperty entryProperty = list.GetArrayElementAtIndex(propertyIndex);
            EntryKind kind = ResolveEntryKind(entryProperty);
            row.Add(CreateTypeSelector(propertyIndex, kind));

            if (entryProperty.managedReferenceValue == null)
            {
                row.Add(new HelpBox("音素エントリの型が未設定です。", HelpBoxMessageType.Warning));
                return;
            }

            AddCommonFields(row, entryProperty);

            if (kind == EntryKind.BlendShape)
            {
                AddBlendShapeFields(row, entryProperty);
            }
            else if (kind == EntryKind.AnimationClip)
            {
                AddAnimationClipFields(row, entryProperty);
            }
            else if (kind == EntryKind.Expression)
            {
                AddExpressionFields(row, entryProperty);
            }
        }

        private DropdownField CreateTypeSelector(int propertyIndex, EntryKind kind)
        {
            string currentLabel = ToLabel(kind);
            var selector = new DropdownField("形式")
            {
                name = EntryTypeSelectorName,
                choices = EntryTypeChoices,
            };
            selector.SetValueWithoutNotify(currentLabel);
            selector.RegisterValueChangedCallback(evt =>
            {
                if (TryParseKind(evt.newValue, out EntryKind selectedKind))
                {
                    SetEntryKind(propertyIndex, selectedKind);
                }
            });
            return selector;
        }

        private static void AddCommonFields(VisualElement row, SerializedProperty entryProperty)
        {
            AddPhonemeIdDropdown(row, entryProperty);
            AddMaxWeightField(row, entryProperty);
        }

        private static void AddPhonemeIdDropdown(VisualElement row, SerializedProperty entryProperty)
        {
            SerializedProperty property = entryProperty.FindPropertyRelative(PhonemeIdFieldName);
            if (property == null)
            {
                row.Add(new Label($"<missing field: {PhonemeIdFieldName}>"));
                return;
            }

            string current = property.stringValue ?? string.Empty;
            var choices = new List<string>(PhonemeIdChoices.Count + 1) { string.Empty };
            choices.AddRange(PhonemeIdChoices);
            // 既存値が一覧外の場合は失わないよう末尾に追加して選択保持する。
            if (!string.IsNullOrEmpty(current) && !choices.Contains(current))
            {
                choices.Add(current);
            }

            var field = new DropdownField("音素 ID", choices, current)
            {
                tooltip = PhonemeIdTooltip,
            };
            field.RegisterValueChangedCallback(evt =>
            {
                SerializedObject so = property.serializedObject;
                so.Update();
                property.stringValue = evt.newValue ?? string.Empty;
                so.ApplyModifiedProperties();
            });
            row.Add(field);
        }

        private static void AddMaxWeightField(VisualElement row, SerializedProperty entryProperty)
        {
            SerializedProperty property = entryProperty.FindPropertyRelative(MaxWeightFieldName);
            if (property == null)
            {
                row.Add(new Label($"<missing field: {MaxWeightFieldName}>"));
                return;
            }

            var field = new FloatField("Max Weight")
            {
                tooltip = MaxWeightTooltip,
            };
            field.BindProperty(property);
            row.Add(field);
        }

        private static void AddBlendShapeFields(VisualElement row, SerializedProperty entryProperty)
        {
            SerializedProperty blendShapeNameProperty =
                entryProperty.FindPropertyRelative(BlendShapeNameFieldName);
            if (blendShapeNameProperty == null)
            {
                row.Add(new Label($"<missing field: {BlendShapeNameFieldName}>"));
                return;
            }

            var field = new TextField("BlendShape 名");
            field.BindProperty(blendShapeNameProperty);
            row.Add(field);

            var warning = new HelpBox("BlendShape 名未設定", HelpBoxMessageType.Warning)
            {
                name = BlendShapeWarningName,
            };
            ApplyBlendShapeWarningVisibility(warning, blendShapeNameProperty.stringValue);
            field.RegisterValueChangedCallback(evt =>
            {
                ApplyBlendShapeWarningVisibility(warning, evt.newValue);
            });
            row.Add(warning);
        }

        private static void AddAnimationClipFields(VisualElement row, SerializedProperty entryProperty)
        {
            SerializedProperty clipProperty = entryProperty.FindPropertyRelative(ClipFieldName);
            if (clipProperty == null)
            {
                row.Add(new Label($"<missing field: {ClipFieldName}>"));
                return;
            }

            var field = new ObjectField("AnimationClip")
            {
                objectType = typeof(AnimationClip),
                allowSceneObjects = false,
            };
            field.BindProperty(clipProperty);
            row.Add(field);

            // AnimationClip 未割り当てだと OnStart 時に snapshot が空になり、リップシンクが何も
            // 出力しない (= ユーザー視点で「動かない」状態) になる。視覚的に未割り当てを伝えるための警告。
            var warning = new HelpBox(
                "AnimationClip 未割り当てです。リップシンクを動かすには、この音素用の口形状クリップを割り当ててください。",
                HelpBoxMessageType.Warning)
            {
                name = AnimationClipWarningName,
            };
            ApplyAnimationClipWarningVisibility(warning, clipProperty.objectReferenceValue);
            field.RegisterValueChangedCallback(evt =>
            {
                ApplyAnimationClipWarningVisibility(warning, evt.newValue);
            });
            row.Add(warning);
        }

        private static void ApplyAnimationClipWarningVisibility(HelpBox warning, UnityEngine.Object clip)
        {
            warning.style.display = clip == null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void AddExpressionFields(VisualElement row, SerializedProperty entryProperty)
        {
            SerializedProperty expressionIdProperty =
                entryProperty.FindPropertyRelative(ExpressionIdFieldName);
            if (expressionIdProperty == null)
            {
                row.Add(new Label($"<missing field: {ExpressionIdFieldName}>"));
                return;
            }

            var warning = new HelpBox(ExpressionUnassignedMessage, HelpBoxMessageType.Warning)
            {
                name = ExpressionWarningName,
            };
            ApplyExpressionWarningVisibility(warning, expressionIdProperty.stringValue);

            var choices = new List<ExpressionChoice>();
            if (TryGetExpressionChoices(entryProperty.serializedObject, choices))
            {
                AddExpressionDropdown(row, expressionIdProperty, choices, warning);
            }
            else
            {
                var field = new TextField("Expression ID")
                {
                    name = ExpressionTextFieldName,
                };
                field.BindProperty(expressionIdProperty);
                field.RegisterValueChangedCallback(evt =>
                {
                    ApplyExpressionWarningVisibility(warning, evt.newValue);
                });
                row.Add(field);
            }

            row.Add(warning);
        }

        private static void AddExpressionDropdown(
            VisualElement row,
            SerializedProperty expressionIdProperty,
            List<ExpressionChoice> expressionChoices,
            HelpBox warning)
        {
            string currentExpressionId = expressionIdProperty.stringValue ?? string.Empty;
            var displayChoices = new List<string>(expressionChoices.Count + 1)
            {
                string.Empty,
            };

            string currentDisplay = string.Empty;
            foreach (ExpressionChoice choice in expressionChoices)
            {
                displayChoices.Add(choice.DisplayName);
                if (string.Equals(choice.Id, currentExpressionId, StringComparison.Ordinal))
                {
                    currentDisplay = choice.DisplayName;
                }
            }

            var field = new DropdownField("Expression", displayChoices, currentDisplay)
            {
                name = ExpressionDropdownName,
            };
            field.RegisterValueChangedCallback(evt =>
            {
                SerializedObject serializedObject = expressionIdProperty.serializedObject;
                serializedObject.Update();
                string expressionId = FindExpressionIdByDisplayName(expressionChoices, evt.newValue);
                expressionIdProperty.stringValue = expressionId;
                serializedObject.ApplyModifiedProperties();
                ApplyExpressionWarningVisibility(warning, expressionId);
            });
            row.Add(field);
        }

        private static bool TryGetExpressionChoices(
            SerializedObject serializedObject,
            List<ExpressionChoice> choices)
        {
            choices.Clear();
            var profileSo = serializedObject.targetObject as FacialCharacterProfileSO;
            if (profileSo == null || profileSo.Expressions == null || profileSo.Expressions.Count == 0)
            {
                return false;
            }

            foreach (ExpressionSerializable expression in profileSo.Expressions)
            {
                if (expression == null || string.IsNullOrEmpty(expression.id))
                {
                    continue;
                }

                string displayName = string.IsNullOrEmpty(expression.name)
                    ? expression.id
                    : expression.name;
                choices.Add(new ExpressionChoice(expression.id, $"{displayName} ({expression.id})"));
            }

            return choices.Count > 0;
        }

        private static string FindExpressionIdByDisplayName(
            List<ExpressionChoice> choices,
            string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
            {
                return string.Empty;
            }

            foreach (ExpressionChoice choice in choices)
            {
                if (string.Equals(choice.DisplayName, displayName, StringComparison.Ordinal))
                {
                    return choice.Id;
                }
            }

            return string.Empty;
        }

        private static void ApplyExpressionWarningVisibility(HelpBox warning, string expressionIdValue)
        {
            warning.style.display = string.IsNullOrEmpty(expressionIdValue)
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        private static void AddTextField(
            VisualElement row,
            SerializedProperty parent,
            string relativePath,
            string label)
        {
            SerializedProperty property = parent.FindPropertyRelative(relativePath);
            if (property == null)
            {
                row.Add(new Label($"<missing field: {relativePath}>"));
                return;
            }

            var field = new TextField(label);
            field.BindProperty(property);
            row.Add(field);
        }

        private static void AddFloatField(
            VisualElement row,
            SerializedProperty parent,
            string relativePath,
            string label)
        {
            SerializedProperty property = parent.FindPropertyRelative(relativePath);
            if (property == null)
            {
                row.Add(new Label($"<missing field: {relativePath}>"));
                return;
            }

            var field = new FloatField(label);
            field.BindProperty(property);
            row.Add(field);
        }

        private void OnItemsAdded(IEnumerable<int> indices)
        {
            foreach (int index in indices)
            {
                AddEntry(EntryKind.BlendShape, index);
            }
        }

        private void OnItemsRemoved(IEnumerable<int> indices)
        {
            var sorted = new List<int>(indices);
            sorted.Sort();
            for (int i = sorted.Count - 1; i >= 0; i--)
            {
                int visualIndex = sorted[i];
                RemoveEntryAt(visualIndex);
            }
        }

        private void OnItemIndexChanged(int fromIndex, int toIndex)
        {
            MoveEntry(fromIndex, toIndex);
        }

        private void OnAttachToPanel(AttachToPanelEvent evt)
        {
            if (_undoRedoSubscribed)
            {
                return;
            }

            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            _undoRedoSubscribed = true;
        }

        private void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            if (!_undoRedoSubscribed)
            {
                return;
            }

            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            _undoRedoSubscribed = false;
        }

        private void OnUndoRedoPerformed()
        {
            SerializedObject serializedObject = _listProperty?.serializedObject;
            if (serializedObject == null || serializedObject.targetObject == null)
            {
                return;
            }

            serializedObject.Update();
            RebuildIndexProxy(_indexProxy, GetListProperty());
            ClampSelection();
            _listView?.Rebuild();
        }

        private void ApplyModifiedPropertiesAndRefresh(SerializedObject serializedObject)
        {
            serializedObject.ApplyModifiedProperties();
            if (serializedObject.targetObject != null)
            {
                EditorUtility.SetDirty(serializedObject.targetObject);
            }

            serializedObject.Update();
            RebuildIndexProxy(_indexProxy, GetListProperty());
            ClampSelection();
            _listView?.Rebuild();
        }

        private void ClampSelection()
        {
            if (_listView == null)
            {
                return;
            }

            int count = _indexProxy.Count;
            if (count == 0)
            {
                _listView.ClearSelection();
                return;
            }

            int selected = _listView.selectedIndex;
            if (selected < 0 || selected >= count)
            {
                _listView.ClearSelection();
            }
        }

        private SerializedProperty GetListProperty()
        {
            return _listProperty.serializedObject.FindProperty(_listProperty.propertyPath) ?? _listProperty;
        }

        private int ResolvePropertyIndex(int visualIndex)
        {
            if (visualIndex >= 0 && visualIndex < _indexProxy.Count)
            {
                return _indexProxy[visualIndex];
            }

            return visualIndex;
        }

        private static void RebuildIndexProxy(List<int> indexProxy, SerializedProperty listProperty)
        {
            indexProxy.Clear();
            if (listProperty == null || !listProperty.isArray)
            {
                return;
            }

            for (int i = 0; i < listProperty.arraySize; i++)
            {
                indexProxy.Add(i);
            }
        }

        private static PhonemeEntryBase CreateEntry(EntryKind kind)
        {
            switch (kind)
            {
                case EntryKind.AnimationClip:
                    return new AnimationClipPhonemeEntry();
                case EntryKind.Expression:
                    return new ExpressionPhonemeEntry();
                default:
                    return new BlendShapePhonemeEntry();
            }
        }

        private static void CopyCommonFields(PhonemeEntryBase source, PhonemeEntryBase destination)
        {
            if (source == null || destination == null)
            {
                return;
            }

            destination.PhonemeId = source.PhonemeId;
            destination.MaxWeight = source.MaxWeight;
        }

        private static EntryKind ResolveEntryKind(SerializedProperty entryProperty)
        {
            object value = entryProperty.managedReferenceValue;
            if (value is AnimationClipPhonemeEntry)
            {
                return EntryKind.AnimationClip;
            }

            if (value is ExpressionPhonemeEntry)
            {
                return EntryKind.Expression;
            }

            if (value is BlendShapePhonemeEntry)
            {
                return EntryKind.BlendShape;
            }

            string fullTypename = entryProperty.managedReferenceFullTypename ?? string.Empty;
            if (fullTypename.Contains(typeof(AnimationClipPhonemeEntry).FullName))
            {
                return EntryKind.AnimationClip;
            }

            if (fullTypename.Contains(typeof(ExpressionPhonemeEntry).FullName))
            {
                return EntryKind.Expression;
            }

            return EntryKind.BlendShape;
        }

        private static string ToLabel(EntryKind kind)
        {
            switch (kind)
            {
                case EntryKind.AnimationClip:
                    return AnimationClipLabel;
                case EntryKind.Expression:
                    return ExpressionLabel;
                default:
                    return BlendShapeLabel;
            }
        }

        private static bool TryParseKind(string label, out EntryKind kind)
        {
            if (string.Equals(label, ExpressionLabel, StringComparison.Ordinal))
            {
                kind = EntryKind.Expression;
                return true;
            }

            if (string.Equals(label, AnimationClipLabel, StringComparison.Ordinal))
            {
                kind = EntryKind.AnimationClip;
                return true;
            }

            if (string.Equals(label, BlendShapeLabel, StringComparison.Ordinal))
            {
                kind = EntryKind.BlendShape;
                return true;
            }

            kind = EntryKind.BlendShape;
            return false;
        }

        private static void ApplyBlendShapeWarningVisibility(HelpBox warning, string blendShapeName)
        {
            warning.style.display = string.IsNullOrEmpty(blendShapeName)
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        private readonly struct ExpressionChoice
        {
            public ExpressionChoice(string id, string displayName)
            {
                Id = id;
                DisplayName = displayName;
            }

            public string Id { get; }
            public string DisplayName { get; }
        }
    }
}
