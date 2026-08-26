using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.ScriptableObject
{
    /// <summary>
    /// Vector2 アナログ入力 (左スティック・OSC・ARKit 等) で両目を同時駆動する目線操作表情の設定。
    /// 入力源には依存しない汎用データ構造で、対応する <see cref="Serializable.ExpressionSerializable"/>
    /// (isGaze=true) に対して <see cref="expressionId"/> で紐づく。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 入力 Vector2 の x 成分が左右方向、y 成分が上下方向の目線回転を駆動する。
    /// 多くのモデルでは目線はボーン操作だが、BlendShape ベースのモデルもあるため両方を任意に併用できる。
    /// </para>
    /// <para>
    /// ボーン制御では、参照モデルから取得した時点の各目ボーン姿勢でグローバル X 軸 (上下) /
    /// Y 軸 (左右) に対応する local 方向ベクトルを算出して保存する。これによりボーンが
    /// どの向きを向いていても、入力に対して常に「世界の上下/左右」を基準とした自然な目線回転を実現できる。
    /// </para>
    /// <para>
    /// 可動範囲は (1) 上下方向 (両目共通)、(2) 左右方向の左右目 × 内側/外側 で別個に指定する。
    /// 例えば「向かって左側に視線を送る」とき、向かって左の眼 (キャラから見て右目) は外側、
    /// 向かって右の眼 (キャラから見て左目) は内側の制限値で動作させることで、左右の眼が
    /// 完全同角度で動くことによる違和感を回避する。
    /// </para>
    /// <para>
    /// BlendShape 経路は 4 系統 (LookLeft / LookRight / LookUp / LookDown) の AnimationClip で指定する。
    /// Vector2 の +X / -X / +Y / -Y がそれぞれ LookRight / LookLeft / LookUp / LookDown clip に対応し、
    /// clip 内の各 BlendShape curve の time=0 における値を keyframe weight として線形駆動する。
    /// </para>
    /// <para>
    /// 入力源との結線は、本クラスを派生させず sidecar の結線型で扱う。
    /// InputSystem 経路では <c>ExpressionBindingEntry</c>（bindingMode = Gaze）が expressionId と
    /// Action 名のみを保持する。
    /// </para>
    /// </remarks>
    [Serializable]
    public class GazeBindingConfig
    {
        [Tooltip("対応する Expression の ID。Expressions リスト内に id 一致するエントリが存在する必要がある。")]
        public string expressionId;

        public bool useDistinctLeftRight;

        public string sourceIdLeft = string.Empty;

        public string sourceIdRight = string.Empty;

        // ----------------- ボーン制御 (主) -----------------

        [Tooltip("左目ボーンの Transform 名または参照モデル相対 path。"
            + " 単純名 (\"LeftEye\") を指定した場合は階層全体を再帰探索し、複数候補があれば警告する。"
            + " 同名ボーン衝突を確実に回避したい場合は相対 path (\"Hips/Spine/Head/LeftEye\") を指定する。"
            + " 空なら無効。")]
        public string leftEyeBonePath;

        [Tooltip("左目ボーンの初期回転 (Euler 度)。アナログ入力 0 のときに保つ姿勢。アナログ入力はこの姿勢に加算される。")]
        public Vector3 leftEyeInitialRotation;

        [Tooltip("左目ボーンの local 空間における「世界の Y 軸 (上方向)」相当ベクトル。"
            + " 左右 (yaw) 回転に使われる。「参照モデルから自動設定」ボタンで初期姿勢から自動算出される。")]
        public Vector3 leftEyeYawAxisLocal = Vector3.up;

        [Tooltip("左目ボーンの local 空間における「世界の X 軸 (右方向)」相当ベクトル。"
            + " 上下 (pitch) 回転に使われる。「参照モデルから自動設定」ボタンで初期姿勢から自動算出される。")]
        public Vector3 leftEyePitchAxisLocal = Vector3.right;

        [Tooltip("右目ボーンの Transform 名または参照モデル相対 path。"
            + " leftEyeBonePath と同じ規約 (単純名は階層全体探索 / 重複時警告、"
            + " path はスラッシュ区切りで一意指定)。空なら無効。")]
        public string rightEyeBonePath;

        [Tooltip("右目ボーンの初期回転 (Euler 度)。")]
        public Vector3 rightEyeInitialRotation;

        [Tooltip("右目ボーンの local 空間における「世界の Y 軸 (上方向)」相当ベクトル。")]
        public Vector3 rightEyeYawAxisLocal = Vector3.up;

        [Tooltip("右目ボーンの local 空間における「世界の X 軸 (右方向)」相当ベクトル。")]
        public Vector3 rightEyePitchAxisLocal = Vector3.right;

        // ----------------- 可動範囲 (角度制限) -----------------

        [Tooltip("上方向 (input.y > 0) の最大回転角度 (度、両目共通)。0〜90 推奨。")]
        [Range(0f, 90f)]
        public float lookUpAngle = 15f;

        [Tooltip("下方向 (input.y < 0) の最大回転角度 (度、両目共通、絶対値で指定)。0〜90 推奨。")]
        [Range(0f, 90f)]
        public float lookDownAngle = 9f;

        [Tooltip("左右方向、外側 (鼻から離れる側) の最大回転角度 (度、絶対値で指定)。0〜90 推奨。")]
        [Range(0f, 90f)]
        public float outerYawAngle = 15f;

        [Tooltip("左右方向、内側 (鼻に近づく側) の最大回転角度 (度、絶対値で指定)。"
            + " 通常は外側より小さめに設定する。0〜90 推奨。")]
        [Range(0f, 90f)]
        public float innerYawAngle = 18f;

        // ----------------- BlendShape 制御 (オプション、4 系統 clip) -----------------

        [Tooltip("input.x < 0 (左方向) の状態を表現する AnimationClip。clip 内 BlendShape curve の time=0 における値を |input.x| で線形駆動する。空なら無効。")]
        public AnimationClip lookLeftClip;

        [Tooltip("input.x > 0 (右方向) の状態を表現する AnimationClip。空なら無効。")]
        public AnimationClip lookRightClip;

        [Tooltip("input.y > 0 (上方向) の状態を表現する AnimationClip。空なら無効。")]
        public AnimationClip lookUpClip;

        [Tooltip("input.y < 0 (下方向) の状態を表現する AnimationClip。空なら無効。")]
        public AnimationClip lookDownClip;

        // ----------------- Editor で焼き付けた BlendShape weight キャッシュ -----------------
        // AnimationClip の curve は runtime API で列挙できないため、Editor (AutoExporter) で
        // GazeClipBlendShapeSampler を介してサンプルし、本配列に永続化する。
        // runtime の BuildAnalogProfile は本キャッシュから AnalogBindingEntry を構築する。

        [HideInInspector]
        public List<GazeBlendShapeSampleEntry> lookLeftSamples = new List<GazeBlendShapeSampleEntry>();

        [HideInInspector]
        public List<GazeBlendShapeSampleEntry> lookRightSamples = new List<GazeBlendShapeSampleEntry>();

        [HideInInspector]
        public List<GazeBlendShapeSampleEntry> lookUpSamples = new List<GazeBlendShapeSampleEntry>();

        [HideInInspector]
        public List<GazeBlendShapeSampleEntry> lookDownSamples = new List<GazeBlendShapeSampleEntry>();
    }
}
