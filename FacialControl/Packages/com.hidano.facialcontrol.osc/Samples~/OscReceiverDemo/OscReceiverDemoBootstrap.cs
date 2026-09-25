using UnityEngine;

namespace Hidano.FacialControl.Samples.OscReceiverDemo
{
    // Receiver 側 demo の最小 helper。procedural mesh の生成は廃止し、
    // ユーザーが Scene に持ち込んだ SkinnedMeshRenderer に対して FacialController が
    // 自動的に動作する設計に合わせる。本 MonoBehaviour の唯一の責務は OSC 通信を
    // ウィンドウ非フォーカス時にも処理させるため `Application.runInBackground` を有効化すること。
    [DefaultExecutionOrder(-1000)]
    [AddComponentMenu("FacialControl/Samples/OSC Receiver Demo Bootstrap")]
    public sealed class OscReceiverDemoBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            UnityEngine.Application.runInBackground = true;
        }
    }
}
