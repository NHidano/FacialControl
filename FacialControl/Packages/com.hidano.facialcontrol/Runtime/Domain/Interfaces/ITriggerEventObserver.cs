namespace Hidano.FacialControl.Domain.Interfaces
{
    /// <summary>
    /// トリガー型入力源の on/off 操作を source 単位で観測する契約。
    /// </summary>
    public interface ITriggerEventObserver
    {
        /// <summary>
        /// TriggerOn 成立後に呼ばれる。
        /// </summary>
        void OnTriggerOn(string sourceId, string expressionId);

        /// <summary>
        /// TriggerOff 成立後に呼ばれる。
        /// </summary>
        void OnTriggerOff(string sourceId, string expressionId);
    }
}
