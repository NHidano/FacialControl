using System;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.ScriptableObject.Serializable
{
    /// <summary>
    /// アナログ入力バインディング 1 件 (analog binding profile: bindings[]) の Inspector シリアライズ表現。
    /// 入力源 (InputAction) → BlendShape / BonePose 軸への 1 対 1 写像を宣言する
    /// （<see cref="Hidano.FacialControl.Domain.Models.AnalogBindingEntry"/> の Adapters 側プロジェクション）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 3 フィールド構成:
    /// <see cref="inputActionRef"/> + <see cref="targetIdentifier"/> + <see cref="targetAxis"/>。
    /// </para>
    /// <para>
    /// 値変換 (deadzone / scale / offset / curve / invert / clamp) は InputActionAsset 側
    /// processor チェーンで完結する。Adapters 側 InputProcessor は登録済み。
    /// </para>
    /// <para>
    /// 本型は core パッケージ (com.hidano.facialcontrol) に所属する asmdef 制約上
    /// <c>UnityEngine.InputSystem.InputActionReference</c> を直接 SerializeField に持てないため、
    /// <see cref="inputActionRef"/> は InputAction の <c>id</c> (GUID 文字列) を保持する。
    /// 実体解決は inputsystem パッケージ側で <c>InputActionAsset.FindAction(id)</c> で行う。
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class AnalogBindingEntrySerializable
    {
        [Tooltip("入力源 InputAction の参照識別子。InputAction.id の GUID 文字列または action 名を保持する。")]
        public string inputActionRef;

        [Tooltip("ターゲット識別子。BlendShape ターゲットなら BlendShape 名、BonePose ターゲットならボーン名。")]
        public string targetIdentifier;

        [Tooltip("BonePose ターゲット時の Euler 軸 (X/Y/Z)。BlendShape ターゲット時は無視される。")]
        public AnalogTargetAxis targetAxis = AnalogTargetAxis.X;
    }
}
