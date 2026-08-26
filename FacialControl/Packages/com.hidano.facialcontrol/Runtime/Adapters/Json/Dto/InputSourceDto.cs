namespace Hidano.FacialControl.Adapters.Json.Dto
{
    /// <summary>
    /// <c>layers[].inputSources[]</c> の 1 エントリを表す JSON 直接 DTO。
    /// <para>
    /// <see cref="optionsJson"/> は JSON 上の <c>options</c> フィールドの
    /// 生 JSON サブ文字列（<c>{ ... }</c> ブロックそのもの）を保持する。
    /// JsonUtility が自由形式 <c>Dictionary&lt;string, string&gt;</c> をサポートしない
    /// 制約を回避するため、後段で id ごとの typed DTO
    /// （<see cref="OscOptionsDto"/> など）へデシリアライズする。
    /// </para>
    /// </summary>
    [System.Serializable]
    public sealed class InputSourceDto
    {
        /// <summary>入力源 ID（予約 ID または <c>x-</c> プレフィックスの拡張 ID）。</summary>
        public string id;

        /// <summary>ソースウェイト。既定値 1.0。JSON 上で省略されても書込側はこの値を採用する。</summary>
        public float weight = 1.0f;

        /// <summary>
        /// <c>options</c> フィールドの生 JSON サブ文字列。
        /// null / 空のときは対応 DTO のデフォルト値インスタンスを用いる。
        /// </summary>
        public string optionsJson;
    }
}
