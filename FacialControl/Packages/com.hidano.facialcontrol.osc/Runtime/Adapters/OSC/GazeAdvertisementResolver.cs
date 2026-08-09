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
        private static readonly Comparison<GazeAdvertisement> s_compareByExpressionIdOrdinal =
            CompareByExpressionIdOrdinal;

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

                bool isDuplicate = false;
                for (int existingIndex = 0; existingIndex < destination.Count; existingIndex++)
                {
                    if (string.Equals(
                            destination[existingIndex].ExpressionId,
                            expressionId,
                            StringComparison.Ordinal))
                    {
                        isDuplicate = true;
                        break;
                    }
                }

                if (!isDuplicate)
                {
                    destination.Add(new GazeAdvertisement(expressionId, format));
                }
            }
        }

        public static uint ComputeNormalizedHash(
            IReadOnlyList<GazeAdvertisement> entries,
            List<GazeAdvertisement> normalizedScratch)
        {
            if (normalizedScratch == null)
            {
                throw new ArgumentNullException(nameof(normalizedScratch));
            }

            normalizedScratch.Clear();
            if (entries == null || entries.Count == 0)
            {
                return HeartbeatHashHelper.Fnv1aOffsetBasis;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                GazeAdvertisement entry = entries[i];
                if (!string.IsNullOrEmpty(entry.ExpressionId) && IsKnownFormat(entry.Format))
                {
                    normalizedScratch.Add(entry);
                }
            }

            normalizedScratch.Sort(s_compareByExpressionIdOrdinal);
            uint hash = HeartbeatHashHelper.Fnv1aOffsetBasis;
            for (int i = 0; i < normalizedScratch.Count; i++)
            {
                GazeAdvertisement entry = normalizedScratch[i];
                hash = HeartbeatHashHelper.AppendFnv1aString(hash, entry.ExpressionId);
                hash = HeartbeatHashHelper.AppendFnv1aString(hash, entry.Format);
            }

            return hash;
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

            for (int i = 0; i < advertised.Count; i++)
            {
                GazeAdvertisement entry = advertised[i];
                bool manuallyCovered = false;
                if (manualEntries != null)
                {
                    for (int manualIndex = 0; manualIndex < manualEntries.Count; manualIndex++)
                    {
                        OscMappingEntry manualEntry = manualEntries[manualIndex];
                        if (IsValidManualGazeEntry(manualEntry) &&
                            string.Equals(
                                manualEntry.expressionId,
                                entry.ExpressionId,
                                StringComparison.Ordinal))
                        {
                            manuallyCovered = true;
                            break;
                        }
                    }
                }

                if (!manuallyCovered)
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
