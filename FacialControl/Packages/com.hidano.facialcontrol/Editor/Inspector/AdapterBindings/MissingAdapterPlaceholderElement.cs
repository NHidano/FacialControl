using System;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Editor.Inspector.AdapterBindings
{
    /// <summary>
    /// <c>[SerializeReference]</c> 経由で型が解決できなくなった adapter binding 要素
    /// （<c>managedReferenceValue == null</c>）の placeholder UI Toolkit 要素。
    /// </summary>
    /// <remarks>
    //: adapter package がアンロード／削除されたまま SO load された場合や、
    /// in-memory list に <c>null</c> 要素が残っている場合に表示される。Remove ボタンで
    /// 当該 row を削除可能。
    /// </remarks>
    public sealed class MissingAdapterPlaceholderElement : VisualElement
    {
        public const string PlaceholderClassName = "facial-control-missing-adapter";

        public MissingAdapterPlaceholderElement(string missingTypeName, Action onRemove)
        {
            AddToClassList(PlaceholderClassName);

            string description = string.IsNullOrEmpty(missingTypeName)
                ? "[FacialControl] Missing adapter binding (null entry). Remove or re-add via the Add button."
                : $"[FacialControl] Missing adapter binding type: {missingTypeName}";

            var label = new Label(description);
            Add(label);

            if (onRemove != null)
            {
                var removeButton = new Button(() => onRemove()) { text = "Remove" };
                Add(removeButton);
            }
        }
    }
}
