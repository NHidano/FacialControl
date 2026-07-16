using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.InputSources;
using UnityEngine;

namespace Hidano.FacialControl.Timeline.Adapters.AdapterBindings
{
    [Serializable]
    [FacialAdapterBinding(displayName: "Timeline")]
    public sealed class TimelineAdapterBinding : AdapterBindingBase
    {
        private const string DefaultSlug = "timeline";
        private const int DefaultMaxStackDepth = 16;

        [SerializeField] private List<string> targetLayerNames = new List<string>();
        [SerializeField] private List<TimelineValueChannelConfig> channelDefinitions = new List<TimelineValueChannelConfig>();

        [NonSerialized] private FacialTimelineReceiver _receiver;

        public TimelineAdapterBinding()
        {
            Slug = DefaultSlug;
        }

        public IReadOnlyList<string> TargetLayerNames => targetLayerNames;

        public IReadOnlyList<TimelineValueChannelConfig> ChannelDefinitions => channelDefinitions;

        public FacialTimelineReceiver Receiver => _receiver;

        public override void OnStart(in AdapterBuildContext ctx)
        {
            string slugText = string.IsNullOrWhiteSpace(Slug) ? DefaultSlug : Slug;
            if (!AdapterSlug.TryParse(slugText, out AdapterSlug slug))
            {
                Debug.LogError(
                    $"[TimelineAdapterBinding] Slug '{slugText}' is invalid. Timeline sinks were not registered.");
                return;
            }

            Slug = slug.Value;

            var expressionSinks = new List<(string layer, TimelineExpressionStateSink sink)>();
            var analogSinks = new List<(string sub, TimelineAnalogInputSource sink)>();
            var gazeSinks = new List<(string sub, TimelineGazeInputSource sink, string takeoverSourceId)>();
            var seenLayers = new HashSet<string>(StringComparer.Ordinal);
            var seenChannels = new HashSet<string>(StringComparer.Ordinal);
            int gazeDiagnosticIndex = 0;

            if (targetLayerNames != null)
            {
                for (int i = 0; i < targetLayerNames.Count; i++)
                {
                    string layerName = targetLayerNames[i];
                    if (string.IsNullOrWhiteSpace(layerName) || !seenLayers.Add(layerName))
                    {
                        continue;
                    }

                    LayerDefinition? layer = ctx.Profile.FindLayerByName(layerName);
                    if (!layer.HasValue)
                    {
                        Debug.LogWarning(
                            $"[TimelineAdapterBinding] Layer '{layerName}' was not found in profile. The state sink is skipped.");
                        continue;
                    }

                    var sink = new TimelineExpressionStateSink(
                        InputSourceId.Parse(slug.Value + ":" + layerName),
                        DefaultMaxStackDepth,
                        layer.Value.ExclusionMode,
                        ctx.Profile);

                    ctx.InputSourceRegistry.Register(slug, layerName, sink);
                    expressionSinks.Add((layerName, sink));
                }
            }

            if (channelDefinitions != null)
            {
                for (int i = 0; i < channelDefinitions.Count; i++)
                {
                    TimelineValueChannelConfig channel = channelDefinitions[i];
                    if (channel == null || string.IsNullOrWhiteSpace(channel.Sub) || !seenChannels.Add(channel.Sub))
                    {
                        continue;
                    }

                    if (channel.IsGaze)
                    {
                        string diagnosticSub = "gaze-" + gazeDiagnosticIndex++;
                        var gazeSink = new TimelineGazeInputSource(
                            InputSourceId.Parse(slug.Value + ":" + diagnosticSub));
                        ctx.InputSourceRegistry.Register(slug, diagnosticSub, gazeSink);
                        gazeSinks.Add((channel.Sub, gazeSink, channel.TakeoverSourceId));
                        continue;
                    }

                    if (channel.AxisCount <= 0)
                    {
                        Debug.LogWarning(
                            $"[TimelineAdapterBinding] Channel '{channel.Sub}' has AxisCount={channel.AxisCount}. The analog sink is skipped.");
                        continue;
                    }

                    var analogSink = new TimelineAnalogInputSource(
                        InputSourceId.Parse(slug.Value + ":" + channel.Sub),
                        channel.AxisCount);
                    ctx.InputSourceRegistry.Register(slug, channel.Sub, analogSink);
                    analogSinks.Add((channel.Sub, analogSink));
                }
            }

            _receiver = ctx.HostGameObject.GetComponent<FacialTimelineReceiver>();
            if (_receiver == null)
            {
                _receiver = ctx.HostGameObject.AddComponent<FacialTimelineReceiver>();
            }

            _receiver.Configure(
                ctx.Profile,
                ctx.InputSourceRegistry,
                expressionSinks,
                Array.Empty<(string sub, TimelineBakedValueSink sink)>(),
                analogSinks,
                gazeSinks);
        }

        public override void Dispose()
        {
            if (_receiver == null)
            {
                return;
            }

            _receiver.ReleaseAll();

            if (UnityEngine.Application.isPlaying)
            {
                UnityEngine.Object.Destroy(_receiver);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(_receiver);
            }

            _receiver = null;
        }
    }

    [Serializable]
    public sealed class TimelineValueChannelConfig
    {
        [SerializeField] private string sub = string.Empty;
        [SerializeField] private int axisCount = 1;
        [SerializeField] private bool isGaze;
        [SerializeField] private string takeoverSourceId = string.Empty;

        public string Sub
        {
            get => sub;
            set => sub = value ?? string.Empty;
        }

        public int AxisCount
        {
            get => axisCount;
            set => axisCount = value;
        }

        public bool IsGaze
        {
            get => isGaze;
            set => isGaze = value;
        }

        public string TakeoverSourceId
        {
            get => takeoverSourceId;
            set => takeoverSourceId = value ?? string.Empty;
        }
    }
}
