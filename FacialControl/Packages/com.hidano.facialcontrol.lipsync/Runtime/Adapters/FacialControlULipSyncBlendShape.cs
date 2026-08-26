using System;
using System.Collections.Generic;
using uLipSync;

namespace Hidano.FacialControl.LipSync.Adapters
{
    /// <summary>
    /// uLipSync 公式 <see cref="uLipSyncBlendShape"/> を <see cref="UpdateMethod.LipSyncUpdateEvent"/> で駆動し、
    /// 音量正規化・SmoothDamp・sum=1 正規化を uLipSync に完全委譲した結果
    /// （音素別ウェイトと音量）を非破壊で公開する算出器。
    /// </summary>
    /// <remarks>
    /// LipSyncUpdateEvent モードでは <c>OnLipSyncUpdate(info)</c> 受信時に <c>UpdateLipSync()</c>
    /// （UpdateVolume/UpdateVowels）が同期実行される。uLipSync の <c>onLipSyncUpdate</c> は実測で
    /// 毎フレーム発火する（解析 Job が 1 フレームで完了するため ratio≈1.0）ので、素の LateUpdate と
    /// 平滑化頻度は同等であり、テストの同期実行が可能な本モードを採用する。
    /// <see cref="OnApplyBlendShapes"/> は no-op 化しているため SkinnedMeshRenderer 直書きは発生せず、
    /// <see cref="OnApplyBlendShapes"/> は no-op 化しているため SkinnedMeshRenderer 直書きは発生せず、
    /// 本クラスは SkinnedMeshRenderer を持たない。算出した値は FacialControl のレイヤー合成系へ
    /// <see cref="IPhonemeWeightSource"/> 経由で渡す。
    /// minVolume / maxVolume / smoothness / usePhonemeBlend は uLipSync 既定値のまま据え置き、
    /// FacialControl 側では一切加工しない。
    /// </remarks>
    public sealed class FacialControlULipSyncBlendShape : uLipSyncBlendShape, IPhonemeWeightSource
    {
        private readonly Dictionary<string, BlendShapeInfo> _phonemeToInfo =
            new Dictionary<string, BlendShapeInfo>(StringComparer.Ordinal);

        /// <inheritdoc />
        public float CurrentVolume => volume;

        /// <summary>
        /// 音素 id 群を算出専用エントリとして登録し、LipSyncUpdateEvent モードに設定する。
        /// </summary>
        /// <remarks>
        /// 各エントリは <c>index = -1</c>（SkinnedMeshRenderer 非依存）で、
        /// <see cref="UpdateMethod.LipSyncUpdateEvent"/> 下では <c>OnLipSyncUpdate</c> 受信時の
        /// UpdateVowels によって <c>weight</c> が同期算出される（発火は実測で毎フレーム）。
        /// min/max/smoothness/usePhonemeBlend は変更しない。
        /// </remarks>
        public void ConfigurePhonemes(IReadOnlyList<string> phonemeIds)
        {
            updateMethod = UpdateMethod.LipSyncUpdateEvent;
            blendShapes.Clear();
            _phonemeToInfo.Clear();
            if (phonemeIds == null)
            {
                return;
            }

            for (int i = 0; i < phonemeIds.Count; i++)
            {
                string phoneme = phonemeIds[i];
                if (string.IsNullOrEmpty(phoneme) || _phonemeToInfo.ContainsKey(phoneme))
                {
                    continue;
                }

                var info = new BlendShapeInfo { phoneme = phoneme, index = -1, maxWeight = 1f };
                blendShapes.Add(info);
                _phonemeToInfo[phoneme] = info;
            }
        }

        /// <inheritdoc />
        public bool TryGetPhonemeWeight(string phonemeId, out float weight)
        {
            if (phonemeId != null && _phonemeToInfo.TryGetValue(phonemeId, out BlendShapeInfo info))
            {
                weight = info.weight;
                return true;
            }

            weight = 0f;
            return false;
        }

        // SkinnedMeshRenderer への直接書き込みを抑止する。FacialControl はレイヤー合成系から
        // BlendShape を書くため、uLipSync 側の直書き経路は no-op 化して二重適用を防ぐ。
        protected override void OnApplyBlendShapes()
        {
        }
    }
}
