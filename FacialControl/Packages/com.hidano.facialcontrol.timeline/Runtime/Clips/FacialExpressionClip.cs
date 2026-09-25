using Hidano.FacialControl.Timeline.Playables;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Clips
{
    [System.Serializable]
    public sealed class FacialExpressionClip : PlayableAsset, ITimelineClipAsset
    {
        [SerializeField] private string expressionId = string.Empty;

        public string ExpressionId
        {
            get => expressionId;
            set => expressionId = value ?? string.Empty;
        }

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<FacialExpressionClipBehaviour> playable =
                ScriptPlayable<FacialExpressionClipBehaviour>.Create(graph);
            FacialExpressionClipBehaviour behaviour = playable.GetBehaviour();
            behaviour.ExpressionId = expressionId;
            return playable;
        }
    }
}
