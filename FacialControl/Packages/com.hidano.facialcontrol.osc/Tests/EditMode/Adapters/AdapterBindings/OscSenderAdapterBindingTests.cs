using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.AdapterBindings
{
    [TestFixture]
    public sealed class OscSenderAdapterBindingTests
    {
        private const int PortBase = 19420;
        private static int s_portCounter;

        [Test]
        public void Type_HasSerializableAndFacialAdapterBindingAttributes()
        {
            Assert.That(Attribute.IsDefined(typeof(OscSenderAdapterBinding), typeof(SerializableAttribute)), Is.True);

            object[] attrs = typeof(OscSenderAdapterBinding)
                .GetCustomAttributes(typeof(FacialAdapterBindingAttribute), inherit: false);

            Assert.That(attrs.Length, Is.EqualTo(1));
            Assert.That(((FacialAdapterBindingAttribute)attrs[0]).DisplayName, Is.EqualTo("OSC Sender"));
        }

        [Test]
        public void Type_IsSealedAdapterBindingWithParameterlessConstructor()
        {
            Type type = typeof(OscSenderAdapterBinding);

            Assert.That(type.IsSealed, Is.True);
            Assert.That(typeof(AdapterBindingBase).IsAssignableFrom(type), Is.True);
            Assert.That(type.GetConstructor(Type.EmptyTypes), Is.Not.Null);
        }

        [Test]
        public void Ctor_HeartbeatIntervalSeconds_DefaultsToFiveSeconds()
        {
            var binding = new OscSenderAdapterBinding();

            Assert.That(
                binding.HeartbeatIntervalSeconds,
                Is.EqualTo(OscSenderAdapterBinding.DefaultHeartbeatIntervalSeconds));
        }

        [Test]
        public void Ctor_SuppressLoopback_DefaultsToTrue()
        {
            var binding = new OscSenderAdapterBinding();

            Assert.That(binding.SuppressLoopback, Is.True);
        }

        [Test]
        public void Ctor_SendPreset_DefaultsToTrue()
        {
            var binding = new OscSenderAdapterBinding();

            Assert.That(binding.SendPreset, Is.True);
        }

        [Test]
        public void Type_GazeExpressionIds_IsSerializableListField()
        {
            FieldInfo field = typeof(OscSenderAdapterBinding).GetField(
                "_gazeExpressionIds",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            Assert.That(field.FieldType, Is.EqualTo(typeof(List<string>)));
            Assert.That(field.GetCustomAttribute<SerializeField>(), Is.Not.Null);
        }

        [Test]
        public void Type_SendPreset_IsSerializableBooleanField()
        {
            FieldInfo field = typeof(OscSenderAdapterBinding).GetField(
                "_sendPreset",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            Assert.That(field.FieldType, Is.EqualTo(typeof(bool)));
            Assert.That(field.GetCustomAttribute<SerializeField>(), Is.Not.Null);
        }

        [Test]
        public void Type_Settings_IsSerializableSettingsField()
        {
            FieldInfo field = typeof(OscSenderAdapterBinding).GetField(
                "_settings",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            Assert.That(field.FieldType, Is.EqualTo(typeof(OscRuntimeSettingsSO)));
            Assert.That(field.GetCustomAttribute<SerializeField>(), Is.Not.Null);
        }

        [Test]
        public void OnStart_SettingsNull_LogsWarningAndSkipsStart()
        {
            // task 5.2 観測可能完了条件: _settings 未代入時に warning が出て binding 起動がスキップされる。
            var bus = new RecordingFacialOutputBus();
            var binding = new OscSenderAdapterBinding { Slug = "osc-sender-no-settings" };
            var host = new GameObject("OscSenderAdapterBindingSettingsNullTests");

            LogAssert.Expect(LogType.Warning, new Regex("_settings が未代入"));

            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile" }));

                Assert.That(binding.IsStarted, Is.False);
                Assert.That(binding.HelperSenderCount, Is.EqualTo(0));
                Assert.That(bus.Observer, Is.Null);
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_SettingsSenderDisabled_LogsWarningAndSkipsStart()
        {
            // task 5.2 観測可能完了条件補強: SenderEnabled=false の SO が割り当てられている場合も skip。
            var bus = new RecordingFacialOutputBus();
            var settings = ScriptableObject.CreateInstance<OscRuntimeSettingsSO>();
            settings.hideFlags = HideFlags.HideAndDontSave;
            settings.FromJson(
                "{\"senderEnabled\":false,\"endpoints\":[{\"endpoint\":\"127.0.0.1\",\"port\":19999,\"enabled\":true,\"preset\":0}]}");

            var binding = new OscSenderAdapterBinding
            {
                Slug = "osc-sender-disabled",
                Settings = settings,
            };

            var host = new GameObject("OscSenderAdapterBindingSenderDisabledTests");

            LogAssert.Expect(LogType.Warning, new Regex("SenderEnabled=false"));

            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile" }));

                Assert.That(binding.IsStarted, Is.False);
                Assert.That(binding.HelperSenderCount, Is.EqualTo(0));
                Assert.That(bus.Observer, Is.Null);
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void OnStart_SettingsAssigned_ConfiguresHostsFromSettingsEndpoints()
        {
            // task 5.2 観測可能完了条件: _settings 経由で Sender Host が configure される (EditMode 範囲)。
            // 実 UDP 検証は task 8.5 で追加する。
            var bus = new RecordingFacialOutputBus();
            int firstPort = AllocatePort();
            int secondPort = AllocatePort();
            var settings = ScriptableObject.CreateInstance<OscRuntimeSettingsSO>();
            settings.hideFlags = HideFlags.HideAndDontSave;
            settings.FromJson(
                "{\"senderEnabled\":true,\"endpoints\":["
                + "{\"endpoint\":\"127.0.0.1\",\"port\":" + firstPort + ",\"enabled\":true,\"preset\":0},"
                + "{\"endpoint\":\"127.0.0.1\",\"port\":" + secondPort + ",\"enabled\":true,\"preset\":0}"
                + "]}");

            var binding = new OscSenderAdapterBinding
            {
                Slug = "osc-sender-settings",
                Settings = settings,
            };

            var host = new GameObject("OscSenderAdapterBindingSettingsAppliedTests");
            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile" }));

                Assert.That(binding.IsStarted, Is.True);
                Assert.That(binding.HelperSenderCount, Is.EqualTo(2));
                Assert.That(binding.GetHelperSender(0).Port, Is.EqualTo(firstPort));
                Assert.That(binding.GetHelperSender(1).Port, Is.EqualTo(secondPort));
                Assert.That(bus.Observer, Is.SameAs(binding));
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void OnStart_HeartbeatIntervalBelowMinimum_ClampsAndWarns()
        {
            var bus = new RecordingFacialOutputBus();
            var binding = new OscSenderAdapterBinding
            {
                Slug = "osc-sender",
                HeartbeatIntervalSeconds = 0.1f
            };
            binding.Configure("127.0.0.1", AllocatePort());
            var host = new GameObject("OscSenderAdapterBindingHeartbeatClampTests");

            LogAssert.Expect(
                LogType.Warning,
                new Regex("heartbeatIntervalSeconds 0\\.1.*below 0\\.5.*clamped"));

            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile" }));

                Assert.That(
                    binding.HeartbeatIntervalSeconds,
                    Is.EqualTo(OscSenderAdapterBinding.MinHeartbeatIntervalSeconds));
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_ValidContext_AddsHostSubscribesAndGeneratesIdentity()
        {
            var bus = new RecordingFacialOutputBus();
            var binding = new OscSenderAdapterBinding { Slug = "osc-sender" };
            binding.Configure("127.0.0.1", AllocatePort());
            var host = new GameObject("OscSenderAdapterBindingTests");

            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile" }));

                Assert.That(binding.IsStarted, Is.True);
                Assert.That(binding.HelperSender, Is.Not.Null);
                Assert.That(binding.HelperSender.IsConfigured, Is.True);
                Assert.That(bus.Observer, Is.SameAs(binding));
                Assert.That(binding.Identity.Uuid, Is.Not.EqualTo(Guid.Empty));
                Assert.That(binding.Identity.StartedAtUnixMs, Is.GreaterThanOrEqualTo(0L));
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_MultipleEnabledEndpoints_AddsIndependentHosts()
        {
            var bus = new RecordingFacialOutputBus();
            var binding = new OscSenderAdapterBinding { Slug = "osc-sender" };
            int firstPort = AllocatePort();
            int secondPort = AllocatePort();
            binding.ConfigureEndpoints(new[]
            {
                new OscSenderEndpointConfig("127.0.0.1", firstPort),
                new OscSenderEndpointConfig("127.0.0.1", secondPort)
            });
            var host = new GameObject("OscSenderAdapterBindingMultiEndpointTests");

            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile" }));

                Assert.That(binding.IsStarted, Is.True);
                Assert.That(binding.HelperSenderCount, Is.EqualTo(2));
                Assert.That(binding.GetHelperSender(0).Port, Is.EqualTo(firstPort));
                Assert.That(binding.GetHelperSender(1).Port, Is.EqualTo(secondPort));
                Assert.That(binding.GetHelperSender(0), Is.Not.SameAs(binding.GetHelperSender(1)));
                Assert.That(host.GetComponents<uOSC.uOscClient>().Length, Is.EqualTo(2));
                Assert.That(bus.Observer, Is.SameAs(binding));
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_MixedPresetEndpoints_BuildsPresetAddressesAndReusesUtf8Cache()
        {
            var bus = new RecordingFacialOutputBus();
            var binding = new OscSenderAdapterBinding { Slug = "osc-sender" };
            binding.ConfigureEndpoints(new[]
            {
                new OscSenderEndpointConfig("127.0.0.1", AllocatePort(), preset: AddressPresetKind.VRChat),
                new OscSenderEndpointConfig("127.0.0.1", AllocatePort(), preset: AddressPresetKind.ARKit),
                new OscSenderEndpointConfig("127.0.0.1", AllocatePort(), preset: AddressPresetKind.ARKit)
            });
            var host = new GameObject("OscSenderAdapterBindingPresetAddressTests");

            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "eyeBlinkLeft" }));

                Assert.That(binding.IsStarted, Is.True);
                Assert.That(binding.HelperSenderCount, Is.EqualTo(3));

                string[] vrchatAddresses = GetPrivateField<string[]>(
                    binding.GetHelperSender(0),
                    "_oscAddresses");
                string[] arkitAddresses = GetPrivateField<string[]>(
                    binding.GetHelperSender(1),
                    "_oscAddresses");
                byte[][] vrchatBytes = GetPrivateField<byte[][]>(
                    binding.GetHelperSender(0),
                    "_oscAddressUtf8");
                byte[][] firstArKitBytes = GetPrivateField<byte[][]>(
                    binding.GetHelperSender(1),
                    "_oscAddressUtf8");
                byte[][] secondArKitBytes = GetPrivateField<byte[][]>(
                    binding.GetHelperSender(2),
                    "_oscAddressUtf8");

                Assert.That(vrchatAddresses[0], Is.EqualTo("/avatar/parameters/eyeBlinkLeft"));
                Assert.That(arkitAddresses[0], Is.EqualTo("/ARKit/eyeBlinkLeft"));
                Assert.That(Encoding.UTF8.GetString(vrchatBytes[0]), Is.EqualTo(vrchatAddresses[0]));
                Assert.That(Encoding.UTF8.GetString(firstArKitBytes[0]), Is.EqualTo(arkitAddresses[0]));
                Assert.That(firstArKitBytes[0], Is.SameAs(secondArKitBytes[0]));
                Assert.That(firstArKitBytes[0], Is.Not.SameAs(vrchatBytes[0]));
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_VRChatGazeExpressionIds_BuildsXAndYGazeAddresses()
        {
            var bus = new RecordingFacialOutputBus();
            var binding = new OscSenderAdapterBinding { Slug = "osc-sender" };
            binding.ConfigureEndpoints(new[]
            {
                new OscSenderEndpointConfig("127.0.0.1", AllocatePort(), preset: AddressPresetKind.VRChat)
            });
            binding.GazeExpressionIds.Add("eyeLook");
            var host = new GameObject("OscSenderAdapterBindingVrchatGazeAddressTests");

            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile" }));

                Assert.That(binding.IsStarted, Is.True);
                string[] addresses = GetPrivateField<string[]>(
                    binding.HelperSender,
                    "_oscAddresses");

                CollectionAssert.AreEqual(
                    new[]
                    {
                        "/avatar/parameters/smile",
                        "/avatar/parameters/eyeLookX",
                        "/avatar/parameters/eyeLookY"
                    },
                    addresses);
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_GazeExpressionIds_PrebuildsPresetSpecificAdvertisementPairsPerSlot()
        {
            var bus = new RecordingFacialOutputBus();
            var binding = new OscSenderAdapterBinding { Slug = "osc-sender" };
            binding.ConfigureEndpoints(new[]
            {
                new OscSenderEndpointConfig("127.0.0.1", AllocatePort(), preset: AddressPresetKind.VRChat),
                new OscSenderEndpointConfig("127.0.0.1", AllocatePort(), preset: AddressPresetKind.ARKit)
            });
            binding.GazeExpressionIds.Add("eyeLook");
            binding.GazeExpressionIds.Add("brow");
            var host = new GameObject("OscSenderAdapterBindingGazeAdvertisementPairTests");

            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile" }));

                IList slots = GetPrivateField<IList>(binding, "_sendSlots");
                object vrchatSlot = slots[0];
                object arkitSlot = slots[1];
                CollectionAssert.AreEqual(
                    new[] { "eyeLook", "VRChat_XY", "brow", "VRChat_XY" },
                    GetPrivateField<string[]>(vrchatSlot, "GazeAdvertisementPairs"));
                CollectionAssert.AreEqual(
                    new[] { "eyeLook", "ARKit_8BS", "brow", "ARKit_8BS" },
                    GetPrivateField<string[]>(arkitSlot, "GazeAdvertisementPairs"));
                Assert.That(GetPrivateField<int>(vrchatSlot, "GazeAdvertisementPairCount"), Is.EqualTo(2));
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_CustomPresetWithGaze_SkipsGazeAndContinuesBinding()
        {
            var bus = new RecordingFacialOutputBus();
            var binding = new OscSenderAdapterBinding { Slug = "osc-sender" };
            binding.ConfigureEndpoints(new[]
            {
                new OscSenderEndpointConfig(
                    "127.0.0.1",
                    AllocatePort(),
                    preset: AddressPresetKind.Custom),
                new OscSenderEndpointConfig(
                    "127.0.0.1",
                    AllocatePort(),
                    preset: AddressPresetKind.VRChat)
            });
            binding.BlendShapeNames.Add("smile");
            binding.GazeExpressionIds.Add("eyeLook");
            var host = new GameObject("OscSenderAdapterBindingCustomGazeTests");

            LogAssert.Expect(LogType.Warning, new Regex("Custom address preset"));
            LogAssert.Expect(LogType.Warning, new Regex("Custom preset.*gaze"));

            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile" }));

                Assert.That(binding.IsStarted, Is.True);
                Assert.That(binding.HelperSenderCount, Is.EqualTo(1));
                string[] addresses = GetPrivateField<string[]>(binding.HelperSender, "_oscAddresses");
                CollectionAssert.AreEqual(
                    new[]
                    {
                        "/avatar/parameters/smile",
                        "/avatar/parameters/eyeLookX",
                        "/avatar/parameters/eyeLookY"
                    },
                    addresses);
                Assert.That(bus.Observer, Is.SameAs(binding));
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_ARKitGazeExpressionIds_BuildsPerfectSyncEyeLookAddresses()
        {
            var bus = new RecordingFacialOutputBus();
            var binding = new OscSenderAdapterBinding { Slug = "osc-sender" };
            binding.ConfigureEndpoints(new[]
            {
                new OscSenderEndpointConfig("127.0.0.1", AllocatePort(), preset: AddressPresetKind.ARKit)
            });
            binding.GazeExpressionIds.Add("eyeLook");
            var host = new GameObject("OscSenderAdapterBindingArKitGazeAddressTests");

            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile" }));

                Assert.That(binding.IsStarted, Is.True);
                string[] addresses = GetPrivateField<string[]>(
                    binding.HelperSender,
                    "_oscAddresses");

                Assert.That(addresses.Length, Is.EqualTo(1 + PerfectSyncEyeLook.Count));
                Assert.That(addresses[0], Is.EqualTo("/ARKit/smile"));
                for (int i = 0; i < PerfectSyncEyeLook.Count; i++)
                {
                    Assert.That(
                        addresses[i + 1],
                        Is.EqualTo(PerfectSyncEyeLook.ArKitAddressPrefix + PerfectSyncEyeLook.Names[i]));
                }
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnFacialOutputPublished_VRChatGazeSnapshot_WritesXAndYToScratchFrame()
        {
            var bus = new RecordingFacialOutputBus();
            var binding = new OscSenderAdapterBinding { Slug = "osc-sender" };
            binding.ConfigureEndpoints(new[]
            {
                new OscSenderEndpointConfig("127.0.0.1", AllocatePort(), preset: AddressPresetKind.VRChat)
            });
            binding.GazeExpressionIds.Add("eyeLook");
            var host = new GameObject("OscSenderAdapterBindingVrchatGazeScratchTests");

            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile" }));

                bus.Publish(
                    new[] { 0.25f },
                    new[]
                    {
                        new GazeSnapshot("eyeLook", -0.4f, 0.6f),
                        new GazeSnapshot("ignored", 1f, 1f)
                    });

                IList slots = GetPrivateField<IList>(binding, "_sendSlots");
                object slot = slots[0];
                int count = GetPrivateField<int>(slot, "ScratchFloatCount");
                byte[][] addresses = GetPrivateField<byte[][]>(slot, "ScratchAddressUtf8");
                float[] values = GetPrivateField<float[]>(slot, "ScratchFloatValues");

                Assert.That(count, Is.EqualTo(3));
                Assert.That(Encoding.UTF8.GetString(addresses[0]), Is.EqualTo("/avatar/parameters/smile"));
                Assert.That(Encoding.UTF8.GetString(addresses[1]), Is.EqualTo("/avatar/parameters/eyeLookX"));
                Assert.That(Encoding.UTF8.GetString(addresses[2]), Is.EqualTo("/avatar/parameters/eyeLookY"));
                Assert.That(values[0], Is.EqualTo(0.25f).Within(0.0001f));
                Assert.That(values[1], Is.EqualTo(-0.4f).Within(0.0001f));
                Assert.That(values[2], Is.EqualTo(0.6f).Within(0.0001f));
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnFacialOutputPublished_ARKitGazeSnapshot_WritesPerfectSyncEyeLookToScratchFrame()
        {
            var bus = new RecordingFacialOutputBus();
            var binding = new OscSenderAdapterBinding { Slug = "osc-sender" };
            binding.ConfigureEndpoints(new[]
            {
                new OscSenderEndpointConfig("127.0.0.1", AllocatePort(), preset: AddressPresetKind.ARKit)
            });
            binding.GazeExpressionIds.Add("eyeLook");
            var host = new GameObject("OscSenderAdapterBindingArKitGazeScratchTests");

            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile" }));

                bus.Publish(
                    new[] { 0.25f },
                    new[]
                    {
                        new GazeSnapshot("eyeLook", -0.4f, 0.6f),
                        new GazeSnapshot("ignored", 1f, 1f)
                    });

                IList slots = GetPrivateField<IList>(binding, "_sendSlots");
                object slot = slots[0];
                int count = GetPrivateField<int>(slot, "ScratchFloatCount");
                byte[][] addresses = GetPrivateField<byte[][]>(slot, "ScratchAddressUtf8");
                float[] values = GetPrivateField<float[]>(slot, "ScratchFloatValues");

                Assert.That(count, Is.EqualTo(1 + PerfectSyncEyeLook.Count));
                Assert.That(Encoding.UTF8.GetString(addresses[0]), Is.EqualTo("/ARKit/smile"));
                Assert.That(values[0], Is.EqualTo(0.25f).Within(0.0001f));

                var expected = new float[PerfectSyncEyeLook.Count];
                PerfectSyncEyeLook.Compose(new Vector2(-0.4f, 0.6f), new Vector2(-0.4f, 0.6f), expected);
                for (int i = 0; i < PerfectSyncEyeLook.Count; i++)
                {
                    Assert.That(
                        Encoding.UTF8.GetString(addresses[i + 1]),
                        Is.EqualTo(PerfectSyncEyeLook.ArKitAddressPrefix + PerfectSyncEyeLook.Names[i]));
                    Assert.That(values[i + 1], Is.EqualTo(expected[i]).Within(0.0001f));
                }
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_DuplicateEndpoint_NormalizesToOneHostAndWarnsOnce()
        {
            var bus = new RecordingFacialOutputBus();
            var binding = new OscSenderAdapterBinding { Slug = "osc-sender" };
            int port = AllocatePort();
            binding.ConfigureEndpoints(new[]
            {
                new OscSenderEndpointConfig(" 127.0.0.1 ", port),
                new OscSenderEndpointConfig("127.0.0.1", port),
                new OscSenderEndpointConfig("127.0.0.1", AllocatePort())
            });
            var host = new GameObject("OscSenderAdapterBindingDuplicateEndpointTests");

            LogAssert.Expect(
                LogType.Warning,
                $"[OscSenderAdapterBinding] Duplicate endpoint '127.0.0.1:{port}' was normalized to one send slot.");

            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile" }));

                Assert.That(binding.IsStarted, Is.True);
                Assert.That(binding.HelperSenderCount, Is.EqualTo(2));
                Assert.That(binding.GetHelperSender(0).Port, Is.EqualTo(port));
                Assert.That(bus.Observer, Is.SameAs(binding));
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_SuppressLoopbackMatchingReceiverEndpoint_RemainsLiveWithoutSenderHost()
        {
            var bus = new RecordingFacialOutputBus();
            int port = AllocatePort();
            var receiver = new OscReceiverAdapterBinding
            {
                Slug = "osc-receiver",
                Endpoint = "127.0.0.1",
                Port = port
            };
            var binding = new OscSenderAdapterBinding { Slug = "osc-sender" };
            binding.Configure("127.0.0.1", port);
            var host = new GameObject("OscSenderAdapterBindingLoopbackSuppressionTests");

            LogAssert.Expect(
                LogType.Warning,
                $"[OscSenderAdapterBinding] Endpoint '127.0.0.1:{port}' matches an OSC receiver in the same child scope and was suppressed.");
            LogAssert.Expect(
                LogType.Warning,
                "[OscSenderAdapterBinding] All endpoints were suppressed by loopback policy. OSC Sender remains live without sending.");

            try
            {
                binding.OnStart(CreateContext(
                    bus,
                    host,
                    new[] { "smile" },
                    new AdapterBindingBase[] { receiver, binding }));

                Assert.That(binding.IsStarted, Is.True);
                Assert.That(binding.HelperSenderCount, Is.EqualTo(0));
                Assert.That(bus.Observer, Is.SameAs(binding));
                Assert.That(binding.LoopbackPolicy, Is.Not.Null);
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_SuppressLoopbackDisabledForMatchingReceiverEndpoint_StartsHost()
        {
            var bus = new RecordingFacialOutputBus();
            int port = AllocatePort();
            var receiver = new OscReceiverAdapterBinding
            {
                Slug = "osc-receiver",
                Endpoint = "127.0.0.1",
                Port = port
            };
            var binding = new OscSenderAdapterBinding
            {
                Slug = "osc-sender",
                SuppressLoopback = false
            };
            binding.Configure("127.0.0.1", port);
            var host = new GameObject("OscSenderAdapterBindingLoopbackDisabledTests");

            try
            {
                binding.OnStart(CreateContext(
                    bus,
                    host,
                    new[] { "smile" },
                    new AdapterBindingBase[] { receiver, binding }));

                Assert.That(binding.IsStarted, Is.True);
                Assert.That(binding.HelperSenderCount, Is.EqualTo(1));
                Assert.That(binding.HelperSender.Port, Is.EqualTo(port));
                Assert.That(bus.Observer, Is.SameAs(binding));
                Assert.That(binding.LoopbackPolicy, Is.Null);
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_NoEnabledEndpoints_WarnsAndDoesNotSubscribe()
        {
            var bus = new RecordingFacialOutputBus();
            var binding = new OscSenderAdapterBinding { Slug = "osc-sender" };
            binding.ConfigureEndpoints(new[]
            {
                new OscSenderEndpointConfig("127.0.0.1", AllocatePort(), enabled: false)
            });
            var host = new GameObject("OscSenderAdapterBindingNoEndpointTests");

            LogAssert.Expect(LogType.Warning, "[OscSenderAdapterBinding] No enabled endpoints. OSC Sender will not start.");

            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile" }));

                Assert.That(binding.IsStarted, Is.False);
                Assert.That(binding.HelperSenderCount, Is.EqualTo(0));
                Assert.That(bus.Observer, Is.Null);
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_InvalidSlug_WarnsAndDoesNotSubscribe()
        {
            var bus = new RecordingFacialOutputBus();
            var binding = new OscSenderAdapterBinding { Slug = string.Empty };
            binding.Configure("127.0.0.1", AllocatePort());
            var host = new GameObject("OscSenderAdapterBindingInvalidSlugTests");

            LogAssert.Expect(LogType.Warning, "[OscSenderAdapterBinding] Slug '' is invalid. OSC Sender will not start.");

            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile" }));

                Assert.That(binding.IsStarted, Is.False);
                Assert.That(binding.HelperSender, Is.Null);
                Assert.That(bus.Observer, Is.Null);
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Dispose_AfterStart_UnsubscribesAndResetsState()
        {
            var bus = new RecordingFacialOutputBus();
            var binding = new OscSenderAdapterBinding { Slug = "osc-sender" };
            binding.Configure("127.0.0.1", AllocatePort());
            var host = new GameObject("OscSenderAdapterBindingDisposeTests");

            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile" }));
                Assert.That(bus.Observer, Is.SameAs(binding));

                binding.Dispose();

                Assert.That(binding.IsStarted, Is.False);
                Assert.That(binding.HelperSender, Is.Null);
                Assert.That(bus.Observer, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static AdapterBuildContext CreateContext(
            IFacialOutputBus bus,
            GameObject host,
            IReadOnlyList<string> blendShapeNames,
            IReadOnlyList<AdapterBindingBase> adapterBindings = null)
        {
            return new AdapterBuildContext(
                new FacialProfile("2.0.0"),
                blendShapeNames,
                new InputSourceRegistry(),
                bus,
                new ManualTimeProvider(),
                host,
                lipSyncProvider: null,
                activeExpressionProvider: null,
                adapterBindings: adapterBindings);
        }

        private static int AllocatePort()
        {
            return PortBase + System.Threading.Interlocked.Increment(ref s_portCounter);
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(target);
        }

        private sealed class RecordingFacialOutputBus : IFacialOutputBus
        {
            public IFacialOutputObserver Observer { get; private set; }

            public bool HasObservers => Observer != null;

            public void Subscribe(IFacialOutputObserver observer)
            {
                Observer = observer;
            }

            public void Unsubscribe(IFacialOutputObserver observer)
            {
                if (ReferenceEquals(Observer, observer))
                {
                    Observer = null;
                }
            }

            public void Publish(
                ReadOnlySpan<float> postBlendValues,
                ReadOnlySpan<GazeSnapshot> gazeSnapshots)
            {
                Observer?.OnFacialOutputPublished(postBlendValues, gazeSnapshots);
            }
        }
    }
}
