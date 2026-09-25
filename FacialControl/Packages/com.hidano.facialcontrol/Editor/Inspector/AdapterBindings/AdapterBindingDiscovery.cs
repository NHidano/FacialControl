using System;
using System.Collections.Generic;
using System.Reflection;
using Hidano.FacialControl.Domain.Adapters;
using UnityEditor;
using UnityEngine;

namespace Hidano.FacialControl.Editor.Inspector.AdapterBindings
{
    /// <summary>
    /// Editor 起動時に <c>[FacialAdapterBinding]</c> 付き具象 <see cref="AdapterBindingBase"/>
    /// 派生型を <see cref="TypeCache"/> 経由で列挙し、displayName 順 sort + 重複検出 +
    /// suffix 付与を行うキャッシュを提供する static service。
    /// </summary>
    /// <remarks>
    /// <para>
    // / 1.4 / 1.7 に対応。<see cref="InitializeOnLoadAttribute"/> により domain reload 直後に
    /// 一度 scan を実行し、以後は <see cref="GetDescriptors"/> がキャッシュを返す。
    /// </para>
    /// <para>
    /// 重複 displayName は <c>"{originalDisplayName} ({FullTypeName})"</c> の形式に suffix 付与し、
    /// あわせて <see cref="Debug.LogWarning(object)"/> で全 FQTN を列挙する。
    /// </para>
    /// </remarks>
    [InitializeOnLoad]
    public static class AdapterBindingDiscovery
    {
        private static IReadOnlyList<AdapterBindingDescriptor> _descriptors = Array.Empty<AdapterBindingDescriptor>();

        /// <summary>
        /// Domain reload 後にキャッシュが再構築されたことを通知するイベント。
        /// </summary>
        public static event Action OnDescriptorsRebuilt;

        static AdapterBindingDiscovery()
        {
            Refresh();
        }

        /// <summary>
        /// Editor キャッシュを再構築する。
        /// </summary>
        /// <remarks>
        /// テスト・他コンポーネントから明示的に再 scan を要求する経路。
        /// 完了後に <see cref="OnDescriptorsRebuilt"/> を発火させる。
        /// </remarks>
        public static void Refresh()
        {
            _descriptors = BuildDescriptors();
            OnDescriptorsRebuilt?.Invoke();
        }

        /// <summary>
        /// 列挙済みの descriptor を displayName 順 (OrdinalIgnoreCase) で返す。
        /// </summary>
        public static IReadOnlyList<AdapterBindingDescriptor> GetDescriptors()
        {
            return _descriptors;
        }

        /// <summary>
        /// 指定型に対応する descriptor を返す。未登録 / null の場合は <c>null</c> を返す。
        /// </summary>
        public static AdapterBindingDescriptor? FindByType(Type type)
        {
            if (type == null)
            {
                return null;
            }

            var descriptors = _descriptors;
            for (int i = 0; i < descriptors.Count; i++)
            {
                if (descriptors[i].Type == type)
                {
                    return descriptors[i];
                }
            }

            return null;
        }

        private static IReadOnlyList<AdapterBindingDescriptor> BuildDescriptors()
        {
            var attributedTypes = TypeCache.GetTypesWithAttribute<FacialAdapterBindingAttribute>();
            var collected = new List<(Type Type, string OriginalDisplayName)>(attributedTypes.Count);

            foreach (var type in attributedTypes)
            {
                if (type == null)
                {
                    continue;
                }

                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                {
                    continue;
                }

                if (!typeof(AdapterBindingBase).IsAssignableFrom(type))
                {
                    continue;
                }

                var attribute = type.GetCustomAttribute<FacialAdapterBindingAttribute>(inherit: false);
                if (attribute == null)
                {
                    continue;
                }

                collected.Add((type, attribute.DisplayName ?? string.Empty));
            }

            collected.Sort((a, b) =>
            {
                int cmp = string.Compare(a.OriginalDisplayName, b.OriginalDisplayName, StringComparison.OrdinalIgnoreCase);
                if (cmp != 0)
                {
                    return cmp;
                }

                return string.Compare(a.Type.FullName, b.Type.FullName, StringComparison.Ordinal);
            });

            var duplicateGroups = new Dictionary<string, List<Type>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in collected)
            {
                if (!duplicateGroups.TryGetValue(entry.OriginalDisplayName, out var bucket))
                {
                    bucket = new List<Type>();
                    duplicateGroups[entry.OriginalDisplayName] = bucket;
                }
                bucket.Add(entry.Type);
            }

            foreach (var kv in duplicateGroups)
            {
                if (kv.Value.Count <= 1)
                {
                    continue;
                }

                Debug.LogWarning(
                    $"[FacialControl] Duplicate FacialAdapterBinding displayName '{kv.Key}' detected on {kv.Value.Count} types. " +
                    "Per-type FQTN will be enumerated in subsequent warnings.");
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    Debug.LogWarning($"[FacialControl]   - {kv.Value[i].FullName}");
                }
            }

            var result = new AdapterBindingDescriptor[collected.Count];
            for (int i = 0; i < collected.Count; i++)
            {
                var entry = collected[i];
                bool isDuplicate = duplicateGroups[entry.OriginalDisplayName].Count > 1;
                string displayName = isDuplicate
                    ? $"{entry.OriginalDisplayName} ({entry.Type.FullName})"
                    : entry.OriginalDisplayName;
                result[i] = new AdapterBindingDescriptor(entry.Type, displayName, entry.OriginalDisplayName);
            }

            return result;
        }
    }

    /// <summary>
    /// <see cref="AdapterBindingDiscovery"/> が返す 1 件分のメタ情報。
    /// </summary>
    public readonly struct AdapterBindingDescriptor : IEquatable<AdapterBindingDescriptor>
    {
        /// <summary>具象 <see cref="AdapterBindingBase"/> 派生型。</summary>
        public Type Type { get; }

        /// <summary>UI 表示用 displayName（重複時は <c>" (FullTypeName)"</c> suffix 付与済み）。</summary>
        public string DisplayName { get; }

        /// <summary><see cref="FacialAdapterBindingAttribute.DisplayName"/> の生値（suffix 未付与）。</summary>
        public string OriginalDisplayName { get; }

        public AdapterBindingDescriptor(Type type, string displayName, string originalDisplayName)
        {
            Type = type;
            DisplayName = displayName ?? string.Empty;
            OriginalDisplayName = originalDisplayName ?? string.Empty;
        }

        public bool Equals(AdapterBindingDescriptor other)
        {
            return Type == other.Type
                && DisplayName == other.DisplayName
                && OriginalDisplayName == other.OriginalDisplayName;
        }

        public override bool Equals(object obj)
        {
            return obj is AdapterBindingDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Type != null ? Type.GetHashCode() : 0;
                hash = (hash * 397) ^ (DisplayName != null ? StringComparer.Ordinal.GetHashCode(DisplayName) : 0);
                hash = (hash * 397) ^ (OriginalDisplayName != null ? StringComparer.Ordinal.GetHashCode(OriginalDisplayName) : 0);
                return hash;
            }
        }
    }
}
