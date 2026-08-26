using System;
using NUnit.Framework;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    /// <summary>
    /// R-A: VContainer 1.17.x が Unity 6000.3.2f1 で動作することを検証する smoke test。
    /// LifetimeScope の build → resolve → dispose を 1 サイクル実行し、
    /// Lifetime.Scoped を含む基本機能が利用可能であることを assert する。
    /// fail した場合は Phase 1 全体を停止し別 DI 検討に切替える判断材料とする。
    /// </summary>
    [TestFixture]
    public class VContainerSmokeTests
    {
        private sealed class SmokeService
        {
            public int Token;
        }

        private sealed class DisposableSmokeService : IDisposable
        {
            public bool Disposed;

            public void Dispose()
            {
                Disposed = true;
            }
        }

        private sealed class TestLifetimeScope : LifetimeScope
        {
            public Action<IContainerBuilder> ConfigureAction;

            protected override void Configure(IContainerBuilder builder)
            {
                ConfigureAction?.Invoke(builder);
            }
        }

        private static TestLifetimeScope BuildScope(Action<IContainerBuilder> configure)
        {
            var go = new GameObject("VContainerSmokeTestScope");
            go.SetActive(false);
            var scope = go.AddComponent<TestLifetimeScope>();
            scope.autoRun = false;
            scope.ConfigureAction = configure;
            go.SetActive(true);
            scope.Build();
            return scope;
        }

        private static void SafeDispose(LifetimeScope scope)
        {
            if (scope == null)
            {
                return;
            }

            var go = scope.gameObject;
            scope.DisposeCore();
            if (go != null)
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LifetimeScope_BuildAndResolve_ReturnsScopedInstance()
        {
            TestLifetimeScope scope = null;
            try
            {
                scope = BuildScope(builder =>
                {
                    builder.Register<SmokeService>(Lifetime.Scoped);
                });

                Assert.IsNotNull(scope, "BuildScope should return a non-null scope");
                Assert.IsNotNull(scope.Container, "Container should be built after Build()");

                var first = scope.Container.Resolve<SmokeService>();
                var second = scope.Container.Resolve<SmokeService>();

                Assert.IsNotNull(first, "Lifetime.Scoped resolve should return a non-null instance");
                Assert.AreSame(first, second, "Lifetime.Scoped resolve within same scope should return the same instance");
            }
            finally
            {
                SafeDispose(scope);
            }
        }

        [Test]
        public void LifetimeScope_Dispose_DisposesScopedDisposableService()
        {
            DisposableSmokeService service = null;
            TestLifetimeScope scope = null;
            try
            {
                scope = BuildScope(builder =>
                {
                    builder.Register<DisposableSmokeService>(Lifetime.Scoped);
                });

                service = scope.Container.Resolve<DisposableSmokeService>();
                Assert.IsNotNull(service);
                Assert.IsFalse(service.Disposed, "Service should not be disposed before scope.Dispose");

                SafeDispose(scope);
                scope = null;

                Assert.IsTrue(service.Disposed, "Service registered with Lifetime.Scoped should be disposed when scope is disposed");
            }
            finally
            {
                SafeDispose(scope);
            }
        }

        [Test]
        public void LifetimeScope_Dispose_ClearsContainerReference()
        {
            TestLifetimeScope scope = null;
            try
            {
                scope = BuildScope(builder =>
                {
                    builder.Register<SmokeService>(Lifetime.Scoped);
                });
                Assert.IsNotNull(scope.Container);

                var go = scope.gameObject;
                scope.DisposeCore();

                Assert.IsNull(scope.Container, "scope.Container should be null after DisposeCore");

                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
                scope = null;
            }
            finally
            {
                SafeDispose(scope);
            }
        }
    }
}
