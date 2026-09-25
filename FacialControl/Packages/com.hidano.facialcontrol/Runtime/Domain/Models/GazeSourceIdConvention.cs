using System;

namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// Gaze source id の合成・分解規約を一元化する Unity 非依存ヘルパー。
    /// </summary>
    public static class GazeSourceIdConvention
    {
        public const string DefaultChannelId = "gaze";

        private const int MaxIdentifierLength = 64;
        private const char Separator = ':';
        private const string LeftSuffix = ".left";
        private const string RightSuffix = ".right";

        /// <summary>{slug}:{channelId}[.left|.right] の source id を作成する。</summary>
        public static string Compose(string slug, string channelId, GazeSide side)
        {
            if (!IsValidIdentifier(slug))
            {
                throw new ArgumentException("slug must be a valid identifier.", nameof(slug));
            }

            if (!IsValidChannelId(channelId))
            {
                throw new ArgumentException("channelId must be a valid channel id.", nameof(channelId));
            }

            string sub = ComposeSub(channelId, side);
            string result = slug + Separator + sub;
            if (result.Length > MaxIdentifierLength)
            {
                throw new ArgumentException("The composed source id must be 64 characters or fewer.");
            }

            return result;
        }

        /// <summary>チャネル id に左右 suffix を適用する。</summary>
        public static string ComposeSub(string channelId, GazeSide side)
        {
            if (!IsValidChannelId(channelId))
            {
                throw new ArgumentException("channelId must be a valid channel id.", nameof(channelId));
            }

            switch (side)
            {
                case GazeSide.Shared:
                    return channelId;
                case GazeSide.Left:
                    return channelId + LeftSuffix;
                case GazeSide.Right:
                    return channelId + RightSuffix;
                default:
                    throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown gaze side.");
            }
        }

        /// <summary>規約 source id を slug・チャネル id・side に分解する。</summary>
        public static bool TryParse(
            string sourceId,
            out string slug,
            out string channelId,
            out GazeSide side)
        {
            slug = null;
            channelId = null;
            side = default;

            if (string.IsNullOrEmpty(sourceId) || sourceId.Length > MaxIdentifierLength)
            {
                return false;
            }

            int separatorIndex = sourceId.IndexOf(Separator);
            if (separatorIndex <= 0 || separatorIndex != sourceId.LastIndexOf(Separator))
            {
                return false;
            }

            string parsedSlug = sourceId.Substring(0, separatorIndex);
            string sub = sourceId.Substring(separatorIndex + 1);
            if (!IsValidIdentifier(parsedSlug) || string.IsNullOrEmpty(sub))
            {
                return false;
            }

            GazeSide parsedSide = GazeSide.Shared;
            string parsedChannel = sub;
            if (sub.EndsWith(LeftSuffix, StringComparison.Ordinal))
            {
                parsedSide = GazeSide.Left;
                parsedChannel = sub.Substring(0, sub.Length - LeftSuffix.Length);
            }
            else if (sub.EndsWith(RightSuffix, StringComparison.Ordinal))
            {
                parsedSide = GazeSide.Right;
                parsedChannel = sub.Substring(0, sub.Length - RightSuffix.Length);
            }

            if (!IsValidChannelId(parsedChannel))
            {
                return false;
            }

            slug = parsedSlug;
            channelId = parsedChannel;
            side = parsedSide;
            return true;
        }

        /// <summary>チャネル id として利用できるかを検証する。</summary>
        public static bool IsValidChannelId(string channelId)
        {
            return IsValidIdentifier(channelId)
                && !channelId.EndsWith(LeftSuffix, StringComparison.Ordinal)
                && !channelId.EndsWith(RightSuffix, StringComparison.Ordinal);
        }

        private static bool IsValidIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaxIdentifierLength)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool valid = (c >= 'a' && c <= 'z')
                    || (c >= 'A' && c <= 'Z')
                    || (c >= '0' && c <= '9')
                    || c == '_' || c == '.' || c == '-';
                if (!valid)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
