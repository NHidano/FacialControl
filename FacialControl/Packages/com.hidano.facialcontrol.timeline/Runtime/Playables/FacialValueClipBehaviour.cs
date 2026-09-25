using UnityEngine;
using UnityEngine.Playables;

namespace Hidano.FacialControl.Timeline.Playables
{
    public sealed class FacialValueClipBehaviour : PlayableBehaviour
    {
        public AnimationCurve[] Axes = System.Array.Empty<AnimationCurve>();
    }
}
