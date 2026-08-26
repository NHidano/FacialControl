using System;
using System.Collections.Generic;
using System.IO;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Adapters.Json.Dto;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Sampling;
using UnityEngine;
using Debug = UnityEngine.Debug;
using GazeBindingConfig = Hidano.FacialControl.Adapters.ScriptableObject.GazeBindingConfig;

namespace Hidano.FacialControl.Editor.AutoExport
{
    /// <summary>
    /// <see cref="FacialCharacterProfileSO"/> 系アセットの汎用エクスポート処理。
    /// AnimationClip サンプリング → <see cref="ExpressionSerializable.cachedSnapshot"/> 反映、
    /// および <c>StreamingAssets/FacialControl/{SO 名}/profile.json</c> (schema v1.0) への
    /// 書き出しを行う。入力方式 (InputSystem / OSC / ARKit 等) には依存しない。
    /// </summary>
    /// <remarks>
    /// <para>
    /// InputSystem 連携固有の追加エクスポート (analog_bindings.json) や Gaze clip サンプリングは
    /// inputsystem パッケージ側のラッパで本クラスを呼出した上で重ねる。これにより
    /// <see cref="FacialCharacterProfileSO"/> 抽象基底だけを参照するインスペクタやツールから
    /// 入力方式を問わずに永続化を実行できる。
    /// </para>
    /// </remarks>
    public static class FacialCharacterProfileExporter
    {
        private const string BaseExpressionSnapshotId = "base-expression";

        /// <summary>
        /// 指定 SO 配下の各 Expression について、AnimationClip があれば sampler で時刻 0 サンプリングを行い、
        /// 結果を <see cref="ExpressionSerializable.cachedSnapshot"/> に書き戻す。
        /// AnimationClip が null の Expression はスキップする。
        /// </summary>
        /// <param name="so">対象 SO。null は no-op。</param>
        /// <param name="sampler">注入された sampler 実装。</param>
        public static void SampleAnimationClipsIntoCachedSnapshots(
            FacialCharacterProfileSO so,
            IExpressionAnimationClipSampler sampler)
        {
            if (so == null) return;
            if (sampler == null) throw new ArgumentNullException(nameof(sampler));

            var expressions = so.Expressions;
            if (expressions != null)
            {
                for (int i = 0; i < expressions.Count; i++)
                {
                    var expr = expressions[i];
                    if (expr == null)
                    {
                        continue;
                    }
                    if (expr.animationClip != null)
                    {
                        var snapshotId = string.IsNullOrEmpty(expr.id) ? string.Empty : expr.id;
                        var snapshot = sampler.SampleSnapshot(snapshotId, expr.animationClip);
                        var dto = ConvertSnapshotToDto(snapshot);
                    // Inspector スライダー (expr.transitionDuration) を cachedSnapshot 側にも反映し、
                    // 後続の JSON 出力経路と SO 直読み経路で値を一致させる。
                        dto.transitionDuration = expr.transitionDuration;
                        expr.cachedSnapshot = dto;
                    }

                    BakeOverlaySnapshots(expr.overlays, sampler);
                }
            }

            BakeOverlaySnapshots(so.DefaultOverlays, sampler);
            BakeBaseExpressionSnapshot(so.BaseExpression, sampler);
        }

        private static void BakeOverlaySnapshots(
            IReadOnlyList<OverlaySlotBindingSerializable> overlays,
            IExpressionAnimationClipSampler sampler)
        {
            if (overlays == null || overlays.Count == 0)
            {
                return;
            }

            for (int i = 0; i < overlays.Count; i++)
            {
                BakeOverlaySnapshot(overlays[i], sampler);
            }
        }

        private static void BakeOverlaySnapshot(
            OverlaySlotBindingSerializable overlay,
            IExpressionAnimationClipSampler sampler)
        {
            if (overlay == null)
            {
                return;
            }

            if (overlay.suppress || overlay.animationClip == null)
            {
                overlay.cachedSnapshot = CreateDefaultOverlaySnapshotDto();
                return;
            }

            var snapshotId = string.IsNullOrEmpty(overlay.slot) ? string.Empty : overlay.slot;
            var snapshot = sampler.SampleSnapshot(snapshotId, overlay.animationClip);
            overlay.cachedSnapshot = ConvertSnapshotToOverlayDto(snapshot);
        }

        private static void BakeBaseExpressionSnapshot(
            BaseExpressionSerializable baseExpression,
            IExpressionAnimationClipSampler sampler)
        {
            if (baseExpression == null)
            {
                return;
            }

            if (baseExpression.animationClip == null)
            {
                baseExpression.cachedSnapshot = BaseExpressionSerializable.CreateEmptySnapshot();
                return;
            }

            var snapshot = sampler.SampleSnapshot(BaseExpressionSnapshotId, baseExpression.animationClip);
            baseExpression.cachedSnapshot = ConvertSnapshotToDto(snapshot);
        }

        /// <summary>
        /// 指定 SO の最新データを規約パス <c>StreamingAssets/FacialControl/{SO 名}/profile.json</c>
        /// に書き出す。本メソッドはサンプリング自体は行わないため、呼出側が事前に
        /// <see cref="SampleAnimationClipsIntoCachedSnapshots"/> を呼ぶか、Inspector で
        /// cachedSnapshot を埋めておく必要がある。
        /// </summary>
        /// <param name="so">エクスポート対象 SO。null の場合は何もせず false を返す。</param>
        /// <returns>少なくとも 1 ファイルを書き出せた場合 true。</returns>
        public static bool ExportProfileJson(FacialCharacterProfileSO so)
        {
            if (so == null)
            {
                return false;
            }

            string assetName = so.CharacterAssetName;
            if (string.IsNullOrWhiteSpace(assetName))
            {
                Debug.LogWarning(
                    "FacialCharacterProfileExporter: SO 名が空のため StreamingAssets エクスポートをスキップします。"
                    + " SO アセットを保存し名前を確定させてください。");
                return false;
            }

            try
            {
                string profilePath = FacialCharacterProfileSO.GetStreamingAssetsProfilePath(assetName);
                if (string.IsNullOrEmpty(profilePath))
                {
                    return false;
                }
                EnsureParentDirectory(profilePath);
                var dto = BuildProfileSnapshotDto(so);
                var json = new SystemTextJsonParser().SerializeProfileSnapshot(dto);
                File.WriteAllText(profilePath, json, System.Text.Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"FacialCharacterProfileExporter: '{assetName}' の profile.json エクスポートに失敗しました: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 親ディレクトリを必要に応じて作成する。<see cref="ExportProfileJson"/> から呼び出されるが、
        /// ラッパ側でも同パスへの書き出しを行う場合に再利用できるよう公開している。
        /// </summary>
        public static void EnsureParentDirectory(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        // ============================================================
        // 内部ヘルパー: snapshot ↔ DTO 変換
        // ============================================================

        internal static ProfileSnapshotDto BuildProfileSnapshotDto(FacialCharacterProfileSO so)
        {
            var dto = new ProfileSnapshotDto
            {
                schemaVersion = string.IsNullOrEmpty(so.SchemaVersion) ? SystemTextJsonParser.SchemaVersionV2 : so.SchemaVersion,
                slots = CopyStringList(so.Slots),
                layers = new List<LayerDefinitionDto>(),
                expressions = new List<ExpressionDto>(),
                rendererPaths = new List<string>(),
                gazeConfigs = ConvertGazeConfigsToDto(so.GazeConfigs),
                defaultOverlays = BuildOverlaySlotBindingDtoList(so.DefaultOverlays),
                // ベース表情は bake 済み snapshot をそのまま JSON へ載せる
                // （AnimationClip 参照は SO 内のみで JSON には含めない）。
                baseExpression = so.BaseExpression.EnsureCachedSnapshot(),
            };

            LogSlotDiagnostics(so);

            // top-level rendererPaths: Inspector 入力 + 各 Expression snapshot からの統合
            var rendererPathSet = new HashSet<string>(StringComparer.Ordinal);
            if (so.RendererPaths != null)
            {
                for (int i = 0; i < so.RendererPaths.Count; i++)
                {
                    var p = so.RendererPaths[i] ?? string.Empty;
                    if (rendererPathSet.Add(p)) dto.rendererPaths.Add(p);
                }
            }

            // layers
            if (so.Layers != null)
            {
                for (int i = 0; i < so.Layers.Count; i++)
                {
                    var src = so.Layers[i];
                    if (src == null) continue;
                    dto.layers.Add(new LayerDefinitionDto
                    {
                        name = src.name ?? string.Empty,
                        priority = src.priority,
                        exclusionMode = SerializeExclusionMode(src.exclusionMode),
                        inputSources = BuildInputSourceDtoList(src.inputSources),
                    });
                }
            }

            // expressions: cachedSnapshot を優先採用
            if (so.Expressions != null)
            {
                for (int i = 0; i < so.Expressions.Count; i++)
                {
                    var src = so.Expressions[i];
                    if (src == null) continue;

                    // LayerOverrideMask は所属 Layer から取得する。Layer 側が空なら従来の
                    // Expression 自体の mask（後方互換）を採用する。
                    var layerMask = FacialCharacterProfileConverter.ResolveLayerMask(so.Layers, src.layer);
                    var resolvedMask = (layerMask != null && layerMask.Count > 0)
                        ? CopyStringList(new List<string>(layerMask))
                        : CopyStringList(src.layerOverrideMask);

                    var exprDto = new ExpressionDto
                    {
                        id = src.id ?? string.Empty,
                        name = src.name ?? string.Empty,
                        layer = src.layer ?? string.Empty,
                        layerOverrideMask = resolvedMask,
                        snapshot = src.cachedSnapshot ?? CreateDefaultSnapshotDto(),
                    };

                    // Inspector スライダーの transitionDuration を JSON 出力にも反映する。
                    // cachedSnapshot 側がベイク時の旧値を保持していても、Inspector で編集された
                    // 最新値が runtime (StreamingAssets JSON 経路) でも採用されるように上書きする。
                    if (exprDto.snapshot != null)
                    {
                        exprDto.snapshot.transitionDuration = src.transitionDuration;
                        exprDto.snapshot.overlays = BuildOverlaySlotBindingDtoList(src.overlays);
                    }

                    // Expression snapshot の rendererPaths を top-level set にマージ
                    MergeRendererPaths(exprDto.snapshot, rendererPathSet, dto.rendererPaths);
                    MergeOverlayRendererPaths(exprDto.snapshot?.overlays, rendererPathSet, dto.rendererPaths);

                    dto.expressions.Add(exprDto);
                }
            }

            // ベース表情 snapshot の rendererPaths も top-level set にマージする。
            MergeRendererPaths(dto.baseExpression, rendererPathSet, dto.rendererPaths);

            MergeOverlayRendererPaths(dto.defaultOverlays, rendererPathSet, dto.rendererPaths);
            return dto;
        }

        private static void LogSlotDiagnostics(FacialCharacterProfileSO so)
        {
            var slots = so.Slots;
            var referencedSlots = new HashSet<string>(StringComparer.Ordinal);

            if (slots != null)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    var slot = slots[i] ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(slot))
                    {
                        continue;
                    }

                    for (int j = i + 1; j < slots.Count; j++)
                    {
                        if (string.Equals(slot, slots[j], StringComparison.Ordinal))
                        {
                            Debug.LogWarning(
                                $"FacialCharacterProfileExporter: duplicate overlay slot '{slot}' in _slots.");
                            break;
                        }
                    }
                }
            }

            CollectReferencedSlots(so.DefaultOverlays, referencedSlots, "defaultOverlays", slots);

            var expressions = so.Expressions;
            if (expressions != null)
            {
                for (int i = 0; i < expressions.Count; i++)
                {
                    var expr = expressions[i];
                    if (expr == null)
                    {
                        continue;
                    }

                    CollectReferencedSlots(
                        expr.overlays,
                        referencedSlots,
                        $"expressions[{i}].snapshot.overlays",
                        slots);
                }
            }

            if (slots == null)
            {
                return;
            }

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i] ?? string.Empty;
                if (string.IsNullOrWhiteSpace(slot))
                {
                    continue;
                }

                if (!referencedSlots.Contains(slot))
                {
                    Debug.LogWarning(
                        $"FacialCharacterProfileExporter: overlay slot '{slot}' is declared in _slots but is not referenced by overlays.");
                }
            }
        }

        private static void CollectReferencedSlots(
            IReadOnlyList<OverlaySlotBindingSerializable> overlays,
            HashSet<string> referencedSlots,
            string path,
            IReadOnlyList<string> declaredSlots)
        {
            if (overlays == null || overlays.Count == 0)
            {
                return;
            }

            for (int i = 0; i < overlays.Count; i++)
            {
                var overlay = overlays[i];
                if (overlay == null || string.IsNullOrWhiteSpace(overlay.slot))
                {
                    continue;
                }

                referencedSlots.Add(overlay.slot);
                if (!ContainsSlot(declaredSlots, overlay.slot))
                {
                    Debug.LogWarning(
                        $"FacialCharacterProfileExporter: overlay slot '{overlay.slot}' referenced by {path}[{i}] is not declared in _slots.");
                }
            }
        }

        private static bool ContainsSlot(IReadOnlyList<string> slots, string slot)
        {
            if (slots == null || string.IsNullOrEmpty(slot))
            {
                return false;
            }

            for (int i = 0; i < slots.Count; i++)
            {
                if (string.Equals(slots[i], slot, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<GazeBindingConfigDto> ConvertGazeConfigsToDto(IReadOnlyList<GazeBindingConfig> configs)
        {
            return FacialCharacterProfileConverter.ToGazeConfigDtos(configs);
        }

        private static ExpressionSnapshotDto ConvertSnapshotToDto(ExpressionSnapshot snapshot)
        {
            var dto = new ExpressionSnapshotDto();
            PopulateSnapshotDto(dto, snapshot);
            return dto;
        }

        /// <summary>
        /// overlay binding 用の snapshot DTO（再帰終端型 <see cref="OverlaySnapshotDto"/>）を生成する。
        /// expression レベルの <see cref="ConvertSnapshotToDto"/> と異なり overlays フィールドを持たない。
        /// </summary>
        private static OverlaySnapshotDto ConvertSnapshotToOverlayDto(ExpressionSnapshot snapshot)
        {
            var dto = new OverlaySnapshotDto();
            PopulateSnapshotDto(dto, snapshot);
            return dto;
        }

        /// <summary>
        /// <see cref="OverlaySnapshotDto"/>（および派生の <see cref="ExpressionSnapshotDto"/>）共通フィールドを
        /// ドメイン snapshot から埋める。expression / overlay 双方の変換経路で共有する。
        /// </summary>
        private static void PopulateSnapshotDto(OverlaySnapshotDto dto, ExpressionSnapshot snapshot)
        {
            dto.transitionDuration = snapshot.TransitionDuration;
            dto.transitionCurvePreset = snapshot.TransitionCurvePreset.ToString();
            dto.blendShapes = new List<BlendShapeSnapshotDto>(snapshot.BlendShapes.Length);
            dto.bones = new List<BoneSnapshotDto>(snapshot.Bones.Length);
            dto.rendererPaths = new List<string>(snapshot.RendererPaths.Length);

            var bsSpan = snapshot.BlendShapes.Span;
            for (int i = 0; i < bsSpan.Length; i++)
            {
                dto.blendShapes.Add(new BlendShapeSnapshotDto
                {
                    rendererPath = bsSpan[i].RendererPath ?? string.Empty,
                    name = bsSpan[i].Name ?? string.Empty,
                    value = bsSpan[i].Value,
                });
            }

            var boneSpan = snapshot.Bones.Span;
            for (int i = 0; i < boneSpan.Length; i++)
            {
                var b = boneSpan[i];
                dto.bones.Add(new BoneSnapshotDto
                {
                    bonePath = b.BonePath ?? string.Empty,
                    position = new Vector3(b.PositionX, b.PositionY, b.PositionZ),
                    rotationEuler = new Vector3(b.EulerX, b.EulerY, b.EulerZ),
                    scale = new Vector3(b.ScaleX, b.ScaleY, b.ScaleZ),
                });
            }

            var rpSpan = snapshot.RendererPaths.Span;
            for (int i = 0; i < rpSpan.Length; i++)
            {
                dto.rendererPaths.Add(rpSpan[i] ?? string.Empty);
            }
        }

        private static ExpressionSnapshotDto CreateDefaultSnapshotDto()
        {
            return new ExpressionSnapshotDto
            {
                transitionDuration = 0.25f,
                transitionCurvePreset = "Linear",
                blendShapes = new List<BlendShapeSnapshotDto>(),
                bones = new List<BoneSnapshotDto>(),
                rendererPaths = new List<string>(),
            };
        }

        /// <summary>
        /// overlay binding 用のデフォルト（空）snapshot DTO（再帰終端型 <see cref="OverlaySnapshotDto"/>）。
        /// suppress / clip 未割り当て時に <c>overlay.cachedSnapshot</c> へ入れる。
        /// </summary>
        private static OverlaySnapshotDto CreateDefaultOverlaySnapshotDto()
        {
            return new OverlaySnapshotDto
            {
                transitionDuration = 0.25f,
                transitionCurvePreset = "Linear",
                blendShapes = new List<BlendShapeSnapshotDto>(),
                bones = new List<BoneSnapshotDto>(),
                rendererPaths = new List<string>(),
            };
        }

        private static List<OverlaySlotBindingDto> BuildOverlaySlotBindingDtoList(
            IReadOnlyList<OverlaySlotBindingSerializable> source)
        {
            if (source == null || source.Count == 0)
            {
                return new List<OverlaySlotBindingDto>();
            }

            var list = new List<OverlaySlotBindingDto>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                var overlay = source[i];
                if (overlay == null || string.IsNullOrWhiteSpace(overlay.slot))
                {
                    continue;
                }

                list.Add(new OverlaySlotBindingDto
                {
                    slot = overlay.slot,
                    suppress = overlay.suppress,
                    snapshot = ShouldEmitOverlaySnapshot(overlay)
                        ? overlay.cachedSnapshot
                        : null,
                });
            }

            return list;
        }

        private static bool ShouldEmitOverlaySnapshot(OverlaySlotBindingSerializable overlay)
        {
            return overlay != null
                && !overlay.suppress
                && overlay.animationClip != null
                && !IsSnapshotEmpty(overlay.cachedSnapshot);
        }

        private static bool IsSnapshotEmpty(OverlaySnapshotDto snapshot)
        {
            return snapshot == null
                || ((snapshot.blendShapes == null || snapshot.blendShapes.Count == 0)
                    && (snapshot.bones == null || snapshot.bones.Count == 0));
        }

        private static void MergeOverlayRendererPaths(
            IReadOnlyList<OverlaySlotBindingDto> overlays,
            HashSet<string> rendererPathSet,
            List<string> rendererPaths)
        {
            if (overlays == null || overlays.Count == 0)
            {
                return;
            }

            for (int i = 0; i < overlays.Count; i++)
            {
                MergeRendererPaths(overlays[i]?.snapshot, rendererPathSet, rendererPaths);
            }
        }

        private static void MergeRendererPaths(
            OverlaySnapshotDto snapshot,
            HashSet<string> rendererPathSet,
            List<string> rendererPaths)
        {
            if (snapshot == null || snapshot.rendererPaths == null)
            {
                return;
            }

            for (int i = 0; i < snapshot.rendererPaths.Count; i++)
            {
                var p = snapshot.rendererPaths[i] ?? string.Empty;
                if (rendererPathSet.Add(p))
                {
                    rendererPaths.Add(p);
                }
            }
        }

        private static List<InputSourceDto> BuildInputSourceDtoList(List<InputSourceDeclarationSerializable> sources)
        {
            if (sources == null || sources.Count == 0)
            {
                return new List<InputSourceDto>
                {
                    new InputSourceDto { id = "input", weight = 1.0f }
                };
            }
            var list = new List<InputSourceDto>(sources.Count);
            for (int i = 0; i < sources.Count; i++)
            {
                var s = sources[i];
                if (s == null) continue;
                list.Add(new InputSourceDto
                {
                    id = s.id ?? string.Empty,
                    weight = s.weight,
                    optionsJson = string.IsNullOrEmpty(s.optionsJson) ? null : s.optionsJson,
                });
            }
            return list;
        }

        private static string SerializeExclusionMode(ExclusionMode mode)
        {
            return mode switch
            {
                ExclusionMode.LastWins => "lastWins",
                ExclusionMode.Blend => "blend",
                _ => "lastWins"
            };
        }

        private static List<string> CopyStringList(IReadOnlyList<string> src)
        {
            if (src == null) return new List<string>();
            var copy = new List<string>(src.Count);
            for (int i = 0; i < src.Count; i++) copy.Add(src[i] ?? string.Empty);
            return copy;
        }
    }
}
