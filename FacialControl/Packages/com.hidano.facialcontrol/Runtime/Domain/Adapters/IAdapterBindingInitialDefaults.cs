namespace Hidano.FacialControl.Domain.Adapters
{
    /// <summary>
    /// Editor 上で <see cref="AdapterBindingBase"/> 派生インスタンスを新規追加した直後に
    /// 呼ばれる初期値設定 hook。実装した binding のみ default 値が適用される。
    /// </summary>
    /// <remarks>
    /// runtime 経由で <see cref="System.Activator.CreateInstance(System.Type)"/> された場合は呼ばれず、
    /// あくまで Inspector 等の Editor 操作で「ユーザーが追加した直後」のシナリオ専用。
    /// </remarks>
    public interface IAdapterBindingInitialDefaults
    {
        void ApplyInitialDefaults();
    }
}
