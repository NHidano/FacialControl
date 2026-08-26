using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Hidano.FacialControl.Editor.Inspector.RuntimeSettings
{
    /// <summary>
    /// <see cref="AdapterRuntimeSettingsCollectionEditor"/> の Add ボタンから開かれる
    /// <see cref="AdvancedDropdown"/>。
    /// <see cref="AdapterRuntimeSettingsTypeRegistry.GetConcreteTypes"/> が返す
    /// <see cref="AdapterRuntimeSettingsBase"/> 派生具象型を displayName で一覧表示する。
    /// </summary>
    public sealed class AdapterRuntimeSettingsAddDropdown : AdvancedDropdown
    {
        private readonly IReadOnlyList<Type> _types;
        private readonly Action<Type> _onSelected;

        public AdapterRuntimeSettingsAddDropdown(
            AdvancedDropdownState state,
            IReadOnlyList<Type> types,
            Action<Type> onSelected)
            : base(state)
        {
            _types = types ?? throw new ArgumentNullException(nameof(types));
            _onSelected = onSelected ?? throw new ArgumentNullException(nameof(onSelected));
            minimumSize = new Vector2(280f, 240f);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Adapter Runtime Settings");
            for (int i = 0; i < _types.Count; i++)
            {
                var type = _types[i];
                if (type == null)
                {
                    continue;
                }
                root.AddChild(new TypeItem(type));
            }
            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is TypeItem typeItem)
            {
                _onSelected(typeItem.Type);
            }
        }

        private sealed class TypeItem : AdvancedDropdownItem
        {
            public Type Type { get; }

            public TypeItem(Type type)
                : base(AdapterRuntimeSettingsTypeRegistry.GetDisplayName(type))
            {
                Type = type;
            }
        }
    }
}
