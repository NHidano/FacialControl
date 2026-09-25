using System;
using System.Collections.Generic;
using UnityEngine;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Adapters.Playable
{
    /// <summary>
    /// BlendShapeValues から AnimationClip を動的生成し、LRU キャッシュで管理するクラス。
    /// キャッシュヒット時は GC フリー。キャッシュミス時のみ AnimationClip を新規生成する。
    /// デフォルト 16 エントリ。
    /// </summary>
    public class AnimationClipCache : IDisposable
    {
        private readonly int _capacity;
        private readonly Dictionary<string, LinkedListNode<CacheEntry>> _lookup;
        private readonly LinkedList<CacheEntry> _order;
        private bool _disposed;

        /// <summary>
        /// 現在のキャッシュ容量。
        /// </summary>
        public int Capacity => _capacity;

        /// <summary>
        /// 現在のキャッシュエントリ数。
        /// </summary>
        public int Count => _lookup.Count;

        /// <summary>
        /// 指定容量で AnimationClipCache を初期化する。
        /// </summary>
        /// <param name="capacity">キャッシュの最大エントリ数。デフォルト 16。1 以上を指定。</param>
        public AnimationClipCache(int capacity = CacheConfiguration.DefaultAnimationClipLruSize)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "キャッシュ容量は 1 以上で指定してください。");
            }

            _capacity = capacity;
            _lookup = new Dictionary<string, LinkedListNode<CacheEntry>>(capacity);
            _order = new LinkedList<CacheEntry>();
        }

        /// <summary>
        /// 指定された Expression ID に対応する AnimationClip を取得する。
        /// キャッシュにヒットした場合は既存の AnimationClip を返し、LRU 順序を更新する。
        /// キャッシュミスの場合は blendShapeValues から AnimationClip を動的生成してキャッシュに追加する。
        /// </summary>
        /// <param name="expressionId">Expression の一意識別子。</param>
        /// <param name="blendShapeValues">AnimationClip 生成用の BlendShape 値配列。</param>
        /// <returns>生成またはキャッシュ済みの AnimationClip。</returns>
        public AnimationClip GetOrCreate(string expressionId, BlendShapeMapping[] blendShapeValues)
        {
            if (expressionId == null)
                throw new ArgumentNullException(nameof(expressionId));
            if (blendShapeValues == null)
                throw new ArgumentNullException(nameof(blendShapeValues));

            // キャッシュヒット
            if (_lookup.TryGetValue(expressionId, out var node))
            {
                // LRU 順序を更新（先頭 = 最近使用）
                _order.Remove(node);
                _order.AddFirst(node);
                return node.Value.Clip;
            }

            // キャッシュミス — AnimationClip を動的生成
            var clip = CreateAnimationClip(blendShapeValues);

            var entry = new CacheEntry(expressionId, clip);
            var newNode = _order.AddFirst(entry);
            _lookup[expressionId] = newNode;

            // キャッシュ容量超過時は LRU エントリを破棄
            if (_lookup.Count > _capacity)
            {
                EvictLeastRecentlyUsed();
            }

            return clip;
        }

        /// <summary>
        /// 指定された Expression ID がキャッシュに存在するか確認する。
        /// </summary>
        public bool ContainsKey(string expressionId)
        {
            return _lookup.ContainsKey(expressionId);
        }

        /// <summary>
        /// キャッシュ内の全エントリを破棄し、AnimationClip を解放する。
        /// </summary>
        public void Clear()
        {
            foreach (var node in _order)
            {
                DestroyClip(node.Clip);
            }

            _lookup.Clear();
            _order.Clear();
        }

        /// <summary>
        /// キャッシュを破棄し、全 AnimationClip を解放する。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            Clear();
            _disposed = true;
        }

        private void EvictLeastRecentlyUsed()
        {
            var lastNode = _order.Last;
            if (lastNode == null)
                return;

            _lookup.Remove(lastNode.Value.ExpressionId);
            _order.RemoveLast();
            DestroyClip(lastNode.Value.Clip);
        }

        private static AnimationClip CreateAnimationClip(BlendShapeMapping[] blendShapeValues)
        {
            var clip = new AnimationClip();

            for (int i = 0; i < blendShapeValues.Length; i++)
            {
                var mapping = blendShapeValues[i];
                // BlendShape のアニメーションカーブを設定
                // パスは Renderer 指定があれば使用、なければ空文字列（ルート相対）
                var curve = new AnimationCurve(new Keyframe(0f, mapping.Value * 100f));
                var path = mapping.Renderer ?? "";
                var propertyName = "blendShape." + mapping.Name;
                clip.SetCurve(path, typeof(SkinnedMeshRenderer), propertyName, curve);
            }

            return clip;
        }

        private static void DestroyClip(AnimationClip clip)
        {
            if (clip != null)
            {
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        private readonly struct CacheEntry
        {
            public string ExpressionId { get; }
            public AnimationClip Clip { get; }

            public CacheEntry(string expressionId, AnimationClip clip)
            {
                ExpressionId = expressionId;
                Clip = clip;
            }
        }
    }
}
