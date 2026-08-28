using System;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.ScriptableObject
{
    /// <summary>GazeChannel を起点に入力源を解決する旧 resolver 後継。</summary>
    public static class GazeChannelResolver
    {
        public static bool TryResolve(
            GazeChannel channel,
            IInputSourceRegistry registry,
            out ResolvedGazeInputSources resolved)
        {
            resolved = default;
            if (channel == null || registry == null || string.IsNullOrWhiteSpace(channel.id))
            {
                return false;
            }

            if (channel.useDistinctLeftRight)
            {
                if (TryResolveExplicit(channel, registry, out resolved))
                {
                    return true;
                }

                return false;
            }

            if (!string.IsNullOrWhiteSpace(channel.providerSlug))
            {
                return TryResolveForSlug(channel.id, channel.providerSlug, registry, out resolved);
            }

            return TryResolveSidePair(channel.id, registry, out resolved)
                || TryResolveShared(channel.id, registry, out resolved);
        }

        private static bool TryResolveExplicit(GazeChannel channel, IInputSourceRegistry registry, out ResolvedGazeInputSources resolved)
        {
            resolved = default;
            TryGetAnalog(registry, channel.sourceIdLeft, out IAnalogInputSource left);
            TryGetAnalog(registry, channel.sourceIdRight, out IAnalogInputSource right);
            if (left == null && right == null) return false;

            if (left == null || right == null)
            {
                Debug.LogWarning($"[FacialControl] Gaze channel '{channel.id}' resolved only one distinct input source; using it for both eyes.");
                left ??= right;
                right ??= left;
            }

            resolved = new ResolvedGazeInputSources(left, right, null, channel.sourceIdLeft, channel.sourceIdRight);
            return true;
        }

        private static bool TryResolveForSlug(string channelId, string slug, IInputSourceRegistry registry, out ResolvedGazeInputSources resolved)
        {
            resolved = default;
            string leftId = GazeSourceIdConvention.Compose(slug, channelId, GazeSide.Left);
            string rightId = GazeSourceIdConvention.Compose(slug, channelId, GazeSide.Right);
            TryGetAnalog(registry, leftId, out IAnalogInputSource left);
            TryGetAnalog(registry, rightId, out IAnalogInputSource right);
            if (left == null && right == null)
            {
                string sharedId = GazeSourceIdConvention.Compose(slug, channelId, GazeSide.Shared);
                if (!TryGetAnalog(registry, sharedId, out IAnalogInputSource shared)) return false;
                resolved = new ResolvedGazeInputSources(shared, shared, slug, sharedId, sharedId);
                return true;
            }

            left ??= right;
            right ??= left;
            resolved = new ResolvedGazeInputSources(left, right, slug, leftId, rightId);
            return true;
        }

        private static bool TryResolveSidePair(string channelId, IInputSourceRegistry registry, out ResolvedGazeInputSources resolved)
        {
            resolved = default;
            string selectedSlug = null;
            IAnalogInputSource left = null, right = null;
            string leftId = null, rightId = null;
            var ids = registry.RegisteredIds;
            for (int i = 0; ids != null && i < ids.Count; i++)
            {
                if (!GazeSourceIdConvention.TryParse(ids[i], out string slug, out string parsedChannel, out GazeSide side)
                    || !string.Equals(parsedChannel, channelId, StringComparison.Ordinal)
                    || side == GazeSide.Shared
                    || !TryGetAnalog(registry, ids[i], out IAnalogInputSource source)) continue;

                if (selectedSlug == null || string.CompareOrdinal(slug, selectedSlug) < 0)
                {
                    if (selectedSlug != null) LogConflict(channelId, slug);
                    selectedSlug = slug;
                    left = right = null;
                    leftId = rightId = null;
                }
                else if (!string.Equals(slug, selectedSlug, StringComparison.Ordinal))
                {
                    LogConflict(channelId, selectedSlug);
                    continue;
                }

                if (side == GazeSide.Left) { left = source; leftId = ids[i]; }
                else { right = source; rightId = ids[i]; }
            }

            if (left == null && right == null) return false;
            left ??= right; right ??= left; leftId ??= rightId; rightId ??= leftId;
            resolved = new ResolvedGazeInputSources(left, right, selectedSlug, leftId, rightId);
            return true;
        }

        private static bool TryResolveShared(string channelId, IInputSourceRegistry registry, out ResolvedGazeInputSources resolved)
        {
            resolved = default;
            string selectedSlug = null, selectedId = null;
            IAnalogInputSource source = null;
            var ids = registry.RegisteredIds;
            for (int i = 0; ids != null && i < ids.Count; i++)
            {
                if (!GazeSourceIdConvention.TryParse(ids[i], out string slug, out string parsedChannel, out GazeSide side)
                    || side != GazeSide.Shared
                    || !string.Equals(parsedChannel, channelId, StringComparison.Ordinal)
                    || !TryGetAnalog(registry, ids[i], out IAnalogInputSource candidate)) continue;
                if (selectedSlug == null || string.CompareOrdinal(slug, selectedSlug) < 0)
                {
                    if (selectedSlug != null) LogConflict(channelId, slug);
                    selectedSlug = slug; selectedId = ids[i]; source = candidate;
                }
                else if (!string.Equals(slug, selectedSlug, StringComparison.Ordinal)) LogConflict(channelId, selectedSlug);
            }
            if (source == null) return false;
            resolved = new ResolvedGazeInputSources(source, source, selectedSlug, selectedId, selectedId);
            return true;
        }

        private static bool TryGetAnalog(IInputSourceRegistry registry, string id, out IAnalogInputSource source)
        {
            source = null;
            return !string.IsNullOrEmpty(id) && registry.TryResolve(id, out IInputSource input) && (source = input as IAnalogInputSource) != null;
        }

        private static void LogConflict(string channelId, string slug)
        {
            Debug.LogWarning($"[FacialControl] Gaze channel '{channelId}' is provided by multiple binding slugs; selected '{slug}' by Ordinal lexicographic order.");
        }
    }
}
