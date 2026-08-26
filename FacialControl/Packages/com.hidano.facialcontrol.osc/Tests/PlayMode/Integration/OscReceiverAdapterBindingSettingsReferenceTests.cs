using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Tests.PlayMode.Integration
{
    /// <summary>
    /// task 8.5 PlayMode 観測可能完了条件:
    /// <see cref="OscReceiverAdapterBinding"/> / <see cref="OscSenderAdapterBinding"/> が
    /// <see cref="OscRuntimeSettingsSO"/> 経由で割り当てられた listenPort / endpoints を
    /// 用いて実 UDP loopback の送受信を成立させられること、および同一 SO を双方の binding に
    /// 注入したときに設定が一貫することを assert する。
    /// </summary>
    [TestFixture]
    public sealed class OscReceiverAdapterBindingSettingsReferenceTests
    {
        private const string Endpoint = "127.0.0.1";
        private const int LoopbackPortBase = 19620;
        private const string BlendShapeNameA = "smile";
        private const string BlendShapeNameB = "frown";
        private const float Tolerance = 0.05f;

        private static int s_portCounter;

        private readonly List<AdapterBindingBase> _startedBindings = new List<AdapterBindingBase>();
        private readonly List<GameObject> _gameObjects = new List<GameObject>();
        private readonly List<OscRuntimeSettingsSO> _settingsAssets = new List<OscRuntimeSettingsSO>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _startedBindings.Count - 1; i >= 0; i--)
            {
                try
                {
                    _startedBindings[i]?.Dispose();
                }
                catch (Exception)
                {
                    // TearDown 中の例外は無視して assertion を優先する。
                }
            }
            _startedBindings.Clear();

            for (int i = _gameObjects.Count - 1; i >= 0; i--)
            {
                GameObject gameObject = _gameObjects[i];
                if (gameObject == null)
                {
                    continue;
                }

                gameObject.SetActive(false);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
            _gameObjects.Clear();

            for (int i = _settingsAssets.Count - 1; i >= 0; i--)
            {
                OscRuntimeSettingsSO so = _settingsAssets[i];
                if (so != null)
                {
                    UnityEngine.Object.DestroyImmediate(so);
                }
            }
            _settingsAssets.Clear();
        }

        // ---------------------------------------------------------------
        // Receiver: SettingsSO.ListenPort 経由で UDP loopback メッセージを受信できる
        // ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator Receiver_SettingsListenPort_ReceivesUdpLoopbackValue()
        {
            const string slug = "osc-receiver-settings-port";
            int port = AllocatePort();

            OscRuntimeSettingsSO settings = CreateSettings(BuildReceiverJson(port));
            OscReceiverAdapterBinding receiver = CreateReceiver(slug, settings);
            GameObject receiverHost = CreateGameObject("OscReceiverAdapterBindingSettingsReferenceTests_Receiver");
            var registry = new InputSourceRegistry();

            StartBinding(receiver, CreateContext(registry, new FacialOutputBus(), receiverHost,
                new[] { BlendShapeNameA, BlendShapeNameB }));

            Assert.That(receiver.IsStarted, Is.True,
                "OscReceiverAdapterBinding は _settings 経由でも起動できるべき。");
            Assert.That(receiver.HelperHost, Is.Not.Null,
                "OnStart 後は OscReceiverHost が AddComponent されているべき。");
            Assert.That(receiver.HelperHost.Port, Is.EqualTo(port),
                "_settings.ListenPort が OscReceiverHost.Configure に伝播するべき。");

            yield return new WaitForSecondsRealtime(0.2f);

            GameObject senderHost = CreateGameObject("OscReceiverAdapterBindingSettingsReferenceTests_RawSender");
            OscSender sender = senderHost.AddComponent<OscSender>();
            sender.Endpoint = Endpoint;
            sender.Port = port;
            OscMapping[] mappings = new[]
            {
                new OscMapping(OscAddressFormatter.VRChatParameterPrefix + BlendShapeNameA, BlendShapeNameA, "emotion"),
                new OscMapping(OscAddressFormatter.VRChatParameterPrefix + BlendShapeNameB, BlendShapeNameB, "emotion"),
            };
            sender.Initialize(mappings);
            sender.StartSending();

            yield return new WaitForSecondsRealtime(0.2f);

            Assert.That(registry.TryResolve(slug, out IInputSource source), Is.True);

            float[] readBuffer = new float[mappings.Length];
            bool received = false;
            for (int attempt = 0; attempt < 20 && !received; attempt++)
            {
                sender.SendAll(new[] { 0.62f, 0.31f });
                yield return new WaitForSecondsRealtime(0.05f);
                receiver.OnFixedTick(0.02f);

                Array.Clear(readBuffer, 0, readBuffer.Length);
                if (source.TryWriteValues(readBuffer.AsSpan()) && readBuffer[0] > 0.01f)
                {
                    received = true;
                    Assert.That(readBuffer[0], Is.EqualTo(0.62f).Within(Tolerance));
                    Assert.That(readBuffer[1], Is.EqualTo(0.31f).Within(Tolerance));
                }
            }

            sender.StopSending();
            Assert.That(received, Is.True,
                "_settings.ListenPort 経由で bind した OscReceiver が UDP loopback 値を受信できるべき。");
        }

        // ---------------------------------------------------------------
        // Sender: SettingsSO.Endpoints 経由で UDP 送信が行われる
        // ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator Sender_SettingsEndpoints_SendsBlendShapeValueOverUdp()
        {
            int port = AllocatePort();

            OscRuntimeSettingsSO settings = CreateSettings(BuildSenderJson(port));
            OscSenderAdapterBinding sender = CreateSender("osc-sender-settings-endpoints", settings,
                new[] { BlendShapeNameA });
            GameObject senderHost = CreateGameObject("OscReceiverAdapterBindingSettingsReferenceTests_Sender");
            var outputBus = new FacialOutputBus();

            // 受信側は通常の OscReceiver MonoBehaviour を直接 bind して値を観測する。
            GameObject receiverHost = CreateGameObject("OscReceiverAdapterBindingSettingsReferenceTests_RawReceiver");
            OscReceiver rawReceiver = receiverHost.AddComponent<OscReceiver>();
            var buffer = new OscDoubleBuffer(1);
            OscMapping[] mappings = new[]
            {
                new OscMapping(OscAddressFormatter.VRChatParameterPrefix + BlendShapeNameA, BlendShapeNameA, "emotion"),
            };
            rawReceiver.Port = port;
            rawReceiver.Initialize(buffer, mappings);
            rawReceiver.StartReceiving();

            try
            {
                yield return new WaitForSecondsRealtime(0.2f);

                StartBinding(sender, CreateContext(new InputSourceRegistry(), outputBus, senderHost,
                    new[] { BlendShapeNameA }));

                Assert.That(sender.IsStarted, Is.True,
                    "OscSenderAdapterBinding は _settings 経由でも起動できるべき。");
                Assert.That(sender.HelperSenderCount, Is.EqualTo(1),
                    "_settings.Endpoints の件数だけ OscSender が AddComponent されるべき。");
                Assert.That(sender.GetHelperSender(0).Port, Is.EqualTo(port),
                    "_settings.Endpoints[0].port が OscSender.Configure に伝播するべき。");

                yield return new WaitForSecondsRealtime(0.2f);

                bool received = false;
                float[] postBlendValues = new[] { 0.81f };
                for (int attempt = 0; attempt < 20 && !received; attempt++)
                {
                    outputBus.Publish(postBlendValues, ReadOnlySpan<GazeSnapshot>.Empty);
                    sender.OnLateTick(0.016f);
                    yield return new WaitForSecondsRealtime(0.05f);

                    buffer.Swap();
                    float observed = buffer.GetReadBuffer()[0];
                    if (observed > 0.01f)
                    {
                        received = true;
                        Assert.That(observed, Is.EqualTo(0.81f).Within(Tolerance));
                    }
                }

                Assert.That(received, Is.True,
                    "_settings.Endpoints 経由で起動した OscSender が UDP 経由で値を送信するべき。");
            }
            finally
            {
                rawReceiver.StopReceiving();
                buffer.Dispose();
            }
        }

        // ---------------------------------------------------------------
        // Shared SO: 同一 OscRuntimeSettingsSO を Receiver と Sender に注入したとき、
        // listen / send が同じ port に対して一貫して動作する (要件 2.7)。
        // ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator SharedSettings_ReceiverAndSender_UseSamePortAndExchangeValueOverUdp()
        {
            int port = AllocatePort();

            OscRuntimeSettingsSO shared = CreateSettings(BuildSharedJson(port));
            OscReceiverAdapterBinding receiver = CreateReceiver("osc-receiver-shared", shared);
            OscSenderAdapterBinding sender = CreateSender("osc-sender-shared", shared,
                new[] { BlendShapeNameA });

            GameObject receiverHost = CreateGameObject("OscReceiverAdapterBindingSettingsReferenceTests_SharedReceiver");
            GameObject senderHost = CreateGameObject("OscReceiverAdapterBindingSettingsReferenceTests_SharedSender");

            var registry = new InputSourceRegistry();
            var outputBus = new FacialOutputBus();

            StartBinding(receiver, CreateContext(registry, new FacialOutputBus(), receiverHost,
                new[] { BlendShapeNameA }));
            yield return new WaitForSecondsRealtime(0.2f);
            StartBinding(sender, CreateContext(new InputSourceRegistry(), outputBus, senderHost,
                new[] { BlendShapeNameA }));

            Assert.That(receiver.IsStarted, Is.True);
            Assert.That(sender.IsStarted, Is.True);
            Assert.That(receiver.HelperHost.Port, Is.EqualTo(port),
                "共有 SO の ListenPort が Receiver に反映されること。");
            Assert.That(sender.GetHelperSender(0).Port, Is.EqualTo(port),
                "共有 SO の Endpoints[0].port が Sender に反映され、Receiver と同じ値であること。");
            Assert.That(receiver.Settings, Is.SameAs(sender.Settings),
                "Receiver と Sender が同一 OscRuntimeSettingsSO インスタンスを参照していること。");

            yield return new WaitForSecondsRealtime(0.2f);

            float[] postBlendValues = new[] { 0.47f };
            float[] readBuffer = new float[1];
            bool reached = false;

            for (int attempt = 0; attempt < 20 && !reached; attempt++)
            {
                outputBus.Publish(postBlendValues, ReadOnlySpan<GazeSnapshot>.Empty);
                sender.OnLateTick(0.016f);
                yield return new WaitForSecondsRealtime(0.05f);
                receiver.OnFixedTick(0.02f);

                if (!registry.TryResolve("osc-receiver-shared", out IInputSource source) || source == null)
                {
                    continue;
                }

                Array.Clear(readBuffer, 0, readBuffer.Length);
                if (source.TryWriteValues(readBuffer.AsSpan()) && readBuffer[0] > 0.01f)
                {
                    reached = true;
                    Assert.That(readBuffer[0], Is.EqualTo(0.47f).Within(Tolerance),
                        "共有 SO の listen/send port を介して Sender 出力が Receiver に到達するべき。");
                }
            }

            Assert.That(reached, Is.True,
                "Receiver/Sender が同一 OscRuntimeSettingsSO を参照したとき、SettingsSO の port 経由で値が送受信できるべき。");
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private OscRuntimeSettingsSO CreateSettings(string json)
        {
            var settings = ScriptableObject.CreateInstance<OscRuntimeSettingsSO>();
            settings.hideFlags = HideFlags.HideAndDontSave;
            settings.FromJson(json);
            _settingsAssets.Add(settings);
            return settings;
        }

        private OscReceiverAdapterBinding CreateReceiver(string slug, OscRuntimeSettingsSO settings)
        {
            return new OscReceiverAdapterBinding
            {
                Slug = slug,
                Settings = settings,
                Mappings = new List<OscMappingEntry>
                {
                    new OscMappingEntry
                    {
                        mode = OscMappingMode.Normal_BlendShape,
                        expressionId = BlendShapeNameA,
                        addressPattern = OscAddressFormatter.VRChatParameterPrefix + BlendShapeNameA,
                    },
                    new OscMappingEntry
                    {
                        mode = OscMappingMode.Normal_BlendShape,
                        expressionId = BlendShapeNameB,
                        addressPattern = OscAddressFormatter.VRChatParameterPrefix + BlendShapeNameB,
                    },
                },
            };
        }

        private OscSenderAdapterBinding CreateSender(
            string slug,
            OscRuntimeSettingsSO settings,
            IReadOnlyList<string> blendShapeNames)
        {
            var binding = new OscSenderAdapterBinding
            {
                Slug = slug,
                Settings = settings,
                BlendShapeNames = new List<string>(blendShapeNames),
            };
            return binding;
        }

        private AdapterBuildContext CreateContext(
            InputSourceRegistry registry,
            FacialOutputBus outputBus,
            GameObject host,
            IReadOnlyList<string> blendShapeNames)
        {
            return new AdapterBuildContext(
                new FacialProfile("2.0.0"),
                blendShapeNames,
                registry,
                outputBus,
                new UnityTimeProvider(),
                host,
                lipSyncProvider: null);
        }

        private void StartBinding(AdapterBindingBase binding, AdapterBuildContext context)
        {
            binding.OnStart(in context);
            _startedBindings.Add(binding);
        }

        private GameObject CreateGameObject(string name)
        {
            var gameObject = new GameObject(name);
            _gameObjects.Add(gameObject);
            return gameObject;
        }

        private static string BuildReceiverJson(int port)
        {
            return "{\"receiverEnabled\":true,\"listenEndpoint\":\"" + Endpoint
                + "\",\"listenPort\":" + port.ToString(CultureInfo.InvariantCulture)
                + ",\"bundleMode\":\"individualMessage\"}";
        }

        private static string BuildSenderJson(int port)
        {
            return "{\"senderEnabled\":true,\"endpoints\":[{\"endpoint\":\"" + Endpoint
                + "\",\"port\":" + port.ToString(CultureInfo.InvariantCulture)
                + ",\"enabled\":true,\"preset\":0}],\"heartbeatIntervalSeconds\":60,\"suppressLoopback\":false}";
        }

        private static string BuildSharedJson(int port)
        {
            string portText = port.ToString(CultureInfo.InvariantCulture);
            return "{\"receiverEnabled\":true,\"listenEndpoint\":\"" + Endpoint
                + "\",\"listenPort\":" + portText
                + ",\"bundleMode\":\"individualMessage\""
                + ",\"senderEnabled\":true,\"endpoints\":[{\"endpoint\":\"" + Endpoint
                + "\",\"port\":" + portText
                + ",\"enabled\":true,\"preset\":0}],\"heartbeatIntervalSeconds\":60,\"suppressLoopback\":false}";
        }

        private static int AllocatePort()
        {
            int next = System.Threading.Interlocked.Increment(ref s_portCounter);
            return LoopbackPortBase + next;
        }
    }
}
