using System;
using System.Collections;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.LipSync.Adapters
{
    public sealed class LipSyncPhonemeOverlayInputSource : ValueProviderInputSourceBase
    {
        public const string SlugPrefix = "lipsync-overlay";

        // provider が出す合成値（uLipSync 委譲後の volume × phoneme weight × snapshot）の総和が
        // この閾値を下回るフレームはレイヤー合成に乗せない。無音時に下位レイヤーを上書きしない契約。
        public const float SilenceThreshold = 1e-4f;

        private readonly string _phonemeId;
        private readonly ULipSyncProvider _provider;
        private readonly BitArray _contributeMask;
        private readonly float[] _scratch;
        private readonly bool _phonemeRegistered;

        // ---- Override / Suppress preemption 解決コンテキスト ----
        // Expression / DefaultOverlays の phoneme slot binding が有効な間、
        // 本 source は無効（TryWriteValues=false）となり lipsync 既定出力を止める。
        // precedence: Expression Override → Suppress → DefaultOverlays → LipSync default
        // （phoneme-overlay-slots design.md / phoneme-overlay-migration.md §5）。
        // null の場合は preemption なし（既定出力のみの従来動作）。
        private readonly string _slot;
        private readonly FacialProfile? _resolutionProfile;
        private readonly IActiveExpressionProvider _activeProvider;
        private readonly string _emotionLayerName;

        public LipSyncPhonemeOverlayInputSource(
            InputSourceId id,
            string phonemeId,
            ULipSyncProvider provider,
            int blendShapeCount)
            : this(id, phonemeId, provider, blendShapeCount, null, null, null, null)
        {
        }

        public LipSyncPhonemeOverlayInputSource(
            InputSourceId id,
            string phonemeId,
            ULipSyncProvider provider,
            int blendShapeCount,
            string slot,
            FacialProfile? resolutionProfile,
            IActiveExpressionProvider activeProvider,
            string emotionLayerName)
            : base(id, blendShapeCount)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            _phonemeId = phonemeId;
            _provider = provider;
            _scratch = new float[blendShapeCount];
            _phonemeRegistered = provider.TryGetPhonemeIndex(phonemeId, out _);
            _contributeMask = _phonemeRegistered
                ? ResolveContributeMask(provider, phonemeId, blendShapeCount)
                : new BitArray(blendShapeCount, false);
            _slot = slot;
            _resolutionProfile = resolutionProfile;
            _activeProvider = activeProvider;
            _emotionLayerName = string.IsNullOrEmpty(emotionLayerName) ? "emotion" : emotionLayerName;
        }

        public override BitArray ContributeMask => _contributeMask;

        public override bool TryWriteValues(Span<float> output)
        {
            if (!_phonemeRegistered)
            {
                return false;
            }

            if (IsPreemptedByOverlayResolution())
            {
                return false;
            }

            Span<float> scratch = _scratch;
            scratch.Clear();
            if (!_provider.TryComposePhonemeWeights(_phonemeId, scratch))
            {
                return false;
            }

            float sum = 0f;
            for (int i = 0; i < scratch.Length; i++)
            {
                sum += scratch[i];
            }

            if (sum < SilenceThreshold)
            {
                return false;
            }

            int copyLength = output.Length < scratch.Length ? output.Length : scratch.Length;
            for (int i = 0; i < copyLength; i++)
            {
                output[i] = scratch[i];
            }

            return true;
        }

        /// <summary>
        /// 自 slot の Expression Override / Suppress / DefaultOverlays が有効なら true。
        /// true の間は本 source が無効となり、同一レイヤーの <c>overlay:{slot}</c>
        /// (OverlayInputSource) 側の出力だけが残る（加重和での二重寄与を防ぐ）。
        /// </summary>
        /// <remarks>
        /// 判定は <see cref="OverlayInputSource"/> の解決順と同じ
        /// 「active 表情の binding → DefaultOverlays」。suppress=false かつ BlendShape を
        /// 1 つも持たない空 snapshot は「Inspector 未設定のまま出力された binding」であり
        /// default fallback として扱う（preempt しない）。毎フレーム呼ばれるが
        /// struct 取得のみで GC アロケーションは発生しない。
        /// </remarks>
        private bool IsPreemptedByOverlayResolution()
        {
            if (!_resolutionProfile.HasValue || string.IsNullOrEmpty(_slot))
            {
                return false;
            }

            if (_activeProvider != null)
            {
                Expression? active = _activeProvider.TryGetTopActiveExpression(_emotionLayerName);
                if (active.HasValue && active.Value.TryGetOverlay(_slot, out OverlaySlotBinding activeBinding))
                {
                    if (activeBinding.Suppress)
                    {
                        return true;
                    }

                    if (HasEffectiveSnapshot(activeBinding))
                    {
                        return true;
                    }

                    // default fallback（snapshot 無し / 空 snapshot）は DefaultOverlays の判定へ続く。
                }
            }

            if (_resolutionProfile.Value.TryGetDefaultOverlay(_slot, out OverlaySlotBinding defaultBinding))
            {
                if (defaultBinding.Suppress)
                {
                    return true;
                }

                if (HasEffectiveSnapshot(defaultBinding))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasEffectiveSnapshot(in OverlaySlotBinding binding)
        {
            return binding.Snapshot.HasValue
                && binding.Snapshot.Value.BlendShapes.Length > 0;
        }

        private static BitArray ResolveContributeMask(
            ULipSyncProvider provider,
            string phonemeId,
            int blendShapeCount)
        {
            BitArray mask = provider.GetPhonemeContributeMask(phonemeId);
            if (mask == null)
            {
                return new BitArray(blendShapeCount, false);
            }

            if (mask.Length != blendShapeCount)
            {
                throw new ArgumentException(
                    "provider phoneme ContributeMask.Length must match blendShapeCount.",
                    nameof(provider));
            }

            return mask;
        }
    }
}
