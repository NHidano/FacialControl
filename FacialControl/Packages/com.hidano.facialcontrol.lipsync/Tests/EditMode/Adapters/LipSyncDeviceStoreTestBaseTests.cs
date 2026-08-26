using Hidano.FacialControl.LipSync.Adapters.Devices;
using Hidano.FacialControl.LipSync.Tests.Shared;
using NUnit.Framework;

namespace Hidano.FacialControl.LipSync.Tests.EditMode.Adapters
{
    [TestFixture]
    internal class LipSyncDeviceStoreTestBaseTests : LipSyncDeviceStoreTestBase
    {
        private bool _installInvoked;
        private IPlayerPrefsBackend _capturedInstalledBackend;

        protected override void InstallBackend(IPlayerPrefsBackend backend)
        {
            _installInvoked = true;
            _capturedInstalledBackend = backend;
        }

        [Test]
        public void SetUp_CreatesFreshFakeBackend()
        {
            Assert.That(Backend, Is.Not.Null);
            Assert.That(Backend.GetString("any", "default"), Is.EqualTo("default"));
            Assert.That(Backend.GetInt("any", 0), Is.EqualTo(0));
        }

        [Test]
        public void SetUp_InvokesInstallBackendHookWithBackend()
        {
            Assert.That(_installInvoked, Is.True);
            Assert.That(_capturedInstalledBackend, Is.SameAs(Backend));
        }

        [Test]
        public void Backend_MutationsArePersistedWithinSingleTest()
        {
            Backend.SetString("scratch", "value");

            Assert.That(Backend.GetString("scratch", "fallback"), Is.EqualTo("value"));
        }

        [Test]
        public void TearDown_AfterManualInvocation_ResetsBackendToNullAndFiresHook()
        {
            var uninstallObserver = new UninstallObservingBase();
            uninstallObserver.SetUp();
            var captured = uninstallObserver.BackendForTest;

            uninstallObserver.TearDown();

            Assert.That(captured, Is.Not.Null);
            Assert.That(uninstallObserver.BackendForTest, Is.Null);
            Assert.That(uninstallObserver.UninstallInvoked, Is.True);
        }

        private sealed class UninstallObservingBase : LipSyncDeviceStoreTestBase
        {
            public bool UninstallInvoked { get; private set; }
            public FakePlayerPrefsBackend BackendForTest => Backend;

            protected override void UninstallBackend()
            {
                UninstallInvoked = true;
            }
        }
    }
}
