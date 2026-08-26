using UnityEngine;
using UnityEngine.InputSystem;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Hidano.FacialControl.Adapters.Processors
{
    /// <summary>
    /// 6 種の Analog <see cref="InputProcessor{TValue}"/>（DeadZone / Scale / Offset / Clamp /
    /// Curve / Invert）を Editor / Runtime 双方の初期化タイミングで一括登録する静的ハブ。
    /// </summary>
    /// <remarks>
    /// Editor では <c>[InitializeOnLoad]</c> 経由でドメインリロード直後に静的コンストラクタが走り、
    /// 6 processor を <see cref="global::UnityEngine.InputSystem.InputSystem.RegisterProcessor{T}(string)"/> で登録する。
    /// Runtime ビルドでは <see cref="RuntimeInitializeOnLoadMethodAttribute"/> の
    /// <see cref="RuntimeInitializeLoadType.BeforeSceneLoad"/> フェーズで同じ登録が走る。
    /// 登録名は <see cref="ProcessorNames"/> として公開し、Migration Guide や Inspector からも参照する。
    /// <see cref="global::UnityEngine.InputSystem.InputSystem.RegisterProcessor{T}(string)"/> は冪等のため、Editor PlayMode 中に
    /// 両経路から二重に呼ばれても害は無い。
    /// </remarks>
#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    public static class AnalogProcessorRegistration
    {
        /// <summary><see cref="AnalogDeadZoneProcessor"/> の登録名。</summary>
        public const string DeadZoneProcessorName = "analogDeadZone";

        /// <summary><see cref="AnalogScaleProcessor"/> の登録名。</summary>
        public const string ScaleProcessorName = "analogScale";

        /// <summary><see cref="AnalogOffsetProcessor"/> の登録名。</summary>
        public const string OffsetProcessorName = "analogOffset";

        /// <summary><see cref="AnalogClampProcessor"/> の登録名。</summary>
        public const string ClampProcessorName = "analogClamp";

        /// <summary><see cref="AnalogCurveProcessor"/> の登録名。</summary>
        public const string CurveProcessorName = "analogCurve";

        /// <summary><see cref="AnalogInvertProcessor"/> の登録名。</summary>
        public const string InvertProcessorName = "analogInvert";

        /// <summary>
        /// 登録対象 6 processor の名前一覧。Migration Guide / Inspector から参照する。
        /// </summary>
        public static readonly string[] ProcessorNames =
        {
            DeadZoneProcessorName,
            ScaleProcessorName,
            OffsetProcessorName,
            ClampProcessorName,
            CurveProcessorName,
            InvertProcessorName,
        };

        static AnalogProcessorRegistration()
        {
            Register();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterRuntime()
        {
            Register();
        }

        private static void Register()
        {
            global::UnityEngine.InputSystem.InputSystem.RegisterProcessor<AnalogDeadZoneProcessor>(DeadZoneProcessorName);
            global::UnityEngine.InputSystem.InputSystem.RegisterProcessor<AnalogScaleProcessor>(ScaleProcessorName);
            global::UnityEngine.InputSystem.InputSystem.RegisterProcessor<AnalogOffsetProcessor>(OffsetProcessorName);
            global::UnityEngine.InputSystem.InputSystem.RegisterProcessor<AnalogClampProcessor>(ClampProcessorName);
            global::UnityEngine.InputSystem.InputSystem.RegisterProcessor<AnalogCurveProcessor>(CurveProcessorName);
            global::UnityEngine.InputSystem.InputSystem.RegisterProcessor<AnalogInvertProcessor>(InvertProcessorName);
        }
    }
}
