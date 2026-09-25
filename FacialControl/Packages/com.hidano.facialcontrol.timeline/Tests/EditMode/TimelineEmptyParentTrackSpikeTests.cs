using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    public sealed class TimelineEmptyParentTrackSpikeTests
    {
        [SetUp]
        public void SetUp()
        {
            ProbeTrack.ResetProbeState();
        }

        [Test]
        public void ParentTrackWithoutClips_WithChildLaneClip_DoesNotCreateParentMixer()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var directorObject = new GameObject("TimelineEmptyParentTrackSpikeTests_Director");
            var director = directorObject.AddComponent<PlayableDirector>();

            try
            {
                var parentTrack = timeline.CreateTrack<ProbeTrack>(null, "ParentTrack");
                var childTrack = timeline.CreateTrack<ProbeTrack>(parentTrack, "ChildLane");
                var childClip = childTrack.CreateClip<ProbeClipAsset>();
                childClip.start = 0.0d;
                childClip.duration = 1.0d;

                director.playableAsset = timeline;
                director.RebuildGraph();
                director.Evaluate();

                CollectionAssert.AreEqual(
                    new[] { "ChildLane" },
                    ProbeTrack.CreatedMixerTrackNames,
                    "Only the child lane mixer should be created in the child-only case.");
            }
            finally
            {
                if (director.playableGraph.IsValid())
                {
                    director.playableGraph.Destroy();
                }

                Object.DestroyImmediate(directorObject);
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void ParentTrackWithOwnClip_AndChildLaneClip_CreatesParentMixer()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var directorObject = new GameObject("TimelineEmptyParentTrackSpikeTests_ParentClip_Director");
            var director = directorObject.AddComponent<PlayableDirector>();

            try
            {
                var parentTrack = timeline.CreateTrack<ProbeTrack>(null, "ParentTrack");
                var childTrack = timeline.CreateTrack<ProbeTrack>(parentTrack, "ChildLane");

                var parentClip = parentTrack.CreateClip<ProbeClipAsset>();
                parentClip.start = 0.0d;
                parentClip.duration = 1.0d;

                var childClip = childTrack.CreateClip<ProbeClipAsset>();
                childClip.start = 0.25d;
                childClip.duration = 0.5d;

                director.playableAsset = timeline;
                director.RebuildGraph();
                director.Evaluate();

                CollectionAssert.AreEqual(
                    new[] { "ParentTrack" },
                    ProbeTrack.CreatedMixerTrackNames,
                    "When the parent owns a clip, Timeline should compile only the parent mixer for the layered track.");
            }
            finally
            {
                if (director.playableGraph.IsValid())
                {
                    director.playableGraph.Destroy();
                }

                Object.DestroyImmediate(directorObject);
                Object.DestroyImmediate(timeline);
            }
        }

        [TrackClipType(typeof(ProbeClipAsset))]
        private sealed class ProbeTrack : TrackAsset, ILayerable
        {
            public static List<string> CreatedMixerTrackNames { get; } = new List<string>();

            public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
            {
                CreatedMixerTrackNames.Add(name);
                return ScriptPlayable<ProbeTrackMixerBehaviour>.Create(graph, inputCount);
            }

            Playable ILayerable.CreateLayerMixer(PlayableGraph graph, GameObject go, int inputCount)
            {
                return Playable.Null;
            }

            public static void ResetProbeState()
            {
                CreatedMixerTrackNames.Clear();
            }
        }

        private sealed class ProbeClipAsset : PlayableAsset, ITimelineClipAsset
        {
            public ClipCaps clipCaps => ClipCaps.None;

            public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
            {
                return ScriptPlayable<ProbeClipBehaviour>.Create(graph);
            }
        }

        private sealed class ProbeTrackMixerBehaviour : PlayableBehaviour
        {
        }

        private sealed class ProbeClipBehaviour : PlayableBehaviour
        {
        }
    }
}
