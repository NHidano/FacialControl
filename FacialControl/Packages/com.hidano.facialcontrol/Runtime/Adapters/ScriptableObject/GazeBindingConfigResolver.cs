using System;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Interfaces;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.ScriptableObject
{
    public enum GazeSide
    {
        Shared,
        Left,
        Right
    }

    public readonly struct ResolvedGazeInputSources
    {
        public ResolvedGazeInputSources(
            IAnalogInputSource leftSource,
            IAnalogInputSource rightSource,
            string selectedSlug,
            string leftSourceId,
            string rightSourceId)
        {
            LeftSource = leftSource;
            RightSource = rightSource;
            SelectedSlug = selectedSlug;
            LeftSourceId = leftSourceId;
            RightSourceId = rightSourceId;
        }

        public IAnalogInputSource LeftSource { get; }
        public IAnalogInputSource RightSource { get; }
        public string SelectedSlug { get; }
        public string LeftSourceId { get; }
        public string RightSourceId { get; }
    }

    public static class GazeBindingConfigResolver
    {
        private const string LeftSuffix = ".left";
        private const string RightSuffix = ".right";

        /// <summary>
        /// 規約に従った gaze source id を合成する。
        /// Shared は "{slug}:{expressionId}"、Left/Right はそれぞれサフィックスを付ける。
        /// </summary>
        public static string ComposeSourceId(string slug, string expressionId, GazeSide side)
        {
            return $"{slug}:{ComposeSourceSub(expressionId, side)}";
        }

        /// <summary>
        /// Register の sub 部分を規約に従って合成する。
        /// </summary>
        public static string ComposeSourceSub(string expressionId, GazeSide side)
        {
            return side switch
            {
                GazeSide.Shared => expressionId,
                GazeSide.Left => expressionId + LeftSuffix,
                GazeSide.Right => expressionId + RightSuffix,
                _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown gaze side.")
            };
        }

        public static bool TryResolve(
            GazeBindingConfig config,
            IInputSourceRegistry registry,
            out ResolvedGazeInputSources resolved)
        {
            resolved = default;
            if (config == null
                || registry == null
                || string.IsNullOrWhiteSpace(config.expressionId))
            {
                return false;
            }

            return config.useDistinctLeftRight
                ? TryResolveDistinct(config, registry, out resolved)
                : TryResolveByConvention(config.expressionId, registry, out resolved);
        }

        private static bool TryResolveDistinct(
            GazeBindingConfig config,
            IInputSourceRegistry registry,
            out ResolvedGazeInputSources resolved)
        {
            TryResolveAnalogSource(registry, config.sourceIdLeft, out IAnalogInputSource leftSource);
            TryResolveAnalogSource(registry, config.sourceIdRight, out IAnalogInputSource rightSource);

            bool hasLeft = leftSource != null;
            bool hasRight = rightSource != null;
            bool configuredLeft = !string.IsNullOrWhiteSpace(config.sourceIdLeft);
            bool configuredRight = !string.IsNullOrWhiteSpace(config.sourceIdRight);

            if (!hasLeft && !hasRight)
            {
                if (configuredLeft != configuredRight)
                {
                    LogDistinctFallback(config.expressionId, config.sourceIdLeft, config.sourceIdRight);
                }

                resolved = default;
                return false;
            }

            if (!hasLeft || !hasRight)
            {
                LogDistinctFallback(config.expressionId, config.sourceIdLeft, config.sourceIdRight);
                leftSource ??= rightSource;
                rightSource ??= leftSource;
            }

            resolved = new ResolvedGazeInputSources(
                leftSource,
                rightSource,
                selectedSlug: null,
                leftSourceId: config.sourceIdLeft,
                rightSourceId: config.sourceIdRight);
            return true;
        }

        private static bool TryResolveByConvention(
            string expressionId,
            IInputSourceRegistry registry,
            out ResolvedGazeInputSources resolved)
        {
            if (TryResolveSidePair(expressionId, registry, out resolved))
            {
                return true;
            }

            return TryResolveShared(expressionId, registry, out resolved);
        }

        private static bool TryResolveSidePair(
            string expressionId,
            IInputSourceRegistry registry,
            out ResolvedGazeInputSources resolved)
        {
            resolved = default;
            var ids = registry.RegisteredIds;
            if (ids == null || ids.Count == 0)
            {
                return false;
            }

            string selectedSlugId = null;
            int selectedSlugSeparator = -1;
            IAnalogInputSource leftSource = null;
            IAnalogInputSource rightSource = null;
            string leftSourceId = null;
            string rightSourceId = null;
            bool hasMultipleSlugs = false;

            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                bool isLeft = IsCompositeSubMatch(id, expressionId, LeftSuffix, out int separatorIndex);
                bool isRight = !isLeft
                    && IsCompositeSubMatch(id, expressionId, RightSuffix, out separatorIndex);
                if (!isLeft && !isRight)
                {
                    continue;
                }

                if (!TryResolveAnalogSource(registry, id, out IAnalogInputSource source))
                {
                    continue;
                }

                if (selectedSlugId == null)
                {
                    selectedSlugId = id;
                    selectedSlugSeparator = separatorIndex;
                }
                else
                {
                    int comparison = CompareSlugOrdinal(id, separatorIndex, selectedSlugId, selectedSlugSeparator);
                    if (comparison < 0)
                    {
                        hasMultipleSlugs = true;
                        selectedSlugId = id;
                        selectedSlugSeparator = separatorIndex;
                        leftSource = null;
                        rightSource = null;
                        leftSourceId = null;
                        rightSourceId = null;
                    }
                    else if (comparison > 0)
                    {
                        hasMultipleSlugs = true;
                        continue;
                    }
                }

                if (isLeft)
                {
                    leftSource = source;
                    leftSourceId = id;
                }
                else
                {
                    rightSource = source;
                    rightSourceId = id;
                }
            }

            if (leftSource == null && rightSource == null)
            {
                return false;
            }

            if (hasMultipleSlugs)
            {
                LogDeterministicSelection(expressionId, selectedSlugId, selectedSlugSeparator);
            }

            leftSource ??= rightSource;
            rightSource ??= leftSource;
            leftSourceId ??= rightSourceId;
            rightSourceId ??= leftSourceId;

            resolved = new ResolvedGazeInputSources(
                leftSource,
                rightSource,
                ExtractSlug(selectedSlugId, selectedSlugSeparator),
                leftSourceId,
                rightSourceId);
            return true;
        }

        private static bool TryResolveShared(
            string expressionId,
            IInputSourceRegistry registry,
            out ResolvedGazeInputSources resolved)
        {
            resolved = default;
            var ids = registry.RegisteredIds;
            if (ids == null || ids.Count == 0)
            {
                return false;
            }

            string selectedId = null;
            int selectedSeparator = -1;
            IAnalogInputSource selectedSource = null;
            bool hasMultipleSlugs = false;

            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                if (!IsCompositeSubMatch(id, expressionId, suffix: null, out int separatorIndex)
                    || !TryResolveAnalogSource(registry, id, out IAnalogInputSource source))
                {
                    continue;
                }

                if (selectedId == null)
                {
                    selectedId = id;
                    selectedSeparator = separatorIndex;
                    selectedSource = source;
                    continue;
                }

                int comparison = CompareSlugOrdinal(id, separatorIndex, selectedId, selectedSeparator);
                if (comparison < 0)
                {
                    hasMultipleSlugs = true;
                    selectedId = id;
                    selectedSeparator = separatorIndex;
                    selectedSource = source;
                }
                else if (comparison > 0)
                {
                    hasMultipleSlugs = true;
                }
            }

            if (selectedSource == null)
            {
                return false;
            }

            if (hasMultipleSlugs)
            {
                LogDeterministicSelection(expressionId, selectedId, selectedSeparator);
            }

            resolved = new ResolvedGazeInputSources(
                selectedSource,
                selectedSource,
                ExtractSlug(selectedId, selectedSeparator),
                selectedId,
                selectedId);
            return true;
        }

        private static bool TryResolveAnalogSource(
            IInputSourceRegistry registry,
            string sourceId,
            out IAnalogInputSource source)
        {
            source = null;
            if (registry == null
                || string.IsNullOrEmpty(sourceId)
                || !registry.TryResolve(sourceId, out IInputSource inputSource)
                || inputSource == null)
            {
                return false;
            }

            source = inputSource as IAnalogInputSource;
            return source != null;
        }

        private static bool IsCompositeSubMatch(
            string registeredId,
            string expressionId,
            string suffix,
            out int separatorIndex)
        {
            separatorIndex = -1;
            if (string.IsNullOrEmpty(registeredId) || string.IsNullOrEmpty(expressionId))
            {
                return false;
            }

            separatorIndex = registeredId.IndexOf(':');
            if (separatorIndex <= 0 || separatorIndex >= registeredId.Length - 1)
            {
                return false;
            }

            int subStart = separatorIndex + 1;
            int suffixLength = suffix?.Length ?? 0;
            int expectedSubLength = expressionId.Length + suffixLength;
            if (registeredId.Length - subStart != expectedSubLength)
            {
                return false;
            }

            if (string.CompareOrdinal(
                    registeredId,
                    subStart,
                    expressionId,
                    0,
                    expressionId.Length) != 0)
            {
                return false;
            }

            return suffix == null
                || string.CompareOrdinal(
                    registeredId,
                    subStart + expressionId.Length,
                    suffix,
                    0,
                    suffixLength) == 0;
        }

        private static int CompareSlugOrdinal(string leftId, int leftSeparator, string rightId, int rightSeparator)
        {
            int minLength = leftSeparator < rightSeparator ? leftSeparator : rightSeparator;
            int comparison = string.CompareOrdinal(leftId, 0, rightId, 0, minLength);
            if (comparison != 0)
            {
                return comparison;
            }

            if (leftSeparator == rightSeparator)
            {
                return 0;
            }

            return leftSeparator < rightSeparator ? -1 : 1;
        }

        private static string ExtractSlug(string id, int separatorIndex)
        {
            if (string.IsNullOrEmpty(id) || separatorIndex <= 0)
            {
                return string.Empty;
            }

            return id.Substring(0, separatorIndex);
        }

        private static void LogDeterministicSelection(
            string expressionId,
            string selectedId,
            int selectedSeparator)
        {
            Debug.LogWarning(
                $"[GazeBindingConfigResolver] expressionId '{expressionId}' is provided by multiple binding slugs; selected '{ExtractSlug(selectedId, selectedSeparator)}' by Ordinal lexicographic order.");
        }

        private static void LogDistinctFallback(
            string expressionId,
            string sourceIdLeft,
            string sourceIdRight)
        {
            Debug.LogWarning(
                $"[GazeBindingConfigResolver] useDistinctLeftRight=true for expressionId '{expressionId}' resolved only one side (left='{sourceIdLeft ?? "<null>"}', right='{sourceIdRight ?? "<null>"}'); using the resolved side for both eyes.");
        }
    }
}
