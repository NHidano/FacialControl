using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.LipSync.Adapters;
using Hidano.FacialControl.LipSync.Adapters.Devices;
using Hidano.FacialControl.LipSync.Adapters.PhonemeEntries;
using Hidano.FacialControl.LipSync.Tests.Shared;
using NUnit.Framework;
using UnityEngine;

namespace Hidano.FacialControl.LipSync.Tests.PlayMode.Lifecycle
{
    [TestFixture]
    internal class ULipSyncAdapterBindingDeviceStoreTests : LipSyncDeviceStoreTestBase
    {
        private const string Slug = "ulipsync";
        private const string PrimaryMicDeviceName = "DeviceStore Primary Mic";
        private const string SecondaryMicDeviceName = "DeviceStore Secondary Mic";
        private const string BlendShapeName = "Mouth_A";
        private const string PhonemeId = "A";

        private GameObject _hostGameObject;
        private InputSourceRegistry _registry;
        private ULipSyncAdapterBinding _binding;
        private uLipSync.Profile _profile;
        private Mesh _mesh;
        private bool _bindingStarted;

        protected override void InstallBackend(IPlayerPrefsBackend backend)
        {
            LipSyncDeviceStore.SetBackend(backend);
        }

        protected override void UninstallBackend()
        {
            LipSyncDeviceStore.ResetBackend();
            PlayerPrefs.DeleteKey(LipSyncDeviceStore.KeyName);
            PlayerPrefs.DeleteKey(LipSyncDeviceStore.KeyDisambiguator);
        }

        public override void SetUp()
        {
            base.SetUp();
            _registry = new InputSourceRegistry();
            _hostGameObject = new GameObject("ULipSyncAdapterBindingDeviceStoreTestsHost");
            _hostGameObject.SetActive(false);
            _profile = CreateAnalyzerProfile();
            CreateSkinnedMeshChild();
        }

        public override void TearDown()
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

            base.TearDown();
        }

        [Test]
        public void OnStart_LoadsDeviceFromStore_InitializesMicWithStoredDeviceName()
        {
            LipSyncDeviceStore.Save(new DeviceDescriptor
            {
                DeviceName = PrimaryMicDeviceName,
                DisambiguatorIndex = 0,
            });

            _binding = CreateBinding(new FakeMicrophoneDeviceEnumerator(
                "Other Mic",
                PrimaryMicDeviceName,
                SecondaryMicDeviceName));
            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            Assert.That(_binding.IsStarted, Is.True,
                "DeviceStore 経由で解決した DeviceName により binding が起動するべき。");

            var microphone = _hostGameObject.GetComponent<uLipSync.uLipSyncMicrophone>();
            Assert.That(microphone, Is.Not.Null,
                "OnStart は HostGameObject に uLipSyncMicrophone を AddComponent するべき。");
            Assert.That(microphone.index, Is.EqualTo(1),
                "Fake enumerator における PrimaryMicDeviceName の列挙 index (1) が uLipSyncMicrophone に反映されるべき。");
        }

        [Test]
        public void OnStart_DeviceStoreReturnsEmptyDeviceName_FallsBackToFirstMicrophoneAndStarts()
        {
            // DeviceStore に何も Save していないので Load は DeviceName="" を返す。
            // デバイス未選択のままでもリップシンクが全滅しないよう、既定のマイク
            // （マイク一覧の先頭）へフォールバックして binding は起動する。
            _binding = CreateBinding(new FakeMicrophoneDeviceEnumerator(
                PrimaryMicDeviceName,
                SecondaryMicDeviceName));
            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            Assert.That(_binding.IsStarted, Is.True,
                "DeviceStore に DeviceName 未保存 (空文字) のとき、既定マイクへフォールバックして起動するべき。");

            var microphone = _hostGameObject.GetComponent<uLipSync.uLipSyncMicrophone>();
            Assert.That(microphone, Is.Not.Null,
                "フォールバック起動時も uLipSyncMicrophone が AddComponent されるべき。");
            Assert.That(microphone.index, Is.EqualTo(0),
                "フォールバックはマイク一覧の先頭 (index 0) を使用するべき。");
        }

        [Test]
        public void OnStart_EmptyDeviceNameAndNoMicrophones_LogsErrorAndDoesNotStart()
        {
            // デバイス未選択かつマイクが 1 台も無い場合は従来どおり未解決エラーで起動しない。
            _binding = CreateBinding(new FakeMicrophoneDeviceEnumerator());
            AdapterBuildContext ctx = CreateContext();

            UnityEngine.TestTools.LogAssert.Expect(
                LogType.Error,
                new System.Text.RegularExpressions.Regex("ULipSyncAdapterBinding.*could not be resolved"));
            _binding.OnStart(in ctx);

            Assert.That(_binding.IsStarted, Is.False,
                "デバイス未選択かつマイク 0 台のとき、binding は起動してはならない。");
            Assert.That(_hostGameObject.GetComponent<uLipSync.uLipSyncMicrophone>(), Is.Null,
                "未解決のとき uLipSyncMicrophone は AddComponent されるべきでない。");
        }

        [Test]
        public void OnStart_FakeBackendInstalled_DoesNotWriteToRealPlayerPrefs()
        {
            LipSyncDeviceStore.Save(new DeviceDescriptor
            {
                DeviceName = PrimaryMicDeviceName,
                DisambiguatorIndex = 0,
            });

            _binding = CreateBinding(new FakeMicrophoneDeviceEnumerator(PrimaryMicDeviceName));
            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            Assert.That(_binding.IsStarted, Is.True);
            Assert.That(PlayerPrefs.HasKey(LipSyncDeviceStore.KeyName), Is.False,
                "Fake backend 経由なので実 PlayerPrefs (DeviceName キー) に書き込まれてはならない。");
            Assert.That(PlayerPrefs.HasKey(LipSyncDeviceStore.KeyDisambiguator), Is.False,
                "Fake backend 経由なので実 PlayerPrefs (Disambiguator キー) に書き込まれてはならない。");
            Assert.That(Backend.ContainsStringKey(LipSyncDeviceStore.KeyName), Is.True,
                "Save 後は Fake backend に DeviceName キーが格納されているべき。");
            Assert.That(Backend.GetString(LipSyncDeviceStore.KeyName, "fallback"),
                Is.EqualTo(PrimaryMicDeviceName));
        }

        private ULipSyncAdapterBinding CreateBinding(IMicrophoneDeviceEnumerator micEnumerator)
        {
            var binding = new ULipSyncAdapterBinding { Slug = Slug };
            binding.Configure(
                _profile,
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
                micEnumerator);
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

            _mesh = new Mesh { name = "ULipSyncAdapterBindingDeviceStoreTestsMesh" };
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
    }
}
