using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Interfaces;

namespace Hidano.FacialControl.Adapters.Bone
{
    public readonly struct GazeBoneBinding
    {
        public GazeChannel Channel { get; }
        public IAnalogInputSource Source { get; }
        public IAnalogInputSource LeftSource { get; }
        public IAnalogInputSource RightSource { get; }

        public GazeBoneBinding(GazeChannel channel, IAnalogInputSource source)
            : this(channel, source, source) { }

        public GazeBoneBinding(GazeChannel channel, IAnalogInputSource leftSource, IAnalogInputSource rightSource)
        {
            Channel = channel;
            Source = leftSource ?? rightSource;
            LeftSource = leftSource;
            RightSource = rightSource;
        }
    }
}
