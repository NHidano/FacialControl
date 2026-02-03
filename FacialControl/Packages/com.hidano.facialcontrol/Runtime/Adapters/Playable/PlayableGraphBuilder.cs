using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Adapters.Playable
{
    /// <summary>
    /// FacialProfile から PlayableGraph を構築するビルダー。
    /// レイヤー分の LayerPlayable ノードを配置し、FacialControlMixer をルートに接続する。
    /// </summary>
    public static class PlayableGraphBuilder
    {
        /// <summary>
        /// FacialProfile から PlayableGraph を構築する。
        /// </summary>
        /// <param name="animator">接続先の Animator コンポーネント</param>
        /// <param name="profile">FacialProfile</param>
        /// <param name="blendShapeNames">全 BlendShape 名のリスト</param>
        /// <returns>構築結果。使用後は Dispose すること。</returns>
        public static BuildResult Build(Animator animator, FacialProfile profile, string[] blendShapeNames)
        {
            if (animator == null)
                throw new ArgumentNullException(nameof(animator));
            if (blendShapeNames == null)
                throw new ArgumentNullException(nameof(blendShapeNames));

            var graph = PlayableGraph.Create("FacialControlGraph");

            // FacialControlMixer（ルートノード）を生成
            var mixer = FacialControlMixer.Create(graph, blendShapeNames);

            // レイヤー分の LayerPlayable を生成・登録
            var layerPlayables = new Dictionary<string, ScriptPlayable<LayerPlayable>>();
            var layerSpan = profile.Layers.Span;

            for (int i = 0; i < layerSpan.Length; i++)
            {
                var layer = layerSpan[i];
                var layerPlayable = LayerPlayable.Create(
                    graph,
                    blendShapeNames.Length,
                    layer.ExclusionMode);

                layerPlayables[layer.Name] = layerPlayable;

                // Mixer にレイヤーを登録（初期ウェイト 1.0）
                mixer.GetBehaviour().RegisterLayer(
                    layer.Name,
                    layer.Priority,
                    1.0f,
                    layerPlayable);
            }

            // AnimationPlayableOutput を生成し、Animator に接続
            var output = AnimationPlayableOutput.Create(graph, "FacialControlOutput", animator);
            output.SetSourcePlayable(mixer);

            return new BuildResult(graph, mixer, layerPlayables);
        }

        /// <summary>
        /// PlayableGraph 構築結果。Graph、Mixer、レイヤー Playable へのアクセスを提供する。
        /// </summary>
        public class BuildResult : IDisposable
        {
            private PlayableGraph _graph;
            private bool _disposed;

            /// <summary>
            /// 構築された PlayableGraph。
            /// </summary>
            public PlayableGraph Graph => _graph;

            /// <summary>
            /// ルートの FacialControlMixer。
            /// </summary>
            public ScriptPlayable<FacialControlMixer> Mixer { get; }

            /// <summary>
            /// レイヤー名 → LayerPlayable の辞書。
            /// </summary>
            public IReadOnlyDictionary<string, ScriptPlayable<LayerPlayable>> LayerPlayables { get; }

            public BuildResult(
                PlayableGraph graph,
                ScriptPlayable<FacialControlMixer> mixer,
                Dictionary<string, ScriptPlayable<LayerPlayable>> layerPlayables)
            {
                _graph = graph;
                Mixer = mixer;
                LayerPlayables = layerPlayables;
            }

            /// <summary>
            /// PlayableGraph とその配下の全 Playable を破棄する。
            /// </summary>
            public void Dispose()
            {
                if (_disposed)
                    return;

                if (_graph.IsValid())
                {
                    _graph.Destroy();
                }

                _disposed = true;
            }
        }
    }
}
