using System;

namespace Hidano.FacialControl.Adapters.IFacialMocap
{
    /// <summary>
    /// iFacialMocap が送出する ARKit 互換 52 BlendShape の名称カタログと、
    /// iFacialMocap 命名（<c>_L</c>/<c>_R</c> サフィックス）→ ARKit 正準名（<c>Left</c>/<c>Right</c>）の
    /// 既定変換を提供する。
    /// </summary>
    /// <remarks>
    /// iFacialMocap の名称は ARKit 正準名の左右接尾辞を <c>_L</c>/<c>_R</c> に置換しただけなので、
    /// 変換は決定的（<see cref="ToArKitName"/>）。左右を持たない名称（<c>jawOpen</c>, <c>mouthLeft</c> 等）は
    /// そのまま通す。最終的なメッシュ BlendShape 名へのマッピングは binding 側で上書き可能。
    /// </remarks>
    public static class IFacialMocapBlendShapeCatalog
    {
        private const string LeftSuffix = "_L";
        private const string RightSuffix = "_R";
        private const string ArKitLeftSuffix = "Left";
        private const string ArKitRightSuffix = "Right";

        /// <summary>iFacialMocap が送出する 52 BlendShape 名（送出順とは無関係の正準集合）。</summary>
        public static readonly string[] Names =
        {
            // Brows
            "browDown_L", "browDown_R", "browInnerUp", "browOuterUp_L", "browOuterUp_R",
            // Cheeks
            "cheekPuff", "cheekSquint_L", "cheekSquint_R",
            // Eyes
            "eyeBlink_L", "eyeBlink_R",
            "eyeLookDown_L", "eyeLookDown_R", "eyeLookIn_L", "eyeLookIn_R",
            "eyeLookOut_L", "eyeLookOut_R", "eyeLookUp_L", "eyeLookUp_R",
            "eyeSquint_L", "eyeSquint_R", "eyeWide_L", "eyeWide_R",
            // Jaw
            "jawForward", "jawLeft", "jawOpen", "jawRight",
            // Mouth
            "mouthClose", "mouthDimple_L", "mouthDimple_R", "mouthFrown_L", "mouthFrown_R",
            "mouthFunnel", "mouthLeft", "mouthLowerDown_L", "mouthLowerDown_R",
            "mouthPress_L", "mouthPress_R", "mouthPucker", "mouthRight",
            "mouthRollLower", "mouthRollUpper", "mouthShrugLower", "mouthShrugUpper",
            "mouthSmile_L", "mouthSmile_R", "mouthStretch_L", "mouthStretch_R",
            "mouthUpperUp_L", "mouthUpperUp_R",
            // Nose
            "noseSneer_L", "noseSneer_R",
            // Tongue
            "tongueOut",
        };

        /// <summary>カタログ件数（52）。</summary>
        public static int Count => Names.Length;

        /// <summary>
        /// iFacialMocap 名を ARKit 正準名へ変換する。<c>_L</c>→<c>Left</c>、<c>_R</c>→<c>Right</c>、
        /// それ以外はそのまま返す。
        /// </summary>
        public static string ToArKitName(string ifacialMocapName)
        {
            if (string.IsNullOrEmpty(ifacialMocapName))
            {
                return ifacialMocapName;
            }

            if (ifacialMocapName.EndsWith(LeftSuffix, StringComparison.Ordinal))
            {
                return ifacialMocapName.Substring(0, ifacialMocapName.Length - LeftSuffix.Length) + ArKitLeftSuffix;
            }

            if (ifacialMocapName.EndsWith(RightSuffix, StringComparison.Ordinal))
            {
                return ifacialMocapName.Substring(0, ifacialMocapName.Length - RightSuffix.Length) + ArKitRightSuffix;
            }

            return ifacialMocapName;
        }
    }
}
