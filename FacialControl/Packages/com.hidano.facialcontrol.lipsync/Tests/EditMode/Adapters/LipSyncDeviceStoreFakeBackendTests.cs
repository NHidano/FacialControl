using Hidano.FacialControl.LipSync.Adapters.Devices;
using Hidano.FacialControl.LipSync.Tests.Shared;
using NUnit.Framework;
using UnityEngine;

namespace Hidano.FacialControl.LipSync.Tests.EditMode.Adapters
{
    [TestFixture]
    internal class LipSyncDeviceStoreFakeBackendTests : LipSyncDeviceStoreTestBase
    {
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

        [Test]
        public void Load_KeysMissing_ReturnsEmptyDeviceNameAndZeroDisambiguator()
        {
            var descriptor = LipSyncDeviceStore.Load();

            Assert.That(descriptor.DeviceName, Is.EqualTo(string.Empty));
            Assert.That(descriptor.DisambiguatorIndex, Is.EqualTo(0));
        }

        [Test]
        public void Load_OnlyDeviceNameKeyPresent_ReturnsZeroDisambiguator()
        {
            Backend.SetString(LipSyncDeviceStore.KeyName, "OnlyName");

            var descriptor = LipSyncDeviceStore.Load();

            Assert.That(descriptor.DeviceName, Is.EqualTo("OnlyName"));
            Assert.That(descriptor.DisambiguatorIndex, Is.EqualTo(0));
        }

        [Test]
        public void Load_OnlyDisambiguatorKeyPresent_ReturnsEmptyDeviceName()
        {
            Backend.SetInt(LipSyncDeviceStore.KeyDisambiguator, 11);

            var descriptor = LipSyncDeviceStore.Load();

            Assert.That(descriptor.DeviceName, Is.EqualTo(string.Empty));
            Assert.That(descriptor.DisambiguatorIndex, Is.EqualTo(11));
        }

        [Test]
        public void Save_Load_RoundTrip_PreservesValuesViaFakeBackend()
        {
            var saved = new DeviceDescriptor
            {
                DeviceName = "Fake-Mic",
                DisambiguatorIndex = 4,
            };

            LipSyncDeviceStore.Save(saved);
            var loaded = LipSyncDeviceStore.Load();

            Assert.That(loaded.DeviceName, Is.EqualTo("Fake-Mic"));
            Assert.That(loaded.DisambiguatorIndex, Is.EqualTo(4));
        }

        [Test]
        public void Save_ConsecutiveSaves_LatestValueIsReturnedByLoad()
        {
            LipSyncDeviceStore.Save(new DeviceDescriptor { DeviceName = "First", DisambiguatorIndex = 1 });
            LipSyncDeviceStore.Save(new DeviceDescriptor { DeviceName = "Second", DisambiguatorIndex = 2 });
            LipSyncDeviceStore.Save(new DeviceDescriptor { DeviceName = "Latest", DisambiguatorIndex = 9 });

            var loaded = LipSyncDeviceStore.Load();

            Assert.That(loaded.DeviceName, Is.EqualTo("Latest"));
            Assert.That(loaded.DisambiguatorIndex, Is.EqualTo(9));
            Assert.That(Backend.SaveCallCount, Is.EqualTo(3));
        }

        [Test]
        public void Save_NullDeviceName_LoadReturnsEmptyString()
        {
            var descriptor = new DeviceDescriptor
            {
                DeviceName = null,
                DisambiguatorIndex = 5,
            };

            LipSyncDeviceStore.Save(descriptor);
            var loaded = LipSyncDeviceStore.Load();

            Assert.That(loaded.DeviceName, Is.EqualTo(string.Empty));
            Assert.That(loaded.DisambiguatorIndex, Is.EqualTo(5));
        }

        [Test]
        public void Save_NullDeviceName_BackendStoresEmptyString()
        {
            LipSyncDeviceStore.Save(new DeviceDescriptor { DeviceName = null, DisambiguatorIndex = 0 });

            Assert.That(Backend.ContainsStringKey(LipSyncDeviceStore.KeyName), Is.True);
            Assert.That(Backend.GetString(LipSyncDeviceStore.KeyName, "fallback"), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Save_DoesNotWriteToRealPlayerPrefs_WhenFakeBackendInstalled()
        {
            var descriptor = new DeviceDescriptor
            {
                DeviceName = "Should-Not-Reach-PlayerPrefs",
                DisambiguatorIndex = 42,
            };

            LipSyncDeviceStore.Save(descriptor);

            Assert.That(PlayerPrefs.HasKey(LipSyncDeviceStore.KeyName), Is.False);
            Assert.That(PlayerPrefs.HasKey(LipSyncDeviceStore.KeyDisambiguator), Is.False);
        }

        [Test]
        public void SetBackend_ReplacesActiveBackend_LoadReadsFromNewBackend()
        {
            var replacement = new FakePlayerPrefsBackend();
            replacement.SetString(LipSyncDeviceStore.KeyName, "Replacement-Mic");
            replacement.SetInt(LipSyncDeviceStore.KeyDisambiguator, 21);

            LipSyncDeviceStore.SetBackend(replacement);
            var loaded = LipSyncDeviceStore.Load();

            Assert.That(loaded.DeviceName, Is.EqualTo("Replacement-Mic"));
            Assert.That(loaded.DisambiguatorIndex, Is.EqualTo(21));
        }

        [Test]
        public void SetBackend_ReplacesActiveBackend_SaveTargetsNewBackendOnly()
        {
            var replacement = new FakePlayerPrefsBackend();
            LipSyncDeviceStore.SetBackend(replacement);

            LipSyncDeviceStore.Save(new DeviceDescriptor { DeviceName = "After-Replace", DisambiguatorIndex = 6 });

            Assert.That(replacement.ContainsStringKey(LipSyncDeviceStore.KeyName), Is.True);
            Assert.That(replacement.GetString(LipSyncDeviceStore.KeyName, "fallback"), Is.EqualTo("After-Replace"));
            Assert.That(Backend.ContainsStringKey(LipSyncDeviceStore.KeyName), Is.False);
        }

        [Test]
        public void SetBackend_Null_DoesNotThrowOnSubsequentLoad()
        {
            LipSyncDeviceStore.SetBackend(null);

            Assert.DoesNotThrow(() => LipSyncDeviceStore.Load());
        }

        [Test]
        public void ResetBackend_AfterFakeBackendValues_LoadDoesNotReturnFakeValues()
        {
            Backend.SetString(LipSyncDeviceStore.KeyName, "Fake-Only");
            Backend.SetInt(LipSyncDeviceStore.KeyDisambiguator, 99);

            LipSyncDeviceStore.ResetBackend();
            var loaded = LipSyncDeviceStore.Load();

            Assert.That(loaded.DeviceName, Is.Not.EqualTo("Fake-Only"));
            Assert.That(loaded.DisambiguatorIndex, Is.Not.EqualTo(99));
        }
    }
}
