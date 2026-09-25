using System.Reflection;
using Hidano.FacialControl.LipSync.Adapters.Devices;
using Hidano.FacialControl.LipSync.Editor.Inspector;
using Hidano.FacialControl.LipSync.Tests.Shared;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.LipSync.Tests.EditMode.Editor
{
    public class DeviceDescriptorPopupTests
    {
        private DeviceDescriptor _capturedDescriptor;
        private int _changedCallCount;

        [SetUp]
        public void SetUp()
        {
            _capturedDescriptor = default;
            _changedCallCount = 0;
        }

        [Test]
        public void Create_EnumeratedAsioAndMicrophone_AddsAllNamesToPopupChoices()
        {
            var popup = CreatePopup(
                default,
                new FakeAsioDriverEnumerator("ASIO Main", "ASIO Backup"),
                new FakeMicrophoneDeviceEnumerator("Built-in Mic", "USB Mic"));

            var devicePopup = popup.Q<PopupField<string>>(DeviceDescriptorPopup.DevicePopupName);

            Assert.That(devicePopup, Is.Not.Null);
            CollectionAssert.Contains(devicePopup.choices, "ASIO Main");
            CollectionAssert.Contains(devicePopup.choices, "ASIO Backup");
            CollectionAssert.Contains(devicePopup.choices, "Built-in Mic");
            CollectionAssert.Contains(devicePopup.choices, "USB Mic");
        }

        [Test]
        public void Create_ValidDescriptor_DoesNotExposeAsioMicToggle()
        {
            var popup = CreatePopup(
                default,
                new FakeAsioDriverEnumerator("ASIO Main"),
                new FakeMicrophoneDeviceEnumerator("USB Mic"));

            Assert.That(popup.Q<Toggle>(), Is.Null);
        }

        [Test]
        public void ManualOverride_Changed_UpdatesDeviceName()
        {
            var popup = CreatePopup(
                default,
                new FakeAsioDriverEnumerator("ASIO Main"),
                new FakeMicrophoneDeviceEnumerator("USB Mic"));
            var manualOverride = popup.Q<TextField>(DeviceDescriptorPopup.ManualOverrideFieldName);

            manualOverride.SetValueWithoutNotify("Disconnected Mic");
            InvokePrivate(popup, "ApplyDeviceNameFromManualOverride", manualOverride.value);

            Assert.That(_capturedDescriptor.DeviceName, Is.EqualTo("Disconnected Mic"));
            Assert.That(_changedCallCount, Is.EqualTo(1));
        }

        [Test]
        public void PopupSelection_Changed_UpdatesDeviceNameAndManualOverride()
        {
            var popup = CreatePopup(
                default,
                new FakeAsioDriverEnumerator("ASIO Main"),
                new FakeMicrophoneDeviceEnumerator("USB Mic"));
            var devicePopup = popup.Q<PopupField<string>>(DeviceDescriptorPopup.DevicePopupName);
            var manualOverride = popup.Q<TextField>(DeviceDescriptorPopup.ManualOverrideFieldName);

            devicePopup.SetValueWithoutNotify("USB Mic");
            InvokePrivate(popup, "ApplyDeviceNameFromPopup", devicePopup.value);

            Assert.That(_capturedDescriptor.DeviceName, Is.EqualTo("USB Mic"));
            Assert.That(manualOverride.value, Is.EqualTo("USB Mic"));
        }

        [Test]
        public void DisambiguatorIndex_Changed_UpdatesDescriptor()
        {
            var popup = CreatePopup(
                default,
                new FakeAsioDriverEnumerator("ASIO Main"),
                new FakeMicrophoneDeviceEnumerator("USB Mic"));
            var disambiguatorIndex =
                popup.Q<IntegerField>(DeviceDescriptorPopup.DisambiguatorIndexFieldName);

            disambiguatorIndex.SetValueWithoutNotify(2);
            InvokePrivate(popup, "ApplyDisambiguatorIndex", disambiguatorIndex.value);

            Assert.That(_capturedDescriptor.DisambiguatorIndex, Is.EqualTo(2));
        }

        [Test]
        public void Create_WithInitialValue_DisplaysInitialDescriptor()
        {
            var initial = new DeviceDescriptor
            {
                DeviceName = "USB Mic",
                DisambiguatorIndex = 4,
            };

            var popup = CreatePopup(
                initial,
                new FakeAsioDriverEnumerator("ASIO Main"),
                new FakeMicrophoneDeviceEnumerator("USB Mic"));

            var devicePopup = popup.Q<PopupField<string>>(DeviceDescriptorPopup.DevicePopupName);
            var manualOverride = popup.Q<TextField>(DeviceDescriptorPopup.ManualOverrideFieldName);
            var disambiguatorIndex =
                popup.Q<IntegerField>(DeviceDescriptorPopup.DisambiguatorIndexFieldName);

            Assert.That(devicePopup.value, Is.EqualTo("USB Mic"));
            Assert.That(manualOverride.value, Is.EqualTo("USB Mic"));
            Assert.That(disambiguatorIndex.value, Is.EqualTo(4));
        }

        private DeviceDescriptorPopup CreatePopup(
            DeviceDescriptor initialValue,
            IAsioDriverEnumerator asioEnumerator,
            IMicrophoneDeviceEnumerator microphoneEnumerator)
        {
            return new DeviceDescriptorPopup(
                initialValue,
                descriptor =>
                {
                    _capturedDescriptor = descriptor;
                    _changedCallCount++;
                },
                asioEnumerator,
                microphoneEnumerator);
        }

        private static void InvokePrivate(
            DeviceDescriptorPopup popup,
            string methodName,
            params object[] args)
        {
            MethodInfo method = typeof(DeviceDescriptorPopup).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(popup, args);
        }
    }
}
