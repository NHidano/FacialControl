using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.Bone
{
    /// <summary>
    /// 名前または参照モデル相対 path から <see cref="UnityEngine.Transform"/> を解決し、
    /// 結果をキャッシュするサービス 。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 解決対象文字列が <c>'/'</c> を含む場合は <see cref="Transform.Find(string)"/> 互換の
    /// 相対 path として扱い、含まない場合は階層全体を再帰探索する単純名解決として扱う。
    /// 単純名解決のとき同名 Transform が複数ヒットすると最初の発見を採用しつつ警告を 1 回だけ出す
    /// 曖昧性を避けたい場合は相対 path を指定する。
    /// </para>
    /// <para>
    /// 相対 path が完全一致しない場合は、path 末尾から最も多くのセグメントが一致するノードへ
    /// フォールバックする。同じ Face Mesh を持つ衣装違いモデルのように、
    /// ボーン名は同じでも上位階層だけが異なるモデル間で path を使い回せるようにするため。
    /// フォールバックで解決したときは path 更新を促す警告を 1 回出す。
    /// </para>
    /// <para>
    /// 解決失敗時は <see cref="Debug.LogWarning"/> + null 返却（throw しない）。
    /// 同一文字列に対する警告は dedupe する。
    /// </para>
    /// </remarks>
    public sealed class BoneTransformResolver
    {
        private static readonly char[] PathSeparators = { '/' };

        private readonly Transform _root;
        private readonly Dictionary<string, Transform> _cache = new Dictionary<string, Transform>();
        private readonly HashSet<string> _missingWarned = new HashSet<string>();

        public BoneTransformResolver(Transform root)
        {
            _root = root;
        }

        public Transform Resolve(string boneNameOrPath)
        {
            if (string.IsNullOrEmpty(boneNameOrPath))
            {
                return null;
            }

            if (_cache.TryGetValue(boneNameOrPath, out var cached))
            {
                return cached;
            }

            if (_missingWarned.Contains(boneNameOrPath))
            {
                return null;
            }

            Transform found = IsRelativePath(boneNameOrPath)
                ? ResolveByRelativePath(boneNameOrPath)
                : FindFirstAndWarnIfDuplicate(boneNameOrPath);

            if (found != null)
            {
                _cache[boneNameOrPath] = found;
                return found;
            }

            _missingWarned.Add(boneNameOrPath);
            Debug.LogWarning($"[BoneTransformResolver] bone '{boneNameOrPath}' を解決できませんでした。");
            return null;
        }

        public void Prime(IReadOnlyList<string> boneNames)
        {
            if (boneNames == null)
            {
                return;
            }
            for (int i = 0; i < boneNames.Count; i++)
            {
                _ = Resolve(boneNames[i]);
            }
        }

        private static bool IsRelativePath(string s)
        {
            return s.IndexOf('/') >= 0;
        }

        // 相対 path は Transform.Find(string) が '/' 区切りで階層を辿ってくれるので、
        // まず root.Find に丸投げする。先頭/末尾の '/' は Unity 側で整理される。
        // 完全一致しない場合のみ suffix 一致へフォールバックする。
        private Transform ResolveByRelativePath(string path)
        {
            if (_root == null) return null;

            Transform exact = _root.Find(path);
            if (exact != null)
            {
                return exact;
            }

            return ResolveByLongestPathSuffix(path);
        }

        /// <summary>
        /// path 末尾から最も多くのセグメントが一致する Transform を探す。
        /// </summary>
        /// <remarks>
        /// 末端のボーン名だけで探すのではなく一致セグメント数で順位付けするのは、
        /// 同名ボーン（左右の <c>Eye</c> 等）が複数ある階層で誤ったノードを掴まないため。
        /// 例: path <c>"Root/Neck/Head"</c> は、モデル側が <c>Armature/Neck/Head</c> なら
        /// 末尾 2 セグメント一致で <c>Neck/Head</c> を選び、階層のどこかにある単独の
        /// <c>Head</c>（1 セグメント一致）よりも優先する。
        /// </remarks>
        private Transform ResolveByLongestPathSuffix(string path)
        {
            var segments = path.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return null;
            }

            Transform best = null;
            int bestScore = 0;
            int bestCount = 0;
            CollectSuffixMatches(_root, segments, ref best, ref bestScore, ref bestCount);

            if (best == null)
            {
                return null;
            }

            if (bestCount >= 2)
            {
                Debug.LogWarning(
                    $"[BoneTransformResolver] path '{path}' はモデル階層と一致しませんでした。"
                    + $" 末尾 {bestScore} セグメントが一致する候補が {bestCount} 件あるため"
                    + $" 最初の '{GetRelativePath(best)}' を採用します。"
                    + " 意図した別ボーンを指したい場合はモデル階層に合わせて path を更新してください。");
            }
            else
            {
                Debug.LogWarning(
                    $"[BoneTransformResolver] path '{path}' はモデル階層と一致しませんでした。"
                    + $" 末尾 {bestScore} セグメントが一致する '{GetRelativePath(best)}' で解決します。"
                    + " 階層構造の異なるモデルを使っている場合は path を更新してください。");
            }

            return best;
        }

        private static void CollectSuffixMatches(
            Transform node,
            string[] segments,
            ref Transform best,
            ref int bestScore,
            ref int bestCount)
        {
            if (node == null) return;

            int score = CountMatchingSuffix(node, segments);
            if (score > 0)
            {
                if (score > bestScore)
                {
                    best = node;
                    bestScore = score;
                    bestCount = 1;
                }
                else if (score == bestScore)
                {
                    bestCount++;
                }
            }

            int childCount = node.childCount;
            for (int i = 0; i < childCount; i++)
            {
                CollectSuffixMatches(node.GetChild(i), segments, ref best, ref bestScore, ref bestCount);
            }
        }

        /// <summary>
        /// <paramref name="node"/> から親方向へ遡り、<paramref name="segments"/> の末尾と
        /// 連続して一致するセグメント数を返す。末端名が違えば 0。
        /// </summary>
        private static int CountMatchingSuffix(Transform node, string[] segments)
        {
            int matched = 0;
            Transform current = node;
            for (int i = segments.Length - 1; i >= 0; i--)
            {
                if (current == null) break;
                if (!string.Equals(current.name, segments[i], StringComparison.Ordinal)) break;
                matched++;
                current = current.parent;
            }

            return matched;
        }

        /// <summary>
        /// 診断ログ用に、<see cref="_root"/> から見た相対 path を組み立てる。
        /// </summary>
        private string GetRelativePath(Transform target)
        {
            if (target == null) return "(missing)";
            if (target == _root) return target.name;

            var builder = new System.Text.StringBuilder(target.name);
            Transform current = target.parent;
            while (current != null && current != _root)
            {
                builder.Insert(0, '/');
                builder.Insert(0, current.name);
                current = current.parent;
            }

            return builder.ToString();
        }

        // 単純名指定の場合は階層全体を歩いて全マッチを数える。複数ヒット時は警告 1 回のみ。
        // 計算量は per-bone 一度きりで cache に乗るためホットパスには影響しない。
        private Transform FindFirstAndWarnIfDuplicate(string boneName)
        {
            Transform first = null;
            int count = 0;
            CollectMatches(_root, boneName, ref first, ref count);

            if (count >= 2)
            {
                Debug.LogWarning(
                    $"[BoneTransformResolver] bone 名 '{boneName}' が階層内に複数 ({count} 件) 存在します。" +
                    " 最初の発見を採用しますが、別ボーンを指したい場合は参照モデル相対 path"
                    + " (例: 'Armature/Hips/Spine/Head/LeftEye') を指定してください。");
            }

            return first;
        }

        private static void CollectMatches(Transform node, string name, ref Transform first, ref int count)
        {
            if (node == null) return;
            if (node.name == name)
            {
                if (first == null) first = node;
                count++;
            }
            int childCount = node.childCount;
            for (int i = 0; i < childCount; i++)
            {
                CollectMatches(node.GetChild(i), name, ref first, ref count);
            }
        }
    }
}
