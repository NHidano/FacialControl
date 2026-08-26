namespace Hidano.FacialControl.Adapters.IFacialMocap
{
    /// <summary>
    /// iFacialMocap の name-value 区切り規約。標準は <c>-</c>、v2 は負値対応の <c>&amp;</c>。
    /// </summary>
    public enum IFacialMocapDataVersion
    {
        /// <summary>標準モード。BlendShape の name-value 区切りは <c>-</c>。値は 0〜100。</summary>
        Standard = 0,

        /// <summary>v2 モード（<c>|sendDataVersion=v2</c>）。区切りは <c>&amp;</c>。負値を許容。</summary>
        V2 = 1,
    }

    /// <summary>
    /// iFacialMocap (iOS) UDP/TCP プロトコルの定数と補助関数。
    /// 通信仕様: https://www.ifacialmocap.com/for-developer/
    /// </summary>
    /// <remarks>
    /// 本クラスは UnityEngine 非依存で、パケット解析 (<see cref="IFacialMocapPacketParser"/>) と
    /// ハンドシェイク生成の双方から参照される。
    /// </remarks>
    public static class IFacialMocapProtocol
    {
        /// <summary>ハンドシェイク送信先（端末側）ポート。</summary>
        public const int DeviceTriggerPort = 49983;

        /// <summary>UDP モードの既定 listen ポート（端末はこのポートへ返信する）。</summary>
        public const int DefaultListenPort = 49983;

        /// <summary>TCP モードのデータ受信ポート。</summary>
        public const int TcpDataPort = 49986;

        /// <summary>UDP ストリーム開始トリガー文字列。</summary>
        public const string UdpTrigger = "iFacialMocap_sahuasouryya9218sauhuiayeta91555dy3719";

        /// <summary>UDP ハンドシェイク → TCP ストリーム開始トリガー文字列。</summary>
        public const string UdpTcpTrigger = "iFacialMocap_UDPTCP_sahuasouryya9218sauhuiayeta91555dy3719";

        /// <summary>TCP ストリーム停止トリガー文字列。</summary>
        public const string UdpTcpStop = "iFacialMocap_UDPTCPSTOP_sahuasouryya9218sauhuiayeta91555dy3719";

        /// <summary>v2（負値対応）モードを要求するトリガー接尾辞。</summary>
        public const string V2Suffix = "|sendDataVersion=v2";

        /// <summary>TCP パケット終端マーカー。</summary>
        public const string TcpTerminator = "___iFacialMocap";

        /// <summary>BlendShape セクションと Transform セクションの区切り。</summary>
        public const char SectionSeparator = '=';

        /// <summary>エントリ区切り。</summary>
        public const char EntrySeparator = '|';

        /// <summary>標準モードの name-value 区切り。</summary>
        public const char StandardNameValueSeparator = '-';

        /// <summary>v2 モードの name-value 区切り。</summary>
        public const char V2NameValueSeparator = '&';

        /// <summary>Transform エントリの key と値列の区切り。</summary>
        public const char TransformSeparator = '#';

        /// <summary>値列内の成分区切り。</summary>
        public const char ComponentSeparator = ',';

        /// <summary>頭部 Transform の key。</summary>
        public const string HeadKey = "head";

        /// <summary>右目 Transform の key。</summary>
        public const string RightEyeKey = "rightEye";

        /// <summary>左目 Transform の key。</summary>
        public const string LeftEyeKey = "leftEye";

        /// <summary>BlendShape 値の最大値（0〜100 スケール）。</summary>
        public const float BlendShapeMaxValue = 100f;

        /// <summary>指定バージョンの name-value 区切り文字を返す。</summary>
        public static char NameValueSeparator(IFacialMocapDataVersion version)
            => version == IFacialMocapDataVersion.V2 ? V2NameValueSeparator : StandardNameValueSeparator;

        /// <summary>
        /// 端末へ送るハンドシェイク文字列を生成する。
        /// </summary>
        /// <param name="version">標準 / v2。v2 のとき <see cref="V2Suffix"/> を付与する。</param>
        /// <param name="tcp">true で TCP モードのトリガーを返す。</param>
        public static string BuildTrigger(IFacialMocapDataVersion version, bool tcp = false)
        {
            string baseTrigger = tcp ? UdpTcpTrigger : UdpTrigger;
            return version == IFacialMocapDataVersion.V2 ? baseTrigger + V2Suffix : baseTrigger;
        }
    }
}
