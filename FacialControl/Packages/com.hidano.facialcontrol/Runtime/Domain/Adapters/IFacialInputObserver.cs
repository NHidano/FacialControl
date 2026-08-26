using System;

namespace Hidano.FacialControl.Domain.Adapters
{
    /// <summary>
    /// Observes trigger and analog input events for a single FacialController scope.
    /// All callbacks are invoked synchronously on the main thread.
    /// </summary>
    public interface IFacialInputObserver
    {
        /// <summary>
        /// Called after a trigger source activates an expression.
        /// </summary>
        void OnTriggerOn(string sourceId, string expressionId);

        /// <summary>
        /// Called after a trigger source deactivates an expression.
        /// </summary>
        void OnTriggerOff(string sourceId, string expressionId);

        /// <summary>
        /// Called when an analog or gaze source publishes a frame sample.
        /// The supplied span is valid only during this call; observers must copy any data they retain.
        /// </summary>
        void OnAnalogSample(string sourceId, ReadOnlySpan<float> axes);
    }
}
