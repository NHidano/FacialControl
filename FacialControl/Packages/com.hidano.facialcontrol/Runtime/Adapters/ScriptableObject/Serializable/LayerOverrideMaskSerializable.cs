using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Adapters.ScriptableObject.Serializable
{
    /// <summary>
    /// <see cref="LayerOverrideMask"/> （Domain 値型: <c>[Flags] enum int</c>）と
    /// レイヤー名配列（永続化形式）の相互変換ヘルパー。
    /// <para>
    /// Domain は bit position と layer 名の対応を関知せず、bit 位置 ↔ layer 名 の
    /// 紐付けは <c>FacialCharacterProfileSO.Layers</c> の宣言順を起点として
    /// Adapters 側でのみ解決される。
    /// </para>
    /// <para>
    /// 永続化フォーマットは layer 名配列（例: <c>["emotion", "eye"]</c>）であり、
    /// レイヤーの並び替えに対して bit 値を保持しない仕様としている（破壊的変更耐性のため）。
    /// </para>
    /// </summary>
    public static class LayerOverrideMaskSerializable
    {
        /// <summary>
        /// レイヤー名配列を <see cref="LayerOverrideMask"/> へ変換する。
        /// </summary>
        /// <param name="layerNames">永続化されたレイヤー名配列（部分集合）。null は <see cref="LayerOverrideMask.None"/> を返す。</param>
        /// <param name="orderedLayerNames">対応表となる順序付きレイヤー名（<c>FacialProfile.Layers</c> の宣言順）。</param>
        /// <returns>対応する bit を立てた <see cref="LayerOverrideMask"/>。
        /// <paramref name="orderedLayerNames"/> 未登録の名前または 32 番目以降の bit はサイレントに無視する。</returns>
        public static LayerOverrideMask ToMask(
            IReadOnlyList<string> layerNames,
            IReadOnlyList<string> orderedLayerNames)
        {
            if (layerNames == null || layerNames.Count == 0 || orderedLayerNames == null || orderedLayerNames.Count == 0)
            {
                return LayerOverrideMask.None;
            }

            int mask = 0;
            int maxBits = orderedLayerNames.Count > 32 ? 32 : orderedLayerNames.Count;

            for (int i = 0; i < layerNames.Count; i++)
            {
                var name = layerNames[i];
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                for (int b = 0; b < maxBits; b++)
                {
                    if (string.Equals(orderedLayerNames[b], name, System.StringComparison.Ordinal))
                    {
                        mask |= 1 << b;
                        break;
                    }
                }
            }

            return (LayerOverrideMask)mask;
        }

        /// <summary>
        /// <see cref="LayerOverrideMask"/> をレイヤー名配列へ変換する。
        /// </summary>
        /// <param name="mask">変換元の <see cref="LayerOverrideMask"/>。</param>
        /// <param name="orderedLayerNames">対応表となる順序付きレイヤー名（<c>FacialProfile.Layers</c> の宣言順）。</param>
        /// <returns>立っている bit に対応する layer 名のリスト。
        /// <paramref name="orderedLayerNames"/> の範囲外の bit はサイレントに無視する。</returns>
        public static List<string> ToLayerNames(
            LayerOverrideMask mask,
            IReadOnlyList<string> orderedLayerNames)
        {
            var result = new List<string>();
            if (orderedLayerNames == null || orderedLayerNames.Count == 0 || mask == LayerOverrideMask.None)
            {
                return result;
            }

            int maxBits = orderedLayerNames.Count > 32 ? 32 : orderedLayerNames.Count;
            int bits = (int)mask;

            for (int b = 0; b < maxBits; b++)
            {
                if ((bits & (1 << b)) != 0)
                {
                    result.Add(orderedLayerNames[b]);
                }
            }

            return result;
        }
    }
}
