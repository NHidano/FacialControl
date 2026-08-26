using System;

namespace Hidano.FacialControl.Adapters.OSC
{
    public readonly struct SenderIdentity : IEquatable<SenderIdentity>
    {
        public const string OscAddress = "/_facialcontrol/sender_id";
        public const int UuidByteLength = 16;

        public readonly Guid Uuid;
        public readonly long StartedAtUnixMs;

        public SenderIdentity(Guid uuid, long startedAtUnixMs)
        {
            if (uuid == Guid.Empty)
            {
                throw new ArgumentException("Sender UUID must not be empty.", nameof(uuid));
            }

            if (startedAtUnixMs < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(startedAtUnixMs), startedAtUnixMs,
                    "StartedAtUnixMs must be greater than or equal to zero.");
            }

            Uuid = uuid;
            StartedAtUnixMs = startedAtUnixMs;
        }

        public Guid SenderId => Uuid;

        public bool Equals(SenderIdentity other)
        {
            return Uuid.Equals(other.Uuid)
                && StartedAtUnixMs == other.StartedAtUnixMs;
        }

        public override bool Equals(object obj)
        {
            return obj is SenderIdentity other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Uuid.GetHashCode() * 397) ^ StartedAtUnixMs.GetHashCode();
            }
        }

        public override string ToString()
        {
            return $"{Uuid:D}@{StartedAtUnixMs}";
        }

        public static bool operator ==(SenderIdentity left, SenderIdentity right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(SenderIdentity left, SenderIdentity right)
        {
            return !left.Equals(right);
        }
    }
}
