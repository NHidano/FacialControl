#!/usr/bin/env bash
# Batch runner for spec inspector-and-data-model-redesign
# Sequentially executes all 32 leaf tasks via `claude -p`
# Logs each task's full output to per-task file, summary to BATCH-SUMMARY.md

set -u

SPEC_DIR=".kiro/specs/inspector-and-data-model-redesign"
LOG_DIR="$SPEC_DIR/run-logs"
SUMMARY_FILE="$SPEC_DIR/BATCH-SUMMARY.md"
mkdir -p "$LOG_DIR"

# Initialize summary
{
  echo "# Spec Batch Execution Summary"
  echo ""
  echo "Started: $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo ""
  echo "| Task ID | Title | Result | Duration | Log |"
  echo "|---------|-------|--------|----------|-----|"
} > "$SUMMARY_FILE"

# Task list: id|title  (32 leaf tasks)
TASKS=(
  "1.1|Domain 値型 LayerOverrideMask を追加し、bit 演算と layer 名配列との往復を確立する"
  "1.2|Domain 値型 BlendShapeSnapshot / BoneSnapshot を追加し、防御コピーと不変性を保証する"
  "1.3|Domain 値型 ExpressionSnapshot を追加し、AnimationClip 由来 snapshot の Domain 受け皿を成立させる"
  "1.4|Phase 1 完了レビュー — Domain 新型 4 種の API surface 確認と asmdef 静的検査"
  "2.1|Editor 専用サンプラの interface と stateless 実装を導入し、AnimationUtility 経由で時刻 0 値を取得する"
  "2.2|AnimationEvent 経由の TransitionDuration / TransitionCurvePreset メタデータを抽出する"
  "2.3|中間 JSON schema v2.0 の DTO を新設し、snapshot table 形式の round-trip を確立する"
  "2.4|サンプラの Editor 専用境界とパフォーマンスを CI で保証する"
  "3.1|Expression struct を SnapshotId 参照型へ破壊置換し、派生 5 値の field を撤去する"
  "3.2|LayerSlot struct を撤去し、LayerOverrideMask への置換を完了する"
  "3.3|FacialProfile から BonePoses 配列を撤去し、Expression snapshot 経路に一元化する"
  "3.4|ExpressionResolver サービスを新設し、SnapshotId → BlendShape / Bone 値の preallocated 解決を提供する"
  "3.5|Domain AnalogBindingEntry から Mapping field を撤去し、AnalogMappingFunction を物理削除する"
  "3.6|SystemTextJsonParser と FacialCharacterProfileConverter を schema v2.0 専用に書換える"
  "3.7|Phase 3 完了レビュー — Domain 全層の破壊書換が CI 緑であることを確認"
  "4.1|Analog deadzone / scale / offset / clamp の 4 種 InputProcessor を stateless で実装する"
  "4.2|Analog curve / invert の 2 種 InputProcessor を stateless で実装する"
  "4.3|InputProcessor 6 種を Editor / Runtime 両方で一括登録する初期化フックを設置する"
  "4.4|InputDeviceCategorizer を新設し、InputBinding.path から DeviceCategory を 0-alloc で推定する"
  "4.5|ExpressionInputSourceAdapter を新設し、Keyboard/Controller を統合した MonoBehaviour として動作させる"
  "4.6|Keyboard/Controller 個別 InputSource を物理削除し、ExpressionBindingEntry.Category を撤去する"
  "4.7|InputSystemAdapter に device 推定経路を追加し、AnalogBindingEntrySerializable を簡素化する"
  "4.8|0-alloc 検証 PlayMode/Performance テストを整備する"
  "5.1|FacialCharacterSOInspector を UI Toolkit で全面改修し、新 UX を提供する"
  "5.2|ExpressionCreatorWindow を AnimationClip ベイク経路に改修する"
  "5.3|FacialCharacterSOAutoExporter を AnimationClip サンプラ経路に改修し、進捗 + abort を提供する"
  "5.4|FacialControllerEditor の概要表示を新モデルに合わせて小修正する"
  "6.1|MultiSourceBlendDemo サンプルを schema v2.0 で再生成する"
  "6.2|AnalogBindingDemo サンプルを mapping field 不在 + processors 埋込 で再生成する"
  "6.3|Migration Guide ドキュメントを Documentation~ に新規作成する"
  "6.4|CHANGELOG.md と package.json バージョンを Breaking Change 表記で更新する"
  "6.5|Phase 6 完了レビュー — Sample 結線確認と CI 全 Phase 緑"
)

TOTAL=${#TASKS[@]}
INDEX=0
OK_COUNT=0
FAIL_COUNT=0
TIMEOUT_COUNT=0

for entry in "${TASKS[@]}"; do
  INDEX=$((INDEX + 1))
  TASK_ID="${entry%%|*}"
  TASK_TITLE="${entry#*|}"
  TASK_NAME="$TASK_ID $TASK_TITLE"
  LOG_FILE="$LOG_DIR/task-$TASK_ID.log"

  echo "" >&2
  echo "================================================================" >&2
  echo "[$INDEX/$TOTAL] $TASK_NAME" >&2
  echo "Start: $(date -u +%Y-%m-%dT%H:%M:%SZ)" >&2
  echo "================================================================" >&2

  START_TS=$(date +%s)

  PROMPT="Execute only this single task ($TASK_NAME) according to the instructions in CLAUDE.md and the spec documents in $SPEC_DIR/ (requirements.md, design.md, tasks.md, gap-analysis.md, research.md). Before starting, output the task name ($TASK_NAME). After completing the task, run UnityTestRunner to verify the result. If any file changes exist, run git add -A and then commit. The commit title must be the task name \"$TASK_NAME\" as-is. The commit body must contain a brief summary of what was done (files created/modified, key changes). Use a multi-line commit message with git commit -m \"title\" -m \"body\". Finally, output only OK or FAIL. tasks.txt is a user-managed file and must not be modified. After outputting OK or FAIL, complete the session without waiting for user input."

  # 30-minute timeout per task (1800s)
  unset CLAUDECODE
  RESULT="UNKNOWN"
  if timeout 1800 bash -c "echo '' | claude -p \"\$1\" --max-turns 60 --enable-auto-mode --verbose" _ "$PROMPT" > "$LOG_FILE" 2>&1; then
    # Last non-empty line should be OK or FAIL
    LAST_LINE=$(tac "$LOG_FILE" | grep -m1 -E "^(OK|FAIL)$" || echo "UNKNOWN")
    RESULT="$LAST_LINE"
  else
    EXIT=$?
    if [ "$EXIT" = "124" ]; then
      RESULT="TIMEOUT"
    else
      RESULT="ERROR(exit=$EXIT)"
    fi
  fi

  END_TS=$(date +%s)
  DURATION=$((END_TS - START_TS))

  case "$RESULT" in
    OK) OK_COUNT=$((OK_COUNT + 1)) ;;
    FAIL) FAIL_COUNT=$((FAIL_COUNT + 1)) ;;
    TIMEOUT) TIMEOUT_COUNT=$((TIMEOUT_COUNT + 1)) ;;
    *) FAIL_COUNT=$((FAIL_COUNT + 1)) ;;
  esac

  # Append to summary
  echo "| $TASK_ID | ${TASK_TITLE:0:60} | $RESULT | ${DURATION}s | run-logs/task-$TASK_ID.log |" >> "$SUMMARY_FILE"

  echo "[$INDEX/$TOTAL] $TASK_NAME : $RESULT (${DURATION}s)" >&2
done

# Summary footer
{
  echo ""
  echo "Completed: $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo ""
  echo "## Totals"
  echo ""
  echo "- OK: $OK_COUNT"
  echo "- FAIL: $FAIL_COUNT"
  echo "- TIMEOUT: $TIMEOUT_COUNT"
  echo "- Total: $TOTAL"
} >> "$SUMMARY_FILE"

echo "" >&2
echo "================================================================" >&2
echo "BATCH COMPLETE: $OK_COUNT OK / $FAIL_COUNT FAIL / $TIMEOUT_COUNT TIMEOUT (of $TOTAL)" >&2
echo "Summary: $SUMMARY_FILE" >&2
echo "================================================================" >&2
