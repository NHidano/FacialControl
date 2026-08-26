using System;
using System.Collections.Generic;

namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// キャラクター単位の表情設定プロファイル。
    /// レイヤー定義と複数の Expression を保持する不変オブジェクト。
    /// </summary>
    public readonly struct FacialProfile
    {
        private const string DefaultFallbackLayer = "emotion";

        /// <summary>
        /// JSON スキーマバージョン
        /// </summary>
        public string SchemaVersion { get; }

        /// <summary>
        /// レイヤー定義の配列
        /// </summary>
        public ReadOnlyMemory<LayerDefinition> Layers { get; }

        /// <summary>
        /// 全 Expression の配列
        /// </summary>
        public ReadOnlyMemory<Expression> Expressions { get; }

        /// <summary>
        /// Overlay slot 識別子の宣言配列。
        /// Adapter Bindings の overlaySlot / Expression.Overlays / DefaultOverlays が参照する権威ソース。
        /// </summary>
        public ReadOnlyMemory<string> Slots { get; }

        /// <summary>
        /// SkinnedMeshRenderer のヒエラルキーパス配列（モデルルートからの相対パス）
        /// </summary>
        public ReadOnlyMemory<string> RendererPaths { get; }

        /// <summary>
        /// レイヤー毎の <c>inputSources</c> 宣言配列。外側のインデックスは <see cref="Layers"/> と揃う。
        /// null / 空配列の場合は Serializer 側が placeholder（<c>input, weight=1.0</c>）を出力する。
        /// JSON round-trip 安定性  の担体。
        /// </summary>
        public ReadOnlyMemory<InputSourceDeclaration[]> LayerInputSources { get; }

        /// <summary>
        /// active 表情側で該当 slot が未宣言、または default fallback を宣言している場合に参照される default overlay。
        /// 各要素は <see cref="OverlaySlotBinding.Suppress"/> と
        /// <see cref="OverlaySlotBinding.Snapshot"/> の組み合わせで default fallback /
        /// 明示 suppress / 個別 snapshot override の 3 状態を表す。
        /// </summary>
        public ReadOnlyMemory<OverlaySlotBinding> DefaultOverlays { get; }

        /// <summary>
        /// ベース表情の BlendShape スナップショット配列（正規化値 0..1）。
        /// どのレイヤーも contribute しない BlendShape index に残す初期値を表す。
        /// 常時固定表情キャラや衣装固定 BlendShape のための土台であり、
        /// 未設定（空）の場合は出力バッファが全 0 で初期化される。
        /// </summary>
        public ReadOnlyMemory<BlendShapeSnapshot> BaseExpression { get; }

        /// <summary>
        /// 表情設定プロファイルを生成する。配列パラメータは防御的コピーされる。
        /// </summary>
        /// <param name="schemaVersion">JSON スキーマバージョン（空文字不可）</param>
        /// <param name="layers">レイヤー定義の配列。null の場合は空配列</param>
        /// <param name="expressions">Expression の配列。null の場合は空配列</param>
        /// <param name="rendererPaths">SkinnedMeshRenderer パスの配列。null の場合は空配列</param>
        /// <param name="layerInputSources">
        /// レイヤー毎の <c>inputSources</c> 宣言（外側インデックスは <paramref name="layers"/> と揃える想定）。
        /// null の場合は空配列。round-trip 用の担体として Parser が設定する。
        /// </param>
        /// <param name="defaultOverlays">default overlay の配列。null の場合は空配列。</param>
        /// <param name="slots">overlay slot 識別子の宣言配列。null の場合は空配列。</param>
        /// <param name="baseExpression">
        /// ベース表情の BlendShape スナップショット配列（正規化値 0..1）。null / 空の場合は空配列。
        /// </param>
        public FacialProfile(
            string schemaVersion,
            LayerDefinition[] layers = null,
            Expression[] expressions = null,
            string[] rendererPaths = null,
            InputSourceDeclaration[][] layerInputSources = null,
            OverlaySlotBinding[] defaultOverlays = null,
            string[] slots = null,
            BlendShapeSnapshot[] baseExpression = null)
        {
            if (schemaVersion == null)
                throw new ArgumentNullException(nameof(schemaVersion));
            if (string.IsNullOrWhiteSpace(schemaVersion))
                throw new ArgumentException("スキーマバージョンを空にすることはできません。", nameof(schemaVersion));

            SchemaVersion = schemaVersion;

            // 防御的コピーで不変性を保証
            if (layers != null)
            {
                var layerCopy = new LayerDefinition[layers.Length];
                Array.Copy(layers, layerCopy, layers.Length);
                Layers = layerCopy;
            }
            else
            {
                Layers = Array.Empty<LayerDefinition>();
            }

            if (expressions != null)
            {
                var exprCopy = new Expression[expressions.Length];
                Array.Copy(expressions, exprCopy, expressions.Length);
                Expressions = exprCopy;
            }
            else
            {
                Expressions = Array.Empty<Expression>();
            }

            if (slots != null)
            {
                var slotCopy = new string[slots.Length];
                Array.Copy(slots, slotCopy, slots.Length);
                Slots = slotCopy;
            }
            else
            {
                Slots = Array.Empty<string>();
            }

            if (rendererPaths != null)
            {
                var pathsCopy = new string[rendererPaths.Length];
                Array.Copy(rendererPaths, pathsCopy, rendererPaths.Length);
                RendererPaths = pathsCopy;
            }
            else
            {
                RendererPaths = Array.Empty<string>();
            }

            if (layerInputSources != null)
            {
                var lisCopy = new InputSourceDeclaration[layerInputSources.Length][];
                for (int i = 0; i < layerInputSources.Length; i++)
                {
                    var inner = layerInputSources[i];
                    if (inner == null)
                    {
                        lisCopy[i] = Array.Empty<InputSourceDeclaration>();
                    }
                    else
                    {
                        var innerCopy = new InputSourceDeclaration[inner.Length];
                        Array.Copy(inner, innerCopy, inner.Length);
                        lisCopy[i] = innerCopy;
                    }
                }
                LayerInputSources = lisCopy;
            }
            else
            {
                LayerInputSources = Array.Empty<InputSourceDeclaration[]>();
            }

            if (defaultOverlays != null && defaultOverlays.Length > 0)
            {
                var doCopy = new OverlaySlotBinding[defaultOverlays.Length];
                Array.Copy(defaultOverlays, doCopy, defaultOverlays.Length);
                DefaultOverlays = doCopy;
            }
            else
            {
                DefaultOverlays = Array.Empty<OverlaySlotBinding>();
            }

            if (baseExpression != null && baseExpression.Length > 0)
            {
                var baseCopy = new BlendShapeSnapshot[baseExpression.Length];
                Array.Copy(baseExpression, baseCopy, baseExpression.Length);
                BaseExpression = baseCopy;
            }
            else
            {
                BaseExpression = Array.Empty<BlendShapeSnapshot>();
            }
        }

        /// <summary>
        /// 指定 slot の fallback 用 overlay 解決状態を表す <see cref="OverlaySlotBinding"/> を
        /// <see cref="DefaultOverlays"/> から検索する。
        /// </summary>
        /// <param name="slot">検索対象 slot 名。</param>
        /// <param name="binding">見つかった binding。見つからなければ default。</param>
        /// <returns>当該 slot の fallback binding が宣言されていれば true（suppress / snapshot / default fallback を含む）。</returns>
        public bool TryGetDefaultOverlay(string slot, out OverlaySlotBinding binding)
        {
            binding = default;
            if (string.IsNullOrEmpty(slot))
            {
                return false;
            }

            var span = DefaultOverlays.Span;
            for (int i = 0; i < span.Length; i++)
            {
                if (string.Equals(span[i].Slot, slot, StringComparison.Ordinal))
                {
                    binding = span[i];
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Slots 宣言の重複と overlay binding からの未宣言 slot 参照を検証する。
        /// </summary>
        /// <returns>検出した不正 slot 参照のリスト。問題がない場合は空リスト。</returns>
        public IReadOnlyList<InvalidSlotReference> ValidateSlotReferences()
        {
            var invalidRefs = new List<InvalidSlotReference>();
            var slotSpan = Slots.Span;

            for (int i = 0; i < slotSpan.Length; i++)
            {
                for (int j = i + 1; j < slotSpan.Length; j++)
                {
                    if (string.Equals(slotSpan[i], slotSpan[j], StringComparison.Ordinal))
                    {
                        invalidRefs.Add(new InvalidSlotReference(
                            slotSpan[i],
                            InvalidSlotReference.DuplicateReason));
                        break;
                    }
                }
            }

            var defaultOverlaySpan = DefaultOverlays.Span;
            for (int i = 0; i < defaultOverlaySpan.Length; i++)
            {
                AddUndeclaredSlotIfNeeded(
                    invalidRefs,
                    slotSpan,
                    defaultOverlaySpan[i].Slot);
            }

            var exprSpan = Expressions.Span;
            for (int i = 0; i < exprSpan.Length; i++)
            {
                var overlaySpan = exprSpan[i].Overlays.Span;
                for (int j = 0; j < overlaySpan.Length; j++)
                {
                    AddUndeclaredSlotIfNeeded(
                        invalidRefs,
                        slotSpan,
                        overlaySpan[j].Slot);
                }
            }

            return invalidRefs;
        }

        /// <summary>
        /// Expression のレイヤー参照が Layers に存在するか検証する。
        /// 未定義レイヤーを参照している Expression のリストを返す。
        /// </summary>
        public List<InvalidLayerReference> ValidateLayerReferences()
        {
            var invalidRefs = new List<InvalidLayerReference>();
            var layerSpan = Layers.Span;
            var exprSpan = Expressions.Span;

            for (int i = 0; i < exprSpan.Length; i++)
            {
                bool found = false;
                for (int j = 0; j < layerSpan.Length; j++)
                {
                    if (layerSpan[j].Name == exprSpan[i].Layer)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    invalidRefs.Add(new InvalidLayerReference(
                        exprSpan[i].Id,
                        exprSpan[i].Layer));
                }
            }

            return invalidRefs;
        }

        /// <summary>
        /// Expression の有効レイヤーを取得する。
        /// 未定義レイヤーを参照している場合は "emotion" レイヤーにフォールバックする。
        /// "emotion" レイヤーも未定義の場合は最初のレイヤーにフォールバックする。
        /// レイヤーが1つも定義されていない場合は Expression の元のレイヤー名を返す。
        /// </summary>
        public string GetEffectiveLayer(Expression expression)
        {
            var layerSpan = Layers.Span;

            // レイヤーが定義されていない場合は元のレイヤー名をそのまま返す
            if (layerSpan.Length == 0)
                return expression.Layer;

            // 参照レイヤーが定義済みならそのまま返す
            for (int i = 0; i < layerSpan.Length; i++)
            {
                if (layerSpan[i].Name == expression.Layer)
                    return expression.Layer;
            }

            // 未定義の場合、"emotion" レイヤーにフォールバック
            for (int i = 0; i < layerSpan.Length; i++)
            {
                if (layerSpan[i].Name == DefaultFallbackLayer)
                    return DefaultFallbackLayer;
            }

            // "emotion" も未定義の場合、最初のレイヤーにフォールバック
            return layerSpan[0].Name;
        }

        /// <summary>
        /// ID で Expression を検索する。見つからない場合は null を返す。
        /// </summary>
        public Expression? FindExpressionById(string id)
        {
            if (id == null)
                throw new ArgumentNullException(nameof(id));

            var span = Expressions.Span;
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i].Id == id)
                    return span[i];
            }

            return null;
        }

        /// <summary>
        /// レイヤー名で Expression を検索する。
        /// </summary>
        public ReadOnlyMemory<Expression> GetExpressionsByLayer(string layer)
        {
            if (layer == null)
                throw new ArgumentNullException(nameof(layer));

            var span = Expressions.Span;
            var results = new List<Expression>();

            for (int i = 0; i < span.Length; i++)
            {
                if (span[i].Layer == layer)
                    results.Add(span[i]);
            }

            return results.ToArray();
        }

        /// <summary>
        /// レイヤー名でレイヤー定義を検索する。見つからない場合は null を返す。
        /// </summary>
        public LayerDefinition? FindLayerByName(string name)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            var span = Layers.Span;
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i].Name == name)
                    return span[i];
            }

            return null;
        }

        private static void AddUndeclaredSlotIfNeeded(
            List<InvalidSlotReference> invalidRefs,
            ReadOnlySpan<string> declaredSlots,
            string slot)
        {
            if (ContainsSlot(declaredSlots, slot))
            {
                return;
            }

            invalidRefs.Add(new InvalidSlotReference(
                slot,
                InvalidSlotReference.UndeclaredReason));
        }

        private static bool ContainsSlot(ReadOnlySpan<string> declaredSlots, string slot)
        {
            for (int i = 0; i < declaredSlots.Length; i++)
            {
                if (string.Equals(declaredSlots[i], slot, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
