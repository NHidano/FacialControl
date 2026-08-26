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
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Tests.PlayMode.Integration
{
    [TestFixture]
    public sealed class OscReceiverAdapterBindingAutoMappingIntegrationTests
    {
        private const string Endpoint = "127.0.0.1";
        private const int LoopbackPortBase = 19630;
        private const string Slug = "osc-auto-mapping";

        private static int s_portCounter;

        private GameObject _host;
        private GameObject _meshObject;
        private Mesh _mesh;
        private SkinnedMeshRenderer _renderer;
        private InputSourceRegistry _registry;
        private OscReceiverAdapterBinding _binding;
        private bool _bindingStarted;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("OscReceiverAutoMappingHost");
            _registry = new InputSourceRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            if (_binding != null && _bindingStarted)
            {
                _binding.Dispose();
            }

            _binding = null;
            _bindingStarted = false;

            if (_meshObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_meshObject);
                _meshObject = null;
            }

            if (_host != null)
            {
                UnityEngine.Object.DestroyImmediate(_host);
                _host = null;
            }

            if (_mesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_mesh);
                _mesh = null;
            }
        }

        [Test]
        public void OnStart_EmptyMappingsAndVrChatHeartbeat_UpdatesSkinnedMeshRendererWeight()
        {
            StartBindingWithMesh("smile", "frown");

            HandleHeartbeat("smile", "frown");
            _binding.OnFixedTick(0.02f);
            SendOscValue("/avatar/parameters/smile", 0.72f);
            _binding.OnFixedTick(0.02f);
            ApplyRegisteredSourceToRenderer();

            Assert.That(_binding.RuntimeMappings.Count, Is.EqualTo(2));
            Assert.That(_binding.RuntimeMappings[0].OscAddress, Is.EqualTo("/avatar/parameters/smile"));
            Assert.That(_binding.GetMappingOrigin(0), Is.EqualTo(OscReceiverAdapterBinding.MappingOrigin.HeartbeatAuto));
            Assert.That(_renderer.GetBlendShapeWeight(0), Is.EqualTo(72f).Within(0.01f));
            Assert.That(_renderer.GetBlendShapeWeight(1), Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void OnStart_EmptyMappingsAndArKitHeartbeat_UpdatesSkinnedMeshRendererWeight()
        {
            const string arkitName = "eyeBlinkLeft";
            StartBindingWithMesh(arkitName);

            SendPreset("arkit");
            HandleHeartbeat(arkitName);
            _binding.OnFixedTick(0.02f);
            SendOscValue("/ARKit/" + arkitName, 0.41f);
            _binding.OnFixedTick(0.02f);
            ApplyRegisteredSourceToRenderer();

            Assert.That(_binding.CurrentPreset, Is.EqualTo(AddressPresetKind.ARKit));
            Assert.That(_binding.RuntimeMappings[0].OscAddress, Is.EqualTo("/ARKit/" + arkitName));
            Assert.That(_renderer.GetBlendShapeWeight(0), Is.EqualTo(41f).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator OnStart_EmptyMappingsAndNoHeartbeat_DoesNotRegisterOscInputSourceOrChangeRenderer()
        {
            StartBindingWithMesh("smile");

            yield return null;
            SendOscValue("/avatar/parameters/smile", 0.9f);
            _binding.OnFixedTick(0.02f);

            Assert.That(_binding.InputSource, Is.Null);
            Assert.That(_registry.TryResolve(Slug, out _), Is.False);
            Assert.That(_renderer.GetBlendShapeWeight(0), Is.EqualTo(0f).Within(0.01f));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void OnStart_VrChatXyLeftRightIndependent_LogsRuntimeWarningOnceAndKeepsRoute()
        {
            var mappings = new[]
            {
                new OscMappingEntry
                {
                    mode = OscMappingMode.Gaze_VRChat_XY,
                    expressionId = "look",
                    addressPattern = "/avatar/parameters/look",
                    leftRightIndependent = true,
                    sourceIdLeft = "look.left",
                    sourceIdRight = "look.right"
                },
                new OscMappingEntry
                {
                    mode = OscMappingMode.Gaze_VRChat_XY,
                    expressionId = "look2",
                    addressPattern = "/avatar/parameters/look2",
                    leftRightIndependent = true,
                    sourceIdLeft = "look2.left",
                    sourceIdRight = "look2.right"
                }
            };

            LogAssert.Expect(
                LogType.Warning,
                new System.Text.RegularExpressions.Regex("VRChat_XY.*Vector2.*左右には同値"));
            StartBindingWithMesh(mappings, "smile");

            Assert.That(_registry.TryResolve(Slug + ":look.left", out _), Is.True);
            Assert.That(_registry.TryResolve(Slug + ":look.right", out _), Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void OnStart_PartialManualMappingsAndHeartbeat_AppendsDiffPreservingManualAddress()
        {
            StartBindingWithMesh(
                new[]
                {
                    new OscMappingEntry
                    {
                        mode = OscMappingMode.Normal_BlendShape,
                        expressionId = "smile",
                        addressPattern = "/manual/smile"
                    }
                },
                "smile",
                "frown");

            HandleHeartbeat("smile", "frown");
            _binding.OnFixedTick(0.02f);
            SendOscValue("/manual/smile", 0.25f);
            SendOscValue("/avatar/parameters/frown", 0.6f);
            _binding.OnFixedTick(0.02f);
            ApplyRegisteredSourceToRenderer();

            Assert.That(_binding.RuntimeMappings.Count, Is.EqualTo(2));
            Assert.That(_binding.RuntimeMappings[0].OscAddress, Is.EqualTo("/manual/smile"));
            Assert.That(_binding.RuntimeMappings[1].OscAddress, Is.EqualTo("/avatar/parameters/frown"));
            Assert.That(_binding.GetMappingOrigin(0), Is.EqualTo(OscReceiverAdapterBinding.MappingOrigin.Manual));
            Assert.That(_binding.GetMappingOrigin(1), Is.EqualTo(OscReceiverAdapterBinding.MappingOrigin.HeartbeatAuto));
            Assert.That(_renderer.GetBlendShapeWeight(0), Is.EqualTo(25f).Within(0.01f));
            Assert.That(_renderer.GetBlendShapeWeight(1), Is.EqualTo(60f).Within(0.01f));
        }

        [Test]
        public void HandleHeartbeat_CustomPresetWithPrefix_GeneratesCustomAddressAndUpdatesRenderer()
        {
            StartBindingWithMesh("smile");

            SendPreset("custom", "/custom/");
            HandleHeartbeat("smile");
            _binding.OnFixedTick(0.02f);
            SendOscValue("/custom/smile", 0.33f);
            _binding.OnFixedTick(0.02f);
            ApplyRegisteredSourceToRenderer();

            Assert.That(_binding.CurrentPreset, Is.EqualTo(AddressPresetKind.Custom));
            Assert.That(_binding.CurrentCustomPrefix, Is.EqualTo("/custom/"));
            Assert.That(_binding.RuntimeMappings[0].OscAddress, Is.EqualTo("/custom/smile"));
            Assert.That(_renderer.GetBlendShapeWeight(0), Is.EqualTo(33f).Within(0.01f));
        }

        [Test]
        public void HandleHeartbeat_HeartbeatHashUnchanged_DoesNotRebuildOscInputSource()
        {
            StartBindingWithMesh("smile", "frown");
            HandleHeartbeat("smile", "frown");
            _binding.OnFixedTick(0.02f);

            OscInputSource source = _binding.InputSource;
            OscDoubleBuffer buffer = _binding.Buffer;
            uint hash = _binding.LastHeartbeatHash;

            HandleHeartbeat("smile", "frown");
            _binding.OnFixedTick(0.02f);

            Assert.That(_binding.InputSource, Is.SameAs(source));
            Assert.That(_binding.Buffer, Is.SameAs(buffer));
            Assert.That(_binding.LastHeartbeatHash, Is.EqualTo(hash));
            Assert.That(_binding.RuntimeMappings.Count, Is.EqualTo(2));
        }

        [Test]
        public void OnFixedTick_EmptyIntersection_LogsWarningOnceAndKeepsInputSourceUnregistered()
        {
            StartBindingWithMesh("smile");

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("heartbeat.*mesh BlendShape intersection is empty"));
            HandleHeartbeat("other");
            _binding.OnFixedTick(0.02f);
            HandleHeartbeat("other");
            _binding.OnFixedTick(0.02f);

            Assert.That(_binding.RuntimeMappings.Count, Is.EqualTo(0));
            Assert.That(_binding.InputSource, Is.Null);
            Assert.That(_registry.TryResolve(Slug, out _), Is.False);
        }

        [Test]
        public void Dispose_HeartbeatDrivenMappingsAllocated_ReleasesRuntimeState()
        {
            StartBindingWithMesh("smile");
            HandleHeartbeat("smile");
            _binding.OnFixedTick(0.02f);

            Assert.That(_binding.RuntimeMappings.Count, Is.EqualTo(1));
            Assert.That(_binding.InputSource, Is.Not.Null);

            _binding.Dispose();
            _bindingStarted = false;

            Assert.That(_binding.RuntimeMappings.Count, Is.EqualTo(0));
            Assert.That(_binding.MappingOrigins.Count, Is.EqualTo(0));
            Assert.That(_binding.InputSource, Is.Null);
            Assert.That(_binding.Buffer, Is.Null);
            Assert.That(_binding.LastHeartbeatHash, Is.EqualTo(0u));
        }

        private void StartBindingWithMesh(params string[] blendShapeNames)
        {
            StartBindingWithMesh(Array.Empty<OscMappingEntry>(), blendShapeNames);
        }

        private void StartBindingWithMesh(IReadOnlyList<OscMappingEntry> mappings, params string[] blendShapeNames)
        {
            CreateRenderer(blendShapeNames);
            _binding = new OscReceiverAdapterBinding
            {
                Slug = Slug,
                Endpoint = Endpoint,
                Port = AllocatePort(),
                BundleMode = BundleInterpretationMode.IndividualMessage,
                Mappings = new List<OscMappingEntry>(mappings)
            };

            AdapterBuildContext ctx = new AdapterBuildContext(
                profile: CreateProfile(),
                blendShapeNames: blendShapeNames,
                inputSourceRegistry: _registry,
                facialOutputBus: new FacialOutputBus(),
                timeProvider: new UnityTimeProvider(),
                hostGameObject: _host,
                lipSyncProvider: null);

            _binding.OnStart(in ctx);
            _bindingStarted = true;
            Assert.That(_binding.IsStarted, Is.True);
        }

        private void CreateRenderer(params string[] blendShapeNames)
        {
            _mesh = new Mesh { name = "OscReceiverAutoMappingMesh" };
            _mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            _mesh.triangles = new[] { 0, 1, 2 };

            var deltas = new[] { Vector3.zero, Vector3.zero, Vector3.zero };
            for (int i = 0; i < blendShapeNames.Length; i++)
            {
                _mesh.AddBlendShapeFrame(blendShapeNames[i], 100f, deltas, null, null);
            }

            _meshObject = new GameObject("OscReceiverAutoMappingRenderer");
            _renderer = _meshObject.AddComponent<SkinnedMeshRenderer>();
            _renderer.sharedMesh = _mesh;
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

        private void SendPreset(string presetName, string customPrefix = null)
        {
            uOSC.Message message = customPrefix == null
                ? new uOSC.Message(OscReceiverAdapterBinding.PresetAddress, presetName)
                : new uOSC.Message(OscReceiverAdapterBinding.PresetAddress, presetName, customPrefix);
            _binding.HelperHost.Receiver.HandleOscMessage(message);
        }

        private void SendOscValue(string address, float value)
        {
            _binding.HelperHost.Receiver.HandleOscMessage(new uOSC.Message(address, value));
        }

        private void ApplyRegisteredSourceToRenderer()
        {
            Assert.That(_registry.TryResolve(Slug, out IInputSource source), Is.True);
            var values = new float[_renderer.sharedMesh.blendShapeCount];
            Assert.That(source.TryWriteValues(values), Is.True);

            for (int i = 0; i < values.Length; i++)
            {
                _renderer.SetBlendShapeWeight(i, values[i] * 100f);
            }
        }

        private static FacialProfile CreateProfile()
        {
            return new FacialProfile(
                "2.0",
                new[]
                {
                    new LayerDefinition("emotion", 0, ExclusionMode.LastWins)
                },
                expressions: null,
                rendererPaths: null,
                layerInputSources: new[]
                {
                    new[]
                    {
                        new InputSourceDeclaration(Slug, 1f, null)
                    }
                });
        }

        private static int AllocatePort()
        {
            int next = System.Threading.Interlocked.Increment(ref s_portCounter);
            return LoopbackPortBase + next;
        }
    }
}
