using System;
using System.Threading;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Adapters.InputSources
{
    /// <summary>
    /// ARKit 52ch (PerfectSync) を 1 つの N-axis <see cref="IAnalogInputSource"/> として公開する
    /// アダプタ 。
    /// </summary>
    /// <remarks>
    /// 内部で <c>arkitParameterNames[i]</c> を <c>"/ARKit/{name}"</c> アドレスとして
    /// <see cref="OscReceiver.RegisterAnalogListener"/> で購読する。
    /// 各受信は per-axis の <c>_pendingValues[i]</c> へ <see cref="Volatile.Write(ref float, float)"/> され、
    /// 任意の axis の受信が共有 <c>_writeTick</c> を <see cref="Interlocked.Increment(ref int)"/> する。
    /// staleness は source 全体に対して 1 つ管理し、いずれかの axis に新規受信があれば
    /// staleness カウンタはリセットされる。
    /// </remarks>
    public sealed class ArKitOscAnalogSource : IAnalogInputSource, IDisposable
    {
        /// <summary>ARKit OSC アドレスプレフィックス。</summary>
        public const string ArKitAddressPrefix = "/ARKit/";

        private readonly InputSourceId _id;
        private readonly OscReceiver _receiver;
        private readonly string[] _arkitParameterNames;
        private readonly string[] _addresses;
        private readonly Action<float>[] _listeners;
        private readonly float _stalenessSeconds;

        // 受信スレッドが書込み、メインスレッドが読取
        private readonly float[] _pendingValues;
        private int _writeTick;

        // メインスレッド占有
        private int _lastObservedTick;
        private readonly float[] _cachedValues;
        private float _secondsSinceLastReceive;
        private bool _hasReceived;
        private bool _isValid;
        private bool _disposed;

        /// <inheritdoc />
        public string Id => _id.Value;

        /// <inheritdoc />
        public bool IsValid => _isValid;

        /// <inheritdoc />
        public int AxisCount => _arkitParameterNames.Length;

        /// <summary>
        /// ARKit OSC source を構築し、各 ARKit パラメータ名に対応する <c>/ARKit/{name}</c>
        /// アドレスを購読する。
        /// </summary>
        /// <param name="id">入力源識別子。</param>
        /// <param name="receiver">listener 登録先の <see cref="OscReceiver"/>。</param>
        /// <param name="arkitParameterNames">ARKit パラメータ名配列（e.g. <c>jawOpen</c>, <c>eyeBlinkLeft</c>, ... 52ch）。</param>
        /// <param name="stalenessSeconds">staleness 判定秒数。0 で last-valid 永続。</param>
        /// <exception cref="ArgumentException"><paramref name="id"/> 未初期化、<paramref name="arkitParameterNames"/> 空、または個別の名前が null/空。</exception>
        /// <exception cref="ArgumentNullException"><paramref name="receiver"/> または <paramref name="arkitParameterNames"/> が null。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="stalenessSeconds"/> が負。</exception>
        public ArKitOscAnalogSource(InputSourceId id, OscReceiver receiver, string[] arkitParameterNames, float stalenessSeconds)
        {
            if (string.IsNullOrEmpty(id.Value))
                throw new ArgumentException("id は未初期化の InputSourceId にできません。", nameof(id));
            if (receiver == null)
                throw new ArgumentNullException(nameof(receiver));
            if (arkitParameterNames == null)
                throw new ArgumentNullException(nameof(arkitParameterNames));
            if (arkitParameterNames.Length == 0)
                throw new ArgumentException("arkitParameterNames は 1 件以上必要です。", nameof(arkitParameterNames));
            if (stalenessSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(stalenessSeconds), stalenessSeconds,
                    "stalenessSeconds は 0 以上を指定してください。");

            _id = id;
            _receiver = receiver;
            _stalenessSeconds = stalenessSeconds;

            int count = arkitParameterNames.Length;
            _arkitParameterNames = new string[count];
            _addresses = new string[count];
            _listeners = new Action<float>[count];
            _pendingValues = new float[count];
            _cachedValues = new float[count];

            for (int i = 0; i < count; i++)
            {
                string name = arkitParameterNames[i];
                if (string.IsNullOrEmpty(name))
                    throw new ArgumentException(
                        $"arkitParameterNames[{i}] が null/空です。",
                        nameof(arkitParameterNames));

                _arkitParameterNames[i] = name;
                _addresses[i] = ArKitAddressPrefix + name;
                int axisIndex = i; // capture
                _listeners[i] = value => OnOscValueReceived(axisIndex, value);
            }

            _isValid = false;

            for (int i = 0; i < count; i++)
            {
                _receiver.RegisterAnalogListener(_addresses[i], _listeners[i]);
            }
        }

        private void OnOscValueReceived(int axisIndex, float value)
        {
            Volatile.Write(ref _pendingValues[axisIndex], value);
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
                for (int i = 0; i < _cachedValues.Length; i++)
                {
                    _cachedValues[i] = Volatile.Read(ref _pendingValues[i]);
                }
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
            if (!_isValid || _cachedValues.Length < 1)
            {
                value = default;
                return false;
            }
            value = _cachedValues[0];
            return true;
        }

        /// <inheritdoc />
        public bool TryReadVector2(out float x, out float y)
        {
            if (!_isValid || _cachedValues.Length < 2)
            {
                x = default;
                y = default;
                return false;
            }
            x = _cachedValues[0];
            y = _cachedValues[1];
            return true;
        }

        /// <inheritdoc />
        public bool TryReadAxes(Span<float> output)
        {
            if (!_isValid || output.Length == 0)
                return false;

            int copyLength = output.Length < _cachedValues.Length ? output.Length : _cachedValues.Length;
            for (int i = 0; i < copyLength; i++)
            {
                output[i] = _cachedValues[i];
            }
            return true;
        }

        /// <summary>
        /// 全 listener を解除する。重複呼出は no-op。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            for (int i = 0; i < _addresses.Length; i++)
            {
                _receiver.UnregisterAnalogListener(_addresses[i], _listeners[i]);
            }
        }
    }
}
