using System;
using System.Collections.Generic;
using UnityEngine.Animations;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Adapters.Playable
{
    /// <summary>
    /// BlendShape → PropertyStreamHandle のマッピングをキャッシュするクラス。
    /// Expression 切替時に未取得分のみファクトリ経由で新規取得し、取得済みはキャッシュから再利用する。
    /// </summary>
    public class PropertyStreamHandleCache
    {
        private readonly Dictionary<string, PropertyStreamHandle> _cache = new Dictionary<string, PropertyStreamHandle>();

        /// <summary>
        /// 現在のキャッシュエントリ数。
        /// </summary>
        public int Count => _cache.Count;

        /// <summary>
        /// 指定された BlendShapeMapping 配列に対して、未キャッシュの BlendShape のみファクトリを呼び出して PropertyStreamHandle を取得しキャッシュする。
        /// 既にキャッシュ済みの BlendShape はスキップする。
        /// </summary>
        /// <param name="mappings">BlendShapeMapping 配列。</param>
        /// <param name="handleFactory">BlendShape 名と Renderer パスから PropertyStreamHandle を生成するファクトリ関数。</param>
        public void EnsureHandles(BlendShapeMapping[] mappings, Func<string, string, PropertyStreamHandle> handleFactory)
        {
            if (mappings == null)
                throw new ArgumentNullException(nameof(mappings));
            if (handleFactory == null)
                throw new ArgumentNullException(nameof(handleFactory));

            for (int i = 0; i < mappings.Length; i++)
            {
                var mapping = mappings[i];
                var key = MakeKey(mapping.Name, mapping.Renderer);

                if (!_cache.ContainsKey(key))
                {
                    var handle = handleFactory(mapping.Name, mapping.Renderer);
                    _cache[key] = handle;
                }
            }
        }

        /// <summary>
        /// 指定された BlendShape 名と Renderer パスに対応する PropertyStreamHandle を取得する。
        /// </summary>
        /// <param name="blendShapeName">BlendShape 名。</param>
        /// <param name="renderer">Renderer パス。null の場合は全 Renderer 対象。</param>
        /// <param name="handle">取得した PropertyStreamHandle。見つからない場合は default。</param>
        /// <returns>キャッシュにエントリが存在する場合は true。</returns>
        public bool TryGetHandle(string blendShapeName, string renderer, out PropertyStreamHandle handle)
        {
            var key = MakeKey(blendShapeName, renderer);
            return _cache.TryGetValue(key, out handle);
        }

        /// <summary>
        /// 指定された BlendShape 名と Renderer パスがキャッシュに存在するか確認する。
        /// </summary>
        /// <param name="blendShapeName">BlendShape 名。</param>
        /// <param name="renderer">Renderer パス。null の場合は全 Renderer 対象。</param>
        /// <returns>キャッシュにエントリが存在する場合は true。</returns>
        public bool ContainsKey(string blendShapeName, string renderer)
        {
            var key = MakeKey(blendShapeName, renderer);
            return _cache.ContainsKey(key);
        }

        /// <summary>
        /// キャッシュ内の全エントリを削除する。
        /// </summary>
        public void Clear()
        {
            _cache.Clear();
        }

        private static string MakeKey(string blendShapeName, string renderer)
        {
            return renderer == null ? blendShapeName : renderer + "/" + blendShapeName;
        }
    }
}
