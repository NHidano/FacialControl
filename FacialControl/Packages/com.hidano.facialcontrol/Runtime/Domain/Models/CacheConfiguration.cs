using System;

namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// キャッシュ設定。AnimationClip の LRU キャッシュサイズ等を保持する。
    /// </summary>
    public readonly struct CacheConfiguration
    {
        /// <summary>
        /// デフォルトの AnimationClip LRU キャッシュサイズ
        /// </summary>
        public const int DefaultAnimationClipLruSize = 16;

        /// <summary>
        /// AnimationClip LRU キャッシュの最大エントリ数
        /// </summary>
        public int AnimationClipLruSize { get; }

        public CacheConfiguration(int animationClipLruSize = DefaultAnimationClipLruSize)
        {
            if (animationClipLruSize < 1)
                throw new ArgumentOutOfRangeException(nameof(animationClipLruSize), "LRU キャッシュサイズは 1 以上で指定してください。");

            AnimationClipLruSize = animationClipLruSize;
        }
    }
}
