using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace Hidano.FacialControl.Tests.PlayMode.Performance
{
    [TestFixture]
    public sealed class OscReceiverGCAllocationTests
    {
        private const int FrameCount = 100;
        private const int PortBase = 19700;
        private const string Endpoint = "127.0.0.1";
        private const string Slug = "osc-receiver-gc";

        private static int s_portCounter;

        private GameObject _host;
        private OscReceiverAdapterBinding _binding;

        [TearDown]
        public void TearDown()
        {
            if (_binding != null)
            {
                _binding.Dispose();
                _binding = null;
            }

            if (_host != null)
            {
                _host.SetActive(false);
                UnityEngine.Object.DestroyImmediate(_host);
                _host = null;
            }
        }

        [Test]
        public void OnFixedTick_IndividualBlendShapeMessages100Frames_RecordsBaseline()
        {
            var registry = new InputSourceRegistry();
            var timeProvider = new ManualTimeProvider();
            StartReceiver(registry, timeProvider, BundleInterpretationMode.IndividualMessage, includeGaze: false);

            uOSC.Message[] messages =
            {
                new uOSC.Message("/avatar/parameters/smile", 0.25f),
                new uOSC.Message("/avatar/parameters/frown", 0.75f),
            };

            WarmUp(messages, timeProvider);

            BaselineResult baseline = MeasureFrames(() =>
            {
                HandleMessages(messages);
                _binding.OnFixedTick(1f / 60f);
            });

            Assert.That(registry.TryResolve(Slug, out IInputSource source), Is.True);
            var output = new float[2];
            Assert.That(source.TryWriteValues(output), Is.True);
            Assert.That(output[0], Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(output[1], Is.EqualTo(0.75f).Within(1e-6f));
            LogBaseline(nameof(OscReceiverGCAllocationTests), "individualBlendShapeMessages", baseline);
        }

        [Test]
        public void OnFixedTick_AtomicBundleGazeMessages100Frames_RecordsBaseline()
        {
            var registry = new InputSourceRegistry();
            var timeProvider = new ManualTimeProvider();
            StartReceiver(registry, timeProvider, BundleInterpretationMode.AtomicSwap, includeGaze: true);

            uOSC.Message[] messages =
            {
                FloatMessage("/avatar/parameters/smile", 0.25f, timestamp: 100UL),
                FloatMessage("/avatar/parameters/eyeX", -0.4f, timestamp: 100UL),
                FloatMessage("/avatar/parameters/eyeY", 0.6f, timestamp: 100UL),
            };

            WarmUp(messages, timeProvider);

            BaselineResult baseline = MeasureFrames(() =>
            {
                HandleMessages(messages);
                timeProvider.UnscaledTimeSeconds += 0.001d;
                _binding.OnFixedTick(1f / 60f);
            });

            GazeVector2InputSource gaze = ResolveGaze(registry, Slug + ":eye");
            Assert.That(gaze.TryReadVector2(out float x, out float y), Is.True);
            Assert.That(x, Is.EqualTo(-0.4f).Within(1e-6f));
            Assert.That(y, Is.EqualTo(0.6f).Within(1e-6f));
            LogBaseline(nameof(OscReceiverGCAllocationTests), "atomicBundleGazeMessages", baseline);
        }

        [Test]
        public void OnFixedTick_HeartbeatHashUnchanged100Frames_ZeroGCAllocation()
        {
            var registry = new InputSourceRegistry();
            var timeProvider = new ManualTimeProvider();
            StartReceiverForAutoMapping(registry, timeProvider, "smile", "frown");

            uOSC.Message heartbeatMessage = HeartbeatMessage("smile", "frown");
            _binding.HelperHost.Receiver.HandleOscMessage(heartbeatMessage);
            _binding.OnFixedTick(1f / 60f);

            OscInputSource inputSource = _binding.InputSource;
            OscDoubleBuffer buffer = _binding.Buffer;
            uint heartbeatHash = _binding.LastHeartbeatHash;

            Assert.That(inputSource, Is.Not.Null);
            Assert.That(registry.TryResolve(Slug, out IInputSource source), Is.True);
            Assert.That(source, Is.SameAs(inputSource));

            for (int i = 0; i < 16; i++)
            {
                _binding.HelperHost.Receiver.HandleOscMessage(heartbeatMessage);
                _binding.OnFixedTick(1f / 60f);
            }

            using var recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC.Alloc",
                1,
                ProfilerRecorderOptions.SumAllSamplesInFrame
                    | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);

            for (int frame = 0; frame < FrameCount; frame++)
            {
                _binding.HelperHost.Receiver.HandleOscMessage(heartbeatMessage);
                _binding.OnFixedTick(1f / 60f);
            }

            long gcAllocBytes = recorder.LastValue;

            Assert.That(_binding.InputSource, Is.SameAs(inputSource));
            Assert.That(_binding.Buffer, Is.SameAs(buffer));
            Assert.That(_binding.LastHeartbeatHash, Is.EqualTo(heartbeatHash));
            Assert.That(_binding.RuntimeMappings.Count, Is.EqualTo(2));
            Assert.That(gcAllocBytes, Is.EqualTo(0L),
                "heartbeat hash unchanged OnFixedTick hot path reported GC.Alloc: " + gcAllocBytes + " bytes.");
        }

        [Test]
        public void GazeAdvertisement_ContentUnchanged_ZeroAllocPerFrame()
        {
            var registry = new InputSourceRegistry();
            var timeProvider = new ManualTimeProvider();
            StartReceiverForAutoMapping(registry, timeProvider, "smile");

            var advertisement = new uOSC.Message(
                OscReceiverAdapterBinding.GazeAdvertisementAddress,
                "eye",
                GazeAdvertisementResolver.VrChatXyFormat);
            _binding.HelperHost.Receiver.HandleOscMessage(advertisement);
            _binding.OnFixedTick(1f / 60f);

            Assert.That(_binding.HasAutoGazeRoutes, Is.True);
            StabilizeManagedHeap();
            using var recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC.Alloc",
                1,
                ProfilerRecorderOptions.SumAllSamplesInFrame
                    | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);

            for (int frame = 0; frame < FrameCount; frame++)
            {
                _binding.OnFixedTick(1f / 60f);
            }

            Assert.That(recorder.LastValue, Is.EqualTo(0L),
                "unchanged gaze advertisement OnFixedTick hot path reported GC.Alloc: "
                + recorder.LastValue + " bytes.");
        }

        [Test]
        public void GazeVector2InputSource_ReadAfterAutoCreation_ZeroAlloc()
        {
            var registry = new InputSourceRegistry();
            var timeProvider = new ManualTimeProvider();
            StartReceiverForAutoMapping(registry, timeProvider, "smile");

            _binding.HelperHost.Receiver.HandleOscMessage(new uOSC.Message(
                OscReceiverAdapterBinding.GazeAdvertisementAddress,
                "eye",
                GazeAdvertisementResolver.VrChatXyFormat));
            _binding.OnFixedTick(1f / 60f);
            GazeVector2InputSource source = ResolveGaze(registry, Slug + ":eye");
            source.Publish(0.25f, -0.5f);

            for (int i = 0; i < 16; i++)
            {
                Assert.That(source.TryReadVector2(out _, out _), Is.True);
            }

            StabilizeManagedHeap();
            using var recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC.Alloc",
                1,
                ProfilerRecorderOptions.SumAllSamplesInFrame
                    | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);

            for (int read = 0; read < FrameCount; read++)
            {
                Assert.That(source.TryReadVector2(out float x, out float y), Is.True);
                Assert.That(x, Is.EqualTo(0.25f).Within(1e-6f));
                Assert.That(y, Is.EqualTo(-0.5f).Within(1e-6f));
            }

            Assert.That(recorder.LastValue, Is.EqualTo(0L),
                "auto-created gaze source read reported GC.Alloc: " + recorder.LastValue + " bytes.");
        }

        private void StartReceiver(
            InputSourceRegistry registry,
            ManualTimeProvider timeProvider,
            BundleInterpretationMode bundleMode,
            bool includeGaze)
        {
            _host = new GameObject("OscReceiverGCAllocationTests");
            _binding = new OscReceiverAdapterBinding
            {
                Slug = Slug,
                Endpoint = Endpoint,
                Port = AllocatePort(),
                StalenessSeconds = 0f,
                BundleMode = bundleMode,
                BundleAccumulationTimeoutMs = 0f,
            };

            if (includeGaze)
            {
                _binding.Mappings = new List<OscMappingEntry>
                {
                    new OscMappingEntry
                    {
                        mode = OscMappingMode.Gaze_VRChat_XY,
                        expressionId = "eye",
                        addressPattern = "/avatar/parameters/eye",
                    },
                };
            }

            _binding.Configure(Endpoint, _binding.Port, new[]
            {
                new OscMapping("/avatar/parameters/smile", "smile", "emotion"),
                new OscMapping("/avatar/parameters/frown", "frown", "emotion"),
            });
            _binding.OnStart(CreateContext(registry, timeProvider));

            Assert.That(_binding.IsStarted, Is.True);
            Assert.That(_binding.HelperHost, Is.Not.Null);
        }

        private void StartReceiverForAutoMapping(
            InputSourceRegistry registry,
            ManualTimeProvider timeProvider,
            params string[] blendShapeNames)
        {
            _host = new GameObject("OscReceiverGCAllocationTests");
            _binding = new OscReceiverAdapterBinding
            {
                Slug = Slug,
                Endpoint = Endpoint,
                Port = AllocatePort(),
                StalenessSeconds = 0f,
                BundleMode = BundleInterpretationMode.IndividualMessage,
                Mappings = new List<OscMappingEntry>(),
            };

            _binding.OnStart(CreateContext(registry, timeProvider, blendShapeNames));

            Assert.That(_binding.IsStarted, Is.True);
            Assert.That(_binding.HelperHost, Is.Not.Null);
            Assert.That(_binding.InputSource, Is.Null);
        }

        private AdapterBuildContext CreateContext(
            InputSourceRegistry registry,
            ManualTimeProvider timeProvider,
            string[] blendShapeNames = null)
        {
            return new AdapterBuildContext(
                new FacialProfile("2.0.0"),
                blendShapeNames ?? Array.Empty<string>(),
                registry,
                new FacialOutputBus(),
                timeProvider,
                _host,
                lipSyncProvider: null);
        }

        private void WarmUp(uOSC.Message[] messages, ManualTimeProvider timeProvider)
        {
            for (int i = 0; i < 4; i++)
            {
                HandleMessages(messages);
                timeProvider.UnscaledTimeSeconds += 0.001d;
                _binding.OnFixedTick(1f / 60f);
            }
        }

        private void HandleMessages(uOSC.Message[] messages)
        {
            for (int i = 0; i < messages.Length; i++)
            {
                _binding.HelperHost.Receiver.HandleOscMessage(messages[i]);
            }
        }

        private static uOSC.Message FloatMessage(string address, float value, ulong timestamp)
        {
            var message = new uOSC.Message(address, value);
            message.timestamp = new uOSC.Timestamp(timestamp);
            return message;
        }

        private static uOSC.Message HeartbeatMessage(params string[] names)
        {
            var values = new object[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                values[i] = names[i];
            }

            return new uOSC.Message(OscReceiverAdapterBinding.BlendShapeNamesAddress, values);
        }

        private static GazeVector2InputSource ResolveGaze(InputSourceRegistry registry, string id)
        {
            Assert.That(registry.TryResolve(id, out IInputSource source), Is.True);
            Assert.That(source, Is.InstanceOf<GazeVector2InputSource>());
            return (GazeVector2InputSource)source;
        }

        private static BaselineResult MeasureFrames(Action measureFrame)
        {
            StabilizeManagedHeap();

            long managedBefore = GC.GetTotalMemory(forceFullCollection: false);
            long profilerBefore = Profiler.GetTotalAllocatedMemoryLong();
            for (int frame = 0; frame < FrameCount; frame++)
            {
                measureFrame();
            }

            long profilerAfter = Profiler.GetTotalAllocatedMemoryLong();
            long managedAfter = GC.GetTotalMemory(forceFullCollection: false);
            return new BaselineResult(
                FrameCount,
                profilerAfter - profilerBefore,
                managedAfter - managedBefore);
        }

        private static void LogBaseline(string fixture, string scenario, BaselineResult baseline)
        {
            string message =
                $"[{fixture}] preview.2 GC baseline scenario={scenario}, frames={baseline.FrameCount}, " +
                $"profilerAllocatedDeltaBytes={baseline.ProfilerAllocatedDeltaBytes}, " +
                $"managedHeapDeltaBytes={baseline.ManagedHeapDeltaBytes}";
            TestContext.Out.WriteLine(message);
            Debug.Log(message);
        }

        private static void StabilizeManagedHeap()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static int AllocatePort()
        {
            return PortBase + System.Threading.Interlocked.Increment(ref s_portCounter);
        }

        private readonly struct BaselineResult
        {
            public readonly int FrameCount;
            public readonly long ProfilerAllocatedDeltaBytes;
            public readonly long ManagedHeapDeltaBytes;

            public BaselineResult(
                int frameCount,
                long profilerAllocatedDeltaBytes,
                long managedHeapDeltaBytes)
            {
                FrameCount = frameCount;
                ProfilerAllocatedDeltaBytes = profilerAllocatedDeltaBytes;
                ManagedHeapDeltaBytes = managedHeapDeltaBytes;
            }
        }
    }
}
