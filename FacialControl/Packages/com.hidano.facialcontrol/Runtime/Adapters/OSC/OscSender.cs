using System;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.OSC
{
    /// <summary>
    /// uOsc クライアントをラップし、BlendShape 値を OSC メッセージとして送信する。
    /// uOscClient の内部スレッドで非同期送信を行い、メインスレッドの負荷をゼロにする。
    /// </summary>
    public class OscSender : MonoBehaviour
    {
        [SerializeField]
        private string _address = "127.0.0.1";

        [SerializeField]
        private int _port = OscConfiguration.DefaultSendPort;

        [SerializeField]
        private bool _autoStart = true;

        private uOSC.uOscClient _client;
        private bool _initialized;
        private bool _sending;

        // マッピング情報: インデックス → OSC アドレス
        private string[] _oscAddresses;

        // マッピング情報を保持
        private OscMapping[] _mappings;

        /// <summary>
        /// 送信先 IP アドレス。
        /// </summary>
        public string Address
        {
            get => _address;
            set => _address = value;
        }

        /// <summary>
        /// 送信先ポート番号。
        /// </summary>
        public int Port
        {
            get => _port;
            set => _port = value;
        }

        /// <summary>
        /// クライアントが稼働中かどうか。
        /// </summary>
        public bool IsRunning => _client != null && _client.isRunning;

        /// <summary>
        /// 初期化済みかどうか。
        /// </summary>
        public bool IsInitialized => _initialized;

        /// <summary>
        /// マッピング数（送信対象の BlendShape 数）。
        /// </summary>
        public int MappingCount => _oscAddresses != null ? _oscAddresses.Length : 0;

        /// <summary>
        /// OscSender を初期化する。
        /// マッピング情報から OSC アドレスのルックアップテーブルを構築する。
        /// </summary>
        /// <param name="mappings">OSC アドレスマッピング配列。</param>
        public void Initialize(OscMapping[] mappings)
        {
            if (mappings == null)
                throw new ArgumentNullException(nameof(mappings));

            _mappings = mappings;
            _oscAddresses = new string[mappings.Length];
            for (int i = 0; i < mappings.Length; i++)
            {
                _oscAddresses[i] = mappings[i].OscAddress;
            }
            _initialized = true;
        }

        /// <summary>
        /// OscConfiguration から初期化する。
        /// </summary>
        /// <param name="config">OSC 設定（ポート番号・マッピング含む）。</param>
        public void Initialize(OscConfiguration config)
        {
            _port = config.SendPort;
            var mappingSpan = config.Mapping.Span;
            var mappings = new OscMapping[mappingSpan.Length];
            for (int i = 0; i < mappingSpan.Length; i++)
            {
                mappings[i] = mappingSpan[i];
            }
            Initialize(mappings);
        }

        /// <summary>
        /// 送信を開始する。uOscClient を起動する。
        /// </summary>
        public void StartSending()
        {
            if (!_initialized)
            {
                Debug.LogError("[FacialControl] OscSender が初期化されていません。Initialize() を先に呼び出してください。");
                return;
            }

            if (_sending)
                return;

            EnsureClient();
            _client.address = _address;
            _client.port = _port;

            // uOscClient は OnEnable で自動的に StartClient を呼ぶため、
            // enabled を制御してライフサイクルを管理する
            if (!_client.isRunning)
            {
                _client.StartClient();
            }
            _sending = true;
        }

        /// <summary>
        /// 送信を停止する。uOscClient を停止する。
        /// </summary>
        public void StopSending()
        {
            if (_client != null && _client.isRunning)
            {
                _client.StopClient();
            }
            _sending = false;
        }

        /// <summary>
        /// 全 BlendShape 値を OSC メッセージとして送信する。
        /// 各マッピングに対応する OSC アドレスに float 値を送信する。
        /// uOscClient の内部キューに追加され、別スレッドで非同期送信される。
        /// </summary>
        /// <param name="values">送信する BlendShape 値の配列。マッピングと同じ長さが必要。</param>
        public void SendAll(float[] values)
        {
            if (!_initialized || _client == null || !_client.isRunning)
                return;

            if (values == null)
                return;

            int count = Math.Min(values.Length, _oscAddresses.Length);
            for (int i = 0; i < count; i++)
            {
                _client.Send(_oscAddresses[i], values[i]);
            }
        }

        /// <summary>
        /// 指定インデックスの BlendShape 値を単一の OSC メッセージとして送信する。
        /// </summary>
        /// <param name="index">マッピングインデックス。</param>
        /// <param name="value">送信する値。</param>
        public void SendSingle(int index, float value)
        {
            if (!_initialized || _client == null || !_client.isRunning)
                return;

            if (index < 0 || index >= _oscAddresses.Length)
                return;

            _client.Send(_oscAddresses[index], value);
        }

        private void OnEnable()
        {
            if (_autoStart && _initialized)
            {
                StartSending();
            }
        }

        private void OnDisable()
        {
            StopSending();
        }

        private void EnsureClient()
        {
            if (_client != null)
                return;

            _client = GetComponent<uOSC.uOscClient>();
            if (_client == null)
            {
                // uOscClient は OnEnable で自動的に StartClient() を呼ぶ。
                // 追加直後に即停止して、手動制御に切り替える。
                _client = gameObject.AddComponent<uOSC.uOscClient>();
                _client.StopClient();
            }
        }

        private void OnDestroy()
        {
            StopSending();
        }
    }
}
