using System;

namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// レイヤー定義。表情制御のレイヤー名、優先度、排他モードを保持する。
    /// </summary>
    public readonly struct LayerDefinition
    {
        /// <summary>
        /// レイヤー名
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 優先度（0 以上、値が大きいほど優先）
        /// </summary>
        public int Priority { get; }

        /// <summary>
        /// レイヤー内の表情排他モード
        /// </summary>
        public ExclusionMode ExclusionMode { get; }

        public LayerDefinition(string name, int priority, ExclusionMode exclusionMode)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("名前を空にすることはできません。", nameof(name));
            if (priority < 0)
                throw new ArgumentOutOfRangeException(nameof(priority), priority, "Priority は 0 以上である必要があります。");

            Name = name;
            Priority = priority;
            ExclusionMode = exclusionMode;
        }
    }
}
