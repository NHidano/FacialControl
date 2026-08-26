namespace Hidano.FacialControl.LipSync.Adapters.Devices
{
    public interface IAsioDriverEnumerator
    {
        string[] GetDriverNames();
    }
}
