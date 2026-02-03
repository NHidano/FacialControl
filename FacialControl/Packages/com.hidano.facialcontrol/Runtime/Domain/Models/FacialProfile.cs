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
        /// 表情設定プロファイルを生成する。配列パラメータは防御的コピーされる。
        /// </summary>
        /// <param name="schemaVersion">JSON スキーマバージョン（空文字不可）</param>
        /// <param name="layers">レイヤー定義の配列。null の場合は空配列</param>
        /// <param name="expressions">Expression の配列。null の場合は空配列</param>
        public FacialProfile(
            string schemaVersion,
            LayerDefinition[] layers = null,
            Expression[] expressions = null)
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
    }
}
