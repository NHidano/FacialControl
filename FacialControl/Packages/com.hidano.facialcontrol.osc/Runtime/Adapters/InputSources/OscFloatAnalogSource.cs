using System;
using System.Threading;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Adapters.InputSources
{
    /// <summary>
    /// 単一 OSC アドレス (e.g. <c>/avatar/parameters/jawOpen</c>) を購読する scalar
    /// <see cref="IAnalogInputSource"/> 実装 。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 受信スレッドが <see cref="OscReceiver.RegisterAnalogListener"/> 経由で float を渡し、
    /// <see cref="Volatile.Write(ref float, float)"/> で <c>_pendingValue</c> に書込む。
    /// </para>
    /// <para>
    /// メインスレッドの <see cref="Tick"/> で <see cref="Volatile.Read(ref float)"/> によりキャッシュへ転写し、
    /// <see cref="Interlocked.Increment(ref int)"/> された tick の差分で「新規受信があったフレーム」を観測する。
    /// </para>
    /// <para>
    /// staleness:
    /// <list type="bullet">
    ///   <item><c>stalenessSeconds &gt; 0</c> のとき最終受信から経過秒が閾値を超えると <see cref="IsValid"/> が false に落ちる。</item>
    ///   <item><c>stalenessSeconds == 0</c> のとき初回受信以降は <see cref="IsValid"/> が常に true。</item>
    ///   <item>受信前は <see cref="IsValid"/> は false。</item>
    /// </list>
    /// </para>
    /// </remarks>
    public sealed class OscFloatAnalogSource : IAnalogInputSource, IDisposable
    {
        private readonly InputSourceId _id;
        private readonly OscReceiver _receiver;
        private readonly string _address;
        private readonly float _stalenessSeconds;
        private readonly Action<float> _listener;

        // 受信スレッドが書込み、メインスレッドが読取
        private float _pendingValue;
        private int _writeTick;

        // メインスレッド占有
        private int _lastObservedTick;
        private float _cachedValue;
        private float _secondsSinceLastReceive;
        private bool _hasReceived;
        private bool _isValid;
        private bool _disposed;

        /// <inheritdoc />
        public string Id => _id.Value;

        /// <inheritdoc />
        public bool IsValid => _isValid;

        /// <inheritdoc />
        public int AxisCount => 1;

        /// <summary>
        /// <see cref="OscFloatAnalogSource"/> を構築し、<paramref name="receiver"/> に listener を登録する。
        /// </summary>
        /// <param name="id">入力源識別子。</param>
        /// <param name="receiver">listener 登録先の <see cref="OscReceiver"/>。</param>
        /// <param name="address">購読する OSC アドレス（完全一致、e.g. <c>/avatar/parameters/jawOpen</c>）。</param>
        /// <param name="stalenessSeconds">staleness 判定秒数。0 で last-valid 永続。</param>
        /// <exception cref="ArgumentNullException"><paramref name="receiver"/> が null。</exception>
        /// <exception cref="ArgumentException"><paramref name="address"/> が null/空 または <paramref name="id"/> が未初期化。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="stalenessSeconds"/> が負。</exception>
        public OscFloatAnalogSource(InputSourceId id, OscReceiver receiver, string address, float stalenessSeconds)
        {
            if (string.IsNullOrEmpty(id.Value))
                throw new ArgumentException("id は未初期化の InputSourceId にできません。", nameof(id));
            if (receiver == null)
                throw new ArgumentNullException(nameof(receiver));
            if (string.IsNullOrEmpty(address))
                throw new ArgumentException("address は null/空にできません。", nameof(address));
            if (stalenessSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(stalenessSeconds), stalenessSeconds,
                    "stalenessSeconds は 0 以上を指定してください。");

            _id = id;
            _receiver = receiver;
            _address = address;
            _stalenessSeconds = stalenessSeconds;
            _listener = OnOscValueReceived;
            _isValid = false;

            _receiver.RegisterAnalogListener(_address, _listener);
        }

        /// <summary>
        /// 受信スレッドから呼ばれるコールバック。Volatile.Write でメインスレッドへ伝搬する。
        /// </summary>
        private void OnOscValueReceived(float value)
        {
            Volatile.Write(ref _pendingValue, value);
            Interlocked.Increment(ref _writeTick);
        }

        /// <inheritdoc />
        public void Tick(float deltaTime)
        {
            if (_disposed)
                return;

            int currentTick = Volatile.Read(ref _writeTick);
            if (currentTick != _lastObservedTick)
            {
                _lastObservedTick = currentTick;
                _cachedValue = Volatile.Read(ref _pendingValue);
                _secondsSinceLastReceive = 0f;
                _hasReceived = true;
                _isValid = true;
            }
            else if (_hasReceived)
            {
                _secondsSinceLastReceive += deltaTime;
                if (_stalenessSeconds > 0f && _secondsSinceLastReceive > _stalenessSeconds)
                {
                    _isValid = false;
                }
            }
        }

        /// <inheritdoc />
        public bool TryReadScalar(out float value)
        {
            if (!_isValid)
            {
                value = default;
                return false;
            }
            value = _cachedValue;
            return true;
        }

        /// <inheritdoc />
        public bool TryReadVector2(out float x, out float y)
        {
            x = default;
            y = default;
            return false;
        }

        /// <inheritdoc />
        public bool TryReadAxes(Span<float> output)
        {
            if (!_isValid || output.Length == 0)
                return false;

            output[0] = _cachedValue;
            return true;
        }

        /// <summary>
        /// listener を解除する。重複呼出は no-op。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            _receiver.UnregisterAnalogListener(_address, _listener);
        }
    }
}
