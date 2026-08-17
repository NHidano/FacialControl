#if FACIALCONTROL_HAS_INPUTSYSTEM_MODULE && FACIALCONTROL_HAS_OSC_MODULE
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.AdapterBindings.ARKit;
using Hidano.FacialControl.Adapters.AdapterBindings.InputSystem;
using Hidano.FacialControl.Adapters.DependencyInjection;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer.Unity;

namespace Hidano.FacialControl.Tests.PlayMode.Performance
{
    /// <summary>
    /// task 12.4: Phase 2 完了後の実 binding 構成（OSC + InputSystem + ARKit）で
    /// AdapterBindingHost の 3 シナリオ 0-alloc 契約を再検証する。
    /// task 7.2 (<see cref="AdapterBindingHostAllocationTests"/>) が <c>TrackingAdapterBinding</c>
    /// のような Mock 派生で確立した契約を、Phase 2 移行後の実具象（<see cref="OscReceiverAdapterBinding"/>,
    /// <see cref="InputSystemAdapterBinding"/>, <see cref="ArKitOscAdapterBinding"/>）でも維持されて
    /// いることを 3 binding × 10 体構成で確認する。
    /// </summary>
    [TestFixture]
    public class AdapterBindingHostAllocationPhase2Tests
    {
        private const int CharacterCount = 10;
        private const int BindingsPerCharacter = 3;
        private const int FramesToMeasure = 60;
        private const long ExceptionFrameAllocationBudgetBytes = 1024;

        private static readonly Regex AdapterBindingHostOnTickErrorPattern =
            new Regex("AdapterBindingHost.*OnTick", RegexOptions.IgnoreCase);

        private readonly List<GameObject> _hostGameObjects = new List<GameObject>(CharacterCount);

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _hostGameObjects.Count; i++)
            {
                if (_hostGameObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_hostGameObjects[i]);
                }
            }

            _hostGameObjects.Clear();
        }

        [Test]
        public void Tick_NormalSteadyState_Phase2RealBindingsAllocateZeroBytes()
        {
            AdapterBindingHost[] hosts = BuildHosts(throwingBinding: null, out _);

            InitializeHosts(hosts);
            ExecuteFrames(hosts, 2);
            StabilizeManagedHeap();

            long allocated = MeasureManagedAllocation(() => ExecuteFrames(hosts, FramesToMeasure));

            Assert.LessOrEqual(allocated, 0,
                "Phase 2 normal steady-state managed allocation was detected over " +
                FramesToMeasure + " frames with " + CharacterCount + " characters × " +
                BindingsPerCharacter + " real bindings: " + allocated + " bytes");
        }

        [Test]
        public void Tick_SkippedSteadyState_AfterPhase2BindingExceptionAllocatesZeroBytes()
        {
            var throwingBinding = new ThrowOnNextTickAdapterBinding();
            AdapterBindingHost[] hosts = BuildHosts(throwingBinding, out _);

            InitializeHosts(hosts);

            throwingBinding.ThrowOnNextTick = true;
            LogAssert.Expect(LogType.Error, AdapterBindingHostOnTickErrorPattern);
            ExecuteFrames(hosts, 1);

            Assert.That(throwingBinding.OnTickCount, Is.EqualTo(1));

            ExecuteFrames(hosts, 2);
            StabilizeManagedHeap();

            long allocated = MeasureManagedAllocation(() => ExecuteFrames(hosts, FramesToMeasure));

            Assert.LessOrEqual(allocated, 0,
                "Phase 2 skipped steady-state managed allocation was detected over " +
                FramesToMeasure + " frames: " + allocated + " bytes");
        }

        [Test]
        public void Tick_ExceptionFrame_Phase2RealBindingsAllocateLessThanOneKilobyte()
        {
            var throwingBinding = new ThrowOnNextTickAdapterBinding();
            AdapterBindingHost[] hosts = BuildHosts(throwingBinding, out int throwingHostIndex);

            InitializeHosts(hosts);
            ExecuteFrames(hosts, 2);
            WarmUpExceptionLogCapture();
            StabilizeManagedHeap();

            throwingBinding.ThrowOnNextTick = true;
            long allocated;
            // Unity TestRunner keeps error stack traces in managed result buffers; this keeps the
            // sample focused on AdapterBindingHost's exception-frame path rather than runner storage.
            using (new ErrorStackTraceScope(StackTraceLogType.None))
            {
                LogAssert.Expect(LogType.Error, AdapterBindingHostOnTickErrorPattern);
                allocated = MeasureManagedAllocation(() =>
                {
                    ((ITickable)hosts[throwingHostIndex]).Tick();
                });
            }

            Assert.That(throwingBinding.OnTickCount, Is.EqualTo(3));
            Assert.Less(allocated, ExceptionFrameAllocationBudgetBytes,
                "Phase 2 exception frame managed allocation exceeded the budget: " + allocated + " bytes");
        }

        private AdapterBindingHost[] BuildHosts(
            ThrowOnNextTickAdapterBinding throwingBinding,
            out int throwingHostIndex)
        {
            var hosts = new AdapterBindingHost[CharacterCount * BindingsPerCharacter];
            throwingHostIndex = -1;

            var profile = new FacialProfile("2.0");
            var blendShapeNames = new[] { "Blink", "Smile", "MouthOpen" };

            int index = 0;
            for (int character = 0; character < CharacterCount; character++)
            {
                var hostGameObject = new GameObject(
                    "AdapterBindingHostAllocationPhase2TestsHost_" + character);
                _hostGameObjects.Add(hostGameObject);

                var context = new AdapterBuildContext(
                    profile,
                    blendShapeNames,
                    new InputSourceRegistry(),
                    new FacialOutputBus(),
                    new ManualTimeProvider(),
                    hostGameObject,
                    lipSyncProvider: null);

                for (int slot = 0; slot < BindingsPerCharacter; slot++)
                {
                    AdapterBindingBase bindingInstance;
                    if (throwingBinding != null && character == 0 && slot == 0)
                    {
                        bindingInstance = throwingBinding;
                        throwingHostIndex = index;
                    }
                    else
                    {
                        bindingInstance = CreatePhase2RealBinding(slot, character);
                    }

                    hosts[index] = new AdapterBindingHost(bindingInstance, context);
                    index++;
                }
            }

            return hosts;
        }

        // 各 character につき OSC + InputSystem + ARKit を 1 binding ずつ生成する。
        // どれも external resource（UDP socket, InputActionAsset）を要する OnStart を完走させない
        // 最小設定で構築するため、各 binding の OnStart は warning ブランチで早期 return し
        // 内部 _started フラグは false のまま維持される。これにより
        // 各具象の Tick / LateTick / FixedTick はすべて early-return path を辿り、
        // AdapterBindingHost 側の dispatch + virtual 呼び出しの 0-alloc 契約のみが検証対象となる。
        private static AdapterBindingBase CreatePhase2RealBinding(int slot, int characterIndex)
        {
            switch (slot)
            {
                case 0:
                    return new OscReceiverAdapterBinding
                    {
                        Slug = "phase2-osc-" + characterIndex,
                    };
                case 1:
                    return new InputSystemAdapterBinding
                    {
                        Slug = "phase2-input-system-" + characterIndex,
                    };
                case 2:
                    return new ArKitOscAdapterBinding
                    {
                        Slug = "phase2-arkit-" + characterIndex,
                    };
                default:
                    throw new InvalidOperationException(
                        "Unexpected Phase 2 binding slot index: " + slot);
            }
        }

        private void WarmUpExceptionLogCapture()
        {
            var warmupBinding = new ThrowOnNextTickAdapterBinding
            {
                ThrowOnNextTick = true,
            };
            AdapterBindingHost[] warmupHosts = BuildHosts(warmupBinding, out int warmupHostIndex);

            InitializeHosts(warmupHosts);
            using (new ErrorStackTraceScope(StackTraceLogType.None))
            {
                LogAssert.Expect(LogType.Error, AdapterBindingHostOnTickErrorPattern);
                ((ITickable)warmupHosts[warmupHostIndex]).Tick();
            }
        }

        private static void InitializeHosts(AdapterBindingHost[] hosts)
        {
            for (int i = 0; i < hosts.Length; i++)
            {
                ((IInitializable)hosts[i]).Initialize();
                ((IStartable)hosts[i]).Start();
            }
        }

        private static void ExecuteFrames(AdapterBindingHost[] hosts, int frameCount)
        {
            for (int frame = 0; frame < frameCount; frame++)
            {
                for (int i = 0; i < hosts.Length; i++)
                {
                    ((ITickable)hosts[i]).Tick();
                }

                for (int i = 0; i < hosts.Length; i++)
                {
                    ((ILateTickable)hosts[i]).LateTick();
                }

                for (int i = 0; i < hosts.Length; i++)
                {
                    ((IFixedTickable)hosts[i]).FixedTick();
                }
            }
        }

        private static long MeasureManagedAllocation(Action action)
        {
            using var recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC.Alloc",
                1,
                ProfilerRecorderOptions.SumAllSamplesInFrame
                    | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);

            action();
            return recorder.LastValue;
        }

        private static void StabilizeManagedHeap()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private sealed class ErrorStackTraceScope : IDisposable
        {
            private readonly StackTraceLogType _previous;

            public ErrorStackTraceScope(StackTraceLogType stackTraceLogType)
            {
                _previous = UnityEngine.Application.GetStackTraceLogType(LogType.Error);
                UnityEngine.Application.SetStackTraceLogType(LogType.Error, stackTraceLogType);
            }

            public void Dispose()
            {
                UnityEngine.Application.SetStackTraceLogType(LogType.Error, _previous);
            }
        }

        [Serializable]
        private sealed class ThrowOnNextTickAdapterBinding : AdapterBindingBase
        {
            private readonly Exception _exception =
                new InvalidOperationException("AdapterBindingHostAllocationPhase2Tests.OnTick");

            public int OnTickCount;
            public bool ThrowOnNextTick;

            public override void OnTick(float deltaTime)
            {
                OnTickCount++;
                if (!ThrowOnNextTick)
                {
                    return;
                }

                ThrowOnNextTick = false;
                throw _exception;
            }
        }
    }
}
#endif
