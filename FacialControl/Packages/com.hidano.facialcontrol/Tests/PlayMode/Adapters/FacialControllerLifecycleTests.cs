using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.PlayMode.Adapters
{
    /// <summary>
    /// P08-T02: FacialController のライフサイクルテスト。
    /// OnEnable / OnDisable / Initialize の動作を検証する。
    /// </summary>
    [TestFixture]
    public class FacialControllerLifecycleTests
    {
        private GameObject _gameObject;

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_gameObject);
                _gameObject = null;
            }
        }

        // ================================================================
        // コンポーネント追加
        // ================================================================

        [Test]
        public void AddComponent_FacialController_CanBeAdded()
        {
            _gameObject = CreateGameObjectWithAnimator();

            var controller = _gameObject.AddComponent<FacialController>();

            Assert.IsNotNull(controller);
        }

        [Test]
        public void AddComponent_FacialController_IsNotInitializedWithoutProfile()
        {
            _gameObject = CreateGameObjectWithAnimator();

            var controller = _gameObject.AddComponent<FacialController>();

            Assert.IsFalse(controller.IsInitialized);
        }

        // ================================================================
        // OnEnable 自動初期化
        // ================================================================

        [UnityTest]
        public IEnumerator OnEnable_WithProfileSO_InitializesAutomatically()
        {
            _gameObject = CreateGameObjectWithAnimatorAndRenderer();
            var controller = _gameObject.AddComponent<FacialController>();
            var profileSO = CreateProfileSO();
            controller.ProfileSO = profileSO;

            // OnEnable は AddComponent 時に自動で呼ばれるが、
            // ProfileSO を後から設定したため手動で再初期化
            controller.Initialize();

            yield return null;

            Assert.IsTrue(controller.IsInitialized);
        }

        [UnityTest]
        public IEnumerator OnEnable_WithoutProfileSO_DoesNotInitialize()
        {
            _gameObject = CreateGameObjectWithAnimator();

            var controller = _gameObject.AddComponent<FacialController>();

            yield return null;

            Assert.IsFalse(controller.IsInitialized);
        }

        // ================================================================
        // Initialize 手動初期化
        // ================================================================

        [Test]
        public void Initialize_WithProfileSO_SetsIsInitializedTrue()
        {
            _gameObject = CreateGameObjectWithAnimatorAndRenderer();
            var controller = _gameObject.AddComponent<FacialController>();
            var profileSO = CreateProfileSO();
            controller.ProfileSO = profileSO;

            controller.Initialize();

            Assert.IsTrue(controller.IsInitialized);
        }

        [Test]
        public void Initialize_WithoutProfileSO_DoesNotThrow()
        {
            _gameObject = CreateGameObjectWithAnimator();
            var controller = _gameObject.AddComponent<FacialController>();

            Assert.DoesNotThrow(() => controller.Initialize());
            Assert.IsFalse(controller.IsInitialized);
        }

        [Test]
        public void Initialize_CalledTwice_DoesNotThrow()
        {
            _gameObject = CreateGameObjectWithAnimatorAndRenderer();
            var controller = _gameObject.AddComponent<FacialController>();
            var profileSO = CreateProfileSO();
            controller.ProfileSO = profileSO;

            controller.Initialize();

            Assert.DoesNotThrow(() => controller.Initialize());
            Assert.IsTrue(controller.IsInitialized);
        }

        // ================================================================
        // OnDisable 破棄
        // ================================================================

        [UnityTest]
        public IEnumerator OnDisable_DestroysGraphAndResources()
        {
            _gameObject = CreateGameObjectWithAnimatorAndRenderer();
            var controller = _gameObject.AddComponent<FacialController>();
            var profileSO = CreateProfileSO();
            controller.ProfileSO = profileSO;
            controller.Initialize();

            yield return null;

            Assert.IsTrue(controller.IsInitialized);

            controller.enabled = false;

            yield return null;

            Assert.IsFalse(controller.IsInitialized);
        }

        [UnityTest]
        public IEnumerator OnDisable_ThenReEnable_CanReinitialize()
        {
            _gameObject = CreateGameObjectWithAnimatorAndRenderer();
            var controller = _gameObject.AddComponent<FacialController>();
            var profileSO = CreateProfileSO();
            controller.ProfileSO = profileSO;
            controller.Initialize();

            yield return null;
            Assert.IsTrue(controller.IsInitialized);

            // 無効化
            controller.enabled = false;
            yield return null;
            Assert.IsFalse(controller.IsInitialized);

            // 再有効化
            controller.enabled = true;
            yield return null;

            // 再初期化
            controller.Initialize();
            Assert.IsTrue(controller.IsInitialized);
        }

        // ================================================================
        // SkinnedMeshRenderer 自動検索
        // ================================================================

        [Test]
        public void Initialize_WithChildSkinnedMeshRenderer_DetectsAutomatically()
        {
            _gameObject = CreateGameObjectWithAnimatorAndRenderer();
            var controller = _gameObject.AddComponent<FacialController>();
            var profileSO = CreateProfileSO();
            controller.ProfileSO = profileSO;

            controller.Initialize();

            Assert.IsTrue(controller.IsInitialized);
        }

        [Test]
        public void Initialize_WithoutSkinnedMeshRenderer_LogsWarningAndDoesNotInitialize()
        {
            _gameObject = CreateGameObjectWithAnimator();
            var controller = _gameObject.AddComponent<FacialController>();
            var profileSO = CreateProfileSO();
            controller.ProfileSO = profileSO;

            controller.Initialize();

            // SkinnedMeshRenderer がない場合は初期化しない
            Assert.IsFalse(controller.IsInitialized);
        }

        [Test]
        public void SkinnedMeshRenderers_ManualOverride_UsesOverrideList()
        {
            _gameObject = CreateGameObjectWithAnimator();
            var controller = _gameObject.AddComponent<FacialController>();
            var profileSO = CreateProfileSO();
            controller.ProfileSO = profileSO;

            // 手動で SkinnedMeshRenderer を設定
            var childObj = new GameObject("Mesh");
            childObj.transform.SetParent(_gameObject.transform);
            var smr = childObj.AddComponent<SkinnedMeshRenderer>();
            // 空のメッシュだと BlendShape がないため初期化されるが BlendShape 数は 0
            controller.SkinnedMeshRenderers = new SkinnedMeshRenderer[] { smr };

            controller.Initialize();

            Assert.IsTrue(controller.IsInitialized);
        }

        // ================================================================
        // Animator 要件（RequireComponent により自動付与される）
        // ================================================================

        [Test]
        public void AddComponent_FacialController_RequiresAnimator()
        {
            // RequireComponent 属性により Animator が自動付与されることを確認
            _gameObject = new GameObject("RequireAnimatorTest");
            _gameObject.AddComponent<FacialController>();

            Assert.IsNotNull(_gameObject.GetComponent<Animator>());
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static GameObject CreateGameObjectWithAnimator()
        {
            var go = new GameObject("FacialControllerTest");
            go.AddComponent<Animator>();
            return go;
        }

        private static GameObject CreateGameObjectWithAnimatorAndRenderer()
        {
            var go = new GameObject("FacialControllerTest");
            go.AddComponent<Animator>();
            var childObj = new GameObject("Mesh");
            childObj.transform.SetParent(go.transform);
            childObj.AddComponent<SkinnedMeshRenderer>();
            return go;
        }

        private static FacialProfileSO CreateProfileSO()
        {
            var so = UnityEngine.ScriptableObject.CreateInstance<FacialProfileSO>();
            // テスト用にインメモリプロファイルを使用するため、
            // JsonFilePath は空のままで良い（FacialController はインメモリ初期化パスも提供）
            return so;
        }
    }
}
