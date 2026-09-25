using System;
using System.Collections.Generic;

namespace Hidano.FacialControl.Rec.Domain.Services
{
    /// <summary>
    /// Tracks source and expression ids by REC binary index.
    /// </summary>
    public sealed class RecIdTable
    {
        private readonly Dictionary<string, ushort> _sourceIndexes = new Dictionary<string, ushort>(StringComparer.Ordinal);
        private readonly Dictionary<string, ushort> _expressionIndexes = new Dictionary<string, ushort>(StringComparer.Ordinal);
        private readonly List<string> _sourceIds = new List<string>();
        private readonly List<string> _expressionIds = new List<string>();

        public IReadOnlyList<string> SourceIds => _sourceIds;

        public IReadOnlyList<string> ExpressionIds => _expressionIds;

        public ushort GetOrAddSourceId(string sourceId)
        {
            return GetOrAddId(sourceId, _sourceIndexes, _sourceIds, nameof(sourceId));
        }

        public ushort GetOrAddExpressionId(string expressionId)
        {
            return GetOrAddId(expressionId, _expressionIndexes, _expressionIds, nameof(expressionId));
        }

        public void AddDefinedId(ushort idIndex, Hidano.FacialControl.Rec.Domain.Models.RecEvent.IdDefinitionKind idKind, string value)
        {
            if (idKind == Hidano.FacialControl.Rec.Domain.Models.RecEvent.IdDefinitionKind.Source)
            {
                AddDefinedIdCore(idIndex, value, _sourceIndexes, _sourceIds, nameof(value));
                return;
            }

            if (idKind == Hidano.FacialControl.Rec.Domain.Models.RecEvent.IdDefinitionKind.Expression)
            {
                AddDefinedIdCore(idIndex, value, _expressionIndexes, _expressionIds, nameof(value));
                return;
            }

            throw new ArgumentOutOfRangeException(nameof(idKind), "Unsupported id definition kind.");
        }

        public bool TryGetSourceId(ushort index, out string sourceId)
        {
            return TryGetId(index, _sourceIds, out sourceId);
        }

        public bool TryGetExpressionId(ushort index, out string expressionId)
        {
            return TryGetId(index, _expressionIds, out expressionId);
        }

        public bool TryGetSourceIndex(string sourceId, out ushort index)
        {
            return _sourceIndexes.TryGetValue(sourceId, out index);
        }

        public bool TryGetExpressionIndex(string expressionId, out ushort index)
        {
            return _expressionIndexes.TryGetValue(expressionId, out index);
        }

        private static ushort GetOrAddId(
            string value,
            Dictionary<string, ushort> indexes,
            List<string> values,
            string paramName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Id value is required.", paramName);
            }

            if (indexes.TryGetValue(value, out ushort existing))
            {
                return existing;
            }

            if (values.Count >= ushort.MaxValue + 1)
            {
                throw new InvalidOperationException("REC id table exceeded the supported 65536 entries.");
            }

            ushort index = checked((ushort)values.Count);
            indexes.Add(value, index);
            values.Add(value);
            return index;
        }

        private static void AddDefinedIdCore(
            ushort idIndex,
            string value,
            Dictionary<string, ushort> indexes,
            List<string> values,
            string paramName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Id value is required.", paramName);
            }

            if (indexes.TryGetValue(value, out ushort existingIndex) && existingIndex != idIndex)
            {
                throw new InvalidOperationException($"Id '{value}' was already assigned to index {existingIndex}.");
            }

            while (values.Count <= idIndex)
            {
                values.Add(null);
            }

            string existingValue = values[idIndex];
            if (existingValue != null && !string.Equals(existingValue, value, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Id index {idIndex} is already assigned to '{existingValue}'.");
            }

            values[idIndex] = value;
            indexes[value] = idIndex;
        }

        private static bool TryGetId(ushort index, List<string> values, out string value)
        {
            if (index < values.Count)
            {
                value = values[index];
                return value != null;
            }

            value = null;
            return false;
        }
    }
}
