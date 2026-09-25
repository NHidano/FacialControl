using System;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.IFacialMocap
{
    /// <summary>
    /// iFacialMocap の目 Transform（オイラー角・度）を、<c>GazeVector2InputSource</c> が期待する
    /// 正規化 Vector2（X=ヨー[-1,1], Y=ピッチ[-1,1]、+X=右 / +Y=上）へ変換する設定付き struct。
    /// </summary>
    /// <remarks>
    /// iFacialMocap の eye euler は概ね X=ピッチ(上下)、Y=ヨー(左右)、Z=ロール。本 struct は可動レンジの
    /// 正規化のみを担い、向き(符号)の反転は<b>アバター固有</b>のためここでは扱わない
    /// （<c>IFacialMocapReceiverAdapterBinding</c> の Gaze Invert Yaw/Pitch で調整する）。
    /// </remarks>
    [Serializable]
    public struct EyeGazeConverter
    {
        [Tooltip("ヨー(左右)を [-1,1] に正規化する際の最大角(度)。0 でヨー出力を無効化。")]
        public float maxYawDegrees;

        [Tooltip("ピッチ(上下)を [-1,1] に正規化する際の最大角(度)。0 でピッチ出力を無効化。")]
        public float maxPitchDegrees;

        /// <summary>既定値（ヨー±30°, ピッチ±25°）。符号(向き)は binding 側 (アバター固有) で反転する。</summary>
        public static EyeGazeConverter Default => new EyeGazeConverter
        {
            maxYawDegrees = 30f,
            maxPitchDegrees = 25f,
        };

        /// <summary>
        /// 目 Transform を正規化 Vector2 に変換する。<paramref name="eye"/> が無効なら <see cref="Vector2.zero"/>。
        /// </summary>
        public Vector2 Convert(in IFacialMocapTransformSample eye)
        {
            if (!eye.HasValue)
            {
                return Vector2.zero;
            }

            float yaw = eye.EulerY;
            float pitch = eye.EulerX;

            float x = maxYawDegrees > 0f ? Mathf.Clamp(yaw / maxYawDegrees, -1f, 1f) : 0f;
            float y = maxPitchDegrees > 0f ? Mathf.Clamp(pitch / maxPitchDegrees, -1f, 1f) : 0f;

            return new Vector2(x, y);
        }
    }
}
