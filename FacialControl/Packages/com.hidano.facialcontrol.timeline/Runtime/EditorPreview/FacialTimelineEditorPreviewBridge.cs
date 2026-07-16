using Hidano.FacialControl.Timeline.Adapters;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.EditorPreview
{
    public static class FacialTimelineEditorPreviewBridge
    {
        public delegate void ApplyPreviewDelegate(FacialTimelineReceiver receiver, TimelineAsset timeline, double timeSeconds);
        public delegate void GatherPropertiesDelegate(PlayableDirector director, TrackAsset track, IPropertyCollector collector);

        public static ApplyPreviewDelegate ApplyPreview { get; set; }

        public static GatherPropertiesDelegate GatherProperties { get; set; }
    }
}
