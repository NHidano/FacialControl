using System;
using System.Collections.Generic;
using Hidano.FacialControl.LipSync.Adapters.Devices;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.LipSync.Editor.Inspector
{
    public sealed class DeviceDescriptorPopup : VisualElement
    {
        public const string RootClassName = "facial-control-lipsync-device-descriptor-popup";
        public const string DevicePopupName = "ulipsync-device-descriptor-popup";
        public const string ManualOverrideFieldName = "ulipsync-device-descriptor-manual-override";
        public const string DisambiguatorIndexFieldName = "ulipsync-device-descriptor-disambiguator-index";

        private readonly Action<DeviceDescriptor> _onValueChanged;
        private readonly List<string> _choices = new List<string>();

        private DeviceDescriptor _currentDescriptor;
        private PopupField<string> _devicePopup;
        private TextField _manualOverrideField;
        private IntegerField _disambiguatorIndexField;

        public DeviceDescriptorPopup(
            DeviceDescriptor initialValue,
            Action<DeviceDescriptor> onValueChanged)
            : this(
                initialValue,
                onValueChanged,
                new DefaultAsioDriverEnumerator(),
                new DefaultMicrophoneDeviceEnumerator())
        {
        }

        public DeviceDescriptorPopup(
            DeviceDescriptor initialValue,
            Action<DeviceDescriptor> onValueChanged,
            IAsioDriverEnumerator asioEnumerator,
            IMicrophoneDeviceEnumerator microphoneEnumerator)
        {
            _onValueChanged = onValueChanged
                ?? throw new ArgumentNullException(nameof(onValueChanged));
            if (asioEnumerator == null)
            {
                throw new ArgumentNullException(nameof(asioEnumerator));
            }

            if (microphoneEnumerator == null)
            {
                throw new ArgumentNullException(nameof(microphoneEnumerator));
            }

            _currentDescriptor = new DeviceDescriptor
            {
                DeviceName = initialValue.DeviceName ?? string.Empty,
                DisambiguatorIndex = initialValue.DisambiguatorIndex,
            };

            AddToClassList(RootClassName);
            CollectChoices(asioEnumerator, microphoneEnumerator);
            Build();
        }

        public DeviceDescriptor CurrentDescriptor => _currentDescriptor;

        private void Build()
        {
            string currentDeviceName = _currentDescriptor.DeviceName ?? string.Empty;
            int currentChoiceIndex = FindChoiceIndex(currentDeviceName);

            _devicePopup = new PopupField<string>("入力デバイス", _choices, currentChoiceIndex)
            {
                name = DevicePopupName,
            };
            // PopupField は GenericMenu を使うため、選択肢に '/' が含まれると階層メニューになる
            // （AG06/AG03 等のオーディオ I/F 名で発生）。表示用にだけ全角スラッシュへ置換する。
            _devicePopup.formatListItemCallback = item =>
                item == null ? string.Empty : item.Replace('/', '／');
            _devicePopup.formatSelectedValueCallback = item => item ?? string.Empty;
            _devicePopup.SetValueWithoutNotify(currentDeviceName);
            _devicePopup.RegisterValueChangedCallback(evt =>
            {
                ApplyDeviceNameFromPopup(evt.newValue);
            });
            Add(_devicePopup);

            _manualOverrideField = new TextField("手動 override")
            {
                name = ManualOverrideFieldName,
                value = currentDeviceName,
            };
            _manualOverrideField.RegisterValueChangedCallback(evt =>
            {
                ApplyDeviceNameFromManualOverride(evt.newValue);
            });
            Add(_manualOverrideField);

            _disambiguatorIndexField = new IntegerField("曖昧性解消 Index")
            {
                name = DisambiguatorIndexFieldName,
                value = _currentDescriptor.DisambiguatorIndex,
                tooltip = "同名のデバイス（USB マイク 2 本など）が複数接続されている場合の選択順。"
                    + "0 を指定すると最初に見つかった候補、1 を指定すると 2 番目の候補が選ばれます。"
                    + "通常は 0 のままで問題ありません。",
            };
            _disambiguatorIndexField.RegisterValueChangedCallback(evt =>
            {
                ApplyDisambiguatorIndex(evt.newValue);
            });
            Add(_disambiguatorIndexField);
        }

        private void ApplyDeviceNameFromPopup(string deviceName)
        {
            string value = deviceName ?? string.Empty;
            SetDeviceName(value);
            _manualOverrideField?.SetValueWithoutNotify(value);
        }

        private void ApplyDeviceNameFromManualOverride(string deviceName)
        {
            string value = deviceName ?? string.Empty;
            SetDeviceName(value);
            _devicePopup?.SetValueWithoutNotify(value);
        }

        private void ApplyDisambiguatorIndex(int disambiguatorIndex)
        {
            SetDisambiguatorIndex(disambiguatorIndex);
        }

        private void CollectChoices(
            IAsioDriverEnumerator asioEnumerator,
            IMicrophoneDeviceEnumerator microphoneEnumerator)
        {
            _choices.Clear();
            _choices.Add(string.Empty);
            AddChoices(asioEnumerator.GetDriverNames());
            AddChoices(microphoneEnumerator.GetDeviceNames());
        }

        private void AddChoices(string[] deviceNames)
        {
            if (deviceNames == null)
            {
                return;
            }

            for (int i = 0; i < deviceNames.Length; i++)
            {
                string deviceName = deviceNames[i];
                if (!string.IsNullOrEmpty(deviceName) && !_choices.Contains(deviceName))
                {
                    _choices.Add(deviceName);
                }
            }
        }

        private int FindChoiceIndex(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
            {
                return 0;
            }

            int index = _choices.IndexOf(deviceName);
            return index >= 0 ? index : 0;
        }

        private void SetDeviceName(string deviceName)
        {
            _currentDescriptor.DeviceName = deviceName ?? string.Empty;
            _onValueChanged(_currentDescriptor);
        }

        private void SetDisambiguatorIndex(int disambiguatorIndex)
        {
            _currentDescriptor.DisambiguatorIndex = disambiguatorIndex;
            _onValueChanged(_currentDescriptor);
        }
    }
}
