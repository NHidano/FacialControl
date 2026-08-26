using System;
using System.Collections;
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
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Tests.PlayMode.Integration
{
    /// <summary>
    /// task 9.1 PlayMode 観測可能完了条件:
    /// 実 UDP loopback で <see cref="OscReceiverAdapterBinding"/> の <c>OnStart</c> が
    /// <c>ctx.HostGameObject.AddComponent&lt;OscReceiverHost&gt;()</c> + <c>Configure(...)</c>
    /// を実行し、<c>InputSourceRegistry.Register(slug, source)</c> で primary
    /// <see cref="IInputSource"/> が解決可能になることを assert する。
    /// <c>Dispose</c> で helper MonoBehaviour が破棄され socket がクローズされること、
    /// helper の <see cref="HideFlags"/> が <c>HideInInspector</c> を含まないことも検証する。
    /// </summary>
    /// <remarks>
    /// 本ファイルは Red 段階のテストであり、
    /// <c>Hidano.FacialControl.Adapters.AdapterBindings.OscReceiverAdapterBinding</c> および
    /// <c>Hidano.FacialControl.Adapters.OSC.OscReceiverHost</c> が未実装のため
    /// コンパイル時に CS0246 / CS0234 が発生して Red 状態となる（task 9.2 の Green 化対象）。
    /// </remarks>
    [TestFixture]
    public class OscReceiverAdapterBindingIntegrationTests
    {
        private const string TestEndpoint = "127.0.0.1";
        private const int LoopbackPortBase = 19130;

        private GameObject _hostGameObject;
        private GameObject _senderGameObject;
        private InputSourceRegistry _registry;
        private OscReceiverAdapterBinding _binding;
        private bool _bindingStarted;
        private static int s_portCounter;

        [SetUp]
        public void SetUp()
        {
            _registry = new InputSourceRegistry();
            _hostGameObject = new GameObject("OscReceiverAdapterBindingIntegrationTestsHost");
            _bindingStarted = false;
        }

        [TearDown]
        public void TearDown()
        {
            if (_binding != null && _bindingStarted)
            {
                try
                {
                    _binding.Dispose();
                }
                catch (Exception)
                {
                    // TearDown では例外を握り潰し、テスト本体の assertion を優先する。
                }
            }
            _binding = null;
            _bindingStarted = false;

            if (_senderGameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_senderGameObject);
                _senderGameObject = null;
            }
            if (_hostGameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_hostGameObject);
                _hostGameObject = null;
            }
        }

        [UnityTest]
        public IEnumerator OnStart_EmptyMappings_StartsSocketWithoutRegisteringPrimaryInputSource()
        {
            const string slug = "osc-empty-socket";
            int port = AllocatePort();
            _binding = new OscReceiverAdapterBinding
            {
                Slug = slug,
                Port = port,
                Mappings = new List<OscMappingEntry>()
            };
            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            yield return new WaitForSeconds(0.2f);

            Assert.That(_binding.IsStarted, Is.True);
            Assert.That(_binding.HelperHost, Is.Not.Null);
            Assert.That(_binding.HelperHost.IsConfigured, Is.True);
            Assert.That(_binding.HelperHost.Receiver, Is.Not.Null);
            Assert.That(_binding.HelperHost.Receiver.IsRunning, Is.True);
            Assert.That(_binding.Buffer, Is.Not.Null);
            Assert.That(_binding.Buffer.Size, Is.EqualTo(0));
            Assert.That(_binding.InputSource, Is.Null);
            Assert.That(_registry.TryResolve(slug, out _), Is.False);
        }

        [Test]
        public void OnFixedTick_EmptyMappingsHeartbeat_GeneratesAutoMappingsAndRegistersInputSource()
        {
            const string slug = "osc-auto-heartbeat";
            int port = AllocatePort();
            _binding = new OscReceiverAdapterBinding
            {
                Slug = slug,
                Port = port,
                Mappings = new List<OscMappingEntry>(),
                BundleMode = BundleInterpretationMode.IndividualMessage
            };
            AdapterBuildContext ctx = CreateContext(blendShapeNames: new List<string> { "smile", "frown" });

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            HandleHeartbeat("smile", "frown");
            Assert.That(_binding.InputSource, Is.Null);

            _binding.OnFixedTick(0.02f);

            Assert.That(_binding.InputSource, Is.Not.Null);
            Assert.That(_binding.Buffer.Size, Is.EqualTo(2));
            Assert.That(_binding.RuntimeMappings.Count, Is.EqualTo(2));
            Assert.That(_binding.MappingOrigins[0], Is.EqualTo(OscReceiverAdapterBinding.MappingOrigin.HeartbeatAuto));
            Assert.That(_binding.MappingOrigins[1], Is.EqualTo(OscReceiverAdapterBinding.MappingOrigin.HeartbeatAuto));
            Assert.That(_registry.TryResolve(slug, out IInputSource source), Is.True);
            Assert.That(source, Is.SameAs(_binding.InputSource));

            _binding.HelperHost.Receiver.HandleOscMessage(
                new uOSC.Message(OscAddressFormatter.VRChatParameterPrefix + "smile", 0.7f));
            _binding.HelperHost.Receiver.HandleOscMessage(
                new uOSC.Message(OscAddressFormatter.VRChatParameterPrefix + "frown", 0.3f));
            _binding.OnFixedTick(0.02f);

            var values = new float[2];
            Assert.That(source.TryWriteValues(values), Is.True);
            Assert.That(values[0], Is.EqualTo(0.7f).Within(1e-6f));
            Assert.That(values[1], Is.EqualTo(0.3f).Within(1e-6f));
        }

        [Test]
        public void OnFixedTick_UnchangedHeartbeatHash_ReusesRuntimeInstances()
        {
            const string slug = "osc-auto-heartbeat-hash";
            int port = AllocatePort();
            _binding = new OscReceiverAdapterBinding
            {
                Slug = slug,
                Port = port,
                Mappings = new List<OscMappingEntry>(),
                BundleMode = BundleInterpretationMode.IndividualMessage
            };
            AdapterBuildContext ctx = CreateContext(blendShapeNames: new List<string> { "smile", "frown" });

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            HandleHeartbeat("smile", "frown");
            _binding.OnFixedTick(0.02f);

            OscInputSource originalSource = _binding.InputSource;
            OscDoubleBuffer originalBuffer = _binding.Buffer;
            HeartbeatConsistencyChecker originalChecker = _binding.HeartbeatChecker;

            HandleHeartbeat("smile", "frown");
            _binding.OnFixedTick(0.02f);

            Assert.That(_binding.InputSource, Is.SameAs(originalSource));
            Assert.That(_binding.Buffer, Is.SameAs(originalBuffer));
            Assert.That(_binding.HeartbeatChecker, Is.SameAs(originalChecker));
            Assert.That(_binding.RuntimeMappings.Count, Is.EqualTo(2));
        }

        [Test]
        public void DiagnosticApis_ExposeOriginsPresetAndHeartbeatHashThenDisposeClearsRuntimeState()
        {
            const string slug = "osc-diagnostic-runtime-state";
            int port = AllocatePort();
            _binding = new OscReceiverAdapterBinding
            {
                Slug = slug,
                Port = port,
                Mappings = new List<OscMappingEntry>(),
                BundleMode = BundleInterpretationMode.IndividualMessage
            };
            AdapterBuildContext ctx = CreateContext(blendShapeNames: new List<string> { "smile", "frown" });

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            _binding.HelperHost.Receiver.HandleOscMessage(
                new uOSC.Message(OscReceiverAdapterBinding.PresetAddress, "custom", "/avatar/custom/"));
            HandleHeartbeat("smile", "frown");
            _binding.OnFixedTick(0.02f);

            Assert.That(_binding.RuntimeMappings.Count, Is.EqualTo(2));
            Assert.That(_binding.CurrentPreset, Is.EqualTo(AddressPresetKind.Custom));
            Assert.That(_binding.CurrentCustomPrefix, Is.EqualTo("/avatar/custom/"));
            Assert.That(_binding.LastHeartbeatHash,
                Is.EqualTo(HeartbeatHashHelper.ComputeFnv1a(new[] { "smile", "frown" })));
            Assert.That(_binding.GetMappingOrigin(0),
                Is.EqualTo(OscReceiverAdapterBinding.MappingOrigin.HeartbeatAuto));
            Assert.That(_binding.GetMappingOrigin(1),
                Is.EqualTo(OscReceiverAdapterBinding.MappingOrigin.HeartbeatAuto));
            Assert.Throws<ArgumentOutOfRangeException>(() => _binding.GetMappingOrigin(2));

            _binding.Dispose();
            _bindingStarted = false;

            Assert.That(_binding.RuntimeMappings.Count, Is.EqualTo(0));
            Assert.That(_binding.MappingOrigins.Count, Is.EqualTo(0));
            Assert.That(_binding.CurrentPreset, Is.Null);
            Assert.That(_binding.CurrentCustomPrefix, Is.Null);
            Assert.That(_binding.LastHeartbeatHash, Is.EqualTo(0u));
            Assert.That(_binding.Buffer, Is.Null);
            Assert.That(_binding.InputSource, Is.Null);
            Assert.That(_binding.IsStarted, Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => _binding.GetMappingOrigin(0));
        }

        // ---------------------------------------------------------------
        // OnStart: helper AddComponent + InputSourceRegistry 登録
        // ---------------------------------------------------------------

        [Test]
        public void OnStart_AddsOscReceiverHostHelperToContextHostGameObject()
        {
            int port = AllocatePort();
            OscMapping[] mappings = CreateDefaultMappings();
            _binding = CreateBinding(slug: "osc-helper-add", endpoint: TestEndpoint, port: port, mappings: mappings);

            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            OscReceiverHost helper = _hostGameObject.GetComponent<OscReceiverHost>();
            Assert.IsNotNull(helper,
                "OnStart は ctx.HostGameObject に OscReceiverHost を AddComponent するべき。");
        }

        [Test]
        public void OnStart_HelperHostHideFlags_DoesNotIncludeHideInInspector()
        {
            int port = AllocatePort();
            _binding = CreateBinding(slug: "osc-helper-hideflags", endpoint: TestEndpoint, port: port,
                mappings: CreateDefaultMappings());
            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            OscReceiverHost helper = _hostGameObject.GetComponent<OscReceiverHost>();
            Assert.IsNotNull(helper);

            HideFlags actualFlags = helper.hideFlags;
            Assert.That((actualFlags & HideFlags.HideInInspector), Is.EqualTo(HideFlags.None),
                "helper MonoBehaviour は Inspector で見える（HideInInspector を含まない）べき。");
        }

        [Test]
        public void OnStart_RegistersPrimaryInputSourceUnderSlug()
        {
            const string slug = "osc-primary-resolve";
            int port = AllocatePort();
            _binding = CreateBinding(slug: slug, endpoint: TestEndpoint, port: port,
                mappings: CreateDefaultMappings());
            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            bool resolved = _registry.TryResolve(slug, out IInputSource source);
            Assert.IsTrue(resolved,
                $"InputSourceRegistry.TryResolve(\"{slug}\") は OnStart 後に true を返すべき。");
            Assert.IsNotNull(source,
                "解決結果の IInputSource は non-null であるべき。");
        }

        // ---------------------------------------------------------------
        // OnStart: 実 UDP loopback でメッセージが registered InputSource に到達する
        // ---------------------------------------------------------------

        [Test]
        public void OnStart_ManualBlendShapeMappings_RegistersPrimarySourceWithMeshIndexContributeMask()
        {
            const string slug = "osc-manual-mask-regression";
            int port = AllocatePort();
            OscMapping[] mappings = new[]
            {
                new OscMapping("/avatar/parameters/frown", "frown", "emotion"),
                new OscMapping("/avatar/parameters/smile", "smile", "emotion")
            };
            _binding = CreateBinding(slug: slug, endpoint: TestEndpoint, port: port, mappings: mappings);
            AdapterBuildContext ctx = CreateContext(blendShapeNames: new List<string> { "smile", "blink", "frown" });

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            Assert.That(_binding.IsStarted, Is.True);
            Assert.That(_binding.Buffer, Is.Not.Null);
            Assert.That(_binding.Buffer.Size, Is.EqualTo(2));
            Assert.That(_binding.InputSource, Is.Not.Null);
            Assert.That(_registry.TryResolve(slug, out IInputSource source), Is.True);
            Assert.That(source, Is.SameAs(_binding.InputSource));
            AssertMask(source.ContributeMask, true, false, true);

            _binding.HelperHost.Receiver.HandleOscMessage(
                new uOSC.Message("/avatar/parameters/frown", 0.75f));
            _binding.HelperHost.Receiver.HandleOscMessage(
                new uOSC.Message("/avatar/parameters/smile", 0.25f));
            _binding.OnFixedTick(0.02f);

            var output = new float[] { -1f, -1f, -1f };
            Assert.That(source.TryWriteValues(output), Is.True);
            Assert.That(output[0], Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(output[1], Is.EqualTo(-1f).Within(1e-6f));
            Assert.That(output[2], Is.EqualTo(0.75f).Within(1e-6f));
        }

        [Test]
        public void OnFixedTick_GazeVrchatBundleOnly_UsesAccumulatorWithoutPrimaryOscInputSource()
        {
            const string slug = "osc-gaze-bundle-regression";
            var time = new ManualTimeProvider { UnscaledTimeSeconds = 0.0 };
            _binding = new OscReceiverAdapterBinding
            {
                Slug = slug,
                Port = AllocatePort(),
                StalenessSeconds = 0f,
                BundleMode = BundleInterpretationMode.AtomicSwap,
                BundleAccumulationTimeoutMs = 5f,
                Mappings = new List<OscMappingEntry>
                {
                    new OscMappingEntry
                    {
                        mode = OscMappingMode.Gaze_VRChat_XY,
                        expressionId = "eye",
                        addressPattern = "/avatar/parameters/eye",
                    }
                }
            };
            AdapterBuildContext ctx = CreateContext(timeProvider: time);

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            Assert.That(_binding.InputSource, Is.Null);
            Assert.That(_registry.TryResolve(slug, out _), Is.False);
            Assert.That(_registry.TryResolve(slug + ":eye", out IInputSource inputSource), Is.True);
            Assert.That(inputSource, Is.InstanceOf<GazeVector2InputSource>());
            AssertMask(inputSource.ContributeMask);

            _binding.HelperHost.Receiver.HandleOscMessage(
                FloatMessage("/avatar/parameters/eyeX", 0.2f, timestamp: 100UL));
            _binding.HelperHost.Receiver.HandleOscMessage(
                FloatMessage("/avatar/parameters/eyeY", -0.6f, timestamp: 100UL));
            _binding.OnFixedTick(0.02f);

            var gaze = (GazeVector2InputSource)inputSource;
            Assert.That(gaze.TryReadVector2(out _, out _), Is.False);

            time.UnscaledTimeSeconds = 0.006;
            _binding.OnFixedTick(0.02f);

            Assert.That(gaze.TryReadVector2(out float x, out float y), Is.True);
            Assert.That(x, Is.EqualTo(0.2f).Within(1e-6f));
            Assert.That(y, Is.EqualTo(-0.6f).Within(1e-6f));
        }

        [Test]
        public void PresetAddress_UpdatesRuntimeStateWithoutRecreatingInputSource()
        {
            const string slug = "osc-preset-runtime-state";
            int port = AllocatePort();
            _binding = CreateBinding(slug: slug, endpoint: TestEndpoint, port: port,
                mappings: CreateDefaultMappings());
            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            OscInputSource originalInputSource = _binding.InputSource;
            HeartbeatConsistencyChecker originalHeartbeatChecker = _binding.HeartbeatChecker;

            _binding.HelperHost.Receiver.HandleOscMessage(
                new uOSC.Message(OscReceiverAdapterBinding.PresetAddress, "custom", "/custom/"));

            Assert.That(_binding.CurrentPresetName, Is.EqualTo("custom"));
            Assert.That(_binding.CurrentCustomPrefix, Is.EqualTo("/custom/"));
            Assert.That(_binding.InputSource, Is.SameAs(originalInputSource));
            Assert.That(_binding.HeartbeatChecker, Is.SameAs(originalHeartbeatChecker));
        }

        [Test]
        public void PresetAddress_StateSurvivesHeartbeatOrderAndUsesLatestPreset()
        {
            const string slug = "osc-preset-heartbeat-order";
            int port = AllocatePort();
            _binding = CreateBinding(slug: slug, endpoint: TestEndpoint, port: port,
                mappings: CreateDefaultMappings());
            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            _binding.HelperHost.Receiver.HandleOscMessage(
                new uOSC.Message(OscReceiverAdapterBinding.PresetAddress, "arkit"));
            _binding.HelperHost.Receiver.HandleOscMessage(
                new uOSC.Message(OscReceiverAdapterBinding.BlendShapeNamesAddress, "smile", "frown"));

            Assert.That(_binding.CurrentPresetName, Is.EqualTo("arkit"));
            Assert.That(_binding.CurrentCustomPrefix, Is.Null);

            _binding.HelperHost.Receiver.HandleOscMessage(
                new uOSC.Message(OscReceiverAdapterBinding.PresetAddress, "custom", "/avatar/custom/"));

            Assert.That(_binding.CurrentPresetName, Is.EqualTo("custom"));
            Assert.That(_binding.CurrentCustomPrefix, Is.EqualTo("/avatar/custom/"));
        }

        [UnityTest]
        public IEnumerator OnStart_UdpLoopback_RegisteredInputSourceReceivesValue()
        {
            const string slug = "osc-loopback";
            int port = AllocatePort();
            OscMapping[] mappings = new OscMapping[]
            {
                new OscMapping("/avatar/parameters/smile", "smile", "emotion"),
                new OscMapping("/avatar/parameters/frown", "frown", "emotion")
            };

            _binding = CreateBinding(slug: slug, endpoint: TestEndpoint, port: port, mappings: mappings);
            AdapterBuildContext ctx = CreateContext(blendShapeNames: new List<string> { "smile", "frown" });

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            // socket bind 待ち
            yield return new WaitForSeconds(0.2f);

            _senderGameObject = new GameObject("OscAdapterBindingIntegrationSender");
            OscSender sender = _senderGameObject.AddComponent<OscSender>();
            sender.Endpoint = TestEndpoint;
            sender.Port = port;
            sender.Initialize(mappings);
            sender.StartSending();

            yield return new WaitForSeconds(0.2f);

            Assert.IsTrue(_registry.TryResolve(slug, out IInputSource source));
            Assert.IsNotNull(source);

            float[] readBuffer = new float[mappings.Length];
            bool received = false;
            for (int attempt = 0; attempt < 10 && !received; attempt++)
            {
                sender.SendAll(new float[] { 0.7f, 0.3f });
                yield return new WaitForSeconds(0.1f);
                _binding.OnFixedTick(0.02f);

                Array.Clear(readBuffer, 0, readBuffer.Length);
                if (TryReadValues(source, readBuffer) && readBuffer[0] > 0.01f)
                {
                    received = true;
                    Assert.That(readBuffer[0], Is.EqualTo(0.7f).Within(0.05f),
                        "Loopback 送信値（smile = 0.7）が registered InputSource から読めるべき。");
                    Assert.That(readBuffer[1], Is.EqualTo(0.3f).Within(0.05f),
                        "Loopback 送信値（frown = 0.3）が registered InputSource から読めるべき。");
                }
            }

            sender.StopSending();
            Assert.IsTrue(received,
                "実 UDP loopback で送信した値が registered InputSource に届くべき。");
        }

        // ---------------------------------------------------------------
        // Dispose: helper destroy + socket close
        // ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator Dispose_DestroysOscReceiverHostHelper()
        {
            int port = AllocatePort();
            _binding = CreateBinding(slug: "osc-dispose-destroy", endpoint: TestEndpoint, port: port,
                mappings: CreateDefaultMappings());
            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            OscReceiverHost helper = _hostGameObject.GetComponent<OscReceiverHost>();
            Assert.IsNotNull(helper, "OnStart 後は helper が AddComponent されているはず。");

            _binding.Dispose();
            _bindingStarted = false;

            yield return null;

            // Unity の MonoBehaviour Object 等価性: Destroy 後の参照は == null となる。
            Assert.IsTrue(helper == null,
                "Dispose 時に Object.Destroy(_helperHost) で helper が破棄されるべき。");

            OscReceiverHost remaining = _hostGameObject.GetComponent<OscReceiverHost>();
            Assert.IsNull(remaining,
                "Dispose 後の Host GameObject から OscReceiverHost が剥がれているべき。");
        }

        [UnityTest]
        public IEnumerator Dispose_ClosesUdpSocket_NewBindingCanRebindSamePort()
        {
            const string slug = "osc-dispose-socket";
            int port = AllocatePort();
            OscMapping[] mappings = CreateDefaultMappings();

            _binding = CreateBinding(slug: slug, endpoint: TestEndpoint, port: port, mappings: mappings);
            AdapterBuildContext ctx = CreateContext();
            _binding.OnStart(in ctx);
            _bindingStarted = true;

            yield return new WaitForSeconds(0.2f);

            _binding.Dispose();
            _bindingStarted = false;

            yield return null;
            yield return new WaitForSeconds(0.2f);

            // 同 port を新規 binding で再 bind できれば socket は close されている。
            var second = CreateBinding(slug: slug + "-2", endpoint: TestEndpoint, port: port, mappings: mappings);
            var secondContext = CreateContext();
            Assert.DoesNotThrow(() => second.OnStart(in secondContext),
                "Dispose 後は同 port を別 binding で再 bind できるべき（socket 解放）。");

            try
            {
                yield return new WaitForSeconds(0.1f);
            }
            finally
            {
                second.Dispose();
            }
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private OscReceiverAdapterBinding CreateBinding(string slug, string endpoint, int port, OscMapping[] mappings)
        {
            var binding = new OscReceiverAdapterBinding();
            binding.Slug = slug;
            binding.Configure(endpoint, port, mappings);
            return binding;
        }

        private AdapterBuildContext CreateContext(
            IReadOnlyList<string> blendShapeNames = null,
            ITimeProvider timeProvider = null)
        {
            return new AdapterBuildContext(
                profile: new FacialProfile("1.0"),
                blendShapeNames: blendShapeNames ?? new List<string> { "smile", "frown" },
                inputSourceRegistry: _registry,
                facialOutputBus: new FacialOutputBus(),
                timeProvider: timeProvider ?? new UnityTimeProvider(),
                hostGameObject: _hostGameObject,
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

        private static OscMapping[] CreateDefaultMappings()
        {
            return new OscMapping[]
            {
                new OscMapping("/avatar/parameters/smile", "smile", "emotion"),
                new OscMapping("/avatar/parameters/frown", "frown", "emotion")
            };
        }

        private static int AllocatePort()
        {
            // テストごとにユニークな loopback port を払い出して socket 衝突を避ける。
            int next = System.Threading.Interlocked.Increment(ref s_portCounter);
            return LoopbackPortBase + next;
        }

        private static bool TryReadValues(IInputSource source, float[] buffer)
        {
            return source.TryWriteValues(buffer.AsSpan());
        }

        private static uOSC.Message FloatMessage(string address, float value, ulong timestamp)
        {
            var message = new uOSC.Message(address, value);
            message.timestamp = new uOSC.Timestamp(timestamp);
            return message;
        }

        private static void AssertMask(BitArray mask, params bool[] expected)
        {
            Assert.That(mask.Length, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(mask[i], Is.EqualTo(expected[i]), $"mask[{i}]");
            }
        }
    }
}
