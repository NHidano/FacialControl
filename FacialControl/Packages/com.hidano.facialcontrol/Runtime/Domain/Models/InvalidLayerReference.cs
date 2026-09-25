namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// Expression が未定義レイヤーを参照している情報。
    /// </summary>
    public readonly struct InvalidLayerReference
    {
        /// <summary>
        /// 対象 Expression の ID
        /// </summary>
        public string ExpressionId { get; }

        /// <summary>
        /// 参照されている未定義レイヤー名
        /// </summary>
        public string ReferencedLayer { get; }

        /// <summary>
        /// 無効なレイヤー参照情報を生成する。
        /// </summary>
        /// <param name="expressionId">対象 Expression の ID</param>
        /// <param name="referencedLayer">参照されている未定義レイヤー名</param>
        public InvalidLayerReference(string expressionId, string referencedLayer)
        {
            ExpressionId = expressionId;
            ReferencedLayer = referencedLayer;
        }
    }
}
