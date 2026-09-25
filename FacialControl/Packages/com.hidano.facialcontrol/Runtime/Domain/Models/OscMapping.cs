using System;

namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// OSC アドレスと BlendShape 名・レイヤーのマッピング。
    /// config.json の osc.mapping 配列の 1 エントリに対応する。
    /// </summary>
    public readonly struct OscMapping
    {
        /// <summary>
        /// OSC アドレス（例: "/avatar/parameters/Fcl_ALL_Joy"）
        /// </summary>
        public string OscAddress { get; }

        /// <summary>
        /// 対象 BlendShape 名
        /// </summary>
        public string BlendShapeName { get; }

        /// <summary>
        /// 対象レイヤー名
        /// </summary>
        public string Layer { get; }

        /// <summary>
        /// OSC マッピングを生成する。
        /// </summary>
        /// <param name="oscAddress">OSC アドレス</param>
        /// <param name="blendShapeName">対象 BlendShape 名</param>
        /// <param name="layer">対象レイヤー名</param>
        public OscMapping(string oscAddress, string blendShapeName, string layer)
        {
            OscAddress = oscAddress ?? throw new ArgumentNullException(nameof(oscAddress));
            BlendShapeName = blendShapeName ?? throw new ArgumentNullException(nameof(blendShapeName));
            Layer = layer ?? throw new ArgumentNullException(nameof(layer));
        }
    }
}
