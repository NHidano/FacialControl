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
    public class ULipSyncAdapterBindingPhonemeOverlayTests
    {
        private const string Slug = "ulipsync";
        private const string MicDeviceName = "Unit Test Mic";

        private static readonly string[] BlendShapeNames =
        {
            "Mouth_A",
            "Mouth_I",
            "Mouth_U",
            "Mouth_E",
            "Mouth_O",
        };

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
            _hostGameObject = new GameObject("ULipSyncAdapterBindingPhonemeOverlayTestsHost");
            _hostGameObject.SetActive(false);
            _profile = CreateAnalyzerProfile();
            CreateSkinnedMeshChild();
        }

        [TearDown]
        public void TearDown()
        {
            if (_binding != null && _bindingStarted)
            {
                _binding.Dispose();
            }

            if (_profile != null)
            {
                UnityEngine.Object.DestroyImmediate(_profile);
            }

            if (_hostGameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_hostGameObject);
            }

            if (_mesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_mesh);
            }
        }

        [Test]
        public void OnStart_WithReservedSlotsDeclared_RegistersLipSyncPhonemeOverlayInputSources()
        {
            _binding = CreateBinding();
            AdapterBuildContext ctx = CreateContext(PhonemeOverlaySlots.ReservedNames.ToArray());

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            AssertRegistered(PhonemeOverlaySlots.A);
            AssertRegistered(PhonemeOverlaySlots.I);
            AssertRegistered(PhonemeOverlaySlots.U);
            AssertRegistered(PhonemeOverlaySlots.E);
            AssertRegistered(PhonemeOverlaySlots.O);
        }

        [Test]
        public void OnStart_NoReservedSlotsDeclared_LogsWarningAndSkips()
        {
            _binding = CreateBinding();
            AdapterBuildContext ctx = CreateContext(Array.Empty<string>());

            LogAssert.Expect(
                LogType.Warning,
                new Regex("ULipSyncAdapterBinding.*No reserved phoneme overlay slots"));
            _binding.OnStart(in ctx);
            _bindingStarted = true;

            Assert.That(_registry.RegisteredIds, Is.Empty);
        }

        [Test]
        public void OnStart_PartialSlotsDeclared_RegistersOnlyDeclaredSlots()
        {
            _binding = CreateBinding();
            AdapterBuildContext ctx = CreateContext(new[] { PhonemeOverlaySlots.A, PhonemeOverlaySlots.U });

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            AssertRegistered(PhonemeOverlaySlots.A);
            AssertRegistered(PhonemeOverlaySlots.U);
            AssertNotRegistered(PhonemeOverlaySlots.I);
            AssertNotRegistered(PhonemeOverlaySlots.E);
            AssertNotRegistered(PhonemeOverlaySlots.O);
        }

        [Test]
        public void OnStart_DoesNotRegisterLegacyLipSyncSource()
        {
            _binding = CreateBinding();
            AdapterBuildContext ctx = CreateContext(PhonemeOverlaySlots.ReservedNames.ToArray());

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            Assert.That(_registry.TryResolve(Slug, out IInputSource source), Is.False);
            Assert.That(source, Is.Null);
            CollectionAssert.DoesNotContain(_registry.RegisteredIds, Slug);
        }

        [UnityTest]
        public IEnumerator Dispose_UnregistersAllPhonemeOverlaySlots()
        {
            _binding = CreateBinding();
            AdapterBuildContext ctx = CreateContext(PhonemeOverlaySlots.ReservedNames.ToArray());

            _binding.OnStart(in ctx);
            _bindingStarted = true;
            Assert.That(_registry.RegisteredIds.Count, Is.EqualTo(PhonemeOverlaySlots.ReservedNames.Length));

            _binding.Dispose();
            _bindingStarted = false;

            yield return null;

            AssertNotRegistered(PhonemeOverlaySlots.A);
            AssertNotRegistered(PhonemeOverlaySlots.I);
            AssertNotRegistered(PhonemeOverlaySlots.U);
            AssertNotRegistered(PhonemeOverlaySlots.E);
            AssertNotRegistered(PhonemeOverlaySlots.O);
            Assert.That(_registry.RegisteredIds, Is.Empty);
        }

        private void AssertRegistered(string slot)
        {
            // 登録キーはレイヤーの入力源 id と同じ固定 prefix "lipsync-overlay:{slot}" でなければ
            // FacialController.ResolveLayerInputSourcesFromRegistry が解決できず集約に乗らない。
            string id = $"{LipSyncPhonemeOverlayInputSource.SlugPrefix}:{slot}";
            Assert.That(_registry.TryResolve(id, out IInputSource source), Is.True, id);
            Assert.That(source, Is.InstanceOf<LipSyncPhonemeOverlayInputSource>(), id);
        }

        private void AssertNotRegistered(string slot)
        {
            string id = $"{LipSyncPhonemeOverlayInputSource.SlugPrefix}:{slot}";
            Assert.That(_registry.TryResolve(id, out IInputSource source), Is.False, id);
            Assert.That(source, Is.Null, id);
        }

        private ULipSyncAdapterBinding CreateBinding()
        {
            var binding = new ULipSyncAdapterBinding { Slug = Slug };
            binding.Configure(
                new DeviceDescriptor
                {
                    DeviceName = MicDeviceName,
                    DisambiguatorIndex = 0,
                },
                _profile,
                CreatePhonemeEntries(),
                new FakeAsioDriverEnumerator(),
                new FakeMicrophoneDeviceEnumerator(MicDeviceName));
            return binding;
        }

        private AdapterBuildContext CreateContext(string[] slots)
        {
            return new AdapterBuildContext(
                profile: new FacialProfile("1.0", slots: slots),
                blendShapeNames: BlendShapeNames,
                inputSourceRegistry: _registry,
                facialOutputBus: new FacialOutputBus(),
                timeProvider: new UnityTimeProvider(),
                hostGameObject: _hostGameObject,
                lipSyncProvider: null);
        }

        private static PhonemeEntryBase[] CreatePhonemeEntries()
        {
            ReadOnlySpan<string> slots = PhonemeOverlaySlots.ReservedNames;
            var entries = new PhonemeEntryBase[slots.Length];
            for (int i = 0; i < slots.Length; i++)
            {
                entries[i] = new BlendShapePhonemeEntry
                {
                    PhonemeId = PhonemeOverlaySlots.MapReservedToPhonemeId(slots[i]),
                    BlendShapeName = BlendShapeNames[i],
                    MaxWeight = 100f,
                };
            }

            return entries;
        }

        private static uLipSync.Profile CreateAnalyzerProfile()
        {
            uLipSync.Profile profile = ScriptableObject.CreateInstance<uLipSync.Profile>();
            ReadOnlySpan<string> slots = PhonemeOverlaySlots.ReservedNames;
            for (int i = 0; i < slots.Length; i++)
            {
                profile.AddMfcc(PhonemeOverlaySlots.MapReservedToPhonemeId(slots[i]));
            }

            return profile;
        }

        private void CreateSkinnedMeshChild()
        {
            var meshObject = new GameObject("FaceMesh");
            meshObject.transform.SetParent(_hostGameObject.transform, false);
            var renderer = meshObject.AddComponent<SkinnedMeshRenderer>();

            _mesh = new Mesh { name = "ULipSyncAdapterBindingPhonemeOverlayTestsMesh" };
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
            for (int i = 0; i < BlendShapeNames.Length; i++)
            {
                _mesh.AddBlendShapeFrame(
                    BlendShapeNames[i],
                    100f,
                    deltaVertices,
                    deltaNormals,
                    deltaTangents);
            }

            renderer.sharedMesh = _mesh;
        }
    }
}
