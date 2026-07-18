using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Hidano.FacialControl.Adapters.Input;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Adapters.InputSources
{
    /// <summary>
    /// Keyboard / Controller 統合の Expression トリガー型入力源アダプタ。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 1 GameObject に 1 個アタッチする MonoBehaviour（<see cref="DisallowMultipleComponentAttribute"/>）。
    /// 内部に keyboard 用 / controller 用の 2 つの <see cref="ExpressionTriggerInputSourceBase"/> インスタンスを
    /// composition で保持し、<see cref="UnityEngine.InputSystem.InputAction.bindings"/> の先頭 path を
    /// <see cref="InputDeviceCategorizer"/> で分類して dispatch する（D-12 既存挙動を継承）。
    /// </para>
    /// <para>
    /// 旧 <c>KeyboardExpressionInputSource</c> / <c>ControllerExpressionInputSource</c> を継承する代わりに
    /// 保有する（コンポジション）。<c>Category</c> プロパティは外部公開しない（自動推定のため不要）。
    /// </para>
    /// <para>
    /// 未認識 device は Controller 側へ dispatch + <see cref="Debug.LogWarning(object)"/> を本インスタンス
    /// 1 回のみ出力する。InputAction の processor チェーンが unsupported な場合は
    /// raw value で続行 + warning 1 回のみ。
    /// </para>
    /// <para>
    /// per-frame 0-alloc: 値読取は <see cref="InputAction.CallbackContext.ReadValue{TValue}"/>
    /// を struct context 経由で呼ぶため boxing なし、Dictionary 走査は preallocated。
    /// 各 <c>BindingEntry</c> はコンストラクタで delegate を 1 回だけ生成しキャッシュする。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("FacialControl/Expression Input Source Adapter")]
    public sealed class ExpressionInputSourceAdapter : MonoBehaviour
    {
        private ExpressionTriggerInputSourceBase _keyboardSink;
        private ExpressionTriggerInputSourceBase _controllerSink;

        private readonly Dictionary<InputAction, BindingEntry> _bindings
            = new Dictionary<InputAction, BindingEntry>();

        private bool _hasWarnedUnknownDevice;
        private bool _hasWarnedUnsupportedProcessor;
        private bool _isSubscribed;
        private bool _wasKeyboardSuspended;
        private bool _wasControllerSuspended;

        /// <summary>Keyboard 系入力に紐付く内部 sink（診断 / テスト用に公開）。</summary>
        public ExpressionTriggerInputSourceBase KeyboardSink => _keyboardSink;

        /// <summary>Controller 系入力に紐付く内部 sink（診断 / テスト用に公開）。</summary>
        public ExpressionTriggerInputSourceBase ControllerSink => _controllerSink;

        /// <summary>
        /// 現在 InputAction にハンドラが購読されているバインディング数（診断 / テスト用）。
        /// <see cref="OnDisable"/> 後は 0、<see cref="OnEnable"/> 後は <see cref="BindingCount"/> と一致する。
        /// </summary>
        public int SubscribedBindingCount { get; private set; }

        /// <summary>登録済みのバインディング数。</summary>
        public int BindingCount => _bindings.Count;

        /// <summary>初期化済みかどうか（Keyboard / Controller 両 sink が注入済み）。</summary>
        public bool IsInitialized => _keyboardSink != null && _controllerSink != null;

        /// <summary>
        /// Keyboard / Controller 双方の <see cref="ExpressionTriggerInputSourceBase"/> sink を注入する。
        /// 既に初期化済みであれば、現存バインディングの購読を一旦解除してから差し替える。
        /// </summary>
        /// <param name="keyboardSink">Keyboard 系入力を受ける sink。null 不可。</param>
        /// <param name="controllerSink">Controller 系入力を受ける sink。null 不可。</param>
        /// <exception cref="ArgumentNullException">いずれかの引数が null の場合。</exception>
        public void Initialize(
            ExpressionTriggerInputSourceBase keyboardSink,
            ExpressionTriggerInputSourceBase controllerSink)
        {
            if (keyboardSink == null) throw new ArgumentNullException(nameof(keyboardSink));
            if (controllerSink == null) throw new ArgumentNullException(nameof(controllerSink));

            if (_isSubscribed)
            {
                UnsubscribeAll();
            }

            _keyboardSink = keyboardSink;
            _controllerSink = controllerSink;
            _wasKeyboardSuspended = keyboardSink.IsTriggerInputSuspended;
            _wasControllerSuspended = controllerSink.IsTriggerInputSuspended;

            if (isActiveAndEnabled)
            {
                SubscribeAll();
            }
        }

        /// <summary>
        /// <see cref="InputAction"/> と Expression ID のバインディングを登録する
        /// （<paramref name="triggerMode"/> 既定 = <see cref="TriggerMode.Hold"/>、
        /// <see cref="ExpressionBindingEntry.triggerMode"/> 既定値と一致）。
        /// </summary>
        /// <remarks>
        /// Button 型は <paramref name="triggerMode"/> によって挙動が変わる：
        /// <list type="bullet">
        ///   <item><see cref="TriggerMode.Hold"/>: 押下中のみ ON（performed で TriggerOn、canceled で TriggerOff）。</item>
        ///   <item><see cref="TriggerMode.Toggle"/>: 押すたびに ON/OFF が切替わる（performed で flip）。</item>
        /// </list>
        /// Value 型は <paramref name="triggerMode"/> を無視し、値が &gt;0 のとき ON、=0 のとき OFF。
        /// </remarks>
        /// <param name="action">対象 <see cref="InputAction"/>。null 不可。</param>
        /// <param name="expressionId">トリガー対象の Expression ID。null / 空文字不可。</param>
        /// <param name="triggerMode">押下時の動作モード（Button 型のみ参照）。</param>
        public void BindExpression(InputAction action, string expressionId, TriggerMode triggerMode = TriggerMode.Hold)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (string.IsNullOrEmpty(expressionId))
                throw new ArgumentException("expressionId は空文字不可。", nameof(expressionId));

            UnbindExpression(action);

            var entry = new BindingEntry(this, action, expressionId, triggerMode);
            _bindings[action] = entry;

            if (_isSubscribed)
            {
                entry.Subscribe();
                SubscribedBindingCount++;
            }
        }

        /// <summary>
        /// 指定 <see cref="InputAction"/> のバインディングを解除する。
        /// 該当が無ければ何もしない。
        /// </summary>
        public void UnbindExpression(InputAction action)
        {
            if (action == null) return;
            if (_bindings.TryGetValue(action, out var entry))
            {
                if (entry.IsSubscribed)
                {
                    entry.Unsubscribe();
                    SubscribedBindingCount--;
                }
                _bindings.Remove(action);
            }
        }

        /// <summary>全バインディングを解除する。</summary>
        public void UnbindAll()
        {
            foreach (var kv in _bindings)
            {
                if (kv.Value.IsSubscribed)
                {
                    kv.Value.Unsubscribe();
                }
            }
            _bindings.Clear();
            SubscribedBindingCount = 0;
        }

        /// <summary>
        /// 内部 sink の遷移時間進行を行う。FacialController の LateUpdate 等から呼ぶ想定。
        /// </summary>
        public void Tick(float deltaTime)
        {
            ReconcileToggleStateAfterSuspension(_keyboardSink, ref _wasKeyboardSuspended);
            ReconcileToggleStateAfterSuspension(_controllerSink, ref _wasControllerSuspended);
            _keyboardSink?.Tick(deltaTime);
            _controllerSink?.Tick(deltaTime);
        }

        private void OnEnable()
        {
            SubscribeAll();
        }

        private void OnDisable()
        {
            UnsubscribeAll();
        }

        private void OnDestroy()
        {
            UnbindAll();
            _keyboardSink = null;
            _controllerSink = null;
            _wasKeyboardSuspended = false;
            _wasControllerSuspended = false;
        }

        private void SubscribeAll()
        {
            if (_isSubscribed)
            {
                return;
            }

            _isSubscribed = true;
            int subscribed = 0;
            foreach (var kv in _bindings)
            {
                kv.Value.Subscribe();
                subscribed++;
            }
            SubscribedBindingCount = subscribed;
        }

        private void UnsubscribeAll()
        {
            if (!_isSubscribed)
            {
                return;
            }

            foreach (var kv in _bindings)
            {
                if (kv.Value.IsSubscribed)
                {
                    kv.Value.Unsubscribe();
                }
            }
            _isSubscribed = false;
            SubscribedBindingCount = 0;
        }

        private void DispatchPerformed(BindingEntry entry, InputAction.CallbackContext ctx)
        {
            if (!IsInitialized)
            {
                return;
            }

            var sink = ResolveSink(entry.Action);
            if (sink == null)
            {
                return;
            }

            if (entry.Action.type == InputActionType.Button)
            {
                if (entry.TriggerMode == TriggerMode.Hold)
                {
                    if (!entry.IsActive)
                    {
                        entry.IsActive = true;
                        sink.TriggerOn(entry.ExpressionId);
                    }
                }
                else
                {
                    if (!ToggleStateReconciler.TryFlip(sink.IsTriggerInputSuspended, entry))
                    {
                        return;
                    }

                    if (entry.IsActive)
                    {
                        sink.TriggerOn(entry.ExpressionId);
                    }
                    else
                    {
                        sink.TriggerOff(entry.ExpressionId);
                    }
                }
                return;
            }

            float value = ReadFloatValueOrRaw(ctx);
            if (value > 0f && !entry.IsActive)
            {
                entry.IsActive = true;
                sink.TriggerOn(entry.ExpressionId);
            }
            else if (value <= 0f && entry.IsActive)
            {
                entry.IsActive = false;
                sink.TriggerOff(entry.ExpressionId);
            }
        }

        private void ReconcileToggleStateAfterSuspension(
            ExpressionTriggerInputSourceBase sink,
            ref bool wasSuspended)
        {
            if (sink == null)
            {
                wasSuspended = false;
                return;
            }

            bool isSuspended = sink.IsTriggerInputSuspended;
            if (wasSuspended && !isSuspended)
            {
                SyncToggleBindingsWithSink(sink);
            }

            wasSuspended = isSuspended;
        }

        private void SyncToggleBindingsWithSink(ExpressionTriggerInputSourceBase sink)
        {
            IReadOnlyList<string> activeExpressionIds = sink.ActiveExpressionIds;
            foreach (KeyValuePair<InputAction, BindingEntry> pair in _bindings)
            {
                BindingEntry entry = pair.Value;
                if (!entry.IsToggleButton)
                {
                    continue;
                }

                if (!ReferenceEquals(ResolveSink(entry.Action), sink))
                {
                    continue;
                }

                ToggleStateReconciler.SyncWithStack(activeExpressionIds, entry);
            }
        }

        private void DispatchCanceled(BindingEntry entry, InputAction.CallbackContext ctx)
        {
            if (!IsInitialized)
            {
                return;
            }

            var sink = ResolveSink(entry.Action);
            if (sink == null)
            {
                return;
            }

            if (entry.Action.type == InputActionType.Button)
            {
                if (entry.TriggerMode == TriggerMode.Hold && entry.IsActive)
                {
                    entry.IsActive = false;
                    sink.TriggerOff(entry.ExpressionId);
                }
                return;
            }

            if (entry.IsActive)
            {
                entry.IsActive = false;
                sink.TriggerOff(entry.ExpressionId);
            }
        }

        private float ReadFloatValueOrRaw(InputAction.CallbackContext ctx)
        {
            try
            {
                return ctx.ReadValue<float>();
            }
            catch (Exception ex)
            {
                if (!_hasWarnedUnsupportedProcessor)
                {
                    _hasWarnedUnsupportedProcessor = true;
                    Debug.LogWarning(
                        $"[ExpressionInputSourceAdapter] unsupported processor combination on action " +
                        $"'{ctx.action?.name}': {ex.GetType().Name} {ex.Message}. " +
                        "Falling back to raw 0 value. This warning is emitted only once per instance.");
                }
                return 0f;
            }
        }

        private ExpressionTriggerInputSourceBase ResolveSink(InputAction action)
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
                    $"[ExpressionInputSourceAdapter] unrecognized device category for action " +
                    $"'{action.name}' (binding path='{path}'). Falling back to controller sink. " +
                    "This warning is emitted only once per instance.");
            }

            return category == DeviceCategory.Keyboard ? _keyboardSink : _controllerSink;
        }

        /// <summary>
        /// 1 件の InputAction バインディングを保持し、performed / canceled の購読状態を管理する。
        /// delegate は構築時に 1 回だけ生成しキャッシュすることで購読 / 解除に伴うヒープ確保を避ける。
        /// </summary>
        private sealed class BindingEntry : IToggleStateEntry
        {
            public InputAction Action { get; }
            public string ExpressionId { get; }
            public TriggerMode TriggerMode { get; }
            public bool IsActive { get; set; }
            public bool IsSubscribed { get; private set; }
            public bool IsToggleButton => TriggerMode == TriggerMode.Toggle && Action.type == InputActionType.Button;

            private readonly ExpressionInputSourceAdapter _adapter;
            private readonly Action<InputAction.CallbackContext> _onPerformed;
            private readonly Action<InputAction.CallbackContext> _onCanceled;

            public BindingEntry(
                ExpressionInputSourceAdapter adapter,
                InputAction action,
                string expressionId,
                TriggerMode triggerMode)
            {
                _adapter = adapter;
                Action = action;
                ExpressionId = expressionId;
                TriggerMode = triggerMode;
                _onPerformed = OnPerformed;
                _onCanceled = OnCanceled;
            }

            public void Subscribe()
            {
                if (IsSubscribed) return;
                Action.performed += _onPerformed;
                Action.canceled += _onCanceled;
                IsSubscribed = true;
            }

            public void Unsubscribe()
            {
                if (!IsSubscribed) return;
                Action.performed -= _onPerformed;
                Action.canceled -= _onCanceled;
                IsSubscribed = false;
            }

            private void OnPerformed(InputAction.CallbackContext ctx)
            {
                _adapter.DispatchPerformed(this, ctx);
            }

            private void OnCanceled(InputAction.CallbackContext ctx)
            {
                _adapter.DispatchCanceled(this, ctx);
            }
        }
    }
}
