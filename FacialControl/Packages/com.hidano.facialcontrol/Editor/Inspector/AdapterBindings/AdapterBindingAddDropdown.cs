using System;
using System.Collections.Generic;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Hidano.FacialControl.Editor.Inspector.AdapterBindings
{
    /// <summary>
    /// <see cref="AdapterBindingsListView"/> の Add ボタン経由で開かれる
    /// <see cref="AdvancedDropdown"/> 派生。<see cref="AdapterBindingDiscovery"/>
    /// が列挙する <see cref="AdapterBindingDescriptor"/> 群を displayName 順に表示する。
    /// </summary>
    /// <remarks>
    //: Add UI は displayName 順 sort + 重複 displayName 警告 + suffix 付与。
    /// Sort / 重複処理は <see cref="AdapterBindingDiscovery"/> が事前に行うため、
    /// 本クラスは渡された descriptors をそのまま列挙する。
    /// </remarks>
    public sealed class AdapterBindingAddDropdown : AdvancedDropdown
    {
        private readonly IReadOnlyList<AdapterBindingDescriptor> _descriptors;
        private readonly Action<AdapterBindingDescriptor> _onSelected;

        public AdapterBindingAddDropdown(
            AdvancedDropdownState state,
            IReadOnlyList<AdapterBindingDescriptor> descriptors,
            Action<AdapterBindingDescriptor> onSelected)
            : base(state)
        {
            _descriptors = descriptors ?? throw new ArgumentNullException(nameof(descriptors));
            _onSelected = onSelected ?? throw new ArgumentNullException(nameof(onSelected));
            minimumSize = new Vector2(240f, 240f);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Adapter Bindings");
            for (int i = 0; i < _descriptors.Count; i++)
            {
                root.AddChild(new DescriptorItem(_descriptors[i]));
            }
            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is DescriptorItem descriptorItem)
            {
                _onSelected(descriptorItem.Descriptor);
            }
        }

        private sealed class DescriptorItem : AdvancedDropdownItem
        {
            public AdapterBindingDescriptor Descriptor { get; }

            public DescriptorItem(AdapterBindingDescriptor descriptor)
                : base(descriptor.DisplayName)
            {
                Descriptor = descriptor;
            }
        }
    }
}
