using System;
using Hidano.FacialControl.Adapters.IFacialMocap;
using Hidano.FacialControl.Adapters.Json.Dto;
using Hidano.FacialControl.Adapters.OSC;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.RuntimeSettings
{
    /// <summary>
    /// iFacialMocap Receiver の環境/運用設定を 1 sub-asset に集約した
    /// <see cref="AdapterRuntimeSettingsBase"/> 派生 SO。
    /// </summary>
    /// <remarks>
    /// JSON ラウンドトリップは <see cref="IFacialMocapOptionsDto"/> を共有形として用いる。
    /// FailSafeMode の文字列化は <c>.osc</c> の <see cref="OscRuntimeSettingsSO"/> の static を再利用する。
    /// <c>CreateAssetMenu</c> は付与しない（sub-asset 専用）。
    /// </remarks>
    public sealed class IFacialMocapRuntimeSettingsSO : AdapterRuntimeSettingsBase, ISerializationCallbackReceiver
    {
        public const int DefaultListenPort = IFacialMocapProtocol.DefaultListenPort;
        public const float DefaultHandshakeIntervalSeconds = 1f;

        public const string DataVersionStandard = "standard";
        public const string DataVersionV2 = "v2";

        [SerializeField]
        private bool _receiverEnabled = true;

        [SerializeField]
        private int _listenPort = DefaultListenPort;

        [Tooltip("ハンドシェイク送信先（iOS 端末）の IP。sendHandshake 有効時のみ使用。")]
        [SerializeField]
        private string _deviceAddress = string.Empty;

        [Tooltip("端末へトリガーを周期送信してストリームを起動する。")]
        [SerializeField]
        private bool _sendHandshake;

        [SerializeField]
        private IFacialMocapDataVersion _dataVersion = IFacialMocapDataVersion.Standard;

        [SerializeField]
        private float _handshakeIntervalSeconds = DefaultHandshakeIntervalSeconds;

        [SerializeField]
        private float _stalenessSeconds;

        [SerializeField]
        private FailSafeMode _failSafeMode = FailSafeMode.RevertToBase;

        [SerializeField]
        private bool _enableGaze = true;

        [SerializeField]
        private EyeGazeConverter _eyeGaze = EyeGazeConverter.Default;

        [SerializeField]
        private bool _enableHead = true;

        [Tooltip("頭部の位置(X,Y,Z)も analog 軸 3..5 として公開する。")]
        [SerializeField]
        private bool _includeHeadPosition;

        public bool ReceiverEnabled => _receiverEnabled;

        public int ListenPort => _listenPort;

        public string DeviceAddress => _deviceAddress;

        public bool SendHandshake => _sendHandshake;

        public IFacialMocapDataVersion DataVersion => _dataVersion;

        public float HandshakeIntervalSeconds => _handshakeIntervalSeconds;

        public float StalenessSeconds => _stalenessSeconds;

        public FailSafeMode FailSafeMode => _failSafeMode;

        public bool EnableGaze => _enableGaze;

        public EyeGazeConverter EyeGaze => _eyeGaze;

        public bool EnableHead => _enableHead;

        public bool IncludeHeadPosition => _includeHeadPosition;

        internal void SetReceiverEnabled(bool value) => _receiverEnabled = value;
        internal void SetListenPort(int value) => _listenPort = value;
        internal void SetDeviceAddress(string value) => _deviceAddress = value;
        internal void SetSendHandshake(bool value) => _sendHandshake = value;
        internal void SetDataVersion(IFacialMocapDataVersion value) => _dataVersion = value;
        internal void SetHandshakeIntervalSeconds(float value) => _handshakeIntervalSeconds = value;
        internal void SetStalenessSeconds(float value) => _stalenessSeconds = value;
        internal void SetFailSafeMode(FailSafeMode value) => _failSafeMode = value;
        internal void SetEnableGaze(bool value) => _enableGaze = value;
        internal void SetEyeGaze(EyeGazeConverter value) => _eyeGaze = value;
        internal void SetEnableHead(bool value) => _enableHead = value;
        internal void SetIncludeHeadPosition(bool value) => _includeHeadPosition = value;

        protected override void OnEnable()
        {
            base.OnEnable();
            NormalizeFields();
        }

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
            NormalizeFields();
        }

        public override string ToJson()
        {
            NormalizeFields();
            var dto = new IFacialMocapOptionsDto
            {
                schemaVersion = _schemaVersion,
                label = _label ?? string.Empty,
                receiverEnabled = _receiverEnabled,
                listenPort = _listenPort,
                deviceAddress = _deviceAddress ?? string.Empty,
                sendHandshake = _sendHandshake,
                dataVersion = ToDataVersionString(_dataVersion),
                handshakeIntervalSeconds = _handshakeIntervalSeconds,
                stalenessSeconds = _stalenessSeconds,
                failSafeMode = ToFailSafeModeString(_failSafeMode),
                enableGaze = _enableGaze,
                eyeMaxYawDegrees = _eyeGaze.maxYawDegrees,
                eyeMaxPitchDegrees = _eyeGaze.maxPitchDegrees,
                enableHead = _enableHead,
                includeHeadPosition = _includeHeadPosition,
            };
            return dto.ToJson(true);
        }

        public override void FromJson(string json)
        {
            IFacialMocapOptionsDto dto = IFacialMocapOptionsDto.FromJson(json);

            _schemaVersion = dto.schemaVersion > 0 ? dto.schemaVersion : 1;
            _label = dto.label ?? string.Empty;
            _receiverEnabled = dto.receiverEnabled;
            _listenPort = dto.listenPort;
            _deviceAddress = dto.deviceAddress ?? string.Empty;
            _sendHandshake = dto.sendHandshake;
            _dataVersion = ToDataVersion(dto.dataVersion);
            _handshakeIntervalSeconds = dto.handshakeIntervalSeconds;
            _stalenessSeconds = dto.stalenessSeconds;
            _failSafeMode = ToFailSafeMode(dto.failSafeMode);
            _enableGaze = dto.enableGaze;
            _eyeGaze = new EyeGazeConverter
            {
                maxYawDegrees = dto.eyeMaxYawDegrees,
                maxPitchDegrees = dto.eyeMaxPitchDegrees,
            };
            _enableHead = dto.enableHead;
            _includeHeadPosition = dto.includeHeadPosition;

            NormalizeFields();
        }

        public static string ToDataVersionString(IFacialMocapDataVersion version)
        {
            return version == IFacialMocapDataVersion.V2 ? DataVersionV2 : DataVersionStandard;
        }

        public static IFacialMocapDataVersion ToDataVersion(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return IFacialMocapDataVersion.Standard;
            }

            string normalized = value.Trim();
            if (string.Equals(normalized, DataVersionV2, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, nameof(IFacialMocapDataVersion.V2), StringComparison.OrdinalIgnoreCase))
            {
                return IFacialMocapDataVersion.V2;
            }

            return IFacialMocapDataVersion.Standard;
        }

        public const string FailSafeRevertToBase = "revertToBase";
        public const string FailSafeHoldLastValue = "holdLastValue";

        public static string ToFailSafeModeString(FailSafeMode mode)
        {
            return mode == FailSafeMode.HoldLastValue ? FailSafeHoldLastValue : FailSafeRevertToBase;
        }

        public static FailSafeMode ToFailSafeMode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return FailSafeMode.RevertToBase;
            }

            string normalized = value.Trim();
            if (string.Equals(normalized, FailSafeHoldLastValue, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, nameof(FailSafeMode.HoldLastValue), StringComparison.OrdinalIgnoreCase))
            {
                return FailSafeMode.HoldLastValue;
            }

            return FailSafeMode.RevertToBase;
        }

        private void NormalizeFields()
        {
            if (_listenPort <= 0 || _listenPort > 65535)
            {
                _listenPort = DefaultListenPort;
            }

            if (_deviceAddress == null)
            {
                _deviceAddress = string.Empty;
            }
            else
            {
                _deviceAddress = _deviceAddress.Trim();
            }

            if (_handshakeIntervalSeconds <= 0f || float.IsNaN(_handshakeIntervalSeconds))
            {
                _handshakeIntervalSeconds = DefaultHandshakeIntervalSeconds;
            }

            if (_stalenessSeconds < 0f || float.IsNaN(_stalenessSeconds))
            {
                _stalenessSeconds = 0f;
            }

            if (!Enum.IsDefined(typeof(IFacialMocapDataVersion), _dataVersion))
            {
                _dataVersion = IFacialMocapDataVersion.Standard;
            }

            if (!Enum.IsDefined(typeof(FailSafeMode), _failSafeMode))
            {
                _failSafeMode = FailSafeMode.RevertToBase;
            }

            if (float.IsNaN(_eyeGaze.maxYawDegrees) || _eyeGaze.maxYawDegrees < 0f)
            {
                _eyeGaze.maxYawDegrees = 0f;
            }

            if (float.IsNaN(_eyeGaze.maxPitchDegrees) || _eyeGaze.maxPitchDegrees < 0f)
            {
                _eyeGaze.maxPitchDegrees = 0f;
            }
        }
    }
}
