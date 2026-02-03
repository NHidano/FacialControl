using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.PlayMode.Performance
{
    /// <summary>
    /// P14-T06: 10 体同時制御のパフォーマンステスト。
    /// 複数キャラクターの同時 PlayableGraph 構築・遷移更新でフレームレートが維持されることを検証する。
    /// </summary>
    [TestFixture]
    public class MultiCharacterPerformanceTests
    {
        private readonly List<GameObject> _gameObjects = new List<GameObject>();
        private readonly List<PlayableGraphBuilder.BuildResult> _buildResults =
            new List<PlayableGraphBuilder.BuildResult>();

        private const int CharacterCount = 10;
        private const int BlendShapeCount = 52; // ARKit 52 パラメータ相当

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _buildResults.Count; i++)
            {
                _buildResults[i]?.Dispose();
            }
            _buildResults.Clear();

            for (int i = 0; i < _gameObjects.Count; i++)
            {
                if (_gameObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_gameObjects[i]);
                }
            }
            _gameObjects.Clear();
        }

        // ================================================================
        // 10 体同時制御テスト
        // ================================================================

        [Test]
        public void TenCharacters_SimultaneousGraphBuild_AllValid()
        {
            var blendShapeNames = CreateBlendShapeNames(BlendShapeCount);
            var profile = CreateMultiLayerProfile();

            for (int i = 0; i < CharacterCount; i++)
            {
                var go = new GameObject($"Character_{i}");
                go.AddComponent<Animator>();
                _gameObjects.Add(go);

                var result = PlayableGraphBuilder.Build(
                    go.GetComponent<Animator>(), profile, blendShapeNames);
                _buildResults.Add(result);

                Assert.IsTrue(result.Graph.IsValid(),
                    $"Character_{i} の PlayableGraph が無効です");
            }

            Assert.AreEqual(CharacterCount, _buildResults.Count);
        }

        [Test]
        public void TenCharacters_SimultaneousTransitionUpdate_CompletesWithinBudget()
        {
            var blendShapeNames = CreateBlendShapeNames(BlendShapeCount);
            var profile = CreateMultiLayerProfile();
            var targetValues = CreateTargetValues(BlendShapeCount, 0.8f);

            // 10 体分のグラフを構築
            var behaviours = new List<LayerPlayable>();
            for (int i = 0; i < CharacterCount; i++)
            {
                var go = new GameObject($"Character_{i}");
                go.AddComponent<Animator>();
                _gameObjects.Add(go);

                var result = PlayableGraphBuilder.Build(
                    go.GetComponent<Animator>(), profile, blendShapeNames);
                _buildResults.Add(result);

                // 各キャラクターの emotion レイヤーで遷移を開始
                var behaviour = result.LayerPlayables["emotion"].GetBehaviour();
                behaviour.SetTargetExpression($"expr-{i}", targetValues, 1.0f, TransitionCurve.Linear);
                behaviours.Add(behaviour);
            }

            // ウォームアップ
            for (int i = 0; i < behaviours.Count; i++)
            {
                behaviours[i].UpdateTransition(0.001f);
            }

            // パフォーマンス計測: 60 フレーム分の遷移更新
            var sw = Stopwatch.StartNew();
            for (int frame = 0; frame < 60; frame++)
            {
                float deltaTime = 0.016f; // 60fps 相当
                for (int i = 0; i < behaviours.Count; i++)
                {
                    behaviours[i].UpdateTransition(deltaTime);
                }
            }
            sw.Stop();

            double msPerFrame = sw.Elapsed.TotalMilliseconds / 60.0;

            // 10 体の遷移更新が 1 フレームあたり 2ms 以内に収まること
            // （16.67ms フレームバジェットの約 12%）
            Assert.Less(msPerFrame, 2.0,
                $"10 体同時遷移更新が 1 フレームあたり {msPerFrame:F3}ms かかりました（上限 2ms）");
        }

        [Test]
        public void TenCharacters_SimultaneousTransitionInterrupt_CompletesWithinBudget()
        {
            var blendShapeNames = CreateBlendShapeNames(BlendShapeCount);
            var profile = CreateMultiLayerProfile();
            var targetA = CreateTargetValues(BlendShapeCount, 1.0f);
            var targetB = CreateTargetValues(BlendShapeCount, 0.0f);

            // 10 体分のグラフを構築
            var behaviours = new List<LayerPlayable>();
            for (int i = 0; i < CharacterCount; i++)
            {
                var go = new GameObject($"Character_{i}");
                go.AddComponent<Animator>();
                _gameObjects.Add(go);

                var result = PlayableGraphBuilder.Build(
                    go.GetComponent<Animator>(), profile, blendShapeNames);
                _buildResults.Add(result);

                var behaviour = result.LayerPlayables["emotion"].GetBehaviour();
                behaviour.SetTargetExpression($"expr-{i}", targetA, 1.0f, TransitionCurve.Linear);
                behaviours.Add(behaviour);
            }

            // ウォームアップ
            for (int i = 0; i < behaviours.Count; i++)
            {
                behaviours[i].UpdateTransition(0.5f);
            }

            // パフォーマンス計測: 60 フレーム分の遷移割込
            var sw = Stopwatch.StartNew();
            for (int frame = 0; frame < 60; frame++)
            {
                float deltaTime = 0.016f;
                for (int i = 0; i < behaviours.Count; i++)
                {
                    behaviours[i].UpdateTransition(deltaTime);
                    // 毎フレーム遷移割込
                    behaviours[i].SetTargetExpression(
                        $"expr-{frame}-{i}",
                        frame % 2 == 0 ? targetA : targetB,
                        0.5f,
                        TransitionCurve.Linear);
                }
            }
            sw.Stop();

            double msPerFrame = sw.Elapsed.TotalMilliseconds / 60.0;

            // 10 体の遷移割込が 1 フレームあたり 2ms 以内
            Assert.Less(msPerFrame, 2.0,
                $"10 体同時遷移割込が 1 フレームあたり {msPerFrame:F3}ms かかりました（上限 2ms）");
        }

        [Test]
        public void TenCharacters_MultiLayerUpdate_CompletesWithinBudget()
        {
            var blendShapeNames = CreateBlendShapeNames(BlendShapeCount);
            var profile = CreateMultiLayerProfile();
            var targetValues = CreateTargetValues(BlendShapeCount, 0.6f);

            // 10 体分のグラフを構築し、3 レイヤー全てに遷移を設定
            var allBehaviours = new List<List<LayerPlayable>>();
            for (int i = 0; i < CharacterCount; i++)
            {
                var go = new GameObject($"Character_{i}");
                go.AddComponent<Animator>();
                _gameObjects.Add(go);

                var result = PlayableGraphBuilder.Build(
                    go.GetComponent<Animator>(), profile, blendShapeNames);
                _buildResults.Add(result);

                var charBehaviours = new List<LayerPlayable>();
                foreach (var kvp in result.LayerPlayables)
                {
                    var behaviour = kvp.Value.GetBehaviour();
                    if (behaviour.ExclusionMode == ExclusionMode.LastWins)
                    {
                        behaviour.SetTargetExpression($"expr-{kvp.Key}-{i}",
                            targetValues, 1.0f, TransitionCurve.Linear);
                    }
                    else
                    {
                        behaviour.AddBlendExpression($"blend-{kvp.Key}-{i}",
                            targetValues, 1.0f);
                    }
                    charBehaviours.Add(behaviour);
                }
                allBehaviours.Add(charBehaviours);
            }

            // ウォームアップ
            for (int i = 0; i < allBehaviours.Count; i++)
            {
                for (int j = 0; j < allBehaviours[i].Count; j++)
                {
                    allBehaviours[i][j].UpdateTransition(0.001f);
                }
            }

            // パフォーマンス計測: 60 フレーム分の全レイヤー更新
            var sw = Stopwatch.StartNew();
            for (int frame = 0; frame < 60; frame++)
            {
                float deltaTime = 0.016f;
                for (int i = 0; i < allBehaviours.Count; i++)
                {
                    for (int j = 0; j < allBehaviours[i].Count; j++)
                    {
                        var behaviour = allBehaviours[i][j];
                        if (behaviour.ExclusionMode == ExclusionMode.LastWins)
                        {
                            behaviour.UpdateTransition(deltaTime);
                        }
                        else
                        {
                            behaviour.ComputeBlendOutput();
                        }
                    }
                }
            }
            sw.Stop();

            double msPerFrame = sw.Elapsed.TotalMilliseconds / 60.0;

            // 10 体 × 3 レイヤー = 30 レイヤー更新が 1 フレームあたり 3ms 以内
            Assert.Less(msPerFrame, 3.0,
                $"10 体 3 レイヤー同時更新が 1 フレームあたり {msPerFrame:F3}ms かかりました（上限 3ms）");
        }

        [Test]
        public void TenCharacters_GCZeroDuringUpdate()
        {
            var blendShapeNames = CreateBlendShapeNames(BlendShapeCount);
            var profile = CreateMultiLayerProfile();
            var targetValues = CreateTargetValues(BlendShapeCount, 0.8f);

            // 10 体分のグラフを構築
            var behaviours = new List<LayerPlayable>();
            for (int i = 0; i < CharacterCount; i++)
            {
                var go = new GameObject($"Character_{i}");
                go.AddComponent<Animator>();
                _gameObjects.Add(go);

                var result = PlayableGraphBuilder.Build(
                    go.GetComponent<Animator>(), profile, blendShapeNames);
                _buildResults.Add(result);

                var behaviour = result.LayerPlayables["emotion"].GetBehaviour();
                behaviour.SetTargetExpression($"expr-{i}", targetValues, 1.0f, TransitionCurve.Linear);
                behaviours.Add(behaviour);
            }

            // ウォームアップ
            for (int i = 0; i < behaviours.Count; i++)
            {
                behaviours[i].UpdateTransition(0.001f);
            }

            // GC 計測
            long allocBefore = GC.GetTotalMemory(false);
            for (int frame = 0; frame < 60; frame++)
            {
                float deltaTime = 0.016f;
                for (int i = 0; i < behaviours.Count; i++)
                {
                    behaviours[i].UpdateTransition(deltaTime);
                }
            }
            long allocAfter = GC.GetTotalMemory(false);

            long allocated = allocAfter - allocBefore;
            Assert.LessOrEqual(allocated, 0,
                $"10 体同時更新で GC アロケーションが検出されました: {allocated} bytes");
        }

        [UnityTest]
        public IEnumerator TenCharacters_FacialController_EndToEnd_AllInitialized()
        {
            var profile = CreateMultiLayerProfile();

            for (int i = 0; i < CharacterCount; i++)
            {
                var go = new GameObject($"Character_{i}");
                go.AddComponent<Animator>();
                var childObj = new GameObject("Mesh");
                childObj.transform.SetParent(go.transform);
                childObj.AddComponent<SkinnedMeshRenderer>();
                _gameObjects.Add(go);

                var controller = go.AddComponent<FacialController>();
                controller.InitializeWithProfile(profile);

                Assert.IsTrue(controller.IsInitialized,
                    $"Character_{i} の FacialController が初期化されていません");
            }

            yield return null;

            // 全キャラクターに Expression をアクティブ化
            for (int i = 0; i < _gameObjects.Count; i++)
            {
                var controller = _gameObjects[i].GetComponent<FacialController>();
                var expression = new Expression(
                    $"expr-{i}", $"Happy_{i}", "emotion", 0.25f,
                    TransitionCurve.Linear,
                    new BlendShapeMapping[] { new BlendShapeMapping("smile", 0.8f) });
                controller.Activate(expression);

                var active = controller.GetActiveExpressions();
                Assert.AreEqual(1, active.Count,
                    $"Character_{i} のアクティブ Expression 数が正しくありません");
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
