using System;
using Hidano.FacialControl.Timeline.Playables;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Clips
{
    [Serializable]
    public sealed class FacialValueClip : PlayableAsset, ITimelineClipAsset
    {
        [SerializeField] private AnimationCurve[] axes = Array.Empty<AnimationCurve>();

        public AnimationCurve[] Axes
        {
            get => axes;
            set => axes = value ?? Array.Empty<AnimationCurve>();
        }

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<FacialValueClipBehaviour> playable =
                ScriptPlayable<FacialValueClipBehaviour>.Create(graph);
            FacialValueClipBehaviour behaviour = playable.GetBehaviour();
            behaviour.Axes = axes ?? Array.Empty<AnimationCurve>();
            return playable;
        }
    }
}
