using System;
using System.Collections.Generic;
using System.Reflection;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Editor.Inspector
{
    /// <summary>Gaze セクションの入力ソース候補を binding 宣言から作る Editor 専用 helper。</summary>
    public sealed class GazeProviderEnumerator
    {
        public const string AutomaticSlug = "";
        public const string AutomaticDisplayName = "自動";

        public IReadOnlyList<GazeProviderOption> Enumerate(
            IReadOnlyList<AdapterBindingBase> bindings,
            string channelId)
        {
            var result = new List<GazeProviderOption>
            {
                new GazeProviderOption(AutomaticDisplayName, AutomaticSlug, true)
            };
            if (bindings == null) return result;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < bindings.Count; i++)
            {
                AdapterBindingBase binding = bindings[i];
                if (binding == null || !(binding is IGazeSourceProvider provider)) continue;

                bool matches = false;
                IEnumerable<GazeSourceDeclaration> declarations = provider.GetGazeSourceDeclarations();
                if (declarations != null)
                {
                    foreach (GazeSourceDeclaration declaration in declarations)
                    {
                        if (string.IsNullOrEmpty(declaration.ChannelId) ||
                            string.Equals(declaration.ChannelId, channelId, StringComparison.Ordinal))
                        {
                            matches = true;
                            break;
                        }
                    }
                }
                if (!matches) continue;

                string slug = binding.Slug ?? string.Empty;
                if (!seen.Add(slug)) continue;
                string displayName = ResolveDisplayName(binding.GetType());
                result.Add(new GazeProviderOption(
                    string.IsNullOrEmpty(slug) ? displayName : displayName + " [" + slug + "]",
                    slug,
                    false));
            }

            result.Sort(1, result.Count - 1, GazeProviderOptionComparer.Instance);
            return result;
        }

        private static string ResolveDisplayName(Type type)
        {
            FacialAdapterBindingAttribute attribute =
                type.GetCustomAttribute<FacialAdapterBindingAttribute>(inherit: false);
            return attribute == null || string.IsNullOrEmpty(attribute.DisplayName)
                ? type.Name
                : attribute.DisplayName;
        }

        private sealed class GazeProviderOptionComparer : IComparer<GazeProviderOption>
        {
            public static readonly GazeProviderOptionComparer Instance = new GazeProviderOptionComparer();

            public int Compare(GazeProviderOption x, GazeProviderOption y)
            {
                int result = string.Compare(x.DisplayName, y.DisplayName, StringComparison.OrdinalIgnoreCase);
                return result != 0 ? result : string.Compare(x.Slug, y.Slug, StringComparison.Ordinal);
            }
        }
    }

    public readonly struct GazeProviderOption : IEquatable<GazeProviderOption>
    {
        public GazeProviderOption(string displayName, string slug, bool isAutomatic)
        {
            DisplayName = displayName ?? string.Empty;
            Slug = slug ?? string.Empty;
            IsAutomatic = isAutomatic;
        }

        public string DisplayName { get; }
        public string Slug { get; }
        public bool IsAutomatic { get; }

        public bool Equals(GazeProviderOption other) =>
            string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal) &&
            string.Equals(Slug, other.Slug, StringComparison.Ordinal) && IsAutomatic == other.IsAutomatic;

        public override bool Equals(object obj) => obj is GazeProviderOption other && Equals(other);
        public override int GetHashCode() => (DisplayName, Slug, IsAutomatic).GetHashCode();
    }
}
