using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.PlayMode.Adapters
{
    /// <summary>
    /// P06-T04: PlayableGraphBuilder の PlayMode テスト。
    /// FacialProfile からの PlayableGraph 構築、レイヤー分の LayerPlayable ノード配置、
    /// 既存 Animator への接続を検証する。
    /// </summary>
    [TestFixture]
    public class PlayableGraphBuilderTests
    {
        private GameObject _gameObject;
        private Animator _animator;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject("PlayableGraphBuilderTest");
            _animator = _gameObject.AddComponent<Animator>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_gameObject);
            }
        }

        // ================================================================
        // 基本構築
        // ================================================================

        [Test]
        public void Build_ValidProfile_ReturnsNonNullResult()
        {
            var profile = CreateSimpleProfile();
            var blendShapeNames = new string[] { "BlendA", "BlendB" };

            var result = PlayableGraphBuilder.Build(_animator, profile, blendShapeNames);

            Assert.IsNotNull(result);
            result.Dispose();
        }

        [Test]
        public void Build_ValidProfile_GraphIsValid()
        {
            var profile = CreateSimpleProfile();
            var blendShapeNames = new string[] { "BlendA", "BlendB" };

            var result = PlayableGraphBuilder.Build(_animator, profile, blendShapeNames);

            Assert.IsTrue(result.Graph.IsValid());
            result.Dispose();
        }

        [Test]
        public void Build_ValidProfile_MixerIsValid()
        {
            var profile = CreateSimpleProfile();
            var blendShapeNames = new string[] { "BlendA", "BlendB" };

            var result = PlayableGraphBuilder.Build(_animator, profile, blendShapeNames);

            Assert.IsTrue(result.Mixer.IsValid());
            result.Dispose();
        }

        // ================================================================
        // レイヤー構築
        // ================================================================

        [Test]
        public void Build_SingleLayer_CreatesOneLayerPlayable()
        {
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins)
            };
            var profile = new FacialProfile("1.0.0", layers);
            var blendShapeNames = new string[] { "BlendA" };

            var result = PlayableGraphBuilder.Build(_animator, profile, blendShapeNames);

            Assert.AreEqual(1, result.LayerPlayables.Count);
            Assert.IsTrue(result.LayerPlayables.ContainsKey("emotion"));
            result.Dispose();
        }

        [Test]
        public void Build_MultipleLayers_CreatesMultipleLayerPlayables()
        {
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("lipsync", 1, ExclusionMode.Blend),
                new LayerDefinition("eye", 2, ExclusionMode.LastWins)
            };
            var profile = new FacialProfile("1.0.0", layers);
            var blendShapeNames = new string[] { "BlendA", "BlendB" };

            var result = PlayableGraphBuilder.Build(_animator, profile, blendShapeNames);

            Assert.AreEqual(3, result.LayerPlayables.Count);
            Assert.IsTrue(result.LayerPlayables.ContainsKey("emotion"));
            Assert.IsTrue(result.LayerPlayables.ContainsKey("lipsync"));
            Assert.IsTrue(result.LayerPlayables.ContainsKey("eye"));
            result.Dispose();
        }

        [Test]
        public void Build_LayerPlayable_HasCorrectExclusionMode()
        {
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("lipsync", 1, ExclusionMode.Blend)
            };
            var profile = new FacialProfile("1.0.0", layers);
            var blendShapeNames = new string[] { "BlendA" };

            var result = PlayableGraphBuilder.Build(_animator, profile, blendShapeNames);

            var emotionBehaviour = result.LayerPlayables["emotion"].GetBehaviour();
            var lipsyncBehaviour = result.LayerPlayables["lipsync"].GetBehaviour();

            Assert.AreEqual(ExclusionMode.LastWins, emotionBehaviour.ExclusionMode);
            Assert.AreEqual(ExclusionMode.Blend, lipsyncBehaviour.ExclusionMode);
            result.Dispose();
        }

        [Test]
        public void Build_LayerPlayable_HasCorrectBlendShapeCount()
        {
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins)
            };
            var profile = new FacialProfile("1.0.0", layers);
            var blendShapeNames = new string[] { "BlendA", "BlendB", "BlendC" };

            var result = PlayableGraphBuilder.Build(_animator, profile, blendShapeNames);

            var behaviour = result.LayerPlayables["emotion"].GetBehaviour();
            Assert.AreEqual(3, behaviour.BlendShapeCount);
            result.Dispose();
        }

        // ================================================================
        // Mixer の登録
        // ================================================================

        [Test]
        public void Build_MixerLayerCount_MatchesProfileLayers()
        {
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("lipsync", 1, ExclusionMode.Blend)
            };
            var profile = new FacialProfile("1.0.0", layers);
            var blendShapeNames = new string[] { "BlendA", "BlendB" };

            var result = PlayableGraphBuilder.Build(_animator, profile, blendShapeNames);

            var mixerBehaviour = result.Mixer.GetBehaviour();
            Assert.AreEqual(2, mixerBehaviour.LayerCount);
            result.Dispose();
        }

        [Test]
        public void Build_MixerBlendShapeCount_MatchesBlendShapeNames()
        {
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins)
            };
            var profile = new FacialProfile("1.0.0", layers);
            var blendShapeNames = new string[] { "BlendA", "BlendB", "BlendC", "BlendD" };

            var result = PlayableGraphBuilder.Build(_animator, profile, blendShapeNames);

            var mixerBehaviour = result.Mixer.GetBehaviour();
            Assert.AreEqual(4, mixerBehaviour.BlendShapeCount);
            result.Dispose();
        }

        // ================================================================
        // Animator 接続
        // ================================================================

        [Test]
        public void Build_GraphOutputCount_IsOne()
        {
            var profile = CreateSimpleProfile();
            var blendShapeNames = new string[] { "BlendA" };

            var result = PlayableGraphBuilder.Build(_animator, profile, blendShapeNames);

            Assert.AreEqual(1, result.Graph.GetOutputCount());
            result.Dispose();
        }

        // ================================================================
        // プロファイルなし / 空レイヤー
        // ================================================================

        [Test]
        public void Build_EmptyLayers_CreatesGraphWithNoLayerPlayables()
        {
            var profile = new FacialProfile("1.0.0", Array.Empty<LayerDefinition>());
            var blendShapeNames = new string[] { "BlendA" };

            var result = PlayableGraphBuilder.Build(_animator, profile, blendShapeNames);

            Assert.AreEqual(0, result.LayerPlayables.Count);
            var mixerBehaviour = result.Mixer.GetBehaviour();
            Assert.AreEqual(0, mixerBehaviour.LayerCount);
            result.Dispose();
        }

        [Test]
        public void Build_EmptyBlendShapeNames_CreatesGraphWithZeroBlendShapes()
        {
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins)
            };
            var profile = new FacialProfile("1.0.0", layers);
            var blendShapeNames = Array.Empty<string>();

            var result = PlayableGraphBuilder.Build(_animator, profile, blendShapeNames);

            var mixerBehaviour = result.Mixer.GetBehaviour();
            Assert.AreEqual(0, mixerBehaviour.BlendShapeCount);
            result.Dispose();
        }

        // ================================================================
        // 引数バリデーション
        // ================================================================

        [Test]
        public void Build_NullAnimator_ThrowsArgumentNullException()
        {
            var profile = CreateSimpleProfile();
            var blendShapeNames = new string[] { "BlendA" };

            Assert.Throws<ArgumentNullException>(() =>
                PlayableGraphBuilder.Build(null, profile, blendShapeNames));
        }

        [Test]
        public void Build_NullBlendShapeNames_ThrowsArgumentNullException()
        {
            var profile = CreateSimpleProfile();

            Assert.Throws<ArgumentNullException>(() =>
                PlayableGraphBuilder.Build(_animator, profile, null));
        }

        // ================================================================
        // Dispose
        // ================================================================

        [Test]
        public void Dispose_DestroysGraph()
        {
            var profile = CreateSimpleProfile();
            var blendShapeNames = new string[] { "BlendA" };

            var result = PlayableGraphBuilder.Build(_animator, profile, blendShapeNames);
            var graph = result.Graph;

            result.Dispose();

            Assert.IsFalse(graph.IsValid());
        }

        [Test]
        public void Dispose_CalledTwice_DoesNotThrow()
        {
            var profile = CreateSimpleProfile();
            var blendShapeNames = new string[] { "BlendA" };

            var result = PlayableGraphBuilder.Build(_animator, profile, blendShapeNames);
            result.Dispose();

            Assert.DoesNotThrow(() => result.Dispose());
        }

        // ================================================================
        // 日本語レイヤー名
        // ================================================================

        [Test]
        public void Build_JapaneseLayerNames_CreatesLayerPlayables()
        {
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("感情", 0, ExclusionMode.LastWins),
                new LayerDefinition("口パク", 1, ExclusionMode.Blend)
            };
            var profile = new FacialProfile("1.0.0", layers);
            var blendShapeNames = new string[] { "まばたき", "笑顔" };

            var result = PlayableGraphBuilder.Build(_animator, profile, blendShapeNames);

            Assert.AreEqual(2, result.LayerPlayables.Count);
            Assert.IsTrue(result.LayerPlayables.ContainsKey("感情"));
            Assert.IsTrue(result.LayerPlayables.ContainsKey("口パク"));
            result.Dispose();
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static FacialProfile CreateSimpleProfile()
        {
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins)
            };
            return new FacialProfile("1.0.0", layers);
        }
    }
}
