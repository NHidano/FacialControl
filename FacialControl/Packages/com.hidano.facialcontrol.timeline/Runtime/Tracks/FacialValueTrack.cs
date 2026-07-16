using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Playables;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tracks
{
    [TrackClipType(typeof(FacialValueClip))]
    [TrackColor(0.23f, 0.56f, 0.78f)]
    public sealed class FacialValueTrack : TrackAsset
    {
        [SerializeField] private string channelSubId = string.Empty;
        [SerializeField] private FacialValueChannelKind channelKind = FacialValueChannelKind.Analog;

        public string ChannelSubId
        {
            get => channelSubId;
            set => channelSubId = value ?? string.Empty;
        }

        public FacialValueChannelKind ChannelKind
        {
            get => channelKind;
            set => channelKind = value;
        }

        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return ScriptPlayable<FacialTrackMixerBehaviour>.Create(graph, inputCount);
        }
    }
}
