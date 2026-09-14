using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.OSC
{
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
        private int _boundPort = -1;

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

        public bool IsRunning => Volatile.Read(ref _isRunning) != 0;
        public bool Faulted => Volatile.Read(ref _faulted) != 0;
        public int BoundPort => Volatile.Read(ref _boundPort);

        public void Start(int port, in OscReceiveOptions options)
        {
            lock (_stateLock)
            {
                if (IsRunning) return;
                if (_thread != null && _thread.IsAlive) return;

                _options = options;
                _stopRequested = 0;
                _faulted = 0;
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
                    _thread.Start();
                }
                catch (Exception ex)
                {
                    Volatile.Write(ref _faulted, 1);
                    Volatile.Write(ref _isRunning, 0);
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
                CloseSocket();
            }

            if (thread != null && thread != Thread.CurrentThread)
                thread.Join(500);

            if (thread == null || !thread.IsAlive)
            {
                Volatile.Write(ref _isRunning, 0);
                _ring.Clear();
                lock (_stateLock) _thread = null;
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
                        int length = _socket.Receive(_ring.GetSlotBytes(slot), SocketFlags.None);
                        if (length > _ring.SlotBytes)
                        {
                            _diagnostics.IncrementOversizedDatagrams();
                            _ring.Abort(slot);
                            continue;
                        }

                        _ring.Commit(slot, length, 0, 0);
                        committed = true;
                        _diagnostics.IncrementReceivedDatagrams();
                        hooks.OnDatagramCommitted?.Invoke();
                        if (_options.CaptureThreadAllocationStats)
                            _diagnostics.SetReceiveThreadAllocatedBytes(GC.GetAllocatedBytesForCurrentThread());
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
                    Debug.LogException(ex);
                }
            }
            finally
            {
                try { hooks.OnThreadStopping?.Invoke(); }
                catch (Exception ex) { Debug.LogException(ex); }
                Volatile.Write(ref _isRunning, 0);
                CloseSocket();
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
