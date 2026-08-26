using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Hidano.FacialControl.Editor.Common
{
    /// <summary>
    /// 参照モデルから BlendShape 名候補を列挙するユーティリティ。
    /// Editor 拡張の BlendShape 名入力におけるタイポ防止を目的とし、
    /// FacialProfileSOEditor や ExpressionCreatorWindow のドロップダウン候補生成に使用する。
    /// </summary>
    /// <remarks>
    /// - 参照モデル配下の全 SkinnedMeshRenderer から BlendShape 名を収集する
    /// - 重複排除およびアルファベット順ソート済みの配列を返す
    /// - null 参照モデル・null SkinnedMeshRenderer・null sharedMesh はスキップ
    /// - 候補が 0 件の場合は空配列（<see cref="Array.Empty{T}"/>）を返す
    /// </remarks>
    public static class BlendShapeNameProvider
    {
        /// <summary>
        /// 参照モデル（GameObject）配下の全 SkinnedMeshRenderer から BlendShape 名を列挙する。
        /// </summary>
        /// <param name="referenceModel">BlendShape 名を取得するモデルルート。null の場合は空配列を返す</param>
        /// <returns>重複排除・アルファベット順ソート済みの BlendShape 名配列</returns>
        public static string[] GetBlendShapeNames(GameObject referenceModel)
        {
            if (referenceModel == null)
                return Array.Empty<string>();

            var renderers = referenceModel.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true);
            return GetBlendShapeNames(renderers);
        }

        /// <summary>
        /// SkinnedMeshRenderer 配列から BlendShape 名を列挙する。
        /// </summary>
        /// <param name="renderers">対象の SkinnedMeshRenderer 配列。null 要素および sharedMesh == null の要素はスキップ</param>
        /// <returns>重複排除・アルファベット順ソート済みの BlendShape 名配列</returns>
        public static string[] GetBlendShapeNames(SkinnedMeshRenderer[] renderers)
        {
            if (renderers == null || renderers.Length == 0)
                return Array.Empty<string>();

            var nameSet = new HashSet<string>();
            for (int i = 0; i < renderers.Length; i++)
            {
                var smr = renderers[i];
                if (smr == null)
                    continue;

                var mesh = smr.sharedMesh;
                if (mesh == null)
                    continue;

                int count = mesh.blendShapeCount;
                for (int j = 0; j < count; j++)
                {
                    var shapeName = mesh.GetBlendShapeName(j);
                    if (!string.IsNullOrEmpty(shapeName))
                    {
                        nameSet.Add(shapeName);
                    }
                }
            }

            if (nameSet.Count == 0)
                return Array.Empty<string>();

            return nameSet.OrderBy(n => n, StringComparer.Ordinal).ToArray();
        }
    }
}
