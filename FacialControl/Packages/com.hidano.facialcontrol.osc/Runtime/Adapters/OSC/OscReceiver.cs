using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.OSC
{
    /// <summary>
    /// uOsc サーバーをラップし、OSC メッセージを受信して OscDoubleBuffer に書き込む。
    /// VRChat / ARKit アドレスパターンを解析し、マッピングテーブルに基づいてバッファインデックスに変換する。
    /// </summary>
    public class OscReceiver : MonoBehaviour
    {
        /// <summary>
        /// VRChat OSC アドレスプレフィックス
        /// </summary>
        public const string VRChatAddressPrefix = "/avatar/parameters/";

        /// <summary>
        /// ARKit OSC アドレスプレフィックス
        /// </summary>
        public const string ARKitAddressPrefix = "/ARKit/";

        [SerializeField]
        private int _port = OscConfiguration.DefaultReceivePort;

        [SerializeField]
        private bool _autoStart = true;

        private uOSC.uOscServer _server;
        private int _activePort = -1;
        private OscDoubleBuffer _buffer;
        private OscBundleAccumulator _bundleAccumulator;
        private BundleInterpretationMode _bundleMode;
        private ITimeProvider _timeProvider;
        private bool _initialized;

        // OSC アドレス → バッファインデックスの高速逆引き辞書
        private Dictionary<string, int> _addressToIndex;

        // BlendShape 名 → バッファインデックスの逆引き（アドレスプレフィックス除去後の名前解決用）
        private Dictionary<string, int> _blendShapeNameToIndex;

        // マッピング情報を保持（レイヤー分配のため）
        private OscMapping[] _mappings;

        // binding 統合用: sender_id / heartbeat / Gaze など、float routing 前のメッセージ判定。
        private Func<uOSC.Message, bool> _messageFilter;

        // analog-input-binding 用: 任意 OSC アドレスごとの float リスナー (加算的拡張)
        private readonly object _analogListenersLock = new object();
        private Dictionary<string, Action<float>> _analogListeners;

        /// <summary>
        /// 受信ポート番号。
        /// </summary>
        public int Port
        {
            get => _port;
            set => _port = value;
        }

        /// <summary>
        /// サーバーが稼働中かどうか。
        /// </summary>
        public bool IsRunning => _server != null && _server.isRunning;

        /// <summary>
        /// 実際に待ち受けているポート番号。
        /// <see cref="Port"/> が使用中だった場合は空きポートへ自動繰り上げされるため、
        /// 設定値と異なることがある。<see cref="StartReceiving"/> 前は -1。
        /// </summary>
        public int ActivePort => _activePort;

        /// <summary>
        /// 受信バッファへの参照。
        /// </summary>
        public OscDoubleBuffer Buffer => _buffer;

        public OscBundleAccumulator BundleAccumulator => _bundleAccumulator;

        public void SetMessageFilter(Func<uOSC.Message, bool> filter)
        {
            _messageFilter = filter;
        }

        /// <summary>
        /// OscReceiver を初期化する。
        /// OscDoubleBuffer とマッピング情報を設定する。
        /// </summary>
        /// <param name="buffer">受信データ書き込み先のダブルバッファ。</param>
        /// <param name="mappings">OSC アドレスマッピング配列。</param>
        public void Initialize(
            OscDoubleBuffer buffer,
            OscMapping[] mappings,
            OscBundleAccumulator bundleAccumulator = null,
            BundleInterpretationMode bundleMode = BundleInterpretationMode.IndividualMessage,
            ITimeProvider timeProvider = null)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (mappings == null)
                throw new ArgumentNullException(nameof(mappings));

            _buffer = buffer;
            _bundleAccumulator = bundleAccumulator;
            _bundleMode = bundleMode;
            _timeProvider = timeProvider;
            _mappings = mappings;
            BuildLookupTables(mappings);
            _initialized = true;
        }

        /// <summary>
        /// OscConfiguration からマッピングを取得して初期化する。
        /// </summary>
        /// <param name="buffer">受信データ書き込み先のダブルバッファ。</param>
        /// <param name="config">OSC 設定（ポート番号・マッピング含む）。</param>
        public void Initialize(OscDoubleBuffer buffer, OscConfiguration config)
        {
            _port = config.ReceivePort;
            var mappingSpan = config.Mapping.Span;
            var mappings = new OscMapping[mappingSpan.Length];
            for (int i = 0; i < mappingSpan.Length; i++)
            {
                mappings[i] = mappingSpan[i];
            }
            Initialize(buffer, mappings);
        }

        /// <summary>
        /// 受信サーバーを開始する。
        /// 設定ポートが使用中の場合は空きポートへ自動繰り上げし、警告ログで通知する
        /// （ビルド済みアプリで衝突が発覚しても無警告で受信不能にならないようにするため）。
        /// </summary>
        public void StartReceiving()
        {
            if (!_initialized)
            {
                Debug.LogError("[FacialControl] OscReceiver が初期化されていません。Initialize() を先に呼び出してください。");
                return;
            }

            // 稼働中の再呼び出しで自分自身の bind を「使用中」と誤検知し、
            // ポートが移動してしまうのを防ぐ。
            if (IsRunning)
            {
                return;
            }

            int resolvedPort = OscPortResolver.ResolveAvailablePort(_port);
            if (resolvedPort < 0)
            {
                Debug.LogError(
                    $"[FacialControl] OSC 受信ポート {_port} から {OscPortResolver.DefaultMaxAttempts} 個連続で空きポートが見つかりませんでした。" +
                    $"ポート {_port} で待ち受けを試みますが、他プロセスが占有している間は受信できません。");
                resolvedPort = _port;
            }
            else if (resolvedPort != _port)
            {
                Debug.LogWarning(
                    $"[FacialControl] OSC 受信ポート {_port} は使用中のため {resolvedPort} に繰り上げて待ち受けます。" +
                    $"送信側の宛先ポートを {resolvedPort} に合わせてください。");
            }

            _activePort = resolvedPort;
            EnsureServer();
            _server.port = resolvedPort;
            _server.autoStart = false;
            _server.StartServer();
        }

        /// <summary>
        /// 受信サーバーを停止する。
        /// </summary>
        public void StopReceiving()
        {
            if (_server != null && _server.isRunning)
            {
                _server.StopServer();
            }
        }

        /// <summary>
        /// OSC メッセージを処理し、バッファに書き込む。
        /// uOscServer の onDataReceived コールバックから呼ばれる。
        /// テスト用にも public として公開。
        /// </summary>
        /// <param name="message">受信した OSC メッセージ。</param>
        public void HandleOscMessage(uOSC.Message message)
        {
            if (!_initialized || _buffer == null)
                return;

            if (string.IsNullOrEmpty(message.address))
                return;

            if (_messageFilter != null)
            {
                bool shouldContinue;
                try
                {
                    shouldContinue = _messageFilter.Invoke(message);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    return;
                }

                if (!shouldContinue)
                {
                    return;
                }
            }

            if (message.values == null || message.values.Length == 0)
                return;

            // float 値の取得
            float value;
            if (message.values[0] is float f)
            {
                value = f;
            }
            else if (message.values[0] is int i)
            {
                // int → float 変換（VRChat は int も送る場合がある）
                value = i;
            }
            else
            {
                return;
            }

            // アドレス完全一致による高速ルックアップ
            if (_addressToIndex.TryGetValue(message.address, out int index))
            {
                WriteValue(message, index, value);
            }
            else
            {
                // アドレスプレフィックスを除去して BlendShape 名で検索
                string blendShapeName = ExtractBlendShapeName(message.address);
                if (blendShapeName != null && _blendShapeNameToIndex.TryGetValue(blendShapeName, out index))
                {
                    WriteValue(message, index, value);
                }
            }

            // 加算的拡張: analog-input-binding 用任意アドレスリスナー通知 
            NotifyAnalogListeners(message.address, value);
        }

        /// <summary>
        /// 任意 OSC アドレスに対する float リスナーを登録する。
        /// 受信スレッドからコールバックされるため、ハンドラ側でスレッドセーフな書込みを行うこと。
        /// 同一 (address, listener) を重複登録した場合は multicast 結合される。
        /// </summary>
        /// <param name="address">購読する OSC アドレス（完全一致）。</param>
        /// <param name="listener">受信時に invoke される delegate。</param>
        /// <exception cref="ArgumentException"><paramref name="address"/> が null/空。</exception>
        /// <exception cref="ArgumentNullException"><paramref name="listener"/> が null。</exception>
        public void RegisterAnalogListener(string address, Action<float> listener)
        {
            if (string.IsNullOrEmpty(address))
                throw new ArgumentException("address は null/空にできません。", nameof(address));
            if (listener == null)
                throw new ArgumentNullException(nameof(listener));

            lock (_analogListenersLock)
            {
                if (_analogListeners == null)
                {
                    _analogListeners = new Dictionary<string, Action<float>>(StringComparer.Ordinal);
                }

                if (_analogListeners.TryGetValue(address, out var existing))
                {
                    _analogListeners[address] = (Action<float>)Delegate.Combine(existing, listener);
                }
                else
                {
                    _analogListeners[address] = listener;
                }
            }
        }

        /// <summary>
        /// 登録済みの analog listener を解除する。
        /// 未登録または既に解除済みの (address, listener) を渡しても例外にはならない。
        /// </summary>
        /// <param name="address">登録時と同じ OSC アドレス。</param>
        /// <param name="listener">登録時と同じ delegate 参照。</param>
        public void UnregisterAnalogListener(string address, Action<float> listener)
        {
            if (string.IsNullOrEmpty(address) || listener == null)
                return;

            lock (_analogListenersLock)
            {
                if (_analogListeners == null)
                    return;

                if (_analogListeners.TryGetValue(address, out var existing))
                {
                    var updated = (Action<float>)Delegate.Remove(existing, listener);
                    if (updated == null)
                    {
                        _analogListeners.Remove(address);
                    }
                    else
                    {
                        _analogListeners[address] = updated;
                    }
                }
            }
        }

        private void NotifyAnalogListeners(string address, float value)
        {
            Action<float> listener = null;
            lock (_analogListenersLock)
            {
                if (_analogListeners == null)
                    return;
                _analogListeners.TryGetValue(address, out listener);
            }

            if (listener == null)
                return;

            try
            {
                listener.Invoke(value);
            }
            catch (Exception ex)
            {
                // 受信スレッドで例外が漏れて uOSC サーバが停止しないよう握り潰してログのみ
                Debug.LogException(ex);
            }
        }

        private void WriteValue(uOSC.Message message, int index, float value)
        {
            if (_bundleMode == BundleInterpretationMode.AtomicSwap && _bundleAccumulator != null)
            {
                _bundleAccumulator.RecordMessage(message, index, value, GetCurrentTimeSeconds());
                return;
            }

            _buffer.Write(index, value);
        }

        private double GetCurrentTimeSeconds()
        {
            return _timeProvider != null ? _timeProvider.UnscaledTimeSeconds : Time.unscaledTimeAsDouble;
        }

        /// <summary>
        /// OSC アドレスから BlendShape 名を抽出する。
        /// VRChat 形式（/avatar/parameters/{name}）または ARKit 形式（/ARKit/{name}）に対応。
        /// </summary>
        /// <param name="address">OSC アドレス。</param>
        /// <returns>抽出された BlendShape 名。パターンに一致しない場合は null。</returns>
        public static string ExtractBlendShapeName(string address)
        {
            if (string.IsNullOrEmpty(address))
                return null;

            if (address.StartsWith(VRChatAddressPrefix, StringComparison.Ordinal))
            {
                string name = address.Substring(VRChatAddressPrefix.Length);
                return name.Length > 0 ? name : null;
            }

            if (address.StartsWith(ARKitAddressPrefix, StringComparison.Ordinal))
            {
                string name = address.Substring(ARKitAddressPrefix.Length);
                return name.Length > 0 ? name : null;
            }

            return null;
        }

        private void OnEnable()
        {
            if (_autoStart && _initialized)
            {
                StartReceiving();
            }
        }

        private void OnDisable()
        {
            StopReceiving();
        }

        private void EnsureServer()
        {
            if (_server != null)
                return;

            _server = GetComponent<uOSC.uOscServer>();
            if (_server == null)
            {
                _server = gameObject.AddComponent<uOSC.uOscServer>();
            }
            _server.autoStart = false;
            _server.onDataReceived.AddListener(HandleOscMessage);
        }

        private void BuildLookupTables(OscMapping[] mappings)
        {
            _addressToIndex = new Dictionary<string, int>(mappings.Length, StringComparer.Ordinal);
            _blendShapeNameToIndex = new Dictionary<string, int>(mappings.Length, StringComparer.Ordinal);

            for (int i = 0; i < mappings.Length; i++)
            {
                var mapping = mappings[i];

                // OSC アドレス → インデックス
                if (!string.IsNullOrEmpty(mapping.OscAddress))
                {
                    _addressToIndex[mapping.OscAddress] = i;
                }

                // BlendShape 名 → インデックス（重複時は後勝ち）
                if (!string.IsNullOrEmpty(mapping.BlendShapeName))
                {
                    _blendShapeNameToIndex[mapping.BlendShapeName] = i;
                }
            }
        }

        private void OnDestroy()
        {
            if (_server != null)
            {
                _server.onDataReceived.RemoveListener(HandleOscMessage);
            }
        }
    }
}
