namespace Hidano.FacialControl.Rec.Domain.Interfaces
{
    /// <summary>
    /// Monotonic relative clock for recording timestamps.
    /// </summary>
    public interface IRecClock
    {
        double ElapsedSeconds { get; }

        void Reset();
    }
}
