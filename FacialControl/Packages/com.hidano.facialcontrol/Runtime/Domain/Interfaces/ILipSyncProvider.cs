using System;

namespace Hidano.FacialControl.Domain.Interfaces
{
    /// <summary>
    /// リップシンクの BlendShape 値を提供するインターフェース。
    /// 外部プラグイン（uLipSync 等）がこのインターフェースを実装し、
    /// FacialControl はリップシンク用レイヤーに値を適用する。
    /// 内部実装は固定長バッファで GC フリーであることを想定する。
    /// </summary>
    public interface ILipSyncProvider
    {
        /// <summary>
        /// リップシンクの BlendShape 値を取得する。
        /// 内部実装は固定長バッファで GC フリー。
        /// </summary>
        /// <param name="output">BlendShape 値の出力先バッファ</param>
        void GetLipSyncValues(Span<float> output);

        /// <summary>
        /// 対応する BlendShape 名の一覧を取得する。
        /// </summary>
        ReadOnlySpan<string> BlendShapeNames { get; }
    }
}
