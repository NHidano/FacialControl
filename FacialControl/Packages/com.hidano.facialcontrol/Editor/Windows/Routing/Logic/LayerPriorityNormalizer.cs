using System;
using System.Collections.Generic;

namespace Hidano.FacialControl.Editor.Windows.Routing.Logic
{
    /// <summary>
    /// レイヤーの priority を「表示順（昇順）を保ったまま distinct な値」へ補正する純粋ロジック。
    /// Composite Output は priority 昇順で行を整列するが、同値が複数あると InputField 上の数値が
    /// 重複したままになる。本クラスは重複を最小限の上方シフトで解消し、UI の整列順を正として
    /// SO へ書き戻すための補正値を算出する（GraphView 非依存・決定的）。
    /// </summary>
    public static class LayerPriorityNormalizer
    {
        /// <summary>
        /// priorities（添字 = layerIndex）を入力とし、補正後の priority（添字 = layerIndex）を返す。
        /// 整列順は (priority, layerIndex) 昇順。各値は 0 以上かつ厳密増加になるよう、
        /// 重複・逆転時のみ「直前 + 1」へ最小限に押し上げる。
        /// </summary>
        public static int[] Normalize(IReadOnlyList<int> priorities)
        {
            if (priorities == null)
            {
                throw new ArgumentNullException(nameof(priorities));
            }

            int count = priorities.Count;
            var corrected = new int[count];
            if (count == 0)
            {
                return corrected;
            }

            var order = new int[count];
            for (int i = 0; i < count; i++)
            {
                order[i] = i;
            }

            Array.Sort(order, (left, right) =>
            {
                int priorityCompare = priorities[left].CompareTo(priorities[right]);
                return priorityCompare != 0 ? priorityCompare : left.CompareTo(right);
            });

            bool hasPrevious = false;
            int previous = 0;
            for (int i = 0; i < count; i++)
            {
                int layerIndex = order[i];
                int value = Math.Max(0, priorities[layerIndex]);
                if (hasPrevious && value <= previous)
                {
                    value = previous + 1;
                }

                corrected[layerIndex] = value;
                previous = value;
                hasPrevious = true;
            }

            return corrected;
        }

        /// <summary>
        /// 補正後の priority が元の priority と 1 つでも異なるかどうか。
        /// 異なる場合のみ SO への書き戻しが必要。
        /// </summary>
        public static bool RequiresCorrection(IReadOnlyList<int> priorities)
        {
            if (priorities == null)
            {
                throw new ArgumentNullException(nameof(priorities));
            }

            int[] corrected = Normalize(priorities);
            for (int i = 0; i < priorities.Count; i++)
            {
                if (corrected[i] != priorities[i])
                {
                    return true;
                }
            }

            return false;
        }
    }
}
