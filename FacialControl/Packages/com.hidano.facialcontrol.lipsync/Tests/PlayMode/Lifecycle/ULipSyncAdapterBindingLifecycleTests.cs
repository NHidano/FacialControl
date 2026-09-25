using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.LipSync.Adapters;
using Hidano.FacialControl.LipSync.Adapters.Devices;
using Hidano.FacialControl.LipSync.Adapters.PhonemeEntries;
using Hidano.FacialControl.LipSync.Tests.Shared;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.LipSync.Tests.PlayMode.Lifecycle
{
    [TestFixture]
    public class ULipSyncAdapterBindingLifecycleTests
    {
        private const string Slug = "ulipsync";
        private const string OverlayASlug = "lipsync-overlay:a";
        private const string MicDeviceName = "Unit Test Mic";
        private const string MissingDeviceName = "Missing Mic";
        private const string BlendShapeName = "Mouth_A";
        private const string PhonemeId = "A";

        private GameObject _hostGameObject;
        private InputSourceRegistry _registry;
        private ULipSyncAdapterBinding _binding;
        private uLipSync.Profile _profile;
        private Mesh _mesh;
        private bool _bindingStarted;

        [SetUp]
        public void SetUp()
        {
            _registry = new InputSourceRegistry();
            _hostGameObject = new GameObject("ULipSyncAdapterBindingLifecycleTestsHost");
            _hostGameObject.SetActive(false);
            _profile = CreateAnalyzerProfile();
            CreateSkinnedMeshChild();
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
                    // TearDown では本体の assertion を優先する。
                }
            }

            _binding = null;
            _bindingStarted = false;

            if (_profile != null)
            {
                UnityEngine.Object.DestroyImmediate(_profile);
                _profile = null;
            }

            if (_hostGameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_hostGameObject);
                _hostGameObject = null;
            }

            if (_mesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_mesh);
                _mesh = null;
            }
        }

        [Test]
        public void OnStart_ResolvedMicDevice_AddsAudioSourceAnalyzerAndMicrophoneInOrder()
        {
            _binding = CreateBinding();
            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            Component[] components = _hostGameObject.GetComponents<Component>();
            int audioSourceIndex = IndexOfComponent<AudioSource>(components);
            int analyzerIndex = IndexOfComponent<uLipSync.uLipSync>(components);
            int microphoneIndex = IndexOfComponent<uLipSync.uLipSyncMicrophone>(components);

            Assert.That(audioSourceIndex, Is.GreaterThan(0),
                "OnStart は HostGameObject に AudioSource を AddComponent するべき。");
            Assert.That(analyzerIndex, Is.GreaterThan(audioSourceIndex),
                "OnStart の AddComponent 順序は AudioSource -> uLipSync.uLipSync であるべき。");
            Assert.That(microphoneIndex, Is.GreaterThan(analyzerIndex),
                "Mic 経路の AddComponent 順序は AudioSource -> uLipSync.uLipSync -> uLipSyncMicrophone であるべき。");

            Assert.That(_binding.Provider, Is.Not.Null, "OnStart 成功後は Provider が構築済みであるべき。");
            Assert.That(_binding.Analyzer, Is.Not.Null, "OnStart 成功後は Analyzer が構築済みであるべき。");
            Assert.That(_binding.Analyzer, Is.SameAs(components[analyzerIndex]));
            Assert.That(_binding.Analyzer.profile, Is.SameAs(_profile),
                "設定済み Analyzer Profile が uLipSync.uLipSync に注入されるべき。");

            var microphone = (uLipSync.uLipSyncMicrophone)components[microphoneIndex];
            Assert.That(microphone.index, Is.EqualTo(0),
                "Fake enumerator で解決した Mic の列挙 index が uLipSyncMicrophone に反映されるべき。");

            Assert.That(_registry.TryResolve(OverlayASlug, out IInputSource source), Is.True,
                "OnStart 成功後は phoneme overlay source が登録されるべき。");
            Assert.That(source, Is.InstanceOf<LipSyncPhonemeOverlayInputSource>());
            Assert.That(_registry.TryResolve(Slug, out _), Is.False);
        }

        [Test]
        public void OnStart_ResolvedMicDevice_FirstProviderReadIsZeroSettled()
        {
            _binding = CreateBinding();
            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            var info = new uLipSync.LipSyncInfo
            {
                phoneme = PhonemeId,
                volume = 1f,
                rawVolume = 1f,
                phonemeRatios = new Dictionary<string, float>
                {
                    { PhonemeId, 1f },
                },
            };
            _binding.Analyzer.onLipSyncUpdate.Invoke(info);

            var output = new float[] { -1f };
            _binding.Provider.GetLipSyncValues(output);

            Assert.That(output[0], Is.EqualTo(0f).Within(1e-6f),
                "OnStart 末尾の RequestZeroOutputForNextFrame により初回 GetLipSyncValues はゼロ化されるべき。");

            _binding.Provider.GetLipSyncValues(output);
            Assert.That(output[0], Is.GreaterThan(0f),
                "zero settle は初回読み出しだけに適用され、受信済みの非ゼロ値は次回以降に読めるべき。");
        }

        [UnityTest]
        public IEnumerator Dispose_AfterStart_RemovesAllAddedComponentsAndUnregistersInputSource()
        {
            _binding = CreateBinding();
            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            var audioSource = _hostGameObject.GetComponent<AudioSource>();
            var analyzer = _hostGameObject.GetComponent<uLipSync.uLipSync>();
            var microphone = _hostGameObject.GetComponent<uLipSync.uLipSyncMicrophone>();
            Assert.That(audioSource, Is.Not.Null);
            Assert.That(analyzer, Is.Not.Null);
            Assert.That(microphone, Is.Not.Null);
            Assert.That(_registry.TryResolve(OverlayASlug, out _), Is.True);

            _binding.Dispose();
            _bindingStarted = false;

            yield return null;

            Assert.That(_binding.IsStarted, Is.False);
            Assert.That(_binding.Provider, Is.Null);
            Assert.That(_binding.Analyzer, Is.Null);
            Assert.That(audioSource == null, Is.True);
            Assert.That(analyzer == null, Is.True);
            Assert.That(microphone == null, Is.True);
            Assert.That(_hostGameObject.GetComponent<AudioSource>(), Is.Null);
            Assert.That(_hostGameObject.GetComponent<uLipSync.uLipSync>(), Is.Null);
            Assert.That(_hostGameObject.GetComponent<uLipSync.uLipSyncMicrophone>(), Is.Null);
            Assert.That(_registry.TryResolve(OverlayASlug, out var removed), Is.False);
            Assert.That(removed, Is.Null);
            CollectionAssert.DoesNotContain(_registry.RegisteredIds, OverlayASlug);
        }

        [Test]
        public void OnFixedTick_WhenSwapIsNotPending_IsNoOp()
        {
            _binding = CreateBinding();
            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            var analyzer = _binding.Analyzer;
            var provider = _binding.Provider;
            Assert.That(_registry.TryResolve(OverlayASlug, out IInputSource inputSource), Is.True);

            _binding.OnFixedTick(0.02f);
            _binding.OnFixedTick(0.02f);

            Assert.That(_binding.IsStarted, Is.True);
            Assert.That(_binding.Analyzer, Is.SameAs(analyzer));
            Assert.That(_binding.Provider, Is.SameAs(provider));
            Assert.That(_registry.TryResolve(OverlayASlug, out IInputSource resolved), Is.True);
            Assert.That(resolved, Is.SameAs(inputSource));
        }

        [Test]
        public void OnStart_UnresolvedDevice_LogsErrorAndDoesNotRegister()
        {
            _binding = CreateBinding(MissingDeviceName, _profile);
            AdapterBuildContext ctx = CreateContext();

            LogAssert.Expect(
                LogType.Error,
                new Regex("ULipSyncAdapterBinding.*Missing Mic.*could not be resolved.*Microphone=\\[Unit Test Mic\\]"));
            _binding.OnStart(in ctx);

            Assert.That(_binding.IsStarted, Is.False);
            Assert.That(_binding.Provider, Is.Null);
            Assert.That(_binding.Analyzer, Is.Null);
            Assert.That(_hostGameObject.GetComponent<AudioSource>(), Is.Null);
            Assert.That(_hostGameObject.GetComponent<uLipSync.uLipSync>(), Is.Null);
            Assert.That(_hostGameObject.GetComponent<uLipSync.uLipSyncMicrophone>(), Is.Null);
            Assert.That(_hostGameObject.GetComponent<uLipSync.uLipSyncAsioInput>(), Is.Null);
            Assert.That(_registry.TryResolve(OverlayASlug, out var source), Is.False);
            Assert.That(source, Is.Null);
            CollectionAssert.DoesNotContain(_registry.RegisteredIds, OverlayASlug);
        }

        [Test]
        public void OnStart_AnalyzerProfileMissing_UsesPackagedDefaultProfile()
        {
            _binding = CreateBinding(MicDeviceName, null);
            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            Assert.That(_binding.IsStarted, Is.True);
            Assert.That(_binding.Provider, Is.Not.Null);
            Assert.That(_binding.Analyzer, Is.Not.Null);
            Assert.That(_binding.Analyzer.profile, Is.Not.Null);
            Assert.That(_binding.Analyzer.profile.name, Is.EqualTo("Default uLipSync Profile"));
            Assert.That(_hostGameObject.GetComponent<AudioSource>(), Is.Not.Null);
            Assert.That(_hostGameObject.GetComponent<uLipSync.uLipSync>(), Is.Not.Null);
            Assert.That(_hostGameObject.GetComponent<uLipSync.uLipSyncMicrophone>(), Is.Not.Null);
            Assert.That(_hostGameObject.GetComponent<uLipSync.uLipSyncAsioInput>(), Is.Null);
            Assert.That(_registry.TryResolve(OverlayASlug, out IInputSource source), Is.True);
            Assert.That(source, Is.InstanceOf<LipSyncPhonemeOverlayInputSource>());
        }

        [Test]
        public void OnStart_DuplicateBindingOnSameCharacter_LogsErrorAndSkips()
        {
            _binding = CreateBinding();
            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            int analyzerCountBefore = _hostGameObject.GetComponents<uLipSync.uLipSync>().Length;
            var duplicate = CreateBinding();

            LogAssert.Expect(
                LogType.Error,
                new Regex("ULipSyncAdapterBinding.*lipsync-overlay:a.*already registered"));
            duplicate.OnStart(in ctx);

            Assert.That(duplicate.IsStarted, Is.False);
            Assert.That(duplicate.Provider, Is.Null);
            Assert.That(_hostGameObject.GetComponents<uLipSync.uLipSync>().Length, Is.EqualTo(analyzerCountBefore));
            Assert.That(_registry.TryResolve(OverlayASlug, out IInputSource source), Is.True);
            Assert.That(source, Is.InstanceOf<LipSyncPhonemeOverlayInputSource>());
        }

        private ULipSyncAdapterBinding CreateBinding()
        {
            return CreateBinding(MicDeviceName, _profile);
        }

        private ULipSyncAdapterBinding CreateBinding(string deviceName, uLipSync.Profile analyzerProfile)
        {
            var binding = new ULipSyncAdapterBinding { Slug = Slug };
            binding.Configure(
                new DeviceDescriptor
                {
                    DeviceName = deviceName,
                    DisambiguatorIndex = 0,
                },
                analyzerProfile,
                new PhonemeEntryBase[]
                {
                    new BlendShapePhonemeEntry
                    {
                        PhonemeId = PhonemeId,
                        BlendShapeName = BlendShapeName,
                        MaxWeight = 80f,
                    },
                },
                new FakeAsioDriverEnumerator(),
                new FakeMicrophoneDeviceEnumerator(MicDeviceName));
            return binding;
        }

        private AdapterBuildContext CreateContext()
        {
            return new AdapterBuildContext(
                profile: new FacialProfile("1.0", slots: new[] { PhonemeOverlaySlots.A }),
                blendShapeNames: new List<string> { BlendShapeName },
                inputSourceRegistry: _registry,
                facialOutputBus: new FacialOutputBus(),
                timeProvider: new UnityTimeProvider(),
                hostGameObject: _hostGameObject,
                lipSyncProvider: null);
        }

        private static uLipSync.Profile CreateAnalyzerProfile()
        {
            uLipSync.Profile profile = ScriptableObject.CreateInstance<uLipSync.Profile>();
            profile.AddMfcc(PhonemeId);
            return profile;
        }

        private void CreateSkinnedMeshChild()
        {
            var meshObject = new GameObject("FaceMesh");
            meshObject.transform.SetParent(_hostGameObject.transform, false);
            var renderer = meshObject.AddComponent<SkinnedMeshRenderer>();

            _mesh = new Mesh { name = "ULipSyncAdapterBindingLifecycleTestsMesh" };
            _mesh.vertices = new[]
            {
                Vector3.zero,
                Vector3.right,
                Vector3.up,
            };
            _mesh.triangles = new[] { 0, 1, 2 };

            Vector3[] deltaVertices = new[]
            {
                Vector3.up * 0.01f,
                Vector3.up * 0.01f,
                Vector3.up * 0.01f,
            };
            Vector3[] deltaNormals = new Vector3[deltaVertices.Length];
            Vector3[] deltaTangents = new Vector3[deltaVertices.Length];
            _mesh.AddBlendShapeFrame(BlendShapeName, 100f, deltaVertices, deltaNormals, deltaTangents);
            renderer.sharedMesh = _mesh;
        }

        private static int IndexOfComponent<T>(Component[] components)
            where T : Component
        {
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] is T)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
