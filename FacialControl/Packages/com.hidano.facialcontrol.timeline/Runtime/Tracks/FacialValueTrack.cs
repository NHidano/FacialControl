using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Playables;
using System;
using System.Collections.Generic;
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
            ScriptPlayable<FacialValueMixerBehaviour> playable =
                ScriptPlayable<FacialValueMixerBehaviour>.Create(graph, inputCount);
            playable.GetBehaviour().Configure(channelSubId, channelKind, CollectClipSamples(this));
            return playable;
        }

        private static FacialValueMixerBehaviour.ClipSample[] CollectClipSamples(FacialValueTrack track)
        {
            if (track == null)
            {
                return Array.Empty<FacialValueMixerBehaviour.ClipSample>();
            }

            var clips = new List<FacialValueMixerBehaviour.ClipSample>();
            foreach (TimelineClip clip in track.GetClips())
            {
                if (!(clip.asset is FacialValueClip valueClip))
                {
                    continue;
                }

                clips.Add(new FacialValueMixerBehaviour.ClipSample(
                    clip.start,
                    clip.end,
                    valueClip.Axes));
            }

            return clips.Count == 0
                ? Array.Empty<FacialValueMixerBehaviour.ClipSample>()
                : clips.ToArray();
        }
    }
}
