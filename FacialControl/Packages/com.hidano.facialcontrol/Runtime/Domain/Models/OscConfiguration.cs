using System;

namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// OSC 通信設定。送受信ポート、プリセット名、マッピングテーブルを保持する。
    /// </summary>
    public readonly struct OscConfiguration
    {
        /// <summary>
        /// デフォルト送信ポート
        /// </summary>
        public const int DefaultSendPort = 9000;

        /// <summary>
        /// デフォルト受信ポート
        /// </summary>
        public const int DefaultReceivePort = 9001;

        /// <summary>
        /// デフォルトプリセット名
        /// </summary>
        public const string DefaultPreset = "vrchat";

        /// <summary>
        /// OSC 送信ポート番号
        /// </summary>
        public int SendPort { get; }

        /// <summary>
        /// OSC 受信ポート番号
        /// </summary>
        public int ReceivePort { get; }

        /// <summary>
        /// プリセット名（例: "vrchat", "arkit"）
        /// </summary>
        public string Preset { get; }

        /// <summary>
        /// OSC アドレスと BlendShape のマッピング配列
        /// </summary>
        public ReadOnlyMemory<OscMapping> Mapping { get; }

        /// <summary>
        /// OSC 通信設定を生成する。mapping は防御的コピーされる。
        /// </summary>
        /// <param name="sendPort">送信ポート番号（0〜65535）</param>
        /// <param name="receivePort">受信ポート番号（0〜65535）</param>
        /// <param name="preset">プリセット名。null の場合はデフォルト値を使用</param>
        /// <param name="mapping">OSC マッピング配列。null の場合は空配列</param>
        public OscConfiguration(
            int sendPort = DefaultSendPort,
            int receivePort = DefaultReceivePort,
            string preset = DefaultPreset,
            OscMapping[] mapping = null)
        {
            if (sendPort < 0 || sendPort > 65535)
                throw new ArgumentOutOfRangeException(nameof(sendPort), "ポート番号は 0〜65535 の範囲で指定してください。");
            if (receivePort < 0 || receivePort > 65535)
                throw new ArgumentOutOfRangeException(nameof(receivePort), "ポート番号は 0〜65535 の範囲で指定してください。");

            SendPort = sendPort;
            ReceivePort = receivePort;
            Preset = preset ?? DefaultPreset;

            // 防御的コピー
            if (mapping != null)
            {
                var copy = new OscMapping[mapping.Length];
                Array.Copy(mapping, copy, mapping.Length);
                Mapping = copy;
            }
            else
            {
                Mapping = Array.Empty<OscMapping>();
            }
        }
    }
}
