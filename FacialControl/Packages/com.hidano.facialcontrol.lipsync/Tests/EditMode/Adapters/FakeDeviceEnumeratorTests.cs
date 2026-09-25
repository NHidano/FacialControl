using Hidano.FacialControl.LipSync.Tests.Shared;
using NUnit.Framework;

namespace Hidano.FacialControl.LipSync.Tests.EditMode.Adapters
{
    [TestFixture]
    public class FakeDeviceEnumeratorTests
    {
        [Test]
        public void GetDriverNames_EmptyNames_ReturnsEmptyArray()
        {
            var enumerator = new FakeAsioDriverEnumerator();

            var names = enumerator.GetDriverNames();

            CollectionAssert.IsEmpty(names);
        }

        [Test]
        public void GetDriverNames_SingleName_ReturnsConfiguredName()
        {
            var enumerator = new FakeAsioDriverEnumerator("ASIO Fireface");

            var names = enumerator.GetDriverNames();

            CollectionAssert.AreEqual(new[] { "ASIO Fireface" }, names);
        }

        [Test]
        public void GetDriverNames_DuplicateNames_ReturnsConfiguredOrder()
        {
            var enumerator = new FakeAsioDriverEnumerator("Dante ASIO", "Dante ASIO");

            var names = enumerator.GetDriverNames();

            CollectionAssert.AreEqual(new[] { "Dante ASIO", "Dante ASIO" }, names);
        }

        [Test]
        public void GetDeviceNames_EmptyNames_ReturnsEmptyArray()
        {
            var enumerator = new FakeMicrophoneDeviceEnumerator();

            var names = enumerator.GetDeviceNames();

            CollectionAssert.IsEmpty(names);
        }

        [Test]
        public void GetDeviceNames_SingleName_ReturnsConfiguredName()
        {
            var enumerator = new FakeMicrophoneDeviceEnumerator("USB Microphone");

            var names = enumerator.GetDeviceNames();

            CollectionAssert.AreEqual(new[] { "USB Microphone" }, names);
        }

        [Test]
        public void GetDeviceNames_DuplicateNames_ReturnsConfiguredOrder()
        {
            var enumerator = new FakeMicrophoneDeviceEnumerator("Dante Mic", "Dante Mic");

            var names = enumerator.GetDeviceNames();

            CollectionAssert.AreEqual(new[] { "Dante Mic", "Dante Mic" }, names);
        }
    }
}
