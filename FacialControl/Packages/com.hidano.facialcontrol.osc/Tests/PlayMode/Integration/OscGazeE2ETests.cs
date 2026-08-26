#if FACIALCONTROL_HAS_INPUTSYSTEM_MODULE && FACIALCONTROL_HAS_UNITY_INPUTSYSTEM
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.AdapterBindings.InputSystem;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.InputSystem.Adapters.ScriptableObject;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Tests.PlayMode.Integration
{
    [TestFixture]
    public class OscGazeE2ETests : InputTestFixture
    {
        private const string Endpoint = "127.0.0.1";
        private const int LoopbackPortBase = 19340;
        private const string ExpressionId = "eye-look";
        private const float Tolerance = 0.06f;

        private static int s_portCounter;

        private readonly List<AdapterBindingBase> _startedBindings = new List<AdapterBindingBase>();
        private readonly List<GameObject> _gameObjects = new List<GameObject>();
        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();

        private InputSourceRegistry _registry;
        private FacialOutputBus _outputBus;
        private Gamepad _gamepad;

        public override void Setup()
        {
            base.Setup();
            _registry = new InputSourceRegistry();
            _outputBus = new FacialOutputBus();
        }

        public override void TearDown()
        {
            for (int i = _startedBindings.Count - 1; i >= 0; i--)
            {
                try
                {
                    _startedBindings[i]?.Dispose();
                }
                catch (Exception)
                {
                    // TearDown ではテスト本体の失敗を優先する。
                }
            }
            _startedBindings.Clear();

            if (_gamepad != null && _gamepad.added)
            {
                UnityEngine.InputSystem.InputSystem.RemoveDevice(_gamepad);
            }
            _gamepad = null;

            for (int i = _gameObjects.Count - 1; i >= 0; i--)
            {
                GameObject go = _gameObjects[i];
                if (go == null)
                {
                    continue;
                }

                go.SetActive(false);
                UnityEngine.Object.DestroyImmediate(go);
            }
            _gameObjects.Clear();

            for (int i = _objects.Count - 1; i >= 0; i--)
            {
                UnityEngine.Object obj = _objects[i];
                if (obj != null)
                {
                    UnityEngine.Object.DestroyImmediate(obj);
                }
            }
            _objects.Clear();

            _registry = null;
            _outputBus = null;

            base.TearDown();
        }

        [UnityTest]
        public IEnumerator GazeVrChatPreset_UdpLoopback_ReconstructsReceiverVector2()
        {
            int port = AllocatePort();
            var expected = new Vector2(0.64f, -0.37f);

            // 手動 mapping は空。送信側の初回 heartbeat に同乗する広告だけで route を生成する。
            OscReceiverAdapterBinding receiver = CreateReceiver("vrchat-gaze-receiver", port);

            OscSenderAdapterBinding sender = CreateSender(
                "vrchat-gaze-sender",
                port,
                AddressPresetKind.VRChat);

            StartBinding(receiver, CreateContext(CreateGameObject("OscGazeE2E_VRChatReceiver")));
            StartBinding(sender, CreateContext(CreateGameObject("OscGazeE2E_VRChatSender")));

            yield return new WaitForSecondsRealtime(0.2f);

            yield return WaitUntilGazeVectorArrives(
                receiver,
                "vrchat-gaze-receiver:" + ExpressionId,
                expected,
                () =>
                {
                    _outputBus.Publish(
                        Array.Empty<float>(),
                        new[] { new GazeSnapshot(ExpressionId, expected.x, expected.y) });
                    sender.OnLateTick(0.016f);
                });
        }

        [UnityTest]
        public IEnumerator GazeArKitPreset_UdpLoopback_DecomposesSenderVector2()
        {
            int port = AllocatePort();
            var expected = new Vector2(-0.42f, 0.58f);

            // 手動 mapping は空。広告受信後に ARKit の左右 source が自動生成される。
            OscReceiverAdapterBinding receiver = CreateReceiver("arkit-gaze-receiver", port);

            OscSenderAdapterBinding sender = CreateSender(
                "arkit-gaze-sender",
                port,
                AddressPresetKind.ARKit);

            StartBinding(receiver, CreateContext(CreateGameObject("OscGazeE2E_ARKitReceiver")));
            StartBinding(sender, CreateContext(CreateGameObject("OscGazeE2E_ARKitSender")));

            yield return new WaitForSecondsRealtime(0.2f);

            yield return WaitUntilResolvedGazeArrives(
                receiver,
                expected,
                expected,
                () =>
                {
                    _outputBus.Publish(
                        Array.Empty<float>(),
                        new[] { new GazeSnapshot(ExpressionId, expected.x, expected.y) });
                    sender.OnLateTick(0.016f);
                });
        }

        [UnityTest]
        public IEnumerator GazeArKit8Bs_DefaultConfig_PreservesLeftRightAsymmetricVectors()
        {
            int port = AllocatePort();
            var expectedLeft = new Vector2(0.75f, -0.2f);
            var expectedRight = new Vector2(-0.35f, 0.6f);
            float[] values = new float[PerfectSyncEyeLook.Count];
            PerfectSyncEyeLook.Compose(expectedLeft, expectedRight, values);

            OscReceiverAdapterBinding receiver = CreateReceiver(
                "arkit-asymmetric-receiver",
                port,
                new OscMappingEntry
                {
                    mode = OscMappingMode.Gaze_ARKit_8BS,
                    expressionId = ExpressionId,
                    leftRightIndependent = false,
                });

            OscSender sender = CreateRawSender(
                "OscGazeE2E_ARKitRawSender",
                port,
                CreateArKitMappings());

            StartBinding(receiver, CreateContext(CreateGameObject("OscGazeE2E_ARKitAsymmetricReceiver")));

            yield return new WaitForSecondsRealtime(0.2f);

            yield return WaitUntilResolvedGazeArrives(
                receiver,
                expectedLeft,
                expectedRight,
                () => sender.SendAll(values));
        }

        [Test]
        public void OldReceiverEquivalent_UnknownGazeAdvertisement_IsIgnoredWithoutLogs()
        {
            GameObject receiverObject = CreateGameObject("OscGazeE2E_OldReceiver");
            OscReceiver receiver = receiverObject.AddComponent<OscReceiver>();
            var buffer = new OscDoubleBuffer(0);
            try
            {
                receiver.Initialize(buffer, Array.Empty<OscMapping>());

                // 旧 receiver は広告 route を知らないが、通常の未知アドレスとして無警告で読み飛ばす。
                receiver.HandleOscMessage(new uOSC.Message(
                    OscReceiverAdapterBinding.GazeAdvertisementAddress,
                    ExpressionId,
                    GazeAdvertisementResolver.VrChatXyFormat));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                buffer.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator GazeAdvertisement_SameContentTwice_ReusesSources()
        {
            int port = AllocatePort();
            OscReceiverAdapterBinding receiver = CreateReceiver("gaze-reuse-receiver", port);
            OscSenderAdapterBinding sender = CreateSender("gaze-reuse-sender", port, AddressPresetKind.VRChat);

            StartBinding(receiver, CreateContext(CreateGameObject("OscGazeE2E_ReuseReceiver")));
            StartBinding(sender, CreateContext(CreateGameObject("OscGazeE2E_ReuseSender")));

            yield return new WaitForSecondsRealtime(0.2f);
            yield return SendGazeUntilProcessed(receiver, sender, ExpressionId);

            Assert.That(receiver.GazeSources.Count, Is.EqualTo(1));
            GazeVector2InputSource original = receiver.GazeSources[0];
            uint originalHash = receiver.LastGazeAdvertisementHash;

            yield return SendGazeUntilProcessed(receiver, sender, ExpressionId);

            Assert.That(receiver.LastGazeAdvertisementHash, Is.EqualTo(originalHash));
            Assert.That(receiver.GazeSources.Count, Is.EqualTo(1));
            Assert.That(receiver.GazeSources[0], Is.SameAs(original));
        }

        [UnityTest]
        public IEnumerator GazeAdvertisement_ContentChanged_RebuildsAndUnregistersRemovedId()
        {
            int port = AllocatePort();
            OscReceiverAdapterBinding receiver = CreateReceiver("gaze-change-receiver", port);
            OscSenderAdapterBinding firstSender = CreateSender("gaze-change-sender-a", port, AddressPresetKind.VRChat);
            StartBinding(receiver, CreateContext(CreateGameObject("OscGazeE2E_ChangeReceiver")));
            StartBinding(firstSender, CreateContext(CreateGameObject("OscGazeE2E_ChangeSenderA")));

            yield return new WaitForSecondsRealtime(0.2f);
            yield return SendGazeUntilProcessed(receiver, firstSender, ExpressionId);
            Assert.That(_registry.TryResolve("gaze-change-receiver:" + ExpressionId, out IInputSource removed), Is.True);

            firstSender.Dispose();
            OscSenderAdapterBinding secondSender = CreateSenderWithIds(
                "gaze-change-sender-b", port, AddressPresetKind.VRChat, "eye-look-new");
            StartBinding(secondSender, CreateContext(CreateGameObject("OscGazeE2E_ChangeSenderB")));

            yield return SendGazeUntilProcessed(receiver, secondSender, "eye-look-new");

            Assert.That(receiver.AutoGazeSourceIds, Does.Contain("gaze-change-receiver:eye-look-new"));
            Assert.That(receiver.AutoGazeSourceIds, Does.Not.Contain("gaze-change-receiver:" + ExpressionId));
            Assert.That(_registry.TryResolve("gaze-change-receiver:" + ExpressionId, out _), Is.False);
            Assert.That(removed, Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator GazeAdvertisement_Stops_PreservesRoutesAndSources()
        {
            int port = AllocatePort();
            OscReceiverAdapterBinding receiver = CreateReceiver("gaze-stop-receiver", port);
            OscSenderAdapterBinding sender = CreateSender("gaze-stop-sender", port, AddressPresetKind.VRChat);
            StartBinding(receiver, CreateContext(CreateGameObject("OscGazeE2E_StopReceiver")));
            StartBinding(sender, CreateContext(CreateGameObject("OscGazeE2E_StopSender")));

            yield return new WaitForSecondsRealtime(0.2f);
            yield return SendGazeUntilProcessed(receiver, sender, ExpressionId);
            GazeVector2InputSource source = receiver.GazeSources[0];

            sender.Dispose();
            for (int i = 0; i < 4; i++)
            {
                receiver.OnFixedTick(0.02f);
                yield return null;
            }

            Assert.That(receiver.HasAutoGazeRoutes, Is.True);
            Assert.That(receiver.GazeSources[0], Is.SameAs(source));
            Assert.That(_registry.TryResolve("gaze-stop-receiver:" + ExpressionId, out IInputSource retained), Is.True);
            Assert.That(retained, Is.SameAs(source));
        }

        [UnityTest]
        public IEnumerator GazeAdvertisement_ManualSameId_ManualMappingWinsWithoutError()
        {
            int port = AllocatePort();
            OscReceiverAdapterBinding receiver = CreateReceiver(
                "gaze-manual-receiver",
                port,
                new OscMappingEntry
                {
                    mode = OscMappingMode.Gaze_VRChat_XY,
                    expressionId = ExpressionId,
                    addressPattern = OscAddressFormatter.VRChatParameterPrefix + ExpressionId,
                });
            OscSenderAdapterBinding sender = CreateSender("gaze-manual-sender", port, AddressPresetKind.VRChat);
            StartBinding(receiver, CreateContext(CreateGameObject("OscGazeE2E_ManualReceiver")));
            StartBinding(sender, CreateContext(CreateGameObject("OscGazeE2E_ManualSender")));

            yield return new WaitForSecondsRealtime(0.2f);
            yield return SendGazeUntilProcessed(receiver, sender, ExpressionId);

            Assert.That(receiver.AutoGazeSourceIds, Is.Empty);
            Assert.That(receiver.GazeSources.Count, Is.EqualTo(1));
            Assert.That(receiver.GazeSources[0].Id, Is.EqualTo("gaze-manual-receiver:" + ExpressionId));
        }

        [UnityTest]
        public IEnumerator GazeAdvertisement_GazeConfigMatch_IsAcceptedForLateBoneConnection()
        {
            int port = AllocatePort();
            OscReceiverAdapterBinding receiver = CreateReceiver("gaze-config-receiver", port);
            receiver.Configure(new[] { new GazeBindingConfig { expressionId = ExpressionId } });
            OscSenderAdapterBinding sender = CreateSender("gaze-config-sender", port, AddressPresetKind.VRChat);
            StartBinding(receiver, CreateContext(CreateGameObject("OscGazeE2E_ConfigReceiver")));
            StartBinding(sender, CreateContext(CreateGameObject("OscGazeE2E_ConfigSender")));

            yield return new WaitForSecondsRealtime(0.2f);
            yield return SendGazeUntilProcessed(receiver, sender, ExpressionId);

            Assert.That(receiver.AutoGazeSourceIds, Does.Contain("gaze-config-receiver:" + ExpressionId));
        }

        [Test]
        public void NoAdvertisementNoManual_NoRoutesNoErrorLog()
        {
            OscReceiverAdapterBinding receiver = CreateReceiver("gaze-empty-receiver", AllocatePort());
            StartBinding(receiver, CreateContext(CreateGameObject("OscGazeE2E_EmptyReceiver")));

            Assert.That(receiver.GazeSources, Is.Empty);
            Assert.That(receiver.HasAutoGazeRoutes, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        private IEnumerator SendGazeUntilProcessed(
            OscReceiverAdapterBinding receiver,
            OscSenderAdapterBinding sender,
            string expressionId)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                _outputBus.Publish(
                    Array.Empty<float>(),
                    new[] { new GazeSnapshot(expressionId, 0.2f, -0.1f) });
                sender.OnLateTick(0.016f);
                yield return new WaitForSecondsRealtime(0.05f);
                receiver.OnFixedTick(0.02f);
                if (receiver.LastGazeAdvertisementHash != 0u ||
                    HasSourceId(receiver.AutoGazeSourceIds, receiver.Slug + ":" + expressionId))
                {
                    yield break;
                }
            }

            Assert.Fail("gaze advertisement was not processed");
        }

        private static bool HasSourceId(IReadOnlyList<string> sourceIds, string expected)
        {
            for (int i = 0; i < sourceIds.Count; i++)
            {
                if (string.Equals(sourceIds[i], expected, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        [UnityTest]
        public IEnumerator GazeResolver_OscAndInputSystemSameExpressionId_SelectsLexicographicallyFirstSlug()
        {
            int port = AllocatePort();
            var inputValue = new Vector2(-0.55f, 0.25f);
            var oscValue = new Vector2(0.91f, -0.87f);

            OscReceiverAdapterBinding receiver = CreateReceiver(
                "z-osc-gaze",
                port,
                new OscMappingEntry
                {
                    mode = OscMappingMode.Gaze_VRChat_XY,
                    expressionId = ExpressionId,
                    addressPattern = OscAddressFormatter.VRChatParameterPrefix + ExpressionId,
                    leftRightIndependent = false,
                });

            InputSystemAdapterBinding inputBinding = CreateInputSystemGazeBinding("a-input-system-gaze");
            OscSender rawSender = CreateRawSender(
                "OscGazeE2E_VRChatRawSender",
                port,
                CreateVrChatMappings());

            _gamepad = UnityEngine.InputSystem.InputSystem.AddDevice<Gamepad>();

            StartBinding(receiver, CreateContext(CreateGameObject("OscGazeE2E_DeterministicOsc")));
            StartBinding(inputBinding, CreateContext(CreateGameObject("OscGazeE2E_DeterministicInput")));

            yield return new WaitForSecondsRealtime(0.2f);

            using (UnityEngine.InputSystem.LowLevel.StateEvent.From(_gamepad, out var eventPtr))
            {
                _gamepad.leftStick.WriteValueIntoEvent(inputValue, eventPtr);
                UnityEngine.InputSystem.InputSystem.QueueEvent(eventPtr);
            }
            UnityEngine.InputSystem.InputSystem.Update();
            inputBinding.OnLateTick(0.016f);

            rawSender.SendAll(new[] { oscValue.x, oscValue.y });
            yield return new WaitForSecondsRealtime(0.05f);
            receiver.OnFixedTick(0.02f);

            LogAssert.Expect(
                LogType.Warning,
                new Regex("expressionId 'eye-look'.*multiple binding slugs.*selected 'a-input-system-gaze'"));

            bool resolved = GazeBindingConfigResolver.TryResolve(
                new GazeBindingConfig { expressionId = ExpressionId },
                _registry,
                out ResolvedGazeInputSources sources);

            Assert.That(resolved, Is.True);
            Assert.That(sources.SelectedSlug, Is.EqualTo("a-input-system-gaze"));
            AssertVector(sources.LeftSource, inputValue, "InputSystem 側の Gaze source が採用されること。");
            AssertVector(sources.RightSource, inputValue, "InputSystem 側の Gaze source が両目に流用されること。");
        }

        private IEnumerator WaitUntilGazeVectorArrives(
            OscReceiverAdapterBinding receiver,
            string sourceId,
            Vector2 expected,
            Action send)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                send();
                yield return new WaitForSecondsRealtime(0.05f);
                receiver.OnFixedTick(0.02f);

                if (TryReadVector(sourceId, out Vector2 actual))
                {
                    Assert.That(actual.x, Is.EqualTo(expected.x).Within(Tolerance));
                    Assert.That(actual.y, Is.EqualTo(expected.y).Within(Tolerance));
                    yield break;
                }
            }

            Assert.Fail($"Gaze source '{sourceId}' に OSC loopback 値が到達しませんでした。");
        }

        private IEnumerator WaitUntilResolvedGazeArrives(
            OscReceiverAdapterBinding receiver,
            Vector2 expectedLeft,
            Vector2 expectedRight,
            Action send)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                send();
                yield return new WaitForSecondsRealtime(0.05f);
                receiver.OnFixedTick(0.02f);

                if (!GazeBindingConfigResolver.TryResolve(
                        new GazeBindingConfig { expressionId = ExpressionId },
                        _registry,
                        out ResolvedGazeInputSources sources))
                {
                    continue;
                }

                if (!TryReadVector(sources.LeftSource, out Vector2 actualLeft) ||
                    !TryReadVector(sources.RightSource, out Vector2 actualRight))
                {
                    continue;
                }

                Assert.That(actualLeft.x, Is.EqualTo(expectedLeft.x).Within(Tolerance));
                Assert.That(actualLeft.y, Is.EqualTo(expectedLeft.y).Within(Tolerance));
                Assert.That(actualRight.x, Is.EqualTo(expectedRight.x).Within(Tolerance));
                Assert.That(actualRight.y, Is.EqualTo(expectedRight.y).Within(Tolerance));
                yield break;
            }

            Assert.Fail("GazeBindingConfig 既定解決経路で左右 Gaze source を読み取れませんでした。");
        }

        private OscReceiverAdapterBinding CreateReceiver(
            string slug,
            int port,
            params OscMappingEntry[] entries)
        {
            return new OscReceiverAdapterBinding
            {
                Slug = slug,
                Endpoint = Endpoint,
                Port = port,
                BundleMode = BundleInterpretationMode.AtomicSwap,
                Mappings = new List<OscMappingEntry>(entries ?? Array.Empty<OscMappingEntry>()),
            };
        }

        private OscSenderAdapterBinding CreateSender(
            string slug,
            int port,
            AddressPresetKind preset)
        {
            return CreateSenderWithIds(slug, port, preset, ExpressionId);
        }

        private OscSenderAdapterBinding CreateSenderWithIds(
            string slug,
            int port,
            AddressPresetKind preset,
            params string[] gazeExpressionIds)
        {
            var binding = new OscSenderAdapterBinding
            {
                Slug = slug,
                SuppressLoopback = false,
                HeartbeatIntervalSeconds = 60f,
            };
            binding.ConfigureEndpoints(
                new[]
                {
                    new OscSenderEndpointConfig(Endpoint, port, true, preset)
                },
                Array.Empty<string>());
            binding.ConfigureGazeExpressionIds(gazeExpressionIds);
            return binding;
        }

        private InputSystemAdapterBinding CreateInputSystemGazeBinding(string slug)
        {
            InputActionAsset asset = ScriptableObject.CreateInstance<InputActionAsset>();
            _objects.Add(asset);

            InputActionMap map = asset.AddActionMap("Expression");
            map.AddAction("GazeLook", InputActionType.Value, expectedControlLayout: "Vector2")
                .AddBinding("<Gamepad>/leftStick");

            var binding = new InputSystemAdapterBinding
            {
                Slug = slug,
            };
            binding.Configure(
                asset,
                "Expression",
                new[]
                {
                    new ExpressionBindingEntry
                    {
                        bindingMode = BindingMode.Gaze,
                        expressionId = ExpressionId,
                        actionName = "GazeLook",
                    }
                },
                new[]
                {
                    new GazeBindingConfig { expressionId = ExpressionId }
                });
            return binding;
        }

        private OscSender CreateRawSender(
            string name,
            int port,
            OscMapping[] mappings)
        {
            GameObject go = CreateGameObject(name);
            OscSender sender = go.AddComponent<OscSender>();
            sender.Endpoint = Endpoint;
            sender.Port = port;
            sender.Initialize(mappings);
            sender.StartSending();
            return sender;
        }

        private AdapterBuildContext CreateContext(GameObject host)
        {
            return new AdapterBuildContext(
                profile: new FacialProfile("2.0"),
                blendShapeNames: Array.Empty<string>(),
                inputSourceRegistry: _registry,
                facialOutputBus: _outputBus,
                timeProvider: new UnityTimeProvider(),
                hostGameObject: host,
                lipSyncProvider: null);
        }

        private GameObject CreateGameObject(string name)
        {
            var go = new GameObject(name);
            _gameObjects.Add(go);
            return go;
        }

        private void StartBinding(AdapterBindingBase binding, AdapterBuildContext context)
        {
            binding.OnStart(in context);
            _startedBindings.Add(binding);
        }

        private bool TryReadVector(string sourceId, out Vector2 value)
        {
            value = default;
            if (!_registry.TryResolve(sourceId, out IInputSource source) ||
                source is not IAnalogInputSource analog)
            {
                return false;
            }

            return TryReadVector(analog, out value);
        }

        private static bool TryReadVector(IAnalogInputSource source, out Vector2 value)
        {
            value = default;
            if (source == null || !source.IsValid)
            {
                return false;
            }

            if (!source.TryReadVector2(out float x, out float y))
            {
                return false;
            }

            value = new Vector2(x, y);
            return true;
        }

        private static void AssertVector(
            IAnalogInputSource source,
            Vector2 expected,
            string message)
        {
            Assert.That(TryReadVector(source, out Vector2 actual), Is.True, message);
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(Tolerance), message);
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(Tolerance), message);
        }

        private static OscMapping[] CreateVrChatMappings()
        {
            return new[]
            {
                new OscMapping(
                    OscAddressFormatter.VRChatParameterPrefix + ExpressionId + OscAddressFormatter.VRChatGazeXAxis,
                    ExpressionId + "X",
                    string.Empty),
                new OscMapping(
                    OscAddressFormatter.VRChatParameterPrefix + ExpressionId + OscAddressFormatter.VRChatGazeYAxis,
                    ExpressionId + "Y",
                    string.Empty),
            };
        }

        private static OscMapping[] CreateArKitMappings()
        {
            var mappings = new OscMapping[PerfectSyncEyeLook.Count];
            for (int i = 0; i < PerfectSyncEyeLook.Count; i++)
            {
                mappings[i] = new OscMapping(
                    PerfectSyncEyeLook.ArKitAddressPrefix + PerfectSyncEyeLook.Names[i],
                    PerfectSyncEyeLook.Names[i],
                    string.Empty);
            }

            return mappings;
        }

        private static int AllocatePort()
        {
            int next = System.Threading.Interlocked.Increment(ref s_portCounter);
            return LoopbackPortBase + next;
        }
    }
}
#endif
