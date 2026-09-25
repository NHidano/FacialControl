using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Editor.Windows.Routing.Logic
{
    public readonly struct SourcePort : IEquatable<SourcePort>
    {
        public SourcePort(string canonicalId, string label, string bindingSlug)
        {
            CanonicalId = canonicalId ?? throw new ArgumentNullException(nameof(canonicalId));
            Label = string.IsNullOrEmpty(label) ? canonicalId : label;
            BindingSlug = bindingSlug ?? string.Empty;
        }

        public string CanonicalId { get; }

        public string Label { get; }

        public string BindingSlug { get; }

        public bool Equals(SourcePort other)
        {
            return string.Equals(CanonicalId, other.CanonicalId, StringComparison.Ordinal)
                && string.Equals(Label, other.Label, StringComparison.Ordinal)
                && string.Equals(BindingSlug, other.BindingSlug, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is SourcePort other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(CanonicalId);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Label);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(BindingSlug);
                return hash;
            }
        }
    }

    public interface ISourcePortEnumerator
    {
        IReadOnlyList<SourcePort> EnumerateForLayer(AdapterBindingBase binding, string layerName);

        IReadOnlyList<SourcePort> Enumerate(AdapterBindingBase binding, IReadOnlyList<string> allLayerNames);

        IReadOnlyList<SourcePort> Enumerate(
            IReadOnlyList<AdapterBindingBase> bindings,
            IReadOnlyList<string> allLayerNames);

        ISet<string> EnumerateCanonicalIds(
            IReadOnlyList<AdapterBindingBase> bindings,
            IReadOnlyList<string> allLayerNames);
    }

    public sealed class SourcePortEnumerator : ISourcePortEnumerator
    {
        private const string OverlayLayerName = "overlay";

        public IReadOnlyList<SourcePort> EnumerateForLayer(AdapterBindingBase binding, string layerName)
        {
            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }

            if (binding is not IAdapterBindingDefaultLayerInputs multipleInputs)
            {
                return Array.Empty<SourcePort>();
            }

            var ports = new List<SourcePort>();
            var seenCanonicalIds = new HashSet<string>(StringComparer.Ordinal);

            IEnumerable<(string id, float weight)> sources = multipleInputs.GetDefaultLayerInputSources(layerName);
            if (sources == null)
            {
                return ports;
            }

            foreach ((string id, _) in sources)
            {
                TryAddPort(binding.Slug, id, seenCanonicalIds, ports);
            }

            return ports;
        }

        public IReadOnlyList<SourcePort> Enumerate(
            AdapterBindingBase binding,
            IReadOnlyList<string> allLayerNames)
        {
            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }

            var ports = new List<SourcePort>();
            var seenCanonicalIds = new HashSet<string>(StringComparer.Ordinal);

            // 1) 自動 Layer 生成系（LipSync / iFacialMocap 等）が宣言する default 入力源。
            if (binding is IAdapterBindingDefaultLayer defaultLayer)
            {
                if (binding is IAdapterBindingDefaultLayerInputs multipleInputs)
                {
                    IReadOnlyList<string> evaluationLayerNames =
                        BuildEvaluationLayerNames(defaultLayer.DefaultLayerName, allLayerNames);
                    for (int i = 0; i < evaluationLayerNames.Count; i++)
                    {
                        IEnumerable<(string id, float weight)> sources =
                            multipleInputs.GetDefaultLayerInputSources(evaluationLayerNames[i]);
                        if (sources == null)
                        {
                            continue;
                        }

                        foreach ((string id, _) in sources)
                        {
                            TryAddPort(binding.Slug, id, seenCanonicalIds, ports);
                        }
                    }

                    TryAddPort(binding.Slug, defaultLayer.DefaultLayerInputSourceId, seenCanonicalIds, ports);
                }
                else
                {
                    TryAddPort(binding.Slug, defaultLayer.DefaultLayerInputSourceId, seenCanonicalIds, ports);
                }
            }

            // 2) InputSystem のように設定済み入力源を動的に組む binding が静的宣言する canonical id。
            if (binding is IAdapterBindingDeclaredInputs declaredInputs)
            {
                IEnumerable<string> declaredIds = declaredInputs.GetDeclaredInputSourceIds();
                if (declaredIds != null)
                {
                    foreach (string id in declaredIds)
                    {
                        TryAddPort(binding.Slug, id, seenCanonicalIds, ports);
                    }
                }
            }

            return ports;
        }

        public IReadOnlyList<SourcePort> Enumerate(
            IReadOnlyList<AdapterBindingBase> bindings,
            IReadOnlyList<string> allLayerNames)
        {
            if (bindings == null)
            {
                throw new ArgumentNullException(nameof(bindings));
            }

            var ports = new List<SourcePort>();
            for (int i = 0; i < bindings.Count; i++)
            {
                AdapterBindingBase binding = bindings[i];
                if (binding == null)
                {
                    continue;
                }

                IReadOnlyList<SourcePort> bindingPorts = Enumerate(binding, allLayerNames);
                for (int portIndex = 0; portIndex < bindingPorts.Count; portIndex++)
                {
                    ports.Add(bindingPorts[portIndex]);
                }
            }

            return ports;
        }

        public ISet<string> EnumerateCanonicalIds(
            IReadOnlyList<AdapterBindingBase> bindings,
            IReadOnlyList<string> allLayerNames)
        {
            IReadOnlyList<SourcePort> ports = Enumerate(bindings, allLayerNames);
            var result = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < ports.Count; i++)
            {
                result.Add(ports[i].CanonicalId);
            }

            return result;
        }

        private static IReadOnlyList<string> BuildEvaluationLayerNames(
            string defaultLayerName,
            IReadOnlyList<string> allLayerNames)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            AddLayerName(defaultLayerName, seen, result);
            AddLayerName(OverlayLayerName, seen, result);

            if (allLayerNames == null)
            {
                return result;
            }

            for (int i = 0; i < allLayerNames.Count; i++)
            {
                AddLayerName(allLayerNames[i], seen, result);
            }

            return result;
        }

        private static void AddLayerName(
            string layerName,
            ISet<string> seen,
            ICollection<string> result)
        {
            if (string.IsNullOrEmpty(layerName) || !seen.Add(layerName))
            {
                return;
            }

            result.Add(layerName);
        }

        private static void TryAddPort(
            string bindingSlug,
            string canonicalId,
            ISet<string> seenCanonicalIds,
            ICollection<SourcePort> ports)
        {
            if (!InputSourceId.TryParse(canonicalId, out InputSourceId inputSourceId))
            {
                return;
            }

            if (!seenCanonicalIds.Add(inputSourceId.Value))
            {
                return;
            }

            ports.Add(new SourcePort(
                inputSourceId.Value,
                CreateLabel(inputSourceId.Value),
                bindingSlug ?? string.Empty));
        }

        private static string CreateLabel(string canonicalId)
        {
            if (string.IsNullOrEmpty(canonicalId))
            {
                return string.Empty;
            }

            int separatorIndex = canonicalId.LastIndexOf(':');
            if (separatorIndex < 0 || separatorIndex >= canonicalId.Length - 1)
            {
                return canonicalId;
            }

            return canonicalId.Substring(separatorIndex + 1);
        }
    }
}
