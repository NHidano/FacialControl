using System;
using System.Collections.Generic;

namespace Hidano.FacialControl.Rec.Domain.Models
{
    /// <summary>
    /// Result of loading and validating a recording timeline.
    /// </summary>
    public sealed class RecLoadResult
    {
        private readonly string[] _missingExpressionIds;

        public RecLoadResult(RecTimeline timeline, IEnumerable<string> missingExpressionIds)
        {
            Timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
            _missingExpressionIds = CopyDistinctIds(missingExpressionIds);
        }

        public RecTimeline Timeline { get; }

        public IReadOnlyList<string> MissingExpressionIds => _missingExpressionIds;

        public bool HasMissingExpressionIds => _missingExpressionIds.Length > 0;

        private static string[] CopyDistinctIds(IEnumerable<string> ids)
        {
            if (ids == null)
            {
                return Array.Empty<string>();
            }

            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in ids)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    throw new ArgumentException("Missing expression ids must be non-empty.", nameof(ids));
                }

                if (seen.Add(id))
                {
                    list.Add(id);
                }
            }

            return list.Count == 0 ? Array.Empty<string>() : list.ToArray();
        }
    }
}
