using System;
using Hidano.FacialControl.Domain.Adapters;
using UnityEngine;
using VContainer.Unity;

namespace Hidano.FacialControl.Adapters.DependencyInjection
{
    /// <summary>
    /// <see cref="AdapterBindingBase"/> を VContainer の Plain C# Entry Point として動かすラッパー。
    /// 各 lifecycle interface を実装し、binding の virtual method に委譲する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 例外 isolation: <c>OnStart</c> / <c>OnTick</c> / <c>OnLateTick</c> / <c>OnFixedTick</c> /
    /// <c>Dispose</c> のいずれかで例外が発生した場合、catch + <c>Debug.LogError</c> + <c>_skipped = true</c>
    /// に遷移する。以降のフレームでは Tick 系を no-op にし、他 binding と core パイプラインの継続を保証する
    /// 。
    /// </para>
    /// <para>
    /// <c>Dispose</c> は <c>_skipped</c> の値に関わらず必ず呼ばれる（VContainer 規約）。Dispose 自体の
    /// 例外も catch + LogError し、後続 host の Dispose 継続を妨げない。
    /// </para>
    /// <para>
    /// DD-3: <c>if (_skipped) return;</c> と通常 dispatch は 0-alloc fast path。
    /// 例外発生フレーム単発の <c>Debug.LogError</c> 文字列補間アロケは &lt; 1 KB の許容範囲。
    /// </para>
    /// <para>
    /// 同期 dispatch 保証: VContainer の <see cref="IStartable.Start"/> は
    /// <c>PlayerLoopHelper.Dispatch(PlayerLoopTiming.Startup, ...)</c> 経由で次フレーム以降の PlayerLoop
    /// で fire される非同期分配であり、<c>FacialControllerLifetimeScope.Build</c> から戻った直後は
    /// 未 fire である。これを補うため <see cref="IInitializable"/> も実装し、Build 時に同期実行される
    /// <c>EntryPointDispatcher.Dispatch</c> 経由で <see cref="AdapterBindingBase.OnStart"/> が
    /// 同フレーム内に到達するよう保証する。<c>_started</c> フラグで Initialize / Start の二重実行を
    /// 抑止し、binding.OnStart は 1 LifetimeScope につき 1 回までに正規化する（task 6.2）。
    /// </para>
    /// </remarks>
    public sealed class AdapterBindingHost
        : IInitializable, IStartable, ITickable, ILateTickable, IFixedTickable, IDisposable
    {
        private readonly AdapterBindingBase _binding;
        private readonly AdapterBuildContext _buildContext;
        private readonly string _onStartErrorMessagePrefix;
        private readonly string _onTickErrorMessagePrefix;
        private readonly string _onLateTickErrorMessagePrefix;
        private readonly string _onFixedTickErrorMessagePrefix;
        private readonly string _disposeErrorMessagePrefix;
        private bool _skipped;
        private bool _started;

        /// <summary>
        /// 1 host = 1 binding の対応関係を強制する。
        /// </summary>
        /// <param name="binding">委譲対象の binding 実体（null 不可）。</param>
        /// <param name="buildContext"><see cref="AdapterBindingBase.OnStart"/> に渡す中立コンテキスト。</param>
        /// <exception cref="ArgumentNullException"><paramref name="binding"/> が null の場合。</exception>
        public AdapterBindingHost(AdapterBindingBase binding, AdapterBuildContext buildContext)
        {
            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }

            _binding = binding;
            _buildContext = buildContext;
            string bindingTypeName = _binding.GetType().FullName;
            _onStartErrorMessagePrefix =
                "[FacialControl] AdapterBindingHost '" + bindingTypeName + "' failed in OnStart: ";
            _onTickErrorMessagePrefix =
                "[FacialControl] AdapterBindingHost '" + bindingTypeName + "' failed in OnTick: ";
            _onLateTickErrorMessagePrefix =
                "[FacialControl] AdapterBindingHost '" + bindingTypeName + "' failed in OnLateTick: ";
            _onFixedTickErrorMessagePrefix =
                "[FacialControl] AdapterBindingHost '" + bindingTypeName + "' failed in OnFixedTick: ";
            _disposeErrorMessagePrefix =
                "[FacialControl] AdapterBindingHost '" + bindingTypeName + "' failed in Dispose: ";
            _skipped = false;
            _started = false;
        }

        void IInitializable.Initialize()
        {
            InvokeOnStartOnce();
        }

        void IStartable.Start()
        {
            InvokeOnStartOnce();
        }

        private void InvokeOnStartOnce()
        {
            if (_skipped) return;
            if (_started) return;
            _started = true;
            try
            {
                PhonemeOverlayInputSourceRegistration.RegisterDeclaredSlots(in _buildContext);
                _binding.OnStart(in _buildContext);
            }
            catch (Exception ex)
            {
                _skipped = true;
                LogFailure(_onStartErrorMessagePrefix, ex);
            }
        }

        void ITickable.Tick()
        {
            if (_skipped) return;
            try
            {
                _binding.OnTick(Time.deltaTime);
            }
            catch (Exception ex)
            {
                _skipped = true;
                LogFailure(_onTickErrorMessagePrefix, ex);
            }
        }

        void ILateTickable.LateTick()
        {
            if (_skipped) return;
            try
            {
                _binding.OnLateTick(Time.deltaTime);
            }
            catch (Exception ex)
            {
                _skipped = true;
                LogFailure(_onLateTickErrorMessagePrefix, ex);
            }
        }

        void IFixedTickable.FixedTick()
        {
            if (_skipped) return;
            try
            {
                _binding.OnFixedTick(Time.fixedDeltaTime);
            }
            catch (Exception ex)
            {
                _skipped = true;
                LogFailure(_onFixedTickErrorMessagePrefix, ex);
            }
        }

        void IDisposable.Dispose()
        {
            try
            {
                _binding.Dispose();
            }
            catch (Exception ex)
            {
                _skipped = true;
                LogFailure(_disposeErrorMessagePrefix, ex);
            }
        }

        private static void LogFailure(string messagePrefix, Exception ex)
        {
            Debug.LogError(messagePrefix + ex.GetType().FullName + ": " + ex.Message);
        }
    }
}
