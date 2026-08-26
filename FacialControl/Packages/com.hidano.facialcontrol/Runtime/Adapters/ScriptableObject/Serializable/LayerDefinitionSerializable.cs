using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.ScriptableObject.Serializable
{
    /// <summary>
    /// レイヤー定義 (旧 JSON: layers[])。
    /// <see cref="Hidano.FacialControl.Domain.Models.LayerDefinition"/> + 当該レイヤーの inputSources を
    /// 1 オブジェクトに集約した Unity Serializable 投影。
    /// </summary>
    [Serializable]
    public sealed class LayerDefinitionSerializable
    {
        [Tooltip("レイヤー名。Expression の所属先と一致させる。")]
        public string name;

        [Tooltip("優先度 (0 以上、値が大きいほど後段で適用)。")]
        [Min(0)]
        public int priority;

        [Tooltip("排他モード。LastWins=後勝ち、Blend=同レイヤー内ブレンド。")]
        public ExclusionMode exclusionMode = ExclusionMode.LastWins;

        [Tooltip("このレイヤーで動作する入力源の宣言。最低 1 件必要。空なら自動で input が補完される。")]
        public List<InputSourceDeclarationSerializable> inputSources = new List<InputSourceDeclarationSerializable>();

        [Tooltip("このレイヤーがアクティブな間、上書きする他レイヤーの名前。同レイヤー名を含めることでレイヤー内ブレンドを担保する。")]
        public List<string> layerOverrideMask = new List<string>();
    }
}
