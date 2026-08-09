using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.OSC
{
    /// <summary>
    /// Parses the flat string-pair payload used by /_facialcontrol/gaze and
    /// produces a deterministic content hash for change detection.
    /// </summary>
    public static class GazeAdvertisementResolver
    {
        public const string VrChatXyFormat = "VRChat_XY";
        public const string ArKit8BsFormat = "ARKit_8BS";

        public readonly struct GazeAdvertisement
        {
            public GazeAdvertisement(string expressionId, string format)
            {
                ExpressionId = expressionId;
                Format = format;
            }

            public string ExpressionId { get; }
            public string Format { get; }
        }

        /// <summary>
        /// Parses [id, format, ...]. Invalid tails, empty ids, duplicate ids,
        /// and unknown formats are ignored without throwing.
        /// </summary>
        public static void Parse(
            IReadOnlyList<string> payload,
            IList<GazeAdvertisement> destination,
            ref bool warnedOnUnknownFormat)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            destination.Clear();
            if (payload == null)
            {
                return;
            }

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            int pairCount = payload.Count / 2;
            for (int i = 0; i < pairCount; i++)
            {
                string expressionId = payload[i * 2];
                string format = payload[i * 2 + 1];
                if (string.IsNullOrEmpty(expressionId) || !IsKnownFormat(format))
                {
                    if (!string.IsNullOrEmpty(expressionId) && !IsKnownFormat(format))
                    {
                        WarnUnknownFormatOnce(format, ref warnedOnUnknownFormat);
                    }

                    continue;
                }

                if (seenIds.Add(expressionId))
                {
                    destination.Add(new GazeAdvertisement(expressionId, format));
                }
            }
        }

        public static uint ComputeNormalizedHash(IReadOnlyList<GazeAdvertisement> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return HeartbeatHashHelper.Fnv1aOffsetBasis;
            }

            var normalized = new List<GazeAdvertisement>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                GazeAdvertisement entry = entries[i];
                if (!string.IsNullOrEmpty(entry.ExpressionId) && IsKnownFormat(entry.Format))
                {
                    normalized.Add(entry);
                }
            }

            normalized.Sort(CompareByExpressionIdOrdinal);
            var interleaved = new string[normalized.Count * 2];
            for (int i = 0; i < normalized.Count; i++)
            {
                interleaved[i * 2] = normalized[i].ExpressionId;
                interleaved[i * 2 + 1] = normalized[i].Format;
            }

            return HeartbeatHashHelper.ComputeFnv1a(interleaved);
        }

        /// <summary>
        /// Builds an auto route plan while excluding gaze expression ids that
        /// are already covered by a valid manual gaze mapping.
        /// </summary>
        public static void BuildPlan(
            IReadOnlyList<GazeAdvertisement> advertised,
            IReadOnlyList<OscMappingEntry> manualEntries,
            IList<GazeAdvertisement> planResults)
        {
            if (planResults == null)
            {
                throw new ArgumentNullException(nameof(planResults));
            }

            planResults.Clear();
            if (advertised == null || advertised.Count == 0)
            {
                return;
            }

            var manuallyCoveredIds = new HashSet<string>(StringComparer.Ordinal);
            if (manualEntries != null)
            {
                for (int i = 0; i < manualEntries.Count; i++)
                {
                    OscMappingEntry entry = manualEntries[i];
                    if (IsValidManualGazeEntry(entry))
                    {
                        manuallyCoveredIds.Add(entry.expressionId);
                    }
                }
            }

            for (int i = 0; i < advertised.Count; i++)
            {
                GazeAdvertisement entry = advertised[i];
                if (!manuallyCoveredIds.Contains(entry.ExpressionId))
                {
                    planResults.Add(entry);
                }
            }
        }

        private static bool IsKnownFormat(string format)
        {
            return string.Equals(format, VrChatXyFormat, StringComparison.Ordinal) ||
                string.Equals(format, ArKit8BsFormat, StringComparison.Ordinal);
        }

        private static bool IsGazeMode(OscMappingMode mode)
        {
            return mode == OscMappingMode.Gaze_VRChat_XY ||
                mode == OscMappingMode.Gaze_ARKit_8BS;
        }

        private static bool IsValidManualGazeEntry(OscMappingEntry entry)
        {
            if (entry == null || !IsGazeMode(entry.mode) || string.IsNullOrEmpty(entry.expressionId))
            {
                return false;
            }

            if (entry.mode == OscMappingMode.Gaze_VRChat_XY && string.IsNullOrEmpty(entry.addressPattern))
            {
                return false;
            }

            return !entry.leftRightIndependent ||
                (!string.IsNullOrEmpty(entry.sourceIdLeft) && !string.IsNullOrEmpty(entry.sourceIdRight));
        }

        private static int CompareByExpressionIdOrdinal(
            GazeAdvertisement left,
            GazeAdvertisement right)
        {
            int idComparison = string.CompareOrdinal(left.ExpressionId, right.ExpressionId);
            return idComparison != 0
                ? idComparison
                : string.CompareOrdinal(left.Format, right.Format);
        }

        private static void WarnUnknownFormatOnce(string format, ref bool warned)
        {
            if (warned)
            {
                return;
            }

            warned = true;
            Debug.LogWarning($"[GazeAdvertisementResolver] Unknown gaze advertisement format '{format}'; the pair was skipped.");
        }
    }
}
