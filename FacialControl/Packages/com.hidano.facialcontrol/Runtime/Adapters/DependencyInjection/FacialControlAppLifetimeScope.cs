using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Interfaces;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Hidano.FacialControl.Adapters.DependencyInjection
{
    /// <summary>
    /// FacialControl の app-level <see cref="LifetimeScope"/>。
    /// PlayMode 開始時に <c>RuntimeInitializeOnLoadMethod</c> で auto-spawn される singleton MonoBehaviour として動作し、
    /// <see cref="ITimeProvider"/> 等のプロセス共有 service を保持する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/> は PlayMode 開始時のみ呼ばれるため、
    /// Edit Mode（PlayMode に入っていない Editor 状態）では auto-spawn されない。
    /// </para>
    /// <para>
    /// <see cref="FacialControllerLifetimeScope.Build"/> から <c>CreateChild</c> を呼ぶための親 scope として使われる。
    /// app scope に保持された service（<see cref="ITimeProvider"/> 等）は parent walkthrough により child scope
    /// から resolve 可能。
    /// </para>
    /// </remarks>
    public sealed class FacialControlAppLifetimeScope : LifetimeScope
    {
        private static FacialControlAppLifetimeScope _instance;

        /// <summary>
        /// 現在の app-level scope インスタンス。auto-spawn 完了後は non-null。
        /// </summary>
        public static FacialControlAppLifetimeScope Instance => _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void AutoSpawn()
        {
#if UNITY_EDITOR
            // Edit Mode 中の auto-spawn を抑止する（PlayMode 限定）。
            if (!UnityEngine.Application.isPlaying)
            {
                return;
            }
#endif
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject(nameof(FacialControlAppLifetimeScope));
            go.SetActive(false);
            var scope = go.AddComponent<FacialControlAppLifetimeScope>();
            scope.autoRun = false;
            DontDestroyOnLoad(go);
            go.SetActive(true);
            scope.Build();
            _instance = scope;
        }

        /// <summary>
        /// app scope を取得する（未生成なら同期的に生成する）。
        /// <see cref="FacialController"/> 等の利用側が <c>Initialize</c> 内で呼び、child scope build の親として使う。
        /// </summary>
        public static FacialControlAppLifetimeScope GetOrCreate()
        {
            if (_instance == null)
            {
                AutoSpawn();
            }
            return _instance;
        }

        /// <inheritdoc />
        protected override void Configure(IContainerBuilder builder)
        {
            // ITimeProvider はプロセス共有 singleton として登録する。
            builder.Register<ITimeProvider, UnityTimeProvider>(Lifetime.Singleton);

            // ILipSyncProvider は optional。利用側が必要に応じて拡張で登録する。
        }

        /// <inheritdoc />
        protected override void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
            base.OnDestroy();
        }
    }
}
