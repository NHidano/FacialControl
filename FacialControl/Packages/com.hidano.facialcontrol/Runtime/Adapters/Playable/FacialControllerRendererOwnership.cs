using System.Collections.Generic;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.Playable
{
    /// <summary>
    /// どの <see cref="SkinnedMeshRenderer"/> をどの <see cref="FacialController"/> が
    /// 制御しているかを保持し、重複制御を検出するプロセス内レジストリ。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 参照は初期化時（<c>Initialize</c>）と破棄時（<c>Cleanup</c>）のみで、
    /// 毎フレームのホットパスからは触らないためアロケーションを許容する。
    /// </para>
    /// <para>
    /// 破棄済みの owner を掴んだままにしないよう、参照のたびに死んだ entry を掃除する。
    /// ドメインリロードで内容は失われるが、失われても各 FacialController の
    /// 初期化時に再登録されるだけなので問題にならない。
    /// </para>
    /// </remarks>
    public static class FacialControllerRendererOwnership
    {
        private static readonly List<Entry> _entries = new List<Entry>();

        private sealed class Entry
        {
            public FacialController Owner;
            public SkinnedMeshRenderer[] Renderers;
        }

        /// <summary>
        /// <paramref name="renderers"/> のいずれかを既に制御している別の FacialController を返す。
        /// </summary>
        /// <param name="candidate">これから制御しようとしている FacialController。</param>
        /// <param name="renderers">制御対象の SkinnedMeshRenderer 配列（null / 空は競合なし）。</param>
        /// <returns>競合相手。存在しなければ null。</returns>
        public static FacialController FindConflict(FacialController candidate, SkinnedMeshRenderer[] renderers)
        {
            PruneDeadEntries();

            if (renderers == null || renderers.Length == 0)
            {
                return null;
            }

            for (int entryIndex = 0; entryIndex < _entries.Count; entryIndex++)
            {
                Entry entry = _entries[entryIndex];
                if (entry.Owner == candidate)
                {
                    continue;
                }

                if (SharesRenderer(entry.Renderers, renderers))
                {
                    return entry.Owner;
                }
            }

            return null;
        }

        /// <summary>
        /// <paramref name="owner"/> の制御対象を登録する。既存の登録は置き換えられる。
        /// </summary>
        public static void Register(FacialController owner, SkinnedMeshRenderer[] renderers)
        {
            if (owner == null)
            {
                return;
            }

            Unregister(owner);

            if (renderers == null || renderers.Length == 0)
            {
                return;
            }

            _entries.Add(new Entry
            {
                Owner = owner,
                Renderers = renderers,
            });
        }

        /// <summary>
        /// <paramref name="owner"/> の登録を解除する。未登録でも安全に呼べる。
        /// </summary>
        public static void Unregister(FacialController owner)
        {
            if (owner == null)
            {
                PruneDeadEntries();
                return;
            }

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Owner == owner)
                {
                    _entries.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 全登録を破棄する。テストのセットアップ / ティアダウン用。
        /// </summary>
        public static void Clear()
        {
            _entries.Clear();
        }

        private static bool SharesRenderer(SkinnedMeshRenderer[] left, SkinnedMeshRenderer[] right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            for (int l = 0; l < left.Length; l++)
            {
                SkinnedMeshRenderer leftRenderer = left[l];
                if (leftRenderer == null)
                {
                    continue;
                }

                for (int r = 0; r < right.Length; r++)
                {
                    if (leftRenderer == right[r])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void PruneDeadEntries()
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Owner == null)
                {
                    _entries.RemoveAt(i);
                }
            }
        }
    }
}
