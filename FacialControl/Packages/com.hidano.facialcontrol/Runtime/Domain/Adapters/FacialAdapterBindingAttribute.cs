using System;

namespace Hidano.FacialControl.Domain.Adapters
{
    /// <summary>
    /// 具象 <see cref="AdapterBindingBase"/> 派生クラスに displayName を付与し、
    /// <c>UnityEditor.TypeCache</c> 経由の discovery のマーカーとなる属性。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Domain layer 配置。adapter package は core を参照するだけで利用可能。
    /// 同一クラスへの多重付与・継承は不可（<see cref="AttributeUsageAttribute.AllowMultiple"/> = false,
    /// <see cref="AttributeUsageAttribute.Inherited"/> = false）。
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class FacialAdapterBindingAttribute : Attribute
    {
        /// <summary>
        /// Inspector / 各種 UI で表示する displayName。
        /// </summary>
        public string DisplayName { get; }

        public FacialAdapterBindingAttribute(string displayName)
        {
            DisplayName = displayName ?? string.Empty;
        }
    }
}
