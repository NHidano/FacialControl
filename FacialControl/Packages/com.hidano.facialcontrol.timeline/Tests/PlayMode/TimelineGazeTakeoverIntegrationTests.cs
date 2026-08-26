using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.AdapterBindings;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.InputSources;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Domain.Services;
using Hidano.FacialControl.Timeline.Editor;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.TestTools;
using UnityEngine.Timeline;
using GazeBindingConfig = Hidano.FacialControl.Adapters.ScriptableObject.GazeBindingConfig;

namespace Hidano.FacialControl.Timeline.Tests.PlayMode
{
    [TestFixture]
    public sealed class TimelineGazeTakeoverIntegrationTests
    {
        private const string TakeoverSourceId = "live:gaze";
        private const string GazeChannelSub = "gaze-main";
        private const float RotationToleranceDegrees = 0.5f;

        [UnityTest]
        public IEnumerator TimelinePlayback_GazeTakeoverDrivesBoneAndGraphDestroyRestoresLiveSource()
        {
            using var fixture = new GazeIntegrationFixture(liveX: -0.25f, outerYawAngle: 28f);

            yield return null;
            Quaternion liveRotation = fixture.EyeBone.localRotation;

            fixture.AttachManualReceiver();
            fixture.EvaluateTimeline(0.20f);
            yield return null;

            Assert.That(fixture.TryResolveTakeoverSource(out IInputSource attachedSource), Is.True);
            Assert.That(attachedSource, Is.SameAs(fixture.GazeSink));
            Assert.That(
                Quaternion.Angle(liveRotation, fixture.EyeBone.localRotation),
                Is.GreaterThan(5f));

            fixture.DestroyGraph();
            yield return null;

            Assert.That(fixture.TryResolveTakeoverSource(out IInputSource restoredSource), Is.True);
            Assert.That(restoredSource, Is.SameAs(fixture.LiveSource));
            Assert.That(
                Quaternion.Angle(liveRotation, fixture.EyeBone.localRotation),
                Is.LessThan(RotationToleranceDegrees));
        }

        [UnityTest]
        public IEnumerator TimelinePlayback_ProfileReplacementChangesGazeResult()
        {
            using var sourceProfileFixture = new GazeIntegrationFixture(liveX: -0.10f, outerYawAngle: 12f);
            using var replacementProfileFixture = new GazeIntegrationFixture(liveX: -0.10f, outerYawAngle: 36f);

            sourceProfileFixture.AttachManualReceiver();
            replacementProfileFixture.AttachManualReceiver();

            sourceProfileFixture.EvaluateTimeline(0.20f);
            replacementProfileFixture.EvaluateTimeline(0.20f);
            yield return null;

            float sourceAngle = Quaternion.Angle(Quaternion.identity, sourceProfileFixture.EyeBone.localRotation);
            float replacementAngle = Quaternion.Angle(Quaternion.identity, replacementProfileFixture.EyeBone.localRotation);

            Assert.That(replacementAngle, Is.GreaterThan(sourceAngle + 8f));
        }

        [UnityTest]
        public IEnumerator TimelinePlayback_DestroyingReceiverRestoresLiveSource()
        {
            using var fixture = new GazeIntegrationFixture(liveX: -0.30f, outerYawAngle: 26f);

            yield return null;
            Quaternion liveRotation = fixture.EyeBone.localRotation;

            fixture.AttachManualReceiver();
            fixture.EvaluateTimeline(0.20f);
            yield return null;

            UnityEngine.Object.Destroy(fixture.Receiver);
            yield return null;

            Assert.That(fixture.TryResolveTakeoverSource(out IInputSource restoredSource), Is.True);
            Assert.That(restoredSource, Is.SameAs(fixture.LiveSource));
            Assert.That(
                Quaternion.Angle(liveRotation, fixture.EyeBone.localRotation),
                Is.LessThan(RotationToleranceDegrees));
        }

        [UnityTest]
        public IEnumerator TimelinePlayback_DisposingBindingRestoresLiveSource()
        {
            using var fixture = new GazeIntegrationFixture(liveX: -0.22f, outerYawAngle: 24f);

            yield return null;
            Quaternion liveRotation = fixture.EyeBone.localRotation;

            fixture.AttachBindingReceiver();
            fixture.EvaluateTimeline(0.20f);
            yield return null;

            fixture.Binding.Dispose();
            yield return null;

            Assert.That(fixture.TryResolveTakeoverSource(out IInputSource restoredSource), Is.True);
            Assert.That(restoredSource, Is.SameAs(fixture.LiveSource));
            Assert.That(fixture.Host.GetComponent<FacialTimelineReceiver>(), Is.Null);
            Assert.That(
                Quaternion.Angle(liveRotation, fixture.EyeBone.localRotation),
                Is.LessThan(RotationToleranceDegrees));
        }

        [UnityTest]
        public IEnumerator TimelinePlayback_WhenOwnershipChangesBeforeStop_LogsWarningAndPreservesLaterOccupant()
        {
            using var fixture = new GazeIntegrationFixture(liveX: -0.18f, outerYawAngle: 28f);
            var otherOwner = new InjectedGazeSource("other:owner", -0.85f, 0f);

            fixture.AttachManualReceiver();
            fixture.EvaluateTimeline(0.20f);
            yield return null;

            fixture.Controller.InputSourceRegistry.Replace(AdapterSlug.Parse("live"), "gaze", otherOwner);
            yield return null;
            Quaternion otherOwnerRotation = fixture.EyeBone.localRotation;

            LogAssert.Expect(
                LogType.Warning,
                "[FacialTimelineReceiver] Gaze takeover source 'live:gaze' is no longer owned by this receiver. Restoration is skipped.");

            fixture.DestroyGraph();
            yield return null;

            Assert.That(fixture.TryResolveTakeoverSource(out IInputSource currentSource), Is.True);
            Assert.That(currentSource, Is.SameAs(otherOwner));
            Assert.That(
                Quaternion.Angle(otherOwnerRotation, fixture.EyeBone.localRotation),
                Is.LessThan(RotationToleranceDegrees));
        }

        [UnityTest]
        public IEnumerator TimelinePlayback_WhenTakeoverAlreadyOccupied_LogsWarningAndKeepsChannelDisabled()
        {
            using var fixture = new GazeIntegrationFixture(liveX: -0.12f, outerYawAngle: 28f);
            var occupiedSource = new InjectedGazeSource("occupied:source", 0.65f, 0f);

            fixture.Controller.InputSourceRegistry.Replace(AdapterSlug.Parse("live"), "gaze", occupiedSource);
            yield return null;
            Quaternion occupiedRotation = fixture.EyeBone.localRotation;

            fixture.AttachManualReceiver();

            LogAssert.Expect(
                LogType.Warning,
                "[FacialTimelineReceiver] Gaze takeover source 'live:gaze' is already occupied by another injected source. The channel is disabled.");

            fixture.EvaluateTimeline(0.20f);
            yield return null;

            Assert.That(fixture.TryResolveTakeoverSource(out IInputSource currentSource), Is.True);
            Assert.That(currentSource, Is.SameAs(occupiedSource));
            Assert.That(
                Quaternion.Angle(occupiedRotation, fixture.EyeBone.localRotation),
                Is.LessThan(RotationToleranceDegrees));
        }

        private sealed class GazeIntegrationFixture : IDisposable
        {
            private readonly GameObject _directorObject;
            private readonly TestProfileSO _profile;
            private readonly FacialTimelineBakeAsset _bakeAsset;
            private bool _disposed;

            public GazeIntegrationFixture(float liveX, float outerYawAngle)
            {
                Host = CreateHost();
                EyeBone = new GameObject("LeftEye").transform;
                EyeBone.SetParent(Host.transform, worldPositionStays: false);
                EyeBone.localRotation = Quaternion.identity;

                Controller = Host.AddComponent<FacialController>();
                LiveSource = new MutableGazeSource(TakeoverSourceId, liveX, 0f);
                _profile = CreateProfileSO(LiveSource, outerYawAngle);
                Controller.CharacterSO = _profile;
                Controller.Initialize();

                Timeline = CreateTimeline();
                _bakeAsset = TimelineBakeService.Bake(Timeline, Controller.CurrentProfile.GetValueOrDefault());

                _directorObject = new GameObject("TimelineGazeTakeoverIntegrationTests_Director");
                Director = _directorObject.AddComponent<PlayableDirector>();
                Director.playableAsset = Timeline;
                Director.timeUpdateMode = DirectorUpdateMode.Manual;
                Director.extrapolationMode = DirectorWrapMode.None;
                Director.SetGenericBinding(Timeline.GetOutputTrack(0), Host);
                Director.Play();
                Director.playableGraph.Evaluate(0f);
            }

            public GameObject Host { get; }

            public Transform EyeBone { get; }

            public FacialController Controller { get; }

            public MutableGazeSource LiveSource { get; }

            public PlayableDirector Director { get; }

            public TimelineAsset Timeline { get; }

            public FacialTimelineReceiver Receiver { get; private set; }

            public TimelineGazeInputSource GazeSink { get; private set; }

            public TimelineAdapterBinding Binding { get; private set; }

            public void AttachManualReceiver()
            {
                EnsureReceiver();
                GazeSink = new TimelineGazeInputSource(InputSourceId.Parse("timeline:gaze-0"));
                Receiver.Configure(
                    Controller.CurrentProfile.GetValueOrDefault(),
                    Controller.InputSourceRegistry,
                    Array.Empty<(string layer, TimelineExpressionStateSink sink)>(),
                    Array.Empty<(string sub, TimelineBakedValueSink sink)>(),
                    Array.Empty<(string sub, TimelineAnalogInputSource sink)>(),
                    new[] { (GazeChannelSub, GazeSink, TakeoverSourceId) });
            }

            public void AttachBindingReceiver()
            {
                if (Binding != null)
                {
                    return;
                }

                Binding = new TimelineAdapterBinding();
                MutableChannelDefinitions(Binding).Add(new TimelineValueChannelConfig
                {
                    Sub = GazeChannelSub,
                    AxisCount = 2,
                    IsGaze = true,
                    TakeoverSourceId = TakeoverSourceId,
                });

                Binding.OnStart(CreateContext());
                Receiver = Binding.Receiver;
                Receiver.BakeAsset = _bakeAsset;
                Assert.That(Receiver.TryGetGazeSink(GazeChannelSub, out TimelineGazeInputSource sink), Is.True);
                GazeSink = sink;
            }

            public void EvaluateTimeline(float deltaTime)
            {
                Director.playableGraph.Evaluate(deltaTime);
            }

            public void DestroyGraph()
            {
                if (Director != null && Director.playableGraph.IsValid())
                {
                    Director.playableGraph.Destroy();
                }
            }

            public bool TryResolveTakeoverSource(out IInputSource source)
            {
                return Controller.InputSourceRegistry.TryResolve(TakeoverSourceId, out source);
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                Binding?.Dispose();

                if (Director != null && Director.playableGraph.IsValid())
                {
                    Director.playableGraph.Destroy();
                }

                if (Receiver != null)
                {
                    UnityEngine.Object.DestroyImmediate(Receiver);
                    Receiver = null;
                }

                if (_bakeAsset != null)
                {
                    UnityEngine.Object.DestroyImmediate(_bakeAsset);
                }

                if (Timeline != null)
                {
                    UnityEngine.Object.DestroyImmediate(Timeline);
                }

                if (_profile != null)
                {
                    UnityEngine.Object.DestroyImmediate(_profile);
                }

                if (_directorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(_directorObject);
                }

                if (Host != null)
                {
                    UnityEngine.Object.DestroyImmediate(Host);
                }
            }

            private void EnsureReceiver()
            {
                Receiver = Host.GetComponent<FacialTimelineReceiver>();
                if (Receiver == null)
                {
                    Receiver = Host.AddComponent<FacialTimelineReceiver>();
                }

                Receiver.BakeAsset = _bakeAsset;
            }

            private AdapterBuildContext CreateContext()
            {
                return new AdapterBuildContext(
                    Controller.CurrentProfile.GetValueOrDefault(),
                    Array.Empty<string>(),
                    Controller.InputSourceRegistry,
                    new FacialOutputBus(),
                    new FakeTimeProvider(),
                    Host,
                    lipSyncProvider: null);
            }
        }

        private sealed class FakeTimeProvider : ITimeProvider
        {
            public double UnscaledTimeSeconds => 0d;
        }

        private sealed class TestProfileSO : FacialCharacterProfileSO
        {
            public List<AdapterBindingBase> WritableAdapterBindings => _adapterBindings;

            public List<GazeBindingConfig> WritableGazeConfigs => _gazeConfigs;

            public override FacialProfile LoadProfile()
            {
                return new FacialProfile(
                    "1.0.0",
                    new[]
                    {
                        new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                    });
            }
        }

        [Serializable]
        private sealed class LiveGazeRegisteringBinding : AdapterBindingBase
        {
            [NonSerialized] public MutableGazeSource Source;

            public override void OnStart(in AdapterBuildContext ctx)
            {
                ctx.InputSourceRegistry.Register(AdapterSlug.Parse("live"), "gaze", Source);
            }
        }

        private class MutableGazeSource : IInputSource, IAnalogInputSource
        {
            private readonly float _x;
            private readonly float _y;
            private readonly BitArray _mask = new BitArray(0);

            public MutableGazeSource(string id, float x, float y)
            {
                Id = id;
                _x = x;
                _y = y;
            }

            public string Id { get; }

            public InputSourceType Type => InputSourceType.ValueProvider;

            public int BlendShapeCount => 0;

            public BitArray ContributeMask => _mask;

            public bool IsValid => true;

            public int AxisCount => 2;

            public void Tick(float deltaTime)
            {
            }

            public bool TryWriteValues(Span<float> output)
            {
                return false;
            }

            public bool TryReadScalar(out float value)
            {
                value = _x;
                return true;
            }

            public bool TryReadVector2(out float x, out float y)
            {
                x = _x;
                y = _y;
                return true;
            }

            public bool TryReadAxes(Span<float> output)
            {
                if (output.Length > 0)
                {
                    output[0] = _x;
                }

                if (output.Length > 1)
                {
                    output[1] = _y;
                }

                return true;
            }
        }

        private sealed class InjectedGazeSource : MutableGazeSource, IInjectedInputSource
        {
            public InjectedGazeSource(string id, float x, float y)
                : base(id, x, y)
            {
            }

            public IInputSource ReplacedSource => null;
        }

        private static GameObject CreateHost()
        {
            var host = new GameObject("TimelineGazeTakeoverIntegrationTests_Host");
            host.AddComponent<Animator>();
            var rendererObject = new GameObject("Renderer");
            rendererObject.transform.SetParent(host.transform, worldPositionStays: false);
            rendererObject.AddComponent<SkinnedMeshRenderer>();
            return host;
        }

        private static TestProfileSO CreateProfileSO(MutableGazeSource liveSource, float outerYawAngle)
        {
            var profile = ScriptableObject.CreateInstance<TestProfileSO>();
            profile.WritableAdapterBindings.Add(new LiveGazeRegisteringBinding
            {
                Slug = "live",
                Source = liveSource,
            });
            profile.WritableGazeConfigs.Add(new GazeBindingConfig
            {
                expressionId = "look",
                useDistinctLeftRight = true,
                sourceIdLeft = TakeoverSourceId,
                sourceIdRight = TakeoverSourceId,
                leftEyeBonePath = "LeftEye",
                outerYawAngle = outerYawAngle,
                innerYawAngle = outerYawAngle,
                lookUpAngle = 12f,
                lookDownAngle = 12f,
            });
            return profile;
        }

        private static TimelineAsset CreateTimeline()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            FacialValueTrack track = timeline.CreateTrack<FacialValueTrack>(null, "Gaze");
            track.ChannelSubId = GazeChannelSub;
            track.ChannelKind = FacialValueChannelKind.Gaze;

            TimelineClip clip = track.CreateClip<FacialValueClip>();
            clip.start = 0d;
            clip.duration = 1d;
            ((FacialValueClip)clip.asset).Axes = new[]
            {
                AnimationCurve.Constant(0f, 1f, 1f),
                AnimationCurve.Constant(0f, 1f, 0f),
            };

            return timeline;
        }

        private static List<TimelineValueChannelConfig> MutableChannelDefinitions(TimelineAdapterBinding binding)
        {
            FieldInfo field = typeof(TimelineAdapterBinding).GetField(
                "channelDefinitions",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            var value = field.GetValue(binding) as List<TimelineValueChannelConfig>;
            Assert.That(value, Is.Not.Null);
            return value;
        }
    }
}
