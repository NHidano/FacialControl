using UnityEngine;

namespace Hidano.FacialControl.IFacialMocap.Samples
{
    /// <summary>
    /// <c>IFacialMocapReceiverDemo.unity</c> 用の最小ヘルパ。
    /// Editor / Player が非フォーカスのときでも UDP 受信を PlayerLoop で処理し続けられるよう
    /// <see cref="UnityEngine.Application.runInBackground"/> を有効化するだけの責務を持つ。
    /// </summary>
    /// <remarks>
    /// 実際の受信〜表情反映は Profile に結線した <c>IFacialMocapReceiverAdapterBinding</c> が担う。
    /// このヘルパは受信用 UDP ソケットを自前で開かない（同梱の診断用
    /// <see cref="IFacialMocapReceiverDemoBootstrap"/> と異なり、binding の host と listen ポートが
    /// 衝突しないため実装 Scene に同居できる）。
    /// </remarks>
    [DefaultExecutionOrder(-1000)]
    [AddComponentMenu("FacialControl/Samples/iFacialMocap Receiver Demo (Run In Background)")]
    public sealed class IFacialMocapDemoRunInBackground : MonoBehaviour
    {
        private void Awake()
        {
            UnityEngine.Application.runInBackground = true;
        }
    }
}
