using System.Collections.Generic;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;

namespace Hidano.FacialControl.Osc.Tests.EditMode.Adapters.AdapterBindings
{
    /// <summary>
    /// osc package の round-trip テスト専用 concrete <see cref="FacialCharacterProfileSO"/>。
    /// protected <c>_adapterBindings</c> field を test 側から書込むために
    /// <see cref="WritableAdapterBindings"/> を公開する（core 側 TestFacialCharacterProfileSO の osc 版）。
    /// ScriptableObject の MonoScript 解決のため独立ファイルかつ public sealed として配置する。
    /// </summary>
    public sealed class OscArKitRoundTripTestProfileSO : FacialCharacterProfileSO
    {
        public List<AdapterBindingBase> WritableAdapterBindings => _adapterBindings;
    }
}
