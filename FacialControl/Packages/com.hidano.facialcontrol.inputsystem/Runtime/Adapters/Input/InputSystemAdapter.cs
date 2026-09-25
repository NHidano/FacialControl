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
    /// InputAction の Button 型と Value 型の両方に対応する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Button 型は <see cref="TriggerMode"/> によって挙動が変わる。
    /// <list type="bullet">
    ///   <item><see cref="TriggerMode.Hold"/>: 押下中のみアクティブ (started で ON、canceled で OFF)。</item>
    ///   <item><see cref="TriggerMode.Toggle"/>: 押すたびにアクティブ/非アクティブが切替わる (performed でトグル)。</item>
    /// </list>
    /// Value 型は <see cref="TriggerMode"/> を無視し、値が 0 より大きいときアクティブ、
    /// 0 のとき非アクティブとなる (アナログ強度はそのまま反映される)。
    /// </para>
    /// <para>
    /// バインディング毎に <see cref="DeviceCategory"/> が決定され、<see cref="TryGetDeviceCategory"/> で参照できる。
    /// 未認識 device は <see cref="DeviceCategory.Controller"/> に fallback し、本インスタンスで
    /// 1 回のみ <see cref="Debug.LogWarning(object)"/> を出力する。
    /// </para>
    /// </remarks>
    public class InputSystemAdapter : IDisposable
    {
        private FacialController _facialController;
        private readonly Dictionary<InputAction, BindingEntry> _bindings = new Dictionary<InputAction, BindingEntry>();
        private bool _disposed;
        private bool _hasWarnedUnknownDevice;

        /// <summary>
        /// 制御対象の FacialController を指定して生成する。
        /// </summary>
        /// <param name="facialController">制御対象の FacialController。null 許容。</param>
        public InputSystemAdapter(FacialController facialController)
        {
            _facialController = facialController;
        }

        /// <summary>
        /// 制御対象の FacialController
        /// </summary>
        public FacialController FacialController
        {
            get => _facialController;
            set => _facialController = value;
        }

        /// <summary>
        /// InputAction と Expression のバインディングを登録する (旧 API、Toggle 動作)。
        /// </summary>
        /// <remarks>
        /// 後方互換のためデフォルトは <see cref="TriggerMode.Toggle"/>。
        /// 新規呼出側は <see cref="BindExpression(InputAction, Expression, TriggerMode)"/> でモードを明示すること。
        /// </remarks>
        public void BindExpression(InputAction action, Expression expression)
        {
            BindExpression(action, expression, TriggerMode.Toggle);
        }

        /// <summary>
        /// InputAction と Expression のバインディングを登録する。
        /// Button 型では <paramref name="triggerMode"/> に従ってトグル/Hold を選択し、
        /// Value 型ではアナログ強度に基づくアクティブ制御を行う (mode は無視される)。
        /// </summary>
        /// <param name="action">バインドする InputAction。</param>
        /// <param name="expression">トリガー対象の Expression。</param>
        /// <param name="triggerMode">押下時の動作モード (Button 型のみ参照)。</param>
        public void BindExpression(InputAction action, Expression expression, TriggerMode triggerMode)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            UnbindExpression(action);

            DeviceCategory category = ClassifyAction(action);

            var entry = new BindingEntry(expression, action, category, triggerMode);
            _bindings[action] = entry;

            if (action.type == InputActionType.Button)
            {
                if (triggerMode == TriggerMode.Hold)
                {
                    action.started += entry.OnButtonStarted;
                    action.canceled += entry.OnButtonCanceled;
                }
                else
                {
                    action.performed += entry.OnButtonPerformed;
                }
            }
            else
            {
                action.performed += entry.OnValuePerformed;
                action.canceled += entry.OnValueCanceled;
            }

            entry.Adapter = this;
        }

        /// <summary>
        /// 既登録の <see cref="InputAction"/> に推定された <see cref="DeviceCategory"/> を返す。
        /// </summary>
        /// <param name="action">対象 <see cref="InputAction"/>。null は false を返す。</param>
        /// <param name="category">推定 <see cref="DeviceCategory"/>（未登録時は <see cref="DeviceCategory.Controller"/>）。</param>
        /// <returns>登録済みであれば true。</returns>
        public bool TryGetDeviceCategory(InputAction action, out DeviceCategory category)
        {
            if (action != null && _bindings.TryGetValue(action, out var entry))
            {
                category = entry.Category;
                return true;
            }
            category = DeviceCategory.Controller;
            return false;
        }

        private DeviceCategory ClassifyAction(InputAction action)
        {
            string path = null;
            var bindings = action.bindings;
            if (bindings.Count > 0)
            {
                path = bindings[0].path;
            }

            DeviceCategory category = InputDeviceCategorizer.Categorize(path, out bool wasFallback);

            if (wasFallback && !_hasWarnedUnknownDevice)
            {
                _hasWarnedUnknownDevice = true;
                Debug.LogWarning(
                    $"[InputSystemAdapter] unrecognized device category for action " +
                    $"'{action.name}' (binding path='{path}'). Falling back to Controller. " +
                    "This warning is emitted only once per instance.");
            }

            return category;
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
                    if (entry.TriggerMode == TriggerMode.Hold)
                    {
                        action.started -= entry.OnButtonStarted;
                        action.canceled -= entry.OnButtonCanceled;
                    }
                    else
                    {
                        action.performed -= entry.OnButtonPerformed;
                    }
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
            var actions = new List<InputAction>(_bindings.Keys);
            for (int i = 0; i < actions.Count; i++)
            {
                UnbindExpression(actions[i]);
            }
        }

        /// <summary>
        /// アダプターを破棄し全バインディングを解除する。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            UnbindAll();
            _disposed = true;
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

        private void HandleButtonHoldOn(BindingEntry entry)
        {
            if (_facialController == null || !_facialController.IsInitialized)
                return;

            if (entry.IsActive)
            {
                return;
            }
            entry.IsActive = true;
            _facialController.Activate(entry.Expression);
        }

        private void HandleButtonHoldOff(BindingEntry entry)
        {
            if (_facialController == null || !_facialController.IsInitialized)
                return;

            if (!entry.IsActive)
            {
                return;
            }
            entry.IsActive = false;
            _facialController.Deactivate(entry.Expression);
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
            public DeviceCategory Category { get; }
            public TriggerMode TriggerMode { get; }
            public bool IsActive { get; set; }
            public InputSystemAdapter Adapter { get; set; }

            public BindingEntry(Expression expression, InputAction action, DeviceCategory category, TriggerMode triggerMode)
            {
                Expression = expression;
                Action = action;
                Category = category;
                TriggerMode = triggerMode;
                IsActive = false;
            }

            public void OnButtonPerformed(InputAction.CallbackContext context)
            {
                Adapter?.HandleButtonToggle(this);
            }

            public void OnButtonStarted(InputAction.CallbackContext context)
            {
                Adapter?.HandleButtonHoldOn(this);
            }

            public void OnButtonCanceled(InputAction.CallbackContext context)
            {
                Adapter?.HandleButtonHoldOff(this);
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
