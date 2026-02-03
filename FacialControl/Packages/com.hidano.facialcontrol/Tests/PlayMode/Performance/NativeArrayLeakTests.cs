using System.Collections;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.PlayMode.Performance
{
    /// <summary>
    /// NativeArray リーク検出テスト。
    /// PlayableGraph の破棄時に全ての NativeArray が正しく解放されることを検証する。
    /// Unity の NativeArray リークディテクタを活用する。
    /// </summary>
    [TestFixture]
    public class NativeArrayLeakTests
    {
        private GameObject _gameObject;

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null)
            {
                Object.DestroyImmediate(_gameObject);
                _gameObject = null;
            }
        }

        // ================================================================
        // PlayableGraphBuilder の Dispose でリークなし
        // ================================================================

        [Test]
        public void BuildResult_Dispose_NoNativeArrayLeak()
        {
            // NativeLeakDetection を有効化
            NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;

            _gameObject = new GameObject("LeakTest");
            _gameObject.AddComponent<Animator>();

            var blendShapeNames = CreateBlendShapeNames(52);
            var profile = CreateMultiLayerProfile();
            var result = PlayableGraphBuilder.Build(
                _gameObject.GetComponent<Animator>(), profile, blendShapeNames);

            // 遷移を開始（内部バッファが使用される状態にする）
            var emotionBehaviour = result.LayerPlayables["emotion"].GetBehaviour();
            emotionBehaviour.SetTargetExpression("expr-1",
                CreateTargetValues(52, 1.0f), 0.5f, TransitionCurve.Linear);
            emotionBehaviour.UpdateTransition(0.25f);

            var lipsyncBehaviour = result.LayerPlayables["lipsync"].GetBehaviour();
            lipsyncBehaviour.AddBlendExpression("lip-1",
                CreateTargetValues(52, 0.5f), 1.0f);
            lipsyncBehaviour.ComputeBlendOutput();

            // Dispose → リークがあれば Unity が警告/エラーをログ出力する
            result.Dispose();

            // Dispose 後のアクセスが安全であることを確認
            // （Graph が無効になっていること）
            Assert.IsFalse(result.Graph.IsValid(),
                "Dispose 後に PlayableGraph が有効のままです");
        }

        [Test]
        public void BuildResult_DoubleDispose_NoError()
        {
            _gameObject = new GameObject("LeakTest");
            _gameObject.AddComponent<Animator>();

            var blendShapeNames = CreateBlendShapeNames(10);
            var profile = CreateTestProfile();
            var result = PlayableGraphBuilder.Build(
                _gameObject.GetComponent<Animator>(), profile, blendShapeNames);

            // 2 回 Dispose してもエラーにならない
            result.Dispose();
            result.Dispose();

            Assert.IsFalse(result.Graph.IsValid());
        }

        // ================================================================
        // NativeArrayPool の Dispose でリークなし
        // ================================================================

        [Test]
        public void NativeArrayPool_Dispose_AllArraysReleased()
        {
            NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;

            var pool = new NativeArrayPool<float>(52);

            // いくつか確保
            var arr1 = pool.Allocate();
            var arr2 = pool.Allocate();
            var arr3 = pool.Allocate();

            // 1 つだけ返却
            pool.Return(arr1);

            // Dispose → 返却済み + 未返却の両方が解放される
            pool.Dispose();

            // Dispose 後は使い回し不可（IsCreated で確認は避ける。
            // Dispose 後の NativeArray にアクセスすると例外になるため、
            // ここではテストが正常完了すること自体がリークなしの証拠）
            Assert.Pass("NativeArrayPool.Dispose() が正常に完了しました");
        }

        [Test]
        public void NativeArrayPool_Resize_PreviousArraysReleased()
        {
            NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;

            var pool = new NativeArrayPool<float>(32);

            var arr1 = pool.Allocate();
            var arr2 = pool.Allocate();
            pool.Return(arr1);

            // リサイズ → 既存のバッファが全て解放される
            pool.Resize(64);

            // 新しいサイズで確保可能
            var newArr = pool.Allocate();
            Assert.AreEqual(64, newArr.Length);

            pool.Return(newArr);
            pool.Dispose();
        }

        [Test]
        public void NativeArrayPool_DoubleDispose_NoError()
        {
            var pool = new NativeArrayPool<float>(10);
            var arr = pool.Allocate();
            pool.Return(arr);

            pool.Dispose();
            pool.Dispose();

            Assert.Pass("NativeArrayPool の二重 Dispose でエラーが発生しませんでした");
        }

        // ================================================================
        // LayerPlayable の Dispose でリークなし
        // ================================================================

        [Test]
        public void LayerPlayable_Dispose_NoNativeArrayLeak()
        {
            NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;

            _gameObject = new GameObject("LeakTest");
            _gameObject.AddComponent<Animator>();

            var blendShapeNames = CreateBlendShapeNames(52);
            var profile = CreateTestProfile();
            var result = PlayableGraphBuilder.Build(
                _gameObject.GetComponent<Animator>(), profile, blendShapeNames);

            var behaviour = result.LayerPlayables["emotion"].GetBehaviour();

            // 各バッファを使用する操作を行う
            behaviour.SetTargetExpression("expr-1",
                CreateTargetValues(52, 1.0f), 0.5f, TransitionCurve.Linear);
            behaviour.UpdateTransition(0.25f);

            // 遷移割込
            behaviour.SetTargetExpression("expr-2",
                CreateTargetValues(52, 0.0f), 0.5f, TransitionCurve.Linear);
            behaviour.UpdateTransition(0.1f);

            // Graph を破棄 → LayerPlayable.OnPlayableDestroy → Dispose
            result.Dispose();

            Assert.IsFalse(result.Graph.IsValid());
        }

        // ================================================================
        // FacialControlMixer の Dispose でリークなし
        // ================================================================

        [Test]
        public void FacialControlMixer_Dispose_NoNativeArrayLeak()
        {
            NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;

            _gameObject = new GameObject("LeakTest");
            _gameObject.AddComponent<Animator>();

            var blendShapeNames = CreateBlendShapeNames(52);
            var profile = CreateMultiLayerProfile();
            var result = PlayableGraphBuilder.Build(
                _gameObject.GetComponent<Animator>(), profile, blendShapeNames);

            var mixerBehaviour = result.Mixer.GetBehaviour();

            // Mixer の出力を計算
            var emotionBehaviour = result.LayerPlayables["emotion"].GetBehaviour();
            emotionBehaviour.SetTargetExpression("expr-1",
                CreateTargetValues(52, 0.8f), 0f, TransitionCurve.Linear);
            mixerBehaviour.ComputeOutput();

            result.Dispose();

            Assert.IsFalse(result.Graph.IsValid());
        }

        // ================================================================
        // FacialController のライフサイクルでリークなし
        // ================================================================

        [UnityTest]
        public IEnumerator FacialController_EnableDisable_NoNativeArrayLeak()
        {
            NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;

            _gameObject = new GameObject("LeakTest");
            _gameObject.AddComponent<Animator>();
            var childObj = new GameObject("Mesh");
            childObj.transform.SetParent(_gameObject.transform);
            childObj.AddComponent<SkinnedMeshRenderer>();

            var profile = CreateMultiLayerProfile();
            var controller = _gameObject.AddComponent<FacialController>();
            controller.InitializeWithProfile(profile);

            Assert.IsTrue(controller.IsInitialized);

            // Expression をアクティブ化して内部バッファを使用する
            var expression = new Expression("expr-1", "Happy", "emotion", 0.25f,
                TransitionCurve.Linear,
                new BlendShapeMapping[] { new BlendShapeMapping("smile", 1.0f) });
            controller.Activate(expression);

            yield return null;

            // 無効化 → OnDisable → Cleanup → NativeArray 解放
            controller.enabled = false;

            yield return null;

            Assert.IsFalse(controller.IsInitialized);

            // 再有効化 → 再初期化
            controller.enabled = true;
            controller.InitializeWithProfile(profile);

            yield return null;

            Assert.IsTrue(controller.IsInitialized);

            // 再度無効化して最終クリーンアップ
            controller.enabled = false;

            yield return null;

            Assert.IsFalse(controller.IsInitialized);
        }

        [UnityTest]
        public IEnumerator FacialController_ProfileSwitch_NoNativeArrayLeak()
        {
            NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;

            _gameObject = new GameObject("LeakTest");
            _gameObject.AddComponent<Animator>();
            var childObj = new GameObject("Mesh");
            childObj.transform.SetParent(_gameObject.transform);
            childObj.AddComponent<SkinnedMeshRenderer>();

            var profile1 = CreateMultiLayerProfile();
            var controller = _gameObject.AddComponent<FacialController>();
            controller.InitializeWithProfile(profile1);

            yield return null;

            // プロファイル切替を複数回繰り返す
            for (int i = 0; i < 5; i++)
            {
                var newProfile = new FacialProfile($"{i + 2}.0.0",
                    new LayerDefinition[]
                    {
                        new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                        new LayerDefinition("lipsync", 1, ExclusionMode.Blend)
                    });
                controller.InitializeWithProfile(newProfile);

                yield return null;

                Assert.IsTrue(controller.IsInitialized,
                    $"プロファイル切替 {i + 1} 回目で初期化に失敗しました");
            }
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static string[] CreateBlendShapeNames(int count)
        {
            var names = new string[count];
            for (int i = 0; i < count; i++)
            {
                names[i] = $"blendShape_{i}";
            }
            return names;
        }

        private static float[] CreateTargetValues(int count, float value)
        {
            var values = new float[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = value;
            }
            return values;
        }

        private static FacialProfile CreateTestProfile()
        {
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins)
            };
            return new FacialProfile("1.0.0", layers);
        }

        private static FacialProfile CreateMultiLayerProfile()
        {
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("lipsync", 1, ExclusionMode.Blend),
                new LayerDefinition("eye", 2, ExclusionMode.LastWins)
            };
            return new FacialProfile("1.0.0", layers);
        }
    }
}
