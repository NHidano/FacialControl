using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Editor.Validation;
using UnityEditor.Timeline;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Editor.TrackEditors
{
    [CustomTimelineEditor(typeof(FacialValueClip))]
    public sealed class FacialValueClipEditor : ClipEditor
    {
        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            return BuildClipOptions(clip, base.GetClipOptions(clip), FacialTimelineValidator.Validate(clip.GetParentTrack()));
        }

        public static ClipDrawOptions BuildClipOptions(
            TimelineClip clip,
            ClipDrawOptions options,
            FacialTimelineValidationReport report)
        {
            if (clip == null || report == null)
            {
                return options;
            }

            string message = report.GetClipMessage(clip);
            if (!string.IsNullOrEmpty(message))
            {
                options.errorText = message;
                options.tooltip = message;
            }

            return options;
        }
    }
}
