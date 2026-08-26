using UnityEngine;

namespace Hidano.FacialControl.Adapters.Json.Dto
{
    /// <summary>
    /// 中間 JSON Schema v2.0: <c>expressions[].snapshot.bones[]</c> の 1 エントリ。
    /// AnimationClip サンプリング由来の単一ボーン姿勢（Position / RotationEuler / Scale）を
    /// JSON へ運搬する DTO。JsonUtility 互換のため <see cref="System.SerializableAttribute"/> を付与する。
    /// <para>
    /// Domain 側の対応値型は <see cref="Hidano.FacialControl.Domain.Models.BoneSnapshot"/>。
    /// </para>
    /// <para>
    /// 補足: ベクトル値は JsonUtility 互換性のため <c>{"x":..,"y":..,"z":..}</c> オブジェクト形式
    /// （<see cref="Vector3"/> の既定シリアライズ形式）を採用する。
    /// </para>
    /// </summary>
    [System.Serializable]
    public sealed class BoneSnapshotDto
    {
        /// <summary>対象ボーンの Transform 階層パス（例: "Armature/Head"）。</summary>
        public string bonePath;

        /// <summary>X/Y/Z 軸ローカル位置。</summary>
        public Vector3 position;

        /// <summary>X/Y/Z 軸オイラー角（度）。</summary>
        public Vector3 rotationEuler;

        /// <summary>X/Y/Z 軸ローカルスケール。</summary>
        public Vector3 scale = Vector3.one;
    }
}
