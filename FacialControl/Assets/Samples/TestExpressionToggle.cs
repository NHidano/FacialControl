using Hidano.FacialControl.Adapters.Input;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Samples
{
    public class TestExpressionToggle : MonoBehaviour
    {
        [SerializeField] private FacialController facialController;

        private InputSystemAdapter _adapter;
        private InputAction _smileAction;

        private void Start()
        {
            _adapter = gameObject.AddComponent<InputSystemAdapter>();
            _adapter.FacialController = facialController;

            // テスト用 Expression を手動で作成
            // ← BlendShape 名はモデルに合わせて変更
            var expression = new Expression(
                id: "00000000-0000-0000-0000-000000000002",
                name: "blink",
                layer: "eye",
                transitionDuration: 0.25f,
                transitionCurve: new TransitionCurve(TransitionCurveType.Linear),
                blendShapeValues: new[]
                {
                    new BlendShapeMapping("まばたき", 1.0f)
                },
                layerSlots: System.Array.Empty<LayerSlot>()
            );

            // スペースキーでトグル（ボタン型: 押すたびに ON/OFF 切り替え）
            _smileAction = new InputAction("blink", InputActionType.Button, "<Keyboard>/space");
            _adapter.BindExpression(_smileAction, expression);
            _smileAction.Enable();

            Debug.Log("スペースキーで blink 表情をトグルします");
        }

        private void OnDestroy()
        {
            _smileAction?.Disable();
            _adapter?.UnbindAll();
        }
    }
}