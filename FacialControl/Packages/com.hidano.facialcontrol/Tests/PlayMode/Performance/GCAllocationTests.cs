using System;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.PlayMode.Performance
{
    /// <summary>
    /// P14-T05: GC アロケーションテスト。
    /// 毎フレーム処理と遷移割込処理で GC が発生しないことを検証する。
    /// </summary>
    [TestFixture]
    public class GCAllocationTests
    {
        private GameObject _gameObject;
        private PlayableGraph _graph;

        [TearDown]
        public void TearDown()
        {
            if (_graph.IsValid())
            {
                _graph.Destroy();
            }
            if (_gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_gameObject);
                _gameObject = null;
            }
        }

        // ================================================================
        // 毎フレーム GC ゼロの検証
        // ================================================================

        [Test]
        public void UpdateTransition_PerFrame_ZeroGCAllocation()
        {
            SetUpGraph();
            var blendShapeNames = CreateBlendShapeNames(52); // ARKit 52 パラメータ相当
            var profile = CreateTestProfile(ExclusionMode.LastWins);
            var result = PlayableGraphBuilder.Build(
                _gameObject.GetComponent<Animator>(), profile, blendShapeNames);

            var behaviour = result.LayerPlayables["emotion"].GetBehaviour();
            var targetValues = CreateTargetValues(52, 0.8f);
            behaviour.SetTargetExpression("expr-1", targetValues, 1.0f, TransitionCurve.Linear);

            // ウォームアップ（JIT 等を排除）
            behaviour.UpdateTransition(0.01f);

            // GC 計測
            long allocBefore = GC.GetTotalMemory(false);
            for (int frame = 0; frame < 100; frame++)
            {
                behaviour.UpdateTransition(0.001f);
            }
            long allocAfter = GC.GetTotalMemory(false);

            // GC が発生していないことを検証（許容範囲: 0 バイト）
            // 注意: GC.GetTotalMemory は正確ではないが、大きなアロケーションは検出できる
            long allocated = allocAfter - allocBefore;
            Assert.LessOrEqual(allocated, 0,
                $"UpdateTransition で GC アロケーションが検出されました: {allocated} bytes");

            result.Dispose();
        }

        [Test]
        public void UpdateTransition_PerFrame_ZeroGCAllocation_Profiler()
        {
            SetUpGraph();
            var blendShapeNames = CreateBlendShapeNames(52);
            var profile = CreateTestProfile(ExclusionMode.LastWins);
            var result = PlayableGraphBuilder.Build(
                _gameObject.GetComponent<Animator>(), profile, blendShapeNames);

            var behaviour = result.LayerPlayables["emotion"].GetBehaviour();
            var targetValues = CreateTargetValues(52, 0.8f);
            behaviour.SetTargetExpression("expr-1", targetValues, 1.0f, TransitionCurve.Linear);

            // ウォームアップ
            behaviour.UpdateTransition(0.01f);

            // Unity Profiler 用 GC 計測
            var recorder = Unity.Profiling.ProfilerRecorder.StartNew(
                Unity.Profiling.ProfilerCategory.Memory, "GC.Alloc");

            for (int frame = 0; frame < 60; frame++)
            {
                behaviour.UpdateTransition(0.016f);
            }

            long gcAlloc = recorder.LastValue;
            recorder.Dispose();

            Assert.AreEqual(0, gcAlloc,
                $"UpdateTransition で GC アロケーションが検出されました: {gcAlloc} bytes");

            result.Dispose();
        }

        [Test]
        public void ComputeBlendOutput_PerFrame_ZeroGCAllocation()
        {
            SetUpGraph();
            var blendShapeNames = CreateBlendShapeNames(52);
            var profile = CreateTestProfile(ExclusionMode.Blend);
            var result = PlayableGraphBuilder.Build(
                _gameObject.GetComponent<Animator>(), profile, blendShapeNames);

            var behaviour = result.LayerPlayables["emotion"].GetBehaviour();

            // 複数 Expression を Blend モードで追加
            behaviour.AddBlendExpression("lip-a", CreateTargetValues(52, 0.5f), 1.0f);
            behaviour.AddBlendExpression("lip-o", CreateTargetValues(52, 0.3f), 1.0f);
            behaviour.AddBlendExpression("lip-e", CreateTargetValues(52, 0.2f), 1.0f);

            // ウォームアップ
            behaviour.ComputeBlendOutput();

            // GC 計測
            long allocBefore = GC.GetTotalMemory(false);
            for (int frame = 0; frame < 100; frame++)
            {
                behaviour.ComputeBlendOutput();
            }
            long allocAfter = GC.GetTotalMemory(false);

            long allocated = allocAfter - allocBefore;
            Assert.LessOrEqual(allocated, 0,
                $"ComputeBlendOutput で GC アロケーションが検出されました: {allocated} bytes");

            result.Dispose();
        }

        // ================================================================
        // 遷移割込時 GC ゼロの検証
        // ================================================================

        [Test]
        public void TransitionInterrupt_ZeroGCAllocation()
        {
            SetUpGraph();
            var blendShapeNames = CreateBlendShapeNames(52);
            var profile = CreateTestProfile(ExclusionMode.LastWins);
            var result = PlayableGraphBuilder.Build(
                _gameObject.GetComponent<Animator>(), profile, blendShapeNames);

            var behaviour = result.LayerPlayables["emotion"].GetBehaviour();

            // 初回遷移設定
            var targetA = CreateTargetValues(52, 1.0f);
            var targetB = CreateTargetValues(52, 0.0f);
            behaviour.SetTargetExpression("expr-a", targetA, 1.0f, TransitionCurve.Linear);
            behaviour.UpdateTransition(0.5f);

            // ウォームアップ: 割込を 1 回実行
            behaviour.SetTargetExpression("expr-warmup", targetB, 1.0f, TransitionCurve.Linear);
            behaviour.UpdateTransition(0.01f);

            // GC 計測: 遷移割込を繰り返す
            long allocBefore = GC.GetTotalMemory(false);
            for (int i = 0; i < 50; i++)
            {
                behaviour.UpdateTransition(0.05f);
                // 遷移割込（スナップショット → 新遷移開始）
                behaviour.SetTargetExpression(
                    i % 2 == 0 ? "expr-a" : "expr-b",
                    i % 2 == 0 ? targetA : targetB,
                    1.0f,
                    TransitionCurve.Linear);
            }
            long allocAfter = GC.GetTotalMemory(false);

            long allocated = allocAfter - allocBefore;
            Assert.LessOrEqual(allocated, 0,
                $"遷移割込で GC アロケーションが検出されました: {allocated} bytes");

            result.Dispose();
        }

        [Test]
        public void TransitionInterrupt_ZeroGCAllocation_Profiler()
        {
            SetUpGraph();
            var blendShapeNames = CreateBlendShapeNames(52);
            var profile = CreateTestProfile(ExclusionMode.LastWins);
            var result = PlayableGraphBuilder.Build(
                _gameObject.GetComponent<Animator>(), profile, blendShapeNames);

            var behaviour = result.LayerPlayables["emotion"].GetBehaviour();
            var targetA = CreateTargetValues(52, 1.0f);
            var targetB = CreateTargetValues(52, 0.0f);

            // ウォームアップ
            behaviour.SetTargetExpression("expr-a", targetA, 1.0f, TransitionCurve.Linear);
            behaviour.UpdateTransition(0.5f);
            behaviour.SetTargetExpression("expr-b", targetB, 1.0f, TransitionCurve.Linear);
            behaviour.UpdateTransition(0.01f);

            // Profiler で GC 計測
            var recorder = Unity.Profiling.ProfilerRecorder.StartNew(
                Unity.Profiling.ProfilerCategory.Memory, "GC.Alloc");

            for (int i = 0; i < 30; i++)
            {
                behaviour.UpdateTransition(0.05f);
                behaviour.SetTargetExpression(
                    i % 2 == 0 ? "expr-a" : "expr-b",
                    i % 2 == 0 ? targetA : targetB,
                    1.0f,
                    TransitionCurve.Linear);
            }

            long gcAlloc = recorder.LastValue;
            recorder.Dispose();

            Assert.AreEqual(0, gcAlloc,
                $"遷移割込で GC アロケーションが検出されました: {gcAlloc} bytes");

            result.Dispose();
        }

        // ================================================================
        // NativeArray 操作の GC ゼロ検証
        // ================================================================

        [Test]
        public void NativeArrayCopy_ZeroGCAllocation()
        {
            var arrayA = new NativeArray<float>(52, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            var arrayB = new NativeArray<float>(52, Allocator.Persistent, NativeArrayOptions.ClearMemory);

            // ウォームアップ
            arrayA.CopyTo(arrayB);

            long allocBefore = GC.GetTotalMemory(false);
            for (int i = 0; i < 1000; i++)
            {
                arrayA.CopyTo(arrayB);
            }
            long allocAfter = GC.GetTotalMemory(false);

            arrayA.Dispose();
            arrayB.Dispose();

            long allocated = allocAfter - allocBefore;
            Assert.LessOrEqual(allocated, 0,
                $"NativeArray.CopyTo で GC アロケーションが検出されました: {allocated} bytes");
        }

        [Test]
        public void NativeArrayElementAccess_ZeroGCAllocation()
        {
            var array = new NativeArray<float>(52, Allocator.Persistent, NativeArrayOptions.ClearMemory);

            // ウォームアップ
            for (int i = 0; i < 52; i++)
            {
                array[i] = i * 0.1f;
            }

            long allocBefore = GC.GetTotalMemory(false);
            for (int frame = 0; frame < 100; frame++)
            {
                for (int i = 0; i < 52; i++)
                {
                    float val = array[i];
                    array[i] = val * 0.99f;
                }
            }
            long allocAfter = GC.GetTotalMemory(false);

            array.Dispose();

            long allocated = allocAfter - allocBefore;
            Assert.LessOrEqual(allocated, 0,
                $"NativeArray 要素アクセスで GC アロケーションが検出されました: {allocated} bytes");
        }

        // ================================================================
        // NativeArrayPool の GC ゼロ検証
        // ================================================================

        [Test]
        public void NativeArrayPool_AllocateAndReturn_ZeroGCAfterWarmup()
        {
            var pool = new NativeArrayPool<float>(52);

            // ウォームアップ: プールに事前確保
            var warmup1 = pool.Allocate();
            var warmup2 = pool.Allocate();
            pool.Return(warmup1);
            pool.Return(warmup2);

            // GC 計測: プールからの再利用のみ
            long allocBefore = GC.GetTotalMemory(false);
            for (int i = 0; i < 100; i++)
            {
                var arr = pool.Allocate();
                pool.Return(arr);
            }
            long allocAfter = GC.GetTotalMemory(false);

            pool.Dispose();

            long allocated = allocAfter - allocBefore;
            Assert.LessOrEqual(allocated, 0,
                $"NativeArrayPool の再利用で GC アロケーションが検出されました: {allocated} bytes");
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private void SetUpGraph()
        {
            _gameObject = new GameObject("GCAllocationTest");
            _gameObject.AddComponent<Animator>();
        }

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

        private static FacialProfile CreateTestProfile(ExclusionMode mode)
        {
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, mode)
            };
            return new FacialProfile("1.0.0", layers);
        }
    }
}
