using System;
using UnityEngine.InputSystem;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Adapters.InputSources
{
    /// <summary>
    /// <see cref="InputAction"/> を <see cref="IAnalogInputSource"/> として公開するアダプタ
    /// 。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 形状 (<see cref="AnalogInputShape"/>) に応じて <see cref="AxisCount"/> が決まる:
    /// <list type="bullet">
    ///   <item><see cref="AnalogInputShape.Scalar"/> → AxisCount = 1</item>
    ///   <item><see cref="AnalogInputShape.Vector2"/> → AxisCount = 2</item>
    /// </list>
    /// </para>
    /// <para>
    /// <see cref="Tick"/> 内で形状に応じて <see cref="InputControl{TValue}.ReadValue"/> を 1 回だけ
    /// 呼び、結果をフィールドにキャッシュする。<see cref="InputAction.ReadValue{TValue}"/> 経由は
    /// boxing が発生する場合があるため、可能なら <see cref="InputControl{TValue}"/> を直叩きする
    /// 。fallback として typed control が解決できない場合は
    /// <see cref="InputAction.ReadValue{TValue}"/> を呼ぶ。
    /// </para>
    /// <para>
    /// 以降の <c>TryRead*</c> はフィールド参照のみで完結し、毎フレーム alloc=0 を満たす。
    /// </para>
    /// <para>
    /// validity:
    /// <list type="bullet">
    ///   <item><see cref="InputAction.enabled"/> が false の間は <see cref="IsValid"/> が false。</item>
    ///   <item><see cref="InputAction.controls"/> が空 (unbound) の間は <see cref="IsValid"/> が false。</item>
    ///   <item>無効状態でも <c>TryRead*</c> は false を返すのみで例外を投げない 。</item>
    /// </list>
    /// </para>
    /// </remarks>
    public sealed class InputActionAnalogSource : IAnalogInputSource, IDisposable
    {
        private readonly InputSourceId _id;
        private readonly InputAction _action;
        private readonly AnalogInputShape _shape;
        private readonly int _axisCount;

        private float _cachedX;
        private float _cachedY;
        private bool _isValid;
        private bool _disposed;

        /// <inheritdoc />
        public string Id => _id.Value;

        /// <inheritdoc />
        public bool IsValid => _isValid;

        /// <inheritdoc />
        public int AxisCount => _axisCount;

        /// <summary>
        /// <see cref="InputActionAnalogSource"/> を構築する。
        /// </summary>
        /// <param name="id">入力源識別子。</param>
        /// <param name="action">読み取り対象の <see cref="InputAction"/>。</param>
        /// <param name="shape">入力源の形状 (Scalar / Vector2)。</param>
        /// <exception cref="ArgumentException"><paramref name="id"/> が未初期化、または
        /// <paramref name="shape"/> が定義済 enum 外。</exception>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> が null。</exception>
        public InputActionAnalogSource(InputSourceId id, InputAction action, AnalogInputShape shape)
        {
            if (string.IsNullOrEmpty(id.Value))
                throw new ArgumentException("id は未初期化の InputSourceId にできません。", nameof(id));
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            switch (shape)
            {
                case AnalogInputShape.Scalar:
                    _axisCount = 1;
                    break;
                case AnalogInputShape.Vector2:
                    _axisCount = 2;
                    break;
                default:
                    throw new ArgumentException($"未対応の AnalogInputShape: {shape}", nameof(shape));
            }

            _id = id;
            _action = action;
            _shape = shape;
            _isValid = false;
        }

        /// <inheritdoc />
        public void Tick(float deltaTime)
        {
            if (_disposed)
                return;

            var controls = _action.controls;
            if (!_action.enabled || controls.Count == 0)
            {
                _isValid = false;
                return;
            }

            var first = controls[0];
            if (_shape == AnalogInputShape.Vector2)
            {
                if (first is InputControl<UnityEngine.Vector2> v2)
                {
                    var v = v2.ReadValue();
                    _cachedX = v.x;
                    _cachedY = v.y;
                }
                else
                {
                    var v = _action.ReadValue<UnityEngine.Vector2>();
                    _cachedX = v.x;
                    _cachedY = v.y;
                }
            }
            else
            {
                if (first is InputControl<float> f)
                {
                    _cachedX = f.ReadValue();
                }
                else
                {
                    _cachedX = _action.ReadValue<float>();
                }
                _cachedY = 0f;
            }

            _isValid = true;
        }

        /// <inheritdoc />
        public bool TryReadScalar(out float value)
        {
            if (!_isValid)
            {
                value = default;
                return false;
            }
            value = _cachedX;
            return true;
        }

        /// <inheritdoc />
        public bool TryReadVector2(out float x, out float y)
        {
            if (!_isValid || _shape != AnalogInputShape.Vector2)
            {
                x = default;
                y = default;
                return false;
            }
            x = _cachedX;
            y = _cachedY;
            return true;
        }

        /// <inheritdoc />
        public bool TryReadAxes(Span<float> output)
        {
            if (!_isValid || output.Length == 0)
                return false;

            output[0] = _cachedX;
            if (_axisCount >= 2 && output.Length >= 2)
            {
                output[1] = _cachedY;
            }
            return true;
        }

        /// <summary>
        /// 本アダプタが保持する状態を解放する。<see cref="InputAction"/> 自体の所有権は
        /// 呼出側にあり、本クラスは Disable / Dispose を行わない。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _isValid = false;
        }
    }
}
