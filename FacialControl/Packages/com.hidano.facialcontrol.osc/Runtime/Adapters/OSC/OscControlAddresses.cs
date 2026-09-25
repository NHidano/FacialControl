using System.Text;

namespace Hidano.FacialControl.Adapters.OSC
{
    public static class OscControlAddresses
    {
        public const string SenderId = "/_facialcontrol/sender_id";
        public const string BlendShapeNames = "/_facialcontrol/blendshape_names";
        public const string Preset = "/_facialcontrol/preset";
        public const string Gaze = "/_facialcontrol/gaze";

        public static readonly byte[] SenderIdUtf8 = Encoding.UTF8.GetBytes(SenderId);
        public static readonly byte[] BlendShapeNamesUtf8 = Encoding.UTF8.GetBytes(BlendShapeNames);
        public static readonly byte[] PresetUtf8 = Encoding.UTF8.GetBytes(Preset);
        public static readonly byte[] GazeUtf8 = Encoding.UTF8.GetBytes(Gaze);
    }
}
