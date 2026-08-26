using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Samples.EditorTools
{
    /// <summary>
    /// Play Mode 中の FacialController + MultiSourceBlendDemoHUD の runtime 状態を
    /// Unity Console にダンプする診断ツール。MCP RunCommand からはメニュー経由で呼ぶ。
    /// </summary>
    public static class DiagMultiSourceBlend
    {
        [MenuItem("Tools/FacialControl/Diag Multi Source Blend")]
        public static void Dump()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[Diag] playing={UnityEngine.Application.isPlaying} frame={Time.frameCount} time={Time.time:0.000}");

            var fc = Object.FindFirstObjectByType<FacialController>(FindObjectsInactive.Include);
            if (fc == null)
            {
                sb.AppendLine("[Diag] FacialController not found");
                Debug.Log(sb.ToString());
            try
            {
                Directory.CreateDirectory("test-results");
                File.WriteAllText("test-results/diag.txt", sb.ToString(), System.Text.Encoding.UTF8);
            }
            catch (System.Exception e) { Debug.LogError($"diag write failed: {e.Message}"); }
                return;
            }

            sb.AppendLine($"[Diag] fc={fc.gameObject.name} IsInitialized={fc.IsInitialized}");
            if (!fc.IsInitialized)
            {
                Debug.Log(sb.ToString());
            try
            {
                Directory.CreateDirectory("test-results");
                File.WriteAllText("test-results/diag.txt", sb.ToString(), System.Text.Encoding.UTF8);
            }
            catch (System.Exception e) { Debug.LogError($"diag write failed: {e.Message}"); }
                return;
            }

            if (fc.CurrentProfile.HasValue)
            {
                var prof = fc.CurrentProfile.Value;
                sb.AppendLine($"[Diag] layers={prof.Layers.Length} expressions={prof.Expressions.Length}");
                var layerSpan = prof.Layers.Span;
                for (int i = 0; i < layerSpan.Length; i++)
                {
                    sb.AppendLine($"  layer[{i}] name={layerSpan[i].Name} excl={layerSpan[i].ExclusionMode}");
                }
                var exprSpan = prof.Expressions.Span;
                for (int i = 0; i < exprSpan.Length; i++)
                {
                    var e = exprSpan[i];
                    sb.AppendLine($"  expr[{i}] id={e.Id} layer={e.Layer} bsValues={e.BlendShapeValues.Length}");
                }
            }

            bool fCtrl = fc.TryGetExpressionTriggerSourceById("controller-expr", out var ctrlSrc);
            sb.AppendLine($"[Diag] controller-expr found={fCtrl}");
            if (fCtrl)
            {
                sb.AppendLine($"  active=[{string.Join(",", ctrlSrc.ActiveExpressionIds)}]");
            }
            bool fKb = fc.TryGetExpressionTriggerSourceById("keyboard-expr", out var kbSrc);
            sb.AppendLine($"[Diag] keyboard-expr found={fKb}");
            if (fKb)
            {
                sb.AppendLine($"  active=[{string.Join(",", kbSrc.ActiveExpressionIds)}]");
            }

            var snap = fc.GetInputSourceWeightsSnapshot();
            sb.AppendLine($"[Diag] snapshot entries={snap.Count}");
            for (int i = 0; i < snap.Count; i++)
            {
                var e = snap[i];
                sb.AppendLine($"  snap layer={e.LayerIdx} id={e.SourceId.Value} w={e.Weight:0.00} valid={e.IsValid} sat={e.Saturated}");
            }

            // Current BlendShape weights on renderer
            foreach (var smr in fc.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (smr == null || smr.sharedMesh == null) continue;
                var mesh = smr.sharedMesh;
                for (int j = 0; j < mesh.blendShapeCount; j++)
                {
                    string n = mesh.GetBlendShapeName(j);
                    if (n == "笑い" || n == "怒り" || n == "びっくり" || n == "困る" || n == "まばたき"
                        || n == "口角上げ" || n == "左眉下げ" || n == "右眉下げ" || n == "▲"
                        || n == "口角下げ")
                    {
                        sb.AppendLine($"  BS[{j}] {n}={smr.GetBlendShapeWeight(j):0.00}");
                    }
                }
            }

            Debug.Log(sb.ToString());
            try
            {
                Directory.CreateDirectory("test-results");
                File.WriteAllText("test-results/diag.txt", sb.ToString(), System.Text.Encoding.UTF8);
            }
            catch (System.Exception e) { Debug.LogError($"diag write failed: {e.Message}"); }
        }

        [MenuItem("Tools/FacialControl/Trigger Controller smile On")]
        public static void TriggerControllerSmileOn()
        {
            var fc = Object.FindFirstObjectByType<FacialController>(FindObjectsInactive.Include);
            if (fc == null || !fc.IsInitialized) { Debug.LogWarning("[Diag] fc not ready"); return; }
            if (fc.TryGetExpressionTriggerSourceById("controller-expr", out var src))
            {
                src.TriggerOn("smile");
                Debug.Log("[Diag] controller-expr.TriggerOn(smile)");
            }
        }

        [MenuItem("Tools/FacialControl/Trigger Keyboard angry On")]
        public static void TriggerKeyboardAngryOn()
        {
            var fc = Object.FindFirstObjectByType<FacialController>(FindObjectsInactive.Include);
            if (fc == null || !fc.IsInitialized) { Debug.LogWarning("[Diag] fc not ready"); return; }
            if (fc.TryGetExpressionTriggerSourceById("keyboard-expr", out var src))
            {
                src.TriggerOn("angry");
                Debug.Log("[Diag] keyboard-expr.TriggerOn(angry)");
            }
        }
    }
}
