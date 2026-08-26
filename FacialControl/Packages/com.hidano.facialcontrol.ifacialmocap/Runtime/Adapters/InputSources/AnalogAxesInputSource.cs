using System;
using System.Collections;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Adapters.InputSources
{
    /// <summary>
    /// N 軸の値をそのまま公開する push 型 <see cref="IAnalogInputSource"/>。
    /// iFacialMocap の頭部ポーズ（オイラー角 + 任意で位置）を analog 入力源として登録するために使う。
    /// </summary>
    /// <remarks>
    /// <para>
    /// BlendShape には寄与しない（<see cref="BlendShapeCount"/> = 0、<see cref="ContributeMask"/> は空）。
    /// 頭部ボーンへの反映は Profile 側の <c>AnalogBindingEntry</c>（TargetKind=BonePose）で
    /// 各軸 → bone Euler/位置へ結線する。
    /// </para>
    /// <para>
    /// <see cref="GazeVector2InputSource"/> と同様、<see cref="Publish(System.ReadOnlySpan{float})"/> /
    /// <see cref="TryReadAxes"/> は同一スレッド（main の OnFixedTick）から呼ばれる前提でロックを持たない。
    /// </para>
    /// </remarks>
    public sealed class AnalogAxesInputSource : IInputSource, IAnalogInputSource
    {
        private readonly InputSourceId _id;
        private readonly BitArray _contributeMask;
        private readonly float[] _values;
        private bool _isValid;

        /// <summary>
        /// N 軸の push 型入力源を構築する。
        /// </summary>
        /// <param name="id">入力源識別子（<c>slug:sub</c> 形式可）。</param>
        /// <param name="axisCount">軸数（&gt;= 1）。</param>
        /// <exception cref="ArgumentException"><paramref name="id"/> が未初期化。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="axisCount"/> が 1 未満。</exception>
        public AnalogAxesInputSource(InputSourceId id, int axisCount)
        {
            if (string.IsNullOrEmpty(id.Value))
            {
                throw new ArgumentException("id must be an initialized InputSourceId.", nameof(id));
            }

            if (axisCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(axisCount), axisCount, "axisCount must be >= 1.");
            }

            _id = id;
            _values = new float[axisCount];
            _contributeMask = new BitArray(0);
        }

        /// <inheritdoc />
        public string Id => _id.Value;

        /// <inheritdoc />
        public InputSourceType Type => InputSourceType.ValueProvider;

        /// <inheritdoc />
        public int BlendShapeCount => 0;

        /// <inheritdoc />
        public BitArray ContributeMask => _contributeMask;

        /// <inheritdoc />
        public bool IsValid => _isValid;

        /// <inheritdoc />
        public int AxisCount => _values.Length;

        /// <summary>現在値を更新し有効化する。<paramref name="values"/> が短い場合は overlap のみ更新。</summary>
        public void Publish(ReadOnlySpan<float> values)
        {
            int count = Math.Min(values.Length, _values.Length);
            for (int i = 0; i < count; i++)
            {
                _values[i] = values[i];
            }

            _isValid = true;
        }

        /// <summary>全軸を 0 にして有効化する（fail-safe 復帰用）。</summary>
        public void PublishZero()
        {
            Array.Clear(_values, 0, _values.Length);
            _isValid = true;
        }

        /// <summary>無効化する。以後 <c>TryRead*</c> は false を返す。</summary>
        public void Invalidate()
        {
            _isValid = false;
        }

        /// <inheritdoc />
        public void Tick(float deltaTime)
        {
        }

        /// <inheritdoc />
        public bool TryWriteValues(Span<float> output)
        {
            return false;
        }

        /// <inheritdoc />
        public bool TryReadScalar(out float value)
        {
            if (!_isValid)
            {
                value = default;
                return false;
            }

            value = _values[0];
            return true;
        }

        /// <inheritdoc />
        public bool TryReadVector2(out float x, out float y)
        {
            if (!_isValid || _values.Length < 2)
            {
                x = default;
                y = default;
                return false;
            }

            x = _values[0];
            y = _values[1];
            return true;
        }

        /// <inheritdoc />
        public bool TryReadAxes(Span<float> output)
        {
            if (!_isValid || output.Length == 0)
            {
                return false;
            }

            int count = Math.Min(output.Length, _values.Length);
            for (int i = 0; i < count; i++)
            {
                output[i] = _values[i];
            }

            return true;
        }
    }
}
