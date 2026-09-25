using Hidano.FacialControl.Adapters.IFacialMocap;
using UnityEngine;

namespace Hidano.FacialControl.IFacialMocap.Samples
{
    /// <summary>
    /// iFacialMocap 受信の最小診断サンプル。GameObject にアタッチして Play すると
    /// <see cref="IFacialMocapReceiverHost"/> を直接起動し、受信フレームの概要を Console に出力する。
    /// （FacialController への結線手順は同梱 README を参照）
    /// </summary>
    public sealed class IFacialMocapReceiverDemoBootstrap : MonoBehaviour
    {
        [Tooltip("UDP listen ポート（既定 49983）。")]
        [SerializeField]
        private int _listenPort = IFacialMocapProtocol.DefaultListenPort;

        [Tooltip("ハンドシェイク送信先（iOS 端末）IP。sendHandshake 有効時のみ使用。")]
        [SerializeField]
        private string _deviceAddress = string.Empty;

        [Tooltip("端末へトリガーを送ってストリームを起動する。")]
        [SerializeField]
        private bool _sendHandshake;

        [SerializeField]
        private IFacialMocapDataVersion _dataVersion = IFacialMocapDataVersion.Standard;

        private IFacialMocapReceiverHost _host;
        private readonly IFacialMocapFrame _frame = new IFacialMocapFrame();
        private int _lastSequence;
        private float _logTimer;

        private void Start()
        {
            _host = gameObject.AddComponent<IFacialMocapReceiverHost>();
            _host.Configure(_listenPort, _deviceAddress, _sendHandshake, _dataVersion, 1f);
            Debug.Log(
                $"[iFacialMocap Demo] listening UDP:{_listenPort} handshake={_sendHandshake} device='{_deviceAddress}' version={_dataVersion}");
        }

        private void Update()
        {
            if (_host == null)
            {
                return;
            }

            int sequence = _host.TryReadLatest(_frame);
            if (sequence == 0 || sequence == _lastSequence)
            {
                return;
            }

            _lastSequence = sequence;

            // 受信は 60fps なので 1 秒ごとに 1 行へ間引いてログする。
            _logTimer += Time.deltaTime;
            if (_logTimer < 1f)
            {
                return;
            }

            _logTimer = 0f;
            Debug.Log(
                $"[iFacialMocap Demo] frame#{sequence} blendShapes={_frame.BlendShapes.Count} "
                + $"head={_frame.Head.HasValue} eyesR/L={_frame.RightEye.HasValue}/{_frame.LeftEye.HasValue}");
        }

        private void OnDestroy()
        {
            if (_host != null)
            {
                _host.Stop();
                _host = null;
            }
        }
    }
}
