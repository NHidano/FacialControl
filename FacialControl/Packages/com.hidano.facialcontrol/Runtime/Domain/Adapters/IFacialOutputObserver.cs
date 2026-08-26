using System;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Domain.Adapters
{
    /// <summary>
    /// Receives one synchronous post-blend output notification from the facial output bus.
    /// </summary>
    public interface IFacialOutputObserver
    {
        /// <summary>
        /// Handles post-blend BlendShape values and connected Gaze snapshots for the current frame.
        /// Both spans are valid only for the duration of this call; observers that need the values
        /// after the callback returns must copy them into their own storage.
        /// </summary>
        /// <param name="postBlendValues">
        /// Post-blend BlendShape values ordered exactly as the publishing FacialController exposes them.
        /// </param>
        /// <param name="gazeSnapshots">
        /// Connected Gaze snapshots keyed by <see cref="GazeSnapshot.ExpressionId"/>.
        /// </param>
        void OnFacialOutputPublished(
            ReadOnlySpan<float> postBlendValues,
            ReadOnlySpan<GazeSnapshot> gazeSnapshots);
    }
}
