using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.OSC
{
    public enum OscReceiveState : byte
    {
        Stopped,
        Running,
        Stopping,
        Faulted
    }

    public struct OscReceiveThreadHooks
    {
        public Action OnThreadStarted;
        public Action OnThreadStopping;
        public Action OnDatagramCommitted;
    }

    /// <summary>固定長リングのスロットへ UDP データグラムを直接受信するバックグラウンドループ。</summary>
    public sealed class OscUdpReceiveLoop : IDisposable
    {
        private readonly OscDatagramRing _ring;
        private readonly OscReceiveDiagnostics _diagnostics;
        private readonly object _stateLock = new object();
        private Socket _socket;
        private Thread _thread;
        private OscReceiveOptions _options;
        private OscReceiveThreadHooks _threadHooks;
        private int _stopRequested;
        private int _isRunning;
        private int _faulted;
        private int _state;
        private int _boundPort = -1;
        private Exception _pendingFault;

        public OscUdpReceiveLoop(OscDatagramRing ring, OscReceiveDiagnostics diagnostics)
        {
            _ring = ring ?? throw new ArgumentNullException(nameof(ring));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public OscReceiveThreadHooks ThreadHooks
        {
            get { return _threadHooks; }
            set
            {
                lock (_stateLock)
                {
                    if (_thread != null) throw new InvalidOperationException("ThreadHooks must be set before Start.");
                    _threadHooks = value;
                }
            }
        }

        /// <summary>受信スレッドが使用する不変テーブルのスナップショット取得。</summary>
        public Func<OscAddressKeyTable> TableProvider { get; set; }

        public bool IsRunning => Volatile.Read(ref _isRunning) != 0;
        public bool Faulted => Volatile.Read(ref _faulted) != 0;
        public OscReceiveState State => (OscReceiveState)Volatile.Read(ref _state);
        public int BoundPort => Volatile.Read(ref _boundPort);

        /// <summary>
        /// 受信スレッドで発生した例外をメインスレッドへ引き渡す（一度取り出すと消える）。
        /// 受信スレッドは Unity API（<c>Debug.Log*</c>）を呼ばず、ここに記録するだけにとどめる（Req 10.7）。
        /// </summary>
        public bool TryTakeFault(out Exception fault)
        {
            fault = Interlocked.Exchange(ref _pendingFault, null);
            return fault != null;
        }

        private void RecordFault(Exception fault)
        {
            // 最初の例外を優先して保持する。
            Interlocked.CompareExchange(ref _pendingFault, fault, null);
        }

        public void Start(int port, in OscReceiveOptions options)
        {
            lock (_stateLock)
            {
                if (IsRunning) return;
                if (State == OscReceiveState.Stopping || (_thread != null && _thread.IsAlive))
                {
                    Debug.LogError("[OscReceiver] UDP receive loop is still stopping; Start is rejected.");
                    return;
                }

                _options = options;
                _stopRequested = 0;
                _faulted = 0;
                Volatile.Write(ref _state, (int)OscReceiveState.Stopped);
                _boundPort = port;
                try
                {
                    var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
                    if (options.SocketReceiveBufferBytes != 0)
                        socket.ReceiveBufferSize = options.SocketReceiveBufferBytes;
                    socket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
                    _socket = socket;

                    _thread = new Thread(ReceiveThreadMain)
                    {
                        IsBackground = true,
                        Name = "FacialControl.OscReceive:" + port
                    };
                    Volatile.Write(ref _isRunning, 1);
                    Volatile.Write(ref _state, (int)OscReceiveState.Running);
                    _thread.Start();
                }
                catch (Exception ex)
                {
                    Volatile.Write(ref _faulted, 1);
                    Volatile.Write(ref _isRunning, 0);
                    Volatile.Write(ref _state, (int)OscReceiveState.Faulted);
                    _thread = null;
                    CloseSocket();
                    Debug.LogError("[OscReceiver] UDP bind failed on port " + port + ": " + ex.Message);
                }
            }
        }

        public void Stop()
        {
            Thread thread;
            lock (_stateLock)
            {
                thread = _thread;
                if (thread == null && _socket == null) return;
                Volatile.Write(ref _stopRequested, 1);
                if (thread != null && thread.IsAlive)
                    Volatile.Write(ref _state, (int)OscReceiveState.Stopping);
                CloseSocket();
            }

            if (thread != null && thread != Thread.CurrentThread)
                thread.Join(500);

            if (thread == null || !thread.IsAlive)
            {
                Volatile.Write(ref _isRunning, 0);
                lock (_stateLock)
                {
                    _thread = null;
                    if (State != OscReceiveState.Faulted)
                        Volatile.Write(ref _state, (int)OscReceiveState.Stopped);
                }
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private void ReceiveThreadMain()
        {
            OscReceiveThreadHooks hooks = _threadHooks;
            try
            {
                hooks.OnThreadStarted?.Invoke();
                while (Volatile.Read(ref _stopRequested) == 0)
                {
                    if (!_ring.TryReserveSlot(out int slot)) continue;
                    bool committed = false;
                    try
                    {
                        // Span<byte> オーバーロードは Unity Mono で呼び出しごとにスロット長の一時配列を確保するため、
                        // backing 配列 + オフセットの byte[] オーバーロードで受ける（確保ゼロ、2026-09-15 実測）。
                        ArraySegment<byte> segment = _ring.GetSlotSegment(slot);
                        int length = _socket.Receive(segment.Array, segment.Offset, segment.Count, SocketFlags.None);
                        if (length > _ring.SlotBytes)
                        {
                            _diagnostics.IncrementOversizedDatagrams();
                            _ring.Abort(slot);
                            continue;
                        }

                        if (Volatile.Read(ref _stopRequested) != 0)
                        {
                            _ring.Abort(slot);
                            break;
                        }

                        OscAddressKeyTable table = TableProvider != null ? TableProvider() : null;
                        if (table == null)
                        {
                            _ring.Commit(slot, length, 0, 0);
                        }
                        else
                        {
                            _ring.ParseAndCommit(slot, length, table);
                        }
                        committed = true;
                        _diagnostics.IncrementReceivedDatagrams();
                        if (Volatile.Read(ref _stopRequested) == 0)
                            hooks.OnDatagramCommitted?.Invoke();
                    }
                    catch (SocketException ex) when (IsExpectedStop(ex))
                    {
                        if (!committed) _ring.Abort(slot);
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        if (!committed) _ring.Abort(slot);
                        if (Volatile.Read(ref _stopRequested) != 0) break;
                        throw;
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.MessageSize)
                    {
                        if (!committed) _ring.Abort(slot);
                        _diagnostics.IncrementOversizedDatagrams();
                    }
                    catch
                    {
                        if (!committed) _ring.Abort(slot);
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                if (Volatile.Read(ref _stopRequested) == 0)
                {
                    Volatile.Write(ref _faulted, 1);
                    Volatile.Write(ref _state, (int)OscReceiveState.Faulted);
                    RecordFault(ex);
                }
            }
            finally
            {
                try { hooks.OnThreadStopping?.Invoke(); }
                catch (Exception ex) { RecordFault(ex); }
                Volatile.Write(ref _isRunning, 0);
                CloseSocket();
                if (Volatile.Read(ref _stopRequested) != 0)
                {
                    try { _ring.Clear(); }
                    catch (Exception ex) { RecordFault(ex); }
                    Volatile.Write(ref _state, (int)OscReceiveState.Stopped);
                }
            }
        }

        private static bool IsExpectedStop(SocketException ex)
        {
            return ex.SocketErrorCode == SocketError.Interrupted ||
                   ex.SocketErrorCode == SocketError.OperationAborted ||
                   ex.SocketErrorCode == SocketError.NotSocket;
        }

        private void CloseSocket()
        {
            Socket socket = Interlocked.Exchange(ref _socket, null);
            if (socket == null) return;
            try { socket.Close(); }
            catch (SocketException) { }
        }
    }
}
