using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Samples.EditorTools
{
    /// <summary>
    /// default_profile.json の Expression.BlendShapeValues で参照する名前が、
    /// SampleScene の Miku prefab にある SkinnedMeshRenderer の BlendShape 名と
    /// 文字列一致するかを検証する。文字化け / 正規化差異 / 空白混入を検出する。
    /// </summary>
    public static class VerifyDemoProfileBlendShapes
    {
        public static void Verify()
        {
            const string prefabGuid = "5588fb1befbdc5344a5457a74467ca94";
            string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[VerifyDemoProfileBlendShapes] prefab not found: {prefabGuid}");
                EditorApplication.Exit(2);
                return;
            }

            // Miku の BlendShape 名を収集（FacialController と同じ手順）
            var meshNames = new HashSet<string>(StringComparer.Ordinal);
            var meshNamesOrdered = new List<string>();
            foreach (var smr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr == null || smr.sharedMesh == null) continue;
                var mesh = smr.sharedMesh;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    string n = mesh.GetBlendShapeName(i);
                    if (meshNames.Add(n))
                    {
                        meshNamesOrdered.Add(n);
                    }
                }
            }

            // default_profile.json を読み込みパース
            string jsonPath = Path.Combine(UnityEngine.Application.streamingAssetsPath, "FacialControl", "default_profile.json");
            if (!File.Exists(jsonPath))
            {
                Debug.LogError($"[VerifyDemoProfileBlendShapes] profile not found: {jsonPath}");
                EditorApplication.Exit(3);
                return;
            }

            string json = File.ReadAllText(jsonPath, Encoding.UTF8);
            var parser = new SystemTextJsonParser();
            FacialProfile profile;
            try
            {
                profile = parser.ParseProfile(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VerifyDemoProfileBlendShapes] JSON parse failed: {ex.Message}");
                EditorApplication.Exit(4);
                return;
            }

            // Expression 毎に BlendShape 名が mesh にあるかチェック
            int totalMappings = 0;
            int matchedMappings = 0;
            var mismatches = new List<string>();
            foreach (var expr in profile.Expressions.ToArray())
            {
                var bsSpan = expr.BlendShapeValues.Span;
                for (int i = 0; i < bsSpan.Length; i++)
                {
                    totalMappings++;
                    string name = bsSpan[i].Name;
                    if (meshNames.Contains(name))
                    {
                        matchedMappings++;
                    }
                    else
                    {
                        // 近い候補を探索（code point 列レベルで比較）
                        string hexName = ToHex(name);
                        string closest = FindClosest(name, meshNamesOrdered);
                        mismatches.Add(
                            $"  Expression '{expr.Id}' → BlendShape '{name}' (utf16 {hexName}) が mesh に存在しない。近い候補: '{closest}' (utf16 {ToHex(closest)})");
                    }
                }
            }

            Debug.Log($"[VerifyDemoProfileBlendShapes] matched {matchedMappings} / {totalMappings} mappings");
            if (mismatches.Count == 0)
            {
                Debug.Log("[VerifyDemoProfileBlendShapes] OK: all BlendShape references resolve to mesh names.");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"[VerifyDemoProfileBlendShapes] {mismatches.Count} mismatch(es):\n" + string.Join("\n", mismatches));
                EditorApplication.Exit(5);
            }
        }

        private static string ToHex(string s)
        {
            if (string.IsNullOrEmpty(s)) return "<empty>";
            var sb = new StringBuilder(s.Length * 5);
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append("U+").Append(((int)s[i]).ToString("X4"));
            }
            return sb.ToString();
        }

        private static string FindClosest(string needle, List<string> names)
        {
            string best = "";
            int bestScore = int.MinValue;
            foreach (var n in names)
            {
                int score = 0;
                int len = Math.Min(needle.Length, n.Length);
                for (int i = 0; i < len; i++)
                {
                    if (needle[i] == n[i]) score++;
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    best = n;
                }
            }
            return best;
        }
    }
}
