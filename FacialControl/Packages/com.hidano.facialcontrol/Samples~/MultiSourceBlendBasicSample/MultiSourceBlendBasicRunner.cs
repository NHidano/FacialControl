using System;
using System.Collections.Generic;
using System.Text;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

using AdapterInputSourceRegistry = Hidano.FacialControl.Adapters.InputSources.InputSourceRegistry;
using DomainLayerInputSourceRegistry = Hidano.FacialControl.Domain.Services.LayerInputSourceRegistry;

namespace Hidano.FacialControl.Samples.MultiSourceBlendBasicSample
{
    public static class MultiSourceBlendBasicRunner
    {
        private static readonly string[] BlendShapeNames =
        {
            "Blink",
            "Smile",
            "MouthOpen",
        };

#if UNITY_EDITOR
        [MenuItem("Tools/FacialControl/Run MultiSourceBlend Basic Sample")]
        private static void RunFromMenu()
        {
            Run();
        }
#endif

        public static void Run()
        {
            var profile = CreateProfile();
            var registry = new AdapterInputSourceRegistry();
            var host = new GameObject("MultiSourceBlendBasicSampleHost");

            try
            {
                var context = new AdapterBuildContext(
                    profile,
                    BlendShapeNames,
                    registry,
                    new FacialOutputBus(),
                    new SampleTimeProvider(),
                    host,
                    null);

                AdapterBindingBase[] bindings =
                {
                    new MockTriggerAdapterBinding
                    {
                        Slug = "mock-trigger",
                        Blink = 1.0f,
                        Smile = 0.2f,
                        MouthOpen = 0.0f,
                    },
                    new MockAnalogAdapterBinding
                    {
                        Slug = "mock-analog",
                        Scale = 1.0f,
                        Blink = 0.0f,
                        Smile = 0.7f,
                        MouthOpen = 0.9f,
                    },
                };

                for (int i = 0; i < bindings.Length; i++)
                {
                    bindings[i].OnStart(in context);
                }

                var sourceBindings = ResolveLayerBindingsFromProfile(profile, registry);
                using var layerRegistry = new DomainLayerInputSourceRegistry(
                    profile,
                    BlendShapeNames.Length,
                    sourceBindings);
                using var weightBuffer = new LayerInputSourceWeightBuffer(
                    layerRegistry.LayerCount,
                    layerRegistry.MaxSourcesPerLayer);

                ApplyDeclaredWeights(profile, weightBuffer);

                var aggregator = new LayerInputSourceAggregator(
                    layerRegistry,
                    weightBuffer,
                    BlendShapeNames.Length);
                Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];

                aggregator.Aggregate(0.016f, outputPerLayer);

                Debug.Log(BuildResultMessage(registry, outputPerLayer[0].BlendShapeValues.Span));
            }
            finally
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(host);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(host);
                }
            }
        }

        private static FacialProfile CreateProfile()
        {
            var layers = new[]
            {
                new LayerDefinition("face", priority: 0, ExclusionMode.Blend),
            };
            var layerInputSources = new[]
            {
                new[]
                {
                    new InputSourceDeclaration("mock-trigger", 0.5f, null),
                    new InputSourceDeclaration("mock-analog", 0.75f, null),
                },
            };

            return new FacialProfile(
                "2.0",
                layers,
                expressions: null,
                rendererPaths: null,
                layerInputSources: layerInputSources);
        }

        private static List<(int layerIdx, int sourceIdx, IInputSource source)> ResolveLayerBindingsFromProfile(
            FacialProfile profile,
            AdapterInputSourceRegistry registry)
        {
            var result = new List<(int layerIdx, int sourceIdx, IInputSource source)>();
            var layers = profile.LayerInputSources.Span;

            for (int layerIdx = 0; layerIdx < layers.Length; layerIdx++)
            {
                var declarations = layers[layerIdx];
                for (int sourceIdx = 0; sourceIdx < declarations.Length; sourceIdx++)
                {
                    if (registry.TryResolve(declarations[sourceIdx].Id, out var source))
                    {
                        result.Add((layerIdx, sourceIdx, source));
                    }
                }
            }

            return result;
        }

        private static void ApplyDeclaredWeights(
            FacialProfile profile,
            LayerInputSourceWeightBuffer weightBuffer)
        {
            var layers = profile.LayerInputSources.Span;
            for (int layerIdx = 0; layerIdx < layers.Length; layerIdx++)
            {
                var declarations = layers[layerIdx];
                for (int sourceIdx = 0; sourceIdx < declarations.Length; sourceIdx++)
                {
                    weightBuffer.SetWeight(layerIdx, sourceIdx, declarations[sourceIdx].Weight);
                }
            }
        }

        private static string BuildResultMessage(
            AdapterInputSourceRegistry registry,
            ReadOnlySpan<float> values)
        {
            var sb = new StringBuilder(256);
            sb.Append("[FacialControl] MultiSourceBlendBasicSample");
            AppendDiscoveredBindings(sb);
            sb.Append(" registered=[");
            AppendCsv(sb, registry.RegisteredIds);
            sb.Append("] output={");

            for (int i = 0; i < BlendShapeNames.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(BlendShapeNames[i]);
                sb.Append('=');
                sb.Append(values[i].ToString("0.###"));
            }

            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendCsv(StringBuilder sb, IReadOnlyList<string> values)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(values[i]);
            }
        }

        private static void AppendDiscoveredBindings(StringBuilder sb)
        {
#if UNITY_EDITOR
            bool hasTrigger = false;
            bool hasAnalog = false;
            var discovered = TypeCache.GetTypesWithAttribute<FacialAdapterBindingAttribute>();
            for (int i = 0; i < discovered.Count; i++)
            {
                if (discovered[i] == typeof(MockTriggerAdapterBinding))
                {
                    hasTrigger = true;
                }
                else if (discovered[i] == typeof(MockAnalogAdapterBinding))
                {
                    hasAnalog = true;
                }
            }

            sb.Append(" discovered=[Mock Trigger:");
            sb.Append(hasTrigger ? "yes" : "no");
            sb.Append(", Mock Analog:");
            sb.Append(hasAnalog ? "yes" : "no");
            sb.Append(']');
#endif
        }

        private sealed class SampleTimeProvider : ITimeProvider
        {
            public double UnscaledTimeSeconds => Time.unscaledTimeAsDouble;
        }
    }
}
