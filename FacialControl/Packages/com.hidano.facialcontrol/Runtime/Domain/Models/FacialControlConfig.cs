using System;

namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// FacialControl の設定データ（config.json 対応）。
    /// OSC 設定やキャッシュ設定を保持する。
    /// </summary>
    public readonly struct FacialControlConfig
    {
        /// <summary>
        /// JSON スキーマバージョン
        /// </summary>
        public string SchemaVersion { get; }

        /// <summary>
        /// OSC 通信設定
        /// </summary>
        public OscConfiguration Osc { get; }

        /// <summary>
        /// キャッシュ設定
        /// </summary>
        public CacheConfiguration Cache { get; }

        public FacialControlConfig(
            string schemaVersion,
            OscConfiguration osc = default,
            CacheConfiguration cache = default)
        {
            if (schemaVersion == null)
                throw new ArgumentNullException(nameof(schemaVersion));
            if (string.IsNullOrWhiteSpace(schemaVersion))
                throw new ArgumentException("スキーマバージョンを空にすることはできません。", nameof(schemaVersion));

            SchemaVersion = schemaVersion;
            Osc = osc;
            Cache = cache;
        }
    }
}
