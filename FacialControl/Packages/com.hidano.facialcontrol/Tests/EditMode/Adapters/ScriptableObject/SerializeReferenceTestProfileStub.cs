using System.Collections.Generic;
using UnityEngine;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests.SerializeReferenceSmoke
{
    /// <summary>
    /// Smoke test 用の SO Stub。core の <c>FacialCharacterProfileSO</c> に追加予定の
    /// <c>[SerializeReference] List&lt;AdapterBindingBase&gt; _adapterBindings</c> を模倣する。
    /// ScriptableObject 派生型は Unity の制約上、同名ファイルに定義する必要があるため
    /// テスト本体（<see cref="SerializeReferenceRoundTripSmokeTests"/>）から分離している。
    /// </summary>
    public sealed class SerializeReferenceTestProfileStub : UnityEngine.ScriptableObject
    {
        public const string AdapterBindingsFieldName = "_adapterBindings";

        [SerializeReference] private List<MockAdapterBindingStubBase> _adapterBindings
            = new List<MockAdapterBindingStubBase>();

        public List<MockAdapterBindingStubBase> AdapterBindings => _adapterBindings;
    }
}
