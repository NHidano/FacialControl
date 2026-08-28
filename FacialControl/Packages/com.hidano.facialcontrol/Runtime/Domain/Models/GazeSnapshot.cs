using System;

namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// Immutable Domain value that identifies a Gaze expression and its normalized two-axis input.
    /// This type intentionally stores primitive floats instead of an engine vector type so Domain
    /// consumers stay independent from runtime engine APIs.
    /// </summary>
    public readonly struct GazeSnapshot : IEquatable<GazeSnapshot>
    {
        /// <summary>Gaze channel id matching the profile GazeChannel id.</summary>
        public readonly string ChannelId;

        /// <summary>互換用の旧名称。新規コードでは ChannelId を使用する。</summary>
        [Obsolete("ChannelId を使用してください。")]
        public string ExpressionId => ChannelId;

        /// <summary>Horizontal gaze value in Domain coordinates.</summary>
        public readonly float X;

        /// <summary>Vertical gaze value in Domain coordinates.</summary>
        public readonly float Y;

        /// <summary>
        /// Creates a Gaze snapshot. A null expression id is normalized to an empty string.
        /// </summary>
        public GazeSnapshot(string channelId, float x, float y)
        {
            ChannelId = channelId ?? string.Empty;
            X = x;
            Y = y;
        }

        public bool Equals(GazeSnapshot other)
        {
            return string.Equals(ChannelId, other.ChannelId, StringComparison.Ordinal)
                && X.Equals(other.X)
                && Y.Equals(other.Y);
        }

        public override bool Equals(object obj) => obj is GazeSnapshot other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (ChannelId != null ? StringComparer.Ordinal.GetHashCode(ChannelId) : 0);
                hash = (hash * 31) + X.GetHashCode();
                hash = (hash * 31) + Y.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(GazeSnapshot left, GazeSnapshot right) => left.Equals(right);

        public static bool operator !=(GazeSnapshot left, GazeSnapshot right) => !left.Equals(right);
    }
}
