using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.DependencyInjection;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer.Unity;

namespace Hidano.FacialControl.Tests.PlayMode.Performance
{
    /// <summary>
    /// task 7.2: locks the AdapterBindingHost GC contract for normal, skipped, and exception frames.
    /// </summary>
    [TestFixture]
    public class AdapterBindingHostAllocationTests
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
        public void Tick_NormalSteadyState_ThreeBindingsTenCharactersAllocatesZeroBytes()
        {
            AdapterBindingHost[] hosts = BuildHosts(null, out _);

            InitializeHosts(hosts);
            ExecuteFrames(hosts, 2);
            StabilizeManagedHeap();

            long allocated = MeasureManagedAllocation(() => ExecuteFrames(hosts, FramesToMeasure));

            Assert.LessOrEqual(allocated, 0,
                $"Normal steady-state managed allocation was detected over 60 frames: {allocated} bytes");
        }

        [Test]
        public void Tick_SkippedSteadyState_AfterExceptionAllocatesZeroBytes()
        {
            var throwingBinding = new ThrowOnNextTickAdapterBinding();
            AdapterBindingHost[] hosts = BuildHosts(throwingBinding, out _);

            InitializeHosts(hosts);

            // Warm Unity TestRunner の log capture pipeline を別 host 群で先に起動し、
            // 本 fixture の measurement window が log buffer 初期化アロケに巻き込まれないようにする。
            WarmUpExceptionLogCapture();

            throwingBinding.ThrowOnNextTick = true;
            using (new ErrorStackTraceScope(StackTraceLogType.None))
            {
                LogAssert.Expect(LogType.Error, AdapterBindingHostOnTickErrorPattern);
                ExecuteFrames(hosts, 1);
            }

            Assert.That(throwingBinding.OnTickCount, Is.EqualTo(1));

            ExecuteFrames(hosts, 2);
            StabilizeManagedHeap();

            long allocated = MeasureManagedAllocation(() => ExecuteFrames(hosts, FramesToMeasure));

            Assert.LessOrEqual(allocated, 0,
                $"Skipped steady-state managed allocation was detected over 60 frames: {allocated} bytes");
        }

        [Test]
        public void Tick_ExceptionFrame_AllocatesLessThanOneKilobyte()
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
                $"Exception frame managed allocation exceeded the budget: {allocated} bytes");
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
                var hostGameObject = new GameObject("AdapterBindingHostAllocationTestsHost_" + character);
                _hostGameObjects.Add(hostGameObject);

                var context = new AdapterBuildContext(
                    profile,
                    blendShapeNames,
                    new InputSourceRegistry(),
                    new FacialOutputBus(),
                    new ManualTimeProvider(),
                    hostGameObject,
                    lipSyncProvider: null);

                for (int binding = 0; binding < BindingsPerCharacter; binding++)
                {
                    AdapterBindingBase bindingInstance;
                    if (throwingBinding != null && character == 0 && binding == 0)
                    {
                        bindingInstance = throwingBinding;
                        throwingHostIndex = index;
                    }
                    else
                    {
                        bindingInstance = new TrackingAdapterBinding();
                    }

                    hosts[index] = new AdapterBindingHost(bindingInstance, context);
                    index++;
                }
            }

            return hosts;
        }

        private void WarmUpExceptionLogCapture()
        {
            var warmupBinding = new ThrowOnNextTickAdapterBinding
            {
                ThrowOnNextTick = true
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
            long before = GC.GetTotalMemory(forceFullCollection: false);
            action();
            long after = GC.GetTotalMemory(forceFullCollection: false);
            return after - before;
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
        private sealed class TrackingAdapterBinding : AdapterBindingBase
        {
            private int _tickCount;

            public override void OnTick(float deltaTime)
            {
                _tickCount++;
            }

            public override void OnLateTick(float deltaTime)
            {
                _tickCount++;
            }

            public override void OnFixedTick(float fixedDeltaTime)
            {
                _tickCount++;
            }
        }

        [Serializable]
        private sealed class ThrowOnNextTickAdapterBinding : AdapterBindingBase
        {
            private readonly Exception _exception =
                new InvalidOperationException("AdapterBindingHostAllocationTests.OnTick");

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
