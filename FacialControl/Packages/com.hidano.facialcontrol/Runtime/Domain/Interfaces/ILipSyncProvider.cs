using System;
using System.Collections;

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

    /// <summary>
    /// リップシンク provider が書き込む BlendShape index 集合を公開する任意契約。
    /// </summary>
    /// <remarks>
    /// <see cref="ContributeMask"/> は構築時に確定した事前確保済み参照を返す。
    /// この契約を実装しない provider は、後方互換のため全 index contribute として扱われる。
    /// </remarks>
    public interface ILipSyncContributeMaskProvider
    {
        /// <summary>
        /// provider が contribute する BlendShape index 集合。
        /// </summary>
        BitArray ContributeMask { get; }
    }
}
