using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Hidano.FacialControl.Adapters.IFacialMocap
{
    /// <summary>
    /// <see cref="Hidano.FacialControl.Adapters.AdapterBindings.IFacialMocapReceiverAdapterBinding"/> が
    /// <c>OnStart</c> で <c>AddComponent</c> する iFacialMocap UDP 受信 helper MonoBehaviour。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 受信は background thread で行い、必要なら端末 <c>IP:49983</c> へハンドシェイク文字列を周期送信して
    /// ストリームを起動する。受信パケットは <see cref="IFacialMocapPacketParser"/> で解析し、最新フレームを
    /// lock 下で保持する。main スレッドは <see cref="TryReadLatest"/> で最新フレームと sequence を取得する。
    /// </para>
    /// <para>
    /// <c>Thread.Abort</c> は使わず、<see cref="Stop"/> で socket close → thread join により安全に停止する。
    /// </para>
    /// </remarks>
    public sealed class IFacialMocapReceiverHost : MonoBehaviour
    {
        private const int ReceiveTimeoutMs = 200;
        private const double ErrorLogThrottleSeconds = 5d;
        private const int ThreadJoinTimeoutMs = 500;

        private Thread _thread;
        private UdpClient _udp;
        private volatile bool _running;

        private int _listenPort;
        private IPEndPoint _deviceEndpoint;
        private bool _sendHandshake;
        private IFacialMocapDataVersion _version;
        private double _handshakeIntervalSeconds;
        private byte[] _handshakeBytes;

        private readonly object _frameLock = new object();
        private readonly IFacialMocapFrame _receiveFrame = new IFacialMocapFrame();
        private readonly IFacialMocapFrame _latestFrame = new IFacialMocapFrame();
        private int _sequence;

        /// <summary>受信スレッドが稼働中なら true。</summary>
        public bool IsRunning => _running;

        /// <summary>bind した listen ポート。</summary>
        public int ListenPort => _listenPort;

        /// <summary>
        /// helper を構成し受信を開始する。
        /// </summary>
        /// <param name="listenPort">UDP listen ポート（既定 49983）。</param>
        /// <param name="deviceAddress">ハンドシェイク送信先（端末）IP。<paramref name="sendHandshake"/> が true のとき使用。</param>
        /// <param name="sendHandshake">true で端末へトリガーを周期送信しストリームを起動する。</param>
        /// <param name="version">標準 / v2。ハンドシェイクとパースの区切りに反映。</param>
        /// <param name="handshakeIntervalSeconds">ハンドシェイク再送間隔（秒）。</param>
        public void Configure(
            int listenPort,
            string deviceAddress,
            bool sendHandshake,
            IFacialMocapDataVersion version,
            float handshakeIntervalSeconds)
        {
            if (_running)
            {
                return;
            }

            _listenPort = listenPort;
            _version = version;
            _handshakeIntervalSeconds = Math.Max(0.1f, handshakeIntervalSeconds);

            _sendHandshake = sendHandshake;
            _deviceEndpoint = null;
            if (sendHandshake)
            {
                if (IPAddress.TryParse(deviceAddress, out IPAddress ip))
                {
                    _deviceEndpoint = new IPEndPoint(ip, IFacialMocapProtocol.DeviceTriggerPort);
                    _handshakeBytes = Encoding.ASCII.GetBytes(IFacialMocapProtocol.BuildTrigger(version));
                }
                else
                {
                    Debug.LogWarning(
                        $"[IFacialMocapReceiverHost] deviceAddress '{deviceAddress}' は不正な IP のためハンドシェイクを送信しません。");
                    _sendHandshake = false;
                }
            }

            try
            {
                _udp = new UdpClient();
                _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udp.Client.ReceiveTimeout = ReceiveTimeoutMs;
                _udp.Client.Bind(new IPEndPoint(IPAddress.Any, listenPort));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[IFacialMocapReceiverHost] port {listenPort} の bind に失敗しました: {ex.Message}");
                _udp?.Dispose();
                _udp = null;
                return;
            }

            _running = true;
            _thread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = "IFacialMocapReceiver",
            };
            _thread.Start();
        }

        /// <summary>
        /// 最新フレームを <paramref name="destination"/> へコピーする。
        /// </summary>
        /// <returns>未受信なら 0、受信済みなら単調増加する sequence（前回値との比較で新規判定に使う）。</returns>
        public int TryReadLatest(IFacialMocapFrame destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            lock (_frameLock)
            {
                if (_sequence == 0)
                {
                    return 0;
                }

                _latestFrame.CopyTo(destination);
                return _sequence;
            }
        }

        private void ReceiveLoop()
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            var stopwatch = Stopwatch.StartNew();
            double nextHandshakeSeconds = 0d;
            double nextErrorLogSeconds = 0d;

            while (_running)
            {
                double now = stopwatch.Elapsed.TotalSeconds;
                if (_sendHandshake && _deviceEndpoint != null && now >= nextHandshakeSeconds)
                {
                    try
                    {
                        _udp.Send(_handshakeBytes, _handshakeBytes.Length, _deviceEndpoint);
                    }
                    catch (Exception)
                    {
                        // ハンドシェイク送信失敗は致命的でないため無視（次周期で再送）
                    }

                    nextHandshakeSeconds = now + _handshakeIntervalSeconds;
                }

                try
                {
                    byte[] data = _udp.Receive(ref remote);
                    string text = Encoding.ASCII.GetString(data);
                    if (IFacialMocapPacketParser.TryParse(text, _version, _receiveFrame))
                    {
                        lock (_frameLock)
                        {
                            _receiveFrame.CopyTo(_latestFrame);
                            _sequence++;
                            if (_sequence == 0)
                            {
                                _sequence = 1;
                            }
                        }
                    }
                }
                catch (SocketException)
                {
                    // ReceiveTimeout / transient。ループ先頭で _running とハンドシェイクを再評価する。
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    double t = stopwatch.Elapsed.TotalSeconds;
                    if (t >= nextErrorLogSeconds)
                    {
                        Debug.LogException(ex);
                        nextErrorLogSeconds = t + ErrorLogThrottleSeconds;
                    }
                }
            }
        }

        private void OnDestroy()
        {
            Stop();
        }

        /// <summary>受信スレッドを停止し socket を close する。</summary>
        public void Stop()
        {
            _running = false;

            try
            {
                _udp?.Close();
            }
            catch (Exception)
            {
                // close 失敗は無視
            }

            if (_thread != null && _thread.IsAlive)
            {
                _thread.Join(ThreadJoinTimeoutMs);
            }

            _thread = null;
            _udp?.Dispose();
            _udp = null;
        }
    }
}
