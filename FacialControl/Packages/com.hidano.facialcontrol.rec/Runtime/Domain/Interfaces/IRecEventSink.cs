using System;
using Hidano.FacialControl.Rec.Domain.Models;

namespace Hidano.FacialControl.Rec.Domain.Interfaces
{
    /// <summary>
    /// Writer abstraction for a single recording session.
    /// </summary>
    public interface IRecEventSink
    {
        void Open(RecBaselineState baseline);

        void AppendEvent(in RecEvent evt, ReadOnlySpan<float> axes);

        void Complete(double durationSeconds, int eventCount);
    }
}
