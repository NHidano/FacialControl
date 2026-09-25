using System;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Domain.Adapters
{
    /// <summary>
    /// Domain contract for publishing post-blend BlendShape values and connected Gaze snapshots.
    /// Implementations own observer dispatch only; they do not sample, blend, or transmit values.
    /// </summary>
    public interface IFacialOutputBus
    {
        /// <summary>
        /// Registers an observer for later synchronous publish calls.
        /// </summary>
        /// <param name="observer">Observer to register.</param>
        void Subscribe(IFacialOutputObserver observer);

        /// <summary>
        /// Removes a previously registered observer.
        /// </summary>
        /// <param name="observer">Observer to remove.</param>
        void Unsubscribe(IFacialOutputObserver observer);

        /// <summary>
        /// Gets whether at least one observer is currently registered.
        /// </summary>
        bool HasObservers { get; }

        /// <summary>
        /// Publishes the current frame's post-blend output to registered observers.
        /// The supplied spans are valid only during this call; observers must copy any data they retain.
        /// </summary>
        /// <param name="postBlendValues">
        /// Post-blend BlendShape values ordered exactly as the publishing FacialController exposes them.
        /// </param>
        /// <param name="gazeSnapshots">
        /// Connected Gaze snapshots only. Disconnected Gaze sources are omitted by the publisher.
        /// </param>
        void Publish(
            ReadOnlySpan<float> postBlendValues,
            ReadOnlySpan<GazeSnapshot> gazeSnapshots);
    }
}
