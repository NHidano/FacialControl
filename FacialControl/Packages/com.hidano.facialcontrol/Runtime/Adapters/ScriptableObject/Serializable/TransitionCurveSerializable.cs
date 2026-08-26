using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.ScriptableObject.Serializable
{
    /// <summary>
    /// 遷移カーブ設定 (旧 JSON: transitionCurve)。
    /// <see cref="Hidano.FacialControl.Domain.Models.TransitionCurve"/> の Unity Serializable 投影。
    /// </summary>
    [Serializable]
    public sealed class TransitionCurveSerializable
    {
        [Tooltip("カーブ種類。プリセット (Linear/EaseIn/EaseOut/EaseInOut) または Custom。")]
        public TransitionCurveType type = TransitionCurveType.Linear;

        [Tooltip("Custom 選択時のキーフレーム配列。プリセット時は無視される。")]
        public List<CurveKeyFrameSerializable> keys = new List<CurveKeyFrameSerializable>();
    }
}
