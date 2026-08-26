namespace Hidano.FacialControl.Rec.Application.UseCases
{
    /// <summary>
    /// Lifecycle state for a REC playback session.
    /// </summary>
    public enum RecPlaybackState
    {
        Idle = 0,
        Playing = 1,
        Completed = 2,
    }
}
