using System.Collections.Generic;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests.AdapterBindings
{
    /// <summary>
    /// Test 専用の concrete <see cref="FacialCharacterProfileSO"/>。
    /// task 4.2 で追加される protected <c>_adapterBindings</c> field に test 側から
    /// 直接書き込めるよう <see cref="WritableAdapterBindings"/> を公開する。
    /// ScriptableObject の MonoScript 解決のため独立ファイルかつ public sealed として配置する。
    /// </summary>
    public sealed class TestFacialCharacterProfileSO : FacialCharacterProfileSO
    {
        public List<AdapterBindingBase> WritableAdapterBindings => _adapterBindings;
    }
}
