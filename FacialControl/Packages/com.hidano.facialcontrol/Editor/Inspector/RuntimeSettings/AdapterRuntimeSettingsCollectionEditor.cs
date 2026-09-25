using System;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Editor.Inspector.RuntimeSettings
{
    /// <summary>
    /// <see cref="AdapterRuntimeSettingsCollectionSO"/> 用 UI Toolkit CustomEditor。
    /// sub-asset の追加/削除 UI と、各 sub-asset の埋め込み Inspector を提供する。
    /// </summary>
    /// <remarks>
    /// task 6.4 / 要件 3.1-3.4, 6.1-6.8 に対応。Add ボタンは
    /// <see cref="AdapterRuntimeSettingsTypeRegistry"/> 経由で具象型一覧を取得し
    /// <see cref="AdvancedDropdown"/> で表示する。Remove ボタンは確認ダイアログを挟み
    /// <c>AssetDatabase.RemoveObjectFromAsset</c> + <c>SerializedProperty</c> から除去する。
    /// 同型 sub-asset の複数登録は許可するが、<c>_label</c> が重複する場合は
    /// <c>Debug.LogWarning</c> を出す (要件 6.8)。
    /// </remarks>
    [CustomEditor(typeof(AdapterRuntimeSettingsCollectionSO))]
    public sealed class AdapterRuntimeSettingsCollectionEditor : UnityEditor.Editor
    {
        public const string RootName = "facial-control-collection-root";
        public const string ItemsContainerName = "facial-control-collection-items";
        public const string EmptyHelpName = "facial-control-collection-empty-help";
        public const string AddButtonName = "facial-control-collection-add";
        public const string ItemRowClassName = "facial-control-collection-item-row";
        public const string ItemRemoveButtonClassName = "facial-control-collection-item-remove";
        public const string ItemTitleClassName = "facial-control-collection-item-title";

        private const string ItemsFieldName = "_items";

        /// <summary>
        /// テストフィクスチャから Remove ボタンの確認ダイアログ呼び出しを抑止するためのトグル。
        /// production の Inspector 操作では常に false (確認ダイアログを表示する)。
        /// </summary>
        public static bool SuppressRemoveConfirmation = false;

        private SerializedProperty _itemsProperty;
        private VisualElement _root;
        private VisualElement _rowsContainer;
        private AdvancedDropdownState _addDropdownState;

        public override VisualElement CreateInspectorGUI()
        {
            _itemsProperty = serializedObject.FindProperty(ItemsFieldName);

            _root = new VisualElement { name = RootName };

            _rowsContainer = new VisualElement { name = ItemsContainerName };
            _root.Add(_rowsContainer);

            var addButton = new Button(OpenAddDropdown)
            {
                name = AddButtonName,
                text = "+ Settings を追加",
                tooltip = "Adapter Runtime Settings の sub-asset を追加",
            };
            addButton.style.marginTop = 6;
            addButton.style.alignSelf = Align.FlexEnd;
            addButton.style.minWidth = 160;
            _root.Add(addButton);

            Rebuild();
            return _root;
        }

        // ------------------------------------------------------------------
        // Public API（テスト + Inspector 操作から呼び出される）
        // ------------------------------------------------------------------

        /// <summary>
        /// 指定型 (<see cref="AdapterRuntimeSettingsBase"/> 派生の具象 ScriptableObject) の
        /// sub-asset を生成し、Collection に追加する。
        /// </summary>
        public void AddSubAssetOfType(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (type.IsAbstract || type.IsGenericTypeDefinition || type.IsInterface
                || !typeof(AdapterRuntimeSettingsBase).IsAssignableFrom(type))
            {
                Debug.LogError(
                    $"[AdapterRuntimeSettingsCollectionEditor] '{type.FullName}' は AdapterRuntimeSettingsBase の" +
                    " 具象派生型ではないため sub-asset として追加できません。");
                return;
            }

            var collection = target as AdapterRuntimeSettingsCollectionSO;
            if (collection == null)
            {
                Debug.LogError(
                    "[AdapterRuntimeSettingsCollectionEditor] target が AdapterRuntimeSettingsCollectionSO ではないため追加できません。");
                return;
            }

            var sub = ScriptableObject.CreateInstance(type) as AdapterRuntimeSettingsBase;
            if (sub == null)
            {
                Debug.LogError(
                    $"[AdapterRuntimeSettingsCollectionEditor] {type.FullName} の CreateInstance に失敗しました。");
                return;
            }

            sub.name = type.Name;

            WarnIfDuplicateLabel(collection, type, sub.Label);

            Undo.RegisterCreatedObjectUndo(sub, "Add Adapter Runtime Settings");
            Undo.RecordObject(collection, "Add Adapter Runtime Settings");

            string parentPath = AssetDatabase.GetAssetPath(collection);
            bool hasPersistentParent = !string.IsNullOrEmpty(parentPath);
            if (hasPersistentParent)
            {
                AssetDatabase.AddObjectToAsset(sub, collection);
            }

            serializedObject.Update();
            int newIndex = _itemsProperty.arraySize;
            _itemsProperty.InsertArrayElementAtIndex(newIndex);
            _itemsProperty.GetArrayElementAtIndex(newIndex).objectReferenceValue = sub;
            serializedObject.ApplyModifiedProperties();

            EditorUtility.SetDirty(collection);
            if (hasPersistentParent)
            {
                AssetDatabase.SaveAssets();
            }

            Rebuild();
        }

        /// <summary>
        /// 指定 index の sub-asset を Collection から削除する。
        /// <see cref="SuppressRemoveConfirmation"/> が false のときは確認ダイアログを表示する。
        /// </summary>
        public void RemoveSubAssetAt(int index)
        {
            var collection = target as AdapterRuntimeSettingsCollectionSO;
            if (collection == null)
            {
                return;
            }

            serializedObject.Update();
            if (index < 0 || index >= _itemsProperty.arraySize)
            {
                Debug.LogWarning(
                    $"[AdapterRuntimeSettingsCollectionEditor] 削除対象 index ({index}) が範囲外です。");
                return;
            }

            var elementProperty = _itemsProperty.GetArrayElementAtIndex(index);
            var sub = elementProperty.objectReferenceValue as AdapterRuntimeSettingsBase;

            if (!SuppressRemoveConfirmation)
            {
                string descriptor = ResolveRemoveDialogLabel(sub);
                bool confirmed = EditorUtility.DisplayDialog(
                    "Sub-asset を削除",
                    $"'{descriptor}' を Collection から削除しますか?",
                    "削除",
                    "キャンセル");
                if (!confirmed)
                {
                    return;
                }
            }

            Undo.RecordObject(collection, "Remove Adapter Runtime Settings");

            // ObjectReference 配列は「null 化 → DeleteArrayElementAtIndex」の 2 段操作で要素を除去する。
            elementProperty.objectReferenceValue = null;
            _itemsProperty.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();

            if (sub != null)
            {
                string subPath = AssetDatabase.GetAssetPath(sub);
                if (!string.IsNullOrEmpty(subPath))
                {
                    AssetDatabase.RemoveObjectFromAsset(sub);
                }
                Undo.DestroyObjectImmediate(sub);
            }

            EditorUtility.SetDirty(collection);
            AssetDatabase.SaveAssets();

            Rebuild();
        }

        // ------------------------------------------------------------------
        // 内部: UI 構築
        // ------------------------------------------------------------------

        private void Rebuild()
        {
            if (_rowsContainer == null)
            {
                return;
            }

            _rowsContainer.Clear();
            serializedObject.Update();

            int count = _itemsProperty.arraySize;
            if (count == 0)
            {
                var help = new HelpBox(
                    "Sub-asset がありません。下の「+ Settings を追加」ボタンから型を選択してください。",
                    HelpBoxMessageType.Info)
                {
                    name = EmptyHelpName,
                };
                _rowsContainer.Add(help);
                return;
            }

            for (int i = 0; i < count; i++)
            {
                var elementProperty = _itemsProperty.GetArrayElementAtIndex(i);
                var sub = elementProperty.objectReferenceValue as AdapterRuntimeSettingsBase;
                _rowsContainer.Add(BuildRow(sub, i));
            }
        }

        private VisualElement BuildRow(AdapterRuntimeSettingsBase sub, int index)
        {
            var row = new VisualElement { name = $"facial-control-collection-item-{index}" };
            row.AddToClassList(ItemRowClassName);
            row.style.marginTop = 2;
            row.style.marginBottom = 4;
            row.style.paddingTop = 6;
            row.style.paddingBottom = 6;
            row.style.paddingLeft = 8;
            row.style.paddingRight = 8;
            row.style.borderTopWidth = 1;
            row.style.borderBottomWidth = 1;
            row.style.borderLeftWidth = 1;
            row.style.borderRightWidth = 1;
            var border = new StyleColor(new Color(0.35f, 0.35f, 0.35f, 0.5f));
            row.style.borderTopColor = border;
            row.style.borderBottomColor = border;
            row.style.borderLeftColor = border;
            row.style.borderRightColor = border;

            var titleBar = new VisualElement();
            titleBar.style.flexDirection = FlexDirection.Row;
            titleBar.style.justifyContent = Justify.SpaceBetween;
            titleBar.style.alignItems = Align.Center;
            titleBar.AddToClassList(ItemTitleClassName);

            string titleText = sub == null
                ? $"[missing sub-asset @ index {index}]"
                : $"{sub.GetType().Name}{(string.IsNullOrEmpty(sub.Label) ? string.Empty : $" ({sub.Label})")}";
            var titleLabel = new Label(titleText);
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleBar.Add(titleLabel);

            int captured = index;
            var removeButton = new Button(() => RemoveSubAssetAt(captured))
            {
                text = "− 削除",
                tooltip = "この sub-asset を Collection から削除",
            };
            removeButton.AddToClassList(ItemRemoveButtonClassName);
            removeButton.style.minWidth = 80;
            titleBar.Add(removeButton);
            row.Add(titleBar);

            if (sub == null)
            {
                row.Add(new HelpBox(
                    "_items エントリが null です。sub-asset 参照が欠落しています。",
                    HelpBoxMessageType.Warning));
                return row;
            }

            var subEditor = CreateEditor(sub);
            if (subEditor != null)
            {
                var inspectorElement = new InspectorElement(subEditor);
                row.Add(inspectorElement);
            }

            return row;
        }

        private void OpenAddDropdown()
        {
            if (_addDropdownState == null)
            {
                _addDropdownState = new AdvancedDropdownState();
            }

            var types = AdapterRuntimeSettingsTypeRegistry.GetConcreteTypes();
            var dropdown = new AdapterRuntimeSettingsAddDropdown(_addDropdownState, types, AddSubAssetOfType);
            dropdown.Show(_root != null ? _root.worldBound : new Rect(0f, 0f, 240f, 0f));
        }

        // ------------------------------------------------------------------
        // 内部ヘルパー
        // ------------------------------------------------------------------

        private static void WarnIfDuplicateLabel(
            AdapterRuntimeSettingsCollectionSO collection, Type type, string label)
        {
            var items = collection.Items;
            if (items == null)
            {
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                var existing = items[i];
                if (existing == null)
                {
                    continue;
                }
                if (existing.GetType() != type)
                {
                    continue;
                }
                if (!string.Equals(existing.Label, label, StringComparison.Ordinal))
                {
                    continue;
                }

                Debug.LogWarning(
                    $"[AdapterRuntimeSettingsCollectionEditor] Collection '{collection.name}' に既に同じ _label ('{label}') の" +
                    $" {type.Name} sub-asset が存在します。同型 sub-asset の複数登録は許可されますが、" +
                    "識別困難になる可能性があります (要件 6.8)。");
                return;
            }
        }

        private static string ResolveRemoveDialogLabel(AdapterRuntimeSettingsBase sub)
        {
            if (sub == null)
            {
                return "(missing)";
            }
            if (!string.IsNullOrEmpty(sub.Label))
            {
                return $"{sub.GetType().Name} ({sub.Label})";
            }
            return sub.GetType().Name;
        }
    }
}
