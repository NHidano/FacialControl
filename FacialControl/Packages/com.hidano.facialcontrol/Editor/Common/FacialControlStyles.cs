using UnityEngine.UIElements;

namespace Hidano.FacialControl.Editor.Common
{
    /// <summary>
    /// FacialControl Editor 共通 USS クラス名定数およびスタイルシート読み込みユーティリティ。
    /// </summary>
    public static class FacialControlStyles
    {
        /// <summary>読み取り専用の情報ラベル</summary>
        public const string InfoLabel = "fc-info-label";

        /// <summary>ステータスラベル（初期非表示）</summary>
        public const string StatusLabel = "fc-status-label";

        /// <summary>ステータス: エラー</summary>
        public const string StatusError = "fc-status-label--error";

        /// <summary>ステータス: 成功</summary>
        public const string StatusSuccess = "fc-status-label--success";

        /// <summary>アクションボタン</summary>
        public const string ActionButton = "fc-action-button";

        private static StyleSheet _cachedStyleSheet;

        /// <summary>
        /// 共通スタイルシートを読み込む。
        /// root VisualElement に追加して使用する。
        /// </summary>
        public static StyleSheet Load()
        {
            if (_cachedStyleSheet == null)
            {
                // パッケージ内のアセットパスで USS を読み込む
                _cachedStyleSheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(
                    "Packages/com.hidano.facialcontrol/Editor/Common/FacialControlCommon.uss");
            }

            return _cachedStyleSheet;
        }
    }
}
