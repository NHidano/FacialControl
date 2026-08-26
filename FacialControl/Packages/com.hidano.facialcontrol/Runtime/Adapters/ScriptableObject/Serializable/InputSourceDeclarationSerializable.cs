using System;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.ScriptableObject.Serializable
{
    /// <summary>
    /// レイヤーごとの入力源宣言 (旧 JSON: layers[].inputSources[])。
    /// <see cref="Hidano.FacialControl.Domain.Models.InputSourceDeclaration"/> の Unity Serializable 投影。
    /// </summary>
    [Serializable]
    public sealed class InputSourceDeclarationSerializable
    {
        [Tooltip("入力源 ID。予約 ID (input / osc / lipsync / analog-blendshape / analog-bonepose) または x- プレフィックス拡張のみ。")]
        public string id;

        [Tooltip("ブレンドウェイト (0〜1)。レイヤー内で複数ソースを混ぜる際の比率。")]
        [Range(0f, 1f)]
        public float weight = 1.0f;

        [Tooltip("入力源固有の options を JSON オブジェクトで記述。空文字なら未指定。")]
        [TextArea(2, 8)]
        public string optionsJson;
    }
}
