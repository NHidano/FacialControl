using Hidano.FacialControl.Timeline.Editor.Validation;
using Hidano.FacialControl.Timeline.Tracks;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Editor.TrackEditors
{
    [CustomTimelineEditor(typeof(FacialExpressionTrack))]
    public sealed class FacialExpressionTrackEditor : TrackEditor
    {
        public override TrackDrawOptions GetTrackOptions(TrackAsset track, Object binding)
        {
            return BuildTrackOptions(track, base.GetTrackOptions(track, binding), FacialTimelineValidator.Validate(track));
        }

        public static TrackDrawOptions BuildTrackOptions(
            TrackAsset track,
            TrackDrawOptions options,
            FacialTimelineValidationReport report)
        {
            if (track == null || report == null)
            {
                return options;
            }

            string message = report.GetTrackMessage(track);
            if (!string.IsNullOrEmpty(message))
            {
                options.errorText = message;
            }

            return options;
        }
    }
}
