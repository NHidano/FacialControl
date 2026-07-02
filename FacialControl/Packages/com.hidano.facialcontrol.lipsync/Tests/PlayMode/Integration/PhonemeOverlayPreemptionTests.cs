using System;
using System.Collections.Generic;
using System.Linq;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.LipSync.Adapters;
using Hidano.FacialControl.LipSync.Adapters.PhonemeEntries;
using Hidano.FacialControl.LipSync.Tests.Shared;
using NUnit.Framework;

namespace Hidano.FacialControl.LipSync.Tests.PlayMode.Integration
{
    /// <summary>
    /// Expression の phoneme Override / Suppress が、実経路
    /// (<see cref="LayerUseCase"/> + Aggregator の加重和) で
    /// <see cref="LipSyncPhonemeOverlayInputSource"/> の既定出力を preempt することを検証する。
    /// </summary>
    /// <remarks>
    /// <see cref="PhonemeOverlayIntegrationTests"/> はテストリグ内で優先順を手動実装しており
    /// 実経路の preemption を検証していなかった（design.md「overlay が立ったら
    /// lipsync-overlay の寄与は無視される」の regression テスト欠落）。本テストは
    /// 同一レイヤーに <c>overlay:a</c> と <c>lipsync-overlay:a</c> を実際に並べ、
    /// 最終ブレンド出力で precedence (Override → Suppress → DefaultOverlays → LipSync default)
    /// を固定する。
    /// </remarks>
    [TestFixture]
    public sealed class PhonemeOverlayPreemptionTests
    {
        private const float Tolerance = 0.0001f;
        private const string EmotionLayer = "emotion";
        private const string OverlayLayer = "overlay";
        private const string SlotA = "a";
        private const string PhonemeA = "A";
        private const string MouthA = "Mouth_A";
        private const string MouthI = "Mouth_I";
        private const float LipSyncDefaultValue = 0.85f;
        private const float OverrideValue = 0.6f;

        private static readonly string[] BlendShapeNames = { MouthA, MouthI };

        [Test]
        public void ActiveExpressionOverride_PreemptsLipSyncDefaultOutput()
        {
            FacialProfile profile = BuildProfile(
                smileOverlays: new[]
                {
                    new OverlaySlotBinding(SlotA, suppress: false, snapshot: OverrideSnapshot(OverrideValue)),
                });
            using Harness harness = CreateHarness(profile);
            harness.ActiveProvider.Active = profile.FindExpressionById("smile");

            harness.LayerUseCase.UpdateWeights(0f);

            // Override active 中は lipsync 既定出力 (0.85) が加算されず、override 値のみが出る。
            Assert.That(harness.LayerUseCase.BlendedOutputSpan[0], Is.EqualTo(OverrideValue).Within(Tolerance),
                "Override active 中は override snapshot の値だけが出力されるべき（lipsync 出力が加算されてはならない）。");
            Assert.That(harness.LayerUseCase.BlendedOutputSpan[1], Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void ActiveExpressionSuppress_BlocksLipSyncDefaultOutput()
        {
            FacialProfile profile = BuildProfile(
                smileOverlays: new[]
                {
                    new OverlaySlotBinding(SlotA, suppress: true, snapshot: null),
                });
            using Harness harness = CreateHarness(profile);
            harness.ActiveProvider.Active = profile.FindExpressionById("smile");

            harness.LayerUseCase.UpdateWeights(0f);

            Assert.That(harness.LayerUseCase.BlendedOutputSpan[0], Is.EqualTo(0f).Within(Tolerance),
                "Suppress active 中は当該 slot のリップシンク出力が完全に止まるべき。");
        }

        [Test]
        public void ActiveExpressionEmptySnapshotBinding_FallsBackToLipSyncDefault()
        {
            // Inspector 未設定のまま出力された「suppress=false + 空 snapshot」の binding は
            // default fallback として扱い、リップシンクを preempt しない（全滅事故の防止）。
            FacialProfile profile = BuildProfile(
                smileOverlays: new[]
                {
                    new OverlaySlotBinding(SlotA, suppress: false, snapshot: EmptySnapshot()),
                });
            using Harness harness = CreateHarness(profile);
            harness.ActiveProvider.Active = profile.FindExpressionById("smile");

            harness.LayerUseCase.UpdateWeights(0f);

            Assert.That(harness.LayerUseCase.BlendedOutputSpan[0], Is.EqualTo(LipSyncDefaultValue).Within(Tolerance),
                "空 snapshot の binding は default fallback 扱いで、lipsync 既定出力が出るべき。");
        }

        [Test]
        public void NoActiveExpression_DefaultOverlayOverride_PreemptsLipSyncDefaultOutput()
        {
            FacialProfile profile = BuildProfile(
                defaultOverlays: new[]
                {
                    new OverlaySlotBinding(SlotA, suppress: false, snapshot: OverrideSnapshot(OverrideValue)),
                });
            using Harness harness = CreateHarness(profile);
            harness.ActiveProvider.Active = null;

            harness.LayerUseCase.UpdateWeights(0f);

            Assert.That(harness.LayerUseCase.BlendedOutputSpan[0], Is.EqualTo(OverrideValue).Within(Tolerance),
                "DefaultOverlays の override は base 表情時も lipsync 既定出力を preempt するべき。");
        }

        [Test]
        public void NoOverlayBinding_LipSyncDefaultOutputIsPreserved()
        {
            FacialProfile profile = BuildProfile();
            using Harness harness = CreateHarness(profile);
            harness.ActiveProvider.Active = profile.FindExpressionById("smile");

            harness.LayerUseCase.UpdateWeights(0f);

            Assert.That(harness.LayerUseCase.BlendedOutputSpan[0], Is.EqualTo(LipSyncDefaultValue).Within(Tolerance),
                "overlay binding が無い場合は lipsync 既定出力がそのまま出るべき。");
        }

        private Harness CreateHarness(FacialProfile profile)
        {
            var activeProvider = new StubActiveExpressionProvider();
            var weightSource = new FakePhonemeWeightSource();
            weightSource.SetFrame(1f, (PhonemeA, 1f));

            var provider = new ULipSyncProvider(
                weightSource,
                new[] { new PhonemeSnapshot(PhonemeA, new[] { LipSyncDefaultValue, 0f }) },
                BlendShapeNames.Length);

            var overlaySource = new OverlayInputSource(
                InputSourceId.Parse($"{OverlayInputSource.ReservedIdPrefix}:{SlotA}"),
                SlotA,
                BlendShapeNames.Length,
                BlendShapeNames,
                profile,
                activeProvider,
                EmotionLayer);

            var lipSyncSource = new LipSyncPhonemeOverlayInputSource(
                InputSourceId.Parse($"{LipSyncPhonemeOverlayInputSource.SlugPrefix}:{SlotA}"),
                PhonemeA,
                provider,
                BlendShapeNames.Length,
                SlotA,
                profile,
                activeProvider,
                EmotionLayer);

            var expressionUseCase = new ExpressionUseCase(profile);
            var additional = new List<(int layerIdx, IInputSource source, float weight)>
            {
                (0, overlaySource, 1f),
                (0, lipSyncSource, 1f),
            };
            var layerUseCase = new LayerUseCase(profile, expressionUseCase, BlendShapeNames, additional);

            return new Harness(provider, layerUseCase, activeProvider);
        }

        private static FacialProfile BuildProfile(
            OverlaySlotBinding[] smileOverlays = null,
            OverlaySlotBinding[] defaultOverlays = null)
        {
            var smile = new Expression(
                id: "smile",
                name: "Smile",
                layer: EmotionLayer,
                transitionDuration: Expression.DefaultTransitionDuration,
                transitionCurve: default,
                blendShapeValues: null,
                overlays: smileOverlays);

            return new FacialProfile(
                "1.0",
                new[] { new LayerDefinition(OverlayLayer, 0, ExclusionMode.Blend) },
                expressions: new[] { smile },
                rendererPaths: null,
                layerInputSources: new[]
                {
                    new[]
                    {
                        new InputSourceDeclaration($"{OverlayInputSource.ReservedIdPrefix}:{SlotA}", 1f, null),
                        new InputSourceDeclaration($"{LipSyncPhonemeOverlayInputSource.SlugPrefix}:{SlotA}", 1f, null),
                    },
                },
                defaultOverlays: defaultOverlays,
                slots: PhonemeOverlaySlots.ReservedNames.ToArray());
        }

        private static ExpressionSnapshot OverrideSnapshot(float value)
        {
            return new ExpressionSnapshot(
                id: "override-a",
                transitionDuration: Expression.DefaultTransitionDuration,
                transitionCurvePreset: TransitionCurvePreset.Linear,
                blendShapes: new[] { new BlendShapeSnapshot(string.Empty, MouthA, value) },
                bones: null,
                rendererPaths: null);
        }

        private static ExpressionSnapshot EmptySnapshot()
        {
            return new ExpressionSnapshot(
                id: "empty-a",
                transitionDuration: Expression.DefaultTransitionDuration,
                transitionCurvePreset: TransitionCurvePreset.Linear,
                blendShapes: null,
                bones: null,
                rendererPaths: null);
        }

        private sealed class Harness : IDisposable
        {
            public Harness(
                ULipSyncProvider provider,
                LayerUseCase layerUseCase,
                StubActiveExpressionProvider activeProvider)
            {
                Provider = provider;
                LayerUseCase = layerUseCase;
                ActiveProvider = activeProvider;
            }

            public ULipSyncProvider Provider { get; }

            public LayerUseCase LayerUseCase { get; }

            public StubActiveExpressionProvider ActiveProvider { get; }

            public void Dispose()
            {
                LayerUseCase.Dispose();
                Provider.Dispose();
            }
        }

        private sealed class StubActiveExpressionProvider : IActiveExpressionProvider
        {
            public Expression? Active;

            public Expression? TryGetTopActiveExpression(string layerName)
            {
                return Active;
            }
        }
    }
}
