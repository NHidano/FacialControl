using System;

namespace Hidano.FacialControl.Adapters.Input
{
    /// <summary>
    /// <see cref="InputDeviceCategorizer"/> が返す入力デバイス分類。
    /// </summary>
    /// <remarks>
    /// Keyboard 以外（Gamepad / Joystick / XRController / Pen / Touchscreen など）は
    /// 設計上 Controller 扱いで一括する。
    /// </remarks>
    public enum DeviceCategory
    {
        /// <summary>キーボード入力。</summary>
        Keyboard = 0,

        /// <summary>コントローラ系入力（Gamepad / Joystick / XR / Pen / Touchscreen）。</summary>
        Controller = 1,
    }

    /// <summary>
    /// <see cref="UnityEngine.InputSystem.InputBinding.path"/> 文字列から
    /// <see cref="DeviceCategory"/> を 0-alloc で推定するユーティリティ。
    /// </summary>
    /// <remarks>
    /// 判別は path 先頭の <c>&lt;DeviceLayout&gt;</c> プレフィクスで行う:
    /// <list type="bullet">
    ///   <item><c>&lt;Keyboard&gt;</c> → <see cref="DeviceCategory.Keyboard"/></item>
    ///   <item><c>&lt;Gamepad&gt;</c> / <c>&lt;Joystick&gt;</c> / <c>&lt;XRController&gt;</c>
    ///   / <c>&lt;Pen&gt;</c> / <c>&lt;Touchscreen&gt;</c> → <see cref="DeviceCategory.Controller"/></item>
    ///   <item>未認識 prefix / <c>null</c> / 空文字 → <see cref="DeviceCategory.Controller"/> + <c>wasFallback=true</c>
    ///   （呼出側で <c>Debug.LogWarning</c> を 1 回だけ出す責務）</item>
    /// </list>
    /// 0-alloc: <see cref="string.StartsWith(string, StringComparison)"/> を
    /// <see cref="StringComparison.Ordinal"/> で呼ぶことでヒープ確保を発生させない。
    /// </remarks>
    public static class InputDeviceCategorizer
    {
        /// <summary>Keyboard 判別用の path プレフィクス。</summary>
        private const string KeyboardPrefix = "<Keyboard>";

        /// <summary>Controller 系として認識する path プレフィクス一覧。</summary>
        /// <remarks>
        /// 配列再確保を避けるため <see cref="System.Array"/> ではなく
        /// <c>static readonly</c> 配列で保持し、<see cref="Categorize"/> 中の foreach は
        /// 配列インデクシング（IL: <c>ldelem.ref</c>）に展開され alloc しない。
        /// </remarks>
        private static readonly string[] ControllerPrefixes =
        {
            "<Gamepad>",
            "<Joystick>",
            // XInputController は Windows で XInput 互換コントローラ (Xbox 系) を表す
            // 具象 layout で、Gamepad の派生扱いだが path 先頭は <XInputController> となる。
            "<XInputController>",
            // DualShockGamepad / DualSenseGamepad / SwitchProControllerHID 等の具象も
            // 設計上 Gamepad の派生で Controller 扱いにしたい。
            "<DualShockGamepad>",
            "<DualSenseGamepadHID>",
            "<SwitchProControllerHID>",
            "<XRController>",
            "<Pen>",
            "<Touchscreen>",
        };

        /// <summary>
        /// 指定の <see cref="UnityEngine.InputSystem.InputBinding.path"/> を解析し、
        /// 対応する <see cref="DeviceCategory"/> を返す。
        /// </summary>
        /// <param name="bindingPath">
        /// 例: <c>&lt;Keyboard&gt;/space</c>, <c>&lt;Gamepad&gt;/buttonSouth</c>。
        /// <c>null</c> または空文字の場合は <c>wasFallback=true</c> + Controller を返す。
        /// </param>
        /// <param name="wasFallback">
        /// 未認識 prefix（または <c>null</c>/空文字）で fallback 分類した場合 <c>true</c>。
        /// 呼出側はこれを見て warning を 1 回だけログ出力する。
        /// </param>
        /// <returns>推定した <see cref="DeviceCategory"/>。</returns>
        public static DeviceCategory Categorize(string bindingPath, out bool wasFallback)
        {
            if (string.IsNullOrEmpty(bindingPath))
            {
                wasFallback = true;
                return DeviceCategory.Controller;
            }

            if (bindingPath.StartsWith(KeyboardPrefix, StringComparison.Ordinal))
            {
                wasFallback = false;
                return DeviceCategory.Keyboard;
            }

            for (int i = 0; i < ControllerPrefixes.Length; i++)
            {
                if (bindingPath.StartsWith(ControllerPrefixes[i], StringComparison.Ordinal))
                {
                    wasFallback = false;
                    return DeviceCategory.Controller;
                }
            }

            wasFallback = true;
            return DeviceCategory.Controller;
        }
    }
}
