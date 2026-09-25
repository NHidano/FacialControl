using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using UnityEditor;
using UnityEngine;

namespace Hidano.FacialControl.Editor.Inspector.AdapterBindings
{
    /// <summary>
    /// 重複 slug を持つ <see cref="FacialCharacterProfileSO"/> の disk save をブロックする
    /// <see cref="UnityEditor.AssetModificationProcessor"/>。
    /// </summary>
    /// <remarks>
    /// <para>
    //: <see cref="AdapterBindingsListView"/> の row error class + summary banner と
    /// 二段構えで重複を検出し、保存パイプラインから当該 path を除外する。
    /// </para>
    /// <para>
    /// 大量 SO の同時 save 時の overhead を抑えるため、まず
    /// <see cref="AssetDatabase.GetMainAssetTypeAtPath"/> で
    /// <see cref="FacialCharacterProfileSO"/> 以外を即 skip し、一致時のみ
    /// <see cref="FacialCharacterProfileSO.AdapterBindings"/> 内の slug uniqueness を再検証する。
    /// </para>
    /// </remarks>
    public class FacialCharacterProfileAssetGuard : UnityEditor.AssetModificationProcessor
    {
        public const string DuplicateSlugDialogTitle = "FacialControl: Save blocked";

        /// <summary>
        /// テスト時に <see cref="EditorUtility.DisplayDialog"/> をスキップするためのフラグ。
        /// 本番フローでは触らないこと（バッチモード判定とは独立に効く）。
        /// </summary>
        public static bool SuppressDialogForTesting;

        public static string[] OnWillSaveAssets(string[] paths)
        {
            if (paths == null || paths.Length == 0) return paths;

            List<string> filtered = null;
            string firstDuplicateSlug = null;
            string firstBlockedPath = null;
            UnityEngine.Object firstBlockedObject = null;

            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                if (string.IsNullOrEmpty(path)) continue;

                var mainType = AssetDatabase.GetMainAssetTypeAtPath(path);
                if (mainType == null) continue;
                if (!typeof(FacialCharacterProfileSO).IsAssignableFrom(mainType)) continue;

                var so = AssetDatabase.LoadAssetAtPath<FacialCharacterProfileSO>(path);
                if (so == null) continue;

                if (TryFindDuplicateSlug(so, out string duplicateSlug))
                {
                    Debug.LogError(
                        $"[FacialControl] Save blocked: duplicate slug '{duplicateSlug}' in {path}");

                    if (firstBlockedObject == null)
                    {
                        firstDuplicateSlug = duplicateSlug;
                        firstBlockedPath = path;
                        firstBlockedObject = so;
                    }

                    if (filtered == null)
                    {
                        filtered = new List<string>(paths.Length);
                        for (int j = 0; j < i; j++)
                        {
                            filtered.Add(paths[j]);
                        }
                    }
                    continue;
                }

                filtered?.Add(path);
            }

            if (filtered == null) return paths;

            if (firstBlockedObject != null)
            {
                Selection.activeObject = firstBlockedObject;
                ShowDuplicateSlugDialog(firstDuplicateSlug, firstBlockedPath);
            }

            return filtered.ToArray();
        }

        public static bool TryFindDuplicateSlug(
            FacialCharacterProfileSO so,
            out string duplicateSlug)
        {
            duplicateSlug = null;
            if (so == null) return false;

            var bindings = so.AdapterBindings;
            if (bindings == null || bindings.Count < 2) return false;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (binding == null) continue;
                string slug = binding.Slug;
                if (string.IsNullOrEmpty(slug)) continue;

                if (!seen.Add(slug))
                {
                    duplicateSlug = slug;
                    return true;
                }
            }
            return false;
        }

        private static void ShowDuplicateSlugDialog(string duplicateSlug, string assetPath)
        {
            // R-F: Display dialog so the user understands why save was blocked.
            // Skipped in batch mode / when running headless tests to avoid blocking.
            if (SuppressDialogForTesting) return;
            if (UnityEngine.Application.isBatchMode) return;

            try
            {
                EditorUtility.DisplayDialog(
                    DuplicateSlugDialogTitle,
                    $"Duplicate adapter binding slug '{duplicateSlug}' detected in:\n{assetPath}\n\n" +
                    "Resolve duplicate slugs in the FacialCharacterProfile Inspector before saving.",
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[FacialControl] Failed to display duplicate-slug dialog: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
