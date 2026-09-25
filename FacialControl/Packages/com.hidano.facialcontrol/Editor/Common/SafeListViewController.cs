using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Editor.Common
{
    /// <summary>
    /// ListView の RemoveItem / RemoveItems が itemsSource の範囲外 index で呼ばれた際に
    /// ArgumentOutOfRangeException を投げないようガードする controller 実装。
    /// </summary>
    /// <remarks>
    /// itemsSource を <c>List&lt;int&gt;</c> ベースで管理する drawer/inspector で、
    /// SerializedProperty 側との同期ずれや残留 selection によって発生する out-of-range を吸収する。
    /// </remarks>
    public sealed class SafeListViewController : ListViewController
    {
        public override void RemoveItem(int index)
        {
            if (itemsSource == null || index < 0 || index >= itemsSource.Count)
            {
                return;
            }

            base.RemoveItem(index);
        }

        public override void RemoveItems(List<int> indices)
        {
            if (indices == null || itemsSource == null)
            {
                return;
            }

            var clamped = new List<int>(indices.Count);
            for (int i = 0; i < indices.Count; i++)
            {
                int idx = indices[i];
                if (idx >= 0 && idx < itemsSource.Count)
                {
                    clamped.Add(idx);
                }
            }

            if (clamped.Count > 0)
            {
                base.RemoveItems(clamped);
            }
        }
    }
}
