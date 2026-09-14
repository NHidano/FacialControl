using System;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.OSC
{
    /// <summary>
    /// <see cref="Hidano.FacialControl.Adapters.AdapterBindings.OscReceiverAdapterBinding"/> が
    /// <c>OnStart</c> 内で <c>ctx.HostGameObject.AddComponent</c> する OSC 受信用 helper
    /// MonoBehaviour。
    /// </summary>
    /// <remarks>
    /// <para>
    /// AddComponent して bind し、binding 経由の <see cref="Configure"/> 呼び出しで
    /// <see cref="OscDoubleBuffer"/> に受信値を流し込む。
    /// </para>
    /// <para>
    /// HideFlags は <see cref="HideFlags.None"/> を維持し Inspector で見える状態を保つ
    /// （デバッグしやすさのため <see cref="HideFlags.HideInInspector"/> は使わない）。
    /// </para>
    /// <para>
    /// <see cref="OnDestroy"/> で同 GameObject 上に追加した <see cref="OscReceiver"/> および
    /// </para>
    /// </remarks>
    public sealed class OscReceiverHost : MonoBehaviour
    {
        private OscReceiver _receiver;
        private OscDoubleBuffer _buffer;
        private OscBundleAccumulator _bundleAccumulator;
        private OscMapping[] _mappings;
        private ITimeProvider _timeProvider;
        private string _endpoint;
        private int _port;
        private BundleInterpretationMode _bundleMode;
        private bool _configured;

        /// <summary>受信に用いる内部 <see cref="OscReceiver"/> 参照（テスト/デバッグ用）。</summary>
        public OscReceiver Receiver => _receiver;

        /// <summary>受信ダブルバッファ参照（binding 側で <see cref="OscDoubleBuffer.Swap"/> を呼ぶ）。</summary>
        public OscDoubleBuffer Buffer => _buffer;

        public OscBundleAccumulator BundleAccumulator => _bundleAccumulator;

        /// <summary>bind 対象のローカルエンドポイント（IP/host）。現状ログ/診断用途。</summary>
        public string Endpoint => _endpoint;

        /// <summary>受信 UDP ポート。</summary>
        public int Port => _port;

        /// <summary><see cref="Configure"/> が呼ばれて受信開始済みなら true。</summary>
        public bool IsConfigured => _configured;

        /// <summary>
        /// helper を構成し OSC 受信を開始する。
        /// </summary>
        /// <param name="endpoint">bind エンドポイント（現状 uOSC は port のみ使用、ログ用に保持）。</param>
        /// <param name="port">UDP 受信ポート。</param>
        /// <param name="buffer">受信値の書き込み先 <see cref="OscDoubleBuffer"/>。</param>
        /// <param name="mappings">OSC アドレス ↔ BlendShape マッピング配列。</param>
        public void Configure(
            string endpoint,
            int port,
            OscDoubleBuffer buffer,
            OscMapping[] mappings,
            OscBundleAccumulator bundleAccumulator = null,
            BundleInterpretationMode bundleMode = BundleInterpretationMode.IndividualMessage,
            ITimeProvider timeProvider = null)
        {
            Configure(endpoint, port, buffer, mappings, bundleAccumulator, bundleMode, timeProvider,
                OscReceiveOptions.Default);
        }

        /// <summary>
        /// OSC 受信器をリング設定込みで初期化・開始する。
        /// </summary>
        public void Configure(
            string endpoint,
            int port,
            OscDoubleBuffer buffer,
            OscMapping[] mappings,
            OscBundleAccumulator bundleAccumulator,
            BundleInterpretationMode bundleMode,
            ITimeProvider timeProvider,
            OscReceiveOptions receiveOptions)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (mappings == null) throw new ArgumentNullException(nameof(mappings));

            _endpoint = endpoint;
            _port = port;
            _buffer = buffer;
            _bundleAccumulator = bundleAccumulator;
            _mappings = mappings;
            _bundleMode = bundleMode;
            _timeProvider = timeProvider;

            if (_receiver == null)
            {
                _receiver = gameObject.AddComponent<OscReceiver>();
            }
            _receiver.Port = port;
            _receiver.ReceiveOptions = receiveOptions;
            _receiver.Initialize(buffer, mappings, bundleAccumulator, bundleMode, timeProvider);
            _receiver.StartReceiving();
            _configured = true;
        }

        public void ReconfigureMappings(
            OscDoubleBuffer buffer,
            OscMapping[] mappings,
            OscBundleAccumulator bundleAccumulator = null)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (mappings == null) throw new ArgumentNullException(nameof(mappings));
            if (_receiver == null)
            {
                throw new InvalidOperationException("OscReceiverHost must be configured before mappings can be replaced.");
            }

            _buffer = buffer;
            _mappings = mappings;
            _bundleAccumulator = bundleAccumulator;
            _receiver.Initialize(buffer, mappings, bundleAccumulator, _bundleMode, _timeProvider);
        }

        /// <summary>
        /// <see cref="OscDoubleBuffer.Swap"/> を呼んで write→read を入れ替える。
        /// binding の <c>OnFixedTick</c> 等から呼ばれる。
        /// </summary>
        public void Tick()
        {
            if (_bundleMode == BundleInterpretationMode.AtomicSwap && _bundleAccumulator != null)
            {
                _bundleAccumulator.FlushDue(GetCurrentTimeSeconds());
            }
            else if (_buffer != null)
            {
                _buffer.Swap();
            }
        }

        private void Update()
        {
            _receiver?.PumpReceived();
        }

        private double GetCurrentTimeSeconds()
        {
            return _timeProvider != null ? _timeProvider.UnscaledTimeSeconds : Time.unscaledTimeAsDouble;
        }

        private void OnDestroy()
        {
            // socket を確実に close する。
            if (_receiver != null)
            {
                try
                {
                    _receiver.StopReceiving();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
                if (UnityEngine.Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(_receiver);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(_receiver);
                }
                _receiver = null;
            }

            _configured = false;
        }
    }
}
