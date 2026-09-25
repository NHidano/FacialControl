using System;
using System.Collections.Generic;
using System.Reflection;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using UnityEditor;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Editor.Inspector.AdapterBindings
{
    /// <summary>
    /// <see cref="AdapterBindingBase.Slug"/> を編集するための共通 UI Toolkit 要素。
    /// 既定候補 (binding type の displayName 由来) を <see cref="PopupField{T}"/> で提示しつつ、
    /// 同 SO 内で同 binding 型を複数装着するケースのために手動 override 用 <see cref="TextField"/>
    /// も併設する。両者は同じ <see cref="SerializedProperty"/> を読み書きするため、
    /// どちらを操作しても永続化先は同一。
    /// </summary>
    /// <remarks>
    /// 設計方針 (S-8 backlog 由来):
    /// <list type="bullet">
    ///   <item>固定候補ドロップダウンに置き換えると複数装着時の slug 衝突回避が困難になるため、
    ///         candidate ドロップダウン + 自由入力テキストの 2 段にする
    ///         (<see cref="LipSync.Editor.Inspector.DeviceDescriptorPopup"/> と同じパターン)。</item>
    ///   <item>candidate は default slug + 連番 suffix (default-2 … default-5) を上限とする。
    ///         binding type が判定できないときはテキスト入力のみで動作する。</item>
    ///   <item>不正な slug (<see cref="AdapterSlug"/> 規約に合わない値) も TextField への直接入力で
    ///         保存できる。同 SO 内 slug 重複検出は
    ///         <see cref="AdapterBindingsListView.ApplyDuplicateSlugMarkers"/> 側に委譲する。</item>
    /// </list>
    /// </remarks>
    public sealed class AdapterBindingSlugField : VisualElement
    {
        public const string RootClassName = "facial-control-adapter-binding-slug-field";
        public const string PopupName = "facial-control-adapter-binding-slug-popup";
        public const string TextFieldName = "facial-control-adapter-binding-slug-text";

        // 連番 suffix の候補上限。複数装着といっても同 SO に大量に並べる用途は想定しない。
        private const int MaxSuffixCandidates = 4;

        private static readonly string ManualEntrySentinel = "(手動入力)";

        private readonly SerializedProperty _slugProperty;
        private readonly List<string> _choices = new List<string>();

        private PopupField<string> _popup;
        private TextField _textField;

        /// <summary>
        /// テストや他 Drawer から再利用する経路。binding type が解決できないときや、
        /// candidate を提示せず TextField のみで運用したいときは <paramref name="bindingType"/> に
        /// <c>null</c> を渡す。
        /// </summary>
        public AdapterBindingSlugField(SerializedProperty slugProperty, Type bindingType)
        {
            _slugProperty = slugProperty ?? throw new ArgumentNullException(nameof(slugProperty));

            AddToClassList(RootClassName);

            CollectChoices(bindingType);
            Build();
        }

        private void Build()
        {
            string currentValue = _slugProperty.stringValue ?? string.Empty;

            if (_choices.Count > 0)
            {
                int initialIndex = ResolvePopupIndex(currentValue);
                _popup = new PopupField<string>(
                    "Slug 候補", _choices, initialIndex)
                {
                    name = PopupName,
                    tooltip = "Adapter Binding を識別する Slug の候補。"
                        + " 同 binding を複数装着する場合は連番 suffix 付き候補を選ぶか、"
                        + " 下のテキスト欄に任意の値を入力してください。",
                };
                _popup.RegisterValueChangedCallback(evt =>
                {
                    if (string.Equals(evt.newValue, ManualEntrySentinel, StringComparison.Ordinal))
                    {
                        // 手動入力センチネルが選ばれたときは TextField にフォーカスだけ移して slug は変えない。
                        _textField?.Focus();
                        return;
                    }
                    ApplyValue(evt.newValue);
                });
                Add(_popup);
            }

            _textField = new TextField("Slug")
            {
                name = TextFieldName,
                value = currentValue,
                tooltip = "Adapter Binding の slug を直接編集する欄。"
                    + " ASCII 英数字 + _ . - のみ、64 文字以内が有効。"
                    + " 上の候補と同じ値にすると候補側にも自動的に反映される。",
            };
            _textField.RegisterValueChangedCallback(evt =>
            {
                ApplyValue(evt.newValue);
                SyncPopupSelection(evt.newValue);
            });
            Add(_textField);
        }

        // 現在の slug 値を popup の選択肢インデックスに変換する。一致する候補が無いときは
        // 「手動入力」センチネル位置を選ぶ。
        private int ResolvePopupIndex(string currentValue)
        {
            if (string.IsNullOrEmpty(currentValue))
            {
                return 0;
            }
            int index = _choices.IndexOf(currentValue);
            return index >= 0 ? index : _choices.IndexOf(ManualEntrySentinel);
        }

        private void SyncPopupSelection(string currentValue)
        {
            if (_popup == null) return;
            if (string.IsNullOrEmpty(currentValue))
            {
                _popup.SetValueWithoutNotify(_choices[0]);
                return;
            }
            int index = _choices.IndexOf(currentValue);
            _popup.SetValueWithoutNotify(index >= 0 ? _choices[index] : ManualEntrySentinel);
        }

        private void ApplyValue(string newValue)
        {
            string normalized = newValue ?? string.Empty;
            var so = _slugProperty.serializedObject;
            so.Update();
            _slugProperty.stringValue = normalized;
            so.ApplyModifiedProperties();

            if (_textField != null
                && !string.Equals(_textField.value, normalized, StringComparison.Ordinal))
            {
                _textField.SetValueWithoutNotify(normalized);
            }
        }

        // binding type の displayName から default slug を作り、連番 suffix 付きの候補と
        // 末尾の「手動入力」センチネルを一覧化する。binding type 不明時は候補なしで
        // TextField のみのフォールバックとして動作する。
        private void CollectChoices(Type bindingType)
        {
            _choices.Clear();
            if (bindingType == null)
            {
                return;
            }

            string defaultSlug = TryBuildDefaultSlug(bindingType);
            if (string.IsNullOrEmpty(defaultSlug))
            {
                return;
            }

            _choices.Add(defaultSlug);
            for (int i = 2; i <= 1 + MaxSuffixCandidates; i++)
            {
                _choices.Add($"{defaultSlug}-{i}");
            }
            _choices.Add(ManualEntrySentinel);
        }

        private static string TryBuildDefaultSlug(Type bindingType)
        {
            var attribute = bindingType
                .GetCustomAttribute<FacialAdapterBindingAttribute>(inherit: false);
            if (attribute == null || string.IsNullOrEmpty(attribute.DisplayName))
            {
                return null;
            }
            try
            {
                return AdapterSlug.FromDisplayName(attribute.DisplayName).Value;
            }
            catch (FormatException)
            {
                // displayName が ASCII 文字を全く含まないと FromDisplayName が空文字を生成して
                // FormatException を投げる。candidate なしで TextField 単独運用にフォールバックする。
                return null;
            }
        }
    }
}
