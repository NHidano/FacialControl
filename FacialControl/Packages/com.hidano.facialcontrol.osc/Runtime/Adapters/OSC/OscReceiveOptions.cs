using System;

namespace Hidano.FacialControl.Adapters.OSC
{
    public readonly struct OscReceiveOptions
    {
        public const int DefaultDatagramSlotBytes = 2048;
        public const int DefaultDatagramSlotCount = 32;
        public const int DefaultSocketReceiveBufferBytes = 0;

        public int DatagramSlotBytes { get; }
        public int DatagramSlotCount { get; }
        public int SocketReceiveBufferBytes { get; }

        public static OscReceiveOptions Default => new OscReceiveOptions(
            DefaultDatagramSlotBytes, DefaultDatagramSlotCount, DefaultSocketReceiveBufferBytes);

        public OscReceiveOptions(
            int datagramSlotBytes,
            int datagramSlotCount,
            int socketReceiveBufferBytes)
        {
            if (datagramSlotBytes < 512 || datagramSlotBytes > 65535)
                throw new ArgumentOutOfRangeException(nameof(datagramSlotBytes));
            if (datagramSlotCount < 4 || datagramSlotCount > 1024)
                throw new ArgumentOutOfRangeException(nameof(datagramSlotCount));
            if (socketReceiveBufferBytes != 0 && (socketReceiveBufferBytes < 8192 || socketReceiveBufferBytes > 8 * 1024 * 1024))
                throw new ArgumentOutOfRangeException(nameof(socketReceiveBufferBytes));

            DatagramSlotBytes = datagramSlotBytes;
            DatagramSlotCount = datagramSlotCount;
            SocketReceiveBufferBytes = socketReceiveBufferBytes;
        }
    }
}
