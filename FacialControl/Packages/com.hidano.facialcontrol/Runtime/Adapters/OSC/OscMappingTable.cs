using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Adapters.OSC
{
    /// <summary>
    /// config.json から読み込んだ OSC マッピングテーブル。
    /// OSC アドレス → (BlendShape 名, レイヤー) の高速変換を提供する。
    /// </summary>
    public sealed class OscMappingTable
    {
        private readonly OscMapping[] _mappings;

        // OSC アドレス → マッピングの高速逆引き
        private readonly Dictionary<string, OscMapping> _addressToMapping;

        // BlendShape 名 → マッピングの逆引き
        private readonly Dictionary<string, OscMapping> _blendShapeNameToMapping;

        // OSC アドレス → インデックスの逆引き
        private readonly Dictionary<string, int> _addressToIndex;

        // レイヤー名 → マッピングリストのグルーピング
        private readonly Dictionary<string, List<OscMapping>> _layerToMappings;

        /// <summary>
        /// マッピングエントリ数。
        /// </summary>
        public int Count => _mappings.Length;

        /// <summary>
        /// OscMapping 配列から OscMappingTable を構築する。
        /// </summary>
        /// <param name="mappings">OSC マッピング配列。</param>
        public OscMappingTable(OscMapping[] mappings)
        {
            if (mappings == null)
                throw new ArgumentNullException(nameof(mappings));

            _mappings = new OscMapping[mappings.Length];
            Array.Copy(mappings, _mappings, mappings.Length);

            _addressToMapping = new Dictionary<string, OscMapping>(mappings.Length, StringComparer.Ordinal);
            _blendShapeNameToMapping = new Dictionary<string, OscMapping>(mappings.Length, StringComparer.Ordinal);
            _addressToIndex = new Dictionary<string, int>(mappings.Length, StringComparer.Ordinal);
            _layerToMappings = new Dictionary<string, List<OscMapping>>(StringComparer.Ordinal);

            for (int i = 0; i < mappings.Length; i++)
            {
                var mapping = mappings[i];

                // OSC アドレス → マッピング（重複時は後勝ち）
                _addressToMapping[mapping.OscAddress] = mapping;
                _addressToIndex[mapping.OscAddress] = i;

                // BlendShape 名 → マッピング（重複時は後勝ち）
                _blendShapeNameToMapping[mapping.BlendShapeName] = mapping;

                // レイヤー別グルーピング
                if (!_layerToMappings.TryGetValue(mapping.Layer, out var list))
                {
                    list = new List<OscMapping>();
                    _layerToMappings[mapping.Layer] = list;
                }
                list.Add(mapping);
            }
        }

        /// <summary>
        /// OscConfiguration から OscMappingTable を構築する。
        /// </summary>
        /// <param name="config">OSC 設定。</param>
        public OscMappingTable(OscConfiguration config)
            : this(ToArray(config.Mapping))
        {
        }

        /// <summary>
        /// FacialControlConfig から OscMappingTable を構築する。
        /// </summary>
        /// <param name="config">FacialControl 設定。</param>
        public OscMappingTable(FacialControlConfig config)
            : this(config.Osc)
        {
        }

        /// <summary>
        /// OSC アドレスから OscMapping を検索する。
        /// </summary>
        /// <param name="address">OSC アドレス。</param>
        /// <param name="mapping">見つかった場合のマッピング。</param>
        /// <returns>見つかった場合 true。</returns>
        public bool TryGetByAddress(string address, out OscMapping mapping)
        {
            if (string.IsNullOrEmpty(address))
            {
                mapping = default;
                return false;
            }

            return _addressToMapping.TryGetValue(address, out mapping);
        }

        /// <summary>
        /// BlendShape 名から OscMapping を検索する。
        /// </summary>
        /// <param name="blendShapeName">BlendShape 名。</param>
        /// <param name="mapping">見つかった場合のマッピング。</param>
        /// <returns>見つかった場合 true。</returns>
        public bool TryGetByBlendShapeName(string blendShapeName, out OscMapping mapping)
        {
            if (string.IsNullOrEmpty(blendShapeName))
            {
                mapping = default;
                return false;
            }

            return _blendShapeNameToMapping.TryGetValue(blendShapeName, out mapping);
        }

        /// <summary>
        /// OSC アドレスからマッピング配列内のインデックスを取得する。
        /// </summary>
        /// <param name="address">OSC アドレス。</param>
        /// <param name="index">見つかった場合のインデックス。</param>
        /// <returns>見つかった場合 true。</returns>
        public bool TryGetIndex(string address, out int index)
        {
            if (string.IsNullOrEmpty(address))
            {
                index = -1;
                return false;
            }

            return _addressToIndex.TryGetValue(address, out index);
        }

        /// <summary>
        /// 全マッピングの防御的コピーを返す。
        /// </summary>
        /// <returns>全マッピング配列。</returns>
        public OscMapping[] GetMappings()
        {
            var copy = new OscMapping[_mappings.Length];
            Array.Copy(_mappings, copy, _mappings.Length);
            return copy;
        }

        /// <summary>
        /// 指定レイヤーに属するマッピングを返す。
        /// </summary>
        /// <param name="layer">レイヤー名。</param>
        /// <returns>該当するマッピング配列。見つからない場合は空配列。</returns>
        public OscMapping[] GetMappingsByLayer(string layer)
        {
            if (string.IsNullOrEmpty(layer))
                return Array.Empty<OscMapping>();

            if (_layerToMappings.TryGetValue(layer, out var list))
                return list.ToArray();

            return Array.Empty<OscMapping>();
        }

        private static OscMapping[] ToArray(ReadOnlyMemory<OscMapping> memory)
        {
            var span = memory.Span;
            var array = new OscMapping[span.Length];
            for (int i = 0; i < span.Length; i++)
            {
                array[i] = span[i];
            }
            return array;
        }
    }
}
