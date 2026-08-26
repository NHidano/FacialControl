using System.Collections;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Tests.PlayMode.Integration
{
    [TestFixture]
    public sealed class OscHeartbeatConsistencyTests
    {
        private const string Endpoint = "127.0.0.1";
        private const int PortBase = 19680;
        private const string Slug = "osc-heartbeat-consistency";
        private const string Smile = "smile";
        private const string Blink = "blink";
        private const string SenderOnly = "sender-only";

        private static int s_portCounter;

        private GameObject _host;
        private OscReceiverAdapterBinding _binding;

        [TearDown]
        public void TearDown()
        {
            _binding?.Dispose();
            _binding = null;

            if (_host != null)
            {
                _host.SetActive(false);
                UnityEngine.Object.DestroyImmediate(_host);
                _host = null;
            }
        }

        [Test]
        public void OnFixedTick_HeartbeatMissingReceiverBlendShape_LogsMismatchWarning()
        {
            var registry = new InputSourceRegistry();
            var time = new ManualTimeProvider { UnscaledTimeSeconds = 0d };
            _host = new GameObject("OscHeartbeatConsistencyTests");
            _binding = CreateReceiver();
            _binding.OnStart(CreateContext(registry, time));

            Regex mismatchLog = MismatchLog(SenderOnly, Blink);
            LogAssert.Expect(LogType.Warning, mismatchLog);

            HandleHeartbeat(Smile, SenderOnly);
            HandleFloat(Smile, 0.65f);
            HandleFloat(Blink, 0.95f);
            _binding.OnFixedTick(0.02f);

            Assert.That(_binding.HeartbeatChecker.HasMismatch, Is.True);
            CollectionAssert.AreEqual(new[] { SenderOnly }, _binding.HeartbeatChecker.SenderOnlyNames);
            CollectionAssert.AreEqual(new[] { Blink }, _binding.HeartbeatChecker.ReceiverOnlyNames);
            AssertMask(_binding.InputSource.ContributeMask, true, true);
            AssertBlendShapes(0.65f, 0.95f);

            HandleHeartbeat(Smile, SenderOnly);
            _binding.OnFixedTick(0.02f);
            LogAssert.NoUnexpectedReceived();
        }

        private static OscReceiverAdapterBinding CreateReceiver()
        {
            var binding = new OscReceiverAdapterBinding
            {
                Slug = Slug,
                Port = AllocatePort(),
                StalenessSeconds = 0f,
                BundleMode = BundleInterpretationMode.IndividualMessage
            };
            binding.Configure(
                Endpoint,
                binding.Port,
                new[]
                {
                    new OscMapping(
                        OscAddressFormatter.VRChatParameterPrefix + Smile,
                        Smile,
                        string.Empty),
                    new OscMapping(
                        OscAddressFormatter.VRChatParameterPrefix + Blink,
                        Blink,
                        string.Empty)
                });
            return binding;
        }

        private AdapterBuildContext CreateContext(
            InputSourceRegistry registry,
            ManualTimeProvider timeProvider)
        {
            return new AdapterBuildContext(
                new FacialProfile("2.0.0"),
                new[] { Smile, Blink },
                registry,
                new FacialOutputBus(),
                timeProvider,
                _host,
                lipSyncProvider: null);
        }

        private void HandleHeartbeat(params string[] names)
        {
            var values = new object[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                values[i] = names[i];
            }

            _binding.HelperHost.Receiver.HandleOscMessage(
                new uOSC.Message(OscReceiverAdapterBinding.BlendShapeNamesAddress, values));
        }

        private void HandleFloat(string blendShapeName, float value)
        {
            _binding.HelperHost.Receiver.HandleOscMessage(
                new uOSC.Message(
                    OscAddressFormatter.VRChatParameterPrefix + blendShapeName,
                    value));
        }

        private void AssertBlendShapes(params float[] expected)
        {
            var values = new float[expected.Length];
            Assert.That(_binding.InputSource, Is.Not.Null);
            Assert.That(_binding.InputSource.TryWriteValues(values), Is.True);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(values[i], Is.EqualTo(expected[i]).Within(1e-6f), $"values[{i}]");
            }
        }

        private static void AssertMask(BitArray mask, params bool[] expected)
        {
            Assert.That(mask.Length, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(mask[i], Is.EqualTo(expected[i]), $"mask[{i}]");
            }
        }

        private static Regex MismatchLog(string senderOnlyName, string receiverOnlyName)
        {
            string pattern = "HeartbeatConsistencyChecker mismatch:"
                + ".*senderOnly=.*" + Regex.Escape(senderOnlyName)
                + ".*receiverOnly=.*" + Regex.Escape(receiverOnlyName);
            return new Regex(pattern);
        }

        private static int AllocatePort()
        {
            return PortBase + System.Threading.Interlocked.Increment(ref s_portCounter);
        }
    }
}
