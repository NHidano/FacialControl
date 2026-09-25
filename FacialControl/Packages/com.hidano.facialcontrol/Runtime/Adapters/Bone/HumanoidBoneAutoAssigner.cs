using UnityEngine;

namespace Hidano.FacialControl.Adapters.Bone
{
    /// <summary>
    /// Humanoid Animator から <see cref="UnityEngine.HumanBodyBones"/> 経由で
    /// 目・頭・首ボーンの名前を取得するヘルパー 。
    /// </summary>
    /// <remarks>
    /// オプトイン (Editor のボタン押下 / ランタイム明示呼出のみ)、Adapters/Bone 配下に配置する。
    /// 非 Humanoid / 未マップ / null Animator は <see cref="Debug.LogWarning"/> + empty 返却 (throw しない)。
    /// </remarks>
    public static class HumanoidBoneAutoAssigner
    {
        /// <summary>
        /// 左右目の bone 名のペア。未解決時は <see cref="string.Empty"/>。
        /// </summary>
        public readonly struct EyeBoneNames
        {
            public string LeftEye { get; }
            public string RightEye { get; }

            public EyeBoneNames(string leftEye, string rightEye)
            {
                LeftEye = leftEye;
                RightEye = rightEye;
            }
        }

        /// <summary>
        /// Humanoid Animator から LeftEye / RightEye の Transform 名を解決する 。
        /// </summary>
        public static EyeBoneNames ResolveEyeBoneNames(Animator animator)
        {
            if (animator == null)
            {
                Debug.LogWarning("[HumanoidBoneAutoAssigner] Animator が null のため Eye bone 名を解決できませんでした。");
                return default;
            }

            if (!IsHumanoid(animator))
            {
                Debug.LogWarning("[HumanoidBoneAutoAssigner] Animator が Humanoid ではないため Eye bone 名を解決できませんでした (Avatar が null または非 Humanoid)。");
                return default;
            }

            var leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            var rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);

            if (leftEye == null && rightEye == null)
            {
                Debug.LogWarning("[HumanoidBoneAutoAssigner] Avatar に LeftEye / RightEye がマップされていません (Eye bone 名は empty)。");
                return default;
            }

            if (leftEye == null || rightEye == null)
            {
                Debug.LogWarning("[HumanoidBoneAutoAssigner] Avatar の Eye マップが片側のみのため、不足側は empty を返します。");
            }

            string left = leftEye != null ? leftEye.name : string.Empty;
            string right = rightEye != null ? rightEye.name : string.Empty;
            return new EyeBoneNames(left, right);
        }

        /// <summary>
        /// 顔相対回転の basis として用いる bone 名を解決する 。
        /// Head を優先し、Head 不在かつ <paramref name="useNeckFallback"/> が true のとき Neck を返す。
        /// </summary>
        public static string ResolveBasisBoneName(Animator animator, bool useNeckFallback = true)
        {
            if (animator == null)
            {
                Debug.LogWarning("[HumanoidBoneAutoAssigner] Animator が null のため basis bone 名を解決できませんでした。");
                return string.Empty;
            }

            if (!IsHumanoid(animator))
            {
                Debug.LogWarning("[HumanoidBoneAutoAssigner] Animator が Humanoid ではないため basis bone 名を解決できませんでした (Avatar が null または非 Humanoid)。");
                return string.Empty;
            }

            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null)
            {
                return head.name;
            }

            if (useNeckFallback)
            {
                var neck = animator.GetBoneTransform(HumanBodyBones.Neck);
                if (neck != null)
                {
                    return neck.name;
                }

                Debug.LogWarning("[HumanoidBoneAutoAssigner] Avatar に Head / Neck のいずれもマップされていません (basis bone 名は empty)。");
                return string.Empty;
            }

            Debug.LogWarning("[HumanoidBoneAutoAssigner] Avatar に Head がマップされておらず useNeckFallback=false のため basis bone 名は empty を返します。");
            return string.Empty;
        }

        private static bool IsHumanoid(Animator animator)
        {
            var avatar = animator.avatar;
            return avatar != null && avatar.isHuman;
        }
    }
}
