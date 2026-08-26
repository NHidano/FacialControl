using Hidano.FacialControl.LipSync.Tests.Shared;
using NUnit.Framework;
using UnityEngine;

namespace Hidano.FacialControl.LipSync.Tests.EditMode.Adapters
{
    [TestFixture]
    public class FakePlayerPrefsBackendTests
    {
        private const string TestStringKey = "Hidano.FacialControl.Tests.FakePlayerPrefsBackend.String";
        private const string TestIntKey = "Hidano.FacialControl.Tests.FakePlayerPrefsBackend.Int";

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(TestStringKey);
            PlayerPrefs.DeleteKey(TestIntKey);
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(TestStringKey);
            PlayerPrefs.DeleteKey(TestIntKey);
            PlayerPrefs.Save();
        }

        [Test]
        public void GetString_KeyMissing_ReturnsProvidedDefault()
        {
            var backend = new FakePlayerPrefsBackend();

            var value = backend.GetString(TestStringKey, "fallback");

            Assert.That(value, Is.EqualTo("fallback"));
        }

        [Test]
        public void SetString_ThenGetString_ReturnsStoredValue()
        {
            var backend = new FakePlayerPrefsBackend();

            backend.SetString(TestStringKey, "stored-value");

            Assert.That(backend.GetString(TestStringKey, "fallback"), Is.EqualTo("stored-value"));
        }

        [Test]
        public void SetString_OverwritesExistingValue()
        {
            var backend = new FakePlayerPrefsBackend();
            backend.SetString(TestStringKey, "first");

            backend.SetString(TestStringKey, "second");

            Assert.That(backend.GetString(TestStringKey, "fallback"), Is.EqualTo("second"));
        }

        [Test]
        public void GetInt_KeyMissing_ReturnsProvidedDefault()
        {
            var backend = new FakePlayerPrefsBackend();

            var value = backend.GetInt(TestIntKey, 7);

            Assert.That(value, Is.EqualTo(7));
        }

        [Test]
        public void SetInt_ThenGetInt_ReturnsStoredValue()
        {
            var backend = new FakePlayerPrefsBackend();

            backend.SetInt(TestIntKey, 42);

            Assert.That(backend.GetInt(TestIntKey, -1), Is.EqualTo(42));
        }

        [Test]
        public void SetInt_OverwritesExistingValue()
        {
            var backend = new FakePlayerPrefsBackend();
            backend.SetInt(TestIntKey, 1);

            backend.SetInt(TestIntKey, 99);

            Assert.That(backend.GetInt(TestIntKey, -1), Is.EqualTo(99));
        }

        [Test]
        public void SetString_DoesNotWriteToRealPlayerPrefs()
        {
            var backend = new FakePlayerPrefsBackend();

            backend.SetString(TestStringKey, "fake-only");

            Assert.That(PlayerPrefs.HasKey(TestStringKey), Is.False);
            Assert.That(backend.ContainsStringKey(TestStringKey), Is.True);
        }

        [Test]
        public void SetInt_DoesNotWriteToRealPlayerPrefs()
        {
            var backend = new FakePlayerPrefsBackend();

            backend.SetInt(TestIntKey, 999);

            Assert.That(PlayerPrefs.HasKey(TestIntKey), Is.False);
            Assert.That(backend.ContainsIntKey(TestIntKey), Is.True);
        }

        [Test]
        public void Save_AfterMutations_DoesNotPersistToRealPlayerPrefs()
        {
            var backend = new FakePlayerPrefsBackend();
            backend.SetString(TestStringKey, "fake-only");
            backend.SetInt(TestIntKey, 999);

            backend.Save();

            Assert.That(PlayerPrefs.HasKey(TestStringKey), Is.False);
            Assert.That(PlayerPrefs.HasKey(TestIntKey), Is.False);
        }

        [Test]
        public void Save_IsCounted()
        {
            var backend = new FakePlayerPrefsBackend();

            backend.Save();
            backend.Save();

            Assert.That(backend.SaveCallCount, Is.EqualTo(2));
        }
    }
}
