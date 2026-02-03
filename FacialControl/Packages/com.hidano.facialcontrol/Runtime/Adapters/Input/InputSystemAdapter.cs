using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Adapters.Input
{
    /// <summary>
    /// InputSystem と FacialController を連携するアダプター。
    /// InputAction の Button 型（トグル）と Value 型（アナログ強度）の両方に対応する。
    /// Button 型: ボタン押下で Expression をアクティブ/非アクティブにトグル切り替え。
    /// Value 型: 0 より大きい値でアクティブ化、0 で非アクティブ化。
    /// </summary>
    [AddComponentMenu("FacialControl/Input System Adapter")]
    public class InputSystemAdapter : MonoBehaviour
    {
        [Tooltip("制御対象の FacialController")]
        [SerializeField]
        private FacialController _facialController;

        private readonly Dictionary<InputAction, BindingEntry> _bindings = new Dictionary<InputAction, BindingEntry>();

        /// <summary>
        /// 制御対象の FacialController
        /// </summary>
        public FacialController FacialController
        {
            get => _facialController;
            set => _facialController = value;
        }

        /// <summary>
        /// InputAction と Expression のバインディングを登録する。
        /// Button 型ではトグル動作、Value 型ではアナログ強度に基づくアクティブ制御を行う。
        /// </summary>
        /// <param name="action">バインドする InputAction</param>
        /// <param name="expression">トリガー対象の Expression</param>
        public void BindExpression(InputAction action, Expression expression)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            // 既存バインディングがあれば先に解除
            UnbindExpression(action);

            var entry = new BindingEntry(expression, action);
            _bindings[action] = entry;

            // コールバック登録
            if (action.type == InputActionType.Button)
            {
                action.performed += entry.OnButtonPerformed;
            }
            else
            {
                // Value 型
                action.performed += entry.OnValuePerformed;
                action.canceled += entry.OnValueCanceled;
            }

            entry.Adapter = this;
        }

        /// <summary>
        /// InputAction のバインディングを解除する。
        /// </summary>
        /// <param name="action">解除する InputAction</param>
        public void UnbindExpression(InputAction action)
        {
            if (action == null)
                return;

            if (_bindings.TryGetValue(action, out var entry))
            {
                if (action.type == InputActionType.Button)
                {
                    action.performed -= entry.OnButtonPerformed;
                }
                else
                {
                    action.performed -= entry.OnValuePerformed;
                    action.canceled -= entry.OnValueCanceled;
                }

                _bindings.Remove(action);
            }
        }

        /// <summary>
        /// 全てのバインディングを解除する。
        /// </summary>
        public void UnbindAll()
        {
            // コールバック解除のためにコピーしてイテレーション
            var actions = new List<InputAction>(_bindings.Keys);
            for (int i = 0; i < actions.Count; i++)
            {
                UnbindExpression(actions[i]);
            }
        }

        private void OnDisable()
        {
            UnbindAll();
        }

        private void HandleButtonToggle(BindingEntry entry)
        {
            if (_facialController == null || !_facialController.IsInitialized)
                return;

            entry.IsActive = !entry.IsActive;
            if (entry.IsActive)
            {
                　_facialController.Activate(entry.Expression);
            }
            else
            {
                _facialController.Deactivate(entry.Expression);
            }
        }

        private void HandleValueChange(BindingEntry entry, float value)
        {
            if (_facialController == null || !_facialController.IsInitialized)
                return;

            if (value > 0f && !entry.IsActive)
            {
                entry.IsActive = true;
                _facialController.Activate(entry.Expression);
            }
            else if (value <= 0f && entry.IsActive)
            {
                entry.IsActive = false;
                _facialController.Deactivate(entry.Expression);
            }
        }

        /// <summary>
        /// バインディングエントリ。InputAction と Expression の紐付けおよびトグル状態を管理する。
        /// </summary>
        private class BindingEntry
        {
            public Expression Expression { get; }
            public InputAction Action { get; }
            public bool IsActive { get; set; }
            public InputSystemAdapter Adapter { get; set; }

            public BindingEntry(Expression expression, InputAction action)
            {
                Expression = expression;
                Action = action;
                IsActive = false;
            }

            public void OnButtonPerformed(InputAction.CallbackContext context)
            {
                Adapter?.HandleButtonToggle(this);
            }

            public void OnValuePerformed(InputAction.CallbackContext context)
            {
                float value = context.ReadValue<float>();
                Adapter?.HandleValueChange(this, value);
            }

            public void OnValueCanceled(InputAction.CallbackContext context)
            {
                Adapter?.HandleValueChange(this, 0f);
            }
        }
    }
}
