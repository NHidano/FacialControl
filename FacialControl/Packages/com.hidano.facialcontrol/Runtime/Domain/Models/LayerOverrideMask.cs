using System;

namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// Expression がオーバーライド対象とするレイヤー集合を 32 bit のビットフラグで表現する値型。
    /// <para>
    /// bit position と layer 名の対応表は Adapters 層のヘルパー（例: <c>FacialCharacterProfileSO.Layers</c> を
    /// 参照する Adapters 側の変換ロジック）が責務として保持し、Domain は bit 値のみを扱う。
    /// JSON 永続化は bit 値ではなく layer 名の配列として保存することで Layer 並び替えに対する脆弱性を排除する。
    /// </para>
    /// <para>
    /// 詳細な設計判断は  Topic 9 を参照。
    /// </para>
    /// </summary>
    [Flags]
    public enum LayerOverrideMask : int
    {
        /// <summary>フラグなし（オーバーライド対象レイヤーが 1 つもない状態）。</summary>
        None = 0,

        /// <summary>bit 0</summary>
        Bit0 = 1 << 0,
        /// <summary>bit 1</summary>
        Bit1 = 1 << 1,
        /// <summary>bit 2</summary>
        Bit2 = 1 << 2,
        /// <summary>bit 3</summary>
        Bit3 = 1 << 3,
        /// <summary>bit 4</summary>
        Bit4 = 1 << 4,
        /// <summary>bit 5</summary>
        Bit5 = 1 << 5,
        /// <summary>bit 6</summary>
        Bit6 = 1 << 6,
        /// <summary>bit 7</summary>
        Bit7 = 1 << 7,
        /// <summary>bit 8</summary>
        Bit8 = 1 << 8,
        /// <summary>bit 9</summary>
        Bit9 = 1 << 9,
        /// <summary>bit 10</summary>
        Bit10 = 1 << 10,
        /// <summary>bit 11</summary>
        Bit11 = 1 << 11,
        /// <summary>bit 12</summary>
        Bit12 = 1 << 12,
        /// <summary>bit 13</summary>
        Bit13 = 1 << 13,
        /// <summary>bit 14</summary>
        Bit14 = 1 << 14,
        /// <summary>bit 15</summary>
        Bit15 = 1 << 15,
        /// <summary>bit 16</summary>
        Bit16 = 1 << 16,
        /// <summary>bit 17</summary>
        Bit17 = 1 << 17,
        /// <summary>bit 18</summary>
        Bit18 = 1 << 18,
        /// <summary>bit 19</summary>
        Bit19 = 1 << 19,
        /// <summary>bit 20</summary>
        Bit20 = 1 << 20,
        /// <summary>bit 21</summary>
        Bit21 = 1 << 21,
        /// <summary>bit 22</summary>
        Bit22 = 1 << 22,
        /// <summary>bit 23</summary>
        Bit23 = 1 << 23,
        /// <summary>bit 24</summary>
        Bit24 = 1 << 24,
        /// <summary>bit 25</summary>
        Bit25 = 1 << 25,
        /// <summary>bit 26</summary>
        Bit26 = 1 << 26,
        /// <summary>bit 27</summary>
        Bit27 = 1 << 27,
        /// <summary>bit 28</summary>
        Bit28 = 1 << 28,
        /// <summary>bit 29</summary>
        Bit29 = 1 << 29,
        /// <summary>bit 30</summary>
        Bit30 = 1 << 30,
        /// <summary>bit 31（符号 bit）</summary>
        Bit31 = 1 << 31,
    }
}
