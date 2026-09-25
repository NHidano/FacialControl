namespace Hidano.FacialControl.Adapters.OSC
{
    public interface IOscResolvedMessageHandler
    {
        bool HandleIncomingOscMessage(in OscMessageView view, in OscResolvedMessage resolved);
    }
}
